using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Interaction
{
    /// <summary>
    /// Builds the simple coloured shapes the lockpicking pieces are made of
    /// (the big lock's body and marks, the picks): boxes and a flat-ended
    /// cylinder, collected into one mesh. Each point carries its own
    /// colour, which the TeaLeaf/LockFade shader draws, so a whole piece is
    /// one mesh and one material however many colours it has.
    ///
    /// Used once, at load: make one, add shapes, call ToMesh(). Nothing
    /// here runs during play.
    /// </summary>
    public class LockMeshBuilder
    {
        private const string ShaderName = "TeaLeaf/LockFade";

        private static readonly int _srcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int _dstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int _minLightId = Shader.PropertyToID("_MinLight");

        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Color32> _colors = new();
        private readonly List<int> _triangles = new();

        /// <summary>
        /// A material for these meshes (the TeaLeaf/LockFade shader).
        /// seeThrough false = an ordinary solid surface; true = one that
        /// can be faded with its _Alpha, drawn with the other see-through
        /// things. minLight is the least light it's ever shown in. The
        /// caller owns the material and must destroy it.
        /// </summary>
        public static Material CreateMaterial(bool seeThrough, float minLight)
        {
            Shader shader = Shader.Find(ShaderName);

            if (shader == null) {
                Debug.LogError($"LockMeshBuilder: shader '{ShaderName}' not found - the lockpicking pieces will be pink.");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }

            Material material = new(shader) { name = seeThrough ? "Lock (Fading)" : "Lock (Solid)" };
            material.SetFloat(_minLightId, minLight);

            if (seeThrough) {
                // Mixed with what's behind it by its alpha, and drawn after
                // everything solid.
                material.SetFloat(_srcBlendId, (float)BlendMode.SrcAlpha);
                material.SetFloat(_dstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                material.renderQueue = (int)RenderQueue.Transparent;
            } else {
                material.SetFloat(_srcBlendId, (float)BlendMode.One);
                material.SetFloat(_dstBlendId, (float)BlendMode.Zero);
                material.renderQueue = (int)RenderQueue.Geometry;
            }

            return material;
        }

        /// <summary>
        /// Adds a box: size metres along its own axes, its middle at
        /// centre, turned by rotation.
        /// </summary>
        public void Box(Vector3 centre, Vector3 size, Quaternion rotation, Color32 color)
        {
            Vector3 half = size * 0.5f;
            Vector3 x = rotation * Vector3.right;
            Vector3 y = rotation * Vector3.up;
            Vector3 z = rotation * Vector3.forward;

            // Each face: its middle, the way it faces, one direction
            // across it, and its half size along the other direction and
            // along that one.
            Face(centre + x * half.x, x, y, half.z, half.y, color);
            Face(centre - x * half.x, -x, y, half.z, half.y, color);
            Face(centre + y * half.y, y, z, half.x, half.z, color);
            Face(centre - y * half.y, -y, z, half.x, half.z, color);
            Face(centre + z * half.z, z, y, half.x, half.y, color);
            Face(centre - z * half.z, -z, y, half.x, half.y, color);
        }

        /// <summary>
        /// Adds one flat four-cornered face, seen from the side normal
        /// points to. v is a direction across the face; the other
        /// direction across it (u) is worked out from the two. The order
        /// the corners are listed in decides which side is drawn: Unity
        /// draws a triangle from the side its corners run clockwise on.
        /// </summary>
        private void Face(Vector3 centre, Vector3 normal, Vector3 v, float halfU, float halfV, Color32 color)
        {
            Vector3 u = Vector3.Cross(normal, v) * halfU;
            v *= halfV;

            int first = _vertices.Count;

            _vertices.Add(centre - u - v);
            _vertices.Add(centre - u + v);
            _vertices.Add(centre + u + v);
            _vertices.Add(centre + u - v);

            for (int i = 0; i < 4; i++) {
                _normals.Add(normal);
                _colors.Add(color);
            }

            AddQuad(first, first + 1, first + 2, first + 3);
        }

        /// <summary>
        /// Adds a cylinder lying along Z, its middle at the origin: a
        /// round face at each end (the one facing -Z in faceColor, the
        /// other in sideColor) and the wall between them. segments is how
        /// many flat pieces the round is made of.
        /// </summary>
        public void Cylinder(float radius, float depth, int segments, Color32 faceColor, Color32 sideColor)
        {
            float front = -depth * 0.5f;
            float back = depth * 0.5f;

            // The two end faces: a middle point and a ring each.
            int frontCentre = _vertices.Count;
            AddPoint(new Vector3(0f, 0f, front), Vector3.back, faceColor);

            for (int i = 0; i < segments; i++) {
                AddPoint(RingPoint(i, segments, radius, front), Vector3.back, faceColor);
            }

            int backCentre = _vertices.Count;
            AddPoint(new Vector3(0f, 0f, back), Vector3.forward, sideColor);

            for (int i = 0; i < segments; i++) {
                AddPoint(RingPoint(i, segments, radius, back), Vector3.forward, sideColor);
            }

            // The wall: both rings again, this time facing outwards (a
            // point can only face one way, so the ends' points can't be
            // shared).
            int wall = _vertices.Count;

            for (int i = 0; i < segments; i++) {
                Vector3 point = RingPoint(i, segments, 1f, 0f);
                AddPoint(RingPoint(i, segments, radius, front), point, sideColor);
                AddPoint(RingPoint(i, segments, radius, back), point, sideColor);
            }

            for (int i = 0; i < segments; i++) {
                int next = (i + 1) % segments;

                // The ring runs anticlockwise seen from the front, so the
                // front face's triangles take it backwards (clockwise) and
                // the back face's forwards.
                AddTriangle(frontCentre, frontCentre + 1 + next, frontCentre + 1 + i);
                AddTriangle(backCentre, backCentre + 1 + i, backCentre + 1 + next);

                AddQuad(wall + next * 2, wall + next * 2 + 1, wall + i * 2 + 1, wall + i * 2);
            }
        }

        /// <summary>
        /// Point i of a ring of count points round the Z axis, at z.
        /// </summary>
        private static Vector3 RingPoint(int i, int count, float radius, float z)
        {
            float angle = Mathf.PI * 2f * i / count;
            return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z);
        }

        private void AddPoint(Vector3 position, Vector3 normal, Color32 color)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _colors.Add(color);
        }

        private void AddTriangle(int a, int b, int c)
        {
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }

        /// <summary>
        /// Two triangles making the four-cornered piece a-b-c-d (corners
        /// in order round it).
        /// </summary>
        private void AddQuad(int a, int b, int c, int d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        /// <summary>
        /// Makes the mesh from everything added so far. The caller owns it
        /// and must destroy it.
        /// </summary>
        public Mesh ToMesh(string name)
        {
            Mesh mesh = new() { name = name };
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
