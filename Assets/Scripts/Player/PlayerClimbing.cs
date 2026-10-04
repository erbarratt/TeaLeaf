using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Handles grabbing and climb movement. Hand ray targeting lives in
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

        // Owns the hand visuals - this class asks it to snap a hand onto
        // what it grabbed (and release it), but never moves a visual itself.
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        [Header("Grab Targeting")]

        // Grabs are ray-targeted: a hand grabs whatever IClimbable its
        // hand ray (and reticle) is on while grip is held, rather than
        // whatever its old SphereCollider happened to overlap. Reusing
        // PlayerHandInteraction's ray means what the reticle is on is exactly
        // what gets grabbed, and its rayLength doubles as grab reach.
        // PlayerController ticks it before this class, so its targets are
        // always this frame's.
        [SerializeField] private PlayerHandInteraction playerHandInteraction;

        // Carrying props. A hand with a prop in it can't grab a climbable -
        // grip is already held, so without this check it would grab any
        // ledge its ray crossed. Optional: found in Awake() if not wired.
        [SerializeField] private PlayerHandHolding playerHandHolding;

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

        // The zip line being ridden, or null. While set, each hand gripping
        // it slides along it every frame - see TickZip().
        private IZipLine _zipLine;

        // Which way the ride goes along the line (+1 towards its far end, -1
        // towards its start), and its current speed in metres per second -
        // shared by both hands, so a second hand joining slides in step.
        private int _zipDirection;
        private float _zipSpeed;

        // How far along the line (0-1) each hand's grip currently is.
        private float _leftZipT;
        private float _rightZipT;

        // How far the primary hand's grip slid this frame, in world space -
        // added to FrameMovement so the player travels with it.
        private Vector3 _zipMovement;

        // A zip ride ends if the player's body falls this far behind the
        // sliding grip, in metres - it has hit something (a wall, the
        // ground at the bottom of the line) and can't follow.
        private const float ZipBlockedDistance = 0.5f;

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

        /// What the left hand is gripping (a ledge, ladder or rope), or null -
        /// so PlayerMantling can ask it whether a mantle is possible.
        public IClimbable LeftGrabbed => _leftGrabbed;

        /// What the right hand is gripping, or null - mirrors LeftGrabbed.
        public IClimbable RightGrabbed => _rightGrabbed;

        /// Where the left hand grabbed what it's gripping (world space, the
        /// hand ray's hit point at the moment of the grab) - e.g. which
        /// ladder rung it's on. Only meaningful while LeftGrabbed isn't null.
        public Vector3 LeftGrabPoint { get; private set; }

        /// Where the right hand grabbed - mirrors LeftGrabPoint.
        public Vector3 RightGrabPoint { get; private set; }

        /// This frame's climb movement, for PlayerController to add to its
        /// frame movement accumulator.
        public Vector3 FrameMovement { get; private set; }

        private void Awake()
        {
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

            // The field was added after this component was set up in the
            // scene. It's on the Hands object below this one; one lookup at
            // startup. Stays null (nothing is ever carried) if there's none.
            if (playerHandHolding == null) {
                playerHandHolding = GetComponentInChildren<PlayerHandHolding>();
            }
        }

        /// <summary>
        /// Runs one frame of climbing logic: grab/release for both hands and
        /// climb movement. Hand ray targeting lives in PlayerHandInteraction,
        /// and placing the hand visuals in PlayerHandVisuals.
        /// </summary>
        public void Tick()
        {
            // Last frame's carrying state (PlayerHandHolding ticks after
            // this class) - which is right: a prop picked up this frame was
            // this frame's ray target, so it wasn't a climbable anyway.
            bool hasHolding = playerHandHolding != null;
            bool isLeftCarrying = hasHolding && playerHandHolding.IsLeftHolding;
            bool isRightCarrying = hasHolding && playerHandHolding.IsRightHolding;

            UpdateHandGrab(
                Hand.Left, Hand.Right,
                playerInput.IsLeftGrabbing && !isLeftCarrying, playerInput.IsRightGrabbing && !isRightCarrying,
                playerTracking.LeftHandPosition, playerTracking.RightHandPosition,
                playerHandInteraction.LeftTarget, playerHandInteraction.LeftTargetPoint,
                playerHandVisuals.LeftVisualSnap,
                ref _leftGrabbed,
                ref _leftNeedsRegrip,
                _rightGrabbed);

            UpdateHandGrab(
                Hand.Right, Hand.Left,
                playerInput.IsRightGrabbing && !isRightCarrying, playerInput.IsLeftGrabbing && !isLeftCarrying,
                playerTracking.RightHandPosition, playerTracking.LeftHandPosition,
                playerHandInteraction.RightTarget, playerHandInteraction.RightTargetPoint,
                playerHandVisuals.RightVisualSnap,
                ref _rightGrabbed,
                ref _rightNeedsRegrip,
                _leftGrabbed);

            TickZip();
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
                playerHandVisuals.LeftVisualSnap.Release();
            }

            if (_rightGrabbed is not null) {
                _rightGrabbed = null;
                playerHandVisuals.RightVisualSnap.Release();
            }

            // Set even for a hand that wasn't gripping - its grip may still be
            // held, pointing at the ledge.
            _leftNeedsRegrip = true;
            _rightNeedsRegrip = true;

            _primaryHand = Hand.None;
            IsClimbing = false;
            FrameMovement = Vector3.zero;
            _pendingLocalDelta = Vector3.zero;
            EndZip();
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
            IHandTarget rayTarget,
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

                // The ray target is only an IHandTarget - anything a hand
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
                // fixed in world space for as long as the hand holds on. The
                // head position tells a two-sided ledge which side the player
                // is on, so the hand snaps onto the face nearest them; the
                // head's forward tells a strung rope which way along it the
                // player faces, so the hand isn't back to front.
                visualSnap.Snap(climbable.GetSnapPose(
                    hand == Hand.Left,
                    rayTargetPoint,
                    playerTracking.HeadPosition,
                    playerTracking.HeadRotation * Vector3.forward));

                // Remember where it grabbed - a ladder uses it to tell which
                // rung the hand is on (for mantling off the top).
                if (hand == Hand.Left) {
                    LeftGrabPoint = rayTargetPoint;
                } else {
                    RightGrabPoint = rayTargetPoint;
                }

                // Every new grab takes over movement, even if the other hand
                // is already gripping something - the most recently grabbed
                // hand always drives climbing.
                _primaryHand = hand;
                _primaryHandLastLocalPosition = playerTransform.InverseTransformPoint(handPosition);

                // A zip line starts carrying the hand along from here. The
                // property pattern is a type check plus one bool read.
                if (climbable is IZipLine { IsZipLine: true } zipLine) {
                    BeginZipGrip(hand, zipLine, rayTargetPoint);
                }

                return;
            }

            if (isGrabbing) {
                // Still gripping - PlayerHandVisuals keeps the visual pinned
                // to the snap pose.
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
        /// A hand has just grabbed a zip line at grabPoint: start a ride if
        /// one isn't already running on this line (from rest, in the
        /// direction the line says - downhill, or the way the player faces
        /// on a level line), and note how far along the line this hand is.
        /// A second hand grabbing the line mid-ride just joins it at the
        /// ride's current speed.
        /// </summary>
        private void BeginZipGrip(Hand hand, IZipLine zipLine, Vector3 grabPoint)
        {
            if (!ReferenceEquals(_zipLine, zipLine)) {
                _zipLine = zipLine;
                _zipSpeed = 0f;
                _zipDirection = zipLine.GetZipDirection(playerTracking.HeadRotation * Vector3.forward);
            }

            float t = zipLine.GetClosestT(grabPoint);

            if (hand == Hand.Left) {
                _leftZipT = t;
            } else {
                _rightZipT = t;
            }
        }

        /// <summary>
        /// Forgets the zip ride. The hands are released separately - this
        /// only clears the ride's own state.
        /// </summary>
        private void EndZip()
        {
            _zipLine = null;
            _zipSpeed = 0f;
            _zipMovement = Vector3.zero;
        }

        /// <summary>
        /// Advances a zip ride by one frame: speeds up towards the line's top
        /// speed, then slides each hand gripping the line that far along it.
        /// The primary hand's slide becomes _zipMovement, which
        /// UpdateFrameMovement() adds to the player's movement - so the body
        /// travels with the grip, and ordinary climbing (pulling yourself
        /// about relative to the hand) still works on top.
        ///
        /// The ride ends when no hand is on the line any more (let go, or
        /// slid off the end), or when the hand driving movement has grabbed
        /// something else - then the hand left on the line is let go too,
        /// rather than sliding away from a body that's now held in place.
        /// Does nothing while there's no ride.
        /// </summary>
        private void TickZip()
        {
            _zipMovement = Vector3.zero;

            if (_zipLine == null) {
                return;
            }

            bool isLeftOnLine = ReferenceEquals(_leftGrabbed, _zipLine);
            bool isRightOnLine = ReferenceEquals(_rightGrabbed, _zipLine);

            if (!isLeftOnLine && !isRightOnLine) {
                EndZip();
                return;
            }

            IClimbable primaryGrabbed = _primaryHand == Hand.Left ? _leftGrabbed : _rightGrabbed;

            if (!ReferenceEquals(primaryGrabbed, _zipLine)) {
                if (isLeftOnLine) {
                    ForceRelease(Hand.Left);
                }

                if (isRightOnLine) {
                    ForceRelease(Hand.Right);
                }

                EndZip();
                return;
            }

            _zipSpeed = Mathf.MoveTowards(_zipSpeed, _zipLine.ZipSpeed, _zipLine.ZipAcceleration * Time.deltaTime);

            // The distance to slide this frame, as a fraction of the line.
            float deltaT = _zipDirection * _zipSpeed * Time.deltaTime / Mathf.Max(_zipLine.Length, 0.01f);

            if (isLeftOnLine) {
                SlideHand(Hand.Left, ref _leftZipT, deltaT, playerHandVisuals.LeftVisualSnap);
            }

            if (isRightOnLine) {
                SlideHand(Hand.Right, ref _rightZipT, deltaT, playerHandVisuals.RightVisualSnap);
            }
        }

        /// <summary>
        /// Slides one hand's grip deltaT further along the zip line (never
        /// past either end): moves the pose its visual is snapped to, and,
        /// if it's the hand driving movement, records the slide for the
        /// player to follow. A grip that reaches the end it was heading for
        /// is let go - with nothing left holding on, the player drops.
        /// </summary>
        private void SlideHand(Hand hand, ref float zipT, float deltaT, HandVisualSnap visualSnap)
        {
            float newT = Mathf.Clamp01(zipT + deltaT);
            Vector3 movement = _zipLine.GetPoint(newT) - _zipLine.GetPoint(zipT);
            zipT = newT;

            visualSnap.MoveSnapPose(movement);

            if (hand == _primaryHand) {
                _zipMovement = movement;
            }

            bool hasReachedEnd = _zipDirection > 0 ? newT >= 1f : newT <= 0f;

            if (hasReachedEnd) {
                ForceRelease(hand);
            }
        }

        /// <summary>
        /// Makes one hand let go, whatever its grip is doing: its visual
        /// starts blending back to the controller, and it can't grab again
        /// until its grip has been released (see _leftNeedsRegrip) - or it
        /// would re-grab the same rope on the next frame. If it was the hand
        /// driving movement, the other hand takes over if it's gripping
        /// something; otherwise climbing stops and the player falls.
        /// </summary>
        private void ForceRelease(Hand hand)
        {
            bool isLeft = hand == Hand.Left;

            if (isLeft) {
                _leftGrabbed = null;
                _leftNeedsRegrip = true;
                playerHandVisuals.LeftVisualSnap.Release();
            } else {
                _rightGrabbed = null;
                _rightNeedsRegrip = true;
                playerHandVisuals.RightVisualSnap.Release();
            }

            if (_primaryHand != hand) {
                return;
            }

            IClimbable otherGrabbed = isLeft ? _rightGrabbed : _leftGrabbed;

            if (otherGrabbed is null) {
                _primaryHand = Hand.None;
                return;
            }

            _primaryHand = isLeft ? Hand.Right : Hand.Left;
            Vector3 otherHandPosition = isLeft ? playerTracking.RightHandPosition : playerTracking.LeftHandPosition;
            _primaryHandLastLocalPosition = playerTransform.InverseTransformPoint(otherHandPosition);
        }

        /// <summary>
        /// Moves the player by the inverse of the primary hand's real-world
        /// movement this frame, so the grabbed point stays fixed in world
        /// space relative to the hand. On a zip line the grabbed point itself
        /// moves, so that frame's slide (_zipMovement) is added on top.
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
            FrameMovement = -playerTransform.TransformVector(_pendingLocalDelta) + _zipMovement;

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

            // On a zip line the grip keeps sliding whether or not the body
            // can follow. Once the body is well behind it - blocked by a
            // wall, or by the ground where the line comes down - let go,
            // rather than stretching further and further from the hands.
            if (_zipLine != null && _pendingLocalDelta.sqrMagnitude > ZipBlockedDistance * ZipBlockedDistance) {
                ReleaseAll();
            }
        }
    }
}
