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
    /// fades out (Noise Circles: the player's and everyone else's toggled
    /// apart), in the headset and the Scene view.
    ///
    /// All the shapes go into one mesh (one draw call), rebuilt each frame
    /// just before rendering, without allocating once its lists have grown.
    /// </summary>
    public class InHeadsetGizmos : MonoBehaviour, IDebugDrawable
    {
        // Show each object's detailed ("selected in the editor") view rather
        // than its plain one - there's no selection in the headset.
        [Tooltip("On: each object's detailed view is drawn (as the Scene view shows it when selected - there's no selection in the headset), e.g. the body zones' chest box and shoulder balls; off: only each object's plain view.")]
        [SerializeField] private bool showDetail = true;

        // Draw the shapes through walls and other geometry, rather than
        // hidden by it. On by default: a hand capsule held back by a wall is
        // half inside it.
        [Tooltip("On: shapes are drawn through walls and other geometry, e.g. a hand capsule held half inside a wall shows whole; off: hidden behind geometry like anything else. Can be flipped while playing.")]
        [SerializeField] private bool seeThroughWalls = true;

        // Which kinds of gizmo are drawn. A new IDebugDrawable type gets a
        // toggle here and a line in IsShown() and SetAllTypes(); until then
        // it's under Other.
        [Header("Gizmo Types")]

        // Ticking or unticking this sets every toggle below (both noise
        // circle toggles included) to match - see OnValidate(). Each can still be changed
        // on its own afterwards.
        [Tooltip("Ticking or unticking sets every gizmo type toggle below, and both noise circle toggles, to match, e.g. untick it, then tick only Guards to see guards alone. Each can still be changed on its own afterwards.")]
        [SerializeField] private bool all = true;

        [Tooltip("On: guards' gizmos are drawn (AI.Guard), e.g. its suspicion and detection meters as lines above its head, coloured by its state; off: hidden.")]
        [SerializeField] private bool guards = true;
        [Tooltip("On: guards' planted-foot balls are drawn (AI.GuardFootLock), e.g. green while a foot is planted, yellow while the lock fades; off: hidden.")]
        [SerializeField] private bool guardFootLocks = true;
        [Tooltip("On: guard patrol routes are drawn (AI.PatrolRoute), e.g. the path a guard walks between its points; off: hidden.")]
        [SerializeField] private bool patrolRoutes = true;
        [Tooltip("On: Core.NoiseDebug's fading wire spheres are drawn, e.g. a sphere the size of each noise's radius; off: hidden.")]
        [SerializeField] private bool noiseDebug = true;
        [Tooltip("On: stand-in noise listeners' markers are drawn (Core.NoiseListenerDebug), e.g. a marker turning yellow with a line to the noise it heard; off: hidden.")]
        [SerializeField] private bool noiseListeners = true;
        [Tooltip("On: colliders left on the Default layer are outlined (Core.DefaultLayerGizmos), e.g. a wall never moved to Environment; off: hidden.")]
        [SerializeField] private bool defaultLayerColliders = true;
        [Tooltip("On: level exit zones are drawn (Core.ExitZone), e.g. the volume the player escapes through; off: hidden.")]
        [SerializeField] private bool exitZones = true;
        [Tooltip("On: gameplay light sources are drawn (Core.LightSource), e.g. a wire sphere of a torch's range; off: hidden.")]
        [SerializeField] private bool lightSources = true;
        [Tooltip("On: sound rooms are drawn (Core.SoundRoom), e.g. the box of each room sound passes through; off: hidden.")]
        [SerializeField] private bool soundRooms = true;
        [Tooltip("On: sound portals are drawn (Core.SoundPortal), e.g. a doorway joining two sound rooms, coloured open or closed (warning colour if both sides are the same room); off: hidden.")]
        [SerializeField] private bool soundPortals = true;
        [Tooltip("On: climbable edges are drawn (Interaction.ClimbableEdge), e.g. a ledge's grab volume box and a line along its lip; off: hidden.")]
        [SerializeField] private bool climbableEdges = true;
        [Tooltip("On: climbable ropes are drawn (Interaction.ClimbableRope), e.g. a rope hung by a rope arrow; off: hidden.")]
        [SerializeField] private bool climbableRopes = true;
        [Tooltip("On: ladders are drawn (Interaction.Ladder), e.g. the outline of its grab box; off: hidden.")]
        [SerializeField] private bool ladders = true;
        [Tooltip("On: doors are drawn (Interaction.Door), e.g. the arc its free edge swings through; off: hidden.")]
        [SerializeField] private bool doors = true;
        [Tooltip("On: door handles are drawn (Interaction.DoorHandle), e.g. the handle's grab volume; off: hidden.")]
        [SerializeField] private bool doorHandles = true;
        [Tooltip("On: sliding door bolts are drawn (Interaction.DoorBolt), e.g. the outline of the bolt's grab box; off: hidden.")]
        [SerializeField] private bool doorBolts = true;
        [Tooltip("On: door keyholes are drawn (Interaction.DoorKeyhole), e.g. the shape of the keyhole's cut through the door; off: hidden.")]
        [SerializeField] private bool doorKeyholes = true;
        [Tooltip("On: keyed locks are drawn (Interaction.KeyLock), e.g. a circle on each face, coloured locked or unlocked; off: hidden.")]
        [SerializeField] private bool keyLocks = true;
        [Tooltip("On: pickable locks are drawn (Interaction.PickableLock), e.g. a circle on each face, coloured locked or unlocked; off: hidden.")]
        [SerializeField] private bool pickableLocks = true;
        [Tooltip("On: the big lock's picking view is drawn (Interaction.BigLockDebug), e.g. where this stage's pin is hidden and where each pick is; off: hidden.")]
        [SerializeField] private bool bigLockDebug = true;
        [Tooltip("On: grabbable props are drawn (Interaction.Grabbable), e.g. a bottle's grip axis and the cylinder the hand closes round; off: hidden.")]
        [SerializeField] private bool grabbables = true;
        [Tooltip("On: the level generator's markers in each generated root are drawn (LevelGen.GeneratedRoot), e.g. a wire box per placement marker, with an upright line in the detailed view; off: hidden.")]
        [SerializeField] private bool generatedRoots = true;
        [Tooltip("On: the player's head-locked visibility gauge is drawn (VisibilityDebug), e.g. a bar from blue to yellow as the player gets more visible; off: hidden.")]
        [SerializeField] private bool visibilityDebug = true;
        [Tooltip("On: the physical hands' collision pieces are drawn (PlayerHandVisuals), e.g. each hand's palm box and finger capsules, green or red in contact; off: hidden.")]
        [SerializeField] private bool playerHandVisuals = true;
        [Tooltip("On: the body reach zones are drawn (PlayerBodyZones, detailed view in Play Mode), e.g. the chest box and both shoulder balls, green while a hand is in one; off: hidden.")]
        [SerializeField] private bool playerBodyZones = true;
        [Tooltip("On: the blackjack's hit capsule is drawn while it's drawn (PlayerBlackjack), e.g. orange, red while touching a guard's head; off: hidden.")]
        [SerializeField] private bool blackjackHit = true;

        // Any IDebugDrawable type without its own toggle above.
        [Tooltip("On: every drawable type without its own toggle above is drawn, e.g. a newly added gizmo type; off: those are hidden.")]
        [SerializeField] private bool other = true;

        // A flat circle of each noise's radius where it was made (footsteps,
        // landings, impacts, doors, bolts - anything that reaches guards).
        // Player: noises whose source is the player's rig (footsteps,
        // landings). Other: everything else (guards, props, doors, bolts).
        [Header("Noise Circles")]
        [Tooltip("On: noises made by the player's rig show as fading circles of their radius, e.g. footsteps and landings; off: hidden (also hides ones already fading).")]
        [SerializeField] private bool noiseCirclesPlayer = true;
        [Tooltip("On: every other noise shows as a fading circle of its radius, e.g. guards, doors, thrown props and arrows; off: hidden (also hides ones already fading).")]
        [SerializeField] private bool noiseCirclesOther = true;

        // How long a circle takes to fade out, in seconds.
        [Tooltip("Seconds a noise circle takes to fade out, e.g. 2 = each footstep's circle is gone two seconds later. Higher = circles linger longer; 0 = no circles at all.")]
        [SerializeField] private float noiseFadeTime = 2f;

        [Tooltip("The noise circles' colour; its alpha is where the fade starts, e.g. light cyan.")]
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
        private readonly bool[] _noiseFromPlayer = new bool[NoiseCapacity];
        private int _nextNoise;

        // The player's root, so a noise can be told apart as the player's.
        // Found once on enable; null when there's no player rig.
        private Transform _playerRoot;
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

            // A scene search, but once per enable in a debug tool only.
            PlayerController player = FindAnyObjectByType<PlayerController>();
            _playerRoot = player != null ? player.transform : null;

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
        /// Sets every gizmo type toggle, and both noise circle toggles, to on.
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
            playerBodyZones = on;
            blackjackHit = on;
            other = on;
            noiseCirclesPlayer = on;
            noiseCirclesOther = on;
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
                PlayerBodyZones => playerBodyZones,
                PlayerBlackjack => blackjackHit,
                _ => other
            };
        }

        /// <summary>
        /// Stores each noise in the ring as it's made, noting whether the
        /// player made it (source under the player's root).
        /// </summary>
        private void OnNoiseEmitted(Noise noise)
        {
            _noises[_nextNoise] = noise;
            _noiseTimes[_nextNoise] = Time.time;
            _noiseFromPlayer[_nextNoise] = _playerRoot != null && noise.Source != null && noise.Source.IsChildOf(_playerRoot);
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
        /// the ring, fading out over noiseFadeTime. The player's noises show
        /// with noiseCirclesPlayer, all others with noiseCirclesOther.
        /// Shared by the headset mesh and the Scene view gizmos.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if ((!noiseCirclesPlayer && !noiseCirclesOther) || noiseFadeTime <= 0f) {
                return;
            }

            for (int i = 0; i < _noiseCount; i++) {
                float age = Time.time - _noiseTimes[i];

                if (age > noiseFadeTime) {
                    continue;
                }

                // Checked when drawn, not when stored, so toggling either
                // shows or hides circles already fading.
                if (!(_noiseFromPlayer[i] ? noiseCirclesPlayer : noiseCirclesOther)) {
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
