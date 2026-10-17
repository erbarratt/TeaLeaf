using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Finds a pipe piece's open ends from its LOD0 meshes, as suggested
    /// connection points ("a", "b", ...) for the Review Pieces window: one at
    /// the middle of each flange where the pipe meets a face of its render
    /// box, pointing out of the piece. A suggestion only - the maintainer
    /// checks each point by eye and drags it where it is wrong (a capped end,
    /// a valve or a vent may well be).
    ///
    /// The pack's pipe parts are only ever turned by right angles, so every
    /// end faces along one of the root's six axes. An end is told from a
    /// face the pipe merely touches by its shape: the vertices on an end's
    /// face make a round flange, as wide one way as the other and going all
    /// the way round its middle, where a straight's side touches the face
    /// along a line and an elbow's bend along an arc.
    /// </summary>
    internal static class PipeEnds
    {
        // How close to a face of the render box a vertex must be to count as
        // lying on it, in metres (root space).
        private const float FaceTolerance = 0.015f;
        // The fewest vertices that make an end (a flange's face has dozens).
        private const int MinVertices = 8;
        // The most an end's width one way may differ from the other, as a
        // share of the larger (a flange is round).
        private const float MaxOutOfRound = 0.25f;
        // The ring round an end's middle is cut into this many sectors, every
        // one of which must hold a vertex.
        private const int Sectors = 8;

        private static readonly Vector3[] _axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        /// <summary>
        /// The open ends of a pipe prefab, in its root's own space (unscaled,
        /// as the catalogue and annotations are), each named a, b, c... in
        /// axis order (+X, -X, +Y, -Y, +Z, -Z). Empty if none is found.
        /// </summary>
        internal static List<ConnectionPoint> Find(GameObject prefab)
        {
            List<ConnectionPoint> ends = new();
            List<Vector3> vertices = Lod0Vertices(prefab.transform);

            if (vertices.Count == 0) {
                return ends;
            }

            Bounds box = new(vertices[0], Vector3.zero);

            foreach (Vector3 vertex in vertices) {
                box.Encapsulate(vertex);
            }

            List<Vector3> onFace = new();

            foreach (Vector3 axis in _axes) {
                // The face's plane: the box's extreme along this axis.
                float plane = Vector3.Dot(axis, box.center) + Mathf.Abs(Vector3.Dot(box.extents, axis));
                onFace.Clear();

                foreach (Vector3 vertex in vertices) {
                    if (Mathf.Abs(Vector3.Dot(axis, vertex) - plane) <= FaceTolerance) {
                        onFace.Add(vertex);
                    }
                }

                if (onFace.Count < MinVertices) {
                    continue;
                }

                Bounds face = new(onFace[0], Vector3.zero);

                foreach (Vector3 vertex in onFace) {
                    face.Encapsulate(vertex);
                }

                if (!IsRing(onFace, face, axis)) {
                    continue;
                }

                // The middle of the flange, on the face's plane.
                Vector3 centre = face.center - axis * Vector3.Dot(axis, face.center) + axis * plane;
                ends.Add(new ConnectionPoint {
                    name = ((char)('a' + ends.Count)).ToString(),
                    position = PieceAnnotation.Round(centre),
                    direction = axis
                });
            }

            return ends;
        }

        /// <summary>
        /// A pipe piece's annotated end by name (a, b, ...), or null.
        /// </summary>
        internal static ConnectionPoint End(PieceAnnotation piece, string endName)
        {
            foreach (ConnectionPoint point in piece.connections) {
                if (point.name == endName) {
                    return point;
                }
            }

            return null;
        }

        /// <summary>
        /// An elbow's bend from its two ends (root space): the point where the
        /// lines of its two legs cross, each leg running from there out
        /// through its end. False unless the ends point at right angles.
        /// </summary>
        internal static bool Bend(ConnectionPoint a, ConnectionPoint b, out Vector3 node)
        {
            node = default;

            if (a == null || b == null || Mathf.Abs(Vector3.Dot(a.direction, b.direction)) > 0.1f) {
                return false;
            }

            // On leg A's line (through a, along its direction), level with b
            // along that direction: the leg B line crosses it there.
            node = a.position + a.direction * Vector3.Dot(b.position - a.position, a.direction);
            return true;
        }

        /// <summary>
        /// Whether the vertices on a face make a round flange: about as wide
        /// in one of the face's directions as the other, with a vertex in
        /// every sector round the middle of their box.
        /// </summary>
        private static bool IsRing(List<Vector3> onFace, Bounds face, Vector3 axis)
        {
            // The face's two directions: the axes other than the normal.
            Vector3 u = Mathf.Abs(axis.x) > 0.5f ? Vector3.up : Vector3.right;
            Vector3 v = Vector3.Cross(axis, u);
            float width = Mathf.Abs(Vector3.Dot(face.size, u));
            float height = Mathf.Abs(Vector3.Dot(face.size, v));
            float larger = Mathf.Max(width, height);

            if (larger <= 0f || Mathf.Abs(width - height) > larger * MaxOutOfRound) {
                return false;
            }

            bool[] filled = new bool[Sectors];
            int count = 0;

            foreach (Vector3 vertex in onFace) {
                Vector3 offset = vertex - face.center;
                float x = Vector3.Dot(offset, u);
                float y = Vector3.Dot(offset, v);

                // Points near the middle say nothing about the ring.
                if (x * x + y * y < larger * larger * 0.04f) {
                    continue;
                }

                int sector = Mathf.Clamp((int)((Mathf.Atan2(y, x) + Mathf.PI) / (2f * Mathf.PI) * Sectors), 0, Sectors - 1);

                if (!filled[sector]) {
                    filled[sector] = true;
                    count++;
                }
            }

            return count == Sectors;
        }

        /// <summary>
        /// Every vertex of the meshes drawn at LOD0, in the root's own space:
        /// a LODGroup's first level where there is one (nested parts each
        /// have their own), otherwise every mesh not named for a lower LOD.
        /// Collision meshes are left out.
        /// </summary>
        private static List<Vector3> Lod0Vertices(Transform root)
        {
            List<Vector3> vertices = new();
            HashSet<Renderer> drawn = new();
            HashSet<Renderer> lower = new();

            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true)) {
                LOD[] lods = group.GetLODs();

                for (int i = 0; i < lods.Length; i++) {
                    foreach (Renderer renderer in lods[i].renderers) {
                        if (renderer != null) {
                            (i == 0 ? drawn : lower).Add(renderer);
                        }
                    }
                }
            }

            Matrix4x4 toRoot = root.worldToLocalMatrix;
            List<Vector3> meshVertices = new();

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true)) {
                Renderer renderer = filter.GetComponent<Renderer>();
                string name = filter.name;

                if (filter.sharedMesh == null || renderer == null || lower.Contains(renderer) || name.Contains("Collision")) {
                    continue;
                }

                if (!drawn.Contains(renderer) && (name.Contains("LOD1") || name.Contains("LOD2") || name.Contains("LOD3"))) {
                    continue;
                }

                Matrix4x4 toRootFromMesh = toRoot * filter.transform.localToWorldMatrix;
                filter.sharedMesh.GetVertices(meshVertices);

                foreach (Vector3 vertex in meshVertices) {
                    vertices.Add(toRootFromMesh.MultiplyPoint3x4(vertex));
                }
            }

            return vertices;
        }
    }
}
