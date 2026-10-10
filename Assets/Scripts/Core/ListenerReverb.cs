using UnityEngine;

namespace Core
{
    /// <summary>
    /// The one reverb: a Unity reverb filter on the same object as the
    /// AudioListener, where it works on everything the player hears after
    /// it has been mixed together - one effect however many sounds are
    /// playing. Its settings are those of the room the player stands in,
    /// and a change of room blends from one room's to the next's.
    ///
    /// A plain class, made and ticked by the SoundPlayer, which is what
    /// knows which room the player is in.
    /// </summary>
    public class ListenerReverb
    {
        private readonly AudioReverbFilter _filter;

        // A change blends from where the settings had got to (_from) to
        // the new ones (_target); _blend runs 0 to 1 and stays at 1 once
        // there, when nothing is touched.
        private ReverbSettings _from;
        private ReverbSettings _current;
        private ReverbSettings _target;
        private float _blend = 1f;
        private float _appliedAmount;
        private bool _hasStarted;

        /// <summary>
        /// Adds the reverb filter to the listener's object. A filter only
        /// works on the listener's output if it's on the very object the
        /// AudioListener is on.
        /// </summary>
        public ListenerReverb(GameObject listenerObject)
        {
            _filter = listenerObject.AddComponent<AudioReverbFilter>();

            // "User" is the preset that takes its numbers from code.
            _filter.reverbPreset = AudioReverbPreset.User;
        }

        /// <summary>
        /// Says what the reverb should be now, with every level multiplied
        /// by amount. Called a few times a second. Comparing the settings
        /// rather than the room means a room tuned in the Inspector while
        /// playing is heard straight away.
        /// </summary>
        public void SetWanted(in ReverbSettings wanted, float amount)
        {
            if (!_hasStarted) {
                // The first call, as the level starts: no blend.
                _hasStarted = true;
                _current = wanted;
                _target = wanted;
                _blend = 1f;
                _appliedAmount = amount;
                wanted.ApplyTo(_filter, amount);
            } else if (!wanted.Matches(_target) || amount != _appliedAmount) {
                // Blend from wherever the reverb has got to, so a change
                // part-way through another is still smooth.
                _from = _current;
                _target = wanted;
                _blend = 0f;
            }
        }

        /// <summary>
        /// Moves the reverb on towards the wanted settings over blendTime
        /// seconds. Does nothing once it's there, so the filter is only
        /// touched for the moment after a change.
        /// </summary>
        public void Tick(float deltaTime, float blendTime, float amount)
        {
            if (_blend >= 1f) {
                return;
            }

            _blend = Mathf.Min(1f, _blend + deltaTime / Mathf.Max(blendTime, 0.01f));

            // SmoothStep eases in and out of the change.
            _current = ReverbSettings.Lerp(_from, _target, Mathf.SmoothStep(0f, 1f, _blend));
            _appliedAmount = amount;
            _current.ApplyTo(_filter, amount);
        }
    }
}
