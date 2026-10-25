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
    /// - On the back of one hand, where they live: the left, or the right
    ///   for a left-handed player (PlayerInputXR.IsLeftHanded).
    /// - In the other hand: gripped while that hand was at them, and
    ///   carried for as long as grip is held. Let go of away from a lock,
    ///   they burn away in that hand and back into being on the hand that
    ///   wears them (ToolBurn).
    /// - In a lock: brought close to a locked simple lock
    ///   (Interaction.PickableLock), they burn away and the
    ///   carrying hand is freed. The big lock's two large picks then burn
    ///   into being in the real lock's keyhole (ToolBurn), standing in
    ///   for them.
    ///
    /// While the picks are in a lock, each hand can grip its own large
    /// pick (left hand the left pick, right hand the right) and turn it by
    /// carrying the hand round the keyhole. The hand visual snaps onto the
    /// pick, as it does onto a door handle. Walking away, or the lock
    /// opening, burns the large picks away where they are and the picks
    /// back into being on the hand that wears them. The lock itself is never
    /// mirrored: whichever hand wears the picks, the left hand works the
    /// left pick and the right hand the right one.
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
            OnHand,
            InHand,
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

        // Optional: the big lock's picks burn into being when the picks go
        // in, and away when the picking ends. Found in Awake() if not wired
        // (it's on this same object); none = it appears and goes at once.
        [SerializeField] private ToolBurn toolBurn;

        [Header("The Picks")]

        // The lockpicks model (Assets/TripoModels/lockpicks/lockpicks.fbx):
        // the hook and the tension wrench side by side, their tips at its
        // origin and their handles along its Z, 1 long. The big lock's
        // picks are large copies of the same two.
        [SerializeField] private GameObject picksModel;

        // How long the picks are, tip to handle end, in metres: the model
        // is scaled to it.
        [SerializeField] private float pickLength = 0.16875f;

        // Where the picks sit on the back of the hand that wears them: the
        // hand's offset from their origin (the tips, Z to the handles),
        // per hand (posed with TeaLeaf > Modify Prefab > Pose with Hand).
        // Its finger pose isn't played: the wearing hand keeps its input
        // curl. Empty: the picks' origin on the hand's.
        [SerializeField] private HandSnapProfile wornProfile;

        // Where the other hand holds the picks while it carries them, from
        // their origin (the tips), and how its fingers close round them
        // (posed with TeaLeaf > Modify Prefab > Pose with Hand). Empty:
        // the picks' origin on the hand's.
        [SerializeField] private HandSnapProfile carryProfile;

        // Where a hand sits on its pick on the big lock, from the pick's
        // grip frame (BigLock.GetGripFrame()), and its fingers (also
        // posed with Pose with Hand). Empty: the hand on the grip frame,
        // fingers keeping their input curl.
        [SerializeField] private HandSnapProfile pickProfile;

        // The other hand takes the picks from within this many metres of
        // them.
        [SerializeField] private float takeDistance = 0.12f;

        [Header("Locks")]

        // The carried picks go into a lock from within this many metres
        // of it.
        [SerializeField] private float insertDistance = 0.15f;

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

        // The picks going into a lock (the hand that carried them).
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

        // The picks: an object whose origin and axes are the model's (the
        // carry profile's grip frame), the model scaled under it.
        private Transform _picks;

        private PicksPlace _place = PicksPlace.OnHand;

        // The hand that takes the picks off the other one and carries
        // them: the right, unless the picks are worn on the right hand.
        private PickHand _taker;

        // The lock the picks are in, while they're in one, and the keyhole
        // it's picked through (null if it has none).
        private PickableLock _lock;
        private DoorKeyhole _keyhole;

        // The taking hand's grip as it was last frame: the picks are taken
        // on the frame grip is pressed, not by a hand that arrives already
        // gripping.
        private bool _wasTakerGrabbing;

        // Set when the big lock says it has opened (from inside
        // TickHeld()), and acted on at the end of that TickHeld().
        private bool _hasJustUnlocked;

        // Counts down to the next pulse while the left hand feels the pin.
        private float _pinPulseTimer;

        // The big lock's two picks as ToolBurn knows them. Null without a
        // ToolBurn.
        private ToolBurn.Target _leftPickBurn;
        private ToolBurn.Target _rightPickBurn;

        // The worn picks' renderers (one per pick in the model) as
        // ToolBurn knows them: let go of in the hand, they burn away there
        // and back in on the wearing hand. Empty without a ToolBurn.
        private ToolBurn.Target[] _picksBurns = System.Array.Empty<ToolBurn.Target>();

        /// True while the left hand carries the picks or is on a pick -
        /// and, after being let go by force, until its grip is released.
        /// The other hand systems leave the hand alone meanwhile.
        public bool IsLeftBusy => IsCarrying(_left) || _left.isOnPick || _left.needsRegrip;

        /// The same for the right hand.
        public bool IsRightBusy => IsCarrying(_right) || _right.isOnPick || _right.needsRegrip;

        /// <summary>
        /// Whose finger pose a hand plays for the picks in it: the carry
        /// profile's while that hand carries them, else null. Read by
        /// PlayerHandAnimation each frame. (A hand on a big lock pick is
        /// snapped, and plays the pick profile through its snap.)
        /// </summary>
        public HandSnapProfile GetHeldProfile(bool isLeftHand)
        {
            return IsCarrying(isLeftHand ? _left : _right) ? carryProfile : null;
        }

        /// <summary>
        /// Whether that hand is the one carrying the picks.
        /// </summary>
        private bool IsCarrying(PickHand hand)
        {
            return _place == PicksPlace.InHand && hand == _taker;
        }

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

            if (toolBurn == null) {
                toolBurn = GetComponent<ToolBurn>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasHaptics = playerHaptics != null;

            _left = new PickHand { isLeftHand = true };
            _right = new PickHand { isLeftHand = false };

            // A right-handed player wears the picks on the left hand and
            // takes them with the right; a left-handed one the other way
            // round. The lock itself is the same either way.
            _taker = playerInput.IsLeftHanded ? _left : _right;

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
        /// Makes the picks, gives the big lock its large copies of them,
        /// then registers its two renderers and the worn picks' with
        /// ToolBurn (after the model, so the burn materials copy its
        /// textures; ToolBurn finds its shader in its own Awake()).
        /// Start() rather than Awake(): they go on the left hand visual,
        /// and PlayerHandVisuals copies each hand visual in its own
        /// Awake() to make the ghost hands - picks already on the hand by
        /// then would be copied onto the ghost too. Every Awake() has run
        /// before the first Start(), the big lock's included.
        /// </summary>
        private void Start()
        {
            BuildPicks();

            if (picksModel != null) {
                bigLock.SetPickModel(picksModel);
            }

            if (toolBurn != null) {
                _leftPickBurn = toolBurn.Register(bigLock.LeftPickRenderer);
                _rightPickBurn = toolBurn.Register(bigLock.RightPickRenderer);

                Renderer[] picksRenderers = _picks.GetComponentsInChildren<Renderer>(true);
                _picksBurns = new ToolBurn.Target[picksRenderers.Length];

                for (int i = 0; i < picksRenderers.Length; i++) {
                    _picksBurns[i] = toolBurn.Register(picksRenderers[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if (bigLock != null) {
                bigLock.StopReached -= OnStopReached;
                bigLock.PinSet -= OnPinSet;
                bigLock.Unlocked -= OnUnlocked;
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
            } else if (_place == PicksPlace.InHand) {
                ReturnPicks();
            }
        }

        /// <summary>
        /// Makes the picks, once: the model scaled to pickLength under an
        /// object of its own, which is what's placed - its origin is the
        /// picks' tips, its Z runs to their handles - on the back of the
        /// hand that wears them. No collider: they're only to look at.
        /// With no model set there's still an object (nothing to see), so
        /// the picking works.
        /// </summary>
        private void BuildPicks()
        {
            GameObject picks = new("Lockpicks");

            if (picksModel != null) {
                Transform model = Instantiate(picksModel, picks.transform, false).transform;
                model.localScale = Vector3.one * pickLength;

                foreach (Renderer modelRenderer in model.GetComponentsInChildren<Renderer>()) {
                    modelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    modelRenderer.receiveShadows = false;
                }
            } else {
                Debug.LogWarning("PlayerLockpicking: no lockpicks model set, so the picks can't be seen.", this);
            }

            _picks = picks.transform;
            ReturnPicks();
        }

        /// <summary>
        /// Makes the picks a child of parent at a pose in parent's own
        /// space. The scale is set too: moving between the right hand
        /// (which is mirrored) and anything else would otherwise leave
        /// them mirrored.
        /// </summary>
        /// <summary>
        /// Puts the picks back on the back of the hand that wears them -
        /// the one that isn't the taker - where the worn profile puts them
        /// (never drawn mirrored), and in sight again if they were in a
        /// lock.
        /// </summary>
        private void ReturnPicks()
        {
            _place = PicksPlace.OnHand;
            _picks.gameObject.SetActive(true);
            PickHand wearer = _taker.isLeftHand ? _right : _left;
            HandSnapProfile.PlaceInHand(wornProfile, wearer.isLeftHand, _picks, VisualFor(wearer), transform);
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

            bool isTakerGrabbing = _taker.isLeftHand ? isLeftGrabbing : isRightGrabbing;

            switch (_place) {
                case PicksPlace.OnHand:
                    TickPicksOnHand(isTakerGrabbing);
                    break;

                case PicksPlace.InHand:
                    TickPicksCarried(isTakerGrabbing);
                    break;

                case PicksPlace.InLock:
                    TickHandAtBigLock(_left, isLeftGrabbing);
                    TickHandAtBigLock(_right, isRightGrabbing);
                    break;
            }

            _wasTakerGrabbing = isTakerGrabbing;
        }

        /// <summary>
        /// The picks are on the hand that wears them: the other hand takes
        /// them if grip is pressed while it's free and within reach of them.
        /// </summary>
        private void TickPicksOnHand(bool isTakerGrabbing)
        {
            // The middle of the picks, half way along them.
            Vector3 picksCentre = _picks.position + _picks.forward * (pickLength * 0.5f);

            bool isInReach = IsFree(_taker) && IsWithin(HandPoint(_taker), picksCentre, takeDistance);
            NoteReach(_taker, isInReach);

            if (isInReach && isTakerGrabbing && !_wasTakerGrabbing) {
                _place = PicksPlace.InHand;

                // A child of the hand visual where the profile puts them,
                // never drawn mirrored.
                HandSnapProfile.PlaceInHand(carryProfile, _taker.isLeftHand, _picks, VisualFor(_taker), transform);
            }
        }

        /// <summary>
        /// A hand is carrying the picks: let go of, they burn away in that
        /// hand and back into being on the hand that wears them; brought
        /// close to a lock, they go into it. A distance check per lock in
        /// the level, and only while the picks are in hand.
        /// </summary>
        private void TickPicksCarried(bool isTakerGrabbing)
        {
            if (!isTakerGrabbing) {
                BurnPicks(false);
                ReturnPicks();
                BurnPicks(true);
                return;
            }

            PickableLock found = PickableLock.FindInRange(_picks.position, insertDistance);

            if (found != null) {
                Insert(found);
            }
        }

        /// <summary>
        /// Puts the picks into found: they burn away where they are, the
        /// carrying hand is freed (and has to let go of grip before it can
        /// take a pick), and the big lock's two large picks burn into being
        /// in the lock's keyhole, on the face the player is on.
        /// </summary>
        private void Insert(PickableLock found)
        {
            found.GetFace(playerTracking.HeadPosition, out Vector3 facePoint, out Vector3 outward);

            _lock = found;
            _place = PicksPlace.InLock;
            _taker.needsRegrip = true;
            _left.wasInReach = false;
            _right.wasInReach = false;
            found.BeginPicking();

            // Burnt away where they are (on the carrying hand, at the lock),
            // and hidden there until ReturnPicks() puts them back on the
            // wearing hand.
            BurnPicks(false);
            _picks.gameObject.SetActive(false);

            // The keyhole stops being an opening and is drawn black on the
            // face instead, so it hides the picks' tips inside it.
            _keyhole = DoorKeyhole.ForLock(found);

            if (_keyhole != null) {
                _keyhole.BeginPicking(facePoint);
            }

            // The large picks in the real keyhole, facing into the door.
            bigLock.Show(found, facePoint, Quaternion.LookRotation(-outward, found.transform.up));
            BurnBigLock(true);
            Pulse(_taker.isLeftHand, insertAmplitude, insertDuration);
        }

        /// <summary>
        /// Burns the big lock's two picks into being (just shown) or away
        /// (call just before it's hidden, while it's still showing), if
        /// the rig has a ToolBurn.
        /// </summary>
        private void BurnBigLock(bool isIn)
        {
            if (toolBurn == null) {
                return;
            }

            if (isIn) {
                toolBurn.BurnIn(_leftPickBurn);
                toolBurn.BurnIn(_rightPickBurn);
            } else {
                toolBurn.BurnAway(_leftPickBurn);
                toolBurn.BurnAway(_rightPickBurn);
            }
        }

        /// <summary>
        /// Burns the worn picks away where they are (call before they're
        /// moved) or into being where they now are (call after), if the
        /// rig has a ToolBurn. Burning in leaves the copy burning away in
        /// the hand that let go of them.
        /// </summary>
        private void BurnPicks(bool isIn)
        {
            for (int i = 0; i < _picksBurns.Length; i++) {
                if (isIn) {
                    toolBurn.BurnIn(_picksBurns[i], true);
                } else {
                    toolBurn.BurnAway(_picksBurns[i]);
                }
            }
        }

        /// <summary>
        /// Where a hand on its own pick on the big lock goes, as the pick
        /// is now: the pick profile's offsets from the pick's grip frame,
        /// with its finger pose (none: the bare grip frame).
        /// </summary>
        private HandSnapPose PickSnapPose(PickHand hand)
        {
            bigLock.GetGripFrame(hand.isLeftHand, out Vector3 position, out Quaternion rotation);

            return pickProfile != null
                ? pickProfile.Apply(hand.isLeftHand, position, rotation)
                : new HandSnapPose(position, rotation, null);
        }

        /// <summary>
        /// A hand's visual (the hand model).
        /// </summary>
        private Transform VisualFor(PickHand hand)
        {
            return hand.isLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;
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
                SnapFor(hand).Snap(PickSnapPose(hand));
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
        /// Ends the picking, opened or not: both hands come off, the large
        /// picks burn away in the keyhole and the picks burn back into being
        /// on the hand that wears them.
        /// </summary>
        private void StopPicking()
        {
            LetGo(_left, true);
            LetGo(_right, true);
            BurnBigLock(false);
            bigLock.Hide();

            // The keyhole stays shut while the large picks burn away in it,
            // then opens again as usual.
            if (_keyhole != null) {
                _keyhole.EndPicking(toolBurn != null ? toolBurn.Duration : 0f);
                _keyhole = null;
            }

            // Unity's == null is true for a lock destroyed while picked.
            if (_lock != null) {
                _lock.EndPicking();
            }

            _lock = null;
            _pinPulseTimer = 0f;
            ReturnPicks();
            BurnPicks(true);
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

            HandSnapPose pose = PickSnapPose(hand);
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
