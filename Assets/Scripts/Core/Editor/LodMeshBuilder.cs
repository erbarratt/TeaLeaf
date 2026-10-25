using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core
{
    /// <summary>
    /// Editor window (TeaLeaf > Modify Prefab > Make LOD Meshes) that makes
    /// lower-detail copies of a mesh, laid out the way the packs' models
    /// are (Medieval_Barrel: a _LOD0, _LOD1 and _LOD2 mesh, and a _Collision
    /// mesh), and shows them before they are saved. The mesh itself is LOD0;
    /// the window saves a <name>_LOD1.asset and <name>_LOD2.asset mesh next
    /// to it, plus a <name>_Collision.asset when asked (off by default) - a
    /// much simpler mesh to put on a convex MeshCollider.
    ///
    /// Works on what is selected in the Project window. A model (FBX) or
    /// mesh gets its mesh assets only. A prefab also gets its LODGroup set
    /// up, the way the game's prefabs are built (root with a LODGroup, a
    /// <name>_LOD0/1/2 child per level): a missing LODGroup is added, a
    /// level's existing renderer gets the new mesh, and a missing one is
    /// made as a copy of LOD0's renderer. The collision mesh is saved but
    /// never put on the prefab (colliders are fitted by hand).
    ///
    /// The simplifying is Unity's own Mesh LOD generator
    /// (MeshLodUtility.GenerateMeshLods, the call behind the model
    /// importer's "Generate Mesh LODs"): it keeps UV seams whole yet still
    /// reduces far, because every level reuses the original vertices. It
    /// makes a fixed run of levels, each about half the last, so each mesh
    /// saved here is the generated level nearest the share asked for. A
    /// generated level is only a range of the copy's index buffer, so it is
    /// copied out into a mesh of its own holding just the vertices it uses.
    /// Running it again rewrites the existing mesh assets in place, so a
    /// prefab that already references them keeps working.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    public class LodMeshBuilder : EditorWindow
    {
        // The share of the original's triangles each level keeps. The pack's
        // props drop to roughly a half and a fifth; a collision mesh only has
        // to hold the shape (a convex MeshCollider keeps 255 faces at most).
        private const float DefaultLod1Ratio = 0.5f;
        private const float DefaultLod2Ratio = 0.2f;
        private const float DefaultCollisionRatio = 0.1f;

        // Screen heights at which each level hands over to the next, for a
        // LODGroup level that didn't exist yet (the game's prefabs use these;
        // an existing level keeps its own). Below the last the prop is culled.
        private static readonly float[] _defaultHeights = { 0.07f, 0.035f, 0.01f };

        // The settings are kept between sessions in EditorPrefs under these keys.
        private const string PrefsPrefix = "TeaLeaf.LodMeshBuilder.";

        // Names of the preview's levels, in the order of _previewMeshes.
        private static readonly string[] _previewNames = { "LOD0", "LOD1", "LOD2", "Collision" };

        private float lod1Ratio = DefaultLod1Ratio;
        private float lod2Ratio = DefaultLod2Ratio;
        private bool makeCollision;
        private float collisionRatio = DefaultCollisionRatio;

        // The selected item previewed, the meshes in it to choose from, and which one.
        private Object _previewTarget;
        private readonly List<Mesh> _previewSources = new List<Mesh>();
        private string[] _previewSourceNames = new string[0];
        private int _previewSourceIndex;
        private string _previewProblem;

        // The generated copy of the previewed mesh (every level in its index
        // buffer), the level each saved mesh would take, the meshes shown
        // (LOD0 is the source itself; the rest are copied-out levels), and
        // which is shown.
        private Mesh _generated;
        private int[] _generatedIndices;
        private readonly int[] _previewLevels = { 0, -1, -1, -1 };
        private readonly Mesh[] _previewMeshes = new Mesh[4];
        private int _previewShown;
        private MeshPreview _preview;

        [MenuItem("TeaLeaf/Modify Prefab/Make LOD Meshes")]
        private static void Open()
        {
            GetWindow<LodMeshBuilder>("LOD Meshes");
        }

        private void OnEnable()
        {
            lod1Ratio = EditorPrefs.GetFloat(PrefsPrefix + "Lod1", DefaultLod1Ratio);
            lod2Ratio = EditorPrefs.GetFloat(PrefsPrefix + "Lod2", DefaultLod2Ratio);
            makeCollision = EditorPrefs.GetBool(PrefsPrefix + "Collision", false);
            collisionRatio = EditorPrefs.GetFloat(PrefsPrefix + "CollisionRatio", DefaultCollisionRatio);
            SetPreviewTarget(Selection.activeObject);
        }

        private void OnDisable()
        {
            EditorPrefs.SetFloat(PrefsPrefix + "Lod1", lod1Ratio);
            EditorPrefs.SetFloat(PrefsPrefix + "Lod2", lod2Ratio);
            EditorPrefs.SetBool(PrefsPrefix + "Collision", makeCollision);
            EditorPrefs.SetFloat(PrefsPrefix + "CollisionRatio", collisionRatio);
            ClearPreview();
            if (_preview != null) {
                _preview.Dispose();
                _preview = null;
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject != _previewTarget) {
                SetPreviewTarget(Selection.activeObject);
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Triangles kept, as a share of the model's mesh", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Unity's generator makes levels of about 1/2, 1/4, 1/8... of the triangles; " +
                "each mesh is the level nearest its share.",
                MessageType.None);
            lod1Ratio = EditorGUILayout.Slider("LOD1", lod1Ratio, 0.05f, 0.95f);
            lod2Ratio = EditorGUILayout.Slider("LOD2", lod2Ratio, 0.02f, 0.9f);
            EditorGUILayout.Space();
            makeCollision = EditorGUILayout.Toggle("Collision mesh", makeCollision);
            using (new EditorGUI.DisabledScope(!makeCollision)) {
                collisionRatio = EditorGUILayout.Slider("Collision", collisionRatio, 0.02f, 0.5f);
            }
            EditorGUILayout.Space();

            List<Object> items = SelectedItems();
            int prefabs = 0;
            foreach (Object item in items) {
                if (item is GameObject) prefabs++;
            }
            EditorGUILayout.LabelField(items.Count == 0
                ? "Select a prefab, a model (FBX) or a mesh in the Project window."
                : $"{items.Count} selected ({prefabs} prefab(s): LODGroup set up too).");
            using (new EditorGUI.DisabledScope(items.Count == 0)) {
                if (GUILayout.Button("Make LOD Meshes")) {
                    Make(items, lod1Ratio, lod2Ratio, makeCollision ? collisionRatio : 0f);
                    // The saved assets may have changed what the selection holds.
                    SetPreviewTarget(Selection.activeObject);
                }
            }

            DrawPreview();
        }

        /// <summary>
        /// The preview half of the window: which mesh, which level, the
        /// triangle counts each saved mesh would get, and the mesh drawn by
        /// Unity's own MeshPreview (drag to orbit, scroll to zoom; the
        /// toolbar picks Shaded, UV Checker - shows texture tearing - and
        /// the rest, plus wireframe).
        /// </summary>
        private void DrawPreview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            if (_previewProblem != null) {
                EditorGUILayout.HelpBox(_previewProblem, MessageType.Info);
                return;
            }
            if (_generated == null) return;

            using (new EditorGUI.DisabledScope(_previewSources.Count < 2)) {
                int index = EditorGUILayout.Popup("Mesh", _previewSourceIndex, _previewSourceNames);
                if (index != _previewSourceIndex) {
                    _previewSourceIndex = index;
                    Generate();
                }
            }

            // Re-pick the levels each frame (cheap); a level is copied out
            // again only when the sliders move it to another.
            UpdatePreviewLevel(1, lod1Ratio);
            UpdatePreviewLevel(2, lod2Ratio);
            UpdatePreviewLevel(3, makeCollision ? collisionRatio : 0f);

            int lod0 = Mathf.Max(1, _previewMeshes[0].triangles.Length / 3);
            for (int i = 0; i < _previewMeshes.Length; i++) {
                if (_previewMeshes[i] == null) continue;
                int triangles = _previewMeshes[i].triangles.Length / 3;
                string level = i == 0 ? "" : $", generated level {_previewLevels[i]}";
                EditorGUILayout.LabelField(
                    $"{_previewNames[i]}: {triangles} tris ({100f * triangles / lod0:0}%), {_previewMeshes[i].vertexCount} verts{level}",
                    i == _previewShown ? EditorStyles.boldLabel : EditorStyles.label);
            }

            if (_previewMeshes[_previewShown] == null) _previewShown = 0;
            int shown = GUILayout.Toolbar(_previewShown, makeCollision ? _previewNames : new[] { "LOD0", "LOD1", "LOD2" });
            if (shown != _previewShown) _previewShown = shown;
            if (_preview == null) _preview = new MeshPreview(_previewMeshes[_previewShown]);
            if (_preview.mesh != _previewMeshes[_previewShown]) _preview.mesh = _previewMeshes[_previewShown];

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                GUILayout.FlexibleSpace();
                _preview.OnPreviewSettings();
            }
            Rect rect = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f);
            _preview.OnPreviewGUI(rect, "PreBackground");
        }

        /// <summary>
        /// Previews <paramref name="item"/>: gathers the meshes it would make
        /// LODs of (a prefab's LOD0 mesh, every mesh in a model, or the one
        /// mesh) and generates the levels of the first.
        /// </summary>
        private void SetPreviewTarget(Object item)
        {
            _previewTarget = item;
            _previewSources.Clear();
            _previewSourceIndex = 0;
            _previewProblem = null;
            if (item == null || !IsUsable(item)) {
                _previewProblem = "Select a prefab, a model (FBX) or a mesh in the Project window.";
            } else if (item is GameObject prefab) {
                Mesh mesh = Lod0Mesh(prefab, out _previewProblem);
                if (mesh != null) _previewSources.Add(mesh);
            } else {
                _previewSources.AddRange(SourceMeshes(item, AssetDatabase.GetAssetPath(item), out _));
            }
            _previewSourceNames = new string[_previewSources.Count];
            for (int i = 0; i < _previewSources.Count; i++) _previewSourceNames[i] = _previewSources[i].name;
            Generate();
        }

        /// <summary>
        /// Generates the Mesh LOD levels of the chosen preview mesh on a copy;
        /// the shown meshes are copied out from it as the levels are picked.
        /// </summary>
        private void Generate()
        {
            ClearPreview();
            if (_previewSources.Count == 0) return;
            Mesh source = _previewSources[_previewSourceIndex];
            if (IsSkinned(source)) {
                _previewProblem = "Skinned or has blend shapes: not made (a copied-out level would lose its rig).";
                return;
            }
            _generated = Instantiate(source);
            _generated.hideFlags = HideFlags.HideAndDontSave;
            MeshLodUtility.GenerateMeshLods(_generated, -1);
            _generatedIndices = ReadIndices(_generated);
            _previewMeshes[0] = source;
        }

        /// <summary>
        /// Points preview slot <paramref name="slot"/> at the generated level
        /// nearest <paramref name="ratio"/> (none for a ratio of 0), copying
        /// the level out again only when it differs from the one shown.
        /// </summary>
        private void UpdatePreviewLevel(int slot, float ratio)
        {
            int level = ratio > 0f ? NearestLevel(_generated, ratio) : -1;
            if (level == _previewLevels[slot]) return;
            if (_previewMeshes[slot] != null) DestroyImmediate(_previewMeshes[slot]);
            _previewMeshes[slot] = null;
            _previewLevels[slot] = level;
            if (level < 0) return;
            Mesh mesh = ExtractLevel(_previewSources[_previewSourceIndex], _generated, _generatedIndices, level, slot == 3);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            _previewMeshes[slot] = mesh;
        }

        /// <summary>
        /// Destroys the generated copy and the copied-out preview meshes
        /// (never slot 0, the source mesh itself).
        /// </summary>
        private void ClearPreview()
        {
            if (_generated != null) DestroyImmediate(_generated);
            _generated = null;
            _generatedIndices = null;
            _previewMeshes[0] = null;
            for (int i = 1; i < _previewMeshes.Length; i++) {
                if (_previewMeshes[i] != null) DestroyImmediate(_previewMeshes[i]);
                _previewMeshes[i] = null;
                _previewLevels[i] = -1;
            }
        }

        /// <summary>
        /// The prefabs, models and meshes the Project window selection holds.
        /// </summary>
        private static List<Object> SelectedItems()
        {
            var items = new List<Object>();
            foreach (Object selected in Selection.objects) {
                if (IsUsable(selected)) items.Add(selected);
            }
            return items;
        }

        /// <summary>
        /// Whether <paramref name="asset"/> is a prefab, a model file (FBX,
        /// OBJ) or a mesh asset. A model's root GameObject counts as the
        /// model, not as a prefab.
        /// </summary>
        private static bool IsUsable(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return false;
            if (AssetImporter.GetAtPath(path) is ModelImporter) return true;
            return asset is Mesh || (asset is GameObject && path.EndsWith(".prefab"));
        }

        /// <summary>
        /// Makes the LOD meshes (and the collision mesh, when
        /// <paramref name="collisionRatio"/> is above zero) for every item in
        /// <paramref name="items"/>, saved next to each source mesh; a prefab
        /// also gets its LODGroup set up. Reports the triangle counts in the
        /// Console.
        /// </summary>
        public static void Make(IEnumerable<Object> items, float lod1Ratio, float lod2Ratio, float collisionRatio)
        {
            var report = new StringBuilder("LodMeshBuilder:\n");
            var prefabMeshes = new List<(string path, Mesh lod1, Mesh lod2)>();
            try {
                AssetDatabase.StartAssetEditing();
                foreach (Object item in items) {
                    if (item is GameObject prefab && !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(item)) is ModelImporter)) {
                        string prefabPath = AssetDatabase.GetAssetPath(prefab);
                        Mesh source = Lod0Mesh(prefab, out string problem);
                        if (source == null) {
                            report.Append($"  {prefabPath}: skipped: {problem}\n");
                            continue;
                        }
                        Mesh[] made = MakeMeshes(source, NameFor(source), lod1Ratio, lod2Ratio, collisionRatio, report);
                        if (made != null) prefabMeshes.Add((prefabPath, made[0], made[1]));
                        continue;
                    }
                    string modelPath = AssetDatabase.GetAssetPath(item);
                    foreach (Mesh source in SourceMeshes(item, modelPath, out string baseName)) {
                        MakeMeshes(source, NameFor(source, baseName, modelPath), lod1Ratio, lod2Ratio, collisionRatio, report);
                    }
                }
            } finally {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();

            // Prefabs are changed once the meshes are saved assets, so the
            // LODGroup can point at them.
            foreach ((string path, Mesh lod1, Mesh lod2) in prefabMeshes) {
                report.Append($"  {path}: {SetUpLodGroup(path, lod1, lod2)}\n");
            }
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Generates the levels of <paramref name="source"/> and saves the
        /// LOD1, LOD2 (and collision) meshes as <paramref name="name"/>_LOD1
        /// and so on, next to the source. Returns the saved LOD1 and LOD2
        /// meshes, or null if the mesh was skipped.
        /// </summary>
        private static Mesh[] MakeMeshes(Mesh source, string name, float lod1Ratio, float lod2Ratio,
            float collisionRatio, StringBuilder report)
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(source)) ?? "";
            report.Append($"  {name}: LOD0 {source.triangles.Length / 3} tris");
            if (IsSkinned(source)) {
                report.Append(" > skipped: skinned or has blend shapes\n");
                return null;
            }
            // The generator writes into the mesh it is given, so it gets a
            // copy: the imported mesh stays as it is. One run makes every
            // level; each saved mesh picks its own from them.
            Mesh generated = Instantiate(source);
            MeshLodUtility.GenerateMeshLods(generated, -1);
            int[] indices = ReadIndices(generated);
            var made = new Mesh[2];
            made[0] = Save(source, generated, indices, name + "_LOD1", folder, lod1Ratio, false, report);
            made[1] = Save(source, generated, indices, name + "_LOD2", folder, lod2Ratio, false, report);
            if (collisionRatio > 0f) {
                // A MeshCollider reads only positions, so the collision mesh
                // carries nothing else.
                Save(source, generated, indices, name + "_Collision", folder, collisionRatio, true, report);
            }
            DestroyImmediate(generated);
            report.Append('\n');
            return made;
        }

        /// <summary>
        /// Whether <paramref name="mesh"/> is skinned or has blend shapes:
        /// copying a level out keeps only the vertex attributes a static prop
        /// has, so such a mesh would lose its rig.
        /// </summary>
        private static bool IsSkinned(Mesh mesh)
        {
            return mesh.blendShapeCount > 0 || mesh.bindposeCount > 0;
        }

        /// <summary>
        /// The renderer showing a prefab's full-detail mesh: LOD0's renderer
        /// when it has a LODGroup, otherwise its only MeshRenderer. Null, with
        /// <paramref name="problem"/> saying why, when there isn't exactly one.
        /// </summary>
        private static MeshRenderer Lod0Renderer(GameObject root, out string problem)
        {
            problem = null;
            LODGroup group = root.GetComponentInChildren<LODGroup>(true);
            if (group != null) {
                LOD[] lods = group.GetLODs();
                if (lods.Length > 0) {
                    MeshRenderer found = null;
                    int count = 0;
                    foreach (Renderer renderer in lods[0].renderers) {
                        if (renderer == null) continue;
                        count++;
                        found = renderer as MeshRenderer;
                    }
                    if (count > 1) {
                        problem = "its LOD0 has several renderers; one is expected.";
                        return null;
                    }
                    if (count == 1) {
                        if (found == null || found.GetComponent<MeshFilter>() == null) {
                            problem = "its LOD0 renderer isn't a MeshRenderer with a MeshFilter.";
                            return null;
                        }
                        return found;
                    }
                }
            }
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length != 1 || renderers[0].GetComponent<MeshFilter>() == null) {
                problem = renderers.Length == 0
                    ? "it has no MeshRenderer with a MeshFilter."
                    : "it has several MeshRenderers and no LODGroup naming LOD0.";
                return null;
            }
            return renderers[0];
        }

        /// <summary>
        /// The mesh a prefab's LOD0 shows (see <see cref="Lod0Renderer"/>),
        /// or null with <paramref name="problem"/> saying why.
        /// </summary>
        private static Mesh Lod0Mesh(GameObject prefab, out string problem)
        {
            MeshRenderer renderer = Lod0Renderer(prefab, out problem);
            if (renderer == null) return null;
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) problem = "its LOD0 MeshFilter has no mesh.";
            return mesh;
        }

        /// <summary>
        /// Points the prefab at <paramref name="path"/> at the new meshes: a
        /// LODGroup added if it has none; LOD0 kept as it is (filled in with
        /// the prefab's renderer if empty); for LOD1 and LOD2 the level's
        /// existing renderer gets the mesh, or a child named &lt;base&gt;_LOD1 / _LOD2
        /// is found or made as a copy of LOD0's renderer. Existing levels keep
        /// their transition heights, new ones get the game's defaults; levels
        /// beyond LOD2 are left alone. Returns a line for the report.
        /// </summary>
        private static string SetUpLodGroup(string path, Mesh lod1, Mesh lod2)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try {
                MeshRenderer lod0 = Lod0Renderer(root, out string problem);
                if (lod0 == null) return "LODGroup not set up: " + problem;

                LODGroup group = root.GetComponentInChildren<LODGroup>(true);
                bool created = group == null;
                if (created) {
                    group = root.AddComponent<LODGroup>();
                    // As the game's prefabs: levels blend into each other.
                    group.fadeMode = LODFadeMode.CrossFade;
                    group.animateCrossFading = true;
                }
                // A new LODGroup starts with Unity's own levels and heights;
                // treated as having none, so it gets the game's.
                LOD[] old = created ? new LOD[0] : group.GetLODs();
                var lods = new LOD[Mathf.Max(old.Length, 3)];
                for (int i = 3; i < old.Length; i++) lods[i] = old[i];

                string baseName = lod0.name.EndsWith("_LOD0") ? lod0.name.Substring(0, lod0.name.Length - 5) : lod0.name;
                Mesh[] meshes = { null, lod1, lod2 };
                var made = new List<string>();
                for (int level = 0; level < 3; level++) {
                    MeshRenderer renderer = level == 0 ? lod0 : LevelRenderer(old, level, lod0, baseName, made);
                    if (level > 0) renderer.GetComponent<MeshFilter>().sharedMesh = meshes[level];
                    float height = level < old.Length ? old[level].screenRelativeTransitionHeight : _defaultHeights[level];
                    // Heights must fall level by level; a level added after an
                    // existing one may start out above it.
                    if (level > 0 && height >= lods[level - 1].screenRelativeTransitionHeight) {
                        height = lods[level - 1].screenRelativeTransitionHeight * 0.5f;
                    }
                    lods[level] = new LOD(height, new Renderer[] { renderer }) {
                        fadeTransitionWidth = level < old.Length ? old[level].fadeTransitionWidth : 0f
                    };
                }
                group.SetLODs(lods);
                group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                string children = made.Count > 0 ? $", made {string.Join(", ", made)}" : "";
                return $"LODGroup {(created ? "added" : "updated")}{children}";
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The MeshRenderer for LOD <paramref name="level"/>: the level's own
        /// first renderer if it has one with a MeshFilter (and it isn't LOD0's);
        /// else a child named &lt;base&gt;_LOD&lt;level&gt; beside LOD0's; else a new
        /// one made beside LOD0's (its name added to <paramref name="made"/>).
        /// </summary>
        private static MeshRenderer LevelRenderer(LOD[] old, int level, MeshRenderer lod0, string baseName, List<string> made)
        {
            if (level < old.Length) {
                foreach (Renderer renderer in old[level].renderers) {
                    if (renderer is MeshRenderer mesh && mesh != lod0 && mesh.GetComponent<MeshFilter>() != null) return mesh;
                }
            }
            // A renderer on the LODGroup's own object gets its levels as
            // children; otherwise they sit beside it.
            Transform parent = lod0.transform.parent != null ? lod0.transform.parent : lod0.transform;
            string name = $"{baseName}_LOD{level}";
            Transform existing = parent.Find(name);
            if (existing != null && existing.TryGetComponent(out MeshRenderer found) && existing.GetComponent<MeshFilter>() != null) {
                return found;
            }

            var copy = new GameObject(name);
            copy.transform.SetParent(parent, false);
            if (parent == lod0.transform) {
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;
            } else {
                copy.transform.localPosition = lod0.transform.localPosition;
                copy.transform.localRotation = lod0.transform.localRotation;
                copy.transform.localScale = lod0.transform.localScale;
            }
            copy.layer = lod0.gameObject.layer;
            copy.tag = lod0.gameObject.tag;
            GameObjectUtility.SetStaticEditorFlags(copy, GameObjectUtility.GetStaticEditorFlags(lod0.gameObject));
            copy.AddComponent<MeshFilter>();
            var copyRenderer = copy.AddComponent<MeshRenderer>();
            // Materials, shadows, light probe and lightmap settings as LOD0's.
            ComponentUtility.CopyComponent(lod0);
            ComponentUtility.PasteComponentValues(copyRenderer);
            made.Add(name);
            return copyRenderer;
        }

        /// <summary>
        /// The meshes to make LODs of: a selected mesh itself, or every mesh
        /// in a selected model that isn't already a lower LOD or a collision
        /// mesh of the pack's kind. <paramref name="baseName"/> is the model
        /// file's name, used when a model has only one mesh.
        /// </summary>
        private static List<Mesh> SourceMeshes(Object model, string modelPath, out string baseName)
        {
            baseName = Path.GetFileNameWithoutExtension(modelPath);
            var meshes = new List<Mesh>();
            if (model is Mesh single) {
                meshes.Add(single);
                return meshes;
            }
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath)) {
                if (asset is Mesh mesh && !IsLowerLod(mesh.name)) meshes.Add(mesh);
            }
            return meshes;
        }

        /// <summary>
        /// Whether a mesh name marks one of the pack's lower LODs or
        /// collision meshes (Medieval_Containers_Barrel_LOD1, ..._Collision).
        /// </summary>
        private static bool IsLowerLod(string meshName)
        {
            return meshName.EndsWith("_LOD1") || meshName.EndsWith("_LOD2") || meshName.EndsWith("_LOD3")
                || meshName.EndsWith("_Collision");
        }

        /// <summary>
        /// The name the LOD assets of <paramref name="source"/> start with,
        /// worked out from the file it lives in (see the overload below).
        /// </summary>
        private static string NameFor(Mesh source)
        {
            string path = AssetDatabase.GetAssetPath(source);
            return NameFor(source, Path.GetFileNameWithoutExtension(path), path);
        }

        /// <summary>
        /// The name the LOD assets of <paramref name="source"/> start with:
        /// the model file's name when it is the model's only mesh, otherwise
        /// the mesh's own name (minus any _LOD0), so each mesh gets its own set.
        /// </summary>
        private static string NameFor(Mesh source, string baseName, string modelPath)
        {
            int meshCount = 0;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath)) {
                if (asset is Mesh mesh && !IsLowerLod(mesh.name)) meshCount++;
            }
            if (meshCount <= 1) return baseName;
            string name = source.name;
            return name.EndsWith("_LOD0") ? name.Substring(0, name.Length - 5) : name;
        }

        /// <summary>
        /// The whole index buffer of <paramref name="mesh"/>: every level of
        /// every submesh, one after another (Mesh.GetLod gives each range).
        /// </summary>
        private static int[] ReadIndices(Mesh mesh)
        {
            using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(mesh)) {
                Mesh.MeshData meshData = data[0];
                int[] indices;
                if (meshData.indexFormat == IndexFormat.UInt16) {
                    var raw = meshData.GetIndexData<ushort>();
                    indices = new int[raw.Length];
                    for (int i = 0; i < raw.Length; i++) indices[i] = raw[i];
                } else {
                    var raw = meshData.GetIndexData<uint>();
                    indices = new int[raw.Length];
                    for (int i = 0; i < raw.Length; i++) indices[i] = (int)raw[i];
                }
                return indices;
            }
        }

        /// <summary>
        /// Triangles in level <paramref name="level"/> of a generated mesh,
        /// all submeshes together.
        /// </summary>
        private static int LevelTriangles(Mesh generated, int level)
        {
            int triangles = 0;
            for (int sub = 0; sub < generated.subMeshCount; sub++) {
                triangles += (int)generated.GetLod(sub, level).indexCount / 3;
            }
            return triangles;
        }

        /// <summary>
        /// The generated level whose triangle count is nearest
        /// <paramref name="ratio"/> of the original's, measured as a ratio
        /// (a level at half the target is as far off as one at double).
        /// </summary>
        private static int NearestLevel(Mesh generated, float ratio)
        {
            float target = Mathf.Max(1f, LevelTriangles(generated, 0) * ratio);
            int best = 0;
            float bestOff = float.MaxValue;
            for (int level = 0; level < generated.lodCount; level++) {
                float off = Mathf.Abs(Mathf.Log(Mathf.Max(1, LevelTriangles(generated, level)) / target));
                if (off < bestOff) {
                    bestOff = off;
                    best = level;
                }
            }
            return best;
        }

        /// <summary>
        /// Saves the generated level nearest <paramref name="ratio"/> as
        /// <paramref name="assetName"/>.asset in <paramref name="folder"/> -
        /// into the existing asset if there is one, so its references hold.
        /// Returns the saved asset.
        /// </summary>
        private static Mesh Save(Mesh source, Mesh generated, int[] indices, string assetName, string folder,
            float ratio, bool positionsOnly, StringBuilder report)
        {
            int level = NearestLevel(generated, ratio);
            string assetPath = Path.Combine(folder, assetName + ".asset").Replace('\\', '/');

            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (saved != null) {
                // Fill the old asset rather than replacing the file: the asset
                // keeps its GUID, so a prefab built on it still points at it.
                // Filled through the Mesh API, not CopySerialized: that swaps
                // the saved data but leaves the loaded mesh's GPU buffers as
                // they were, so the Editor drew the old triangles over the new
                // vertices (holes) until it was restarted.
                ExtractLevel(source, generated, indices, level, positionsOnly, saved);
                EditorUtility.SetDirty(saved);
            } else {
                saved = ExtractLevel(source, generated, indices, level, positionsOnly);
                saved.name = assetName;
                AssetDatabase.CreateAsset(saved, assetPath);
            }
            int triangles = saved.triangles.Length / 3;
            report.Append($" > {assetName.Substring(assetName.LastIndexOf('_') + 1)} {triangles} (level {level})");
            return saved;
        }

        /// <summary>
        /// A new mesh holding level <paramref name="level"/> of
        /// <paramref name="generated"/>: that level's triangles per submesh,
        /// and only the vertices they use, renumbered, with the source's
        /// normals, tangents, colours and every UV channel (lightmap UVs
        /// included) - or positions alone when <paramref name="positionsOnly"/>.
        /// Written into <paramref name="into"/> (cleared first) when given.
        /// </summary>
        private static Mesh ExtractLevel(Mesh source, Mesh generated, int[] indices, int level, bool positionsOnly,
            Mesh into = null)
        {
            // Old vertex number -> new, -1 while unused; vertices numbered in
            // the order the level's triangles first reach them.
            var remap = new int[source.vertexCount];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var used = new List<int>();
            var subTriangles = new List<int>[generated.subMeshCount];
            for (int sub = 0; sub < generated.subMeshCount; sub++) {
                MeshLodRange range = generated.GetLod(sub, level);
                int baseVertex = generated.GetSubMesh(sub).baseVertex;
                var triangles = new List<int>((int)range.indexCount);
                for (int i = 0; i < range.indexCount; i++) {
                    int old = indices[(int)range.indexStart + i] + baseVertex;
                    if (remap[old] < 0) {
                        remap[old] = used.Count;
                        used.Add(old);
                    }
                    triangles.Add(remap[old]);
                }
                subTriangles[sub] = triangles;
            }

            Mesh mesh = into != null ? into : new Mesh();
            mesh.Clear();
            mesh.indexFormat = used.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Pick(source.vertices, used));
            if (!positionsOnly) {
                if (source.HasVertexAttribute(VertexAttribute.Normal)) mesh.SetNormals(Pick(source.normals, used));
                if (source.HasVertexAttribute(VertexAttribute.Tangent)) mesh.SetTangents(Pick(source.tangents, used));
                if (source.HasVertexAttribute(VertexAttribute.Color)) mesh.SetColors(Pick(source.colors32, used));
                for (int channel = 0; channel < 8; channel++) {
                    var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    if (!source.HasVertexAttribute(attribute)) continue;
                    // Each channel keeps its own width (a lightmap UV stays two floats).
                    switch (source.GetVertexAttributeDimension(attribute)) {
                        case 2: {
                            var uvs = new List<Vector2>();
                            source.GetUVs(channel, uvs);
                            mesh.SetUVs(channel, Pick(uvs, used));
                            break;
                        }
                        case 3: {
                            var uvs = new List<Vector3>();
                            source.GetUVs(channel, uvs);
                            mesh.SetUVs(channel, Pick(uvs, used));
                            break;
                        }
                        default: {
                            var uvs = new List<Vector4>();
                            source.GetUVs(channel, uvs);
                            mesh.SetUVs(channel, Pick(uvs, used));
                            break;
                        }
                    }
                }
            }
            mesh.subMeshCount = subTriangles.Length;
            for (int sub = 0; sub < subTriangles.Length; sub++) mesh.SetTriangles(subTriangles[sub], sub, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The values of <paramref name="values"/> at the positions listed
        /// in <paramref name="used"/>, in that order.
        /// </summary>
        private static List<T> Pick<T>(IList<T> values, List<int> used)
        {
            var picked = new List<T>(used.Count);
            foreach (int index in used) picked.Add(values[index]);
            return picked;
        }
    }
}
