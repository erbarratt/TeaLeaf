using System.Collections.Generic;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Marks a designer-placed cube as a climbable edge. The same BoxCollider
    /// serves as both the visual highlight bounds and the volume used to check
    /// whether a hand is inside it when grabbing - there is no separate
    /// trigger/visual pair.
    ///
    /// This class only knows about itself: its own bounds and its own
    /// highlighted/not-highlighted appearance. Deciding which edge should be
    /// highlighted, and handling grabbing, is PlayerClimbing's job.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ClimbableEdge : MonoBehaviour, IHighlightable
    {
        [SerializeField] private Renderer targetRenderer;

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

        // Every enabled ClimbableEdge registers itself here, so PlayerClimbing
        // can check all of them each frame for grabbing without an expensive
        // scene search.
        private static readonly List<ClimbableEdge> _active = new();
        public static IReadOnlyList<ClimbableEdge> Active => _active;

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
            _active.Add(this);
            HighlightableRegistry.Register(_boxCollider, this);
        }

        private void OnDisable()
        {
            _active.Remove(this);
            HighlightableRegistry.Unregister(_boxCollider);
        }

        /// <summary>
        /// Closest point on the BoxCollider's surface to the given world point,
        /// used to snap a grabbing hand's visual model to the edge.
        /// </summary>
        public Vector3 ClosestPoint(Vector3 point)
        {
            return _boxCollider.ClosestPoint(point);
        }

        /// World-space axis-aligned bounds of this edge's BoxCollider -
        /// exposed so PlayerClimbing can cheaply reject far-away edges
        /// before calling the more expensive Overlaps() below.
        public Bounds Bounds => _boxCollider.bounds;

        /// <summary>
        /// Whether a sphere of the given radius centred on point overlaps
        /// this edge's box, respecting the box's rotation (unlike an
        /// axis-aligned Bounds check). Collider.ClosestPoint returns the
        /// input point unchanged when it's already inside a solid collider,
        /// so a hand whose centre is inside the box always overlaps
        /// regardless of radius; otherwise it overlaps only if the box's
        /// nearest surface point is within the sphere.
        /// </summary>
        public bool Overlaps(Vector3 point, float radius)
        {
            Vector3 closest = ClosestPoint(point);
            return (closest - point).sqrMagnitude <= radius * radius;
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
