using Player;
using UnityEditor;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Build Sound Test House) that builds a
    /// greybox two-storey house with a walled yard: a fuller test of
    /// SoundRooms and SoundPortals than the three-room Sound Test Area,
    /// with the cases a real level has - several rooms deep, a floor above,
    /// a stairwell, a closed door, a window, and outdoor space split into
    /// rooms. Built from code so it can be rebuilt identically (a rebuild
    /// keeps where the old one was moved to), and undone with Ctrl+Z.
    ///
    /// Seen from the front (the front wall faces -Z), x runs left to right
    /// from 0 to 12 and z front to back from 0 to 8:
    ///
    /// Ground floor: Hall (left, the full depth; front door from the yard,
    /// stairs up along its left wall), Kitchen (front middle, doorway from
    /// the Hall), Store (front right, behind a closed door from the
    /// Kitchen), Lounge (back, doorway from the Hall, a window in the back
    /// wall).
    /// Upper floor: Landing (over the Hall, joined to it by the stairwell)
    /// and Bedroom (over everything else, doorway from the Landing). No
    /// roof, so the upper floor can be seen from above in the Scene view.
    /// Yard: in front of the house, walled, with a gate to outside.
    ///
    /// A stand-in guard (footsteps on a moving cube) walks from the Bedroom
    /// across the Landing, down the stairs, through the Hall into the
    /// Kitchen and back; a knock repeats in the Store. A NoiseListenerDebug
    /// cube stands in every room and outside for the N key report.
    ///
    /// Uses the player test areas' TestGeometry for the solid pieces
    /// (editor-only code, so Core's runtime scripts still don't depend on
    /// Player).
    /// </summary>
    public static class SoundTestHouse
    {
        private const string RootName = "Sound Test House";
        private const string EnvironmentLayerName = "Environment";

        private const float Width = 12f;
        private const float Depth = 8f;
        private const float WallHeight = 3f;
        private const float WallThickness = 0.2f;
        private const float SlabThickness = 0.2f;
        private const float DoorWidth = 1f;
        private const float DoorHeight = 2.1f;
        private const float YardDepth = 6f;
        private const float YardWallHeight = 2.4f;
        private const float EarHeight = 1.6f;

        // The upper floor's walking surface: the top of the slab that rests
        // on the ground floor walls.
        private const float UpperFloor = WallHeight + SlabThickness;

        // The stairs: a ramp along the Hall's left wall, rising towards the
        // back, under a hole in the upper floor.
        private const float StairWidth = 1.2f;
        private const float StairHoleWidth = 1.4f;
        private const float StairBottomZ = 2.5f;
        private const float StairTopZ = 7f;

        // Where the house is built: beyond the Sound Test Area. It brings
        // its own ground, so it can stand anywhere - move the root.
        private static readonly Vector3 _rootPosition = new(-36f, 0f, 12f);

        /// <summary>
        /// Builds the house, replacing an existing one (after asking).
        /// </summary>
        [MenuItem("TeaLeaf/Build Sound Test House")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer(EnvironmentLayerName);

            if (environment < 0) {
                Debug.LogError($"SoundTestHouse: the '{EnvironmentLayerName}' layer must exist.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);
            Vector3 rootPosition = _rootPosition;
            Quaternion rootRotation = Quaternion.identity;

            if (existing != null) {
                if (!EditorUtility.DisplayDialog(
                        "Sound Test House",
                        "A test house already exists in the scene. Replace it?",
                        "Replace",
                        "Cancel")) {
                    return;
                }

                // Rebuild where the old one stood, in case it was moved.
                rootPosition = existing.transform.position;
                rootRotation = existing.transform.rotation;
                Undo.DestroyObjectImmediate(existing);
            }

            PlaceholderSounds.Create();
            SoundCue footstepCue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.FootstepCuePath);
            SoundCue impactCue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.ImpactCuePath);

            SoundTestArea.EnsureSceneObjects();

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(rootPosition, rootRotation);
            Undo.RegisterCreatedObjectUndo(root, "Build Sound Test House");
            Transform parent = root.transform;

            BuildGeometry(parent, environment);
            BuildRoomsAndPortals(parent);
            BuildListeners(parent);
            BuildEmitters(parent, footstepCue, impactCue);

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// The solid pieces: ground, walls with their doorways, the upper
        /// floor with its stair hole, the stair ramp and the yard walls.
        /// </summary>
        private static void BuildGeometry(Transform parent, int layer)
        {
            // Its own ground under the house and yard, a centimetre proud of
            // y = 0 so it doesn't flicker against a scene floor at the same
            // height.
            TestGeometry.Box("Ground", parent, new Vector3(Width * 0.5f, -0.09f, (Depth - YardDepth) * 0.5f), new Vector3(Width + 1f, 0.2f, Depth + YardDepth + 1f), layer);

            // Ground floor. Outer walls, then the inner ones: the wall down
            // the Hall's right side (doorways to the Kitchen and Lounge),
            // the Kitchen-Store wall (the closed door) and the wall between
            // the front rooms and the Lounge.
            Wall("Front Wall", parent, true, 0f, 0f, Width, 0f, WallHeight, layer, 2f);
            Wall("Back Wall", parent, true, Depth, 0f, Width, 0f, WallHeight, layer);
            Wall("Left Wall", parent, false, 0f, 0f, Depth, 0f, WallHeight, layer);
            Wall("Right Wall", parent, false, Width, 0f, Depth, 0f, WallHeight, layer);
            Wall("Hall Wall", parent, false, 4f, 0f, Depth, 0f, WallHeight, layer, 2f, 6f);
            Wall("Kitchen-Store Wall", parent, false, 8f, 0f, 4f, 0f, WallHeight, layer, 2f);
            Wall("Lounge Wall", parent, true, 4f, 4f, Width, 0f, WallHeight, layer);

            // The Store's door, shut: a thin slab filling its doorway.
            TestGeometry.Box("Store Door (closed)", parent, new Vector3(8f, DoorHeight * 0.5f, 2f), new Vector3(0.08f, DoorHeight, DoorWidth), layer);

            // Upper floor slab, in three pieces round the stair hole (which
            // runs along the left wall from StairBottomZ to StairTopZ).
            float slabY = WallHeight + SlabThickness * 0.5f;
            float mainWidth = Width - StairHoleWidth;
            float backLength = Depth - StairTopZ;
            Tag(TestGeometry.Box("Upper Floor", parent, new Vector3(StairHoleWidth + mainWidth * 0.5f, slabY, Depth * 0.5f), new Vector3(mainWidth, SlabThickness, Depth), layer), SurfaceType.Wood);
            Tag(TestGeometry.Box("Upper Floor Stair Front", parent, new Vector3(StairHoleWidth * 0.5f, slabY, StairBottomZ * 0.5f), new Vector3(StairHoleWidth, SlabThickness, StairBottomZ), layer), SurfaceType.Wood);
            Tag(TestGeometry.Box("Upper Floor Stair Back", parent, new Vector3(StairHoleWidth * 0.5f, slabY, StairTopZ + backLength * 0.5f), new Vector3(StairHoleWidth, SlabThickness, backLength), layer), SurfaceType.Wood);

            // Floor coverings for the footstep surfaces, as thin slabs on
            // the ground (which is untagged, so stone - the Hall and the
            // edges of the Yard): a carpeted Lounge, a tiled Kitchen, a
            // metal plate floor in the Store, and across the Yard from left
            // to right a puddle, a gravel path from the gate and a lawn.
            // The upper floor and stairs are wood.
            const float coveringThickness = 0.02f;
            float coveringY = 0.01f + coveringThickness * 0.5f;
            Tag(TestGeometry.Box("Lounge Carpet", parent, new Vector3(8f, coveringY, 6f), new Vector3(7.6f, coveringThickness, 3.6f), layer), SurfaceType.Carpet);
            Tag(TestGeometry.Box("Store Metal Floor", parent, new Vector3(10f, coveringY, 2f), new Vector3(3.6f, coveringThickness, 3.6f), layer), SurfaceType.Metal);
            Tag(TestGeometry.Box("Yard Puddle", parent, new Vector3(2.5f, coveringY, -3f), new Vector3(3f, coveringThickness, 3f), layer), SurfaceType.Water);
            Tag(TestGeometry.Box("Kitchen Tile Floor", parent, new Vector3(6f, coveringY, 2f), new Vector3(3.6f, coveringThickness, 3.6f), layer), SurfaceType.Tile);
            Tag(TestGeometry.Box("Yard Gravel Path", parent, new Vector3(6f, coveringY, -3f), new Vector3(2f, coveringThickness, 5.6f), layer), SurfaceType.Gravel);
            Tag(TestGeometry.Box("Yard Lawn", parent, new Vector3(9.5f, coveringY, -3f), new Vector3(4f, coveringThickness, 5f), layer), SurfaceType.Grass);

            // The stairs: a smooth ramp, like the town's. It rises the
            // height of the upper floor over the length of the hole (about
            // 35 degrees, under the CharacterController's 45 degree limit).
            // A box turned about X: negative tilts its far (+Z) end up.
            const float rampThickness = 0.1f;
            float run = StairTopZ - StairBottomZ;
            float angle = Mathf.Atan2(UpperFloor, run) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(run * run + UpperFloor * UpperFloor);
            GameObject ramp = TestGeometry.Box("Stairs", parent, new Vector3(WallThickness * 0.5f + StairWidth * 0.5f, UpperFloor * 0.5f - rampThickness * 0.6f, (StairBottomZ + StairTopZ) * 0.5f), new Vector3(StairWidth, rampThickness, length), layer);
            ramp.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            Tag(ramp, SurfaceType.Wood);

            // Upper floor walls: the outer four and the Landing-Bedroom wall
            // with its doorway.
            Wall("Upper Front Wall", parent, true, 0f, 0f, Width, UpperFloor, WallHeight, layer);
            Wall("Upper Back Wall", parent, true, Depth, 0f, Width, UpperFloor, WallHeight, layer);
            Wall("Upper Left Wall", parent, false, 0f, 0f, Depth, UpperFloor, WallHeight, layer);
            Wall("Upper Right Wall", parent, false, Width, 0f, Depth, UpperFloor, WallHeight, layer);
            Wall("Landing Wall", parent, false, 4f, 0f, Depth, UpperFloor, WallHeight, layer, 2f);

            // Yard walls, with the gate in the middle of the front one.
            Wall("Yard Left Wall", parent, false, 0f, -YardDepth, 0f, 0f, YardWallHeight, layer);
            Wall("Yard Right Wall", parent, false, Width, -YardDepth, 0f, 0f, YardWallHeight, layer);
            Wall("Yard Front Wall", parent, true, -YardDepth, 0f, Width, 0f, YardWallHeight, layer, 6f);
        }

        /// <summary>
        /// The sound rooms (each box meets its neighbours in the middle of
        /// the wall or floor between them) and the portals joining them.
        /// </summary>
        private static void BuildRoomsAndPortals(Transform parent)
        {
            // Ground floor rooms reach up to the middle of the upper floor
            // slab; upper rooms start there.
            float groundHeight = WallHeight + SlabThickness * 0.5f;
            float groundY = groundHeight * 0.5f;
            float upperHeight = WallHeight + SlabThickness * 0.5f;
            float upperY = groundHeight + upperHeight * 0.5f;

            SoundTestArea.AddRoom("Sound Room Hall", parent, new Vector3(2f, groundY, 4f), new Vector3(4f, groundHeight, 8f));
            SoundTestArea.AddRoom("Sound Room Kitchen", parent, new Vector3(6f, groundY, 2f), new Vector3(4f, groundHeight, 4f));
            SoundTestArea.AddRoom("Sound Room Store", parent, new Vector3(10f, groundY, 2f), new Vector3(4f, groundHeight, 4f));
            SoundTestArea.AddRoom("Sound Room Lounge", parent, new Vector3(8f, groundY, 6f), new Vector3(8f, groundHeight, 4f));
            SoundTestArea.AddRoom("Sound Room Landing", parent, new Vector3(2f, upperY, 4f), new Vector3(4f, upperHeight, 8f));
            SoundTestArea.AddRoom("Sound Room Bedroom", parent, new Vector3(8f, upperY, 4f), new Vector3(8f, upperHeight, 8f));

            // Outdoor space only blocks sound if it's a room too: without
            // this the yard would be part of "outside", and everything
            // outside hears everything else outside in a straight line.
            SoundTestArea.AddRoom("Sound Room Yard", parent, new Vector3(Width * 0.5f, 1.5f, -YardDepth * 0.5f), new Vector3(Width, 3f, YardDepth));

            // Lights, for the visibility test: a torch in the Hall and one
            // in the Kitchen, and a dimmer lamp at the doorway end of the
            // Lounge. Nothing else is placed - the moon (a Moonlight on the
            // scene's Directional Light) lights the Yard and the roofless
            // upper floor wherever the walls don't shadow them, and the
            // ground floor's ceiling keeps it out of the rooms below. The
            // Store is dark: its closed door blocks the Kitchen's torch.
            AddLight("Light Source Hall Torch", parent, new Vector3(2.5f, 2.2f, 4f), 1f, 6f);
            AddLight("Light Source Kitchen Torch", parent, new Vector3(6f, 2.2f, 2f), 1f, 5f);
            AddLight("Light Source Lounge Lamp", parent, new Vector3(5f, 1.2f, 6f), 0.7f, 4f);

            Vector2 doorSize = new(DoorWidth, DoorHeight);
            float doorY = DoorHeight * 0.5f;

            // Doorways in walls that run along X face along Z (yaw 0); those
            // in walls that run along Z are turned to face along X (yaw 90).
            SoundTestArea.AddPortal("Sound Portal Front Door", parent, new Vector3(2f, doorY, 0f), 0f, doorSize, true);
            SoundTestArea.AddPortal("Sound Portal Yard Gate", parent, new Vector3(6f, doorY, -YardDepth), 0f, doorSize, true);
            SoundTestArea.AddPortal("Sound Portal Hall-Kitchen", parent, new Vector3(4f, doorY, 2f), 90f, doorSize, true);
            SoundTestArea.AddPortal("Sound Portal Hall-Lounge", parent, new Vector3(4f, doorY, 6f), 90f, doorSize, true);
            SoundTestArea.AddPortal("Sound Portal Kitchen-Store (closed door)", parent, new Vector3(8f, doorY, 2f), 90f, doorSize, false);
            SoundTestArea.AddPortal("Sound Portal Landing-Bedroom", parent, new Vector3(4f, UpperFloor + doorY, 2f), 90f, doorSize, true);

            // The Lounge's window: no opening in the wall, a closed portal
            // that lets muffled sound through to outside at the back.
            SoundTestArea.AddPortal("Sound Portal Lounge Window (closed)", parent, new Vector3(8f, 1.5f, Depth), 0f, new Vector2(1.2f, 1.2f), false);

            // The stairwell: a portal lying flat in the stair hole, level
            // with the middle of the slab, turned so its local Z points up
            // and down - the only way sound gets between the floors.
            SoundTestArea.AddPortal("Sound Portal Stairwell", parent, new Vector3(StairHoleWidth * 0.5f, groundHeight, (StairBottomZ + StairTopZ) * 0.5f), Quaternion.Euler(90f, 0f, 0f), new Vector2(StairHoleWidth, StairTopZ - StairBottomZ), true);
        }

        /// <summary>
        /// A NoiseListenerDebug cube at ear height in every room, in the
        /// yard, and outside at the front and back.
        /// </summary>
        private static void BuildListeners(Transform parent)
        {
            float upperEar = UpperFloor + EarHeight;

            SoundTestArea.AddListener("Noise Listener Hall", parent, new Vector3(3f, EarHeight, 4.5f));
            SoundTestArea.AddListener("Noise Listener Kitchen", parent, new Vector3(6f, EarHeight, 3f));
            SoundTestArea.AddListener("Noise Listener Store", parent, new Vector3(10f, EarHeight, 2f));
            SoundTestArea.AddListener("Noise Listener Lounge", parent, new Vector3(9f, EarHeight, 6f));
            SoundTestArea.AddListener("Noise Listener Landing", parent, new Vector3(2.8f, upperEar, 4f));
            SoundTestArea.AddListener("Noise Listener Bedroom", parent, new Vector3(9f, upperEar, 3f));
            SoundTestArea.AddListener("Noise Listener Yard", parent, new Vector3(9f, EarHeight, -3f));
            SoundTestArea.AddListener("Noise Listener Outside Front", parent, new Vector3(6f, EarHeight, -8f));
            SoundTestArea.AddListener("Noise Listener Outside Back", parent, new Vector3(8f, EarHeight, 9.5f));
        }

        /// <summary>
        /// The stand-in guard walking its route, and the knock in the Store.
        /// </summary>
        private static void BuildEmitters(Transform parent, SoundCue footstepCue, SoundCue impactCue)
        {
            // The guard's route, as points just above the floor: Bedroom,
            // out of its door, along the Landing to the top of the stairs,
            // down them, across the Hall and into the Kitchen.
            float low = SoundTestArea.CubeSize * 0.5f;
            float high = UpperFloor + low;

            Vector3[] route = {
                new(9f, high, 5f),
                new(5f, high, 2f),
                new(2.6f, high, 2f),
                new(2.6f, high, 7.5f),
                new(0.7f, high, 7.2f),
                new(0.7f, low, 2.3f),
                new(2.5f, low, 1.5f),
                new(4f, low, 2f),
                new(6.5f, low, 2.5f)
            };

            GameObject path = new("Guard Path");
            path.transform.SetParent(parent, false);
            Transform[] waypoints = new Transform[route.Length];

            for (int i = 0; i < route.Length; i++) {
                GameObject point = new($"Point {i + 1}");
                point.transform.SetParent(path.transform, false);
                point.transform.localPosition = route[i];
                waypoints[i] = point.transform;
            }

            GameObject guard = SoundTestArea.AddEmitter("Sound Emitter Guard (footsteps)", parent, route[0], footstepCue, 0.5f);

            // The waypoint list is a private serialized array, so it's
            // filled the way the Inspector would fill it.
            SerializedObject serialized = new(guard.AddComponent<WaypointMoverDebug>());
            SerializedProperty list = serialized.FindProperty("waypoints");
            list.arraySize = waypoints.Length;

            for (int i = 0; i < waypoints.Length; i++) {
                list.GetArrayElementAtIndex(i).objectReferenceValue = waypoints[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // The guard's steps follow the floor it's walking on (wood
            // upstairs and on the stairs, stone in the Hall, tile in the
            // Kitchen) rather than always playing the one cue.
            SerializedObject emitter = new(guard.GetComponent<SoundEmitterDebug>());
            emitter.FindProperty("surfaceSounds").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SurfaceSounds>(PlaceholderSounds.SurfaceSoundsPath);
            emitter.ApplyModifiedPropertiesWithoutUndo();

            SoundTestArea.AddEmitter("Sound Emitter Store (impact)", parent, new Vector3(10.5f, 1f, 1f), impactCue, 2f);
        }

        /// <summary>
        /// Makes a LightSource under parent, with a Unity point light of
        /// the same reach so the light can be seen as well as counted. The
        /// source's level and range are private serialized fields, so
        /// they're set the way the Inspector would set them.
        /// </summary>
        private static void AddLight(string name, Transform parent, Vector3 localPosition, float level, float range)
        {
            GameObject light = new(name);
            light.transform.SetParent(parent, false);
            light.transform.localPosition = localPosition;

            Light visible = light.AddComponent<Light>();
            visible.type = LightType.Point;
            visible.range = range;
            visible.intensity = 4f * level;
            visible.color = new Color(1f, 0.75f, 0.45f);
            visible.shadows = LightShadows.None;

            SerializedObject serialized = new(light.AddComponent<LightSource>());
            serialized.FindProperty("level").floatValue = level;
            serialized.FindProperty("range").floatValue = range;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Gives a floor piece a SurfaceTag. Its surface is a private
        /// serialized field, so it's set the way the Inspector would set it.
        /// </summary>
        private static void Tag(GameObject floor, SurfaceType surface)
        {
            SerializedObject serialized = new(floor.AddComponent<SurfaceTag>());
            serialized.FindProperty("surface").enumValueIndex = (int)surface;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Builds a straight wall with doorways in it. The wall runs along X
        /// (alongX) at z = fixedCoord, or along Z at x = fixedCoord, from
        /// "from" to "to" on that axis, standing on baseY. Each value in
        /// doors is the middle of a doorway along the wall: the wall is
        /// built as the pieces between the doorways plus a lintel over each.
        /// Doorways must be given in order along the wall.
        /// </summary>
        private static void Wall(string name, Transform parent, bool alongX, float fixedCoord, float from, float to, float baseY, float height, int layer, params float[] doors)
        {
            // Run half a thickness past each end, so walls meeting at a
            // corner fill it.
            float cursor = from - WallThickness * 0.5f;
            float end = to + WallThickness * 0.5f;

            for (int i = 0; i < doors.Length; i++) {
                float doorStart = doors[i] - DoorWidth * 0.5f;
                float doorEnd = doors[i] + DoorWidth * 0.5f;

                WallPiece(name, parent, alongX, fixedCoord, cursor, doorStart, baseY, height, layer);
                WallPiece(name + " Lintel", parent, alongX, fixedCoord, doorStart, doorEnd, baseY + DoorHeight, height - DoorHeight, layer);
                cursor = doorEnd;
            }

            WallPiece(name, parent, alongX, fixedCoord, cursor, end, baseY, height, layer);
        }

        /// <summary>
        /// One solid box of a wall, between two positions along it.
        /// </summary>
        private static void WallPiece(string name, Transform parent, bool alongX, float fixedCoord, float from, float to, float baseY, float height, int layer)
        {
            float length = to - from;

            if (length <= 0f || height <= 0f) {
                return;
            }

            float middle = (from + to) * 0.5f;
            float y = baseY + height * 0.5f;

            Vector3 position = alongX ? new Vector3(middle, y, fixedCoord) : new Vector3(fixedCoord, y, middle);
            Vector3 size = alongX ? new Vector3(length, height, WallThickness) : new Vector3(WallThickness, height, length);
            TestGeometry.Box(name, parent, position, size, layer);
        }
    }
}
