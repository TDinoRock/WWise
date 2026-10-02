using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Arena
{
    /// <summary>
    /// Scene singleton that owns the logical grid.
    ///
    /// Levels are painted with Tilemaps. On load the floor and blocking Tilemaps are flattened into a
    /// flat walkability array; nothing outside this class queries a Tilemap for gameplay. GridService
    /// also owns per-cell occupancy, so two characters can never share a tile or swap through each other.
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Grid))]
    [DefaultExecutionOrder(-200)]
    public sealed class GridService : MonoBehaviour
    {
        private static GridService instance;

        /// <summary>The scene's GridService. Falls back to a scene search after a domain reload.</summary>
        public static GridService Instance
        {
            get
            {
                if (instance == null) instance = FindAnyObjectByType<GridService>();
                return instance;
            }
            private set => instance = value;
        }

        [Header("Tilemap Sources")]
        [Tooltip("A cell with a tile on any of these maps is walkable, unless a blocking map overrides it.")]
        [SerializeField] private Tilemap[] floorTilemaps = Array.Empty<Tilemap>();

        [Tooltip("A cell with a tile on any of these maps is never walkable.")]
        [SerializeField] private Tilemap[] blockingTilemaps = Array.Empty<Tilemap>();

        [Header("Baking")]
        [Tooltip("Bake the walkability array automatically in Awake.")]
        [SerializeField] private bool bakeOnAwake = true;

        [Tooltip("Treat cells outside every Tilemap's bounds as walkable (open world) instead of blocked (enclosed room).")]
        [SerializeField] private bool outOfBoundsWalkable = false;

        [Tooltip("Log a warning when a mover starts on a blocked or already-occupied cell.")]
        [SerializeField] private bool warnOnBadSpawn = true;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool drawGizmosOnlyWhenSelected = false;
        [Range(0.1f, 1f)]
        [SerializeField] private float gizmoCellScale = 0.9f;
        [SerializeField] private Color walkableGizmoColor = new Color(0f, 1f, 0f, 0.12f);
        [SerializeField] private Color blockedGizmoColor = new Color(1f, 0f, 0f, 0.22f);
        [SerializeField] private Color occupiedGizmoColor = new Color(1f, 1f, 0f, 0.40f);

        [Header("Runtime (read-only)")]
        [InspectorReadOnly, SerializeField] private bool isBaked;
        [InspectorReadOnly, SerializeField] private Vector2Int origin;
        [InspectorReadOnly, SerializeField] private Vector2Int size;
        [InspectorReadOnly, SerializeField] private int walkableCellCount;
        [InspectorReadOnly, SerializeField] private int occupiedCellCount;

        /// <summary>Raised after every bake. Movers use this to re-snap and re-reserve their cells.</summary>
        public event Action Baked;

        public bool IsBaked => isBaked;
        public Vector2Int Origin => origin;
        public Vector2Int Size => size;
        public int WalkableCellCount => walkableCellCount;
        public UnityEngine.Grid UnityGrid { get; private set; }
        public float CellSize => UnityGrid != null ? UnityGrid.cellSize.x : 1f;

        private bool[] walkable = Array.Empty<bool>();
        private Component[] occupants = Array.Empty<Component>();

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogError($"[GridService] More than one GridService in the scene; destroying the one on '{name}'.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            UnityGrid = GetComponent<UnityEngine.Grid>();
            if (bakeOnAwake) Bake();
        }

        private void OnEnable()
        {
            // After a domain reload in Play mode the serialized flags survive but the arrays do not.
            if (instance == null) Instance = this;
            if (UnityGrid == null) UnityGrid = GetComponent<UnityEngine.Grid>();
            if (Application.isPlaying && isBaked && walkable.Length != size.x * size.y) Bake();
        }

        private void OnDestroy()
        {
            if (instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ baking

        /// <summary>
        /// Flattens the Tilemaps into the walkability array. Safe to call at runtime; occupancy is
        /// cleared and every mover re-registers via <see cref="Baked"/>.
        /// </summary>
        [ContextMenu("Bake Now")]
        public void Bake()
        {
            if (UnityGrid == null) UnityGrid = GetComponent<UnityEngine.Grid>();

            BoundsInt bounds = ComputeUnionBounds();
            origin = new Vector2Int(bounds.xMin, bounds.yMin);
            size = new Vector2Int(Mathf.Max(bounds.size.x, 0), Mathf.Max(bounds.size.y, 0));

            walkable = new bool[size.x * size.y];
            occupants = new Component[size.x * size.y];
            walkableCellCount = 0;
            occupiedCellCount = 0;

            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                var cell = new Vector3Int(origin.x + x, origin.y + y, 0);
                bool isFloor = false;

                foreach (Tilemap floor in floorTilemaps)
                    if (floor != null && floor.HasTile(cell)) { isFloor = true; break; }

                if (isFloor)
                    foreach (Tilemap blocker in blockingTilemaps)
                        if (blocker != null && blocker.HasTile(cell)) { isFloor = false; break; }

                walkable[x + y * size.x] = isFloor;
                if (isFloor) walkableCellCount++;
            }

            isBaked = true;
            Baked?.Invoke();
        }

        private BoundsInt ComputeUnionBounds()
        {
            bool any = false;
            int xMin = 0, yMin = 0, xMax = 0, yMax = 0;

            void Include(Tilemap map)
            {
                if (map == null) return;
                map.CompressBounds();
                BoundsInt b = map.cellBounds;
                if (b.size.x <= 0 || b.size.y <= 0) return;

                if (!any) { xMin = b.xMin; yMin = b.yMin; xMax = b.xMax; yMax = b.yMax; any = true; }
                else
                {
                    xMin = Mathf.Min(xMin, b.xMin); yMin = Mathf.Min(yMin, b.yMin);
                    xMax = Mathf.Max(xMax, b.xMax); yMax = Mathf.Max(yMax, b.yMax);
                }
            }

            foreach (Tilemap m in floorTilemaps) Include(m);
            foreach (Tilemap m in blockingTilemaps) Include(m);

            return any ? new BoundsInt(xMin, yMin, 0, xMax - xMin, yMax - yMin, 1)
                       : new BoundsInt(0, 0, 0, 0, 0, 1);
        }

        // ------------------------------------------------------------------ queries

        public bool IsInBounds(Vector2Int cell)
            => cell.x >= origin.x && cell.y >= origin.y
            && cell.x < origin.x + size.x && cell.y < origin.y + size.y;

        private int IndexOf(Vector2Int cell) => (cell.x - origin.x) + (cell.y - origin.y) * size.x;

        /// <summary>Static walkability only; ignores occupancy. See <see cref="CanEnter"/>.</summary>
        public bool IsWalkable(Vector2Int cell)
        {
            if (!isBaked || !IsInBounds(cell)) return outOfBoundsWalkable;
            return walkable[IndexOf(cell)];
        }

        /// <summary>
        /// Overrides one cell's walkability at runtime, for doors opening or destructible walls.
        /// Does not touch Tilemap visuals. Returns false if out of bounds.
        /// </summary>
        public bool SetWalkable(Vector2Int cell, bool value)
        {
            if (!isBaked || !IsInBounds(cell)) return false;

            int i = IndexOf(cell);
            if (walkable[i] == value) return true;

            walkable[i] = value;
            walkableCellCount += value ? 1 : -1;
            return true;
        }

        public bool IsOccupied(Vector2Int cell) => GetOccupant(cell) != null;

        public Component GetOccupant(Vector2Int cell)
            => isBaked && IsInBounds(cell) ? occupants[IndexOf(cell)] : null;

        /// <summary>True if <paramref name="who"/> may step into the cell: walkable and free (or already theirs).</summary>
        public bool CanEnter(Vector2Int cell, Component who)
        {
            if (!IsWalkable(cell)) return false;
            Component current = GetOccupant(cell);
            return current == null || current == who;
        }

        /// <summary>
        /// Atomically claims a cell. Movers reserve their destination when a step begins and hold the
        /// origin until they arrive, which is what makes swapping through each other impossible.
        /// </summary>
        public bool TryReserve(Vector2Int cell, Component who)
        {
            if (who == null || !isBaked || !IsInBounds(cell) || !CanEnter(cell, who)) return false;

            int i = IndexOf(cell);
            if (occupants[i] == null) occupiedCellCount++;
            occupants[i] = who;
            return true;
        }

        /// <summary>Releases a cell, but only if <paramref name="who"/> currently holds it.</summary>
        public void Release(Vector2Int cell, Component who)
        {
            if (!isBaked || !IsInBounds(cell)) return;

            int i = IndexOf(cell);
            if (occupants[i] == who)
            {
                occupants[i] = null;
                occupiedCellCount--;
            }
        }

        internal bool WarnOnBadSpawn => warnOnBadSpawn;

        // ------------------------------------------------------------------ conversions

        public Vector3 CellToWorld(Vector2Int cell)
        {
            if (UnityGrid == null) UnityGrid = GetComponent<UnityEngine.Grid>();
            return UnityGrid.GetCellCenterWorld(new Vector3Int(cell.x, cell.y, 0));
        }

        public Vector2Int WorldToCell(Vector3 world)
        {
            if (UnityGrid == null) UnityGrid = GetComponent<UnityEngine.Grid>();
            Vector3Int c = UnityGrid.WorldToCell(world);
            return new Vector2Int(c.x, c.y);
        }

        // ------------------------------------------------------------------ gizmos

        private void OnDrawGizmos() { if (!drawGizmosOnlyWhenSelected) DrawGrid(); }
        private void OnDrawGizmosSelected() { if (drawGizmosOnlyWhenSelected) DrawGrid(); }

        private void DrawGrid()
        {
            if (!drawGizmos || !isBaked || UnityGrid == null) return;
            if (walkable.Length != size.x * size.y) return;   // stale serialized state after a reload

            Vector3 cube = new Vector3(UnityGrid.cellSize.x, UnityGrid.cellSize.y, 0.01f) * gizmoCellScale;

            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                int i = x + y * size.x;
                Gizmos.color = occupants[i] != null ? occupiedGizmoColor
                             : walkable[i] ? walkableGizmoColor
                             : blockedGizmoColor;
                Gizmos.DrawCube(CellToWorld(new Vector2Int(origin.x + x, origin.y + y)), cube);
            }
        }
    }
}
