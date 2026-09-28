using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core
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
    /// All the shapes go into one mesh (one draw call), rebuilt each frame
    /// just before rendering, without allocating once its lists have grown.
    /// </summary>
    public class InHeadsetGizmos : MonoBehaviour
    {
        // Show each object's detailed ("selected in the editor") view rather
        // than its plain one - there's no selection in the headset.
        [SerializeField] private bool showDetail = true;

        // Draw the shapes through walls and other geometry, rather than
        // hidden by it. On by default: a hand capsule held back by a wall is
        // half inside it.
        [SerializeField] private bool seeThroughWalls = true;

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

        private void OnEnable()
        {
            if (_mesh == null) {
                Build();
            }

            _meshRenderer.enabled = true;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

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
        /// so seeThroughWalls can be flipped while playing.
        /// </summary>
        private void OnValidate()
        {
            ApplyDepthTest();
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
        /// Rebuilds the mesh from every registered drawable. Runs as URP
        /// starts rendering, rather than in LateUpdate(): the hands' Tracked
        /// Pose Drivers move the controllers once more just before rendering,
        /// and building any earlier would draw the hand capsules where the
        /// hands were a moment ago, trailing behind them.
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
                _lines.Draw(DebugDrawRegistry.Get(i), showDetail);
            }

            _lines.CopyTo(_mesh);
        }
    }
}
