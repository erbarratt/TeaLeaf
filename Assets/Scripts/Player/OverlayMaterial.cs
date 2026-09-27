using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// Builds materials for in-world UI markers (hand reticles, the mantle
    /// arrow): a flat colour, drawn after everything else and on top of it,
    /// in both eyes. One place for this so every UI marker renders the same
    /// way.
    ///
    /// Uses the project's own TeaLeaf/Overlay shader
    /// (Art/Shaders/Resources/Overlay.shader), which has the depth test
    /// switched off in the shader itself - see its header for why no
    /// built-in shader can do this. Being in a Resources folder, it's always
    /// in builds, so Shader.Find() works on the Quest too.
    /// </summary>
    public static class OverlayMaterial
    {
        private const string ShaderName = "TeaLeaf/Overlay";

        /// <summary>
        /// Creates a new overlay material in the given colour. Called once per
        /// marker, in Awake() - never per frame.
        /// </summary>
        public static Material Create(Color color)
        {
            Shader shader = Shader.Find(ShaderName);

            if (shader == null) {
                Debug.LogError($"OverlayMaterial: shader '{ShaderName}' not found - UI markers will be pink.");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }

            return new Material(shader) {
                color = color,

                // Overlay queue (4000): drawn after all opaque and transparent
                // world geometry. The shader asks for this already; set here
                // too so it's obvious from the code.
                renderQueue = (int)RenderQueue.Overlay
            };
        }
    }
}
