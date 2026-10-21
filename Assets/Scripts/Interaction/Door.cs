using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A hinged door. The door turns about its local Y axis, its local X
    /// runs along the door, and its local Z is the way through the
    /// doorway. The hinge is at this object's origin, or hingeOffset away
    /// from it - for a model whose origin is somewhere else, such as the
    /// middle of the leaf. The door's solid part (the leaf) is this object
    /// or a child, and its DoorHandle, if it has one, is a child.
    ///
    /// A door with no DoorHandle has no latch to turn: pushing it, with a
    /// hand or the body, opens it from shut.
    ///
    /// A door has two separate states. Locked or unlocked: whether the
    /// handle will turn far enough to free it. Latched (closed) or open: a
    /// latched door is shut and can't move at all; an open one swings
    /// freely as far as maxOpenAngle - either way, or one way only (swing).
    ///
    /// The door only knows about itself. It's told what to do: the hand on
    /// its handle (Player.PlayerHandDoors) frees the latch and says what
    /// angle the hand has pulled it to, and when the hand lets go near the
    /// frame the door swings itself shut and latches. An open door with no
    /// hand on its handle is also pushed by a hand pressing on it (Push(),
    /// called by the physical hands), and swings on by itself for a
    /// moment after a push or a moving release.
    ///
    /// The angle is set from code, not worked out by physics: no hinge
    /// joint, no forces. The Rigidbody is kinematic and is only there
    /// because physics handles a collider that moves much more cheaply when
    /// it has one - and a kinematic body pushes loose props out of its way.
    ///
    /// Update() only runs while the door is moving with no hand on its
    /// handle: the component is disabled the rest of the time, so a door
    /// at rest costs nothing.
    ///
    /// Set up: this object and its children unscaled apart from the leaf
    /// itself, placed closed, on the Interactable layer.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Door : MonoBehaviour, IHandPushable, IDebugDrawable
    {
        [Header("Lock")]

        // What kind of lock the door has, and whether it starts locked. A
        // door with no lock can't be locked at all.
        [Tooltip("What kind of lock the door has: None (the handle always opens it), Simple (can be picked, through a PickableLock child) or Keyed (its own key only, through a KeyLock child). A door with no lock can't be locked at all.")]
        [SerializeField] private DoorLock lockType;
        [Tooltip("Whether the door starts the level locked. Only matters with a lock.")]
        [SerializeField] private bool startsLocked;

        [Tooltip("For a Keyed lock, the id its key must have: the same text as the Key's Key Id. Ignored for the other lock kinds.")]
        [SerializeField] private string keyId;

        [Header("Swing")]

        [Tooltip("The door's solid part. Its box is what is checked against the Blocking Layers before every move, so the door never swings into the player. Found when the component is added: the first non-trigger box below the door.")]
        [SerializeField] private BoxCollider leaf;

        [Tooltip("Where the hinge is from this object's origin, along the door's own axes, in metres. Zero means the object sits on the hinge; for a pack door whose origin is the middle of the leaf, half the leaf's width along X, with the sign choosing which edge is hinged. Only X and Z matter: the hinge line runs straight up through this point.")]
        [SerializeField] private Vector3 hingeOffset;

        [Tooltip("Which way the door opens: either way, or only out to one side of the doorway along the door's local Z (a door that shuts against a stop in its frame).")]
        [SerializeField] private DoorSwing swing;

        [Tooltip("How far the door opens from closed, in degrees, each way it is allowed to swing.")]
        [SerializeField] private float maxOpenAngle = 110f;

        [Tooltip("Let go of, or come to rest, within this many degrees of closed, the door shuts itself and latches. Further open, it stays where it was left.")]
        [SerializeField] private float closeAngle = 10f;

        [Tooltip("How fast the door turns itself shut from within the Close Angle, in degrees a second.")]
        [SerializeField] private float closeSpeed = 60f;

        [Tooltip("The layers the door will not swing into: the player's body, so a door pulled towards the player stops against them instead of passing through. Set to Player when the component is added.")]
        [SerializeField] private LayerMask blockingLayers;

        [Header("Free Swing")]

        [Tooltip("How quickly a door swinging by itself slows down: each second it loses this many times its speed, so 3 is down to a twentieth after a second. Higher is stiffer hinges.")]
        [SerializeField] private float swingDrag = 3f;

        [Tooltip("Slower than this, in degrees a second, a door swinging by itself counts as stopped, and either shuts itself or stays put.")]
        [SerializeField] private float minSwingSpeed = 5f;

        [Tooltip("The fastest a hand's or the body's push can turn the door, in degrees a second, so a hand shoved deep into it can't fling it round in one frame.")]
        [SerializeField] private float maxPushSpeed = 360f;

        [Header("Handle")]

        [Tooltip("How far the handle has to be turned to free the latch, in degrees, either way.")]
        [SerializeField] private float unlatchTwist = 60f;

        [Tooltip("How far the handle of a locked or bolted door turns before it stops, in degrees: enough to feel that it is held shut.")]
        [SerializeField] private float lockedTwist = 10f;

        [Header("Sound")]

        [Tooltip("The sound portal in this doorway. Closed while the door is latched and open while it isn't, so a shut door muffles what is behind it. Optional.")]
        [SerializeField] private SoundPortal soundPortal;

        // The latch freeing and catching, the rattle of a locked handle,
        // and the hinges. All optional (empty = silent).
        [Tooltip("The sound of the latch freeing and catching, played from the handle or the leaf so guards hear it too. Optional: empty is silent.")]
        [SerializeField] private SoundCue latchCue;
        [Tooltip("The rattle of the handle being turned while the door is locked or bolted. Optional: empty is silent.")]
        [SerializeField] private SoundCue lockedCue;
        [Tooltip("The creak of the hinges as the door moves, played once every Creak Interval of travel. Optional: empty is silent.")]
        [SerializeField] private SoundCue creakCue;

        [Tooltip("The hinges sound once every this many degrees the door moves.")]
        [SerializeField] private float creakInterval = 25f;

        // A point this close to the hinge line (metres) has no usable
        // direction round it - see TryGetBearing().
        private const float MinBearingRadius = 0.1f;

        // How many straight pieces the gizmo's arc is drawn with.
        private const int ArcSegments = 16;

        private static readonly Color _latchedColor = new(1f, 0.85f, 0.2f, 1f);
        private static readonly Color _lockedColor = new(1f, 0.3f, 0.3f, 1f);
        private static readonly Color _openColor = new(0.3f, 1f, 0.4f, 1f);

        // The door's rotation when closed, relative to its parent. Every
        // angle is a turn about Y on top of this.
        private Quaternion _closedLocalRotation;

        // The hinge, which never moves: where it is relative to the
        // door's parent, and how far it is from the door's origin along
        // the door's axes (hingeOffset times the door's scale). Turning
        // the door about it moves the origin - see MoveTo().
        private Vector3 _hingeInParent;
        private Vector3 _scaledHingeOffset;

        // True if the door has a DoorHandle. Without one, a push opens it
        // from shut - see Push().
        private bool _hasHandle;

        // The leaf's box, measured once from the hinge along the door's
        // axes (position and rotation, not scale, so the numbers are real
        // metres): where its middle is, half its size, and which way it's
        // turned.
        private Vector3 _leafCentre;
        private Vector3 _leafHalfSize;
        private Quaternion _leafRotation;
        private bool _hasLeaf;

        // Degrees moved since the hinges last sounded.
        private float _creakTravel;

        // How fast the door is turning, in degrees a second (signed like
        // Angle). Measured while a hand moves it, so it carries on from
        // there when the hand lets go or stops pushing; then it's what
        // Update() moves the door by, dying away with swingDrag.
        private float _angularVelocity;

        // The frame a hand last pushed the door - see Update(). Starts
        // well in the past.
        private int _lastPushFrame = -10;

        /// What kind of lock the door has.
        public DoorLock LockType => lockType;

        /// Which key opens it, for a Keyed lock.
        public string KeyId => keyId;

        /// True while the lock is holding the latch: the handle won't free
        /// it. Only ever true for a door with a lock.
        public bool IsLocked { get; private set; }

        /// True while a sliding bolt (DoorBolt) holds the door shut,
        /// whatever its lock is doing.
        public bool IsBolted { get; private set; }

        /// True while the lock or a bolt is holding the latch: the handle
        /// stops short and the door stays shut.
        public bool IsHeldShut => IsLocked || IsBolted;

        /// True while the door is shut and caught by its latch. It can't
        /// move until Unlatch().
        public bool IsLatched { get; private set; } = true;

        /// True while the door is free to swing - the opposite of
        /// IsLatched, whatever angle it's at.
        public bool IsOpen => !IsLatched;

        /// True while a hand is on the handle. One hand at a time.
        public bool IsHeld { get; private set; }

        /// How far open the door is, in degrees: 0 closed, positive
        /// turned clockwise seen from above, negative anticlockwise.
        public float Angle { get; private set; }

        /// The furthest the door turns each way, in degrees: from
        /// maxOpenAngle and which way it opens. One of them is 0 for a
        /// door that only opens one way.
        public float MinAngle { get; private set; }
        public float MaxAngle { get; private set; }

        public float UnlatchTwist => unlatchTwist;
        public float LockedTwist => lockedTwist;

        /// A point on the hinge line in the world. It stays where it is
        /// as the door turns.
        public Vector3 HingePosition => transform.TransformPoint(hingeOffset);

        /// The middle of the leaf in the world, right now - where the
        /// door's own sounds come from.
        public Vector3 LeafCentre => HingePosition + transform.rotation * _leafCentre;

        /// <summary>
        /// Editor-only: runs when the component is added. Finds the leaf -
        /// the first solid box below this object, since the handle's grab
        /// volume is a box too, but a trigger - and sets what blocks the
        /// door to the player's body.
        /// </summary>
        private void Reset()
        {
            BoxCollider[] boxes = GetComponentsInChildren<BoxCollider>();

            for (int i = 0; i < boxes.Length; i++) {
                if (!boxes[i].isTrigger) {
                    leaf = boxes[i];
                    break;
                }
            }

            blockingLayers = LayerMask.GetMask("Player");
        }

        private void Awake()
        {
            _closedLocalRotation = transform.localRotation;
            _scaledHingeOffset = Vector3.Scale(transform.localScale, hingeOffset);
            _hingeInParent = transform.localPosition + _closedLocalRotation * _scaledHingeOffset;
            IsLocked = startsLocked && lockType != DoorLock.None;
            _hasHandle = GetComponentInChildren<DoorHandle>() != null;

            // Moved from code, never by physics.
            GetComponent<Rigidbody>().isKinematic = true;

            MeasureLeaf();
            GetLimits(_leafCentre.x, out float minAngle, out float maxAngle);
            MinAngle = minAngle;
            MaxAngle = maxAngle;

            // Not OnEnable/OnDisable, as other registrations are: this
            // component disables itself whenever the door is at rest.
            DebugDrawRegistry.Register(this);

            // So a hand stopped by the leaf pushes the door - see Push().
            if (_hasLeaf) {
                HandPushRegistry.Register(leaf, this);
            }
        }

        /// <summary>
        /// Start() rather than Awake() for the sound portal: the portal
        /// sets itself open or closed in its own Awake(), which may run
        /// after this one. Then the component switches itself off until the
        /// door next has to move by itself.
        /// </summary>
        private void Start()
        {
            SetPortalOpen(false);
            enabled = false;
        }

        private void OnDestroy()
        {
            DebugDrawRegistry.Unregister(this);

            if (_hasLeaf) {
                HandPushRegistry.Unregister(leaf);
            }
        }

        /// <summary>
        /// Measures the leaf's box from the hinge along the door's axes, so
        /// where it would be at any angle can be worked out without moving
        /// it.
        /// </summary>
        private void MeasureLeaf()
        {
            _hasLeaf = leaf != null;

            if (!_hasLeaf) {
                Debug.LogWarning($"Door '{name}': no leaf collider assigned, so nothing can block it.", this);
                return;
            }

            Transform leafTransform = leaf.transform;
            Quaternion toDoor = Quaternion.Inverse(transform.rotation);
            Vector3 scale = leafTransform.lossyScale;

            _leafCentre = toDoor * (leafTransform.TransformPoint(leaf.center) - HingePosition);
            _leafRotation = toDoor * leafTransform.rotation;

            // A box's size is in its own unscaled units; times the scale
            // gives metres. Abs, since a scale can be negative.
            _leafHalfSize = new Vector3(
                Mathf.Abs(leaf.size.x * scale.x),
                Mathf.Abs(leaf.size.y * scale.y),
                Mathf.Abs(leaf.size.z * scale.z)) * 0.5f;
        }

        /// <summary>
        /// The furthest the door turns each way, from maxOpenAngle and
        /// which way it opens. leafSide is where the leaf's middle is
        /// along the door's X from the hinge: a positive turn about Y
        /// takes +X towards -Z, so it swings a leaf on the +X side out
        /// towards -Z and one on the -X side out towards +Z.
        /// </summary>
        private void GetLimits(float leafSide, out float minAngle, out float maxAngle)
        {
            minAngle = -maxOpenAngle;
            maxAngle = maxOpenAngle;

            if (swing == DoorSwing.BothWays) {
                return;
            }

            bool positiveIsMinusZ = leafSide >= 0f;

            if ((swing == DoorSwing.TowardsMinusZ) == positiveIsMinusZ) {
                minAngle = 0f;
            } else {
                maxAngle = 0f;
            }
        }

        /// <summary>
        /// Only runs while an open door is moving with no hand on its
        /// handle (the component is disabled otherwise). In order:
        /// - A hand pushed it this frame or last: the hand is moving it,
        ///   nothing to do here.
        /// - It still has speed: it swings on, slowing down (TickSwing()).
        /// - It has stopped within closeAngle of closed: it turns itself
        ///   shut and latches. If something is in the way it stops where
        ///   it is and stays open.
        /// - It has stopped further open: it stays there, and the
        ///   component switches itself off.
        /// </summary>
        private void Update()
        {
            // "Or last": this Update() may run before or after the player's
            // in a frame, and a hand pressing on the door pushes it every
            // frame it's stopped by it.
            if (Time.frameCount - _lastPushFrame <= 1) {
                return;
            }

            if (_angularVelocity != 0f) {
                TickSwing();
                return;
            }

            if (Mathf.Abs(Angle) > closeAngle) {
                enabled = false;
                return;
            }

            float angle = Mathf.MoveTowards(Angle, 0f, closeSpeed * Time.deltaTime);

            if (!MoveTo(angle)) {
                enabled = false;
                return;
            }

            if (Angle == 0f) {
                Latch();
                enabled = false;
            }
        }

        /// <summary>
        /// One frame of a door swinging by itself, after a hand let go of
        /// it moving or stopped pushing it: it turns by its speed, and the
        /// speed dies away. It stops dead at its limit or against the
        /// player. Reaching closed, the latch catches it - a door only
        /// swings through its frame while a hand is taking it through.
        /// </summary>
        private void TickSwing()
        {
            float deltaTime = Time.deltaTime;
            float target = Angle + _angularVelocity * deltaTime;

            // Reaching or passing closed: the two angles have different
            // signs (or one is zero), so multiplying them isn't positive.
            bool reachesClosed = target * Angle <= 0f;

            if (reachesClosed) {
                target = 0f;
            }

            float clamped = Mathf.Clamp(target, MinAngle, MaxAngle);
            bool moved = MoveTo(clamped);

            if (reachesClosed && moved) {
                Latch();
                enabled = false;
                return;
            }

            // Blocked, or at its limit: it stops. The next Update() decides
            // whether it then shuts itself or stays.
            if (!moved || clamped != target) {
                _angularVelocity = 0f;
                return;
            }

            // Losing a fixed share of its speed every moment is an
            // exponential fall: Exp(-drag x time) is the share left.
            _angularVelocity *= Mathf.Exp(-swingDrag * deltaTime);

            if (Mathf.Abs(_angularVelocity) < minSwingSpeed) {
                _angularVelocity = 0f;
            }
        }

        /// <summary>
        /// A hand has taken the handle. Stops the door moving by itself: it
        /// goes where the hand takes it from here.
        /// </summary>
        public void BeginHold()
        {
            IsHeld = true;
            _angularVelocity = 0f;
            enabled = false;
        }

        /// <summary>
        /// The hand has let go. An open door carries on at the speed the
        /// hand was moving it; once stopped, within closeAngle of closed it
        /// shuts itself and latches, and further open it stays put -
        /// Update() works through that.
        /// </summary>
        public void EndHold()
        {
            IsHeld = false;

            if (Mathf.Abs(_angularVelocity) < minSwingSpeed) {
                _angularVelocity = 0f;
            }

            if (IsOpen) {
                enabled = true;
            }
        }

        /// <summary>
        /// A hand has pressed on the leaf (IHandPushable - called by the
        /// hand's collision sweep). An open door with no hand on its
        /// handle turns out of the way: by as much as moving the pressed
        /// point by displacement would turn it round the hinge, so a push
        /// near the hinge turns it further than the same push at the
        /// handle edge, and a push along the door turns it not at all.
        /// It also takes up the speed it was pushed at, and swings on
        /// from there when the pushing stops. True if the door moved.
        ///
        /// A shut door only moves if it has no handle: then a push that
        /// would turn it frees it, unless it's locked or bolted.
        /// </summary>
        public bool Push(Vector3 point, Vector3 displacement)
        {
            float deltaTime = Time.deltaTime;

            if (IsHeld || deltaTime <= 0f) {
                return false;
            }

            if (IsLatched && (_hasHandle || IsHeldShut)) {
                return false;
            }

            // Once a frame: after a push the hand tries its move again,
            // and if the door couldn't get fully out of the way (it's
            // limited to maxPushSpeed) the hand meets it a second time.
            // That time the door holds, and the hand stops against it.
            if (_lastPushFrame == Time.frameCount) {
                return false;
            }

            if (!TryGetBearing(point, out float from) || !TryGetBearing(point + displacement, out float to)) {
                return false;
            }

            float maxStep = maxPushSpeed * deltaTime;
            float step = Mathf.Clamp(Mathf.DeltaAngle(from, to), -maxStep, maxStep);

            if (step == 0f) {
                return false;
            }

            float before = Angle;
            float target = Mathf.Clamp(Angle + step, MinAngle, MaxAngle);

            // A shut door with no handle: freed only by a push that turns
            // it, so one against the way it opens leaves it shut.
            if (IsLatched) {
                if (target == before) {
                    return false;
                }

                Unlatch(point);
            }

            MoveTo(target);
            TrackVelocity(before, deltaTime);

            _lastPushFrame = Time.frameCount;
            enabled = true;

            // Not moved = at its limit, or against the player's body.
            return Angle != before;
        }

        /// <summary>
        /// Updates the door's speed from how far it has just been moved,
        /// from the angle it was at before. Half the old value and half
        /// the new each time, so one jerky frame doesn't decide how the
        /// door swings on - and a hand that stops moving it brings the
        /// speed down to nothing within a few frames.
        /// </summary>
        private void TrackVelocity(float angleBefore, float deltaTime)
        {
            _angularVelocity = Mathf.Lerp(_angularVelocity, (Angle - angleBefore) / deltaTime, 0.5f);
        }

        /// <summary>
        /// Frees the latch, so the door can swing: called when the handle
        /// has been turned far enough. Does nothing to a locked or bolted
        /// door. soundPosition is where the click comes from (the handle).
        /// </summary>
        public void Unlatch(Vector3 soundPosition)
        {
            if (!IsLatched || IsHeldShut) {
                return;
            }

            IsLatched = false;
            SetPortalOpen(true);
            PlayCue(latchCue, soundPosition);
        }

        /// <summary>
        /// The handle of a locked door has been turned as far as it goes:
        /// plays the rattle.
        /// </summary>
        public void RattleLocked(Vector3 soundPosition)
        {
            PlayCue(lockedCue, soundPosition);
        }

        /// <summary>
        /// Turns the door to angle (degrees from closed, kept within
        /// MinAngle and MaxAngle), unless it's latched or the player's
        /// body is in the way.
        /// </summary>
        public void SetAngle(float angle)
        {
            float deltaTime = Time.deltaTime;

            if (IsLatched || deltaTime <= 0f) {
                return;
            }

            float before = Angle;
            MoveTo(Mathf.Clamp(angle, MinAngle, MaxAngle));
            TrackVelocity(before, deltaTime);
        }

        /// <summary>
        /// Locks the door, if it has a lock and is shut. For a key or a
        /// guard to call later. False if it couldn't be locked.
        /// </summary>
        public bool Lock()
        {
            if (lockType == DoorLock.None || !IsLatched) {
                return false;
            }

            IsLocked = true;
            return true;
        }

        /// <summary>
        /// Unlocks the door. For the lockpick or a key to call later. It
        /// stays shut: the handle still has to be turned.
        /// </summary>
        public void Unlock()
        {
            IsLocked = false;
        }

        /// <summary>
        /// Called by the door's DoorBolt when it's shot or drawn back. A
        /// bolt can only be shot while the door is shut, which the bolt
        /// itself sees to.
        /// </summary>
        public void SetBolted(bool bolted)
        {
            IsBolted = bolted;
        }

        [ContextMenu("Test Lock")]
        private void TestLock()
        {
            Lock();
        }

        [ContextMenu("Test Unlock")]
        private void TestUnlock()
        {
            Unlock();
        }

        /// <summary>
        /// Which way round the hinge a world point is, in degrees, measured
        /// the same way as Angle: a point along the closed door's +X from
        /// the hinge is at 0, and it grows clockwise seen from above. So a
        /// hand moving round the hinge changes its bearing by exactly as
        /// much as the door should turn. False (no bearing) for a point
        /// almost on the hinge line, where a tiny movement would swing the
        /// answer wildly.
        /// </summary>
        public bool TryGetBearing(Vector3 worldPoint, out float bearing)
        {
            // Into the closed door's frame: take away the hinge's position,
            // then undo the closed rotation.
            Vector3 local = Quaternion.Inverse(ClosedRotation()) * (worldPoint - HingePosition);
            bearing = 0f;

            if (local.x * local.x + local.z * local.z < MinBearingRadius * MinBearingRadius) {
                return false;
            }

            // A turn of +angle about Y takes +X towards -Z, hence the minus.
            bearing = Mathf.Atan2(-local.z, local.x) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>
        /// The door's rotation in the world when closed.
        /// </summary>
        private Quaternion ClosedRotation()
        {
            Transform parent = transform.parent;
            return parent != null ? parent.rotation * _closedLocalRotation : _closedLocalRotation;
        }

        /// <summary>
        /// Turns the door to angle and sounds the hinges as it goes. False
        /// if the way is blocked and it didn't move.
        /// </summary>
        private bool MoveTo(float angle)
        {
            if (angle == Angle) {
                return true;
            }

            // Blocked only if moving would put the door INTO something it
            // isn't already in: if the body somehow overlaps it now, it
            // must still be able to swing clear. The second check is only
            // made when the first one finds something.
            if (IsBlockedAt(angle) && !IsBlockedAt(Angle)) {
                return false;
            }

            _creakTravel += Mathf.Abs(angle - Angle);
            Angle = angle;

            // Turning about the hinge rather than the origin: the origin
            // goes round the hinge, so it's put back hingeOffset away from
            // the hinge along the door's new axes. With no offset the
            // position doesn't change.
            Quaternion localRotation = _closedLocalRotation * Quaternion.AngleAxis(angle, Vector3.up);
            transform.SetLocalPositionAndRotation(_hingeInParent - localRotation * _scaledHingeOffset, localRotation);

            // Physics normally only learns that a collider has moved at
            // its next step (50 a second), but the hands sweep against the
            // leaf every frame: against a stale leaf, a hand would push a
            // door that had already moved out of its way. This tells
            // physics now. Only on frames a door actually moves.
            Physics.SyncTransforms();

            if (_creakTravel >= creakInterval) {
                _creakTravel = 0f;
                PlayCue(creakCue, LeafCentre);
            }

            return true;
        }

        /// <summary>
        /// Whether the leaf would overlap anything on blockingLayers with
        /// the door at angle. One box check, worked out from the leaf's
        /// measured size - nothing is moved to find out.
        /// </summary>
        private bool IsBlockedAt(float angle)
        {
            if (!_hasLeaf) {
                return false;
            }

            Quaternion rotation = ClosedRotation() * Quaternion.AngleAxis(angle, Vector3.up);
            Vector3 centre = HingePosition + rotation * _leafCentre;

            return Physics.CheckBox(centre, _leafHalfSize, rotation * _leafRotation, blockingLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// The door has reached closed: caught by the latch again.
        /// </summary>
        private void Latch()
        {
            IsLatched = true;
            _angularVelocity = 0f;
            _creakTravel = 0f;
            SetPortalOpen(false);
            PlayCue(latchCue, LeafCentre);
        }

        /// <summary>
        /// Opens or closes the doorway for sound, if the door has a portal.
        /// </summary>
        private void SetPortalOpen(bool open)
        {
            if (soundPortal != null) {
                soundPortal.SetOpen(open);
            }
        }

        /// <summary>
        /// Plays one of the door's sounds: the player hears it, and guards
        /// hear it as a noise. Nothing for an empty cue.
        /// </summary>
        private void PlayCue(SoundCue cue, Vector3 position)
        {
            if (cue == null) {
                return;
            }

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(cue, position, transform);
            } else {
                // A scene with no sound player (a test scene): guards
                // still hear it.
                cue.EmitNoise(position, transform);
            }
        }

        /// <summary>
        /// While selected, draws how far the door swings - see DrawDebug().
        /// Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// In the detailed view (selected, or in the headset with detail
        /// on), draws the hinge line and the door's swing on the floor: the
        /// arc its free edge travels through, a line at each limit and at
        /// closed, and a line each side at closeAngle - inside those it
        /// shuts itself. Red while locked, yellow while latched, green
        /// while open.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!detailed || leaf == null) {
                return;
            }

            // Awake() hasn't run in the editor: there the door is as placed,
            // which is closed.
            bool isPlaying = Application.isPlaying;
            Quaternion closed = isPlaying ? ClosedRotation() : transform.rotation;
            Vector3 hinge = HingePosition;

            // How far the free edge is from the hinge: the far end of the
            // leaf's box along the door. Negative for a leaf on the -X
            // side of the hinge, so the arc is drawn on the leaf's side.
            Transform leafTransform = leaf.transform;
            Vector3 leafScale = leafTransform.lossyScale;
            Vector3 leafCentre = Quaternion.Inverse(transform.rotation) * (leafTransform.TransformPoint(leaf.center) - hinge);
            float reach = Mathf.Abs(leafCentre.x) + Mathf.Abs(leaf.size.x * leafScale.x) * 0.5f;

            if (leafCentre.x < 0f) {
                reach = -reach;
            }

            // Worked out here, not read from MinAngle and MaxAngle, which
            // Awake() sets: in the editor it hasn't run.
            GetLimits(leafCentre.x, out float minAngle, out float maxAngle);

            // In the editor nothing has run yet: go by how it will start.
            bool isLocked = isPlaying ? IsLocked : startsLocked && lockType != DoorLock.None;

            if (isLocked) {
                lines.Color = _lockedColor;
            } else {
                lines.Color = !isPlaying || IsLatched ? _latchedColor : _openColor;
            }

            // The hinge line, as tall as the leaf: for placing hingeOffset.
            Vector3 up = closed * Vector3.up;
            float halfHeight = Mathf.Abs(leaf.size.y * leafScale.y) * 0.5f;
            lines.Line(hinge + up * (leafCentre.y - halfHeight), hinge + up * (leafCentre.y + halfHeight));

            Vector3 previous = ArcPoint(hinge, closed, minAngle, reach);

            for (int i = 1; i <= ArcSegments; i++) {
                float angle = Mathf.Lerp(minAngle, maxAngle, i / (float)ArcSegments);
                Vector3 point = ArcPoint(hinge, closed, angle, reach);
                lines.Line(previous, point);
                previous = point;
            }

            lines.Line(hinge, ArcPoint(hinge, closed, minAngle, reach));
            lines.Line(hinge, ArcPoint(hinge, closed, maxAngle, reach));
            lines.Line(hinge, ArcPoint(hinge, closed, 0f, reach));
            lines.Line(hinge, ArcPoint(hinge, closed, Mathf.Max(-closeAngle, minAngle), reach * 0.5f));
            lines.Line(hinge, ArcPoint(hinge, closed, Mathf.Min(closeAngle, maxAngle), reach * 0.5f));
        }

        /// <summary>
        /// Where a point reach metres along the door's X from the hinge
        /// is, with the door at angle.
        /// </summary>
        private static Vector3 ArcPoint(Vector3 hinge, Quaternion closed, float angle, float reach)
        {
            return hinge + closed * Quaternion.AngleAxis(angle, Vector3.up) * new Vector3(reach, 0f, 0f);
        }
    }
}
