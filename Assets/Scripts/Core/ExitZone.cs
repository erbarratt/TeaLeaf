using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// A box the player escapes through: reaching it while carrying the
    /// objective wins the level (LevelManager does the checking).
    ///
    /// Not a trigger collider - just a box worked out with maths, like the
    /// sound rooms. That keeps it off the physics layers
    /// entirely: nothing for hand rays to hit, no collision matrix pair to
    /// get right, and it works while a mantle has the CharacterController
    /// switched off.
    ///
    /// The box is centred on this object and follows its position, rotation
    /// and scale. The point tested is the player's feet, so sink the box a
    /// little into the floor rather than resting its bottom face on it.
    /// </summary>
    public class ExitZone : MonoBehaviour, IDebugDrawable
    {
        // The box's full width, height and depth along this object's own
        // axes, in metres (before the object's scale).
        [SerializeField] private Vector3 size = new(3f, 3f, 3f);

        // Every enabled exit zone in the scene. Zones add and remove
        // themselves, so nothing ever searches the scene for them.
        private static readonly List<ExitZone> _zones = new();

        private static readonly Color _gizmoColor = new(0.2f, 1f, 0.4f, 0.6f);
        private static readonly Color _gizmoSelectedColor = new(0.2f, 1f, 0.4f, 1f);

        private void OnEnable()
        {
            _zones.Add(this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            _zones.Remove(this);
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// Whether a world-space point is inside any enabled exit zone.
        /// </summary>
        public static bool AnyContains(Vector3 worldPoint)
        {
            for (int i = 0; i < _zones.Count; i++) {
                if (_zones[i].Contains(worldPoint)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a world-space point is inside this zone's box. The point
        /// is converted into this object's local space, where the box is
        /// axis-aligned and centred on the origin - so the test is just
        /// "within half the size on each axis", however the zone is turned
        /// or scaled in the world.
        /// </summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);

            return Mathf.Abs(local.x) <= size.x * 0.5f
                && Mathf.Abs(local.y) <= size.y * 0.5f
                && Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        /// <summary>
        /// Draws the zone in the Scene view at all times, since it has no
        /// mesh - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// Draws the zone brighter while it's selected. Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// The box outline, in this object's local space so it matches
        /// Contains() exactly. Shared by the Scene view gizmos and the
        /// in-headset view (InHeadsetGizmos).
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            lines.Color = detailed ? _gizmoSelectedColor : _gizmoColor;
            lines.Matrix = transform.localToWorldMatrix;
            lines.WireCube(Vector3.zero, size);
        }
    }
}
