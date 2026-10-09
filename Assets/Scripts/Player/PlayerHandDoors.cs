using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The player's side of opening doors. A hand takes the door handle
    /// (Interaction.DoorHandle) its hand ray is on while grip is held, the
    /// same way PlayerClimbing grabs a ledge, and the hand visual snaps
    /// onto the lever.
    ///
    /// Then, while it holds on:
    /// - Turning the wrist turns the lever. Turned far enough
    ///   (Door.UnlatchTwist) the latch frees and the door can swing. A
    ///   locked door's lever stops short and rattles.
    /// - Once free, the door follows the hand round its hinge: the hand's
    ///   movement round the hinge line is the door's turn, so pushing,
    ///   pulling or walking through with the handle all work, either way.
    /// - Letting go of grip lets go of the handle. The door then decides
    ///   for itself whether to swing shut (Door.EndHold()).
    ///
    /// The hand is let go by force if the real hand gets too far from the
    /// handle (breakDistance) - the door has stopped against something, or
    /// the player has walked off. It then has to release grip before it
    /// can take a handle again.
    ///
    /// A hand does one thing at a time: a hand climbing or carrying a prop
    /// can't take a handle, and PlayerClimbing and PlayerHandHolding leave
    /// a hand on a door alone (IsLeftOnDoor/IsRightOnDoor).
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// Tick() (take hold / let go) after climbing and carrying, and
    /// TickHeld() (turn the lever, move the door, move the snapped hand)
    /// after the body has moved and just before the hand visuals are
    /// placed.
    /// </summary>
    public class PlayerHandDoors : MonoBehaviour
    {
        /// One hand's state. A class, made once per hand in Awake(), so the
        /// methods below can change it without ref parameters.
        private class HandOnDoor
        {
            public bool isLeftHand;

            // The handle this hand is on, or null.
            public DoorHandle handle;

            // Which side of the door it took the handle from.
            public bool isFront;

            // True after the hand was let go by force, until grip is
            // released: grip is still held, and would take the handle
            // straight back.
            public bool needsRegrip;

            // The controller's rotation when it took hold, relative to the
            // door. The wrist's twist is measured from this.
            public Quaternion grabRotation;

            // True once the door is following the hand: lastBearing is
            // which way round the hinge the hand was last frame, and
            // swingAngle the angle the hand has asked the door for (which
            // can be past where the door could actually go).
            public bool isSwinging;
            public float lastBearing;
            public float swingAngle;

            // True once a locked door's rattle has played, until the lever
            // is turned back - so it plays once per try, not every frame.
            public bool hasRattled;

            // The sliding bolt this hand is on instead, or null. A hand is
            // on a handle or a bolt, never both.
            public DoorBolt bolt;

            // The difference between the bolt's slide and where the hand
            // was along it when it took hold, so the bolt moves as far as
            // the hand does from there rather than jumping to the hand.
            public float boltSlideOffset;
        }

        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;
        [SerializeField] private PlayerClimbing playerClimbing;

        // Optional: a rig with no carrying or no haptics still opens doors.
        [SerializeField] private PlayerHandHolding playerHandHolding;
        [SerializeField] private PlayerHaptics playerHaptics;

        // A hand carrying the lockpicks or on a pick can't take a handle.
        // Optional: found in Awake() (it's on this same object).
        [SerializeField] private PlayerLockpicking playerLockpicking;

        // The hand is let go of the handle when the real hand is further
        // than this from it, in metres.
        [SerializeField] private float breakDistance = 0.4f;

        [Header("Haptics")]

        // The click as the latch frees.
        [SerializeField] private float unlatchAmplitude = 0.4f;
        [SerializeField] private float unlatchDuration = 0.05f;

        // The knock of a locked lever reaching its stop.
        [SerializeField] private float lockedAmplitude = 0.7f;
        [SerializeField] private float lockedDuration = 0.08f;

        // How far past the door's limit the angle the hand asks for is
        // allowed to run, in degrees. The hand has to come back that far
        // before the door starts to follow it again.
        private const float SwingOverrun = 45f;

        private HandOnDoor _left;
        private HandOnDoor _right;

        private bool _hasHolding;
        private bool _hasHaptics;
        private bool _hasLockpicking;

        /// True while the left hand is on a door handle - and, after being
        /// let go by force, until its grip is released: the hand is still
        /// busy as far as climbing and carrying are concerned.
        public bool IsLeftOnDoor => _left.handle is not null || _left.bolt is not null || _left.needsRegrip;

        /// True while the right hand is on a door handle (as IsLeftOnDoor).
        public bool IsRightOnDoor => _right.handle is not null || _right.bolt is not null || _right.needsRegrip;

        /// <summary>
        /// Editor-only: fills in the references when the component is added
        /// (on the Hands object; the body systems are on the Player root
        /// above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerTracking = GetComponentInParent<PlayerTracking>();
            playerClimbing = GetComponentInParent<PlayerClimbing>();
            playerHaptics = GetComponentInParent<PlayerHaptics>();
            playerHandInteraction = GetComponent<PlayerHandInteraction>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
            playerHandHolding = GetComponent<PlayerHandHolding>();
        }

        private void Awake()
        {
            if (playerHandHolding == null) {
                playerHandHolding = GetComponent<PlayerHandHolding>();
            }

            if (playerHaptics == null) {
                playerHaptics = GetComponentInParent<PlayerHaptics>();
            }

            if (playerLockpicking == null) {
                playerLockpicking = GetComponent<PlayerLockpicking>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasHolding = playerHandHolding != null;
            _hasHaptics = playerHaptics != null;
            _hasLockpicking = playerLockpicking != null;

            _left = new HandOnDoor { isLeftHand = true };
            _right = new HandOnDoor { isLeftHand = false };
        }

        private void OnDisable()
        {
            // The scene is being unloaded (a level restart): the doors are
            // on their way out too.
            if (!gameObject.scene.isLoaded) {
                return;
            }

            LetGo(_left, false);
            LetGo(_right, false);
            LetGoOfBolt(_left, false);
            LetGoOfBolt(_right, false);
        }

        /// <summary>
        /// Takes hold of and lets go of handles, for both hands. Called by
        /// PlayerController after PlayerClimbing.Tick() and
        /// PlayerHandHolding.Tick(), so it sees this frame's climbing grips
        /// and carried props.
        /// </summary>
        public void Tick()
        {
            TickHand(
                _left,
                playerInput.IsLeftGrabbing,
                playerClimbing.IsLeftHandGripping || (_hasHolding && playerHandHolding.IsLeftHolding) || (_hasLockpicking && playerLockpicking.IsLeftBusy),
                playerHandInteraction.LeftTarget);

            TickHand(
                _right,
                playerInput.IsRightGrabbing,
                playerClimbing.IsRightHandGripping || (_hasHolding && playerHandHolding.IsRightHolding) || (_hasLockpicking && playerLockpicking.IsRightBusy),
                playerHandInteraction.RightTarget);
        }

        /// <summary>
        /// One hand: let go of its handle if grip was released, otherwise
        /// take the handle its ray is on if grip is held and the hand is
        /// free.
        /// </summary>
        private void TickHand(HandOnDoor hand, bool isGrabbing, bool isBusy, IHandTarget rayTarget)
        {
            if (hand.handle is not null) {
                if (!isGrabbing) {
                    LetGo(hand, false);
                }

                return;
            }

            if (hand.bolt is not null) {
                if (!isGrabbing) {
                    LetGoOfBolt(hand, false);
                }

                return;
            }

            if (!isGrabbing) {
                // Grip released - this hand may take a handle again.
                hand.needsRegrip = false;
                return;
            }

            if (hand.needsRegrip || isBusy) {
                return;
            }

            // The ray target is only an IHandTarget; a type pattern checks
            // whether it's a door handle (as PlayerClimbing does for
            // climbables). A sliding bolt is taken the same way.
            if (rayTarget is DoorBolt bolt) {
                TakeBolt(hand, bolt);
                return;
            }

            // A door takes one hand at a time.
            if (rayTarget is not DoorHandle handle || handle.Door == null || handle.Door.IsHeld) {
                return;
            }

            TakeHold(hand, handle);
        }

        /// <summary>
        /// Puts this hand on a sliding bolt: the hand visual snaps onto
        /// its knob, and where the hand is along the bolt is noted so the
        /// bolt moves with it from here. One hand at a time.
        /// </summary>
        private void TakeBolt(HandOnDoor hand, DoorBolt bolt)
        {
            HandOnDoor other = hand.isLeftHand ? _right : _left;

            if (other.bolt == bolt) {
                return;
            }

            Vector3 controllerPosition = hand.isLeftHand ? playerTracking.LeftHandPosition : playerTracking.RightHandPosition;

            hand.bolt = bolt;
            hand.boltSlideOffset = bolt.Slide - bolt.SlideAt(controllerPosition);
            SnapFor(hand).Snap(bolt.GetSnapPose(hand.isLeftHand));
        }

        /// <summary>
        /// Takes this hand off its bolt (nothing happens if it isn't on
        /// one): a bolt left part way settles at the nearer end, and the
        /// hand visual blends back to the controller. With byForce, the
        /// hand can't take anything again until its grip has been released.
        /// </summary>
        private void LetGoOfBolt(HandOnDoor hand, bool byForce)
        {
            if (hand == null || hand.bolt is null) {
                return;
            }

            // Unity's == null is true for a bolt destroyed while held.
            if (hand.bolt != null) {
                hand.bolt.Release();
            }

            hand.bolt = null;
            hand.needsRegrip = byForce;
            SnapFor(hand).Release();
        }

        /// <summary>
        /// One hand on a bolt: the bolt slides as far as the real hand has
        /// moved along it since it took hold, with a click in the hand as
        /// it reaches either end, and the snapped hand moves with the
        /// knob. The hand is let go by force if the real hand strays too
        /// far.
        /// </summary>
        private void TickHeldBolt(HandOnDoor hand, Vector3 controllerPosition)
        {
            if (hand.bolt is null) {
                return;
            }

            // Bolt destroyed while held (Unity's == null).
            if (hand.bolt == null) {
                LetGoOfBolt(hand, true);
                return;
            }

            DoorBolt bolt = hand.bolt;

            if (bolt.SetSlide(bolt.SlideAt(controllerPosition) + hand.boltSlideOffset)) {
                Pulse(hand, unlatchAmplitude, unlatchDuration);
            }

            HandSnapPose pose = bolt.GetSnapPose(hand.isLeftHand);
            SnapFor(hand).SetSnapPose(pose.Position, pose.Rotation);

            if ((controllerPosition - bolt.GetGripPoint()).sqrMagnitude > breakDistance * breakDistance) {
                LetGoOfBolt(hand, true);
            }
        }

        /// <summary>
        /// This hand's snap, owned by PlayerHandVisuals. Looked up when
        /// needed rather than kept from Awake(): PlayerHandVisuals makes
        /// them in its own Awake(), which may run after this class's.
        /// </summary>
        private HandVisualSnap SnapFor(HandOnDoor hand)
        {
            return hand.isLeftHand ? playerHandVisuals.LeftVisualSnap : playerHandVisuals.RightVisualSnap;
        }

        /// <summary>
        /// Puts this hand on handle: the hand visual snaps onto the lever
        /// on the player's side of the door, and the controller's rotation
        /// is noted so the wrist's twist can be measured from here.
        /// </summary>
        private void TakeHold(HandOnDoor hand, DoorHandle handle)
        {
            Door door = handle.Door;
            Quaternion controllerRotation = hand.isLeftHand ? playerTracking.LeftHandRotation : playerTracking.RightHandRotation;

            hand.handle = handle;
            hand.isFront = handle.IsInFront(playerTracking.HeadPosition);
            hand.grabRotation = Quaternion.Inverse(door.transform.rotation) * controllerRotation;
            hand.isSwinging = false;
            hand.hasRattled = false;

            door.BeginHold();
            SnapFor(hand).Snap(handle.GetSnapPose(hand.isLeftHand, hand.isFront, handle.LeverAngle));
        }

        /// <summary>
        /// Takes this hand off its handle (nothing happens if it isn't on
        /// one): the lever springs back, the door is told, and the hand
        /// visual blends back to the controller. With byForce, the hand
        /// can't take a handle again until its grip has been released.
        /// </summary>
        private void LetGo(HandOnDoor hand, bool byForce)
        {
            if (hand == null || hand.handle is null) {
                return;
            }

            // Unity's == null is true for a handle destroyed while held.
            if (hand.handle != null) {
                hand.handle.SetLeverAngle(0f);

                if (hand.handle.Door != null) {
                    hand.handle.Door.EndHold();
                }
            }

            hand.handle = null;
            hand.isSwinging = false;
            hand.needsRegrip = byForce;
            SnapFor(hand).Release();
        }

        /// <summary>
        /// Works each held handle for this frame. Called by
        /// PlayerController after the body has moved and turned and just
        /// before PlayerHandVisuals.Tick() - the door has to be where the
        /// hand has taken it before the hand visual is placed on it - on
        /// every path through Update(), so a hand left on a handle during
        /// a mantle is let go once the body has moved away.
        /// </summary>
        public void TickHeld()
        {
            TickHeldHand(_left, playerTracking.LeftHandPosition, playerTracking.LeftHandRotation);
            TickHeldHand(_right, playerTracking.RightHandPosition, playerTracking.RightHandRotation);
            TickHeldBolt(_left, playerTracking.LeftHandPosition);
            TickHeldBolt(_right, playerTracking.RightHandPosition);
        }

        /// <summary>
        /// One hand on a handle: turn the lever with the wrist, free the
        /// latch or rattle the lock when it reaches its stop, swing the
        /// door with the hand once it's free, then move the snapped hand
        /// to where the lever now is.
        /// </summary>
        private void TickHeldHand(HandOnDoor hand, Vector3 controllerPosition, Quaternion controllerRotation)
        {
            if (hand.handle is null) {
                return;
            }

            // Handle or door destroyed while held (Unity's == null).
            if (hand.handle == null || hand.handle.Door == null) {
                LetGo(hand, true);
                return;
            }

            DoorHandle handle = hand.handle;
            Door door = handle.Door;

            // How far the wrist has turned about the spindle since it took
            // hold. Both rotations are relative to the door, so the door
            // swinging under a hand that isn't turning reads as no twist.
            Quaternion relative = Quaternion.Inverse(door.transform.rotation) * controllerRotation;
            float twist = TwistAboutZ(relative * Quaternion.Inverse(hand.grabRotation));

            // The lever turns with the wrist as far as its stop: the full
            // turn that frees the latch, or the short one a lock or a shot
            // bolt allows.
            float stop = door.IsHeldShut ? door.LockedTwist : door.UnlatchTwist;
            handle.SetLeverAngle(Mathf.Clamp(twist, -stop, stop));

            bool isAtStop = Mathf.Abs(twist) >= stop;

            if (door.IsLatched) {
                if (door.IsHeldShut) {
                    TickLocked(hand, door, handle, isAtStop, twist, stop);
                } else if (isAtStop) {
                    door.Unlatch(handle.GetGripPoint(hand.isFront));
                    Pulse(hand, unlatchAmplitude, unlatchDuration);
                }
            }

            if (door.IsOpen) {
                TickSwing(hand, door, controllerPosition);
            }

            // The handle has moved with the door and turned with the
            // wrist: the snapped hand goes with it.
            HandSnapPose pose = handle.GetSnapPose(hand.isLeftHand, hand.isFront, handle.LeverAngle);
            SnapFor(hand).SetSnapPose(pose.Position, pose.Rotation);

            // Compared as squared distances - no square root needed.
            float sqrDistance = (controllerPosition - handle.GetGripPoint(hand.isFront)).sqrMagnitude;

            if (sqrDistance > breakDistance * breakDistance) {
                LetGo(hand, true);
            }
        }

        /// <summary>
        /// A hand turning the lever of a locked door: the first time it
        /// reaches the stop, the lock rattles and the controller knocks.
        /// It can rattle again once the lever has been turned at least
        /// half way back.
        /// </summary>
        private void TickLocked(HandOnDoor hand, Door door, DoorHandle handle, bool isAtStop, float twist, float stop)
        {
            if (isAtStop && !hand.hasRattled) {
                hand.hasRattled = true;
                door.RattleLocked(handle.GetGripPoint(hand.isFront));
                Pulse(hand, lockedAmplitude, lockedDuration);
            } else if (Mathf.Abs(twist) < stop * 0.5f) {
                hand.hasRattled = false;
            }
        }

        /// <summary>
        /// Swings a freed door with the hand. Each frame the hand's
        /// bearing round the hinge is compared with last frame's, and the
        /// difference is added to the angle asked of the door - so the
        /// door turns exactly as far as the hand went round, however far
        /// from the hinge the hand is. Moving straight towards or away
        /// from the hinge changes nothing.
        ///
        /// The angle asked for is kept even when the door can't follow
        /// (at its limit, or against the player): the hand then has to
        /// come back as far as it overshot before the door moves again,
        /// as a real handle would.
        /// </summary>
        private void TickSwing(HandOnDoor hand, Door door, Vector3 controllerPosition)
        {
            if (!door.TryGetBearing(controllerPosition, out float bearing)) {
                return;
            }

            // First frame of swinging (the latch has just freed, or the
            // door was already open when taken): start from where the
            // door and the hand are now.
            if (!hand.isSwinging) {
                hand.isSwinging = true;
                hand.lastBearing = bearing;
                hand.swingAngle = door.Angle;
                return;
            }

            // DeltaAngle gives the short way round, so passing from 179
            // to -179 degrees is a turn of 2, not 358.
            float limit = door.MaxOpenAngle + SwingOverrun;
            hand.swingAngle = Mathf.Clamp(hand.swingAngle + Mathf.DeltaAngle(hand.lastBearing, bearing), -limit, limit);
            hand.lastBearing = bearing;

            door.SetAngle(hand.swingAngle);
        }

        /// <summary>
        /// How far rotation turns about the Z axis, in degrees (-180 to
        /// 180), ignoring whatever else it does. A quaternion's z and w
        /// parts hold the sine and cosine of half its turn about Z, so
        /// the angle is twice their arctangent.
        /// </summary>
        private static float TwistAboutZ(Quaternion rotation)
        {
            float angle = 2f * Mathf.Atan2(rotation.z, rotation.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, angle);
        }

        /// <summary>
        /// Buzzes this hand's controller, if the rig has haptics.
        /// </summary>
        private void Pulse(HandOnDoor hand, float amplitude, float duration)
        {
            if (_hasHaptics) {
                playerHaptics.Pulse(hand.isLeftHand, amplitude, duration);
            }
        }
    }
}
