using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGen
{
    /// <summary>
    /// Suggests which way a piece faces from its LOD0 meshes, for the
    /// catalogue. The pack's walls have two big sides: the front is
    /// detailed (many triangles at many depths) and the back is one flat
    /// plane (few triangles, all in one plane). So for a slab-shaped piece,
    /// the triangles facing each way along its thin axis are compared, and
    /// the busier side is the front. The flat back plane then gives the
    /// wall's true module size and where it joins its neighbours.
    ///
    /// A corner is a piece with that pattern along both level axes (two
    /// flat backs at right angles). Anything else - a fence, an arch, a
    /// floor - gets no suggestion. Every suggestion is confirmed by eye in
    /// stage 2; nothing here is trusted on its own.
    ///
    /// Mesh data is read with Mesh.AcquireReadOnlyMeshData, which leaves
    /// the meshes' import settings alone.
    /// </summary>
    internal static class WallOrientation
    {
        // CatalogueEntry.orientation values.
        internal const string KindWall = "wall";
        internal const string KindCorner = "corner";
        internal const string KindUnsure = "unsure";
        internal const string KindNotSlab = "not-a-slab";
        internal const string KindNoMeshData = "no-mesh-data";

        // A piece is a slab when its thin level side is at most SlabRatio of its
        // other level side, and at most SlabDepthRatio of the larger of that
        // side and its height (so a narrow but tall half-wall counts).
        private const float SlabRatio = 0.6f;
        private const float SlabDepthRatio = 0.5f;

        // A triangle faces a side when its normal is within about 45 degrees of
        // that side's axis.
        private const float FacingDot = 0.7f;

        // Triangles within this many metres of the same depth are in one plane.
        private const float PlaneTolerance = 0.01f;

        // A wall's back must have at least FlatBack of its area in one plane,
        // and be at least FlatterBy flatter than its front (planarity 0-1).
        private const float FlatBack = 0.7f;
        private const float FlatterBy = 0.2f;

        // A corner needs, along both level axes, a front with at least
        // CornerRatio times the back's triangles and a back at least
        // CornerFlatBack flat; the back of an L is two arms' inner faces plus
        // their end caps, so it is less flat than a wall's. It must also be at
        // least CornerMinHeight of its larger level side tall, so a floor tile
        // never counts.
        private const float CornerRatio = 1.5f;
        private const float CornerFlatBack = 0.4f;
        private const float CornerMinHeight = 0.25f;

        // Confidence grows with how many times more triangles the front has
        // than the back, reaching 1 at FullConfidenceRatio; under
        // MinConfidence the result is "unsure".
        private const float FullConfidenceRatio = 10f;
        private const float MinConfidence = 0.3f;

        /// <summary>
        /// One mesh drawn at LOD0, with the matrix that takes it into the
        /// prefab root's space.
        /// </summary>
        internal readonly struct DrawnMesh
        {
            public readonly Mesh Mesh;
            public readonly Matrix4x4 Matrix;

            public DrawnMesh(Mesh mesh, Matrix4x4 matrix)
            {
                Mesh = mesh;
                Matrix = matrix;
            }
        }

        /// <summary>
        /// One LOD0 triangle in root space, kept with the side it faces.
        /// </summary>
        internal readonly struct Triangle
        {
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 C;
            public readonly float Depth;

            public Triangle(Vector3 a, Vector3 b, Vector3 c, float depth)
            {
                A = a;
                B = b;
                C = c;
                Depth = depth;
            }
        }

        /// <summary>
        /// The triangles facing one way along one level axis.
        /// </summary>
        internal class Side
        {
            public readonly int Axis;
            public readonly float Sign;
            public readonly string Name;
            public readonly List<Triangle> Triangles = new();
            public readonly Dictionary<int, float> AreaByDepth = new();
            public float Area;
            public float Planarity;
            public float PlaneDepth;

            public Side(int axis, float sign, string name)
            {
                Axis = axis;
                Sign = sign;
                Name = name;
            }

            public Vector3 Direction
            {
                get
                {
                    Vector3 direction = Vector3.zero;
                    direction[Axis] = Sign;
                    return direction;
                }
            }
        }

        /// <summary>
        /// Fills in the entry's orientation, suggested fronts, confidence,
        /// back plane, connection points and side stats from the meshes it
        /// draws at LOD0. Needs the entry's render box already measured.
        /// Returns the four sides (+X, -X, +Z, -Z, in that order) for
        /// PieceShape to measure further, or null if no mesh could be read.
        /// </summary>
        internal static Side[] Suggest(CatalogueEntry entry, List<DrawnMesh> drawn)
        {
            Side[] sides = {
                new(0, 1f, "+X"), new(0, -1f, "-X"), new(2, 1f, "+Z"), new(2, -1f, "-Z")
            };

            if (!TryCollect(drawn, sides)) {
                entry.orientation = KindNoMeshData;
                return null;
            }

            foreach (Side side in sides) {
                FindPlane(side);
                entry.sides.Add(new SideStats {
                    side = side.Name,
                    triangles = side.Triangles.Count,
                    area = Round(side.Area),
                    planarity = Round(side.Planarity),
                    planeDepth = Round(side.PlaneDepth)
                });
            }

            float sizeX = entry.renderSize.x;
            float sizeZ = entry.renderSize.z;
            float thin = Mathf.Min(sizeX, sizeZ);
            float other = Mathf.Max(sizeX, sizeZ);

            // A slab: one level axis is thin, so that's the one the wall faces along.
            if (thin <= other * SlabRatio && thin <= Mathf.Max(other, entry.renderSize.y) * SlabDepthRatio) {
                int axisIndex = sizeX <= sizeZ ? 0 : 1;
                bool sure = JudgeWall(sides[axisIndex * 2], sides[axisIndex * 2 + 1], out Side front, out Side back, out float confidence);
                entry.frontConfidence = Round(confidence);

                if (!sure) {
                    entry.orientation = KindUnsure;
                    return sides;
                }

                entry.orientation = KindWall;
                entry.suggestedFronts.Add(front.Direction);
                AddBackPlane(entry, back, front.Direction);
                return sides;
            }

            // Not a slab: a corner if it stands up and both level axes show the pattern.
            if (entry.renderSize.y < other * CornerMinHeight) {
                entry.orientation = KindNotSlab;
                return sides;
            }

            bool sureX = JudgeCorner(sides[0], sides[1], out Side frontX, out float confidenceX);
            bool sureZ = JudgeCorner(sides[2], sides[3], out Side frontZ, out float confidenceZ);

            if (sureX && sureZ) {
                entry.orientation = KindCorner;
                entry.suggestedFronts.Add(frontX.Direction);
                entry.suggestedFronts.Add(frontZ.Direction);
                entry.frontConfidence = Round(Mathf.Min(confidenceX, confidenceZ));
                return sides;
            }

            entry.orientation = KindNotSlab;
            return sides;
        }

        /// <summary>
        /// Reads every drawn mesh's triangles into root space and files each
        /// under the side it faces (or none). False if no mesh data could
        /// be read.
        /// </summary>
        private static bool TryCollect(List<DrawnMesh> drawn, Side[] sides)
        {
            bool readAny = false;

            foreach (DrawnMesh item in drawn) {
                readAny |= ReadTriangles(item.Mesh, item.Matrix, (a, b, c) => AddTriangle(sides, a, b, c));
            }

            return readAny;
        }

        /// <summary>
        /// Calls back with every triangle of a mesh, taken through a matrix
        /// (into the prefab root's space), wound so that the normal from
        /// (b - a) x (c - a) points out of the surface even under a mirrored
        /// matrix. Reads with Mesh.AcquireReadOnlyMeshData, so the mesh
        /// needn't be Read/Write. False if it couldn't be read or is empty.
        /// </summary>
        internal static bool ReadTriangles(Mesh mesh, Matrix4x4 matrix, System.Action<Vector3, Vector3, Vector3> onTriangle)
        {
            Mesh.MeshDataArray dataArray;

            try {
                dataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            } catch (System.Exception) {
                return false;
            }

            try {
                Mesh.MeshData data = dataArray[0];

                if (data.vertexCount == 0) {
                    return false;
                }

                // A mirrored matrix turns the winding round, so normals made
                // from it would point inwards: two corners are swapped back.
                bool mirrored = matrix.determinant < 0f;
                using NativeArray<Vector3> vertices = new(data.vertexCount, Allocator.Temp);
                data.GetVertices(vertices);

                for (int sub = 0; sub < data.subMeshCount; sub++) {
                    SubMeshDescriptor descriptor = data.GetSubMesh(sub);

                    if (descriptor.topology != MeshTopology.Triangles) {
                        continue;
                    }

                    using NativeArray<int> indices = new(descriptor.indexCount, Allocator.Temp);
                    data.GetIndices(indices, sub);

                    for (int i = 0; i + 2 < indices.Length; i += 3) {
                        Vector3 a = matrix.MultiplyPoint3x4(vertices[indices[i]]);
                        Vector3 b = matrix.MultiplyPoint3x4(vertices[indices[i + 1]]);
                        Vector3 c = matrix.MultiplyPoint3x4(vertices[indices[i + 2]]);

                        if (mirrored) {
                            onTriangle(a, c, b);
                        } else {
                            onTriangle(a, b, c);
                        }
                    }
                }

                return true;
            } finally {
                dataArray.Dispose();
            }
        }

        /// <summary>
        /// Files one root-space triangle under the side its normal faces, if
        /// any, by its area and the depth of its centre along that side's axis.
        /// </summary>
        internal static void AddTriangle(Side[] sides, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float doubleArea = cross.magnitude;

            if (doubleArea < 1e-8f) {
                return;
            }

            Vector3 normal = cross / doubleArea;

            foreach (Side side in sides) {
                if (normal[side.Axis] * side.Sign < FacingDot) {
                    continue;
                }

                float area = doubleArea * 0.5f;
                float depth = (a[side.Axis] + b[side.Axis] + c[side.Axis]) / 3f;
                int bin = Mathf.RoundToInt(depth / PlaneTolerance);
                side.Triangles.Add(new Triangle(a, b, c, depth));
                side.AreaByDepth.TryGetValue(bin, out float binArea);
                side.AreaByDepth[bin] = binArea + area;
                side.Area += area;
                return;
            }
        }

        /// <summary>
        /// Finds the depth holding the most of a side's area (counting the
        /// bins either side of it, so a plane on a bin edge isn't split) and
        /// how much of the side's area is in that plane.
        /// </summary>
        internal static void FindPlane(Side side)
        {
            if (side.Area <= 0f) {
                return;
            }

            int bestBin = 0;
            float bestArea = -1f;

            foreach (int bin in side.AreaByDepth.Keys) {
                side.AreaByDepth.TryGetValue(bin - 1, out float below);
                side.AreaByDepth.TryGetValue(bin + 1, out float above);
                float area = below + side.AreaByDepth[bin] + above;

                // Ties go to the lower bin, so the result never depends on the
                // dictionary's order.
                if (area > bestArea || (Mathf.Approximately(area, bestArea) && bin < bestBin)) {
                    bestArea = area;
                    bestBin = bin;
                }
            }

            side.PlaneDepth = bestBin * PlaneTolerance;
            side.Planarity = Mathf.Clamp01(bestArea / side.Area);
        }

        /// <summary>
        /// Of a slab's two big sides, picks the back and the front and says
        /// whether that's sure: the back must be flat, and clearly flatter
        /// than the front, and the confidence high enough.
        /// </summary>
        private static bool JudgeWall(Side a, Side b, out Side front, out Side back, out float confidence)
        {
            if (!Compare(a, b, out front, out back, out float ratio)) {
                confidence = 0f;
                return false;
            }

            confidence = Confidence(ratio, back);
            return back.Planarity >= FlatBack &&
                   back.Planarity >= front.Planarity + FlatterBy &&
                   confidence >= MinConfidence;
        }

        /// <summary>
        /// One level axis of a possible corner: whether its busier side has
        /// enough more triangles than a fairly flat back.
        /// </summary>
        private static bool JudgeCorner(Side a, Side b, out Side front, out float confidence)
        {
            if (!Compare(a, b, out front, out Side back, out float ratio)) {
                confidence = 0f;
                return false;
            }

            confidence = Confidence(ratio, back);
            return ratio >= CornerRatio && back.Planarity >= CornerFlatBack;
        }

        /// <summary>
        /// Of two opposite sides, the back is the one with fewer triangles
        /// (the flatter on a tie) and the front the other; ratio is how many
        /// times more triangles the front has. False when the back has no
        /// triangles at all (an open back), which says nothing.
        /// </summary>
        private static bool Compare(Side a, Side b, out Side front, out Side back, out float ratio)
        {
            bool aIsBack = a.Triangles.Count < b.Triangles.Count ||
                           (a.Triangles.Count == b.Triangles.Count && a.Planarity >= b.Planarity);
            back = aIsBack ? a : b;
            front = aIsBack ? b : a;

            if (back.Triangles.Count == 0) {
                ratio = 0f;
                return false;
            }

            ratio = (float)front.Triangles.Count / back.Triangles.Count;
            return true;
        }

        /// <summary>
        /// How sure a front is: rises with how many times busier the front
        /// is than the back (1 at FullConfidenceRatio), times how flat the
        /// back is.
        /// </summary>
        private static float Confidence(float ratio, Side back)
        {
            return Mathf.Clamp01((ratio - 1f) / (FullConfidenceRatio - 1f)) * back.Planarity;
        }

        /// <summary>
        /// The box round the back's in-plane triangles, and from it the
        /// wall's suggested connection points: left and right at the base of
        /// the plane's two ends (as seen from in front of the wall), top and
        /// bottom at the middle of its upper and lower edges.
        /// </summary>
        internal static void AddBackPlane(CatalogueEntry entry, Side back, Vector3 front)
        {
            if (!TryGetPlaneBox(back, out Bounds plane)) {
                return;
            }

            entry.hasBackPlane = true;
            entry.backPlaneMin = Round(plane.min);
            entry.backPlaneMax = Round(plane.max);
            entry.backPlaneSize = Round(plane.size);
            AddEdgeConnections(entry, plane, back.Axis, back.PlaneDepth, front);
        }

        /// <summary>
        /// The box round a side's in-plane triangles (those at its plane
        /// depth). False if it has none.
        /// </summary>
        internal static bool TryGetPlaneBox(Side side, out Bounds plane)
        {
            plane = default;
            bool hasPlane = false;

            foreach (Triangle triangle in InPlane(side)) {
                Grow(ref plane, ref hasPlane, triangle.A);
                Grow(ref plane, ref hasPlane, triangle.B);
                Grow(ref plane, ref hasPlane, triangle.C);
            }

            return hasPlane;
        }

        /// <summary>
        /// A side's triangles that lie in its plane.
        /// </summary>
        internal static IEnumerable<Triangle> InPlane(Side side)
        {
            foreach (Triangle triangle in side.Triangles) {
                if (Mathf.Abs(triangle.Depth - side.PlaneDepth) <= PlaneTolerance * 1.5f) {
                    yield return triangle;
                }
            }
        }

        /// <summary>
        /// Left, right, top and bottom connection points on a box's face at
        /// the given depth along the back's axis: left and right at the base
        /// of its two ends as seen from in front, top and bottom at the
        /// middle of its upper and lower edges, each with the way out.
        /// </summary>
        internal static void AddEdgeConnections(CatalogueEntry entry, Bounds box, int backAxis, float depth, Vector3 front)
        {
            // Seen from in front, looking back at the piece (along -front), the
            // viewer's right is up x -front in Unity's left-handed axes.
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            int widthAxis = backAxis == 0 ? 2 : 0;
            float halfWidth = box.size[widthAxis] * 0.5f;

            Vector3 centre = box.center;
            centre[backAxis] = depth;
            Vector3 baseCentre = new(centre.x, box.min.y, centre.z);
            Vector3 topCentre = new(centre.x, box.max.y, centre.z);

            AddConnection(entry, "left", baseCentre - right * halfWidth, -right);
            AddConnection(entry, "right", baseCentre + right * halfWidth, right);
            AddConnection(entry, "top", topCentre, Vector3.up);
            AddConnection(entry, "bottom", baseCentre, Vector3.down);
        }

        /// <summary>
        /// Adds one rounded connection point to the entry.
        /// </summary>
        internal static void AddConnection(CatalogueEntry entry, string name, Vector3 position, Vector3 direction)
        {
            entry.suggestedConnections.Add(new ConnectionPoint {
                name = name,
                position = Round(position),
                direction = Round(direction)
            });
        }

        /// <summary>
        /// Grows a box (or starts it) to take in a point.
        /// </summary>
        private static void Grow(ref Bounds box, ref bool hasBox, Vector3 point)
        {
            if (hasBox) {
                box.Encapsulate(point);
            } else {
                box = new Bounds(point, Vector3.zero);
                hasBox = true;
            }
        }

        /// <summary>
        /// Rounds to the millimetre, as the rest of the catalogue is.
        /// </summary>
        internal static float Round(float value)
        {
            // Adding 0 turns -0 into 0, so the file never says "-0.0".
            return Mathf.Round(value * 1000f) / 1000f + 0f;
        }

        /// <summary>
        /// Rounds each axis to the millimetre.
        /// </summary>
        internal static Vector3 Round(Vector3 value)
        {
            return new Vector3(Round(value.x), Round(value.y), Round(value.z));
        }
    }
}
