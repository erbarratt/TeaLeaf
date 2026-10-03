using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// One room for sound: a box that says "everything in here shares the
    /// same air". Sound travels freely inside a room, and from room to room
    /// only through SoundPortals (doorways, stairwells, windows) - never
    /// straight through a wall. The idea is Thief's room brushes.
    ///
    /// Anywhere not inside a room counts as "outside", which behaves as one
    /// big room of its own (a null SoundRoom).
    ///
    /// Like ExitZone, a box worked out with maths, not a trigger collider:
    /// no physics layer and nothing for hand rays to hit. The box is centred
    /// on this object and follows its position, rotation and scale. Make
    /// neighbouring rooms meet in the middle of the wall between them.
    ///
    /// ExecuteAlways makes OnEnable/OnDisable run in Edit Mode too, so rooms
    /// are registered while the level is being built and a SoundPortal can
    /// show which rooms it joins before Play is pressed.
    /// </summary>
    [ExecuteAlways]
    public class SoundRoom : MonoBehaviour, IDebugDrawable
    {
        // The box's full width, height and depth along this object's own
        // axes, in metres (before the object's scale).
        [SerializeField] private Vector3 size = new(4f, 3f, 4f);

        // Every enabled room in the scene. Rooms add and remove themselves,
        // so nothing ever searches the scene for them.
        private static readonly List<SoundRoom> _rooms = new();

        private static readonly Color _gizmoColor = new(0.3f, 0.6f, 1f, 0.5f);
        private static readonly Color _gizmoSelectedColor = new(0.3f, 0.6f, 1f, 1f);

        /// <summary>
        /// The box's volume. Only worked out where rooms overlap, to pick
        /// the smallest (a cupboard inside a hall) - so it isn't cached,
        /// and a room resized in the editor is right straight away.
        /// </summary>
        private float Volume
        {
            get
            {
                Vector3 scale = transform.lossyScale;
                return Mathf.Abs(size.x * scale.x * size.y * scale.y * size.z * scale.z);
            }
        }

        private void OnEnable()
        {
            _rooms.Add(this);
            DebugDrawRegistry.Register(this);
            SoundPropagation.MarkLayoutDirty();
        }

        private void OnDisable()
        {
            _rooms.Remove(this);
            DebugDrawRegistry.Unregister(this);
            SoundPropagation.MarkLayoutDirty();
        }

        /// <summary>
        /// The room a world-space point is in, or null if it's outside every
        /// room. Where rooms overlap, the smallest.
        /// </summary>
        public static SoundRoom Find(Vector3 worldPoint)
        {
            SoundRoom best = null;

            for (int i = 0; i < _rooms.Count; i++) {
                SoundRoom room = _rooms[i];

                if (room.Contains(worldPoint) && (best == null || room.Volume < best.Volume)) {
                    best = room;
                }
            }

            return best;
        }

        /// <summary>
        /// Whether a world-space point is inside this room's box - the same
        /// local-space test as ExitZone.Contains().
        /// </summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);

            return Mathf.Abs(local.x) <= size.x * 0.5f
                && Mathf.Abs(local.y) <= size.y * 0.5f
                && Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        /// <summary>
        /// Draws the room in the Scene view at all times, since it has no
        /// mesh - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// Draws the room brighter while it's selected. Editor-only.
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
