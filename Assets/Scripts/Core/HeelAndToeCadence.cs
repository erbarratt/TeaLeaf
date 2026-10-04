using UnityEngine;

namespace Core
{
    /// <summary>
    /// Decides which of a walker's steps get the second "toe" sound, when a
    /// surface asks for it on only some steps (see SurfaceSounds). Each
    /// walker keeps its own (it's a struct: a field, not an object).
    ///
    /// A plain dice roll per step would average out right but can run
    /// several steps the same way. Instead each step adds the ratio to a
    /// running credit, and a toe sounds when the credit reaches a target
    /// that's picked at random each time, somewhere round 1. So at a ratio
    /// of 0.5 it's every second step on average, sometimes a step early or
    /// late, but never a long run with or without.
    /// </summary>
    public struct HeelAndToeCadence
    {
        // How far the random target strays either side of 1.
        private const float Variation = 0.5f;

        private float _credit;
        private float _target;

        /// <summary>
        /// Call once per step: whether this step gets a toe sound. ratio is
        /// the share of steps that should (1 = every step, 0 = none).
        /// </summary>
        public bool Next(float ratio)
        {
            if (ratio >= 1f) {
                return true;
            }

            if (ratio <= 0f) {
                return false;
            }

            // First use: a struct starts as all zeros.
            if (_target <= 0f) {
                _target = Random.Range(1f - Variation, 1f + Variation);
            }

            _credit += ratio;

            if (_credit < _target) {
                return false;
            }

            // Spend one toe's worth of credit (not all of it), so being
            // early or late this time is made up for next time and the
            // average stays at the ratio.
            _credit -= 1f;
            _target = Random.Range(1f - Variation, 1f + Variation);
            return true;
        }
    }
}
