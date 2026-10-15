using Core;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Measures what a piece is walked on, for the catalogue: the walking
    /// surface from its colliders, and the slope of every RampCollider.
    ///
    /// Floors are lined up by their colliders rather than their meshes: the
    /// player walks on the collider, so pieces laid edge to edge make one
    /// even surface when their collider tops meet at the same height, however
    /// uneven the painted boards or stones are. The surface is the height
    /// holding the most upward-facing collider area (1cm bins, as the back
    /// planes are found), and the box round it.
    /// </summary>
    internal static class WalkSurface
    {
        /// <summary>
        /// Fills in the entry's walking surface and ramps. Colliders that are
        /// off, triggers, or a RampCollider's own are left out of the surface.
        /// </summary>
        internal static void Measure(CatalogueEntry entry, GameObject root, Matrix4x4 toRoot)
        {
            WallOrientation.Side up = new(1, 1f, "+Y");
            WallOrientation.Side[] sides = { up };

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) {
                    continue;
                }

                if (collider.TryGetComponent(out RampCollider _)) {
                    continue;
                }

                Matrix4x4 matrix = toRoot * collider.transform.localToWorldMatrix;

                switch (collider) {
                    case BoxCollider box:
                        AddBox(sides, box, matrix);
                        break;
                    case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                        WallOrientation.ReadTriangles(meshCollider.sharedMesh, matrix, (a, b, c) => WallOrientation.AddTriangle(sides, a, b, c));
                        break;
                }
            }

            WallOrientation.FindPlane(up);

            if (up.Area > 0f && WallOrientation.TryGetPlaneBox(up, out Bounds plane)) {
                float height = plane.center.y;
                entry.hasWalkSurface = true;
                entry.walkHeight = WallOrientation.Round(height);
                entry.walkShare = WallOrientation.Round(up.Planarity);
                entry.walkMin = WallOrientation.Round(new Vector3(plane.min.x, height, plane.min.z));
                entry.walkMax = WallOrientation.Round(new Vector3(plane.max.x, height, plane.max.z));
            }

            MeasureRamps(entry, root, toRoot);
        }

        /// <summary>
        /// Adds a box collider's six faces as triangles in root space, each
        /// wound to face out of the box (also under a mirrored matrix).
        /// </summary>
        private static void AddBox(WallOrientation.Side[] sides, BoxCollider box, Matrix4x4 matrix)
        {
            Vector3 half = box.size * 0.5f;

            for (int axis = 0; axis < 3; axis++) {
                for (int sign = -1; sign <= 1; sign += 2) {
                    Vector3 normal = Vector3.zero;
                    normal[axis] = sign;
                    Vector3 u = Vector3.zero;
                    u[(axis + 1) % 3] = half[(axis + 1) % 3];
                    Vector3 v = Vector3.zero;
                    v[(axis + 2) % 3] = half[(axis + 2) % 3];
                    Vector3 centre = box.center + normal * half[axis];

                    Vector3 a = matrix.MultiplyPoint3x4(centre - u - v);
                    Vector3 b = matrix.MultiplyPoint3x4(centre + u - v);
                    Vector3 c = matrix.MultiplyPoint3x4(centre + u + v);
                    Vector3 d = matrix.MultiplyPoint3x4(centre - u + v);

                    // The face's outward way in root space; the corners are taken
                    // the other way round if their winding points inwards.
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), matrix.MultiplyVector(normal)) < 0f) {
                        (b, d) = (d, b);
                    }

                    WallOrientation.AddTriangle(sides, a, b, c);
                    WallOrientation.AddTriangle(sides, a, c, d);
                }
            }
        }

        /// <summary>
        /// Every RampCollider's slope, from its own position and axes and its
        /// three sizes (read through SerializedObject, as they are private):
        /// the slope starts on the floor at the object's position and reaches
        /// height at length along its +Z, width centred across it.
        /// </summary>
        private static void MeasureRamps(CatalogueEntry entry, GameObject root, Matrix4x4 toRoot)
        {
            foreach (RampCollider ramp in root.GetComponentsInChildren<RampCollider>(true)) {
                if (!ramp.gameObject.activeInHierarchy) {
                    continue;
                }

                SerializedObject serialized = new(ramp);
                float length = serialized.FindProperty("length").floatValue;
                float height = serialized.FindProperty("height").floatValue;
                float width = serialized.FindProperty("width").floatValue;
                Matrix4x4 matrix = toRoot * ramp.transform.localToWorldMatrix;
                Vector3 forward = matrix.MultiplyVector(Vector3.forward);
                forward.y = 0f;

                entry.ramps.Add(new RampInfo {
                    foot = WallOrientation.Round(matrix.MultiplyPoint3x4(Vector3.zero)),
                    top = WallOrientation.Round(matrix.MultiplyPoint3x4(new Vector3(0f, height, length))),
                    direction = WallOrientation.Round(forward.normalized),
                    width = WallOrientation.Round(matrix.MultiplyVector(Vector3.right).magnitude * width)
                });
            }
        }
    }
}
