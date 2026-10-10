using Core;
using Player;
using UnityEditor;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Build Chest Test Area) that puts
    /// three chests in front of the main camera, one of each kind of
    /// lock: no lock, a simple lock (starts locked; pick it) and a keyed
    /// lock (starts locked; its green key is on a stand beside it).
    ///
    /// Each chest is built the way Chest and ChestLid expect: an unscaled
    /// root with its front (+Z) towards the camera, a solid body on a
    /// stand, a Lid object on the hinge line along the top back edge with
    /// the lid's board under it, and a trigger grab volume on the middle
    /// of the lid's front edge. The chests are solid blocks: there is
    /// nothing inside them yet.
    ///
    /// Also makes sure the player can open them: PlayerHandDoors, the
    /// lockpicks and the keys, through the Door Test Area's own set-up.
    ///
    /// Built from code so it can be rebuilt identically, and undone with
    /// Ctrl+Z. In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class ChestTestArea
    {
        private const string RootName = "Chest Test Area";

        // How far apart the chests' middles are, in metres.
        private const float Spacing = 1.2f;

        // The stand each chest is on (so the lid is at a hand's height),
        // the chest's body, and the lid's thickness.
        private const float StandHeight = 0.35f;
        private const float BodyWidth = 0.7f;
        private const float BodyHeight = 0.4f;
        private const float BodyDepth = 0.45f;
        private const float LidThickness = 0.06f;

        // The lock: how far below the top of the body's front it is, how
        // far behind the front face its object sits, and how far its
        // plate stands out.
        private const float LockDrop = 0.1f;
        private const float LockInset = 0.02f;
        private const float LockPlateProud = 0.006f;
        private const float LockPlateSize = 0.07f;

        // The key id the keyed chest asks for and the green test key has.
        private const string TestKeyId = "green";

        [MenuItem("TeaLeaf/Build Chest Test Area")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer("Environment");
            int interactable = LayerMask.NameToLayer("Interactable");

            if (environment < 0 || interactable < 0) {
                Debug.LogError("ChestTestArea: the 'Environment' and 'Interactable' layers must exist.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog("Chest Test Area", "A chest test area already exists in the scene. Replace it?", "Replace", "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            // On the floor, 1.5m ahead of the main camera, facing the same
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

                position = camera.transform.position + rotation * new Vector3(0f, 0f, 1.5f);
                position.y = 0f;
            }

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(root, "Build Chest Test Area");
            Transform parent = root.transform;

            // Makes any placeholder sounds that don't exist yet.
            PlaceholderSounds.Create();

            SoundCue cue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.ImpactCuePath);
            HandSnapProfile profile = DoorTestArea.EnsureHandleProfile();

            BuildChest("Chest (No Lock)", parent, -Spacing, DoorLock.None, profile, cue, environment, interactable);
            BuildChest("Chest (Simple Lock)", parent, 0f, DoorLock.Simple, profile, cue, environment, interactable);
            BuildChest("Chest (Keyed Lock)", parent, Spacing, DoorLock.Keyed, profile, cue, environment, interactable);

            BuildKey(parent, environment, interactable);

            DoorTestArea.EnsurePlayerHandDoors();
            DoorTestArea.EnsureLockpicking(cue);
            DoorTestArea.EnsurePlayerKeys(profile);
            Selection.activeGameObject = root;
        }

        /// <summary>
        /// One chest at x along the row: its stand, its root (turned so
        /// its front faces the camera), body, lid, grab volume and lock.
        /// </summary>
        private static void BuildChest(string name, Transform parent, float x, DoorLock lockType, HandSnapProfile profile, SoundCue cue, int environment, int interactable)
        {
            TestGeometry.Box($"{name} Stand", parent, new Vector3(x, StandHeight * 0.5f, 0f), new Vector3(BodyWidth + 0.1f, StandHeight, BodyDepth + 0.1f), environment).isStatic = true;

            // The root: on top of the stand, at the middle of the body's
            // base, turned half way round so its front (+Z) is towards
            // the camera.
            GameObject chest = new(name) { layer = interactable };
            chest.transform.SetParent(parent, false);
            chest.transform.SetLocalPositionAndRotation(new Vector3(x, StandHeight, 0f), Quaternion.Euler(0f, 180f, 0f));

            TestGeometry.Box("Body", chest.transform, new Vector3(0f, BodyHeight * 0.5f, 0f), new Vector3(BodyWidth, BodyHeight, BodyDepth), interactable);

            // The lid: an empty object on the hinge line (the top back
            // edge), with the board under it reaching forward from there.
            GameObject lid = new("Lid") { layer = interactable };
            lid.transform.SetParent(chest.transform, false);
            lid.transform.localPosition = new Vector3(0f, BodyHeight, -BodyDepth * 0.5f);

            TestGeometry.Box("Lid Board", lid.transform, new Vector3(0f, LidThickness * 0.5f, BodyDepth * 0.5f), new Vector3(BodyWidth, LidThickness, BodyDepth), interactable);

            // The grab volume on the middle of the lid's front edge.
            // Trigger first, so nothing ever sees it as a solid box.
            GameObject grip = new("Lid Grip") { layer = interactable };
            grip.transform.SetParent(lid.transform, false);
            grip.transform.localPosition = new Vector3(0f, LidThickness * 0.5f, BodyDepth);

            BoxCollider box = grip.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(BodyWidth * 0.8f, 0.16f, 0.16f);

            Chest component = chest.AddComponent<Chest>();
            SerializedObject settings = new(component);
            settings.FindProperty("lockType").enumValueIndex = (int)lockType;
            settings.FindProperty("startsLocked").boolValue = lockType != DoorLock.None;
            settings.FindProperty("keyId").stringValue = lockType == DoorLock.Keyed ? TestKeyId : string.Empty;
            settings.FindProperty("lid").objectReferenceValue = lid.transform;

            // One placeholder sound for the lid opening, dropping shut
            // and the locked rattle.
            settings.FindProperty("openCue").objectReferenceValue = cue;
            settings.FindProperty("closeCue").objectReferenceValue = cue;
            settings.FindProperty("lockedCue").objectReferenceValue = cue;
            settings.ApplyModifiedPropertiesWithoutUndo();

            // The hand's pose on the lid is the door handle's, as a
            // stand-in.
            SerializedObject gripSettings = new(grip.AddComponent<ChestLid>());
            gripSettings.FindProperty("chest").objectReferenceValue = component;
            gripSettings.FindProperty("snapProfile").objectReferenceValue = profile;
            gripSettings.ApplyModifiedPropertiesWithoutUndo();

            if (lockType != DoorLock.None) {
                BuildLock(component, lockType, cue, interactable);
            }
        }

        /// <summary>
        /// The chest's lock: an empty object just behind the front face,
        /// below the lid, with the chest's axes (X across the face, Y up,
        /// Z out of the front), a plate to show where it is, and a
        /// PickableLock or a KeyLock given the chest.
        /// </summary>
        private static void BuildLock(Chest chest, DoorLock lockType, SoundCue cue, int layer)
        {
            GameObject lockObject = new(lockType == DoorLock.Simple ? "Lock (pick)" : "Lock (key)") { layer = layer };
            lockObject.transform.SetParent(chest.transform, false);
            lockObject.transform.localPosition = new Vector3(0f, BodyHeight - LockDrop, BodyDepth * 0.5f - LockInset);

            // The plate: only to look at - no collider, no shadows.
            GameObject plate = TestGeometry.Box("Lock Plate", lockObject.transform, new Vector3(0f, 0f, LockInset), new Vector3(LockPlateSize, LockPlateSize, LockPlateProud * 2f), layer);
            Object.DestroyImmediate(plate.GetComponent<BoxCollider>());

            MeshRenderer renderer = plate.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            float faceOffset = LockInset + LockPlateProud;

            if (lockType == DoorLock.Simple) {
                SerializedObject settings = new(lockObject.AddComponent<PickableLock>());
                settings.FindProperty("chest").objectReferenceValue = chest;
                settings.FindProperty("faceOffset").floatValue = faceOffset;
                settings.ApplyModifiedPropertiesWithoutUndo();
            } else {
                SerializedObject settings = new(lockObject.AddComponent<KeyLock>());
                settings.FindProperty("chest").objectReferenceValue = chest;
                settings.FindProperty("faceOffset").floatValue = faceOffset;
                settings.FindProperty("unlockCue").objectReferenceValue = cue;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// The keyed chest's key: a small flat bar on a stand to the
        /// right of the row - an ordinary prop
        /// (GrabbableTestProps.MakeGrabbable()) with a Key, whose
        /// settings are private serialized fields, so they're set the way
        /// the Inspector would set them.
        /// </summary>
        private static void BuildKey(Transform parent, int environment, int interactable)
        {
            const float standHeight = 0.9f;
            Vector3 standPosition = new(Spacing * 1.7f, 0f, -0.2f);

            TestGeometry.Box("Key Stand", parent, standPosition + Vector3.up * (standHeight * 0.5f), new Vector3(0.3f, standHeight, 0.3f), environment).isStatic = true;

            GameObject key = TestGeometry.Box("Key (green - keyed chest)", parent, standPosition + new Vector3(0f, standHeight + 0.01f, 0f), new Vector3(0.03f, 0.015f, 0.09f), interactable);
            GrabbableTestProps.MakeGrabbable(key, 0.1f, 0.05f);

            SerializedObject settings = new(key.AddComponent<Inventory.Key>());
            settings.FindProperty("keyId").stringValue = TestKeyId;
            settings.FindProperty("color").colorValue = new Color(0.15f, 0.7f, 0.25f, 1f);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
