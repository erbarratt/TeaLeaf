using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Colour variants of the pieces' materials, for the generator to give a
    /// group of pieces (a building's roof) one colour chosen at random. A
    /// variant is a material anywhere under Assets/Art/Materials or
    /// Assets/Prefabs whose name is a piece material's name with a colour
    /// suffix: Medieval_Roofs_4k_green is the green variant of
    /// Medieval_Roofs_4k. The suffix (green) names the colour across
    /// materials, so one colour can be applied to pieces using different
    /// materials - the tile panels and the hip pieces - as long as each has
    /// a variant with that suffix; a material with none is left as it is.
    /// Applying a colour swaps the materials on a placed instance's
    /// renderers (every LOD), an override on the instance: the prefabs
    /// themselves are never touched.
    /// </summary>
    internal class ColourVariants
    {
        // Where the variant materials are looked for (the whole trees).
        private static readonly string[] _roots = { "Assets/Art/Materials", "Assets/Prefabs" };

        // The path of every material under the roots, by file name.
        private readonly Dictionary<string, string> _paths = new();
        // Each material's variants by suffix, filled as materials are first
        // met (null for a material with no variants).
        private readonly Dictionary<Material, Dictionary<string, Material>> _variants = new();

        /// <summary>
        /// Lists the materials under the roots, once per generation (the
        /// materials themselves load as they're used).
        /// </summary>
        internal ColourVariants()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", _roots)) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                _paths[System.IO.Path.GetFileNameWithoutExtension(path)] = path;
            }
        }

        // The plain colour, one of the choices whenever there are variants:
        // the unsuffixed copy of a piece's material under the roots (the
        // maintainer's own, to be edited like the variants) where there is
        // one, else the piece's own material left as it is.
        internal const string Default = "default";

        /// <summary>
        /// The colours available to the pieces under some roots (a
        /// building's group and its roof group): Default plus every suffix
        /// any of their renderers' materials has a variant for. Empty if
        /// none has.
        /// </summary>
        internal List<string> Suffixes(IReadOnlyList<Transform> roots)
        {
            List<string> suffixes = new();

            foreach (Transform root in roots) {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) {
                    foreach (Material material in renderer.sharedMaterials) {
                        Dictionary<string, Material> variants = Variants(material);

                        if (variants == null) {
                            continue;
                        }

                        foreach (string suffix in variants.Keys) {
                            if (!suffixes.Contains(suffix)) {
                                suffixes.Add(suffix);
                            }
                        }
                    }
                }
            }

            if (suffixes.Count > 0 && !suffixes.Contains(Default)) {
                suffixes.Add(Default);
            }

            // Sorted so the random pick is the same for the same seed
            // whatever order the renderers come in.
            suffixes.Sort();
            return suffixes;
        }

        /// <summary>
        /// Gives every renderer under a root the suffix's variant of each of
        /// its materials that has one. Adds to missing the name of any
        /// material met that has variants but none for this suffix (so the
        /// log can say which to clone). Default swaps a material for its
        /// unsuffixed copy under the roots, or leaves it as it is.
        /// </summary>
        internal void Apply(Transform root, string suffix, HashSet<string> missing)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++) {
                    Dictionary<string, Material> variants = Variants(materials[i]);

                    if (variants == null) {
                        continue;
                    }

                    if (variants.TryGetValue(suffix, out Material variant)) {
                        materials[i] = variant;
                        changed = true;
                    } else if (suffix != Default) {
                        missing.Add(materials[i].name);
                    }
                }

                if (changed) {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        /// <summary>
        /// A material's variants by suffix, found the first time it is asked
        /// for: the materials under the roots named "[its name]_[suffix]",
        /// and under Default one named exactly as it is (another asset: the
        /// maintainer's unsuffixed copy). Null if it has none (or is itself
        /// null).
        /// </summary>
        private Dictionary<string, Material> Variants(Material material)
        {
            if (material == null) {
                return null;
            }

            if (_variants.TryGetValue(material, out Dictionary<string, Material> variants)) {
                return variants;
            }

            if (_paths.TryGetValue(material.name, out string copyPath)) {
                Material copy = AssetDatabase.LoadAssetAtPath<Material>(copyPath);

                if (copy != null && copy != material) {
                    variants = new Dictionary<string, Material> { [Default] = copy };
                }
            }

            string prefix = material.name + "_";

            foreach (KeyValuePair<string, string> pair in _paths) {
                if (!pair.Key.StartsWith(prefix) || pair.Key.Length == prefix.Length) {
                    continue;
                }

                Material variant = AssetDatabase.LoadAssetAtPath<Material>(pair.Value);

                if (variant == null || variant == material) {
                    continue;
                }

                variants ??= new Dictionary<string, Material>();
                variants[pair.Key.Substring(prefix.Length)] = variant;
            }

            _variants[material] = variants;
            return variants;
        }
    }
}
