using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// An opening sound can pass through between two SoundRooms (or a room
    /// and outside): a doorway, stairwell, window or archway. A flat
    /// rectangle centred on this object, in its local X/Y plane, with local
    /// Z pointing through the opening. Place it in the middle of the wall's
    /// thickness, filling the opening.
    ///
    /// The two rooms it joins aren't assigned by hand: SoundPropagation
    /// looks a short way out from each face (probeDistance) and takes
    /// whatever room is there.
    ///
    /// A portal can be closed (a shut door): sound still gets through, but
    /// muffled and as if it had travelled further. One that starts closed
    /// and never opens is a thin wall or a window that leaks a little sound.
    /// </summary>
    public class SoundPortal : MonoBehaviour, IDebugDrawable
    {
        // The opening's width and height, in metres (before the object's
        // scale).
        [SerializeField] private Vector2 size = new(1f, 2.1f);

        // How far out from each face to look for the room on that side, in
        // metres. More than half the wall's thickness.
        [SerializeField] private float probeDistance = 0.25f;

        // How much sound is muffled just by coming through the opening while
        // it's open: 0 = not at all, 1 = fully. Mild, and it adds up, so a
        // sound from three rooms away is duller than one from next door.
        // Not applied to a sound heard in a straight line through the
        // opening from the room next door.
        [SerializeField, Range(0f, 1f)] private float openMuffle = 0.3f;

        [Header("Closed (a shut door, or a thin wall)")]
        [SerializeField] private bool startsOpen = true;

        // How much the sound is muffled passing through while closed: 0 =
        // not at all, 1 = fully.
        [SerializeField, Range(0f, 1f)] private float closedMuffle = 0.6f;

        // Passing through while closed counts as this many extra metres
        // travelled, so the sound is quieter and reaches less far.
        [SerializeField] private float closedExtraDistance = 4f;

        // Every enabled portal in the scene (self-registering, like the
        // rooms).
        private static readonly List<SoundPortal> _portals = new();

        private static readonly Color _openColor = new(0.2f, 1f, 1f, 1f);
        private static readonly Color _closedColor = new(1f, 0.3f, 0.3f, 1f);
        private static readonly Color _brokenColor = new(1f, 0.9f, 0f, 1f);

        /// How many portals are enabled.
        public static int Count => _portals.Count;

        /// Whether sound passes freely. Set with SetOpen().
        public bool IsOpen { get; private set; }

        /// The rooms on the +Z and -Z sides (null = outside). Set by
        /// SoundPropagation when it rebuilds.
        public SoundRoom FrontRoom { get; private set; }
        public SoundRoom BackRoom { get; private set; }

        /// The middle of the opening, in world space.
        public Vector3 Centre => transform.position;

        /// Extra metres added to a path through this portal (0 while open).
        public float Penalty => IsOpen ? 0f : closedExtraDistance;

        /// Muffle added to a path through this portal: mild while open, more
        /// while closed.
        public float Muffle => IsOpen ? openMuffle : closedMuffle;

        /// <summary>
        /// The portal at index - read with Count in a for loop.
        /// </summary>
        public static SoundPortal Get(int index)
        {
            return _portals[index];
        }

        private void Awake()
        {
            IsOpen = startsOpen;
        }

        private void OnEnable()
        {
            _portals.Add(this);
            DebugDrawRegistry.Register(this);
            SoundPropagation.MarkLayoutDirty();
        }

        private void OnDisable()
        {
            _portals.Remove(this);
            DebugDrawRegistry.Unregister(this);
            SoundPropagation.MarkLayoutDirty();
        }

        /// <summary>
        /// Opens or closes the portal - for a door to call as it opens and
        /// shuts. The sound paths are worked out again the next time one is
        /// needed.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (open == IsOpen) {
                return;
            }

            IsOpen = open;
            SoundPropagation.MarkDirty();
        }

        /// <summary>
        /// Looks up the room on each side and remembers them. Called by
        /// SoundPropagation when the level's rooms or portals have changed.
        /// </summary>
        public void FindRooms()
        {
            ProbeRooms(out SoundRoom front, out SoundRoom back);
            FrontRoom = front;
            BackRoom = back;

            if (FrontRoom == BackRoom) {
                Debug.LogWarning($"SoundPortal: '{name}' has the same room on both sides, so it joins nothing. Check its position, which way it faces, and Probe Distance.", this);
            }
        }

        /// <summary>
        /// Works out the room on each side right now, without storing it:
        /// the room containing the point probeDistance out from the +Z face
        /// (front) and from the -Z face (back); null = outside. Also used in
        /// Edit Mode, by the gizmo and the Inspector, to show what the
        /// portal joins while it's being placed.
        /// </summary>
        public void ProbeRooms(out SoundRoom front, out SoundRoom back)
        {
            Vector3 offset = transform.forward * probeDistance;
            front = SoundRoom.Find(transform.position + offset);
            back = SoundRoom.Find(transform.position - offset);
        }

        /// <summary>
        /// Whether room is on one side of this portal (null = outside).
        /// </summary>
        public bool Borders(SoundRoom room)
        {
            return FrontRoom == room || BackRoom == room;
        }

        /// <summary>
        /// The point of the opening that sound going from one world-space
        /// point to another passes through: where the straight line between
        /// them crosses the portal's plane, pulled inside the rectangle if
        /// the line misses it. So when the listener can see the source
        /// through the opening this is on the true line of sight, and
        /// otherwise it's the nearest part of the opening - the edge of the
        /// doorway the sound bends round.
        /// </summary>
        public Vector3 CrossingPoint(Vector3 from, Vector3 to)
        {
            return CrossingPoint(from, to, out _);
        }

        /// <summary>
        /// The same, also reporting whether the straight line really does
        /// pass through the opening (isStraightThrough) - the two points are
        /// on opposite sides and the line didn't have to be pulled inside
        /// the rectangle.
        /// </summary>
        public Vector3 CrossingPoint(Vector3 from, Vector3 to, out bool isStraightThrough)
        {
            Vector3 a = transform.InverseTransformPoint(from);
            Vector3 b = transform.InverseTransformPoint(to);

            // In local space the plane is z = 0, so t is how far along the
            // line from a to b the z value reaches 0. If both points are on
            // the same side (or level with the plane) there's no crossing:
            // take the end nearer the plane.
            float depth = a.z - b.z;
            float t = Mathf.Abs(depth) > 0.0001f ? Mathf.Clamp01(a.z / depth) : 0.5f;
            Vector3 local = Vector3.Lerp(a, b, t);

            // Opposite sides = the two z values have different signs, so
            // multiplying them gives a negative number.
            isStraightThrough = a.z * b.z < 0f
                && Mathf.Abs(local.x) <= size.x * 0.5f
                && Mathf.Abs(local.y) <= size.y * 0.5f;

            local.x = Mathf.Clamp(local.x, -size.x * 0.5f, size.x * 0.5f);
            local.y = Mathf.Clamp(local.y, -size.y * 0.5f, size.y * 0.5f);
            local.z = 0f;

            return transform.TransformPoint(local);
        }

        /// <summary>
        /// Draws the portal in the Scene view at all times, since it has no
        /// mesh - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// The opening's rectangle with a cross through it, and a short line
        /// out of each face to where it looks for its rooms. Cyan while open,
        /// red while closed (in Edit Mode, by Starts Open), yellow if it has
        /// the same room on both sides. Shared by the Scene view gizmos and
        /// the in-headset view (InHeadsetGizmos).
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            // A portal with the same room on both sides joins nothing: drawn
            // in the warning colour so the mistake shows while placing it.
            ProbeRooms(out SoundRoom front, out SoundRoom back);
            bool open = Application.isPlaying ? IsOpen : startsOpen;

            if (front == back) {
                lines.Color = _brokenColor;
            } else {
                lines.Color = open ? _openColor : _closedColor;
            }

            lines.Matrix = transform.localToWorldMatrix;

            float x = size.x * 0.5f;
            float y = size.y * 0.5f;
            Vector3 bottomLeft = new(-x, -y, 0f);
            Vector3 bottomRight = new(x, -y, 0f);
            Vector3 topLeft = new(-x, y, 0f);
            Vector3 topRight = new(x, y, 0f);

            lines.Line(bottomLeft, bottomRight);
            lines.Line(bottomRight, topRight);
            lines.Line(topRight, topLeft);
            lines.Line(topLeft, bottomLeft);
            lines.Line(bottomLeft, topRight);
            lines.Line(bottomRight, topLeft);
            lines.Line(new Vector3(0f, 0f, -probeDistance), new Vector3(0f, 0f, probeDistance));
        }
    }
}
