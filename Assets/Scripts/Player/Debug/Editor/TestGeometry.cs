using Interaction;
using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Shared helpers for the editor test-area builders
    /// (PhysicalHandsTestArea, LocomotionTestCourse, TownTestArea): makes
    /// primitives with the test geometry material, a flat brown, so test
    /// pieces stand out from the grey floor at a glance, and ClimbableEdges
    /// set up the way their setup checks expect. One place for it, so every
    /// test area looks and climbs the same and a new one gets it for free.
    ///
    /// In an Editor folder, so it's compiled into the editor-only assembly
    /// and never ends up in a build.
    /// </summary>
    public static class TestGeometry
    {
        private const string MaterialPath = "Assets/Art/Materials/TestGeometry.mat";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        // A mid brown, like unfinished wood - clearly not the floor's grey.
        private static readonly Color _brown = new(0.45f, 0.3f, 0.18f);

        // Edge grab volumes: a strip along the lip, EdgeHeight tall and
        // EdgeDepth deep, sticking EdgeOverhang out past the solid top and
        // front face so hand rays reach it before the solid geometry - the
        // same shape as the hand-placed edges in Main.unity.
        private const float EdgeHeight = 0.2f;
        private const float EdgeDepth = 0.3f;
        private const float EdgeOverhang = 0.05f;

        /// <summary>
        /// The test geometry material asset, created the first time it's
        /// needed (an ordinary URP Lit material, so it can be tweaked in the
        /// Inspector like any other, and the change sticks across rebuilds).
        /// </summary>
        public static Material GetMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            if (material != null) {
                return material;
            }

            material = new Material(Shader.Find(LitShaderName)) { name = "TestGeometry" };

            // URP Lit's main colour is _BaseColor, not the built-in _Color.
            material.SetColor("_BaseColor", _brown);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        /// <summary>
        /// Makes a cube primitive (with its BoxCollider) of the given size at
        /// localPosition under parent, in the test material. Scale is the size
        /// here, since a primitive cube is 1m.
        /// </summary>
        public static GameObject Box(string name, Transform parent, Vector3 localPosition, Vector3 size, int layer)
        {
            GameObject box = Primitive(PrimitiveType.Cube, name, parent, layer);
            box.transform.localPosition = localPosition;
            box.transform.localScale = size;
            return box;
        }

        /// <summary>
        /// Makes any primitive (with its default collider) under parent, on
        /// layer, in the test material, at the parent's origin - the caller
        /// places and sizes it.
        /// </summary>
        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, int layer)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.layer = layer;
            primitive.transform.SetParent(parent, false);
            primitive.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial();
            return primitive;
        }

        /// <summary>
        /// A ClimbableEdge on a solid lip: lipPosition is the middle of the
        /// solid's top-front line, and yaw (degrees about Y, in parent space)
        /// turns the edge so its +Z points out from the wall towards the
        /// player (0 = facing +Z). The grab volume is a trigger strip (see
        /// EdgeHeight) on the Climbable layer, unscaled, with its origin on
        /// the lip, so the numbers are real metres. A mantle lands the feet
        /// on the grab volume's top, landingInset in from its lip - past the
        /// lip's overhang, so e.g. the middle of a wall top of thickness t is
        /// an inset of EdgeOverhang + t / 2 (see TopCentreInset()).
        /// moveHorizontallyToPoint off lands a mantle straight ahead of the
        /// player rather than always at the middle of the edge - for long
        /// edges (see ClimbableEdge).
        /// </summary>
        public static GameObject Edge(
            string name,
            Transform parent,
            Vector3 lipPosition,
            float yaw,
            float width,
            bool isMantleable,
            bool endsCrouched,
            float landingInset,
            HandSnapProfile profile,
            int climbableLayer,
            bool moveHorizontallyToPoint = true)
        {
            GameObject edge = new(name);
            edge.layer = climbableLayer;
            edge.transform.SetParent(parent, false);
            edge.transform.SetLocalPositionAndRotation(lipPosition, Quaternion.Euler(0f, yaw, 0f));

            // Trigger first, so the ClimbableEdge's setup check never sees a
            // solid box.
            BoxCollider box = edge.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, EdgeOverhang - EdgeHeight * 0.5f, EdgeOverhang - EdgeDepth * 0.5f);
            box.size = new Vector3(width, EdgeHeight, EdgeDepth);

            SerializedObject settings = new(edge.AddComponent<ClimbableEdge>());
            settings.FindProperty("snapProfile").objectReferenceValue = profile;
            settings.FindProperty("isMantleable").boolValue = isMantleable;
            settings.FindProperty("mantleEndsCrouched").boolValue = endsCrouched;
            settings.FindProperty("mantlePoint").vector3Value = new Vector3(0f, EdgeOverhang, EdgeOverhang - landingInset);
            settings.FindProperty("moveHorizontallyToPoint").boolValue = moveHorizontallyToPoint;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return edge;
        }

        /// <summary>
        /// The landing inset (see Edge()) that puts a mantle's feet on the
        /// middle of the top of a wall, parapet or sill thickness deep.
        /// </summary>
        public static float TopCentreInset(float thickness)
        {
            return EdgeOverhang + thickness * 0.5f;
        }
    }
}
