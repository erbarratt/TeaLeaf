using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Draws debug wireframes - lines, boxes, circles, spheres, capsules -
    /// built entirely from straight lines, to one of two places:
    /// - ForGizmos: Unity's Gizmos, for the Scene view (call only from
    ///   OnDrawGizmos/OnDrawGizmosSelected).
    /// - A new DebugLines(): a list of line vertices that CopyTo() turns into
    ///   a line mesh - what InHeadsetGizmos renders, so the same shapes show
    ///   in both eyes in VR.
    ///
    /// A component writes its drawing once against this class (see
    /// IDebugDrawable), so the Scene view and the headset always match.
    ///
    /// Like Gizmos, shapes are drawn in Matrix's space in Color. Everything
    /// ends up in Line(), which applies the matrix, so every shape works in
    /// any space.
    /// </summary>
    public sealed class DebugLines
    {
        // Straight lines per circle. 24 looks round at hand scale without
        // making spheres expensive (a sphere is three circles).
        private const int CircleSegments = 24;

        // Unit circle points, worked out once - no sin/cos per circle drawn.
        private static readonly Vector2[] _circlePoints = BuildCirclePoints();

        private readonly bool _toGizmos;

        // The mesh output: two vertices (and colours) per line. The index
        // list is just 0, 1, 2, ... and only ever grows, so it's built once.
        private readonly List<Vector3> _vertices = new();
        private readonly List<Color32> _colors = new();
        private readonly List<int> _indices = new();

        /// The instance that draws with Unity's Gizmos - Scene view only.
        public static DebugLines ForGizmos { get; } = new(true);

        /// The space shapes are drawn in (like Gizmos.matrix).
        public Matrix4x4 Matrix { get; set; } = Matrix4x4.identity;

        /// The colour shapes are drawn in (like Gizmos.color).
        public Color Color { get; set; } = Color.white;

        /// <summary>
        /// Creates an instance that collects lines for a mesh (see CopyTo()).
        /// </summary>
        public DebugLines() : this(false)
        {
        }

        private DebugLines(bool toGizmos)
        {
            _toGizmos = toGizmos;
        }

        /// <summary>
        /// Resets Matrix and Color, then has drawable draw itself - so one
        /// drawable's matrix can never leak into the next one's shapes.
        /// </summary>
        public void Draw(IDebugDrawable drawable, bool detailed)
        {
            Matrix = Matrix4x4.identity;
            Color = Color.white;
            drawable.DrawDebug(this, detailed);
        }

        /// <summary>
        /// Empties the collected lines, ready for the next frame. Keeps the
        /// lists' memory, so refilling them doesn't allocate.
        /// </summary>
        public void Clear()
        {
            _vertices.Clear();
            _colors.Clear();
        }

        /// <summary>
        /// Replaces mesh's contents with the collected lines. The mesh's
        /// bounds aren't recalculated (the renderer's bounds are fixed - see
        /// InHeadsetGizmos).
        /// </summary>
        public void CopyTo(Mesh mesh)
        {
            // The index list only has to be as long as the longest frame so far.
            while (_indices.Count < _vertices.Count) {
                _indices.Add(_indices.Count);
            }

            // Cleared first: setting fewer vertices than the old indices
            // point at is an error.
            mesh.Clear();
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetIndices(_indices, 0, _vertices.Count, MeshTopology.Lines, 0, false);
        }

        /// <summary>
        /// Draws a line from one point to another - what every other shape
        /// is made of.
        /// </summary>
        public void Line(Vector3 from, Vector3 to)
        {
            Matrix4x4 matrix = Matrix;
            from = matrix.MultiplyPoint3x4(from);
            to = matrix.MultiplyPoint3x4(to);

            if (_toGizmos) {
                Gizmos.color = Color;
                Gizmos.DrawLine(from, to);
                return;
            }

            Color32 color = Color;
            _vertices.Add(from);
            _vertices.Add(to);
            _colors.Add(color);
            _colors.Add(color);
        }

        /// <summary>
        /// Draws a circle around centre. axisA and axisB are two directions
        /// at right angles across the circle, each as long as the radius.
        /// </summary>
        public void Circle(Vector3 centre, Vector3 axisA, Vector3 axisB)
        {
            Vector3 previous = centre + axisA;

            for (int i = 1; i <= CircleSegments; i++) {
                Vector2 point = _circlePoints[i % CircleSegments];
                Vector3 next = centre + axisA * point.x + axisB * point.y;
                Line(previous, next);
                previous = next;
            }
        }

        /// <summary>
        /// Draws a sphere as three circles, one around each axis - the same
        /// look as Gizmos.DrawWireSphere.
        /// </summary>
        public void WireSphere(Vector3 centre, float radius)
        {
            Vector3 x = Vector3.right * radius;
            Vector3 y = Vector3.up * radius;
            Vector3 z = Vector3.forward * radius;

            Circle(centre, x, y);
            Circle(centre, x, z);
            Circle(centre, y, z);
        }

        /// <summary>
        /// Draws a box's 12 edges, like Gizmos.DrawWireCube.
        /// </summary>
        public void WireCube(Vector3 centre, Vector3 size)
        {
            Vector3 h = size * 0.5f;

            // The four corners of the bottom face and of the top face, in
            // the same order, so corner i of one sits under corner i of the
            // other.
            Vector3 b0 = centre + new Vector3(-h.x, -h.y, -h.z);
            Vector3 b1 = centre + new Vector3(h.x, -h.y, -h.z);
            Vector3 b2 = centre + new Vector3(h.x, -h.y, h.z);
            Vector3 b3 = centre + new Vector3(-h.x, -h.y, h.z);
            Vector3 t0 = centre + new Vector3(-h.x, h.y, -h.z);
            Vector3 t1 = centre + new Vector3(h.x, h.y, -h.z);
            Vector3 t2 = centre + new Vector3(h.x, h.y, h.z);
            Vector3 t3 = centre + new Vector3(-h.x, h.y, h.z);

            Line(b0, b1);
            Line(b1, b2);
            Line(b2, b3);
            Line(b3, b0);

            Line(t0, t1);
            Line(t1, t2);
            Line(t2, t3);
            Line(t3, t0);

            Line(b0, t0);
            Line(b1, t1);
            Line(b2, t2);
            Line(b3, t3);
        }

        /// <summary>
        /// Draws a capsule between two points (the centres of its rounded
        /// ends): a sphere at each end and four lines down its sides.
        /// </summary>
        public void WireCapsule(Vector3 a, Vector3 b, float radius)
        {
            WireSphere(a, radius);
            WireSphere(b, radius);

            Vector3 axis = b - a;

            if (axis.sqrMagnitude < 0.000001f) {
                return;
            }

            // Two directions at right angles to the capsule's axis, for the
            // side lines.
            Vector3 side = Vector3.Cross(axis, Vector3.up);

            if (side.sqrMagnitude < 0.000001f) {
                side = Vector3.Cross(axis, Vector3.right);
            }

            side = side.normalized * radius;
            Vector3 other = Vector3.Cross(axis.normalized, side);

            Line(a + side, b + side);
            Line(a - side, b - side);
            Line(a + other, b + other);
            Line(a - other, b - other);
        }

        /// <summary>
        /// The points of a unit circle, CircleSegments of them, starting at
        /// (1, 0).
        /// </summary>
        private static Vector2[] BuildCirclePoints()
        {
            var points = new Vector2[CircleSegments];

            for (int i = 0; i < CircleSegments; i++) {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            return points;
        }
    }
}
