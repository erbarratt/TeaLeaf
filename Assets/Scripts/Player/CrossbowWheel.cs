using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The crossbow's wheel as something a hand ray can target: a trigger
    /// ball round the wheel, bigger than the wheel itself so it's easy to
    /// aim at. The reticle shows on it like on anything else that can be
    /// grabbed.
    ///
    /// Only the hand that isn't wearing the crossbow can target it: a
    /// hand can't reach the back of its own wrist.
    ///
    /// Made by PlayerCrossbow at load, never placed by hand. What taking
    /// hold of the wheel does is PlayerCrossbow's business.
    /// </summary>
    public class CrossbowWheel : MonoBehaviour, IHandTarget
    {
        private Collider _collider;
        private PlayerTracking _playerTracking;
        private bool _isCrossbowOnLeft;

        /// <summary>
        /// Called by the crossbow straight after it adds the component.
        /// </summary>
        public void SetUp(Collider wheelCollider, PlayerTracking playerTracking, bool isCrossbowOnLeft)
        {
            _collider = wheelCollider;
            _playerTracking = playerTracking;
            _isCrossbowOnLeft = isCrossbowOnLeft;

            // OnEnable() has already run (it runs as the component is
            // added), before there was a collider to register.
            HandTargetRegistry.Register(_collider, this);
        }

        private void OnEnable()
        {
            if (_collider != null) {
                HandTargetRegistry.Register(_collider, this);
            }
        }

        private void OnDisable()
        {
            if (_collider != null) {
                HandTargetRegistry.Unregister(_collider);
            }
        }

        /// <summary>
        /// Refuses the crossbow hand's own ray. A ray is told apart by
        /// which hand its start is nearer to (squared distances: the same
        /// answer as comparing the distances, without the square roots).
        /// </summary>
        public bool CanBeTargetedFrom(Vector3 rayOrigin)
        {
            float toLeft = (rayOrigin - _playerTracking.LeftHandPosition).sqrMagnitude;
            float toRight = (rayOrigin - _playerTracking.RightHandPosition).sqrMagnitude;
            bool isFromLeft = toLeft < toRight;

            return isFromLeft != _isCrossbowOnLeft;
        }
    }
}
