using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshPro: sharp, scalable text (replaces the legacy Text component)

namespace GameAudio
{
    /// <summary>
    /// The audio settings menu. One row per output: pick a device, set the volume, press Test.
    /// This script only connects the UI to AudioOutputManager (which opens the devices) and
    /// RoutedSound (which plays a test sound on exactly one output).
    /// </summary>
    public class AudioMenu : MonoBehaviour
    {
        // The UI controls for one output, wired up in the scene.
        [System.Serializable]
        public class Row
        {
            public OutputRoute output;
            public TMP_Dropdown device;
            public Slider volume;
            public Button test;
            public TMP_Text status;
        }

        public Row[] rows = new Row[4];

        [Tooltip("Plays the test sound. Its sends are switched to one output per Test press.")]
        public RoutedSound testSound;

        [Tooltip("Optional button that re-reads the device list (e.g. after plugging in headphones).")]
        public Button refreshButton;

        // Dropdown entries for each row, in the same order as the dropdown's options.
        // Non-primary rows start with "Off"; every row then has "System Default" and the real devices.
        private readonly Dictionary<OutputRoute, List<(bool on, AudioOutputManager.Device device)>> choices =
            new Dictionary<OutputRoute, List<(bool, AudioOutputManager.Device)>>();

        private AudioOutputManager Manager => AudioOutputManager.Instance;

        private void Start()
        {
            foreach (Row row in rows)
            {
                Row r = row; // copy for the lambdas below
                r.device.onValueChanged.AddListener(i => OnDeviceChosen(r, i));
                r.volume.onValueChanged.AddListener(v => Manager.SetVolume(r.output, v));
                r.test.onClick.AddListener(() => testSound.PlayOnly(r.output));
            }
            if (refreshButton != null) refreshButton.onClick.AddListener(Rebuild);

            // If the sound engine is already running, fill the dropdowns now; otherwise OnEnable
            // asked to be called back when it starts.
            if (AkUnitySoundEngine.IsInitialized()) Rebuild();
        }

        private void OnEnable()
        {
            if (Manager != null) Manager.Changed += RefreshStatus;

            // Done here (not in Start) so it is re-registered whenever the menu is re-enabled.
            if (!AkUnitySoundEngine.IsInitialized() && AkUnitySoundEngineInitialization.Instance != null)
                AkUnitySoundEngineInitialization.Instance.initializationDelegate += Rebuild;
        }

        private void OnDisable()
        {
            if (Manager != null) Manager.Changed -= RefreshStatus;
            if (AkUnitySoundEngineInitialization.Instance != null)
                AkUnitySoundEngineInitialization.Instance.initializationDelegate -= Rebuild;
        }

        /// <summary>Re-reads the devices and fills every dropdown, selecting each output's current device.</summary>
        private void Rebuild()
        {
            // OnEnable may have run before the manager's Awake; subscribe now if so.
            Manager.Changed -= RefreshStatus;
            Manager.Changed += RefreshStatus;

            Manager.RefreshDevices();

            foreach (Row r in rows)
            {
                var output = Manager.Get(r.output);
                var list = new List<(bool, AudioOutputManager.Device)>();
                var labels = new List<string>();

                if (!output.IsPrimary) { list.Add((false, default)); labels.Add("Off"); }
                list.Add((true, new AudioOutputManager.Device { name = "System Default", id = 0 }));
                labels.Add("System Default");
                foreach (var d in Manager.Devices) { list.Add((true, d)); labels.Add(d.name); }
                choices[r.output] = list;

                r.device.ClearOptions();
                r.device.AddOptions(labels);
            }
            RefreshStatus();
        }

        private void OnDeviceChosen(Row r, int index)
        {
            var (on, device) = choices[r.output][index];
            Manager.SetEnabled(r.output, on, device);
        }

        // Makes every row match what the manager actually has open (the manager may open saved
        // devices after this menu was built), and shows whether each output actually opened.
        // "Not streaming" usually means Windows can't run that device at the same time as another
        // one (two jacks on the same sound chip), or it is still opening.
        private void RefreshStatus()
        {
            foreach (Row r in rows)
            {
                var o = Manager.Get(r.output);
                if (choices.TryGetValue(r.output, out var list))
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].on == o.enabled && (!o.enabled || list[i].device.name == o.deviceName))
                            r.device.SetValueWithoutNotify(i); // "WithoutNotify" so this doesn't re-open the device
                r.volume.SetValueWithoutNotify(o.volume);
                if (!o.enabled) r.status.text = "Off";
                else if (o.isLive) r.status.text = "Live";
                else r.status.text = "Not streaming";
                r.test.interactable = o.enabled;
            }
        }
    }
}
