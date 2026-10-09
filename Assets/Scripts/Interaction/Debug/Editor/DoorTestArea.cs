using Core;
using Player;
using UnityEditor;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Build Door Test Area) that puts a
    /// wall with three doors in it in front of the main camera, one of
    /// each kind of lock: no lock, a simple lock (starts locked) and a
    /// keyed lock (starts locked). In Play Mode, right-click a Door
    /// component and pick Test Unlock / Test Lock to change one.
    ///
    /// Each door is built the way Door and DoorHandle expect: an unscaled
    /// root on the hinge with a kinematic Rigidbody, a solid leaf, and a
    /// handle whose trigger grab volume sticks out of both faces, with a
    /// lever on each side that has no collider of its own.
    ///
    /// The simple-lock door also gets a keyhole to look through (a
    /// DoorKeyhole below the handle, and the door leaf material on its
    /// leaf).
    ///
    /// Also adds PlayerHandDoors to the player's Hands object and
    /// PlayerKeyholes to the Player root if they aren't there, and makes the door handle's hand snap profile
    /// (Assets/Data/DoorHandle.asset) as a copy of the bottle's the first
    /// time - a stand-in to be tuned in the headset.
    ///
    /// Built from code so it can be rebuilt identically, and undone with
    /// Ctrl+Z. In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class DoorTestArea
    {
        private const string RootName = "Door Test Area";

        private const string HandleProfilePath = "Assets/Data/DoorHandle.asset";
        private const string BottleProfilePath = "Assets/Data/BottleHold.asset";

        // The wall, in metres.
        private const float WallHeight = 2.5f;
        private const float WallThickness = 0.15f;
        private const float WallHalfLength = 3.5f;

        // Each doorway, and how far apart their middles are.
        private const float DoorwayWidth = 0.9f;
        private const float DoorwayHeight = 2.1f;
        private const float DoorwaySpacing = 2.2f;

        // The leaf: a little smaller than the doorway all round, so it
        // doesn't rub the frame or the floor.
        private const float LeafThickness = 0.05f;
        private const float LeafGap = 0.01f;

        // The handle: how high it is, how far in from the leaf's free
        // edge, and how far the lever's axis is from the middle of the
        // leaf (DoorHandle's standOff and gripAlong defaults match).
        private const float HandleHeight = 1f;
        private const float HandleInset = 0.08f;
        private const float LeverStandOff = 0.075f;
        private const float LeverLength = 0.14f;
        private const float LeverThickness = 0.022f;

        // The keyhole (simple lock only): how far below the handle its
        // middle is - far enough that, fully open, it's clear of the lever.
        private const float KeyholeDrop = 0.18f;

        private const string DoorLeafMaterialPath = "Assets/Art/Materials/DoorLeaf.mat";
        private const string DoorLeafShaderName = "TeaLeaf/DoorLeaf";

        [MenuItem("TeaLeaf/Build Door Test Area")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer("Environment");
            int interactable = LayerMask.NameToLayer("Interactable");

            if (environment < 0 || interactable < 0) {
                Debug.LogError("DoorTestArea: the 'Environment' and 'Interactable' layers must exist.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog("Door Test Area", "A door test area already exists in the scene. Replace it?", "Replace", "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            // On the floor, 2.5m ahead of the main camera, facing the same
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

                position = camera.transform.position + rotation * new Vector3(0f, 0f, 2.5f);
                position.y = 0f;
            }

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(root, "Build Door Test Area");
            Transform parent = root.transform;

            BuildWall(parent, environment);

            HandSnapProfile profile = EnsureHandleProfile();
            SoundCue cue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.ImpactCuePath);

            BuildDoor("Door (No Lock)", parent, -DoorwaySpacing, DoorLock.None, false, profile, cue, interactable);
            BuildDoor("Door (Simple Lock)", parent, 0f, DoorLock.Simple, true, profile, cue, interactable);
            BuildDoor("Door (Keyed Lock)", parent, DoorwaySpacing, DoorLock.Keyed, true, profile, cue, interactable);

            EnsurePlayerHandDoors();
            EnsurePlayerKeyholes();
            Selection.activeGameObject = root;
        }

        /// <summary>
        /// The wall: solid pieces between and beside the three doorways,
        /// and a lintel over each. Never moves, so it's marked Static.
        /// </summary>
        private static void BuildWall(Transform parent, int layer)
        {
            float halfDoorway = DoorwayWidth * 0.5f;
            float previousEnd = -WallHalfLength;

            for (int i = -1; i <= 1; i++) {
                float centre = i * DoorwaySpacing;

                WallPiece($"Wall {i + 2}", parent, previousEnd, centre - halfDoorway, 0f, WallHeight, layer);
                WallPiece($"Lintel {i + 2}", parent, centre - halfDoorway, centre + halfDoorway, DoorwayHeight, WallHeight, layer);
                previousEnd = centre + halfDoorway;
            }

            WallPiece("Wall 4", parent, previousEnd, WallHalfLength, 0f, WallHeight, layer);
        }

        /// <summary>
        /// One piece of the wall, from fromX to toX along the wall and
        /// from fromY up to toY.
        /// </summary>
        private static void WallPiece(string name, Transform parent, float fromX, float toX, float fromY, float toY, int layer)
        {
            Vector3 centre = new((fromX + toX) * 0.5f, (fromY + toY) * 0.5f, 0f);
            Vector3 size = new(toX - fromX, toY - fromY, WallThickness);
            TestGeometry.Box(name, parent, centre, size, layer).isStatic = true;
        }

        /// <summary>
        /// One door in the doorway centred at doorwayX: the root on the
        /// hinge (the doorway's -X edge, on the floor), its leaf, and its
        /// handle near the other edge.
        /// </summary>
        private static void BuildDoor(string name, Transform parent, float doorwayX, DoorLock lockType, bool startsLocked, HandSnapProfile profile, SoundCue cue, int layer)
        {
            float leafWidth = DoorwayWidth - LeafGap * 2f;
            float leafHeight = DoorwayHeight - LeafGap * 2f;

            GameObject door = new(name) { layer = layer };
            door.transform.SetParent(parent, false);
            door.transform.localPosition = new Vector3(doorwayX - DoorwayWidth * 0.5f + LeafGap, LeafGap, 0f);

            // Kinematic: the Door turns it from code. See Door's comment.
            Rigidbody body = door.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            GameObject leaf = TestGeometry.Box("Leaf", door.transform, new Vector3(leafWidth * 0.5f, leafHeight * 0.5f, 0f), new Vector3(leafWidth, leafHeight, LeafThickness), layer);

            // The handle before the Door component, so the Door's Reset()
            // can't mistake the handle's box for the leaf.
            DoorHandle handle = BuildHandle(door.transform, new Vector3(leafWidth - HandleInset, HandleHeight, 0f), profile, layer);

            SerializedObject settings = new(door.AddComponent<Door>());
            settings.FindProperty("lockType").enumValueIndex = (int)lockType;
            settings.FindProperty("startsLocked").boolValue = startsLocked;
            settings.FindProperty("leaf").objectReferenceValue = leaf.GetComponent<BoxCollider>();
            settings.FindProperty("blockingLayers").intValue = LayerMask.GetMask("Player");

            // One placeholder sound for the latch and the locked rattle;
            // the hinges are silent until there's a creak to give them.
            settings.FindProperty("latchCue").objectReferenceValue = cue;
            settings.FindProperty("lockedCue").objectReferenceValue = cue;
            settings.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject handleSettings = new(handle);
            handleSettings.FindProperty("door").objectReferenceValue = door.GetComponent<Door>();
            handleSettings.ApplyModifiedPropertiesWithoutUndo();

            // A lock that can be picked has a keyhole to look through.
            if (lockType == DoorLock.Simple) {
                BuildKeyhole(door.transform, leaf, new Vector3(leafWidth - HandleInset, HandleHeight - KeyholeDrop, 0f), layer);
            }
        }

        /// <summary>
        /// The keyhole: an empty object in the middle of the leaf's
        /// thickness, below the handle, with the door's axes (X across the
        /// face, Y up). The leaf is given the door leaf material, whose
        /// shader is what cuts the opening.
        /// </summary>
        private static void BuildKeyhole(Transform door, GameObject leaf, Vector3 localPosition, int layer)
        {
            MeshRenderer leafRenderer = leaf.GetComponent<MeshRenderer>();
            Material material = GetDoorLeafMaterial();

            if (material != null) {
                leafRenderer.sharedMaterial = material;
            }

            GameObject keyhole = new("Keyhole") { layer = layer };
            keyhole.transform.SetParent(door, false);
            keyhole.transform.localPosition = localPosition;

            SerializedObject settings = new(keyhole.AddComponent<DoorKeyhole>());
            settings.FindProperty("door").objectReferenceValue = door.GetComponent<Door>();
            settings.FindProperty("leafRenderer").objectReferenceValue = leafRenderer;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The door leaf material asset (the TeaLeaf/DoorLeaf shader),
        /// created the first time it's needed in the test geometry's
        /// brown. Null, with a warning, if the shader can't be found.
        /// </summary>
        private static Material GetDoorLeafMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(DoorLeafMaterialPath);

            if (material != null) {
                return material;
            }

            Shader shader = Shader.Find(DoorLeafShaderName);

            if (shader == null) {
                Debug.LogWarning($"DoorTestArea: shader '{DoorLeafShaderName}' not found, so the simple-lock door has no keyhole opening.");
                return null;
            }

            material = new Material(shader) { name = "DoorLeaf" };
            material.SetColor("_BaseColor", TestGeometry.GetMaterial().GetColor("_BaseColor"));
            AssetDatabase.CreateAsset(material, DoorLeafMaterialPath);
            return material;
        }

        /// <summary>
        /// The handle: an unscaled object on the spindle with the trigger
        /// grab volume, and under it a Spindle object carrying the lever
        /// on each face (a stem out from the door and a bar along it,
        /// pointing back towards the hinge). The levers are only to look
        /// at: no colliders, no shadows.
        /// </summary>
        private static DoorHandle BuildHandle(Transform door, Vector3 localPosition, HandSnapProfile profile, int layer)
        {
            GameObject handle = new("Handle") { layer = layer };
            handle.transform.SetParent(door, false);
            handle.transform.localPosition = localPosition;

            // Trigger first, so the DoorHandle's setup check never sees a
            // solid box. Right through the door and well out of each face,
            // shifted towards the hinge to cover the lever.
            BoxCollider box = handle.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(-LeverLength * 0.4f, 0f, 0f);
            box.size = new Vector3(LeverLength + 0.1f, 0.16f, (LeverStandOff + 0.08f) * 2f);

            GameObject spindle = new("Spindle") { layer = layer };
            spindle.transform.SetParent(handle.transform, false);

            float stemLength = LeverStandOff - LeafThickness * 0.5f;

            for (int side = -1; side <= 1; side += 2) {
                string sideName = side > 0 ? "Front" : "Back";
                Vector3 stemCentre = new(0f, 0f, side * (LeafThickness * 0.5f + stemLength * 0.5f));
                Vector3 barCentre = new(-LeverLength * 0.5f, 0f, side * LeverStandOff);

                LeverPart($"{sideName} Stem", spindle.transform, stemCentre, new Vector3(LeverThickness, LeverThickness, stemLength), layer);
                LeverPart($"{sideName} Lever", spindle.transform, barCentre, new Vector3(LeverLength, LeverThickness, LeverThickness), layer);
            }

            DoorHandle component = handle.AddComponent<DoorHandle>();
            SerializedObject settings = new(component);
            settings.FindProperty("lever").objectReferenceValue = spindle.transform;
            settings.FindProperty("snapProfile").objectReferenceValue = profile;
            settings.FindProperty("standOff").floatValue = LeverStandOff;
            settings.FindProperty("gripAlong").floatValue = -LeverLength * 0.5f;
            settings.FindProperty("gripRadius").floatValue = LeverThickness * 0.5f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        /// <summary>
        /// One visible piece of a lever: a box with its collider removed
        /// and its shadows off.
        /// </summary>
        private static void LeverPart(string name, Transform parent, Vector3 localPosition, Vector3 size, int layer)
        {
            GameObject part = TestGeometry.Box(name, parent, localPosition, size, layer);
            Object.DestroyImmediate(part.GetComponent<BoxCollider>());

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// The door handle's hand snap profile, made the first time as a
        /// copy of the bottle's (the nearest existing grip: a hand closed
        /// round a bar), so it can be tuned without changing how a bottle
        /// is held. An empty profile if the bottle's is missing.
        /// </summary>
        private static HandSnapProfile EnsureHandleProfile()
        {
            HandSnapProfile profile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(HandleProfilePath);

            if (profile != null) {
                return profile;
            }

            HandSnapProfile bottle = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(BottleProfilePath);

            if (bottle != null) {
                profile = Object.Instantiate(bottle);
            } else {
                Debug.LogWarning($"DoorTestArea: couldn't load {BottleProfilePath} to copy - the door handle profile starts empty.");
                profile = ScriptableObject.CreateInstance<HandSnapProfile>();
            }

            profile.name = "DoorHandle";
            AssetDatabase.CreateAsset(profile, HandleProfilePath);
            return profile;
        }

        /// <summary>
        /// Makes sure the player can open doors: adds PlayerHandDoors next
        /// to PlayerHandVisuals (on the Hands object) if it's missing. Its
        /// Reset() fills in its references.
        /// </summary>
        private static void EnsurePlayerHandDoors()
        {
            PlayerHandVisuals hands = Object.FindFirstObjectByType<PlayerHandVisuals>();

            if (hands == null) {
                Debug.LogWarning("DoorTestArea: no PlayerHandVisuals in the scene, so PlayerHandDoors wasn't added - add it to the Hands object by hand.");
                return;
            }

            if (hands.GetComponent<PlayerHandDoors>() == null) {
                Undo.AddComponent<PlayerHandDoors>(hands.gameObject);
            }
        }

        /// <summary>
        /// Makes sure the player can look through keyholes: adds
        /// PlayerKeyholes next to PlayerController (on the Player root) if
        /// it's missing. Its Reset() fills in its reference, and
        /// PlayerController finds it by itself in Awake().
        /// </summary>
        private static void EnsurePlayerKeyholes()
        {
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();

            if (player == null) {
                Debug.LogWarning("DoorTestArea: no PlayerController in the scene, so PlayerKeyholes wasn't added - add it to the Player root by hand.");
                return;
            }

            if (player.GetComponent<PlayerKeyholes>() == null) {
                Undo.AddComponent<PlayerKeyholes>(player.gameObject);
            }
        }
    }
}
