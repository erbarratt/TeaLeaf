using UnityEngine;
using UnityEngine.Rendering;

namespace Core
{
    /// <summary>
    /// A temporary look at Silent Hill 2 style fog: thick fog close to the
    /// player that hides everything beyond a short distance. For judging
    /// the look only - not a decision to use it (see the devlog entry of
    /// 2026-10-07 for why distance haze is only a reserve).
    ///
    /// Put it on the Debug object. While it's enabled in Play Mode it
    /// turns on Unity's built-in fog. Disabling it - or stopping Play
    /// Mode - puts everything back, and nothing is saved into the scene.
    /// The values can be changed in the Inspector while playing.
    ///
    /// Two extras, each with its own switch:
    /// - Sky gradient: the fog clears towards the sky. The stars and moon
    ///   stay, and a dome round the camera is drawn over them in the fog's
    ///   colour - solid at the horizon, thinning with height. With it off,
    ///   the camera just clears to the fog colour (and the sky can be
    ///   hidden), so the fog is the same in every direction.
    /// - Clear indoors: the fog thins out while the player has a roof over
    ///   their head, and comes back when they step outside. The fog is one
    ///   setting for the whole view, so this is all-or-nothing: from
    ///   indoors, the street seen through a window is clear too.
    ///
    /// The fog itself is cheap: it isn't a full-screen effect. Each lit
    /// shader blends its own colour towards the fog colour by distance, a
    /// few instructions per vertex or pixel. Only shaders that support
    /// Unity's fog are affected (URP Lit and Unlit are); the project's own
    /// overlay, ghost and sky shaders aren't, so the UI markers stay clear.
    /// Fog on geometry goes by distance only, not height: something tall
    /// and far away is fog-coloured to its top, against a sky that has
    /// cleared behind it.
    /// </summary>
    public class FogTestDebug : MonoBehaviour
    {
        // The fog's colour. Pale grey is the Silent Hill daytime look; try
        // a dark blue-grey for night.
        [SerializeField] private Color fogColor = new(0.55f, 0.57f, 0.6f, 1f);

        // How the fog thickens with distance. Linear: none before
        // startDistance, total at endDistance - a definite wall. The two
        // exponential modes use density instead and have no hard edge.
        [SerializeField] private FogMode mode = FogMode.Linear;

        // Linear only, in metres.
        [SerializeField] private float startDistance = 1f;
        [SerializeField] private float endDistance = 18f;

        // Exponential modes only. For ExponentialSquared, things are all
        // but gone at about 2 / density metres (0.1 = about 20m).
        [SerializeField] private float density = 0.1f;

        // Only without the sky gradient: hide the stars and moon while the
        // fog is on.
        [SerializeField] private bool hideSky = true;

        [Header("Sky Gradient")]
        [SerializeField] private bool skyGradient = true;

        // Up to this many degrees above the horizon the sky is solid fog.
        [SerializeField] private float hazeFullAngle = 5f;

        // From there the fog thins, until at this many degrees above the
        // horizon (and on up to straight overhead) only overheadFog of it
        // is left.
        [SerializeField] private float hazeClearAngle = 50f;

        // How much fog is left overhead: 0 = a clear night sky, 1 = as
        // thick as at the horizon.
        [SerializeField, Range(0f, 1f)] private float overheadFog = 0.35f;

        [Header("Indoors")]
        [SerializeField] private bool clearIndoors = true;

        // How much of the fog is left indoors: 0.1 = a tenth as thick (it
        // reaches ten times as far).
        [SerializeField, Range(0.01f, 1f)] private float indoorFogAmount = 0.1f;

        // "Indoors" = something on roofLayers within this many metres
        // straight above the head.
        [SerializeField] private float roofCheckHeight = 6f;
        [SerializeField] private LayerMask roofLayers;

        // Seconds between roof checks, and seconds the fog takes to thin
        // out or come back.
        [SerializeField] private float roofCheckInterval = 0.2f;
        [SerializeField] private float indoorBlendTime = 1.5f;

        // The objects Core.ProceduralSky makes at the scene root.
        private const string StarsObjectName = "Sky Stars";
        private const string MoonObjectName = "Sky Moon";

        // The dome: its radius in metres (inside the camera's far clip
        // plane, beyond anything the fog leaves visible), how many pieces
        // round, and how many rings the thinning part is drawn with.
        private const float DomeRadius = 100f;
        private const int DomeSegments = 32;
        private const int DomeBandSteps = 8;

        // Per-vertex colour, no depth write, and a depth test it can be
        // told to use - all the dome needs. See BuildDome().
        private const string DomeShaderName = "TeaLeaf/DebugLines";
        private static readonly int _zTestProperty = Shader.PropertyToID("_ZTest");

        // What was there before, to put back.
        private bool _previousFog;
        private Color _previousFogColor;
        private FogMode _previousMode;
        private float _previousStart;
        private float _previousEnd;
        private float _previousDensity;
        private Color _previousBackground;

        private Camera _camera;
        private GameObject _stars;
        private GameObject _moon;
        private bool _isApplied;

        // The sky gradient's dome, made in Begin().
        private GameObject _dome;
        private Mesh _domeMesh;
        private Material _domeMaterial;

        // Whether the last roof check found a roof, how far the fog has
        // thinned for it (0 = outdoors, 1 = fully indoors), and the time
        // until the next check.
        private bool _isIndoors;
        private float _indoorBlend;
        private float _roofCheckTimer;

        /// <summary>
        /// Editor-only: runs when the component is added.
        /// </summary>
        private void Reset()
        {
            roofLayers = LayerMask.GetMask("Environment");
        }

        /// <summary>
        /// Start() rather than OnEnable() for the first time: the sky makes
        /// its objects and sets the camera's colour in its own OnEnable(),
        /// which may run after this one. Every OnEnable() has run before
        /// the first Start().
        /// </summary>
        private void Start()
        {
            Begin();
        }

        private void OnEnable()
        {
            // Re-enabled later in Play Mode (Start() only runs once).
            if (didStart) {
                Begin();
            }
        }

        private void OnDisable()
        {
            if (!_isApplied) {
                return;
            }

            _isApplied = false;

            RenderSettings.fog = _previousFog;
            RenderSettings.fogColor = _previousFogColor;
            RenderSettings.fogMode = _previousMode;
            RenderSettings.fogStartDistance = _previousStart;
            RenderSettings.fogEndDistance = _previousEnd;
            RenderSettings.fogDensity = _previousDensity;

            // Unity's != null is false for objects already destroyed, as
            // they are when the scene is unloading.
            if (_camera != null) {
                _camera.backgroundColor = _previousBackground;
            }

            SetSkyVisible(true);

            if (_dome != null) {
                Destroy(_dome);
            }

            // A mesh and a material made in code aren't cleaned up with
            // the object that used them.
            if (_domeMesh != null) {
                Destroy(_domeMesh);
            }

            if (_domeMaterial != null) {
                Destroy(_domeMaterial);
            }
        }

        /// <summary>
        /// Editor-only: applies a value changed in the Inspector straight
        /// away while playing.
        /// </summary>
        private void OnValidate()
        {
            if (_isApplied) {
                Apply();
            }
        }

        /// <summary>
        /// Remembers how things were, finds the camera and the sky's
        /// objects, makes the dome and turns the fog on. The two finds
        /// search the scene by name - fine once, in a debug script.
        /// </summary>
        private void Begin()
        {
            _previousFog = RenderSettings.fog;
            _previousFogColor = RenderSettings.fogColor;
            _previousMode = RenderSettings.fogMode;
            _previousStart = RenderSettings.fogStartDistance;
            _previousEnd = RenderSettings.fogEndDistance;
            _previousDensity = RenderSettings.fogDensity;

            _camera = Camera.main;

            if (_camera != null) {
                _previousBackground = _camera.backgroundColor;
            }

            _stars = GameObject.Find(StarsObjectName);
            _moon = GameObject.Find(MoonObjectName);

            // A component added before roofLayers existed has none set.
            if (roofLayers.value == 0) {
                roofLayers = LayerMask.GetMask("Environment");
            }

            CreateDome();

            _indoorBlend = 0f;
            _roofCheckTimer = 0f;
            _isApplied = true;
            Apply();
        }

        /// <summary>
        /// Sets everything from the fields: the fog, and either the sky
        /// gradient (sky shown, dome drawn over it) or the plain version
        /// (camera cleared to the fog colour, sky optionally hidden).
        /// </summary>
        private void Apply()
        {
            RenderSettings.fog = true;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = mode;
            ApplyFogAmount();

            if (_camera != null) {
                _camera.backgroundColor = skyGradient ? _previousBackground : fogColor;
            }

            SetSkyVisible(skyGradient || !hideSky);

            if (_dome != null) {
                _dome.SetActive(skyGradient);

                if (skyGradient) {
                    BuildDome();
                }
            }
        }

        /// <summary>
        /// Sets how far the fog reaches, thinned by however far indoors
        /// the player currently is. Thinner fog = the linear distances
        /// further out, or the density lower, by the same factor.
        /// </summary>
        private void ApplyFogAmount()
        {
            // SmoothStep eases the change in and out.
            float amount = Mathf.Lerp(1f, indoorFogAmount, Mathf.SmoothStep(0f, 1f, _indoorBlend));

            RenderSettings.fogStartDistance = startDistance / amount;
            RenderSettings.fogEndDistance = endDistance / amount;
            RenderSettings.fogDensity = density * amount;
        }

        /// <summary>
        /// Checks for a roof a few times a second (one ray straight up
        /// from the head) and eases the fog towards its indoor or outdoor
        /// amount. Only touches the fog while it's changing.
        /// </summary>
        private void Update()
        {
            if (!_isApplied || _camera == null) {
                return;
            }

            _roofCheckTimer -= Time.deltaTime;

            if (_roofCheckTimer <= 0f) {
                _roofCheckTimer = roofCheckInterval;
                _isIndoors = clearIndoors && Physics.Raycast(
                    _camera.transform.position,
                    Vector3.up,
                    roofCheckHeight,
                    roofLayers,
                    QueryTriggerInteraction.Ignore);
            }

            float step = indoorBlendTime > 0f ? Time.deltaTime / indoorBlendTime : 1f;
            float blend = Mathf.MoveTowards(_indoorBlend, _isIndoors ? 1f : 0f, step);

            if (blend != _indoorBlend) {
                _indoorBlend = blend;
                ApplyFogAmount();
            }
        }

        /// <summary>
        /// Keeps the dome centred on the head, after everything has moved
        /// this frame. It never turns: the horizon stays level.
        /// </summary>
        private void LateUpdate()
        {
            if (_isApplied && skyGradient && _dome != null && _camera != null) {
                _dome.transform.position = _camera.transform.position;
            }
        }

        /// <summary>
        /// Shows or hides the stars and the moon, if the scene has them.
        /// </summary>
        private void SetSkyVisible(bool visible)
        {
            if (_stars != null) {
                _stars.SetActive(visible);
            }

            if (_moon != null) {
                _moon.SetActive(visible);
            }
        }

        /// <summary>
        /// Makes the dome's object, mesh and material (once per enable).
        /// It borrows the debug lines' shader, which draws a mesh in its
        /// vertex colours with alpha, writes no depth, and takes its depth
        /// test from the material: set to LessEqual, the level hides the
        /// dome. Its render queue is moved to just after the sky's (which
        /// draws at Transparent - 400), so it covers the stars and moon but
        /// is drawn before everything else see-through.
        /// </summary>
        private void CreateDome()
        {
            Shader shader = Shader.Find(DomeShaderName);

            if (shader == null) {
                Debug.LogWarning($"FogTestDebug: shader '{DomeShaderName}' not found - no sky gradient.", this);
                return;
            }

            _domeMaterial = new Material(shader) { renderQueue = (int)RenderQueue.Transparent - 390 };
            _domeMaterial.SetFloat(_zTestProperty, (float)CompareFunction.LessEqual);

            _domeMesh = new Mesh { name = "Fog Test Sky Haze" };

            _dome = new GameObject("Fog Test Sky Haze");
            _dome.AddComponent<MeshFilter>().sharedMesh = _domeMesh;

            MeshRenderer domeRenderer = _dome.AddComponent<MeshRenderer>();
            domeRenderer.sharedMaterial = _domeMaterial;
            domeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            domeRenderer.receiveShadows = false;
            domeRenderer.lightProbeUsage = LightProbeUsage.Off;
            domeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// Builds the dome's mesh: a whole ball round the camera made of
        /// rings of vertices, every one the fog's colour, differing only
        /// in alpha (how solid). From straight down up to hazeFullAngle
        /// above the horizon it's solid; over the next rings, up to
        /// hazeClearAngle, it thins to overheadFog; and it stays at that
        /// to straight overhead. The graphics card blends between rings.
        ///
        /// Run when the fog is switched on and when a value changes in the
        /// Inspector - it allocates, which is fine for that.
        /// </summary>
        private void BuildDome()
        {
            // Rings: straight down, the thinning band, straight up.
            int ringCount = DomeBandSteps + 3;
            float clearAngle = Mathf.Max(hazeClearAngle, hazeFullAngle + 1f);

            Vector3[] vertices = new Vector3[ringCount * DomeSegments];
            Color[] colors = new Color[vertices.Length];
            int[] triangles = new int[(ringCount - 1) * DomeSegments * 6];

            // The shader outputs vertex colours as they are, while Unity
            // converts the fog colour for lit shaders: convert this one the
            // same way, or the dome wouldn't match the fog at the horizon.
            Color color = QualitySettings.activeColorSpace == ColorSpace.Linear ? fogColor.linear : fogColor;

            for (int ring = 0; ring < ringCount; ring++) {
                float elevation;
                float alpha;

                if (ring == 0) {
                    elevation = -90f;
                    alpha = 1f;
                } else if (ring == ringCount - 1) {
                    elevation = 90f;
                    alpha = overheadFog;
                } else {
                    float t = (ring - 1) / (float)DomeBandSteps;
                    elevation = Mathf.Lerp(hazeFullAngle, clearAngle, t);
                    alpha = Mathf.Lerp(1f, overheadFog, Mathf.SmoothStep(0f, 1f, t));
                }

                float height = Mathf.Sin(elevation * Mathf.Deg2Rad);
                float ringRadius = Mathf.Cos(elevation * Mathf.Deg2Rad);

                for (int segment = 0; segment < DomeSegments; segment++) {
                    float around = segment / (float)DomeSegments * Mathf.PI * 2f;
                    int index = ring * DomeSegments + segment;

                    vertices[index] = new Vector3(Mathf.Cos(around) * ringRadius, height, Mathf.Sin(around) * ringRadius) * DomeRadius;
                    colors[index] = new Color(color.r, color.g, color.b, alpha);
                }
            }

            // Two triangles for each four-cornered piece between a ring
            // and the next. % wraps the last piece round to the first. The
            // shader draws both faces, so which way they wind doesn't
            // matter.
            int next = 0;

            for (int ring = 0; ring < ringCount - 1; ring++) {
                for (int segment = 0; segment < DomeSegments; segment++) {
                    int a = ring * DomeSegments + segment;
                    int b = ring * DomeSegments + (segment + 1) % DomeSegments;
                    int c = a + DomeSegments;
                    int d = b + DomeSegments;

                    triangles[next++] = a;
                    triangles[next++] = c;
                    triangles[next++] = b;
                    triangles[next++] = b;
                    triangles[next++] = c;
                    triangles[next++] = d;
                }
            }

            _domeMesh.Clear();
            _domeMesh.vertices = vertices;
            _domeMesh.colors = colors;
            _domeMesh.triangles = triangles;
            _domeMesh.RecalculateBounds();
        }
    }
}
