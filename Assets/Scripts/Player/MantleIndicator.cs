using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// A small white up-arrow shown while a mantle is possible - see
    /// PlayerMantling. Lives on a child of the Main Camera, so it's locked to
    /// the headset's position and rotation with no code: place the object
    /// where the arrow should sit in view (e.g. a little below centre,
    /// ~0.5m ahead) and it stays there.
    ///
    /// Like HandRayReticle, it builds its own mesh and material at runtime
    /// rather than needing assets. Unlike the reticle it must draw on top of
    /// everything: while climbing, the player's face is usually right up
    /// against the wall, so a normally depth-tested arrow 0.5m ahead would
    /// be hidden inside it.
    /// </summary>
    public class MantleIndicator : MonoBehaviour
    {
        // Overall height of the arrow in metres (width is 0.75x this).
        [SerializeField] private float size = 0.04f;
        [SerializeField] private Color color = Color.white;

        // UI/Default's depth test reads this property - see BuildMaterial().
        private static readonly int _zTestModeId = Shader.PropertyToID("unity_GUIZTestMode");

        private MeshRenderer _meshRenderer;
        private bool _isVisible;

        private void Awake()
        {
            MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = BuildArrowMesh(size);

            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _meshRenderer.material = BuildMaterial(color);
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.enabled = false;
        }

        /// <summary>
        /// Shows or hides the arrow. Only touches the renderer when the
        /// visibility actually changes, so PlayerMantling can call this every
        /// frame with its current state at no cost.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (visible == _isVisible) {
                return;
            }

            _isVisible = visible;
            _meshRenderer.enabled = visible;
        }

        /// <summary>
        /// Builds a flat arrow pointing up (+Y) in the local XY plane, facing
        /// -Z towards the camera it's parented under: a triangular head on
        /// top of a narrower rectangular shaft, centred on the origin.
        /// </summary>
        private static Mesh BuildArrowMesh(float size)
        {
            float halfWidth = size * 0.375f;
            float shaftHalfWidth = size * 0.125f;
            float top = size * 0.5f;
            float headBase = 0f;
            float bottom = -size * 0.5f;

            var vertices = new[] {
                // Head (triangle).
                new Vector3(0f, top, 0f),
                new Vector3(halfWidth, headBase, 0f),
                new Vector3(-halfWidth, headBase, 0f),

                // Shaft (quad).
                new Vector3(-shaftHalfWidth, headBase, 0f),
                new Vector3(shaftHalfWidth, headBase, 0f),
                new Vector3(shaftHalfWidth, bottom, 0f),
                new Vector3(-shaftHalfWidth, bottom, 0f)
            };

            // UI/Default renders both faces, so winding order doesn't matter.
            var triangles = new[] {
                0, 1, 2,
                3, 4, 5,
                3, 5, 6
            };

            var mesh = new Mesh { name = "MantleIndicatorArrow" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Builds an unlit material that ignores depth, so the arrow always
        /// draws over walls. Uses Unity's built-in UI/Default shader because
        /// it's unlit, double-sided, and already supports single-pass stereo
        /// VR rendering (a hand-written shader without the stereo macros
        /// would only draw in one eye). Its ZTest comes from the
        /// unity_GUIZTestMode property, which a material can override -
        /// setting it to Always disables the depth test for this material.
        /// </summary>
        private static Material BuildMaterial(Color color)
        {
            var material = new Material(Shader.Find("UI/Default")) {
                color = color,

                // Overlay queue, so it draws after all world geometry.
                renderQueue = (int)RenderQueue.Overlay
            };

            material.SetInt(_zTestModeId, (int)CompareFunction.Always);
            return material;
        }
    }
}
