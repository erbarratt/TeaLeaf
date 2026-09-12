using System.Collections.Generic;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Static lookup from a Collider to whatever IHighlightable owns it.
    ///
    /// PlayerHandInteraction needs to turn a raycast hit's Collider into an
    /// IHighlightable every frame, for both hands. GetComponent<T>() with an
    /// interface type is slower than with a concrete Component type - Unity
    /// can't use its fast native per-type lookup for interfaces, so it has
    /// to walk every component on the hit GameObject checking each one's
    /// type. Doing that up to twice a frame adds up, so instead every
    /// IHighlightable registers itself here once (in OnEnable) and PlayerHandInteraction
    /// does a plain dictionary lookup instead - the same self-registration
    /// pattern ClimbableEdge already uses for its own Active list, just
    /// generalised to any IHighlightable rather than one specific type.
    /// </summary>
    public static class HighlightableRegistry
    {
        private static readonly Dictionary<Collider, IHighlightable> _byCollider = new();

        /// <summary>
        /// Registers a Collider as belonging to the given IHighlightable.
        /// Call from OnEnable.
        /// </summary>
        public static void Register(Collider collider, IHighlightable highlightable)
        {
            _byCollider[collider] = highlightable;
        }

        /// <summary>
        /// Removes a Collider's registration. Call from OnDisable.
        /// </summary>
        public static void Unregister(Collider collider)
        {
            _byCollider.Remove(collider);
        }

        /// <summary>
        /// Returns whatever IHighlightable the given Collider is registered
        /// to, or null if it isn't registered.
        /// </summary>
        public static IHighlightable Find(Collider collider)
        {
            return _byCollider.TryGetValue(collider, out IHighlightable highlightable) ? highlightable : null;
        }
    }
}
