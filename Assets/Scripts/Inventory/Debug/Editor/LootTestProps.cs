using Core;
using Interaction;
using Player;
using UnityEditor;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Add Loot Test Props) that puts a
    /// table of loot in front and to the right of the main camera (beside
    /// the Grabbable Test Props' table, if that's there), for testing the
    /// pack. More than the pack can hold, so it has to be chosen between:
    /// four coins (gold: they just add to the pack's gold), four purses
    /// (one coin of worth), three goblets (two coins), two candlesticks
    /// (two coins), a crown (three coins) and an idol
    /// that is the level's objective. Each is an ordinary prop (a
    /// Rigidbody with a Grabbable, on the Interactable layer) with a Loot
    /// component, in gold so it can be told from the plain props.
    ///
    /// Also adds PlayerInventory (and its debug readout) to the Player
    /// root and PlayerPack to the player's Hands object, and makes the
    /// scene's one Pack object, if they aren't there.
    ///
    /// Built from code so it can be rebuilt identically, and undone with
    /// Ctrl+Z. In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class LootTestProps
    {
        private const string RootName = "Loot Test Props";
        private const float TableHeight = 0.9f;
        private const float TableWidth = 1.6f;
        private const float TableDepth = 0.7f;

        private const string GoldMaterialPath = "Assets/Art/Materials/LootGold.mat";
        private const string BottleProfilePath = "Assets/Data/BottleHold.asset";

        [MenuItem("TeaLeaf/Add Loot Test Props")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer("Environment");
            int interactable = LayerMask.NameToLayer("Interactable");

            if (environment < 0 || interactable < 0) {
                Debug.LogError("LootTestProps: the 'Environment' and 'Interactable' layers must exist.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog("Loot Test Props", "Loot test props already exist in the scene. Replace them?", "Replace", "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            // On the floor, 1.2m ahead of the main camera and 1.7m to its
            // right, facing the same way it does (level - only its turn
            // about Y is used).
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Camera camera = Camera.main;

            if (camera != null) {
                Vector3 forward = camera.transform.forward;
                forward.y = 0f;

                if (forward.sqrMagnitude > 0.001f) {
                    rotation = Quaternion.LookRotation(forward);
                }

                position = camera.transform.position + rotation * new Vector3(1.7f, 0f, 1.2f);
                position.y = 0f;
            }

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(root, "Add Loot Test Props");
            Transform parent = root.transform;

            TestGeometry.Box("Table", parent, new Vector3(0f, TableHeight * 0.5f, 0f), new Vector3(TableWidth, TableHeight, TableDepth), environment);

            Material gold = GetGoldMaterial();
            HandSnapProfile bottleProfile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(BottleProfilePath);

            // Two rows along the table: the near one (towards the camera)
            // and the far one.
            const float nearZ = -0.18f;
            const float farZ = 0.14f;

            // Coins: small flat boxes. Gold - they take no space of
            // their own.
            for (int i = 0; i < 4; i++) {
                GameObject coin = TestGeometry.Box($"Loot Coin {i + 1}", parent, new Vector3(-0.7f + i * 0.08f, TableHeight + 0.01f, nearZ), new Vector3(0.045f, 0.02f, 0.045f), interactable);
                MakeLoot(coin, gold, 0.1f, 0.035f, 5, 1, true, false);
            }

            // Purses: worth a little.
            for (int i = 0; i < 4; i++) {
                GameObject purse = TestGeometry.Box($"Loot Purse {i + 1}", parent, new Vector3(-0.3f + i * 0.14f, TableHeight + 0.035f, nearZ), new Vector3(0.09f, 0.07f, 0.07f), interactable);
                MakeLoot(purse, gold, 0.3f, 0.06f, 20, 1, false, false);
            }

            // Goblets: worth more. Cylinders, held like the bottle.
            for (int i = 0; i < 3; i++) {
                GameObject goblet = Cylinder($"Loot Goblet {i + 1}", parent, new Vector3(0.35f + i * 0.14f, TableHeight + 0.07f, nearZ), 0.07f, 0.14f, interactable);
                MakeLoot(goblet, gold, 0.5f, 0.09f, 50, 2, false, false, true, bottleProfile);
            }

            // Candlesticks: tall, so they shrink a long way to fit a space.
            for (int i = 0; i < 2; i++) {
                GameObject candlestick = Cylinder($"Loot Candlestick {i + 1}", parent, new Vector3(-0.6f + i * 0.16f, TableHeight + 0.15f, farZ), 0.05f, 0.3f, interactable);
                MakeLoot(candlestick, gold, 0.8f, 0.16f, 80, 2, false, false, true, bottleProfile);
            }

            // A crown: the best of it.
            GameObject crown = Cylinder("Loot Crown", parent, new Vector3(0f, TableHeight + 0.04f, farZ), 0.16f, 0.08f, interactable);
            MakeLoot(crown, gold, 0.6f, 0.1f, 150, 3, false, false);

            // The objective: packing it is what lets the player leave.
            GameObject idol = TestGeometry.Box("Loot Idol (objective)", parent, new Vector3(0.5f, TableHeight + 0.09f, farZ), new Vector3(0.1f, 0.18f, 0.1f), interactable);
            MakeLoot(idol, gold, 1f, 0.11f, 100, 3, false, true);

            EnsurePlayerComponents();
            Selection.activeGameObject = root;
        }

        /// <summary>
        /// An upright cylinder of the given width and height, with a box
        /// collider: a cylinder primitive's own capsule collider has a
        /// round bottom and would roll over (see the bottle in
        /// GrabbableTestProps). Unity's cylinder is 1m across and 2m
        /// tall, so the height scale is half the height wanted.
        /// </summary>
        private static GameObject Cylinder(string name, Transform parent, Vector3 localPosition, float diameter, float height, int layer)
        {
            GameObject cylinder = TestGeometry.Primitive(PrimitiveType.Cylinder, name, parent, layer);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);

            Object.DestroyImmediate(cylinder.GetComponent<CapsuleCollider>());
            cylinder.AddComponent<BoxCollider>().size = new Vector3(0.8f, 2f, 0.8f);
            return cylinder;
        }

        /// <summary>
        /// Turns a shape into loot: the gold material, the Rigidbody and
        /// Grabbable any prop has (GrabbableTestProps.MakeGrabbable()),
        /// and a Loot with its worth. Loot's settings are private
        /// serialized fields, so they're set the way the Inspector would
        /// set them.
        /// </summary>
        private static void MakeLoot(GameObject prop, Material gold, float mass, float holdRadius, int value, int coinLevel, bool isGold, bool isObjective, bool isCylinder = false, HandSnapProfile profile = null)
        {
            prop.GetComponent<MeshRenderer>().sharedMaterial = gold;
            GrabbableTestProps.MakeGrabbable(prop, mass, holdRadius, isCylinder, profile);

            SerializedObject serialized = new(prop.AddComponent<Loot>());
            serialized.FindProperty("value").intValue = value;
            serialized.FindProperty("coinLevel").intValue = coinLevel;
            serialized.FindProperty("isGold").boolValue = isGold;
            serialized.FindProperty("isObjective").boolValue = isObjective;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The gold material asset, created the first time it's needed as
        /// a copy of the test geometry's material in a different colour.
        /// </summary>
        private static Material GetGoldMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GoldMaterialPath);

            if (material != null) {
                return material;
            }

            material = new Material(TestGeometry.GetMaterial()) { name = "LootGold" };
            material.SetColor("_BaseColor", new Color(0.85f, 0.65f, 0.15f, 1f));
            AssetDatabase.CreateAsset(material, GoldMaterialPath);
            return material;
        }

        /// <summary>
        /// Makes sure the player has a pack: PlayerInventory and
        /// InventoryDebug next to PlayerController (on the Player root),
        /// PlayerPack next to PlayerHandHolding (on the Hands object), and
        /// the scene's one Pack object - each only if missing. The pack
        /// sits at the scene's root, not under the test props, so
        /// rebuilding them keeps any tuning done to it (at run time it's
        /// moved onto the left hand). The sound of something going in is
        /// the placeholder footstep, as a soft stand-in.
        /// </summary>
        internal static void EnsurePlayerComponents()
        {
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();

            if (player == null) {
                Debug.LogWarning("LootTestProps: no PlayerController in the scene, so PlayerInventory wasn't added - add it to the Player root by hand.");
            } else {
                if (player.GetComponent<PlayerInventory>() == null) {
                    Undo.AddComponent<PlayerInventory>(player.gameObject);
                }

                if (player.GetComponent<InventoryDebug>() == null) {
                    Undo.AddComponent<InventoryDebug>(player.gameObject);
                }
            }

            PlayerHandHolding hands = Object.FindFirstObjectByType<PlayerHandHolding>();

            if (hands == null) {
                Debug.LogWarning("LootTestProps: no PlayerHandHolding in the scene, so PlayerPack wasn't added - add it to the Hands object by hand.");
                return;
            }

            Pack pack = Object.FindFirstObjectByType<Pack>();

            if (pack == null) {
                GameObject packObject = new("Pack");
                Undo.RegisterCreatedObjectUndo(packObject, "Add Loot Test Props");
                pack = packObject.AddComponent<Pack>();
            }

            PlayerPack playerPack = hands.GetComponent<PlayerPack>();

            if (playerPack == null) {
                playerPack = Undo.AddComponent<PlayerPack>(hands.gameObject);

                SerializedObject sound = new(playerPack);
                sound.FindProperty("storeCue").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.FootstepCuePath);
                sound.ApplyModifiedPropertiesWithoutUndo();
            }

            SerializedObject serialized = new(playerPack);
            serialized.FindProperty("pack").objectReferenceValue = pack;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
