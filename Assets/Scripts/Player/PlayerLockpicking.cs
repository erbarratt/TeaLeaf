using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The player's side of lockpicking: the two picks and the hands that
    /// work them. How a lock is actually picked - the stages, the pins -
    /// is the big lock's business (Interaction.BigLock).
    ///
    /// The two picks are one small object, always in one of three places:
    /// - On the back of the left hand, where they live.
    /// - In the right hand: gripped while the right hand was at them, and
    ///   carried for as long as grip is held. Let go of away from a lock,
    ///   they go back to the left hand.
    /// - In a lock: brought close to a locked simple lock
    ///   (Interaction.PickableLock), they snap into its keyhole and the
    ///   right hand is freed. The big lock then fades in in front of the
    ///   door.
    ///
    /// While the picks are in a lock, each hand can grip its own pick on
    /// the big lock (left hand the left pick, right hand the right) and
    /// turn it by carrying the hand round the lock's middle. The hand
    /// visual snaps onto the pick, as it does onto a door handle. Walking
    /// away, or the lock opening, fades the big lock out and puts the
    /// picks back on the left hand.
    ///
    /// The picks and the big lock's picks are taken by reaching for them
    /// (a hand close enough, with grip), not by the hand rays: they're on
    /// or just in front of the player's own body. A light tap on the
    /// controller says a hand has come within reach of one.
    ///
    /// A hand does one thing at a time: a hand climbing, carrying a prop
    /// or on a door handle can't take a pick, and those systems leave a
    /// hand that's busy here alone (IsLeftBusy/IsRightBusy).
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// Tick() (take and let go) before climbing, and TickHeld() (turn the
    /// picks, move the snapped hands, run the big lock) after the body has
    /// moved and just before the hand visuals are placed.
    /// </summary>
    public class PlayerLockpicking : MonoBehaviour
    {
        /// Where the two picks are.
        private enum PicksPlace
        {
            OnLeftHand,
            InRightHand,
            InLock
        }

        /// One hand's state. A class, made once per hand in Awake(), so
        /// the methods below can change it without ref parameters.
        private class PickHand
        {
            public bool isLeftHand;

            // True while this hand grips its pick on the big lock.
            public bool isOnPick;

            // True after the hand was let go by force (or has just put the
            // picks into a lock), until grip is released: grip is still
            // held, and would take a pick straight away.
            public bool needsRegrip;

            // Whether the hand was within reach of something it could
            // take last frame, so the tap is felt once on coming into
            // reach, not every frame.
            public bool wasInReach;
        }

        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // What each hand is busy with: a hand climbing, carrying a prop,
        // on a door handle or using the keys can't take a pick. Found
        // (or made) in Awake() if not wired (it's on this same object).
        [SerializeField] private PlayerHandState playerHandState;

        // Optional: a rig with no haptics still picks locks.
        [SerializeField] private PlayerHaptics playerHaptics;

        // The big lock in the scene. Optional: found in Awake() if not
        // wired, and made with its default settings if there is none.
        [SerializeField] private BigLock bigLock;

        [Header("The Picks")]

        // Each pick: a long thin box, in metres, and how far apart the two
        // lie.
        [SerializeField] private float pickLength = 0.09f;
        [SerializeField] private float pickThickness = 0.004f;
        [SerializeField] private float pickSpacing = 0.012f;
        [SerializeField] private Color pickColor = new(0.75f, 0.77f, 0.8f, 1f);

        // The least light the picks are ever shown in, 0-1.
        [SerializeField] private float minLight = 0.35f;

        // Where the picks sit on the back of the left hand, in the left
        // hand visual's own space (its fingers point along -Y, the back of
        // the hand is -X, the thumb side +Z). The picks' own Z runs from
        // their tips to their handles.
        [SerializeField] private Vector3 onHandPosition = new(-0.022f, -0.09f, 0f);
        [SerializeField] private Vector3 onHandRotation = new(-90f, 0f, 90f);

        // Where they sit in the right hand while it carries them, in the
        // right hand visual's own space (the same axes, mirrored).
        [SerializeField] private Vector3 inHandPosition = new(0.02f, -0.17f, 0.02f);
        [SerializeField] private Vector3 inHandRotation = new(-90f, 0f, 90f);

        // The right hand takes the picks from within this many metres of
        // them.
        [SerializeField] private float takeDistance = 0.12f;

        [Header("Locks")]

        // The carried picks go into a lock from within this many metres
        // of it, and sit this deep in its keyhole.
        [SerializeField] private float insertDistance = 0.15f;
        [SerializeField] private float insertDepth = 0.02f;

        // Where the big lock appears: this far out from the real lock's
        // face, and this far below the head (never lower than the real
        // lock), in metres.
        [SerializeField] private float bigLockDistance = 0.2f;
        [SerializeField] private float bigLockBelowHead = 0.3f;

        // Further than this from the lock (metres, measured level from
        // the head), the picking is given up.
        [SerializeField] private float leaveDistance = 1.5f;

        [Header("Hands On The Big Lock")]

        // A hand takes its pick from within this many metres of where the
        // pick is gripped, and is let go when the real hand is further
        // than breakDistance from it.
        [SerializeField] private float pickReach = 0.1f;
        [SerializeField] private float breakDistance = 0.3f;

        [Header("Haptics")]

        // A hand coming within reach of something it can take.
        [SerializeField] private float reachAmplitude = 0.15f;
        [SerializeField] private float reachDuration = 0.03f;

        // The picks going into a lock (right hand).
        [SerializeField] private float insertAmplitude = 0.5f;
        [SerializeField] private float insertDuration = 0.06f;

        // The right pick reaching a stop (right hand).
        [SerializeField] private float stopAmplitude = 0.6f;
        [SerializeField] private float stopDuration = 0.06f;

        // A pin being set (left hand).
        [SerializeField] private float pinSetAmplitude = 0.9f;
        [SerializeField] private float pinSetDuration = 0.12f;

        // The lock opening (both hands).
        [SerializeField] private float unlockAmplitude = 1f;
        [SerializeField] private float unlockDuration = 0.2f;

        // The left hand feeling for the pin: from the weakest, where it
        // can first be felt, to the strongest, on it. Sent as a run of
        // short pulses, one every pinPulseInterval seconds.
        [SerializeField] private float pinMinAmplitude = 0.08f;
        [SerializeField] private float pinMaxAmplitude = 0.7f;
        [SerializeField] private float pinPulseInterval = 0.05f;

        private PickHand _left;
        private PickHand _right;

        private bool _hasHaptics;

        // The picks: one object with a mesh of two thin boxes.
        private Transform _picks;
        private Mesh _picksMesh;
        private Material _picksMaterial;

        private PicksPlace _place = PicksPlace.OnLeftHand;

        // The lock the picks are in, while they're in one.
        private PickableLock _lock;

        // Right grip as it was last frame: the picks are taken on the
        // frame grip is pressed, not by a hand that arrives already
        // gripping.
        private bool _wasRightGrabbing;

        // Set when the big lock says it has opened (from inside
        // TickHeld()), and acted on at the end of that TickHeld().
        private bool _hasJustUnlocked;

        // Counts down to the next pulse while the left hand feels the pin.
        private float _pinPulseTimer;

        /// True while the left hand is on a pick - and, after being let go
        /// by force, until its grip is released. Climbing, carrying and
        /// doors leave the hand alone meanwhile.
        public bool IsLeftBusy => _left.isOnPick || _left.needsRegrip;

        /// True while the right hand carries the picks or is on a pick (as
        /// IsLeftBusy).
        public bool IsRightBusy => _place == PicksPlace.InRightHand || _right.isOnPick || _right.needsRegrip;

        /// <summary>
        /// Editor-only: fills in the references when the component is
        /// added (on the Hands object; the body systems are on the Player
        /// root above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerTracking = GetComponentInParent<PlayerTracking>();
            playerHaptics = GetComponentInParent<PlayerHaptics>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
        }

        private void Awake()
        {
            if (playerHandState == null) {
                playerHandState = PlayerHandState.GetOrAdd(playerHandVisuals);
            }

            if (playerHaptics == null) {
                playerHaptics = GetComponentInParent<PlayerHaptics>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasHaptics = playerHaptics != null;

            _left = new PickHand { isLeftHand = true };
            _right = new PickHand { isLeftHand = false };

            // One big lock for every lock in the level, made at load. A
            // search of the scene, but only once, at startup.
            if (bigLock == null) {
                bigLock = FindFirstObjectByType<BigLock>();
            }

            if (bigLock == null) {
                bigLock = new GameObject("Big Lock").AddComponent<BigLock>();
            }

            bigLock.StopReached += OnStopReached;
            bigLock.PinSet += OnPinSet;
            bigLock.Unlocked += OnUnlocked;
        }

        /// <summary>
        /// Makes the picks. Start() rather than Awake(): they go on the
        /// left hand visual, and PlayerHandVisuals copies each hand visual
        /// in its own Awake() to make the ghost hands - picks already on
        /// the hand by then would be copied onto the ghost too. Every
        /// Awake() has run before the first Start().
        /// </summary>
        private void Start()
        {
            BuildPicks();
        }

        private void OnDestroy()
        {
            if (bigLock != null) {
                bigLock.StopReached -= OnStopReached;
                bigLock.PinSet -= OnPinSet;
                bigLock.Unlocked -= OnUnlocked;
            }

            // A mesh and a material made from code aren't cleaned up with
            // their object.
            if (_picksMesh != null) {
                Destroy(_picksMesh);
            }

            if (_picksMaterial != null) {
                Destroy(_picksMaterial);
            }
        }

        private void OnDisable()
        {
            // The scene is being unloaded (a level restart): everything in
            // it is on its way out, and objects can't be re-parented while
            // they're being destroyed.
            if (!gameObject.scene.isLoaded || _picks == null) {
                return;
            }

            if (_place == PicksPlace.InLock) {
                StopPicking();
            } else if (_place == PicksPlace.InRightHand) {
                ReturnPicks();
            }
        }

        /// <summary>
        /// Makes the picks, once: two long thin boxes side by side in one
        /// mesh, running from the object's origin (their tips) along its Z
        /// (to their handles), on the back of the left hand. No collider:
        /// they're only to look at.
        /// </summary>
        private void BuildPicks()
        {
            LockMeshBuilder builder = new();
            Vector3 size = new(pickThickness, pickThickness, pickLength);
            float aside = pickSpacing * 0.5f;

            builder.Box(new Vector3(-aside, 0f, pickLength * 0.5f), size, Quaternion.identity, pickColor);
            builder.Box(new Vector3(aside, 0f, pickLength * 0.5f), size, Quaternion.identity, pickColor);

            _picksMesh = builder.ToMesh("Lockpicks");
            _picksMaterial = LockMeshBuilder.CreateMaterial(false, minLight);

            GameObject picks = new("Lockpicks");
            picks.AddComponent<MeshFilter>().sharedMesh = _picksMesh;

            MeshRenderer picksRenderer = picks.AddComponent<MeshRenderer>();
            picksRenderer.sharedMaterial = _picksMaterial;
            picksRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            picksRenderer.receiveShadows = false;

            _picks = picks.transform;
            ReturnPicks();
        }

        /// <summary>
        /// Makes the picks a child of parent at a pose in parent's own
        /// space. The scale is set too: moving between the right hand
        /// (which is mirrored) and anything else would otherwise leave
        /// them mirrored.
        /// </summary>
        private void PlacePicks(Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            _picks.SetParent(parent, false);
            _picks.SetLocalPositionAndRotation(localPosition, localRotation);
            _picks.localScale = Vector3.one;
        }

        /// <summary>
        /// Puts the picks back on the back of the left hand.
        /// </summary>
        private void ReturnPicks()
        {
            _place = PicksPlace.OnLeftHand;
            PlacePicks(playerHandVisuals.LeftHandVisual, onHandPosition, Quaternion.Euler(onHandRotation));
        }

        /// <summary>
        /// Takes and lets go of the picks, for both hands. Called by
        /// PlayerController before PlayerClimbing.Tick(), so a hand that
        /// takes a pick this frame is already busy when climbing, carrying
        /// and doors look at it.
        /// </summary>
        public void Tick()
        {
            bool isLeftGrabbing = playerInput.IsLeftGrabbing;
            bool isRightGrabbing = playerInput.IsRightGrabbing;

            // Grip released - the hand may take a pick again.
            if (!isLeftGrabbing) {
                _left.needsRegrip = false;
            }

            if (!isRightGrabbing) {
                _right.needsRegrip = false;
            }

            switch (_place) {
                case PicksPlace.OnLeftHand:
                    TickPicksOnHand(isRightGrabbing);
                    break;

                case PicksPlace.InRightHand:
                    TickPicksCarried(isRightGrabbing);
                    break;

                case PicksPlace.InLock:
                    TickHandAtBigLock(_left, isLeftGrabbing);
                    TickHandAtBigLock(_right, isRightGrabbing);
                    break;
            }

            _wasRightGrabbing = isRightGrabbing;
        }

        /// <summary>
        /// The picks are on the left hand: the right hand takes them if
        /// grip is pressed while it's free and within reach of them.
        /// </summary>
        private void TickPicksOnHand(bool isRightGrabbing)
        {
            // The middle of the picks, half way along them.
            Vector3 picksCentre = _picks.position + _picks.forward * (pickLength * 0.5f);

            bool isInReach = IsFree(_right) && IsWithin(HandPoint(_right), picksCentre, takeDistance);
            NoteReach(_right, isInReach);

            if (isInReach && isRightGrabbing && !_wasRightGrabbing) {
                _place = PicksPlace.InRightHand;
                PlacePicks(playerHandVisuals.RightHandVisual, inHandPosition, Quaternion.Euler(inHandRotation));
            }
        }

        /// <summary>
        /// The right hand is carrying the picks: they go back to the left
        /// hand if grip is let go, or into a lock they're brought close
        /// to. A distance check per lock in the level, and only while the
        /// picks are in hand.
        /// </summary>
        private void TickPicksCarried(bool isRightGrabbing)
        {
            if (!isRightGrabbing) {
                ReturnPicks();
                return;
            }

            PickableLock found = PickableLock.FindInRange(_picks.position, insertDistance);

            if (found != null) {
                Insert(found);
            }
        }

        /// <summary>
        /// Puts the picks into found: they snap into its keyhole on the
        /// player's side, sticking straight out, the right hand is freed
        /// (and has to let go of grip before it can take a pick), and the
        /// big lock appears in front of the door.
        /// </summary>
        private void Insert(PickableLock found)
        {
            Vector3 headPosition = playerTracking.HeadPosition;
            found.GetFace(headPosition, out Vector3 facePoint, out Vector3 outward);

            _lock = found;
            _place = PicksPlace.InLock;
            _right.needsRegrip = true;
            _left.wasInReach = false;
            _right.wasInReach = false;
            found.BeginPicking();

            // A child of the lock (unscaled), placed in the world: tips
            // just inside the keyhole, handles straight out.
            Transform lockTransform = found.transform;
            PlacePicks(lockTransform, Vector3.zero, Quaternion.identity);
            _picks.SetPositionAndRotation(facePoint - outward * insertDepth, Quaternion.LookRotation(outward, lockTransform.up));

            // The big lock: out from the real one, facing the same way,
            // lifted to a height that's comfortable to work at.
            Vector3 position = facePoint + outward * bigLockDistance;
            position.y = Mathf.Max(facePoint.y, headPosition.y - bigLockBelowHead);

            bigLock.Show(found, position, Quaternion.LookRotation(-outward, lockTransform.up));
            Pulse(false, insertAmplitude, insertDuration);
        }

        /// <summary>
        /// One hand, while the picks are in a lock: lets go of its pick if
        /// grip was released, otherwise takes it if grip is held and the
        /// hand is free and within reach of it.
        /// </summary>
        private void TickHandAtBigLock(PickHand hand, bool isGrabbing)
        {
            if (hand.isOnPick) {
                if (!isGrabbing) {
                    LetGo(hand, false);
                }

                return;
            }

            if (!bigLock.CanBeWorked) {
                return;
            }

            bool isInReach = !hand.needsRegrip
                && IsFree(hand)
                && IsWithin(HandPoint(hand), bigLock.GetGripPoint(hand.isLeftHand), pickReach);

            NoteReach(hand, isInReach);

            if (isInReach && isGrabbing) {
                hand.isOnPick = true;
                bigLock.HoldPick(hand.isLeftHand);
                SnapFor(hand).Snap(bigLock.GetSnapPose(hand.isLeftHand));
            }
        }

        /// <summary>
        /// Takes this hand off its pick (nothing happens if it isn't on
        /// one): the big lock is told, and the hand visual blends back to
        /// the controller. With byForce, the hand can't take a pick again
        /// until its grip has been released.
        /// </summary>
        private void LetGo(PickHand hand, bool byForce)
        {
            if (!hand.isOnPick) {
                return;
            }

            hand.isOnPick = false;
            hand.needsRegrip = byForce;
            hand.wasInReach = false;
            bigLock.ReleasePick(hand.isLeftHand);
            SnapFor(hand).Release();
        }

        /// <summary>
        /// Ends the picking, opened or not: both hands come off, the big
        /// lock fades out and the picks go back to the left hand.
        /// </summary>
        private void StopPicking()
        {
            LetGo(_left, true);
            LetGo(_right, true);
            bigLock.Hide();

            // Unity's == null is true for a lock destroyed while picked.
            if (_lock != null) {
                _lock.EndPicking();
            }

            _lock = null;
            _pinPulseTimer = 0f;
            ReturnPicks();
        }

        /// <summary>
        /// Works the big lock for this frame. Called by PlayerController
        /// after the body has moved and turned and just before
        /// PlayerHandVisuals.Tick() - a pick has to be where the hand has
        /// taken it before the hand visual is placed on it - on every path
        /// through Update(), so the picking is still given up if the body
        /// is carried away by a mantle.
        /// </summary>
        public void TickHeld()
        {
            float deltaTime = Time.deltaTime;

            if (_place == PicksPlace.InLock) {
                if (ShouldStopPicking()) {
                    StopPicking();
                } else {
                    TickHeldHand(_left, playerTracking.LeftHandPosition);
                    TickHeldHand(_right, playerTracking.RightHandPosition);
                }
            }

            // Also while it fades out after the picking has ended.
            if (bigLock.IsActive) {
                bigLock.Tick(deltaTime);
            }

            if (_place == PicksPlace.InLock) {
                TickPinFeel(deltaTime);
            }

            // The lock opened this frame (the big lock said so from inside
            // one of the calls above).
            if (_hasJustUnlocked) {
                _hasJustUnlocked = false;
                Pulse(true, unlockAmplitude, unlockDuration);
                Pulse(false, unlockAmplitude, unlockDuration);
                StopPicking();
            }
        }

        /// <summary>
        /// True if the picking can't go on: the lock has gone or been
        /// opened some other way, or the player has moved away from it.
        /// </summary>
        private bool ShouldStopPicking()
        {
            if (_lock == null || !_lock.IsLocked) {
                return true;
            }

            // Measured level: crouching or standing up at the lock isn't
            // leaving it.
            Vector3 away = playerTracking.HeadPosition - _lock.transform.position;
            away.y = 0f;

            return away.sqrMagnitude > leaveDistance * leaveDistance;
        }

        /// <summary>
        /// One hand on a pick: the pick turns to where the real hand is,
        /// the snapped hand visual moves with it, and the hand is let go
        /// by force if the real hand has strayed too far from the pick.
        /// </summary>
        private void TickHeldHand(PickHand hand, Vector3 controllerPosition)
        {
            if (!hand.isOnPick) {
                return;
            }

            bigLock.TurnPick(hand.isLeftHand, controllerPosition);

            HandSnapPose pose = bigLock.GetSnapPose(hand.isLeftHand);
            SnapFor(hand).SetSnapPose(pose.Position, pose.Rotation);

            if (!IsWithin(controllerPosition, bigLock.GetGripPoint(hand.isLeftHand), breakDistance)) {
                LetGo(hand, true);
            }
        }

        /// <summary>
        /// The left hand feeling for the pin: a run of short pulses,
        /// stronger the nearer the left pick is to it. Pulses rather than
        /// one every frame, since each one sent costs a little (see
        /// PlayerHaptics.Pulse()); each lasts a bit longer than the gap to
        /// the next, so they join into one vibration.
        /// </summary>
        private void TickPinFeel(float deltaTime)
        {
            float nearness = bigLock.PinNearness;

            if (nearness <= 0f) {
                _pinPulseTimer = 0f;
                return;
            }

            _pinPulseTimer -= deltaTime;

            if (_pinPulseTimer > 0f) {
                return;
            }

            _pinPulseTimer = pinPulseInterval;
            Pulse(true, Mathf.Lerp(pinMinAmplitude, pinMaxAmplitude, nearness), pinPulseInterval * 1.5f);
        }

        /// <summary>
        /// The big lock's right pick has reached a stop.
        /// </summary>
        private void OnStopReached()
        {
            Pulse(false, stopAmplitude, stopDuration);
        }

        /// <summary>
        /// A pin has been set.
        /// </summary>
        private void OnPinSet()
        {
            Pulse(true, pinSetAmplitude, pinSetDuration);
        }

        /// <summary>
        /// The lock has opened. Raised from inside TickHeld(), while the
        /// hands are still being worked, so it's only noted here.
        /// </summary>
        private void OnUnlocked()
        {
            _hasJustUnlocked = true;
        }

        /// <summary>
        /// True if no other hand system has the hand (PlayerHandState
        /// knows them all). Last frame's, for climbing, carrying and doors
        /// tick after this class - which is right: a hand that was doing
        /// one of those is still doing it until its own system says
        /// otherwise.
        /// </summary>
        private bool IsFree(PickHand hand)
        {
            return !playerHandState.IsBusyExcept(hand.isLeftHand, HandUse.Lockpicks);
        }

        /// <summary>
        /// Where a hand is for reaching: its controller's pose, moved to
        /// where the hand visual really is when a surface holds it back.
        /// </summary>
        private Vector3 HandPoint(PickHand hand)
        {
            Vector3 position;

            if (hand.isLeftHand) {
                playerHandVisuals.GetLeftHandPose(out position, out _);
            } else {
                playerHandVisuals.GetRightHandPose(out position, out _);
            }

            return position;
        }

        /// <summary>
        /// True if a and b are no more than distance apart. Compared as
        /// squared distances - no square root needed.
        /// </summary>
        private static bool IsWithin(Vector3 a, Vector3 b, float distance)
        {
            return (a - b).sqrMagnitude <= distance * distance;
        }

        /// <summary>
        /// Remembers whether a hand is within reach of something it can
        /// take, and taps its controller on the frame it comes into reach.
        /// </summary>
        private void NoteReach(PickHand hand, bool isInReach)
        {
            if (isInReach && !hand.wasInReach) {
                Pulse(hand.isLeftHand, reachAmplitude, reachDuration);
            }

            hand.wasInReach = isInReach;
        }

        /// <summary>
        /// This hand's snap, owned by PlayerHandVisuals. Looked up when
        /// needed rather than kept from Awake(): PlayerHandVisuals makes
        /// them in its own Awake(), which may run after this class's.
        /// </summary>
        private HandVisualSnap SnapFor(PickHand hand)
        {
            return hand.isLeftHand ? playerHandVisuals.LeftVisualSnap : playerHandVisuals.RightVisualSnap;
        }

        /// <summary>
        /// Buzzes one controller, if the rig has haptics.
        /// </summary>
        private void Pulse(bool isLeftHand, float amplitude, float duration)
        {
            if (_hasHaptics) {
                playerHaptics.Pulse(isLeftHand, amplitude, duration);
            }
        }
    }
}
