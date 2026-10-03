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

        // A much smaller overhang, for edges that should follow the solid
        // geometry almost exactly (the town): the grab volume and the lip a
        // hand snaps to sit a centimetre off the solid surface rather than
        // five. Not zero - a volume flush with the solid would tie with it
        // for hand rays, and the reticle would flicker between the two.
        public const float TightOverhang = 0.01f;

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
        ///
        /// bothSidesThickness above 0 makes a two-sided edge for the top of
        /// something free-standing that thick (a wall, parapet, railing,
        /// sill): one grab volume across the whole top, EdgeOverhang past
        /// both faces, with grabbableFromBothSides ticked so a hand snaps
        /// onto whichever side the player is on. Use a top-centre landing
        /// with it, so a mantle from either side lands in the same place.
        ///
        /// overhang is how far the grab volume sticks out past the solid's
        /// top and face(s) - EdgeOverhang by default, TightOverhang to follow
        /// the geometry closely. height is how tall the volume is, from its
        /// top down: pass the solid's own height plus two overhangs for
        /// something thinner than EdgeHeight (a hood, a pergola roof), so
        /// the volume doesn't hang below it.
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
            bool moveHorizontallyToPoint = true,
            float bothSidesThickness = 0f,
            float overhang = EdgeOverhang,
            float height = EdgeHeight)
        {
            bool isTwoSided = bothSidesThickness > 0f;

            // One-sided: a strip EdgeDepth deep behind the lip. Two-sided:
            // the whole thickness plus the overhang past the back face too.
            float depth = isTwoSided ? bothSidesThickness + overhang * 2f : EdgeDepth;

            GameObject edge = new(name);
            edge.layer = climbableLayer;
            edge.transform.SetParent(parent, false);
            edge.transform.SetLocalPositionAndRotation(lipPosition, Quaternion.Euler(0f, yaw, 0f));

            // Trigger first, so the ClimbableEdge's setup check never sees a
            // solid box.
            BoxCollider box = edge.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, overhang - height * 0.5f, overhang - depth * 0.5f);
            box.size = new Vector3(width, height, depth);

            SerializedObject settings = new(edge.AddComponent<ClimbableEdge>());
            settings.FindProperty("snapProfile").objectReferenceValue = profile;
            settings.FindProperty("isMantleable").boolValue = isMantleable;
            settings.FindProperty("mantleEndsCrouched").boolValue = endsCrouched;
            settings.FindProperty("mantlePoint").vector3Value = new Vector3(0f, overhang, overhang - landingInset);
            settings.FindProperty("moveHorizontallyToPoint").boolValue = moveHorizontallyToPoint;
            settings.FindProperty("grabbableFromBothSides").boolValue = isTwoSided;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return edge;
        }

        /// <summary>
        /// The landing inset (see Edge()) that puts a mantle's feet on the
        /// middle of the top of a wall, parapet or sill thickness deep, for
        /// an edge made with the same overhang.
        /// </summary>
        public static float TopCentreInset(float thickness, float overhang = EdgeOverhang)
        {
            return overhang + thickness * 0.5f;
        }
    }
}
