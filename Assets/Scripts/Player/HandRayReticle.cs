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

        private void Awake()
        {
            MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = BuildCircleMesh(radius, segments);

            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _meshRenderer.material = BuildMaterial(color);
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.enabled = false;
        }

        /// <summary>
        /// Shows the reticle at worldPoint facing towards viewerPosition (the
        /// player's head) if active, otherwise hides it. Called once per
        /// frame from PlayerHandInteraction.Tick(), for both hands.
        /// </summary>
        public void Tick(bool active, Vector3 worldPoint, Vector3 viewerPosition)
        {
            _meshRenderer.enabled = active;

            if (!active) {
                return;
            }

            transform.position = worldPoint;

            // Face the disc's front (local +Z) towards the viewer, rather
            // than along the ray direction, so it always reads as a flat
            // circle rather than foreshortening as the ray's angle changes.
            transform.rotation = Quaternion.LookRotation(viewerPosition - worldPoint);
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

        /// <summary>
        /// Builds the reticle's material. Sprites/Default is unlit,
        /// alpha-blended, and double-sided (Cull Off) by default - exactly
        /// what a simple billboard marker needs, without having to configure
        /// URP's Lit/Unlit transparent surface keywords by hand.
        /// </summary>
        private static Material BuildMaterial(Color color)
        {
            return new Material(Shader.Find("Sprites/Default")) {
                color = color,

                // Queue 3100 - just after ClimbableLedgeMat's queue of 3000 -
                // so the reticle reliably draws on top of the highlighted
                // edge's translucent surface instead of z-fighting with it.
                renderQueue = 3100
            };
        }
    }
}
