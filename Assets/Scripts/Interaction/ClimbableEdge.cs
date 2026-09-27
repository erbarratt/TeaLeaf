using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Marks a designer-placed cube as a climbable edge. The same BoxCollider
    /// serves as both the visual highlight bounds and the target hand rays
    /// hit - there is no separate trigger/visual pair. A hand grabs the edge
    /// by holding grip while its ray is on it.
    ///
    /// This class only knows about itself: its own collider, its own
    /// highlighted/not-highlighted appearance, and where a hand snaps onto
    /// it. Deciding which edge should be highlighted is
    /// PlayerHandInteraction's job, and grabbing is PlayerClimbing's.
    ///
    /// Orientation convention - place every edge so that its local X runs
    /// along the edge, +Y is up, and +Z points out from the wall towards the
    /// player. The lip a hand curls over is therefore the box's top-front
    /// line (top face, +Z face).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ClimbableEdge : MonoBehaviour, IHighlightable, IHandSnapTarget
    {
        [SerializeField] private Renderer targetRenderer;

        // Shared hand offsets/pose for all ledges - see HandSnapProfile.
        [SerializeField] private HandSnapProfile snapProfile;

        [Header("Mantling")]

        // Whether the player can mantle up onto this edge (see
        // PlayerMantling). Only tick this on level edges - mantling assumes
        // the edge and its top face are horizontal.
        [SerializeField] private bool isMantleable;

        // Whether a mantle onto this edge ends with the player crouched -
        // tick it for low shelves and vents with no room to stand.
        [SerializeField] private bool mantleEndsCrouched;

        // Where a mantle always lands the bottom of the player's capsule
        // (their feet), wherever along the edge it started. In metres along
        // this edge's own axes (X along the edge, Y up, Z out from the wall),
        // from its origin - position and rotation apply, scale doesn't, so
        // the numbers stay real distances on a stretched cube. Filled in by
        // Reset() / the "Reset Mantle Point" context menu to sit on the top
        // face, DefaultMantleInset in from the lip.
        [SerializeField] private Vector3 mantlePoint = new(0f, 0.1f, -0.2f);

        // How far in from the lip the default mantle point is, in metres.
        private const float DefaultMantleInset = 0.4f;

        // Opacity while not highlighted - 0 makes the box invisible until a
        // hand ray points at it.
        [SerializeField] private float baseOpacity;

        // Opacity while highlighted.
        [SerializeField] private float highlightedOpacity = 0.2f;

        // Shader property ID for URP Lit/Unlit's base colour - cached once
        // since Shader.PropertyToID() hashes a string every call. If the
        // ledge material's shader ever changes to one that reads colour from
        // "_Color" instead (e.g. Built-in Standard), this needs updating too.
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private BoxCollider _boxCollider;

        // The material's own colour, cached once - only its RGB is used;
        // alpha is always overridden by baseOpacity/highlightedOpacity in
        // SetHighlighted() below. Read from sharedMaterial rather than
        // .material - see SetHighlighted() for why we never instance a
        // per-renderer material copy at all.
        private Color _baseColor;

        // Reused every SetHighlighted() call rather than allocated fresh, so
        // toggling highlighting doesn't allocate.
        private MaterialPropertyBlock _propertyBlock;

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
            _baseColor = targetRenderer.sharedMaterial.color;
            _propertyBlock = new MaterialPropertyBlock();

            // Force the not-highlighted opacity immediately, rather than
            // waiting for the first highlight transition - otherwise the box
            // would render at whatever alpha happens to be baked into the
            // material asset until a hand ray first points at it.
            SetHighlighted(false);
        }

        private void OnEnable()
        {
            HighlightableRegistry.Register(_boxCollider, this);
        }

        private void OnDisable()
        {
            HighlightableRegistry.Unregister(_boxCollider);
        }

        /// Whether the player can mantle onto this edge - see isMantleable.
        public bool IsMantleable => isMantleable;

        /// Whether a mantle onto this edge ends crouched.
        public bool MantleEndsCrouched => mantleEndsCrouched;

        /// World position a mantle lands the player's feet at - mantlePoint
        /// rotated and offset by this edge's transform, but not scaled (see
        /// mantlePoint), which is why this isn't TransformPoint().
        public Vector3 MantlePointWorld => transform.position + transform.rotation * mantlePoint;

        /// <summary>
        /// Editor-only: runs when the component is first added. Fills in the
        /// renderer and a sensible default mantle point for this box.
        /// </summary>
        private void Reset()
        {
            targetRenderer = GetComponent<Renderer>();
            ResetMantlePoint();
        }

        /// <summary>
        /// Puts the mantle point on this box's top face, DefaultMantleInset
        /// in from the lip, centred along the edge. The box's collider size
        /// is multiplied by the transform's scale here to get real metres,
        /// since mantlePoint itself is unscaled. Also available from the
        /// component's right-click menu, for edges placed before this
        /// existed or after resizing one.
        /// </summary>
        [ContextMenu("Reset Mantle Point")]
        private void ResetMantlePoint()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            Vector3 scale = transform.lossyScale;

            float along = box.center.x * scale.x;
            float top = (box.center.y + box.size.y * 0.5f) * scale.y;
            float lip = (box.center.z + box.size.z * 0.5f) * scale.z;

#if UNITY_EDITOR
            // Lets the change be undone, and marks the object dirty so it's
            // actually saved - a context menu edit isn't otherwise tracked.
            UnityEditor.Undo.RecordObject(this, "Reset Mantle Point");
#endif

            mantlePoint = new Vector3(along, top, lip - DefaultMantleInset);
        }

        /// <summary>
        /// While this edge is selected in the editor, marks where a mantle
        /// lands: a sphere at the feet and a line up to roughly head height
        /// (lower when the mantle ends crouched). Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!isMantleable) {
                return;
            }

            Vector3 feet = MantlePointWorld;
            float headHeight = mantleEndsCrouched ? 1f : 1.8f;

            Gizmos.color = mantleEndsCrouched ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(feet, 0.1f);
            Gizmos.DrawLine(feet, feet + Vector3.up * headHeight);
        }

        /// <summary>
        /// The point on this edge's lip (its top-front line) nearest to
        /// worldPoint, clamped so it never goes past either end of the edge.
        /// Used both to snap a grabbing hand and to find where a mantle lands.
        /// </summary>
        public Vector3 ClosestLipPoint(Vector3 worldPoint)
        {
            // Work in the box's local space, where the lip is a simple
            // axis-aligned line. BoxCollider.center/size are local values,
            // so this is correct however the cube is scaled or rotated.
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 centre = _boxCollider.center;
            Vector3 halfSize = _boxCollider.size * 0.5f;

            // Slide along the edge to wherever the point is, but never past
            // either end; then pin to the top face and the front face.
            local.x = Mathf.Clamp(local.x, centre.x - halfSize.x, centre.x + halfSize.x);
            local.y = centre.y + halfSize.y;
            local.z = centre.z + halfSize.z;

            return transform.TransformPoint(local);
        }

        /// <summary>
        /// Snaps a grabbing hand onto this edge's lip: the point on the
        /// top-front line nearest to where the ray hit, facing into the wall
        /// with the grip frame's up matching the edge's up. The profile then
        /// offsets that into the hand visual's actual root pose.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint)
        {
            Vector3 gripPosition = ClosestLipPoint(grabPoint);

            // Grip frame: forward points into the wall (-Z), up is the edge's
            // up - the direction the back of a palm-down hand faces.
            Quaternion gripRotation = Quaternion.LookRotation(-transform.forward, transform.up);

            // Without a profile, still snap to the bare grip frame so a
            // missing reference is obvious (hand badly offset), not a crash.
            // == null rather than "is null": in the editor Unity can store an
            // unassigned serialized reference as a "fake null" object that
            // only its overloaded == operator treats as null.
            if (snapProfile == null) {
                return new HandSnapPose(gripPosition, gripRotation, HandPose.LedgeGrip);
            }

            return snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }

        /// <summary>
        /// Fades this edge's rendered opacity between baseOpacity and
        /// highlightedOpacity - the colour itself never changes. Implements
        /// IHighlightable so PlayerHandInteraction can highlight this edge
        /// without knowing it's specifically a ClimbableEdge.
        ///
        /// Uses a MaterialPropertyBlock rather than writing to
        /// targetRenderer.material.color. That property getter instances a
        /// per-renderer copy of the material asset the first time it's
        /// touched, and every colour write after that still marks the
        /// Material dirty - on this project's URP setup that forces the GPU
        /// Resident Drawer to re-upload this renderer's GPU-resident data,
        /// which is exactly the per-toggle cost that was showing up as a
        /// stutter. A MaterialPropertyBlock is a per-renderer override that
        /// sits outside the shared Material entirely, so toggling it doesn't
        /// touch the asset or trigger that re-upload.
        /// </summary>
        public void SetHighlighted(bool highlighted)
        {
            Color color = _baseColor;
            color.a = highlighted ? highlightedOpacity : baseOpacity;
            _propertyBlock.SetColor(_baseColorId, color);
            targetRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
