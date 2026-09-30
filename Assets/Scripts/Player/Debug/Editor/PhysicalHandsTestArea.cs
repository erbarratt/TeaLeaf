using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Build Physical Hands Test Area) that
    /// builds a greybox row of test pieces for the physical hands - one piece
    /// per awkward case the hand sweep has to handle, at exact sizes (the
    /// narrow gap must be narrower than the hand, the thin panel thinner than
    /// the skin width matters at). Built from code so it can be rebuilt
    /// identically after changes, and undone with Ctrl+Z like any edit.
    ///
    /// Everything is a plain primitive with its own collider on the
    /// Environment layer (the Interactable crate on Interactable), under one
    /// root object that can be moved as a whole. The pieces face +Z, towards
    /// the spawn point, and stand in a row along X.
    ///
    /// In an Editor folder, so it's compiled into the editor-only assembly
    /// and never ends up in a build.
    /// </summary>
    public static class PhysicalHandsTestArea
    {
        private const string RootName = "Physical Hands Test Area";
        private const string EnvironmentLayerName = "Environment";
        private const string InteractableLayerName = "Interactable";

        // Where the row is built: behind the spawn, clear of the ledges,
        // ladder and rope already in Main.unity.
        private static readonly Vector3 _rootPosition = new(4.5f, 0f, -5.5f);

        /// <summary>
        /// Builds the test area, replacing an existing one (after asking).
        /// </summary>
        [MenuItem("TeaLeaf/Build Physical Hands Test Area")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer(EnvironmentLayerName);
            int interactable = LayerMask.NameToLayer(InteractableLayerName);

            if (environment < 0 || interactable < 0) {
                Debug.LogError($"PhysicalHandsTestArea: the '{EnvironmentLayerName}' and '{InteractableLayerName}' layers must exist.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog(
                        "Physical Hands Test Area",
                        "A test area already exists in the scene. Replace it?",
                        "Replace",
                        "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            GameObject root = new(RootName);
            root.transform.position = _rootPosition;
            Undo.RegisterCreatedObjectUndo(root, "Build Physical Hands Test Area");
            Transform parent = root.transform;

            // 1. Inside corner: a flat wall (sliding, pressing, jitter at
            //    rest) with a return wall at its left end, so the hand can
            //    be pushed into a corner where two surfaces touch it at once.
            TestGeometry.Box("Wall", parent, new Vector3(-4f, 1f, 0f), new Vector3(1.6f, 2f, 0.2f), environment);
            TestGeometry.Box("Wall Return", parent, new Vector3(-4.7f, 1f, 0.6f), new Vector3(0.2f, 2f, 1f), environment);

            // 2. Thin panel, 2cm - fast swings mustn't tunnel through it, and
            //    a hand pushed hard into it mustn't be pushed out the far side.
            //    Also the easiest place to reach past maxSeparation (0.4m) and
            //    check the snap-back.
            TestGeometry.Box("Thin Panel", parent, new Vector3(-2.5f, 1f, 0f), new Vector3(1f, 2f, 0.02f), environment);

            // 3. Table: pressing down on a horizontal surface, sliding over
            //    its edge and corners, and reaching underneath the top.
            const float tableHeight = 0.75f;
            const float topThickness = 0.05f;
            TestGeometry.Box("Table Top", parent, new Vector3(-1f, tableHeight - topThickness * 0.5f, 0f), new Vector3(1.2f, topThickness, 0.8f), environment);

            for (int x = -1; x <= 1; x += 2) {
                for (int z = -1; z <= 1; z += 2) {
                    Vector3 legPosition = new(-1f + x * 0.55f, (tableHeight - topThickness) * 0.5f, z * 0.35f);
                    TestGeometry.Box("Table Leg", parent, legPosition, new Vector3(0.05f, tableHeight - topThickness, 0.05f), environment);
                }
            }

            // 4. A crate on the table, on the Interactable layer - the hands
            //    collide with Interactable too. Static for now (no Rigidbody):
            //    pushing props comes with the grab system.
            TestGeometry.Box("Interactable Crate", parent, new Vector3(-0.8f, tableHeight + 0.15f, 0f), new Vector3(0.3f, 0.3f, 0.3f), interactable);

            // 5. Pillar: outside corners - sliding round a corner from one
            //    face onto the next.
            TestGeometry.Box("Pillar", parent, new Vector3(0.5f, 1.25f, 0f), new Vector3(0.4f, 2.5f, 0.4f), environment);

            // 6. Narrow gap, 5cm - narrower than the hand capsule (2 x 0.035m
            //    radius = 7cm). The hand can't fit, so it shouldn't jitter or
            //    get stuck pushing into it (a sweep that starts inside is let
            //    through rather than pinned).
            const float gap = 0.05f;
            const float blockWidth = 0.4f;
            float blockOffset = (gap + blockWidth) * 0.5f;
            TestGeometry.Box("Gap Block Left", parent, new Vector3(1.8f - blockOffset, 0.8f, 0f), new Vector3(blockWidth, 1.6f, 0.4f), environment);
            TestGeometry.Box("Gap Block Right", parent, new Vector3(1.8f + blockOffset, 0.8f, 0f), new Vector3(blockWidth, 1.6f, 0.4f), environment);

            // 7. Slope at 45 degrees: sliding along a surface that isn't
            //    lined up with the world axes.
            GameObject slope = TestGeometry.Box("Slope", parent, new Vector3(3.1f, 1f, 0f), new Vector3(1f, 0.05f, 1f), environment);
            slope.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

            // 8. Round post: a curved surface. A Unity cylinder comes with a
            //    CapsuleCollider, so it has rounded ends - fine at 2m tall.
            GameObject post = TestGeometry.Primitive(PrimitiveType.Cylinder, "Round Post", parent, environment);
            post.transform.localPosition = new Vector3(4.3f, 1f, 0f);
            post.transform.localScale = new Vector3(0.3f, 1f, 0.3f);

            Selection.activeGameObject = root;
        }
    }
}
