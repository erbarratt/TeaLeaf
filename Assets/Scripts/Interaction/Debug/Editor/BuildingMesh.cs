using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Interaction
{
    /// <summary>
    /// Collects flat faces into one mesh, for editor scripts that build a
    /// building from code (SmithyBuilding). One of these per material: every
    /// box, beam and roof slope in that material is added to it, and the
    /// result is a single mesh - one draw call - instead of hundreds of
    /// separate cubes.
    ///
    /// Each face is given the two directions its texture runs along (u and
    /// v), and a point's texture coordinate is simply how far along those
    /// directions it is, in metres, divided by tileSize (how many metres
    /// one repeat of the texture covers). So a texture is the same size on
    /// every piece however big the piece is, and carries on unbroken from
    /// one piece to the next. The u direction is also stored as the
    /// tangent, which a normal map needs.
    ///
    /// Matrix moves and turns everything added after it's set, so a part
    /// (a wall, a ladder) can be written in its own simple coordinates.
    /// Texture coordinates are worked out before the matrix is applied.
    ///
    /// Faces are flat-shaded: no points are shared between faces, so every
    /// edge is a hard one. In an Editor folder, so never in a build.
    /// </summary>
    public class BuildingMesh
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector4> _tangents = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int> _triangles = new();

        // Metres covered by one repeat of the texture.
        private readonly float _tileSize;

        /// <summary>
        /// Where everything added next is placed: its position and
        /// rotation (no scaling).
        /// </summary>
        public Matrix4x4 Matrix { get; set; } = Matrix4x4.identity;

        public bool IsEmpty => _vertices.Count == 0;

        public BuildingMesh(float tileSize)
        {
            _tileSize = Mathf.Max(tileSize, 0.01f);
        }

        /// <summary>
        /// One flat face with any number of corners, given in order round
        /// its edge (either way round). It must be convex - no dents - as
        /// it's split into triangles fanning out from the first corner.
        /// normal is the way it faces; uAxis and vAxis are the directions
        /// its texture runs along.
        /// </summary>
        public void Polygon(Vector3 normal, Vector3 uAxis, Vector3 vAxis, params Vector3[] points)
        {
            int first = _vertices.Count;

            Vector3 worldNormal = Matrix.MultiplyVector(normal).normalized;
            Vector3 worldTangent = Matrix.MultiplyVector(uAxis).normalized;

            // The tangent's fourth number says which way v runs relative
            // to u and the normal: +1 or -1.
            float handedness = Vector3.Dot(Vector3.Cross(normal, uAxis), vAxis) < 0f ? -1f : 1f;
            Vector4 tangent = new(worldTangent.x, worldTangent.y, worldTangent.z, handedness);

            for (int i = 0; i < points.Length; i++) {
                _vertices.Add(Matrix.MultiplyPoint3x4(points[i]));
                _normals.Add(worldNormal);
                _tangents.Add(tangent);
                _uvs.Add(new Vector2(Vector3.Dot(points[i], uAxis), Vector3.Dot(points[i], vAxis)) / _tileSize);
            }

            // Unity draws the side of a triangle its corners go clockwise
            // round. Rather than trust every caller to list corners the
            // right way, check: this cross product points out of the
            // drawn side, so if it disagrees with the normal, the corners
            // are used in the opposite order.
            bool flip = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0f;

            for (int i = 1; i < points.Length - 1; i++) {
                _triangles.Add(first);
                _triangles.Add(first + (flip ? i + 1 : i));
                _triangles.Add(first + (flip ? i : i + 1));
            }
        }

        /// <summary>
        /// A box at any angle: centre, its three directions (each of
        /// length 1, square to each other) and its size along each. The
        /// texture runs along right on the top, bottom, front and back
        /// faces - so a beam whose length is along right has its grain
        /// along its length - and up the box on its four sides.
        /// </summary>
        public void OrientedBox(Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 size)
        {
            Vector3 x = right * (size.x * 0.5f);
            Vector3 y = up * (size.y * 0.5f);
            Vector3 z = forward * (size.z * 0.5f);

            Polygon(up, right, forward, centre + y - x - z, centre + y - x + z, centre + y + x + z, centre + y + x - z);
            Polygon(-up, right, forward, centre - y - x - z, centre - y - x + z, centre - y + x + z, centre - y + x - z);
            Polygon(forward, right, up, centre + z - x - y, centre + z - x + y, centre + z + x + y, centre + z + x - y);
            Polygon(-forward, right, up, centre - z - x - y, centre - z - x + y, centre - z + x + y, centre - z + x - y);
            Polygon(right, forward, up, centre + x - z - y, centre + x - z + y, centre + x + z + y, centre + x + z - y);
            Polygon(-right, forward, up, centre - x - z - y, centre - x - z + y, centre - x + z + y, centre - x + z - y);
        }

        /// <summary>
        /// A box square to the axes, from its lowest corner to its highest.
        /// </summary>
        public void Box(Vector3 min, Vector3 max)
        {
            OrientedBox((min + max) * 0.5f, Vector3.right, Vector3.up, Vector3.forward, max - min);
        }

        /// <summary>
        /// A straight beam from one point to another, with its grain along
        /// its length. depthAxis is the direction its depth is measured in
        /// (for a beam on a wall, out of the wall) and must be square to
        /// the beam; width is its size the other way across.
        /// </summary>
        public void Beam(Vector3 from, Vector3 to, float width, float depth, Vector3 depthAxis)
        {
            Vector3 along = to - from;
            float length = along.magnitude;

            if (length < 0.0001f) {
                return;
            }

            along /= length;
            Vector3 across = Vector3.Cross(depthAxis, along).normalized;

            OrientedBox((from + to) * 0.5f, along, across, depthAxis, new Vector3(length, width, depth));
        }

        /// <summary>
        /// A flat convex shape pushed through a thickness: a triangular
        /// gable wall, a wedge of masonry over an arch. points are the
        /// shape's corners in order; extrude is how far and which way it's
        /// pushed. uAxis and vAxis are the texture directions on its two
        /// flat faces.
        /// </summary>
        public void Prism(Vector3[] points, Vector3 extrude, Vector3 uAxis, Vector3 vAxis)
        {
            int count = points.Length;
            Vector3 direction = extrude.normalized;
            Vector3 middle = Vector3.zero;

            Vector3[] far = new Vector3[count];

            for (int i = 0; i < count; i++) {
                far[i] = points[i] + extrude;
                middle += points[i];
            }

            middle /= count;

            Polygon(-direction, uAxis, vAxis, points);
            Polygon(direction, uAxis, vAxis, far);

            // The sides: one four-cornered face per edge of the shape,
            // facing away from the shape's middle.
            for (int i = 0; i < count; i++) {
                Vector3 a = points[i];
                Vector3 b = points[(i + 1) % count];
                Vector3 edge = (b - a).normalized;
                Vector3 normal = Vector3.Cross(edge, direction).normalized;

                if (Vector3.Dot(normal, a - middle) < 0f) {
                    normal = -normal;
                }

                Polygon(normal, edge, direction, a, b, b + extrude, a + extrude);
            }
        }

        /// <summary>
        /// An upright round shape made of flat sides, standing on
        /// baseCentre: a barrel, a tree stump, a hoop. heights and radii
        /// describe its outline from the bottom up (the radius at each
        /// height), so three entries with a wider middle make a barrel's
        /// bulge. The texture runs round it and up it.
        /// </summary>
        public void Cylinder(Vector3 baseCentre, float[] heights, float[] radii, int sides, bool capTop, bool capBottom)
        {
            for (int ring = 0; ring < heights.Length - 1; ring++) {
                for (int side = 0; side < sides; side++) {
                    Vector3 a = RingDirection(side, sides);
                    Vector3 b = RingDirection(side + 1, sides);
                    Vector3 lowUp = Vector3.up * heights[ring];
                    Vector3 highUp = Vector3.up * heights[ring + 1];

                    Polygon(
                        (a + b).normalized,
                        (b - a).normalized,
                        Vector3.up,
                        baseCentre + a * radii[ring] + lowUp,
                        baseCentre + b * radii[ring] + lowUp,
                        baseCentre + b * radii[ring + 1] + highUp,
                        baseCentre + a * radii[ring + 1] + highUp);
                }
            }

            if (capTop) {
                Cap(baseCentre + Vector3.up * heights[^1], radii[^1], sides, Vector3.up);
            }

            if (capBottom) {
                Cap(baseCentre + Vector3.up * heights[0], radii[0], sides, Vector3.down);
            }
        }

        /// <summary>
        /// The flat round end of a Cylinder().
        /// </summary>
        private void Cap(Vector3 centre, float radius, int sides, Vector3 normal)
        {
            Vector3[] points = new Vector3[sides];

            for (int side = 0; side < sides; side++) {
                points[side] = centre + RingDirection(side, sides) * radius;
            }

            Polygon(normal, Vector3.right, Vector3.forward, points);
        }

        /// <summary>
        /// The level direction (length 1) to corner number side of a round
        /// shape with that many sides.
        /// </summary>
        private static Vector3 RingDirection(int side, int sides)
        {
            float angle = side * 2f * Mathf.PI / sides;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        /// <summary>
        /// Writes everything collected into mesh, replacing what it held.
        /// </summary>
        public void WriteTo(Mesh mesh)
        {
            mesh.Clear();

            // The default index size only reaches 65,535 points.
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetTangents(_tangents);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
