using System;
using UnityEngine;
using UnityEngine.Events;

namespace Arena
{
    /// <summary>
    /// Tile-to-tile movement state machine.
    ///
    /// Position interpolates between cell centres. When a step finishes, leftover frame time carries
    /// into the next same-axis step so chained steps never stall. A direction pressed mid-step is
    /// buffered and fires the instant the character lands; a turn always costs exactly one landing.
    ///
    /// The events are the Wwise and haptics hooks. Both C# events (for code) and UnityEvents (for
    /// designer wiring in the Inspector) are raised.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GridMover : MonoBehaviour
    {
        public enum State { Idle = 0, Moving = 1 }

        [Header("Movement")]
        [Tooltip("Seconds to travel one tile.")]
        [Min(0.01f)]
        [SerializeField] private float stepDuration = 0.18f;

        [Tooltip("Shapes the step: X = normalised time, Y = normalised distance. Leave linear (0,0)->(1,1) for constant speed.")]
        [SerializeField] private AnimationCurve stepProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Uncheck to freeze this mover without disabling the component.")]
        [SerializeField] private bool movementEnabled = true;

        [Header("Turning")]
        [Tooltip("Facing used before any input arrives.")]
        [SerializeField] private GridDirection initialFacing = GridDirection.Down;

        [Tooltip("ON: leftover frame time also carries through a turn. OFF (default): a turn snaps to the cell and starts fresh, so every turn costs exactly one landing.")]
        [SerializeField] private bool carryLeftoverIntoTurns = false;

        [Tooltip("Tapping a blocked direction still turns the character to face it.")]
        [SerializeField] private bool turnToFaceBlocked = true;

        [Header("Blocked Feedback")]
        [Tooltip("While a blocked direction stays held, re-raise StepBlocked every N seconds. 0 = once per press, which is what audio and haptics usually want.")]
        [Min(0f)]
        [SerializeField] private float blockedRepeatInterval = 0f;

        [Header("Visuals")]
        [Tooltip("Rotate a transform to match facing. Leave empty to rotate nothing.")]
        [SerializeField] private Transform rotateToFacing;

        [Tooltip("Degrees added to the facing angle, to correct for how the sprite is drawn.")]
        [SerializeField] private float facingRotationOffset = -90f;

        [Tooltip("Keep this Z when snapping to cell centres. Useful for 2D sorting.")]
        [SerializeField] private bool overrideZ = false;
        [SerializeField] private float z = 0f;

        [Header("Input")]
        [Tooltip("Optional. Any component implementing IPlayerInputSource. Left empty, the mover looks for one on this GameObject.")]
        [SerializeField] private MonoBehaviour inputSourceOverride;

        [Header("Runtime (read-only)")]
        [InspectorReadOnly, SerializeField] private State state = State.Idle;
        [InspectorReadOnly, SerializeField] private Vector2Int currentCell;
        [InspectorReadOnly, SerializeField] private Vector2Int targetCell;
        [InspectorReadOnly, SerializeField] private GridDirection facing = GridDirection.Down;
        [InspectorReadOnly, SerializeField] private GridDirection stepDirection = GridDirection.None;
        [InspectorReadOnly, SerializeField] private GridDirection bufferedDirection = GridDirection.None;
        [InspectorReadOnly, Range(0f, 1f), SerializeField] private float stepProgress;
        [InspectorReadOnly, SerializeField] private int stepsTaken;
        [InspectorReadOnly, SerializeField] private int blockedCount;

        [Header("Unity Events")]
        [SerializeField] private UnityEvent<GridDirection> onStepStarted = new();
        [SerializeField] private UnityEvent<Vector2Int> onArrived = new();
        [SerializeField] private UnityEvent<GridDirection> onStepBlocked = new();
        [SerializeField] private UnityEvent<GridDirection> onFacingChanged = new();

        /// <summary>A step has begun. Argument: direction of travel.</summary>
        public event Action<GridMover, GridDirection> StepStarted;
        /// <summary>The character landed on a cell. Argument: the cell arrived at.</summary>
        public event Action<GridMover, Vector2Int> Arrived;
        /// <summary>A step into a blocked or occupied cell was attempted. Argument: attempted direction.</summary>
        public event Action<GridMover, GridDirection> StepBlocked;
        /// <summary>Facing changed. Argument: the new facing.</summary>
        public event Action<GridMover, GridDirection> FacingChanged;

        public State CurrentState => state;
        public Vector2Int CurrentCell => currentCell;
        public Vector2Int TargetCell => targetCell;
        public GridDirection Facing => facing;
        public GridDirection StepDirection => stepDirection;
        public GridDirection BufferedDirection => bufferedDirection;
        public float StepProgress => stepProgress;
        public bool IsMoving => state == State.Moving;
        public float StepDuration { get => stepDuration; set => stepDuration = Mathf.Max(0.01f, value); }
        public bool MovementEnabled { get => movementEnabled; set => movementEnabled = value; }

        public IPlayerInputSource InputSource { get; set; }

        private GridService grid;
        private Vector3 stepFrom, stepTo;
        private bool registered;
        private GridDirection lastBlockedDirection = GridDirection.None;
        private float heldBlockedTime;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            facing = initialFacing;
            ResolveInputSource();
        }

        private void OnEnable()
        {
            ResolveInputSource();               // also runs after a domain reload in Play mode
            grid = GridService.Instance;
            if (grid == null)
            {
                Debug.LogError($"[GridMover] No GridService in the scene; '{name}' will not move.", this);
                enabled = false;
                return;
            }

            grid.Baked += OnGridBaked;
            if (grid.IsBaked) SnapToGrid();
        }

        private void OnDisable()
        {
            if (grid == null) return;
            grid.Baked -= OnGridBaked;
            ReleaseAllCells();
        }

        private void OnValidate()
        {
            if (inputSourceOverride != null && inputSourceOverride is not IPlayerInputSource)
            {
                Debug.LogWarning($"[GridMover] Input Source Override on '{name}' does not implement IPlayerInputSource; clearing it.", this);
                inputSourceOverride = null;
            }
        }

        private void ResolveInputSource()
        {
            if (InputSource != null) return;
            InputSource = inputSourceOverride as IPlayerInputSource ?? GetComponent<IPlayerInputSource>();
        }

        private void OnGridBaked()
        {
            registered = false;             // a rebake wipes occupancy
            SnapToGrid();
        }

        // ------------------------------------------------------------------ grid registration

        /// <summary>Snaps to the nearest cell centre and reserves it. Cancels any step in progress.</summary>
        public void SnapToGrid()
        {
            if (grid == null || !grid.IsBaked) return;
            if (registered) ReleaseAllCells();

            state = State.Idle;
            stepDirection = GridDirection.None;
            stepProgress = 0f;
            lastBlockedDirection = GridDirection.None;

            currentCell = grid.WorldToCell(transform.position);
            targetCell = currentCell;

            registered = grid.TryReserve(currentCell, this);
            if (!registered && grid.WarnOnBadSpawn)
                Debug.LogWarning($"[GridMover] '{name}' starts on blocked or occupied cell {currentCell}.", this);

            ApplyPosition();
            ApplyFacingRotation();
        }

        /// <summary>Teleports to a cell if it is free. Returns false and changes nothing otherwise.</summary>
        public bool TryTeleport(Vector2Int cell)
        {
            if (grid == null || !grid.IsBaked || !grid.CanEnter(cell, this)) return false;

            ReleaseAllCells();
            currentCell = targetCell = cell;
            state = State.Idle;
            stepDirection = GridDirection.None;
            bufferedDirection = GridDirection.None;
            stepProgress = 0f;

            registered = grid.TryReserve(currentCell, this);
            ApplyPosition();
            return registered;
        }

        private void ReleaseAllCells()
        {
            if (grid == null) return;
            grid.Release(currentCell, this);
            if (targetCell != currentCell) grid.Release(targetCell, this);
            registered = false;
        }

        // ------------------------------------------------------------------ per-frame

        private void Update()
        {
            if (grid == null || !grid.IsBaked || !registered || !movementEnabled) return;

            if (InputSource != null)
            {
                GridDirection pressed = InputSource.PressedThisFrame;
                if (pressed != GridDirection.None) bufferedDirection = pressed;   // latest press wins
            }

            Advance(Time.deltaTime);
        }

        private void Advance(float dt)
        {
            // Loop so one large frame can finish a step and start the next with the remainder.
            int guard = 0;
            while (dt > 0f && guard++ < 8)
            {
                if (state == State.Idle)
                {
                    bool fromBuffer = bufferedDirection != GridDirection.None;
                    GridDirection next = PeekNextDirection();
                    bufferedDirection = GridDirection.None;

                    if (next == GridDirection.None) { lastBlockedDirection = GridDirection.None; return; }
                    if (!TryStartStep(next, fromBuffer, dt)) return;
                }

                float remaining = (1f - stepProgress) * stepDuration;
                if (dt < remaining)
                {
                    stepProgress += dt / stepDuration;
                    dt = 0f;
                }
                else
                {
                    dt -= remaining;
                    stepProgress = 1f;
                    GridDirection finished = stepDirection;
                    Land();

                    // A turn costs exactly one landing: don't bleed leftover time into a new axis.
                    GridDirection upcoming = PeekNextDirection();
                    if (!carryLeftoverIntoTurns && upcoming != GridDirection.None && upcoming != finished) dt = 0f;
                }

                ApplyPosition();
            }
        }

        private GridDirection PeekNextDirection()
        {
            if (bufferedDirection != GridDirection.None) return bufferedDirection;
            return InputSource?.HeldDirection ?? GridDirection.None;
        }

        private bool TryStartStep(GridDirection direction, bool freshPress, float dt)
        {
            Vector2Int destination = currentCell + direction.ToVector();

            if (!grid.TryReserve(destination, this))
            {
                if (turnToFaceBlocked) SetFacing(direction);

                // Raise once per fresh attempt, then optionally repeat while the direction stays held.
                bool raise = freshPress || direction != lastBlockedDirection;
                if (raise) heldBlockedTime = 0f;
                else
                {
                    heldBlockedTime += dt;
                    if (blockedRepeatInterval > 0f && heldBlockedTime >= blockedRepeatInterval)
                    {
                        heldBlockedTime -= blockedRepeatInterval;
                        raise = true;
                    }
                }

                lastBlockedDirection = direction;
                if (raise)
                {
                    blockedCount++;
                    StepBlocked?.Invoke(this, direction);
                    onStepBlocked.Invoke(direction);
                }
                return false;
            }

            lastBlockedDirection = GridDirection.None;
            SetFacing(direction);

            state = State.Moving;
            stepDirection = direction;
            targetCell = destination;
            stepProgress = 0f;
            stepFrom = CellWorld(currentCell);
            stepTo = CellWorld(targetCell);

            StepStarted?.Invoke(this, direction);
            onStepStarted.Invoke(direction);
            return true;
        }

        private void Land()
        {
            grid.Release(currentCell, this);
            currentCell = targetCell;

            state = State.Idle;
            stepDirection = GridDirection.None;
            stepProgress = 0f;
            stepsTaken++;

            Arrived?.Invoke(this, currentCell);
            onArrived.Invoke(currentCell);
        }

        private void SetFacing(GridDirection direction)
        {
            if (direction == GridDirection.None || direction == facing) return;

            facing = direction;
            ApplyFacingRotation();
            FacingChanged?.Invoke(this, facing);
            onFacingChanged.Invoke(facing);
        }

        private void ApplyFacingRotation()
        {
            if (rotateToFacing != null)
                rotateToFacing.localRotation = Quaternion.Euler(0f, 0f, facing.ToAngle() + facingRotationOffset);
        }

        private Vector3 CellWorld(Vector2Int cell)
        {
            Vector3 p = grid.CellToWorld(cell);
            if (overrideZ) p.z = z;
            return p;
        }

        private void ApplyPosition()
        {
            transform.position = state == State.Moving
                ? Vector3.LerpUnclamped(stepFrom, stepTo, stepProfile.Evaluate(stepProgress))
                : CellWorld(currentCell);
        }
    }
}
