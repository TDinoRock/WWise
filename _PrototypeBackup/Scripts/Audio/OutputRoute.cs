using System;

namespace Arena
{
    /// <summary>
    /// The four physical audio outputs. Each master bus in the Wwise project carries its own Audio
    /// Device ShareSet, and <see cref="AudioOutputManager"/> binds each ShareSet to a real device:
    ///
    /// <list type="bullet">
    ///   <item>Primary    → Main Audio Bus       → ShareSet <c>System</c></item>
    ///   <item>Secondary  → Secondary Audio Bus  → ShareSet <c>System_Secondary</c></item>
    ///   <item>Tertiary   → Tertiary Audio Bus   → ShareSet <c>System_Tertiary</c></item>
    ///   <item>Quadernary → Quadernary Audio Bus → ShareSet <c>System_Quadernary</c></item>
    /// </list>
    ///
    /// A sound is not tied to any one of these. Its dry path is muted and it reaches the outputs
    /// through game-defined auxiliary sends, so an <see cref="AudioEmitter"/> can feed any
    /// combination of outputs, each at its own level.
    ///
    /// "Quadernary" matches the spelling of the Wwise objects; the names must agree exactly.
    /// </summary>
    public enum OutputRoute
    {
        Primary = 0,
        Secondary = 1,
        Tertiary = 2,
        Quadernary = 3,
    }

    /// <summary>Which bus family a sound mixes through on every output. Mirrors the Players_/Arena_ busses.</summary>
    public enum MixGroup
    {
        Players = 0,
        Arena = 1,
    }

    public static class OutputRouting
    {
        /// <summary>Number of outputs, so array sizes and loops stay in step with the enum.</summary>
        public const int RouteCount = 4;

        /// <summary>Every route, in order.</summary>
        public static readonly OutputRoute[] All =
            { OutputRoute.Primary, OutputRoute.Secondary, OutputRoute.Tertiary, OutputRoute.Quadernary };

        /// <summary>Default Audio Device ShareSet for a route, matching the Wwise project.</summary>
        public static string DefaultShareSet(this OutputRoute route)
            => route == OutputRoute.Primary ? "System" : $"System_{route}";

        /// <summary>Master bus name for a route, matching the Wwise project.</summary>
        public static string MasterBus(this OutputRoute route)
            => route == OutputRoute.Primary ? "Main Audio Bus" : $"{route} Audio Bus";

        /// <summary>The auxiliary bus a sound in <paramref name="group"/> sends into to reach <paramref name="route"/>.</summary>
        public static string AuxBusName(MixGroup group, OutputRoute route) => $"{group}_{route}_Send";

        private static readonly uint[,] auxIds = new uint[2, RouteCount];

        /// <summary>Wwise short ID of the aux bus, hashed once and cached.</summary>
        public static uint AuxBusId(MixGroup group, OutputRoute route)
        {
            ref uint id = ref auxIds[(int)group, (int)route];
            if (id == 0) id = AkUnitySoundEngine.GetIDFromString(AuxBusName(group, route));
            return id;
        }
    }

    /// <summary>One output's send from an emitter: on/off plus a level. Shown as a row per output in the Inspector.</summary>
    [Serializable]
    public sealed class OutputSend
    {
        // First string field, so Unity uses it as the array element's label.
        [InspectorReadOnly] public string output = "";
        [InspectorReadOnly] public OutputRoute route;

        public bool enabled;

        [UnityEngine.Tooltip("Send level into this output. 1 = full level, 0 = silent.")]
        [UnityEngine.Range(0f, 1f)] public float level = 1f;

        public OutputSend() { }

        public OutputSend(OutputRoute route, bool enabled = false, float level = 1f)
        {
            this.route = route;
            output = route.ToString();
            this.enabled = enabled;
            this.level = level;
        }

        public bool IsAudible => enabled && level > 0f;
    }
}
