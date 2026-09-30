using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Shared helpers for the editor test-area builders
    /// (PhysicalHandsTestArea, LocomotionTestCourse): makes primitives with
    /// the test geometry material, a flat brown, so test pieces stand out
    /// from the grey floor at a glance. One place for it, so every test area
    /// looks the same and a new one gets it for free.
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
    }
}
