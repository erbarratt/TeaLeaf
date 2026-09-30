using Interaction;
using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Build Locomotion Test Course) that
    /// builds a greybox course for every traversal case in Phase 1: ledges
    /// at chest height and above the head, a mantle into a low-clearance
    /// shelf, a 4m ladder and rope up one tower, walking and sprinting jump
    /// gaps, crates to jump onto, and a low ceiling to jump under. Sizes are
    /// worked out from the player's settings (1.6m capsule, 0.3m step offset,
    /// 0.5m jump, 3 / 4.5 m/s walk / sprint, 1m crouch) - see each piece.
    /// Built from code so it can be rebuilt identically after changes, and
    /// undone with Ctrl+Z like any edit.
    ///
    /// Solid geometry is plain primitives on the Environment layer. Every
    /// climbable is set up the way its own setup checks expect: a separate
    /// trigger grab volume on the Climbable layer, a little larger than the
    /// solid thing it belongs to, so hand rays hit it first. Everything sits
    /// under one root object that can be moved as a whole. In the root's own
    /// frame the pieces stand in a row along X with their fronts at z = 0,
    /// facing +Z - the climbables' "out towards the player" direction - and
    /// the root is turned to face the spawn point.
    ///
    /// In an Editor folder, so it's compiled into the editor-only assembly
    /// and never ends up in a build.
    /// </summary>
    public static class LocomotionTestCourse
    {
        private const string RootName = "Locomotion Test Course";
        private const string EnvironmentLayerName = "Environment";
        private const string ClimbableLayerName = "Climbable";

        private const string LedgeProfilePath = "Assets/Data/LedgeGrip.asset";
        private const string LadderProfilePath = "Assets/Data/LadderRung.asset";
        private const string RopeProfilePath = "Assets/Data/RopeGrip.asset";

        // 10m in front of the spawn, turned round so the pieces' fronts face
        // back towards it. The row runs from local x = -1 to 25, so local
        // x = 12 (the middle) lands straight ahead of the spawn.
        private static readonly Vector3 _rootPosition = new(12f, 0f, 10f);
        private static readonly Quaternion _rootRotation = Quaternion.Euler(0f, 180f, 0f);

        // How far in from the lip a mantle lands the feet - the same as
        // ClimbableEdge's own "Reset Mantle Point" default.
        private const float MantleInset = 0.4f;

        // Ladder rungs, matching the Ladder component's own defaults.
        private const float FirstRungHeight = 0.3f;
        private const float RungSpacing = 0.3f;

        /// <summary>
        /// Builds the course, replacing an existing one (after asking).
        /// </summary>
        [MenuItem("TeaLeaf/Build Locomotion Test Course")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer(EnvironmentLayerName);
            int climbable = LayerMask.NameToLayer(ClimbableLayerName);

            if (environment < 0 || climbable < 0) {
                Debug.LogError($"LocomotionTestCourse: the '{EnvironmentLayerName}' and '{ClimbableLayerName}' layers must exist.");
                return;
            }

            HandSnapProfile ledgeProfile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(LedgeProfilePath);
            HandSnapProfile ladderProfile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(LadderProfilePath);
            HandSnapProfile ropeProfile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(RopeProfilePath);

            if (ledgeProfile == null || ladderProfile == null || ropeProfile == null) {
                Debug.LogError($"LocomotionTestCourse: couldn't load the hand snap profiles ({LedgeProfilePath}, {LadderProfilePath}, {RopeProfilePath}).");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog(
                        "Locomotion Test Course",
                        "A test course already exists in the scene. Replace it?",
                        "Replace",
                        "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(_rootPosition, _rootRotation);
            Undo.RegisterCreatedObjectUndo(root, "Build Locomotion Test Course");
            Transform parent = root.transform;

            // 1. Chest-height ledge (1.3m): grab and mantle from standing.
            Block("Chest Ledge", parent, 0f, 2f, 1.3f, 1.5f, environment);
            Edge("Chest Ledge Edge", parent, 0f, 2f, 1.3f, 0f, true, false, ledgeProfile, climbable);

            // 2. Above-head ledge (2.3m): reach up (or jump) to grab, pull
            //    up, then mantle.
            Block("High Ledge", parent, 3.5f, 2f, 2.3f, 1.5f, environment);
            Edge("High Ledge Edge", parent, 3.5f, 2f, 2.3f, 0f, true, false, ledgeProfile, climbable);

            // 3. Low-clearance shelf: top at 1.1m with a ceiling 1.2m above
            //    it - too low to stand in (1.6m) but tall enough for the 1m
            //    crouch - so the edge is set to end the mantle crouched. Side
            //    walls hold the ceiling up and close the shelf off.
            const float shelfX = 7f;
            const float shelfTop = 1.1f;
            const float shelfClearance = 1.2f;
            const float shelfWidth = 2f;
            const float shelfDepth = 2f;
            const float slabThickness = 0.2f;
            Block("Low Shelf", parent, shelfX, shelfWidth, shelfTop, shelfDepth, environment);
            TestGeometry.Box("Low Shelf Ceiling", parent,
                new Vector3(shelfX, shelfTop + shelfClearance + slabThickness * 0.5f, -shelfDepth * 0.5f),
                new Vector3(shelfWidth + 0.4f, slabThickness, shelfDepth), environment);

            for (int side = -1; side <= 1; side += 2) {
                TestGeometry.Box("Low Shelf Side", parent,
                    new Vector3(shelfX + side * (shelfWidth * 0.5f + 0.1f), (shelfTop + shelfClearance) * 0.5f, -shelfDepth * 0.5f),
                    new Vector3(0.2f, shelfTop + shelfClearance, shelfDepth), environment);
            }

            Edge("Low Shelf Edge", parent, shelfX, shelfWidth, shelfTop, 0f, true, true, ledgeProfile, climbable);

            // 4 and 5. Tower, 4m: a ladder up the left half of its front and
            //    a rope in front of the right half, both topping out at one
            //    mantleable edge along the whole lip.
            const float towerX = 11f;
            const float towerWidth = 3f;
            const float towerHeight = 4f;
            Block("Tower", parent, towerX, towerWidth, towerHeight, 2f, environment);
            Edge("Tower Edge", parent, towerX, towerWidth, towerHeight, 0f, true, false, ledgeProfile, climbable);
            BuildLadder(parent, towerX - 0.75f, towerHeight, ladderProfile, environment, climbable);
            BuildRope(parent, towerX + 0.75f, ropeProfile, environment, climbable);

            // 6. Jump gaps: a 20 degree ramp up to three 1m-high platforms in
            //    a line running away from the front (-Z). A 0.5m jump is about
            //    0.64s in the air, so roughly 1.9m at walking speed and 2.9m
            //    sprinting: the 1.5m gap takes a walking jump, the 2.5m gap
            //    needs a sprint. Missing a jump drops to the floor - walk back
            //    round to the ramp.
            BuildJumpGaps(parent, 16f, environment);

            // 7. Crates: 0.45m is above the 0.3m step offset (can't just walk
            //    up) but inside the 0.5m jump; 0.6m is the control - it
            //    shouldn't be jumpable.
            TestGeometry.Box("Crate 0.45m", parent, new Vector3(19.5f, 0.225f, -0.4f), new Vector3(0.8f, 0.45f, 0.8f), environment);
            TestGeometry.Box("Crate 0.6m", parent, new Vector3(21f, 0.3f, -0.4f), new Vector3(0.8f, 0.6f, 0.8f), environment);

            // 8. Low ceiling: a slab 2m up on four posts, above a standing
            //    head but inside a jump - jumping underneath must stop the
            //    rise cleanly when the head hits (CollisionFlags.Above).
            const float ceilingX = 24f;
            const float ceilingHeight = 2f;
            TestGeometry.Box("Low Ceiling", parent, new Vector3(ceilingX, ceilingHeight + slabThickness * 0.5f, -1f), new Vector3(2f, slabThickness, 2f), environment);

            for (int x = -1; x <= 1; x += 2) {
                for (int z = -1; z <= 1; z += 2) {
                    TestGeometry.Box("Low Ceiling Post", parent,
                        new Vector3(ceilingX + x * 0.95f, ceilingHeight * 0.5f, -1f + z * 0.95f),
                        new Vector3(0.1f, ceilingHeight, 0.1f), environment);
                }
            }

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// The 4m ladder against the tower's front face: rails and rungs as
        /// solid Environment geometry, inside one Ladder grab volume. The
        /// rung line sits 0.15m out from the wall; the grab box is 0.2m deep
        /// around it, so its front face is well in front of the rungs.
        /// </summary>
        private static void BuildLadder(Transform parent, float x, float height, HandSnapProfile profile, int environment, int climbable)
        {
            const float standoff = 0.15f;
            const float width = 0.5f;
            const float railInset = 0.03f;

            // Grab volume first, set up as a trigger on Climbable before the
            // Ladder is added, so nothing about it looks wrong at any point.
            GameObject ladder = new("Ladder");
            ladder.layer = climbable;
            ladder.transform.SetParent(parent, false);
            ladder.transform.localPosition = new Vector3(x, 0f, standoff);

            BoxCollider box = ladder.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, height * 0.5f, 0f);
            box.size = new Vector3(width, height, 0.2f);

            SerializedObject settings = new(ladder.AddComponent<Ladder>());
            settings.FindProperty("snapProfile").objectReferenceValue = profile;
            settings.FindProperty("firstRungHeight").floatValue = FirstRungHeight;
            settings.FindProperty("rungSpacing").floatValue = RungSpacing;
            settings.ApplyModifiedPropertiesWithoutUndo();

            // The visible ladder, as children, inside the grab volume. Rungs
            // at exactly the heights the Ladder snaps to (the same maths as
            // Ladder.MeasureRungs()).
            for (int side = -1; side <= 1; side += 2) {
                TestGeometry.Box("Rail", ladder.transform,
                    new Vector3(side * (width * 0.5f - railInset), height * 0.5f, 0f),
                    new Vector3(0.04f, height, 0.04f), environment);
            }

            int rungCount = Mathf.FloorToInt((height - FirstRungHeight) / RungSpacing + 0.001f) + 1;

            for (int i = 0; i < rungCount; i++) {
                TestGeometry.Box("Rung", ladder.transform,
                    new Vector3(0f, FirstRungHeight + i * RungSpacing, 0f),
                    new Vector3(width - railInset * 2f, 0.03f, 0.03f), environment);
            }
        }

        /// <summary>
        /// A rope hanging 0.4m in front of the tower's face from a short beam
        /// just below the top, down to just off the floor. The ClimbableRope
        /// makes its own trigger grab capsule (grabRadius 0.08m); the visible
        /// rope is a thin solid cylinder inside it (0.02m radius, well inside
        /// the grab radius so the rope's setup check stays quiet).
        /// </summary>
        private static void BuildRope(Transform parent, float x, HandSnapProfile profile, int environment, int climbable)
        {
            const float top = 3.8f;
            const float outFromWall = 0.4f;
            const float length = 3.7f;

            TestGeometry.Box("Rope Beam", parent, new Vector3(x, top + 0.05f, outFromWall * 0.5f + 0.05f), new Vector3(0.1f, 0.1f, outFromWall + 0.1f), environment);

            GameObject rope = new("Rope");
            rope.layer = climbable;
            rope.transform.SetParent(parent, false);
            rope.transform.localPosition = new Vector3(x, top, outFromWall);
            ClimbableRope ropeComponent = rope.AddComponent<ClimbableRope>();

            // A Unity cylinder is 2m tall at scale 1, centred on its origin.
            GameObject visible = TestGeometry.Primitive(PrimitiveType.Cylinder, "Rope Visible", rope.transform, environment);
            visible.transform.localPosition = new Vector3(0f, -length * 0.5f, 0f);
            visible.transform.localScale = new Vector3(0.04f, length * 0.5f, 0.04f);

            // Applying the length runs the rope's OnValidate(), which fits its
            // grab capsule to it (and checks the cylinder added above).
            SerializedObject settings = new(ropeComponent);
            settings.FindProperty("snapProfile").objectReferenceValue = profile;
            settings.FindProperty("length").floatValue = length;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A ramp up to three 1m platforms in a line along -Z, with a 1.5m
        /// and then a 2.5m gap between them - see Build().
        /// </summary>
        private static void BuildJumpGaps(Transform parent, float x, int environment)
        {
            const float height = 1f;
            const float width = 1.5f;
            const float platformDepth = 2f;
            const float rampAngle = 20f;
            const float rampThickness = 0.1f;

            // The ramp's top surface runs from the floor at the front up to
            // the first platform's front edge (z = 0). Rotating a box by a
            // positive angle about X tips its +Z end down, so it rises
            // towards -Z. The box is pushed down by half its thickness along
            // its own up, so its top surface (not its middle) is on that line.
            float rampRun = height / Mathf.Tan(rampAngle * Mathf.Deg2Rad);
            float rampLength = height / Mathf.Sin(rampAngle * Mathf.Deg2Rad) + 0.1f;
            Quaternion rampRotation = Quaternion.Euler(rampAngle, 0f, 0f);
            Vector3 rampTopMiddle = new(x, height * 0.5f, rampRun * 0.5f);
            GameObject ramp = TestGeometry.Box("Jump Ramp", parent, rampTopMiddle - rampRotation * Vector3.up * (rampThickness * 0.5f),
                new Vector3(width, rampThickness, rampLength), environment);
            ramp.transform.localRotation = rampRotation;

            float front = 0f;
            float[] gapsAfter = { 1.5f, 2.5f, 0f };

            for (int i = 0; i < gapsAfter.Length; i++) {
                TestGeometry.Box($"Jump Platform {i + 1}", parent, new Vector3(x, height * 0.5f, front - platformDepth * 0.5f),
                    new Vector3(width, height, platformDepth), environment);
                front -= platformDepth + gapsAfter[i];
            }
        }

        /// <summary>
        /// A solid block standing on the floor, centred on x, with its front
        /// face at z = 0 and running back (-Z) for depth.
        /// </summary>
        private static void Block(string name, Transform parent, float x, float width, float height, float depth, int layer)
        {
            TestGeometry.Box(name, parent, new Vector3(x, height * 0.5f, -depth * 0.5f), new Vector3(width, height, depth), layer);
        }

        /// <summary>
        /// A ClimbableEdge along the lip of a block whose top is at top and
        /// front face at front, facing +Z (see TestGeometry.Edge()). The
        /// mantle point goes MantleInset in from the lip - where
        /// ClimbableEdge's "Reset Mantle Point" would put it.
        /// </summary>
        private static void Edge(
            string name,
            Transform parent,
            float x,
            float width,
            float top,
            float front,
            bool isMantleable,
            bool endsCrouched,
            HandSnapProfile profile,
            int climbable)
        {
            TestGeometry.Edge(name, parent, new Vector3(x, top, front), 0f, width, isMantleable, endsCrouched, MantleInset, profile, climbable);
        }
    }
}
