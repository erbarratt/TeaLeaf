using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Core
{
    /// <summary>
    /// A shaft of moonlight through an opening: the beam the moon throws
    /// through the mouth of an alley, a gap in a roof or a window, made
    /// visible by the dust in the air. The component describes only the
    /// opening - a rectangle, <see cref="width"/> across its local X and
    /// <see cref="height"/> along its local Y, the object's position at the
    /// middle of its local-Y foot edge; stood upright for a window or a
    /// mouth, laid level for the strip of sky over an alley - and builds the
    /// beam itself: at load it asks the scene's
    /// <see cref="Moonlight"/> which way the light travels, follows that
    /// direction from each corner of the opening with a physics ray until
    /// it meets the level (the ground, a wall), and makes a prism of six
    /// sheets joining the opening to where the light lands. The
    /// TeaLeaf/MoonShaft shader (the material) shades the sheets as one
    /// soft volume and takes its colour from the main light, so the shaft
    /// always agrees with the rendered moon: direction, colour and
    /// brightness.
    ///
    /// Honest about shadow: with <see cref="fitToMoon"/> the opening is cut
    /// down to the part the moon actually reaches. Along each of its two
    /// local-Y edges, from the foot, a ray towards the moon finds where the
    /// edge comes out of the shadow of whatever stands beside the opening
    /// (a roof, the building across the street), and the sheet starts
    /// there; an edge in shadow all the way narrows to a point at its far
    /// end, and an opening in shadow altogether draws nothing. So a shaft
    /// can be placed - by hand or by the level generator - without working
    /// out where the moon is, and only ever shows light that is there. Put
    /// the foot edge on the side the moon is, so the fit cuts the shadowed
    /// side off.
    ///
    /// Nothing runs per frame: the mesh is built once in Start() (or in the
    /// editor whenever something changes) and is never saved - it exists
    /// only while the component is enabled. The object needs no collider
    /// and casts and receives no shadows. Keep its scale at 1: the sizes
    /// are in metres.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MoonShaft : MonoBehaviour
    {
        /// <summary>The shared material every shaft wears, where the look is tuned.</summary>
        public const string MaterialPath = "Assets/Art/Materials/MoonShaft.mat";

        [Header("Opening")]
        // The opening's width, across the object's local X, in metres.
        [SerializeField, Min(0.1f)] private float width = 3f;

        // The opening's height, up from the object's position along its
        // local Y, in metres.
        [SerializeField, Min(0.1f)] private float height = 6f;

        [Header("Beam")]
        // The furthest the light is followed through the opening before
        // the beam is cut off, in metres, where nothing stops it sooner.
        [SerializeField, Range(1f, 100f)] private float maxLength = 40f;

        // Cut the opening down to the part the moon reaches: each vertical
        // edge starts where it comes out of shadow, and an opening in
        // shadow altogether draws nothing. Off, the whole opening is used
        // as given.
        [SerializeField] private bool fitToMoon = true;

        // The material (TeaLeaf/MoonShaft). In the editor the project's
        // shared one is filled in when this is empty.
        [SerializeField] private Material material;

        // The light must come through the opening, not along it: below this
        // dot between the light's direction and the opening's normal, the
        // beam would be a sliver and nothing is built.
        private const float MinThrough = 0.2f;

        // How far short of a surface the beam stops, and how far rays start
        // off the opening, so neither begins inside a collider.
        private const float Clearance = 0.05f;

        // How many times the search for where an edge comes out of shadow
        // halves its range: 6 gives about a tenth of the height.
        private const int FitSteps = 6;

        // How far towards the moon a ray looks for something in the way.
        private const float MoonReach = 200f;

        private static readonly int _lightColorId = Shader.PropertyToID("_MoonShaftLight");
        private static int _lightMask = -1;
        private Mesh _mesh;

        /// <summary>Whether the last build drew a beam (the moon reaches the opening).</summary>
        public bool IsLit { get; private set; }

        public float Width => width;
        public float Height => height;
        public float MaxLength => maxLength;

        /// <summary>
        /// Sets the opening and the beam's reach, then rebuilds. For code
        /// that places shafts (the level generator): the colliders the
        /// beam ends on must already be in place.
        /// </summary>
        public void Configure(float openingWidth, float openingHeight, float beamMaxLength)
        {
            width = Mathf.Max(0.1f, openingWidth);
            height = Mathf.Max(0.1f, openingHeight);
            maxLength = Mathf.Clamp(beamMaxLength, 1f, 100f);
            Rebuild();
        }

        private void Start()
        {
            // At runtime every collider is in by Start(); the level-start
            // black (ScreenFade) hides the build.
            if (Application.isPlaying) {
                Rebuild();
            }
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // In the editor, build once the scene has finished loading (a
            // collider enabled after this object would be missed by rays
            // cast now).
            if (!Application.isPlaying) {
                EditorApplication.delayCall += DelayedRebuild;
            }
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.delayCall -= DelayedRebuild;
#endif
            DestroyMesh();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Physics may not be used from OnValidate, so the rebuild waits
            // a tick.
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
        /// Builds the beam's mesh from the opening and the moon's direction,
        /// or clears it where the moon reaches none of the opening. Safe to
        /// call again at any time; nothing is saved.
        /// </summary>
        public void Rebuild()
        {
            IsLit = false;
            MeshFilter filter = GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            SetUpRenderer(meshRenderer);

            Moonlight moon = Moonlight.Instance;

#if UNITY_EDITOR
            // Moonlight registers itself only in Play Mode.
            if (moon == null) {
                moon = FindAnyObjectByType<Moonlight>();
            }
#endif

            if (moon == null) {
                filter.sharedMesh = null;
                return;
            }

            // The shader's colour: the moon's Light colour times its
            // intensity, as linear light, set as a global for every shaft
            // (URP's own main light colour is black wherever the moon is a
            // Baked light, so it isn't used). No Light beside the Moonlight
            // gives plain white.
            Color lightColor = Color.white;

            if (moon.TryGetComponent(out Light moonLight)) {
                lightColor = (QualitySettings.activeColorSpace == ColorSpace.Linear ? moonLight.color.linear : moonLight.color) * moonLight.intensity;
            }

            Shader.SetGlobalColor(_lightColorId, lightColor);

            // The light travels away from the moon.
            Vector3 lightDirection = -moon.DirectionToMoon.normalized;
            Vector3 toMoon = -lightDirection;

            // Along the opening, not through it: nothing to see.
            if (Mathf.Abs(Vector3.Dot(lightDirection, transform.forward)) < MinThrough) {
                filter.sharedMesh = null;
                return;
            }

            if (_lightMask < 0) {
                _lightMask = LayerMask.GetMask("Environment", "Interactable");
            }

            // The opening's two vertical edges, in world space, each from
            // its bottom to its top.
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            Vector3 origin = transform.position;
            Vector3 leftFoot = origin - right * (width * 0.5f);
            Vector3 rightFoot = origin + right * (width * 0.5f);

            // Where each edge comes out of the moon's shadow: 0 at its
            // foot (all lit) to 1 at its top (nothing lit).
            float leftStart = fitToMoon ? LitFrom(leftFoot, up, toMoon) : 0f;
            float rightStart = fitToMoon ? LitFrom(rightFoot, up, toMoon) : 0f;

            if (leftStart >= 1f && rightStart >= 1f) {
                filter.sharedMesh = null;
                return;
            }

            // The four corners of the lit part of the opening: bottom-left,
            // bottom-right, top-right, top-left.
            Vector3[] near = {
                leftFoot + up * (height * leftStart),
                rightFoot + up * (height * rightStart),
                rightFoot + up * height,
                leftFoot + up * height
            };

            // Follow the light from each corner until it lands.
            Vector3[] far = new Vector3[4];
            float meanLength = 0f;

            for (int i = 0; i < 4; i++) {
                Vector3 from = near[i] + lightDirection * Clearance;
                float reach = maxLength;

                if (Physics.Raycast(from, lightDirection, out RaycastHit hit, maxLength, _lightMask, QueryTriggerInteraction.Ignore)) {
                    reach = Mathf.Max(0f, hit.distance - Clearance);
                }

                far[i] = from + lightDirection * reach;
                meanLength += reach * 0.25f;
            }

            if (meanLength < Clearance) {
                filter.sharedMesh = null;
                return;
            }

            // The beam's thickness behind each sheet, in metres: the two
            // caps show its length, the sides its width, the top and bottom
            // its lit height.
            float litHeight = height * (1f - 0.5f * (leftStart + rightStart));
            BuildMesh(near, far, meanLength, width, litHeight);
            filter.sharedMesh = _mesh;
            meshRenderer.sharedMaterial = material;
            IsLit = true;
        }

        /// <summary>
        /// How far up an edge of the opening (0 = its foot, 1 = its top) the
        /// moon first reaches: 0 when the foot is lit, 1 when even the top
        /// is in shadow, else found by halving the range between a point in
        /// shadow and one in the light.
        /// </summary>
        private float LitFrom(Vector3 foot, Vector3 up, Vector3 toMoon)
        {
            if (IsMoonlit(foot, toMoon)) {
                return 0f;
            }

            if (!IsMoonlit(foot + up * height, toMoon)) {
                return 1f;
            }

            float shadowed = 0f;
            float lit = 1f;

            for (int step = 0; step < FitSteps; step++) {
                float middle = (shadowed + lit) * 0.5f;

                if (IsMoonlit(foot + up * (height * middle), toMoon)) {
                    lit = middle;
                } else {
                    shadowed = middle;
                }
            }

            return lit;
        }

        /// <summary>
        /// Whether the moon reaches a point: nothing on the light layers
        /// between it and the moon (the ray starts a little way towards the
        /// moon, so a point on a surface doesn't start inside it).
        /// </summary>
        private static bool IsMoonlit(Vector3 point, Vector3 toMoon)
        {
            Vector3 from = point + toMoon * Clearance;
            return !Physics.Raycast(from, toMoon, MoonReach, _lightMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Makes the six sheets: the opening, the far end, and the four
        /// sides between them. Each sheet has its own four vertices (flat
        /// normals), with its own 0..1 coordinates across it (so the shader
        /// can soften its edges), how far along the beam each vertex is, and
        /// the beam's thickness behind the sheet, in the UVs. A side's
        /// second coordinate is held at the middle, since along the beam it
        /// is the length fade that softens it, not an edge. Positions are
        /// the object's local space, since the renderer draws the mesh
        /// through the object's transform.
        /// </summary>
        private void BuildMesh(Vector3[] near, Vector3[] far, float meanLength, float beamWidth, float beamHeight)
        {
            if (_mesh == null) {
                _mesh = new Mesh {
                    name = "Moon Shaft",
                    hideFlags = HideFlags.DontSave
                };
            }

            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var shaft = new Vector2[24];
            var beam = new Vector2[24];
            var triangles = new int[36];
            int vertex = 0;
            int index = 0;

            // A cap's four corners run round its 0..1 square; a side's run
            // 0..1 across it with the second coordinate held at the middle.
            Vector2 c00 = new(0f, 0f);
            Vector2 c10 = new(1f, 0f);
            Vector2 c11 = new(1f, 1f);
            Vector2 c01 = new(0f, 1f);
            Vector2 s0 = new(0f, 0.5f);
            Vector2 s1 = new(1f, 0.5f);

            // The near cap: the opening itself.
            AddSheet(near[0], near[1], near[2], near[3], c00, c10, c11, c01, 0f, 0f, 0f, 0f, meanLength);
            // The far cap, where the light lands.
            AddSheet(far[0], far[1], far[2], far[3], c00, c10, c11, c01, 1f, 1f, 1f, 1f, meanLength);
            // The foot edge's sheet: its two corners, near then far.
            AddSheet(near[0], near[1], far[1], far[0], s0, s1, s1, s0, 0f, 0f, 1f, 1f, beamHeight);
            // The far edge's sheet.
            AddSheet(near[3], near[2], far[2], far[3], s0, s1, s1, s0, 0f, 0f, 1f, 1f, beamHeight);
            // The left end's sheet (foot corner to far corner).
            AddSheet(near[0], near[3], far[3], far[0], s0, s1, s1, s0, 0f, 0f, 1f, 1f, beamWidth);
            // The right end's sheet.
            AddSheet(near[1], near[2], far[2], far[1], s0, s1, s1, s0, 0f, 0f, 1f, 1f, beamWidth);

            _mesh.Clear();
            _mesh.indexFormat = IndexFormat.UInt16;
            _mesh.SetVertices(vertices);
            _mesh.SetNormals(normals);
            _mesh.SetUVs(0, shaft);
            _mesh.SetUVs(1, beam);
            _mesh.SetTriangles(triangles, 0, true);
            _mesh.UploadMeshData(false);
            return;

            // One sheet from four world corners in order round it, each
            // with its coordinates on the sheet, its distance along the
            // beam (0 near, 1 far), and the beam's thickness behind it.
            void AddSheet(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                Vector2 sheetA, Vector2 sheetB, Vector2 sheetC, Vector2 sheetD,
                float alongA, float alongB, float alongC, float alongD, float thickness)
            {
                Vector3 normal = Vector3.Cross(b - a, d - a);
                normal = normal.sqrMagnitude > 1e-8f ? transform.InverseTransformDirection(normal.normalized) : Vector3.forward;
                int first = vertex;
                Add(a, sheetA, alongA);
                Add(b, sheetB, alongB);
                Add(c, sheetC, alongC);
                Add(d, sheetD, alongD);
                triangles[index++] = first;
                triangles[index++] = first + 1;
                triangles[index++] = first + 2;
                triangles[index++] = first;
                triangles[index++] = first + 2;
                triangles[index++] = first + 3;
                return;

                void Add(Vector3 world, Vector2 place, float along)
                {
                    vertices[vertex] = transform.InverseTransformPoint(world);
                    normals[vertex] = normal;
                    shaft[vertex] = place;
                    beam[vertex] = new Vector2(along, thickness);
                    vertex++;
                }
            }
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
