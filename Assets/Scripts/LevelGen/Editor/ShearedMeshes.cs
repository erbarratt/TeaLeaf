using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Sheared copies of wall meshes, for the side walls of a leaning facade
    /// (FacadeBuilder.PlaceSideWall): a side wall's base stays level and its
    /// back edge plumb while its top street-end corner moves towards the
    /// street by its height times the lean's tangent, so its front edge
    /// leans with the facade. No position and rotation can do that, so the
    /// shear is baked into the mesh instead: every vertex moves along the
    /// wall's width by its height above the wall's base times the shear
    /// factor, normals and tangents are carried with it, and the copy is
    /// saved as an asset in the sector's own folder under Folder, named for
    /// its source mesh, the factor's angle (rounded to AngleStep) and the
    /// way it leans, so one copy serves every wall of the sector sheared
    /// alike. The placed instance's MeshFilters (every LOD's) are given the
    /// copy as an override; its transform, colliders and prefab link are
    /// untouched, so every later pass of the generator reads it as any other
    /// wall. Each generation ends by deleting the copies in its sector's
    /// folder the new level doesn't use (ClearUnused); other sectors'
    /// folders are never touched. A folder may be deleted at any time (the
    /// sector's walls then lose their meshes until it is generated again,
    /// which remakes them). Roof panels ending on a hip line are sheared
    /// the same way at that end only (ShearEnds, FacadeRoofs).
    /// </summary>
    internal static class ShearedMeshes
    {
        // Where the sectors' folders of copies are saved.
        private const string Folder = "Assets/Data/LevelGen/Sheared";
        // The shear angles are rounded to this many degrees, so a few copies
        // serve every scale a wall is placed at.
        private const float AngleStep = 0.25f;
        // A roof panel's end shear factors (ShearEnds) are rounded to this,
        // so the panels of one corner share copies.
        private const float FactorStep = 0.01f;
        // The copies made or loaded this session, by asset path.
        private static readonly Dictionary<string, Mesh> _meshes = new();
        // The folder of the sector being generated, set by Begin().
        private static string _sectorFolder = Folder;

        /// <summary>
        /// Starts a generation of a sector: copies made from here on are
        /// saved in (and found in) that sector's own folder.
        /// </summary>
        internal static void Begin(string sector)
        {
            _sectorFolder = $"{Folder}/{Safe(sector.Trim())}";
        }

        /// <summary>
        /// Deletes every copy in the sector's folder that no MeshFilter under
        /// the generated root uses (left over from an earlier generation that
        /// leaned other walls or at other angles, or pieces the cull took
        /// out). Run once the level is complete. Not undoable, like the
        /// ground meshes a generation rewrites.
        /// </summary>
        internal static void ClearUnused(Transform root, List<string> log)
        {
            if (!AssetDatabase.IsValidFolder(_sectorFolder)) {
                log.Add("Sheared meshes: none.");
                return;
            }

            HashSet<string> used = new();

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh != null) {
                    used.Add(AssetDatabase.GetAssetPath(filter.sharedMesh));
                }
            }

            int kept = 0;
            int deleted = 0;

            foreach (string file in Directory.GetFiles(_sectorFolder, "*.asset")) {
                string path = file.Replace('\\', '/');

                if (used.Contains(path)) {
                    kept++;
                    continue;
                }

                _meshes.Remove(path);
                AssetDatabase.DeleteAsset(path);
                deleted++;
            }

            log.Add($"Sheared meshes: {kept} in use in {_sectorFolder}, {deleted} unused deleted.");
        }

        /// <summary>
        /// Whether a mesh is one of the sheared copies (saved under Folder).
        /// </summary>
        internal static bool IsSheared(Mesh mesh)
        {
            return mesh != null && AssetDatabase.GetAssetPath(mesh).StartsWith(Folder + "/");
        }

        /// <summary>
        /// Shears an instance's meshes in its own space: each vertex moves
        /// along the unit axis (right, in the piece's space) by its height
        /// above baseY times tan(angle), angle in degrees; a negative angle
        /// shears the other way. Nothing happens for an angle that rounds to
        /// 0. Mesh colliders are left as they are.
        /// </summary>
        internal static void Apply(GameObject instance, Vector3 right, float baseY, float angle)
        {
            angle = Mathf.Round(angle / AngleStep) * AngleStep;

            if (Mathf.Approximately(angle, 0f)) {
                return;
            }

            float factor = Mathf.Tan(angle * Mathf.Deg2Rad);

            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh != null) {
                    // The source's own asset (an FBX) tells copies of
                    // same-named meshes from different packs apart.
                    Mesh source = filter.sharedMesh;
                    string axis = Mathf.Abs(right.x) >= Mathf.Abs(right.z) ? "x" : "z";
                    float signed = angle * (Mathf.Abs(right.x) >= Mathf.Abs(right.z) ? Mathf.Sign(right.x) : Mathf.Sign(right.z));
                    string key = $"{axis}{(signed >= 0f ? "+" : "-")}{Mathf.Abs(signed):0.00}";
                    filter.sharedMesh = Sheared(source, key, () => Make(source, right, baseY, factor, factor));
                }
            }
        }

        /// <summary>
        /// Shears a roof panel's ends (FacadeRoofs, a row ending on a hip
        /// line), in the instance's own space: each vertex moves along x by
        /// its height (y) times a factor that runs evenly across the panel's
        /// width from minFactor at its -x end to maxFactor at its +x end, so
        /// either end's edge can slant on its own while the other stays
        /// square. Factors are rounded to FactorStep; nothing happens if both
        /// round to 0. Colliders are left as they are.
        /// </summary>
        internal static void ShearEnds(GameObject instance, float minFactor, float maxFactor)
        {
            minFactor = Mathf.Round(minFactor / FactorStep) * FactorStep;
            maxFactor = Mathf.Round(maxFactor / FactorStep) * FactorStep;

            if (Mathf.Approximately(minFactor, 0f) && Mathf.Approximately(maxFactor, 0f)) {
                return;
            }

            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh != null) {
                    Mesh source = filter.sharedMesh;
                    string key = $"ends{minFactor:+0.00;-0.00}{maxFactor:+0.00;-0.00}";
                    filter.sharedMesh = Sheared(source, key, () => Make(source, Vector3.right, 0f, minFactor, maxFactor));
                }
            }
        }

        /// <summary>
        /// The sheared copy of a mesh for one shear (key names it): from this
        /// session's cache, else the asset saved by an earlier generation,
        /// else made now (make) and saved.
        /// </summary>
        private static Mesh Sheared(Mesh source, string key, System.Func<Mesh> make)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            string path = $"{_sectorFolder}/{source.name}_{(guid.Length >= 8 ? guid.Substring(0, 8) : guid)}_{key}.asset";

            if (_meshes.TryGetValue(path, out Mesh cached) && cached != null) {
                return cached;
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (mesh == null) {
                mesh = make();
                mesh.name = Path.GetFileNameWithoutExtension(path);

                MakeFolder(Folder);
                MakeFolder(_sectorFolder);
                AssetDatabase.CreateAsset(mesh, path);
            }

            _meshes[path] = mesh;
            return mesh;
        }

        /// <summary>
        /// A copy of a mesh with the shear applied: positions moved along
        /// right by (height above baseY) times a factor running evenly from
        /// minFactor at the mesh's furthest point against right to maxFactor
        /// at its furthest along it (one factor for both: a plain shear);
        /// normals by the shear's inverse transpose and tangents by the
        /// shear itself, both normalised, at each vertex's own factor;
        /// bounds recalculated. Everything else (UVs, lightmap UVs, colours,
        /// submeshes) is the source's.
        /// </summary>
        private static Mesh Make(Mesh source, Vector3 right, float baseY, float minFactor, float maxFactor)
        {
            Mesh mesh = Object.Instantiate(source);
            Vector3[] vertices = mesh.vertices;
            float[] factors = new float[vertices.Length];
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;

            foreach (Vector3 vertex in vertices) {
                min = Mathf.Min(min, Vector3.Dot(vertex, right));
                max = Mathf.Max(max, Vector3.Dot(vertex, right));
            }

            for (int i = 0; i < vertices.Length; i++) {
                float across = max > min ? (Vector3.Dot(vertices[i], right) - min) / (max - min) : 0.5f;
                factors[i] = Mathf.Lerp(minFactor, maxFactor, across);
                vertices[i] += right * ((vertices[i].y - baseY) * factors[i]);
            }

            mesh.vertices = vertices;
            // The shear is M = I + factor * right * up^T. A normal transforms
            // by the inverse transpose, I - factor * up * right^T; a tangent
            // by M itself. (Where the factor changes across the width the
            // small extra turn that adds is left out.)
            Vector3[] normals = mesh.normals;

            if (normals.Length == vertices.Length) {
                for (int i = 0; i < normals.Length; i++) {
                    normals[i] = (normals[i] - Vector3.up * (factors[i] * Vector3.Dot(right, normals[i]))).normalized;
                }

                mesh.normals = normals;
            }

            Vector4[] tangents = mesh.tangents;

            if (tangents.Length == vertices.Length) {
                for (int i = 0; i < tangents.Length; i++) {
                    Vector3 tangent = tangents[i];
                    tangent = (tangent + right * (factors[i] * tangent.y)).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
                }

                mesh.tangents = tangents;
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Makes a folder if it isn't there (its parent must be).
        /// </summary>
        private static void MakeFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
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
