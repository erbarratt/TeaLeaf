using System;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace Core
{
    /// <summary>
    /// The tuning tool for a barrel's collision: a twelve-sided shape made of
    /// four rings, each with its own height and radius, so it can follow a
    /// barrel's bulge. It is tuned once, then baked to a mesh asset that
    /// every barrel's MeshCollider shares - so Unity prepares the collision
    /// shape once, at import, and nothing is built when the level loads.
    ///
    /// Goes on an empty child of the barrel's prefab variant, next to a
    /// convex MeshCollider, placed at the middle of the barrel's base.
    /// Orientation convention - local +Y runs up the barrel, and each ring is
    /// a circle around that axis at its height above the object's position.
    ///
    /// The sizes are in the object's own units, so keep the object (and its
    /// parents) at scale 1 for them to be metres.
    ///
    /// How to use it: select the object, change the rings until the yellow
    /// outline fits the barrel, then right-click the component and choose
    /// "Bake To Mesh Asset". The collider only changes when baked. Other
    /// barrel prefabs need no BarrelCollider: give them a convex MeshCollider
    /// and assign the baked mesh.
    ///
    /// This component does nothing while the game runs (it has no Awake or
    /// Update): it stays on the prefab only to remember the numbers, so the
    /// shape can be tuned again later.
    ///
    /// The collider is convex, so it can only bulge: a ring set narrower than
    /// a straight line between its neighbours is simply bridged over.
    /// </summary>
    [RequireComponent(typeof(MeshCollider))]
    public class BarrelCollider : MonoBehaviour, IDebugDrawable
    {
        /// <summary>
        /// One ring of corners: a circle around the barrel's axis.
        /// </summary>
        [Serializable]
        private struct Ring
        {
            // How far above the object's position the ring is.
            public float height;

            // How far the ring's corners are from the barrel's axis.
            public float radius;

            public Ring(float height, float radius)
            {
                this.height = height;
                this.radius = radius;
            }
        }

        // The four rings, from the base upwards. The two rim rings are also
        // the flat bottom and top of the shape.
        [SerializeField] private Ring bottomRim = new(0.01f, 0.33f);
        [SerializeField] private Ring lowerBelly = new(0.35f, 0.4f);
        [SerializeField] private Ring upperBelly = new(0.69f, 0.4f);
        [SerializeField] private Ring topRim = new(1.03f, 0.33f);

        // Where "Bake To Mesh Asset" saves the mesh. Baking again writes over
        // the same asset, so every collider using it gets the new shape.
        [SerializeField] private string meshAssetPath = "Assets/Art/Colliders/BarrelCollider.asset";

        // How many corners each ring has.
        private const int Sides = 12;

        private const int RingCount = 4;

        // Smallest allowed radius, and smallest gap between one ring and the
        // next - a shape with no width or height has no volume, and a convex
        // collider can't be made from it.
        private const float MinSize = 0.01f;

        private static readonly Color _outlineColor = new(1f, 0.85f, 0.2f, 1f);

        /// <summary>
        /// The outline of the shape the numbers describe now (the collider
        /// itself only changes when baked): the four rings, and a line up
        /// each of the twelve edges. In this object's local space.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            lines.Color = _outlineColor;
            lines.Matrix = transform.localToWorldMatrix;

            for (int side = 0; side < Sides; side++) {
                int nextSide = (side + 1) % Sides;

                for (int ring = 0; ring < RingCount; ring++) {
                    // Round the ring.
                    lines.Line(Corner(ring, side), Corner(ring, nextSide));

                    // Up to the ring above.
                    if (ring < RingCount - 1) {
                        lines.Line(Corner(ring, side), Corner(ring + 1, side));
                    }
                }
            }
        }

        /// <summary>
        /// One corner of the shape: corner number `side` (0 to 11) of ring
        /// number `ring` (0 = bottom rim, 3 = top rim). The corners of every
        /// ring sit at the same angles, so they line up vertically.
        /// </summary>
        private Vector3 Corner(int ring, int side)
        {
            Ring r = GetRing(ring);
            float angle = side * (Mathf.PI * 2f / Sides);

            return new Vector3(Mathf.Cos(angle) * r.radius, r.height, Mathf.Sin(angle) * r.radius);
        }

        /// <summary>
        /// The four rings by number, from the base upwards.
        /// </summary>
        private Ring GetRing(int ring)
        {
            switch (ring) {
                case 0: return bottomRim;
                case 1: return lowerBelly;
                case 2: return upperBelly;
                default: return topRim;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: runs whenever a value is changed in the Inspector.
        /// Keeps every ring wide enough and above the one below it.
        /// </summary>
        private void OnValidate()
        {
            bottomRim.radius = Mathf.Max(bottomRim.radius, MinSize);
            lowerBelly.radius = Mathf.Max(lowerBelly.radius, MinSize);
            upperBelly.radius = Mathf.Max(upperBelly.radius, MinSize);
            topRim.radius = Mathf.Max(topRim.radius, MinSize);

            lowerBelly.height = Mathf.Max(lowerBelly.height, bottomRim.height + MinSize);
            upperBelly.height = Mathf.Max(upperBelly.height, lowerBelly.height + MinSize);
            topRim.height = Mathf.Max(topRim.height, upperBelly.height + MinSize);
        }

        /// <summary>
        /// Editor-only: draws the outline while the object is selected. It
        /// is not registered for the in-headset view - it is a tool for
        /// tuning in the Scene view, and registering would give every barrel
        /// in a level work to do while the game runs.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// Editor-only, on the component's right-click menu: builds the mesh
        /// from the four rings, saves it as an asset at `meshAssetPath` and
        /// gives it to this object's MeshCollider. If the asset is already
        /// there it is filled in again rather than replaced, so colliders
        /// that already use it keep pointing at it.
        /// </summary>
        [ContextMenu("Bake To Mesh Asset")]
        private void BakeToMeshAsset()
        {
            if (string.IsNullOrEmpty(meshAssetPath) || !meshAssetPath.StartsWith("Assets/") || !meshAssetPath.EndsWith(".asset")) {
                Debug.LogError("BarrelCollider: the mesh asset path must start with \"Assets/\" and end with \".asset\".", this);
                return;
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            bool isNew = mesh == null;

            if (isNew) {
                mesh = new Mesh();
            }

            FillMesh(mesh);

            if (isNew) {
                // Make the folder first: CreateAsset fails without it.
                Directory.CreateDirectory(Path.GetDirectoryName(meshAssetPath));
                AssetDatabase.Refresh();
                AssetDatabase.CreateAsset(mesh, meshAssetPath);
            } else {
                EditorUtility.SetDirty(mesh);
            }

            AssetDatabase.SaveAssets();

            MeshCollider meshCollider = GetComponent<MeshCollider>();

            // Recorded so the change is saved with the prefab (and can be undone).
            Undo.RecordObject(meshCollider, "Bake Barrel Collider");

            // Clearing the collider's mesh first makes it take the new shape:
            // given the same Mesh object again, it would keep the old one.
            meshCollider.sharedMesh = null;
            meshCollider.convex = true;
            meshCollider.sharedMesh = mesh;

            PrefabUtility.RecordPrefabInstancePropertyModifications(meshCollider);

            Debug.Log($"BarrelCollider: baked to {meshAssetPath}.", mesh);
        }

        /// <summary>
        /// Writes the shape into a mesh: 48 corners (four rings of twelve),
        /// joined by the side faces and the flat bottom and top.
        /// </summary>
        private void FillMesh(Mesh mesh)
        {
            Vector3[] corners = new Vector3[RingCount * Sides];

            // Corner number = ring * Sides + side.
            for (int ring = 0; ring < RingCount; ring++) {
                for (int side = 0; side < Sides; side++) {
                    corners[ring * Sides + side] = Corner(ring, side);
                }
            }

            // Each side face between two rings is two triangles; the bottom
            // and top are each a fan of ten triangles from their first corner.
            int sideTriangles = (RingCount - 1) * Sides * 2;
            int capTriangles = (Sides - 2) * 2;
            int[] triangles = new int[(sideTriangles + capTriangles) * 3];
            int t = 0;

            // The sides. Clockwise seen from outside, which is the way
            // Unity's triangles face.
            for (int ring = 0; ring < RingCount - 1; ring++) {
                for (int side = 0; side < Sides; side++) {
                    int nextSide = (side + 1) % Sides;
                    int low = ring * Sides + side;
                    int lowNext = ring * Sides + nextSide;
                    int high = low + Sides;
                    int highNext = lowNext + Sides;

                    triangles[t++] = low;
                    triangles[t++] = high;
                    triangles[t++] = lowNext;

                    triangles[t++] = lowNext;
                    triangles[t++] = high;
                    triangles[t++] = highNext;
                }
            }

            int topFirst = (RingCount - 1) * Sides;

            for (int side = 1; side < Sides - 1; side++) {
                // The bottom, facing down.
                triangles[t++] = 0;
                triangles[t++] = side;
                triangles[t++] = side + 1;

                // The top, facing up.
                triangles[t++] = topFirst;
                triangles[t++] = topFirst + side + 1;
                triangles[t++] = topFirst + side;
            }

            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(meshAssetPath);
            mesh.vertices = corners;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }
#endif
    }
}
