using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core
{
    /// <summary>
    /// Keeps deep shadow readable: after every lighting bake, lifts every
    /// lightmap texel and every legacy light probe darker than twice a floor
    /// light level, so no baked surface, and nothing lit by the probes, ever
    /// receives less light than the floor. Adaptive Probe Volumes get the
    /// same lift during their own bake (ProbeVolumeShadowFloor), so movers
    /// and walls match. A lift, not a clamp: black goes to the floor and
    /// everything up to twice the floor is squeezed evenly in above it (see
    /// Lift()), so dark corners keep half their variation instead of going
    /// one flat level, and nothing darker ever ends up brighter than
    /// something lighter. The floor is the scene Moonlight's shadowFloor,
    /// tuned there (a change needs a rebake); with no Moonlight in the scene,
    /// or a black floor, nothing is applied. The floor is a light level, not
    /// a pixel value: the shader still multiplies it by the surface's albedo,
    /// so plaster lifts more than black timber, as a minimum light should.
    ///
    /// Why in the data and not the bake's lights: a flat sky ambient reaches
    /// an enclosed alley at about one thirtieth of what open shade gets, so
    /// any ambient that lifts the alleys washes out the shade everywhere
    /// else. Flooring the baked result lifts only what is below the floor
    /// and leaves the moon-to-shade contrast alone. Costs nothing at
    /// runtime and changes no shader.
    ///
    /// The lightmaps are read back through the GPU (so the compressed
    /// BC6H textures need no importer changes), floored, written back
    /// over their EXR files and reimported; the probes get an ambient
    /// term added where their darkest direction is below twice the floor.
    /// Each floored lightmap's importer remembers the hash of the file it
    /// wrote, so a map already floored is never lifted twice; a rebake
    /// writes a new file and is floored afresh.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    [InitializeOnLoad]
    public static class ShadowFloor
    {
        // Directions a probe is evaluated in to find its darkest side.
        private static readonly Vector3[] _probeDirections = {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };

        // Start of a floored lightmap's importer userData; the file's hash follows.
        private const string FlooredMark = "ShadowFloor:";

        // True while a floor is being applied, so a second apply can't start
        // inside the first one's lightmap reimports.
        private static bool _applying;

        static ShadowFloor()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
        }

        /// <summary>
        /// A bake finished: floor its results once the editor is idle (after
        /// every other bake handler, with the files written).
        /// </summary>
        private static void OnBakeCompleted()
        {
            EditorApplication.delayCall -= Apply;
            EditorApplication.delayCall += Apply;
        }

        /// <summary>
        /// Lifts the open scene's lightmaps (those not already floored) and
        /// legacy light probes.
        /// </summary>
        private static void Apply()
        {
            if (_applying) {
                return;
            }
            Moonlight moon = Object.FindFirstObjectByType<Moonlight>();
            if (moon == null) {
                Debug.Log("ShadowFloor: no Moonlight in the scene, so no floor applied.");
                return;
            }
            Color floor = moon.ShadowFloor;
            if (floor.r <= 0f && floor.g <= 0f && floor.b <= 0f) {
                return;
            }
            _applying = true;
            try {
                LightmapData[] lightmaps = LightmapSettings.lightmaps;
                int mapsFloored = 0;
                long texelsRaised = 0;
                foreach (LightmapData lightmap in lightmaps) {
                    Texture2D colour = lightmap.lightmapColor;
                    if (colour == null) {
                        continue;
                    }
                    // Auto-generated lighting has no asset path and is skipped.
                    string path = AssetDatabase.GetAssetPath(colour);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".exr")) {
                        continue;
                    }
                    AssetImporter importer = AssetImporter.GetAtPath(path);
                    if (importer == null || importer.userData == FlooredMark + HashOf(File.ReadAllBytes(path))) {
                        continue;
                    }
                    byte[] floored = FloorLightmap(colour, floor, out long raised);
                    if (floored == null) {
                        continue;
                    }
                    File.WriteAllBytes(path, floored);
                    importer.userData = FlooredMark + HashOf(floored);
                    // Synchronous: the texture is reloaded before the next map is read.
                    importer.SaveAndReimport();
                    mapsFloored++;
                    texelsRaised += raised;
                }
                int probesRaised = FloorProbes(floor);
                Debug.Log($"ShadowFloor: raised {texelsRaised} texel(s) in {mapsFloored} of {lightmaps.Length} lightmap(s) and {probesRaised} legacy light probe(s) onto the floor {floor} (Moonlight.shadowFloor).");
            } finally {
                _applying = false;
            }
        }

        /// <summary>
        /// A file's contents as a short hash string.
        /// </summary>
        private static string HashOf(byte[] bytes)
        {
            return Hash128.Compute(bytes).ToString();
        }

        /// <summary>
        /// Reads one lightmap back through the GPU as linear floats and
        /// raises every texel to the floor. Returns the floored EXR file's
        /// bytes, or null if no texel needed raising (file left alone).
        /// </summary>
        private static byte[] FloorLightmap(Texture2D lightmap, Color floor, out long raised)
        {
            int width = lightmap.width, height = lightmap.height;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Texture2D readable = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active;
            try {
                Graphics.Blit(lightmap, target);
                RenderTexture.active = target;
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply(false);
            } finally {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }

            Color[] texels = readable.GetPixels();
            raised = 0;
            for (int i = 0; i < texels.Length; i++) {
                Color texel = texels[i];
                if (texel.r >= floor.r * 2f && texel.g >= floor.g * 2f && texel.b >= floor.b * 2f) {
                    continue;
                }
                texel.r = Lift(texel.r, floor.r);
                texel.g = Lift(texel.g, floor.g);
                texel.b = Lift(texel.b, floor.b);
                texels[i] = texel;
                raised++;
            }

            byte[] floored = null;
            if (raised > 0) {
                readable.SetPixels(texels);
                readable.Apply(false);
                floored = readable.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            }
            Object.DestroyImmediate(readable);
            return floored;
        }

        /// <summary>
        /// One channel's light after the lift: everything from black up to
        /// twice the floor is squeezed evenly into floor..twice the floor
        /// (black lands on the floor, half the step between two darks is
        /// kept, order never flips); from twice the floor up it is untouched.
        /// Shared with ProbeVolumeShadowFloor; safe on any thread.
        /// </summary>
        internal static float Lift(float light, float floor)
        {
            return light >= floor * 2f ? light : floor + Mathf.Max(0f, light) * 0.5f;
        }

        /// <summary>
        /// Adds ambient light to every baked legacy probe (Light Probe
        /// Groups; none under Adaptive Probe Volumes) whose darkest direction
        /// is below twice the floor, by what Lift() gives that darkest
        /// direction per channel, and saves the lighting data. Runs once per
        /// bake, on the fresh probes. Returns how many probes were raised.
        /// </summary>
        private static int FloorProbes(Color floor)
        {
            LightProbes probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0) {
                return 0;
            }

            SphericalHarmonicsL2[] baked = probes.bakedProbes;
            Color[] results = new Color[_probeDirections.Length];
            int raised = 0;
            for (int i = 0; i < baked.Length; i++) {
                baked[i].Evaluate(_probeDirections, results);
                Color darkest = results[0];
                for (int d = 1; d < results.Length; d++) {
                    darkest.r = Mathf.Min(darkest.r, results[d].r);
                    darkest.g = Mathf.Min(darkest.g, results[d].g);
                    darkest.b = Mathf.Min(darkest.b, results[d].b);
                }
                // The whole probe moves up by what its darkest side gains, so
                // its own spread between sides is kept.
                Color lift = new Color(
                    Lift(darkest.r, floor.r) - darkest.r,
                    Lift(darkest.g, floor.g) - darkest.g,
                    Lift(darkest.b, floor.b) - darkest.b, 1f);
                if (lift.r <= 0f && lift.g <= 0f && lift.b <= 0f) {
                    continue;
                }
                baked[i].AddAmbientLight(lift);
                raised++;
            }

            if (raised > 0) {
                probes.bakedProbes = baked;
                EditorUtility.SetDirty(probes);
                AssetDatabase.SaveAssets();
            }
            return raised;
        }
    }
}
