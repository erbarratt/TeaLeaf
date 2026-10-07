using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Core
{
    /// <summary>
    /// The level's night sky. There is no skybox: the camera clears to a
    /// flat colour (black) and this component draws the stars and the moon
    /// over it as flat discs, with the TeaLeaf/Sky shader - which keeps
    /// them locked to the camera, pin-sharp at any resolution, and makes
    /// the stars twinkle (see the shader for how). When the level loads it
    /// builds one mesh holding every star, placed at random above the
    /// horizon from the settings and seed, and one for the moon, which is
    /// aimed where the Directional Light shines from - so the moon in the
    /// sky is where the moonlight and its shadows come from. Building takes
    /// about a millisecond and happens in OnEnable(), before the first
    /// frame, behind ScreenFade's level-start black; nothing runs per frame
    /// afterwards. The sky is a picture only: it gives off no light, which
    /// is left to the Directional Light and the ambient setting. Also runs
    /// in Edit Mode, so the sky shows in the Scene view and is rebuilt as
    /// the settings are changed.
    /// </summary>
    [ExecuteAlways]
    public class ProceduralSky : MonoBehaviour
    {
        [Header("Sky")]

        // The camera whose background becomes the sky colour. Left empty,
        // the main camera is used.
        [SerializeField] private Camera viewCamera;

        // The empty sky behind everything.
        [SerializeField] private Color skyColor = Color.black;

        // Which sky: every seed scatters the stars differently.
        [SerializeField, Min(0)] private int seed = 1;

        [Header("Stars")]

        // All of them are above the horizon; none are made below it.
        [SerializeField, Range(0, 20000)] private int starCount = 2500;

        // How wide a star is, in degrees, from the faintest stars (x) to
        // the brightest (y). One Quest 3 pixel is about 0.04 degrees.
        [SerializeField] private Vector2 starSize = new Vector2(0.05f, 0.16f);

        // The smallest a star is ever drawn, in screen pixels across. A
        // star set smaller than this is drawn this size and dimmer instead,
        // because a star under a pixel wide flickers as the head moves.
        [SerializeField, Range(0.5f, 4f)] private float minStarPixels = 1.5f;

        // Brightness of the faintest and the brightest stars.
        [SerializeField, Range(0f, 1f)] private float starMinBrightness = 0.15f;
        [SerializeField, Range(0f, 1f)] private float starMaxBrightness = 1f;

        // How rare bright stars are. 1 = every brightness is equally
        // likely; higher = mostly faint stars with a few bright ones, as
        // in a real sky.
        [SerializeField, Range(1f, 8f)] private float starFaintBias = 3f;

        // The colours stars come in: each star picks a random point along
        // this.
        [SerializeField] private Gradient starColors = DefaultStarColors();

        // How strongly stars are coloured: 0 = all white, 1 = the full
        // colours above.
        [SerializeField, Range(0f, 1f)] private float starColorAmount = 0.6f;

        // Stars fade out over this many degrees above the horizon, as real
        // ones do through the thicker air, rather than stopping at a line.
        // 0 = full brightness right down to the horizon.
        [SerializeField, Range(0f, 30f)] private float horizonFade = 6f;

        [Header("Twinkle")]

        // How much a star's brightness wavers: 0.2 = up to 20% either way,
        // 0 = steady.
        [SerializeField, Range(0f, 1f)] private float twinkleAmount = 0.2f;

        // How fast it wavers.
        [SerializeField, Range(0f, 20f)] private float twinkleSpeed = 5f;

        // The share of the twinkle that stars straight overhead keep; stars
        // at the horizon get all of it. 1 = the same everywhere.
        [SerializeField, Range(0f, 1f)] private float twinkleOverhead = 0.3f;

        [Header("Moon")]
        [SerializeField] private bool showMoon = true;

        // The Directional Light: the moon is drawn where it shines from.
        [SerializeField] private Transform moonLight;

        // How wide the moon is, in degrees. The real one is half a degree,
        // which looks tiny in a headset; games draw it several times that.
        [SerializeField, Range(0.5f, 30f)] private float moonSize = 4f;

        [SerializeField] private Color moonColor = new Color(0.85f, 0.87f, 0.9f);

        // Paint the moon's face (seas, craters, rays). Off, the moon is a
        // plain disc of the colour above.
        [SerializeField] private bool showMoonSurface = true;

        [SerializeField] private MoonSurfaceSettings moonSurface = new MoonSurfaceSettings();

        private const string ShaderName = "TeaLeaf/Sky";
        private const string SurfaceKeyword = "_SURFACE_TEXTURE";

        // The meshes are never culled: the shader draws them around the
        // camera wherever it is, so their real bounds mean nothing.
        private const float BoundsSize = 100000f;

        private static readonly int _twinkleAmountId = Shader.PropertyToID("_TwinkleAmount");
        private static readonly int _twinkleSpeedId = Shader.PropertyToID("_TwinkleSpeed");
        private static readonly int _twinkleOverheadId = Shader.PropertyToID("_TwinkleOverhead");
        private static readonly int _minPixelRadiusId = Shader.PropertyToID("_MinPixelRadius");
        private static readonly int _surfaceTexId = Shader.PropertyToID("_SurfaceTex");

        // The four corners of a quad, and the two triangles joining them.
        private static readonly Vector2[] _corners = {
            new Vector2(-1f, -1f),
            new Vector2(1f, -1f),
            new Vector2(1f, 1f),
            new Vector2(-1f, 1f)
        };

        private static readonly int[] _cornerOrder = { 0, 1, 2, 0, 2, 3 };

        // Made in code in OnEnable(), so this component must free them.
        private Material _material;

        // The moon has a material of its own, the same shader with its
        // face switched on, and the picture of that face.
        private Material _moonMaterial;
        private Texture2D _moonTexture;
        private MeshFilter _stars;
        private MeshFilter _moon;

        // What the scene and camera had before this component took over,
        // put back when it is disabled.
        private Material _previousSkybox;
        private CameraClearFlags _previousClearFlags;
        private Color _previousBackground;
        private Camera _claimedCamera;

#if UNITY_EDITOR
        // Set when a field is changed in the Inspector - see OnValidate().
        private bool _isDirty;
#endif

        /// <summary>
        /// Auto-fills the references when the component is added: the main
        /// camera, and this object's own light if it has one, otherwise the
        /// scene's Moonlight.
        /// </summary>
        private void Reset()
        {
            viewCamera = Camera.main;

            if (TryGetComponent(out Light ownLight)) {
                moonLight = ownLight.transform;
                return;
            }

            Moonlight moonlight = FindAnyObjectByType<Moonlight>();

            if (moonlight != null) {
                moonLight = moonlight.transform;
            }
        }

        private void OnEnable()
        {
            Shader shader = Shader.Find(ShaderName);

            if (shader == null) {
                Debug.LogError($"ProceduralSky: shader '{ShaderName}' not found - the sky is left as it was.");
                return;
            }

            // DontSave on everything made here: it exists only while the
            // component is enabled, and must not be written into the scene
            // file when the scene is saved in Edit Mode.
            _material = new Material(shader) {
                name = "Procedural Sky",
                hideFlags = HideFlags.DontSave
            };

            // The moon is drawn after the stars (a higher sorting order),
            // so it covers the ones behind it.
            _moonMaterial = new Material(shader) {
                name = "Procedural Sky Moon",
                hideFlags = HideFlags.DontSave
            };

            _stars = CreatePart("Sky Stars", _material, 0);
            _moon = CreatePart("Sky Moon", _moonMaterial, 1);

            // No skybox: the camera clears to the sky colour instead.
            _previousSkybox = RenderSettings.skybox;
            RenderSettings.skybox = null;

            // The main camera if none was assigned.
            _claimedCamera = viewCamera != null ? viewCamera : Camera.main;

            if (_claimedCamera != null) {
                _previousClearFlags = _claimedCamera.clearFlags;
                _previousBackground = _claimedCamera.backgroundColor;
                _claimedCamera.clearFlags = CameraClearFlags.SolidColor;
            }

            Rebuild();
        }

        private void OnDisable()
        {
            if (_material == null) {
                return;
            }

            RenderSettings.skybox = _previousSkybox;
            _previousSkybox = null;

            if (_claimedCamera != null) {
                _claimedCamera.clearFlags = _previousClearFlags;
                _claimedCamera.backgroundColor = _previousBackground;
                _claimedCamera = null;
            }

            ReleasePart(_stars);
            ReleasePart(_moon);
            Release(_material);
            Release(_moonMaterial);
            Release(_moonTexture);
            _moonMaterial = null;
            _moonTexture = null;
            _stars = null;
            _moon = null;
            _material = null;
        }

        /// <summary>
        /// Makes the sky again from the current settings: the star and moon
        /// meshes, the twinkle, the moon's face, the background colour and
        /// the moon's direction. Quick (a few milliseconds, most of it the
        /// moon's face), but it allocates, so in the game it's only for
        /// level load.
        /// </summary>
        public void Rebuild()
        {
            if (_material == null) {
                return;
            }

#if UNITY_EDITOR
            _isDirty = false;
#endif

            ReplaceMesh(_stars, BuildStarMesh());
            ReplaceMesh(_moon, BuildMoonMesh());

            _material.SetFloat(_twinkleAmountId, twinkleAmount);
            _material.SetFloat(_twinkleSpeedId, twinkleSpeed);
            _material.SetFloat(_twinkleOverheadId, twinkleOverhead);

            // The setting is a width, the shader wants a radius.
            _material.SetFloat(_minPixelRadiusId, minStarPixels * 0.5f);

            // The moon's face: painted afresh, or switched off in the
            // shader (a keyword picks which version of it is used).
            Release(_moonTexture);
            _moonTexture = null;

            if (showMoonSurface) {
                _moonTexture = MoonSurfaceBuilder.Build(moonSurface);
                _moonTexture.hideFlags = HideFlags.DontSave;
                _moonMaterial.SetTexture(_surfaceTexId, _moonTexture);
                _moonMaterial.EnableKeyword(SurfaceKeyword);
            } else {
                _moonMaterial.DisableKeyword(SurfaceKeyword);
            }

            if (_claimedCamera != null) {
                _claimedCamera.backgroundColor = skyColor;
            }

            AimMoon();
        }

        /// <summary>
        /// Turns the moon to where the Directional Light shines from. Cheap
        /// (one rotation). Call it again if anything ever turns the light
        /// while playing; in the editor that is followed automatically.
        /// </summary>
        public void AimMoon()
        {
            if (_moon == null) {
                return;
            }

            bool isVisible = showMoon && moonLight != null;
            _moon.gameObject.SetActive(isVisible);

            if (isVisible) {
                // The moon's mesh has it straight ahead (+Z), and a
                // directional light shines along its forward axis, so the
                // moon faces the opposite way (as Moonlight.DirectionToMoon).
                _moon.transform.rotation = Quaternion.LookRotation(-moonLight.forward);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor only: called when a field is changed in the Inspector
        /// (or by Undo). Objects can't be made or destroyed from here, so
        /// it only leaves a note for Update() to rebuild.
        /// </summary>
        private void OnValidate()
        {
            _isDirty = true;
        }

        /// <summary>
        /// Editor only (it isn't compiled into a build, where nothing
        /// changes the settings or turns the light): rebuilds after an
        /// Inspector change and keeps the moon on the Directional Light
        /// while it's rotated, in Edit Mode and Play Mode.
        /// </summary>
        private void Update()
        {
            if (_isDirty) {
                Rebuild();
            } else if (_moon != null && showMoon && moonLight != null && _moon.transform.forward != -moonLight.forward) {
                AimMoon();
            }
        }
#endif

        /// <summary>
        /// Builds the mesh holding every star: four vertices and two
        /// triangles each. All four vertices of a star carry the same
        /// direction, colour and size; only "which corner" differs, and the
        /// shader moves each one out to its corner.
        /// </summary>
        private Mesh BuildStarMesh()
        {
            int vertexCount = starCount * 4;
            var directions = new Vector3[vertexCount];
            var colors = new Color[vertexCount];
            var corners = new Vector4[vertexCount];
            var twinkles = new Vector2[vertexCount];
            var triangles = new int[starCount * 6];

            Random random = Random.CreateFromIndex((uint)seed);

            for (int i = 0; i < starCount; i++) {
                // A random direction, equally likely anywhere on the upper
                // half of the sphere: a random height from the horizon (0)
                // to straight up (1), then a random angle round the
                // vertical axis. (Picking two random angles instead would
                // bunch the stars up overhead.)
                float height = random.NextFloat(0f, 1f);
                float angle = random.NextFloat(0f, 2f * Mathf.PI);
                float ring = Mathf.Sqrt(1f - height * height);
                var direction = new Vector3(ring * Mathf.Cos(angle), height, ring * Mathf.Sin(angle));

                // One number decides both brightness and size, so the
                // bright stars are also the big ones. Raising a 0-1 random
                // number to a power pushes most results towards 0: faint.
                float magnitude = Mathf.Pow(random.NextFloat(), starFaintBias);
                float brightness = Mathf.Lerp(starMinBrightness, starMaxBrightness, magnitude);
                float width = Mathf.Lerp(starSize.x, starSize.y, magnitude);

                // Dimmer towards the horizon.
                if (horizonFade > 0f) {
                    float elevation = Mathf.Asin(height) * Mathf.Rad2Deg;
                    brightness *= Mathf.Clamp01(elevation / horizonFade);
                }

                Color tint = Color.Lerp(Color.white, starColors.Evaluate(random.NextFloat()), starColorAmount);

                // Alpha 0 = added to what's behind it (see the shader).
                Color color = ToLinear(tint, brightness, 0f);

                // Each star starts at its own point in its twinkle and
                // runs at its own speed, so no two keep time.
                float phase = random.NextFloat(0f, 100f);
                float speed = random.NextFloat(0.6f, 1.4f);

                WriteQuad(i, direction, color, RadiusFromWidth(width), phase, new Vector2(speed, 1f),
                    directions, colors, corners, twinkles, triangles);
            }

            return CreateMesh("Sky Stars", directions, colors, corners, twinkles, triangles);
        }

        /// <summary>
        /// Builds the moon's mesh: one quad straight ahead (+Z), which
        /// AimMoon() turns towards the light. It doesn't twinkle, and
        /// covers what's behind it.
        /// </summary>
        private Mesh BuildMoonMesh()
        {
            var directions = new Vector3[4];
            var colors = new Color[4];
            var corners = new Vector4[4];
            var twinkles = new Vector2[4];
            var triangles = new int[6];

            WriteQuad(0, Vector3.forward, ToLinear(moonColor, 1f, 1f), RadiusFromWidth(moonSize), 0f, Vector2.zero,
                directions, colors, corners, twinkles, triangles);

            return CreateMesh("Sky Moon", directions, colors, corners, twinkles, triangles);
        }

        /// <summary>
        /// Fills in one disc's four vertices and two triangles in the mesh
        /// arrays. The layout matches the shader's Attributes.
        /// </summary>
        private static void WriteQuad(
            int index, Vector3 direction, Color color, float radius, float phase, Vector2 twinkle,
            Vector3[] directions, Color[] colors, Vector4[] corners, Vector2[] twinkles, int[] triangles)
        {
            int firstVertex = index * 4;

            for (int c = 0; c < 4; c++) {
                directions[firstVertex + c] = direction;
                colors[firstVertex + c] = color;
                corners[firstVertex + c] = new Vector4(_corners[c].x, _corners[c].y, radius, phase);
                twinkles[firstVertex + c] = twinkle;
            }

            for (int t = 0; t < 6; t++) {
                triangles[index * 6 + t] = firstVertex + _cornerOrder[t];
            }
        }

        /// <summary>
        /// Turns the filled arrays into a mesh.
        /// </summary>
        private static Mesh CreateMesh(
            string meshName, Vector3[] directions, Color[] colors, Vector4[] corners, Vector2[] twinkles, int[] triangles)
        {
            var mesh = new Mesh {
                name = meshName,
                hideFlags = HideFlags.DontSave,

                // 16-bit triangle indices only reach 65,535 vertices
                // (16,383 stars); more needs 32-bit.
                indexFormat = directions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            mesh.SetVertices(directions);
            mesh.SetColors(colors);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, twinkles);
            mesh.SetTriangles(triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);

            // Sent to the graphics card and dropped from main memory.
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// A disc's radius as the shader wants it - its size at 1m away -
        /// from its width in degrees.
        /// </summary>
        private static float RadiusFromWidth(float widthDegrees)
        {
            return Mathf.Tan(widthDegrees * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>
        /// A colour as picked in the Inspector, dimmed by brightness, as
        /// linear light - what a mesh's vertex colours must hold, since
        /// they aren't converted the way a material's colours are. The
        /// brightness is applied first, so 0.5 looks half as bright.
        /// </summary>
        private static Color ToLinear(Color color, float brightness, float alpha)
        {
            Color linear = (color * brightness).linear;
            linear.a = alpha;
            return linear;
        }

        /// <summary>
        /// Makes one of the sky's two objects, at the scene root with no
        /// rotation (as a child it would turn with whatever this component
        /// is on - the Directional Light, usually).
        /// </summary>
        private MeshFilter CreatePart(string partName, Material material, int sortingOrder)
        {
            var part = new GameObject(partName) {
                hideFlags = HideFlags.DontSave
            };

            MeshFilter meshFilter = part.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = sortingOrder;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return meshFilter;
        }

        /// <summary>
        /// Gives a part its new mesh and frees the one it had.
        /// </summary>
        private static void ReplaceMesh(MeshFilter part, Mesh mesh)
        {
            Release(part.sharedMesh);
            part.sharedMesh = mesh;
        }

        /// <summary>
        /// Destroys one of the sky's objects and its mesh.
        /// </summary>
        private static void ReleasePart(MeshFilter part)
        {
            if (part == null) {
                return;
            }

            Release(part.sharedMesh);
            Release(part.gameObject);
        }

        /// <summary>
        /// Destroys an object this component made. Destroy() isn't allowed
        /// in Edit Mode, where DestroyImmediate() is used instead.
        /// </summary>
        private static void Release(Object made)
        {
            if (made == null) {
                return;
            }

            if (Application.isPlaying) {
                Destroy(made);
            } else {
                DestroyImmediate(made);
            }
        }

        /// <summary>
        /// The star colours a new sky starts with: blue-white through white
        /// and yellow to orange, roughly the range real stars come in.
        /// </summary>
        private static Gradient DefaultStarColors()
        {
            var gradient = new Gradient();

            gradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(0.65f, 0.75f, 1f), 0f),
                    new GradientColorKey(Color.white, 0.4f),
                    new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0.7f),
                    new GradientColorKey(new Color(1f, 0.7f, 0.5f), 1f)
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });

            return gradient;
        }
    }
}
