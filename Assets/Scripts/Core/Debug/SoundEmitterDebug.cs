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
        [Tooltip("The sound played over and over, e.g. a footstep or bell SoundCue. With Surface Sounds set, it plays only when no floor is found. Left empty, nothing plays (unless walking on a found floor).")]
        [SerializeField] private SoundCue cue;

        // Seconds between plays.
        [Tooltip("Seconds between plays. e.g. 0.5 = two a second, like a guard walking. Lower = plays more often.")]
        [SerializeField] private float interval = 0.5f;

        // Also emit the cue's noise for guards each time. Off by default,
        // so a repeating test sound doesn't flood the noise debug view.
        [Tooltip("On: each play also makes the cue's noise for guards to hear, e.g. to test guards' hearing through portals. Off: sound only, so a repeating test sound doesn't flood the noise debug view.")]
        [SerializeField] private bool emitNoise;

        [Header("Footsteps (optional)")]
        // If assigned, this emitter is something walking: each play looks
        // at the floor beneath it and uses that surface's footstep cue
        // instead of the cue above (which is kept for when no floor is
        // found).
        [Tooltip("Optional: makes this emitter something walking. Each play looks at the floor beneath it and plays that surface's footstep from these, e.g. the guard's SurfaceSounds asset. Left empty, Cue plays every time.")]
        [SerializeField] private SurfaceSounds surfaceSounds;

        // What counts as floor, and how far below this object to look.
        [Tooltip("Layers that count as floor when looking for the surface beneath, e.g. Environment. Everything (the default) finds any collider; Nothing = never finds a floor, so Cue always plays.")]
        [SerializeField] private LayerMask groundLayers = ~0;
        [Tooltip("How far below this object to look for the floor, in metres. e.g. 1 = an emitter up to a metre above the floor still finds it. Higher = finds floors further down (or the one below a thin floor).")]
        [SerializeField] private float groundProbeDistance = 1f;

        // Counts down to the next play.
        private float _timer;

        // The cue played most recently - the surface's, for a walker.
        private SoundCue _lastCue;

        // Every enabled debug emitter, so SoundDebug can list them all in
        // its report.
        private static readonly List<SoundEmitterDebug> _all = new();

        /// How many debug emitters are enabled.
        public static int Count => _all.Count;

        /// The cue this emitter plays: the one it played last, for a walker.
        public SoundCue Cue => _lastCue != null ? _lastCue : cue;

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

            float noiseScale = emitNoise ? 1f : 0f;

            // A walker: a step on whatever floor is under it right now.
            if (surfaceSounds != null && SurfaceTag.TryFindBelow(transform.position, groundProbeDistance, groundLayers, out SurfaceType surface)) {
                if (surfaceSounds.PlayStep(surface, transform.position, transform, noiseScale, 1f)) {
                    _lastCue = surfaceSounds.GetFootstep(surface);
                    return;
                }
            }

            _lastCue = cue;

            if (cue != null && SoundPlayer.Instance != null) {
                SoundPlayer.Instance.Play(cue, transform.position, transform, noiseScale);
            }
        }
    }
}
