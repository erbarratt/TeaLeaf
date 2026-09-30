using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A climbable rope: a static, straight line hanging from this object's
    /// position down its local -Y for `length` metres. A CapsuleCollider
    /// around that line is the hand-ray target - sized by this component
    /// itself, so a rope can be placed by hand in a level or spawned at
    /// runtime (Phase 5's rope bolt) and given its length with SetLength().
    ///
    /// Climbing it is PlayerClimbing's job and moves the player exactly like
    /// a ledge or ladder (unconstrained - the body hangs off the grab point).
    /// No mesh: gizmos show it in the Scene view, and a visible rope adds its
    /// own renderer (e.g. the rope-bolt prefab's).
    ///
    /// Unlike ClimbableEdge and Ladder there's no front or back: a hand
    /// wraps around the rope from whichever side it grabbed.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public class ClimbableRope : MonoBehaviour, IHandTarget, IClimbable, IDebugDrawable
    {
        // Shared hand offsets/pose for all ropes - see HandSnapProfile.
        [SerializeField] private HandSnapProfile snapProfile;

        // How far the rope hangs down from this object's position, in metres -
        // the rope's size is set here, never on the (locked) CapsuleCollider.
        // The top stays put: the object's position is the top of the rope.
        [SerializeField] private float length = 4f;

        // Radius of the grab collider around the rope, in metres. Much
        // thicker than a real rope on purpose: a 2cm target is almost
        // impossible to land a hand ray on.
        [SerializeField] private float grabRadius = 0.08f;

        // Smallest allowed length - keeps the capsule valid.
        private const float MinLength = 0.1f;

        // The layer the rope (its grab volume) must be on - see WarnAboutSetup().
        private const string ClimbableLayerName = "Climbable";

        // Gizmo colours: faint when the rope isn't selected, bright when it is.
        private static readonly Color _gizmoColor = new(0.9f, 0.8f, 0.4f, 0.35f);
        private static readonly Color _gizmoSelectedColor = new(0.9f, 0.8f, 0.4f, 1f);

        private CapsuleCollider _capsuleCollider;

        /// How far the rope hangs down, in metres.
        public float Length => length;

        private void Awake()
        {
            _capsuleCollider = GetComponent<CapsuleCollider>();
            FitCollider(_capsuleCollider);
        }

        private void OnEnable()
        {
            HandTargetRegistry.Register(_capsuleCollider, this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            HandTargetRegistry.Unregister(_capsuleCollider);
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// Editor-only: runs whenever a value is changed in the Inspector,
        /// and when the scene loads. Keeps the values sane and the collider
        /// matching them.
        ///
        /// Also locks the CapsuleCollider (greyed out in the Inspector):
        /// `length` and `grabRadius` here are the only controls. Editing the
        /// collider's Height directly grows it around its centre rather than
        /// down from the top, and the gizmo and snapping would never hear
        /// about it - so it isn't allowed.
        /// </summary>
        private void OnValidate()
        {
            length = Mathf.Max(length, MinLength);
            grabRadius = Mathf.Max(grabRadius, 0.01f);

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();

            if (capsule != null) {
                capsule.hideFlags |= HideFlags.NotEditable;
            }

            FitCollider(capsule);
            WarnAboutSetup(capsule);
        }

        /// <summary>
        /// Editor-only, from OnValidate(): warns (clickable, selecting the
        /// problem object) about set-ups that stop the rope being grabbed.
        /// The intended set-up is this object's CapsuleCollider as the grab
        /// volume - a trigger (FitCollider() makes sure) on the Climbable
        /// layer - plus, optionally, a thinner solid collider on a child as
        /// the rope itself (Environment, so the physical hands stop against
        /// it). Hands inside the grab volume can still grab
        /// (PlayerHandInteraction), so the solid rope only has to fit inside:
        /// - this object not on the Climbable layer;
        /// - a child collider reaching as far as grabRadius or further - hand
        ///   rays would hit it before (or tied with) the grab volume, so the
        ///   rope couldn't be aimed at reliably. Found 2026-09-28: both were
        ///   0.04m, and the reticle flickered as rays hit one or the other.
        /// </summary>
        private void WarnAboutSetup(CapsuleCollider capsule)
        {
            int climbableLayer = LayerMask.NameToLayer(ClimbableLayerName);

            if (climbableLayer >= 0 && gameObject.layer != climbableLayer) {
                Debug.LogWarning(
                    $"ClimbableRope '{name}': not on the {ClimbableLayerName} layer, so hand rays may miss " +
                    "it or it may collide with things it shouldn't.",
                    this);
            }

            // Allocates, but OnValidate only runs in the editor.
            foreach (Collider child in GetComponentsInChildren<Collider>(true)) {
                if (child == capsule || child.isTrigger) {
                    continue;
                }

                if (FurthestFromAxis(child.bounds) >= grabRadius) {
                    Debug.LogWarning(
                        $"ClimbableRope '{name}': the collider on '{child.name}' reaches as far from the rope " +
                        $"as grabRadius ({grabRadius}m) or further, so hand rays can hit it instead of the grab volume. " +
                        "Make the solid rope thinner than the grab volume, or raise grabRadius.",
                        child);
                }
            }
        }

        /// <summary>
        /// How far the furthest corner of bounds (a world-space box) is from
        /// the rope's axis, sideways. A box's corners reach further than a
        /// round rope inside it, so this errs towards warning - fine for a
        /// check that only has to catch a clearly oversized collider.
        /// </summary>
        private float FurthestFromAxis(Bounds bounds)
        {
            Vector3 top = transform.position;
            Vector3 axis = transform.up;
            float furthest = 0f;

            for (int i = 0; i < 8; i++) {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));

                Vector3 sideways = Vector3.ProjectOnPlane(corner - top, axis);
                furthest = Mathf.Max(furthest, sideways.magnitude);
            }

            return furthest;
        }

        /// <summary>
        /// Sets how far the rope hangs down and resizes its collider to
        /// match - e.g. for the rope bolt, once it knows the distance to the
        /// ground. Only call on spawn/change, not every frame.
        /// </summary>
        public void SetLength(float newLength)
        {
            length = Mathf.Max(newLength, MinLength);
            FitCollider(_capsuleCollider);
        }

        /// <summary>
        /// Sizes the capsule to run from this object's position down local
        /// -Y for `length` metres with `grabRadius` thickness. The collider's
        /// values are in scaled local units, so the real-metre values are
        /// divided by the scale here - the rope stays the right size even if
        /// someone scales the object (though scale 1 is simplest).
        /// </summary>
        private void FitCollider(CapsuleCollider capsule)
        {
            if (capsule == null) {
                return;
            }

            Vector3 scale = transform.lossyScale;
            float scaleY = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
            float scaleXZ = Mathf.Max(Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)), 0.0001f);

            // The grab volume is always a trigger: it's something to aim at,
            // not a surface. Set here because the collider is locked in the
            // Inspector, so it can't be ticked there.
            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.center = new Vector3(0f, -length * 0.5f / scaleY, 0f);
            capsule.height = length / scaleY;
            capsule.radius = grabRadius / scaleXZ;
        }

        /// <summary>
        /// Snaps a grabbing hand onto the rope: the point on the rope's axis
        /// nearest to where the ray hit (never past either end), with the grip
        /// frame facing the rope from the side the hand grabbed from and its
        /// up matching the rope's up. The profile then offsets that into the
        /// hand visual's actual root pose. Only called on grab.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint)
        {
            // Work in the unscaled local frame (position + rotation, real
            // metres), where the rope is simply the line x = 0, z = 0 from
            // y = 0 down to y = -length.
            Vector3 local = Quaternion.Inverse(transform.rotation) * (grabPoint - transform.position);
            float along = Mathf.Clamp(-local.y, 0f, length);

            Vector3 gripPosition = transform.position + transform.rotation * new Vector3(0f, -along, 0f);

            // The ray hit is on the capsule's surface on the hand's side, so
            // the horizontal direction from the axis out to it says which
            // side the hand is on. Fall back to the rope's own +Z if the hit
            // was (almost) exactly on the axis, e.g. on an end cap.
            Vector3 side = new(local.x, 0f, local.z);

            if (side.sqrMagnitude < 0.000001f) {
                side = Vector3.forward;
            }

            // Grip frame: forward points from the hand's side into the rope,
            // up is the rope's up - the same shape as a ledge's frame, with
            // "into the wall" replaced by "into the rope".
            Vector3 intoRope = transform.rotation * -side.normalized;
            Quaternion gripRotation = Quaternion.LookRotation(intoRope, transform.up);

            // See ClimbableEdge.GetSnapPose() for why == null.
            if (snapProfile == null) {
                return new HandSnapPose(gripPosition, gripRotation, HandPose.RopeGrip);
            }

            return snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }

        /// <summary>
        /// Always draws the rope's line faintly in the Scene view, so it's
        /// visible while laying out a level even though it has no mesh - see
        /// DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// While selected, draws the rope brightly with its grab thickness -
        /// see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// Draws the rope - as Scene view gizmos and, while InHeadsetGizmos
        /// is on, in the headset: its axis, faint. With detailed on
        /// (selected), bright, plus its grab thickness: a sphere at each end
        /// and four lines down the capsule's sides. Drawn in the unscaled
        /// local frame, so the numbers are real metres.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            lines.Matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            lines.Color = detailed ? _gizmoSelectedColor : _gizmoColor;

            Vector3 bottom = Vector3.down * length;
            lines.Line(Vector3.zero, bottom);

            if (detailed) {
                lines.WireCapsule(Vector3.zero, bottom, grabRadius);
            }

            lines.Matrix = Matrix4x4.identity;
        }
    }
}
