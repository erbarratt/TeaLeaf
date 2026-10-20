using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Flatten Variant Colliders) that moves
    /// the colliders of the prefabs selected in the Project window -
    /// single prefabs, or whole folders, under Assets/Prefabs - onto each
    /// prefab's root object, and deletes the child objects that held
    /// nothing else. The packs put every collider on its own child, most
    /// of them an empty transform; one object with its colliders is
    /// lighter (one Transform fewer to sync and serialise per collider,
    /// and a shorter walk for SurfaceTag.Find()) and easier to see.
    ///
    /// A collider stays on its child when it needs the child: when it is
    /// turned relative to the root (a collider has no rotation of its
    /// own), when the child has its own layer or SurfaceTag or is a
    /// trigger, when it carries another script (a RampCollider, a
    /// BarrelCollider), or when it sits inside a nested prefab instance.
    /// A MeshCollider moves only from a child whose transform matches the
    /// root's exactly, since its mesh can't be re-centred.
    ///
    /// The variant is opened as if for editing, changed and saved, so each
    /// change is stored as an override - an added collider on the root, a
    /// removed collider on the child, a removed child object - and the
    /// pack's own prefab is never changed. Nothing is saved for a prefab
    /// with nothing to move.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class ColliderFlattener
    {
        private const string PrefabsRoot = "Assets/Prefabs";

        // How far a child may be turned from its root and still count as
        // aligned with it - tracking noise in an exported transform, not a
        // real angle.
        private const float AlignedAngle = 0.01f;

        // How far a child's position may be from its root's, and its scale
        // from the root's, for a MeshCollider to move without changing shape.
        private const float SameTransformTolerance = 0.0001f;

        // Why a collider was left where it is, for the report.
        private enum Keep
        {
            None,
            Rotated,
            Nested,
            Layer,
            SurfaceTag,
            Trigger,
            OtherComponent,
            MeshOffset
        }

        private struct Totals
        {
            public int Prefabs;
            public int Moved;
            public int ObjectsRemoved;
            public int Rotated;
            public int Nested;
            public int Layer;
            public int SurfaceTag;
            public int Trigger;
            public int OtherComponent;
            public int MeshOffset;
        }

        [MenuItem("TeaLeaf/Modify Prefab/Flatten Variant Colliders")]
        internal static void Flatten()
        {
            List<string> prefabs = FindSelectedPrefabs();

            if (prefabs.Count == 0) {
                Debug.LogWarning($"ColliderFlattener: select prefabs, or folders holding them, under '{PrefabsRoot}' in the Project window first.");
                return;
            }

            Totals totals = default;
            StringBuilder report = new();

            foreach (string path in prefabs) {
                FlattenPrefab(path, ref totals, report);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"ColliderFlattener: moved {totals.Moved} collider(s) to the root in {totals.Prefabs} of {prefabs.Count} prefab(s) and removed {totals.ObjectsRemoved} emptied child object(s). " +
                      $"Kept on children: {totals.Rotated} turned, {totals.Nested} in nested prefabs, {totals.Layer} on another layer, {totals.SurfaceTag} with their own surface, " +
                      $"{totals.Trigger} triggers, {totals.OtherComponent} with another component, {totals.MeshOffset} mesh colliders not at the root's transform.\n{report}");
        }

        /// <summary>
        /// Flattens one prefab and saves it if anything moved, adding a line
        /// to the report and counting what was done into the totals.
        /// </summary>
        private static void FlattenPrefab(string path, ref Totals totals, StringBuilder report)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try {
                int moved = 0;
                int removed = 0;
                // Objects whose last collider moved away, to delete once every
                // collider has been looked at (a child may hold its own child's
                // collider object, which must go first for the parent to count
                // as empty, so they are checked deepest first).
                List<GameObject> emptied = new();

                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) {
                    if (collider.gameObject == root) {
                        continue;
                    }

                    Keep keep = WhyKeep(collider, root);

                    if (keep != Keep.None) {
                        Count(keep, ref totals);
                        continue;
                    }

                    MoveToRoot(collider, root);
                    GameObject holder = collider.gameObject;
                    UnityEngine.Object.DestroyImmediate(collider);
                    moved++;

                    if (!emptied.Contains(holder)) {
                        emptied.Add(holder);
                    }
                }

                // Deepest first, so a collision object under another collision
                // object is gone before its parent is checked.
                emptied.Sort((a, b) => Depth(b.transform).CompareTo(Depth(a.transform)));

                foreach (GameObject holder in emptied) {
                    if (HoldsNothingElse(holder)) {
                        UnityEngine.Object.DestroyImmediate(holder);
                        removed++;
                    }
                }

                if (moved > 0) {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    totals.Prefabs++;
                    totals.Moved += moved;
                    totals.ObjectsRemoved += removed;
                    report.AppendLine($"{path}: moved {moved}, removed {removed} object(s)");
                }
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Why a collider must stay on its child, or None if it can move
        /// to the root.
        /// </summary>
        private static Keep WhyKeep(Collider collider, GameObject root)
        {
            GameObject holder = collider.gameObject;
            Transform child = holder.transform;

            // Part of a prefab nested inside this one: the collider belongs to
            // that prefab, and moving it would be an override on the nested
            // instance that a change to the nested prefab could then fight.
            if (PrefabUtility.GetNearestPrefabInstanceRoot(holder) != root) {
                return Keep.Nested;
            }

            if (holder.layer != root.layer) {
                return Keep.Layer;
            }

            if (collider.isTrigger) {
                return Keep.Trigger;
            }

            // A SurfaceTag anywhere between the collider and the root gives
            // the collider a surface the root's own tag (if any) wouldn't.
            for (Transform current = child; current != root.transform; current = current.parent) {
                if (current.TryGetComponent(out SurfaceTag _)) {
                    return Keep.SurfaceTag;
                }
            }

            // Anything but the transform, a mesh and colliders means a script
            // that owns or shapes the collider (RampCollider, BarrelCollider).
            foreach (Component component in holder.GetComponents<Component>()) {
                if (component is not Transform && component is not MeshFilter && component is not MeshRenderer && component is not Collider) {
                    return Keep.OtherComponent;
                }
            }

            Quaternion relative = Quaternion.Inverse(root.transform.rotation) * child.rotation;

            if (Quaternion.Angle(Quaternion.identity, relative) > AlignedAngle) {
                return Keep.Rotated;
            }

            if (collider is MeshCollider) {
                Vector3 offset = root.transform.InverseTransformPoint(child.position);
                Vector3 scaleRatio = ScaleRatio(child, root.transform);

                if (offset.sqrMagnitude > SameTransformTolerance * SameTransformTolerance ||
                    (scaleRatio - Vector3.one).sqrMagnitude > SameTransformTolerance * SameTransformTolerance) {
                    return Keep.MeshOffset;
                }
            }

            return Keep.None;
        }

        /// <summary>
        /// Adds a copy of the collider to the root, re-centred and re-sized
        /// so it covers the same space as it did on its child. Only called
        /// for a child aligned with the root, so a box's sides stay on the
        /// root's axes and a size scales axis by axis.
        /// </summary>
        private static void MoveToRoot(Collider collider, GameObject root)
        {
            Transform child = collider.transform;
            Vector3 scaleRatio = ScaleRatio(child, root.transform);

            switch (collider) {
                case BoxCollider box: {
                    BoxCollider moved = root.AddComponent<BoxCollider>();
                    moved.center = root.transform.InverseTransformPoint(child.TransformPoint(box.center));
                    moved.size = Vector3.Scale(box.size, Abs(scaleRatio));
                    CopyShared(box, moved);
                    break;
                }
                case SphereCollider sphere: {
                    // Unity scales a sphere's radius by the largest scale axis.
                    SphereCollider moved = root.AddComponent<SphereCollider>();
                    moved.center = root.transform.InverseTransformPoint(child.TransformPoint(sphere.center));
                    moved.radius = sphere.radius * MaxAbs(scaleRatio);
                    CopyShared(sphere, moved);
                    break;
                }
                case CapsuleCollider capsule: {
                    // Height scales along the capsule's axis; the radius by the
                    // larger of the other two axes.
                    CapsuleCollider moved = root.AddComponent<CapsuleCollider>();
                    moved.center = root.transform.InverseTransformPoint(child.TransformPoint(capsule.center));
                    moved.direction = capsule.direction;
                    moved.height = capsule.height * Mathf.Abs(scaleRatio[capsule.direction]);
                    moved.radius = capsule.radius * MaxAbsExcept(scaleRatio, capsule.direction);
                    CopyShared(capsule, moved);
                    break;
                }
                case MeshCollider mesh: {
                    // Only reached with the child at the root's exact transform.
                    MeshCollider moved = root.AddComponent<MeshCollider>();
                    moved.sharedMesh = mesh.sharedMesh;
                    moved.convex = mesh.convex;
                    moved.cookingOptions = mesh.cookingOptions;
                    CopyShared(mesh, moved);
                    break;
                }
                default:
                    throw new InvalidOperationException($"ColliderFlattener: no way to move a {collider.GetType().Name}.");
            }
        }

        /// <summary>
        /// Copies the settings every collider kind shares.
        /// </summary>
        private static void CopyShared(Collider from, Collider to)
        {
            to.enabled = from.enabled;
            to.isTrigger = from.isTrigger;
            to.sharedMaterial = from.sharedMaterial;
            to.providesContacts = from.providesContacts;
            to.layerOverridePriority = from.layerOverridePriority;
            to.includeLayers = from.includeLayers;
            to.excludeLayers = from.excludeLayers;
        }

        /// <summary>
        /// Whether an object can be deleted now its colliders have gone:
        /// nothing on it but the transform (and a mesh that nothing draws),
        /// and nothing under it.
        /// </summary>
        private static bool HoldsNothingElse(GameObject holder)
        {
            if (holder.transform.childCount > 0) {
                return false;
            }

            foreach (Component component in holder.GetComponents<Component>()) {
                if (component is not Transform && component is not MeshFilter) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The child's world scale over the root's, axis by axis: what a
        /// size on the child is worth on the root.
        /// </summary>
        private static Vector3 ScaleRatio(Transform child, Transform root)
        {
            Vector3 c = child.lossyScale;
            Vector3 r = root.lossyScale;
            return new Vector3(c.x / r.x, c.y / r.y, c.z / r.z);
        }

        private static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        private static float MaxAbs(Vector3 v)
        {
            return Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
        }

        private static float MaxAbsExcept(Vector3 v, int axis)
        {
            float best = 0f;

            for (int i = 0; i < 3; i++) {
                if (i != axis) {
                    best = Mathf.Max(best, Mathf.Abs(v[i]));
                }
            }

            return best;
        }

        private static int Depth(Transform transform)
        {
            int depth = 0;

            for (Transform current = transform; current != null; current = current.parent) {
                depth++;
            }

            return depth;
        }

        private static void Count(Keep keep, ref Totals totals)
        {
            switch (keep) {
                case Keep.Rotated: totals.Rotated++; break;
                case Keep.Nested: totals.Nested++; break;
                case Keep.Layer: totals.Layer++; break;
                case Keep.SurfaceTag: totals.SurfaceTag++; break;
                case Keep.Trigger: totals.Trigger++; break;
                case Keep.OtherComponent: totals.OtherComponent++; break;
                case Keep.MeshOffset: totals.MeshOffset++; break;
            }
        }

        /// <summary>
        /// The asset paths of every prefab under Assets/Prefabs selected in
        /// the Project window: the ones selected themselves, and all those
        /// inside selected folders. Anything outside Assets/Prefabs is left
        /// out - the packs' own prefabs are never changed.
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

            List<string> prefabs = new();

            foreach (string path in found) {
                if (path.StartsWith(PrefabsRoot + "/", StringComparison.OrdinalIgnoreCase)) {
                    prefabs.Add(path);
                }
            }

            prefabs.Sort(StringComparer.OrdinalIgnoreCase);
            return prefabs;
        }
    }
}
