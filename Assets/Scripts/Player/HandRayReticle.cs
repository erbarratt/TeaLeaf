using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// A small circular marker that always faces the camera (a billboard),
    /// shown wherever a hand's interaction ray currently hits something.
    ///
    /// Builds its own mesh and material at runtime in Awake() rather than
    /// needing a mesh/material asset - it's a simple flat white disc with
    /// nothing designer-facing beyond radius/colour, so there's nothing an
    /// asset would give us that a few lines of code don't.
    ///
    /// This class does not run its own Update(). PlayerHandInteraction owns
    /// one of these per hand and calls Tick() on it every frame with
    /// wherever (if anywhere) that hand's ray is currently hitting,
    /// following the same explicit-Tick() pattern as the rest of the Player
    /// scripts.
    /// </summary>
    public class HandRayReticle : MonoBehaviour
    {
        [SerializeField] private float radius = 0.02f;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private int segments = 16;

        private MeshRenderer _meshRenderer;

        // Cached rather than read through the transform property each frame -
        // that's a call into the engine every time, for a value that never
        // changes.
        private Transform _transform;

        // Whether the renderer is currently enabled, so Tick() only touches
        // it when visibility actually changes - see SetVisible().
        private bool _isVisible;

        private void Awake()
        {
            _transform = transform;

            MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = BuildCircleMesh(radius, segments);

            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            // UI: drawn last and on top of everything, like the mantle arrow.
            _meshRenderer.material = OverlayMaterial.Create(color);
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.enabled = false;
        }

        /// <summary>
        /// Shows the reticle at worldPoint facing towards viewerPosition (the
        /// player's head) if active, otherwise hides it. Called once per
        /// frame from PlayerHandInteraction.TickReticles(), for both hands.
        /// </summary>
        public void Tick(bool active, Vector3 worldPoint, Vector3 viewerPosition)
        {
            // The reticle lives under its hand's controller object, which
            // XRI's XR Input Modality Manager (on Player) deactivates
            // whenever that controller isn't tracked - including at scene
            // start, in its OnEnable, before this object's Awake() has run.
            // Awake() only runs once the controller is first tracked, so
            // until then _meshRenderer doesn't exist yet (PlayerController
            // still ticks this every frame). Skip the frame:
            // an untracked hand has nothing to show a reticle for anyway.
            if (!isActiveAndEnabled) {
                return;
            }

            SetVisible(active);

            if (!active) {
                return;
            }

            // Face the disc's front (local +Z) towards the viewer, rather
            // than along the ray direction, so it always reads as a flat
            // circle rather than foreshortening as the ray's angle changes.
            // Position and rotation are set together: setting them one at a
            // time makes Unity update the transform (and notify anything
            // listening for transform changes, like the renderer's bounds)
            // twice.
            _transform.SetPositionAndRotation(
                worldPoint,
                Quaternion.LookRotation(viewerPosition - worldPoint));
        }

        /// <summary>
        /// Shows or hides the disc. Only touches the renderer when the
        /// visibility actually changes, like MantleIndicator.SetVisible(),
        /// since writing renderer state every frame isn't free even when the
        /// value is the same.
        /// </summary>
        private void SetVisible(bool visible)
        {
            if (visible == _isVisible) {
                return;
            }

            _isVisible = visible;
            _meshRenderer.enabled = visible;
        }

        /// <summary>
        /// Builds a flat, unlit disc mesh of the given radius in the local
        /// XY plane, as a triangle fan around the origin.
        /// </summary>
        private static Mesh BuildCircleMesh(float radius, int segments)
        {
            var vertices = new Vector3[segments + 1];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < segments; i++) {
                float angle = i / (float)segments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            }

            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++) {
                int next = (i + 1) % segments;
                triangles[i * 3 + 0] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = next + 1;
            }

            var mesh = new Mesh { name = "HandRayReticle" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

    }
}
