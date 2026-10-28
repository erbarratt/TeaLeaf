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
        [Tooltip("The camera whose background becomes the Sky Color (clear flags set to a flat colour), e.g. drag the Main Camera here. Left empty, the main camera is used.")]
        [SerializeField] private Camera viewCamera;

        // The empty sky behind everything.
        [Tooltip("The empty sky behind the stars, moon and clouds, e.g. black for a clear night. Lighter = a hazy, glowing sky. Picture only: lights nothing.")]
        [SerializeField] private Color skyColor = Color.black;

        // Which sky: every seed scatters the stars differently.
        [Tooltip("Which sky: every seed scatters the stars differently, e.g. 1 and 2 give two different starfields with the same settings. The same seed always gives the same sky.")]
        [SerializeField, Min(0)] private int seed = 1;

        [Header("Stars")]

        // All of them are above the horizon; none are made below it.
        [Tooltip("How many stars are made, all above the horizon, e.g. 2500 = a well-filled night sky. Higher = a denser sky (one mesh, one draw call either way, a few more pixels each); 0 = no stars.")]
        [SerializeField, Range(0, 20000)] private int starCount = 2500;

        // How wide a star is, in degrees, from the faintest stars (x) to
        // the brightest (y). One Quest 3 pixel is about 0.04 degrees.
        [Tooltip("How wide a star is, in degrees, from the faintest (X) to the brightest (Y), e.g. (0.05, 0.16) = faint stars about a pixel, bright ones about four (one Quest 3 pixel is about 0.04 degrees). Higher = bigger, softer-looking stars.")]
        [SerializeField] private Vector2 starSize = new Vector2(0.05f, 0.16f);

        // The smallest a star is ever drawn, in screen pixels across. A
        // star set smaller than this is drawn this size and dimmer instead,
        // because a star under a pixel wide flickers as the head moves.
        [Tooltip("The smallest a star is ever drawn, in screen pixels across, e.g. 1.5: a star set smaller is drawn this size and dimmer instead, because a sub-pixel star flickers as the head moves. Higher = steadier but blobbier faint stars.")]
        [SerializeField, Range(0.5f, 4f)] private float minStarPixels = 1.5f;

        // Brightness of the faintest and the brightest stars.
        [Tooltip("Brightness of the faintest stars, 0-1, e.g. 0.15 = barely there. Higher = even the faintest stars stand out.")]
        [SerializeField, Range(0f, 1f)] private float starMinBrightness = 0.15f;
        [Tooltip("Brightness of the brightest stars, 0-1, e.g. 1 = full white points. Lower = a dimmer sky overall.")]
        [SerializeField, Range(0f, 1f)] private float starMaxBrightness = 1f;

        // How rare bright stars are. 1 = every brightness is equally
        // likely; higher = mostly faint stars with a few bright ones, as
        // in a real sky.
        [Tooltip("How rare bright stars are, e.g. 3 = mostly faint stars with a few bright ones, as in a real sky. 1 = every brightness equally likely; higher = fewer bright stars.")]
        [SerializeField, Range(1f, 8f)] private float starFaintBias = 3f;

        // The colours stars come in: each star picks a random point along
        // this.
        [Tooltip("The colours stars come in: each star picks a random point along this gradient, e.g. blue-white through white to orange. Strength set by Star Color Amount.")]
        [SerializeField] private Gradient starColors = DefaultStarColors();

        // How strongly stars are coloured: 0 = all white, 1 = the full
        // colours above.
        [Tooltip("How strongly stars take the Star Colors, 0-1, e.g. 0.6 = gently tinted. 0 = all white, 1 = the gradient's full colours.")]
        [SerializeField, Range(0f, 1f)] private float starColorAmount = 0.6f;

        // Stars fade out over this many degrees above the horizon, as real
        // ones do through the thicker air, rather than stopping at a line.
        // 0 = full brightness right down to the horizon.
        [Tooltip("Stars fade out over this many degrees above the horizon, as real ones do through thicker air, e.g. 6 = gone by the horizon, full by 6 degrees up. Higher = a wider dark band low down; 0 = full brightness right down to the horizon.")]
        [SerializeField, Range(0f, 30f)] private float horizonFade = 6f;

        [Header("Moon")]
        [Tooltip("On: the moon is drawn where the Moon Light shines from. Off: no moon in the sky (moonlight still lights the level).")]
        [SerializeField] private bool showMoon = true;

        // The Directional Light: the moon is drawn where it shines from.
        [Tooltip("The Directional Light's transform: the moon is drawn where it shines from, so it matches the moonlight and shadows, e.g. drag the Moon and Sky light here. Filled in when the component is added; empty = no moon drawn.")]
        [SerializeField] private Transform moonLight;

        // How wide the moon is, in degrees. The real one is half a degree,
        // which looks tiny in a headset; games draw it several times that.
        [Tooltip("How wide the moon is, in degrees, e.g. 4 = about eight times the real moon's half a degree, which looks tiny in a headset. Higher = a bigger moon.")]
        [SerializeField, Range(0.5f, 30f)] private float moonSize = 4f;

        [Tooltip("The moon's colour, multiplied into the Moon Texture, e.g. a pale blue-white (0.85, 0.87, 0.9). Darker = a dimmer moon.")]
        [SerializeField] private Color moonColor = new Color(0.85f, 0.87f, 0.9f);

        // The picture of the moon's face, multiplied by the colour above.
        // Left empty, the moon is a plain disc of that colour.
        [Tooltip("The picture of the moon's face, multiplied by the Moon Color, e.g. Assets/Art/Textures/Moon/moon_circle.png. Left empty, the moon is a plain disc of that colour.")]
        [SerializeField] private Texture2D moonTexture;

        // The square of the picture the moon's disc fills, in 0-1 picture
        // coordinates (x, y from the bottom left; width, height), so a
        // picture with a margin round the moon needs no cropping.
        [Tooltip("The square of the Moon Texture the moon's disc fills, in 0-1 picture coordinates (X, Y from the bottom left; width, height), e.g. (0.1, 0.1, 0.8, 0.8) = the disc inside a 10% margin, so the picture needs no cropping. (0, 0, 1, 1) = the whole picture. Remeasure for a new picture.")]
        [SerializeField] private Rect moonTextureArea = new Rect(0f, 0f, 1f, 1f);

        [Header("Clouds")]
        [Tooltip("On: the drifting cloud layer is drawn over stars and moon (needs a Cloud Cubemap). Off: a clear sky.")]
        [SerializeField] private bool showClouds = true;

        // The clouds all round: bright where thick, black where the sky is
        // clear. Left empty, there are no clouds.
        [Tooltip("The clouds all round as a cubemap: bright where thick, black where the sky is clear, e.g. SkyClouds. Left empty, there are no clouds.")]
        [SerializeField] private Cubemap cloudCubemap;

        // What the cubemap's colour is multiplied by. The numbers are used
        // as they are, not converted to linear like a material colour.
        [Tooltip("What the cloud cubemap's colour is multiplied by, used as raw numbers (not converted to linear like a material colour), e.g. (0.118, 0.102, 0.18) = dim violet night clouds. Brighter = lighter clouds.")]
        [SerializeField] private Color cloudTint = new Color(0.118f, 0.102f, 0.18f);

        // Brightness on top of the tint.
        [Tooltip("Cloud brightness on top of the Cloud Tint, e.g. 1.1 = a touch brighter than the tint. Higher = brighter clouds; 0 = black clouds.")]
        [SerializeField, Range(0f, 8f)] private float cloudExposure = 1.1f;

        // Turns the clouds round the vertical axis, in degrees.
        [Tooltip("Turns the cloud layer round the vertical axis, in degrees, e.g. 214 = Thief VR's setting. Change it to move a cloud bank away from (or onto) the moon.")]
        [SerializeField, Range(0f, 360f)] private float cloudRotation = 214f;

        // Keeps the clouds turning round the vertical axis, so they travel
        // across the sky (and over the moon) rather than only churning in
        // place. Turned on the GPU from the shader's time; nothing per frame
        // here.
        [Tooltip("How fast the cloud layer keeps turning round the vertical axis, in degrees per second, e.g. 0.3 = one full turn every 20 minutes, a cloud taking the best part of a minute to pass over the moon. Higher = clouds sail by faster; negative turns the other way; 0 = clouds stay where Cloud Rotation puts them.")]
        [SerializeField, Range(-2f, 2f)] private float cloudTurnSpeed = 0.3f;

        // How much thick cloud hides the stars and moon behind it: 0 = the
        // clouds only add light, 1 = the thickest cloud hides them.
        [Tooltip("How much thick cloud hides the stars and moon behind it, 0-1, e.g. 1 = the thickest cloud hides them completely. Lower = see-through clouds; 0 = clouds only add light.")]
        [SerializeField, Range(0f, 1f)] private float cloudOpacity = 1f;

        // Which way the clouds flow across the sky (world x, z). Its length
        // scales the distance below too.
        [Tooltip("Which way the clouds flow across the sky (world X, Z), e.g. (0.78, 2.27) = mostly towards +Z. Its length scales Flow Distance too: longer = clouds churn further each cycle.")]
        [SerializeField] private Vector2 wind = new Vector2(0.78f, 2.27f);

        // How far the clouds flow in one cycle before the read jumps back
        // (see the TeaLeaf/SkyClouds shader); negative flows the other way.
        // Small: too far and the jump shows as a pulse.
        [Tooltip("How far the clouds flow in one cycle before the read jumps back (hidden by a crossfade), e.g. -0.02 = a small drift against the Wind. Negative flows the other way; keep it small, or the jump shows as a pulse. 0 = no flow.")]
        [SerializeField, Range(-0.2f, 0.2f)] private float flowDistance = -0.02f;

        // Flow cycles per second.
        [Tooltip("Flow cycles per second, e.g. 0.05 = one cycle every 20 seconds. Higher = faster churning clouds; 0 = still.")]
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
        private static readonly int _turnSpeedId = Shader.PropertyToID("_TurnSpeed");
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

            // The moon is drawn after the stars (a later render queue),
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
                _cloudMaterial.SetFloat(_turnSpeedId, cloudTurnSpeed);
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
        /// is on - the Directional Light, usually). order puts the parts
        /// in drawing order: each is drawn that many render queue steps
        /// after the shader's own queue. Never the renderer's sorting
        /// order: URP sorts see-through things by that before render
        /// queue, so a sky part with a higher one draws over the screen
        /// fade and the overlay markers.
        /// </summary>
        private MeshFilter CreatePart(string partName, Material material, int order)
        {
            material.renderQueue = material.shader.renderQueue + order;

            var part = new GameObject(partName) {
                hideFlags = HideFlags.DontSave
            };

            MeshFilter meshFilter = part.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
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
