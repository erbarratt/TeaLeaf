using Core;
using Player;
using UnityEditor;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Build Smithy) that builds a
    /// half-timbered blacksmith's house in front of the main camera: a
    /// stone ground floor with a forge arch and a doorway, a jettied
    /// (overhanging) timber-framed upper floor with plaster panels, a
    /// steep tiled roof with a cross gable over the front, a lean-to
    /// awning over the forge, a chimney, a ladder against the side, and
    /// barrels, an anvil and a workbench outside.
    ///
    /// How it's put together, for the Quest:
    /// - Six materials (plaster, timber, stone, roof tiles, planks, iron -
    ///   made by SmithyTextures) and ONE mesh per material, so the whole
    ///   building is six draw calls however many beams it has. The meshes
    ///   are saved as assets (Assets/Art/Models/Smithy) and overwritten
    ///   by a rebuild.
    /// - Depth is modelled: beams, sills, tiles' slabs, the arch's stones
    ///   are real geometry. Textures and normal maps only add the fine
    ///   surface detail.
    /// - Everything is Static, on Environment, and each mesh is its own
    ///   (non-moving) mesh collider, tagged with its surface for
    ///   footsteps.
    ///
    /// The building is hollow: the ground floor room is entered by the
    /// doorway, a ladder inside goes up through a hatch to the upper
    /// room, and the upper room's four windows are open. The attic is
    /// closed.
    ///
    /// Climbing (all ClimbableEdges and Ladders, under "Climbables"):
    /// - Every upper wall has three rails to grab - the jetty beam, the
    ///   rail at sill height and the rail over the windows - and each
    ///   window's sill can be mantled into, crouched, from either side.
    /// - The awning's front edge can be mantled onto, and from the awning
    ///   the front window is in reach.
    /// - The outside ladder leads up to the side window.
    /// - Each gable end has rails up to the roof's ridge, and the ridge
    ///   itself can be mantled onto and walked along (it has a flat cap).
    ///   The front gable leads to its own, lower ridge, and from there
    ///   the main ridge can be mantled onto.
    /// - The eaves, and the barred window's sill, can be hung from.
    /// The roof slopes themselves are too steep to stand on.
    ///
    /// All measurements are metres in the building's own space: X along
    /// the front, Y up, Z out of the front (towards the forge side).
    ///
    /// Nothing is quite true, in the storybook way: the building is
    /// written square and upright and then pushed out of true as a whole
    /// (see Warp()) - each storey leans and turns a different way - the
    /// roofs sweep in a curve from a steep ridge to flared eaves, the
    /// studs wander, the braces and posts bow and the chimney staggers.
    /// What the player stands on or holds stays true: Warp() only slides
    /// and turns each level, so every floor, rail, sill and ridge is
    /// still level and straight, and the ledges and ladders are moved by
    /// the same amounts as the timber they sit on.
    ///
    /// Built from code so it can be rebuilt identically, and undone with
    /// Ctrl+Z. In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class SmithyBuilding
    {
        private const string RootName = "Smithy";
        private const string MeshFolder = "Assets/Art/Models/Smithy";
        private const string LedgeProfilePath = "Assets/Data/LedgeGrip.asset";
        private const string LadderProfilePath = "Assets/Data/LadderRung.asset";

        // The paving the building stands on: its top, a little above the
        // scene's floor.
        private const float GroundY = 0.03f;

        // The stone ground floor: half its length along X, its front and
        // back faces, how thick its walls are and how high they go.
        private const float StoneHalfX = 3f;
        private const float StoneFrontZ = 2.5f;
        private const float StoneBackZ = -2.5f;
        private const float StoneThickness = 0.4f;
        private const float StoneTop = 2.6f;

        // The upper floor: its floorboards' top, and its walls' outer
        // faces - 0.3m further out than the stone at the front and sides
        // (the jetty), flush at the back.
        private const float FloorTop = 2.8f;
        private const float UpperHalfX = 3.3f;
        private const float UpperFrontZ = 2.8f;
        private const float UpperBackZ = -2.5f;
        private const float UpperHeight = 2.6f;
        private const float UpperThickness = 0.2f;

        // Windows in the upper walls: how wide, and where their sill
        // board's top and their head are above the upper floor.
        private const float WindowWidth = 1f;
        private const float WindowSill = 0.93f;
        private const float WindowHead = 2.1f;

        // The roof: where it starts (the top of the upper walls), how
        // steep it is on average, how thick its slabs are, and how far it
        // overhangs the walls at the gable ends (verge).
        private const float EaveY = FloorTop + UpperHeight;
        private const float RoofPitch = 48f;
        private const float RoofThickness = 0.14f;
        private const float VergeOverhang = 0.2f;

        // The roof's sweep. A slope is a curve, not a straight line:
        // steeper than RoofPitch at the ridge and shallower at the eaves.
        // RoofBow is how strong the curve is (0 = straight; 0.3 would be
        // upright at the ridge). EaveReach is how far down the curve the
        // slab goes, where 1 is the top of the wall - so the part past 1
        // is the eaves. RoofPieces is how many flat strips the curve is
        // cut into.
        private const float RoofBow = 0.12f;
        private const float EaveReach = 1.14f;
        private const int RoofPieces = 8;

        // How the building is pushed out of true (see Warp()). At each of
        // these heights, everything is slid sideways by the lean (metres
        // along X and Z) and turned about the building's middle by the
        // twist (degrees); between two heights the amounts blend. The
        // ground doesn't move, and the floor (StoneTop to FloorTop) moves
        // in one piece.
        private static readonly float[] _warpHeights = { 0f, StoneTop, FloorTop, EaveY, EaveY + 3f };
        private static readonly Vector2[] _warpLean = { new(0f, 0f), new(0.08f, -0.03f), new(0.08f, -0.03f), new(-0.07f, 0.05f), new(0f, 0.12f) };
        private static readonly float[] _warpTwist = { 0f, 1.2f, 1.2f, -1.2f, -2.5f };

        // The seed for the timber's unevenness: the same seed gives the
        // same building every time.
        private const int CrookedSeed = 1907;

        // The main roof's ridge runs along X over the middle of the upper
        // floor, front to back.
        private const float RoofCentreZ = (UpperFrontZ + UpperBackZ) * 0.5f;
        private const float RoofHalfSpan = (UpperFrontZ - UpperBackZ) * 0.5f;

        // The cross gable on the front: where its middle is along X, and
        // half its width.
        private const float GableX = -1.2f;
        private const float GableHalfWidth = 1.8f;

        // The flat cap along each ridge: half its width, and how far its
        // top is above the ridge line. Wide enough to stand on.
        private const float RidgeCapHalfWidth = 0.17f;
        private const float RidgeCapTop = 0.2f;

        // The lean-to awning over the forge.
        private const float AwningPitch = 12f;
        private const float AwningWallY = 2.3f;
        private const float AwningRun = 1.3f;
        private const float AwningThickness = 0.1f;

        // How far in from an edge's lip a mantle lands - the same as
        // ClimbableEdge's own default.
        private const float MantleInset = 0.4f;

        // Ladder rungs, matching the Ladder component's own defaults.
        private const float FirstRungHeight = 0.3f;
        private const float RungSpacing = 0.3f;

        // One mesh being collected per material - see BuildingMesh.
        private static BuildingMesh _plaster;
        private static BuildingMesh _timber;
        private static BuildingMesh _stone;
        private static BuildingMesh _tiles;
        private static BuildingMesh _planks;
        private static BuildingMesh _iron;

        // Where the climbables go, and what they're set up with.
        private static Transform _climbables;
        private static HandSnapProfile _ledgeProfile;
        private static HandSnapProfile _ladderProfile;
        private static int _climbableLayer;

        // How high the two ridges are, worked out once from RoofPitch.
        private static float _ridgeY;
        private static float _gableRidgeY;

        // The source of the timber's unevenness - see Jitter().
        private static System.Random _random;

        [MenuItem("TeaLeaf/Build Smithy")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer("Environment");
            _climbableLayer = LayerMask.NameToLayer("Climbable");

            if (environment < 0 || _climbableLayer < 0) {
                Debug.LogError("SmithyBuilding: the 'Environment' and 'Climbable' layers must exist.");
                return;
            }

            _ledgeProfile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(LedgeProfilePath);
            _ladderProfile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(LadderProfilePath);

            if (_ledgeProfile == null || _ladderProfile == null) {
                Debug.LogError($"SmithyBuilding: couldn't load the hand snap profiles ({LedgeProfilePath}, {LadderProfilePath}).");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog("Smithy", "A smithy already exists in the scene. Replace it?", "Replace", "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            // On the floor, 10m ahead of the main camera, turned so its
            // front faces back towards it.
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Camera camera = Camera.main;

            if (camera != null) {
                Vector3 forward = camera.transform.forward;
                forward.y = 0f;

                if (forward.sqrMagnitude > 0.001f) {
                    rotation = Quaternion.LookRotation(-forward);
                }

                position = camera.transform.position + rotation * new Vector3(0f, 0f, -10f);
                position.y = 0f;
            }

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(root, "Build Smithy");

            GameObject climbables = new("Climbables");
            climbables.transform.SetParent(root.transform, false);
            _climbables = climbables.transform;

            SmithyTextures.Materials materials = SmithyTextures.GetMaterials();

            _plaster = new BuildingMesh(SmithyTextures.PlasterTileSize);
            _timber = new BuildingMesh(SmithyTextures.TimberTileSize);
            _stone = new BuildingMesh(SmithyTextures.StoneTileSize);
            _tiles = new BuildingMesh(SmithyTextures.TilesTileSize);
            _planks = new BuildingMesh(SmithyTextures.PlanksTileSize);
            _iron = new BuildingMesh(1f);

            float tan = Mathf.Tan(RoofPitch * Mathf.Deg2Rad);
            _ridgeY = EaveY + RoofHalfSpan * tan;
            _gableRidgeY = EaveY + GableHalfWidth * tan;

            _random = new System.Random(CrookedSeed);
            SetWarp(Warp);

            BuildGroundFloor();
            BuildForge();
            BuildFloors();
            BuildUpperWalls();
            BuildGables();
            BuildRoof();
            BuildAwning();
            BuildLadders();
            BuildProps();

            SmithyTextures.EnsureFolder(MeshFolder);

            AddPart(root.transform, "Plaster", _plaster, materials.Plaster, SurfaceType.Plaster, environment);
            AddPart(root.transform, "Timber", _timber, materials.Timber, SurfaceType.Wood, environment);
            AddPart(root.transform, "Stone", _stone, materials.Stone, SurfaceType.Stone, environment);
            AddPart(root.transform, "Tiles", _tiles, materials.Tiles, SurfaceType.Tile, environment);
            AddPart(root.transform, "Planks", _planks, materials.Planks, SurfaceType.Wood, environment);
            AddPart(root.transform, "Iron", _iron, materials.Iron, SurfaceType.Metal, environment);

            AssetDatabase.SaveAssets();

            // Nothing is kept between builds.
            _plaster = _timber = _stone = _tiles = _planks = _iron = null;
            _climbables = null;
            _random = null;

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// Turns one collected mesh into an object under the root: the
        /// mesh saved as an asset (the existing one overwritten, so the
        /// scene keeps pointing at it), drawn with its material, solid,
        /// tagged with its surface, and Static.
        /// </summary>
        private static void AddPart(Transform parent, string name, BuildingMesh builder, Material material, SurfaceType surface, int layer)
        {
            if (builder.IsEmpty) {
                return;
            }

            string path = $"{MeshFolder}/Smithy{name}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;

            if (isNew) {
                mesh = new Mesh { name = $"Smithy{name}" };
            }

            builder.WriteTo(mesh);

            if (isNew) {
                AssetDatabase.CreateAsset(mesh, path);
            } else {
                EditorUtility.SetDirty(mesh);
            }

            GameObject part = new(name) { layer = layer };
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
            part.AddComponent<MeshCollider>().sharedMesh = mesh;

            // Stone is what an untagged collider counts as anyway.
            if (surface != SurfaceType.Stone) {
                SerializedObject tag = new(part.AddComponent<SurfaceTag>());
                tag.FindProperty("surface").enumValueIndex = (int)surface;
                tag.ApplyModifiedPropertiesWithoutUndo();
            }

            part.isStatic = true;
        }

        /// <summary>
        /// Places and turns everything added next, in every material - so
        /// a wall or a ladder can be written in its own coordinates.
        /// </summary>
        private static void SetMatrix(Matrix4x4 matrix)
        {
            _plaster.Matrix = matrix;
            _timber.Matrix = matrix;
            _stone.Matrix = matrix;
            _tiles.Matrix = matrix;
            _planks.Matrix = matrix;
            _iron.Matrix = matrix;
        }

        /// <summary>
        /// Switches the warp on (pass Warp) or off (pass null) for
        /// everything added next, in every material.
        /// </summary>
        private static void SetWarp(System.Func<Vector3, Vector3> warp)
        {
            _plaster.Warp = warp;
            _timber.Warp = warp;
            _stone.Warp = warp;
            _tiles.Warp = warp;
            _planks.Warp = warp;
            _iron.Warp = warp;
        }

        /// <summary>
        /// How far the building is slid sideways (lean: x and z, metres)
        /// and turned about its middle (twist, degrees) at a height:
        /// blended between the two entries of the warp tables that the
        /// height lies between.
        /// </summary>
        private static void WarpAt(float y, out Vector2 lean, out float twist)
        {
            int last = _warpHeights.Length - 1;

            if (y <= _warpHeights[0]) {
                lean = _warpLean[0];
                twist = _warpTwist[0];
                return;
            }

            for (int i = 0; i < last; i++) {
                if (y <= _warpHeights[i + 1]) {
                    float t = Mathf.InverseLerp(_warpHeights[i], _warpHeights[i + 1], y);
                    lean = Vector2.Lerp(_warpLean[i], _warpLean[i + 1], t);
                    twist = Mathf.Lerp(_warpTwist[i], _warpTwist[i + 1], t);
                    return;
                }
            }

            lean = _warpLean[last];
            twist = _warpTwist[last];
        }

        /// <summary>
        /// Pushes a point out of true: turned about the building's middle
        /// and slid sideways by the amounts for its height. Its height is
        /// never changed, and every point at one height is moved alike -
        /// so a level surface stays level, flat and the same shape, and
        /// only what joins one height to another (a wall, a post) leans.
        /// </summary>
        private static Vector3 Warp(Vector3 point)
        {
            WarpAt(point.y, out Vector2 lean, out float twist);

            Vector3 turned = Quaternion.Euler(0f, twist, 0f) * new Vector3(point.x, 0f, point.z);
            return new Vector3(turned.x + lean.x, point.y, turned.z + lean.y);
        }

        /// <summary>
        /// A random amount between -amount and +amount: how far a timber
        /// is off where a ruler would put it.
        /// </summary>
        private static float Jitter(float amount)
        {
            return ((float)_random.NextDouble() * 2f - 1f) * amount;
        }

        /// <summary>
        /// Shorthand for a point.
        /// </summary>
        private static Vector3 P(float x, float y, float z)
        {
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// A ledge a hand can grab: lip is the middle of the solid's
        /// top-front line and yaw turns it to face out from its wall (0 =
        /// facing +Z). The grab volume follows the solid closely
        /// (TestGeometry.TightOverhang) and is no taller than the solid
        /// (solidHeight). With isMantleable the player can climb onto it,
        /// landing inset in from the lip; bothSidesThickness above 0 makes
        /// it grabbable from either side of something that thick. Long
        /// ledges land a mantle straight ahead of the player; short ones
        /// (a window) at their middle. lip and yaw are given as the
        /// building is written, square and upright: the ledge is warped
        /// here, as the solid it sits on is.
        /// </summary>
        private static GameObject Ledge(
            string name,
            Vector3 lip,
            float yaw,
            float width,
            float solidHeight,
            bool isMantleable = false,
            bool endsCrouched = false,
            float inset = MantleInset,
            float bothSidesThickness = 0f)
        {
            float overhang = TestGeometry.TightOverhang;
            WarpAt(lip.y, out _, out float twist);

            return TestGeometry.Edge(
                name,
                _climbables,
                Warp(lip),
                yaw + twist,
                width,
                isMantleable,
                endsCrouched,
                inset,
                _ledgeProfile,
                _climbableLayer,
                width <= 1.2f,
                bothSidesThickness,
                overhang,
                Mathf.Min(0.2f, solidHeight + overhang * 2f));
        }

        // ---- Ground floor ----

        /// <summary>
        /// The paving, the four stone walls (the front one with the forge
        /// arch and the doorway, the left one with a barred window), the
        /// corner buttress and the open door.
        /// </summary>
        private static void BuildGroundFloor()
        {
            _stone.Box(P(-4.6f, 0f, -3.3f), P(4.9f, GroundY, 5.2f));

            float inner = StoneFrontZ - StoneThickness;

            // Front wall, piece by piece along X: solid, the forge bay
            // (only the part above the arch - see BuildForge()), solid,
            // the doorway (only the part above it), solid.
            _stone.Box(P(-StoneHalfX, 0f, inner), P(-2.4f, StoneTop, StoneFrontZ));
            _stone.Box(P(-2.4f, 2f, inner), P(-0.6f, StoneTop, StoneFrontZ));
            _stone.Box(P(-0.6f, 0f, inner), P(0.3f, StoneTop, StoneFrontZ));
            _stone.Box(P(0.3f, 2f, inner), P(1.3f, StoneTop, StoneFrontZ));
            _stone.Box(P(1.3f, 0f, inner), P(StoneHalfX, StoneTop, StoneFrontZ));

            // Back wall, and the right wall between the front and back.
            _stone.Box(P(-StoneHalfX, 0f, StoneBackZ), P(StoneHalfX, StoneTop, StoneBackZ + StoneThickness));
            _stone.Box(P(StoneHalfX - StoneThickness, 0f, StoneBackZ + StoneThickness), P(StoneHalfX, StoneTop, inner));

            // Left wall, round a small window: each side of it, below
            // and above. Three iron bars, and a sill that sticks out far
            // enough to hang from.
            float leftInner = -StoneHalfX + StoneThickness;
            _stone.Box(P(-StoneHalfX, 0f, StoneBackZ + StoneThickness), P(leftInner, StoneTop, -0.6f));
            _stone.Box(P(-StoneHalfX, 0f, 0.4f), P(leftInner, StoneTop, inner));
            _stone.Box(P(-StoneHalfX, 0f, -0.6f), P(leftInner, 1.13f, 0.4f));
            _stone.Box(P(-StoneHalfX, 2.1f, -0.6f), P(leftInner, StoneTop, 0.4f));
            _stone.Box(P(-StoneHalfX - 0.07f, 1.13f, -0.7f), P(leftInner, 1.2f, 0.5f));

            for (int i = 0; i < 3; i++) {
                float z = -0.35f + i * 0.25f;
                _iron.Box(P(-2.82f, 1.2f, z - 0.012f), P(-2.795f, 2.1f, z + 0.012f));
            }

            Ledge("Barred Window Sill", P(-StoneHalfX - 0.07f, 1.2f, -0.1f), -90f, 1.2f, 0.07f);

            // A buttress on the front right corner, tucked under the jetty.
            _stone.Box(P(2.45f, 0f, 2f), P(3.15f, StoneTop, 2.65f));

            // The doorway's timber lintel, and the plank door standing
            // open into the room: hinged on the doorway's right, swung
            // 100 degrees inwards.
            _timber.Box(P(0.15f, 2f, StoneFrontZ), P(1.45f, 2.2f, StoneFrontZ + 0.06f));

            float swing = 100f * Mathf.Deg2Rad;
            Vector3 along = P(-Mathf.Cos(swing), 0f, -Mathf.Sin(swing));
            Vector3 hinge = P(1.3f, GroundY + 0.99f, inner);
            _planks.OrientedBox(hinge + along * 0.5f, along, Vector3.up, Vector3.Cross(along, Vector3.up), P(1f, 1.96f, 0.05f));
        }

        /// <summary>
        /// The forge: an arched opening in the front wall with a hearth
        /// behind it. The arch is a half circle of eight straight pieces.
        /// The wall above it is filled with eight wedges of masonry (each
        /// from one piece of the arch straight up to the wall above), and
        /// a ring of raised stones follows the arch on the wall's face.
        /// </summary>
        private static void BuildForge()
        {
            const int pieces = 8;
            const float centreX = -1.5f;
            const float springY = 1f;
            const float radius = 0.9f;
            const float ringWidth = 0.2f;
            const float ringProud = 0.05f;

            Vector3 through = P(0f, 0f, -StoneThickness);

            for (int i = 0; i < pieces; i++) {
                // From the left end of the arch, over the top, to the right.
                float angleA = Mathf.PI * (1f - i / (float)pieces);
                float angleB = Mathf.PI * (1f - (i + 1) / (float)pieces);

                Vector3 directionA = P(Mathf.Cos(angleA), Mathf.Sin(angleA), 0f);
                Vector3 directionB = P(Mathf.Cos(angleB), Mathf.Sin(angleB), 0f);
                Vector3 centre = P(centreX, springY, StoneFrontZ);

                Vector3 a = centre + directionA * radius;
                Vector3 b = centre + directionB * radius;

                _stone.Prism(new[] { a, b, P(b.x, 2f, StoneFrontZ), P(a.x, 2f, StoneFrontZ) }, through, Vector3.right, Vector3.up);

                Vector3 outerA = centre + directionA * (radius + ringWidth);
                Vector3 outerB = centre + directionB * (radius + ringWidth);

                _stone.Prism(new[] { a, b, outerB, outerA }, P(0f, 0f, ringProud), Vector3.right, Vector3.up);
            }

            // The ring carried down to the ground as a raised stone each
            // side of the opening.
            _stone.Box(P(centreX - radius - ringWidth, 0f, StoneFrontZ), P(centreX - radius, springY, StoneFrontZ + ringProud));
            _stone.Box(P(centreX + radius, 0f, StoneFrontZ), P(centreX + radius + ringWidth, springY, StoneFrontZ + ringProud));

            // The hearth: a raised stone bed with coals on it, and a
            // stone box round it inside the room (sides, back and top).
            float left = centreX - radius;
            float right = centreX + radius;
            float inner = StoneFrontZ - StoneThickness;

            _stone.Box(P(left, 0f, 1.3f), P(right, 0.45f, StoneFrontZ));
            _iron.Box(P(left + 0.2f, 0.45f, 1.5f), P(right - 0.2f, 0.5f, 2.3f));

            _stone.Box(P(left - 0.15f, 0f, 1.2f), P(left, 2.15f, inner));
            _stone.Box(P(right, 0f, 1.2f), P(right + 0.15f, 2.15f, inner));
            _stone.Box(P(left, 0f, 1.2f), P(right, 2.15f, 1.3f));
            _stone.Box(P(left, 2f, 1.3f), P(right, 2.15f, inner));
        }

        // ---- Floors ----

        /// <summary>
        /// The upper floor (with a hatch for the inside ladder), the
        /// attic floor that is the upper room's ceiling, the timber fascia
        /// hiding the floor's edge, and the joist ends showing under the
        /// jetty.
        /// </summary>
        private static void BuildFloors()
        {
            float bottom = StoneTop;

            // The hatch: against the back wall's inner face, in the right
            // half of the room.
            const float hatchLeft = 1.4f;
            const float hatchRight = 2.3f;
            float hatchBack = StoneBackZ + StoneThickness;
            float hatchFront = hatchBack + 0.9f;

            // The floor in four pieces round the hatch.
            _planks.Box(P(-UpperHalfX, bottom, UpperBackZ), P(UpperHalfX, FloorTop, hatchBack));
            _planks.Box(P(-UpperHalfX, bottom, hatchBack), P(hatchLeft, FloorTop, hatchFront));
            _planks.Box(P(hatchRight, bottom, hatchBack), P(UpperHalfX, FloorTop, hatchFront));
            _planks.Box(P(-UpperHalfX, bottom, hatchFront), P(UpperHalfX, FloorTop, UpperFrontZ));

            // The attic floor, inside the upper walls.
            _planks.Box(
                P(-UpperHalfX + UpperThickness, EaveY - 0.1f, UpperBackZ + UpperThickness),
                P(UpperHalfX - UpperThickness, EaveY, UpperFrontZ - UpperThickness));

            // Fascia boards over the floor's edge, all round.
            const float fascia = 0.02f;
            _timber.Box(P(-UpperHalfX - fascia, bottom, UpperFrontZ), P(UpperHalfX + fascia, FloorTop, UpperFrontZ + fascia));
            _timber.Box(P(-UpperHalfX - fascia, bottom, UpperBackZ - fascia), P(UpperHalfX + fascia, FloorTop, UpperBackZ));
            _timber.Box(P(UpperHalfX, bottom, UpperBackZ), P(UpperHalfX + fascia, FloorTop, UpperFrontZ));
            _timber.Box(P(-UpperHalfX - fascia, bottom, UpperBackZ), P(-UpperHalfX, FloorTop, UpperFrontZ));

            // Joist ends under the jetty: along the front...
            float joistY = bottom - 0.07f;

            for (float x = -3.1f; x <= 3.11f; x += 0.62f) {
                _timber.Beam(P(x, joistY, StoneFrontZ), P(x, joistY, UpperFrontZ), 0.14f, 0.12f, Vector3.right);
            }

            // ...and along both sides.
            for (float z = -2.2f; z <= 2.31f; z += 0.64f) {
                _timber.Beam(P(StoneHalfX, joistY, z), P(UpperHalfX, joistY, z), 0.14f, 0.12f, Vector3.forward);
                _timber.Beam(P(-UpperHalfX, joistY, z), P(-StoneHalfX, joistY, z), 0.14f, 0.12f, Vector3.forward);
            }
        }

        // ---- Upper floor walls ----

        /// <summary>
        /// The four timber-framed walls of the upper floor, each with a
        /// window, and a post on each corner.
        /// </summary>
        private static void BuildUpperWalls()
        {
            float width = UpperHalfX * 2f;
            float depth = UpperFrontZ - UpperBackZ;

            // The front window sits under the cross gable; the others are
            // in the middle of their walls.
            BuildUpperWall("Front", P(0f, FloorTop, UpperFrontZ), 0f, width, GableX);
            BuildUpperWall("Back", P(0f, FloorTop, UpperBackZ), 180f, width, 0f);
            BuildUpperWall("Right", P(UpperHalfX, FloorTop, RoofCentreZ), 90f, depth, 0f);
            BuildUpperWall("Left", P(-UpperHalfX, FloorTop, RoofCentreZ), -90f, depth, 0f);

            // Corner posts: over the corners where two walls overlap,
            // standing a little proud of both faces.
            const float post = 0.25f;
            const float proud = 0.05f;

            for (int sideX = -1; sideX <= 1; sideX += 2) {
                for (int sideZ = -1; sideZ <= 1; sideZ += 2) {
                    float x = sideX * (UpperHalfX + proud - post * 0.5f);
                    float cornerZ = sideZ > 0 ? UpperFrontZ : UpperBackZ;
                    float z = cornerZ + sideZ * (proud - post * 0.5f);

                    // Each bows a little, its own way.
                    _timber.BentBeam(P(x, FloorTop, z), P(x, EaveY, z), post, post, Vector3.forward, Jitter(0.035f));
                }
            }
        }

        /// <summary>
        /// One upper wall, written in the wall's own coordinates: X along
        /// the wall, Y up from the upper floor, Z out of the wall, with
        /// z = 0 its outer face. origin is the middle of the foot of that
        /// face and yaw turns the wall to face the right way.
        ///
        /// Plaster panels round the window; then the frame on top, each
        /// kind of beam standing a different distance proud so no two
        /// overlapping beams share a face (which would flicker): the
        /// bottom and top plates most, then the rails, the studs, and the
        /// diagonal braces least. The window has a sill board and a pair
        /// of open shutters. Then the ledges: the bottom plate, the sill
        /// rail either side of the window, the window's own sill
        /// (mantled into), and the head rail.
        /// </summary>
        private static void BuildUpperWall(string name, Vector3 origin, float yaw, float width, float windowX)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            SetMatrix(Matrix4x4.TRS(origin, rotation, Vector3.one));

            float half = width * 0.5f;
            float windowLeft = windowX - WindowWidth * 0.5f;
            float windowRight = windowX + WindowWidth * 0.5f;
            float sillRailTop = WindowSill - 0.03f;

            // Plaster: each side of the window, below it and above it.
            _plaster.Box(P(-half, 0f, -UpperThickness), P(windowLeft, UpperHeight, 0f));
            _plaster.Box(P(windowRight, 0f, -UpperThickness), P(half, UpperHeight, 0f));
            _plaster.Box(P(windowLeft, 0f, -UpperThickness), P(windowRight, sillRailTop, 0f));
            _plaster.Box(P(windowLeft, WindowHead, -UpperThickness), P(windowRight, UpperHeight, 0f));

            // The sill board, wider than the wall is thick, and the
            // shutters folded back flat against the wall.
            const float sillOut = 0.08f;
            _planks.Box(P(windowLeft, sillRailTop, -UpperThickness - sillOut), P(windowRight, WindowSill, sillOut));
            _planks.Box(P(windowLeft - 0.62f, WindowSill, 0.045f), P(windowLeft - 0.12f, WindowHead, 0.075f));
            _planks.Box(P(windowRight + 0.12f, WindowSill, 0.045f), P(windowRight + 0.62f, WindowHead, 0.075f));

            // Plates and rails, the full width of the wall.
            const float plateTop = 0.18f;
            const float plateProud = 0.06f;
            const float railProud = 0.04f;
            const float railHeight = 0.15f;
            float headRailTop = WindowHead + railHeight;

            _timber.Box(P(-half, 0f, -0.04f), P(half, plateTop, plateProud));
            _timber.Box(P(-half, UpperHeight - 0.15f, -0.04f), P(half, UpperHeight, plateProud));
            _timber.Box(P(-half, sillRailTop - railHeight, -0.04f), P(half, sillRailTop, railProud));
            _timber.Box(P(-half, WindowHead, -0.04f), P(half, headRailTop, railProud));

            // Studs: one each side of the window (nearly straight, so
            // the opening keeps its width), and the rest spaced evenly
            // and wandering, leaving out any that would land on the
            // window or its shutters.
            Stud(windowLeft - 0.07f, plateTop, 0.012f);
            Stud(windowRight + 0.07f, plateTop, 0.012f);

            int bays = Mathf.Max(2, Mathf.RoundToInt(width / 1.3f));

            for (int i = 1; i < bays; i++) {
                float x = -half + i * width / bays;

                if (x < windowLeft - 0.7f || x > windowRight + 0.7f) {
                    Stud(x, plateTop, 0.06f);
                }
            }

            // Curved braces in the end bays, below and above the sill
            // rail, mirrored at the two ends.
            for (int side = -1; side <= 1; side += 2) {
                float corner = side * (half - 0.15f);
                float inward = side * (half - 0.95f);

                Brace(P(corner, sillRailTop - railHeight, 0f), P(inward, plateTop, 0f), side * 0.09f);
                Brace(P(corner, sillRailTop, 0f), P(inward, WindowHead, 0f), side * 0.09f);
            }

            // Ledges. Kept clear of the corners, where the next wall's
            // would overlap them.
            float ledgeWidth = width - 0.6f;
            Ledge($"{name} Jetty Beam", origin + rotation * P(0f, plateTop, plateProud), yaw, ledgeWidth, plateTop);
            Ledge($"{name} Head Rail", origin + rotation * P(0f, headRailTop, railProud), yaw, ledgeWidth, railHeight);

            float leftEnd = -half + 0.3f;
            float rightEnd = half - 0.3f;
            float gap = 0.05f;

            Ledge($"{name} Sill Rail L", origin + rotation * P((leftEnd + windowLeft - gap) * 0.5f, sillRailTop, railProud), yaw, windowLeft - gap - leftEnd, railHeight);
            Ledge($"{name} Sill Rail R", origin + rotation * P((windowRight + gap + rightEnd) * 0.5f, sillRailTop, railProud), yaw, rightEnd - windowRight - gap, railHeight);

            // The window: grabbed from outside or in, and mantled onto
            // the middle of the sill, crouched (the opening is lower than
            // a standing player).
            float sillDepth = UpperThickness + sillOut * 2f;
            Ledge(
                $"{name} Window",
                origin + rotation * P(windowX, WindowSill, sillOut),
                yaw,
                WindowWidth,
                0.2f,
                true,
                true,
                TestGeometry.TopCentreInset(sillDepth, TestGeometry.TightOverhang),
                sillDepth);

            SetMatrix(Matrix4x4.identity);
        }

        /// <summary>
        /// A stud on the wall being built, from the bottom plate to the
        /// top plate. Not quite upright, straight or the same width as
        /// the next: its foot and its head are each up to wander metres
        /// off x, and it bows by about as much.
        /// </summary>
        private static void Stud(float x, float plateTop, float wander)
        {
            Vector3 foot = P(x + Jitter(wander), plateTop, -0.005f);
            Vector3 head = P(x + Jitter(wander), UpperHeight - 0.15f, -0.005f);
            float width = 0.14f + Jitter(wander * 0.4f);

            _timber.BentBeam(foot, head, width, 0.07f, Vector3.forward, Jitter(wander * 0.6f));
        }

        /// <summary>
        /// A curved brace on the wall being built: its middle is bow
        /// metres off the straight line between its ends.
        /// </summary>
        private static void Brace(Vector3 from, Vector3 to, float bow)
        {
            Vector3 back = P(0f, 0f, -0.01f);
            _timber.BentBeam(from + back, to + back, 0.13f, 0.06f, Vector3.forward, bow);
        }

        // ---- Gables ----

        /// <summary>
        /// The three gable walls: one at each end of the main roof, and
        /// the cross gable's on the front.
        /// </summary>
        private static void BuildGables()
        {
            float mainRise = _ridgeY - EaveY;
            float crossRise = _gableRidgeY - EaveY;

            // Rail heights above the eaves: about a metre apart from the
            // wall's head rail below to the ridge above.
            float[] mainRails = { 0.8f, 1.7f, 2.48f };
            float[] crossRails = { 1.03f };

            BuildGable("Right Gable", P(UpperHalfX, EaveY, RoofCentreZ), 90f, RoofHalfSpan, mainRise, mainRails, false);
            BuildGable("Left Gable", P(-UpperHalfX, EaveY, RoofCentreZ), -90f, RoofHalfSpan, mainRise, mainRails, false);
            BuildGable("Front Gable", P(GableX, EaveY, UpperFrontZ), 0f, GableHalfWidth, crossRise, crossRails, true);
        }

        /// <summary>
        /// One gable: a wall of plaster under a roof's end, halfBase each
        /// side of its middle and rise high, in the same wall coordinates
        /// as BuildUpperWall(). Its two sides follow the roof's sweep, so
        /// it is a pointed arch rather than a triangle - built as a
        /// stack of level bands, each as wide as the roof is at its
        /// height. Framed with a tie beam along its foot, a curved
        /// rafter up each side, a king post up the middle and a level
        /// rail at each of railTops, each rail a ledge. A shuttered attic
        /// window sits just above the tie beam. tieIsLedge also makes
        /// the tie beam a ledge.
        /// </summary>
        private static void BuildGable(string name, Vector3 origin, float yaw, float halfBase, float rise, float[] railTops, bool tieIsLedge)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            SetMatrix(Matrix4x4.TRS(origin, rotation, Vector3.one));

            const float tieTop = 0.18f;
            const float tieProud = 0.05f;
            const float railProud = 0.04f;
            const float railHeight = 0.15f;
            const int bands = 6;

            // The rafters' middle lines, filled in band by band: a
            // little inside the plaster's edge, so most of each rafter
            // lies on the wall and the rest tucks under the roof.
            Vector3[] leftRafter = new Vector3[bands + 1];
            Vector3[] rightRafter = new Vector3[bands + 1];

            for (int i = 0; i <= bands; i++) {
                // 0 at the gable's foot, 1 at its point.
                float up = i / (float)bands;
                float halfWidth = RoofRun(halfBase, 1f - up);
                float rafterX = Mathf.Max(halfWidth - 0.05f, 0f);
                float rafterY = Mathf.Lerp(0.09f, rise - 0.08f, up);

                leftRafter[i] = P(-rafterX, rafterY, 0.005f);
                rightRafter[i] = P(rafterX, rafterY, 0.005f);

                if (i == bands) {
                    break;
                }

                float nextUp = (i + 1) / (float)bands;
                float nextHalfWidth = RoofRun(halfBase, 1f - nextUp);
                float y = rise * up;
                float nextY = rise * nextUp;

                // The top band comes to a point: three corners, not four.
                Vector3[] band = i == bands - 1
                    ? new[] { P(-halfWidth, y, 0f), P(halfWidth, y, 0f), P(0f, nextY, 0f) }
                    : new[] { P(-halfWidth, y, 0f), P(halfWidth, y, 0f), P(nextHalfWidth, nextY, 0f), P(-nextHalfWidth, nextY, 0f) };

                _plaster.Prism(band, P(0f, 0f, -UpperThickness), Vector3.right, Vector3.up);
            }

            _timber.Box(P(-halfBase, 0f, -0.04f), P(halfBase, tieTop, tieProud));

            // Rafters, a little prouder than everything else.
            _timber.PolyBeam(leftRafter, 0.16f, 0.11f, Vector3.forward);
            _timber.PolyBeam(rightRafter, 0.16f, 0.11f, Vector3.forward);

            _timber.Beam(P(0f, tieTop, -0.005f), P(0f, rise - 0.3f, -0.005f), 0.14f, 0.07f, Vector3.forward);

            for (int i = 0; i < railTops.Length; i++) {
                float top = railTops[i];

                // The gable narrows as it rises: half its width at this
                // height, less a little so the rail's ends stay inside.
                float halfWidth = RoofRun(halfBase, 1f - top / rise) - 0.06f;

                _timber.Box(P(-halfWidth, top - railHeight, -0.04f), P(halfWidth, top, railProud));
                Ledge($"{name} Rail {i + 1}", origin + rotation * P(0f, top, railProud), yaw, halfWidth * 2f - 0.1f, railHeight);
            }

            if (tieIsLedge) {
                Ledge($"{name} Tie Beam", origin + rotation * P(0f, tieTop, tieProud), yaw, halfBase * 2f - 0.6f, tieTop);
            }

            _planks.Box(P(-0.28f, tieTop + 0.08f, 0f), P(0.28f, tieTop + 0.42f, 0.075f));

            SetMatrix(Matrix4x4.identity);
        }

        // ---- Roof ----

        /// <summary>
        /// How far out from its ridge, level, a swept roof is at a point
        /// fraction of the way down it (0 = the ridge, 1 = the top of
        /// the wall, halfSpan out; more than 1 = the eaves). A straight
        /// roof would be halfSpan x fraction; taking a sine wave's hump
        /// off that holds the roof in near the ridge (steeper there) and
        /// lets it out again towards the wall (shallower), and past the
        /// wall the sine turns negative, which flares the eaves.
        /// </summary>
        private static float RoofRun(float halfSpan, float fraction)
        {
            return halfSpan * (fraction - RoofBow * Mathf.Sin(Mathf.PI * fraction));
        }

        /// <summary>
        /// A point of a swept roof's cross-section: x is how far out
        /// from the ridge it is, level, and y how far above the ridge
        /// (so negative). fraction is how far down the roof (as for
        /// RoofRun()), and above lifts the point that far off the roof's
        /// underside, square to the slope there - 0 for the underside,
        /// the slab's thickness for its top.
        /// </summary>
        private static Vector2 RoofSection(float halfSpan, float rise, float fraction, float above)
        {
            // How fast the roof goes out as it goes down: RoofRun()'s
            // slope. The roof's direction here is that far out for rise
            // down, and the direction square to it swaps the two.
            float runRate = halfSpan * (1f - RoofBow * Mathf.PI * Mathf.Cos(Mathf.PI * fraction));
            Vector2 square = new Vector2(rise, runRate).normalized;

            return new Vector2(RoofRun(halfSpan, fraction), -rise * fraction) + square * above;
        }

        /// <summary>
        /// One slope of a roof: a thin slab swept in a curve from the
        /// ridge down to reach (1 = the top of the wall, EaveReach = the
        /// eaves), in RoofPieces flat strips. ridgeMiddle is the middle
        /// of the ridge line (the slab's underside starts there), along
        /// is the way the ridge runs, outward is the level direction
        /// away from the ridge, halfSpan is how far out the wall is and
        /// rise how far below the ridge its top is. The tile texture
        /// runs along the ridge and down the curve - placed by distance
        /// down the slab's top, so the rows of tiles follow the sweep -
        /// which is what SmithyTextures paints the tiles for.
        /// </summary>
        private static void RoofSlab(Vector3 ridgeMiddle, Vector3 along, Vector3 outward, float length, float halfSpan, float rise, float reach, float thickness)
        {
            Vector3[] under = new Vector3[RoofPieces + 1];
            Vector3[] top = new Vector3[RoofPieces + 1];
            float[] distance = new float[RoofPieces + 1];

            for (int i = 0; i <= RoofPieces; i++) {
                float fraction = reach * i / RoofPieces;
                Vector2 low = RoofSection(halfSpan, rise, fraction, 0f);
                Vector2 high = RoofSection(halfSpan, rise, fraction, thickness);

                under[i] = ridgeMiddle + outward * low.x + Vector3.up * low.y;
                top[i] = ridgeMiddle + outward * high.x + Vector3.up * high.y;
                distance[i] = i == 0 ? 0f : distance[i - 1] + Vector3.Distance(top[i], top[i - 1]);
            }

            // The slab's two ends along the ridge, and where they are in
            // the texture.
            Vector3 halfAlong = along * (length * 0.5f);
            float uMiddle = Vector3.Dot(ridgeMiddle, along);
            float uLow = uMiddle - length * 0.5f;
            float uHigh = uMiddle + length * 0.5f;

            for (int i = 0; i < RoofPieces; i++) {
                Vector3 down = (top[i + 1] - top[i]).normalized;
                Vector3 underDown = (under[i + 1] - under[i]).normalized;

                // Square to this strip, on its upper side.
                Vector3 normal = Vector3.Cross(down, along).normalized;

                if (normal.y < 0f) {
                    normal = -normal;
                }

                _tiles.MappedPolygon(
                    normal,
                    along,
                    down,
                    new[] { top[i] - halfAlong, top[i] + halfAlong, top[i + 1] + halfAlong, top[i + 1] - halfAlong },
                    new[] { new Vector2(uLow, distance[i]), new Vector2(uHigh, distance[i]), new Vector2(uHigh, distance[i + 1]), new Vector2(uLow, distance[i + 1]) });

                _tiles.Polygon(-normal, along, underDown, under[i] - halfAlong, under[i] + halfAlong, under[i + 1] + halfAlong, under[i + 1] - halfAlong);

                // The slab's edge at each end of the ridge.
                _tiles.Polygon(along, down, normal, under[i] + halfAlong, under[i + 1] + halfAlong, top[i + 1] + halfAlong, top[i] + halfAlong);
                _tiles.Polygon(-along, down, normal, under[i] - halfAlong, under[i + 1] - halfAlong, top[i + 1] - halfAlong, top[i] - halfAlong);
            }

            // The slab's edge at the ridge, and at its foot.
            int last = RoofPieces;
            Vector3 ridgeDown = (top[1] - top[0]).normalized;
            Vector3 footDown = (top[last] - top[last - 1]).normalized;

            _tiles.Polygon(-ridgeDown, along, (top[0] - under[0]).normalized, under[0] - halfAlong, under[0] + halfAlong, top[0] + halfAlong, top[0] - halfAlong);
            _tiles.Polygon(footDown, along, (top[last] - under[last]).normalized, under[last] - halfAlong, under[last] + halfAlong, top[last] + halfAlong, top[last] - halfAlong);
        }

        /// <summary>
        /// The main roof (ridge along X), the cross gable's roof (ridge
        /// along Z, running back into the main roof's front slope), a
        /// flat cap along both ridges, the chimney, and the roof's
        /// ledges.
        ///
        /// Where two slopes cross, nothing is cut to fit: each is a plain
        /// swept slab and the part of one inside the other is simply
        /// hidden. The one exception is the main roof's front slope,
        /// which is in three pieces so that its eaves don't stick out
        /// through the front gable's wall.
        /// </summary>
        private static void BuildRoof()
        {
            float mainRise = _ridgeY - EaveY;
            float crossRise = _gableRidgeY - EaveY;

            float halfX = UpperHalfX + VergeOverhang;
            float gableLeft = GableX - GableHalfWidth;
            float gableRight = GableX + GableHalfWidth;

            RoofSlab(P(0f, _ridgeY, RoofCentreZ), Vector3.right, Vector3.back, halfX * 2f, RoofHalfSpan, mainRise, EaveReach, RoofThickness);

            // Front slope: left of the gable, behind the gable (stopping
            // at the wall, no eaves), right of the gable.
            RoofSlab(P((-halfX + gableLeft) * 0.5f, _ridgeY, RoofCentreZ), Vector3.right, Vector3.forward, gableLeft + halfX, RoofHalfSpan, mainRise, EaveReach, RoofThickness);
            RoofSlab(P(GableX, _ridgeY, RoofCentreZ), Vector3.right, Vector3.forward, GableHalfWidth * 2f, RoofHalfSpan, mainRise, 1f, RoofThickness);
            RoofSlab(P((gableRight + halfX) * 0.5f, _ridgeY, RoofCentreZ), Vector3.right, Vector3.forward, halfX - gableRight, RoofHalfSpan, mainRise, EaveReach, RoofThickness);

            // The cap starts just under the ridge line, so it closes the
            // gap between the tops of the two slabs.
            float capBottom = _ridgeY - 0.01f;
            float capTop = _ridgeY + RidgeCapTop;
            _tiles.Box(P(-halfX, capBottom, RoofCentreZ - RidgeCapHalfWidth), P(halfX, capTop, RoofCentreZ + RidgeCapHalfWidth));

            // The cross gable's roof, from its verge in front of the
            // gable wall back to a little past where its ridge meets the
            // main roof's front slope - which is where that slope is as
            // high as the cross ridge.
            float crossFront = UpperFrontZ + VergeOverhang;
            float crossBack = RoofCentreZ + RoofRun(RoofHalfSpan, 1f - crossRise / mainRise) - 0.25f;
            float crossLength = crossFront - crossBack;
            Vector3 crossRidge = P(GableX, _gableRidgeY, (crossFront + crossBack) * 0.5f);

            RoofSlab(crossRidge, Vector3.forward, Vector3.right, crossLength, GableHalfWidth, crossRise, EaveReach, RoofThickness);
            RoofSlab(crossRidge, Vector3.forward, Vector3.left, crossLength, GableHalfWidth, crossRise, EaveReach, RoofThickness);

            float crossCapTop = _gableRidgeY + RidgeCapTop;
            _tiles.Box(
                P(GableX - RidgeCapHalfWidth, _gableRidgeY - 0.01f, crossBack + 0.1f),
                P(GableX + RidgeCapHalfWidth, crossCapTop, crossFront));

            BuildChimney();

            // Ledges. The main ridge: along its length from either side
            // (how the lower ridge leads onto it), and end-on from each
            // gable (how the gable's rails lead onto it). All mantled.
            float capWidth = RidgeCapHalfWidth * 2f;
            float topCentre = TestGeometry.TopCentreInset(capWidth, TestGeometry.TightOverhang);

            Ledge("Main Ridge", P(0f, capTop, RoofCentreZ + RidgeCapHalfWidth), 0f, halfX * 2f - 0.8f, 0.14f, true, false, topCentre, capWidth);
            Ledge("Main Ridge Right End", P(halfX, capTop, RoofCentreZ), 90f, capWidth, 0.14f, true);
            Ledge("Main Ridge Left End", P(-halfX, capTop, RoofCentreZ), -90f, capWidth, 0.14f, true);
            Ledge("Front Ridge End", P(GableX, crossCapTop, crossFront), 0f, capWidth, 0.14f, true);

            // The eaves, to hang from: the slab's top outer edge, which
            // is the foot of the sweep, the slab's thickness up off it.
            Vector2 eave = RoofSection(RoofHalfSpan, mainRise, EaveReach, RoofThickness);
            float eaveY = _ridgeY + eave.y;
            float eaveOut = eave.x;

            Ledge("Front Eaves", P((gableRight + 0.4f + halfX) * 0.5f, eaveY, RoofCentreZ + eaveOut), 0f, halfX - gableRight - 0.6f, RoofThickness);
            Ledge("Back Eaves", P(0f, eaveY, RoofCentreZ - eaveOut), 180f, halfX * 2f - 0.4f, RoofThickness);
        }

        /// <summary>
        /// The chimney, up through the back slope: four blocks of stone
        /// stacked askew - each narrower than the one below, slid a
        /// little off it and turned a few degrees - then a capstone and
        /// two clay pots.
        /// </summary>
        private static void BuildChimney()
        {
            // How far each block is slid along X (and half as far back
            // along Z), and how far it's turned.
            float[] slide = { 0f, 0.04f, -0.02f, 0.05f };
            float[] turn = { 0f, 6f, -5f, 4f };

            Vector3 foot = P(1.85f, EaveY, -1.25f);
            float height = _ridgeY + 0.7f - EaveY;
            float blockHeight = height / slide.Length;

            for (int i = 0; i < slide.Length; i++) {
                float width = 0.72f - i * 0.04f;
                Quaternion turned = Quaternion.Euler(0f, turn[i], 0f);
                Vector3 centre = foot + P(slide[i], (i + 0.5f) * blockHeight, -slide[i] * 0.5f);

                _stone.OrientedBox(centre, turned * Vector3.right, Vector3.up, turned * Vector3.forward, P(width, blockHeight, width));
            }

            Vector3 top = foot + P(0.05f, height, -0.025f);
            Quaternion capTurn = Quaternion.Euler(0f, 9f, 0f);
            _stone.OrientedBox(top + Vector3.up * 0.06f, capTurn * Vector3.right, Vector3.up, capTurn * Vector3.forward, P(0.8f, 0.12f, 0.8f));

            for (int side = -1; side <= 1; side += 2) {
                Vector3 pot = top + P(side * 0.16f, 0.12f, side * 0.05f);
                _tiles.Cylinder(pot, new[] { 0f, 0.3f + side * 0.04f }, new[] { 0.11f, 0.08f }, 8, true, false);
            }
        }

        /// <summary>
        /// The lean-to over the forge: a shallow tiled slope out from the
        /// front wall, on a beam and two brackets. Shallow enough to
        /// stand on, and its front edge can be mantled onto - from there
        /// the front window is in reach.
        /// </summary>
        private static void BuildAwning()
        {
            const float left = -2.9f;
            const float right = 0.1f;

            float sin = Mathf.Sin(AwningPitch * Mathf.Deg2Rad);
            float cos = Mathf.Cos(AwningPitch * Mathf.Deg2Rad);
            float slopeLength = AwningRun / cos;
            float middle = (left + right) * 0.5f;

            // A plain flat slab (the awning is stood on, so it isn't
            // swept): lying on the line from the wall down to its front
            // edge, with the tiles running along the wall and down it.
            Vector3 down = P(0f, -sin, cos);
            Vector3 normal = P(0f, cos, sin);
            Vector3 centre = P(middle, AwningWallY, StoneFrontZ) + down * (slopeLength * 0.5f) + normal * (AwningThickness * 0.5f);
            _tiles.OrientedBox(centre, Vector3.right, normal, down, P(right - left, AwningThickness, slopeLength));

            // The beam under the outer edge: its top just under the slab
            // there (the slab's underside drops by the tangent of the
            // pitch for every metre out).
            const float beamZ = 3.7f;
            float beamTop = AwningWallY - (beamZ - StoneFrontZ) * sin / cos - 0.015f;
            _timber.Box(P(left, beamTop - 0.1f, beamZ - 0.06f), P(right, beamTop, beamZ + 0.06f));

            // A post on the wall and a diagonal strut out to the beam,
            // near each end.
            for (int i = 0; i < 2; i++) {
                float x = i == 0 ? left + 0.15f : right - 0.15f;

                _timber.Beam(P(x, 1.25f, StoneFrontZ + 0.04f), P(x, 2.2f, StoneFrontZ + 0.04f), 0.09f, 0.08f, Vector3.forward);
                _timber.Beam(P(x, 1.35f, StoneFrontZ + 0.08f), P(x, beamTop - 0.08f, beamZ - 0.04f), 0.09f, 0.09f, Vector3.right);
            }

            // The front edge's lip: the slab's top outer corner.
            float lipY = AwningWallY - sin * slopeLength + cos * AwningThickness;
            float lipZ = StoneFrontZ + cos * slopeLength + sin * AwningThickness;

            GameObject edge = Ledge("Awning", P(middle, lipY, lipZ), 0f, right - left - 0.2f, AwningThickness, true);

            // A ledge's landing is level with its lip, but this roof
            // rises behind its lip: lift the landing by how much the
            // slope climbs over the inset (and a little more), so the
            // feet land on the tiles, not in them.
            SerializedObject settings = new(edge.GetComponent<ClimbableEdge>());
            SerializedProperty mantlePoint = settings.FindProperty("mantlePoint");
            mantlePoint.vector3Value += Vector3.up * (MantleInset * sin / cos + 0.03f);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Ladders ----

        /// <summary>
        /// The ladder leaning against the right side, up to just under
        /// the side window (climb it, then take the window's sill); and
        /// the ladder inside, straight up the back wall through the
        /// hatch, which mantles onto the upper floor beside the hatch.
        /// </summary>
        private static void BuildLadders()
        {
            // Outside: its foot out from the wall, its top resting just
            // clear of the sill board. The lean is worked out from the
            // two ends.
            Vector3 foot = P(3.9f, GroundY, RoofCentreZ);
            Vector3 top = P(UpperHalfX + 0.12f, FloorTop + WindowSill - 0.13f, RoofCentreZ);
            float lean = Mathf.Atan2(foot.x - top.x, top.y - foot.y) * Mathf.Rad2Deg;

            // Turned to face +X (out from the right wall), then tipped
            // back so its top leans in towards the wall.
            Quaternion leaning = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(-lean, 0f, 0f);
            BuildLadder("Outside Ladder", foot, leaning, Vector3.Distance(foot, top), false, Vector3.zero);

            // Inside: 0.15m out from the back wall's inner face, in the
            // hatch. Its mantle lands 0.9m to the left, on the floor
            // beside the hatch.
            float height = FloorTop - GroundY;
            Vector3 insideFoot = P(1.85f, GroundY, StoneBackZ + StoneThickness + 0.15f);
            BuildLadder("Inside Ladder", insideFoot, Quaternion.identity, height, true, insideFoot + P(-0.9f, height, 0.3f));
        }

        /// <summary>
        /// A ladder: the Ladder component on one trigger grab box (on
        /// Climbable), and its two rails and its rungs in the timber
        /// mesh, well inside the box. The ladder's own Y runs up it, so a
        /// leaning one is just turned. Rungs at exactly the heights the
        /// Ladder snaps hands to. landing is where a mantle off its top
        /// puts the feet.
        ///
        /// foot, rotation, length and landing are given as the building
        /// is written, square and upright. A ladder has to stay straight
        /// (its grab box and its rungs' heights are), so it isn't warped
        /// point by point like the rest: its foot and its top are, and a
        /// straight ladder is built between where the two end up.
        /// </summary>
        private static void BuildLadder(string name, Vector3 foot, Quaternion rotation, float length, bool isMantleable, Vector3 landing)
        {
            const float width = 0.5f;
            const float railInset = 0.03f;

            Vector3 top = foot + rotation * Vector3.up * length;
            Vector3 warpedTop = Warp(top);
            WarpAt((foot.y + top.y) * 0.5f, out _, out float twist);

            // The ladder's new directions: up it from foot to top, the
            // way it faces turned with the building, and then facing
            // made exactly square to up.
            Vector3 up = (warpedTop - Warp(foot)).normalized;
            Vector3 facing = Quaternion.Euler(0f, twist, 0f) * (rotation * Vector3.forward);
            Vector3 across = Vector3.Cross(up, facing).normalized;

            foot = Warp(foot);
            rotation = Quaternion.LookRotation(Vector3.Cross(across, up), up);
            length = Vector3.Distance(foot, warpedTop);

            // The landing, moved the same way, then written in the
            // ladder's own coordinates, as the Ladder component wants.
            Vector3 mantlePoint = isMantleable ? Quaternion.Inverse(rotation) * (Warp(landing) - foot) : Vector3.zero;

            GameObject ladder = new(name) { layer = _climbableLayer };
            ladder.transform.SetParent(_climbables, false);
            ladder.transform.SetLocalPositionAndRotation(foot, rotation);

            // Trigger first, so the Ladder's setup check never sees a
            // solid box.
            BoxCollider box = ladder.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = P(0f, length * 0.5f, 0f);
            box.size = P(width, length, 0.2f);

            SerializedObject settings = new(ladder.AddComponent<Ladder>());
            settings.FindProperty("snapProfile").objectReferenceValue = _ladderProfile;
            settings.FindProperty("firstRungHeight").floatValue = FirstRungHeight;
            settings.FindProperty("rungSpacing").floatValue = RungSpacing;
            settings.FindProperty("isMantleable").boolValue = isMantleable;
            settings.FindProperty("mantlePoint").vector3Value = mantlePoint;
            settings.ApplyModifiedPropertiesWithoutUndo();

            // Already placed where the warp puts it, so the warp is off
            // while its timber is added.
            SetWarp(null);
            SetMatrix(Matrix4x4.TRS(foot, rotation, Vector3.one));

            float railX = width * 0.5f - railInset;

            for (int side = -1; side <= 1; side += 2) {
                _timber.Beam(P(side * railX, 0f, 0f), P(side * railX, length, 0f), 0.045f, 0.045f, Vector3.forward);
            }

            int rungCount = Mathf.FloorToInt((length - FirstRungHeight) / RungSpacing + 0.001f) + 1;

            for (int i = 0; i < rungCount; i++) {
                float y = FirstRungHeight + i * RungSpacing;
                _timber.Beam(P(-railX, y, 0f), P(railX, y, 0f), 0.035f, 0.035f, Vector3.forward);
            }

            SetMatrix(Matrix4x4.identity);
            SetWarp(Warp);
        }

        // ---- Props ----

        /// <summary>
        /// What stands outside: barrels, the anvil on its stump, a
        /// workbench with a hammer on it, and a rail of tongs on the
        /// wall. All part of the building's meshes - none can be picked
        /// up.
        /// </summary>
        private static void BuildProps()
        {
            // Barrels: x, z, radius, height.
            Barrel(3.75f, -1.3f, 0.4f, 1.05f);
            Barrel(4.2f, 1.5f, 0.36f, 0.95f);
            Barrel(3.45f, 3.25f, 0.33f, 0.85f);
            Barrel(-3.6f, 3.3f, 0.32f, 0.8f);
            Barrel(-4f, 2.55f, 0.22f, 0.5f);
            Barrel(-0.55f, 3.45f, 0.26f, 0.6f);

            // The anvil: a stump, then the anvil in four blocks - foot,
            // waist, face and horn.
            Vector3 anvil = P(-1.5f, GroundY, 3.2f);
            _timber.Cylinder(anvil, new[] { 0f, 0.45f }, new[] { 0.28f, 0.26f }, 10, true, false);

            Vector3 top = anvil + Vector3.up * 0.45f;
            _iron.Box(top + P(-0.17f, 0f, -0.12f), top + P(0.17f, 0.07f, 0.12f));
            _iron.Box(top + P(-0.09f, 0.07f, -0.07f), top + P(0.09f, 0.19f, 0.07f));
            _iron.Box(top + P(-0.22f, 0.19f, -0.085f), top + P(0.22f, 0.29f, 0.085f));
            _iron.Box(top + P(0.22f, 0.22f, -0.045f), top + P(0.42f, 0.28f, 0.045f));

            // The workbench: a plank top on four legs, right of the door.
            const float benchLeft = 1.5f;
            const float benchRight = 2.7f;
            const float benchBack = 3f;
            const float benchFront = 3.5f;
            const float benchTop = 0.84f;

            _planks.Box(P(benchLeft, benchTop - 0.06f, benchBack), P(benchRight, benchTop, benchFront));

            for (int i = 0; i < 4; i++) {
                float x = i < 2 ? benchLeft + 0.08f : benchRight - 0.08f;
                float z = i % 2 == 0 ? benchBack + 0.08f : benchFront - 0.08f;

                _timber.Beam(P(x, GroundY, z), P(x, benchTop - 0.06f, z), 0.07f, 0.07f, Vector3.forward);
            }

            // A hammer on the bench: an iron head on a wooden handle.
            _iron.Box(P(1.8f, benchTop, 3.2f), P(1.9f, benchTop + 0.05f, 3.25f));
            _timber.Box(P(1.84f, benchTop + 0.012f, 3.25f), P(1.865f, benchTop + 0.037f, 3.53f));

            // Tongs and pokers hanging from a rail between the forge and
            // the door.
            _timber.Box(P(-0.35f, 1.6f, StoneFrontZ), P(0.2f, 1.66f, StoneFrontZ + 0.04f));

            for (int i = 0; i < 3; i++) {
                float x = -0.25f + i * 0.15f;
                _iron.Box(P(x - 0.01f, 1.1f, StoneFrontZ + 0.02f), P(x + 0.01f, 1.6f, StoneFrontZ + 0.04f));
            }
        }

        /// <summary>
        /// A barrel standing on the paving: twelve flat staves, wider in
        /// the middle than at the ends, a flat top, and four iron hoops.
        /// </summary>
        private static void Barrel(float x, float z, float radius, float height)
        {
            const int staves = 12;
            const float endScale = 0.85f;

            Vector3 foot = P(x, GroundY, z);

            _planks.Cylinder(
                foot,
                new[] { 0f, height * 0.5f, height },
                new[] { radius * endScale, radius, radius * endScale },
                staves,
                true,
                false);

            // Hoops: thin bands just outside the staves, at four heights.
            // The barrel's radius at each height runs from the end's to
            // the middle's and back.
            float[] hoops = { 0.1f, 0.36f, 0.64f, 0.9f };

            for (int i = 0; i < hoops.Length; i++) {
                float fraction = hoops[i];
                float bulge = 1f - Mathf.Abs(fraction * 2f - 1f);
                float hoopRadius = radius * Mathf.Lerp(endScale, 1f, bulge) + 0.01f;

                _iron.Cylinder(
                    foot + Vector3.up * (fraction * height - 0.02f),
                    new[] { 0f, 0.04f },
                    new[] { hoopRadius, hoopRadius },
                    staves,
                    true,
                    true);
            }
        }
    }
}
