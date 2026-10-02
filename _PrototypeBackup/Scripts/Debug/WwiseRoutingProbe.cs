using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace Arena.DebugTools
{
    /// <summary>
    /// Diagnostic. Connects Wwise Authoring's profiler to this running sound engine over WAAPI and
    /// dumps every live bus instance with the output device it renders on, plus the active voices.
    /// That is the only reliable way to prove a sound actually reached the device you intended.
    ///
    /// Needs Wwise Authoring open on this project with WAAPI enabled (ws://127.0.0.1:8080).
    /// Add it to a GameObject in Play mode; the report lands in <see cref="Report"/> and in a text
    /// file under Application.persistentDataPath.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WwiseRoutingProbe : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string waapiUri = "ws://127.0.0.1:8080/waapi";
        [Min(1000)]
        [SerializeField] private int timeoutMs = 10000;

        [Header("Capture")]
        [Tooltip("Seconds to let sound play before sampling the profiler.")]
        [Min(0.1f)]
        [SerializeField] private float captureSeconds = 2f;

        [Tooltip("Optional. Emitters to trigger while capturing, so there is something to see.")]
        [SerializeField] private AmbientAudio[] pokeEmitters = Array.Empty<AmbientAudio>();

        [Tooltip("Write the report next to the player log as well as to the Inspector.")]
        [SerializeField] private bool writeToFile = true;

        [Header("Result (read-only)")]
        [InspectorReadOnly, SerializeField, TextArea(6, 30)] private string report = "";

        public string Report => report;

        private object wamp;
        private MethodInfo call;

        private IEnumerator Start() => Run();

        [ContextMenu("Run Probe")]
        public void RunFromMenu()
        {
            if (Application.isPlaying) StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            report = "";

            Type wampType = Type.GetType("Wamp, Ak.Wwise.Api.WAAPI");
            if (wampType == null) { Log("Wamp type not found — is the Wwise WAAPI assembly present?"); yield break; }

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            wamp = Activator.CreateInstance(wampType);
            call = wampType.GetMethod("Call", flags);
            MethodInfo connect = wampType.GetMethod("Connect", flags);

            var connectTask = (Task)connect.Invoke(wamp, new object[] { waapiUri, timeoutMs });
            yield return Await(connectTask);
            if (connectTask.IsFaulted) { Log("could not connect — is Wwise Authoring open?"); yield break; }
            Log("connected");

            yield return Call("ak.wwise.core.remote.connect", "{\"host\":\"127.0.0.1\"}", "{}", _ => { });
            yield return new WaitForSeconds(1.5f);
            yield return Call("ak.wwise.core.remote.getConnectionStatus", "{}", "{}",
                r => Log(r.Contains("\"isConnected\":true") ? "profiler attached" : $"profiler not attached: {r}"));
            yield return Call("ak.wwise.core.profiler.startCapture", "{}", "{}", _ => { });

            foreach (AmbientAudio e in pokeEmitters)
                if (e != null) e.PlayRandomOneShot();

            yield return new WaitForSeconds(captureSeconds);

            yield return Call("ak.wwise.core.profiler.getBusses", "{\"time\":\"capture\"}",
                "{\"return\":[\"objectName\",\"deviceID\",\"voiceCount\",\"gameObjectName\",\"pipelineID\",\"depth\"]}",
                r => Log($"BUSSES {r}"));

            yield return Call("ak.wwise.core.profiler.getVoices", "{\"time\":\"capture\"}",
                "{\"return\":[\"objectName\",\"gameObjectName\",\"playTargetName\",\"isVirtual\",\"isStarted\"]}",
                r => Log($"VOICES {r}"));

            yield return Call("ak.wwise.core.profiler.stopCapture", "{}", "{}", _ => { });
            Log("done");

            if (writeToFile)
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "wwise_routing_probe.txt");
                System.IO.File.WriteAllText(path, report);
                Debug.Log($"[WwiseRoutingProbe] wrote {path}");
            }
        }

        private IEnumerator Await(Task t)
        {
            while (!t.IsCompleted) yield return null;
            if (t.IsFaulted) Log($"FAULT {t.Exception?.InnerException?.Message}");
        }

        private IEnumerator Call(string uri, string args, string options, Action<string> onResult)
        {
            var t = (Task<string>)call.Invoke(wamp, new object[] { uri, args, options, timeoutMs });
            yield return Await(t);
            if (!t.IsFaulted) onResult(t.Result);
        }

        private void Log(string line) => report += line + "\n";
    }
}
