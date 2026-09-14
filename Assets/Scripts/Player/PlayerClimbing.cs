using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Handles grabbing and climb movement. Edge highlighting lives in
    /// PlayerHandInteraction instead - see that class' comment.
    ///
    /// This class does not run its own Update(). Instead PlayerLocomotion calls
    /// Tick() explicitly once per frame, at the point where it wants climbing
    /// to run, the same way it already sequences its own Handle*() methods -
    /// that keeps frame ordering deterministic instead of depending on Unity's
    /// undefined order between different components' Update() calls.
    /// </summary>
    public class PlayerClimbing : MonoBehaviour
    {
        // Which hand is currently driving climb movement. Hand.None means
        // nobody is climbing.
        private enum Hand { None, Left, Right }

        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;

        // Only touched once, in Awake(), to disable minMoveDistance - see
        // its comment there. PlayerLocomotion still owns the actual Move()
        // call.
        [SerializeField] private CharacterController characterController;

        // The rig root transform that characterController.Move() displaces -
        // the same transform PlayerLocomotion calls playerTransform. Hand
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

        [Header("Hand Grab Volume")]

        // Real SphereCollider components on each hand, so a grab succeeds
        // if any part of the hand's sphere overlaps a ClimbableEdge, not
        // only its exact tracked origin point - see FindEdgeOverlapping().
        // Only ever read here for their radius; they're not used as
        // physics colliders (no rigidbody, no collision response), just a
        // designer-tunable, inspector-visible shape. Assumes the hand
        // transforms aren't non-uniformly scaled, since SphereCollider.radius
        // doesn't itself account for scale.
        [SerializeField] private SphereCollider leftHandSphere;
        [SerializeField] private SphereCollider rightHandSphere;

        private ClimbableEdge _leftGrabbedEdge;
        private ClimbableEdge _rightGrabbedEdge;

        private Hand _primaryHand = Hand.None;

        // The primary hand's position last frame, in playerTransform's local
        // space rather than world space - see the playerTransform field comment.
        private Vector3 _primaryHandLastLocalPosition;

        // Local-space hand movement measured but not yet actually applied
        // to the CharacterController - see UpdateFrameMovement() and
        // ReportAppliedMovement().
        private Vector3 _pendingLocalDelta;

        // Fixed world-space points the hand visuals are pinned to while
        // gripping, computed once at grab time.
        private Vector3 _leftSnapPosition;
        private Vector3 _rightSnapPosition;

        // The hand visuals' original local positions, restored on release.
        private Vector3 _leftVisualOriginalLocalPosition;
        private Vector3 _rightVisualOriginalLocalPosition;

        /// True while a hand is gripping an edge and driving climb movement.
        public bool IsClimbing { get; private set; }

        /// True while the left hand is gripping a climbable edge, regardless
        /// of whether it's the primary hand currently driving movement -
        /// e.g. hand animation needs to know per-hand grip state, not just
        /// which hand (if any) is steering.
        public bool IsLeftHandGripping => _leftGrabbedEdge is not null;

        /// True while the right hand is gripping a climbable edge - mirrors
        /// IsLeftHandGripping.
        public bool IsRightHandGripping => _rightGrabbedEdge is not null;

        /// This frame's climb movement, for PlayerLocomotion to add to its
        /// own frame movement accumulator.
        public Vector3 FrameMovement { get; private set; }

        private void Awake()
        {
            _leftVisualOriginalLocalPosition = leftHandVisual.localPosition;
            _rightVisualOriginalLocalPosition = rightHandVisual.localPosition;

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
        /// Runs one frame of climbing logic: grab/release for both hands,
        /// climb movement, and pinning the hand visuals. Edge highlighting
        /// now lives in PlayerHandInteraction, since it's no longer specific
        /// to climbing.
        /// </summary>
        public void Tick()
        {
            UpdateHandGrab(
                Hand.Left, Hand.Right,
                playerInput.IsLeftGrabbing, playerInput.IsRightGrabbing,
                playerTracking.LeftHandPosition, playerTracking.RightHandPosition,
                leftHandSphere, leftHandVisual, _leftVisualOriginalLocalPosition,
                ref _leftGrabbedEdge, ref _leftSnapPosition,
                _rightGrabbedEdge);

            UpdateHandGrab(
                Hand.Right, Hand.Left,
                playerInput.IsRightGrabbing, playerInput.IsLeftGrabbing,
                playerTracking.RightHandPosition, playerTracking.LeftHandPosition,
                rightHandSphere, rightHandVisual, _rightVisualOriginalLocalPosition,
                ref _rightGrabbedEdge, ref _rightSnapPosition,
                _leftGrabbedEdge);

            UpdateFrameMovement();
        }

        /// <summary>
        /// Finds any climbable edge whose box overlaps a sphere of the given
        /// radius centred on point, regardless of whether it's currently
        /// highlighted - a hand can grab any edge any part of it touches,
        /// not just the one the player is looking at.
        /// </summary>
        private static ClimbableEdge FindEdgeOverlapping(Vector3 point, float radius)
        {
            foreach (ClimbableEdge edge in ClimbableEdge.Active) {

                // Cheap AABB reject before the oriented ClosestPoint check
                // inside Overlaps() - ClosestPoint is a real physics query,
                // so with many ledges in a level this skips most of them
                // without ever touching a collider.
                Bounds bounds = edge.Bounds;
                bounds.Expand(radius * 2f);

                if (!bounds.Contains(point)) {
                    continue;
                }

                if (edge.Overlaps(point, radius)) {
                    return edge;
                }
            }

            return null;
        }

        /// <summary>
        /// Handles one hand grabbing whatever climbable edge it's inside, or
        /// releasing whatever it's currently gripping. UpdateLeftHand()/
        /// UpdateRightHand() used to be separate, hand-mirrored copies of
        /// this method - unified here so a future change can't be applied
        /// to one hand and forgotten on the other.
        ///
        /// hand/otherHand identify which hand this call is for, so a single
        /// _primaryHand field (shared between both hands) can still be set
        /// correctly. otherGrabbedEdge/otherIsGrabbing/otherHandPosition
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
            SphereCollider handSphere,
            Transform handVisual,
            Vector3 visualOriginalLocalPosition,
            ref ClimbableEdge grabbedEdge,
            ref Vector3 snapPosition,
            ClimbableEdge otherGrabbedEdge)
        {
            if (grabbedEdge is null) {

                if (!isGrabbing) {
                    return;
                }

                ClimbableEdge edge = FindEdgeOverlapping(handPosition, handSphere.radius);

                if (edge is null) {
                    return;
                }

                grabbedEdge = edge;
                snapPosition = grabbedEdge.ClosestPoint(handPosition);

                // Every new grab takes over movement, even if the other hand
                // is already gripping something - the most recently grabbed
                // hand always drives climbing.
                _primaryHand = hand;
                _primaryHandLastLocalPosition = playerTransform.InverseTransformPoint(handPosition);

                return;
            }

            if (isGrabbing) {
                // Still gripping - keep the visual pinned to the fixed grab
                // point, regardless of where tracking moved the real hand.
                handVisual.position = snapPosition;
                return;
            }

            // Released.
            grabbedEdge = null;
            handVisual.localPosition = visualOriginalLocalPosition;

            if (_primaryHand != hand) {
                return;
            }

            // Hand off to the other hand if it's still gripping, checking
            // its actual current state rather than assuming - this must not
            // depend on which hand's UpdateHandGrab() call runs first this
            // frame.
            if (otherGrabbedEdge is not null && otherIsGrabbing) {
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
            // straight after PlayerLocomotion calls characterController.Move().
            _pendingLocalDelta = Vector3.zero;
        }

        /// <summary>
        /// Called by PlayerLocomotion immediately after it calls
        /// characterController.Move(), with however much the player's
        /// position actually changed this frame - only while climbing (see
        /// PlayerLocomotion.Update()), since otherwise this frame's actual
        /// movement came from HandleMovement()/HandleGravity() instead, not
        /// FrameMovement.
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
