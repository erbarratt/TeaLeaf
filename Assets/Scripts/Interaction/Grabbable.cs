using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A physics prop a hand can pick up, hold and drop: a crate, a bottle,
    /// later loot and tools. It's a hand target, so the hand ray and reticle
    /// find it like a ledge, and PlayerHandHolding picks it up while grip is
    /// held.
    ///
    /// Where it sits in the hand is authored, the same way a ledge grip is:
    /// the prop works out a grip frame (where on it the hand holds it, and
    /// which way round), and the shared HandSnapProfile turns that frame
    /// into each hand's pose. The prop is then carried at exactly that pose
    /// relative to the hand, however it was lying when it was picked up.
    ///
    /// The grip frame depends on gripShape. Point: always gripPoint, so the
    /// prop is held one way - right for something with a handle. Cylinder:
    /// the hand takes hold on the player's side of the prop (the side
    /// nearest their head), at the height it's reaching for, as a hand
    /// grips a hanging rope - right for a bottle or a candlestick, which
    /// have no "front", so the hand never has to go round to the far side.
    ///
    /// While held it stops being a physics object the world pushes around:
    /// its Rigidbody is kinematic (moved by the hand, unaffected by gravity
    /// or knocks) and it's moved onto the hands' physics layer, so the
    /// player's own body and hands don't collide with it and hand rays pass
    /// through it. Dropping puts both back.
    ///
    /// Set up: a Rigidbody and collider(s) on the Interactable layer.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour, IHandTarget, IHandSnapTarget, IDebugDrawable
    {
        /// How the hand takes hold - see the class comment.
        public enum GripShape
        {
            // One fixed grip: the grip point, as placed.
            Point,
            // Anywhere round a cylinder, from the hand's side.
            Cylinder
        }

        [SerializeField] private GripShape gripShape;

        // The grip frame: a child placed where the hand holds the prop. Left
        // empty, the prop's own origin and rotation are used. For a
        // cylinder it's the middle of the part that can be gripped, with
        // its Y axis along the cylinder, pointing to the prop's top.
        [SerializeField] private Transform gripPoint;

        [Header("Cylinder")]

        // The cylinder's radius, in metres: the grip frame sits on its
        // surface, so one profile fits cylinders of different widths.
        [SerializeField] private float cylinderRadius = 0.04f;

        // How much of the cylinder's length can be gripped, in metres,
        // centred on the grip point. The hand grips level with where it
        // aimed, kept within this. 0 = always at the grip point's height.
        [SerializeField] private float cylinderGripLength = 0.1f;

        // How far round the cylinder each hand's grip is turned from
        // pointing its wrist straight at the player's head, in degrees, so
        // the wrist points at that hand's shoulder instead - as for a
        // hanging rope (ClimbableRope.shoulderAngle).
        [SerializeField] private float shoulderAngle = 20f;

        // Gizmo colour for the cylinder's grippable part.
        private static readonly Color _gizmoColor = new(0.3f, 0.8f, 1f, 1f);

        [Header("Holding")]

        // Per-hand offsets from the grip frame to the hand visual, shared by
        // every prop held the same way. Left empty, the hand sits exactly
        // on the grip frame.
        [SerializeField] private HandSnapProfile snapProfile;

        // The prop's size for the hand's collision while it's carried: the
        // hand stops when a ball this big round the prop's middle would
        // touch a surface, so the prop doesn't go into walls. In metres.
        [SerializeField] private float holdRadius = 0.15f;

        // How far beyond the prop's bounds WakeNeighbours() looks, in
        // metres, and the colliders it found - shared by every prop, since
        // only one is picked up at a time.
        private const float WakeMargin = 0.05f;
        private static readonly Collider[] _wakeBuffer = new Collider[16];

        private Rigidbody _rigidbody;

        // How the Rigidbody was set to detect collisions in the scene,
        // put back whenever the prop is picked up - see EndHold().
        private CollisionDetectionMode _originalDetectionMode;

        // Every collider of the prop, and the layer each one's object was on
        // before the prop was picked up.
        private Collider[] _colliders;
        private int[] _originalLayers;

        // True from BeginHold() until RestoreLayers(): the colliders are on
        // the hands' layer and _originalLayers holds their own.
        private bool _isOnHeldLayer;

        /// True while a hand is holding this.
        public bool IsHeld { get; private set; }

        public float HoldRadius => holdRadius;

        /// The middle of the prop's colliders in the world, right now.
        public Vector3 WorldCentre => transform.position + transform.rotation * LocalCentre;

        /// A prop can be picked up from further away than a ledge can be
        /// gripped - see IHandTarget.HasLongReach.
        public bool HasLongReach => true;

        /// <summary>
        /// The middle of the prop's colliders, as an offset from its origin
        /// in real metres along its own axes (rotation only, so a scaled
        /// prop needs no special handling). Measured once, in Awake().
        /// </summary>
        public Vector3 LocalCentre { get; private set; }

        /// <summary>
        /// Editor-only: runs when the component is added. Guesses the hold
        /// radius from the prop's colliders.
        /// </summary>
        private void Reset()
        {
            if (TryGetColliderBounds(GetComponentsInChildren<Collider>(), out Bounds bounds)) {
                Vector3 extents = bounds.extents;
                holdRadius = Mathf.Max(extents.x, extents.y, extents.z);
            }
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _originalDetectionMode = _rigidbody.collisionDetectionMode;
            _colliders = GetComponentsInChildren<Collider>();
            _originalLayers = new int[_colliders.Length];

            if (TryGetColliderBounds(_colliders, out Bounds bounds)) {
                LocalCentre = Quaternion.Inverse(transform.rotation) * (bounds.center - transform.position);
            }
        }

        private void OnEnable()
        {
            for (int i = 0; i < _colliders.Length; i++) {
                HandTargetRegistry.Register(_colliders[i], this);
            }

            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            for (int i = 0; i < _colliders.Length; i++) {
                HandTargetRegistry.Unregister(_colliders[i]);
            }

            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// The world-space box round every solid (non-trigger) collider in
        /// colliders. False if there are none.
        /// </summary>
        private static bool TryGetColliderBounds(Collider[] colliders, out Bounds bounds)
        {
            bounds = default;
            bool found = false;

            for (int i = 0; i < colliders.Length; i++) {
                if (colliders[i].isTrigger) {
                    continue;
                }

                if (found) {
                    bounds.Encapsulate(colliders[i].bounds);
                } else {
                    bounds = colliders[i].bounds;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Where the hand visual would have to be to hold the prop as it
        /// lies right now: the grip frame, with the profile's offset for
        /// that hand. PlayerHandHolding sends the hand visual out to this
        /// pose to take hold, and carries the prop at the same pose relative
        /// to the hand from then on. grabPoint (where the hand ray hit the
        /// prop) and headPosition only matter to a cylinder: the height and
        /// the side the hand grips at.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint, Vector3 headPosition, Vector3 headForward)
        {
            GetGripFrame(out Vector3 gripPosition, out Quaternion gripRotation);

            if (gripShape == GripShape.Cylinder) {
                GetCylinderGripFrame(isLeftHand, grabPoint, headPosition, ref gripPosition, ref gripRotation);
            }

            if (snapProfile == null) {
                return new HandSnapPose(gripPosition, gripRotation, default);
            }

            return snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }

        /// <summary>
        /// The grip point's world position and rotation - or the prop's own,
        /// if it has no grip point.
        /// </summary>
        private void GetGripFrame(out Vector3 position, out Quaternion rotation)
        {
            // == null rather than "is null": a serialized reference that was
            // never assigned is a Unity "fake null" object in the editor.
            Transform frame = gripPoint == null ? transform : gripPoint;
            frame.GetPositionAndRotation(out position, out rotation);
        }

        /// <summary>
        /// Turns the grip point's frame (passed in) into the frame for a
        /// hand gripping the cylinder, worked out as a hanging rope's is: on
        /// the cylinder's surface level with grabPoint, on the player's side
        /// of it, facing straight in at the axis, with up along the axis.
        /// The side comes from the head, not from where the hand or its ray
        /// happened to be, so the grip looks the same every time.
        /// </summary>
        private void GetCylinderGripFrame(bool isLeftHand, Vector3 grabPoint, Vector3 headPosition, ref Vector3 position, ref Quaternion rotation)
        {
            Vector3 up = rotation * Vector3.up;

            // How far along the axis grabPoint is from the grip point (the
            // dot product measures one vector along another), kept within
            // the part that can be gripped.
            float halfLength = cylinderGripLength * 0.5f;
            float along = Mathf.Clamp(Vector3.Dot(grabPoint - position, up), -halfLength, halfLength);
            Vector3 axisPoint = position + up * along;

            // Which side the hand goes on: towards the player's head,
            // square to the axis, then turned round the axis by
            // shoulderAngle so the wrist points at that hand's shoulder
            // rather than the middle of the face. Seen from above that's
            // anticlockwise for the right hand and clockwise for the left;
            // Unity's positive angles about an upward axis are clockwise
            // from above, so the right hand's is negative. The axis must
            // point up for that to hold, even if the prop is upside down.
            Vector3 upAlongAxis = up.y >= 0f ? up : -up;
            Vector3 side = Vector3.ProjectOnPlane(headPosition - axisPoint, up);
            float angle = isLeftHand ? shoulderAngle : -shoulderAngle;
            side = Quaternion.AngleAxis(angle, upAlongAxis) * side;

            // Fall back to the side the hand ray hit, then the grip point's
            // own back, if that came out as (almost) nothing - e.g. the
            // head directly above a standing bottle.
            if (side.sqrMagnitude < 0.000001f) {
                side = Vector3.ProjectOnPlane(grabPoint - axisPoint, up);
            }

            if (side.sqrMagnitude < 0.000001f) {
                side = rotation * Vector3.back;
            }

            side.Normalize();
            position = axisPoint + side * cylinderRadius;
            rotation = Quaternion.LookRotation(-side, up);
        }

        /// <summary>
        /// While selected, draws the part of a cylinder that can be gripped
        /// - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// For a cylinder, draws its grippable part: the axis, and (in the
        /// detailed view, when selected) the surface the grip frame sits on,
        /// for checking cylinderRadius and cylinderGripLength against the
        /// model. Nothing for a point grip - its grip point is an object
        /// that can be seen and selected itself.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (gripShape != GripShape.Cylinder) {
                return;
            }

            GetGripFrame(out Vector3 position, out Quaternion rotation);
            Vector3 halfAxis = rotation * Vector3.up * (cylinderGripLength * 0.5f);

            lines.Color = _gizmoColor;
            lines.Line(position - halfAxis, position + halfAxis);

            if (detailed) {
                lines.WireCapsule(position - halfAxis, position + halfAxis, cylinderRadius);
            }
        }

        /// <summary>
        /// Called by PlayerHandHolding as a hand picks the prop up: physics
        /// stops moving it, and it goes onto heldLayer (the hands' layer).
        /// </summary>
        public void BeginHold(int heldLayer)
        {
            IsHeld = true;

            // Back to its own setting before going kinematic: a throw
            // leaves the prop on continuous detection (see EndHold()),
            // which a kinematic body doesn't support.
            _rigidbody.collisionDetectionMode = _originalDetectionMode;
            _rigidbody.isKinematic = true;

            // No smoothing between physics steps while it's in the hand
            // (see EndHold()): the hand places it every frame through its
            // parent, and smoothing would pull it back towards where
            // physics last had it.
            _rigidbody.interpolation = RigidbodyInterpolation.None;

            // Already there if it was dropped a moment ago and its layers
            // haven't been put back yet - then the remembered layers are
            // still the right ones.
            if (_isOnHeldLayer) {
                return;
            }

            _isOnHeldLayer = true;

            for (int i = 0; i < _colliders.Length; i++) {
                GameObject colliderObject = _colliders[i].gameObject;
                _originalLayers[i] = colliderObject.layer;
                colliderObject.layer = heldLayer;
            }
        }

        /// <summary>
        /// Called by PlayerHandHolding when the hand has reached the prop
        /// and is about to carry it off (BeginHold() was the start of the
        /// reach; until now the prop has stayed where it lay, still solid,
        /// so anything stacked on it stayed put). From here physics
        /// ignores it altogether, like the hand visual, which has no
        /// collider: a kinematic body still shoves any physics body it's
        /// moved into, so a carried prop would sweep other props off a
        /// table. It still stops AT things - the hand's own sweep covers
        /// the prop (PlayerHandVisuals.SetHeldShape) and holds the hand
        /// back. EndHold() makes it solid again.
        /// </summary>
        public void BeginCarry()
        {
            WakeNeighbours();
            _rigidbody.detectCollisions = false;
        }

        /// <summary>
        /// Wakes every physics body touching or very near this prop. A
        /// body that has come to rest is put to sleep by physics - it
        /// isn't simulated at all, gravity included, until something
        /// touches it. Taking this prop out of physics (BeginCarry) touches
        /// nothing, so props resting on it would stay asleep, hanging in
        /// the air. Woken, they notice nothing is under them and fall;
        /// one with something still under it just goes back to sleep.
        /// </summary>
        private void WakeNeighbours()
        {
            if (!TryGetColliderBounds(_colliders, out Bounds bounds)) {
                return;
            }

            // The box round the prop, a little bigger so it reaches into
            // whatever rests against it. NonAlloc fills the shared buffer
            // instead of making a new array; more neighbours than fit are
            // woken anyway by the ones that were, as they start to move.
            Vector3 halfSize = bounds.extents + Vector3.one * WakeMargin;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, halfSize, _wakeBuffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++) {
                Rigidbody neighbour = _wakeBuffer[i].attachedRigidbody;

                // Static geometry has no Rigidbody; and not this prop.
                if (neighbour != null && neighbour != _rigidbody) {
                    neighbour.WakeUp();
                }

                _wakeBuffer[i] = null;
            }
        }

        /// <summary>
        /// Called by PlayerHandHolding as the hand lets go: under physics
        /// again, starting with the velocity and spin (radians a second
        /// about the axis the vector points along) it's given. Both zero
        /// is a drop from rest; anything else is a throw. It stays on the
        /// hands' layer until RestoreLayers(): the hand that let go is
        /// still wrapped round it, and on its own layer the hand's
        /// collision would shove the hand visual out of it.
        /// </summary>
        public void EndHold(Vector3 velocity, Vector3 angularVelocity)
        {
            IsHeld = false;

            // Velocity can only be set on a body physics is moving, so
            // after the switch back.
            _rigidbody.isKinematic = false;

            // Solid again - see BeginCarry().
            _rigidbody.detectCollisions = true;

            // Physics only moves a body 50 times a second (the project's
            // Fixed Timestep, 0.02s), but the headset draws 72-120 frames a
            // second - so without this a falling or thrown prop sits still
            // for a frame or two, then jumps: smooth physics that looks
            // like a low frame rate. Interpolate draws it each frame part
            // way between its last two physics steps instead.
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            // Physics moves a body in steps (50 a second by default), and
            // normally only checks for a collision where each step ends -
            // so a small prop thrown fast can be on one side of a thin wall
            // at one step and past it at the next. Continuous detection
            // sweeps the body along its path instead. It costs more, so
            // it's only switched on for a throw.
            bool isThrown = velocity != Vector3.zero;
            _rigidbody.collisionDetectionMode = isThrown ? CollisionDetectionMode.ContinuousDynamic : _originalDetectionMode;

            _rigidbody.linearVelocity = velocity;
            _rigidbody.angularVelocity = angularVelocity;
        }

        /// <summary>
        /// Puts the prop's colliders back on the layers they were on before
        /// it was picked up, so hands can touch and target it again. Called
        /// by PlayerHandHolding once a dropped prop is clear of the hand
        /// that dropped it. Does nothing while it's held.
        /// </summary>
        public void RestoreLayers()
        {
            if (!_isOnHeldLayer || IsHeld) {
                return;
            }

            _isOnHeldLayer = false;

            for (int i = 0; i < _colliders.Length; i++) {
                _colliders[i].gameObject.layer = _originalLayers[i];
            }
        }
    }
}
