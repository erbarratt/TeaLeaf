using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Level Gen > Scan Prefab Catalogue)
    /// that measures every prefab under Assets/Prefabs/Environment and writes what it
    /// finds to Assets/Data/LevelGen/catalogue.json: bounds, pivot, hierarchy,
    /// components, lights, cost, and flags for anything that needs a look by
    /// eye before the generator trusts it.
    ///
    /// Read-only towards assets: each prefab is opened with
    /// LoadPrefabContents (an isolated preview scene, nothing in the open
    /// scene changes) and unloaded again without being saved. Run it again
    /// whenever prefabs change; the file is only rewritten if something in
    /// it changed, and it never holds anything typed by hand.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class PrefabCatalogueScanner
    {
        private const string PrefabsRoot = "Assets/Prefabs";
        // Only level art is catalogued: gameplay prefabs (Interactive, Core, ...)
        // live outside this folder. Entries' folders stay relative to
        // PrefabsRoot ("Environment/Walls").
        private const string ScanRoot = PrefabsRoot + "/Environment";

        // The pack's ready-made buildings and building storeys. The generator
        // builds from component parts only, so these are left out of the
        // catalogue altogether.
        private static readonly string[] _excludedFolders = {
            ScanRoot + "/Building_Exteriors/",
            ScanRoot + "/Building_Layers/"
        };
        internal const string DataFolder = "Assets/Data/LevelGen";
        internal const string CataloguePath = DataFolder + "/catalogue.json";
        private const string EnvironmentLayerName = "Environment";

        // How far (metres) the pivot may sit from the bottom of the render box,
        // or outside its footprint, before it's flagged. Pack pieces are meant
        // to stand on their pivot, so more than this needs checking.
        private const float PivotTolerance = 0.05f;

        // A renderer counts as turned when one of its axes is more than about
        // 2.5 degrees off every axis of the root: its box then no longer fits
        // it tightly, so the measured size is too big.
        private const float AxisAlignedDot = 0.999f;

        // Flags written to CatalogueEntry.flags.
        internal const string FlagNotVariant = "not-a-variant";
        internal const string FlagNoRenderBounds = "no-render-bounds";
        internal const string FlagNoColliders = "no-colliders";
        internal const string FlagHasLights = "has-lights";
        internal const string FlagRotatedPieces = "rotated-pieces";
        internal const string FlagUnevenScale = "uneven-scale";
        internal const string FlagMirrored = "mirrored";
        internal const string FlagPivotNotOnBase = "pivot-not-on-base";
        internal const string FlagPivotOutsideFootprint = "pivot-outside-footprint";
        internal const string FlagOffLayer = "off-environment-layer";
        internal const string FlagNotStatic = "not-batching-static";
        internal const string FlagMissingMesh = "missing-mesh";
        internal const string FlagMissingScript = "missing-script";
        internal const string FlagSharesSource = "shares-source";
        internal const string FlagDuplicateName = "duplicate-name";
        internal const string FlagGameplayScripts = "has-gameplay-scripts";
        internal const string FlagAssembly = "assembly";
        internal const string FlagFrontUnsure = "front-unsure";
        internal const string FlagNoMeshData = "no-mesh-data";

        // Scripts that are part of a static piece's set-up rather than gameplay.
        // Anything else on a prefab (Door, Ladder, ...) marks it as gameplay,
        // which the generator never places.
        private static readonly HashSet<string> _setUpScripts = new(StringComparer.Ordinal) {
            "SurfaceTag", "RampCollider", "BarrelCollider"
        };

        // Unity's "copy" suffix: "Name 1", "Name 2".
        private static readonly Regex _copySuffix = new(@" \d+$");

        // A JSON string (kept as it is, so names and paths are never touched) or
        // a number (group "number"; whole numbers come back unchanged).
        private static readonly Regex _decimalNumber = new(@"""(?:[^""\\]|\\.)*""|(?<number>-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)");

        [MenuItem("TeaLeaf/Level Gen/Scan Prefab Catalogue")]
        internal static void Scan()
        {
            if (!AssetDatabase.IsValidFolder(ScanRoot)) {
                Debug.LogWarning($"PrefabCatalogueScanner: there is no '{ScanRoot}' folder.");
                return;
            }

            int environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);
            List<string> paths = FindPrefabs();
            PrefabCatalogue catalogue = new() { unityVersion = Application.unityVersion };

            try {
                for (int i = 0; i < paths.Count; i++) {
                    EditorUtility.DisplayProgressBar("Scanning prefab catalogue", paths[i], (float)i / paths.Count);
                    catalogue.prefabs.Add(Measure(paths[i], environmentLayer));
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }

            FlagSharedSources(catalogue.prefabs);
            catalogue.prefabCount = catalogue.prefabs.Count;
            bool written = Write(catalogue);
            Debug.Log(Summary(catalogue, written));
        }

        /// <summary>
        /// Every prefab under Assets/Prefabs/Environment but the excluded folders,
        /// sorted by path so the catalogue's order is the same on every scan.
        /// </summary>
        private static List<string> FindPrefabs()
        {
            List<string> paths = new();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ScanRoot })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!IsExcluded(path)) {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>
        /// Whether a prefab is in one of the folders the catalogue leaves out.
        /// </summary>
        private static bool IsExcluded(string path)
        {
            foreach (string folder in _excludedFolders) {
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Opens one prefab in a preview scene, measures it and unloads it
        /// again. Nothing is saved.
        /// </summary>
        private static CatalogueEntry Measure(string path, int environmentLayer)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            CatalogueEntry entry = new() {
                name = asset.name,
                path = path,
                guid = AssetDatabase.AssetPathToGUID(path),
                folder = Path.GetDirectoryName(path).Replace('\\', '/').Substring(PrefabsRoot.Length).TrimStart('/'),
                isVariant = PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant
            };

            if (entry.isVariant) {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(asset);
                entry.sourcePrefab = source != null ? AssetDatabase.GetAssetPath(source) : "";
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try {
                Transform rootTransform = root.transform;
                entry.rootEuler = Round(rootTransform.localEulerAngles);
                entry.rootScale = Round(rootTransform.localScale);
                Matrix4x4 toRoot = rootTransform.worldToLocalMatrix;

                MeasureHierarchy(entry, rootTransform, environmentLayer);
                List<WallOrientation.DrawnMesh> drawn = MeasureRenderers(entry, root, toRoot);
                WallOrientation.Side[] sides = entry.hasRenderBounds ? WallOrientation.Suggest(entry, drawn) : null;
                MeasureColliders(entry, root, toRoot);
                WalkSurface.Measure(entry, root, toRoot);

                // Category, assumed +Z front, openings and the shape's points:
                // after the colliders, as floors are measured by them.
                if (entry.hasRenderBounds) {
                    PieceShape.Measure(entry, sides);
                }

                MeasureLights(entry, root, rootTransform);
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AddFlags(entry);
            return entry;
        }

        /// <summary>
        /// Object count, depth, top-level children, component types, the
        /// outermost nested prefabs, and every object's layer, static flags
        /// and missing scripts.
        /// </summary>
        private static void MeasureHierarchy(CatalogueEntry entry, Transform root, int environmentLayer)
        {
            Dictionary<string, int> components = new(StringComparer.Ordinal);
            Dictionary<string, int> nested = new(StringComparer.Ordinal);

            foreach (Transform child in root) {
                entry.topChildren.Add(child.name);
            }

            Walk(root, 0, false);
            entry.components = ToSortedList(components);
            entry.nestedPrefabs = ToSortedList(nested);
            entry.gameplayScripts.Sort(StringComparer.Ordinal);

            void Walk(Transform t, int depth, bool insideNested)
            {
                GameObject go = t.gameObject;
                entry.objectCount++;
                entry.maxDepth = Mathf.Max(entry.maxDepth, depth);

                if (environmentLayer >= 0 && go.layer != environmentLayer) {
                    entry.offLayerObjects++;
                }

                if ((GameObjectUtility.GetStaticEditorFlags(go) & StaticEditorFlags.BatchingStatic) == 0) {
                    entry.nonStaticObjects++;
                }

                entry.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);

                foreach (Component component in go.GetComponents<Component>()) {
                    // A missing script shows up as a null component; it's counted above.
                    if (component == null || component is Transform) {
                        continue;
                    }

                    string typeName = component.GetType().Name;
                    components.TryGetValue(typeName, out int count);
                    components[typeName] = count + 1;

                    if (component is MonoBehaviour && !_setUpScripts.Contains(typeName) && !entry.gameplayScripts.Contains(typeName)) {
                        entry.gameplayScripts.Add(typeName);
                    }
                }

                // Only the outermost nested prefabs are listed: a nested house
                // counts once, not once per wall inside it.
                bool isNestedRoot = depth > 0 && !insideNested && PrefabUtility.IsAnyPrefabInstanceRoot(go);

                if (isNestedRoot) {
                    string nestedPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    nested.TryGetValue(nestedPath, out int count);
                    nested[nestedPath] = count + 1;
                }

                foreach (Transform child in t) {
                    Walk(child, depth + 1, insideNested || isNestedRoot);
                }
            }
        }

        /// <summary>
        /// The render box and the cost of what is drawn up close: LOD0
        /// renderers, plus any renderer no LODGroup lists. Inactive objects
        /// and disabled renderers are left out, as they aren't drawn. Returns
        /// those meshes, with their matrices into root space, for the
        /// orientation suggestion.
        /// </summary>
        private static List<WallOrientation.DrawnMesh> MeasureRenderers(CatalogueEntry entry, GameObject root, Matrix4x4 toRoot)
        {
            List<WallOrientation.DrawnMesh> drawn = new();
            HashSet<Renderer> lowerLods = new();
            LODGroup[] lodGroups = root.GetComponentsInChildren<LODGroup>(true);
            entry.lodGroupCount = lodGroups.Length;

            foreach (LODGroup lodGroup in lodGroups) {
                LOD[] lods = lodGroup.GetLODs();
                entry.lodLevels = Mathf.Max(entry.lodLevels, lods.Length);

                for (int i = 1; i < lods.Length; i++) {
                    foreach (Renderer renderer in lods[i].renderers) {
                        if (renderer != null) {
                            lowerLods.Add(renderer);
                        }
                    }
                }
            }

            // A renderer listed in LOD0 of one group and a lower LOD of another
            // is still drawn up close.
            foreach (LODGroup lodGroup in lodGroups) {
                LOD[] lods = lodGroup.GetLODs();

                if (lods.Length > 0) {
                    foreach (Renderer renderer in lods[0].renderers) {
                        lowerLods.Remove(renderer);
                    }
                }
            }

            HashSet<Material> materials = new();
            Bounds box = default;

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true)) {
                if (lowerLods.Contains(renderer) || !renderer.enabled || !renderer.gameObject.activeInHierarchy) {
                    continue;
                }

                if (!renderer.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null) {
                    entry.missingMeshes++;
                    continue;
                }

                Mesh mesh = meshFilter.sharedMesh;
                Matrix4x4 matrix = toRoot * renderer.transform.localToWorldMatrix;
                Encapsulate(ref box, ref entry.hasRenderBounds, matrix, mesh.bounds);
                CheckAxes(entry, matrix);
                drawn.Add(new WallOrientation.DrawnMesh(mesh, matrix));
                entry.renderersLod0++;

                for (int i = 0; i < mesh.subMeshCount; i++) {
                    if (mesh.GetTopology(i) == MeshTopology.Triangles) {
                        entry.trianglesLod0 += (int)(mesh.GetIndexCount(i) / 3);
                    }
                }

                foreach (Material material in renderer.sharedMaterials) {
                    if (material != null) {
                        materials.Add(material);
                    }
                }
            }

            entry.materialsLod0 = materials.Count;

            if (!entry.hasRenderBounds) {
                return drawn;
            }

            entry.renderMin = Round(box.min);
            entry.renderMax = Round(box.max);
            entry.renderSize = Round(box.size);
            entry.pivotInBounds = Round(new Vector3(
                Fraction(0f, box.min.x, box.size.x),
                Fraction(0f, box.min.y, box.size.y),
                Fraction(0f, box.min.z, box.size.z)));
            return drawn;
        }

        /// <summary>
        /// Counts a renderer that is turned off the root's axes, scaled
        /// unevenly or mirrored, from the matrix that takes it into the
        /// root's space.
        /// </summary>
        private static void CheckAxes(CatalogueEntry entry, Matrix4x4 matrix)
        {
            Vector3 x = matrix.GetColumn(0);
            Vector3 y = matrix.GetColumn(1);
            Vector3 z = matrix.GetColumn(2);

            if (!IsAxisAligned(x) || !IsAxisAligned(y) || !IsAxisAligned(z)) {
                entry.rotatedRenderers++;
            }

            float shortest = Mathf.Min(x.magnitude, Mathf.Min(y.magnitude, z.magnitude));
            float longest = Mathf.Max(x.magnitude, Mathf.Max(y.magnitude, z.magnitude));

            if (shortest > 0f && longest / shortest > 1.001f) {
                entry.unevenScaleRenderers++;
            }

            if (matrix.determinant < 0f) {
                entry.mirroredRenderers++;
            }
        }

        /// <summary>
        /// Whether a direction lies along one of the root's axes, either way.
        /// </summary>
        private static bool IsAxisAligned(Vector3 axis)
        {
            Vector3 n = axis.normalized;
            return Mathf.Abs(n.x) > AxisAlignedDot || Mathf.Abs(n.y) > AxisAlignedDot || Mathf.Abs(n.z) > AxisAlignedDot;
        }

        /// <summary>
        /// The collider box (enabled, non-trigger colliders on active
        /// objects) and a count of collider kinds. Worked out from each
        /// collider's own shape rather than Collider.bounds, which isn't
        /// kept up to date in a preview scene.
        /// </summary>
        private static void MeasureColliders(CatalogueEntry entry, GameObject root, Matrix4x4 toRoot)
        {
            Dictionary<string, int> kinds = new(StringComparer.Ordinal);
            Bounds box = default;

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy) {
                    continue;
                }

                string kind = collider.GetType().Name;
                kinds.TryGetValue(kind, out int count);
                kinds[kind] = count + 1;

                if (collider.isTrigger) {
                    entry.triggerColliders++;
                    continue;
                }

                if (!TryGetLocalShape(collider, out Bounds local)) {
                    continue;
                }

                Matrix4x4 matrix = toRoot * collider.transform.localToWorldMatrix;
                Encapsulate(ref box, ref entry.hasColliderBounds, matrix, local);
            }

            entry.colliders = ToSortedList(kinds);

            if (entry.hasColliderBounds) {
                entry.colliderMin = Round(box.min);
                entry.colliderMax = Round(box.max);
                entry.colliderSize = Round(box.size);
            }
        }

        /// <summary>
        /// A collider's shape as a box in its own object's space. Spheres
        /// and capsules give the box round them; under uneven scale Unity
        /// grows a sphere by the largest axis, so that box is a little small.
        /// A mesh collider with no mesh counts as a missing mesh.
        /// </summary>
        private static bool TryGetLocalShape(Collider collider, out Bounds local)
        {
            switch (collider) {
                case BoxCollider boxCollider:
                    local = new Bounds(boxCollider.center, boxCollider.size);
                    return true;
                case SphereCollider sphere:
                    local = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                    return true;
                case CapsuleCollider capsule: {
                    Vector3 size = Vector3.one * (capsule.radius * 2f);
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                    local = new Bounds(capsule.center, size);
                    return true;
                }
                case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                    local = meshCollider.sharedMesh.bounds;
                    return true;
                default:
                    local = default;
                    return false;
            }
        }

        /// <summary>
        /// Every Light in the prefab, enabled or not, with its path from the
        /// root, type, bake mode, shadows and range.
        /// </summary>
        private static void MeasureLights(CatalogueEntry entry, GameObject root, Transform rootTransform)
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true)) {
                entry.lights.Add(new LightInfo {
                    objectPath = AnimationUtility.CalculateTransformPath(light.transform, rootTransform),
                    type = light.type.ToString(),
                    mode = light.lightmapBakeType.ToString(),
                    shadows = light.shadows.ToString(),
                    range = Round(light.range),
                    enabled = light.enabled && light.gameObject.activeInHierarchy
                });
            }
        }

        /// <summary>
        /// Adds the flags that one entry's own measurements call for.
        /// </summary>
        private static void AddFlags(CatalogueEntry entry)
        {
            List<string> flags = entry.flags;

            if (!entry.isVariant) {
                flags.Add(FlagNotVariant);
            }

            if (!entry.hasRenderBounds) {
                flags.Add(FlagNoRenderBounds);
            } else {
                if (Mathf.Abs(entry.renderMin.y) > PivotTolerance) {
                    flags.Add(FlagPivotNotOnBase);
                }

                bool outsideX = entry.renderMin.x > PivotTolerance || entry.renderMax.x < -PivotTolerance;
                bool outsideZ = entry.renderMin.z > PivotTolerance || entry.renderMax.z < -PivotTolerance;

                if (outsideX || outsideZ) {
                    flags.Add(FlagPivotOutsideFootprint);
                }
            }

            if (!entry.hasColliderBounds) {
                flags.Add(FlagNoColliders);
            }

            if (entry.lights.Count > 0) {
                flags.Add(FlagHasLights);
            }

            if (entry.rotatedRenderers > 0) {
                flags.Add(FlagRotatedPieces);
            }

            if (entry.unevenScaleRenderers > 0) {
                flags.Add(FlagUnevenScale);
            }

            if (entry.mirroredRenderers > 0) {
                flags.Add(FlagMirrored);
            }

            if (entry.offLayerObjects > 0) {
                flags.Add(FlagOffLayer);
            }

            if (entry.nonStaticObjects > 0) {
                flags.Add(FlagNotStatic);
            }

            if (entry.missingMeshes > 0) {
                flags.Add(FlagMissingMesh);
            }

            if (entry.missingScripts > 0) {
                flags.Add(FlagMissingScript);
            }

            if (_copySuffix.IsMatch(entry.name)) {
                flags.Add(FlagDuplicateName);
            }

            if (entry.gameplayScripts.Count > 0) {
                flags.Add(FlagGameplayScripts);
            }

            // Made of other prefabs (a chimney stack, a fixture group) rather than
            // being one part: the generator builds from single parts, so an
            // assembly is only used if it is marked so by hand.
            if (entry.nestedPrefabs.Count > 0) {
                flags.Add(FlagAssembly);
            }

            // A slab whose two big sides couldn't be told apart, so its front
            // has to be set by eye.
            if (entry.orientation == WallOrientation.KindUnsure) {
                flags.Add(FlagFrontUnsure);
            }

            if (entry.orientation == WallOrientation.KindNoMeshData) {
                flags.Add(FlagNoMeshData);
            }
        }

        /// <summary>
        /// Flags every variant whose source prefab also has another variant,
        /// so only one of them is used.
        /// </summary>
        private static void FlagSharedSources(List<CatalogueEntry> entries)
        {
            Dictionary<string, int> sourceCounts = new(StringComparer.Ordinal);

            foreach (CatalogueEntry entry in entries) {
                if (!string.IsNullOrEmpty(entry.sourcePrefab)) {
                    sourceCounts.TryGetValue(entry.sourcePrefab, out int count);
                    sourceCounts[entry.sourcePrefab] = count + 1;
                }
            }

            foreach (CatalogueEntry entry in entries) {
                if (!string.IsNullOrEmpty(entry.sourcePrefab) && sourceCounts[entry.sourcePrefab] > 1) {
                    entry.flags.Add(FlagSharesSource);
                }
            }
        }

        /// <summary>
        /// Writes the catalogue as JSON, only if it differs from the file
        /// already there. Returns whether it wrote.
        /// </summary>
        private static bool Write(PrefabCatalogue catalogue)
        {
            EnsureFolder(DataFolder);
            string json = ShortenNumbers(JsonUtility.ToJson(catalogue, true));

            if (File.Exists(CataloguePath) && File.ReadAllText(CataloguePath) == json) {
                return false;
            }

            File.WriteAllText(CataloguePath, json);
            AssetDatabase.ImportAsset(CataloguePath);
            return true;
        }

        /// <summary>
        /// JsonUtility writes a float's full double expansion (0.3 becomes
        /// 0.30000001192092898). Every number in the catalogue is already
        /// rounded to the millimetre, so each is written back with at most
        /// three decimals.
        /// </summary>
        internal static string ShortenNumbers(string json)
        {
            return _decimalNumber.Replace(json, match => {
                if (!match.Groups["number"].Success) {
                    return match.Value;
                }

                double value = double.Parse(match.Value, CultureInfo.InvariantCulture);
                // Adding 0 turns -0 into 0.
                return (Math.Round(value, 3) + 0d).ToString("0.###", CultureInfo.InvariantCulture);
            });
        }

        /// <summary>
        /// One Console entry: how many prefabs, how many flagged, and a
        /// count per flag, suggested orientation, suggested category and folder.
        /// </summary>
        private static string Summary(PrefabCatalogue catalogue, bool written)
        {
            Dictionary<string, int> flagCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> folderCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> orientationCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> categoryCounts = new(StringComparer.Ordinal);
            int flagged = 0;

            foreach (CatalogueEntry entry in catalogue.prefabs) {
                folderCounts.TryGetValue(entry.folder, out int inFolder);
                folderCounts[entry.folder] = inFolder + 1;
                string orientation = string.IsNullOrEmpty(entry.orientation) ? "none" : entry.orientation;
                orientationCounts.TryGetValue(orientation, out int withOrientation);
                orientationCounts[orientation] = withOrientation + 1;
                string category = string.IsNullOrEmpty(entry.suggestedCategory) ? "none" : entry.suggestedCategory;
                categoryCounts.TryGetValue(category, out int inCategory);
                categoryCounts[category] = inCategory + 1;

                if (entry.flags.Count > 0) {
                    flagged++;
                }

                foreach (string flag in entry.flags) {
                    flagCounts.TryGetValue(flag, out int count);
                    flagCounts[flag] = count + 1;
                }
            }

            System.Text.StringBuilder text = new();
            text.Append($"PrefabCatalogueScanner: {catalogue.prefabCount} prefab(s), {flagged} flagged; ");
            text.Append(written ? $"wrote '{CataloguePath}'." : $"'{CataloguePath}' unchanged.");
            text.Append("\nFlags:");

            foreach (NamedCount flag in ToSortedList(flagCounts)) {
                text.Append($"\n  {flag.name}: {flag.count}");
            }

            text.Append("\nSuggested orientation:");

            foreach (NamedCount orientation in ToSortedList(orientationCounts)) {
                text.Append($"\n  {orientation.name}: {orientation.count}");
            }

            text.Append("\nSuggested category:");

            foreach (NamedCount category in ToSortedList(categoryCounts)) {
                text.Append($"\n  {category.name}: {category.count}");
            }

            text.Append("\nFolders:");

            foreach (NamedCount folder in ToSortedList(folderCounts)) {
                text.Append($"\n  {folder.name}: {folder.count}");
            }

            return text.ToString();
        }

        /// <summary>
        /// Grows box (or starts it, when hasBox is false) to take in the eight
        /// corners of local, taken through matrix.
        /// </summary>
        private static void Encapsulate(ref Bounds box, ref bool hasBox, Matrix4x4 matrix, Bounds local)
        {
            Vector3 min = local.min;
            Vector3 max = local.max;

            for (int i = 0; i < 8; i++) {
                Vector3 corner = new(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                Vector3 point = matrix.MultiplyPoint3x4(corner);

                if (hasBox) {
                    box.Encapsulate(point);
                } else {
                    box = new Bounds(point, Vector3.zero);
                    hasBox = true;
                }
            }
        }

        /// <summary>
        /// Where value sits between min and min + size, as 0-1 (outside that
        /// range when it's outside the box). 0.5 for a box with no size.
        /// </summary>
        private static float Fraction(float value, float min, float size)
        {
            return size > 0.0001f ? (value - min) / size : 0.5f;
        }

        /// <summary>
        /// A name-count dictionary as a list sorted by name, for JsonUtility
        /// and a stable file.
        /// </summary>
        private static List<NamedCount> ToSortedList(Dictionary<string, int> counts)
        {
            List<NamedCount> list = new(counts.Count);

            foreach (KeyValuePair<string, int> pair in counts) {
                list.Add(new NamedCount { name = pair.Key, count = pair.Value });
            }

            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        /// <summary>
        /// Rounds to the millimetre, so floating-point noise doesn't change
        /// the file between scans.
        /// </summary>
        private static float Round(float value)
        {
            // Adding 0 turns -0 into 0, so the file never says "-0.0".
            return Mathf.Round(value * 1000f) / 1000f + 0f;
        }

        /// <summary>
        /// Rounds each axis to the millimetre.
        /// </summary>
        private static Vector3 Round(Vector3 value)
        {
            return new Vector3(Round(value.x), Round(value.y), Round(value.z));
        }

        /// <summary>
        /// Makes an asset folder, and any folders above it, if missing.
        /// Through AssetDatabase so each gets its .meta file straight away.
        /// </summary>
        internal static void EnsureFolder(string folder)
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
