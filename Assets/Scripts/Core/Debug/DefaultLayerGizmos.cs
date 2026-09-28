using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Outlines every enabled collider left on the Default layer, in the
    /// Scene view (edit and play mode) and in the headset while
    /// InHeadsetGizmos is on - a quick way to spot objects that were never
    /// given a proper layer. Default collides with every layer, so a
    /// forgotten one can block the player, the hands or thrown objects in
    /// ways the collision matrix was set up to prevent.
    ///
    /// Only colliders are checked: the layer matters for physics, and plenty
    /// of collider-less objects (lights, empty groups, runtime-built markers)
    /// are fine on Default.
    ///
    /// Put it on the Debug object; disable it to turn the outlines off.
    /// There's no registry to find these objects (they have none of our
    /// components), so it searches the scene - every rescanInterval seconds,
    /// not every frame. That search allocates, which is fine for a debug tool
    /// but is why this is one.
    /// </summary>
    public class DefaultLayerGizmos : MonoBehaviour, IDebugDrawable
    {
        // Seconds between scene searches - objects added, removed or moved
        // to another layer show up within this long. Positions are read
        // every draw, so moving objects are always outlined where they are.
        [SerializeField] private float rescanInterval = 1f;

        // Layer 0 is always Default - it can't be renamed or removed.
        private const int DefaultLayer = 0;

        // Magenta, the "something's wrong" colour.
        private static readonly Color _gizmoColor = new(1f, 0.2f, 1f, 1f);

        private readonly List<Collider> _colliders = new();

        // When the next search is due, on the real-time clock, which also
        // runs in the editor outside Play Mode.
        private float _nextScanTime;

        private void OnEnable()
        {
            DebugDrawRegistry.Register(this);

            // Search on the next draw, rather than showing an old list.
            _nextScanTime = 0f;
        }

        private void OnDisable()
        {
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// Always draws the outlines in the Scene view while this component
        /// is enabled - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            // Unity can call this on a disabled component, so check.
            if (!isActiveAndEnabled) {
                return;
            }

            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// Outlines each collider on the Default layer: boxes, spheres and
        /// capsules as their real shape, anything else (e.g. a MeshCollider)
        /// as its world-space bounding box. The same either way, so detailed
        /// is ignored.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (Time.realtimeSinceStartup >= _nextScanTime) {
                Rescan();
            }

            lines.Color = _gizmoColor;

            for (int i = 0; i < _colliders.Count; i++) {
                Collider target = _colliders[i];

                // Destroyed, disabled or moved off Default since the last
                // search - skip it until the next one drops it.
                if (target == null || !target.enabled || target.gameObject.layer != DefaultLayer) {
                    continue;
                }

                DrawCollider(lines, target);
            }

            lines.Matrix = Matrix4x4.identity;
        }

        /// <summary>
        /// Refills the list with every active, enabled collider on the
        /// Default layer.
        /// </summary>
        private void Rescan()
        {
            _nextScanTime = Time.realtimeSinceStartup + Mathf.Max(rescanInterval, 0.1f);
            _colliders.Clear();

            Collider[] all = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (Collider found in all) {
                if (found.enabled && found.gameObject.layer == DefaultLayer) {
                    _colliders.Add(found);
                }
            }
        }

        /// <summary>
        /// Draws one collider's shape in world space. Sphere and capsule
        /// sizes are scaled the way PhysX scales them: a sphere by the
        /// largest axis of the object's scale, a capsule's length by the
        /// scale along it and its radius by the largest of the other two.
        /// </summary>
        private static void DrawCollider(DebugLines lines, Collider collider)
        {
            Transform t = collider.transform;

            switch (collider) {
                case BoxCollider box:
                    // In the box's own scaled local space, so it's drawn
                    // rotated exactly like the collider.
                    lines.Matrix = t.localToWorldMatrix;
                    lines.WireCube(box.center, box.size);
                    lines.Matrix = Matrix4x4.identity;
                    break;

                case SphereCollider sphere: {
                    Vector3 scale = Abs(t.lossyScale);
                    float radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                    lines.WireSphere(t.TransformPoint(sphere.center), radius);
                    break;
                }

                case CapsuleCollider capsule: {
                    Vector3 scale = Abs(t.lossyScale);

                    // direction: 0 = X, 1 = Y, 2 = Z.
                    Vector3 axis;
                    float axisScale;
                    float radiusScale;

                    if (capsule.direction == 0) {
                        axis = t.right;
                        axisScale = scale.x;
                        radiusScale = Mathf.Max(scale.y, scale.z);
                    } else if (capsule.direction == 1) {
                        axis = t.up;
                        axisScale = scale.y;
                        radiusScale = Mathf.Max(scale.x, scale.z);
                    } else {
                        axis = t.forward;
                        axisScale = scale.z;
                        radiusScale = Mathf.Max(scale.x, scale.y);
                    }

                    float radius = capsule.radius * radiusScale;

                    // From the centre to each end sphere's centre - the height
                    // includes the rounded ends, so they're taken off.
                    float halfLine = Mathf.Max(capsule.height * axisScale * 0.5f - radius, 0f);
                    Vector3 centre = t.TransformPoint(capsule.center);

                    lines.WireCapsule(centre - axis * halfLine, centre + axis * halfLine, radius);
                    break;
                }

                default: {
                    Bounds bounds = collider.bounds;
                    lines.WireCube(bounds.center, bounds.size);
                    break;
                }
            }
        }

        /// <summary>
        /// A scale with every axis made positive - a mirrored object (-1)
        /// is still the same size.
        /// </summary>
        private static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }
    }
}
