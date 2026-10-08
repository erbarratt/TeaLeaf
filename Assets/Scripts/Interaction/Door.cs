using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A hinged door. This object sits on the hinge: the door turns about
    /// its local Y axis, its local X runs along the door towards the edge
    /// with the handle, and its local Z is the way through the doorway. The
    /// door's solid part (the leaf) and its DoorHandle are children.
    ///
    /// A door has two separate states. Locked or unlocked: whether the
    /// handle will turn far enough to free it. Latched (closed) or open: a
    /// latched door is shut and can't move at all; an open one swings
    /// freely, either way, as far as maxOpenAngle.
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
        [SerializeField] private DoorLock lockType;
        [SerializeField] private bool startsLocked;

        // Which key opens a Keyed lock. Not used yet: keys come with the
        // inventory.
        [SerializeField] private string keyId;

        [Header("Swing")]

        // The door's solid part. Its box is what's checked against the
        // player's body - see IsBlockedAt().
        [SerializeField] private BoxCollider leaf;

        // How far the door opens each way from closed, in degrees.
        [SerializeField] private float maxOpenAngle = 110f;

        // Let go of within this many degrees of closed, the door shuts
        // itself and latches. Further open, it stays where it was left.
        [SerializeField] private float closeAngle = 10f;

        // How fast it shuts itself, in degrees a second.
        [SerializeField] private float closeSpeed = 60f;

        // What stops the door: it won't swing into anything on these
        // layers. The player's body, so a door pulled towards the player
        // stops against them instead of passing through.
        [SerializeField] private LayerMask blockingLayers;

        [Header("Free Swing")]

        // How quickly a door swinging by itself slows down: each second
        // it loses this many times its speed (so 3 = down to a twentieth
        // after a second). Higher = stiffer hinges.
        [SerializeField] private float swingDrag = 3f;

        // Slower than this, in degrees a second, a door swinging by
        // itself counts as stopped.
        [SerializeField] private float minSwingSpeed = 5f;

        // The fastest a hand's push can turn the door, in degrees a
        // second - so a hand shoved deep into it can't fling it round in
        // one frame.
        [SerializeField] private float maxPushSpeed = 360f;

        [Header("Handle")]

        // How far the handle has to be turned to free the latch, in
        // degrees, either way.
        [SerializeField] private float unlatchTwist = 60f;

        // How far the handle of a locked door turns before it stops, in
        // degrees - enough to feel that it's locked.
        [SerializeField] private float lockedTwist = 10f;

        [Header("Sound")]

        // The sound opening this doorway joins two rooms for. Optional:
        // closed while the door is latched, open while it isn't, so a shut
        // door muffles what's behind it.
        [SerializeField] private SoundPortal soundPortal;

        // The latch freeing and catching, the rattle of a locked handle,
        // and the hinges. All optional (empty = silent).
        [SerializeField] private SoundCue latchCue;
        [SerializeField] private SoundCue lockedCue;
        [SerializeField] private SoundCue creakCue;

        // The hinges sound once every this many degrees the door moves.
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

        // The leaf's box, measured once in the door's own frame (position
        // and rotation, not scale, so the numbers are real metres): where
        // its middle is, half its size, and which way it's turned.
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

        /// True while the door is shut and caught by its latch. It can't
        /// move until Unlatch().
        public bool IsLatched { get; private set; } = true;

        /// True while the door is free to swing - the opposite of
        /// IsLatched, whatever angle it's at.
        public bool IsOpen => !IsLatched;

        /// True while a hand is on the handle. One hand at a time.
        public bool IsHeld { get; private set; }

        /// How far open the door is, in degrees: 0 closed, positive
        /// swinging towards its local -Z, negative towards +Z.
        public float Angle { get; private set; }

        public float MaxOpenAngle => maxOpenAngle;
        public float UnlatchTwist => unlatchTwist;
        public float LockedTwist => lockedTwist;

        /// The middle of the leaf in the world, right now - where the
        /// door's own sounds come from.
        public Vector3 LeafCentre => transform.position + transform.rotation * _leafCentre;

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
            IsLocked = startsLocked && lockType != DoorLock.None;

            // Moved from code, never by physics.
            GetComponent<Rigidbody>().isKinematic = true;

            MeasureLeaf();

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
        /// Measures the leaf's box in the door's own frame, so where it
        /// would be at any angle can be worked out without moving it.
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

            _leafCentre = toDoor * (leafTransform.TransformPoint(leaf.center) - transform.position);
            _leafRotation = toDoor * leafTransform.rotation;

            // A box's size is in its own unscaled units; times the scale
            // gives metres. Abs, since a scale can be negative.
            _leafHalfSize = new Vector3(
                Mathf.Abs(leaf.size.x * scale.x),
                Mathf.Abs(leaf.size.y * scale.y),
                Mathf.Abs(leaf.size.z * scale.z)) * 0.5f;
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

            float clamped = Mathf.Clamp(target, -maxOpenAngle, maxOpenAngle);
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
        /// </summary>
        public bool Push(Vector3 point, Vector3 displacement)
        {
            float deltaTime = Time.deltaTime;

            if (IsLatched || IsHeld || deltaTime <= 0f) {
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
            MoveTo(Mathf.Clamp(Angle + step, -maxOpenAngle, maxOpenAngle));
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
        /// has been turned far enough. Does nothing to a locked door.
        /// soundPosition is where the click comes from (the handle).
        /// </summary>
        public void Unlatch(Vector3 soundPosition)
        {
            if (!IsLatched || IsLocked) {
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
        /// maxOpenAngle either way), unless it's latched or the player's
        /// body is in the way.
        /// </summary>
        public void SetAngle(float angle)
        {
            float deltaTime = Time.deltaTime;

            if (IsLatched || deltaTime <= 0f) {
                return;
            }

            float before = Angle;
            MoveTo(Mathf.Clamp(angle, -maxOpenAngle, maxOpenAngle));
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
        /// the same way as Angle: a point on the closed door's handle edge
        /// is at 0, and it grows as the door opens towards local -Z. So a
        /// hand moving round the hinge changes its bearing by exactly as
        /// much as the door should turn. False (no bearing) for a point
        /// almost on the hinge line, where a tiny movement would swing the
        /// answer wildly.
        /// </summary>
        public bool TryGetBearing(Vector3 worldPoint, out float bearing)
        {
            // Into the closed door's frame: take away the hinge's position,
            // then undo the closed rotation.
            Vector3 local = Quaternion.Inverse(ClosedRotation()) * (worldPoint - transform.position);
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
            transform.localRotation = _closedLocalRotation * Quaternion.AngleAxis(angle, Vector3.up);

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
            Vector3 centre = transform.position + rotation * _leafCentre;

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
        /// on), draws the door's swing on the floor: the arc its handle
        /// edge travels through, a line at each limit and at closed, and a
        /// line each side at closeAngle - inside those it shuts itself.
        /// Red while locked, yellow while latched, green while open.
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
            Vector3 hinge = transform.position;

            // How far the handle edge is from the hinge: the far end of the
            // leaf's box along the door.
            Transform leafTransform = leaf.transform;
            Vector3 leafCentre = Quaternion.Inverse(transform.rotation) * (leafTransform.TransformPoint(leaf.center) - hinge);
            float reach = Mathf.Abs(leafCentre.x) + Mathf.Abs(leaf.size.x * leafTransform.lossyScale.x) * 0.5f;

            // In the editor nothing has run yet: go by how it will start.
            bool isLocked = isPlaying ? IsLocked : startsLocked && lockType != DoorLock.None;

            if (isLocked) {
                lines.Color = _lockedColor;
            } else {
                lines.Color = !isPlaying || IsLatched ? _latchedColor : _openColor;
            }

            Vector3 previous = ArcPoint(hinge, closed, -maxOpenAngle, reach);

            for (int i = 1; i <= ArcSegments; i++) {
                float angle = Mathf.Lerp(-maxOpenAngle, maxOpenAngle, i / (float)ArcSegments);
                Vector3 point = ArcPoint(hinge, closed, angle, reach);
                lines.Line(previous, point);
                previous = point;
            }

            lines.Line(hinge, ArcPoint(hinge, closed, -maxOpenAngle, reach));
            lines.Line(hinge, ArcPoint(hinge, closed, maxOpenAngle, reach));
            lines.Line(hinge, ArcPoint(hinge, closed, 0f, reach));
            lines.Line(hinge, ArcPoint(hinge, closed, -closeAngle, reach * 0.5f));
            lines.Line(hinge, ArcPoint(hinge, closed, closeAngle, reach * 0.5f));
        }

        /// <summary>
        /// Where a point reach metres along the door from the hinge is,
        /// with the door at angle.
        /// </summary>
        private static Vector3 ArcPoint(Vector3 hinge, Quaternion closed, float angle, float reach)
        {
            return hinge + closed * Quaternion.AngleAxis(angle, Vector3.up) * new Vector3(reach, 0f, 0f);
        }
    }
}
