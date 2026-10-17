using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The roof cull, the last thing a generation does (LevelGenerator): a
    /// probe walks the middle of every street of the spec, RoofCullHeight
    /// above the ground (up and down the flights of steps as walked), and
    /// looks every RoofCullStep metres. Every roof piece under the generated
    /// root's Roofs it can't see from any of those looks is removed, so
    /// slopes nobody in the streets will see (the back slopes of facades,
    /// the hidden sides of lots) cost nothing.
    ///
    /// A piece is seen from a look when a ray from the probe to one of its
    /// sample points (a grid through its render box, from its LOD0 meshes in
    /// its own space, so turned and scaled with it) first hits the piece's
    /// own collider, or nothing before the point. Rays hit the Environment
    /// layer only, so everything static occludes: walls, other roofs,
    /// pieces placed by hand. A piece further than RoofCullRange from a look
    /// isn't tried from it.
    /// </summary>
    internal static class RoofCull
    {
        // How far each sample point is drawn in from the render box's faces
        // towards its middle, as a share of the half size, so a point lies
        // inside the piece (its collider) rather than on its box's edge.
        private const float Shrink = 0.8f;
        // How far short of a sample point a ray may stop and still reach it.
        private const float Reach = 0.05f;

        /// <summary>
        /// One roof piece: its root and its sample points in world space.
        /// </summary>
        private class Piece
        {
            public Transform Root;
            public Vector3[] Points;
            public Bounds World;
            public bool Seen;
        }

        /// <summary>
        /// Culls the roofs under roofsRoot, seen from the streets of spec.
        /// Adds one line to log.
        /// </summary>
        internal static void Run(LayoutSpec spec, List<StreetSteps.Flight> flights, StoreyPlan plan, Transform roofsRoot, GeneratorSettings settings,
            List<string> log)
        {
            Physics.SyncTransforms();
            List<Piece> pieces = Gather(roofsRoot);
            List<Vector3> looks = new();

            foreach (StreetSpec street in spec.streets) {
                StreetTest.Walk(street, flights, plan, settings, settings.RoofCullStep, settings.StreetBallRadius + settings.OverlapTolerance,
                    (surface, segment, travelled) => looks.Add(surface + Vector3.up * settings.RoofCullHeight));
            }

            int mask = LayerMask.GetMask("Environment");
            float range = settings.RoofCullRange;

            foreach (Vector3 look in looks) {
                foreach (Piece piece in pieces) {
                    if (piece.Seen || piece.World.SqrDistance(look) > range * range) {
                        continue;
                    }

                    piece.Seen = SeenFrom(look, piece, mask);
                }
            }

            int removed = 0;

            foreach (Piece piece in pieces) {
                if (piece.Seen) {
                    continue;
                }

                Transform parent = piece.Root.parent;
                Undo.DestroyObjectImmediate(piece.Root.gameObject);
                removed++;

                // A building or roof group left empty goes too.
                while (parent != null && parent != roofsRoot && parent.childCount == 0) {
                    Transform next = parent.parent;
                    Undo.DestroyObjectImmediate(parent.gameObject);
                    parent = next;
                }
            }

            log.Add($"Roof cull: {removed} of {pieces.Count} roof piece(s) removed, unseen from a probe {settings.RoofCullHeight}m above the streets "
                + $"({looks.Count} looks, every {settings.RoofCullStep}m, out to {range}m).");
        }

        /// <summary>
        /// Whether any of a piece's sample points can be seen from a point:
        /// the ray to it first hits the piece itself, or nothing on the way.
        /// </summary>
        private static bool SeenFrom(Vector3 from, Piece piece, int mask)
        {
            foreach (Vector3 point in piece.Points) {
                Vector3 way = point - from;
                float distance = way.magnitude;

                if (distance < Reach) {
                    return true;
                }

                if (!Physics.Raycast(from, way / distance, out RaycastHit hit, distance - Reach, mask, QueryTriggerInteraction.Ignore)
                    || hit.collider.transform.IsChildOf(piece.Root)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every roof piece under the root (an object named like a prefab
        /// variant, TL_..., not inside another), with its sample points: the
        /// middle, the eight corners and the six face middles of its LOD0
        /// render box (all its renderers if it has no LODs), drawn in by
        /// Shrink.
        /// </summary>
        private static List<Piece> Gather(Transform roofsRoot)
        {
            List<Piece> pieces = new();
            List<MeshRenderer> renderers = new();

            foreach (Transform t in roofsRoot.GetComponentsInChildren<Transform>(true)) {
                if (!t.name.StartsWith("TL_") || InsidePiece(t.parent, roofsRoot)) {
                    continue;
                }

                renderers.Clear();
                t.GetComponentsInChildren(true, renderers);
                bool lods = renderers.Exists(r => r.name.EndsWith("_LOD0"));
                Vector3 min = Vector3.positiveInfinity;
                Vector3 max = Vector3.negativeInfinity;

                foreach (MeshRenderer renderer in renderers) {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();

                    if ((lods && !renderer.name.EndsWith("_LOD0")) || filter == null || filter.sharedMesh == null) {
                        continue;
                    }

                    Bounds mesh = filter.sharedMesh.bounds;

                    for (int c = 0; c < 8; c++) {
                        Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                        Vector3 local = t.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                    }
                }

                if (min.x > max.x) {
                    continue;
                }

                Vector3 centre = (min + max) * 0.5f;
                Vector3 half = (max - min) * (0.5f * Shrink);
                List<Vector3> points = new();
                Bounds world = new(t.TransformPoint(centre), Vector3.zero);

                // Of the 3 x 3 x 3 grid, the points with no middle
                // coordinate (the corners), two (the face middles) or three
                // (the box's middle); the edge middles are left out.
                for (int x = -1; x <= 1; x++) {
                    for (int y = -1; y <= 1; y++) {
                        for (int z = -1; z <= 1; z++) {
                            int middles = (x == 0 ? 1 : 0) + (y == 0 ? 1 : 0) + (z == 0 ? 1 : 0);

                            if (middles != 1) {
                                Vector3 point = t.TransformPoint(centre + Vector3.Scale(half, new Vector3(x, y, z)));
                                points.Add(point);
                                world.Encapsulate(point);
                            }
                        }
                    }
                }

                pieces.Add(new Piece { Root = t, Points = points.ToArray(), World = world });
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
