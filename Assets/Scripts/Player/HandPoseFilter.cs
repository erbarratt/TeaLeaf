using UnityEngine;

namespace Player
{
    /// <summary>
    /// Smooths one hand's tracked pose, taking out the small shake of a
    /// real hand (and of the tracking) without making the hand feel behind.
    /// A plain class owned by PlayerTracking, one per hand.
    ///
    /// The simple way to smooth is to move only part of the way to each
    /// new reading: the more of the old value is kept, the steadier the
    /// result - and the further it trails a moving hand. That trailing is
    /// lag, and no single amount is right for both a still hand and a
    /// moving one.
    ///
    /// This is a "One Euro" filter, which uses that same simple smoothing
    /// but changes its strength with the hand's speed:
    /// - nearly still: strong smoothing. Shake is all there is to see, and
    ///   a few milliseconds of delay on a hand that isn't going anywhere
    ///   can't be seen.
    /// - moving fast: almost none. Shake can't be seen in a fast movement,
    ///   but lag can.
    ///
    /// Strength is given as a "cutoff" frequency in hertz: wobbles faster
    /// than the cutoff are smoothed away, slower movement passes through.
    /// The cutoff starts at a minimum (the still hand) and rises with
    /// speed - see HandSmoothingSettings for the two numbers and how to
    /// tune them.
    /// </summary>
    public class HandPoseFilter
    {
        // The hand's speed is itself worked out from shaky readings, so it
        // is smoothed too before it's used, with this fixed cutoff in
        // hertz (the value the filter's authors suggest).
        private const float SpeedCutoff = 1f;

        // False until the first reading, and again after Reset().
        private bool _hasValue;

        // The smoothed pose given out last frame, and the smoothed speeds
        // (metres a second as a vector; radians a second as a size only).
        private Vector3 _position;
        private Quaternion _rotation;
        private Vector3 _velocity;
        private float _turnSpeed;

        /// <summary>
        /// Forgets everything: the next reading is passed through as it
        /// is. For when smoothing is switched off, so switching it back on
        /// doesn't start from a stale pose.
        /// </summary>
        public void Reset()
        {
            _hasValue = false;
        }

        /// <summary>
        /// Smooths this frame's reading: position and rotation go in as
        /// tracked and come out smoothed. deltaTime is the time since the
        /// last call, in seconds.
        /// </summary>
        public void Filter(ref Vector3 position, ref Quaternion rotation, HandSmoothingSettings settings, float deltaTime)
        {
            // Nothing to smooth towards yet (or no time has passed): take
            // the reading as it is.
            if (!_hasValue || deltaTime <= 0f) {
                _position = position;
                _rotation = rotation;
                _velocity = Vector3.zero;
                _turnSpeed = 0f;
                _hasValue = true;
                return;
            }

            // Position. How fast is the hand moving? The step from the
            // last smoothed position to this reading, over the time it
            // took - smoothed itself, since one shaky reading would
            // otherwise look like a burst of speed.
            Vector3 velocity = (position - _position) / deltaTime;
            _velocity = Vector3.Lerp(_velocity, velocity, Weight(SpeedCutoff, deltaTime));

            // The faster it's moving, the higher the cutoff - the less
            // smoothing. Lerp then moves that share of the way from the
            // old smoothed position to the new reading.
            float cutoff = settings.positionMinCutoff + settings.positionSpeedCoefficient * _velocity.magnitude;
            _position = Vector3.Lerp(_position, position, Weight(cutoff, deltaTime));

            // Rotation, the same way. Its "speed" is how far the hand
            // turned (the angle between the two rotations) over the time;
            // Slerp is Lerp for rotations, turning by the shortest way.
            float turnSpeed = Quaternion.Angle(_rotation, rotation) * Mathf.Deg2Rad / deltaTime;
            _turnSpeed = Mathf.Lerp(_turnSpeed, turnSpeed, Weight(SpeedCutoff, deltaTime));

            float turnCutoff = settings.rotationMinCutoff + settings.rotationSpeedCoefficient * _turnSpeed;
            _rotation = Quaternion.Slerp(_rotation, rotation, Weight(turnCutoff, deltaTime));

            position = _position;
            rotation = _rotation;
        }

        /// <summary>
        /// How much of a new reading to take this frame (0-1) for a given
        /// cutoff frequency: near 0 keeps the old value (heavy smoothing),
        /// near 1 takes the new one (none). It depends on the frame's
        /// length too, so the smoothing feels the same at 72, 90 or 120
        /// frames a second.
        /// </summary>
        private static float Weight(float cutoff, float deltaTime)
        {
            // The cutoff as a time: roughly how long the smoothed value
            // takes to catch up with a change. A cutoff of 0 or less would
            // never catch up, so it's kept above that.
            float catchUpTime = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 0.01f));

            return deltaTime / (deltaTime + catchUpTime);
        }
    }
}
