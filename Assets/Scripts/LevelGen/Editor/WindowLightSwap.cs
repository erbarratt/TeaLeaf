using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// TeaLeaf > Level Gen > Swap Window Light: each selected window insert
    /// (a TL_Window_* prefab instance, or anything inside one) is replaced by
    /// its twin - unlit by its _Lit version, lit by its plain one - from the
    /// same folder, in the same parent, sibling slot and local transform. The
    /// lit twin brings its own Baked spot, so nothing else is set. One Undo
    /// step. Inside a Generated root this is a hand edit (the next
    /// regeneration asks first and puts back what the generator drew); the
    /// building's proxy and the lighting need rebuilding after.
    /// </summary>
    internal static class WindowLightSwap
    {
        private const string Prefix = "TL_Window_";
        private const string LitSuffix = "_Lit";

        [MenuItem("TeaLeaf/Level Gen/Swap Window Light")]
        private static void SwapSelected()
        {
            // Windows found from the selection, each once (two selected
            // children of one window swap it once, not back again).
            var windows = new List<GameObject>();

            foreach (GameObject selected in Selection.gameObjects) {
                GameObject window = WindowOf(selected);
                if (window == null) {
                    Debug.LogWarning($"WindowLightSwap: '{selected.name}' isn't in a {Prefix}* window instance.", selected);
                } else if (!windows.Contains(window)) {
                    windows.Add(window);
                }
            }

            if (windows.Count == 0) {
                Debug.LogWarning($"WindowLightSwap: select one or more {Prefix}* windows in the scene.");
                return;
            }

            Undo.SetCurrentGroupName("Swap Window Light");
            int group = Undo.GetCurrentGroup();
            var swapped = new List<Object>();

            foreach (GameObject window in windows) {
                GameObject twin = Swap(window);
                if (twin != null) {
                    swapped.Add(twin);
                }
            }

            Undo.CollapseUndoOperations(group);
            Selection.objects = swapped.ToArray();
            Debug.Log($"WindowLightSwap: {swapped.Count} of {windows.Count} window(s) swapped. Rebuild Building Proxies and rebake the lighting to see it from afar and in the street.");
        }

        /// <summary>
        /// The outermost window prefab instance holding an object (itself or a
        /// parent), or null. A window nested inside another prefab (as in the
        /// Open Window Wall) can't be swapped on the instance, so isn't found.
        /// </summary>
        private static GameObject WindowOf(GameObject selected)
        {
            for (Transform t = selected.transform; t != null; t = t.parent) {
                GameObject root = t.gameObject;
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(root)) {
                    continue;
                }

                GameObject asset = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (asset != null && asset.name.StartsWith(Prefix)) {
                    return root;
                }
            }

            return null;
        }

        /// <summary>
        /// Replaces one window instance by its lit or unlit twin, keeping its
        /// place in the hierarchy and its local transform. Null (logged) if the
        /// twin prefab doesn't exist.
        /// </summary>
        private static GameObject Swap(GameObject window)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(window);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            string twinName = name.EndsWith(LitSuffix) ? name.Substring(0, name.Length - LitSuffix.Length) : name + LitSuffix;
            string twinPath = $"{System.IO.Path.GetDirectoryName(path).Replace('\\', '/')}/{twinName}.prefab";

            GameObject twinAsset = AssetDatabase.LoadAssetAtPath<GameObject>(twinPath);
            if (twinAsset == null) {
                Debug.LogWarning($"WindowLightSwap: '{window.name}' has no twin ({twinPath} missing).", window);
                return null;
            }

            Transform old = window.transform;
            var twin = (GameObject)PrefabUtility.InstantiatePrefab(twinAsset, old.parent);
            twin.transform.SetLocalPositionAndRotation(old.localPosition, old.localRotation);
            twin.transform.localScale = old.localScale;
            twin.transform.SetSiblingIndex(old.GetSiblingIndex());
            Undo.RegisterCreatedObjectUndo(twin, "Swap Window Light");
            Undo.DestroyObjectImmediate(window);
            return twin;
        }
    }
}
