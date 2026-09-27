using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Handles grabbing and climb movement. Edge highlighting lives in
    /// PlayerHandInteraction instead - see that class' comment.
    ///
    /// This class does not run its own Update(). Instead PlayerController
    /// calls Tick() explicitly once per frame, at the point where it wants
    /// climbing to run - that keeps frame ordering deterministic instead of
    /// depending on Unity's undefined order between different components'
    /// Update() calls.
    /// </summary>
    public class PlayerClimbing : MonoBehaviour
    {
        // Which hand is currently driving climb movement. Hand.None means
        // nobody is climbing.
        private enum Hand { None, Left, Right }

        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;

        // Only touched once, in Awake(), to disable minMoveDistance - see
        // its comment there. PlayerController owns the actual Move() call.
        [SerializeField] private CharacterController characterController;

        // The rig root transform that characterController.Move() displaces -
        // the same transform PlayerController calls playerTransform. Hand
        // positions are converted into this transform's local space before
        // computing a movement delta - see UpdateFrameMovement() - since the
        // hand is a descendant of this transform, so its WORLD position
        // shifts the instant we move the rig, which would otherwise feed
        // straight back into next frame's delta and jitter the player.
        [SerializeField] private Transform playerTransform;

        [Header("Hand Visuals")]

        // The cosmetic controller model transforms, NOT the tracked pose
        // transforms on PlayerTracking - snapping these to a grabbed edge
        // never touches real input tracking.
        [SerializeField] private Transform leftHandVisual;
        [SerializeField] private Transform rightHandVisual;

        // Seconds for a hand visual to blend onto its snap pose when grabbing
        // (and back to the controller on release) - short enough to feel
        // instant, long enough not to pop.
        [SerializeField] private float snapBlendDuration = 0.08f;

        [Header("Grab Targeting")]

        // Grabs are ray-targeted: a hand grabs whatever IClimbable its
        // hand ray (and reticle) is on while grip is held, rather than
        // whatever its old SphereCollider happened to overlap. Reusing
        // PlayerHandInteraction's ray means what's highlighted is exactly
        // what gets grabbed, and its rayLength doubles as grab reach.
        // PlayerController ticks it before this class, so its targets are
        // always this frame's.
        [SerializeField] private PlayerHandInteraction playerHandInteraction;

        // Whatever each hand is holding - a ledge, a ladder, ... Typed as the
        // interface so this class never needs to know which.
        private IClimbable _leftGrabbed;
        private IClimbable _rightGrabbed;

        private Hand _primaryHand = Hand.None;

        // Set for both hands by ReleaseAll(): that hand can't grab anything
        // until its grip has been let go once. Grip is still held when a
        // system forces a release (e.g. a mantle), and "held" grabbing would
        // otherwise re-grab whatever the ray is on straight away.
        private bool _leftNeedsRegrip;
        private bool _rightNeedsRegrip;

        // The primary hand's position last frame, in playerTransform's local
        // space rather than world space - see the playerTransform field comment.
        private Vector3 _primaryHandLastLocalPosition;

        // Local-space hand movement measured but not yet actually applied
        // to the CharacterController - see UpdateFrameMovement() and
        // ReportAppliedMovement().
        private Vector3 _pendingLocalDelta;

        /// True while a hand is gripping a climbable and driving climb movement.
        public bool IsClimbing { get; private set; }

        /// True while the left hand is gripping a climbable, regardless of
        /// whether it's the primary hand currently driving movement - e.g.
        /// hand animation needs to know per-hand grip state, not just which
        /// hand (if any) is steering.
        public bool IsLeftHandGripping => _leftGrabbed is not null;

        /// True while the right hand is gripping a climbable - mirrors
        /// IsLeftHandGripping.
        public bool IsRightHandGripping => _rightGrabbed is not null;

        /// The ledge the left hand is gripping, or null if it's gripping
        /// nothing or something that isn't a ledge (e.g. a ladder) - so
        /// PlayerMantling can check whether it's mantleable. "as" is a cheap
        /// type check, fine every frame.
        public ClimbableEdge LeftGrabbedEdge => _leftGrabbed as ClimbableEdge;

        /// The ledge the right hand is gripping, or null - mirrors
        /// LeftGrabbedEdge.
        public ClimbableEdge RightGrabbedEdge => _rightGrabbed as ClimbableEdge;

        /// This frame's climb movement, for PlayerController to add to its
        /// frame movement accumulator.
        public Vector3 FrameMovement { get; private set; }

        /// Blends the left hand visual onto a grabbed edge's snap pose and
        /// back. Exposed e.g. for PlayerHandAnimation to read which finger
        /// pose the grabbed target wants.
        public HandVisualSnap LeftVisualSnap { get; private set; }

        /// The right hand's snap state - mirrors LeftVisualSnap.
        public HandVisualSnap RightVisualSnap { get; private set; }

        private void Awake()
        {
            // Created once here (capturing each visual's rest pose), never
            // per grab, so snapping doesn't allocate.
            LeftVisualSnap = new HandVisualSnap(leftHandVisual, snapBlendDuration);
            RightVisualSnap = new HandVisualSnap(rightHandVisual, snapBlendDuration);

            // CharacterController.Move() silently does nothing for a single
            // call shorter than minMoveDistance (0.001 by default on this
            // rig), to suppress jitter from numerical noise. That's fine for
            // thumbstick movement - at any real walking speed a frame's
            // delta is always far above it - but climb movement is driven
            // directly by real hand tracking, which can move slower than
            // that between frames. Disabling it here means every genuine
            // bit of hand movement actually reaches the CharacterController
            // instead of being silently eaten.
            characterController.minMoveDistance = 0f;
        }

        /// <summary>
        /// Runs one frame of climbing logic: grab/release for both hands and
        /// climb movement. Edge highlighting lives in PlayerHandInteraction,
        /// and placing the hand visuals happens later in TickHandVisuals().
        /// </summary>
        public void Tick()
        {
            UpdateHandGrab(
                Hand.Left, Hand.Right,
                playerInput.IsLeftGrabbing, playerInput.IsRightGrabbing,
                playerTracking.LeftHandPosition, playerTracking.RightHandPosition,
                playerHandInteraction.LeftTarget, playerHandInteraction.LeftTargetPoint,
                LeftVisualSnap,
                ref _leftGrabbed,
                ref _leftNeedsRegrip,
                _rightGrabbed);

            UpdateHandGrab(
                Hand.Right, Hand.Left,
                playerInput.IsRightGrabbing, playerInput.IsLeftGrabbing,
                playerTracking.RightHandPosition, playerTracking.LeftHandPosition,
                playerHandInteraction.RightTarget, playerHandInteraction.RightTargetPoint,
                RightVisualSnap,
                ref _rightGrabbed,
                ref _rightNeedsRegrip,
                _leftGrabbed);

            UpdateFrameMovement();
        }

        /// <summary>
        /// Lets go of everything immediately, whatever the grips are doing -
        /// e.g. when a mantle takes over. Hand visuals start blending back to
        /// the controllers, climbing stops this frame, and each hand must let
        /// go of grip before it can grab again (see _leftNeedsRegrip).
        /// </summary>
        public void ReleaseAll()
        {
            if (_leftGrabbed is not null) {
                _leftGrabbed = null;
                LeftVisualSnap.Release();
            }

            if (_rightGrabbed is not null) {
                _rightGrabbed = null;
                RightVisualSnap.Release();
            }

            // Set even for a hand that wasn't gripping - its grip may still be
            // held, pointing at the ledge.
            _leftNeedsRegrip = true;
            _rightNeedsRegrip = true;

            _primaryHand = Hand.None;
            IsClimbing = false;
            FrameMovement = Vector3.zero;
            _pendingLocalDelta = Vector3.zero;
        }

        /// <summary>
        /// Places both hand visuals (snapped, blending, or following the
        /// controller). Called by PlayerController at the very end of the
        /// frame, after Move() and turning - the visuals are children of the
        /// rig, so placing them any earlier would let this frame's movement
        /// drag a world-space snap pose off the ledge until next frame.
        /// </summary>
        public void TickHandVisuals()
        {
            LeftVisualSnap.Tick(Time.deltaTime);
            RightVisualSnap.Tick(Time.deltaTime);
        }

        /// <summary>
        /// Handles one hand grabbing the climbable its ray is on, or
        /// releasing whatever it's currently gripping. UpdateLeftHand()/
        /// UpdateRightHand() used to be separate, hand-mirrored copies of
        /// this method - unified here so a future change can't be applied
        /// to one hand and forgotten on the other.
        ///
        /// hand/otherHand identify which hand this call is for, so a single
        /// _primaryHand field (shared between both hands) can still be set
        /// correctly. otherGrabbed/otherIsGrabbing/otherHandPosition
        /// describe the OTHER hand's current state, needed for the
        /// hand-off check at the bottom - see its comment for why passing
        /// these in (rather than reading the other hand's fields directly)
        /// keeps this order-independent between the two Tick() calls.
        /// </summary>
        private void UpdateHandGrab(
            Hand hand,
            Hand otherHand,
            bool isGrabbing,
            bool otherIsGrabbing,
            Vector3 handPosition,
            Vector3 otherHandPosition,
            IHighlightable rayTarget,
            Vector3 rayTargetPoint,
            HandVisualSnap visualSnap,
            ref IClimbable grabbed,
            ref bool needsRegrip,
            IClimbable otherGrabbed)
        {
            if (grabbed is null) {

                if (!isGrabbing) {
                    // Grip let go - this hand may grab again after a
                    // ReleaseAll().
                    needsRegrip = false;
                    return;
                }

                if (needsRegrip) {
                    return;
                }

                // The ray target is only an IHighlightable - anything a hand
                // can point at. A type pattern checks whether it's also
                // climbable (a ledge, a ladder, ...): a single cheap type
                // check, not a component lookup, so it's fine to do every
                // frame grip is held.
                if (rayTarget is not IClimbable climbable) {
                    return;
                }

                grabbed = climbable;

                // The climbable decides where this hand goes and how it's
                // posed - this class never needs to know what a ledge or rung
                // grip looks like. Computed once at grab time: the pose is
                // fixed in world space for as long as the hand holds on.
                visualSnap.Snap(climbable.GetSnapPose(hand == Hand.Left, rayTargetPoint));

                // Every new grab takes over movement, even if the other hand
                // is already gripping something - the most recently grabbed
                // hand always drives climbing.
                _primaryHand = hand;
                _primaryHandLastLocalPosition = playerTransform.InverseTransformPoint(handPosition);

                return;
            }

            if (isGrabbing) {
                // Still gripping - visualSnap keeps the visual pinned to the
                // snap pose in TickHandVisuals().
                return;
            }

            // Released.
            grabbed = null;
            visualSnap.Release();

            if (_primaryHand != hand) {
                return;
            }

            // Hand off to the other hand if it's still gripping, checking
            // its actual current state rather than assuming - this must not
            // depend on which hand's UpdateHandGrab() call runs first this
            // frame.
            if (otherGrabbed is not null && otherIsGrabbing) {
                _primaryHand = otherHand;
                _primaryHandLastLocalPosition = playerTransform.InverseTransformPoint(otherHandPosition);
            } else {
                _primaryHand = Hand.None;
            }
        }

        /// <summary>
        /// Moves the player by the inverse of the primary hand's real-world
        /// movement this frame, so the grabbed point stays fixed in world
        /// space relative to the hand.
        ///
        /// The delta is computed in playerTransform's local space, not world
        /// space, then converted back to a world-space vector at the end.
        /// Local space is unaffected by characterController.Move() (moving a
        /// parent doesn't change a child's local position), so this avoids a
        /// feedback loop: if we compared world positions, the Move() call
        /// this same method produces would itself shift the hand's world
        /// position, which we'd then read as further "movement" next frame,
        /// jittering the player back and forth.
        ///
        /// This frame's raw hand delta is added to _pendingLocalDelta rather
        /// than becoming FrameMovement directly, because _pendingLocalDelta
        /// might already hold a leftover from a previous frame that
        /// characterController.Move() didn't fully apply - see
        /// ReportAppliedMovement(). Folding that in here, rather than
        /// discarding it, is what guarantees the grabbed point never drifts:
        /// every bit of real hand movement is retried until it actually
        /// lands, however long that takes.
        /// </summary>
        private void UpdateFrameMovement()
        {
            if (_primaryHand == Hand.None) {
                IsClimbing = false;
                FrameMovement = Vector3.zero;
                _pendingLocalDelta = Vector3.zero;
                return;
            }

            Vector3 currentWorldPosition = _primaryHand == Hand.Left
                ? playerTracking.LeftHandPosition
                : playerTracking.RightHandPosition;

            Vector3 currentLocalPosition = playerTransform.InverseTransformPoint(currentWorldPosition);
            _pendingLocalDelta += currentLocalPosition - _primaryHandLastLocalPosition;
            _primaryHandLastLocalPosition = currentLocalPosition;

            IsClimbing = true;
            FrameMovement = -playerTransform.TransformVector(_pendingLocalDelta);

            // Optimistically assume this will be applied in full - if it
            // isn't, ReportAppliedMovement() folds whatever's left back in
            // straight after PlayerController calls characterController.Move().
            _pendingLocalDelta = Vector3.zero;
        }

        /// <summary>
        /// Called by PlayerController immediately after it calls
        /// characterController.Move(), with however much the player's
        /// position actually changed this frame - only while climbing (see
        /// PlayerController.Update()), since otherwise this frame's actual
        /// movement came from PlayerLocomotion's walking/gravity instead,
        /// not FrameMovement.
        ///
        /// characterController.Move() can apply less than it was asked to -
        /// nearby collision geometry can absorb or redirect part of the
        /// requested motion, for example. Comparing what actually happened
        /// against what FrameMovement requested, and folding any shortfall
        /// back into _pendingLocalDelta, means UpdateFrameMovement() retries
        /// it next frame instead of it being silently lost - which is what
        /// let the grabbed point drift away from the hand.
        /// </summary>
        public void ReportAppliedMovement(Vector3 actualWorldMovement)
        {
            Vector3 unappliedWorldMovement = FrameMovement - actualWorldMovement;
            _pendingLocalDelta += -playerTransform.InverseTransformVector(unappliedWorldMovement);
        }
    }
}
