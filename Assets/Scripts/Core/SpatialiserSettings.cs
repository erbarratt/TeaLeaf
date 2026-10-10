using System;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// The numbers behind the project's own spatialiser (SpatialVoice): how
    /// strongly each of the cues the ear uses for direction is applied.
    /// One set, on the SoundPlayer, shared by every voice - and read every
    /// frame, so they can be tuned by ear in Play Mode.
    ///
    /// The cues, in order of how much they matter:
    /// - A sound to one side reaches the far ear a fraction of a
    ///   millisecond late (the time difference).
    /// - The head is in the way of the far ear, so it's quieter there and
    ///   has lost its high end (the head shadow).
    /// - A sound from behind is a little duller in both ears, because the
    ///   outer ears face forwards.
    /// There is no cue for up and down: that one comes from the detailed
    /// shape of the ear, and takes measured data to imitate.
    /// </summary>
    [Serializable]
    public class SpatialiserSettings
    {
        // Overall volume of spatialised sounds, to match them to what
        // they were under Unity's own panning.
        [SerializeField, Range(0f, 2f)] private float volume = 1f;

        [Header("Time difference between the ears")]
        // The head's radius in metres: it sets how late the far ear
        // hears a sound. 0.0875 is an average adult.
        [SerializeField, Range(0.05f, 0.12f)] private float headRadius = 0.0875f;

        // Multiplies the delay: 1 = real, 0 = none.
        [SerializeField, Range(0f, 2f)] private float timeDifference = 1f;

        [Header("Head shadow (the far ear)")]
        // The far ear's volume with the sound fully to one side.
        [SerializeField, Range(0f, 1f)] private float farEarVolume = 0.7f;

        // The near ear's volume with the sound fully to one side: a
        // little over 1, so a sound doesn't seem to drop as it moves
        // from in front to beside.
        [SerializeField, Range(1f, 1.5f)] private float nearEarVolume = 1.15f;

        // Frequencies above this are rolled off in the far ear, in Hz.
        [SerializeField, Range(300f, 8000f)] private float shadowCutoff = 1500f;

        // How much of that roll-off is applied with the sound fully to
        // one side: 0 = none, 1 = all.
        [SerializeField, Range(0f, 1f)] private float shadowAmount = 0.85f;

        [Header("Behind")]
        // Frequencies above this are rolled off for a sound behind, in Hz.
        [SerializeField, Range(1000f, 12000f)] private float rearCutoff = 3500f;

        // How much of that roll-off is applied directly behind.
        [SerializeField, Range(0f, 1f)] private float rearAmount = 0.6f;

        // The volume directly behind.
        [SerializeField, Range(0.5f, 1f)] private float rearVolume = 0.9f;

        public float Volume => volume;
        public float HeadRadius => headRadius;
        public float TimeDifference => timeDifference;
        public float FarEarVolume => farEarVolume;
        public float NearEarVolume => nearEarVolume;
        public float ShadowAmount => shadowAmount;
        public float RearAmount => rearAmount;
        public float RearVolume => rearVolume;

        /// The audio output's sample rate, in samples per second.
        public int SampleRate { get; private set; }

        /// The two roll-offs as the per-sample number their filters use.
        public float ShadowCoefficient { get; private set; }
        public float RearCoefficient { get; private set; }

        /// <summary>
        /// Works out the values every voice shares. Called once a frame by
        /// the SoundPlayer, before it updates the voices, so a change in
        /// the Inspector is heard at once.
        /// </summary>
        public void Prepare(int sampleRate)
        {
            SampleRate = sampleRate;
            ShadowCoefficient = Coefficient(shadowCutoff, sampleRate);
            RearCoefficient = Coefficient(rearCutoff, sampleRate);
        }

        /// <summary>
        /// The simplest roll-off filter moves its output a fraction of the
        /// way towards each new sample: a small fraction follows slowly
        /// (only low frequencies get through), a fraction of 1 follows
        /// exactly. This is that fraction for a cutoff frequency in Hz.
        /// </summary>
        private static float Coefficient(float cutoff, int sampleRate)
        {
            return 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sampleRate);
        }
    }
}
