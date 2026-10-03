using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Editor helper that makes placeholder sounds from maths, so the sound
    /// system can be built and heard before any real audio exists: a few
    /// short .wav files (Assets/Audio/Placeholder) and a SoundCue asset for
    /// each kind (Assets/Data). They're ordinary assets - swap the clips in
    /// a cue for real recordings later and everything using the cue follows.
    ///
    /// Menu: TeaLeaf > Create Placeholder Sounds (also run by the sound test
    /// area builder). Existing files are kept, so tuning a cue in the
    /// Inspector survives; delete a file to have it made again.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class PlaceholderSounds
    {
        private const string ClipFolder = "Assets/Audio/Placeholder";
        private const string CueFolder = "Assets/Data";
        private const int SampleRate = 44100;

        public const string FootstepCuePath = CueFolder + "/FootstepPlaceholder.asset";
        public const string ImpactCuePath = CueFolder + "/ImpactPlaceholder.asset";

        /// <summary>
        /// Makes any placeholder clips and cues that don't exist yet.
        /// </summary>
        [MenuItem("TeaLeaf/Create Placeholder Sounds")]
        public static void Create()
        {
            Directory.CreateDirectory(ClipFolder);

            // Three footsteps and two impacts, each slightly different, so a
            // cue has variants to pick between.
            AudioClip[] footsteps = new AudioClip[3];

            for (int i = 0; i < footsteps.Length; i++) {
                footsteps[i] = EnsureClip($"{ClipFolder}/FootstepPlaceholder{i + 1}.wav", Footstep(i));
            }

            AudioClip[] impacts = new AudioClip[2];

            for (int i = 0; i < impacts.Length; i++) {
                impacts[i] = EnsureClip($"{ClipFolder}/ImpactPlaceholder{i + 1}.wav", Impact(i));
            }

            EnsureCue(FootstepCuePath, footsteps, 0.8f, 25f, NoiseType.Footstep, 6f);
            EnsureCue(ImpactCuePath, impacts, 1f, 35f, NoiseType.Impact, 10f);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// A footstep: a soft thud. A low tone and some hiss, both dying
        /// away within about a tenth of a second. The hiss is what a muffled
        /// version loses, so the difference can be heard.
        /// </summary>
        private static float[] Footstep(int variant)
        {
            float[] samples = new float[(int)(SampleRate * 0.15f)];
            System.Random random = new(100 + variant);
            float tone = 75f + variant * 12f;
            float smoothed = 0f;

            for (int i = 0; i < samples.Length; i++) {
                float time = i / (float)SampleRate;
                float hiss = (float)(random.NextDouble() * 2.0 - 1.0);

                // Averaging each hiss sample with the ones before it takes
                // the sharpness off: a duller, heavier rustle.
                smoothed += (hiss - smoothed) * 0.2f;

                // exp(-time * rate) starts at 1 and falls away smoothly; a
                // bigger rate dies faster.
                float thud = Mathf.Sin(2f * Mathf.PI * tone * time) * Mathf.Exp(-time * 35f);
                float scuff = (smoothed * 0.9f + hiss * 0.25f) * Mathf.Exp(-time * 45f);
                samples[i] = thud * 0.7f + scuff * 0.5f;
            }

            return samples;
        }

        /// <summary>
        /// An impact: a knock. A sharp click of hiss, then a tone that
        /// drops slightly in pitch as it rings out over a third of a second.
        /// </summary>
        private static float[] Impact(int variant)
        {
            float[] samples = new float[(int)(SampleRate * 0.35f)];
            System.Random random = new(200 + variant);
            float tone = 190f + variant * 40f;

            for (int i = 0; i < samples.Length; i++) {
                float time = i / (float)SampleRate;
                float hiss = (float)(random.NextDouble() * 2.0 - 1.0);
                float click = hiss * Mathf.Exp(-time * 90f);
                float ring = Mathf.Sin(2f * Mathf.PI * tone * time * (1f - time * 0.4f)) * Mathf.Exp(-time * 14f);
                samples[i] = click * 0.6f + ring * 0.7f;
            }

            return samples;
        }

        /// <summary>
        /// Loads the clip at path, writing and importing the .wav first if
        /// it isn't there.
        /// </summary>
        private static AudioClip EnsureClip(string path, float[] samples)
        {
            if (!File.Exists(path)) {
                WriteWav(path, samples);

                // Has Unity notice the new file (and its new folder) and
                // import it straight away, so it can be loaded as a clip.
                AssetDatabase.Refresh();
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        /// <summary>
        /// Creates the SoundCue asset at path if it isn't there. Its fields
        /// are private and serialized, so they're set the way the Inspector
        /// would set them.
        /// </summary>
        private static void EnsureCue(string path, AudioClip[] clips, float volume, float audibleRange, NoiseType noiseType, float noiseRadius)
        {
            if (AssetDatabase.LoadAssetAtPath<SoundCue>(path) != null) {
                return;
            }

            SoundCue cue = ScriptableObject.CreateInstance<SoundCue>();
            SerializedObject serialized = new(cue);

            SerializedProperty clipList = serialized.FindProperty("clips");
            clipList.arraySize = clips.Length;

            for (int i = 0; i < clips.Length; i++) {
                clipList.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }

            serialized.FindProperty("volume").floatValue = volume;
            serialized.FindProperty("audibleRange").floatValue = audibleRange;
            serialized.FindProperty("noiseType").enumValueIndex = (int)noiseType;
            serialized.FindProperty("noiseRadius").floatValue = noiseRadius;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(cue, path);
        }

        /// <summary>
        /// Writes samples (-1 to 1) as a mono 16-bit .wav file: a 44-byte
        /// header describing the format, then each sample as a whole number
        /// from -32767 to 32767.
        /// </summary>
        private static void WriteWav(string path, float[] samples)
        {
            const short channels = 1;
            const short bytesPerSample = 2;
            int dataSize = samples.Length * bytesPerSample;

            using FileStream stream = new(path, FileMode.Create);
            using BinaryWriter writer = new(stream);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * channels * bytesPerSample);
            writer.Write((short)(channels * bytesPerSample));
            writer.Write((short)(bytesPerSample * 8));
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            for (int i = 0; i < samples.Length; i++) {
                writer.Write((short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f));
            }
        }
    }
}
