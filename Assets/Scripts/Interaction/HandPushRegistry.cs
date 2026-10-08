using System.Collections.Generic;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Static lookup from a solid Collider to the IHandPushable it belongs
    /// to, the same idea as HandTargetRegistry: the hand sweep gets a
    /// Collider from physics and needs to know, cheaply, whether pressing
    /// on it should move something. Only looked up on frames a hand is
    /// stopped by a surface, and it's nearly always empty-handed: walls
    /// aren't in it.
    /// </summary>
    public static class HandPushRegistry
    {
        private static readonly Dictionary<Collider, IHandPushable> _byCollider = new();

        /// <summary>
        /// Registers a Collider as belonging to the given IHandPushable.
        /// </summary>
        public static void Register(Collider collider, IHandPushable pushable)
        {
            _byCollider[collider] = pushable;
        }

        /// <summary>
        /// Removes a Collider's registration.
        /// </summary>
        public static void Unregister(Collider collider)
        {
            _byCollider.Remove(collider);
        }

        /// <summary>
        /// Returns whatever IHandPushable the given Collider is registered
        /// to, or null if it isn't registered.
        /// </summary>
        public static IHandPushable Find(Collider collider)
        {
            return _byCollider.TryGetValue(collider, out IHandPushable pushable) ? pushable : null;
        }
    }
}
