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
    /// Editor menu command (TeaLeaf > Dev Room > Set Up Test Guard) that puts a
    /// guard in the scene and bakes the NavMesh it walks on.
    ///
    /// With a Sound Test House in the scene, the guard patrols the route
    /// the house lays out (Bedroom, Landing, stairs, Hall, Kitchen, and
    /// back). Without one, the guard stands 4m ahead of the main camera.
    ///
    /// The guard is an instance of the Guard prefab
    /// (Assets/Prefabs/Characters/Guard.prefab): the model, its
    /// Animator, colliders, agent and components are all in there; this
    /// only places it, gives it the route and the placeholder sounds,
    /// and adds a GuardDebug.
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
    /// Works on whichever scene is open; the batch versions open the scene
    /// given on the command line (-scene Assets/Scenes/Name.unity).
    ///
    /// Can be undone with Ctrl+Z, apart from the NavMesh asset. In an
    /// Editor folder, so it never ends up in a build.
    /// </summary>
    public static class GuardTestSetup
    {
        // The command-line option naming the scene for the batch versions.
        private const string SceneArgument = "-scene";

        private const string HouseName = "Sound Test House";
        private const string HousePathName = "Guard Path";

        private const string GuardName = "Test Guard";
        private const string RouteName = "Test Guard Route";
        private const string NavMeshName = "NavMesh";
        private const string GuardPrefabPath = "Assets/Prefabs/Characters/Guard.prefab";

        [MenuItem("TeaLeaf/Dev Room/Set Up Test Guard")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer("Environment");

            if (environment < 0) {
                Debug.LogError("GuardTestSetup: the 'Environment' layer must exist.");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuardPrefabPath);

            if (prefab == null) {
                Debug.LogError($"GuardTestSetup: no guard prefab at {GuardPrefabPath}.");
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

            GameObject guard = PlaceGuard(prefab, position, rotation, route);

            PlayerHandTools.Add();
            GiveBlackjackItsThud();

            BakeNavMesh(environment);

            EditorSceneManager.MarkSceneDirty(guard.scene);
            Selection.activeGameObject = guard;
        }

        /// <summary>
        /// The same, for running Unity from the command line with no
        /// window (-batchmode -executeMethod AI.GuardTestSetup.BuildInBatch
        /// -scene Assets/Scenes/Name.unity): opens that scene, sets the
        /// guard up, and saves.
        /// </summary>
        public static void BuildInBatch()
        {
            if (!TryOpenSceneFromCommandLine(out Scene scene)) {
                return;
            }

            Build();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("GuardTestSetup: done.");
        }

        /// <summary>
        /// Editor menu command (TeaLeaf > Dev Room > Check Test Guard Route): asks
        /// the baked NavMesh for a path between each pair of route points
        /// in turn and logs what it finds - so a point that's off the
        /// NavMesh, or a stretch that can't be walked (a doorway too
        /// narrow, stairs too steep), shows up without playing.
        /// </summary>
        [MenuItem("TeaLeaf/Dev Room/Check Test Guard Route")]
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
        /// The same, from the command line: opens the scene given with
        /// -scene first.
        /// </summary>
        public static void CheckRouteInBatch()
        {
            if (TryOpenSceneFromCommandLine(out _)) {
                CheckRoute();
            }
        }

        /// <summary>
        /// Opens the scene named after -scene on the command line. Logs an
        /// error and returns false if there's no -scene, or no scene at
        /// that path.
        /// </summary>
        private static bool TryOpenSceneFromCommandLine(out Scene scene)
        {
            scene = default;
            string[] args = System.Environment.GetCommandLineArgs();
            string path = null;

            for (int i = 0; i + 1 < args.Length; i++) {
                if (args[i] == SceneArgument) {
                    path = args[i + 1];
                    break;
                }
            }

            if (string.IsNullOrEmpty(path)) {
                Debug.LogError($"GuardTestSetup: give the scene to open with {SceneArgument} Assets/Scenes/Name.unity.");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) {
                Debug.LogError($"GuardTestSetup: no scene at {path}.");
                return false;
            }

            scene = EditorSceneManager.OpenScene(path);
            return true;
        }

        /// <summary>
        /// Where the scene's NavMesh is saved: beside it, as
        /// "Name-NavMesh.asset". Null for a scene never saved, which has
        /// nowhere to put it.
        /// </summary>
        private static string NavMeshPathFor(Scene scene)
        {
            if (string.IsNullOrEmpty(scene.path)) {
                return null;
            }

            string folder = System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/');
            return $"{folder}/{scene.name}-NavMesh.asset";
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
        /// An instance of the guard prefab at the place, with the route
        /// and, where the prefab has none, the placeholder sounds.
        /// Guard's settings are private serialized fields, so they're
        /// set the way the Inspector would set them.
        /// </summary>
        private static GameObject PlaceGuard(GameObject prefab, Vector3 position, Quaternion rotation, PatrolRoute route)
        {
            GameObject guard = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            guard.name = GuardName;
            Undo.RegisterCreatedObjectUndo(guard, "Set Up Test Guard");
            guard.transform.SetPositionAndRotation(position, rotation);

            SoundCue bark = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.BarkCuePath);

            SerializedObject settings = new(guard.GetComponent<Guard>());
            settings.FindProperty("patrolRoute").objectReferenceValue = route;
            FillIfEmpty(settings.FindProperty("surfaceSounds"), AssetDatabase.LoadAssetAtPath<SurfaceSounds>(PlaceholderSounds.SurfaceSoundsPath));

            // One placeholder grunt stands in for every line.
            FillIfEmpty(settings.FindProperty("suspiciousBark"), bark);
            FillIfEmpty(settings.FindProperty("searchBark"), bark);
            FillIfEmpty(settings.FindProperty("gaveUpBark"), bark);
            FillIfEmpty(settings.FindProperty("spottedBark"), bark);
            FillIfEmpty(settings.FindProperty("foundBodyBark"), bark);
            settings.ApplyModifiedPropertiesWithoutUndo();

            // Writes what the guard does to the Console while playing.
            if (guard.GetComponent<GuardDebug>() == null) {
                guard.AddComponent<GuardDebug>();
            }

            return guard;
        }

        /// <summary>
        /// Sets an object reference only where the prefab left it empty.
        /// </summary>
        private static void FillIfEmpty(SerializedProperty property, Object value)
        {
            if (property.objectReferenceValue == null) {
                property.objectReferenceValue = value;
            }
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
        /// Bakes the active scene's NavMesh from every collider on the
        /// Environment layer, and saves it as an asset beside the scene
        /// (replacing the last one). The NavMeshSurface is on a NavMesh
        /// object at the scene root, made if there isn't one.
        /// </summary>
        private static void BakeNavMesh(int environmentLayer)
        {
            string navMeshPath = NavMeshPathFor(SceneManager.GetActiveScene());

            if (navMeshPath == null) {
                Debug.LogError("GuardTestSetup: save the scene first - its NavMesh is saved beside it.");
                return;
            }

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

            AssetDatabase.DeleteAsset(navMeshPath);
            AssetDatabase.CreateAsset(data, navMeshPath);
            EditorUtility.SetDirty(surface);

            Bounds bounds = data.sourceBounds;
            Debug.Log($"GuardTestSetup: NavMesh baked, {bounds.size.x:0} x {bounds.size.z:0}m, saved to {navMeshPath}.");
        }
    }
}
