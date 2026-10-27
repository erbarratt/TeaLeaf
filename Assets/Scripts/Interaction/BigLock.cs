using System;
using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// The big lock: two large picks sticking out of the real lock's
    /// keyhole while it's being picked, for the hands to work. One in the
    /// scene, built once at load and reused for every lock. The picks are
    /// large copies of the player's own (the lockpicks model, given by
    /// SetPickModel()): the hook on the left, searching for pins, and the
    /// bent tension wrench on the right, turning. Nothing else is drawn:
    /// the lock is the real one on the door.
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
    /// Each pick turns about its tip, deep in the keyhole: the left
    /// pick's behind the middle of the keyhole's round top, the right's
    /// behind the middle of the slot below it. A pick is turned by carrying the hand round its
    /// pivot, like a clock hand: the pick's angle is the hand's, not the
    /// wrist's twist.
    ///
    /// This object's own axes: X to the player's right, Y up the face, Z
    /// away from the player (into the door). Shown, its origin is the
    /// middle of the round top of the real keyhole, on the lock's face.
    /// Unscaled.
    ///
    /// It appears and goes at once (Show()/Hide()); the burn into being
    /// and away is the player's (PlayerLockpicking, through its ToolBurn,
    /// with the two renderers this exposes).
    ///
    /// No Update(): PlayerLockpicking calls Tick() while it's in use.
    /// </summary>
    public class BigLock : MonoBehaviour
    {
        // How many pins there are to find. The right pick has one more
        // stop than this: the last turn, which unlocks.
        public const int PinCount = 3;

        [Header("Shape")]

        // The picks: how long each is, how far each goes into the
        // keyhole, and how far they lean out of the face towards the
        // player, in degrees (0 = flat against the face, 90 = straight
        // out).
        [Tooltip("How long each pick is from its tip in the keyhole to the end of its handle, in metres; the lockpicks model is scaled to it at load. e.g. 0.2 = picks 20cm long. Higher = bigger picks with the hands working further from the lock; lower = smaller ones, worked close in.")]
        [SerializeField] private float pickLength = 0.2f;
        [Tooltip("How deep into the keyhole each pick's tip sits past the lock's face, in metres; each pick turns about its tip there. e.g. 0.004 = 4mm in. Kept shallow so a leaning pick leaves through the keyhole, not the face round it (the Lock Plate's keyhole is 8mm in radius and 5.7mm deep); higher = tips deeper, and a leaning pick may poke through the plate.")]
        [SerializeField] private float pickDepth = 0.004f;
        [Tooltip("How far both picks' tips are moved up the keyhole from where they otherwise sit (the left behind the round top's centre, the right behind the slot's middle), in metres; negative is down. The hand angles are measured from the moved tips too. Applied on each Show(). e.g. 0.002 = both tips 2mm higher; 0 leaves them where the keyhole puts them.")]
        [SerializeField] private float pickRise;
        [Tooltip("How far the picks lean out of the face towards the player, in degrees: 0 is flat against the face, 90 straight out. e.g. 35 = handles tilted towards the player, easy to reach. Higher = picks stick out more; lower = they lie closer to the door.")]
        [SerializeField] private float pickLift = 35f;

        [Tooltip("How far along a pick from its tip in the keyhole a hand grips it, in metres: on the handle, which is the outer half. e.g. 0.15 = the hand holds 15cm out from the keyhole. Higher = grip nearer the handle's end; lower = nearer the lock. Keep below Pick Length.")]
        [SerializeField] private float gripAlong = 0.15f;

        [Header("Right Pick (turning)")]

        [Tooltip("Where the right (turning) pick starts, as a clock position on the face seen from the player, in degrees clockwise from 12. e.g. 30 = 1 o'clock, 90 = 3 o'clock. Its stops follow on clockwise from here.")]
        [SerializeField] private float rightStartAngle = 30f;

        [Tooltip("How far the right pick turns from one stop to the next, in degrees. There are four stops in all, evenly spaced from Right Start Angle: one per pin, and the last turn that unlocks. e.g. 28 with a start of 30 = stops at 58, 86, 114 and 142. Higher = bigger hand movements per stage; lower = smaller, fiddlier ones.")]
        [SerializeField] private float stopSpacing = 28f;

        [Tooltip("The right pick counts as at its stop within this many degrees of it, and must be turned back three times this before reaching the stop is felt again. e.g. 1.5 = at the stop within 1.5 degrees, re-armed 4.5 degrees back. Higher = stops easier to reach; lower = the hand must turn right up to them.")]
        [SerializeField] private float stopTolerance = 1.5f;

        [Tooltip("How fast the right pick swings back to its start when let go, in degrees a second. e.g. 240 = a full four-stop turn (112 degrees) springs back in about half a second. Higher = snaps back faster; lower = drifts back slowly.")]
        [SerializeField] private float returnSpeed = 240f;

        [Header("Left Pick (searching)")]

        [Tooltip("Where the left (searching) pick starts, as a clock position on the face seen from the player, in degrees clockwise from 12. e.g. -30 = 11 o'clock. It sweeps anticlockwise from here.")]
        [SerializeField] private float leftStartAngle = -30f;

        [Tooltip("How far the left pick sweeps anticlockwise from its start, down the left side of the face, in degrees; the pins are hidden somewhere along this sweep. e.g. 120 from 11 o'clock reaches 7 o'clock. Higher = more ground to search, so pins are harder to find; lower = a shorter, easier search.")]
        [SerializeField] private float leftArc = 120f;

        [Header("Pins")]

        // The left hand starts to feel the pin within this many degrees of
        // it, and has found it within pinSetAngle.
        [Tooltip("The left hand starts to feel the pin (through haptics) within this many degrees of it, growing stronger up to Pin Set Angle. e.g. 10 = a faint buzz 10 degrees away, full at 5. Higher = the pin is felt from further off, an easier search; lower = it must be almost found before anything is felt. Keep above Pin Set Angle.")]
        [SerializeField] private float pinFeelAngle = 10f;
        [Tooltip("Within this many degrees of the pin the left pick counts as on it and the hold timer runs. e.g. 5 = anywhere within 5 degrees either side sets it. Higher = easier to stay on the pin; lower = the hand must be held very still. Keep below Pin Feel Angle.")]
        [SerializeField] private float pinSetAngle = 5f;

        [Tooltip("How long the left pick must be held on the pin to set it, in seconds; leaving the pin early starts the count again. e.g. 1 = hold steady on it for one second. Higher = slower, tenser picking; lower = pins set almost as soon as they're found.")]
        [SerializeField] private float pinHoldTime = 1f;

        [Tooltip("A new pin is never put closer than this to where the left pick already is, in degrees, so it can't be found by doing nothing. e.g. 20 = the next pin is at least 20 degrees from the left pick. Higher = the left pick always has to travel further; 0 allows anywhere along the sweep.")]
        [SerializeField] private float minPinGap = 20f;

        [Header("Hands")]

        [Tooltip("A hand closer than this to the line through its pick's pivot, square to the face, in metres, has no clear clock angle, so the pick it holds stays where it is. e.g. 0.03 = within 3cm of the keyhole's axis the pick stops following the hand. Higher = less twitching near the middle but a bigger dead patch; lower = the pick follows closer in, but can jump about there; 0 turns it off.")]
        [SerializeField] private float deadRadius = 0.03f;

        [Header("Sound")]

        // The quiet click and scrape of picking, and the lock opening.
        // Both optional (empty = silent, and unheard by guards).
        [Tooltip("The SoundCue for the quiet click and scrape of picking, played at each stop, pin set, reset and scrape, from the real lock so guards hear it. e.g. drag a small metal-click cue here. Empty = picking is silent and guards hear nothing.")]
        [SerializeField] private SoundCue pickCue;
        [Tooltip("The SoundCue for the lock opening on the last turn, played at full volume from the real lock so guards hear it. e.g. drag a heavier clunk cue here. Empty = the lock opens silently.")]
        [SerializeField] private SoundCue unlockCue;

        // How loud the picking sounds are, and how far they carry to
        // guards, as a share of the cue's own volume and noise radius.
        [Tooltip("How loud the picking sounds are, as a share of the Pick Cue's own volume, 0 to 1. e.g. 0.4 = 40% of the cue's volume. Higher = louder clicks for the player; lower = quieter. Doesn't change what guards hear (see Pick Noise Scale).")]
        [SerializeField] private float pickVolume = 0.4f;
        [Tooltip("How far the picking sounds carry to guards, as a share of the Pick Cue's own noise radius, 0 to 1. e.g. 0.3 = guards hear it within 30% of the cue's radius. Higher = picking is riskier near guards; lower = safer; 0 = guards never hear picking.")]
        [SerializeField] private float pickNoiseScale = 0.3f;

        [Tooltip("The left pick scrapes (plays the Pick Cue) once every this many degrees it is swept. e.g. 25 = a scrape about every 25 degrees of sweep, four or five across the whole arc. Higher = fewer scrapes, a quieter search; lower = more often.")]
        [SerializeField] private float scrapeAngle = 25f;

        // The lockpicks model's two picks, by name: the hook works the
        // left of the lock, the tension wrench the right. Each lies along
        // its own Z from its tip (the origin) to its handle, 1 long, its
        // bent tip pointing down its -Y.
        public const string LeftPickName = "Lockpick_Hook";
        public const string RightPickName = "Lockpick_Wrench";

        /// The grip frame on a pick, in the pick's own space, turned from
        /// it: up along the pick, away from the keyhole, and facing its -Y
        /// (into the lock, square to the pick), so the hand closes round
        /// the pick from the player's side, as on a rope.
        public static readonly Quaternion GripTurn = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        // How far below the keyhole's round top the slot's middle is for a
        // lock with no DoorKeyhole (a chest's, say), in metres: the Lock
        // Plate model's (8mm radius, DoorKeyhole's default wedge).
        private const float DefaultSlotMiddle = (DoorKeyhole.SlotTop + DoorKeyhole.DefaultSlotBottom) * 0.5f * 0.008f;

        private Transform _leftPick;
        private Transform _rightPick;

        // Where each pick's tip sits and the pick turns about, pickDepth
        // into the keyhole, in this object's space: the left pick behind
        // the middle of the keyhole's round top (this object's origin),
        // the right behind the middle of the slot below it (set by Show(),
        // from the lock's keyhole).
        private Vector3 _leftPivot;
        private Vector3 _rightPivot;

        // The two picks.
        private MeshRenderer _leftPickRenderer;
        private MeshRenderer _rightPickRenderer;

        private bool _isShown;

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

        /// True while the lock is in view.
        public bool IsActive => _isShown;

        /// True while a hand can take a pick: in view and not yet open.
        public bool CanBeWorked => _isShown && !_isUnlocked;

        /// The two picks' renderers, for the player's side to burn in and
        /// away. Made in Awake().
        public MeshRenderer LeftPickRenderer => _leftPickRenderer;
        public MeshRenderer RightPickRenderer => _rightPickRenderer;

        /// How many pins have been set so far, 0 to PinCount.
        public int PinsSet { get; private set; }

        /// The right pick's clock angle, in degrees.
        public float RightAngle { get; private set; }

        /// How far the left pick has been swept from its start, in
        /// degrees (0 to the arc).
        public float LeftSweep { get; private set; }

        /// The left pick's clock angle, in degrees, about LeftPivot.
        public float LeftAngle => leftStartAngle - LeftSweep;

        /// Where the left pick's tip is and it turns, in this object's
        /// space: in the keyhole behind the middle of its round top.
        public Vector3 LeftPivot => _leftPivot;

        /// Where the right pick's tip is and it turns, in this object's
        /// space: in the keyhole behind the middle of its slot.
        public Vector3 RightPivot => _rightPivot;

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
            BuildPicks();
            PlacePivots(DefaultSlotMiddle);

            ApplyLeftSweep(0f);
            ApplyRightAngle(rightStartAngle);
            SetRenderersEnabled(false);
        }

        /// <summary>
        /// Makes the two picks' objects, scaled so the model (1 long) is
        /// pickLength long. Each turns about its tip, the model's origin
        /// (ApplyLeftSweep()/ApplyRightAngle()), placed at its pivot by
        /// PlacePivots(), and draws nothing until SetPickModel() gives it
        /// its mesh and materials.
        /// </summary>
        private void BuildPicks()
        {
            _leftPickRenderer = AddPart("Left Pick");
            _leftPick = _leftPickRenderer.transform;
            _leftPick.localScale = Vector3.one * pickLength;

            _rightPickRenderer = AddPart("Right Pick");
            _rightPick = _rightPickRenderer.transform;
            _rightPick.localScale = Vector3.one * pickLength;
        }

        /// <summary>
        /// Puts each pick's tip at its pivot, pickDepth into the keyhole:
        /// the left pick behind the keyhole's round top, the right behind
        /// the middle of the slot below it, slotMiddle metres below the
        /// round top's centre (DoorKeyhole.RestSlotMiddle), both raised
        /// pickRise. Called on each
        /// Show(), so the picks fit whichever lock is being picked.
        /// </summary>
        private void PlacePivots(float slotMiddle)
        {
            _leftPivot = new Vector3(0f, pickRise, pickDepth);
            _rightPivot = new Vector3(0f, slotMiddle + pickRise, pickDepth);

            _leftPick.localPosition = _leftPivot;
            _rightPick.localPosition = _rightPivot;
        }

        /// <summary>
        /// Gives the picks their looks from the lockpicks model (see
        /// LeftPickName/RightPickName): each pick's mesh, drawn with the
        /// model's own materials. Called once at load by
        /// PlayerLockpicking, which owns the model. A pick the model lacks
        /// stays unseen, with a warning.
        /// </summary>
        public void SetPickModel(GameObject model)
        {
            SetPickMesh(_leftPickRenderer, model, LeftPickName);
            SetPickMesh(_rightPickRenderer, model, RightPickName);
        }

        /// <summary>
        /// One pick: the mesh and materials of the model's child named
        /// pickName.
        /// </summary>
        private void SetPickMesh(MeshRenderer pick, GameObject model, string pickName)
        {
            Transform part = model.transform.Find(pickName);
            MeshFilter filter = part != null ? part.GetComponent<MeshFilter>() : null;

            if (filter == null) {
                Debug.LogWarning($"BigLock: the lockpicks model {model.name} has no {pickName} mesh, so that pick isn't shown.", this);
                return;
            }

            pick.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            pick.sharedMaterials = part.GetComponent<MeshRenderer>().sharedMaterials;
        }

        /// <summary>
        /// A child object with no mesh or material yet (given by
        /// SetPickModel()), and no shadows, like the player's other tools.
        /// </summary>
        private MeshRenderer AddPart(string partName)
        {
            GameObject part = new(partName) { layer = gameObject.layer };
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>();

            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return partRenderer;
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
        /// Brings the picks into view in target's keyhole, at the middle
        /// of its round top on the face the player is on, with everything
        /// back at the start and new pins. The picks fit the size of the
        /// lock's DoorKeyhole (none: the Lock Plate's).
        /// </summary>
        public void Show(PickableLock target, Vector3 position, Quaternion rotation)
        {
            Target = target;
            transform.SetPositionAndRotation(position, rotation);

            // Once per lock picked, not per frame. No keyhole: the Lock
            // Plate's.
            DoorKeyhole keyhole = DoorKeyhole.ForLock(target);
            PlacePivots(keyhole != null ? keyhole.RestSlotMiddle : DefaultSlotMiddle);

            _isShown = true;
            _isUnlocked = false;
            _isRightHeld = false;
            _isLeftHeld = false;
            _scrapeTravel = 0f;

            ApplyLeftSweep(0f);
            ApplyRightAngle(rightStartAngle);
            StartAgain();
            SetRenderersEnabled(true);
        }

        /// <summary>
        /// Takes the lock out of view at once. Any hand on a pick must
        /// already have been taken off it.
        /// </summary>
        public void Hide()
        {
            _isShown = false;
            SetRenderersEnabled(false);
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
        /// angle is the hand's, round the point that pick turns about,
        /// kept within what that pick can do. Called every frame a hand is
        /// on a pick.
        /// </summary>
        public void TurnPick(bool isLeftPick, Vector3 handPosition)
        {
            if (!_isShown || _isUnlocked) {
                return;
            }

            // The hand in the lock's own space from the pick's pivot: x to
            // the right, y up. (The object is unscaled, so this is in
            // metres.)
            Vector3 local = transform.InverseTransformPoint(handPosition) - (isLeftPick ? _leftPivot : _rightPivot);

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
        /// One frame of the lock: the right pick swinging back when nobody
        /// holds it, and the search for the pin.
        /// </summary>
        public void Tick(float deltaTime)
        {
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
        /// Switches both picks on or off together. Called only on
        /// Show()/Hide() (and once in Awake()).
        /// </summary>
        private void SetRenderersEnabled(bool isEnabled)
        {
            _leftPickRenderer.enabled = isEnabled;
            _rightPickRenderer.enabled = isEnabled;
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
        /// own Z) from its tip along a clock angle and leaning out of the
        /// face towards the player by pickLift.
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
        /// The grip frame on a pick as it is now, in the world: at the
        /// grip point, turned by GripTurn from the pick. The pick's own Y
        /// leans towards the player (see PickRotation()), so the frame
        /// faces into the lock, square to the pick. The hand's offsets
        /// from it are the player's (PlayerLockpicking's pick profile).
        /// Asked every frame a pick is held, since the pick turns.
        /// </summary>
        public void GetGripFrame(bool isLeftPick, out Vector3 position, out Quaternion rotation)
        {
            Transform pick = isLeftPick ? _leftPick : _rightPick;
            position = pick.position + pick.forward * gripAlong;
            rotation = pick.rotation * GripTurn;
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
        /// Plays cue from the real lock, where the sound truly is, for the
        /// player's ears and the guards'. With no sound player in the
        /// scene, the guards still hear it.
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
