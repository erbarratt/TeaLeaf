using System.Collections.Generic;
using AI;
using Core;
using Interaction;
using LevelGen;
using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// Shows every IDebugDrawable's debug shapes in the headset - the
    /// in-headset version of the Scene view's gizmos. Unity's Gizmos can't be
    /// used in VR: they're drawn for one flat camera, so they show in one eye
    /// only, offset and distorted. This draws the same shapes (from the same
    /// DrawDebug() code) as a real line mesh that URP renders in both eyes.
    ///
    /// Put it on any scene object and enable it to turn the view on - it can
    /// be toggled in the Inspector while playing. Disabled, it costs nothing:
    /// no mesh building and nothing drawn.
    ///
    /// It sits in Player rather than Core because it names gizmo types from
    /// every system (AI, Interaction, LevelGen, Player), and Core depends
    /// on none of them.
    ///
    /// Each kind of gizmo has its own toggle (Gizmo Types), and All flips
    /// every one of them at once. Every noise made
    /// while this is enabled also shows as a flat circle of its radius that
    /// fades out (Noise Circles), in the headset and the Scene view.
    ///
    /// All the shapes go into one mesh (one draw call), rebuilt each frame
    /// just before rendering, without allocating once its lists have grown.
    /// </summary>
    public class InHeadsetGizmos : MonoBehaviour, IDebugDrawable
    {
        // Show each object's detailed ("selected in the editor") view rather
        // than its plain one - there's no selection in the headset.
        [SerializeField] private bool showDetail = true;

        // Draw the shapes through walls and other geometry, rather than
        // hidden by it. On by default: a hand capsule held back by a wall is
        // half inside it.
        [SerializeField] private bool seeThroughWalls = true;

        // Which kinds of gizmo are drawn. A new IDebugDrawable type gets a
        // toggle here and a line in IsShown() and SetAllTypes(); until then
        // it's under Other.
        [Header("Gizmo Types")]

        // Ticking or unticking this sets every toggle below (noise circles
        // included) to match - see OnValidate(). Each can still be changed
        // on its own afterwards.
        [SerializeField] private bool all = true;

        [SerializeField] private bool guards = true;
        [SerializeField] private bool guardFootLocks = true;
        [SerializeField] private bool patrolRoutes = true;
        [SerializeField] private bool noiseDebug = true;
        [SerializeField] private bool noiseListeners = true;
        [SerializeField] private bool defaultLayerColliders = true;
        [SerializeField] private bool exitZones = true;
        [SerializeField] private bool lightSources = true;
        [SerializeField] private bool soundRooms = true;
        [SerializeField] private bool soundPortals = true;
        [SerializeField] private bool climbableEdges = true;
        [SerializeField] private bool climbableRopes = true;
        [SerializeField] private bool ladders = true;
        [SerializeField] private bool doors = true;
        [SerializeField] private bool doorHandles = true;
        [SerializeField] private bool doorBolts = true;
        [SerializeField] private bool doorKeyholes = true;
        [SerializeField] private bool keyLocks = true;
        [SerializeField] private bool pickableLocks = true;
        [SerializeField] private bool bigLockDebug = true;
        [SerializeField] private bool grabbables = true;
        [SerializeField] private bool generatedRoots = true;
        [SerializeField] private bool visibilityDebug = true;
        [SerializeField] private bool playerHandVisuals = true;
        [SerializeField] private bool playerBlackjack = true;
        [SerializeField] private bool playerPack = true;

        // Any IDebugDrawable type without its own toggle above.
        [SerializeField] private bool other = true;

        // A flat circle of each noise's radius where it was made (footsteps,
        // landings, impacts, doors, bolts - anything that reaches guards).
        [Header("Noise Circles")]
        [SerializeField] private bool noiseCircles = true;

        // How long a circle takes to fade out, in seconds.
        [SerializeField] private float noiseFadeTime = 2f;

        [SerializeField] private Color noiseColor = new(0.2f, 0.9f, 1f, 1f);

        private const string ShaderName = "TeaLeaf/DebugLines";

        // Big enough to contain any level - see Build().
        private const float BoundsSize = 100000f;

        private static readonly int _zTestId = Shader.PropertyToID("_ZTest");

        private readonly DebugLines _lines = new();

        private Mesh _mesh;
        private Material _material;
        private GameObject _meshObject;
        private MeshRenderer _meshRenderer;

        // The frame the mesh was last built in - see OnBeginContextRendering().
        private int _builtFrame = -1;

        // The most recent noises and when each was made. A fixed-size ring:
        // a new noise overwrites the oldest, so nothing is allocated however
        // many are made.
        private const int NoiseCapacity = 32;
        private readonly Noise[] _noises = new Noise[NoiseCapacity];
        private readonly float[] _noiseTimes = new float[NoiseCapacity];
        private int _nextNoise;
        private int _noiseCount;

        // The value of all when OnValidate() last ran, so it can tell the
        // All box was just clicked from any other Inspector change. Null
        // until the first run, which only records it (OnValidate() also runs
        // on load, and mustn't overwrite the saved toggles then).
        private bool? _lastAll;

        private void OnEnable()
        {
            if (_mesh == null) {
                Build();
            }

            _meshRenderer.enabled = true;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            NoiseSystem.Emitted += OnNoiseEmitted;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

            // A static event outlives a scene reload, so it must be left.
            NoiseSystem.Emitted -= OnNoiseEmitted;

            // Forget old noises, so re-enabling doesn't show half-faded ones.
            _noiseCount = 0;

            if (_meshRenderer != null) {
                _meshRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            // The mesh object lives at the scene root, so it isn't destroyed
            // along with this one; the mesh and material were made in code,
            // so nothing else will free them.
            Destroy(_meshObject);
            Destroy(_mesh);
            Destroy(_material);
        }

        /// <summary>
        /// Editor-only: runs whenever a value is changed in the Inspector,
        /// so seeThroughWalls can be flipped while playing, and a click on
        /// All sets every gizmo type toggle to match it.
        /// </summary>
        private void OnValidate()
        {
            ApplyDepthTest();

            if (_lastAll.HasValue && _lastAll.Value != all) {
                SetAllTypes(all);
            }

            _lastAll = all;
        }

        /// <summary>
        /// Sets every gizmo type toggle, and the noise circles, to on.
        /// </summary>
        private void SetAllTypes(bool on)
        {
            guards = on;
            guardFootLocks = on;
            patrolRoutes = on;
            noiseDebug = on;
            noiseListeners = on;
            defaultLayerColliders = on;
            exitZones = on;
            lightSources = on;
            soundRooms = on;
            soundPortals = on;
            climbableEdges = on;
            climbableRopes = on;
            ladders = on;
            doors = on;
            doorHandles = on;
            doorBolts = on;
            doorKeyholes = on;
            keyLocks = on;
            pickableLocks = on;
            bigLockDebug = on;
            grabbables = on;
            generatedRoots = on;
            visibilityDebug = on;
            playerHandVisuals = on;
            playerBlackjack = on;
            playerPack = on;
            other = on;
            noiseCircles = on;
        }

        /// <summary>
        /// Creates the mesh, material and the object that renders them - once,
        /// the first time this is enabled.
        /// </summary>
        private void Build()
        {
            _mesh = new Mesh {
                name = "In-Headset Gizmos",

                // 32-bit indices, so a busy level can't overflow the 65,535
                // vertex limit of the default 16-bit ones.
                indexFormat = IndexFormat.UInt32
            };

            // Tells Unity the mesh changes every frame, so it keeps the data
            // somewhere cheap to update.
            _mesh.MarkDynamic();

            Shader shader = Shader.Find(ShaderName);

            if (shader == null) {
                Debug.LogError($"InHeadsetGizmos: shader '{ShaderName}' not found - the lines will be pink.");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }

            _material = new Material(shader);
            ApplyDepthTest();

            // At the scene root with an identity transform, because the mesh's
            // vertices are already world positions - parented to anything
            // that moves, they'd be moved twice.
            _meshObject = new GameObject("In-Headset Gizmos Mesh");
            _meshObject.AddComponent<MeshFilter>().sharedMesh = _mesh;

            _meshRenderer = _meshObject.AddComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Fixed, huge bounds: the renderer is never culled, and nothing
            // has to recalculate bounds after each rebuild.
            _meshRenderer.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);
        }

        /// <summary>
        /// Sets the material's depth test from seeThroughWalls: Always draws
        /// through everything, LessEqual is the normal depth test.
        /// </summary>
        private void ApplyDepthTest()
        {
            if (_material == null) {
                return;
            }

            CompareFunction zTest = seeThroughWalls ? CompareFunction.Always : CompareFunction.LessEqual;
            _material.SetFloat(_zTestId, (float)zTest);
        }

        /// <summary>
        /// Rebuilds the mesh from every registered drawable whose type is
        /// toggled on, plus the noise circles. Runs as URP starts rendering,
        /// rather than in LateUpdate(): the hands' Tracked Pose Drivers move
        /// the controllers once more just before rendering, and building any
        /// earlier would draw the hand capsules where the hands were a moment
        /// ago, trailing behind them.
        ///
        /// URP calls this once per render - including the Scene view's while
        /// it's open - so the frame count check builds the mesh only once
        /// per frame.
        /// </summary>
        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_builtFrame == Time.frameCount) {
                return;
            }

            _builtFrame = Time.frameCount;

            _lines.Clear();

            for (int i = 0; i < DebugDrawRegistry.Count; i++) {
                IDebugDrawable drawable = DebugDrawRegistry.Get(i);

                if (IsShown(drawable)) {
                    _lines.Draw(drawable, showDetail);
                }
            }

            // This component's own shapes (the noise circles). It isn't in
            // the registry, so the type toggles never hide it.
            _lines.Draw(this, showDetail);

            _lines.CopyTo(_mesh);
        }

        /// <summary>
        /// Whether a drawable's type is toggled on. A chain of type checks
        /// per drawable per frame - cheap, and only while this is enabled.
        /// </summary>
        private bool IsShown(IDebugDrawable drawable)
        {
            return drawable switch {
                Guard => guards,
                GuardFootLock => guardFootLocks,
                PatrolRoute => patrolRoutes,
                NoiseDebug => noiseDebug,
                NoiseListenerDebug => noiseListeners,
                DefaultLayerGizmos => defaultLayerColliders,
                ExitZone => exitZones,
                LightSource => lightSources,
                SoundRoom => soundRooms,
                SoundPortal => soundPortals,
                ClimbableEdge => climbableEdges,
                ClimbableRope => climbableRopes,
                Ladder => ladders,
                Door => doors,
                DoorHandle => doorHandles,
                DoorBolt => doorBolts,
                DoorKeyhole => doorKeyholes,
                KeyLock => keyLocks,
                PickableLock => pickableLocks,
                BigLockDebug => bigLockDebug,
                Grabbable => grabbables,
                GeneratedRoot => generatedRoots,
                VisibilityDebug => visibilityDebug,
                PlayerHandVisuals => playerHandVisuals,
                PlayerBlackjack => playerBlackjack,
                PlayerPack => playerPack,
                _ => other
            };
        }

        /// <summary>
        /// Stores each noise in the ring as it's made.
        /// </summary>
        private void OnNoiseEmitted(Noise noise)
        {
            _noises[_nextNoise] = noise;
            _noiseTimes[_nextNoise] = Time.time;
            _nextNoise = (_nextNoise + 1) % NoiseCapacity;
            _noiseCount = Mathf.Min(_noiseCount + 1, NoiseCapacity);
        }

        /// <summary>
        /// Draws the noise circles in the Scene view too - see DrawDebug().
        /// Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (isActiveAndEnabled) {
                DebugLines.ForGizmos.Draw(this, false);
            }
        }

        /// <summary>
        /// One flat (level) circle per recent noise, centred where it was
        /// made, its radius the noise's, with a line from the source out to
        /// the ring, fading out over noiseFadeTime.
        /// Shared by the headset mesh and the Scene view gizmos.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!noiseCircles || noiseFadeTime <= 0f) {
                return;
            }

            for (int i = 0; i < _noiseCount; i++) {
                float age = Time.time - _noiseTimes[i];

                if (age > noiseFadeTime) {
                    continue;
                }

                Color color = noiseColor;
                color.a *= 1f - age / noiseFadeTime;
                lines.Color = color;

                float radius = _noises[i].Radius;
                Vector3 position = _noises[i].Position;
                lines.Circle(position, Vector3.right * radius, Vector3.forward * radius);
                // A radius from the source out to the ring, so where the
                // noise was made shows in the middle of a large circle.
                lines.Line(position, position + Vector3.right * radius);
            }
        }
    }
}
