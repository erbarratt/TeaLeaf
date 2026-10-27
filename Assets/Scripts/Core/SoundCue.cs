using UnityEngine;

namespace Core
{
    /// <summary>
    /// One kind of sound, as an asset: "footstep on wood", "crate impact",
    /// "door creak". It holds both halves of the sound - the audio the
    /// player hears and the noise guards hear - so the two are tuned in one
    /// place and can't drift apart.
    ///
    /// A ScriptableObject: data saved as a file in the project rather than
    /// on a scene object, so many emitters share one cue. Create one with
    /// Assets > Create > TeaLeaf > Sound Cue.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundCue", menuName = "TeaLeaf/Sound Cue")]
    public class SoundCue : ScriptableObject
    {
        [Header("Audio (what the player hears)")]
        // One is picked at random each time, so repeats don't sound
        // identical.
        [Tooltip("The clips this cue can play; each entry is one take, and one is picked at random each time, e.g. four wooden footstep recordings so repeats don't sound identical. Empty = nothing is heard (the noise half still works).")]
        [SerializeField] private AudioClip[] clips;

        [Tooltip("Loudness of the clips (0-1), e.g. 0.5 = half as loud as recorded. Higher = louder; also sets how strongly the compass shows it.")]
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        // Each play shifts the pitch at random by up to this much either
        // way (0.05 = 5%), for the same reason as the several clips.
        [Tooltip("Each play shifts the pitch at random by up to this fraction either way, e.g. 0.05 = up to 5% higher or lower, so repeats don't sound identical. Higher = more varied; 0 = always the same pitch.")]
        [SerializeField, Range(0f, 0.5f)] private float pitchVariation = 0.05f;

        // How far away the player can hear it, in metres. Beyond this it
        // isn't played at all.
        [Tooltip("How far the player can hear it, in metres along the route through rooms and portals, e.g. 15 = silent once the path is longer than 15m. Higher carries further; it fades to nothing at this distance. Separate from Noise Radius.")]
        [SerializeField] private float audibleRange = 15f;

        [Header("Noise (what guards hear)")]
        [Tooltip("What kind of noise guards are told it is, e.g. Footstep for steps, Impact for a dropped prop, Mechanism for doors and locks, Distraction for a noisemaker.")]
        [SerializeField] private NoiseType noiseType = NoiseType.Impact;

        // How far the noise carries to guards, in metres. 0 = guards never
        // hear this sound (ambience, UI). Emitters may scale it, e.g.
        // footsteps by stance.
        [Tooltip("How far the noise carries to guards, in metres along the route through rooms and portals, e.g. 8 = a guard within 8m of path hears it, louder the closer. Higher = more dangerous; 0 = guards never hear it (ambience, UI). Emitters may scale it, e.g. footsteps by stance.")]
        [SerializeField] private float noiseRadius = 8f;

        public float Volume => volume;
        public float PitchVariation => pitchVariation;
        public float AudibleRange => audibleRange;
        public NoiseType NoiseType => noiseType;
        public float NoiseRadius => noiseRadius;
        public int ClipCount => clips != null ? clips.Length : 0;

        /// <summary>
        /// The clip at index - read with ClipCount.
        /// </summary>
        public AudioClip GetClip(int index)
        {
            return clips[index];
        }

        /// <summary>
        /// Emits this cue's noise for guards, with its radius multiplied by
        /// radiusScale (1 = as authored). Does nothing for a cue with no
        /// noise radius.
        /// </summary>
        public void EmitNoise(Vector3 position, Transform source, float radiusScale = 1f)
        {
            NoiseSystem.Emit(position, noiseRadius * radiusScale, noiseType, source);
        }
    }
}
