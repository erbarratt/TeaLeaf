using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityMeshSimplifier;

namespace Core
{
    /// <summary>
    /// Editor window (TeaLeaf > Make LOD Meshes) that makes lower-detail
    /// copies of the meshes in the models selected in the Project window,
    /// laid out the way the packs' models are (Medieval_Barrel: a
    /// _LOD0, _LOD1 and _LOD2 mesh, and a _Collision mesh): the model's
    /// own mesh is LOD0, and the window saves a <name>_LOD1.asset and
    /// <name>_LOD2.asset mesh next to the model, plus a
    /// <name>_Collision.asset when asked (off by default) - a much
    /// simpler mesh to put on a convex MeshCollider. It makes mesh
    /// assets only: the prefab (root with a LODGroup, a child per LOD,
    /// a collision child) is put together by hand from them.
    ///
    /// The simplifying is done by the Unity Mesh Simplifier package
    /// (com.whinarn.unitymeshsimplifier, editor-only use here): quadric
    /// edge collapse, keeping UVs, normals and tangents, each LOD cut to
    /// a share of the original's triangles. Running it again on the same
    /// model rewrites the existing mesh assets in place, so a prefab that
    /// already references them keeps working.
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

        // The settings are kept between sessions in EditorPrefs under these keys.
        private const string PrefsPrefix = "TeaLeaf.LodMeshBuilder.";

        private float lod1Ratio = DefaultLod1Ratio;
        private float lod2Ratio = DefaultLod2Ratio;
        private bool makeCollision;
        private float collisionRatio = DefaultCollisionRatio;
        private bool preserveUvSeams;

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
            preserveUvSeams = EditorPrefs.GetBool(PrefsPrefix + "Seams", false);
        }

        private void OnDisable()
        {
            EditorPrefs.SetFloat(PrefsPrefix + "Lod1", lod1Ratio);
            EditorPrefs.SetFloat(PrefsPrefix + "Lod2", lod2Ratio);
            EditorPrefs.SetBool(PrefsPrefix + "Collision", makeCollision);
            EditorPrefs.SetFloat(PrefsPrefix + "CollisionRatio", collisionRatio);
            EditorPrefs.SetBool(PrefsPrefix + "Seams", preserveUvSeams);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Triangles kept, as a share of the model's mesh", EditorStyles.boldLabel);
            lod1Ratio = EditorGUILayout.Slider("LOD1", lod1Ratio, 0.05f, 0.95f);
            lod2Ratio = EditorGUILayout.Slider("LOD2", lod2Ratio, 0.02f, 0.9f);
            EditorGUILayout.Space();
            makeCollision = EditorGUILayout.Toggle("Collision mesh", makeCollision);
            using (new EditorGUI.DisabledScope(!makeCollision)) {
                collisionRatio = EditorGUILayout.Slider("Collision", collisionRatio, 0.02f, 0.5f);
            }
            EditorGUILayout.Space();
            preserveUvSeams = EditorGUILayout.Toggle(
                new GUIContent("Preserve UV seams",
                    "Keeps the edges where the texture is cut, so the texture doesn't tear across them. " +
                    "A generated model (Tripo) is cut everywhere, so this stops it being reduced much at all: " +
                    "check the triangle counts in the Console. Off, a seam may show a small tear up close."),
                preserveUvSeams);
            EditorGUILayout.Space();

            List<Object> models = SelectedModels();
            EditorGUILayout.LabelField(models.Count == 0
                ? "Select a model (FBX) or a mesh in the Project window."
                : $"{models.Count} selected.");
            using (new EditorGUI.DisabledScope(models.Count == 0)) {
                if (GUILayout.Button("Make LOD Meshes")) {
                    Make(models, lod1Ratio, lod2Ratio, makeCollision ? collisionRatio : 0f, preserveUvSeams);
                }
            }
        }

        /// <summary>
        /// The models and meshes the Project window selection holds: a model
        /// file (FBX, OBJ) or a mesh inside one.
        /// </summary>
        private static List<Object> SelectedModels()
        {
            var models = new List<Object>();
            foreach (Object selected in Selection.objects) {
                string path = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrEmpty(path)) continue;
                if (selected is Mesh || AssetImporter.GetAtPath(path) is ModelImporter) models.Add(selected);
            }
            return models;
        }

        /// <summary>
        /// Makes the LOD meshes (and the collision mesh, when
        /// <paramref name="collisionRatio"/> is above zero) for every mesh in
        /// <paramref name="models"/>, saved next to each model, and reports
        /// the triangle counts in the Console.
        /// </summary>
        public static void Make(IEnumerable<Object> models, float lod1Ratio, float lod2Ratio,
            float collisionRatio, bool preserveUvSeams)
        {
            var report = new StringBuilder("LodMeshBuilder:\n");
            try {
                AssetDatabase.StartAssetEditing();
                foreach (Object model in models) {
                    string modelPath = AssetDatabase.GetAssetPath(model);
                    foreach (Mesh source in SourceMeshes(model, modelPath, out string baseName)) {
                        string name = NameFor(source, baseName, modelPath);
                        report.Append($"  {name}: LOD0 {source.triangles.Length / 3} tris");
                        Save(source, name + "_LOD1", modelPath, lod1Ratio, preserveUvSeams, report);
                        Save(source, name + "_LOD2", modelPath, lod2Ratio, preserveUvSeams, report);
                        if (collisionRatio > 0f) {
                            // The collision mesh never shows, so its seams needn't be kept.
                            Save(source, name + "_Collision", modelPath, collisionRatio, false, report);
                        }
                        report.Append('\n');
                    }
                }
            } finally {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
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
        /// Simplifies <paramref name="source"/> to <paramref name="ratio"/> of
        /// its triangles and saves the result as <paramref name="assetName"/>.asset
        /// in the model's folder - into the existing asset if there is one, so
        /// its references hold.
        /// </summary>
        private static void Save(Mesh source, string assetName, string modelPath, float ratio,
            bool preserveUvSeams, StringBuilder report)
        {
            Mesh simplified = Simplify(source, ratio, preserveUvSeams);
            simplified.name = assetName;
            int triangles = simplified.triangles.Length / 3;
            string assetPath = Path.Combine(Path.GetDirectoryName(modelPath) ?? "", assetName + ".asset")
                .Replace('\\', '/');

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null) {
                // Copy the new data over the old asset rather than replacing the
                // file: the asset keeps its GUID, so a prefab built on it still
                // points at it.
                EditorUtility.CopySerialized(simplified, existing);
                existing.name = assetName;
                EditorUtility.SetDirty(existing);
                DestroyImmediate(simplified);
            } else {
                AssetDatabase.CreateAsset(simplified, assetPath);
            }
            report.Append($" > {assetName.Substring(assetName.LastIndexOf('_') + 1)} {triangles}");
        }

        /// <summary>
        /// A new mesh with about <paramref name="ratio"/> of the triangles of
        /// <paramref name="source"/>, by quadric edge collapse: each step
        /// merges the pair of vertices whose removal changes the shape
        /// least, carrying the UVs, normals and tangents along.
        /// </summary>
        private static Mesh Simplify(Mesh source, float ratio, bool preserveUvSeams)
        {
            var simplifier = new MeshSimplifier(source);
            SimplificationOptions options = SimplificationOptions.Default;
            options.PreserveUVSeamEdges = preserveUvSeams;
            // Open edges (a mesh that isn't closed - a plank, a sheet) stay put,
            // or the silhouette shrinks inwards as its rim is collapsed.
            options.PreserveBorderEdges = true;
            simplifier.SimplificationOptions = options;
            simplifier.SimplifyMesh(ratio);
            Mesh result = simplifier.ToMesh();
            result.RecalculateBounds();
            return result;
        }
    }
}
