using UnityEngine;

namespace Player
{
    /// <summary>
    /// The light gem in the middle of the compass: dark when the player
    /// is hidden, glowing when they're in plain sight (PlayerVisibility),
    /// a mix in between. The material is URP Lit, so the gem itself is lit
    /// like the rest of the compass; the reading is its emission, which
    /// the light around never darkens - it shows as well in shadow as in
    /// torchlight. The material must have Emission ticked (its colour is
    /// set here). Written through a MaterialPropertyBlock, so the material
    /// asset is shared and untouched, and only when the reading has
    /// changed enough to see.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class CompassGem : MonoBehaviour
    {
        // The gem's glow when the player is hidden and when they're in
        // plain sight; in between, a mix. Added on top of the lit gem.
        [Tooltip("The gem's glow when the player is fully hidden (Visibility 0), e.g. black = no glow, just the gem as the light around shows it; it blends towards Light Color as they become visible. Added on top of the lit gem, never darkened by shadow.")]
        [ColorUsage(false, true)]
        [SerializeField] private Color darkColor = Color.black;
        [Tooltip("The gem's glow when the player is in plain sight (Visibility 1), e.g. a warm yellow. Brighter (HDR intensity above 0) = a stronger glow; lower = subtler. Added on top of the lit gem, never darkened by shadow.")]
        [ColorUsage(false, true)]
        [SerializeField] private Color lightColor = new(1f, 0.95f, 0.6f, 1f);

        // How far the reading must change before the colour is written
        // again.
        private const float Step = 0.02f;

        private static readonly int _emissionColorId = Shader.PropertyToID("_EmissionColor");

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;

        // The reading as last written (-1 = never).
        private float _shown = -1f;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();
            Show(0f);
        }

        /// <summary>
        /// Sets the gem's glow for how visible the player is, 0 (hidden)
        /// to 1 (plain sight).
        /// </summary>
        public void Show(float visibility)
        {
            if (Mathf.Abs(visibility - _shown) < Step) {
                return;
            }

            _shown = visibility;
            _block.SetColor(_emissionColorId, Color.Lerp(darkColor, lightColor, visibility));
            _renderer.SetPropertyBlock(_block);
        }
    }
}
