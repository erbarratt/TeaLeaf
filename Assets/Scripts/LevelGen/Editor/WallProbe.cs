using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Finds where a ray meets the drawn surface of the pieces already placed
    /// (the walls, their timbers and trim), for the dressing: a capped end
    /// put on the wall where a pipe goes in, a vent seated on the wall and the
    /// test that the wall is smooth under it. The pieces' colliders are mostly
    /// plain boxes, so they only say which pieces are near the ray; the ray is
    /// then tested against those pieces' LOD0 meshes, triangle by triangle.
    /// The dressing's own pieces (pipes, vents, lamp posts) are never hit.
    ///
    /// Editor only, while generating: Begin() clears the mesh cache and syncs
    /// the physics scene with what was just placed.
    /// </summary>
    internal static class WallProbe
    {
        // How far round the ray to look for pieces' colliders.
        private const float SearchRadius = 0.3f;

        private static readonly Dictionary<Mesh, (Vector3[] vertices, int[] triangles)> _meshes = new();
        private static readonly Collider[] _colliders = new Collider[64];
        private static readonly HashSet<GameObject> _roots = new();
        private static readonly List<MeshFilter> _filters = new();

        /// <summary>
        /// Makes ready for a generation's probes: forgets cached meshes and
        /// brings the physics scene up to date with the pieces placed so far.
        /// </summary>
        internal static void Begin()
        {
            _meshes.Clear();
            Physics.SyncTransforms();
        }

        /// <summary>
        /// The distance along a ray (origin, way - a unit vector) to the first
        /// drawn surface of a placed piece within maxDistance; false if none.
        /// </summary>
        internal static bool Cast(Vector3 origin, Vector3 way, float maxDistance, out float distance)
        {
            distance = float.MaxValue;
            int count = Physics.OverlapCapsuleNonAlloc(origin, origin + way * maxDistance, SearchRadius, _colliders, ~0, QueryTriggerInteraction.Collide);
            _roots.Clear();

            for (int i = 0; i < count; i++) {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(_colliders[i].gameObject);
                _roots.Add(root != null ? root : _colliders[i].gameObject);
            }

            foreach (GameObject root in _roots) {
                if (IsDressing(root.name)) {
                    continue;
                }

                root.GetComponentsInChildren(false, _filters);

                foreach (MeshFilter filter in _filters) {
                    if (IsLowerLodOrCollision(filter.name) || filter.sharedMesh == null) {
                        continue;
                    }

                    Renderer renderer = filter.GetComponent<Renderer>();

                    if (renderer == null || !renderer.enabled) {
                        continue;
                    }

                    if (CastMesh(filter, origin, way, maxDistance, out float hit) && hit < distance) {
                        distance = hit;
                    }
                }
            }

            return distance <= maxDistance;
        }

        /// <summary>
        /// The pieces the probe looks through: the dressing itself.
        /// </summary>
        private static bool IsDressing(string name)
        {
            return name.StartsWith("TL_Pipe") || name.StartsWith("TL_Lampost") || name.StartsWith("TL_IronFixtures");
        }

        /// <summary>
        /// A child that isn't drawn at LOD0: a lower LOD or a collision mesh.
        /// </summary>
        internal static bool IsLowerLodOrCollision(string name)
        {
            return name.Contains("LOD1") || name.Contains("LOD2") || name.Contains("LOD3") || name.Contains("Collision");
        }

        /// <summary>
        /// The ray against one mesh, in the mesh's own space (the ray's
        /// direction is carried over unnormalised, so its parameter stays in
        /// world metres): the nearest triangle hit, either side facing.
        /// </summary>
        private static bool CastMesh(MeshFilter filter, Vector3 origin, Vector3 way, float maxDistance, out float distance)
        {
            distance = float.MaxValue;
            Transform transform = filter.transform;
            Vector3 localOrigin = transform.InverseTransformPoint(origin);
            Vector3 localWay = transform.InverseTransformVector(way);
            Mesh mesh = filter.sharedMesh;
            Bounds bounds = mesh.bounds;
            bounds.Expand(0.01f);

            if (!bounds.IntersectRay(new Ray(localOrigin, localWay)) && !bounds.Contains(localOrigin)) {
                return false;
            }

            if (!_meshes.TryGetValue(mesh, out (Vector3[] vertices, int[] triangles) data)) {
                data = (mesh.vertices, mesh.triangles);
                _meshes.Add(mesh, data);
            }

            Vector3[] v = data.vertices;
            int[] t = data.triangles;

            for (int i = 0; i + 2 < t.Length; i += 3) {
                if (RayTriangle(localOrigin, localWay, v[t[i]], v[t[i + 1]], v[t[i + 2]], out float hit) && hit < distance && hit <= maxDistance) {
                    distance = hit;
                }
            }

            return distance <= maxDistance;
        }

        /// <summary>
        /// Möller-Trumbore ray-triangle test, either side facing: the ray's
        /// parameter at the hit, if it is ahead of the origin.
        /// </summary>
        internal static bool RayTriangle(Vector3 origin, Vector3 way, Vector3 a, Vector3 b, Vector3 c, out float hit)
        {
            hit = 0f;
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 p = Vector3.Cross(way, ac);
            float det = Vector3.Dot(ab, p);

            if (Mathf.Abs(det) < 1e-9f) {
                return false;
            }

            float inverse = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inverse;

            if (u < 0f || u > 1f) {
                return false;
            }

            Vector3 q = Vector3.Cross(s, ab);
            float w = Vector3.Dot(way, q) * inverse;

            if (w < 0f || u + w > 1f) {
                return false;
            }

            hit = Vector3.Dot(ac, q) * inverse;
            return hit >= 0f;
        }
    }
}
