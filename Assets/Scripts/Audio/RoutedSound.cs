using UnityEngine;

namespace GameAudio
{
    /// <summary>
    /// Plays a Wwise Event from this GameObject and decides which of the four outputs hear it.
    ///
    /// HOW THE SOUND GETS FROM WWISE INTO UNITY
    /// 1. In Wwise, a Sound (the WAV) is played by an Event (e.g. "Play_Chirp").
    /// 2. Generating SoundBanks writes the sounds + events into .bnk files under StreamingAssets.
    ///    This project uses auto-defined SoundBanks, so the Unity integration loads the right bank
    ///    automatically when an event is used. No manual bank loading needed.
    /// 3. The "AK.Wwise.Event" field below is a reference picked from the Wwise Picker in the
    ///    Inspector. It stores the event's id. Calling Post(gameObject) tells the Wwise sound engine
    ///    "play this event on this game object". Unity's own AudioSource is not involved at all.
    /// 4. The AkGameObj component registers this GameObject with Wwise (and its position), which
    ///    Wwise requires before anything can be posted on it.
    ///
    /// HOW THE SOUND GETS TO A SPECIFIC OUTPUT
    /// In the Wwise project every Sound's normal ("dry") path goes to a muted bus, so on its own it
    /// is silent everywhere. Each Sound also has "Use game-defined auxiliary sends" on. That means
    /// the GAME chooses which aux busses the sound is sent into, per game object. There is one aux
    /// bus per output, named "{mixGroup}_{Output}_Send", e.g.:
    ///     Arena_Tertiary_Send -> Arena_Tertiary -> Tertiary Audio Bus -> System_Tertiary (a device)
    /// So enabling "Tertiary" below = sending into Arena_Tertiary_Send = heard on the Tertiary device.
    /// Enable several outputs and the same sound plays on several devices at once.
    /// </summary>
    [RequireComponent(typeof(AkGameObj))]
    public class RoutedSound : MonoBehaviour
    {
        [System.Serializable]
        public class Send
        {
            public OutputRoute output;
            public bool enabled;
            [Tooltip("Linear gain: 1 = full, 0.5 = -6 dB, 0.25 = -12 dB.")]
            [Range(0f, 1f)] public float level = 1f;
        }

        [Tooltip("The Wwise Event to play. Pick it with the Wwise Picker.")]
        public AK.Wwise.Event playEvent = new AK.Wwise.Event();

        [Tooltip("Optional event to stop a looping sound (e.g. Stop_Hum).")]
        public AK.Wwise.Event stopEvent = new AK.Wwise.Event();

        [Tooltip("Which bus family in Wwise to send through: \"Arena\" or \"Players\" (the prefix of the _Send aux busses).")]
        public string mixGroup = "Arena";

        [Tooltip("One row per output. Turn on any combination.")]
        public Send[] sends =
        {
            new Send { output = OutputRoute.Primary, enabled = true },
            new Send { output = OutputRoute.Secondary },
            new Send { output = OutputRoute.Tertiary },
            new Send { output = OutputRoute.Quadernary },
        };

        public bool playOnStart;

        private void Reset() => DisableEnvironmentSends();

        private void Awake() => DisableEnvironmentSends();

        // AkGameObj can manage aux sends itself for Wwise "environment" zones. When it does, it
        // overwrites ours with an empty list, which would silence the sound. We route manually, so turn it off.
        private void DisableEnvironmentSends() => GetComponent<AkGameObj>().isEnvironmentAware = false;

        private void Start()
        {
            if (playOnStart) Play();
        }

        /// <summary>Pushes the current sends to Wwise, then posts the event.</summary>
        public void Play()
        {
            if (!AkUnitySoundEngine.IsInitialized()) return;
            ApplySends();
            playEvent.Post(gameObject);
        }

        public void Stop()
        {
            if (AkUnitySoundEngine.IsInitialized() && stopEvent.IsValid()) stopEvent.Post(gameObject);
        }

        /// <summary>Turns on one output and turns off the rest.</summary>
        public void PlayOnly(OutputRoute output)
        {
            foreach (Send s in sends) s.enabled = s.output == output;
            Play();
        }

        /// <summary>
        /// Tells Wwise which aux busses this game object sends into, and how loud.
        /// Applies to sounds already playing on this object as well as new ones.
        /// </summary>
        public void ApplySends()
        {
            // AkAuxSendArray is the integration's wrapper for a native list of (aux bus id, level).
            using (var array = new AkAuxSendArray())
            {
                foreach (Send s in sends)
                {
                    if (!s.enabled || s.level <= 0f) continue;

                    // Wwise identifies objects by a 32-bit hash of their name, so the bus name
                    // string becomes the id Wwise expects.
                    uint auxBusId = AkUnitySoundEngine.GetIDFromString($"{mixGroup}_{s.output}_Send");
                    array.Add(auxBusId, s.level);
                }

                // An empty list = no sends = silent (the dry path is muted in Wwise).
                AkUnitySoundEngine.SetGameObjectAuxSendValues(gameObject, array, (uint)array.Count());
            }
        }

        // Inspector changes during Play mode take effect immediately, even on a playing loop.
        private void OnValidate()
        {
            if (Application.isPlaying && AkUnitySoundEngine.IsInitialized()) ApplySends();
        }
    }
}
