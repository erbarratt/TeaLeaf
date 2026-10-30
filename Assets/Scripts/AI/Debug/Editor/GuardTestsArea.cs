using System.Collections.Generic;
using Player;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Dev Room > Guard Tests) that lays a
    /// plain floor ahead of the camera with one guard model on it for
    /// every animation clip in Assets/Art/Animations/Guard, each looping
    /// its clip (GuardAnimationTest) with a label over its head. Every
    /// clip can then be watched side by side in Play Mode, and its
    /// playback speed and the speed the guard travels at tweaked in the
    /// Inspector until the feet don't slide.
    ///
    /// Each guard has a lane of the floor to itself, running across the
    /// player's view from left to right so the feet are seen side-on,
    /// and walks its lane over and over. The walk and run start at the
    /// speeds their clips were authored at; the rest stand still.
    ///
    /// The guards are bare instances of the guard model - no Guard,
    /// agent or controller - so nothing but the test touches their
    /// Animators. Rebuilding replaces the last area. Can be undone with
    /// Ctrl+Z. In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class GuardTestsArea
    {
        private const string RootName = "Guard Tests";
        private const string ClipFolder = "Assets/Art/Animations/Guard";
        private const string ModelPath = "Assets/Art/Models/Guard/Guard.fbx";
        private const string EnvironmentLayerName = "Environment";

        // How far ahead of the camera the near edge of the floor is.
        private const float DistanceAhead = 3f;

        // Each guard's lane: how far it walks before starting again, and
        // the gap between lanes.
        private const float LapLength = 6f;
        private const float LaneSpacing = 1.5f;
        private const float FloorThickness = 0.1f;

        // The floor sits a little above the ground so it never fights a
        // floor already there.
        private const float FloorLift = 0.02f;

        // How fast a planted foot travels back in the walk and run clips,
        // the starting point for matching their feet.
        private const float WalkClipSpeed = 0.97f;
        private const float RunClipSpeed = 3.1f;

        [MenuItem("TeaLeaf/Dev Room/Guard Tests")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer(EnvironmentLayerName);

            if (environment < 0) {
                Debug.LogError($"GuardTestsArea: the '{EnvironmentLayerName}' layer must exist.");
                return;
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

            if (model == null) {
                Debug.LogError($"GuardTestsArea: no guard model at {ModelPath}.");
                return;
            }

            List<AnimationClip> clips = FindClips();

            if (clips.Count == 0) {
                Debug.LogError($"GuardTestsArea: no animation clips in {ClipFolder}.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                Undo.DestroyObjectImmediate(existing);
            }

            // TextMeshPro's font and shader are imported into the project
            // once (Window > TextMeshPro > Import TMP Essential Resources);
            // without them the guards have no labels.
            bool hasText = Resources.Load<TMP_Settings>("TMP Settings") != null;

            if (!hasText) {
                Debug.LogWarning("GuardTestsArea: no TextMeshPro resources in the project, so the guards have no labels. Import them from Window > TextMeshPro > Import TMP Essential Resources.");
            }

            GameObject root = new(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Guard Tests");
            root.transform.SetPositionAndRotation(RootPosition(out Quaternion rotation), rotation);
            Transform parent = root.transform;

            // The floor: wide enough for a lap with a margin either side,
            // deep enough for every lane, its top at the root.
            float width = LapLength + 2f;
            float depth = clips.Count * LaneSpacing + 1f;
            TestGeometry.Box("Floor", parent, new Vector3(0f, -FloorThickness * 0.5f, depth * 0.5f), new Vector3(width, FloorThickness, depth), environment);

            for (int i = 0; i < clips.Count; i++) {
                PlaceGuard(model, clips[i], parent, new Vector3(-LapLength * 0.5f, 0f, 1f + i * LaneSpacing), hasText);
            }

            Selection.activeGameObject = root;
            Debug.Log($"GuardTestsArea: built with {clips.Count} guard(s), one per clip in {ClipFolder}.");
        }

        /// <summary>
        /// Every animation clip asset in the clip folder, in name order.
        /// </summary>
        private static List<AnimationClip> FindClips()
        {
            List<AnimationClip> clips = new();

            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipFolder })) {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));

                if (clip != null) {
                    clips.Add(clip);
                }
            }

            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips;
        }

        /// <summary>
        /// Where the area goes: ahead of the main camera on the ground,
        /// facing away from it, so the lanes run across the view. With no
        /// camera, ahead of the origin.
        /// </summary>
        private static Vector3 RootPosition(out Quaternion rotation)
        {
            Vector3 forward = Vector3.forward;
            Vector3 from = Vector3.zero;
            Camera camera = Camera.main;

            if (camera != null) {
                Vector3 cameraForward = camera.transform.forward;
                cameraForward.y = 0f;

                if (cameraForward.sqrMagnitude > 0.001f) {
                    forward = cameraForward.normalized;
                }

                from = camera.transform.position;
            }

            rotation = Quaternion.LookRotation(forward);
            Vector3 position = from + forward * DistanceAhead;
            position.y = FloorLift;
            return position;
        }

        /// <summary>
        /// A guard root at the start of its lane, facing along it (the
        /// area's +X), with an instance of the model under it, the test
        /// component and a label over its head facing the player.
        /// </summary>
        private static void PlaceGuard(GameObject model, AnimationClip clip, Transform parent, Vector3 localPosition, bool hasLabel)
        {
            GameObject guard = new(clip.name);
            guard.transform.SetParent(parent, false);
            guard.transform.localPosition = localPosition;
            guard.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, guard.transform);
            instance.name = "Model";

            TextMeshPro label = hasLabel ? BuildLabel(guard.transform) : null;

            GuardAnimationTest test = guard.AddComponent<GuardAnimationTest>();
            SerializedObject settings = new(test);
            settings.FindProperty("clip").objectReferenceValue = clip;
            settings.FindProperty("animator").objectReferenceValue = instance.GetComponent<Animator>();
            settings.FindProperty("moveSpeed").floatValue = StartingMoveSpeed(clip);
            settings.FindProperty("lapLength").floatValue = LapLength;
            settings.FindProperty("label").objectReferenceValue = label;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The travel speed a guard starts with: the clip's authored
        /// speed for the walk and run, standing still for the rest.
        /// </summary>
        private static float StartingMoveSpeed(AnimationClip clip)
        {
            if (clip.name.Contains("Walk")) {
                return WalkClipSpeed;
            }

            if (clip.name.Contains("Run")) {
                return RunClipSpeed;
            }

            return 0f;
        }

        /// <summary>
        /// A TextMeshPro label above the guard's head, turned to face the
        /// player (the guard faces along its lane, so the label is turned
        /// to its left, the area's forward).
        /// </summary>
        private static TextMeshPro BuildLabel(Transform guard)
        {
            GameObject labelObject = new("Label");
            labelObject.transform.SetParent(guard, false);
            labelObject.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            labelObject.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);

            TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
            label.rectTransform.sizeDelta = new Vector2(3f, 0.5f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.fontSize = 1.5f;
            label.text = guard.name;

            MeshRenderer labelRenderer = label.renderer as MeshRenderer;

            if (labelRenderer != null) {
                labelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                labelRenderer.receiveShadows = false;
            }

            return label;
        }
    }
}
