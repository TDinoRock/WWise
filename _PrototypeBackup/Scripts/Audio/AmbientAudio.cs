using System;
using System.Collections;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// Plays random one-shots at random intervals and optionally holds a loop while enabled.
    /// Place these around a level for drips, creaks, machinery hum and so on.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientAudio : AudioEmitter
    {
        [Serializable]
        public sealed class Shot
        {
            public AK.Wwise.Event evt = new();

            [Tooltip("Relative chance of being chosen. 0 disables this entry.")]
            [Min(0f)] public float weight = 1f;
        }

        [Header("One-shots")]
        [Tooltip("One entry is chosen at random, by weight, each time the interval elapses.")]
        [SerializeField] private Shot[] oneShots = Array.Empty<Shot>();

        [SerializeField] private bool playOneShots = true;

        [Min(0.05f)]
        [SerializeField] private float minInterval = 2f;

        [Min(0.05f)]
        [SerializeField] private float maxInterval = 6f;

        [Tooltip("Avoid playing the same entry twice in a row when more than one is available.")]
        [SerializeField] private bool avoidRepeats = true;

        [Tooltip("Random delay before the first one-shot, so several emitters don't all fire at t=0.")]
        [SerializeField] private bool randomiseFirstDelay = true;

        [Header("Loop")]
        [SerializeField] private bool playLoop = true;
        [SerializeField] private AK.Wwise.Event loopStartEvent = new();

        [Tooltip("Optional. If unset, the loop is stopped by calling Stop on the start event instead.")]
        [SerializeField] private AK.Wwise.Event loopStopEvent = new();

        [Tooltip("Fade-out in milliseconds when stopping the loop via the start event.")]
        [Min(0)]
        [SerializeField] private int loopStopFadeMs = 0;

        [Header("Runtime (read-only)")]
        [InspectorReadOnly, SerializeField] private bool loopPlaying;
        [InspectorReadOnly, SerializeField] private string lastOneShot = "";
        [InspectorReadOnly, SerializeField] private float nextOneShotIn;
        [InspectorReadOnly, SerializeField] private int oneShotsPlayed;

        public bool LoopPlaying => loopPlaying;

        private int lastIndex = -1;
        private Coroutine scheduler;

        protected override void OnDisable()
        {
            base.OnDisable();
            if (scheduler != null) { StopCoroutine(scheduler); scheduler = null; }
            StopLoop();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (maxInterval < minInterval) maxInterval = minInterval;
        }

        protected override void OnSoundEngineReady()
        {
            base.OnSoundEngineReady();
            if (playLoop) StartLoop();
            if (playOneShots && scheduler == null && oneShots.Length > 0) scheduler = StartCoroutine(Schedule());
        }

        [ContextMenu("Start Loop")]
        public void StartLoop()
        {
            if (loopPlaying || loopStartEvent == null || !loopStartEvent.IsValid()) return;
            loopPlaying = Post(loopStartEvent, "loop");
        }

        [ContextMenu("Stop Loop")]
        public void StopLoop()
        {
            if (!loopPlaying) return;
            loopPlaying = false;
            if (!AkUnitySoundEngine.IsInitialized()) return;

            if (loopStopEvent != null && loopStopEvent.IsValid()) loopStopEvent.Post(gameObject);
            else loopStartEvent?.Stop(gameObject, loopStopFadeMs);
        }

        private IEnumerator Schedule()
        {
            if (randomiseFirstDelay)
            {
                nextOneShotIn = UnityEngine.Random.Range(0f, maxInterval);
                yield return new WaitForSeconds(nextOneShotIn);
            }

            while (true)
            {
                if (playOneShots) PlayRandomOneShot();
                nextOneShotIn = UnityEngine.Random.Range(minInterval, maxInterval);
                yield return new WaitForSeconds(nextOneShotIn);
            }
        }

        /// <summary>Plays one weighted-random one-shot now. Handy from a UnityEvent or the context menu.</summary>
        [ContextMenu("Play Random One-Shot")]
        public void PlayRandomOneShot()
        {
            int index = PickIndex();
            if (index < 0) return;

            lastIndex = index;
            if (Post(oneShots[index].evt, "one-shot"))
            {
                oneShotsPlayed++;
                lastOneShot = oneShots[index].evt.Name;
            }
        }

        private int PickIndex()
        {
            float total = 0f;
            foreach (Shot s in oneShots)
                if (s.evt != null && s.evt.IsValid()) total += Mathf.Max(0f, s.weight);
            if (total <= 0f) return -1;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                float roll = UnityEngine.Random.Range(0f, total);
                for (int i = 0; i < oneShots.Length; i++)
                {
                    Shot s = oneShots[i];
                    if (s.evt == null || !s.evt.IsValid()) continue;

                    roll -= Mathf.Max(0f, s.weight);
                    if (roll > 0f) continue;
                    if (avoidRepeats && i == lastIndex && CountPlayable() > 1) break;   // re-roll
                    return i;
                }
            }
            return lastIndex >= 0 ? lastIndex : 0;
        }

        private int CountPlayable()
        {
            int n = 0;
            foreach (Shot s in oneShots)
                if (s.evt != null && s.evt.IsValid() && s.weight > 0f) n++;
            return n;
        }
    }
}
