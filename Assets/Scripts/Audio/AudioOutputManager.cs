using System.Collections.Generic;
using UnityEngine;

namespace GameAudio
{
    // The four outputs the Wwise project defines. The names must match Wwise exactly, because we
    // build Wwise object names from them as strings (e.g. "System_Tertiary", "Arena_Tertiary_Send").
    // "Quadernary" is spelled the way it is in the Wwise project on purpose.
    public enum OutputRoute { Primary = 0, Secondary = 1, Tertiary = 2, Quadernary = 3 }

    /// <summary>
    /// Opens the four Wwise outputs on real Windows audio devices (speakers, headphones, ...).
    ///
    /// HOW AN OUTPUT WORKS IN WWISE
    /// In the Wwise project each output is an "Audio Device ShareSet" (System, System_Secondary,
    /// System_Tertiary, System_Quadernary). Each ShareSet has its own master bus (Main Audio Bus,
    /// Secondary Audio Bus, ...). Anything mixed into that master bus comes out of whatever physical
    /// device the ShareSet is opened on. Wwise doesn't pick the device itself: the game does,
    /// at runtime, by calling AddOutput / ReplaceOutput with a ShareSet name + a Windows device id.
    ///
    /// The engine opens "System" on the default device when it starts. That is the Primary output.
    /// The other three don't exist until this script adds them.
    ///
    /// This object survives scene loads, so devices chosen in the main menu stay in effect in the levels.
    /// </summary>
    public class AudioOutputManager : MonoBehaviour
    {
        // One Windows audio device as Wwise reports it. id 0 means "the system default device".
        public struct Device
        {
            public string name;
            public uint id;
        }

        // One of the four outputs.
        [System.Serializable]
        public class Output
        {
            public OutputRoute route;
            [Tooltip("Off = output not opened at all. Primary can't be turned off, only moved to another device.")]
            public bool enabled = true;
            [Range(0f, 1f)] public float volume = 1f;

            // Filled in at runtime. Shown in the Inspector so you can see what happened.
            public string deviceName = "";
            public ulong outputId;          // the handle Wwise gives back from AddOutput; needed to change/remove it
            public bool isLive;             // true only if Windows actually opened the device

            public bool IsPrimary => route == OutputRoute.Primary;

            // Name of the Audio Device ShareSet in the Wwise project for this output.
            public string ShareSet
            {
                get
                {
                    if (IsPrimary) return "System";
                    return "System_" + route;
                }
            }
        }

        public static AudioOutputManager Instance { get; private set; }

        [SerializeField]
        private Output[] outputs =
        {
            new Output { route = OutputRoute.Primary },
            new Output { route = OutputRoute.Secondary, enabled = false },
            new Output { route = OutputRoute.Tertiary, enabled = false },
            new Output { route = OutputRoute.Quadernary, enabled = false },
        };

        // Devices currently plugged in and active, refreshed by RefreshDevices().
        private readonly List<Device> devices = new List<Device>();

        public IReadOnlyList<Device> Devices => devices;
        public Output Get(OutputRoute route) => outputs[(int)route];

        // Called after any output changes, so UI can redraw.
        public event System.Action Changed;

        private void Awake()
        {
            // Keep exactly one manager alive across scenes.
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Wwise's AkInitializer starts the sound engine in its own Awake. By Start it is normally up;
            // if not, ask to be called back when it is. Calling the engine before init does nothing.
            if (AkUnitySoundEngine.IsInitialized()) OpenSavedOutputs();
            else AkUnitySoundEngineInitialization.Instance.initializationDelegate += OpenSavedOutputs;
        }

        // Windows opens a device a moment after AddOutput returns, so "live" can read false at first.
        // Re-check twice a second and tell the UI when anything changes.
        private float nextStatusCheck;
        private void Update()
        {
            if (Time.unscaledTime < nextStatusCheck || !AkUnitySoundEngine.IsInitialized()) return;
            nextStatusCheck = Time.unscaledTime + 0.5f;

            bool changed = false;
            foreach (Output o in outputs)
            {
                bool live = o.outputId != 0 && AkUnitySoundEngine.GetSpeakerConfiguration(o.outputId).IsValid();
                if (live != o.isLive) { o.isLive = live; changed = true; }
            }
            if (changed) Changed?.Invoke();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (AkUnitySoundEngineInitialization.Instance != null)
                AkUnitySoundEngineInitialization.Instance.initializationDelegate -= OpenSavedOutputs;
        }

        // Re-opens every output on the device the player picked last time (saved in PlayerPrefs by name,
        // because Windows device ids can change between sessions but names usually don't).
        private void OpenSavedOutputs()
        {
            RefreshDevices();
            foreach (Output o in outputs)
            {
                int defaultEnabled = 0;
                if (o.enabled) defaultEnabled = 1;
                o.enabled = o.IsPrimary || PlayerPrefs.GetInt(Key(o, "enabled"), defaultEnabled) == 1;
                o.volume = PlayerPrefs.GetFloat(Key(o, "volume"), o.volume);
                Open(o.route, FindDevice(PlayerPrefs.GetString(Key(o, "device"), "")));
            }
        }

        /// <summary>Asks Wwise for the list of active Windows playback devices.</summary>
        public void RefreshDevices()
        {
            devices.Clear();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Wwise's own device list (not Unity's). The id it returns is what AkOutputSettings needs.
            uint count = AkUnitySoundEngine.GetWindowsDeviceCount(AkAudioDeviceState.AkDeviceState_Active);
            for (int i = 0; i < count; i++)
            {
                string name = AkUnitySoundEngine.GetWindowsDeviceName(i, out uint id, AkAudioDeviceState.AkDeviceState_Active);
                devices.Add(new Device { name = name, id = id });
            }
#endif
        }

        /// <summary>
        /// Opens one output on a device (id 0 = system default), or moves it there if already open.
        /// If the output is disabled, closes it instead.
        /// </summary>
        public void Open(OutputRoute route, Device device)
        {
            Output o = Get(route);

            if (!o.enabled && !o.IsPrimary) { Close(o); Save(o, device); Changed?.Invoke(); return; }

            // AkOutputSettings = "open this ShareSet on this device". An empty AkChannelConfig lets
            // Wwise use the device's own speaker layout (stereo, 5.1, ...).
            var settings = new AkOutputSettings(o.ShareSet, device.id, new AkChannelConfig(), AkPanningRule.AkPanningRule_Speakers);
            AKRESULT result;

            if (o.IsPrimary)
            {
                // The main output already exists (id 0 = "the main output"). Just retarget it.
                result = AkUnitySoundEngine.ReplaceOutput(settings, 0);
                o.outputId = AkUnitySoundEngine.GetOutputID(o.ShareSet, device.id);
            }
            else if (o.outputId != 0)
            {
                // Already open: move it to the new device. Wwise hands back a new id.
                result = AkUnitySoundEngine.ReplaceOutput(settings, o.outputId, out ulong newId);
                if (result == AKRESULT.AK_Success) o.outputId = newId;
            }
            else
            {
                // Not open yet: create it. From now on, everything reaching this ShareSet's master bus
                // (e.g. "Tertiary Audio Bus") plays on this device.
                result = AkUnitySoundEngine.AddOutput(settings, out ulong id);
                if (result == AKRESULT.AK_Success) o.outputId = id;
                else o.outputId = 0;
            }

            if (result != AKRESULT.AK_Success)
                Debug.LogWarning($"[Audio] {route}: opening '{o.ShareSet}' on '{device.name}' failed ({result}). " +
                                 "Does the ShareSet exist in the Wwise project, and were the SoundBanks regenerated?");

            if (device.id == 0) { o.deviceName = "System Default"; }
            else { o.deviceName = device.name; }
            ApplyVolume(o);
            Save(o, device);
            Changed?.Invoke();
        }

        /// <summary>Removes a non-primary output. Sounds sent to it are then simply not heard.</summary>
        private void Close(Output o)
        {
            if (o.outputId != 0) AkUnitySoundEngine.RemoveOutput(o.outputId);
            o.outputId = 0;
            o.isLive = false;
            o.deviceName = "Off";
        }

        public void SetEnabled(OutputRoute route, bool enabled, Device device)
        {
            Get(route).enabled = enabled || route == OutputRoute.Primary;
            Open(route, device);
        }

        public void SetVolume(OutputRoute route, float volume)
        {
            Output o = Get(route);
            o.volume = Mathf.Clamp01(volume);
            ApplyVolume(o);
            PlayerPrefs.SetFloat(Key(o, "volume"), o.volume);
        }

        // Whole-device volume (linear 0..1), applied on top of everything mixed into that output.
        private void ApplyVolume(Output o)
        {
            if (o.outputId == 0 && !o.IsPrimary) { o.isLive = false; return; }
            AkUnitySoundEngine.SetOutputVolume(o.outputId, o.volume);

            // A device that exists but can't stream (e.g. Realtek speakers + headphones are one chip;
            // Windows only drives one at a time) reports an invalid speaker configuration.
            o.isLive = AkUnitySoundEngine.GetSpeakerConfiguration(o.outputId).IsValid();
        }

        /// <summary>Device with this exact name, or the system default (id 0) if not found.</summary>
        public Device FindDevice(string name)
        {
            foreach (Device d in devices)
                if (d.name == name) return d;
            return new Device { name = "System Default", id = 0 };
        }

        private void Save(Output o, Device device)
        {
            int enabledValue = 0;
            if (o.enabled) enabledValue = 1;
            PlayerPrefs.SetInt(Key(o, "enabled"), enabledValue);

            string deviceValue = device.name;
            if (device.id == 0) deviceValue = "";
            PlayerPrefs.SetString(Key(o, "device"), deviceValue);
        }

        private static string Key(Output o, string field) => $"audio.{o.route}.{field}";
    }
}
