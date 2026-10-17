using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Takes the colliders off the roof pieces that face into the void
    /// beyond the level, round its edge (LevelGenerator, after the roof
    /// cull). Each roof piece under the generated root's Roofs faces out of
    /// its building (its roof group, Roofs/.../Building i): a slope panel
    /// down its slope (its +Z, tilted up), anything else (gables, hip
    /// pieces) away from the middle of its building. A box is put beyond the
    /// building that way: from RoofEdgeGap past the building's furthest
    /// point to RoofEdgeReach further, as wide as the piece, from the piece's
    /// top down Depth. If nothing on the Environment layer is in it - no
    /// ground, no building, nothing placed by hand - the piece faces the
    /// void, and every collider on it is removed (an override, the prefab
    /// untouched). Its renderers stay.
    /// </summary>
    internal static class RoofEdgeColliders
    {
        // How far below a piece's top the box looks for anything.
        private const float Depth = 100f;
        // A piece whose +Z rises at least this steeply is a slope panel,
        // facing down its slope.
        private const float PanelRise = 0.3f;

        /// <summary>
        /// One roof piece: its root, its building, and the corners of its
        /// meshes' boxes in world space.
        /// </summary>
        private class Piece
        {
            public Transform Root;
            public Transform Group;
            public Vector3[] Corners;
        }

        /// <summary>
        /// Removes the colliders of the roofs under roofsRoot that face the
        /// void. Adds one line to log.
        /// </summary>
        internal static void Run(Transform roofsRoot, GeneratorSettings settings, List<string> log)
        {
            Physics.SyncTransforms();
            List<Piece> pieces = Gather(roofsRoot);
            // The middle of each building's roof, from all its pieces' corners.
            Dictionary<Transform, Bounds> groups = new();

            foreach (Piece piece in pieces) {
                if (!groups.TryGetValue(piece.Group, out Bounds bounds)) {
                    bounds = new Bounds(piece.Corners[0], Vector3.zero);
                }

                foreach (Vector3 corner in piece.Corners) {
                    bounds.Encapsulate(corner);
                }

                groups[piece.Group] = bounds;
            }

            int mask = LayerMask.GetMask("Environment");
            List<Piece> facingVoid = new();

            foreach (Piece piece in pieces) {
                if (FacesVoid(piece, groups[piece.Group].center, pieces, mask, settings)) {
                    facingVoid.Add(piece);
                }
            }

            // Removed only once every piece is tried, so no piece's test
            // sees another's colliders already gone.
            int colliders = 0;

            foreach (Piece piece in facingVoid) {
                foreach (Collider collider in piece.Root.GetComponentsInChildren<Collider>(true)) {
                    Object.DestroyImmediate(collider);
                    colliders++;
                }
            }

            log.Add($"Roof edge: {colliders} collider(s) removed from {facingVoid.Count} of {pieces.Count} roof piece(s) facing the void "
                + $"(nothing from {settings.RoofEdgeGap}m to {settings.RoofEdgeGap + settings.RoofEdgeReach}m beyond their building).");
        }

        /// <summary>
        /// Whether nothing on the mask lies in the box beyond the piece's
        /// building, the way the piece faces.
        /// </summary>
        private static bool FacesVoid(Piece piece, Vector3 groupCentre, List<Piece> pieces, int mask, GeneratorSettings settings)
        {
            Vector3 middle = Vector3.zero;

            foreach (Vector3 corner in piece.Corners) {
                middle += corner;
            }

            middle /= piece.Corners.Length;
            Vector3 forward = piece.Root.forward;
            Vector3 outward = forward.y >= PanelRise ? forward : middle - groupCentre;
            outward.y = 0f;

            if (outward.sqrMagnitude < 1e-6f) {
                return false;
            }

            outward.Normalize();
            Vector3 across = Vector3.Cross(Vector3.up, outward);
            // The building's furthest point out, and the piece's width
            // across and top, along those axes.
            float furthest = float.NegativeInfinity;

            foreach (Piece other in pieces) {
                if (other.Group != piece.Group) {
                    continue;
                }

                foreach (Vector3 corner in other.Corners) {
                    furthest = Mathf.Max(furthest, Vector3.Dot(corner, outward));
                }
            }

            float acrossMin = float.PositiveInfinity;
            float acrossMax = float.NegativeInfinity;
            float top = float.NegativeInfinity;

            foreach (Vector3 corner in piece.Corners) {
                float a = Vector3.Dot(corner, across);
                acrossMin = Mathf.Min(acrossMin, a);
                acrossMax = Mathf.Max(acrossMax, a);
                top = Mathf.Max(top, corner.y);
            }

            float reach = settings.RoofEdgeReach;
            Vector3 centre = outward * (furthest + settings.RoofEdgeGap + reach * 0.5f) + across * ((acrossMin + acrossMax) * 0.5f) + Vector3.up * (top - Depth * 0.5f);
            Vector3 half = new(Mathf.Max((acrossMax - acrossMin) * 0.5f, 0.05f), Depth * 0.5f, reach * 0.5f);
            return !Physics.CheckBox(centre, half, Quaternion.LookRotation(outward, Vector3.up), mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Every roof piece under the root (an object named like a prefab
        /// variant, TL_..., not inside another), with the world corners of
        /// its LOD0 meshes' boxes (all its meshes if it has no LODs).
        /// </summary>
        private static List<Piece> Gather(Transform roofsRoot)
        {
            List<Piece> pieces = new();
            List<MeshRenderer> renderers = new();
            List<Vector3> corners = new();

            foreach (Transform t in roofsRoot.GetComponentsInChildren<Transform>(true)) {
                if (!t.name.StartsWith("TL_") || InsidePiece(t.parent, roofsRoot)) {
                    continue;
                }

                renderers.Clear();
                corners.Clear();
                t.GetComponentsInChildren(true, renderers);
                bool lods = renderers.Exists(r => r.name.EndsWith("_LOD0"));

                foreach (MeshRenderer renderer in renderers) {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();

                    if ((lods && !renderer.name.EndsWith("_LOD0")) || filter == null || filter.sharedMesh == null) {
                        continue;
                    }

                    Bounds mesh = filter.sharedMesh.bounds;

                    for (int c = 0; c < 8; c++) {
                        Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                        corners.Add(renderer.transform.TransformPoint(corner));
                    }
                }

                if (corners.Count > 0) {
                    pieces.Add(new Piece { Root = t, Group = t.parent, Corners = corners.ToArray() });
                }
            }

            return pieces;
        }

        /// <summary>
        /// Whether an object is, or is inside, a placed piece below the root.
        /// </summary>
        private static bool InsidePiece(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent) {
                if (p.name.StartsWith("TL_")) {
                    return true;
                }
            }

            return false;
        }
    }
}
