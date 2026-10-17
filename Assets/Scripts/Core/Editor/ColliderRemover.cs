using UnityEditor;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Remove Colliders From Selection) that
    /// removes every collider from the objects selected in the Hierarchy and
    /// from all their children, inactive ones included, as one Undo step. A
    /// RampCollider is removed too (it would only rebuild its MeshCollider),
    /// before its collider, since it requires one. On a prefab instance the
    /// removal is stored as an override; the prefab itself is untouched.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class ColliderRemover
    {
        /// <summary>
        /// Removes the colliders under every selected object and logs how
        /// many were removed.
        /// </summary>
        [MenuItem("TeaLeaf/Remove Colliders From Selection")]
        internal static void RemoveFromSelection()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove Colliders");
            int ramps = 0;
            int colliders = 0;

            foreach (GameObject selected in Selection.gameObjects) {
                // A child selected along with its parent has already been
                // done; a removed component is null by then, so it's skipped.
                foreach (RampCollider ramp in selected.GetComponentsInChildren<RampCollider>(true)) {
                    if (ramp != null) {
                        Undo.DestroyObjectImmediate(ramp);
                        ramps++;
                    }
                }

                foreach (Collider collider in selected.GetComponentsInChildren<Collider>(true)) {
                    if (collider != null) {
                        Undo.DestroyObjectImmediate(collider);
                        colliders++;
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"ColliderRemover: removed {colliders} collider(s) and {ramps} ramp collider(s) from {Selection.gameObjects.Length} selected object(s) and their children.");
        }

        /// <summary>
        /// The command is only offered while something is selected in the
        /// Hierarchy.
        /// </summary>
        [MenuItem("TeaLeaf/Remove Colliders From Selection", true)]
        private static bool CanRemoveFromSelection()
        {
            return Selection.gameObjects.Length > 0;
        }
    }
}
