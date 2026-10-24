using Interaction;
using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Dev Room > Add Hand Tools To Player) that adds
    /// the body reach zones, the blackjack, the bow (with its arrows) and
    /// the compass to the player's Hands object - each only if it's
    /// missing. Each one's Reset() fills in its own references, and
    /// PlayerController and PlayerHandState find them by themselves when
    /// the game starts. Empty bow models (one per handedness), arrow model, arrow material,
    /// arrow rope grip and bow, arrow and string hand profiles are filled
    /// in from their assets.
    ///
    /// Undone with Ctrl+Z. In an Editor folder, so it never ends up in a
    /// build.
    /// </summary>
    public static class PlayerHandTools
    {
        // The bow models (a mesh with a "Drawn" blend shape and its three
        // empties), one for each hand holding it, and how a hand grips a
        // rope arrow's rope - the same as a placed rope.
        private const string BowModelPath = "Assets/TripoModels/bow_3d_model/bow_rigged_right_handed.fbx";
        private const string LeftHandedBowModelPath = "Assets/TripoModels/bow_3d_model/bow_rigged_left_handed.fbx";
        private const string RopeProfilePath = "Assets/Data/Hand Poses/RopeGrip.asset";

        // How the hands hold the bow, an arrow and the string (posed with
        // Pose with Hand).
        private const string BowProfilePath = "Assets/Data/Hand Poses/BowGrip.asset";
        private const string ArrowProfilePath = "Assets/Data/Hand Poses/ArrowHold.asset";
        private const string StringProfilePath = "Assets/Data/Hand Poses/BowString.asset";

        // The arrow models, one file per kind named after the kind
        // (arrows_3d_model_water.fbx...), and the material they share.
        private const string ArrowModelFolder = "Assets/TripoModels/arrows_3d_model/";
        private const string ArrowMaterialPath = ArrowModelFolder + "Materials/arrows_3d_model.mat";

        [MenuItem("TeaLeaf/Dev Room/Add Hand Tools To Player")]
        internal static void Add()
        {
            PlayerHandVisuals hands = Object.FindFirstObjectByType<PlayerHandVisuals>();

            if (hands == null) {
                Debug.LogWarning("PlayerHandTools: no PlayerHandVisuals in the scene - add the components to the Hands object by hand.");
                return;
            }

            int added = 0;
            added += Ensure<PlayerBodyZones>(hands.gameObject);
            added += Ensure<PlayerBlackjack>(hands.gameObject);
            added += Ensure<PlayerArrows>(hands.gameObject);
            added += Ensure<PlayerBow>(hands.gameObject);
            added += Ensure<PlayerCompass>(hands.gameObject);

            PlayerArrows arrows = hands.GetComponent<PlayerArrows>();
            FillEmpty<GameObject>(hands.GetComponent<PlayerBow>(), "bowModel", BowModelPath);
            FillEmpty<GameObject>(hands.GetComponent<PlayerBow>(), "leftHandedBowModel", LeftHandedBowModelPath);
            FillEmpty<HandSnapProfile>(hands.GetComponent<PlayerBow>(), "bowProfile", BowProfilePath);
            FillEmpty<HandSnapProfile>(hands.GetComponent<PlayerBow>(), "arrowProfile", ArrowProfilePath);
            FillEmpty<HandSnapProfile>(hands.GetComponent<PlayerBow>(), "stringProfile", StringProfilePath);
            FillEmpty<HandSnapProfile>(arrows, "ropeProfile", RopeProfilePath);
            FillEmpty<Mesh>(arrows, "waterArrow", ArrowModelFolder + "arrows_3d_model_water.fbx");
            FillEmpty<Mesh>(arrows, "noisemakerArrow", ArrowModelFolder + "arrows_3d_model_noisemaker.fbx");
            FillEmpty<Mesh>(arrows, "ropeArrow", ArrowModelFolder + "arrows_3d_model_rope.fbx");
            FillEmpty<Mesh>(arrows, "stunArrow", ArrowModelFolder + "arrows_3d_model_stun.fbx");
            FillEmpty<Mesh>(arrows, "sleepDart", ArrowModelFolder + "arrows_3d_model_sleep.fbx");
            FillEmpty<Mesh>(arrows, "mossArrow", ArrowModelFolder + "arrows_3d_model_moss.fbx");
            FillEmpty<Material>(arrows, "arrowMaterial", ArrowMaterialPath);

            Debug.Log($"PlayerHandTools: added {added} component(s) to '{hands.name}'.", hands);
            Selection.activeGameObject = hands.gameObject;
        }

        /// <summary>
        /// Adds a component to target if it has none. Returns how many
        /// were added (0 or 1).
        /// </summary>
        private static int Ensure<T>(GameObject target) where T : Component
        {
            if (target.GetComponent<T>() != null) {
                return 0;
            }

            Undo.AddComponent<T>(target);
            return 1;
        }

        /// <summary>
        /// Sets a component's asset field to the asset of type T at a path
        /// (for a mesh, the one inside a model file), if the field is
        /// empty. Warns if the asset isn't there.
        /// </summary>
        private static void FillEmpty<T>(Component component, string fieldName, string assetPath) where T : Object
        {
            SerializedObject serialized = new(component);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null || property.objectReferenceValue != null) {
                return;
            }

            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);

            if (asset == null) {
                Debug.LogWarning($"PlayerHandTools: no asset at {assetPath} for {component.GetType().Name}.{fieldName} - set it by hand.", component);
                return;
            }

            property.objectReferenceValue = asset;
            serialized.ApplyModifiedProperties();
        }
    }
}
