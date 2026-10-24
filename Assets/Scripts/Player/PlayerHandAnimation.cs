using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Drives each hand model's fingers. Two things feed them:
    ///
    /// - Input: the hand Animator's Base (index) and Grip (middle/ring/pinky)
    ///   layers always follow live trigger/grip values.
    /// - Snapping: while a hand is snapped to something (a ledge, rung, rope,
    ///   held prop or tool), the snap target's HandSnapProfile finger pose is
    ///   laid on top by a HandFingerPoser per hand, its weight following the
    ///   hand's HandVisualSnap.Weight - so the fingers close exactly as the
    ///   hand lands and open exactly as it leaves. At weight 0 the input
    ///   curl shows as normal.
    /// - Holding: a hand holding the bow, an arrow or the pack (none of
    ///   which snap the hand) plays that thing's profile at full weight
    ///   while it holds it (PlayerBow/PlayerPack.GetHeldProfile()). A snap
    ///   under way wins (the string).
    ///
    /// Tick() is called explicitly from PlayerController.Update(), after
    /// PlayerHandVisuals.Tick(), so it reads this frame's snap weight. The
    /// finger pose itself is applied in LateUpdate(): the Animator writes
    /// the bones after every Update(), and would overwrite it there.
    /// </summary>
    public class PlayerHandAnimation : MonoBehaviour
    {
        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // Read to keep a carried prop's finger pose on while it's in the
        // hand. Optional: found in Awake() if not wired (it's on this same
        // object).
        [SerializeField] private PlayerHandHolding playerHandHolding;

        // Read for the finger pose of the bow, an arrow or the pack in a
        // hand. Optional: found in Awake() if not wired (on this same
        // object).
        [SerializeField] private PlayerBow playerBow;
        [SerializeField] private PlayerPack playerPack;

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

        // Whether the rig has a PlayerHandHolding, checked once in Awake().
        private bool _hasHandHolding;
        private bool _hasBow;
        private bool _hasPack;

        // Each hand's finger pose over the Animator's input curl.
        private HandFingerPoser _leftFingers;
        private HandFingerPoser _rightFingers;

        // The grip/trigger curl each hand's Animator was last given, so
        // SetFloat() only runs when the input actually moved - a resting
        // finger reads exactly 0 (and a fully pulled one exactly 1) frame
        // after frame. Start as NaN, which never equals anything (not even
        // itself), so the very first frame always sets them.
        private float _leftAppliedGrip = float.NaN;
        private float _leftAppliedTrigger = float.NaN;
        private float _rightAppliedGrip = float.NaN;
        private float _rightAppliedTrigger = float.NaN;

        private void Awake()
        {
            if (playerHandHolding == null) {
                playerHandHolding = GetComponent<PlayerHandHolding>();
            }

            _hasHandHolding = playerHandHolding != null;

            if (playerBow == null) {
                playerBow = GetComponent<PlayerBow>();
            }

            if (playerPack == null) {
                playerPack = GetComponent<PlayerPack>();
            }

            _hasBow = playerBow != null;
            _hasPack = playerPack != null;

            // If a controller object (hand visual and Animator included) is
            // ever deactivated - a likely way to hide an untracked hand
            // later - then by default the disabled Animator resets its
            // parameters, which would leave the "last applied" caches below
            // describing values the Animator no longer has, so e.g. a curled
            // finger would stay open after tracking returns until the input
            // next changed. Keeping the state on disable means the caches
            // always match the Animator.
            leftHandAnimator.keepAnimatorStateOnDisable = true;
            rightHandAnimator.keepAnimatorStateOnDisable = true;

            _leftFingers = new HandFingerPoser(leftHandAnimator.transform);
            _rightFingers = new HandFingerPoser(rightHandAnimator.transform);
        }

        /// <summary>
        /// Updates both hands' animation for this frame.
        /// </summary>
        public void Tick()
        {
            UpdateHand(
                leftHandAnimator,
                _leftFingers,
                playerHandVisuals.LeftVisualSnap,
                _hasHandHolding ? playerHandHolding.LeftPoseWeight : 0f,
                HeldProfile(true),
                playerInput.LeftGrip,
                playerInput.LeftTrigger,
                ref _leftAppliedGrip,
                ref _leftAppliedTrigger);

            UpdateHand(
                rightHandAnimator,
                _rightFingers,
                playerHandVisuals.RightVisualSnap,
                _hasHandHolding ? playerHandHolding.RightPoseWeight : 0f,
                HeldProfile(false),
                playerInput.RightGrip,
                playerInput.RightTrigger,
                ref _rightAppliedGrip,
                ref _rightAppliedTrigger);
        }

        /// <summary>
        /// After the Animators have written this frame's input curl: lays
        /// each hand's finger pose over it.
        /// </summary>
        private void LateUpdate()
        {
            _leftFingers.Apply();
            _rightFingers.Apply();
        }

        /// <summary>
        /// The profile of what a hand holds without being snapped (bow,
        /// arrow, pack), or null. A hand holds one thing at a time.
        /// </summary>
        private HandSnapProfile HeldProfile(bool isLeftHand)
        {
            HandSnapProfile profile = _hasBow ? playerBow.GetHeldProfile(isLeftHand) : null;

            if (profile == null && _hasPack) {
                profile = playerPack.GetHeldProfile(isLeftHand);
            }

            return profile;
        }

        /// <summary>
        /// Applies one hand's input curl, and readies its finger pose and
        /// weight for LateUpdate(). Input is always applied, even while
        /// snapped - it's hidden under the pose then, and already in place
        /// the moment the pose fades out on release.
        /// </summary>
        private void UpdateHand(
            Animator handAnimator,
            HandFingerPoser fingers,
            HandVisualSnap visualSnap,
            float holdPoseWeight,
            HandSnapProfile heldProfile,
            float gripValue,
            float triggerValue,
            ref float appliedGrip,
            ref float appliedTrigger)
        {
            // Exact comparison: an unchanged input is bit-for-bit the same
            // float, and any real movement (however small) still gets
            // through.
            if (gripValue != appliedGrip) {
                handAnimator.SetFloat(_gripCurlParam, gripValue);
                appliedGrip = gripValue;
            }

            if (triggerValue != appliedTrigger) {
                handAnimator.SetFloat(_triggerCurlParam, triggerValue);
                appliedTrigger = triggerValue;
            }

            // A carried prop keeps its pose on after the snap that reached
            // for it has let go (the snap's weight falls as the hand comes
            // back; the snap still remembers the pose) - so whichever asks
            // for more wins.
            float weight = Mathf.Max(visualSnap.Weight, holdPoseWeight);

            if (weight > 0f) {
                // A held thing keeps the hand fully posed while a snap
                // comes and goes (the arrow's fingers to the string's).
                fingers.Prepare(visualSnap.SnapPose.Profile, heldProfile != null ? 1f : weight);
            } else {
                fingers.Prepare(heldProfile, heldProfile != null ? 1f : 0f);
            }
        }
    }
}
