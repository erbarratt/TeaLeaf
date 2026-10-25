using UnityEngine;

namespace Player
{
    /// <summary>
    /// One piece of a hand's collision shape - the palm, the thumb, a finger,
    /// or one collider of a prop the hand carries - and the physics queries
    /// HandPhysicalFollow runs with it: sweep (cast), overlap, check and
    /// push-out (ComputePenetration). A box, a sphere or a capsule, whichever
    /// its HandShapeGeometry says; a piece can change kind (a slot used for a
    /// carried prop's colliders takes whatever each prop has).
    ///
    /// Each piece owns a real collider of its shape, only ever used as the
    /// "this is the hand" half of Physics.ComputePenetration, which needs an
    /// enabled collider but takes its position and rotation as arguments.
    /// The colliders are made once, parked far below the level under one
    /// object per hand (see Create()) and never moved - moving a collider
    /// isn't free in PhysX. They're triggers on the PlayerHands layer, so they
    /// can't push, block or be hit by anything: hand rays and the hand sweep
    /// don't include that layer, and Player-PlayerHands is off in the
    /// collision matrix. Each slot has one collider of each kind; only the
    /// one in use is enabled, and its size is only written when the shape
    /// changes (resizing makes PhysX rebuild it).
    ///
    /// A plain C# class, owned by HandPhysicalFollow.
    /// </summary>
    public class HandCollisionShape
    {
        private const string PlayerHandsLayerName = "PlayerHands";

        private readonly GameObject _slot;
        private readonly BoxCollider _box;
        private readonly SphereCollider _sphere;
        private readonly CapsuleCollider _capsule;

        private HandShapeGeometry _geometry;

        // Where the shape sits this frame, from Orient(): its middle as an
        // offset from the visual's root, its rotation, and (a capsule) half
        // its line - all in world axes.
        private Vector3 _offset;
        private Quaternion _worldRotation;
        private Vector3 _halfAxis;

        /// False while the slot holds no shape (a carried-prop slot with no
        /// prop, or a hand whose bones weren't found): skipped by every query.
        public bool IsActive { get; private set; }

        /// The shape's numbers, as last given to Set().
        public HandShapeGeometry Geometry => _geometry;

        private HandCollisionShape(GameObject slot)
        {
            _slot = slot;
            _box = AddCollider<BoxCollider>(slot);
            _sphere = AddCollider<SphereCollider>(slot);
            _capsule = AddCollider<CapsuleCollider>(slot);
            _capsule.direction = 2;
        }

        /// <summary>
        /// Makes count empty shapes, their colliders on children of one new
        /// object called name, parked at parkedPosition. At the scene root,
        /// not under the player (a child would be dragged round by the rig
        /// every frame), and unscaled, so the right hand visual's mirrored -1
        /// scale never reaches them. Load time only.
        /// </summary>
        public static HandCollisionShape[] Create(string name, int count, Vector3 parkedPosition, out GameObject parent)
        {
            parent = new GameObject(name);
            parent.transform.position = parkedPosition;
            int layer = LayerMask.NameToLayer(PlayerHandsLayerName);

            if (layer < 0) {
                Debug.LogWarning($"HandCollisionShape: no '{PlayerHandsLayerName}' layer - {name} left on Default.");
                layer = 0;
            }

            var shapes = new HandCollisionShape[count];

            for (int i = 0; i < count; i++) {
                GameObject slot = new($"Shape {i}") { layer = layer };
                slot.transform.SetParent(parent.transform, false);
                shapes[i] = new HandCollisionShape(slot);
            }

            return shapes;
        }

        /// <summary>
        /// Adds one collider to a slot: a disabled trigger, centred.
        /// </summary>
        private static T AddCollider<T>(GameObject slot) where T : Collider
        {
            T collider = slot.AddComponent<T>();
            collider.isTrigger = true;
            collider.enabled = false;
            return collider;
        }

        /// <summary>
        /// Gives the slot a shape. The collider for its kind is enabled and
        /// sized only if something changed - so calling this with the same
        /// shape every frame would cost nothing, but it's only called when a
        /// pose switches or a prop is picked up.
        /// </summary>
        public void Set(in HandShapeGeometry geometry)
        {
            if (IsActive && _geometry.Matches(geometry)) {
                return;
            }

            _geometry = geometry;
            IsActive = true;

            _box.enabled = geometry.Kind == HandShapeGeometry.ShapeKind.Box;
            _sphere.enabled = geometry.Kind == HandShapeGeometry.ShapeKind.Sphere;
            _capsule.enabled = geometry.Kind == HandShapeGeometry.ShapeKind.Capsule;

            switch (geometry.Kind) {
                case HandShapeGeometry.ShapeKind.Box:
                    _box.size = geometry.HalfExtents * 2f;
                    break;
                case HandShapeGeometry.ShapeKind.Sphere:
                    _sphere.radius = geometry.Radius;
                    break;
                default:
                    // A Unity capsule's height is measured end to end, round
                    // caps included.
                    _capsule.radius = geometry.Radius;
                    _capsule.height = (geometry.HalfLength + geometry.Radius) * 2f;
                    break;
            }
        }

        /// <summary>
        /// Empties the slot: no queries, collider off.
        /// </summary>
        public void Clear()
        {
            if (!IsActive) {
                return;
            }

            IsActive = false;
            _box.enabled = false;
            _sphere.enabled = false;
            _capsule.enabled = false;
        }

        /// <summary>
        /// Works out where the shape sits for a hand whose controller is
        /// turned to controllerRotation, as an offset from the visual's root.
        /// Every query below then only adds the root's position, so a sweep
        /// (which only moves, never turns) can use it all along its path.
        /// </summary>
        public void Orient(Quaternion controllerRotation)
        {
            _offset = controllerRotation * _geometry.Centre;
            _worldRotation = controllerRotation * _geometry.Rotation;
            _halfAxis = _worldRotation * Vector3.forward * _geometry.HalfLength;
        }

        /// <summary>
        /// Sweeps the shape (root at root) along direction for distance.
        /// Triggers are ignored: ledges, ladders and ropes are trigger
        /// volumes for the hand rays, not surfaces to stop at. No allocation:
        /// each cast returns its one hit through the out parameter.
        /// </summary>
        public bool Cast(Vector3 root, Vector3 direction, float distance, LayerMask layers, out RaycastHit hit)
        {
            Vector3 centre = root + _offset;

            switch (_geometry.Kind) {
                case HandShapeGeometry.ShapeKind.Box:
                    return Physics.BoxCast(centre, _geometry.HalfExtents, direction, out hit, _worldRotation, distance, layers, QueryTriggerInteraction.Ignore);
                case HandShapeGeometry.ShapeKind.Sphere:
                    return Physics.SphereCast(centre, _geometry.Radius, direction, out hit, distance, layers, QueryTriggerInteraction.Ignore);
                default:
                    return Physics.CapsuleCast(centre - _halfAxis, centre + _halfAxis, _geometry.Radius, direction, out hit, distance, layers, QueryTriggerInteraction.Ignore);
            }
        }

        /// <summary>
        /// Lists what the shape (root at root) is inside into buffer and
        /// returns how many; triggers ignored.
        /// </summary>
        public int Overlap(Vector3 root, Collider[] buffer, LayerMask layers)
        {
            Vector3 centre = root + _offset;

            switch (_geometry.Kind) {
                case HandShapeGeometry.ShapeKind.Box:
                    return Physics.OverlapBoxNonAlloc(centre, _geometry.HalfExtents, buffer, _worldRotation, layers, QueryTriggerInteraction.Ignore);
                case HandShapeGeometry.ShapeKind.Sphere:
                    return Physics.OverlapSphereNonAlloc(centre, _geometry.Radius, buffer, layers, QueryTriggerInteraction.Ignore);
                default:
                    return Physics.OverlapCapsuleNonAlloc(centre - _halfAxis, centre + _halfAxis, _geometry.Radius, buffer, layers, QueryTriggerInteraction.Ignore);
            }
        }

        /// <summary>
        /// True if the shape (root at root) is inside anything on layers.
        /// </summary>
        public bool Check(Vector3 root, LayerMask layers)
        {
            Vector3 centre = root + _offset;

            switch (_geometry.Kind) {
                case HandShapeGeometry.ShapeKind.Box:
                    return Physics.CheckBox(centre, _geometry.HalfExtents, _worldRotation, layers, QueryTriggerInteraction.Ignore);
                case HandShapeGeometry.ShapeKind.Sphere:
                    return Physics.CheckSphere(centre, _geometry.Radius, layers, QueryTriggerInteraction.Ignore);
                default:
                    return Physics.CheckCapsule(centre - _halfAxis, centre + _halfAxis, _geometry.Radius, layers, QueryTriggerInteraction.Ignore);
            }
        }

        /// <summary>
        /// The shortest way out of other for the shape (root at root): a
        /// direction and a distance. False if they only touch.
        /// </summary>
        public bool ComputePenetration(Vector3 root, Collider other, out Vector3 direction, out float distance)
        {
            Collider own = _geometry.Kind switch {
                HandShapeGeometry.ShapeKind.Box => _box,
                HandShapeGeometry.ShapeKind.Sphere => _sphere,
                _ => _capsule
            };

            other.transform.GetPositionAndRotation(out Vector3 otherPosition, out Quaternion otherRotation);

            return Physics.ComputePenetration(
                own, root + _offset, _worldRotation,
                other, otherPosition, otherRotation,
                out direction, out distance);
        }

        /// <summary>
        /// True if collider is one of this slot's own - only possible if the
        /// layers queried wrongly include PlayerHands.
        /// </summary>
        public bool Owns(Collider collider)
        {
            return collider.gameObject == _slot;
        }

        /// <summary>
        /// Destroys every slot's colliders with the parent Create() made.
        /// Already gone if the scene is unloading - Unity's == null is also
        /// true for a destroyed object.
        /// </summary>
        public static void Destroy(GameObject parent)
        {
            if (parent != null) {
                Object.Destroy(parent);
            }
        }
    }
}
