using System;
using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Drives each hand model's Animator. Two things feed it:
    ///
    /// - Input: the Base (index) and Grip (middle/ring/pinky) layers always
    ///   follow live trigger/grip values.
    /// - Snapping: the "Snap Pose" layer sits on top as an override. While a
    ///   hand is snapped to something (a ledge now; rungs, ropes, held props
    ///   and tools later), that layer plays the snap target's HandPose, and
    ///   its weight follows the hand's HandVisualSnap.Weight - so the fingers
    ///   close exactly as the hand lands and open exactly as it leaves. At
    ///   weight 0 the layer contributes nothing and input curl shows as normal.
    ///
    /// This replaced an earlier HandState enum (Idle/Climbing): every
    /// non-input pose is a snap pose, so the snap layer's weight already says
    /// who owns the fingers.
    ///
    /// Ticked explicitly from PlayerController.Update(), after
    /// PlayerHandVisuals.Tick(), so it reads this frame's snap weight
    /// rather than last frame's. Animators evaluate after every Update() has
    /// run, so ticking this last costs nothing.
    /// </summary>
    public class PlayerHandAnimation : MonoBehaviour
    {
        // Name of the override layer in Left Hand Visual.controller. Its
        // states must be named exactly like the HandPose enum values - see
        // BuildPoseStateHashes().
        private const string SnapPoseLayerName = "Snap Pose";

        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

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

        // Snap Pose layer state hash for each HandPose, indexed by (int)pose.
        private static readonly int[] _poseStateHashes = BuildPoseStateHashes();

        // Index of the Snap Pose layer, looked up once by name rather than
        // hard-coded, so reordering layers in the controller can't silently
        // break it. Both hands share one controller, so one index serves both.
        // -1 if the layer is missing.
        private int _snapPoseLayer;

        // The layer weight and pose each hand's Animator was last given, so
        // SetLayerWeight()/Play() only run when something actually changes -
        // not every frame a hand sits fully snapped or fully at rest.
        private float _leftAppliedWeight;
        private float _rightAppliedWeight;
        private int _leftPlayedPose = -1;
        private int _rightPlayedPose = -1;

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
            _snapPoseLayer = leftHandAnimator.GetLayerIndex(SnapPoseLayerName);

            // If a controller object (hand visual and Animator included) is
            // ever deactivated - XRI's Input Modality Manager used to do this
            // whenever it lost tracking; nothing does now, but it's a likely
            // way to hide an untracked hand later - then by default the
            // disabled Animator resets its parameters, layer
            // weights and states - which would leave the "last applied"
            // caches below describing values the Animator no longer has, so
            // e.g. a curled finger would stay open after tracking returns
            // until the input next changed. Keeping the state on disable
            // means the caches always match the Animator.
            leftHandAnimator.keepAnimatorStateOnDisable = true;
            rightHandAnimator.keepAnimatorStateOnDisable = true;

            if (_snapPoseLayer < 0) {
                Debug.LogWarning($"PlayerHandAnimation: no \"{SnapPoseLayerName}\" layer on the hand Animator Controller - snapped hands will keep their input pose.", this);
                return;
            }

            WarnAboutMissingPoseStates();
        }

        /// <summary>
        /// Checks once, at startup, that the Snap Pose layer has a state for
        /// every HandPose value. A HandPose added in code without its
        /// same-named state would otherwise only show up as a vague Animator
        /// warning on every grab, with the hand keeping its input pose.
        /// </summary>
        private void WarnAboutMissingPoseStates()
        {
            string[] names = Enum.GetNames(typeof(HandPose));

            for (int i = 0; i < _poseStateHashes.Length; i++) {
                if (!leftHandAnimator.HasState(_snapPoseLayer, _poseStateHashes[i])) {
                    Debug.LogWarning($"PlayerHandAnimation: no \"{names[i]}\" state on the \"{SnapPoseLayerName}\" layer - hands snapped with HandPose.{names[i]} will keep their input pose.", this);
                }
            }
        }

        /// <summary>
        /// Updates both hands' animation for this frame.
        /// </summary>
        public void Tick()
        {
            UpdateHand(
                leftHandAnimator,
                playerHandVisuals.LeftVisualSnap,
                playerInput.LeftGrip,
                playerInput.LeftTrigger,
                ref _leftAppliedGrip,
                ref _leftAppliedTrigger,
                ref _leftAppliedWeight,
                ref _leftPlayedPose);

            UpdateHand(
                rightHandAnimator,
                playerHandVisuals.RightVisualSnap,
                playerInput.RightGrip,
                playerInput.RightTrigger,
                ref _rightAppliedGrip,
                ref _rightAppliedTrigger,
                ref _rightAppliedWeight,
                ref _rightPlayedPose);
        }

        /// <summary>
        /// Applies one hand's input curl, then its snap pose and weight on
        /// top. Input is always applied, even while snapped - it's hidden
        /// under the override layer then, and already in place the moment
        /// the layer fades out on release.
        /// </summary>
        private void UpdateHand(
            Animator handAnimator,
            HandVisualSnap visualSnap,
            float gripValue,
            float triggerValue,
            ref float appliedGrip,
            ref float appliedTrigger,
            ref float appliedWeight,
            ref int playedPose)
        {
            // Exact comparison, like the layer weight below: an unchanged
            // input is bit-for-bit the same float, and any real movement
            // (however small) still gets through.
            if (gripValue != appliedGrip) {
                handAnimator.SetFloat(_gripCurlParam, gripValue);
                appliedGrip = gripValue;
            }

            if (triggerValue != appliedTrigger) {
                handAnimator.SetFloat(_triggerCurlParam, triggerValue);
                appliedTrigger = triggerValue;
            }

            if (_snapPoseLayer < 0) {
                return;
            }

            float weight = visualSnap.Weight;

            // Switch the layer to the target's pose when it changes - e.g. the
            // first grab, or later a ledge followed by a ladder rung. Played
            // instantly rather than cross-faded: the weight itself is the
            // fade, since it rises from 0 as the hand moves in.
            if (weight > 0f) {
                int pose = (int)visualSnap.SnapPose.Pose;

                if (pose != playedPose) {
                    handAnimator.Play(_poseStateHashes[pose], _snapPoseLayer, 0f);
                    playedPose = pose;
                }
            }

            // Exact comparison on purpose: Weight only changes when
            // HandVisualSnap changes it, and it lands on exactly 0 and 1 at
            // the ends of a blend, which an approximate check could skip.
            if (weight != appliedWeight) {
                handAnimator.SetLayerWeight(_snapPoseLayer, weight);
                appliedWeight = weight;
            }
        }

        /// <summary>
        /// Hashes every HandPose name once, at class load, so a pose maps to
        /// its Snap Pose layer state with an array lookup. This is the
        /// convention that ties code to the controller: a state named
        /// "LedgeGrip" plays for HandPose.LedgeGrip, and so on. Relies on
        /// HandPose values being the default 0, 1, 2, ... (no explicit
        /// numbers), so each value is also its index here.
        /// </summary>
        private static int[] BuildPoseStateHashes()
        {
            string[] names = Enum.GetNames(typeof(HandPose));
            int[] hashes = new int[names.Length];

            for (int i = 0; i < names.Length; i++) {
                hashes[i] = Animator.StringToHash(names[i]);
            }

            return hashes;
        }
    }
}
