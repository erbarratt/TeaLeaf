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
        [Tooltip("Full width, height and depth of the room's box along this object's own axes, in metres before the object's scale, e.g. (4, 3, 4) = a small 4m-square room. Boxes of neighbouring rooms should meet mid-wall; anything outside every room counts as one big outside room.")]
        [SerializeField] private Vector3 size = new(4f, 3f, 4f);

        // Where rooms overlap, the one with the higher priority wins, and
        // only between equal priorities the smallest. Left at 0 for rooms
        // placed by hand; the level generator's junction rooms are 1, so
        // each wins over the street rooms running into it.
        [Tooltip("Where rooms overlap, the higher priority wins; between equal priorities the smallest room wins, e.g. 1 makes a junction room win over the street rooms running into it. Leave at 0 for rooms placed by hand.")]
        [SerializeField] private int priority;

        [Header("Reverb (heard while the player is in this room)")]
        // How this room echoes. It's the listener's room that counts, not
        // the sound's: every sound the player hears while standing in here
        // gets this reverb, wherever it was made. Right-click the
        // component's title for presets to start from.
        [Tooltip("How this room echoes; every sound the player hears while standing in here gets it, wherever the sound was made, e.g. the Stone Hall preset for a big bare hall. Right-click the component's title for Reverb Preset/... starting points.")]
        [SerializeField] private ReverbSettings reverb = ReverbSettings.Room;

        [Header("Ambience (heard while the player is in this room)")]
        // A looping background for the room with no position of its own
        // (a warehouse's hum, wind in the rafters). Empty = silence. Rooms
        // that share a cue carry on with the same sound from one to the
        // next. A sound that comes from somewhere (a torch, a fire) is a
        // SoundLoop instead.
        [Tooltip("A looping background with no position, heard while the player is in this room (its first clip is used), e.g. a warehouse hum or wind in the rafters. Rooms sharing a cue carry on with the same sound between them. Empty = silence. A sound from somewhere (a torch) is a SoundLoop instead.")]
        [SerializeField] private SoundCue ambience;

        // Off, the room's ambience plays at the Sound Player's Ambience
        // Volume, the same everywhere; on, at this room's own volume.
        [Tooltip("On = this room's ambience plays at its own Ambience Volume below, e.g. a quieter side room. Off = it plays at the Sound Player's Ambience Volume, shared by every room without an override.")]
        [SerializeField] private bool overrideAmbienceVolume;

        // Multiplies the ambience cue's volume in this room, when
        // overridden.
        [Tooltip("Multiplies the Ambience cue's volume in this room, 0-1, used only with Override Ambience Volume on, e.g. 0.5 = the same hum at half loudness in a side room. Lower = quieter; 0 = silent.")]
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 1f;

        // Every enabled room in the scene. Rooms add and remove themselves,
        // so nothing ever searches the scene for them.
        private static readonly List<SoundRoom> _rooms = new();

        private static readonly Color _gizmoColor = new(0.3f, 0.6f, 1f, 0.5f);
        private static readonly Color _gizmoSelectedColor = new(0.3f, 0.6f, 1f, 1f);

        // The box as Contains() and Find() need it: the matrix that turns a
        // world point into this object's local space, half the box's size,
        // and its volume. Rooms don't move while playing and Find() is
        // called for every sound, so in Play Mode these are worked out
        // once and kept. In Edit Mode they're worked out afresh each time
        // (_isCached stays false), so a room being moved or resized is
        // right straight away.
        private Matrix4x4 _worldToLocal;
        private Vector3 _halfSize;
        private float _volume;
        private bool _isCached;

        /// <summary>
        /// The box's volume, for picking the smallest where rooms overlap
        /// (a cupboard inside a hall).
        /// </summary>
        private float Volume
        {
            get
            {
                if (!_isCached) {
                    Cache();
                }

                return _volume;
            }
        }

        /// <summary>
        /// Works out the box's matrix, half size and volume.
        /// </summary>
        private void Cache()
        {
            Vector3 scale = transform.lossyScale;
            _worldToLocal = transform.worldToLocalMatrix;
            _halfSize = size * 0.5f;
            _volume = Mathf.Abs(size.x * scale.x * size.y * scale.y * size.z * scale.z);
            _isCached = Application.isPlaying;
        }

        /// <summary>
        /// Editor-only: an Inspector change (the size, say) while playing
        /// throws the kept values away.
        /// </summary>
        private void OnValidate()
        {
            _isCached = false;
        }

        public ReverbSettings Reverb => reverb;
        public SoundCue Ambience => ambience;
        public bool OverrideAmbienceVolume => overrideAmbienceVolume;
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
            _isCached = false;
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
        /// room. Where rooms overlap, the highest priority, then the smallest.
        /// </summary>
        public static SoundRoom Find(Vector3 worldPoint)
        {
            SoundRoom best = null;

            for (int i = 0; i < _rooms.Count; i++) {
                SoundRoom room = _rooms[i];

                if (!room.Contains(worldPoint)) {
                    continue;
                }

                if (best == null || room.priority > best.priority || (room.priority == best.priority && room.Volume < best.Volume)) {
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
            if (!_isCached) {
                Cache();
            }

            Vector3 local = _worldToLocal.MultiplyPoint3x4(worldPoint);

            return Mathf.Abs(local.x) <= _halfSize.x
                && Mathf.Abs(local.y) <= _halfSize.y
                && Mathf.Abs(local.z) <= _halfSize.z;
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
