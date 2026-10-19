using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core
{
    /// <summary>
    /// Keeps deep shadow readable: after every lightmap bake (and from the
    /// menu, TeaLeaf > Apply Shadow Floor) raises every lightmap texel and
    /// every light probe to at least a floor light level, so no baked
    /// surface, and nothing lit by the probes, ever receives less light
    /// than the floor. The floor is the scene Moonlight's shadowFloor,
    /// tuned there; with no Moonlight in the scene nothing is applied. The floor is a light level, not a pixel value: the
    /// shader still multiplies it by the surface's albedo, so plaster
    /// lifts more than black timber, as a minimum light should.
    ///
    /// Why in the data and not the bake: a flat sky ambient reaches an
    /// enclosed alley at about one thirtieth of what open shade gets, so
    /// any ambient that lifts the alleys washes out the shade everywhere
    /// else. Flooring the baked result lifts only what is below the floor
    /// and leaves the moon-to-shade contrast alone. Costs nothing at
    /// runtime and changes no shader.
    ///
    /// The lightmaps are read back through the GPU (so the compressed
    /// BC6H textures need no importer changes), floored, written back
    /// over their EXR files and reimported; the probes get an ambient
    /// term added where their darkest direction is below the floor. Both
    /// steps are idempotent, so applying the floor twice changes nothing.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    [InitializeOnLoad]
    public static class ShadowFloor
    {
        // A texel this close under the floor (BC6H rounding) counts as floored,
        // so re-applying never rewrites a lightmap that is already done.
        private const float Tolerance = 0.98f;

        // Directions a probe is evaluated in to find its darkest side.
        private static readonly Vector3[] _probeDirections = {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };

        // Reimporting a lightmap makes Unity raise bakeCompleted again, from
        // inside the import: while a floor is being applied that event is
        // ignored, or it would read the half-reloaded texture and write it back.
        private static bool _applying;

        static ShadowFloor()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
        }

        /// <summary>
        /// Applies the floor after a real bake, not after its own reimports.
        /// </summary>
        private static void OnBakeCompleted()
        {
            if (!_applying) {
                Apply();
            }
        }

        /// <summary>
        /// Floors the open scene's lightmaps and light probes. Runs by itself
        /// when a bake completes; the menu item re-applies it by hand.
        /// </summary>
        [MenuItem("TeaLeaf/Apply Shadow Floor")]
        internal static void Apply()
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
                    string path = AssetDatabase.GetAssetPath(colour);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".exr")) {
                        continue;
                    }
                    long raised = FloorLightmap(colour, path, floor);
                    if (raised > 0) {
                        // Synchronous: the texture is reloaded before the next map is read.
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                        mapsFloored++;
                        texelsRaised += raised;
                    }
                }
                int probesRaised = FloorProbes(floor);
                Debug.Log($"ShadowFloor: raised {texelsRaised} texel(s) in {mapsFloored} of {lightmaps.Length} lightmap(s) and {probesRaised} light probe(s) to the floor {floor} (Moonlight.shadowFloor).");
            } finally {
                _applying = false;
            }
        }

        /// <summary>
        /// Reads one lightmap back through the GPU as linear floats, raises
        /// every texel to the floor and writes it back over its EXR file.
        /// Returns how many texels were raised; zero means the file was left
        /// alone.
        /// </summary>
        private static long FloorLightmap(Texture2D lightmap, string path, Color floor)
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
            long raised = 0;
            for (int i = 0; i < texels.Length; i++) {
                Color texel = texels[i];
                if (texel.r >= floor.r * Tolerance && texel.g >= floor.g * Tolerance && texel.b >= floor.b * Tolerance) {
                    continue;
                }
                texel.r = Mathf.Max(texel.r, floor.r);
                texel.g = Mathf.Max(texel.g, floor.g);
                texel.b = Mathf.Max(texel.b, floor.b);
                texels[i] = texel;
                raised++;
            }

            if (raised > 0) {
                readable.SetPixels(texels);
                readable.Apply(false);
                File.WriteAllBytes(path, readable.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
            }
            Object.DestroyImmediate(readable);
            return raised;
        }

        /// <summary>
        /// Adds ambient light to every baked probe whose darkest direction is
        /// below the floor, by just enough to reach it per channel, and saves
        /// the lighting data. Returns how many probes were raised.
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
                Color lift = new Color(
                    Mathf.Max(0f, floor.r - darkest.r),
                    Mathf.Max(0f, floor.g - darkest.g),
                    Mathf.Max(0f, floor.b - darkest.b), 1f);
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
