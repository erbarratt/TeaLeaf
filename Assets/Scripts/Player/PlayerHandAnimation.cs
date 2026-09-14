using UnityEngine;

namespace Player
{
    /// <summary>
    /// Drives each hand model's finger-curl Animator parameters from
    /// controller input, except while some other system owns the hand's
    /// pose (e.g. gripping a climbable edge) - see HandState.
    ///
    /// Ticked explicitly from PlayerLocomotion.Update(), after
    /// PlayerClimbing.Tick(), rather than running its own Update() - the
    /// same reasoning as PlayerClimbing/PlayerHandInteraction's own class
    /// comments: this needs THIS frame's grab state, so frame ordering has
    /// to be deterministic rather than left to Unity's Update() order.
    /// </summary>
    public class PlayerHandAnimation : MonoBehaviour
    {
        /// What's currently driving a hand's finger pose. Idle means finger
        /// curl follows live grip/trigger input; every other state means
        /// some other system owns the pose and input is ignored here.
        /// Checked in priority order in GetState() - add new cases (holding
        /// an item, pointing, etc.) there as they're needed.
        private enum HandState { Idle, Climbing }

        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerClimbing playerClimbing;

        [Header("Left Hand")]
        [SerializeField] private Animator leftHandAnimator;

        [Header("Right Hand")]
        [SerializeField] private Animator rightHandAnimator;

        // Cached parameter hashes rather than calling Animator.SetFloat with
        // a string every frame - StringToHash avoids the Animator doing a
        // string lookup internally on every single call, which matters
        // since this runs twice a frame (once per hand).
        private static readonly int _gripCurlParam = Animator.StringToHash("GripCurl");
        private static readonly int _triggerCurlParam = Animator.StringToHash("TriggerCurl");

        /// <summary>
        /// Updates both hands' finger-curl animation for this frame.
        /// </summary>
        public void Tick()
        {
            UpdateHand(
                leftHandAnimator,
                GetState(playerClimbing.IsLeftHandGripping),
                playerInput.LeftGrip,
                playerInput.LeftTrigger);

            UpdateHand(
                rightHandAnimator,
                GetState(playerClimbing.IsRightHandGripping),
                playerInput.RightGrip,
                playerInput.RightTrigger);
        }

        /// <summary>
        /// Decides which HandState a hand is in this frame. Climbing takes
        /// priority over Idle since a gripped edge should always override
        /// input-driven finger curl.
        /// </summary>
        private static HandState GetState(bool isGripping)
        {
            return isGripping ? HandState.Climbing : HandState.Idle;
        }

        /// <summary>
        /// Applies one hand's animation for its current state. Only Idle
        /// actually touches the Animator - every other state leaves it
        /// exactly as it was last frame, until a dedicated pose for that
        /// state is built.
        /// </summary>
        private static void UpdateHand(Animator handAnimator, HandState state, float gripValue, float triggerValue)
        {
            switch (state) {

                case HandState.Idle:
                    handAnimator.SetFloat(_gripCurlParam, gripValue);
                    handAnimator.SetFloat(_triggerCurlParam, triggerValue);
                    break;

                case HandState.Climbing:
                    // Intentionally does nothing for now - see class comment.
                    break;
            }
        }
    }
}
