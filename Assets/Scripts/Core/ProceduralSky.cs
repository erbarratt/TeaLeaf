using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Core
{
    /// <summary>
    /// The level's night sky. There is no skybox: the camera clears to a
    /// flat colour (black) and this component draws the stars and the moon
    /// over it as flat discs, with the TeaLeaf/Sky shader - which keeps
    /// them locked to the camera and pin-sharp at any resolution (see the
    /// shader for how). The stars are steady. When the level loads it
    /// builds one mesh holding every star, placed at random above the
    /// horizon from the settings and seed, and one for the moon, which is
    /// aimed where the Directional Light shines from - so the moon in the
    /// sky is where the moonlight and its shadows come from. Over both go
    /// the clouds, a cubemap drifting with the wind (TeaLeaf/SkyClouds,
    /// as Thief VR's cloud layer). Building takes
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

        [Header("Moon")]
        [SerializeField] private bool showMoon = true;

        // The Directional Light: the moon is drawn where it shines from.
        [SerializeField] private Transform moonLight;

        // How wide the moon is, in degrees. The real one is half a degree,
        // which looks tiny in a headset; games draw it several times that.
        [SerializeField, Range(0.5f, 30f)] private float moonSize = 4f;

        [SerializeField] private Color moonColor = new Color(0.85f, 0.87f, 0.9f);

        // The picture of the moon's face, multiplied by the colour above.
        // Left empty, the moon is a plain disc of that colour.
        [SerializeField] private Texture2D moonTexture;

        // The square of the picture the moon's disc fills, in 0-1 picture
        // coordinates (x, y from the bottom left; width, height), so a
        // picture with a margin round the moon needs no cropping.
        [SerializeField] private Rect moonTextureArea = new Rect(0f, 0f, 1f, 1f);

        [Header("Clouds")]
        [SerializeField] private bool showClouds = true;

        // The clouds all round: bright where thick, black where the sky is
        // clear. Left empty, there are no clouds.
        [SerializeField] private Cubemap cloudCubemap;

        // What the cubemap's colour is multiplied by. The numbers are used
        // as they are, not converted to linear like a material colour.
        [SerializeField] private Color cloudTint = new Color(0.118f, 0.102f, 0.18f);

        // Brightness on top of the tint.
        [SerializeField, Range(0f, 8f)] private float cloudExposure = 1.1f;

        // Turns the clouds round the vertical axis, in degrees.
        [SerializeField, Range(0f, 360f)] private float cloudRotation = 214f;

        // How much thick cloud hides the stars and moon behind it: 0 = the
        // clouds only add light, 1 = the thickest cloud hides them.
        [SerializeField, Range(0f, 1f)] private float cloudOpacity = 1f;

        // Which way the clouds flow across the sky (world x, z). Its length
        // scales the distance below too.
        [SerializeField] private Vector2 wind = new Vector2(0.78f, 2.27f);

        // How far the clouds flow in one cycle before the read jumps back
        // (see the TeaLeaf/SkyClouds shader); negative flows the other way.
        // Small: too far and the jump shows as a pulse.
        [SerializeField, Range(-0.2f, 0.2f)] private float flowDistance = -0.02f;

        // Flow cycles per second.
        [SerializeField, Range(0f, 1f)] private float flowSpeed = 0.05f;

        private const string ShaderName = "TeaLeaf/Sky";
        private const string CloudShaderName = "TeaLeaf/SkyClouds";
        private const string SurfaceKeyword = "_SURFACE_TEXTURE";

        // The meshes are never culled: the shader draws them around the
        // camera wherever it is, so their real bounds mean nothing.
        private const float BoundsSize = 100000f;

        private static readonly int _minPixelRadiusId = Shader.PropertyToID("_MinPixelRadius");
        private static readonly int _surfaceTexId = Shader.PropertyToID("_SurfaceTex");
        private static readonly int _cloudTexId = Shader.PropertyToID("_CloudTex");
        private static readonly int _tintId = Shader.PropertyToID("_Tint");
        private static readonly int _exposureId = Shader.PropertyToID("_Exposure");
        private static readonly int _rotationId = Shader.PropertyToID("_Rotation");
        private static readonly int _opacityId = Shader.PropertyToID("_Opacity");
        private static readonly int _windId = Shader.PropertyToID("_Wind");

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
        // face switched on.
        private Material _moonMaterial;

        // The clouds have a shader of their own.
        private Material _cloudMaterial;
        private MeshFilter _stars;
        private MeshFilter _moon;
        private MeshFilter _clouds;

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
            Shader cloudShader = Shader.Find(CloudShaderName);

            if (shader == null || cloudShader == null) {
                Debug.LogError($"ProceduralSky: shader '{ShaderName}' or '{CloudShaderName}' not found - the sky is left as it was.");
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

            _cloudMaterial = new Material(cloudShader) {
                name = "Procedural Sky Clouds",
                hideFlags = HideFlags.DontSave
            };

            // The clouds last of all, so they pass in front of the stars
            // and the moon.
            _stars = CreatePart("Sky Stars", _material, 0);
            _moon = CreatePart("Sky Moon", _moonMaterial, 1);
            _clouds = CreatePart("Sky Clouds", _cloudMaterial, 2);

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
            ReleasePart(_clouds);
            Release(_material);
            Release(_moonMaterial);
            Release(_cloudMaterial);
            _moonMaterial = null;
            _cloudMaterial = null;
            _stars = null;
            _moon = null;
            _clouds = null;
            _material = null;
        }

        /// <summary>
        /// Makes the sky again from the current settings: the star, moon
        /// and cloud meshes, the moon's face, the clouds' look, the
        /// background colour and the moon's direction. Quick (about a
        /// millisecond), but it allocates, so in the game it's only for
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
            ReplaceMesh(_clouds, BuildCloudMesh());

            // The clouds: the cubemap and its look, all read by the shader
            // (the flow runs there, off the shader's own clock).
            bool hasClouds = showClouds && cloudCubemap != null;
            _clouds.gameObject.SetActive(hasClouds);

            if (hasClouds) {
                _cloudMaterial.SetTexture(_cloudTexId, cloudCubemap);
                _cloudMaterial.SetVector(_tintId, cloudTint);
                _cloudMaterial.SetFloat(_exposureId, cloudExposure);
                _cloudMaterial.SetFloat(_rotationId, cloudRotation);
                _cloudMaterial.SetFloat(_opacityId, cloudOpacity);
                _cloudMaterial.SetVector(_windId, new Vector4(wind.x, wind.y, flowDistance, flowSpeed));
            }

            // The setting is a width, the shader wants a radius.
            _material.SetFloat(_minPixelRadiusId, minStarPixels * 0.5f);

            // The moon's face, or switched off in the shader (a keyword
            // picks which version of it is used). The texture's scale and
            // offset fit the picture's moon to the disc.
            if (moonTexture != null) {
                _moonMaterial.SetTexture(_surfaceTexId, moonTexture);
                _moonMaterial.SetTextureScale(_surfaceTexId, moonTextureArea.size);
                _moonMaterial.SetTextureOffset(_surfaceTexId, moonTextureArea.position);
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
            var corners = new Vector3[vertexCount];
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

                WriteQuad(i, direction, color, RadiusFromWidth(width), directions, colors, corners, triangles);
            }

            return CreateMesh("Sky Stars", directions, colors, corners, triangles);
        }

        /// <summary>
        /// Builds the moon's mesh: one quad straight ahead (+Z), which
        /// AimMoon() turns towards the light. It covers what's behind it.
        /// </summary>
        private Mesh BuildMoonMesh()
        {
            var directions = new Vector3[4];
            var colors = new Color[4];
            var corners = new Vector3[4];
            var triangles = new int[6];

            WriteQuad(0, Vector3.forward, ToLinear(moonColor, 1f, 1f), RadiusFromWidth(moonSize), directions, colors, corners, triangles);

            return CreateMesh("Sky Moon", directions, colors, corners, triangles);
        }

        /// <summary>
        /// Builds the clouds' mesh: a cube round the eye, its eight corners
        /// directions, which the shader places round the camera and reads
        /// the cubemap along. Drawn from inside (the shader culls nothing).
        /// </summary>
        private static Mesh BuildCloudMesh()
        {
            var mesh = new Mesh {
                name = "Sky Clouds",
                hideFlags = HideFlags.DontSave
            };

            mesh.SetVertices(new[] {
                new Vector3(-1f, -1f, -1f), new Vector3(1f, -1f, -1f), new Vector3(1f, 1f, -1f), new Vector3(-1f, 1f, -1f),
                new Vector3(-1f, -1f, 1f), new Vector3(1f, -1f, 1f), new Vector3(1f, 1f, 1f), new Vector3(-1f, 1f, 1f)
            });

            // Two triangles per face: back, front, left, right, bottom, top.
            mesh.SetTriangles(new[] {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 3, 7, 0, 7, 4,
                1, 5, 6, 1, 6, 2,
                0, 4, 5, 0, 5, 1,
                3, 2, 6, 3, 6, 7
            }, 0, false);

            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// Fills in one disc's four vertices and two triangles in the mesh
        /// arrays. The layout matches the shader's Attributes.
        /// </summary>
        private static void WriteQuad(
            int index, Vector3 direction, Color color, float radius,
            Vector3[] directions, Color[] colors, Vector3[] corners, int[] triangles)
        {
            int firstVertex = index * 4;

            for (int c = 0; c < 4; c++) {
                directions[firstVertex + c] = direction;
                colors[firstVertex + c] = color;
                corners[firstVertex + c] = new Vector3(_corners[c].x, _corners[c].y, radius);
            }

            for (int t = 0; t < 6; t++) {
                triangles[index * 6 + t] = firstVertex + _cornerOrder[t];
            }
        }

        /// <summary>
        /// Turns the filled arrays into a mesh.
        /// </summary>
        private static Mesh CreateMesh(
            string meshName, Vector3[] directions, Color[] colors, Vector3[] corners, int[] triangles)
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
