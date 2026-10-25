using System;
using System.Collections.Generic;
using Inventory;
using Player;
using UnityEditor;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Editor window (TeaLeaf > Modify Prefab > Pose with Hand) for posing
    /// how a hand holds something without going into Play Mode: where the
    /// hand sits on it and how its five digits are bent. Save writes both
    /// into a HandSnapProfile (made in Assets/Data/Hand Poses if there is
    /// none), exactly as the game reads them.
    ///
    /// What can be posed: every prefab under Assets/Prefabs with a
    /// Grabbable (the profile goes on its Grabbable, read by
    /// PlayerHandHolding), and the things the player holds that aren't
    /// props - the bow, an arrow, the bow string, the pack, the blackjack,
    /// the lockpicks worn on the back of the hand and carried, and a pick
    /// on the big lock (the profile goes
    /// on PlayerBow, PlayerPack, PlayerBlackjack or PlayerLockpicking in
    /// the Player prefab). Opened on a selected Grabbable prefab, it
    /// starts on that.
    ///
    /// What's posed is copied into the open scene high above the level,
    /// with a copy of the Player prefab's left or right hand visual placed
    /// where the profile puts it. The hand is moved and turned with Unity's
    /// own tools (Select Hand), the fingers with the sliders here or by
    /// selecting a bone and using the Rotate tool. Everything can be undone.
    /// The offsets are measured from a grip frame: a prop's real one
    /// (Grabbable.GetGripFrame(); a cylinder grip is posed for a head
    /// standing in front of the prop, -Z, as the player would hold it), or
    /// for the held things the frame they're placed by (the bow's origin,
    /// an arrow's nock, the pack's origin, the blackjack model's origin,
    /// the lockpicks' tips, a big lock pick's grip frame).
    ///
    /// The pose being edited is kept in the window, so it survives a script
    /// reload or Play Mode; the copies are plain Instantiates marked
    /// DontSave, destroyed when the window closes, scripts reload or play
    /// starts, and never saved with the scene. Previous and Next walk
    /// through everything that can be posed.
    /// </summary>
    public class HandPoseWindow : EditorWindow
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Core/Player.prefab";
        private const string ProfileFolder = "Assets/Data/Hand Poses";
        private const string PrefabFolder = "Assets/Prefabs";
        private const string PreviewName = "[Hand Pose Preview]";

        // What a held thing's subject key starts with (the rest is its
        // Held name); a prop's key is its prefab's GUID.
        private const string ToolKeyPrefix = "tool:";

        // How far a finger joint's curl slider goes, in degrees about the
        // bone's own X (the axis the hand's clips curl the fingers about).
        private const float MinCurl = -40f;
        private const float MaxCurl = 120f;

        // Where the copies stand: high above the level, apart from Review
        // Pieces' preview.
        private static readonly Vector3 _previewPosition = new(20f, 300f, 0f);

        // Where the head is taken to be for a cylinder grip, from the
        // grip point: in front of the prop and a little above.
        private static readonly Vector3 _headOffset = new(0f, 0.3f, -0.45f);

        private static readonly string[] _handNames = { "Left Hand", "Right Hand" };
        private static readonly string[] _fingerNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        // First bone of each finger in HandSnapProfile.FingerBoneNames, and
        // how many joints it has.
        private static readonly int[] _fingerFirstBone = { 0, 4, 7, 10, 13 };
        private static readonly int[] _fingerJoints = { 4, 3, 3, 3, 3 };

        /// <summary>
        /// The things a player holds that aren't Grabbable props, each
        /// with its profile on a component in the Player prefab.
        /// </summary>
        private enum Held
        {
            Bow,
            Arrow,
            BowString,
            Pack,
            Blackjack,
            LockpicksWorn,
            Lockpicks,
            Lockpick
        }

        /// <summary>
        /// One whole hold: both hands' offsets from the grip frame and the
        /// shared finger pose - what a HandSnapProfile stores.
        /// </summary>
        [Serializable]
        private class HoldPose
        {
            public Vector3 leftPosition;
            public Quaternion leftRotation = Quaternion.identity;
            public Vector3 rightPosition;
            public Quaternion rightRotation = Quaternion.identity;
            public Quaternion[] fingers = new Quaternion[0];
        }

        [Tooltip("What's being posed: a prop prefab's GUID, or \"tool:\" and a held thing's name, so the same one is shown again after a script reload.")]
        [SerializeField] private string subject;
        [Tooltip("The profile being edited: the subject's own, or one chosen here (given to it on Save).")]
        [SerializeField] private HandSnapProfile profile;
        [Tooltip("Which hand is shown and posed.")]
        [SerializeField] private bool isLeftHand = true;
        [Tooltip("The pose being edited, not yet saved.")]
        [SerializeField] private HoldPose working;

        // Everything that can be posed - held things first, then every
        // prefab under Assets/Prefabs whose root has a Grabbable, by path -
        // with its name and the profile it uses.
        private readonly List<string> _subjectKeys = new();
        private readonly List<string> _subjectNames = new();
        private readonly List<HandSnapProfile> _subjectProfiles = new();

        // The copy being posed against. A prop's Grabbable (null for a
        // held thing), and the transform the grip frame is read from: a
        // prop's grip point (null: its origin), or a held thing's frame.
        private GameObject _prop;
        private Grabbable _grabbable;
        private Transform _gripPoint;

        // The pack's chest, under the pack copy: mirrored for the left hand
        // as in play (Pack.SetHandleOnLeft()).
        private Transform _packChest;
        private Vector3 _packChestPosition;
        private float _packChestScale;

        private GameObject _hand;
        private Transform[] _bones;
        private Quaternion[] _restRotations;
        private Vector2 _scroll;
        private static GUIStyle _sceneLabelStyle;

        [MenuItem("TeaLeaf/Modify Prefab/Pose with Hand")]
        private static void Open()
        {
            HandPoseWindow window = GetWindow<HandPoseWindow>("Pose with Hand");
            GameObject selected = Selection.activeGameObject;

            // On a selected Grabbable prefab, that; else what it showed
            // last, or the first thing there is.
            if (selected != null && PrefabUtility.IsPartOfPrefabAsset(selected) && selected.transform.parent == null && selected.GetComponent<Grabbable>() != null) {
                window.Choose(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected)));
            } else if (string.IsNullOrEmpty(window.subject) && window._subjectKeys.Count > 0) {
                window.Choose(window._subjectKeys[0]);
            }
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Undo.undoRedoPerformed += Repaint;
            saveChangesMessage = "This hand pose has unsaved changes. Save them to its profile?";
            FindSubjects();
            ShowPreview(false);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed -= Repaint;
            Capture();
            DestroyPreview();
        }

        /// <summary>
        /// Takes the copies out before play starts (keeping the pose being
        /// edited), and puts them back after.
        /// </summary>
        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) {
                Capture();
                DestroyPreview();
            } else if (state == PlayModeStateChange.EnteredEditMode) {
                ShowPreview(false);
            }
        }

        /// <summary>
        /// Keeps the fields and the unsaved-changes flag up to date while
        /// the hand is moved with the Scene view tools.
        /// </summary>
        private void OnInspectorUpdate()
        {
            hasUnsavedChanges = HasChanges();
            Repaint();
        }

        /// <summary>
        /// Called by Unity when the window is closed with unsaved changes
        /// and Save is chosen.
        /// </summary>
        public override void SaveChanges()
        {
            Save();
            base.SaveChanges();
        }

        /// <summary>
        /// Lists everything that can be posed, with its profile: the held
        /// things whose component the Player prefab has, then every prefab
        /// under Assets/Prefabs whose root has a Grabbable, by path.
        /// </summary>
        private void FindSubjects()
        {
            _subjectKeys.Clear();
            _subjectNames.Clear();
            _subjectProfiles.Clear();

            foreach (Held tool in Enum.GetValues(typeof(Held))) {
                SerializedProperty field = ToolField(tool);

                if (field != null) {
                    _subjectKeys.Add(ToolKeyPrefix + tool);
                    _subjectNames.Add(ToolName(tool));
                    _subjectProfiles.Add(field.objectReferenceValue as HandSnapProfile);
                }
            }

            List<string> paths = new();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder })) {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            paths.Sort(StringComparer.Ordinal);

            foreach (string path in paths) {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Grabbable grabbable = asset != null ? asset.GetComponent<Grabbable>() : null;

                if (grabbable != null) {
                    _subjectKeys.Add(AssetDatabase.AssetPathToGUID(path));
                    _subjectNames.Add(asset.name);
                    _subjectProfiles.Add(grabbable.SnapProfile);
                }
            }
        }

        /// <summary>
        /// Starts posing something: its own profile and the pose it holds.
        /// </summary>
        private void Choose(string key)
        {
            subject = key;
            int index = _subjectKeys.IndexOf(key);
            profile = index >= 0 ? _subjectProfiles[index] : null;
            working = null;
            GUIUtility.keyboardControl = 0;
            ShowPreview(true);
            Repaint();
        }

        /// <summary>
        /// Moves to the subject before or after this one in the list, first
        /// asking about unsaved changes.
        /// </summary>
        private void Step(int delta)
        {
            if (_subjectKeys.Count == 0 || !ConfirmLeave()) {
                return;
            }

            int index = _subjectKeys.IndexOf(subject);
            index = index < 0 ? 0 : Mathf.Clamp(index + delta, 0, _subjectKeys.Count - 1);
            Choose(_subjectKeys[index]);
        }

        /// <summary>
        /// Before leaving this subject: if the pose has unsaved changes,
        /// asks whether to save them. False if the player cancelled.
        /// </summary>
        private bool ConfirmLeave()
        {
            if (!HasChanges()) {
                return true;
            }

            int choice = EditorUtility.DisplayDialogComplex("Pose with Hand", saveChangesMessage, "Save", "Cancel", "Discard");

            if (choice == 1) {
                return false;
            }

            if (choice == 0) {
                Save();
            }

            return true;
        }

        private void OnGUI()
        {
            if (_subjectKeys.Count == 0) {
                EditorGUILayout.HelpBox($"Nothing to pose: no bow, pack, blackjack or lockpicks in {PlayerPrefabPath}, and no prefab under {PrefabFolder} has a Grabbable on its root.", MessageType.Info);
                return;
            }

            int index = _subjectKeys.IndexOf(subject);

            EditorGUI.BeginChangeCheck();
            int chosen = EditorGUILayout.Popup("Pose", Mathf.Max(index, 0), _subjectNames.ToArray());

            if (EditorGUI.EndChangeCheck() && chosen != index && ConfirmLeave()) {
                Choose(_subjectKeys[chosen]);
                GUIUtility.ExitGUI();
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                EditorGUILayout.HelpBox("Leave Play Mode to pose the hand.", MessageType.Info);
                return;
            }

            if (_prop == null || _hand == null) {
                EditorGUILayout.HelpBox("Choose something to pose.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawProfile();
            EditorGUILayout.Space();
            DrawHand();
            EditorGUILayout.Space();
            DrawFingers();
            EditorGUILayout.EndScrollView();
            DrawButtons();
        }

        /// <summary>
        /// The profile being edited, the others that share it and a button
        /// to give this subject a profile of its own.
        /// </summary>
        private void DrawProfile()
        {
            EditorGUILayout.LabelField("Profile", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            HandSnapProfile chosen = (HandSnapProfile)EditorGUILayout.ObjectField("Hand Snap Profile", profile, typeof(HandSnapProfile), false);

            if (EditorGUI.EndChangeCheck()) {
                // Another profile's pose replaces this one's: the hand and
                // fingers show what it holds, saved to it on Save.
                profile = chosen;
                working = ReadProfile(profile);
                PlaceHand();
            }

            if (profile == null) {
                EditorGUILayout.HelpBox($"No profile yet: Save makes {ProfileFolder}/{SubjectName()}.asset and gives it to {OwnerDescription()}.", MessageType.None);
            } else {
                List<string> sharing = new();

                for (int i = 0; i < _subjectKeys.Count; i++) {
                    if (_subjectProfiles[i] == profile && _subjectKeys[i] != subject) {
                        sharing.Add(_subjectNames[i]);
                    }
                }

                if (sharing.Count > 0) {
                    EditorGUILayout.HelpBox("Shared with " + string.Join(", ", sharing) + ": saving changes them too.", MessageType.Warning);
                }

                if (!AssetDatabase.GetAssetPath(profile).StartsWith(ProfileFolder, StringComparison.Ordinal) || sharing.Count > 0) {
                    if (GUILayout.Button(new GUIContent("New profile for this", $"Copy this pose into {ProfileFolder}/{SubjectName()}.asset and use that instead, given to {OwnerDescription()} on Save."))) {
                        Capture();
                        profile = null;
                    }
                }
            }

            EditorGUILayout.LabelField("Grip", GripDescription(), EditorStyles.wordWrappedLabel);
        }

        /// <summary>
        /// Which hand, where it sits, and the buttons that move it.
        /// </summary>
        private void DrawHand()
        {
            EditorGUILayout.LabelField("Hand", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int hand = GUILayout.Toolbar(isLeftHand ? 0 : 1, _handNames);

            if (EditorGUI.EndChangeCheck()) {
                Capture();
                isLeftHand = hand == 0;

                // Each hand gets its own bow (mirrored) or big lock pick
                // (the left the hook, the right the wrench), so the whole
                // preview is remade; for anything else only the hand.
                if (TryGetTool(out Held tool) && (tool == Held.Bow || tool == Held.BowString || tool == Held.Lockpick)) {
                    ShowPreview(false);
                } else {
                    ShowHand();
                }
            }

            Capture();
            Vector3 position = isLeftHand ? working.leftPosition : working.rightPosition;
            Quaternion rotation = isLeftHand ? working.leftRotation : working.rightRotation;
            EditorGUILayout.LabelField("Offset", $"{position.x:0.0000}, {position.y:0.0000}, {position.z:0.0000} m");
            Vector3 euler = SignedEuler(rotation);
            EditorGUILayout.LabelField("Rotation", $"{euler.x:0.00}, {euler.y:0.00}, {euler.z:0.00}°");

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("Select Hand", "Select the hand so the Move and Rotate tools place it."))) {
                Selection.activeGameObject = _hand;
            }

            string other = isLeftHand ? "Right" : "Left";

            if (GUILayout.Button(new GUIContent($"Mirror to {other} Hand", $"Make the {other.ToLowerInvariant()} hand's pose this one mirrored across the grip frame's X (right for anything the same either side of its grip). The fingers are shared already."))) {
                Undo.RecordObject(this, "Mirror Hand Pose");
                MirrorToOtherHand();
            }

            if (GUILayout.Button(new GUIContent("Frame", "Point the Scene view at what's posed and the hand."))) {
                FramePreview();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// A curl slider per finger (turns all its joints together) and per
        /// joint, the joint's other two angles, and a button selecting the
        /// bone for the Rotate tool.
        /// </summary>
        private void DrawFingers()
        {
            EditorGUILayout.LabelField("Fingers (degrees; curl is about each bone's X)", EditorStyles.boldLabel);

            for (int finger = 0; finger < _fingerNames.Length; finger++) {
                int first = _fingerFirstBone[finger];
                int joints = _fingerJoints[finger];

                // The finger's curl: the joints' average, moved together.
                float average = 0f;
                int found = 0;

                for (int j = 0; j < joints; j++) {
                    if (_bones[first + j] != null) {
                        average += TransformUtils.GetInspectorRotation(_bones[first + j]).x;
                        found++;
                    }
                }

                if (found == 0) {
                    EditorGUILayout.LabelField(_fingerNames[finger], "no bones found");
                    continue;
                }

                average /= found;
                EditorGUI.BeginChangeCheck();
                float curl = EditorGUILayout.Slider(_fingerNames[finger], average, MinCurl, MaxCurl);

                if (EditorGUI.EndChangeCheck()) {
                    for (int j = 0; j < joints; j++) {
                        Transform bone = _bones[first + j];

                        if (bone != null) {
                            Undo.RecordObject(bone, "Curl Finger");
                            Vector3 angles = TransformUtils.GetInspectorRotation(bone);
                            angles.x += curl - average;
                            TransformUtils.SetInspectorRotation(bone, angles);
                        }
                    }
                }

                EditorGUI.indentLevel++;

                for (int j = 0; j < joints; j++) {
                    DrawJoint(_bones[first + j], $"Joint {j + 1}");
                }

                EditorGUI.indentLevel--;
            }

            if (GUILayout.Button(new GUIContent("Rest Fingers", "Put every finger back as the hand model has it, straight."))) {
                for (int i = 0; i < _bones.Length; i++) {
                    if (_bones[i] != null) {
                        Undo.RecordObject(_bones[i], "Rest Fingers");
                        _bones[i].localRotation = _restRotations[i];
                    }
                }
            }
        }

        /// <summary>
        /// One joint: curl slider, the other two angles, and Select.
        /// </summary>
        private static void DrawJoint(Transform bone, string label)
        {
            if (bone == null) {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            Vector3 angles = TransformUtils.GetInspectorRotation(bone);
            EditorGUI.BeginChangeCheck();
            angles.x = EditorGUILayout.Slider(label, angles.x, MinCurl, MaxCurl);
            angles.y = EditorGUILayout.FloatField(angles.y, GUILayout.Width(50f));
            angles.z = EditorGUILayout.FloatField(angles.z, GUILayout.Width(50f));

            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(bone, "Turn Finger Joint");
                TransformUtils.SetInspectorRotation(bone, angles);
            }

            if (GUILayout.Button(new GUIContent("Select", $"Select {bone.name} to turn it with the Rotate tool."), GUILayout.Width(55f))) {
                Selection.activeTransform = bone;
                Tools.current = Tool.Rotate;
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Moving through the subjects, reverting, saving and closing. Kept
        /// below the scroll view so they are always in reach.
        /// </summary>
        private void DrawButtons()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("◀ Previous")) {
                Step(-1);
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Next ▶")) {
                Step(1);
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button(new GUIContent("Revert", "Throw away the changes and show the saved pose again."))) {
                working = ReadProfile(profile);
                PlaceHand();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!HasChanges() && profile != null)) {
                if (GUILayout.Button("Save", GUILayout.Height(28f))) {
                    Save();
                }
            }

            if (GUILayout.Button("Close", GUILayout.Height(28f))) {
                if (ConfirmLeave()) {
                    hasUnsavedChanges = false;
                    Close();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Writes the pose to the profile, making the profile first (and
        /// giving it to the prop's Grabbable, or the held thing's component
        /// in the Player prefab) if there is none or it uses another.
        /// </summary>
        private void Save()
        {
            Capture();

            if (working == null || _prop == null) {
                return;
            }

            // A new profile is filled before it becomes an asset, and every
            // asset is written before a prefab is saved: saving the prefab
            // reimports, which reloads an asset just made from its file and
            // loses changes not yet written.
            bool isNew = profile == null;

            if (isNew) {
                profile = CreateInstance<HandSnapProfile>();
            }

            SerializedObject data = new(profile);
            data.FindProperty("leftPositionOffset").vector3Value = Round(working.leftPosition, 10000f);
            data.FindProperty("leftRotationOffset").vector3Value = Round(SignedEuler(working.leftRotation), 100f);
            data.FindProperty("rightPositionOffset").vector3Value = Round(working.rightPosition, 10000f);
            data.FindProperty("rightRotationOffset").vector3Value = Round(SignedEuler(working.rightRotation), 100f);
            SerializedProperty fingers = data.FindProperty("fingerRotations");
            fingers.arraySize = working.fingers.Length;

            for (int i = 0; i < working.fingers.Length; i++) {
                fingers.GetArrayElementAtIndex(i).quaternionValue = working.fingers[i];
            }

            data.ApplyModifiedProperties();

            if (isNew) {
                AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath($"{ProfileFolder}/{SubjectName()}.asset"));
            }

            AssetDatabase.SaveAssets();
            GiveProfile();
            AssetDatabase.SaveAssets();
            FindSubjects();

            // Back to what was written (rounded), so it reads as saved.
            working = ReadProfile(profile);
            PlaceHand();
            hasUnsavedChanges = false;
            ShowNotification(new GUIContent($"Saved to {profile.name}"));
        }

        /// <summary>
        /// The subject takes the profile, if it holds another (or none):
        /// the prop's Grabbable, or the held thing's field in the Player
        /// prefab. Its prefab is saved.
        /// </summary>
        private void GiveProfile()
        {
            SerializedProperty field;
            GameObject prefab;

            if (TryGetTool(out Held tool)) {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                field = ToolField(tool);
            } else {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(subject));
                field = prefab != null ? new SerializedObject(prefab.GetComponent<Grabbable>()).FindProperty("snapProfile") : null;
            }

            if (field == null || field.objectReferenceValue == profile) {
                return;
            }

            field.objectReferenceValue = profile;
            field.serializedObject.ApplyModifiedProperties();
            PrefabUtility.SavePrefabAsset(prefab);
        }

        /// <summary>
        /// The pose a profile holds, as the window edits it: fingers at rest
        /// if it has no finger pose; zero offsets with no profile.
        /// </summary>
        private HoldPose ReadProfile(HandSnapProfile source)
        {
            HoldPose pose = new() { fingers = _restRotations != null ? (Quaternion[])_restRotations.Clone() : new Quaternion[0] };

            if (source == null) {
                return pose;
            }

            SerializedObject data = new(source);
            pose.leftPosition = data.FindProperty("leftPositionOffset").vector3Value;
            pose.leftRotation = Quaternion.Euler(data.FindProperty("leftRotationOffset").vector3Value);
            pose.rightPosition = data.FindProperty("rightPositionOffset").vector3Value;
            pose.rightRotation = Quaternion.Euler(data.FindProperty("rightRotationOffset").vector3Value);

            if (source.HasFingerPose) {
                for (int i = 0; i < pose.fingers.Length; i++) {
                    pose.fingers[i] = source.FingerRotation(i);
                }
            }

            return pose;
        }

        /// <summary>
        /// True if the pose being edited differs from what the profile holds
        /// (by more than a tenth of a millimetre or a fiftieth of a degree).
        /// </summary>
        private bool HasChanges()
        {
            if (working == null || _restRotations == null) {
                return false;
            }

            Capture();
            HoldPose saved = ReadProfile(profile);

            bool same = Vector3.Distance(saved.leftPosition, working.leftPosition) < 0.0001f
                && Vector3.Distance(saved.rightPosition, working.rightPosition) < 0.0001f
                && Quaternion.Angle(saved.leftRotation, working.leftRotation) < 0.02f
                && Quaternion.Angle(saved.rightRotation, working.rightRotation) < 0.02f;

            for (int i = 0; same && i < working.fingers.Length; i++) {
                same = Quaternion.Angle(saved.fingers[i], working.fingers[i]) < 0.02f;
            }

            return !same;
        }

        /// <summary>
        /// Reads the shown hand back into the pose being edited: its offset
        /// from the grip frame and its finger rotations.
        /// </summary>
        private void Capture()
        {
            if (_hand == null || _prop == null || working == null) {
                return;
            }

            GetGripFrame(isLeftHand, out Vector3 gripPosition, out Quaternion gripRotation);
            Quaternion inverse = Quaternion.Inverse(gripRotation);
            Vector3 position = inverse * (_hand.transform.position - gripPosition);
            Quaternion rotation = inverse * _hand.transform.rotation;

            if (isLeftHand) {
                working.leftPosition = position;
                working.leftRotation = rotation;
            } else {
                working.rightPosition = position;
                working.rightRotation = rotation;
            }

            for (int i = 0; i < _bones.Length; i++) {
                if (_bones[i] != null) {
                    working.fingers[i] = _bones[i].localRotation;
                }
            }
        }

        /// <summary>
        /// The other hand's offset becomes this one's mirrored across the
        /// grip frame's X: position x negated, and the rotation reflected
        /// the same way (x, -y, -z, w), which for the mirrored right visual
        /// (scale.x -1) is exactly the left hand seen in a mirror.
        /// </summary>
        private void MirrorToOtherHand()
        {
            Capture();
            Vector3 position = isLeftHand ? working.leftPosition : working.rightPosition;
            Quaternion rotation = isLeftHand ? working.leftRotation : working.rightRotation;
            Vector3 mirroredPosition = new(-position.x, position.y, position.z);
            Quaternion mirroredRotation = new(rotation.x, -rotation.y, -rotation.z, rotation.w);

            if (isLeftHand) {
                working.rightPosition = mirroredPosition;
                working.rightRotation = mirroredRotation;
            } else {
                working.leftPosition = mirroredPosition;
                working.leftRotation = mirroredRotation;
            }

            ShowNotification(new GUIContent($"Mirrored to the {(isLeftHand ? "right" : "left")} hand"));
        }

        /// <summary>
        /// The grip frame for a hand, as the copy lies: a prop's (for a
        /// cylinder, the hand's side of it seen from a head in front), or
        /// a held thing's frame.
        /// </summary>
        private void GetGripFrame(bool isLeft, out Vector3 position, out Quaternion rotation)
        {
            if (_grabbable == null) {
                Transform frame = _gripPoint != null ? _gripPoint : _prop.transform;
                position = frame.position;
                rotation = frame.rotation;
                return;
            }

            Vector3 middle = _gripPoint != null ? _gripPoint.position : _prop.transform.position;
            _grabbable.GetGripFrame(isLeft, middle, middle + _headOffset, out position, out rotation);
        }

        /// <summary>
        /// Puts a copy of what's chosen and the hand in the open scene, at
        /// the preview position, never saved.
        /// </summary>
        private void ShowPreview(bool frame)
        {
            DestroyPreview();

            if (string.IsNullOrEmpty(subject) || EditorApplication.isPlayingOrWillChangePlaymode) {
                return;
            }

            if (TryGetTool(out Held tool)) {
                BuildToolPreview(tool);
            } else {
                BuildPropPreview();
            }

            if (_prop == null) {
                return;
            }

            foreach (Transform child in _prop.GetComponentsInChildren<Transform>(true)) {
                child.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            }

            ShowHand();

            if (frame) {
                FramePreview();
            }
        }

        /// <summary>
        /// A copy of the chosen Grabbable prefab, and its grip point.
        /// </summary>
        private void BuildPropPreview()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(subject));

            if (asset == null || asset.GetComponent<Grabbable>() == null) {
                return;
            }

            _prop = Instantiate(asset, _previewPosition, asset.transform.rotation);
            _prop.name = PreviewName;
            _grabbable = _prop.GetComponent<Grabbable>();
            _gripPoint = new SerializedObject(_grabbable).FindProperty("gripPoint").objectReferenceValue as Transform;
        }

        /// <summary>
        /// A copy of a held thing as the game places it, from the models
        /// set on PlayerBow, PlayerArrows, Pack, PlayerBlackjack and
        /// PlayerLockpicking in the Player prefab: the bow (frame: its
        /// origin), an arrow (frame: its nock, Z to the tip), the bow with
        /// an arrow nocked at the string's rest (frame: the nock), the
        /// pack's chest with a block where its spaces are (frame: the
        /// pack's origin), the blackjack (frame: its model's origin), the
        /// lockpicks (frame: their tips, Z to the handles) or the shown
        /// hand's big lock pick (frame: its grip frame).
        /// </summary>
        private void BuildToolPreview(Held tool)
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            if (player == null) {
                return;
            }

            _prop = new GameObject(PreviewName);
            _prop.transform.position = _previewPosition;

            if (tool == Held.Pack) {
                BuildPackPreview(player);
                return;
            }

            if (tool == Held.Blackjack) {
                // The model under the frame object, keeping its import
                // turn, as PlayerBlackjack lays it.
                PlayerBlackjack blackjack = player.GetComponentInChildren<PlayerBlackjack>(true);
                GameObject model = blackjack != null ? new SerializedObject(blackjack).FindProperty("blackjackModel").objectReferenceValue as GameObject : null;

                if (model == null) {
                    Debug.LogError($"Pose with Hand: no blackjack model on PlayerBlackjack in {PlayerPrefabPath}.");
                    return;
                }

                Instantiate(model, _prop.transform, false);
                return;
            }

            if (tool == Held.Arrow) {
                _gripPoint = BuildArrowPreview(player, _prop.transform);
                return;
            }

            if (tool == Held.LockpicksWorn || tool == Held.Lockpicks || tool == Held.Lockpick) {
                BuildLockpickPreview(player, tool == Held.Lockpick);
                return;
            }

            // The bow for the player whose hand is shown: the left hand
            // holds a right-handed player's bow and the right hand draws
            // it; the other way round, the left-handed bow (empty: the
            // right-handed one, as PlayerBow does).
            PlayerBow bow = player.GetComponentInChildren<PlayerBow>(true);
            bool isRightHandedBow = (tool == Held.Bow) == isLeftHand;
            SerializedObject bowFields = bow != null ? new SerializedObject(bow) : null;
            GameObject bowModel = bowFields != null ? bowFields.FindProperty(isRightHandedBow ? "bowModel" : "leftHandedBowModel").objectReferenceValue as GameObject : null;

            if (bowModel == null && bowFields != null) {
                bowModel = bowFields.FindProperty("bowModel").objectReferenceValue as GameObject;
            }

            if (bowModel == null) {
                Debug.LogError($"Pose with Hand: no bow model on PlayerBow in {PlayerPrefabPath}.");
                return;
            }

            Transform bowCopy = Instantiate(bowModel, _prop.transform, false).transform;

            if (tool == Held.Bow) {
                return;
            }

            // The string: an arrow nocked at the string's rest, pointing
            // through the arrow rest, as PlayerBow lays it.
            Transform stringRest = bowCopy.Find("String_Rest");
            Transform arrowRest = bowCopy.Find("Arrow_Rest");

            if (stringRest == null || arrowRest == null) {
                Debug.LogError("Pose with Hand: the bow model has no String_Rest or Arrow_Rest.");
                return;
            }

            Vector3 nock = bowCopy.InverseTransformPoint(stringRest.position);
            Vector3 direction = (bowCopy.InverseTransformPoint(arrowRest.position) - nock).normalized;
            Transform arrow = new GameObject("Nocked Arrow").transform;
            arrow.SetParent(bowCopy, false);
            arrow.SetLocalPositionAndRotation(nock, Quaternion.LookRotation(direction, Vector3.up));
            _gripPoint = BuildArrowPreview(player, arrow) != null ? arrow : null;
        }

        /// <summary>
        /// An arrow as PlayerArrows draws it, its nock on parent's origin
        /// and its tip along parent's Z (the stun arrow's mesh, placed by
        /// the longest arrow's length, as in play). Returns parent, or null
        /// with no arrow meshes.
        /// </summary>
        private static Transform BuildArrowPreview(GameObject player, Transform parent)
        {
            PlayerArrows arrows = player.GetComponentInChildren<PlayerArrows>(true);

            if (arrows == null) {
                return null;
            }

            SerializedObject data = new(arrows);
            string[] meshFields = { "waterArrow", "noisemakerArrow", "ropeArrow", "stunArrow", "sleepDart", "mossArrow" };
            float length = 0f;

            foreach (string field in meshFields) {
                if (data.FindProperty(field).objectReferenceValue is Mesh kind) {
                    length = Mathf.Max(length, kind.bounds.size.z);
                }
            }

            Mesh mesh = data.FindProperty("stunArrow").objectReferenceValue as Mesh;

            if (mesh == null) {
                Debug.LogError($"Pose with Hand: no stun arrow mesh on PlayerArrows in {PlayerPrefabPath}.");
                return null;
            }

            // The mesh is centred on its origin: half the length forward
            // puts its back end on the nock (for the longest arrow).
            GameObject model = new("Arrow Model");
            model.transform.SetParent(parent, false);
            model.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = data.FindProperty("arrowMaterial").objectReferenceValue as Material;
            return parent;
        }

        /// <summary>
        /// The lockpicks model from PlayerLockpicking. Worn or carried:
        /// both picks at the player's size, the frame their tips (the copy's
        /// origin). On the big lock: only the shown hand's pick (left the
        /// hook, right the wrench) at the big lock's size, lying along Z,
        /// the frame its grip frame (BigLock.GripTurn, gripAlong out). The
        /// big lock's size is the open scene's Big Lock's, or BigLock's
        /// own defaults with none.
        /// </summary>
        private void BuildLockpickPreview(GameObject player, bool isOnBigLock)
        {
            PlayerLockpicking lockpicking = player.GetComponentInChildren<PlayerLockpicking>(true);
            SerializedObject data = lockpicking != null ? new SerializedObject(lockpicking) : null;
            GameObject model = data != null ? data.FindProperty("picksModel").objectReferenceValue as GameObject : null;

            if (model == null) {
                Debug.LogError($"Pose with Hand: no lockpicks model on PlayerLockpicking in {PlayerPrefabPath}.");
                return;
            }

            if (!isOnBigLock) {
                Transform picks = Instantiate(model, _prop.transform, false).transform;
                picks.localScale = Vector3.one * data.FindProperty("pickLength").floatValue;
                return;
            }

            Transform part = model.transform.Find(isLeftHand ? BigLock.LeftPickName : BigLock.RightPickName);

            if (part == null) {
                Debug.LogError($"Pose with Hand: the lockpicks model has no {(isLeftHand ? BigLock.LeftPickName : BigLock.RightPickName)}.");
                return;
            }

            // Read from a component, never woken here (BigLock has no
            // ExecuteAlways): the scene's, or a passing one for defaults.
            BigLock bigLock = FindFirstObjectByType<BigLock>();
            GameObject passing = null;

            if (bigLock == null) {
                passing = new GameObject { hideFlags = HideFlags.HideAndDontSave };
                bigLock = passing.AddComponent<BigLock>();
            }

            SerializedObject lockData = new(bigLock);
            float pickLength = lockData.FindProperty("pickLength").floatValue;
            float gripAlong = lockData.FindProperty("gripAlong").floatValue;

            if (passing != null) {
                DestroyImmediate(passing);
            }

            GameObject pick = Instantiate(part.gameObject, _prop.transform, false);
            pick.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            pick.transform.localScale = Vector3.one * pickLength;

            _gripPoint = new GameObject("Grip Frame").transform;
            _gripPoint.SetParent(_prop.transform, false);
            _gripPoint.SetLocalPositionAndRotation(new Vector3(0f, 0f, gripAlong), BigLock.GripTurn);
        }

        /// <summary>
        /// The pack: its chest where Pack puts it (mirrored for the left
        /// hand, see PlaceChest()), and a thin block covering its spaces on
        /// the back wall, so the pose can be checked against both.
        /// </summary>
        private void BuildPackPreview(GameObject player)
        {
            Pack pack = player.GetComponentInChildren<Pack>(true);

            if (pack == null) {
                Debug.LogError($"Pose with Hand: no Pack in {PlayerPrefabPath}.");
                return;
            }

            SerializedObject data = new(pack);

            if (data.FindProperty("chestModel").objectReferenceValue is GameObject chestModel) {
                _packChest = new GameObject("Chest").transform;
                _packChest.SetParent(_prop.transform, false);
                Instantiate(chestModel, _packChest, false);
                _packChest.localRotation = Quaternion.Euler(data.FindProperty("chestRotation").vector3Value);
                _packChestPosition = data.FindProperty("chestPosition").vector3Value;
                _packChestScale = data.FindProperty("chestScale").floatValue;
            }

            int columns = data.FindProperty("columns").intValue;
            int rows = data.FindProperty("rows").intValue;
            float cell = data.FindProperty("cellSize").floatValue;
            float gap = data.FindProperty("cellGap").floatValue;
            GameObject spaces = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(spaces.GetComponent<Collider>());
            spaces.name = "Spaces";
            spaces.transform.SetParent(_prop.transform, false);
            spaces.transform.localScale = new Vector3(columns * cell + (columns + 1) * gap, (rows + 1) * cell + (rows + 2) * gap, 0.004f);
        }

        /// <summary>
        /// Puts the pack copy's chest as Pack.SetHandleOnLeft() does for
        /// the hand shown: handle on that hand's side.
        /// </summary>
        private void PlaceChest()
        {
            if (_packChest == null) {
                return;
            }

            float side = isLeftHand ? 1f : -1f;
            _packChest.localPosition = new Vector3(_packChestPosition.x * side, _packChestPosition.y, _packChestPosition.z);
            _packChest.localScale = new Vector3(_packChestScale * side, _packChestScale, _packChestScale);
        }

        /// <summary>
        /// (Re)makes the hand copy for the chosen hand, placed and posed as
        /// the pose being edited has it.
        /// </summary>
        private void ShowHand()
        {
            if (_hand != null) {
                DestroyImmediate(_hand);
            }

            _hand = null;

            if (_prop == null) {
                return;
            }

            PlaceChest();

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerHandVisuals visuals = player != null ? player.GetComponentInChildren<PlayerHandVisuals>(true) : null;
            Transform visual = visuals != null ? (isLeftHand ? visuals.LeftHandVisual : visuals.RightHandVisual) : null;

            if (visual == null) {
                Debug.LogError($"Pose with Hand: no hand visual on PlayerHandVisuals in {PlayerPrefabPath}.");
                return;
            }

            _hand = Instantiate(visual.gameObject);
            _hand.name = $"{PreviewName} {(isLeftHand ? "Left" : "Right")} Hand";
            _hand.transform.localScale = visual.lossyScale;

            foreach (Transform child in _hand.GetComponentsInChildren<Transform>(true)) {
                child.gameObject.hideFlags = HideFlags.DontSave;
            }

            // Nothing animates it here: the pose is the bones themselves.
            Animator animator = _hand.GetComponent<Animator>();

            if (animator != null) {
                DestroyImmediate(animator);
            }

            _bones = HandSnapProfile.FindFingerBones(_hand.transform);
            _restRotations = new Quaternion[_bones.Length];

            for (int i = 0; i < _bones.Length; i++) {
                _restRotations[i] = _bones[i] != null ? _bones[i].localRotation : Quaternion.identity;
            }

            working ??= ReadProfile(profile);

            if (working.fingers.Length != _bones.Length) {
                working.fingers = (Quaternion[])_restRotations.Clone();
            }

            PlaceHand();
        }

        /// <summary>
        /// Puts the hand copy where the pose being edited has it, fingers
        /// included - the same copy, so a selected hand or bone stays
        /// selected.
        /// </summary>
        private void PlaceHand()
        {
            if (_hand == null || working == null) {
                return;
            }

            GetGripFrame(isLeftHand, out Vector3 gripPosition, out Quaternion gripRotation);
            Vector3 position = isLeftHand ? working.leftPosition : working.rightPosition;
            Quaternion rotation = isLeftHand ? working.leftRotation : working.rightRotation;
            _hand.transform.SetPositionAndRotation(gripPosition + gripRotation * position, gripRotation * rotation);

            for (int i = 0; i < _bones.Length; i++) {
                if (_bones[i] != null) {
                    _bones[i].localRotation = working.fingers[i];
                }
            }

            SceneView.RepaintAll();
        }

        /// <summary>
        /// Removes the copies, if there are any.
        /// </summary>
        private void DestroyPreview()
        {
            if (_hand != null) {
                DestroyImmediate(_hand);
            }

            if (_prop != null) {
                DestroyImmediate(_prop);
            }

            _hand = null;
            _prop = null;
            _grabbable = null;
            _gripPoint = null;
            _packChest = null;
        }

        /// <summary>
        /// Points the Scene view at what's posed and the hand.
        /// </summary>
        private void FramePreview()
        {
            SceneView view = SceneView.lastActiveSceneView;

            if (view == null || _prop == null) {
                return;
            }

            Bounds bounds = new(_prop.transform.position, Vector3.one * 0.1f);

            foreach (Renderer renderer in _prop.GetComponentsInChildren<Renderer>()) {
                bounds.Encapsulate(renderer.bounds);
            }

            if (_hand != null) {
                foreach (Renderer renderer in _hand.GetComponentsInChildren<Renderer>()) {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            view.Frame(bounds, false);
        }

        /// <summary>
        /// Draws the grip frame the offsets are measured from (red X, green
        /// Y, blue Z) and, for a cylinder, where the head is taken to be.
        /// </summary>
        private void OnSceneGUI(SceneView view)
        {
            if (_prop == null || Event.current.type != EventType.Repaint) {
                return;
            }

            _sceneLabelStyle ??= new GUIStyle(EditorStyles.whiteBoldLabel);
            GetGripFrame(isLeftHand, out Vector3 position, out Quaternion rotation);
            const float axis = 0.08f;
            Handles.color = Color.red;
            Handles.DrawLine(position, position + rotation * Vector3.right * axis, 2f);
            Handles.color = Color.green;
            Handles.DrawLine(position, position + rotation * Vector3.up * axis, 2f);
            Handles.color = Color.blue;
            Handles.DrawLine(position, position + rotation * Vector3.forward * axis, 2f);
            Handles.Label(position, "grip", _sceneLabelStyle);

            if (IsCylinder()) {
                Vector3 middle = _gripPoint != null ? _gripPoint.position : _prop.transform.position;
                Vector3 head = middle + _headOffset;
                Handles.color = Color.yellow;
                Handles.DrawDottedLine(middle, head, 3f);
                Handles.Label(head, "head", _sceneLabelStyle);
            }
        }

        /// <summary>
        /// True if a prop is being posed, gripped anywhere round a
        /// cylinder.
        /// </summary>
        private bool IsCylinder()
        {
            return _grabbable != null && new SerializedObject(_grabbable).FindProperty("gripShape").enumValueIndex == (int)Grabbable.GripShape.Cylinder;
        }

        /// <summary>
        /// A line saying what the grip frame is, for the window.
        /// </summary>
        private string GripDescription()
        {
            if (TryGetTool(out Held tool)) {
                return tool switch {
                    Held.Bow => "The bow's origin and axes (Z the way it shoots, Y its top). The hand holding the bow.",
                    Held.Arrow => "The arrow's nock (Z along the arrow to its tip). The drawing hand holding an arrow off the string.",
                    Held.BowString => "The nock of an arrow on the string at rest. In play only where the nock sits in the hand and the fingers are used: the hand keeps its controller's turn.",
                    Held.Blackjack => "The blackjack model's origin (X along it to the head, Y up). The hand without the lockpicks holding it.",
                    Held.LockpicksWorn => "The picks' tips (Z along them to the handles). The hand wearing them on its back (right-handed: the left). Only where they sit is used: the hand keeps its input curl.",
                    Held.Lockpicks => "The picks' tips (Z along them to the handles). The hand without the lockpicks carrying both to a lock.",
                    Held.Lockpick => "The grip frame on this hand's big lock pick (left the hook, right the wrench): Y along the pick away from the keyway, Z into the lock. In play the pick leans out of the lock's face and turns with the hand.",
                    _ => "The pack's origin: the middle of its spaces on the chest's back wall (X right, Y up, Z away from the player). The chest's handle is on the shown hand's side, as in play."
                };
            }

            string point = _gripPoint != null ? $"grip point '{_gripPoint.name}'" : "no grip point (the prop's origin)";
            return IsCylinder()
                ? $"Cylinder round {point}, posed for a head {-_headOffset.z:0.##} m in front and {_headOffset.y:0.##} m above."
                : $"Point, at {point}.";
        }

        /// <summary>
        /// The chosen subject's name, for a new profile: a held thing's
        /// profile name, or the prop prefab's.
        /// </summary>
        private string SubjectName()
        {
            if (TryGetTool(out Held tool)) {
                return tool switch {
                    Held.Bow => "BowGrip",
                    Held.Arrow => "ArrowHold",
                    Held.BowString => "BowString",
                    Held.Blackjack => "BlackjackGrip",
                    Held.LockpicksWorn => "LockpicksWorn",
                    Held.Lockpicks => "LockpicksCarry",
                    Held.Lockpick => "LockpickHold",
                    _ => "PackHold"
                };
            }

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(subject));
            return asset != null ? asset.name : "Hand Pose";
        }

        /// <summary>
        /// What a new profile is given to, for the window's messages.
        /// </summary>
        private string OwnerDescription()
        {
            if (!TryGetTool(out Held tool)) {
                return "the prefab";
            }

            string owner = tool switch {
                Held.Pack => "PlayerPack",
                Held.Blackjack => "PlayerBlackjack",
                Held.LockpicksWorn or Held.Lockpicks or Held.Lockpick => "PlayerLockpicking",
                _ => "PlayerBow"
            };

            return $"{owner} in the Player prefab";
        }

        /// <summary>
        /// Whether the subject is a held thing, and which.
        /// </summary>
        private bool TryGetTool(out Held tool)
        {
            tool = default;
            return subject != null && subject.StartsWith(ToolKeyPrefix, StringComparison.Ordinal)
                && Enum.TryParse(subject.Substring(ToolKeyPrefix.Length), out tool);
        }

        /// <summary>
        /// A held thing's name in the list.
        /// </summary>
        private static string ToolName(Held tool)
        {
            return tool switch {
                Held.Bow => "Bow (held)",
                Held.Arrow => "Arrow (in hand)",
                Held.BowString => "Bow String (drawing)",
                Held.Blackjack => "Blackjack (held)",
                Held.LockpicksWorn => "Lockpicks (on the back of the hand)",
                Held.Lockpicks => "Lockpicks (carried)",
                Held.Lockpick => "Lockpick (on the big lock)",
                _ => "Pack (held)"
            };
        }

        /// <summary>
        /// The profile field of a held thing, on its component in the Player
        /// prefab; null if the prefab hasn't that component.
        /// </summary>
        private static SerializedProperty ToolField(Held tool)
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            if (player == null) {
                return null;
            }

            Component owner = tool switch {
                Held.Pack => player.GetComponentInChildren<PlayerPack>(true),
                Held.Blackjack => player.GetComponentInChildren<PlayerBlackjack>(true),
                Held.LockpicksWorn or Held.Lockpicks or Held.Lockpick => player.GetComponentInChildren<PlayerLockpicking>(true),
                _ => player.GetComponentInChildren<PlayerBow>(true)
            };

            if (owner == null) {
                return null;
            }

            string field = tool switch {
                Held.Bow => "bowProfile",
                Held.Arrow => "arrowProfile",
                Held.BowString => "stringProfile",
                Held.Blackjack => "blackjackProfile",
                Held.LockpicksWorn => "wornProfile",
                Held.Lockpicks => "carryProfile",
                Held.Lockpick => "pickProfile",
                _ => "packProfile"
            };

            return new SerializedObject(owner).FindProperty(field);
        }

        /// <summary>
        /// A rotation's Euler angles, each between -180 and 180 degrees.
        /// </summary>
        private static Vector3 SignedEuler(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            return new Vector3(Mathf.DeltaAngle(0f, euler.x), Mathf.DeltaAngle(0f, euler.y), Mathf.DeltaAngle(0f, euler.z));
        }

        /// <summary>
        /// A vector rounded to 1/steps (10000: a tenth of a millimetre).
        /// </summary>
        private static Vector3 Round(Vector3 value, float steps)
        {
            return new Vector3(Mathf.Round(value.x * steps) / steps, Mathf.Round(value.y * steps) / steps, Mathf.Round(value.z * steps) / steps);
        }
    }
}
