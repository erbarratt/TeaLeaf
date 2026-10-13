using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Make Environment Variants) that
    /// makes a prefab variant of every pack prefab selected in the
    /// Project window - single prefabs, or whole folders - for static
    /// level geometry: every object in the variant is put on the
    /// Environment layer and marked Static.
    ///
    /// The variants go in Assets/Prefabs/Environment/(category), where
    /// the category is the pack folder the prefab came from. A pack
    /// prefab that already has a variant anywhere under Assets/Prefabs
    /// is left alone, so running this again never overwrites work done
    /// on a variant by hand. The pack's own prefabs are never changed.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class EnvironmentVariants
    {
        // Where the game's own prefabs live, and where the new variants go.
        private const string PrefabsRoot = "Assets/Prefabs";
        private const string EnvironmentRoot = PrefabsRoot + "/Environment";
        private const string EnvironmentLayerName = "Environment";

        // The village pack starts its prefabs' names with PackPrefix; the
        // game's variants start with GamePrefix instead.
        private const string PackPrefix = "Medieval_";
        private const string GamePrefix = "TL_";

        // Every Static flag that still does something: lightmap baking, occlusion
        // culling (both ways), static batching and reflection probe baking. The
        // two navigation flags are obsolete - the NavMesh is baked by layer.
        private const StaticEditorFlags StaticFlags =
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.ReflectionProbeStatic;

        // The packs keep their prefabs in a folder with one of these names, inside
        // the folder that says what they are (Walls/Prfbs, Roofs/Prbs, ...).
        private static readonly HashSet<string> _prefabFolderNames = new(StringComparer.OrdinalIgnoreCase) {
            "Prfbs", "Pfbs", "Prefbs", "Prbs", "Prefab", "Prefabs"
        };

        [MenuItem("TeaLeaf/Make Environment Variants")]
        internal static void Make()
        {
            int layer = LayerMask.NameToLayer(EnvironmentLayerName);

            if (layer < 0) {
                Debug.LogError($"EnvironmentVariants: there is no '{EnvironmentLayerName}' layer.");
                return;
            }

            List<string> sources = FindSelectedPrefabs();

            if (sources.Count == 0) {
                Debug.LogWarning("EnvironmentVariants: select pack prefabs, or folders holding them, in the Project window first.");
                return;
            }

            HashSet<string> alreadyHaveVariant = FindSourcesWithVariant();
            int made = 0;
            int skipped = 0;

            foreach (string sourcePath in sources) {
                if (alreadyHaveVariant.Contains(sourcePath)) {
                    skipped++;
                    continue;
                }

                string folder = EnvironmentRoot + "/" + CategoryOf(sourcePath);
                string variantPath = folder + "/" + VariantFileName(sourcePath);

                // Two pack prefabs with the same name in the same category: the
                // first one made keeps the name, and nothing is overwritten.
                if (File.Exists(variantPath)) {
                    Debug.LogWarning($"EnvironmentVariants: '{variantPath}' already exists and isn't a variant of '{sourcePath}' - skipped.");
                    skipped++;
                    continue;
                }

                EnsureFolder(folder);
                MakeVariant(sourcePath, variantPath, layer);
                made++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"EnvironmentVariants: made {made} variant(s) under '{EnvironmentRoot}', skipped {skipped} that already had one.");
        }

        /// <summary>
        /// Makes one variant: an instance of the pack prefab is put in the
        /// scene, changed, saved as a new prefab (saving an instance of a
        /// prefab is what makes it a variant - only the changes are stored)
        /// and removed again.
        /// </summary>
        private static void MakeVariant(string sourcePath, string variantPath, int layer)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);

            try {
                // Every object in the prefab, inactive ones too: LOD meshes and
                // collision children all need the layer.
                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true)) {
                    child.gameObject.layer = layer;
                    GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticFlags);
                }

                PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
            } finally {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// The asset paths of every prefab selected in the Project window:
        /// the ones selected themselves, and all those inside selected
        /// folders. Prefabs already under Assets/Prefabs are left out -
        /// those are the game's own.
        /// </summary>
        private static List<string> FindSelectedPrefabs()
        {
            List<string> folders = new();
            HashSet<string> found = new();

            foreach (string guid in Selection.assetGUIDs) {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (AssetDatabase.IsValidFolder(path)) {
                    folders.Add(path);
                } else if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
                    found.Add(path);
                }
            }

            if (folders.Count > 0) {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders.ToArray())) {
                    found.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            List<string> sources = new();

            foreach (string path in found) {
                if (!path.StartsWith(PrefabsRoot + "/", StringComparison.OrdinalIgnoreCase)) {
                    sources.Add(path);
                }
            }

            sources.Sort(StringComparer.OrdinalIgnoreCase);
            return sources;
        }

        /// <summary>
        /// The asset paths of every prefab that something under
        /// Assets/Prefabs is a variant of. Found from the variants
        /// themselves rather than by file name, so a variant that has been
        /// renamed or moved to another folder still counts.
        /// </summary>
        private static HashSet<string> FindSourcesWithVariant()
        {
            HashSet<string> sources = new();

            if (!AssetDatabase.IsValidFolder(PrefabsRoot)) {
                return sources;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsRoot })) {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

                if (prefab == null || PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Variant) {
                    continue;
                }

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefab);

                if (source != null) {
                    sources.Add(AssetDatabase.GetAssetPath(source));
                }
            }

            return sources;
        }

        /// <summary>
        /// The file name a pack prefab's variant is saved under: the pack's
        /// own, with a leading Medieval_ swapped for TL_. A name that
        /// starts with anything else is kept as it is.
        /// </summary>
        private static string VariantFileName(string prefabPath)
        {
            string fileName = Path.GetFileName(prefabPath);

            if (fileName.StartsWith(PackPrefix, StringComparison.Ordinal)) {
                fileName = GamePrefix + fileName.Substring(PackPrefix.Length);
            }

            return fileName;
        }

        /// <summary>
        /// The folder name a pack prefab's variant is filed under: the
        /// folder it is in, or that folder's parent when it is just the
        /// pack's "prefabs" folder (Walls/Prfbs/Wall.prefab gives Walls).
        /// </summary>
        private static string CategoryOf(string prefabPath)
        {
            string folder = Path.GetDirectoryName(prefabPath);
            string category = Path.GetFileName(folder);

            if (_prefabFolderNames.Contains(category)) {
                category = Path.GetFileName(Path.GetDirectoryName(folder));
            }

            // The village pack's own spelling.
            if (category == "Masonary") {
                category = "Masonry";
            }

            return category;
        }

        /// <summary>
        /// Makes an asset folder, and any folders above it, if missing.
        /// Through AssetDatabase so each gets its .meta file straight away.
        /// </summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
