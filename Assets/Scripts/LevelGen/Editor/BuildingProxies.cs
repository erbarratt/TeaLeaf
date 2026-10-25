using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGen
{
    /// <summary>
    /// Each building seen from further off as one renderer (menu Rebuild
    /// Building Proxies, after a generation and the open window interiors).
    /// A building's pieces stand under two groups - its walls, foundations,
    /// beams and dressing under Facades (or Lots)/[owner]/Building i, its
    /// roofs, chimneys and dormers under Roofs/[owner]/Building i - plus any
    /// open window standing in it and the room behind it, outside the
    /// generated root (OpenWindowInteriors); each piece carries the pack's own LODGroup
    /// (or several, one per nested part), switching at its own small size.
    /// The building switches for the whole building at once: up close every
    /// piece's full detail, piece by piece (each culled on its own,
    /// lightmapped with the pack's own UVs); further off every piece's second
    /// level, merged into one mesh (a "Proxy" child), down to where the pack
    /// would cull its pieces. The pack's lowest level is left out: it has
    /// barely fewer triangles than the second (about a tenth), not worth a
    /// second merged mesh and lightmap. The merged mesh draws from the
    /// sector's texture atlas (ProxyAtlas) wherever it can, one sub-mesh
    /// for all of that, plus one per material kept apart, so the same
    /// colours and silhouette - lean, jetties, chimneys - from one renderer
    /// in a few draws instead of hundreds.
    ///
    /// Done with one LODGroup per building on its facade group: level 0 every
    /// piece's full-detail renderers (and those in no LODGroup), level 1 the
    /// proxy (a piece's full detail where it has one level; those in no
    /// LODGroup too).
    /// A renderer can belong to one LODGroup only, so the pieces' own
    /// LODGroups are removed and every renderer of theirs but the full
    /// detail switched off (overrides on the instances; the prefabs
    /// untouched). The proxy is lightmapped at its own share of the level's
    /// resolution (lightmap UVs unwrapped here), so it shows the baked
    /// shadows the full detail casts, where probes out in the streets would
    /// give flat, blotchy light. Its mesh is saved under Folder, named for
    /// the sector, owner and building; the sector's old ones are deleted
    /// each generation.
    /// </summary>
    internal static class BuildingProxies
    {
        internal const string Folder = PrefabCatalogueScanner.DataFolder + "/Proxies";
        private const string ProxyName = "Proxy";

        // The proxies' own Lightmap Parameters (bake settings), made here
        // when missing; its pushoff is set from the settings each build.
        private const string ParametersPath = PrefabCatalogueScanner.DataFolder + "/ProxyLightmapParameters.asset";

        // Vertices this close (metres) are one for the lightmap unwrap.
        private const float WeldDistance = 0.001f;

        // Static for the bake (as a lower level of detail it is lightmapped
        // but casts no light or shadow into the bake: the full detail does),
        // occlusion culling (culled, the full detail occludes) and reflection
        // probes; not batching static, being one renderer already.
        private const StaticEditorFlags StaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        // The level's lightmap resolution (texels per metre) when the scene
        // has no Lighting Settings asset to read it from.
        private const float DefaultLightmapResolution = 10f;

        // The gap left round each lightmap chart, in lightmap texels at the
        // mesh's own resolution, so a chart's light doesn't bleed into the
        // next one's.
        private const float ChartMarginTexels = 1.5f;

        /// <summary>
        /// One building's renderers: those drawn up close, and those merged
        /// into its proxy.
        /// </summary>
        private class Building
        {
            public Transform Group;
            public Transform Roofs;
            // Open windows standing in the building and their interiors,
            // outside the generated root (OpenWindowInteriors).
            public readonly List<Transform> Extras = new();
            public readonly List<Renderer> Near = new();
            public readonly List<MeshRenderer> Merged = new();
            // The pack's fading, copied from its first LODGroup.
            public LODFadeMode FadeMode;
            public bool Animate;
        }

        /// <summary>
        /// Builds the proxies again on a level already generated (menu
        /// TeaLeaf > Level Gen > Rebuild Building Proxies), for the selected
        /// Generated root, else every one in the open scene: after facades
        /// were changed by hand, without generating the level again. Each
        /// building is first put back as placed (Restore), then built as a
        /// generation builds it. Not undoable (the old meshes are deleted);
        /// a rebake follows.
        /// </summary>
        [MenuItem("TeaLeaf/Level Gen/Rebuild Building Proxies")]
        private static void RebuildSelected()
        {
            GeneratedRoot[] roots = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentsInParent<GeneratedRoot>(true)
                : Object.FindObjectsByType<GeneratedRoot>(FindObjectsSortMode.None);

            if (roots.Length == 0) {
                Debug.LogWarning("BuildingProxies: select something under a Generated root, or have one in the scene.");
                return;
            }

            GeneratorSettings settings = GeneratorSettings.Load();

            foreach (GeneratedRoot root in roots) {
                // Generated sits under Environment/Sectors/<sector>/Geometry.
                string sector = root.transform.parent.parent.name;
                int restored = 0;

                foreach (Building building in Gather(root.transform)) {
                    restored += Restore(building);
                }

                List<string> log = new();
                Build(sector, root.transform, settings, log);
                // The proxies are the generator's own work: recorded in the
                // fingerprint, so a regeneration doesn't take them for hand edits.
                root.Record(root.SpecPath, LevelGenerator.Fingerprint(root.transform));
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
                Debug.Log($"BuildingProxies: rebuilt '{sector}', {restored} piece LODGroup(s) given back first.\n{string.Join("\n", log)}\nRebake the lighting (and occlusion) to light them.");
            }
        }

        /// <summary>
        /// Puts a building back as placed, before Build folded it in: its own
        /// LODGroup and merged children removed, its pieces' LODGroups given
        /// back from their prefabs and their renderers switched on or off as
        /// the prefabs have them. Pieces added by hand since come with their
        /// LODGroups and are left as they are. Gives back how many LODGroups
        /// came back.
        /// </summary>
        private static int Restore(Building building)
        {
            if (building.Group.TryGetComponent(out LODGroup own)) {
                Object.DestroyImmediate(own);
            }

            Transform proxy = building.Group.Find(ProxyName);

            if (proxy != null) {
                Object.DestroyImmediate(proxy.gameObject);
            }

            int restored = RestorePieces(building.Group);

            if (building.Roofs != null) {
                restored += RestorePieces(building.Roofs);
            }

            foreach (Transform extra in building.Extras) {
                restored += RestorePieces(extra);
            }

            return restored;
        }

        /// <summary>
        /// Gives back every LODGroup removed from the placed pieces under a
        /// group (each piece is a prefab instance; the removal is an override
        /// on it), then reverts every override of the switch of a renderer
        /// one of the pieces' LODGroups holds.
        /// </summary>
        private static int RestorePieces(Transform tree)
        {
            int restored = 0;

            foreach (Transform transform in tree.GetComponentsInChildren<Transform>(true)) {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(transform.gameObject)) {
                    continue;
                }

                foreach (RemovedComponent removed in PrefabUtility.GetRemovedComponents(transform.gameObject)) {
                    if (removed.assetComponent is LODGroup) {
                        removed.Revert(InteractionMode.AutomatedAction);
                        restored++;
                    }
                }
            }

            foreach (LODGroup group in tree.GetComponentsInChildren<LODGroup>(true)) {
                foreach (LOD lod in group.GetLODs()) {
                    foreach (Renderer renderer in lod.renderers) {
                        if (renderer == null) {
                            continue;
                        }

                        SerializedProperty enabled = new SerializedObject(renderer).FindProperty("m_Enabled");

                        if (enabled.prefabOverride) {
                            PrefabUtility.RevertPropertyOverride(enabled, InteractionMode.AutomatedAction);
                        }
                    }
                }
            }

            return restored;
        }

        /// <summary>
        /// Gives every building under the generated root its proxy, if the
        /// settings ask for them (the sector's old proxy meshes deleted
        /// first). Adds one line to log.
        /// </summary>
        internal static void Build(string sector, Transform root, GeneratorSettings settings, List<string> log)
        {
            string prefix = Safe($"{sector.Trim()} - ");
            DeleteOld(prefix);

            if (!settings.BuildingProxies) {
                log.Add("Building proxies: none (off in the settings).");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Folder)) {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));
            }

            List<Building> buildings = Gather(root);
            int layer = LayerMask.NameToLayer("Environment");
            float resolution = Lightmapping.TryGetLightingSettings(out LightingSettings lighting) ? lighting.lightmapResolution : DefaultLightmapResolution;
            LightmapParameters parameters = ProxyParameters(settings.ProxyBakePushoff);
            int made = 0;
            int pieceGroups = 0;
            long triangles = 0;
            long charts = 0;
            double texels = 0;
            int subMeshes = 0;
            int keptTriangles = 0;

            // Every building split first, so the atlas is built (and
            // imported) from all the materials the proxies will use before
            // the meshes are saved.
            List<Material> materials = new();
            HashSet<Material> seen = new();

            foreach (Building building in buildings) {
                pieceGroups += Split(building, out building.FadeMode, out building.Animate);

                foreach (MeshRenderer renderer in building.Merged) {
                    foreach (Material material in renderer.sharedMaterials) {
                        if (material != null && seen.Add(material)) {
                            materials.Add(material);
                        }
                    }
                }
            }

            ProxyAtlas atlas = ProxyAtlas.Build(Folder, prefix, materials, settings);

            try {
                AssetDatabase.StartAssetEditing();

                foreach (Building building in buildings) {
                    if (building.Near.Count == 0 || building.Merged.Count == 0) {
                        continue;
                    }

                    string name = $"{sector.Trim()} -{building.Group.parent.name} {building.Group.name}";
                    MeshRenderer proxy = AddProxy(building.Merged, building.Group, name, atlas, settings.ProxyLightmapScale, resolution, parameters, layer, out int meshCharts, out float area, out int meshKept);

                    // The building's own LODGroup: its full detail down to
                    // LowerDetailShare of the view's height, then the proxy
                    // down to ProxyCullShare, below which nothing is drawn.
                    // Each switch must come below the one before.
                    LODGroup group = building.Group.gameObject.AddComponent<LODGroup>();
                    group.fadeMode = building.FadeMode;
                    group.animateCrossFading = building.Animate;
                    group.SetLODs(new[] {
                        new LOD(Mathf.Max(settings.LowerDetailShare, settings.ProxyCullShare + 0.001f), building.Near.ToArray()),
                        new LOD(settings.ProxyCullShare, new Renderer[] { proxy })
                    });
                    group.RecalculateBounds();

                    made++;
                    Mesh mesh = proxy.GetComponent<MeshFilter>().sharedMesh;
                    triangles += TriangleCount(mesh);
                    subMeshes += mesh.subMeshCount;
                    charts += meshCharts;
                    keptTriangles += meshKept;
                    texels += area * Mathf.Pow(settings.ProxyLightmapScale * resolution, 2f);
                }
            } finally {
                AssetDatabase.StopAssetEditing();
            }

            string atlasLine = atlas == null
                ? "no atlas (no material can go in one)"
                : $"{atlas.Tiles} material(s) in a {settings.ProxyAtlasSize} atlas of {settings.ProxyAtlasTile} tiles ({settings.ProxyAtlasPadding}px padding), {atlas.KeptApart} kept apart, {keptTriangles} triangle(s) outside their texture's square kept on their own material";
            log.Add($"Building proxies: {made} of {buildings.Count} building(s), {pieceGroups} piece LODGroup(s) folded in; "
                + $"{triangles} triangles, {(made > 0 ? (float)subMeshes / made : 0f):0.#} material(s) per proxy on average; {atlasLine}; "
                + $"lightmapped at {settings.ProxyLightmapScale:0.##} of {resolution:0.#} texels/m, {texels:0} texels, {(charts > 0 ? texels / charts : 0):0.#} per chart, bake pushoff {settings.ProxyBakePushoff:0.###}m; "
                + $"proxy below {settings.LowerDetailShare:0.###} of the view's height, culled below {settings.ProxyCullShare:0.###}.");
        }

        /// <summary>
        /// Merges the renderers into one mesh (saved under Folder as
        /// meshName), drawn from the atlas where it can be (none: one
        /// sub-mesh per material), with lightmap UVs for lightmapScale of the
        /// level's resolution, and draws it from a new static Proxy child of
        /// the building's group, lightmapped with the given bake parameters.
        /// Gives back its renderer, the mesh's lightmap chart count, its
        /// surface area and its triangles kept off the atlas.
        /// </summary>
        private static MeshRenderer AddProxy(List<MeshRenderer> renderers, Transform group, string meshName, ProxyAtlas atlas, float lightmapScale, float resolution, LightmapParameters parameters, int layer, out int charts, out float area, out int kept)
        {
            Mesh mesh = Merge(renderers, group, out Material[] materials);
            kept = 0;

            if (atlas != null) {
                mesh = atlas.Apply(mesh, materials, out materials, out kept);
            }

            mesh.name = meshName;
            AddLightmapUVs(mesh, lightmapScale * resolution, out charts, out area);
            AssetDatabase.CreateAsset(mesh, $"{Folder}/{Safe(meshName)}.asset");

            GameObject child = new(ProxyName) {
                layer = layer < 0 ? 0 : layer
            };
            child.transform.SetParent(group, false);
            GameObjectUtility.SetStaticEditorFlags(child, StaticFlags);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            // Casting (static, so its shadow is the full detail's, baked),
            // lightmapped at its own share of the level's resolution.
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.receiveGI = ReceiveGI.Lightmaps;
            renderer.scaleInLightmap = lightmapScale;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            // Its own bake parameters (only reachable as a serialized field).
            SerializedObject serialized = new(renderer);
            serialized.FindProperty("m_LightmapParameters").objectReferenceValue = parameters;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return renderer;
        }

        /// <summary>
        /// The proxies' Lightmap Parameters at ParametersPath (made with the
        /// default values when missing), its pushoff set: bake rays from a
        /// proxy's texels start this far off its surface, beyond the relief
        /// of the building's full detail, which stays in the bake as solid
        /// geometry a few millimetres away and would otherwise darken it.
        /// </summary>
        private static LightmapParameters ProxyParameters(float pushoff)
        {
            LightmapParameters parameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(ParametersPath);

            if (parameters == null) {
                parameters = new LightmapParameters();
                AssetDatabase.CreateAsset(parameters, ParametersPath);
            }

            if (!Mathf.Approximately(parameters.pushoff, pushoff)) {
                parameters.pushoff = pushoff;
                EditorUtility.SetDirty(parameters);
                AssetDatabase.SaveAssetIfDirty(parameters);
            }

            return parameters;
        }

        /// <summary>
        /// Unwraps the mesh's lightmap UVs (uv2), leaving ChartMarginTexels
        /// between charts at texelsPerMetre: the margin is given in UV space,
        /// so it is worked out from the mesh's lightmap size (the bake gives a
        /// mesh a square about its surface area's root times its resolution
        /// across). The unwrap is done on a copy welded by position: the
        /// pack's meshes are split at every texture seam, and unwrapped as
        /// they are nearly every face would be a chart of its own. The copy's
        /// triangles come back in the same order, so each corner's lightmap
        /// UV is carried back onto the mesh, a vertex copied where a chart's
        /// edge runs through it. Gives back the chart count and surface area.
        /// </summary>
        private static void AddLightmapUVs(Mesh mesh, float texelsPerMetre, out int charts, out float area)
        {
            Vector3[] vertices = mesh.vertices;
            // Every sub-mesh's triangles in turn.
            int[] triangles = mesh.triangles;
            area = 0f;

            for (int t = 0; t < triangles.Length; t += 3) {
                Vector3 a = vertices[triangles[t]];
                area += Vector3.Cross(vertices[triangles[t + 1]] - a, vertices[triangles[t + 2]] - a).magnitude * 0.5f;
            }

            // The welded copy: one vertex per position, one sub-mesh.
            Dictionary<Vector3Int, int> welded = new();
            List<Vector3> weldedVertices = new();
            int[] weldedTriangles = new int[triangles.Length];

            for (int i = 0; i < triangles.Length; i++) {
                Vector3 position = vertices[triangles[i]];
                Vector3Int key = Vector3Int.RoundToInt(position / WeldDistance);

                if (!welded.TryGetValue(key, out int index)) {
                    index = weldedVertices.Count;
                    welded[key] = index;
                    weldedVertices.Add(position);
                }

                weldedTriangles[i] = index;
            }

            Mesh copy = new() {
                indexFormat = IndexFormat.UInt32
            };
            copy.SetVertices(weldedVertices);
            copy.SetTriangles(weldedTriangles, 0);
            copy.RecalculateNormals();

            float texelsAcross = Mathf.Max(1f, Mathf.Sqrt(area) * texelsPerMetre);
            UnwrapParam.SetDefaults(out UnwrapParam unwrap);
            unwrap.packMargin = Mathf.Max(ChartMarginTexels / texelsAcross, 1f / 1024f);
            Unwrapping.GenerateSecondaryUVSet(copy, unwrap);
            int[] unwrappedTriangles = copy.triangles;
            Vector2[] unwrappedUVs = copy.uv2;
            charts = CountCharts(unwrappedTriangles, copy.vertexCount);
            Object.DestroyImmediate(copy);

            // Each corner's UV onto the mesh's own vertex; a vertex already
            // holding another unwrapped vertex's UV is copied (once per
            // unwrapped vertex) and the corner moved onto the copy.
            List<Vector3> positions = new(vertices);
            List<Vector3> normals = new();
            List<Vector4> tangents = new();
            List<Vector2> uvs = new();
            List<Color> colors = new();
            mesh.GetNormals(normals);
            mesh.GetTangents(tangents);
            mesh.GetUVs(0, uvs);
            mesh.GetColors(colors);
            List<Vector2> lightmapUVs = new(new Vector2[vertices.Length]);
            int[] holds = new int[vertices.Length];
            System.Array.Fill(holds, -1);
            Dictionary<long, int> copies = new();
            int[] corners = new int[triangles.Length];

            for (int i = 0; i < triangles.Length; i++) {
                int vertex = triangles[i];
                int unwrapped = unwrappedTriangles[i];

                if (holds[vertex] < 0) {
                    holds[vertex] = unwrapped;
                    lightmapUVs[vertex] = unwrappedUVs[unwrapped];
                } else if (holds[vertex] != unwrapped) {
                    long key = (long)vertex * unwrappedUVs.Length + unwrapped;

                    if (!copies.TryGetValue(key, out int copied)) {
                        copied = positions.Count;
                        copies[key] = copied;
                        positions.Add(positions[vertex]);
                        AddCopy(normals, vertex);
                        AddCopy(tangents, vertex);
                        AddCopy(uvs, vertex);
                        AddCopy(colors, vertex);
                        lightmapUVs.Add(unwrappedUVs[unwrapped]);
                    }

                    vertex = copied;
                }

                corners[i] = vertex;
            }

            // Sub-meshes read before the index format can change (a change
            // empties them), then written back in the same order.
            int[] indexCounts = new int[mesh.subMeshCount];

            for (int s = 0; s < indexCounts.Length; s++) {
                indexCounts[s] = (int)mesh.GetIndexCount(s);
            }

            if (positions.Count > ushort.MaxValue) {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetUVs(1, lightmapUVs);
            mesh.subMeshCount = indexCounts.Length;

            for (int s = 0, start = 0; s < indexCounts.Length; start += indexCounts[s], s++) {
                mesh.SetTriangles(corners, start, indexCounts[s], s);
            }
        }

        /// <summary>
        /// Adds a copy of the vertex's value to an attribute list, if the mesh
        /// has that attribute.
        /// </summary>
        private static void AddCopy<T>(List<T> values, int vertex)
        {
            if (values.Count > 0) {
                values.Add(values[vertex]);
            }
        }

        /// <summary>
        /// The number of separate pieces the triangles make by shared
        /// vertices: after an unwrap, its lightmap charts.
        /// </summary>
        private static int CountCharts(int[] triangles, int vertexCount)
        {
            int[] parent = new int[vertexCount];

            for (int v = 0; v < vertexCount; v++) {
                parent[v] = v;
            }

            for (int t = 0; t < triangles.Length; t += 3) {
                int a = Root(parent, triangles[t]);
                parent[Root(parent, triangles[t + 1])] = a;
                parent[Root(parent, triangles[t + 2])] = a;
            }

            HashSet<int> roots = new();

            for (int t = 0; t < triangles.Length; t += 3) {
                roots.Add(Root(parent, triangles[t]));
            }

            return roots.Count;
        }

        private static int Root(int[] parent, int v)
        {
            while (parent[v] != v) {
                parent[v] = parent[parent[v]];
                v = parent[v];
            }

            return v;
        }

        /// <summary>
        /// Every building under the root: each Building i under an owner
        /// group in Facades or Lots, with its roof group (Roofs/[owner]/
        /// Building i) where it has one.
        /// </summary>
        private static List<Building> Gather(Transform root)
        {
            List<Building> buildings = new();
            Transform roofs = root.Find("Roofs");

            foreach (string treeName in new[] { "Facades", "Lots" }) {
                Transform tree = root.Find(treeName);

                if (tree == null) {
                    continue;
                }

                foreach (Transform owner in tree) {
                    Transform ownerRoofs = roofs != null ? roofs.Find(owner.name) : null;

                    foreach (Transform group in owner) {
                        if (!group.name.StartsWith("Building ")) {
                            continue;
                        }

                        buildings.Add(new Building { Group = group, Roofs = ownerRoofs != null ? ownerRoofs.Find(group.name) : null });
                    }
                }
            }

            // Each open window (and the room behind it) goes with the
            // building holding the generated wall nearest it.
            List<OpenWindowInterior> windows = OpenWindowInteriors.InSector(root.parent);
            PieceAnnotations annotations = windows.Count > 0 ? PieceAnnotations.Load() : null;

            foreach (OpenWindowInterior window in windows) {
                Transform group = annotations != null ? OpenWindowInteriors.BuildingOf(window, root, annotations) : null;
                Building building = buildings.Find(b => b.Group == group);

                if (building == null) {
                    continue;
                }

                building.Extras.Add(window.transform);

                if (window.Built != null) {
                    building.Extras.Add(window.Built.transform);
                }
            }

            return buildings;
        }

        /// <summary>
        /// Sorts a building's renderers into Near (full detail) and Merged
        /// (the second level, else the full detail), those in no LODGroup in
        /// both; removes the pieces' LODGroups and switches off every
        /// renderer they held but the full detail (the proxy draws them now;
        /// the lowest levels are drawn by nothing). Gives back how many
        /// LODGroups were removed, and the pack's fading to copy (from the
        /// first one).
        /// </summary>
        private static int Split(Building building, out LODFadeMode fadeMode, out bool animate)
        {
            fadeMode = LODFadeMode.None;
            animate = false;
            List<LODGroup> groups = new();
            List<MeshRenderer> renderers = new();
            AddChildren(building.Group, groups, renderers);

            if (building.Roofs != null) {
                AddChildren(building.Roofs, groups, renderers);
            }

            foreach (Transform extra in building.Extras) {
                AddChildren(extra, groups, renderers);
            }

            HashSet<Renderer> inGroups = new();
            HashSet<Renderer> near = new();
            HashSet<Renderer> merged = new();

            foreach (LODGroup group in groups) {
                LOD[] lods = group.GetLODs();

                if (lods.Length == 0) {
                    continue;
                }

                if (inGroups.Count == 0) {
                    fadeMode = group.fadeMode;
                    animate = group.animateCrossFading;
                }

                for (int level = 0; level < lods.Length; level++) {
                    foreach (Renderer renderer in lods[level].renderers) {
                        if (renderer == null) {
                            continue;
                        }

                        inGroups.Add(renderer);

                        if (level == 0) {
                            near.Add(renderer);
                        }

                        if (level == Mathf.Min(1, lods.Length - 1)) {
                            merged.Add(renderer);
                        }
                    }
                }
            }

            foreach (MeshRenderer renderer in renderers) {
                // Off already, or drawing only a shadow: left as it is, never
                // in the proxy.
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) {
                    continue;
                }

                bool grouped = inGroups.Contains(renderer);

                if (!grouped || near.Contains(renderer)) {
                    building.Near.Add(renderer);
                }

                if ((!grouped || merged.Contains(renderer)) && HasMesh(renderer)) {
                    building.Merged.Add(renderer);
                }
            }

            foreach (LODGroup group in groups) {
                Object.DestroyImmediate(group);
            }

            // Below the full detail: drawn by the proxy from now on, or not at
            // all (a renderer left out of every LODGroup would otherwise draw
            // at every distance, over its full detail).
            foreach (Renderer renderer in inGroups) {
                if (!near.Contains(renderer)) {
                    renderer.enabled = false;
                }
            }

            return groups.Count;
        }

        /// <summary>
        /// Adds every LODGroup and mesh renderer under a group.
        /// </summary>
        private static void AddChildren(Transform group, List<LODGroup> groups, List<MeshRenderer> renderers)
        {
            groups.AddRange(group.GetComponentsInChildren<LODGroup>(true));
            renderers.AddRange(group.GetComponentsInChildren<MeshRenderer>(true));
        }

        private static bool HasMesh(MeshRenderer renderer)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null;
        }

        /// <summary>
        /// The renderers' meshes merged into one, in the space of the
        /// building's group: every sub-mesh drawn with one material joined
        /// into one sub-mesh (mirrored pieces' triangles turned the right way
        /// round by CombineMeshes), the materials in the order first met.
        /// The pieces' lightmap UVs are dropped (each fills the whole 0-1
        /// square; the merged mesh gets its own, AddLightmapUVs).
        /// </summary>
        private static Mesh Merge(List<MeshRenderer> renderers, Transform space, out Material[] materials)
        {
            List<Material> order = new();
            Dictionary<Material, List<CombineInstance>> parts = new();
            Matrix4x4 toSpace = space.worldToLocalMatrix;

            foreach (MeshRenderer renderer in renderers) {
                Mesh source = renderer.GetComponent<MeshFilter>().sharedMesh;
                Material[] shared = renderer.sharedMaterials;
                int count = Mathf.Min(source.subMeshCount, shared.Length);

                for (int s = 0; s < count; s++) {
                    Material material = shared[s];

                    if (material == null) {
                        continue;
                    }

                    if (!parts.TryGetValue(material, out List<CombineInstance> list)) {
                        list = new List<CombineInstance>();
                        parts[material] = list;
                        order.Add(material);
                    }

                    list.Add(new CombineInstance { mesh = source, subMeshIndex = s, transform = toSpace * renderer.localToWorldMatrix });
                }
            }

            // One mesh per material first (its parts' sub-meshes merged),
            // then those joined as one sub-mesh each.
            CombineInstance[] byMaterial = new CombineInstance[order.Count];
            List<Mesh> temporary = new();
            int vertexCount = 0;

            for (int m = 0; m < order.Count; m++) {
                Mesh part = new() {
                    indexFormat = IndexFormat.UInt32
                };
                part.CombineMeshes(parts[order[m]].ToArray(), true, true);
                temporary.Add(part);
                vertexCount += part.vertexCount;
                byMaterial[m] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
            }

            // The index format chosen before the triangles go in: changing it
            // afterwards empties the sub-meshes.
            Mesh mesh = new() {
                indexFormat = vertexCount <= ushort.MaxValue ? IndexFormat.UInt16 : IndexFormat.UInt32
            };
            mesh.CombineMeshes(byMaterial, false, false);
            mesh.uv2 = null;
            mesh.RecalculateBounds();

            foreach (Mesh part in temporary) {
                Object.DestroyImmediate(part);
            }

            materials = order.ToArray();
            return mesh;
        }

        private static long TriangleCount(Mesh mesh)
        {
            long count = 0;

            for (int s = 0; s < mesh.subMeshCount; s++) {
                count += mesh.GetIndexCount(s) / 3;
            }

            return count;
        }

        /// <summary>
        /// Deletes the sector's proxy meshes, atlases and atlas material from
        /// an earlier generation.
        /// </summary>
        private static void DeleteOld(string prefix)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) {
                return;
            }

            foreach (string path in Directory.GetFiles(Folder)) {
                if (!path.EndsWith(".meta") && Path.GetFileName(path).StartsWith(prefix, System.StringComparison.Ordinal)) {
                    AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
                }
            }
        }

        /// <summary>
        /// A name with the characters a file name can't hold replaced.
        /// </summary>
        private static string Safe(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) {
                name = name.Replace(invalid, '_');
            }

            return name;
        }
    }
}
