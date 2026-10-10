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

        [Header("Reverb (heard while the player is in this room)")]
        // How this room echoes. It's the listener's room that counts, not
        // the sound's: every sound the player hears while standing in here
        // gets this reverb, wherever it was made. Right-click the
        // component's title for presets to start from.
        [SerializeField] private ReverbSettings reverb = ReverbSettings.Room;

        [Header("Ambience (heard while the player is in this room)")]
        // A looping background for the room with no position of its own
        // (a warehouse's hum, wind in the rafters). Empty = silence. Rooms
        // that share a cue carry on with the same sound from one to the
        // next. A sound that comes from somewhere (a torch, a fire) is a
        // SoundLoop instead.
        [SerializeField] private SoundCue ambience;

        // Multiplies the ambience cue's volume in this room.
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 1f;

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

        public ReverbSettings Reverb => reverb;
        public SoundCue Ambience => ambience;
        public float AmbienceVolume => ambienceVolume;

        // Reverb presets, in the menu opened by right-clicking the
        // component's title (or its three dots). Each replaces the four
        // reverb numbers, to be tuned from there.
        [ContextMenu("Reverb Preset/None")]
        private void PresetNone() => SetReverb(ReverbSettings.None);

        [ContextMenu("Reverb Preset/Small Room")]
        private void PresetSmallRoom() => SetReverb(ReverbSettings.SmallRoom);

        [ContextMenu("Reverb Preset/Room")]
        private void PresetRoom() => SetReverb(ReverbSettings.Room);

        [ContextMenu("Reverb Preset/Stone Room")]
        private void PresetStoneRoom() => SetReverb(ReverbSettings.StoneRoom);

        [ContextMenu("Reverb Preset/Stone Hall")]
        private void PresetStoneHall() => SetReverb(ReverbSettings.StoneHall);

        [ContextMenu("Reverb Preset/Cellar")]
        private void PresetCellar() => SetReverb(ReverbSettings.Cellar);

        [ContextMenu("Reverb Preset/Warehouse")]
        private void PresetWarehouse() => SetReverb(ReverbSettings.Warehouse);

        [ContextMenu("Reverb Preset/Alley")]
        private void PresetAlley() => SetReverb(ReverbSettings.Alley);

        [ContextMenu("Reverb Preset/Outdoors")]
        private void PresetOutdoors() => SetReverb(ReverbSettings.Outdoors);

        /// <summary>
        /// Replaces the room's reverb with a preset, as an undoable edit.
        /// </summary>
        private void SetReverb(ReverbSettings settings)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Reverb Preset");
#endif
            reverb = settings;
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
