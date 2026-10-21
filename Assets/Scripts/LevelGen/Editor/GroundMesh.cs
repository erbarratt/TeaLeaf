using System.Collections.Generic;
using System.IO;
using Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGen
{
    /// <summary>
    /// The streets' ground as meshes the generator builds (StreetGround
    /// decides where it goes; this makes it): a grid of square cells laid
    /// in the street area's own turned frame, every vertex at the height
    /// the street's surface has there (level ground, slopes and the two
    /// levels either side of a flight of steps all come out of the height
    /// callback, no tilted pieces needed) and painted with a vertex colour
    /// (red for cobbles, blue for wet dirt, nothing for the dirt underneath)
    /// that the TeaLeaf/GroundBlend shader turns into cobbles, mud and
    /// puddles.
    ///
    /// The ground is one mesh per region: a callback sorts each cell into
    /// none (dropped: outside the area, or under a flight's steps), one of
    /// the slopes (each its own region) or a level; the level cells are
    /// joined into one region per connected level, neighbours never joined
    /// where another callback says a flight cuts between them. A vertex can
    /// be moved by a callback before it is placed (the vertices under a
    /// flight's steps are moved onto its front or top line, so a level's
    /// mesh ends exactly on the steps). Each region is an object of its own
    /// under the area's group, named by a callback, holding its chunks:
    /// the grid is cut into squares of ChunkCells cells a side, each its own
    /// mesh asset and object, so a far chunk is frustum- and
    /// occlusion-culled on its own and each chunk's lightmap chart stays a
    /// sensible size. Each chunk is its own collider too (a MeshCollider
    /// of its mesh, with a Stone SurfaceTag). Normals come from the whole
    /// grid's heights (central differences), so chunk and region borders
    /// show no seam. Lightmap UVs are planar over each chunk. The meshes
    /// are saved under Folder, named for the sector and area, and the
    /// area's old ones are deleted first; the folder may be deleted at any
    /// time (the scene's ground then loses its meshes and its collision
    /// until the level is generated again, which remakes them).
    /// </summary>
    internal static class GroundMesh
    {
        // Where the meshes are saved.
        internal const string Folder = PrefabCatalogueScanner.DataFolder + "/Ground";
        // The material every chunk draws with.
        internal const string MaterialPath = "Assets/Art/Materials/Environment/Ground/StreetGround.mat";
        // Cells a chunk side: at the default 0.75m cell, 24m chunks.
        private const int ChunkCells = 32;
        // A level region of fewer cells than this is a stray, joined to a
        // neighbouring region.
        private const int StrayCells = 6;
        // The static flags a chunk gets: lightmapped, an occluder and
        // occludee, batched (one chunk is one mesh anyway).
        private const StaticEditorFlags Static = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
            | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;

        /// <summary>
        /// What was built, for the log.
        /// </summary>
        internal struct Result
        {
            public int Regions;
            public int Chunks;
            public int Vertices;
            public int Triangles;
        }

        /// <summary>
        /// The grid's frame: its origin on the ground and its two axes
        /// (x, z), and the extent to cover in it (min and max along the
        /// axes, from the origin).
        /// </summary>
        internal struct Frame
        {
            public Vector2 Origin;
            public Vector2 AxisX;
            public Vector2 AxisZ;
            public Vector2 Min;
            public Vector2 Max;

            public Vector2 Point(float x, float z)
            {
                return Origin + AxisX * x + AxisZ * z;
            }
        }

        /// <summary>
        /// Builds one area's ground under parent, one object per region
        /// ("Ground " + nameOf's name) holding chunk objects ("Chunk x_z"),
        /// on the Environment layer, static, casting no shadow (nothing
        /// stands under a street), lit by probes until the level is baked.
        /// Each chunk collides by its own mesh, with a Stone SurfaceTag.
        /// regionOf sorts a cell by its four corners and its middle: -1
        /// none, 0 a level, more an explicit region of its own; cuts says
        /// whether two neighbouring level cells (their middles) are kept
        /// apart; place moves a vertex's ground position; heightAt and
        /// paintAt give a placed vertex its height and colour. The mesh
        /// assets are named "sector - area region x_z".
        /// </summary>
        internal static Result Build(string sector, string area, Frame frame, float cell, System.Func<Vector2[], Vector2, int> regionOf,
            System.Func<Vector2, Vector2, bool> cuts, System.Func<Vector2, Vector2> place, System.Func<Vector2, float> heightAt,
            System.Func<Vector2, Color32> paintAt, System.Func<int, Vector2, string> nameOf, Transform parent, List<string> log)
        {
            Result result = new();
            cell = Mathf.Max(cell, 0.1f);
            // The grid's cells, one past the extent each way so the last
            // cells' far corners are tested too.
            int i0 = Mathf.FloorToInt(frame.Min.x / cell) - 1;
            int i1 = Mathf.CeilToInt(frame.Max.x / cell) + 1;
            int j0 = Mathf.FloorToInt(frame.Min.y / cell) - 1;
            int j1 = Mathf.CeilToInt(frame.Max.y / cell) + 1;
            int columns = i1 - i0;
            int rows = j1 - j0;

            if (columns <= 0 || rows <= 0) {
                return result;
            }

            // Each cell's region as the callback sorts it (-1 none, 0 a
            // level, more explicit), then the levels joined into regions of
            // their own, numbered on from the explicit ones.
            int[,] sorted = new int[columns, rows];
            int explicitMax = 0;
            bool any = false;
            Vector2[] corners = new Vector2[4];

            for (int i = 0; i < columns; i++) {
                for (int j = 0; j < rows; j++) {
                    float x = (i0 + i) * cell;
                    float z = (j0 + j) * cell;
                    corners[0] = frame.Point(x, z);
                    corners[1] = frame.Point(x + cell, z);
                    corners[2] = frame.Point(x + cell, z + cell);
                    corners[3] = frame.Point(x, z + cell);
                    int region = regionOf(corners, frame.Point(x + cell * 0.5f, z + cell * 0.5f));
                    sorted[i, j] = region;
                    explicitMax = Mathf.Max(explicitMax, region);
                    any |= region >= 0;
                }
            }

            if (!any) {
                return result;
            }

            int[,] regionOfCell = JoinLevels(sorted, columns, rows, explicitMax, (i, j) => frame.Point((i0 + i + 0.5f) * cell, (j0 + j + 0.5f) * cell), cuts,
                out int regionCount);

            // Every vertex's height (those of kept cells and their
            // neighbours, for the normals), found once, before any vertex
            // is moved.
            float[,] height = new float[columns + 1, rows + 1];
            bool[,] used = new bool[columns + 1, rows + 1];

            for (int i = 0; i < columns; i++) {
                for (int j = 0; j < rows; j++) {
                    if (regionOfCell[i, j] < 0) {
                        continue;
                    }

                    for (int di = -1; di <= 2; di++) {
                        for (int dj = -1; dj <= 2; dj++) {
                            int vi = i + di;
                            int vj = j + dj;

                            if (vi >= 0 && vi <= columns && vj >= 0 && vj <= rows) {
                                used[vi, vj] = true;
                            }
                        }
                    }
                }
            }

            for (int vi = 0; vi <= columns; vi++) {
                for (int vj = 0; vj <= rows; vj++) {
                    if (used[vi, vj]) {
                        height[vi, vj] = heightAt(frame.Point((i0 + vi) * cell, (j0 + vj) * cell));
                    }
                }
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            if (material == null) {
                log.Add($"Ground material '{MaterialPath}' not found: the ground is drawn with the default material.");
            }

            DeleteOld(sector, area);

            if (!AssetDatabase.IsValidFolder(Folder)) {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));
            }

            int layer = LayerMask.NameToLayer("Environment");
            int chunkX0 = Mathf.FloorToInt((float)i0 / ChunkCells);
            int chunkX1 = Mathf.FloorToInt((float)(i1 - 1) / ChunkCells);
            int chunkZ0 = Mathf.FloorToInt((float)j0 / ChunkCells);
            int chunkZ1 = Mathf.FloorToInt((float)(j1 - 1) / ChunkCells);
            List<Vector3> vertices = new();
            List<Vector3> normals = new();
            List<Color32> colours = new();
            List<Vector2> lightmapUV = new();
            List<int> triangles = new();
            Dictionary<(int, int), int> index = new();

            try {
                AssetDatabase.StartAssetEditing();

                for (int region = 1; region <= regionCount; region++) {
                    Transform regionGroup = null;

                    for (int cx = chunkX0; cx <= chunkX1; cx++) {
                        for (int cz = chunkZ0; cz <= chunkZ1; cz++) {
                            vertices.Clear();
                            normals.Clear();
                            colours.Clear();
                            lightmapUV.Clear();
                            triangles.Clear();
                            index.Clear();
                            // The chunk's cells, in grid indices.
                            int ci0 = Mathf.Max(cx * ChunkCells, i0) - i0;
                            int ci1 = Mathf.Min((cx + 1) * ChunkCells, i1) - i0;
                            int cj0 = Mathf.Max(cz * ChunkCells, j0) - j0;
                            int cj1 = Mathf.Min((cz + 1) * ChunkCells, j1) - j0;
                            Vector2 sample = Vector2.zero;

                            for (int i = ci0; i < ci1; i++) {
                                for (int j = cj0; j < cj1; j++) {
                                    if (regionOfCell[i, j] != region) {
                                        continue;
                                    }

                                    if (triangles.Count == 0) {
                                        sample = frame.Point((i0 + i + 0.5f) * cell, (j0 + j + 0.5f) * cell);
                                    }

                                    int a = Vertex(i, j);
                                    int b = Vertex(i + 1, j);
                                    int c = Vertex(i + 1, j + 1);
                                    int d = Vertex(i, j + 1);
                                    // Two triangles, wound to face up (the frame's
                                    // Z is to the left of its X, so a, b, c goes
                                    // clockwise seen from above).
                                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                                }
                            }

                            if (triangles.Count == 0) {
                                continue;
                            }

                            if (regionGroup == null) {
                                regionGroup = new GameObject("Ground " + nameOf(region <= explicitMax ? region : 0, sample)).transform;
                                regionGroup.SetParent(parent, false);
                                result.Regions++;
                            }

                            Mesh mesh = new() {
                                name = $"{sector} - {area} {region} {cx}_{cz}",
                                indexFormat = IndexFormat.UInt16
                            };
                            mesh.SetVertices(vertices);
                            mesh.SetNormals(normals);
                            mesh.SetColors(colours);
                            mesh.SetUVs(1, lightmapUV);
                            mesh.SetTriangles(triangles, 0);
                            mesh.RecalculateBounds();
                            AssetDatabase.CreateAsset(mesh, $"{Folder}/{Safe(mesh.name)}.asset");

                            GameObject chunk = new($"Chunk {cx}_{cz}") {
                                layer = layer
                            };
                            GameObjectUtility.SetStaticEditorFlags(chunk, Static);
                            chunk.transform.SetParent(regionGroup, false);
                            chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                            MeshRenderer renderer = chunk.AddComponent<MeshRenderer>();
                            renderer.sharedMaterial = material;
                            renderer.shadowCastingMode = ShadowCastingMode.Off;
                            renderer.receiveShadows = true;
                            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                            renderer.receiveGI = ReceiveGI.Lightmaps;
                            // The chunk's own mesh is its collider, so what is walked on is
                            // exactly what is seen: slopes, bends and junctions included.
                            chunk.AddComponent<MeshCollider>().sharedMesh = mesh;
                            SurfaceTag tag = chunk.AddComponent<SurfaceTag>();
                            SerializedObject serialized = new(tag);
                            serialized.FindProperty("surface").enumValueIndex = (int)SurfaceType.Stone;
                            serialized.ApplyModifiedPropertiesWithoutUndo();

                            result.Chunks++;
                            result.Vertices += vertices.Count;
                            result.Triangles += triangles.Count / 3;

                            // The chunk's vertex for a grid vertex, made on first use:
                            // its placed world position, normal, paint and lightmap UV.
                            int Vertex(int vi, int vj)
                            {
                                if (index.TryGetValue((vi, vj), out int found)) {
                                    return found;
                                }

                                Vector2 at = place(frame.Point((i0 + vi) * cell, (j0 + vj) * cell));
                                vertices.Add(new Vector3(at.x, heightAt(at), at.y));
                                normals.Add(Normal(vi, vj));
                                colours.Add(paintAt(at));
                                lightmapUV.Add(new Vector2((float)(vi - ci0) / (ci1 - ci0), (float)(vj - cj0) / (cj1 - cj0)));
                                index[(vi, vj)] = vertices.Count - 1;
                                return vertices.Count - 1;
                            }
                        }
                    }
                }
            } finally {
                AssetDatabase.StopAssetEditing();
            }

            return result;

            // The surface normal at a grid vertex from the heights round it:
            // on each axis the gentler of the two one-sided differences, so
            // the jump between a flight's two levels doesn't tilt the
            // vertices beside it; the frame's axes carry the slope into the
            // world.
            Vector3 Normal(int vi, int vj)
            {
                float dx = Gentler(vi, vj, 1, 0);
                float dz = Gentler(vi, vj, 0, 1);
                Vector3 alongX = new(frame.AxisX.x, dx, frame.AxisX.y);
                Vector3 alongZ = new(frame.AxisZ.x, dz, frame.AxisZ.y);
                Vector3 normal = Vector3.Cross(alongZ, alongX).normalized;
                return normal.y < 0f ? -normal : normal;
            }

            float Gentler(int vi, int vj, int di, int dj)
            {
                float here = height[vi, vj];
                bool hasBefore = vi - di >= 0 && vj - dj >= 0 && used[vi - di, vj - dj];
                bool hasAfter = vi + di <= columns && vj + dj <= rows && used[vi + di, vj + dj];
                float before = hasBefore ? (here - height[vi - di, vj - dj]) / cell : 0f;
                float after = hasAfter ? (height[vi + di, vj + dj] - here) / cell : 0f;

                if (!hasBefore) {
                    return after;
                }

                if (!hasAfter) {
                    return before;
                }

                return Mathf.Abs(before) <= Mathf.Abs(after) ? before : after;
            }
        }

        /// <summary>
        /// The cells' regions: the explicit ones (1 to explicitMax) kept,
        /// the level cells (0) flood-filled into one region per connected
        /// level, numbered on from explicitMax, two neighbours (sharing a
        /// side) joined unless cuts says a flight lies between their
        /// middles. -1 stays none.
        /// </summary>
        private static int[,] JoinLevels(int[,] sorted, int columns, int rows, int explicitMax, System.Func<int, int, Vector2> centreOf,
            System.Func<Vector2, Vector2, bool> cuts, out int regionCount)
        {
            int[,] region = new int[columns, rows];
            regionCount = explicitMax;
            Stack<(int, int)> open = new();

            for (int i = 0; i < columns; i++) {
                for (int j = 0; j < rows; j++) {
                    region[i, j] = sorted[i, j] > 0 ? sorted[i, j] : sorted[i, j] < 0 ? -1 : 0;
                }
            }

            for (int i = 0; i < columns; i++) {
                for (int j = 0; j < rows; j++) {
                    if (sorted[i, j] != 0 || region[i, j] != 0) {
                        continue;
                    }

                    int id = ++regionCount;
                    region[i, j] = id;
                    open.Push((i, j));

                    while (open.Count > 0) {
                        (int ci, int cj) = open.Pop();
                        Vector2 centre = centreOf(ci, cj);

                        foreach ((int ni, int nj) in new[] { (ci + 1, cj), (ci - 1, cj), (ci, cj + 1), (ci, cj - 1) }) {
                            if (ni < 0 || nj < 0 || ni >= columns || nj >= rows || sorted[ni, nj] != 0 || region[ni, nj] != 0) {
                                continue;
                            }

                            if (cuts(centre, centreOf(ni, nj))) {
                                continue;
                            }

                            region[ni, nj] = id;
                            open.Push((ni, nj));
                        }
                    }
                }
            }

            // A level of only a few cells (a stray cell nearest a flight's
            // segment among a slope's cells at a junction, say) joins the
            // region beside it rather than standing as a mesh of its own:
            // the heights at the shared vertices are the same either way.
            int[] cells = new int[regionCount + 1];

            for (int i = 0; i < columns; i++) {
                for (int j = 0; j < rows; j++) {
                    if (region[i, j] > 0) {
                        cells[region[i, j]]++;
                    }
                }
            }

            for (int i = 0; i < columns; i++) {
                for (int j = 0; j < rows; j++) {
                    int id = region[i, j];

                    if (id <= explicitMax || cells[id] >= StrayCells) {
                        continue;
                    }

                    foreach ((int ni, int nj) in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) }) {
                        if (ni < 0 || nj < 0 || ni >= columns || nj >= rows || region[ni, nj] <= 0 || region[ni, nj] == id || cells[region[ni, nj]] < StrayCells) {
                            continue;
                        }

                        region[i, j] = region[ni, nj];
                        cells[id]--;
                        cells[region[ni, nj]]++;
                        break;
                    }
                }
            }

            return region;
        }

        /// <summary>
        /// Deletes the area's mesh assets from an earlier generation.
        /// </summary>
        private static void DeleteOld(string sector, string area)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) {
                return;
            }

            string prefix = Safe($"{sector} - {area} ");

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

        /// <summary>
        /// Smooth noise, 0-1, at a point on the ground: a random value at
        /// every whole-number corner of a grid, blended across each square
        /// with an eased curve (so the grid never shows), plus a second,
        /// finer layer at half the size and weight to break the blobs up.
        /// One unit of p is one blob; the seed gives a different field.
        /// </summary>
        internal static float Noise(Vector2 p, int seed)
        {
            return (Value(p, seed) * 2f + Value(p * 2f + new Vector2(13.7f, 7.1f), seed + 1)) / 3f;
        }

        private static float Value(Vector2 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x);
            int y = Mathf.FloorToInt(p.y);
            float fx = p.x - x;
            float fy = p.y - y;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x, y, seed);
            float b = Hash(x + 1, y, seed);
            float c = Hash(x, y + 1, seed);
            float d = Hash(x + 1, y + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>
        /// A random number 0-1 from a grid corner and a seed, the same every
        /// time on every machine (integer arithmetic only).
        /// </summary>
        private static float Hash(int x, int y, int seed)
        {
            unchecked {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }
    }
}
