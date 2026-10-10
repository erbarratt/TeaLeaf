using Core;
using Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace AI
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Set Up Test Guard) that puts a
    /// greybox guard in the scene and bakes the NavMesh it walks on.
    ///
    /// With a Sound Test House in the scene, the guard patrols the route
    /// the house lays out (Bedroom, Landing, stairs, Hall, Kitchen, and
    /// back). Without one, the guard stands 4m ahead of the main camera.
    ///
    /// The guard is built the way Guard expects: a root on the Guard
    /// layer with a NavMeshAgent, a kinematic Rigidbody and a capsule
    /// collider; a Head object on the GuardHead layer (its collider is
    /// what the blackjack hits, and its forward is the way the guard
    /// looks); and a Visual object holding the shapes that are drawn.
    ///
    /// The NavMesh is baked from every collider on the Environment layer
    /// by a NavMeshSurface on a NavMesh object at the scene root, and
    /// saved as an asset beside the scene. Run it again after changing
    /// level geometry. Doors aren't part of it: a guard walks through a
    /// shut door.
    ///
    /// Also adds the player's hand tools if they're missing (the
    /// blackjack is what knocks a guard out), and gives the blackjack
    /// its thud.
    ///
    /// Can be undone with Ctrl+Z, apart from the NavMesh asset. In an
    /// Editor folder, so it never ends up in a build.
    /// </summary>
    public static class GuardTestSetup
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string NavMeshPath = "Assets/Scenes/Main-NavMesh.asset";

        private const string HouseName = "Sound Test House";
        private const string HousePathName = "Guard Path";

        private const string GuardName = "Test Guard";
        private const string RouteName = "Test Guard Route";
        private const string NavMeshName = "NavMesh";

        // The guard's size, in metres. The NavMesh agent settings
        // (ProjectSettings/NavMeshAreas.asset) match.
        private const float Radius = 0.3f;
        private const float Height = 1.8f;
        private const float HeadHeight = 1.65f;
        private const float HeadRadius = 0.14f;

        [MenuItem("TeaLeaf/Set Up Test Guard")]
        private static void Build()
        {
            int guardLayer = LayerMask.NameToLayer("Guard");
            int headLayer = LayerMask.NameToLayer("GuardHead");
            int environment = LayerMask.NameToLayer("Environment");

            if (guardLayer < 0 || headLayer < 0 || environment < 0) {
                Debug.LogError("GuardTestSetup: the 'Guard', 'GuardHead' and 'Environment' layers must exist.");
                return;
            }

            // Makes any placeholder sounds that don't exist yet (the
            // guard's bark is newer than the others).
            PlaceholderSounds.Create();

            Remove(GuardName);
            Remove(RouteName);

            // Where the guard goes: the test house's route if there is
            // one, otherwise a post in front of the camera.
            GameObject house = GameObject.Find(HouseName);
            Transform housePath = house != null ? house.transform.Find(HousePathName) : null;
            PatrolRoute route = null;
            Vector3 position;
            Quaternion rotation = Quaternion.identity;

            if (housePath != null && housePath.childCount > 0) {
                route = BuildRoute(house.transform, housePath);
                position = route.transform.GetChild(0).position;
            } else {
                position = Vector3.forward * 4f;
                Camera camera = Camera.main;

                if (camera != null) {
                    Vector3 forward = camera.transform.forward;
                    forward.y = 0f;

                    if (forward.sqrMagnitude > 0.001f) {
                        // Facing back at the camera.
                        rotation = Quaternion.LookRotation(-forward);
                        position = camera.transform.position + forward.normalized * 4f;
                        position.y = 0f;
                    }
                }
            }

            GameObject guard = BuildGuard(position, rotation, route, guardLayer, headLayer);

            PlayerHandTools.Add();
            GiveBlackjackItsThud();

            BakeNavMesh(environment);

            EditorSceneManager.MarkSceneDirty(guard.scene);
            Selection.activeGameObject = guard;
        }

        /// <summary>
        /// The same, for running Unity from the command line with no
        /// window (-batchmode -executeMethod AI.GuardTestSetup.BuildInBatch):
        /// opens the main scene, sets the guard up, and saves.
        /// </summary>
        public static void BuildInBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            Build();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("GuardTestSetup: done.");
        }

        /// <summary>
        /// Editor menu command (TeaLeaf > Check Test Guard Route): asks
        /// the baked NavMesh for a path between each pair of route points
        /// in turn and logs what it finds - so a point that's off the
        /// NavMesh, or a stretch that can't be walked (a doorway too
        /// narrow, stairs too steep), shows up without playing.
        /// </summary>
        [MenuItem("TeaLeaf/Check Test Guard Route")]
        private static void CheckRoute()
        {
            GameObject routeObject = GameObject.Find(RouteName);

            if (routeObject == null) {
                Debug.LogWarning("GuardTestSetup: no test guard route in the scene.");
                return;
            }

            Transform route = routeObject.transform;
            NavMeshPath path = new();
            int problems = 0;

            for (int i = 0; i + 1 < route.childCount; i++) {
                Vector3 from = route.GetChild(i).position;
                Vector3 to = route.GetChild(i + 1).position;

                bool fromOn = NavMesh.SamplePosition(from, out NavMeshHit fromHit, 1f, NavMesh.AllAreas);
                bool toOn = NavMesh.SamplePosition(to, out NavMeshHit toHit, 1f, NavMesh.AllAreas);
                bool found = fromOn && toOn && NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path);
                bool isWhole = found && path.status == NavMeshPathStatus.PathComplete;

                if (!isWhole) {
                    problems++;
                }

                Debug.Log($"GuardTestSetup: point {i + 1} to {i + 2}: {(isWhole ? "ok" : "NO PATH")} (on NavMesh: {fromOn}, {toOn}; status {(found ? path.status.ToString() : "none")}).");
            }

            Debug.Log($"GuardTestSetup: route check finished, {problems} problem(s).");
        }

        /// <summary>
        /// The same, from the command line: opens the main scene first.
        /// </summary>
        public static void CheckRouteInBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            CheckRoute();
        }

        /// <summary>
        /// Removes every object with a name left by an earlier run.
        /// </summary>
        private static void Remove(string objectName)
        {
            GameObject existing = GameObject.Find(objectName);

            while (existing != null) {
                Undo.DestroyObjectImmediate(existing);
                existing = GameObject.Find(objectName);
            }
        }

        /// <summary>
        /// A PatrolRoute under the test house with a point where each of
        /// the house's own path points is, there and back.
        /// </summary>
        private static PatrolRoute BuildRoute(Transform house, Transform housePath)
        {
            GameObject routeObject = new(RouteName);
            Undo.RegisterCreatedObjectUndo(routeObject, "Set Up Test Guard");
            routeObject.transform.SetParent(house, false);

            for (int i = 0; i < housePath.childCount; i++) {
                GameObject point = new($"Point {i + 1}");
                point.transform.SetParent(routeObject.transform, false);
                point.transform.position = housePath.GetChild(i).position;
            }

            return routeObject.AddComponent<PatrolRoute>();
        }

        /// <summary>
        /// The guard: see the class comment for how it's put together.
        /// Guard's settings are private serialized fields, so they're
        /// set the way the Inspector would set them.
        /// </summary>
        private static GameObject BuildGuard(Vector3 position, Quaternion rotation, PatrolRoute route, int guardLayer, int headLayer)
        {
            GameObject guard = new(GuardName) { layer = guardLayer };
            Undo.RegisterCreatedObjectUndo(guard, "Set Up Test Guard");
            guard.transform.SetPositionAndRotation(position, rotation);

            // Moved by its agent every frame: a Rigidbody tells physics
            // it's a moving thing, which is much cheaper than moving a
            // bare collider.
            Rigidbody body = guard.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            CapsuleCollider capsule = guard.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, Height * 0.5f, 0f);
            capsule.radius = Radius;
            capsule.height = Height;

            NavMeshAgent agent = guard.AddComponent<NavMeshAgent>();
            agent.radius = Radius;
            agent.height = Height;
            agent.angularSpeed = 360f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.1f;

            // The head: the blackjack's target, and where it looks from.
            GameObject head = new("Head") { layer = headLayer };
            head.transform.SetParent(guard.transform, false);
            head.transform.localPosition = new Vector3(0f, HeadHeight, 0f);

            SphereCollider headCollider = head.AddComponent<SphereCollider>();
            headCollider.radius = HeadRadius + 0.03f;

            // What's drawn: a capsule body, a ball head and a nose to
            // show which way it faces. No colliders of their own.
            GameObject visual = new("Visual") { layer = guardLayer };
            visual.transform.SetParent(guard.transform, false);

            // A primitive capsule is 2m tall and 1m across.
            GameObject bodyShape = Shape(PrimitiveType.Capsule, "Body", visual.transform, guardLayer);
            bodyShape.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            bodyShape.transform.localScale = new Vector3(0.5f, 0.75f, 0.35f);

            GameObject headShape = Shape(PrimitiveType.Sphere, "Head Shape", visual.transform, guardLayer);
            headShape.transform.localPosition = new Vector3(0f, HeadHeight, 0f);
            headShape.transform.localScale = Vector3.one * (HeadRadius * 2f);

            GameObject nose = Shape(PrimitiveType.Cube, "Nose", visual.transform, guardLayer);
            nose.transform.localPosition = new Vector3(0f, HeadHeight, HeadRadius);
            nose.transform.localScale = new Vector3(0.05f, 0.05f, 0.1f);

            SoundCue bark = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.BarkCuePath);

            SerializedObject settings = new(guard.AddComponent<Guard>());
            settings.FindProperty("eye").objectReferenceValue = head.transform;
            settings.FindProperty("visual").objectReferenceValue = visual.transform;
            settings.FindProperty("bodyCollider").objectReferenceValue = capsule;
            settings.FindProperty("headCollider").objectReferenceValue = headCollider;
            settings.FindProperty("stateRenderer").objectReferenceValue = headShape.GetComponent<MeshRenderer>();
            settings.FindProperty("patrolRoute").objectReferenceValue = route;
            settings.FindProperty("sightLayers").intValue = LayerMask.GetMask("Environment", "Interactable");
            settings.FindProperty("groundLayers").intValue = LayerMask.GetMask("Environment", "Interactable");
            settings.FindProperty("surfaceSounds").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SurfaceSounds>(PlaceholderSounds.SurfaceSoundsPath);

            // One placeholder grunt stands in for every line.
            settings.FindProperty("suspiciousBark").objectReferenceValue = bark;
            settings.FindProperty("searchBark").objectReferenceValue = bark;
            settings.FindProperty("gaveUpBark").objectReferenceValue = bark;
            settings.FindProperty("spottedBark").objectReferenceValue = bark;
            settings.FindProperty("foundBodyBark").objectReferenceValue = bark;
            settings.ApplyModifiedPropertiesWithoutUndo();

            // Writes what the guard does to the Console while playing.
            guard.AddComponent<GuardDebug>();

            return guard;
        }

        /// <summary>
        /// One drawn shape: a primitive in the test material with its
        /// collider removed.
        /// </summary>
        private static GameObject Shape(PrimitiveType type, string shapeName, Transform parent, int layer)
        {
            GameObject shape = TestGeometry.Primitive(type, shapeName, parent, layer);
            Object.DestroyImmediate(shape.GetComponent<Collider>());
            return shape;
        }

        /// <summary>
        /// Gives the player's blackjack the placeholder impact sound for
        /// a blow, if it has none.
        /// </summary>
        private static void GiveBlackjackItsThud()
        {
            PlayerBlackjack blackjack = Object.FindFirstObjectByType<PlayerBlackjack>();

            if (blackjack == null) {
                return;
            }

            SerializedObject settings = new(blackjack);
            SerializedProperty hitCue = settings.FindProperty("hitCue");

            if (hitCue.objectReferenceValue == null) {
                hitCue.objectReferenceValue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.ImpactCuePath);
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Bakes the scene's NavMesh from every collider on the
        /// Environment layer, and saves it as an asset beside the scene
        /// (replacing the last one). The NavMeshSurface is on a NavMesh
        /// object at the scene root, made if there isn't one.
        /// </summary>
        private static void BakeNavMesh(int environmentLayer)
        {
            NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();

            if (surface == null) {
                GameObject surfaceObject = new(NavMeshName);
                Undo.RegisterCreatedObjectUndo(surfaceObject, "Set Up Test Guard");
                surface = surfaceObject.AddComponent<NavMeshSurface>();
            }

            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << environmentLayer;

            // Physics must know where everything is before its colliders
            // are collected.
            Physics.SyncTransforms();

            surface.BuildNavMesh();

            NavMeshData data = surface.navMeshData;

            if (data == null) {
                Debug.LogWarning("GuardTestSetup: the NavMesh bake made nothing.");
                return;
            }

            AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(data, NavMeshPath);
            EditorUtility.SetDirty(surface);

            Bounds bounds = data.sourceBounds;
            Debug.Log($"GuardTestSetup: NavMesh baked, {bounds.size.x:0} x {bounds.size.z:0}m, saved to {NavMeshPath}.");
        }
    }
}
