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
        [SerializeField] private AudioClip[] clips;

        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        // Each play shifts the pitch at random by up to this much either
        // way (0.05 = 5%), for the same reason as the several clips.
        [SerializeField, Range(0f, 0.5f)] private float pitchVariation = 0.05f;

        // How far away the player can hear it, in metres. Beyond this it
        // isn't played at all.
        [SerializeField] private float audibleRange = 15f;

        [Header("Noise (what guards hear)")]
        [SerializeField] private NoiseType noiseType = NoiseType.Impact;

        // How far the noise carries to guards, in metres. 0 = guards never
        // hear this sound (ambience, UI). Emitters may scale it, e.g.
        // footsteps by stance.
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
