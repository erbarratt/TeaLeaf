using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Debug-only: plays a sound cue from this object's position over and
    /// over, as a stand-in for something that makes sound (a guard's
    /// footsteps) until real emitters exist. Leave it running and walk
    /// round it to hear how the sound changes through doorways and walls.
    /// </summary>
    public class SoundEmitterDebug : MonoBehaviour
    {
        [SerializeField] private SoundCue cue;

        // Seconds between plays.
        [SerializeField] private float interval = 0.5f;

        // Also emit the cue's noise for guards each time. Off by default,
        // so a repeating test sound doesn't flood the noise debug view.
        [SerializeField] private bool emitNoise;

        // Counts down to the next play.
        private float _timer;

        // Every enabled debug emitter, so SoundDebug can list them all in
        // its report.
        private static readonly List<SoundEmitterDebug> _all = new();

        /// How many debug emitters are enabled.
        public static int Count => _all.Count;

        /// The cue this emitter plays.
        public SoundCue Cue => cue;

        /// <summary>
        /// The debug emitter at index - read with Count in a for loop.
        /// </summary>
        public static SoundEmitterDebug Get(int index)
        {
            return _all[index];
        }

        private void OnEnable()
        {
            _all.Add(this);
        }

        private void OnDisable()
        {
            _all.Remove(this);
        }

        private void Update()
        {
            _timer -= Time.deltaTime;

            if (_timer > 0f) {
                return;
            }

            _timer = interval;

            if (cue != null && SoundPlayer.Instance != null) {
                SoundPlayer.Instance.Play(cue, transform.position, transform, emitNoise ? 1f : 0f);
            }
        }
    }
}
