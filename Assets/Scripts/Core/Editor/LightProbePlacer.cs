using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Place Light Probes) that fills the
    /// level's light probe group: a probe at two heights above every
    /// floor the player could stand on, on a regular grid, so moving
    /// things (guards, props, hands) and the small pieces lit by probes
    /// pick up the baked light and shadow wherever they are.
    ///
    /// Floors are found with rays straight down on Environment colliders,
    /// every floor in a column (cellar, ground, upper storeys, roofs). A
    /// probe is left out where it would be inside geometry, under a
    /// ceiling lower than its height, or shut inside a building that has
    /// no interior. Run it again after the layout changes, then rebake:
    /// it replaces the group's probes each time.
    ///
    /// In an Editor folder, so it never ends up in a build.
    /// </summary>
    public static class LightProbePlacer
    {
        // Where the group lives: level-wide lighting, under the Global sector
        // (see Assets/Scenes/CLAUDE.md). Missing groups on the way are made.
        private static readonly string[] _groupPath = { "Environment", "Sectors", "Global", "Lighting", "Light Probes" };
        private const string EnvironmentLayerName = "Environment";

        // Grid spacing across the level, in metres.
        private const float Spacing = 2.5f;

        // Heights above each floor: about knee height and above head height,
        // so the light is blended between them for anything standing there.
        private static readonly float[] _heights = { 0.5f, 2f };

        // A floor is anything flatter than this (the y of its normal; 0.7 is
        // about 45 degrees), which keeps the steps' ramps and leaves out walls.
        private const float MinFloorNormalY = 0.7f;

        // How close to geometry a probe may be, and how close two floors in
        // one column may be before the second gives no new probes (a floor
        // covering lying on a floor).
        private const float Clearance = 0.15f;
        private const float MinFloorGap = 0.5f;

        // The most floors looked for in one column.
        private const int MaxFloorsPerColumn = 32;

        // A probe is shut inside a building when at least this many of the
        // six rays out from it first hit the inside of a mesh collider.
        private const int InsideHitsToSkip = 3;

        private static readonly Vector3[] _directions = {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };

        [MenuItem("TeaLeaf/Place Light Probes")]
        internal static void Place()
        {
            int layer = LayerMask.NameToLayer(EnvironmentLayerName);

            if (layer < 0) {
                Debug.LogError($"LightProbePlacer: there is no '{EnvironmentLayerName}' layer.");
                return;
            }

            int mask = 1 << layer;

            if (!TryGetLevelBounds(layer, out Bounds level)) {
                Debug.LogWarning("LightProbePlacer: the scene has no colliders on the Environment layer.");
                return;
            }

            // Edit-mode queries use where the colliders are now.
            Physics.SyncTransforms();
            List<Vector3> probes = new();
            bool hitBackfaces = Physics.queriesHitBackfaces;

            try {
                int columnsX = Mathf.CeilToInt(level.size.x / Spacing) + 1;
                int columnsZ = Mathf.CeilToInt(level.size.z / Spacing) + 1;

                for (int ix = 0; ix < columnsX; ix++) {
                    if (EditorUtility.DisplayCancelableProgressBar("Place Light Probes", $"Column row {ix + 1} of {columnsX}", (float)ix / columnsX)) {
                        Debug.LogWarning("LightProbePlacer: cancelled - nothing changed.");
                        return;
                    }

                    for (int iz = 0; iz < columnsZ; iz++) {
                        float x = level.min.x + ix * Spacing;
                        float z = level.min.z + iz * Spacing;
                        AddColumn(new Vector3(x, level.max.y + 1f, z), level.size.y + 2f, mask, probes);
                    }
                }
            } finally {
                Physics.queriesHitBackfaces = hitBackfaces;
                EditorUtility.ClearProgressBar();
            }

            LightProbeGroup group = FindOrMakeGroup();
            Undo.RecordObject(group, "Place Light Probes");
            Transform groupTransform = group.transform;

            // The group stores its probes in its own local space.
            Vector3[] local = new Vector3[probes.Count];

            for (int i = 0; i < probes.Count; i++) {
                local[i] = groupTransform.InverseTransformPoint(probes[i]);
            }

            group.probePositions = local;
            EditorUtility.SetDirty(group);
            EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
            Selection.activeGameObject = group.gameObject;
            Debug.Log($"LightProbePlacer: placed {probes.Count} light probes. Generate Lighting to bake them.");
        }

        /// <summary>
        /// Finds every floor in one column, from the top down, and adds the
        /// probes above each one that pass the checks.
        /// </summary>
        private static void AddColumn(Vector3 top, float depth, int mask, List<Vector3> probes)
        {
            Vector3 from = top;
            float remaining = depth;
            float lastFloorY = float.PositiveInfinity;

            for (int i = 0; i < MaxFloorsPerColumn && remaining > 0f; i++) {
                // Downward sweep: only surfaces facing the ray (tops of things).
                Physics.queriesHitBackfaces = false;

                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, remaining, mask, QueryTriggerInteraction.Ignore)) {
                    return;
                }

                // Carry on from just below this surface for the next floor down.
                float step = hit.distance + 0.01f;
                from += Vector3.down * step;
                remaining -= step;

                if (hit.normal.y < MinFloorNormalY || lastFloorY - hit.point.y < MinFloorGap) {
                    continue;
                }

                lastFloorY = hit.point.y;

                foreach (float height in _heights) {
                    Vector3 probe = hit.point + Vector3.up * height;

                    if (IsClear(hit.point, probe, mask)) {
                        probes.Add(probe);
                    }
                }
            }
        }

        /// <summary>
        /// Whether a probe at this point would see the level: not inside or
        /// touching geometry, with nothing between it and the floor below
        /// it (a ceiling lower than the probe), and not shut inside a
        /// building with no interior - found by rays out from the probe
        /// that hit the inside of a mesh collider first.
        /// </summary>
        private static bool IsClear(Vector3 floor, Vector3 probe, int mask)
        {
            Physics.queriesHitBackfaces = false;

            if (Physics.CheckSphere(probe, Clearance, mask, QueryTriggerInteraction.Ignore)) {
                return false;
            }

            Vector3 start = floor + Vector3.up * 0.02f;

            if (Physics.Linecast(start, probe, mask, QueryTriggerInteraction.Ignore)) {
                return false;
            }

            // With back faces on, a ray from inside a closed mesh hits it from
            // the inside first: its face normal then points along the ray.
            Physics.queriesHitBackfaces = true;
            int insideHits = 0;

            foreach (Vector3 direction in _directions) {
                if (Physics.Raycast(probe, direction, out RaycastHit hit, 100f, mask, QueryTriggerInteraction.Ignore)
                    && Vector3.Dot(hit.normal, direction) > 0f) {
                    insideHits++;
                }
            }

            return insideHits < InsideHitsToSkip;
        }

        /// <summary>
        /// The box around every enabled, non-trigger collider on the
        /// Environment layer in the open scenes. False if there are none.
        /// </summary>
        private static bool TryGetLevelBounds(int layer, out Bounds bounds)
        {
            bounds = default;
            bool found = false;

            foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)) {
                if (!collider.enabled || collider.isTrigger || collider.gameObject.layer != layer) {
                    continue;
                }

                if (found) {
                    bounds.Encapsulate(collider.bounds);
                } else {
                    bounds = collider.bounds;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// The light probe group at Environment/Sectors/Global/Lighting/Light
        /// Probes, made (with any missing groups on the way, at the origin)
        /// if it isn't there yet.
        /// </summary>
        private static LightProbeGroup FindOrMakeGroup()
        {
            Transform parent = null;

            foreach (string name in _groupPath) {
                Transform next = FindChild(parent, name);

                if (next == null) {
                    GameObject made = new(name);
                    Undo.RegisterCreatedObjectUndo(made, "Place Light Probes");

                    if (parent != null) {
                        Undo.SetTransformParent(made.transform, parent, "Place Light Probes");
                        made.transform.localPosition = Vector3.zero;
                        made.transform.localRotation = Quaternion.identity;
                        made.transform.localScale = Vector3.one;
                    }

                    next = made.transform;
                }

                parent = next;
            }

            if (!parent.TryGetComponent(out LightProbeGroup group)) {
                group = Undo.AddComponent<LightProbeGroup>(parent.gameObject);
            }

            return group;
        }

        /// <summary>
        /// The child of parent with this name, or the root object with this
        /// name in the active scene when parent is null.
        /// </summary>
        private static Transform FindChild(Transform parent, string name)
        {
            if (parent != null) {
                return parent.Find(name);
            }

            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects()) {
                if (root.name == name) {
                    return root.transform;
                }
            }

            return null;
        }
    }
}
