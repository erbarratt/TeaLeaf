using Core;
using Interaction;
using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Add Grabbable Test Props) that puts a
    /// table with three props on it in front of the main camera, for
    /// testing picking up, carrying and dropping: a small cube, a bottle
    /// (a body cylinder with a neck cylinder on top) and a crate. Each is a
    /// Rigidbody with a Grabbable on the Interactable layer. The cube and
    /// crate have no grip point or snap profile, so the hand holds each by
    /// its middle until those are authored. The bottle has a cylinder
    /// grip, so the hand takes it from the player's side, and the
    /// BottleHold snap profile, which places the hand and curls the
    /// fingers round it.
    ///
    /// Built from code so it can be rebuilt identically, and undone with
    /// Ctrl+Z. In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class GrabbableTestProps
    {
        private const string RootName = "Grabbable Test Props";
        private const float TableHeight = 0.9f;

        // The bottle's hold: per-hand offsets and the BottleHold finger pose.
        private const string BottleProfilePath = "Assets/Data/BottleHold.asset";

        // The bottle: a wide body with a narrow neck on top, in metres.
        private const float BottleBodyDiameter = 0.08f;
        private const float BottleBodyHeight = 0.18f;
        private const float BottleNeckDiameter = 0.03f;
        private const float BottleNeckHeight = 0.08f;

        [MenuItem("TeaLeaf/Add Grabbable Test Props")]
        private static void Build()
        {
            int environment = LayerMask.NameToLayer("Environment");
            int interactable = LayerMask.NameToLayer("Interactable");

            if (environment < 0 || interactable < 0) {
                Debug.LogError("GrabbableTestProps: the 'Environment' and 'Interactable' layers must exist.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog("Grabbable Test Props", "Test props already exist in the scene. Replace them?", "Replace", "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            // On the floor, 1.2m ahead of the main camera, facing the same
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

                position = camera.transform.position + rotation * new Vector3(0f, 0f, 1.2f);
                position.y = 0f;
            }

            GameObject root = new(RootName);
            root.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(root, "Add Grabbable Test Props");
            Transform parent = root.transform;

            TestGeometry.Box("Table", parent, new Vector3(0f, TableHeight * 0.5f, 0f), new Vector3(1.2f, TableHeight, 0.6f), environment);

            GameObject cube = TestGeometry.Box("Prop Cube", parent, new Vector3(-0.4f, TableHeight + 0.06f, 0f), Vector3.one * 0.12f, interactable);
            MakeGrabbable(cube, 0.5f, 0.08f);

            GameObject bottle = BuildBottle(parent, new Vector3(0f, TableHeight + BottleBodyHeight * 0.5f, 0f), interactable);
            // Without its profile the bottle still works, held with the
            // hand on the grip frame and the fallback finger pose - so a
            // missing asset is a warning, not a reason to stop.
            HandSnapProfile bottleProfile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(BottleProfilePath);

            if (bottleProfile == null) {
                Debug.LogWarning($"GrabbableTestProps: couldn't load the bottle's hand snap profile ({BottleProfilePath}) - the bottle has none.");
            }

            MakeGrabbable(bottle, 0.7f, 0.12f, true, bottleProfile);

            GameObject crate = TestGeometry.Box("Prop Crate", parent, new Vector3(0.38f, TableHeight + 0.15f, 0f), Vector3.one * 0.3f, interactable);
            MakeGrabbable(crate, 4f, 0.18f);

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// Builds the bottle: an unscaled root (which gets the Rigidbody and
        /// Grabbable) at the middle of the body, with a body cylinder and a
        /// neck cylinder as children. The root's origin is the middle of the
        /// body because a cylinder grip with no grip point is centred on
        /// the prop's origin - so the hand takes the bottle by its body.
        /// </summary>
        private static GameObject BuildBottle(Transform parent, Vector3 localPosition, int layer)
        {
            GameObject bottle = new("Prop Bottle") { layer = layer };
            bottle.transform.SetParent(parent, false);
            bottle.transform.localPosition = localPosition;

            // Unity's cylinder is 1m across and 2m tall, so the height
            // scale is half the height wanted.
            GameObject body = TestGeometry.Primitive(PrimitiveType.Cylinder, "Body", bottle.transform, layer);
            body.transform.localScale = new Vector3(BottleBodyDiameter, BottleBodyHeight * 0.5f, BottleBodyDiameter);

            // A cylinder primitive comes with a capsule collider, whose
            // round bottom would roll the bottle over. A box stands up:
            // 2 tall like the mesh, and a little narrower than the cylinder
            // so its corners don't stick out far.
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            body.AddComponent<BoxCollider>().size = new Vector3(0.8f, 2f, 0.8f);

            // The neck keeps its capsule; it sits on top of the body.
            GameObject neck = TestGeometry.Primitive(PrimitiveType.Cylinder, "Neck", bottle.transform, layer);
            neck.transform.localPosition = new Vector3(0f, (BottleBodyHeight + BottleNeckHeight) * 0.5f, 0f);
            neck.transform.localScale = new Vector3(BottleNeckDiameter, BottleNeckHeight * 0.5f, BottleNeckDiameter);

            return bottle;
        }

        /// <summary>
        /// Adds the Rigidbody and Grabbable. The Grabbable's settings are
        /// private serialized fields, so they're set the way the Inspector
        /// would set them. isCylinder gives it a cylinder grip (gripped
        /// from the hand's side) with the Grabbable's default cylinder
        /// size, which suits the bottle. profile is the prop's hand snap
        /// profile (null = none: the hand sits on the grip frame).
        /// </summary>
        private static void MakeGrabbable(GameObject prop, float mass, float holdRadius, bool isCylinder = false, HandSnapProfile profile = null)
        {
            Rigidbody body = prop.AddComponent<Rigidbody>();
            body.mass = mass;

            // Smoothed between physics steps from the start, so a prop that
            // is knocked over before it's ever picked up moves smoothly too
            // (Grabbable sets this itself each time a hand lets go).
            body.interpolation = RigidbodyInterpolation.Interpolate;

            SerializedObject serialized = new(prop.AddComponent<Grabbable>());
            serialized.FindProperty("holdRadius").floatValue = holdRadius;
            serialized.FindProperty("snapProfile").objectReferenceValue = profile;

            if (isCylinder) {
                serialized.FindProperty("gripShape").enumValueIndex = (int)Grabbable.GripShape.Cylinder;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // The sound it makes when it lands: the placeholder impact cue,
            // scaled by the surface it hits. Missing assets just leave the
            // fields empty (no cue = silent).
            SerializedObject impact = new(prop.AddComponent<ImpactNoise>());
            impact.FindProperty("cue").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SoundCue>(PlaceholderSounds.ImpactCuePath);
            impact.FindProperty("surfaceSounds").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SurfaceSounds>(PlaceholderSounds.SurfaceSoundsPath);
            impact.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
