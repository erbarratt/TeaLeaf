using UnityEngine;

namespace Core
{
    /// <summary>
    /// Makes a fire's visible light waver like a flame: its intensity
    /// wanders up and down a little round the value set in the editor,
    /// driven by smooth noise. Only the Unity Light moves - the gameplay
    /// light guards see by (LightSource) stays steady, so detection never
    /// jitters.
    ///
    /// Works because the light is Mixed and the lighting mode is
    /// Shadowmask: the light's direct part is computed every frame, so a
    /// change of intensity shows on everything, static and moving, with
    /// no rebake. A Baked light contributes nothing at runtime, so on one
    /// of those this switches itself off and costs nothing.
    ///
    /// Each flame walks its own path through the noise, offset by a hash
    /// of where it stands, so neighbouring lamps never flicker in step.
    /// Put it on the fire's object beside its Flame, which pauses it while
    /// the fire is out.
    /// </summary>
    public class FlameFlicker : MonoBehaviour
    {
        // The light that flickers. Found on this object if left empty.
        [SerializeField] private Light flameLight;

        // How far the intensity swings either side of its set value, as a
        // fraction of it: 0.15 is a lantern in still air, 0.4 a torch in
        // the wind.
        [SerializeField, Range(0f, 1f)] private float amount = 0.15f;

        // How quickly it wavers. Higher is a more nervous flame.
        [SerializeField] private float speed = 6f;

        // The intensity set in the editor, which the flicker is centred on.
        private float _baseIntensity;

        // Where in the noise this flame starts, so no two match.
        private float _seedX;
        private float _seedY;

        /// <summary>
        /// Editor-only: fills in the light when the component is added.
        /// </summary>
        private void Reset()
        {
            flameLight = GetComponent<Light>();
        }

        private void Awake()
        {
            if (flameLight == null) {
                flameLight = GetComponent<Light>();
            }

            if (flameLight == null) {
                enabled = false;
                return;
            }

            _baseIntensity = flameLight.intensity;

            // A fully baked light is a picture in the lightmap: changing
            // its intensity at runtime shows nothing, so don't bother.
            if (flameLight.bakingOutput.lightmapBakeType == LightmapBakeType.Baked) {
                enabled = false;
                return;
            }

            // Hash the world position into a start point in the noise.
            // Perlin noise is sampled at these plus time, and loses
            // precision at large coordinates, so keep them small.
            Vector3 p = transform.position;
            float hash = Mathf.Sin(p.x * 12.9898f + p.y * 78.233f + p.z * 37.719f) * 43758.5453f;
            hash -= Mathf.Floor(hash);
            _seedX = hash * 128f;
            _seedY = (hash * 7.31f) % 1f * 128f;
        }

        private void Update()
        {
            float t = Time.time * speed + _seedX;

            // Two octaves: a slow wander with a quicker tremble on top.
            // PerlinNoise returns roughly 0..1 centred on 0.5.
            float noise = 0.65f * Mathf.PerlinNoise(t, _seedY)
                        + 0.35f * Mathf.PerlinNoise(t * 2.7f, _seedY + 17f);

            flameLight.intensity = _baseIntensity * (1f + (noise - 0.5f) * 2f * amount);
        }

        /// <summary>
        /// Puts the intensity back where the editor set it, so a flame
        /// relit after a pause doesn't start mid-flicker.
        /// </summary>
        private void OnDisable()
        {
            if (flameLight != null) {
                flameLight.intensity = _baseIntensity;
            }
        }
    }
}
