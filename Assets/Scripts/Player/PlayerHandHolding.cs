using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Picking up, carrying, dropping and throwing props
    /// (Interaction.Grabbable). A hand picks up the prop its hand ray is on
    /// while grip is held, the same way PlayerClimbing grabs a ledge - what
    /// the reticle is on is what gets picked up. Letting go of grip with
    /// the hand still drops it; letting go with the hand moving throws it,
    /// with the movement the prop had over the last tenth of a second.
    ///
    /// Picking up is in two parts. First the hand visual reaches out to the
    /// prop where it lies - snapped, as climbing snaps a hand onto a ledge,
    /// to the pose the prop's grip point and snap profile describe - while
    /// the prop stays put. Once the hand is on it, the snap is released:
    /// the hand goes back to the controller and the prop comes with it.
    ///
    /// A carried prop is a CHILD of the hand visual, so nothing has to place
    /// it: wherever the visual goes - with the controller, including the
    /// Tracked Pose Driver's last move just before rendering, or held back
    /// by a surface - the prop is locked to it. (Placing it from code each
    /// frame instead left a slight wobble between hand and prop.) The right
    /// hand's visual is mirrored (x scale -1), which a child inherits; the
    /// prop is parented keeping its world pose, so Unity gives it a
    /// mirrored local scale that cancels the parent's and it's drawn the
    /// right way round. Its own scale is put back when it's dropped.
    ///
    /// So the prop follows the hand VISUAL, not the controller. The visual
    /// stops at surfaces (the physical hands), and while a prop is carried
    /// the hand's collision shape is enlarged to cover it
    /// (PlayerHandVisuals.SetHeldShape) - so a carried prop stops at a wall
    /// with the hand instead of being pushed through it.
    ///
    /// A hand does one thing at a time: a hand gripping a climbable can't
    /// pick up, and a hand carrying a prop can't climb (PlayerClimbing
    /// checks IsLeftHolding/IsRightHolding).
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls Tick()
    /// (pick up / drop) after climbing, and TickHeld() (attaching a prop
    /// once the hand has reached it) straight after the hand visuals are
    /// placed.
    /// </summary>
    public class PlayerHandHolding : MonoBehaviour
    {
        /// One hand's carrying state. A class, made once per hand in Awake(),
        /// so the methods below can change it without ref parameters.
        private class HandHold
        {
            // The hand model's transform - a carried prop's parent.
            public Transform visual;
            public bool isLeftHand;

            // What this hand is carrying, or null.
            public Grabbable grabbable;

            // True while the hand visual is still on its way out to the
            // prop: the prop isn't a child of the hand yet.
            public bool isReaching;

            // The prop's parent and local scale before it was picked up,
            // put back when it's dropped.
            public Transform originalParent;
            public Vector3 originalScale;

            // How much of the prop's finger pose the hand shows (0-1) on
            // top of what the snap asks for - see LeftPoseWeight.
            public float poseWeight;

            // The prop this hand last dropped, while it's still on the
            // hands' layer waiting to get clear of the hand (or null), and
            // how long ago it was dropped - see TickReleased().
            public Grabbable released;
            public float releasedTime;

            // The middle of the carried prop, as a point in the hand
            // visual's own space - set when the prop is attached. The
            // throw's speed is measured there, not at the wrist: a flick
            // of the wrist moves the prop much further than the wrist.
            public Vector3 centreInVisual;

            // Where the prop's middle was, which way the hand was turned
            // and when, for the last few frames it has been carried - all
            // relative to the rig. A ring: sampleNext is the slot the next
            // sample goes in, wrapping round to overwrite the oldest, and
            // sampleCount is how many slots hold a sample. See
            // TickThrowSamples().
            public readonly Vector3[] samplePositions = new Vector3[SampleCapacity];
            public readonly Quaternion[] sampleRotations = new Quaternion[SampleCapacity];
            public readonly float[] sampleTimes = new float[SampleCapacity];
            public int sampleNext;
            public int sampleCount;
        }

        [SerializeField] private PlayerInputXR playerInput;

        // For the head's position: a cylinder prop is gripped on the
        // player's side. Optional: found in Awake() if not wired.
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;
        [SerializeField] private PlayerClimbing playerClimbing;

        // Seconds for the hand visual to reach out to a prop being picked
        // up, and seconds for hand and prop to come back to the controller.
        [SerializeField] private float reachDuration = 0.12f;
        [SerializeField] private float returnDuration = 0.18f;

        // Seconds for the fingers to open from the prop's hold pose back to
        // following the grip and trigger, after a drop.
        [SerializeField] private float poseReleaseDuration = 0.1f;

        // A dropped prop goes back on its own layer (where hands collide
        // with it and can target it) once its middle is its hold radius
        // plus this far from the hand, in metres - about a hand's length...
        [SerializeField] private float releaseClearance = 0.12f;

        // ...or after this many seconds, if it hasn't got clear (dropped
        // onto a table with the hand left resting on it).
        [SerializeField] private float releaseTimeout = 0.5f;

        [Header("Throwing")]

        // For the body's own speed, added to a throw, and for the rig the
        // hand's movement is measured in. Optional: found in Awake() if
        // not wired.
        [SerializeField] private CharacterController characterController;

        // How far back the throw looks, in seconds: the prop's speed is
        // its movement over this long, up to the moment it's let go.
        // Longer is steadier but slower to notice a quick flick.
        [SerializeField] private float throwSampleWindow = 0.1f;

        // Letting go with the prop moving slower than this (metres a
        // second, relative to the body) is a drop from rest, not a throw.
        [SerializeField] private float throwMinSpeed = 1f;

        // Multiplies the hand's part of the throw: above 1 throws further
        // than the real movement would.
        [SerializeField] private float throwStrength = 1f;

        // The fastest a prop can leave the hand, in metres a second - so a
        // tracking glitch can't fire one across the level.
        [SerializeField] private float throwMaxSpeed = 12f;

        // How many frames of movement each hand remembers. Must cover
        // throwSampleWindow at the headset's frame rate: 0.1s is 12 frames
        // at 120 a second.
        private const int SampleCapacity = 32;

        // The layer carried props are moved to. The hands' own layer: the
        // player's body doesn't collide with it, and neither hand rays nor
        // the hands' own collision sweep include it.
        private const string HeldLayerName = "PlayerHands";

        private HandHold _left;
        private HandHold _right;
        private int _heldLayer;

        // The rig root (the transform the CharacterController is on).
        private Transform _rig;

        /// True while the left hand is carrying a prop.
        public bool IsLeftHolding => _left.grabbable is not null;

        /// True while the right hand is carrying a prop.
        public bool IsRightHolding => _right.grabbable is not null;

        /// What the left hand is carrying, or null - for throwing, and for
        /// anything that cares what the player has in hand.
        public Grabbable LeftHeld => _left.grabbable;

        /// What the right hand is carrying, or null.
        public Grabbable RightHeld => _right.grabbable;

        /// How strongly the left hand should keep showing its snap's finger
        /// pose (the prop's hold pose) regardless of the snap's own weight:
        /// 1 while a prop is in the hand, easing to 0 after a drop. The snap
        /// only covers the reach - its weight falls again as the hand comes
        /// back - so PlayerHandAnimation takes the larger of the two.
        public float LeftPoseWeight => _left.poseWeight;

        /// The right hand's version of LeftPoseWeight.
        public float RightPoseWeight => _right.poseWeight;

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
            characterController = GetComponentInParent<CharacterController>();
            playerHandInteraction = GetComponent<PlayerHandInteraction>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
        }

        private void Awake()
        {
            _heldLayer = LayerMask.NameToLayer(HeldLayerName);

            if (playerTracking == null) {
                playerTracking = GetComponentInParent<PlayerTracking>();
            }

            if (characterController == null) {
                characterController = GetComponentInParent<CharacterController>();
            }

            _rig = characterController.transform;

            // The visuals are read here, while they're still children of
            // their controllers - PlayerHandVisuals exposes them.
            _left = new HandHold { visual = playerHandVisuals.LeftHandVisual, isLeftHand = true };
            _right = new HandHold { visual = playerHandVisuals.RightHandVisual, isLeftHand = false };
        }

        private void OnDisable()
        {
            // The scene is being unloaded (a level restart): everything in
            // it is on its way out, and objects can't be re-parented while
            // they're being destroyed.
            if (!gameObject.scene.isLoaded) {
                return;
            }

            // Nothing may be left kinematic on the hands' layer, or under a
            // hand, with no hand carrying it.
            Drop(_left, false);
            Drop(_right, false);
            FinishRelease(_left);
            FinishRelease(_right);
        }

        /// <summary>
        /// Picks up and drops, for both hands. Called by PlayerController
        /// after PlayerClimbing.Tick(), so it sees this frame's climbing
        /// grips.
        /// </summary>
        public void Tick()
        {
            TickHand(
                _left,
                playerInput.IsLeftGrabbing,
                playerClimbing.IsLeftHandGripping,
                playerHandInteraction.LeftTarget,
                playerHandInteraction.LeftTargetPoint);

            TickHand(
                _right,
                playerInput.IsRightGrabbing,
                playerClimbing.IsRightHandGripping,
                playerHandInteraction.RightTarget,
                playerHandInteraction.RightTargetPoint);
        }

        /// <summary>
        /// One hand: drop or throw what it's carrying if grip was let go,
        /// otherwise pick up the prop its ray is on if grip is held and the
        /// hand is free.
        /// </summary>
        private void TickHand(HandHold hold, bool isGrabbing, bool isClimbing, IHandTarget rayTarget, Vector3 rayTargetPoint)
        {
            if (hold.grabbable is not null) {
                if (!isGrabbing) {
                    Drop(hold, true);
                }

                return;
            }

            if (!isGrabbing || isClimbing) {
                return;
            }

            // The ray target is only an IHandTarget; a type pattern checks
            // whether it's a prop (as PlayerClimbing does for climbables).
            // One already in the other hand can't be targeted at all - it's
            // off the layers hand rays hit - but check anyway.
            if (rayTarget is not Grabbable grabbable || grabbable.IsHeld) {
                return;
            }

            PickUp(hold, grabbable, rayTargetPoint);
        }

        /// <summary>
        /// This hand's snap, owned by PlayerHandVisuals. Looked up when
        /// needed rather than kept from Awake(): PlayerHandVisuals makes
        /// them in its own Awake(), which may run after this class's.
        /// </summary>
        private HandVisualSnap SnapFor(HandHold hold)
        {
            return hold.isLeftHand ? playerHandVisuals.LeftVisualSnap : playerHandVisuals.RightVisualSnap;
        }

        /// <summary>
        /// Starts carrying grabbable in this hand: the prop says where the
        /// hand would have to be to hold it as it lies (GetSnapPose), and
        /// the hand visual is sent out to that pose. The prop stays where it
        /// is until the hand arrives (see FinishReach()).
        /// </summary>
        private void PickUp(HandHold hold, Grabbable grabbable, Vector3 grabPoint)
        {
            // Whatever this hand dropped a moment ago is done with: one prop
            // at a time waits on a hand.
            FinishRelease(hold);

            // The head position tells a cylinder (a bottle) which side the
            // player is on, so the hand takes it from that side.
            HandSnapPose handPose = grabbable.GetSnapPose(hold.isLeftHand, grabPoint, playerTracking.HeadPosition, default);
            Transform prop = grabbable.transform;
            prop.GetPositionAndRotation(out Vector3 propPosition, out Quaternion propRotation);

            hold.grabbable = grabbable;
            hold.isReaching = true;
            hold.originalParent = prop.parent;
            hold.originalScale = prop.localScale;

            // Kinematic from now, so the prop can't be knocked away or roll
            // off while the hand is on its way to it.
            grabbable.BeginHold(_heldLayer);
            SnapFor(hold).Snap(handPose, reachDuration);

            // The hand's collision now covers the prop too: a ball round
            // the prop's middle, given where that will be relative to the
            // hand. "Relative to the hand": undo the hand pose's rotation,
            // so the offset is in the hand's own axes and turns with it.
            Quaternion toHand = Quaternion.Inverse(handPose.Rotation);
            Vector3 centre = propPosition + propRotation * grabbable.LocalCentre;
            Vector3 centreFromHand = toHand * (centre - handPose.Position);
            playerHandVisuals.SetHeldShape(hold.isLeftHand, centreFromHand, grabbable.HoldRadius);
        }

        /// <summary>
        /// Lets go of whatever this hand is carrying (nothing happens if
        /// it's empty): the prop goes back under physics and the hand's
        /// collision goes back to just the hand. With canThrow, a prop the
        /// hand was moving fast enough leaves with that movement (see
        /// TryGetThrow()); otherwise it starts from rest. A hand still
        /// reaching for the prop goes back to the controller without it.
        /// </summary>
        private void Drop(HandHold hold, bool canThrow)
        {
            if (hold == null || hold.grabbable is null) {
                return;
            }

            // Unity's == null is true for a prop destroyed while carried.
            if (hold.grabbable != null) {
                // Off the hand first (if it ever got there), keeping its
                // world pose. Its scale is set back outright: a trip through
                // a mirrored parent can leave it very slightly off.
                if (!hold.isReaching) {
                    Transform prop = hold.grabbable.transform;
                    prop.SetParent(hold.originalParent, true);
                    prop.localScale = hold.originalScale;
                }

                // From rest unless this is a throw. (The out values are
                // only used when TryGetThrow() returns true - "&&" doesn't
                // call it at all when canThrow is false.)
                Vector3 velocity = Vector3.zero;
                Vector3 angularVelocity = Vector3.zero;

                if (canThrow && TryGetThrow(hold, out Vector3 throwVelocity, out Vector3 throwSpin)) {
                    velocity = throwVelocity;
                    angularVelocity = throwSpin;
                }

                hold.grabbable.EndHold(velocity, angularVelocity);

                // Still on the hands' layer: the hand is wrapped round it,
                // and would be pushed off it by its own collision the moment
                // it could touch it. TickReleased() puts it back.
                hold.released = hold.grabbable;
                hold.releasedTime = 0f;
            }

            hold.originalParent = null;

            hold.grabbable = null;
            hold.sampleCount = 0;
            playerHandVisuals.ClearHeldShape(hold.isLeftHand);

            if (hold.isReaching) {
                hold.isReaching = false;
                SnapFor(hold).Release(returnDuration);
            }
        }

        /// <summary>
        /// Finishes any reach that has arrived, and notes where each
        /// carried prop is for a later throw. Called by PlayerController
        /// straight after PlayerHandVisuals.Tick(), every frame - also
        /// during a mantle and once the level has ended, so a reach under
        /// way then still ends with the prop in the hand.
        /// </summary>
        public void TickHeld()
        {
            FinishReach(_left);
            FinishReach(_right);
            TickThrowSamples(_left);
            TickThrowSamples(_right);
            TickPoseWeight(_left);
            TickPoseWeight(_right);
            TickReleased(_left);
            TickReleased(_right);
        }

        /// <summary>
        /// Watches the prop this hand last dropped, and puts it back on its
        /// own layer once it has fallen clear of the hand - or after
        /// releaseTimeout if it never does.
        /// </summary>
        private void TickReleased(HandHold hold)
        {
            if (hold.released is null) {
                return;
            }

            // Destroyed since it was dropped (Unity's == null).
            if (hold.released == null) {
                hold.released = null;
                return;
            }

            hold.releasedTime += Time.deltaTime;

            // Compared as squared distances - no square root needed.
            float clearDistance = hold.released.HoldRadius + releaseClearance;
            float sqrDistance = (hold.released.WorldCentre - hold.visual.position).sqrMagnitude;

            if (hold.releasedTime >= releaseTimeout || sqrDistance > clearDistance * clearDistance) {
                FinishRelease(hold);
            }
        }

        /// <summary>
        /// Puts the prop this hand last dropped back on its own layer now,
        /// if it's still waiting to be.
        /// </summary>
        private void FinishRelease(HandHold hold)
        {
            if (hold == null || hold.released is null) {
                return;
            }

            if (hold.released != null) {
                hold.released.RestoreLayers();
            }

            hold.released = null;
        }

        /// <summary>
        /// Keeps the hold finger pose fully on while a prop is in the hand
        /// (after the reach - the snap's weight brings it in on the way
        /// out), and eases it off once the prop is dropped.
        /// </summary>
        private void TickPoseWeight(HandHold hold)
        {
            if (hold.grabbable is not null && !hold.isReaching) {
                hold.poseWeight = 1f;
                return;
            }

            if (hold.poseWeight > 0f) {
                float step = poseReleaseDuration > 0f ? Time.deltaTime / poseReleaseDuration : 1f;
                hold.poseWeight = Mathf.MoveTowards(hold.poseWeight, 0f, step);
            }
        }

        /// <summary>
        /// Once the hand visual is fully on the prop (snap weight 1), makes
        /// the prop a child of the visual - keeping its world pose, so it
        /// doesn't move - and lets the snap go: the visual blends back to
        /// the controller and the prop comes with it.
        /// </summary>
        private void FinishReach(HandHold hold)
        {
            if (hold.grabbable is null) {
                return;
            }

            // Destroyed while carried (Unity's == null): just let go.
            if (hold.grabbable == null) {
                Drop(hold, false);
                return;
            }

            if (!hold.isReaching) {
                return;
            }

            HandVisualSnap snap = SnapFor(hold);

            if (snap.Weight < 1f) {
                return;
            }

            hold.isReaching = false;

            // Where the prop's middle is in the hand, for measuring a
            // throw. Read before parenting: under the mirrored right hand
            // the prop's own rotation can no longer be trusted, but a
            // point in the visual's space is right for either hand.
            hold.centreInVisual = hold.visual.InverseTransformPoint(hold.grabbable.WorldCentre);

            // The prop leaves where it lay now: anything resting on it
            // falls, and from here it pushes nothing.
            hold.grabbable.BeginCarry();
            hold.grabbable.transform.SetParent(hold.visual, true);
            snap.Release(returnDuration);
        }

        /// <summary>
        /// Notes where this hand's carried prop is this frame, so a throw
        /// can be worked out from its recent movement when it's let go.
        ///
        /// Measured relative to the rig, not in the world: a snap turn
        /// swings the hand a metre sideways in one frame, which in the
        /// world would look like a huge throw. Within the rig only the
        /// hand's own movement shows; the body's speed is added back at
        /// the throw. It's still the VISUAL hand that's measured, so a
        /// hand held against a wall throws nothing.
        /// </summary>
        private void TickThrowSamples(HandHold hold)
        {
            // Nothing to measure until the prop is in the hand and the hand
            // is back at the controller: the trip back from where the prop
            // lay is the snap's movement, not the player's, and letting go
            // during it would throw the prop at the player.
            if (hold.grabbable is null || hold.isReaching || SnapFor(hold).Weight > 0f) {
                hold.sampleCount = 0;
                return;
            }

            // Into the rig's space: take away its position, then undo its
            // rotation (the rig is never scaled).
            Quaternion toRig = Quaternion.Inverse(_rig.rotation);
            Vector3 centre = hold.visual.TransformPoint(hold.centreInVisual);
            int slot = hold.sampleNext;

            hold.samplePositions[slot] = toRig * (centre - _rig.position);
            hold.sampleRotations[slot] = toRig * hold.visual.rotation;
            hold.sampleTimes[slot] = Time.time;

            // % wraps the slot number back to 0 after the last one.
            hold.sampleNext = (slot + 1) % SampleCapacity;
            hold.sampleCount = Mathf.Min(hold.sampleCount + 1, SampleCapacity);
        }

        /// <summary>
        /// Works out the velocity and spin this hand's prop should leave
        /// with, from its movement over the last throwSampleWindow seconds.
        /// False if that was too slow to be a throw (or there's nothing to
        /// go on), and the prop should just drop.
        ///
        /// Speed is distance over time between the newest sample and the
        /// oldest one inside the window, so it's an average over several
        /// frames - one frame of tracking noise can't decide the throw.
        /// </summary>
        private bool TryGetThrow(HandHold hold, out Vector3 velocity, out Vector3 angularVelocity)
        {
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;

            if (hold.sampleCount < 2) {
                return false;
            }

            // Walk back from the newest sample to the oldest still inside
            // the window. Adding SampleCapacity before the % keeps the slot
            // number positive when it steps back past 0.
            int newest = (hold.sampleNext - 1 + SampleCapacity) % SampleCapacity;
            float newestTime = hold.sampleTimes[newest];
            int oldest = newest;

            for (int back = 1; back < hold.sampleCount; back++) {
                int slot = (newest - back + SampleCapacity) % SampleCapacity;

                if (newestTime - hold.sampleTimes[slot] > throwSampleWindow) {
                    break;
                }

                oldest = slot;
            }

            float elapsed = newestTime - hold.sampleTimes[oldest];

            if (elapsed <= 0f) {
                return false;
            }

            // The hand's part, still in the rig's space.
            Vector3 handVelocity = (hold.samplePositions[newest] - hold.samplePositions[oldest]) / elapsed;

            if (handVelocity.sqrMagnitude < throwMinSpeed * throwMinSpeed) {
                return false;
            }

            // Spin: the turn that takes the old rotation to the new one,
            // as an angle about an axis, over the same time. ToAngleAxis
            // can give the long way round (over 180 degrees); the short way
            // is the same turn less a full circle.
            Quaternion turn = hold.sampleRotations[newest] * Quaternion.Inverse(hold.sampleRotations[oldest]);
            turn.ToAngleAxis(out float angle, out Vector3 axis);

            if (angle > 180f) {
                angle -= 360f;
            }

            Vector3 handSpin = axis * (angle * Mathf.Deg2Rad / elapsed);

            // Back into the world, with the body's own movement added: a
            // prop thrown while running leaves faster, as it would.
            velocity = _rig.rotation * handVelocity * throwStrength + characterController.velocity;
            velocity = Vector3.ClampMagnitude(velocity, throwMaxSpeed);
            angularVelocity = _rig.rotation * handSpin;

            return true;
        }
    }
}
