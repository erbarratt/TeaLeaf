using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Core
{
    /// <summary>
    /// The sound each surface makes underfoot, as one asset: a footstep cue
    /// per SurfaceType, and how it's played. Shared by anything that walks
    /// - the player now, guards later - so a surface sounds the same
    /// whoever treads on it. Create one with Assets > Create > TeaLeaf >
    /// Surface Sounds.
    ///
    /// Hard surfaces can have "heel and toe": a step is two sounds a moment
    /// apart, the heel coming down and then the toe, on every step or only
    /// on a share of them.
    ///
    /// There's no separate landing sound: a landing is the surface's step
    /// played loud, once per foot (see Player.PlayerFootsteps).
    ///
    /// How loud a surface is to guards isn't stored here: it's the noise
    /// radius of that surface's SoundCue, so each surface is tuned in its
    /// own cue (audio and noise together, as for every other sound).
    /// </summary>
    [CreateAssetMenu(fileName = "SurfaceSounds", menuName = "TeaLeaf/Surface Sounds")]
    public class SurfaceSounds : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            public SurfaceType surface;
            public SoundCue footstep;

            // Steps on this surface can be two sounds, heel then toe.
            public bool heelAndToe;

            // The share of steps that get it: 1 = every step, 0.6 = six in
            // ten on average, spread unevenly so no pattern is heard.
            [Range(0f, 1f)] public float heelAndToeRatio;

            // The toe's volume as a fraction of the heel's. (Briefly saved
            // as "heelVolume" while the two were tried the other way round
            // - the attribute keeps a value saved under that name.)
            [FormerlySerializedAs("heelVolume")]
            [Range(0f, 1f)] public float toeVolume;

            // Seconds from the heel to the toe.
            public float toeDelay;

            // How dull the toe is: 0 = as bright as the heel, 1 = its high
            // end fully rolled off (SoundPlayer's muffled cutoff).
            [Range(0f, 1f)] public float toeMuffle;
        }

        // One entry per surface, in any order. A surface with no entry (or
        // no cue) is played as Stone.
        [SerializeField] private Entry[] entries;

        // The entries rearranged so a surface's is found by using the
        // enum's number as the array index - no searching when a foot comes
        // down. Built the first time it's needed.
        private Entry[] _bySurface;

        /// <summary>
        /// Editor-only: an entry was changed in the Inspector, so the
        /// lookup array is out of date. Dropped here, rebuilt on next use.
        /// </summary>
        private void OnValidate()
        {
            _bySurface = null;
        }

        /// <summary>
        /// The cue for a step on surface. Null only if neither the surface
        /// nor Stone has one.
        /// </summary>
        public SoundCue GetFootstep(SurfaceType surface)
        {
            if (_bySurface == null) {
                BuildLookup();
            }

            return _bySurface[(int)surface].footstep;
        }

        /// <summary>
        /// How loud a surface is compared with stone: 1 for stone, less
        /// for a softer surface (carpet), more for a louder one (metal).
        /// It's the surface's footstep noise radius over stone's, so a
        /// surface's loudness is still set in one place - its footstep
        /// cue - and anything else that lands on it (a thrown prop) can
        /// scale its own sound to match. 1 if either cue is missing.
        /// </summary>
        public float GetLoudness(SurfaceType surface)
        {
            SoundCue cue = GetFootstep(surface);
            SoundCue stone = GetFootstep(SurfaceType.Stone);

            if (cue == null || stone == null || stone.NoiseRadius <= 0f) {
                return 1f;
            }

            return cue.NoiseRadius / stone.NoiseRadius;
        }

        /// <summary>
        /// Plays one step on surface at position: the surface's cue, with
        /// its noise (see SoundPlayer.Play() for source and the scales),
        /// then on a heel-and-toe surface the quieter toe a moment later -
        /// audio only, since it's the same step to a guard. cadence is the
        /// walker's own, which decides the steps that get a toe. Returns
        /// false if there was nothing to play with (no SoundPlayer in the
        /// scene, or no cue).
        /// </summary>
        public bool PlayStep(SurfaceType surface, Vector3 position, Transform source, float noiseScale, float volumeScale, ref HeelAndToeCadence cadence)
        {
            if (_bySurface == null) {
                BuildLookup();
            }

            SoundPlayer soundPlayer = SoundPlayer.Instance;
            Entry entry = _bySurface[(int)surface];

            if (soundPlayer == null || entry.footstep == null) {
                return false;
            }

            soundPlayer.Play(entry.footstep, position, source, noiseScale, volumeScale);

            if (entry.heelAndToe && cadence.Next(entry.heelAndToeRatio)) {
                soundPlayer.Play(entry.footstep, position, source, 0f, volumeScale * entry.toeVolume, entry.toeDelay, entry.toeMuffle);
            }

            return true;
        }

        /// <summary>
        /// Fills the lookup array from the entries, then fills the gaps: a
        /// surface with no cue gets the whole of Stone's entry.
        /// </summary>
        private void BuildLookup()
        {
            int surfaceCount = Enum.GetValues(typeof(SurfaceType)).Length;
            _bySurface = new Entry[surfaceCount];

            if (entries != null) {
                for (int i = 0; i < entries.Length; i++) {
                    _bySurface[(int)entries[i].surface] = entries[i];
                }
            }

            Entry stone = _bySurface[(int)SurfaceType.Stone];

            for (int i = 0; i < surfaceCount; i++) {
                if (_bySurface[i].footstep == null) {
                    _bySurface[i] = stone;
                }
            }
        }
    }
}
