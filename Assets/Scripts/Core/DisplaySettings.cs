using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Core
{
    /// <summary>
    /// The player's display settings, as Thief VR's: brightness and gamma,
    /// so a player whose headset shows deep shadow too black can read it.
    /// Brightness scales the whole picture (URP Color Adjustments' Post
    /// Exposure, in photographic stops: lamps brighten with the darks).
    /// Gamma brightens (or darkens) the midtones and darks while black and
    /// white stay where they are (URP Lift Gamma Gain's gamma). Both on one
    /// global Volume - the one way that treats everything on screen alike
    /// (pack materials, guards, hands, sky).
    ///
    /// Costs nothing at the defaults: while both are 0 the camera's
    /// post-processing is switched off, so the full-screen pass only runs
    /// while the player uses a setting. Post-processing is never depended on.
    ///
    /// On the player's Main Camera (with the Volume it requires). The profile
    /// is made once in Awake() - no asset - holding only the two overrides.
    /// Works in Play Mode only; the editor's views show the picture without it.
    /// </summary>
    [RequireComponent(typeof(Camera), typeof(Volume))]
    public class DisplaySettings : MonoBehaviour
    {
        // Brightness, in stops (each stop doubles or halves the picture).
        [Tooltip("Brightness, -2 to 2 stops, e.g. 0.5 = the whole picture about 1.4x brighter, lamps included. 0 = off (no cost while gamma is 0 too); higher brightens everything, lower darkens it; each stop doubles or halves. Live in Play Mode.")]
        [SerializeField, Range(-2f, 2f)] private float brightness;

        // The gamma trackball's offset (gamma.w), which URP applies as a
        // power curve on the picture between black and white.
        [Tooltip("Display gamma, -1 to 1, e.g. 0.3 = darks and midtones a little brighter, black and white unchanged. 0 = off (no cost while brightness is 0 too); higher brightens the darks more, lower darkens them. Live in Play Mode.")]
        [SerializeField, Range(-1f, 1f)] private float gamma;

        private Volume _volume;
        private VolumeProfile _profile;
        private ColorAdjustments _colorAdjustments;
        private LiftGammaGain _liftGammaGain;
        private UniversalAdditionalCameraData _cameraData;

        /// <summary>
        /// Brightness in stops, -2 to 2 (0 = off); setting it applies it at
        /// once. For a settings menu to set.
        /// </summary>
        public float Brightness
        {
            get => brightness;
            set
            {
                brightness = Mathf.Clamp(value, -2f, 2f);
                Apply();
            }
        }

        /// <summary>
        /// Gamma, -1 to 1 (0 = off); setting it applies it at once. For a
        /// settings menu to set.
        /// </summary>
        public float Gamma
        {
            get => gamma;
            set
            {
                gamma = Mathf.Clamp(value, -1f, 1f);
                Apply();
            }
        }

        /// <summary>
        /// Makes the profile with its two overrides and gives it to the
        /// Volume, made global (it is the only one, and covers everything).
        /// </summary>
        private void Awake()
        {
            _cameraData = GetComponent<Camera>().GetUniversalAdditionalCameraData();
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Display Settings (runtime)";
            _colorAdjustments = _profile.Add<ColorAdjustments>();
            _colorAdjustments.postExposure.overrideState = true;
            _liftGammaGain = _profile.Add<LiftGammaGain>();
            _liftGammaGain.gamma.overrideState = true;
            _volume = GetComponent<Volume>();
            _volume.isGlobal = true;
            _volume.sharedProfile = _profile;
        }

        private void OnEnable()
        {
            Apply();
        }

        /// <summary>
        /// Switched off = no adjustment: post-processing off with it.
        /// </summary>
        private void OnDisable()
        {
            if (_cameraData != null) {
                _cameraData.renderPostProcessing = false;
            }
        }

        private void OnDestroy()
        {
            Destroy(_colorAdjustments);
            Destroy(_liftGammaGain);
            Destroy(_profile);
        }

        /// <summary>
        /// An Inspector change in Play Mode applies at once (before Awake()
        /// there is nothing to apply to).
        /// </summary>
        private void OnValidate()
        {
            if (_liftGammaGain != null && isActiveAndEnabled) {
                Apply();
            }
        }

        /// <summary>
        /// Writes the settings into the overrides and turns post-processing
        /// on only while either is away from 0. Runs on a change, never per
        /// frame.
        /// </summary>
        private void Apply()
        {
            if (_liftGammaGain == null) {
                return;
            }
            _colorAdjustments.postExposure.value = brightness;
            // The trackball's colour stays neutral white; w is the offset.
            _liftGammaGain.gamma.value = new Vector4(1f, 1f, 1f, gamma);
            _cameraData.renderPostProcessing = !Mathf.Approximately(brightness, 0f) || !Mathf.Approximately(gamma, 0f);
        }
    }
}
