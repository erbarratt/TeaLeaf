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
    /// leaf), which is also the lock the lockpicks go into (a
    /// PickableLock), with a round lock plate on each face.
    ///
    /// For lockpicking it also adds PlayerLockpicking to the player's
    /// Hands object and makes the scene's Big Lock object (BigLock and
    /// its debug view), if they aren't there, and the pick's hand snap
    /// profile (Assets/Data/LockpickHold.asset) as a copy of the rope's.
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

        // The lock plate round the keyhole (simple lock only): a round
        // disc on each face - really one short cylinder through the door -
        // and how far it stands out of each face.
        private const float LockPlateRadius = 0.035f;
        private const float LockPlateProud = 0.006f;
        private const string LockPlateMaterialPath = "Assets/Art/Materials/LockPlate.mat";

        // The sliding bolt (on the door with no lock): how high it is,
        // how far in from the leaf's free edge its knob starts, and how
        // far it slides.
        private const float BoltHeight = 1.35f;
        private const float BoltInset = 0.09f;
        private const float BoltTravel = 0.05f;

        // The key id the keyed door asks for and the red test key has.
        private const string TestKeyId = "red";

        // The sound room each side of the wall.
        private const float SoundRoomHeight = 3f;
        private const float SoundRoomDepth = 6f;

        // The hand snap profile for a hand on one of the big lock's picks,
        // made as a copy of the rope's (the nearest grip: a hand closed
        // round something thin).
        private const string PickProfilePath = "Assets/Data/LockpickHold.asset";
        private const string RopeProfilePath = "Assets/Data/RopeGrip.asset";

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

            // Makes any placeholder sounds that don't exist yet (the creak
            // is newer than the others).
            PlaceholderSounds.Create();

            HandSnapProfile profile = EnsureHandleProfile();
            SoundCue cue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.ImpactCuePath);
            SoundCue creakCue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.CreakCuePath);

            Door plainDoor = BuildDoor("Door (No Lock)", parent, -DoorwaySpacing, DoorLock.None, false, profile, cue, creakCue, interactable);
            BuildDoor("Door (Simple Lock)", parent, 0f, DoorLock.Simple, true, profile, cue, creakCue, interactable);
            BuildDoor("Door (Keyed Lock)", parent, DoorwaySpacing, DoorLock.Keyed, true, profile, cue, creakCue, interactable);

            // A sliding bolt on the door with no lock, on the side the
            // area is built facing (the door's back, -Z).
            BuildBolt(plainDoor, false, profile, cue, interactable);

            BuildSound(parent, cue);
            BuildKeys(parent, environment, interactable);
            EnsurePlayerKeys(profile);

            EnsurePlayerHandDoors();
            EnsurePlayerKeyholes();
            EnsurePlayerBodyPushing();
            EnsureLockpicking(cue);
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
        private static Door BuildDoor(string name, Transform parent, float doorwayX, DoorLock lockType, bool startsLocked, HandSnapProfile profile, SoundCue cue, SoundCue creakCue, int layer)
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

            // One placeholder sound for the latch and the locked rattle,
            // and a placeholder creak for the hinges.
            settings.FindProperty("latchCue").objectReferenceValue = cue;
            settings.FindProperty("lockedCue").objectReferenceValue = cue;
            settings.FindProperty("creakCue").objectReferenceValue = creakCue;

            // The doorway's sound portal: in the wall, not on the door, so
            // it stays put while the door swings. The door shuts and opens
            // it, so a shut door muffles what's behind it.
            settings.FindProperty("soundPortal").objectReferenceValue = BuildPortal(name, parent, doorwayX);
            settings.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject handleSettings = new(handle);
            handleSettings.FindProperty("door").objectReferenceValue = door.GetComponent<Door>();
            handleSettings.ApplyModifiedPropertiesWithoutUndo();

            // A lock that can be picked has a keyhole to look through.
            if (lockType == DoorLock.Simple) {
                BuildKeyhole(door.transform, leaf, new Vector3(leafWidth - HandleInset, HandleHeight - KeyholeDrop, 0f), layer);
            }

            // A lock that takes a key has a lock plate in the same place,
            // and its key's id.
            if (lockType == DoorLock.Keyed) {
                BuildKeyLock(door.transform, new Vector3(leafWidth - HandleInset, HandleHeight - KeyholeDrop, 0f), cue, layer);
            }

            return door.GetComponent<Door>();
        }

        /// <summary>
        /// The keyed door's lock: an empty object in the middle of the
        /// leaf's thickness, below the handle, with the door's axes, a
        /// KeyLock on it and a lock plate through the door. The door is
        /// given the key id the red test key has.
        /// </summary>
        private static void BuildKeyLock(Transform door, Vector3 localPosition, SoundCue cue, int layer)
        {
            GameObject keyLock = new("Key Lock") { layer = layer };
            keyLock.transform.SetParent(door, false);
            keyLock.transform.localPosition = localPosition;

            BuildLockPlate(keyLock.transform, layer);

            Door doorComponent = door.GetComponent<Door>();
            SerializedObject doorSettings = new(doorComponent);
            doorSettings.FindProperty("keyId").stringValue = TestKeyId;
            doorSettings.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject settings = new(keyLock.AddComponent<KeyLock>());
            settings.FindProperty("door").objectReferenceValue = doorComponent;
            settings.FindProperty("faceOffset").floatValue = LeafThickness * 0.5f + LockPlateProud;
            settings.FindProperty("unlockCue").objectReferenceValue = cue;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The keys: a small stand on the camera's side of the wall,
        /// between the simple-lock and keyed doors, with two key props on
        /// it - a red one that opens the keyed door, and a blue one that
        /// opens nothing here, to show two colours on the keyring. Each is
        /// an ordinary prop (GrabbableTestProps.MakeGrabbable()) with a
        /// Key.
        /// </summary>
        private static void BuildKeys(Transform parent, int environment, int interactable)
        {
            const float standHeight = 0.9f;
            Vector3 standPosition = new(DoorwaySpacing * 0.5f, 0f, -0.6f);

            TestGeometry.Box("Key Stand", parent, standPosition + Vector3.up * (standHeight * 0.5f), new Vector3(0.4f, standHeight, 0.3f), environment).isStatic = true;

            BuildKey("Key (red - keyed door)", parent, standPosition + new Vector3(-0.08f, standHeight + 0.01f, 0f), TestKeyId, new Color(0.85f, 0.15f, 0.12f, 1f), interactable);
            BuildKey("Key (blue - no door)", parent, standPosition + new Vector3(0.08f, standHeight + 0.01f, 0f), "blue", new Color(0.15f, 0.35f, 0.9f, 1f), interactable);
        }

        /// <summary>
        /// One key prop: a small flat bar, with a Key saying which locks
        /// it opens and its colour (the Key tints the prop itself when
        /// the game starts). Key's settings are private serialized
        /// fields, so they're set the way the Inspector would set them.
        /// </summary>
        private static void BuildKey(string name, Transform parent, Vector3 localPosition, string keyId, Color color, int layer)
        {
            GameObject key = TestGeometry.Box(name, parent, localPosition, new Vector3(0.03f, 0.015f, 0.09f), layer);
            GrabbableTestProps.MakeGrabbable(key, 0.1f, 0.05f);

            SerializedObject settings = new(key.AddComponent<Inventory.Key>());
            settings.FindProperty("keyId").stringValue = keyId;
            settings.FindProperty("color").colorValue = color;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Makes sure the player can use keys: the pack and inventory
        /// (the loot test props' own set-up, since the keyring lives in
        /// the pack), then PlayerKeys next to PlayerPack on the Hands
        /// object if it's missing. Its Reset() fills in its references;
        /// its hand pose on the key is the door handle's, as a stand-in.
        /// </summary>
        private static void EnsurePlayerKeys(HandSnapProfile profile)
        {
            Inventory.LootTestProps.EnsurePlayerComponents();

            PlayerPack playerPack = Object.FindFirstObjectByType<PlayerPack>();

            if (playerPack == null) {
                Debug.LogWarning("DoorTestArea: no PlayerPack in the scene, so PlayerKeys wasn't added - add it to the Hands object by hand.");
                return;
            }

            if (playerPack.GetComponent<PlayerKeys>() == null) {
                PlayerKeys keys = Undo.AddComponent<PlayerKeys>(playerPack.gameObject);

                SerializedObject settings = new(keys);
                settings.FindProperty("snapProfile").objectReferenceValue = profile;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// A sound portal filling the doorway centred at doorwayX, in the
        /// middle of the wall's thickness. It starts closed; the door
        /// opens it when it's unlatched.
        /// </summary>
        private static SoundPortal BuildPortal(string doorName, Transform parent, float doorwayX)
        {
            GameObject portal = new($"Sound Portal {doorName}");
            portal.transform.SetParent(parent, false);
            portal.transform.localPosition = new Vector3(doorwayX, DoorwayHeight * 0.5f, 0f);

            SoundPortal component = portal.AddComponent<SoundPortal>();
            SerializedObject settings = new(component);
            settings.FindProperty("size").vector2Value = new Vector2(DoorwayWidth, DoorwayHeight);
            settings.FindProperty("startsOpen").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        /// <summary>
        /// The sound side of the test: a sound room each side of the wall
        /// (they meet in the middle of it), so sound only gets from one to
        /// the other through the doorways' portals, and a cube beyond the
        /// wall that knocks every few seconds - muffled while the doors
        /// are shut, clear through one that's open. Disable the cube to
        /// test in silence.
        /// </summary>
        private static void BuildSound(Transform parent, SoundCue cue)
        {
            Vector3 roomSize = new(WallHalfLength * 2f, SoundRoomHeight, SoundRoomDepth);
            float roomZ = SoundRoomDepth * 0.5f;

            SoundTestArea.AddRoom("Sound Room Near", parent, new Vector3(0f, SoundRoomHeight * 0.5f, -roomZ), roomSize);
            SoundTestArea.AddRoom("Sound Room Far", parent, new Vector3(0f, SoundRoomHeight * 0.5f, roomZ), roomSize);

            if (cue != null) {
                SoundTestArea.AddEmitter("Sound Emitter Behind Doors (impact)", parent, new Vector3(0f, 1f, 2.5f), cue, 3f);
            }

            SoundTestArea.EnsureSceneObjects();
        }

        /// <summary>
        /// A sliding bolt on one face of door, above the handle: a plate
        /// on the leaf, a bar with a knob that slides towards the door's
        /// free edge (into the wall beside it), and a trigger grab volume
        /// on that side of the door only. The parts are only to look at:
        /// no colliders, no shadows. Its hand pose is the door handle's,
        /// as a stand-in.
        /// </summary>
        private static void BuildBolt(Door door, bool onFront, HandSnapProfile profile, SoundCue cue, int layer)
        {
            float leafWidth = DoorwayWidth - LeafGap * 2f;
            float side = onFront ? 1f : -1f;
            float faceZ = side * LeafThickness * 0.5f;

            // On the face, where the knob is with the bolt drawn back.
            GameObject bolt = new("Bolt") { layer = layer };
            bolt.transform.SetParent(door.transform, false);
            bolt.transform.localPosition = new Vector3(leafWidth - BoltInset, BoltHeight, faceZ);

            // Trigger first, so the DoorBolt's setup check never sees a
            // solid box. Out from the face on the bolt's side only.
            BoxCollider box = bolt.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(BoltTravel * 0.5f, 0f, side * 0.06f);
            box.size = new Vector3(0.2f, 0.12f, 0.12f);

            LeverPart("Plate", bolt.transform, new Vector3(0f, 0f, side * 0.004f), new Vector3(0.14f, 0.05f, 0.008f), layer);

            // The part that slides: the bar along the door and a knob
            // standing out of it.
            GameObject bar = new("Bar") { layer = layer };
            bar.transform.SetParent(bolt.transform, false);
            LeverPart("Rod", bar.transform, new Vector3(0f, 0f, side * 0.016f), new Vector3(0.12f, 0.016f, 0.016f), layer);
            LeverPart("Knob", bar.transform, new Vector3(0f, 0f, side * 0.034f), new Vector3(0.016f, 0.016f, 0.03f), layer);

            SerializedObject settings = new(bolt.AddComponent<DoorBolt>());
            settings.FindProperty("door").objectReferenceValue = door;
            settings.FindProperty("onFront").boolValue = onFront;
            settings.FindProperty("bar").objectReferenceValue = bar.transform;
            settings.FindProperty("travel").floatValue = BoltTravel;
            settings.FindProperty("snapProfile").objectReferenceValue = profile;
            settings.FindProperty("slideCue").objectReferenceValue = cue;
            settings.ApplyModifiedPropertiesWithoutUndo();
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

            MeshRenderer plateRenderer = BuildLockPlate(keyhole.transform, layer);

            SerializedObject settings = new(keyhole.AddComponent<DoorKeyhole>());
            settings.FindProperty("door").objectReferenceValue = door.GetComponent<Door>();
            settings.FindProperty("leafRenderer").objectReferenceValue = leafRenderer;

            // The opening goes through the lock plate too, and its walls
            // run the plate's whole depth, not just the leaf's.
            if (plateRenderer != null) {
                SerializedProperty plates = settings.FindProperty("plateRenderers");
                plates.arraySize = 1;
                plates.GetArrayElementAtIndex(0).objectReferenceValue = plateRenderer;
                settings.FindProperty("thickness").floatValue = LeafThickness + LockPlateProud * 2f;
            }

            settings.ApplyModifiedPropertiesWithoutUndo();

            // The same object is the lock the picks go into.
            SerializedObject lockSettings = new(keyhole.AddComponent<PickableLock>());
            lockSettings.FindProperty("door").objectReferenceValue = door.GetComponent<Door>();
            lockSettings.FindProperty("faceOffset").floatValue = LeafThickness * 0.5f + LockPlateProud;
            lockSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The lock plate: a short cylinder through the door at the
        /// keyhole, standing a little out of each face, so it shows as a
        /// round plate on both. Only to look at: no collider, no shadows.
        /// It uses the door leaf shader (in its own dark material), so the
        /// keyhole is cut through it as well. Null, with a warning, if
        /// that shader can't be found.
        /// </summary>
        private static MeshRenderer BuildLockPlate(Transform keyhole, int layer)
        {
            Material material = GetLockPlateMaterial();

            if (material == null) {
                return null;
            }

            GameObject plate = TestGeometry.Primitive(PrimitiveType.Cylinder, "Lock Plate", keyhole, layer);
            Object.DestroyImmediate(plate.GetComponent<Collider>());

            // A primitive cylinder is 1m across and 2m long, along its Y:
            // turned to lie through the door, and scaled to size.
            plate.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            plate.transform.localScale = new Vector3(LockPlateRadius * 2f, LeafThickness * 0.5f + LockPlateProud, LockPlateRadius * 2f);

            MeshRenderer renderer = plate.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /// <summary>
        /// The lock plate material asset (the TeaLeaf/DoorLeaf shader, in
        /// dark iron), created the first time it's needed.
        /// </summary>
        private static Material GetLockPlateMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(LockPlateMaterialPath);

            if (material != null) {
                return material;
            }

            Shader shader = Shader.Find(DoorLeafShaderName);

            if (shader == null) {
                Debug.LogWarning($"DoorTestArea: shader '{DoorLeafShaderName}' not found, so the simple-lock door has no lock plate.");
                return null;
            }

            material = new Material(shader) { name = "LockPlate" };
            material.SetColor("_BaseColor", new Color(0.16f, 0.15f, 0.14f, 1f));
            AssetDatabase.CreateAsset(material, LockPlateMaterialPath);
            return material;
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

        /// <summary>
        /// Makes sure the player's body pushes open doors: adds
        /// PlayerBodyPushing next to PlayerController (on the Player root,
        /// where the CharacterController is) if it's missing.
        /// </summary>
        private static void EnsurePlayerBodyPushing()
        {
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();

            if (player == null) {
                Debug.LogWarning("DoorTestArea: no PlayerController in the scene, so PlayerBodyPushing wasn't added - add it to the Player root by hand.");
                return;
            }

            if (player.GetComponent<PlayerBodyPushing>() == null) {
                Undo.AddComponent<PlayerBodyPushing>(player.gameObject);
            }
        }

        /// <summary>
        /// Makes sure the player can pick locks: adds PlayerLockpicking
        /// next to PlayerHandVisuals (on the Hands object) if it's missing
        /// - its Reset() fills in its references - and makes the scene's
        /// one Big Lock object (BigLock, with its debug view) if there
        /// isn't one. The big lock sits at the scene's root, not under the
        /// test area, so rebuilding the area keeps any tuning done to it.
        /// </summary>
        private static void EnsureLockpicking(SoundCue cue)
        {
            PlayerHandVisuals hands = Object.FindFirstObjectByType<PlayerHandVisuals>();

            if (hands == null) {
                Debug.LogWarning("DoorTestArea: no PlayerHandVisuals in the scene, so PlayerLockpicking wasn't added - add it to the Hands object by hand.");
                return;
            }

            PlayerLockpicking lockpicking = hands.GetComponent<PlayerLockpicking>();

            if (lockpicking == null) {
                lockpicking = Undo.AddComponent<PlayerLockpicking>(hands.gameObject);
            }

            BigLock bigLock = Object.FindFirstObjectByType<BigLock>();

            if (bigLock == null) {
                GameObject lockObject = new("Big Lock");
                Undo.RegisterCreatedObjectUndo(lockObject, "Build Door Test Area");
                bigLock = lockObject.AddComponent<BigLock>();
                lockObject.AddComponent<BigLockDebug>();

                // One placeholder sound for the picking and the unlock.
                SerializedObject lockSettings = new(bigLock);
                lockSettings.FindProperty("snapProfile").objectReferenceValue = EnsurePickProfile();
                lockSettings.FindProperty("pickCue").objectReferenceValue = cue;
                lockSettings.FindProperty("unlockCue").objectReferenceValue = cue;
                lockSettings.ApplyModifiedPropertiesWithoutUndo();
            }

            SerializedObject settings = new(lockpicking);
            settings.FindProperty("bigLock").objectReferenceValue = bigLock;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The hand snap profile for a hand on a pick, made the first time
        /// as a copy of the rope's, so it can be tuned without changing
        /// how a rope is held. An empty profile if the rope's is missing.
        /// </summary>
        private static HandSnapProfile EnsurePickProfile()
        {
            HandSnapProfile profile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(PickProfilePath);

            if (profile != null) {
                return profile;
            }

            HandSnapProfile rope = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(RopeProfilePath);

            if (rope != null) {
                profile = Object.Instantiate(rope);
            } else {
                Debug.LogWarning($"DoorTestArea: couldn't load {RopeProfilePath} to copy - the lockpick profile starts empty.");
                profile = ScriptableObject.CreateInstance<HandSnapProfile>();
            }

            profile.name = "LockpickHold";
            AssetDatabase.CreateAsset(profile, PickProfilePath);
            return profile;
        }
    }
}
