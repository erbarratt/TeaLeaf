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
    /// gripPoint is the grip frame (where on the prop the hand holds it, and
    /// which way round), and the shared HandSnapProfile turns that frame
    /// into each hand's pose. The prop is then carried at exactly that pose
    /// relative to the hand, however it was lying when it was picked up.
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
    public class Grabbable : MonoBehaviour, IHandTarget, IHandSnapTarget
    {
        // The grip frame: a child placed where the hand holds the prop. Left
        // empty, the prop's own origin and rotation are used.
        [SerializeField] private Transform gripPoint;

        // Per-hand offsets from the grip frame to the hand visual, shared by
        // every prop held the same way. Left empty, the hand sits exactly
        // on the grip frame.
        [SerializeField] private HandSnapProfile snapProfile;

        // The prop's size for the hand's collision while it's carried: the
        // hand stops when a ball this big round the prop's middle would
        // touch a surface, so the prop doesn't go into walls. In metres.
        [SerializeField] private float holdRadius = 0.15f;

        private Rigidbody _rigidbody;

        // Every collider of the prop, and the layer each one's object was on
        // before the prop was picked up.
        private Collider[] _colliders;
        private int[] _originalLayers;

        /// True while a hand is holding this.
        public bool IsHeld { get; private set; }

        public float HoldRadius => holdRadius;

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
        }

        private void OnDisable()
        {
            for (int i = 0; i < _colliders.Length; i++) {
                HandTargetRegistry.Unregister(_colliders[i]);
            }
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
        /// that hand. PlayerHandHolding turns this round - it keeps the hand
        /// where it is and brings the prop to it. The grab point and head
        /// aren't used: a prop is held one way, wherever it was grabbed.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint, Vector3 headPosition, Vector3 headForward)
        {
            // == null rather than "is null": a serialized reference that was
            // never assigned is a Unity "fake null" object in the editor.
            Transform frame = gripPoint == null ? transform : gripPoint;
            frame.GetPositionAndRotation(out Vector3 gripPosition, out Quaternion gripRotation);

            if (snapProfile == null) {
                return new HandSnapPose(gripPosition, gripRotation, default);
            }

            return snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }

        /// <summary>
        /// Called by PlayerHandHolding as a hand picks the prop up: physics
        /// stops moving it, and it goes onto heldLayer (the hands' layer).
        /// </summary>
        public void BeginHold(int heldLayer)
        {
            IsHeld = true;
            _rigidbody.isKinematic = true;

            for (int i = 0; i < _colliders.Length; i++) {
                GameObject colliderObject = _colliders[i].gameObject;
                _originalLayers[i] = colliderObject.layer;
                colliderObject.layer = heldLayer;
            }
        }

        /// <summary>
        /// Called by PlayerHandHolding as the hand lets go: back on its own
        /// layer and under physics again, starting from rest - it drops.
        /// </summary>
        public void EndHold()
        {
            IsHeld = false;

            for (int i = 0; i < _colliders.Length; i++) {
                _colliders[i].gameObject.layer = _originalLayers[i];
            }

            // Velocity can only be set on a body physics is moving, so
            // after the switch back.
            _rigidbody.isKinematic = false;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }
    }
}
