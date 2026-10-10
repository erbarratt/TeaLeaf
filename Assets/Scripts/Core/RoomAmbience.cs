using UnityEngine;

namespace Core
{
    /// <summary>
    /// The background sound of wherever the player is: a loop with no
    /// position (a warehouse's hum, wind outside), chosen by the room the
    /// player stands in and faded over to the next room's on a change.
    ///
    /// Two plain (not 3D) looping sources, so one room's can fade out on
    /// one while the next room's fades in on the other. Nothing runs once
    /// the fades have arrived.
    ///
    /// A plain class, made and ticked by the SoundPlayer, which is what
    /// knows which room the player is in.
    /// </summary>
    public class RoomAmbience
    {
        // For each of the two sources: the cue it's playing, the cue's
        // volume times the room's, how far it has faded in (0-1) and where
        // that fade is heading. _active is the one playing the current
        // room's.
        private readonly AudioSource[] _sources = new AudioSource[2];
        private readonly SoundCue[] _cues = new SoundCue[2];
        private readonly float[] _volumes = new float[2];
        private readonly float[] _gains = new float[2];
        private readonly float[] _targets = new float[2];
        private int _active;
        private bool _isFading;

        /// <summary>
        /// Makes the two sources as children of parent.
        /// </summary>
        public RoomAmbience(Transform parent)
        {
            for (int i = 0; i < _sources.Length; i++) {
                GameObject bed = new($"Ambience {i}");
                bed.transform.SetParent(parent, false);

                AudioSource source = bed.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;

                // Not 3D: it comes from nowhere in particular.
                source.spatialBlend = 0f;
                _sources[i] = source;
            }
        }

        /// <summary>
        /// Says what the ambience should be now: cue (null = silence) at
        /// roomVolume times the cue's own. If that isn't what's playing,
        /// the current one starts fading out and the new one fading in on
        /// the other source. Called a few times a second.
        /// </summary>
        public void Set(SoundCue cue, float roomVolume)
        {
            if (cue != null && (cue.ClipCount == 0 || cue.GetClip(0) == null)) {
                cue = null;
            }

            int active = _active;
            float volume = cue != null ? cue.Volume * roomVolume : 0f;

            // Already playing it (the same room, or the next room shares
            // the cue): carry on, at this room's volume.
            if (_cues[active] == cue) {
                if (cue != null && !Mathf.Approximately(_volumes[active], volume)) {
                    _volumes[active] = volume;
                    _isFading = true;
                }

                return;
            }

            int other = 1 - active;
            AudioSource source = _sources[other];
            _targets[active] = 0f;
            _active = other;
            _isFading = true;

            if (cue == null) {
                // Into silence: nothing to start.
                source.Stop();
                _cues[other] = null;
                _gains[other] = 0f;
                _targets[other] = 0f;
                return;
            }

            // Stepping back into the room just left, its ambience is still
            // fading out on this source: turn the fade round rather than
            // starting the clip again.
            if (_cues[other] != cue || !source.isPlaying) {
                // An ambience uses its cue's first clip, so the same cue
                // is always the same sound.
                source.clip = cue.GetClip(0);
                source.volume = 0f;
                source.Play();
                _gains[other] = 0f;
            }

            _cues[other] = cue;
            _volumes[other] = volume;
            _targets[other] = 1f;
        }

        /// <summary>
        /// Moves the two sources' fades on over fadeTime seconds, and stops
        /// one that has faded out. Does nothing once both have arrived.
        /// </summary>
        public void Tick(float deltaTime, float fadeTime)
        {
            if (!_isFading) {
                return;
            }

            float step = deltaTime / Mathf.Max(fadeTime, 0.01f);
            bool stillFading = false;

            for (int i = 0; i < _sources.Length; i++) {
                if (_cues[i] == null) {
                    continue;
                }

                _gains[i] = Mathf.MoveTowards(_gains[i], _targets[i], step);
                _sources[i].volume = _volumes[i] * _gains[i];

                if (_gains[i] != _targets[i]) {
                    stillFading = true;
                } else if (_targets[i] <= 0f) {
                    _sources[i].Stop();
                    _cues[i] = null;
                }
            }

            _isFading = stillFading;
        }
    }
}
