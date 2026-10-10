using Interaction;
using Inventory;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The player's side of using keys: taking the keyring
    /// (Inventory.Keyring) out of the pack, carrying it to a keyed lock
    /// (Interaction.KeyLock) and turning the key. Collecting keys is the
    /// pack's business (PlayerPack puts a key let go of at the pack onto
    /// the keyring).
    ///
    /// The keyring is always in one of three places:
    /// - In the pack, in its space, where it lives.
    /// - In a hand: taken the usual way (hand ray on it in the pack,
    ///   grip) by the hand that isn't holding the pack, and carried for
    ///   as long as grip is held. Let go of away from a lock, it goes
    ///   back to the pack.
    /// - In a lock: brought close to a locked keyed lock whose key is on
    ///   the ring, it snaps in and the carrying hand is freed. A lock whose
    ///   key isn't on the ring buzzes the hand instead.
    ///
    /// With the key in a lock, either hand can grip it (by reaching for
    /// it, as for the lockpicks) and turn it with the wrist: a quarter
    /// turn, anticlockwise if the lock is on the right of the door leaf
    /// from the player's side, clockwise if it's on the left. The hand
    /// visual snaps onto the key and turns with it. Turned all the way,
    /// the door is unlocked and the keyring goes back to the pack; let go
    /// of sooner, the key springs back. Walking away also sends the
    /// keyring back to the pack.
    ///
    /// A hand does one thing at a time: a hand climbing, carrying a prop,
    /// on a door handle or busy with the lockpicks can't take the keyring
    /// or the key, and those systems leave a hand that's busy here alone
    /// (IsLeftBusy/IsRightBusy).
    ///
    /// Lives on the Hands object, and needs PlayerPack there. No Update():
    /// PlayerController calls Tick() (take and let go) before climbing,
    /// and TickHeld() (turn the key, move the snapped hand) after the
    /// body has moved and just before the hand visuals are placed.
    /// </summary>
    public class PlayerKeys : MonoBehaviour
    {
        /// Where the keyring is.
        private enum RingPlace
        {
            InPack,
            InHand,
            InLock
        }

        /// One hand's state. A class, made once per hand in Awake(), so
        /// the methods below can change it without ref parameters.
        private class KeyHand
        {
            public bool isLeftHand;

            // True while this hand grips the key in the lock.
            public bool isOnKey;

            // True after the hand was let go by force (or has just put
            // the keyring into a lock), until grip is released: grip is
            // still held, and would take the key straight away.
            public bool needsRegrip;

            // Whether the hand was within reach of the key last frame, so
            // the tap is felt once on coming into reach.
            public bool wasInReach;

            // The controller's rotation when it took the key, relative to
            // the lock's face. The wrist's turn is measured from this.
            public Quaternion grabRotation;
        }

        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;
        [SerializeField] private PlayerPack playerPack;

        // What each hand is busy with: a hand climbing, carrying a prop,
        // on a door handle or using the lockpicks can't take the keyring
        // or the key. Found (or made) in Awake() if not wired (it's on
        // this same object).
        [SerializeField] private PlayerHandState playerHandState;

        // Optional: a rig with no haptics still uses keys.
        [SerializeField] private PlayerHaptics playerHaptics;

        [Header("Carrying The Keyring")]

        // Where the keyring sits in the hand while it's carried, in that
        // hand visual's own space (its fingers point along -Y, its thumb
        // side is +Z), and which way it's turned. The right hand visual
        // is the left one mirrored, so the same numbers put it at the
        // mirror-image place in either hand.
        [SerializeField] private Vector3 inHandPosition = new(0.02f, -0.13f, 0.03f);
        [SerializeField] private Vector3 inHandRotation = new(90f, 0f, 0f);

        [Header("Locks")]

        // The carried keyring goes into a lock from within this many
        // metres of it.
        [SerializeField] private float insertDistance = 0.15f;

        // Further than this from the lock (metres, measured level from
        // the head), the keyring goes back to the pack.
        [SerializeField] private float leaveDistance = 1.5f;

        [Header("Turning The Key")]

        // How far the key has to be turned to unlock, in degrees.
        [SerializeField] private float unlockTurn = 90f;

        // A hand takes the key from within this many metres of it, and
        // is let go when the real hand is further than breakDistance.
        [SerializeField] private float keyReach = 0.1f;
        [SerializeField] private float breakDistance = 0.3f;

        // How far out from the lock's face the hand grips the key, in
        // metres, and the hand snap profile for it. With no profile the
        // hand sits exactly on the grip frame.
        [SerializeField] private float gripStandOff = 0.04f;
        [SerializeField] private HandSnapProfile snapProfile;

        // How fast a key let go of part way springs back, in degrees a
        // second.
        [SerializeField] private float returnSpeed = 360f;

        [Header("Haptics")]

        // A hand coming within reach of the key.
        [SerializeField] private float reachAmplitude = 0.15f;
        [SerializeField] private float reachDuration = 0.03f;

        // The key going into a lock (the carrying hand).
        [SerializeField] private float insertAmplitude = 0.5f;
        [SerializeField] private float insertDuration = 0.06f;

        // A lock the ring has no key for (the carrying hand): long and rough.
        [SerializeField] private float refuseAmplitude = 0.9f;
        [SerializeField] private float refuseDuration = 0.25f;

        // The lock opening (the turning hand).
        [SerializeField] private float unlockAmplitude = 1f;
        [SerializeField] private float unlockDuration = 0.15f;

        private KeyHand _left;
        private KeyHand _right;

        private bool _hasHaptics;

        private Pack _pack;
        private Keyring _keyring;

        private RingPlace _place = RingPlace.InPack;

        // Which hand carries the keyring, while it's in a hand.
        private bool _isCarrierLeft;

        // The lock the key is in, while it's in one; how something
        // facing that lock from the player's side is turned (see
        // KeyLock.GetFace()); and which way the key turns there.
        private KeyLock _lock;
        private Quaternion _lockFacing;
        private bool _turnsAnticlockwise;

        // How far the key is turned towards unlocking, in degrees
        // (0 to unlockTurn).
        private float _turn;

        // The lock the carried ring was last refused by, so the buzz is
        // felt once per lock, not every frame it's held there.
        private KeyLock _refusedBy;

        // Set when the key reaches its full turn (inside TickHeld()),
        // and acted on at the end of that TickHeld().
        private bool _hasJustUnlocked;

        /// True while the left hand carries the keyring or is on the key -
        /// and, after being let go by force, until its grip is released.
        /// The other hand systems leave the hand alone meanwhile.
        public bool IsLeftBusy => IsCarrying(true) || _left.isOnKey || _left.needsRegrip;

        /// The same for the right hand.
        public bool IsRightBusy => IsCarrying(false) || _right.isOnKey || _right.needsRegrip;

        /// <summary>
        /// Whether that hand is the one carrying the keyring.
        /// </summary>
        private bool IsCarrying(bool isLeftHand)
        {
            return _place == RingPlace.InHand && _isCarrierLeft == isLeftHand;
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
            playerHandInteraction = GetComponent<PlayerHandInteraction>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
            playerPack = GetComponent<PlayerPack>();
        }

        private void Awake()
        {
            if (playerPack == null) {
                playerPack = GetComponent<PlayerPack>();
            }

            if (playerHandState == null) {
                playerHandState = PlayerHandState.GetOrAdd(playerHandVisuals);
            }

            if (playerHaptics == null) {
                playerHaptics = GetComponentInParent<PlayerHaptics>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasHaptics = playerHaptics != null;

            _left = new KeyHand { isLeftHand = true };
            _right = new KeyHand { isLeftHand = false };
        }

        /// <summary>
        /// Finds the pack and its keyring. Start() rather than Awake():
        /// PlayerPack finds (or makes) the pack in its own Awake(), which
        /// may run after this class's.
        /// </summary>
        private void Start()
        {
            if (playerPack == null) {
                Debug.LogWarning("PlayerKeys: no PlayerPack on the Hands object, so there is no keyring to use.", this);
                enabled = false;
                return;
            }

            _pack = playerPack.Pack;
            _keyring = _pack.Keyring;
        }

        private void OnDisable()
        {
            // The scene is being unloaded (a level restart): everything in
            // it is on its way out, and objects can't be re-parented while
            // they're being destroyed.
            if (!gameObject.scene.isLoaded || _keyring == null) {
                return;
            }

            if (_place != RingPlace.InPack) {
                ReturnRing();
            }
        }

        /// <summary>
        /// Takes and lets go of the keyring and the key, for both hands.
        /// Called by PlayerController before PlayerClimbing.Tick(), so a
        /// hand that takes one this frame is already busy when climbing,
        /// carrying and doors look at it.
        /// </summary>
        public void Tick()
        {
            // No pack, so no keyring - see Start().
            if (_keyring is null) {
                return;
            }

            bool isLeftGrabbing = playerInput.IsLeftGrabbing;
            bool isRightGrabbing = playerInput.IsRightGrabbing;

            // Grip released - the hand may take the key again.
            if (!isLeftGrabbing) {
                _left.needsRegrip = false;
            }

            if (!isRightGrabbing) {
                _right.needsRegrip = false;
            }

            switch (_place) {
                case RingPlace.InPack:
                    TickRingInPack(_left, isLeftGrabbing, playerHandInteraction.LeftTarget);

                    if (_place == RingPlace.InPack) {
                        TickRingInPack(_right, isRightGrabbing, playerHandInteraction.RightTarget);
                    }

                    break;

                case RingPlace.InHand:
                    TickRingCarried(_isCarrierLeft ? isLeftGrabbing : isRightGrabbing);
                    break;

                case RingPlace.InLock:
                    TickHandAtKey(_left, isLeftGrabbing);
                    TickHandAtKey(_right, isRightGrabbing);
                    break;
            }
        }

        /// <summary>
        /// The keyring is in the pack: a hand takes it if grip is held
        /// while it's free and its ray is on the keyring's space. (The
        /// space can only be targeted while the keyring is there with a
        /// key on it; the hand holding the pack isn't free.)
        /// </summary>
        private void TickRingInPack(KeyHand hand, bool isGrabbing, IHandTarget target)
        {
            if (!isGrabbing || hand.needsRegrip || !IsFree(hand)) {
                return;
            }

            // The ray target is only an IHandTarget; a type pattern
            // checks whether it's the pack's keyring space.
            if (target is not PackSlot slot || slot.Pack != _pack || slot.Index != Pack.KeyringSpace) {
                return;
            }

            if (!_pack.TakeKeyring()) {
                return;
            }

            _place = RingPlace.InHand;
            _isCarrierLeft = hand.isLeftHand;
            _refusedBy = null;

            // Into the hand, at the same local place in either hand's
            // visual (see inHandPosition).
            Transform ring = _keyring.transform;
            ring.SetParent(hand.isLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual, false);
            ring.SetLocalPositionAndRotation(inHandPosition, Quaternion.Euler(inHandRotation));
            ring.localScale = Vector3.one;
        }

        /// <summary>
        /// A hand is carrying the keyring: it goes back to the
        /// pack if grip is let go, or into a locked lock it's brought
        /// close to if the lock's key is on it. A distance check per lock
        /// in the level, and only while the ring is in hand.
        /// </summary>
        private void TickRingCarried(bool isGrabbing)
        {
            if (!isGrabbing) {
                ReturnRing();
                return;
            }

            KeyLock found = KeyLock.FindInRange(_keyring.transform.position, insertDistance);

            if (found == null) {
                // Away from every lock: the next one tried can buzz again.
                _refusedBy = null;
                return;
            }

            if (_keyring.Has(found.KeyId)) {
                Insert(found);
                return;
            }

            // The wrong ring for this lock: say so, once.
            if (found != _refusedBy) {
                _refusedBy = found;
                Pulse(_isCarrierLeft, refuseAmplitude, refuseDuration);
            }
        }

        /// <summary>
        /// Puts the key into found: the keyring snaps onto the lock's
        /// face on the player's side, with the key that fits shown going
        /// into the door, and the carrying hand is freed (it has to let go
        /// of grip before it can take the key).
        /// </summary>
        private void Insert(KeyLock found)
        {
            found.GetFace(playerTracking.HeadPosition, out Vector3 facePoint, out Quaternion facing);

            _lock = found;
            _lockFacing = facing;
            _turnsAnticlockwise = found.TurnsAnticlockwise(facing);
            _turn = 0f;
            _place = RingPlace.InLock;
            (_isCarrierLeft ? _left : _right).needsRegrip = true;
            _left.wasInReach = false;
            _right.wasInReach = false;
            found.BeginKey();

            // A child of the lock (unscaled), so it would swing with the
            // door; placed in the world, on the face.
            Transform ring = _keyring.transform;
            ring.SetParent(found.transform, false);
            ring.localScale = Vector3.one;
            ring.SetPositionAndRotation(facePoint, facing);

            if (_keyring.TryGetColor(found.KeyId, out Color color)) {
                _keyring.ShowKeyInLock(true, color);
            }

            Pulse(_isCarrierLeft, insertAmplitude, insertDuration);
        }

        /// <summary>
        /// One hand, while the key is in a lock: lets go of it if grip
        /// was released, otherwise takes it if grip is held and the hand
        /// is free and within reach of it.
        /// </summary>
        private void TickHandAtKey(KeyHand hand, bool isGrabbing)
        {
            if (hand.isOnKey) {
                if (!isGrabbing) {
                    LetGo(hand, false);
                }

                return;
            }

            // One hand on the key at a time.
            KeyHand other = hand.isLeftHand ? _right : _left;

            bool isInReach = !other.isOnKey
                && !hand.needsRegrip
                && IsFree(hand)
                && IsWithin(HandPoint(hand), GripPoint(), keyReach);

            if (isInReach && !hand.wasInReach) {
                Pulse(hand.isLeftHand, reachAmplitude, reachDuration);
            }

            hand.wasInReach = isInReach;

            if (!isInReach || !isGrabbing) {
                return;
            }

            hand.isOnKey = true;

            // The wrist's turn is measured from how the controller is
            // turned now, less however far the key is already turned (it
            // may still be springing back) - so the key doesn't jump.
            Quaternion controllerRotation = hand.isLeftHand ? playerTracking.LeftHandRotation : playerTracking.RightHandRotation;
            Quaternion relative = Quaternion.Inverse(_lockFacing) * controllerRotation;
            hand.grabRotation = Quaternion.Inverse(Quaternion.AngleAxis(SignedTurn(), Vector3.forward)) * relative;

            SnapFor(hand).Snap(GetSnapPose(hand.isLeftHand));
        }

        /// <summary>
        /// Takes this hand off the key (nothing happens if it isn't on
        /// it): the hand visual blends back to the controller, and the
        /// key springs back by itself (TickHeld()). With byForce, the hand
        /// can't take the key again until its grip has been released.
        /// </summary>
        private void LetGo(KeyHand hand, bool byForce)
        {
            if (!hand.isOnKey) {
                return;
            }

            hand.isOnKey = false;
            hand.needsRegrip = byForce;
            hand.wasInReach = false;
            SnapFor(hand).Release();
        }

        /// <summary>
        /// Sends the keyring back to its space in the pack, from the hand
        /// or from a lock: both hands come off the key, the key in the
        /// lock is hidden and the lock is free again.
        /// </summary>
        private void ReturnRing()
        {
            LetGo(_left, true);
            LetGo(_right, true);

            // Unity's == null is true for a lock destroyed with a key in.
            if (_lock != null) {
                _lock.EndKey();
            }

            _lock = null;
            _turn = 0f;
            _place = RingPlace.InPack;

            _keyring.ShowKeyInLock(false, default);
            _pack.ReturnKeyring();
        }

        /// <summary>
        /// Works the key for this frame. Called by PlayerController after
        /// the body has moved and turned and just before
        /// PlayerHandVisuals.Tick() - the key has to be where the hand
        /// has turned it before the hand visual is placed on it - on
        /// every path through Update(), so the keyring still goes back to
        /// the pack if the body is carried away by a mantle.
        /// </summary>
        public void TickHeld()
        {
            if (_place != RingPlace.InLock) {
                return;
            }

            if (ShouldLeaveLock()) {
                ReturnRing();
                return;
            }

            TickHeldHand(_left, playerTracking.LeftHandPosition, playerTracking.LeftHandRotation);
            TickHeldHand(_right, playerTracking.RightHandPosition, playerTracking.RightHandRotation);

            // Nobody on the key: it springs back.
            if (!_left.isOnKey && !_right.isOnKey && _turn > 0f) {
                _turn = Mathf.MoveTowards(_turn, 0f, returnSpeed * Time.deltaTime);
                ApplyTurn();
            }

            // The key reached its full turn this frame.
            if (_hasJustUnlocked) {
                _hasJustUnlocked = false;
                _lock.Unlock();
                ReturnRing();
            }
        }

        /// <summary>
        /// True if the key can't stay in the lock: the lock has gone or
        /// been opened some other way, or the player has moved away.
        /// </summary>
        private bool ShouldLeaveLock()
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
        /// One hand on the key: the key turns as far as the wrist has
        /// turned about the lock's own axis since it took hold - only the
        /// way that unlocks, and no further than the full turn. The
        /// snapped hand visual turns with it. Reaching the full turn
        /// unlocks; a real hand that has strayed too far is let go by
        /// force.
        /// </summary>
        private void TickHeldHand(KeyHand hand, Vector3 controllerPosition, Quaternion controllerRotation)
        {
            if (!hand.isOnKey) {
                return;
            }

            // Both rotations are relative to the lock's face, whose Z
            // points away from the player - and a turn about an axis
            // pointing away from you is anticlockwise as you see it when
            // it's positive.
            Quaternion relative = Quaternion.Inverse(_lockFacing) * controllerRotation;
            float twist = TwistAboutZ(relative * Quaternion.Inverse(hand.grabRotation));

            _turn = Mathf.Clamp(_turnsAnticlockwise ? twist : -twist, 0f, unlockTurn);
            ApplyTurn();

            HandSnapPose pose = GetSnapPose(hand.isLeftHand);
            SnapFor(hand).SetSnapPose(pose.Position, pose.Rotation);

            if (_turn >= unlockTurn) {
                Pulse(hand.isLeftHand, unlockAmplitude, unlockDuration);
                _hasJustUnlocked = true;
                return;
            }

            if (!IsWithin(controllerPosition, GripPoint(), breakDistance)) {
                LetGo(hand, true);
            }
        }

        /// <summary>
        /// The key's turn as an angle about the lock face's Z: positive
        /// is anticlockwise as the player sees it.
        /// </summary>
        private float SignedTurn()
        {
            return _turnsAnticlockwise ? _turn : -_turn;
        }

        /// <summary>
        /// Shows the keyring turned with the key, about the lock's axis.
        /// </summary>
        private void ApplyTurn()
        {
            _keyring.transform.rotation = _lockFacing * Quaternion.AngleAxis(SignedTurn(), Vector3.forward);
        }

        /// <summary>
        /// Where a hand grips the key: just out from the lock's face,
        /// towards the player.
        /// </summary>
        private Vector3 GripPoint()
        {
            return _keyring.transform.position - _lockFacing * Vector3.forward * gripStandOff;
        }

        /// <summary>
        /// The hand snap pose for a hand on the key as it's turned now:
        /// facing into the door, thumb up at rest, turned with the key.
        /// Called every frame the key is held.
        /// </summary>
        private HandSnapPose GetSnapPose(bool isLeftHand)
        {
            Vector3 point = GripPoint();
            Quaternion rotation = _lockFacing * Quaternion.AngleAxis(SignedTurn(), Vector3.forward);

            if (snapProfile != null) {
                return snapProfile.Apply(isLeftHand, point, rotation);
            }

            return new HandSnapPose(point, rotation, default);
        }

        /// <summary>
        /// How far rotation turns about the Z axis, in degrees (-180 to
        /// 180), ignoring whatever else it does. A quaternion's z and w
        /// parts hold the sine and cosine of half its turn about Z, so
        /// the angle is twice their arctangent. (As PlayerHandDoors does
        /// for a door handle.)
        /// </summary>
        private static float TwistAboutZ(Quaternion rotation)
        {
            float angle = 2f * Mathf.Atan2(rotation.z, rotation.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, angle);
        }

        /// <summary>
        /// True if no other hand system has the hand (PlayerHandState
        /// knows them all).
        /// </summary>
        private bool IsFree(KeyHand hand)
        {
            return !playerHandState.IsBusyExcept(hand.isLeftHand, HandUse.Keys);
        }

        /// <summary>
        /// Where a hand is for reaching: its controller's pose, moved to
        /// where the hand visual really is when a surface holds it back.
        /// </summary>
        private Vector3 HandPoint(KeyHand hand)
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
        /// This hand's snap, owned by PlayerHandVisuals. Looked up when
        /// needed rather than kept from Awake(): PlayerHandVisuals makes
        /// them in its own Awake(), which may run after this class's.
        /// </summary>
        private HandVisualSnap SnapFor(KeyHand hand)
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
