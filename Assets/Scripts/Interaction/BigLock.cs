using System;
using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// The big lock: a large copy of a lock that floats in front of it
    /// while it's being picked, with the two picks sticking out of its
    /// keyway for the hands to work. One in the scene, built once at load
    /// and reused for every lock.
    ///
    /// It also IS the puzzle - which stage the lock is at, where the pins
    /// are, how long a pin has been held. The player's half
    /// (Player.PlayerLockpicking) only tells it where a hand on a pick is
    /// and listens for what happens.
    ///
    /// Angles are clock positions on the lock's face, seen from the
    /// player's side, in degrees clockwise from 12 o'clock.
    ///
    /// The right pick turns clockwise from its start to a stop. Held at
    /// the stop, the left pick is swept down the left side of the face to
    /// find that stage's pin - a random angle. Held on the pin for a
    /// moment, the pin is set and the right pick is free to turn on to its
    /// next stop. Three pins, then one last turn, which unlocks. Letting
    /// go of the right pick before that swings it back to its start and
    /// starts the lock again, with new pins.
    ///
    /// A pick is turned by carrying the hand round the lock's middle, like
    /// a clock hand: the pick's angle is the hand's, not the wrist's
    /// twist.
    ///
    /// This object's own axes: X to the player's right, Y up the face, Z
    /// away from the player (into the door). Its origin is the middle of
    /// the lock's body. Unscaled.
    ///
    /// No Update(): PlayerLockpicking calls Tick() while it's in use.
    /// </summary>
    public class BigLock : MonoBehaviour
    {
        // How many pins there are to find. The right pick has one more
        // stop than this: the last turn, which unlocks.
        public const int PinCount = 3;

        [Header("Shape")]

        // The lock's round body, in metres.
        [SerializeField] private float faceRadius = 0.11f;
        [SerializeField] private float bodyDepth = 0.05f;

        // The keyway: the dark rectangle in the middle of the face.
        [SerializeField] private float keywayWidth = 0.03f;
        [SerializeField] private float keywayHeight = 0.07f;

        // The picks: how long and thick each is, and how far they lean
        // out of the face towards the player, in degrees (0 = flat
        // against the face, 90 = straight out).
        [SerializeField] private float pickLength = 0.2f;
        [SerializeField] private float pickThickness = 0.012f;
        [SerializeField] private float pickLift = 35f;

        // How far along a pick, from the keyway, a hand grips it.
        [SerializeField] private float gripAlong = 0.14f;

        [Header("Colours")]
        [SerializeField] private Color bodyColor = new(0.32f, 0.3f, 0.28f, 1f);
        [SerializeField] private Color faceColor = new(0.5f, 0.46f, 0.38f, 1f);
        [SerializeField] private Color keywayColor = new(0.03f, 0.03f, 0.03f, 1f);
        [SerializeField] private Color markColor = new(0.9f, 0.85f, 0.7f, 1f);
        [SerializeField] private Color pickColor = new(0.75f, 0.77f, 0.8f, 1f);

        // The least light the lock is ever shown in, 0-1: levels are dark,
        // and it has to be seen to be worked.
        [SerializeField] private float minLight = 0.35f;

        [Header("Right Pick (turning)")]

        // Where the right pick starts: 30 = 1 o'clock.
        [SerializeField] private float rightStartAngle = 30f;

        // How far it turns from one stop to the next, in degrees. Four
        // stops in all, evenly spaced.
        [SerializeField] private float stopSpacing = 28f;

        // The pick counts as at its stop within this many degrees of it.
        [SerializeField] private float stopTolerance = 1.5f;

        // How fast it swings back to its start when let go, in degrees a
        // second.
        [SerializeField] private float returnSpeed = 240f;

        [Header("Left Pick (searching)")]

        // Where the left pick starts: -30 = 11 o'clock.
        [SerializeField] private float leftStartAngle = -30f;

        // How far it sweeps anticlockwise from there, down the left side:
        // 120 = to 7 o'clock.
        [SerializeField] private float leftArc = 120f;

        [Header("Pins")]

        // The left hand starts to feel the pin within this many degrees of
        // it, and has found it within pinSetAngle.
        [SerializeField] private float pinFeelAngle = 10f;
        [SerializeField] private float pinSetAngle = 5f;

        // Seconds the left pick must be held on the pin to set it.
        [SerializeField] private float pinHoldTime = 1f;

        // A new pin is never put closer than this to where the left pick
        // already is, in degrees, so it can't be found by doing nothing.
        // 0 = anywhere.
        [SerializeField] private float minPinGap = 20f;

        [Header("Hands")]

        // A hand closer than this to the lock's middle line, in metres,
        // has no clear clock angle: the pick stays where it is.
        [SerializeField] private float deadRadius = 0.03f;

        // The hand snap profile for a hand on a pick. Optional: with none
        // the hand sits exactly on the grip frame, in the rope grip pose.
        [SerializeField] private HandSnapProfile snapProfile;

        [Header("Fade")]

        // Seconds to fade in or out.
        [SerializeField] private float fadeDuration = 0.25f;

        [Header("Sound")]

        // The quiet click and scrape of picking, and the lock opening.
        // Both optional (empty = silent, and unheard by guards).
        [SerializeField] private SoundCue pickCue;
        [SerializeField] private SoundCue unlockCue;

        // How loud the picking sounds are, and how far they carry to
        // guards, as a share of the cue's own volume and noise radius.
        [SerializeField] private float pickVolume = 0.4f;
        [SerializeField] private float pickNoiseScale = 0.3f;

        // The left pick scrapes once every this many degrees it's swept.
        [SerializeField] private float scrapeAngle = 25f;

        // How many flat pieces the round body is made of.
        private const int BodySegments = 32;

        // How far the keyway and the marks stand out of the face, in
        // metres - enough not to flicker against it.
        private const float Proud = 0.002f;

        private static readonly int _alphaId = Shader.PropertyToID("_Alpha");

        // How the lock is being drawn. Renderers and materials are only
        // touched when this changes.
        private enum Look
        {
            Hidden,
            Fading,
            Solid
        }

        private Transform _leftPick;
        private Transform _rightPick;

        // The body and the two picks.
        private readonly MeshRenderer[] _renderers = new MeshRenderer[3];
        private Mesh _bodyMesh;
        private Mesh _pickMesh;

        // Two materials, shared by all three renderers: the solid one worn
        // while fully in, and the see-through one worn only while fading.
        private Material _solidMaterial;
        private Material _fadeMaterial;

        private Look _look = Look.Hidden;
        private bool _isShown;
        private float _fade;

        private bool _isRightHeld;
        private bool _isLeftHeld;

        // True from the right pick reaching its stop until it's turned
        // well back from it (or the stop moves on).
        private bool _isRightAtStop;

        // True once the last turn has been made, until the lock is shown
        // again: nothing more can happen to it.
        private bool _isUnlocked;

        // Degrees the left pick has swept since it last scraped.
        private float _scrapeTravel;

        // The angles the two pick objects were last turned to, so they're
        // only turned again when an angle changes. They start as NaN,
        // which never equals anything, so the first one is always applied.
        private float _appliedLeftSweep = float.NaN;
        private float _appliedRightAngle = float.NaN;

        /// The lock being picked. Null until the first Show().
        public PickableLock Target { get; private set; }

        /// True while the lock is in view, or still fading out.
        public bool IsActive => _isShown || _fade > 0f;

        /// True once the lock has faded in enough for a hand to take a
        /// pick.
        public bool CanBeWorked => _isShown && !_isUnlocked && _fade >= 0.5f;

        /// How many pins have been set so far, 0 to PinCount.
        public int PinsSet { get; private set; }

        /// The right pick's clock angle, in degrees.
        public float RightAngle { get; private set; }

        /// How far the left pick has been swept from its start, in
        /// degrees (0 to the arc).
        public float LeftSweep { get; private set; }

        /// The left pick's clock angle, in degrees.
        public float LeftAngle => leftStartAngle - LeftSweep;

        /// Where this stage's pin is, as a sweep of the left pick.
        public float PinSweep { get; private set; }

        /// The pin's clock angle, in degrees.
        public float PinAngle => leftStartAngle - PinSweep;

        /// Seconds the left pick has been held on the pin.
        public float HoldTimer { get; private set; }

        /// Seconds it must be held to set the pin.
        public float PinHoldTime => pinHoldTime;

        /// The clock angle of the stop the right pick can turn to now.
        public float CurrentStop => rightStartAngle + stopSpacing * (PinsSet + 1);

        /// True while a hand holds the right pick at its stop with a pin
        /// still to find: the left pick's search counts.
        public bool IsSearching => _isRightHeld && _isRightAtStop && !_isUnlocked && PinsSet < PinCount;

        /// How strongly the left hand feels the pin, 0 (nothing) to 1 (on
        /// it). Only above 0 while searching with the left pick in hand.
        public float PinNearness { get; private set; }

        /// The right pick has reached a stop with a pin still to find.
        public event Action StopReached;

        /// A pin has been set.
        public event Action PinSet;

        /// The right pick was let go and the lock has started again.
        public event Action WasReset;

        /// The last turn has been made: the lock is open.
        public event Action Unlocked;

        private void Awake()
        {
            // Solid things only ever switch between these two.
            _solidMaterial = LockMeshBuilder.CreateMaterial(false, minLight);
            _fadeMaterial = LockMeshBuilder.CreateMaterial(true, minLight);

            BuildBody();
            BuildPicks();

            ApplyLeftSweep(0f);
            ApplyRightAngle(rightStartAngle);
            SetRenderersEnabled(false);
        }

        private void OnDestroy()
        {
            // Meshes and materials made from code aren't cleaned up with
            // their objects.
            Destroy(_bodyMesh);
            Destroy(_pickMesh);
            Destroy(_solidMaterial);
            Destroy(_fadeMaterial);
        }

        /// <summary>
        /// Makes the lock's body: a round slab facing the player, the
        /// dark keyway in the middle of its face, and a mark on the face's
        /// rim at each of the right pick's four stops and at each end of
        /// the left pick's sweep.
        /// </summary>
        private void BuildBody()
        {
            LockMeshBuilder builder = new();
            builder.Cylinder(faceRadius, bodyDepth, BodySegments, faceColor, bodyColor);

            float faceZ = -bodyDepth * 0.5f;

            builder.Box(
                new Vector3(0f, 0f, faceZ),
                new Vector3(keywayWidth, keywayHeight, Proud * 2f),
                Quaternion.identity,
                keywayColor);

            for (int stop = 1; stop <= PinCount + 1; stop++) {
                AddMark(builder, rightStartAngle + stopSpacing * stop, faceZ);
            }

            AddMark(builder, leftStartAngle, faceZ);
            AddMark(builder, leftStartAngle - leftArc, faceZ);

            _bodyMesh = builder.ToMesh("Big Lock Body");
            _renderers[0] = AddPart("Body", _bodyMesh).GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// Adds a short mark on the face's rim at a clock angle, pointing
        /// at the middle.
        /// </summary>
        private void AddMark(LockMeshBuilder builder, float clockAngle, float faceZ)
        {
            Vector3 direction = ClockDirection(clockAngle);
            Vector3 centre = direction * (faceRadius * 0.86f) + new Vector3(0f, 0f, faceZ);

            // A turn about Z of minus the clock angle takes "up" to that
            // clock position, so the mark's long side lies along it.
            Quaternion rotation = Quaternion.AngleAxis(-clockAngle, Vector3.forward);
            builder.Box(centre, new Vector3(faceRadius * 0.05f, faceRadius * 0.2f, Proud * 2f), rotation, markColor);
        }

        /// <summary>
        /// Makes the two picks: one long thin box each, sharing a mesh,
        /// on a child object that turns about the keyway. The box runs
        /// from the object's origin along its Z.
        /// </summary>
        private void BuildPicks()
        {
            LockMeshBuilder builder = new();

            builder.Box(
                new Vector3(0f, 0f, pickLength * 0.5f),
                new Vector3(pickThickness, pickThickness, pickLength),
                Quaternion.identity,
                pickColor);

            _pickMesh = builder.ToMesh("Big Lock Pick");

            // Side by side in the keyway, on the face.
            float aside = keywayWidth * 0.25f;
            float faceZ = -bodyDepth * 0.5f;

            _leftPick = AddPart("Left Pick", _pickMesh).transform;
            _leftPick.localPosition = new Vector3(-aside, 0f, faceZ);
            _renderers[1] = _leftPick.GetComponent<MeshRenderer>();

            _rightPick = AddPart("Right Pick", _pickMesh).transform;
            _rightPick.localPosition = new Vector3(aside, 0f, faceZ);
            _renderers[2] = _rightPick.GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// A child object drawing mesh, with no shadows: it's a floating
        /// aid, not part of the room.
        /// </summary>
        private GameObject AddPart(string partName, Mesh mesh)
        {
            GameObject part = new(partName) { layer = gameObject.layer };
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterial = _solidMaterial;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return part;
        }

        /// <summary>
        /// The direction across the face, in this object's own space, of
        /// a clock angle: 0 = up, 90 = to the player's right.
        /// </summary>
        private static Vector3 ClockDirection(float clockAngle)
        {
            float radians = clockAngle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), Mathf.Cos(radians), 0f);
        }

        /// <summary>
        /// Brings the lock into view for target, at a place in the world,
        /// with everything back at the start and new pins.
        /// </summary>
        public void Show(PickableLock target, Vector3 position, Quaternion rotation)
        {
            Target = target;
            transform.SetPositionAndRotation(position, rotation);

            _isShown = true;
            _isUnlocked = false;
            _isRightHeld = false;
            _isLeftHeld = false;
            _scrapeTravel = 0f;

            ApplyLeftSweep(0f);
            ApplyRightAngle(rightStartAngle);
            StartAgain();
        }

        /// <summary>
        /// Starts the lock fading out. Any hand on a pick must already
        /// have been taken off it.
        /// </summary>
        public void Hide()
        {
            _isShown = false;
            _isRightHeld = false;
            _isLeftHeld = false;
            PinNearness = 0f;
            HoldTimer = 0f;
        }

        /// <summary>
        /// A hand has taken hold of a pick.
        /// </summary>
        public void HoldPick(bool isLeftPick)
        {
            if (isLeftPick) {
                _isLeftHeld = true;
            } else {
                _isRightHeld = true;
            }
        }

        /// <summary>
        /// A hand has let go of a pick. The left pick stays where it was
        /// left. Letting go of the right pick before the lock is open
        /// starts the lock again: it swings back to its start (see
        /// Tick()), every pin set is lost and new ones are chosen.
        /// </summary>
        public void ReleasePick(bool isLeftPick)
        {
            if (isLeftPick) {
                _isLeftHeld = false;
                return;
            }

            _isRightHeld = false;

            if (_isUnlocked) {
                return;
            }

            // Only worth telling anyone if there was something to lose.
            bool hadProgress = PinsSet > 0 || _isRightAtStop;
            StartAgain();

            if (hadProgress) {
                PlayPick();
                WasReset?.Invoke();
            }
        }

        /// <summary>
        /// Back to the first stage, with a new pin to find. The picks
        /// aren't moved here.
        /// </summary>
        private void StartAgain()
        {
            PinsSet = 0;
            HoldTimer = 0f;
            PinNearness = 0f;
            _isRightAtStop = false;
            ChoosePin();
        }

        /// <summary>
        /// Chooses this stage's pin: anywhere along the left pick's sweep
        /// except within minPinGap of where the left pick is now.
        ///
        /// Done without trying again and again: the stretch the pin can't
        /// be in is cut out of the sweep, a spot is chosen along what's
        /// left, and a spot beyond the cut is moved along by the cut's
        /// length.
        /// </summary>
        private void ChoosePin()
        {
            float cutFrom = Mathf.Max(0f, LeftSweep - minPinGap);
            float cutTo = Mathf.Min(leftArc, LeftSweep + minPinGap);
            float allowed = leftArc - (cutTo - cutFrom);

            if (minPinGap <= 0f || allowed <= 0f) {
                PinSweep = UnityEngine.Random.Range(0f, leftArc);
                return;
            }

            float spot = UnityEngine.Random.Range(0f, allowed);
            PinSweep = spot < cutFrom ? spot : spot + (cutTo - cutFrom);
        }

        /// <summary>
        /// Turns a pick to where the hand holding it is: the pick's clock
        /// angle is the hand's, round the lock's middle, kept within what
        /// that pick can do. Called every frame a hand is on a pick.
        /// </summary>
        public void TurnPick(bool isLeftPick, Vector3 handPosition)
        {
            if (!_isShown || _isUnlocked) {
                return;
            }

            // The hand in the lock's own space: x to the right, y up.
            // (The object is unscaled, so this is in metres.)
            Vector3 local = transform.InverseTransformPoint(handPosition);

            if (local.x * local.x + local.y * local.y < deadRadius * deadRadius) {
                return;
            }

            // Atan2(x, y), not (y, x): measured from "up", clockwise.
            float clock = Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;

            if (isLeftPick) {
                TurnLeftPick(clock);
            } else {
                TurnRightPick(clock);
            }
        }

        /// <summary>
        /// The left pick follows the hand between its start and the end
        /// of its sweep. The hand's angle is measured from the middle of
        /// the sweep (DeltaAngle gives the short way round, so there's no
        /// jump where the angle wraps from 180 to -180), then limited to
        /// half the sweep either way.
        /// </summary>
        private void TurnLeftPick(float clock)
        {
            float half = leftArc * 0.5f;
            float fromMiddle = Mathf.Clamp(Mathf.DeltaAngle(leftStartAngle - half, clock), -half, half);
            float sweep = half - fromMiddle;

            _scrapeTravel += Mathf.Abs(sweep - LeftSweep);

            if (_scrapeTravel >= scrapeAngle) {
                _scrapeTravel = 0f;
                PlayPick();
            }

            ApplyLeftSweep(sweep);
        }

        /// <summary>
        /// The right pick follows the hand between its start and its
        /// current stop, measured the same way as the left. Reaching the
        /// stop is felt once (StopReached); reaching the last stop, with
        /// every pin set, unlocks.
        /// </summary>
        private void TurnRightPick(float clock)
        {
            float stop = CurrentStop;
            float half = (stop - rightStartAngle) * 0.5f;
            float middle = rightStartAngle + half;

            ApplyRightAngle(middle + Mathf.Clamp(Mathf.DeltaAngle(middle, clock), -half, half));

            if (_isRightAtStop) {
                // Turned well back from the stop: coming up to it again
                // will be felt again.
                if (RightAngle < stop - stopTolerance * 3f) {
                    _isRightAtStop = false;
                }

                return;
            }

            if (RightAngle < stop - stopTolerance) {
                return;
            }

            _isRightAtStop = true;

            if (PinsSet >= PinCount) {
                Unlock();
                return;
            }

            PlayPick();
            StopReached?.Invoke();
        }

        /// <summary>
        /// The last turn: the real lock opens.
        /// </summary>
        private void Unlock()
        {
            _isUnlocked = true;
            PinNearness = 0f;
            HoldTimer = 0f;

            if (unlockCue != null) {
                Play(unlockCue, 1f, 1f);
            }

            Target.Unlock();
            Unlocked?.Invoke();
        }

        /// <summary>
        /// One frame of the lock: its fade, the right pick swinging back
        /// when nobody holds it, and the search for the pin.
        /// </summary>
        public void Tick(float deltaTime)
        {
            TickFade(deltaTime);

            if (!_isShown || _isUnlocked) {
                return;
            }

            if (!_isRightHeld && RightAngle > rightStartAngle) {
                ApplyRightAngle(Mathf.MoveTowards(RightAngle, rightStartAngle, returnSpeed * deltaTime));
            }

            TickSearch(deltaTime);
        }

        /// <summary>
        /// The search: while the right pick is held at its stop and the
        /// left pick is in hand, works out how near the left pick is to
        /// the pin, and counts the time it's held on it. Held on it for
        /// long enough, the pin is set. Leaving the pin early starts the
        /// count again.
        /// </summary>
        private void TickSearch(float deltaTime)
        {
            if (!IsSearching || !_isLeftHeld) {
                PinNearness = 0f;
                HoldTimer = 0f;
                return;
            }

            float away = Mathf.Abs(LeftSweep - PinSweep);

            // 0 at the feel angle, rising to 1 at the set angle and inside
            // it (InverseLerp limits its answer to 0-1).
            PinNearness = Mathf.InverseLerp(pinFeelAngle, pinSetAngle, away);

            if (away > pinSetAngle) {
                HoldTimer = 0f;
                return;
            }

            HoldTimer += deltaTime;

            if (HoldTimer >= pinHoldTime) {
                SetPin();
            }
        }

        /// <summary>
        /// This stage's pin is found: the right pick's stop moves on, and
        /// the next stage gets a pin of its own.
        /// </summary>
        private void SetPin()
        {
            PinsSet++;
            HoldTimer = 0f;
            PinNearness = 0f;
            _isRightAtStop = false;

            if (PinsSet < PinCount) {
                ChoosePin();
            }

            PlayPick();
            PinSet?.Invoke();
        }

        /// <summary>
        /// Moves the fade towards in or out, and swaps how the lock is
        /// drawn when it gets there.
        /// </summary>
        private void TickFade(float deltaTime)
        {
            float target = _isShown ? 1f : 0f;

            // Fully in or out already - the usual case. (SetLook() does
            // nothing when the look hasn't changed.)
            if (_fade == target) {
                SetLook(_isShown ? Look.Solid : Look.Hidden);
                return;
            }

            _fade = fadeDuration > 0f ? Mathf.MoveTowards(_fade, target, deltaTime / fadeDuration) : target;

            if (_fade <= 0f) {
                SetLook(Look.Hidden);
            } else if (_fade >= 1f) {
                SetLook(Look.Solid);
            } else {
                SetLook(Look.Fading);

                // A material this class made and owns, shared by the
                // three renderers, so writing to it is one cheap call and
                // changes no asset.
                _fadeMaterial.SetFloat(_alphaId, _fade);
            }
        }

        /// <summary>
        /// Changes how the lock is drawn: not at all, see-through (while
        /// fading), or solid. Nothing is touched unless it's a change.
        /// </summary>
        private void SetLook(Look look)
        {
            if (look == _look) {
                return;
            }

            if (_look == Look.Hidden) {
                SetRenderersEnabled(true);
            }

            _look = look;

            if (look == Look.Hidden) {
                SetRenderersEnabled(false);
                return;
            }

            Material material = look == Look.Solid ? _solidMaterial : _fadeMaterial;

            for (int i = 0; i < _renderers.Length; i++) {
                _renderers[i].sharedMaterial = material;
            }
        }

        private void SetRenderersEnabled(bool isEnabled)
        {
            for (int i = 0; i < _renderers.Length; i++) {
                _renderers[i].enabled = isEnabled;
            }
        }

        /// <summary>
        /// Puts the left pick at a sweep, turning its object only if the
        /// sweep has changed.
        /// </summary>
        private void ApplyLeftSweep(float sweep)
        {
            if (sweep == _appliedLeftSweep) {
                return;
            }

            _appliedLeftSweep = sweep;
            LeftSweep = sweep;
            _leftPick.localRotation = PickRotation(LeftAngle);
        }

        /// <summary>
        /// Puts the right pick at a clock angle, turning its object only
        /// if the angle has changed.
        /// </summary>
        private void ApplyRightAngle(float angle)
        {
            if (angle == _appliedRightAngle) {
                return;
            }

            _appliedRightAngle = angle;
            RightAngle = angle;
            _rightPick.localRotation = PickRotation(angle);
        }

        /// <summary>
        /// The rotation, in this object's space, that points a pick (its
        /// own Z) along a clock angle and leaning out of the face towards
        /// the player by pickLift.
        /// </summary>
        private Quaternion PickRotation(float clockAngle)
        {
            float lift = pickLift * Mathf.Deg2Rad;
            Vector3 direction = ClockDirection(clockAngle) * Mathf.Cos(lift) + Vector3.back * Mathf.Sin(lift);

            // "Up" for the pick is towards the player: never the same way
            // as the pick points, so the rotation is always well defined.
            return Quaternion.LookRotation(direction, Vector3.back);
        }

        /// <summary>
        /// Where on a pick a hand grips it, in the world.
        /// </summary>
        public Vector3 GetGripPoint(bool isLeftPick)
        {
            Transform pick = isLeftPick ? _leftPick : _rightPick;
            return pick.position + pick.forward * gripAlong;
        }

        /// <summary>
        /// The hand snap pose for a hand on a pick as it is now: the left
        /// hand on the left pick, the right on the right. The grip frame
        /// is a rope's - up along the pick, away from the keyway, and
        /// facing away from the player - so the hand closes round the
        /// pick from the player's side. Called every frame a pick is held,
        /// since the pick turns.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftPick)
        {
            Transform pick = isLeftPick ? _leftPick : _rightPick;
            Vector3 along = pick.forward;
            Vector3 point = pick.position + along * gripAlong;

            // The way into the lock, straightened to be square to the
            // pick (the pick leans, so the two aren't quite at right
            // angles).
            Vector3 right = Vector3.Cross(along, transform.forward).normalized;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(right, along), along);

            if (snapProfile != null) {
                return snapProfile.Apply(isLeftPick, point, rotation);
            }

            return new HandSnapPose(point, rotation, HandPose.RopeGrip);
        }

        /// <summary>
        /// A quiet picking sound, if there's a cue for it.
        /// </summary>
        private void PlayPick()
        {
            if (pickCue != null) {
                Play(pickCue, pickVolume, pickNoiseScale);
            }
        }

        /// <summary>
        /// Plays cue from the real lock - where the sound truly is, for
        /// the player's ears and the guards' - not from this floating
        /// copy. With no sound player in the scene, the guards still hear
        /// it.
        /// </summary>
        private void Play(SoundCue cue, float volumeScale, float noiseScale)
        {
            Transform source = Target.transform;

            if (SoundPlayer.Instance != null) {
                SoundPlayer.Instance.Play(cue, source.position, source, noiseScale, volumeScale);
            } else {
                cue.EmitNoise(source.position, source, noiseScale);
            }
        }
    }
}
