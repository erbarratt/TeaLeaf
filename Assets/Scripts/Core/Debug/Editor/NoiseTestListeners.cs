using UnityEditor;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Add Noise Test Listeners) that puts a
    /// few NoiseListenerDebug cubes in the scene - stand-in guards' ears for
    /// testing the noise system. They stand in a line ahead of the player,
    /// at ear height, at distances chosen against NoiseDebug's 8m test
    /// noise: two that hear it (near = loud, further = faint) and one out
    /// of range. Under one root object that can be moved as a whole;
    /// undoable with Ctrl+Z.
    ///
    /// In an Editor folder, so it's compiled into the editor-only assembly
    /// and never ends up in a build.
    /// </summary>
    public static class NoiseTestListeners
    {
        private const string RootName = "Noise Test Listeners";
        private const float EarHeight = 1.6f;
        private const float CubeSize = 0.3f;

        // How far ahead of the player each cube stands, in metres.
        private static readonly float[] _distances = { 3f, 6f, 12f };

        /// <summary>
        /// Builds the listeners, replacing existing ones (after asking).
        /// </summary>
        [MenuItem("TeaLeaf/Add Noise Test Listeners")]
        private static void Build()
        {
            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog(
                        "Noise Test Listeners",
                        "Test listeners already exist in the scene. Replace them?",
                        "Replace",
                        "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            // Start from the player's head, looking the way it faces but
            // level, so the line runs along the ground. The world origin
            // facing +Z if the scene has no main camera.
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;
            Camera head = Camera.main;

            if (head != null) {
                origin = head.transform.position;
                origin.y = 0f;

                Vector3 flatForward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);

                if (flatForward.sqrMagnitude > 0.001f) {
                    forward = flatForward.normalized;
                }
            }

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward));
            Undo.RegisterCreatedObjectUndo(root, "Add Noise Test Listeners");

            for (int i = 0; i < _distances.Length; i++) {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Noise Listener {_distances[i]:0}m";
                cube.transform.SetParent(root.transform, false);
                cube.transform.localPosition = new Vector3(0f, EarHeight, _distances[i]);
                cube.transform.localScale = Vector3.one * CubeSize;

                // A marker, not geometry: without its collider it can't
                // block the player or the hands, and it doesn't need a
                // physics layer.
                Object.DestroyImmediate(cube.GetComponent<Collider>());

                cube.AddComponent<NoiseListenerDebug>();
            }

            Selection.activeGameObject = root;
        }
    }
}
