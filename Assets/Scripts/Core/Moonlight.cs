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
        [Tooltip("Gameplay brightness of moonlight (0-1), where 1 = standing next to a torch, e.g. 0.4 = a player standing in moonlight is fairly visible, crouched less so. Higher makes moonlit ground more dangerous; 0 makes it as safe as shadow.")]
        [SerializeField, Range(0f, 1f)] private float level = 0.4f;

        // How far towards the moon to look for something in the way, in
        // metres. Long enough to clear the tallest building from anywhere
        // in the level.
        [Tooltip("How far the ray towards the moon looks for something in the way, in metres, e.g. 200 = clears the tallest building from anywhere in the level. Too low and points under a distant roof read as moonlit; higher costs a little more per ray.")]
        [SerializeField] private float maxDistance = 200f;

        // The least light any baked surface receives, so deep shadow keeps
        // its detail: a linear light level (not a pixel value - the surface's
        // own colour still multiplies it), in the moon's blue-grey. Applied
        // to the lightmaps (and any legacy light probes) only when the
        // editor's ShadowFloor is run from its menu, which reads it from
        // here; the rendered look only, the gameplay light above is unaffected.
        [Tooltip("The least light a baked surface gets when TeaLeaf > Lighting > Apply Shadow Floor is run by hand (linear, before the surface's colour), e.g. (0.045, 0.05, 0.06) = deep shadow stays a faint moon blue-grey instead of black. Never applied by itself after a bake; brighter lifts the darkest shade, black turns it off. Rendered look only, not gameplay light.")]
        [SerializeField, ColorUsage(false, true)] private Color shadowFloor = new Color(0.045f, 0.05f, 0.06f, 1f);

        /// The scene's moon, or null if there isn't one (then nothing is
        /// moonlit: everywhere not near a LightSource is full shadow).
        public static Moonlight Instance { get; private set; }

        public float Level => level;
        public float MaxDistance => maxDistance;
        public Color ShadowFloor => shadowFloor;

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
