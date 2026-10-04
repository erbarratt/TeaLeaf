using Interaction;
using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Add Grabbable Test Props) that puts a
    /// table with three props on it in front of the main camera, for
    /// testing picking up, carrying and dropping: a small cube, a bottle
    /// (a capsule) and a crate. Each is a Rigidbody with a Grabbable on the
    /// Interactable layer, with no grip point or snap profile - so the hand
    /// holds each by its middle until those are authored.
    ///
    /// Built from code so it can be rebuilt identically, and undone with
    /// Ctrl+Z. In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class GrabbableTestProps
    {
        private const string RootName = "Grabbable Test Props";
        private const float TableHeight = 0.9f;

        [MenuItem("TeaLeaf/Add Grabbable Test Props")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer("Environment");
            int interactable = LayerMask.NameToLayer("Interactable");

            if (environment < 0 || interactable < 0) {
                Debug.LogError("GrabbableTestProps: the 'Environment' and 'Interactable' layers must exist.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog("Grabbable Test Props", "Test props already exist in the scene. Replace them?", "Replace", "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            // On the floor, 1.2m ahead of the main camera, facing the same
            // way it does (level - only its turn about Y is used).
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Camera camera = Camera.main;

            if (camera != null) {
                Vector3 forward = camera.transform.forward;
                forward.y = 0f;

                if (forward.sqrMagnitude > 0.001f) {
                    rotation = Quaternion.LookRotation(forward);
                }

                position = camera.transform.position + rotation * new Vector3(0f, 0f, 1.2f);
                position.y = 0f;
            }

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(root, "Add Grabbable Test Props");
            Transform parent = root.transform;

            TestGeometry.Box("Table", parent, new Vector3(0f, TableHeight * 0.5f, 0f), new Vector3(1.2f, TableHeight, 0.6f), environment);

            GameObject cube = TestGeometry.Box("Prop Cube", parent, new Vector3(-0.4f, TableHeight + 0.06f, 0f), Vector3.one * 0.12f, interactable);
            MakeGrabbable(cube, 0.5f, 0.08f);

            GameObject bottle = TestGeometry.Primitive(PrimitiveType.Capsule, "Prop Bottle", parent, interactable);
            bottle.transform.localPosition = new Vector3(0f, TableHeight + 0.125f, 0f);
            bottle.transform.localScale = new Vector3(0.08f, 0.125f, 0.08f);
            MakeGrabbable(bottle, 0.7f, 0.1f);

            GameObject crate = TestGeometry.Box("Prop Crate", parent, new Vector3(0.38f, TableHeight + 0.15f, 0f), Vector3.one * 0.3f, interactable);
            MakeGrabbable(crate, 4f, 0.18f);

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// Adds the Rigidbody and Grabbable. The hold radius is a private
        /// serialized field, so it's set the way the Inspector would set it.
        /// </summary>
        private static void MakeGrabbable(GameObject prop, float mass, float holdRadius)
        {
            Rigidbody body = prop.AddComponent<Rigidbody>();
            body.mass = mass;

            SerializedObject serialized = new(prop.AddComponent<Grabbable>());
            serialized.FindProperty("holdRadius").floatValue = holdRadius;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
