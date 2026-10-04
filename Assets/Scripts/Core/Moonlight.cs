using UnityEngine;

namespace Core
{
    /// <summary>
    /// The moon, for gameplay: the level's base light. Put it on the
    /// scene's Directional Light. Anywhere the moon can reach is at its
    /// light level; anywhere geometry stands between a point and the moon
    /// is in full shadow - indoors, under a roof, behind a wall. Nothing is
    /// placed by hand: the shadows are wherever the level's shape puts
    /// them, the same places the rendered shadows fall, because both come
    /// from the same light direction.
    ///
    /// It doesn't read the rendered shadows (those live on the graphics
    /// card, and fetching them back is slow). SceneLight asks the same
    /// question with one physics ray instead: from the point, towards the
    /// moon - does it hit anything?
    /// </summary>
    public class Moonlight : MonoBehaviour
    {
        // How bright moonlight is, where 1 is standing next to a torch.
        [SerializeField, Range(0f, 1f)] private float level = 0.4f;

        // How far towards the moon to look for something in the way, in
        // metres. Long enough to clear the tallest building from anywhere
        // in the level.
        [SerializeField] private float maxDistance = 200f;

        /// The scene's moon, or null if there isn't one (then nothing is
        /// moonlit: everywhere not near a LightSource is full shadow).
        public static Moonlight Instance { get; private set; }

        public float Level => level;
        public float MaxDistance => maxDistance;

        /// <summary>
        /// The direction from anywhere in the level towards the moon. A
        /// directional light shines along its forward axis, so the moon is
        /// the opposite way.
        /// </summary>
        public Vector3 DirectionToMoon => -transform.forward;

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) {
                Instance = null;
            }
        }
    }
}
