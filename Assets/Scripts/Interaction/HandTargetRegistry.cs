using System.Collections.Generic;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Static lookup from a Collider to whatever IHandTarget owns it.
    ///
    /// PlayerHandInteraction needs to turn a raycast hit's Collider into an
    /// IHandTarget every frame, for both hands. GetComponent<T>() with an
    /// interface type is slower than with a concrete Component type - Unity
    /// can't use its fast native per-type lookup for interfaces, so it has
    /// to walk every component on the hit GameObject checking each one's
    /// type. Doing that up to twice a frame adds up, so instead every
    /// IHandTarget registers itself here once (in OnEnable) and
    /// PlayerHandInteraction does a plain dictionary lookup instead (a
    /// self-registering registry, per the project's performance habits).
    /// </summary>
    public static class HandTargetRegistry
    {
        private static readonly Dictionary<Collider, IHandTarget> _byCollider = new();

        /// <summary>
        /// Registers a Collider as belonging to the given IHandTarget.
        /// Call from OnEnable.
        /// </summary>
        public static void Register(Collider collider, IHandTarget target)
        {
            _byCollider[collider] = target;
        }

        /// <summary>
        /// Removes a Collider's registration. Call from OnDisable.
        /// </summary>
        public static void Unregister(Collider collider)
        {
            _byCollider.Remove(collider);
        }

        /// <summary>
        /// Returns whatever IHandTarget the given Collider is registered to,
        /// or null if it isn't registered.
        /// </summary>
        public static IHandTarget Find(Collider collider)
        {
            return _byCollider.TryGetValue(collider, out IHandTarget target) ? target : null;
        }
    }
}
