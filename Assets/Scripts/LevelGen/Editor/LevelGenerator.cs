using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LevelGen
{
    /// <summary>
    /// Builds a layout spec into the open scene (menu TeaLeaf > Level Gen >
    /// Generate Level, on the spec selected in the Project window), with the
    /// values in GeneratorSettings.
    ///
    /// The spec is validated first and the report logged. What is built goes
    /// under Environment/Sectors/&lt;sector&gt;/Geometry/Generated (a
    /// GeneratedRoot), made if missing; a regeneration replaces that object
    /// whole, asking first if anything inside it was changed by hand since it
    /// was built, or if it was built from another spec. Every random choice
    /// comes from one System.Random made from the spec's seed, in a fixed
    /// order, so the same spec, catalogue, annotations and settings build the
    /// same level. One Undo step undoes the whole generation.
    ///
    /// Built so far: every street segment's width (LayoutSpec.ResolveWidths,
    /// during validation), every building's storey count (StoreyPlan, logged),
    /// the streets' ground (StreetGround), the facades, lots, dead ends and
    /// their roofs (FacadeBuilder; roofs under a Roofs object of their own),
    /// then the foundations' and the walkways' colliders merged into as few boxes
    /// as possible (MergedColliders).
    /// A street the generator can't lay is skipped and logged, so the ground
    /// can be tried while the buildings' styles are still being reviewed.
    /// Then the street test (StreetTest): a ball down every street that must
    /// touch no wall, logged as an error if one does. Last of all, always,
    /// the visibility cull (VisibilityCull): every piece a probe walking the
    /// streets can't see, at eye height or from high above them, is removed;
    /// then the roof pieces left that face the void beyond the level lose
    /// their colliders (RoofEdgeColliders), and the walls' and the roof
    /// panels' colliders left are merged into as few boxes as possible too
    /// (MergedColliders).
    /// </summary>
    internal static class LevelGenerator
    {
        private const string GeneratedName = "Generated";

        [MenuItem("TeaLeaf/Level Gen/Generate Level")]
        private static void GenerateSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);

            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !path.StartsWith(LayoutSpec.LayoutsFolder + "/", StringComparison.Ordinal)) {
                Debug.LogWarning($"LevelGenerator: select a layout spec (.json in {LayoutSpec.LayoutsFolder}) in the Project window first.");
                return;
            }

            Generate(path);
        }

        /// <summary>
        /// Validates and builds one spec into the open scene.
        /// </summary>
        internal static void Generate(string path)
        {
            if (!LayoutSpecValidator.LoadInputs(out PrefabCatalogue catalogue, out PieceAnnotations annotations)) {
                return;
            }

            GeneratorSettings settings = GeneratorSettings.Load();
            LayoutSpecValidator.Report report = LayoutSpecValidator.ValidateFile(path, catalogue, annotations, settings, out LayoutSpec spec, out System.Random random);
            LayoutSpecValidator.Log(path, report);

            if (spec == null || string.IsNullOrWhiteSpace(spec.sector)) {
                Debug.LogError($"LevelGenerator: '{path}' can't be built (see the validation above).");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Generate {spec.sector}");

            Transform geometry = FindOrMake(SceneManager.GetActiveScene(), "Environment", "Sectors", spec.sector.Trim(), "Geometry");
            Transform old = geometry.Find(GeneratedName);
            int siblingIndex = -1;

            if (old != null) {
                if (!MayReplace(old, path)) {
                    return;
                }

                siblingIndex = old.GetSiblingIndex();
                Undo.DestroyObjectImmediate(old.gameObject);
            }

            GameObject rootObject = new(GeneratedName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Generate");
            rootObject.transform.SetParent(geometry, false);

            if (siblingIndex >= 0) {
                rootObject.transform.SetSiblingIndex(siblingIndex);
            }

            GeneratedRoot root = rootObject.AddComponent<GeneratedRoot>();
            GeneratedReport.Begin();
            Dictionary<string, BuildingStyle> styles = BuildingStyle.Gather(annotations, catalogue, settings);
            // The random sequence goes on from where the street widths left it.
            PiecePlacer placer = new(catalogue);
            List<string> log = new();
            // The foliage (grass on the ground, nettles at the foundations)
            // draws from a sequence of its own, so it changes nothing else.
            StreetFoliage.Gather(annotations, placer);
            System.Random foliageRandom = StreetFoliage.RandomFor(spec);

            // Storeys first, so the rolls come before any tile's in the
            // random sequence; the facades and lots are built to it.
            StoreyPlan plan = StoreyPlan.Make(spec, styles, settings, random, log);

            Transform streets = new GameObject("Streets").transform;
            streets.SetParent(rootObject.transform, false);
            // The foliage in a tree of its own, to hide in one go.
            Transform foliage = new GameObject("Foliage").transform;
            foliage.SetParent(rootObject.transform, false);
            StreetGround.Build(spec, styles, settings, random, foliageRandom, placer, streets, foliage, log);

            Transform facades = new GameObject("Facades").transform;
            facades.SetParent(rootObject.transform, false);
            Transform lots = new GameObject("Lots").transform;
            lots.SetParent(rootObject.transform, false);
            // The roofs in a tree of their own, to hide in one go.
            Transform roofs = new GameObject("Roofs").transform;
            roofs.SetParent(rootObject.transform, false);
            FacadeBuilder.Build(spec, styles, annotations, plan, settings, random, placer, facades, lots, roofs, log);
            // Every foundation and walkway placed, the street test then checks
            // the boxes that replace their colliders.
            MergedColliders.RunFoundations(rootObject.transform, annotations, settings, log);
            MergedColliders.RunWalkways(rootObject.transform, settings, log);
            // Nettles at the foot of the foundations, once every foundation
            // stands and the ground's colliders are in (their ground is found
            // by rays).
            StreetFoliage.BuildNettles(spec, annotations, settings, foliageRandom, placer, rootObject.transform, foliage, log);

            // The free wall ends everything above has left (walls either side
            // of a bend stopping short of each other, an end standing past its
            // neighbour) are closed now, before the street test and the cull,
            // so the fillers are tested and culled like the walls.
            GapFiller.Run(rootObject.transform, annotations, styles, spec, settings, placer, log);

            // The street test: a ball down every street must touch nothing built.
            List<string> test = new();
            List<StreetSteps.Flight> flights = StreetSteps.Find(spec.streets, styles, placer, settings);
            bool valid = StreetTest.Run(spec, flights, plan, rootObject.transform, streets, settings, test);

            // Always last of everything built: every piece no one in the
            // streets can see is taken out.
            VisibilityCull.Run(spec, flights, plan, rootObject.transform, settings, log);
            // Then the roofs left that face the void beyond the level lose
            // their colliders.
            RoofEdgeColliders.Run(roofs, settings, log);
            // Then the walls' and the roof panels' colliders are merged into as
            // few boxes as possible: after the cull, so no box stands where a
            // piece was removed, and after the roof edge, which takes whole
            // pieces' colliders.
            MergedColliders.RunWalls(rootObject.transform, settings, log);
            MergedColliders.RunRoofs(rootObject.transform, annotations, settings, log);
            // Shafts of moonlight across the narrow streets' mouths: once
            // every piece and collider stands, since each beam is cut by
            // rays against them, and after the cull, which never sees them.
            MoonShafts.Build(spec, settings, rootObject.transform, log);

            // The report on the result: its markers go on the root, before
            // the fingerprint is taken.
            List<string> findings = GeneratedReport.Run(rootObject.transform, annotations, spec, out List<GeneratedRoot.Marker> markers);
            root.SetMarkers(markers);
            root.Record(path, Fingerprint(rootObject.transform));
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(rootObject.scene);
            Selection.activeGameObject = rootObject;

            StringBuilder text = new();
            text.Append($"LevelGenerator: built '{path}' into sector '{spec.sector}': {placer.Placed} piece(s).");

            foreach (string line in log) {
                text.Append('\n').Append(line);
            }

            Debug.Log(text.ToString());
            string testText = $"LevelGenerator: {string.Join("\n", test)}";

            if (valid) {
                Debug.Log(testText);
            } else {
                Debug.LogError(testText);
            }

            string reportText = $"LevelGenerator: {string.Join("\n", findings)}";

            if (markers.Count > 0) {
                Debug.LogWarning(reportText);
            } else {
                Debug.Log(reportText);
            }
        }

        /// <summary>
        /// Whether an existing Generated object may be replaced: never one
        /// the generator didn't make; otherwise only after asking if it was
        /// built from another spec or changed by hand since it was built.
        /// </summary>
        private static bool MayReplace(Transform old, string path)
        {
            GeneratedRoot root = old.GetComponent<GeneratedRoot>();

            if (root == null) {
                Debug.LogError($"LevelGenerator: '{old.name}' under '{old.parent.name}' wasn't made by the generator; rename or move it first.");
                return false;
            }

            if (!string.IsNullOrEmpty(root.SpecPath) && root.SpecPath != path
                && !EditorUtility.DisplayDialog("Replace generated level?",
                    $"This sector was built from '{root.SpecPath}'. Replace it with '{path}'?", "Replace", "Cancel")) {
                return false;
            }

            if (root.Fingerprint != Fingerprint(old)
                && !EditorUtility.DisplayDialog("Replace generated level?",
                    "Something inside the generated object was changed by hand since it was built. Regenerating throws that away. Replace it?",
                    "Replace", "Cancel")) {
                return false;
            }

            return true;
        }

        /// <summary>
        /// A short hash of everything under a generated root: each object's
        /// path, active state, local position, rotation and scale (to the
        /// millimetre and hundredth of a degree), and the prefab it is an
        /// instance of, so moving, adding, removing or swapping anything
        /// changes it.
        /// </summary>
        private static string Fingerprint(Transform root)
        {
            StringBuilder text = new();
            Transform[] all = root.GetComponentsInChildren<Transform>(true);

            foreach (Transform transform in all) {
                if (transform == root) {
                    continue;
                }

                Vector3 euler = transform.localEulerAngles;
                text.Append(AnimationUtility.CalculateTransformPath(transform, root)).Append('|')
                    .Append(transform.gameObject.activeSelf ? '1' : '0').Append('|')
                    .Append(Number(transform.localPosition.x, 3)).Append(',').Append(Number(transform.localPosition.y, 3)).Append(',')
                    .Append(Number(transform.localPosition.z, 3)).Append('|')
                    .Append(Number(euler.x, 2)).Append(',').Append(Number(euler.y, 2)).Append(',').Append(Number(euler.z, 2)).Append('|')
                    .Append(Number(transform.localScale.x, 3)).Append(',').Append(Number(transform.localScale.y, 3)).Append(',')
                    .Append(Number(transform.localScale.z, 3)).Append('|')
                    .Append(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject)).Append('\n');
            }

            return Hash128.Compute(text.ToString()).ToString();
        }

        /// <summary>
        /// A number rounded to some decimals, the same on every machine.
        /// </summary>
        private static string Number(float value, int decimals)
        {
            return Math.Round(value, decimals).ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The object at a path of names from a scene root, each one made
        /// (with Undo) if missing.
        /// </summary>
        private static Transform FindOrMake(Scene scene, string rootName, params string[] path)
        {
            Transform current = null;

            foreach (GameObject rootObject in scene.GetRootGameObjects()) {
                if (rootObject.name == rootName) {
                    current = rootObject.transform;
                    break;
                }
            }

            if (current == null) {
                GameObject made = new(rootName);
                Undo.RegisterCreatedObjectUndo(made, "Generate");
                current = made.transform;
            }

            foreach (string name in path) {
                Transform child = current.Find(name);

                if (child == null) {
                    GameObject made = new(name);
                    Undo.RegisterCreatedObjectUndo(made, "Generate");
                    child = made.transform;
                    child.SetParent(current, false);
                }

                current = child;
            }

            return current;
        }
    }
}
