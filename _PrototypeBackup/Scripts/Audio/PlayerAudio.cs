using UnityEngine;

namespace Arena
{
    /// <summary>
    /// Turns <see cref="GridMover"/> events into Wwise events: Arrived → footstep,
    /// StepBlocked → bump, FacingChanged without a step → turn.
    /// </summary>
    [RequireComponent(typeof(GridMover))]
    [DisallowMultipleComponent]
    public sealed class PlayerAudio : AudioEmitter
    {
        [Header("Events")]
        [SerializeField] private AK.Wwise.Event footstepEvent = new();
        [SerializeField] private AK.Wwise.Event bumpEvent = new();
        [SerializeField] private AK.Wwise.Event turnEvent = new();

        [Header("Footsteps")]
        [SerializeField] private bool playFootsteps = true;

        [Tooltip("Minimum seconds between footsteps. Stops very short step durations from machine-gunning.")]
        [Min(0f)]
        [SerializeField] private float minFootstepInterval = 0f;

        [Header("Bumps")]
        [SerializeField] private bool playBumps = true;

        [Header("Turns")]
        [Tooltip("Play the turn sound when facing changes without a step — a turn in place, or bumping a wall in a new direction.")]
        [SerializeField] private bool playTurns = true;

        [Tooltip("Suppress the turn sound when a step starts in the same frame, so a normal turn-and-go plays only a footstep.")]
        [SerializeField] private bool muteTurnWhenStepping = true;

        [Header("Runtime (read-only)")]
        [InspectorReadOnly, SerializeField] private int footsteps;
        [InspectorReadOnly, SerializeField] private int bumps;
        [InspectorReadOnly, SerializeField] private int turns;

        private GridMover mover;
        private bool facingChangedThisFrame;
        private int lastStepFrame = -1;
        private float lastFootstepTime = -999f;

        protected override MixGroup DefaultMixGroup => MixGroup.Players;

        protected override void Awake()
        {
            base.Awake();
            mover = GetComponent<GridMover>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            mover.StepStarted += OnStepStarted;
            mover.Arrived += OnArrived;
            mover.StepBlocked += OnStepBlocked;
            mover.FacingChanged += OnFacingChanged;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            mover.StepStarted -= OnStepStarted;
            mover.Arrived -= OnArrived;
            mover.StepBlocked -= OnStepBlocked;
            mover.FacingChanged -= OnFacingChanged;
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!facingChangedThisFrame) return;
            facingChangedThisFrame = false;

            bool stepped = lastStepFrame == Time.frameCount;
            if (playTurns && !(muteTurnWhenStepping && stepped) && Post(turnEvent, "turn")) turns++;
        }

        private void OnStepStarted(GridMover m, GridDirection d) => lastStepFrame = Time.frameCount;

        private void OnArrived(GridMover m, Vector2Int cell)
        {
            if (!playFootsteps) return;
            if (Time.time - lastFootstepTime < minFootstepInterval) return;

            if (Post(footstepEvent, "footstep"))
            {
                footsteps++;
                lastFootstepTime = Time.time;
            }
        }

        private void OnStepBlocked(GridMover m, GridDirection d)
        {
            if (playBumps && Post(bumpEvent, "bump")) bumps++;
        }

        private void OnFacingChanged(GridMover m, GridDirection facing) => facingChangedThisFrame = true;
    }
}
