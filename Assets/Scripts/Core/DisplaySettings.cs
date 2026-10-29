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
    /// is made on enable - no asset, never saved - holding only the two
    /// overrides, and handed to the Volume as its runtime profile (not the
    /// serialized shared one), so nothing in the scene or prefab changes.
    ///
    /// Runs in Edit Mode too, so the Scene view previews both settings (with
    /// its Effects > Post Processing ticked). The camera's post-processing
    /// switch is only touched in Play Mode: it is saved with the camera.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera), typeof(Volume))]
    public class DisplaySettings : MonoBehaviour
    {
        // Brightness, in stops (each stop doubles or halves the picture).
        [Tooltip("Brightness, -2 to 2 stops, e.g. 0.5 = the whole picture about 1.4x brighter, lamps included. 0 = off (no cost while gamma is 0 too); higher brightens everything, lower darkens it; each stop doubles or halves. Live in Edit Mode (Scene view, Effects > Post Processing ticked) and Play Mode.")]
        [SerializeField, Range(-2f, 2f)] private float brightness;

        // The gamma trackball's offset (gamma.w), which URP applies as a
        // power curve on the picture between black and white.
        [Tooltip("Display gamma, -1 to 1, e.g. 0.3 = darks and midtones a little brighter, black and white unchanged. 0 = off (no cost while brightness is 0 too); higher brightens the darks more, lower darkens them. Live in Edit Mode (Scene view, Effects > Post Processing ticked) and Play Mode.")]
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
        /// Made here rather than in Awake(): in Edit Mode a script reload
        /// calls OnDisable() then OnEnable() but never Awake() again.
        /// </summary>
        private void OnEnable()
        {
            _cameraData = GetComponent<Camera>().GetUniversalAdditionalCameraData();
            // DontSave: in Edit Mode the scene never tries to save them.
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Display Settings (runtime)";
            _profile.hideFlags = HideFlags.DontSave;
            _colorAdjustments = _profile.Add<ColorAdjustments>();
            _colorAdjustments.hideFlags = HideFlags.DontSave;
            _colorAdjustments.postExposure.overrideState = true;
            _liftGammaGain = _profile.Add<LiftGammaGain>();
            _liftGammaGain.hideFlags = HideFlags.DontSave;
            _liftGammaGain.gamma.overrideState = true;
            _volume = GetComponent<Volume>();
            _volume.isGlobal = true;
            // The runtime profile, not sharedProfile: it isn't serialized, so
            // the Edit Mode preview leaves no change on the scene or prefab.
            _volume.profile = _profile;
            Apply();
        }

        /// <summary>
        /// Switched off = no adjustment: the profile goes, and in Play Mode
        /// post-processing is turned off with it.
        /// </summary>
        private void OnDisable()
        {
            if (_volume != null) {
                _volume.profile = null;
            }
            if (Application.isPlaying && _cameraData != null) {
                _cameraData.renderPostProcessing = false;
            }
            DestroyMade(_colorAdjustments);
            DestroyMade(_liftGammaGain);
            DestroyMade(_profile);
            _colorAdjustments = null;
            _liftGammaGain = null;
            _profile = null;
        }

        /// <summary>
        /// Destroys an object this made: DestroyImmediate in Edit Mode, where
        /// Destroy isn't allowed.
        /// </summary>
        private static void DestroyMade(Object made)
        {
            if (made == null) {
                return;
            }
            if (Application.isPlaying) {
                Destroy(made);
            } else {
                DestroyImmediate(made);
            }
        }

        /// <summary>
        /// An Inspector change applies at once, in Edit Mode or Play Mode
        /// (before OnEnable() there is nothing to apply to).
        /// </summary>
        private void OnValidate()
        {
            if (_liftGammaGain != null && isActiveAndEnabled) {
                Apply();
            }
        }

        /// <summary>
        /// Writes the settings into the overrides and, in Play Mode, turns
        /// post-processing on only while either is away from 0. Runs on a
        /// change, never per frame.
        /// </summary>
        private void Apply()
        {
            if (_liftGammaGain == null) {
                return;
            }
            _colorAdjustments.postExposure.value = brightness;
            // The trackball's colour stays neutral white; w is the offset.
            _liftGammaGain.gamma.value = new Vector4(1f, 1f, 1f, gamma);
            // Not in Edit Mode: the switch is saved with the camera, and the
            // Scene view has its own (Effects > Post Processing).
            if (Application.isPlaying) {
                _cameraData.renderPostProcessing = !Mathf.Approximately(brightness, 0f) || !Mathf.Approximately(gamma, 0f);
            }
        }
    }
}
