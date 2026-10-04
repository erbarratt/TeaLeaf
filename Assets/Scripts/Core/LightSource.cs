using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// A light that makes things visible: a torch, a lamp, a lit window.
    /// Place it where the flame is and set how far it reaches; what it
    /// lights is worked out from the level's shape (SceneLight), so a wall
    /// or a closed door between the light and a point leaves that point in
    /// the dark.
    ///
    /// This is the gameplay light only. The light you see is an ordinary
    /// Unity Light on the same object, set up to look about the same size.
    ///
    /// Disabling the component puts the light out.
    /// </summary>
    public class LightSource : MonoBehaviour, IDebugDrawable
    {
        // How bright it is close up: 1 = as bright as light gets.
        [SerializeField, Range(0f, 1f)] private float level = 1f;

        // How far the light reaches, in metres. Beyond this it adds nothing.
        [SerializeField] private float range = 6f;

        // The fraction of the range that is at full brightness. From there
        // the light fades in a straight line to nothing at the range.
        [SerializeField, Range(0f, 1f)] private float fullBrightnessFraction = 0.5f;

        // Every enabled light source in the scene. Sources add and remove
        // themselves, so nothing ever searches the scene for them.
        private static readonly List<LightSource> _sources = new();

        private static readonly Color _gizmoColor = new(1f, 0.6f, 0.15f, 0.6f);
        private static readonly Color _gizmoSelectedColor = new(1f, 0.6f, 0.15f, 1f);

        /// How many light sources are lit.
        public static int Count => _sources.Count;

        public Vector3 Position => transform.position;
        public float Range => range;

        /// <summary>
        /// The lit source at index - read with Count in a for loop.
        /// </summary>
        public static LightSource Get(int index)
        {
            return _sources[index];
        }

        private void OnEnable()
        {
            _sources.Add(this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            _sources.Remove(this);
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// How bright this light is at distance metres from it, with
        /// nothing in the way: level out to the full-brightness distance,
        /// then fading to 0 at the range.
        /// </summary>
        public float LevelAtDistance(float distance)
        {
            float fullDistance = range * fullBrightnessFraction;

            // InverseLerp(a, b, x) is how far x is from a to b, as 0-1,
            // clamped: 1 at the full-brightness distance, 0 at the range.
            return level * Mathf.InverseLerp(range, fullDistance, distance);
        }

        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// The range as a wire sphere, and when detailed the full-brightness
        /// distance as a second one inside it. Shared by the Scene view
        /// gizmos and the in-headset view (InHeadsetGizmos).
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            lines.Color = detailed ? _gizmoSelectedColor : _gizmoColor;
            lines.WireSphere(transform.position, range);

            if (detailed) {
                lines.WireSphere(transform.position, range * fullBrightnessFraction);
            }
        }
    }
}
