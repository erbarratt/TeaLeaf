using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGen
{
    /// <summary>
    /// Each building seen from afar as one renderer (LevelGenerator, last
    /// before the report). A building's pieces stand under two groups - its walls,
    /// foundations, beams and dressing under Facades (or Lots)/[owner]/Building
    /// i, its roofs, chimneys and dormers under Roofs/[owner]/Building i - and
    /// each piece carries the pack's own LODGroup (or several, one per nested
    /// part), switching at its own small size. The building keeps the pack's
    /// steps, switched for the whole building at once: up close every piece's
    /// full detail; further off every piece's second level, still piece by
    /// piece; furthest, where the pack would show its lowest, one proxy: every
    /// piece's lowest level of detail, merged into one mesh with one sub-mesh
    /// per material, so the same materials, colours and silhouette - lean,
    /// jetties, chimneys - from one renderer instead of hundreds.
    ///
    /// Done with one LODGroup per building on its facade group: level 0 every
    /// piece's full-detail renderers, level 1 every piece's second-level
    /// renderers (its full detail where it has one level; those in no
    /// LODGroup in both), level 2 the proxy (a "Proxy" child), culled where
    /// the pack would cull its pieces.
    /// A renderer can belong to one LODGroup only, so the pieces' own
    /// LODGroups are removed and the renderers of the levels below their
    /// second switched off (overrides on the instances; the prefabs
    /// untouched). The proxy is lit from probes, as the pack's lower
    /// levels are (only the nearest level is lightmapped), so never batching
    /// static. Its mesh is saved under Folder, named for the sector, owner and
    /// building; the sector's old ones are deleted each generation.
    /// </summary>
    internal static class BuildingProxies
    {
        internal const string Folder = PrefabCatalogueScanner.DataFolder + "/Proxies";
        private const string ProxyName = "Proxy";

        // Static for the bake (as a lower level of detail it is lit from
        // probes and gives no light of its own), occlusion culling (culled,
        // the full detail occludes) and reflection probes; not batching
        // static, since it is lit from probes.
        private const StaticEditorFlags StaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        /// <summary>
        /// One building's renderers: those drawn up close, those drawn
        /// further off, and those merged into its proxy.
        /// </summary>
        private class Building
        {
            public Transform Group;
            public Transform Roofs;
            public readonly List<Renderer> Near = new();
            public readonly List<Renderer> Middle = new();
            public readonly List<MeshRenderer> Far = new();
        }

        /// <summary>
        /// Gives every building under the generated root its proxy, if the
        /// settings ask for them. Adds one line to log.
        /// </summary>
        internal static void Build(LayoutSpec spec, Transform root, GeneratorSettings settings, List<string> log)
        {
            string prefix = Safe($"{spec.sector.Trim()} - ");
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
            int made = 0;
            int pieceGroups = 0;
            long vertices = 0;
            long triangles = 0;
            int subMeshes = 0;

            try {
                AssetDatabase.StartAssetEditing();

                foreach (Building building in buildings) {
                    pieceGroups += Split(building, out LODFadeMode fadeMode, out bool animate);

                    if (building.Near.Count == 0 || building.Far.Count == 0) {
                        continue;
                    }

                    Mesh mesh = Merge(building.Far, building.Group, out Material[] materials);
                    mesh.name = $"{spec.sector.Trim()} - {building.Group.parent.name} {building.Group.name}";
                    AssetDatabase.CreateAsset(mesh, $"{Folder}/{Safe(mesh.name)}.asset");

                    GameObject proxy = new(ProxyName) {
                        layer = layer < 0 ? 0 : layer
                    };
                    proxy.transform.SetParent(building.Group, false);
                    GameObjectUtility.SetStaticEditorFlags(proxy, StaticFlags);
                    proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer renderer = proxy.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = materials;
                    // As the pieces' own lower levels: casting (static, so its
                    // shadow is the full detail's, baked), lit from probes.
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.receiveGI = ReceiveGI.LightProbes;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

                    // The building's own LODGroup: its full detail down to
                    // LowerDetailShare of the view's height, its pieces'
                    // second levels down to ProxyShare, then the proxy down to
                    // ProxyCullShare, below which nothing is drawn. Each
                    // switch must come below the one before.
                    float cull = Mathf.Min(settings.ProxyCullShare, settings.ProxyShare - 0.001f);
                    LODGroup group = building.Group.gameObject.AddComponent<LODGroup>();
                    group.fadeMode = fadeMode;
                    group.animateCrossFading = animate;
                    group.SetLODs(new[] {
                        new LOD(Mathf.Max(settings.LowerDetailShare, settings.ProxyShare + 0.001f), building.Near.ToArray()),
                        new LOD(settings.ProxyShare, building.Middle.ToArray()),
                        new LOD(cull, new Renderer[] { renderer })
                    });
                    group.RecalculateBounds();

                    made++;
                    vertices += mesh.vertexCount;
                    triangles += TriangleCount(mesh);
                    subMeshes += mesh.subMeshCount;
                }
            } finally {
                AssetDatabase.StopAssetEditing();
            }

            log.Add($"Building proxies: {made} of {buildings.Count} building(s), {pieceGroups} piece LODGroup(s) folded in; "
                + $"{vertices} vertices, {triangles} triangles, {(made > 0 ? (float)subMeshes / made : 0f):0.#} material(s) per proxy on average; "
                + $"pieces' second levels below {settings.LowerDetailShare:0.###} of the view's height, proxy below {settings.ProxyShare:0.###}, "
                + $"culled below {settings.ProxyCullShare:0.###}.");
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

            return buildings;
        }

        /// <summary>
        /// Sorts a building's renderers into Near (full detail), Middle (the
        /// second level, else the full detail) and Far (the lowest level),
        /// those in no LODGroup in all three; removes the pieces' LODGroups
        /// and switches off every renderer they held in neither Near nor
        /// Middle (the proxy draws them now). Gives back how many LODGroups
        /// were removed, and the pack's fading to copy (from the first one).
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

            HashSet<Renderer> inGroups = new();
            HashSet<Renderer> near = new();
            HashSet<Renderer> middle = new();
            HashSet<Renderer> far = new();

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
                            middle.Add(renderer);
                        }

                        if (level == lods.Length - 1) {
                            far.Add(renderer);
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

                if (!grouped || middle.Contains(renderer)) {
                    building.Middle.Add(renderer);
                }

                if ((!grouped || far.Contains(renderer)) && HasMesh(renderer)) {
                    building.Far.Add(renderer);
                }
            }

            foreach (LODGroup group in groups) {
                Object.DestroyImmediate(group);
            }

            // Below the second level: drawn by the proxy from now on (a
            // renderer left out of every LODGroup would otherwise draw at
            // every distance, over its full detail).
            foreach (Renderer renderer in inGroups) {
                if (!near.Contains(renderer) && !middle.Contains(renderer)) {
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
        /// Lightmap UVs are dropped (the proxy is lit from probes).
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
        /// Deletes the sector's proxy meshes from an earlier generation.
        /// </summary>
        private static void DeleteOld(string prefix)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) {
                return;
            }

            foreach (string path in Directory.GetFiles(Folder, "*.asset")) {
                if (Path.GetFileName(path).StartsWith(prefix, System.StringComparison.Ordinal)) {
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
