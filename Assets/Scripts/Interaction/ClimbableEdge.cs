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

        /// <summary>
        /// Snaps a grabbing hand onto this edge's lip: the point on the
        /// top-front line nearest to where the ray hit, facing into the wall
        /// with the grip frame's up matching the edge's up. The profile then
        /// offsets that into the hand visual's actual root pose.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint)
        {
            // Work in the box's local space, where the lip is a simple
            // axis-aligned line. BoxCollider.center/size are local values,
            // so this is correct however the cube is scaled or rotated.
            Vector3 local = transform.InverseTransformPoint(grabPoint);
            Vector3 centre = _boxCollider.center;
            Vector3 halfSize = _boxCollider.size * 0.5f;

            // Slide along the edge to wherever the ray hit, but never past
            // either end; then pin to the top face and the front face.
            local.x = Mathf.Clamp(local.x, centre.x - halfSize.x, centre.x + halfSize.x);
            local.y = centre.y + halfSize.y;
            local.z = centre.z + halfSize.z;

            Vector3 gripPosition = transform.TransformPoint(local);

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
