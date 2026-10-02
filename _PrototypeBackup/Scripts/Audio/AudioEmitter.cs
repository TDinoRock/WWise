using System.Collections.Generic;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// Shared base for anything that posts Wwise events from a game object.
    ///
    /// Lists every audio output with its own on/off and level. Sounds in the Wwise project have a
    /// muted dry path and use game-defined auxiliary sends, so this component decides per game
    /// object which outputs actually hear it — any combination, from none to all four.
    ///
    /// Requires an <see cref="AkGameObj"/> so Wwise knows about the object and its position.
    /// </summary>
    [RequireComponent(typeof(AkGameObj))]
    public abstract class AudioEmitter : MonoBehaviour
    {
        [Header("Outputs")]
        [Tooltip("Every audio output. Enable any combination; each has its own send level.")]
        [SerializeField] protected OutputSend[] outputs = DefaultOutputs();

        [Tooltip("Bus family this emitter mixes through on every output (the Players_ or Arena_ busses in Wwise).")]
        [SerializeField] protected MixGroup mixGroup = MixGroup.Arena;

        [Header("Behaviour")]
        [Tooltip("Push output changes made in the Inspector during Play mode immediately.")]
        [SerializeField] private bool liveUpdate = true;

        [Tooltip("Re-apply the sends every frame. Leave off unless another system is also writing aux sends on this object.")]
        [SerializeField] private bool reapplyEveryFrame = false;

        [Header("Runtime (read-only)")]
        [InspectorReadOnly, SerializeField] private bool routingApplied;
        [InspectorReadOnly, SerializeField] private string activeOutputs = "";
        [InspectorReadOnly, SerializeField] private string lastEvent = "";
        [InspectorReadOnly, SerializeField] private int eventsPosted;

        public IReadOnlyList<OutputSend> Outputs => outputs;
        public bool RoutingApplied => routingApplied;
        public int EventsPosted => eventsPosted;

        public MixGroup MixGroup
        {
            get => mixGroup;
            set { mixGroup = value; ApplyRouting(); }
        }

        /// <summary>The bus family a freshly added component of this type starts with.</summary>
        protected virtual MixGroup DefaultMixGroup => MixGroup.Arena;

        // AkGameObj's environment system can push an empty send list when it registers the object,
        // wiping ours. The sends are re-applied for the first few frames to win that race.
        private int guardFrames;
        private AkAuxSendArray sendArray;

        // ------------------------------------------------------------------ defaults & validation

        private static OutputSend[] DefaultOutputs()
        {
            var sends = new OutputSend[OutputRouting.RouteCount];
            for (int i = 0; i < sends.Length; i++)
                sends[i] = new OutputSend(OutputRouting.All[i], enabled: i == 0);
            return sends;
        }

        protected virtual void Reset()
        {
            outputs = DefaultOutputs();
            mixGroup = DefaultMixGroup;
            DisableEnvironmentSends();
        }

        protected virtual void OnValidate()
        {
            NormaliseOutputs();
            DisableEnvironmentSends();
            if (Application.isPlaying && liveUpdate && isActiveAndEnabled) ApplyRouting();
        }

        /// <summary>Keeps exactly one entry per output, in order, preserving whatever was configured.</summary>
        private void NormaliseOutputs()
        {
            if (outputs != null && outputs.Length == OutputRouting.RouteCount)
            {
                bool ordered = true;
                for (int i = 0; i < outputs.Length; i++)
                    if (outputs[i] == null || outputs[i].route != OutputRouting.All[i]) { ordered = false; break; }
                if (ordered)
                {
                    foreach (OutputSend s in outputs) s.output = s.route.ToString();
                    return;
                }
            }

            var fixedList = DefaultOutputs();
            if (outputs != null)
                foreach (OutputSend old in outputs)
                    if (old != null && (int)old.route >= 0 && (int)old.route < fixedList.Length)
                    {
                        fixedList[(int)old.route].enabled = old.enabled;
                        fixedList[(int)old.route].level = old.level;
                    }
            outputs = fixedList;
        }

        private void DisableEnvironmentSends()
        {
            var ak = GetComponent<AkGameObj>();
            if (ak != null && ak.isEnvironmentAware) ak.isEnvironmentAware = false;
        }

        // ------------------------------------------------------------------ lifecycle

        protected virtual void Awake()
        {
            NormaliseOutputs();
            DisableEnvironmentSends();
        }

        protected virtual void OnEnable()
        {
            if (AkUnitySoundEngine.IsInitialized()) OnSoundEngineReady();
            else if (AkUnitySoundEngineInitialization.Instance != null)
                AkUnitySoundEngineInitialization.Instance.initializationDelegate += OnSoundEngineReady;
        }

        protected virtual void OnDisable()
        {
            if (AkUnitySoundEngineInitialization.Instance != null)
                AkUnitySoundEngineInitialization.Instance.initializationDelegate -= OnSoundEngineReady;
            routingApplied = false;
        }

        protected virtual void OnDestroy()
        {
            sendArray?.Dispose();
            sendArray = null;
        }

        protected virtual void LateUpdate()
        {
            if (reapplyEveryFrame || guardFrames > 0)
            {
                if (guardFrames > 0) guardFrames--;
                ApplyRouting();
            }
        }

        /// <summary>Called once the sound engine is up. Override to start loops, schedule one-shots, etc.</summary>
        protected virtual void OnSoundEngineReady()
        {
            guardFrames = 3;
            ApplyRouting();
        }

        // ------------------------------------------------------------------ routing API

        /// <summary>
        /// Pushes the enabled outputs and their levels to Wwise as this game object's aux sends.
        /// With nothing enabled the emitter is silent on every output.
        /// </summary>
        [ContextMenu("Apply Routing")]
        public bool ApplyRouting()
        {
            if (!AkUnitySoundEngine.IsInitialized()) { routingApplied = false; return false; }

            sendArray ??= new AkAuxSendArray();
            sendArray.Reset();

            var names = new List<string>(OutputRouting.RouteCount);
            foreach (OutputSend s in outputs)
            {
                if (s == null || !s.IsAudible) continue;
                sendArray.Add(OutputRouting.AuxBusId(mixGroup, s.route), s.level);
                names.Add(s.level < 1f ? $"{s.route} {s.level:0.##}" : s.route.ToString());
            }

            AKRESULT result = AkUnitySoundEngine.SetGameObjectAuxSendValues(gameObject, sendArray, (uint)sendArray.Count());
            routingApplied = result == AKRESULT.AK_Success;
            activeOutputs = names.Count > 0 ? string.Join(", ", names) : "(none - silent)";
            return routingApplied;
        }

        /// <summary>Turns one output on or off, optionally setting its level too.</summary>
        public void SetOutput(OutputRoute route, bool enabled, float? level = null)
        {
            OutputSend s = outputs[(int)route];
            s.enabled = enabled;
            if (level.HasValue) s.level = Mathf.Clamp01(level.Value);
            ApplyRouting();
        }

        public void SetOutputLevel(OutputRoute route, float level)
        {
            outputs[(int)route].level = Mathf.Clamp01(level);
            ApplyRouting();
        }

        public bool IsOutputEnabled(OutputRoute route) => outputs[(int)route].enabled;

        /// <summary>Enables exactly one output and disables the rest.</summary>
        public void EnableOnly(OutputRoute route)
        {
            foreach (OutputSend s in outputs) s.enabled = s.route == route;
            ApplyRouting();
        }

        [ContextMenu("Outputs/Enable All")]
        public void EnableAll() { foreach (OutputSend s in outputs) s.enabled = true; ApplyRouting(); }

        [ContextMenu("Outputs/Disable All")]
        public void DisableAll() { foreach (OutputSend s in outputs) s.enabled = false; ApplyRouting(); }

        [ContextMenu("Outputs/Primary Only")]    private void OnlyPrimary() => EnableOnly(OutputRoute.Primary);
        [ContextMenu("Outputs/Secondary Only")]  private void OnlySecondary() => EnableOnly(OutputRoute.Secondary);
        [ContextMenu("Outputs/Tertiary Only")]   private void OnlyTertiary() => EnableOnly(OutputRoute.Tertiary);
        [ContextMenu("Outputs/Quadernary Only")] private void OnlyQuadernary() => EnableOnly(OutputRoute.Quadernary);

        // ------------------------------------------------------------------ posting

        /// <summary>Posts an event on this game object. Returns false if the event is unset or invalid.</summary>
        protected bool Post(AK.Wwise.Event evt, string label = null)
        {
            if (evt == null || !evt.IsValid() || !AkUnitySoundEngine.IsInitialized()) return false;

            uint playingId = evt.Post(gameObject);
            if (playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID) return false;

            eventsPosted++;
            lastEvent = label != null ? $"{label} ({evt.Name})" : evt.Name;
            return true;
        }
    }
}
