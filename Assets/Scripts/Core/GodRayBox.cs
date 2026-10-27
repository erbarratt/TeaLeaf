using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Core
{
    /// <summary>
    /// A hand-placed shaft of moonlight: an open four-sided box (no top, no
    /// bottom) drawn with the TeaLeaf/GodRayBox shader, the lantern cone's
    /// look for a beam through a window or a gap in a roof. Shaped by its
    /// eight corners, placed one by one in the Scene view (GodRayBoxEditor:
    /// click a corner's dot, drag the move handle): the top four round the
    /// opening the light comes through, the bottom four where it has died
    /// away, so a shaft can lean with the moon and widen or narrow as it
    /// falls.
    ///
    /// Nothing runs per frame: the mesh is built when the component is
    /// enabled (and in the editor whenever a corner moves) and is never
    /// saved - it exists only while the component is enabled. The object
    /// needs no collider and casts and receives no shadows. Corners are in
    /// the object's local space, so moving or turning the object carries
    /// the whole shaft.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GodRayBox : MonoBehaviour
    {
        /// <summary>The shared material every shaft wears, where the look is tuned.</summary>
        public const string MaterialPath = "Assets/Art/Materials/GodRayBox.mat";

        /// <summary>Corners round each end: the box has four sides.</summary>
        public const int Sides = 4;

        // The eight corners in local space: 0-3 round the top (the
        // opening), 4-7 round the bottom, each bottom corner below the top
        // one four places before it, so side i joins top corners i and i+1
        // to the bottom corners under them.
        [Tooltip("The box's eight corners in this object's local space, in metres: 0-3 in order round the top (the opening the light comes through), 4-7 round the bottom (where it has died away), each under the top corner four before it, e.g. bottom corners shifted along the moon's direction make a leaning shaft. Move them with the dots and move handle in the Scene view.")]
        [SerializeField] private Vector3[] corners = DefaultCorners();

        // The material (TeaLeaf/GodRayBox). In the editor the project's
        // shared one is filled in when this is empty.
        [Tooltip("The TeaLeaf/GodRayBox material the shaft is drawn with, e.g. Assets/Art/Materials/GodRayBox.mat (shared by every shaft, where the look is tuned). Left empty, the editor fills in the shared one.")]
        [SerializeField] private Material material;

        private Mesh _mesh;

        /// <summary>How many corners there are (eight once set up).</summary>
        public int CornerCount => corners != null ? corners.Length : 0;

        /// <summary>A corner's position in the object's local space.</summary>
        public Vector3 GetCorner(int index)
        {
            return corners[index];
        }

        /// <summary>
        /// Sets all eight corners from world positions (top four round the
        /// opening, then the bottom four under them) and rebuilds. For code
        /// that places shafts (the level generator).
        /// </summary>
        public void SetCorners(Vector3[] worldCorners)
        {
            corners = new Vector3[worldCorners.Length];

            for (int i = 0; i < worldCorners.Length; i++) {
                corners[i] = transform.InverseTransformPoint(worldCorners[i]);
            }

            Rebuild();
        }

        /// <summary>
        /// A new shaft's corners: a 1m square opening, falling straight down 2m.
        /// </summary>
        private static Vector3[] DefaultCorners()
        {
            return new[] {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(-0.5f, -2f, -0.5f), new Vector3(0.5f, -2f, -0.5f), new Vector3(0.5f, -2f, 0.5f), new Vector3(-0.5f, -2f, 0.5f)
            };
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.delayCall -= DelayedRebuild;
#endif
            DestroyMesh();
        }

#if UNITY_EDITOR
        private void Reset()
        {
            corners = DefaultCorners();
        }

        private void OnValidate()
        {
            // A corner typed in the Inspector or put back by Undo: rebuild a
            // tick later (a renderer's mesh may not be swapped from inside
            // OnValidate).
            EditorApplication.delayCall -= DelayedRebuild;
            EditorApplication.delayCall += DelayedRebuild;
        }

        private void DelayedRebuild()
        {
            if (this != null && isActiveAndEnabled) {
                Rebuild();
            }
        }
#endif

        /// <summary>
        /// Builds the box's mesh from the corners. Safe to call again at any
        /// time; nothing is saved.
        /// </summary>
        public void Rebuild()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            SetUpRenderer(GetComponent<MeshRenderer>());

            if (corners == null || corners.Length != Sides * 2) {
                Debug.LogWarning($"GodRayBox '{name}' needs exactly {Sides * 2} corners; nothing drawn.", this);
                filter.sharedMesh = null;
                return;
            }

            BuildMesh();
            filter.sharedMesh = _mesh;
        }

        /// <summary>
        /// Makes the four sides. Each side has its own four vertices (flat
        /// normals, so the shader sees each side square on or edge on), with
        /// how far round the box (a quarter per side) and down it (0 top,
        /// 1 bottom) each vertex is in the first UVs, how far across its
        /// side (0..1, so the shader can soften the corners) in the second,
        /// and the normals of the sides beyond its two long edges in the
        /// third and fourth.
        /// </summary>
        private void BuildMesh()
        {
            if (_mesh == null) {
                _mesh = new Mesh {
                    name = "God Ray Box",
                    hideFlags = HideFlags.DontSave
                };
            }

            var vertices = new Vector3[Sides * 4];
            var normals = new Vector3[Sides * 4];
            var uvs = new Vector2[Sides * 4];
            var across = new Vector2[Sides * 4];
            var neighbourBefore = new Vector3[Sides * 4];
            var neighbourAfter = new Vector3[Sides * 4];
            var triangles = new int[Sides * 6];

            // Every side's normal first: each side also carries its two
            // neighbours', so the shader can fade its edges by how its
            // neighbours are seen. Each side faces out of the box (away
            // from the middle of its eight corners), whichever way round
            // the corners were placed, since the shader draws only a side's
            // outer face. A side squashed to a line gets any normal; it
            // draws nothing anyway.
            var sideNormals = new Vector3[Sides];
            var flipped = new bool[Sides];
            Vector3 middle = Vector3.zero;

            foreach (Vector3 corner in corners) {
                middle += corner / corners.Length;
            }

            for (int side = 0; side < Sides; side++) {
                int next = (side + 1) % Sides;
                Vector3 normal = Vector3.Cross(corners[next] - corners[side], corners[Sides + side] - corners[side]);
                Vector3 sideMiddle = (corners[side] + corners[next] + corners[Sides + side] + corners[Sides + next]) * 0.25f;
                flipped[side] = Vector3.Dot(normal, sideMiddle - middle) < 0f;
                normal = flipped[side] ? -normal : normal;
                sideNormals[side] = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
            }

            for (int side = 0; side < Sides; side++) {
                int next = (side + 1) % Sides;
                int previous = (side + Sides - 1) % Sides;
                Vector3 topA = corners[side];
                Vector3 topB = corners[next];
                Vector3 bottomB = corners[Sides + next];
                Vector3 bottomA = corners[Sides + side];
                Vector3 normal = sideNormals[side];

                float aroundA = side / (float)Sides;
                float aroundB = (side + 1) / (float)Sides;
                int first = side * 4;

                vertices[first] = topA;
                vertices[first + 1] = topB;
                vertices[first + 2] = bottomB;
                vertices[first + 3] = bottomA;
                uvs[first] = new Vector2(aroundA, 0f);
                uvs[first + 1] = new Vector2(aroundB, 0f);
                uvs[first + 2] = new Vector2(aroundB, 1f);
                uvs[first + 3] = new Vector2(aroundA, 1f);
                across[first] = new Vector2(0f, 0f);
                across[first + 1] = new Vector2(1f, 0f);
                across[first + 2] = new Vector2(1f, 0f);
                across[first + 3] = new Vector2(0f, 0f);

                // The side before shares this side's across-0 edge (top
                // corner i), the side after its across-1 edge.
                for (int corner = 0; corner < 4; corner++) {
                    normals[first + corner] = normal;
                    neighbourBefore[first + corner] = sideNormals[previous];
                    neighbourAfter[first + corner] = sideNormals[next];
                }

                // Wound so the front face (the one drawn) is the one the
                // normal points out of: corners in this order face along
                // the unflipped normal, so a flipped side runs them backwards.
                int index = side * 6;
                int second = flipped[side] ? first + 2 : first + 1;
                int third = flipped[side] ? first + 1 : first + 2;
                int fourth = flipped[side] ? first + 3 : first + 2;
                int fifth = flipped[side] ? first + 2 : first + 3;
                triangles[index] = first;
                triangles[index + 1] = second;
                triangles[index + 2] = third;
                triangles[index + 3] = first;
                triangles[index + 4] = fourth;
                triangles[index + 5] = fifth;
            }

            _mesh.Clear();
            _mesh.indexFormat = IndexFormat.UInt16;
            _mesh.SetVertices(vertices);
            _mesh.SetNormals(normals);
            _mesh.SetUVs(0, uvs);
            _mesh.SetUVs(1, across);
            _mesh.SetUVs(2, neighbourBefore);
            _mesh.SetUVs(3, neighbourAfter);
            _mesh.SetTriangles(triangles, 0, true);
        }

        /// <summary>
        /// A light beam casts and receives no shadows, is lit by nothing and
        /// moves nowhere.
        /// </summary>
        private void SetUpRenderer(MeshRenderer meshRenderer)
        {
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

#if UNITY_EDITOR
            if (material == null) {
                material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            }
#endif

            if (meshRenderer.sharedMaterial != material) {
                meshRenderer.sharedMaterial = material;
            }
        }

        private void DestroyMesh()
        {
            if (TryGetComponent(out MeshFilter filter)) {
                filter.sharedMesh = null;
            }

            if (_mesh == null) {
                return;
            }

            if (Application.isPlaying) {
                Destroy(_mesh);
            } else {
                DestroyImmediate(_mesh);
            }

            _mesh = null;
        }
    }
}
