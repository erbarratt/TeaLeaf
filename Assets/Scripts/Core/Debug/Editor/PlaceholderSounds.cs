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
        public const string SurfaceSoundsPath = CueFolder + "/SurfaceSoundsPlaceholder.asset";
        public const string CreakCuePath = CueFolder + "/CreakPlaceholder.asset";
        public const string TorchLoopCuePath = CueFolder + "/TorchLoopPlaceholder.asset";
        public const string OutsideAmbienceCuePath = CueFolder + "/AmbienceOutsidePlaceholder.asset";
        public const string IndoorAmbienceCuePath = CueFolder + "/AmbienceIndoorPlaceholder.asset";

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

            // The original footstep doubles as stone, the default surface.
            SoundCue stone = EnsureCue(FootstepCuePath, footsteps, 0.8f, 25f, NoiseType.Footstep, 6f);
            EnsureCue(ImpactCuePath, impacts, 1f, 35f, NoiseType.Impact, 10f);

            // A door's hinges: quieter than a knock, and it doesn't carry
            // as far to guards.
            AudioClip[] creaks = new AudioClip[3];

            for (int i = 0; i < creaks.Length; i++) {
                creaks[i] = EnsureClip($"{ClipFolder}/CreakPlaceholder{i + 1}.wav", Creak(i));
            }

            EnsureCue(CreakCuePath, creaks, 0.6f, 20f, NoiseType.Mechanism, 5f);

            // Loops: a torch's crackle for SoundLoop (heard from where the
            // torch is, within 12m), and two backgrounds for the rooms'
            // ambience - wind for outside, a low room tone for indoors.
            // Noise radius 0: guards never react to them. No pitch
            // variation: a loop is one long sound, not a repeated one.
            AudioClip[] torch = { EnsureClip($"{ClipFolder}/TorchLoopPlaceholder.wav", Seamless(Fire(700, 4f + LoopOverlap), LoopOverlap)) };
            AudioClip[] wind = { EnsureClip($"{ClipFolder}/AmbienceOutsidePlaceholder.wav", Seamless(Wind(800, 9f + LoopOverlap), LoopOverlap)) };
            AudioClip[] roomTone = { EnsureClip($"{ClipFolder}/AmbienceIndoorPlaceholder.wav", Seamless(RoomTone(900, 6f + LoopOverlap), LoopOverlap)) };

            EnsureCue(TorchLoopCuePath, torch, 0.5f, 12f, NoiseType.Impact, 0f, 0f);
            EnsureCue(OutsideAmbienceCuePath, wind, 0.3f, 0f, NoiseType.Impact, 0f, 0f);
            EnsureCue(IndoorAmbienceCuePath, roomTone, 0.25f, 0f, NoiseType.Impact, 0f, 0f);

            // The other surfaces: the same recipe with different numbers
            // (see SurfaceStep()), so each can be told apart by ear. The
            // noise radius is the surface's loudness to guards at a walk:
            // carpet barely carries, metal rings a long way.
            SoundCue wood = EnsureCue(CueFolder + "/FootstepWoodPlaceholder.asset",
                SurfaceClips("FootstepWoodPlaceholder", 300, 0.15f, 160f, 40f, 0.7f, 0.3f, 60f, 0.4f, 0.1f),
                0.8f, 25f, NoiseType.Footstep, 7f);
            SoundCue carpet = EnsureCue(CueFolder + "/FootstepCarpetPlaceholder.asset",
                SurfaceClips("FootstepCarpetPlaceholder", 400, 0.15f, 60f, 45f, 0.35f, 0f, 40f, 0.6f, 0f),
                0.5f, 15f, NoiseType.Footstep, 2.5f);
            SoundCue metal = EnsureCue(CueFolder + "/FootstepMetalPlaceholder.asset",
                SurfaceClips("FootstepMetalPlaceholder", 500, 0.4f, 430f, 14f, 0.5f, 0.5f, 80f, 0.2f, 0.4f),
                0.9f, 30f, NoiseType.Footstep, 10f);
            SoundCue water = EnsureCue(CueFolder + "/FootstepWaterPlaceholder.asset",
                SurfaceClips("FootstepWaterPlaceholder", 600, 0.4f, 90f, 30f, 0.2f, 0f, 12f, 0.5f, 0.45f),
                0.8f, 25f, NoiseType.Footstep, 8f);

            EnsureSurfaceSounds(
                new[] { SurfaceType.Stone, SurfaceType.Wood, SurfaceType.Carpet, SurfaceType.Metal, SurfaceType.Water },
                new[] { stone, wood, carpet, metal, water });

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Three variants of one surface's step, as clips named
        /// name1.wav to name3.wav. Each variant is pitched a little higher
        /// than the last. The numbers are SurfaceStep()'s.
        /// </summary>
        private static AudioClip[] SurfaceClips(string name, int seed, float duration, float tone, float toneDecay, float toneLevel, float overtoneLevel, float hissDecay, float dullHiss, float sharpHiss)
        {
            AudioClip[] clips = new AudioClip[3];

            for (int i = 0; i < clips.Length; i++) {
                float variantTone = tone * (1f + i * 0.12f);
                float[] samples = SurfaceStep(seed + i, duration, variantTone, toneDecay, toneLevel, overtoneLevel, hissDecay, dullHiss, sharpHiss);
                clips[i] = EnsureClip($"{ClipFolder}/{name}{i + 1}.wav", samples);
            }

            return clips;
        }

        /// <summary>
        /// A step on some surface, from two ingredients. A tone (the body
        /// of the sound: low for a thud, high for a ring), with an overtone
        /// at a pitch that isn't a whole multiple of it - what makes metal
        /// sound like metal - both dying away at toneDecay. And hiss (the
        /// scuff or splash): a dull part and a sharp part, dying away at
        /// hissDecay. A bigger decay is a shorter sound.
        /// </summary>
        private static float[] SurfaceStep(int seed, float duration, float tone, float toneDecay, float toneLevel, float overtoneLevel, float hissDecay, float dullHiss, float sharpHiss)
        {
            float[] samples = new float[(int)(SampleRate * duration)];
            System.Random random = new(seed);
            float smoothed = 0f;

            for (int i = 0; i < samples.Length; i++) {
                float time = i / (float)SampleRate;
                float hiss = (float)(random.NextDouble() * 2.0 - 1.0);
                smoothed += (hiss - smoothed) * 0.2f;

                float body = Mathf.Sin(2f * Mathf.PI * tone * time)
                    + overtoneLevel * Mathf.Sin(2f * Mathf.PI * tone * 2.76f * time);

                samples[i] = body * toneLevel * Mathf.Exp(-time * toneDecay)
                    + (smoothed * dullHiss + hiss * sharpHiss) * Mathf.Exp(-time * hissDecay);
            }

            return samples;
        }

        /// <summary>
        /// Creates the SurfaceSounds asset if it isn't there: one entry per
        /// surface, each with its footstep cue.
        /// </summary>
        private static void EnsureSurfaceSounds(SurfaceType[] surfaces, SoundCue[] footstepCues)
        {
            if (AssetDatabase.LoadAssetAtPath<SurfaceSounds>(SurfaceSoundsPath) != null) {
                return;
            }

            SurfaceSounds sounds = ScriptableObject.CreateInstance<SurfaceSounds>();
            SerializedObject serialized = new(sounds);

            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = surfaces.Length;

            for (int i = 0; i < surfaces.Length; i++) {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("surface").enumValueIndex = (int)surfaces[i];
                entry.FindPropertyRelative("footstep").objectReferenceValue = footstepCues[i];

                // Heel and toe on stone only of these five (hard floor);
                // the other values are set anyway, so ticking the box on
                // another surface starts from something sensible.
                entry.FindPropertyRelative("heelAndToe").boolValue = surfaces[i] == SurfaceType.Stone;
                entry.FindPropertyRelative("heelAndToeRatio").floatValue = 1f;
                entry.FindPropertyRelative("toeVolume").floatValue = 0.6f;
                entry.FindPropertyRelative("toeDelay").floatValue = 0.1f;
                entry.FindPropertyRelative("toeMuffle").floatValue = 0.5f;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(sounds, SurfaceSoundsPath);
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
        /// A creak: a hinge groaning for about half a second. A buzzy
        /// tone (a sawtooth wave: it climbs steadily then drops, over and
        /// over, which sounds harsh rather than pure) whose pitch rises
        /// as it goes and wavers a little, fading in and out. Smoothed
        /// slightly, to take the edge off.
        /// </summary>
        private static float[] Creak(int variant)
        {
            const float duration = 0.5f;
            float[] samples = new float[(int)(SampleRate * duration)];
            float startTone = 95f + variant * 25f;
            float phase = 0f;
            float smoothed = 0f;

            for (int i = 0; i < samples.Length; i++) {
                float time = i / (float)SampleRate;
                float progress = time / duration;

                // The pitch climbs by a half over the creak, with a slow
                // waver on top.
                float tone = startTone * (1f + 0.5f * progress) * (1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 9f * time));

                // The wave's place in its cycle (0-1) moves on by one
                // sample's worth of the current pitch. Keeping a running
                // place, instead of working it out from the time, is what
                // lets the pitch change without clicks.
                phase += tone / SampleRate;
                phase -= Mathf.Floor(phase);

                float saw = phase * 2f - 1f;
                smoothed += (saw - smoothed) * 0.35f;

                // Sin over half a turn: 0 at the start, 1 in the middle,
                // 0 at the end.
                float loudness = Mathf.Sin(Mathf.PI * progress);
                samples[i] = smoothed * loudness * 0.6f;
            }

            return samples;
        }

        // How much of a loop's end is blended into its start, in seconds.
        private const float LoopOverlap = 0.5f;

        /// <summary>
        /// A fire: a low flutter (the flame), a soft hiss, and sharp
        /// crackles at random moments - each a burst of hiss that dies in a
        /// few hundredths of a second.
        /// </summary>
        private static float[] Fire(int seed, float duration)
        {
            float[] samples = new float[(int)(SampleRate * duration)];
            System.Random random = new(seed);
            float rumble = 0f;
            float soft = 0f;
            float crackle = 0f;

            for (int i = 0; i < samples.Length; i++) {
                float time = i / (float)SampleRate;
                float hiss = (float)(random.NextDouble() * 2.0 - 1.0);

                // Two smoothings of the same hiss: a heavy one leaves only
                // a low rumble, a light one a soft rush.
                rumble += (hiss - rumble) * 0.02f;
                soft += (hiss - soft) * 0.25f;

                // The flame's flutter: two slow waves multiplied, so it
                // swells unevenly.
                float flutter = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 7.3f * time) * Mathf.Sin(2f * Mathf.PI * 1.1f * time);

                // About a dozen crackles a second, each a different size.
                if (random.NextDouble() < 12.0 / SampleRate) {
                    crackle = 0.4f + (float)random.NextDouble() * 0.6f;
                }

                crackle *= 0.9985f;
                samples[i] = rumble * 2.5f * flutter + soft * 0.06f + hiss * crackle * 0.45f;
            }

            return samples;
        }

        /// <summary>
        /// Wind: hiss smoothed twice into a low rush, rising and falling
        /// in slow gusts that also brighten it as they rise.
        /// </summary>
        private static float[] Wind(int seed, float duration)
        {
            float[] samples = new float[(int)(SampleRate * duration)];
            System.Random random = new(seed);
            float first = 0f;
            float second = 0f;

            for (int i = 0; i < samples.Length; i++) {
                float time = i / (float)SampleRate;
                float hiss = (float)(random.NextDouble() * 2.0 - 1.0);

                // Two gust waves of unrelated lengths, so the pattern
                // doesn't obviously repeat within the loop.
                float gust = 0.55f + 0.3f * Mathf.Sin(2f * Mathf.PI * time / 4.5f) + 0.15f * Mathf.Sin(2f * Mathf.PI * time / 1.7f);
                float openness = 0.015f + 0.03f * gust;

                first += (hiss - first) * openness;
                second += (first - second) * openness;
                samples[i] = second * gust * 7f;
            }

            return samples;
        }

        /// <summary>
        /// The sound of an empty room: a very low, steady rumble, barely
        /// there.
        /// </summary>
        private static float[] RoomTone(int seed, float duration)
        {
            float[] samples = new float[(int)(SampleRate * duration)];
            System.Random random = new(seed);
            float first = 0f;
            float second = 0f;

            for (int i = 0; i < samples.Length; i++) {
                float hiss = (float)(random.NextDouble() * 2.0 - 1.0);
                first += (hiss - first) * 0.01f;
                second += (first - second) * 0.01f;
                samples[i] = second * 18f;
            }

            return samples;
        }

        /// <summary>
        /// Makes a sound loop without a click: the last overlap seconds
        /// are cut off and blended into the first overlap seconds, so the
        /// loop's end runs straight on into its start. The blend keeps the
        /// loudness even through it (square-root weights, right for
        /// noise-like sounds, where a straight blend would dip).
        /// </summary>
        private static float[] Seamless(float[] samples, float overlap)
        {
            int blend = (int)(SampleRate * overlap);
            int length = samples.Length - blend;
            float[] result = new float[length];

            for (int i = 0; i < length; i++) {
                if (i < blend) {
                    float t = i / (float)blend;
                    result[i] = samples[i] * Mathf.Sqrt(t) + samples[length + i] * Mathf.Sqrt(1f - t);
                } else {
                    result[i] = samples[i];
                }
            }

            return result;
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
        /// Creates the SoundCue asset at path if it isn't there, and returns
        /// it either way. Its fields are private and serialized, so they're
        /// set the way the Inspector would set them.
        /// </summary>
        private static SoundCue EnsureCue(string path, AudioClip[] clips, float volume, float audibleRange, NoiseType noiseType, float noiseRadius, float pitchVariation = 0.05f)
        {
            SoundCue cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);

            if (cue != null) {
                return cue;
            }

            cue = ScriptableObject.CreateInstance<SoundCue>();
            SerializedObject serialized = new(cue);

            SerializedProperty clipList = serialized.FindProperty("clips");
            clipList.arraySize = clips.Length;

            for (int i = 0; i < clips.Length; i++) {
                clipList.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }

            serialized.FindProperty("volume").floatValue = volume;
            serialized.FindProperty("pitchVariation").floatValue = pitchVariation;
            serialized.FindProperty("audibleRange").floatValue = audibleRange;
            serialized.FindProperty("noiseType").enumValueIndex = (int)noiseType;
            serialized.FindProperty("noiseRadius").floatValue = noiseRadius;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(cue, path);
            return cue;
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
