using Player;
using UnityEditor;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Build Sound Test Area) that builds a
    /// greybox row of three rooms for testing how sound travels through
    /// SoundRooms and SoundPortals, with a NoiseListenerDebug cube in each
    /// place worth listening from and two SoundEmitterDebug cubes that play
    /// placeholder sounds on repeat. Built from code so it can be rebuilt
    /// identically (a rebuild keeps where the old one was moved to), and
    /// undone with Ctrl+Z. Also makes sure the scene has a Sound Player and
    /// the Debug object a SoundDebug.
    ///
    /// Seen from the front (the rooms' front wall faces -Z):
    /// Room A (left) has a doorway to outside in its front wall and a
    /// doorway through to Room B. Room B (middle) has that doorway and a
    /// closed portal in its solid wall to Room C (right) - a thin wall that
    /// lets muffled sound through; C has no other way in. No ceilings, so
    /// the inside can be seen from above in the Scene view; sound rooms are
    /// maths, so they don't need one.
    ///
    /// Uses the player test areas' TestGeometry for the walls (editor-only
    /// code, so Core's runtime scripts still don't depend on Player).
    /// </summary>
    public static class SoundTestArea
    {
        private const string RootName = "Sound Test Area";
        private const string EnvironmentLayerName = "Environment";

        private const float RoomSize = 4f;
        private const float WallHeight = 3f;
        private const float WallThickness = 0.2f;
        private const float DoorWidth = 1f;
        private const float DoorHeight = 2.1f;
        private const float EarHeight = 1.6f;
        internal const float CubeSize = 0.3f;

        // Where the rooms are built: to the left of the spawn. Move the root
        // if something is already there.
        private static readonly Vector3 _rootPosition = new(-14f, 0f, 4f);

        /// <summary>
        /// Builds the test area, replacing an existing one (after asking).
        /// </summary>
        [MenuItem("TeaLeaf/Build Sound Test Area")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer(EnvironmentLayerName);

            if (environment < 0) {
                Debug.LogError($"SoundTestArea: the '{EnvironmentLayerName}' layer must exist.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);
            Vector3 rootPosition = _rootPosition;
            Quaternion rootRotation = Quaternion.identity;

            if (existing != null) {
                if (!EditorUtility.DisplayDialog(
                        "Sound Test Area",
                        "A test area already exists in the scene. Replace it?",
                        "Replace",
                        "Cancel")) {
                    return;
                }

                // Rebuild where the old one stood, in case it was moved.
                rootPosition = existing.transform.position;
                rootRotation = existing.transform.rotation;
                Undo.DestroyObjectImmediate(existing);
            }

            // The placeholder sounds the emitters play (made if missing).
            PlaceholderSounds.Create();
            SoundCue footstepCue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.FootstepCuePath);
            SoundCue impactCue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.ImpactCuePath);

            EnsureSceneObjects();

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(rootPosition, rootRotation);
            Undo.RegisterCreatedObjectUndo(root, "Build Sound Test Area");
            Transform parent = root.transform;

            // The rooms sit in a row along X: A from x = -4 to 0, B from 0
            // to 4, C from 4 to 8, all from z = 0 (front) to 4 (back). Wall
            // positions below are the middle of each wall.
            float midHeight = WallHeight * 0.5f;
            float midDepth = RoomSize * 0.5f;
            float lintelHeight = WallHeight - DoorHeight;
            float lintelY = DoorHeight + lintelHeight * 0.5f;

            // Back wall, one piece behind all three rooms.
            TestGeometry.Box("Back Wall", parent, new Vector3(2f, midHeight, RoomSize), new Vector3(12f + WallThickness, WallHeight, WallThickness), environment);

            // Front wall, with Room A's doorway to outside at x = -2: a
            // piece each side of the opening and a lintel over it.
            TestGeometry.Box("Front Wall Left", parent, new Vector3(-3.3f, midHeight, 0f), new Vector3(1.6f, WallHeight, WallThickness), environment);
            TestGeometry.Box("Front Wall Right", parent, new Vector3(3.3f, midHeight, 0f), new Vector3(9.6f, WallHeight, WallThickness), environment);
            TestGeometry.Box("Front Door Lintel", parent, new Vector3(-2f, lintelY, 0f), new Vector3(DoorWidth, lintelHeight, WallThickness), environment);

            // End walls.
            TestGeometry.Box("Left Wall", parent, new Vector3(-4f, midHeight, midDepth), new Vector3(WallThickness, WallHeight, RoomSize), environment);
            TestGeometry.Box("Right Wall", parent, new Vector3(8f, midHeight, midDepth), new Vector3(WallThickness, WallHeight, RoomSize), environment);

            // Wall between A and B, with a doorway in the middle (z = 2).
            TestGeometry.Box("A-B Wall Front", parent, new Vector3(0f, midHeight, 0.75f), new Vector3(WallThickness, WallHeight, 1.5f), environment);
            TestGeometry.Box("A-B Wall Back", parent, new Vector3(0f, midHeight, 3.25f), new Vector3(WallThickness, WallHeight, 1.5f), environment);
            TestGeometry.Box("A-B Door Lintel", parent, new Vector3(0f, lintelY, midDepth), new Vector3(WallThickness, lintelHeight, DoorWidth), environment);

            // Wall between B and C: solid.
            TestGeometry.Box("B-C Wall", parent, new Vector3(4f, midHeight, midDepth), new Vector3(WallThickness, WallHeight, RoomSize), environment);

            // The sound rooms: each box meets its neighbour in the middle of
            // the wall between them.
            Vector3 roomBox = new(RoomSize, WallHeight, RoomSize);
            AddRoom("Sound Room A", parent, new Vector3(-2f, midHeight, midDepth), roomBox);
            AddRoom("Sound Room B", parent, new Vector3(2f, midHeight, midDepth), roomBox);
            AddRoom("Sound Room C", parent, new Vector3(6f, midHeight, midDepth), roomBox);

            // The portals, each filling its doorway in the middle of the
            // wall's thickness. A portal's local Z points through the
            // opening, so the one in the A-B wall is turned to face along X.
            Vector2 doorSize = new(DoorWidth, DoorHeight);
            AddPortal("Sound Portal Front Door", parent, new Vector3(-2f, DoorHeight * 0.5f, 0f), 0f, doorSize, true);
            AddPortal("Sound Portal A-B", parent, new Vector3(0f, DoorHeight * 0.5f, midDepth), 90f, doorSize, true);

            // A closed portal on the solid B-C wall: no opening, but sound
            // gets through it muffled, as through a thin wall or shut door.
            AddPortal("Sound Portal B-C (closed)", parent, new Vector3(4f, DoorHeight * 0.5f, midDepth), 90f, doorSize, false);

            // Emitters: footsteps pacing on the spot in Room B, and a knock
            // every two seconds in Room C, heard only through the closed
            // portal.
            AddEmitter("Sound Emitter Room B (footsteps)", parent, new Vector3(3f, CubeSize * 0.5f, 1f), footstepCue, 0.5f);
            AddEmitter("Sound Emitter Room C (impact)", parent, new Vector3(7f, 1f, 1f), impactCue, 2f);

            // Listeners. With the 8m test noise made in the middle of Room
            // A: B hears it through the doorway, C doesn't (the closed portal
            // adds too much distance; it does from inside Room B), the one
            // outside the front door hears it through that door, and the
            // one behind Room B doesn't - close in a straight line, but the
            // way round through the front door is too far.
            AddListener("Noise Listener Room A", parent, new Vector3(-3f, EarHeight, 3f));
            AddListener("Noise Listener Room B", parent, new Vector3(2.5f, EarHeight, 3.2f));
            AddListener("Noise Listener Room C", parent, new Vector3(6f, EarHeight, midDepth));
            AddListener("Noise Listener Outside Front", parent, new Vector3(-2f, EarHeight, -2f));
            AddListener("Noise Listener Outside Back", parent, new Vector3(2f, EarHeight, 5.2f));

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// Makes a SoundRoom of the given box size under parent.
        /// </summary>
        internal static void AddRoom(string name, Transform parent, Vector3 localPosition, Vector3 size)
        {
            GameObject room = new(name);
            room.transform.SetParent(parent, false);
            room.transform.localPosition = localPosition;

            // The size is a private serialized field, so it's set the way
            // the Inspector would set it.
            SerializedObject serialized = new(room.AddComponent<SoundRoom>());
            serialized.FindProperty("size").vector3Value = size;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The same, with the room's reverb and (optionally) its ambience
        /// set rather than left at the defaults.
        /// </summary>
        internal static void AddRoom(string name, Transform parent, Vector3 localPosition, Vector3 size, ReverbSettings reverb, SoundCue ambience = null, float ambienceVolume = 1f)
        {
            AddRoom(name, parent, localPosition, size);

            SerializedObject serialized = new(parent.Find(name).GetComponent<SoundRoom>());
            SerializedProperty settings = serialized.FindProperty("reverb");
            settings.FindPropertyRelative("level").floatValue = reverb.Level;
            settings.FindPropertyRelative("decayTime").floatValue = reverb.DecayTime;
            settings.FindPropertyRelative("damping").floatValue = reverb.Damping;
            settings.FindPropertyRelative("size").floatValue = reverb.Size;
            serialized.FindProperty("ambience").objectReferenceValue = ambience;
            serialized.FindProperty("ambienceVolume").floatValue = ambienceVolume;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Makes a SoundPortal of the given opening size under parent,
        /// turned by yaw degrees about the vertical.
        /// </summary>
        internal static void AddPortal(string name, Transform parent, Vector3 localPosition, float yaw, Vector2 size, bool startsOpen)
        {
            AddPortal(name, parent, localPosition, Quaternion.Euler(0f, yaw, 0f), size, startsOpen);
        }

        /// <summary>
        /// The same with any rotation - a portal lying flat in a floor (a
        /// stairwell) is turned 90 degrees about X, so its local Z points
        /// up and down.
        /// </summary>
        internal static void AddPortal(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector2 size, bool startsOpen)
        {
            GameObject portal = new(name);
            portal.transform.SetParent(parent, false);
            portal.transform.localPosition = localPosition;
            portal.transform.localRotation = localRotation;

            SerializedObject serialized = new(portal.AddComponent<SoundPortal>());
            serialized.FindProperty("size").vector2Value = size;
            serialized.FindProperty("startsOpen").boolValue = startsOpen;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Makes a SoundEmitterDebug cube under parent that plays cue every
        /// interval seconds. A marker with no collider, like the listeners.
        /// </summary>
        internal static GameObject AddEmitter(string name, Transform parent, Vector3 localPosition, SoundCue cue, float interval)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = Vector3.one * CubeSize;
            Object.DestroyImmediate(cube.GetComponent<Collider>());

            SerializedObject serialized = new(cube.AddComponent<SoundEmitterDebug>());
            serialized.FindProperty("cue").objectReferenceValue = cue;
            serialized.FindProperty("interval").floatValue = interval;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return cube;
        }

        /// <summary>
        /// Makes sure the scene has what the test needs outside the test
        /// area itself: a Sound Player object (listening from the main
        /// camera) and a SoundDebug on the Debug object.
        /// </summary>
        internal static void EnsureSceneObjects()
        {
            if (Object.FindAnyObjectByType<SoundPlayer>() == null) {
                GameObject player = new("Sound Player");
                Undo.RegisterCreatedObjectUndo(player, "Build Sound Test Area");

                // Adding the component runs its Reset(), which finds the
                // main camera; set here too in case that changes.
                SerializedObject serialized = new(player.AddComponent<SoundPlayer>());

                if (Camera.main != null) {
                    serialized.FindProperty("listener").objectReferenceValue = Camera.main.transform;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                } else {
                    Debug.LogWarning("SoundTestArea: no main camera found - assign the Sound Player's Listener by hand.");
                }
            }

            // The wind for outside, unless the Sound Player already has an
            // outside ambience of its own.
            SoundPlayer soundPlayer = Object.FindAnyObjectByType<SoundPlayer>();
            SerializedObject ambience = new(soundPlayer);
            SerializedProperty outside = ambience.FindProperty("outsideAmbience");

            if (outside.objectReferenceValue == null) {
                outside.objectReferenceValue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.OutsideAmbienceCuePath);
                ambience.ApplyModifiedProperties();
            }

            GameObject debug = GameObject.Find("Debug");

            if (debug == null) {
                Debug.LogWarning("SoundTestArea: no 'Debug' object found - add SoundDebug to an object by hand for the L key report.");
            } else if (debug.GetComponent<SoundDebug>() == null) {
                Undo.AddComponent<SoundDebug>(debug);
            }
        }

        /// <summary>
        /// Makes a NoiseListenerDebug cube under parent: a marker with no
        /// collider, so it can't block the player or the hands.
        /// </summary>
        internal static void AddListener(string name, Transform parent, Vector3 localPosition)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = Vector3.one * CubeSize;
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.AddComponent<NoiseListenerDebug>();
        }
    }
}
