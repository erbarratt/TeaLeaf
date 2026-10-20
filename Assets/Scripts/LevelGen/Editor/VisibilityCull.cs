using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The visibility cull, the last thing a generation does
    /// (LevelGenerator): every piece the generator placed that no one in the
    /// streets can see is removed - ground tiles under and behind the
    /// buildings, the back slopes of roofs, walls, beams, pipes and walkway
    /// parts hidden behind other pieces - so what nobody sees costs nothing.
    /// The lamp posts are left alone: they are lights.
    ///
    /// A probe walks the middle of every street of the spec (up and down the
    /// flights of steps and the slopes as walked), looking every CullStep metres from
    /// CullEyeHeight above the ground - at the middle of the street and at
    /// each edge, a ball's radius in from the buildings - and from CullHeight
    /// above the middle, for the roofs. A look inside something built (an
    /// edge look at a bend, say) is dropped, since from inside a collider a
    /// ray sees through it.
    ///
    /// A piece is seen from a look when a ray from the look to one of its
    /// sample points (a grid through its render box, from its LOD0 meshes in
    /// its own space, so turned and scaled with it) reaches the piece: it
    /// hits nothing on the way, or the first thing it hits is the piece's own
    /// collider, or a point on the piece's render box - the merged box that
    /// stands in for a ground tile's, a foundation's or a walkway's own
    /// colliders. Rays hit the Environment layer only, so everything static
    /// occludes: walls, roofs, pieces placed by hand. A piece further than
    /// CullRange from a look isn't tried from it.
    ///
    /// Each piece is tried from its nearest few looks first, one ray at a
    /// time, stopping at the first that sees it (most pieces are seen from
    /// the street beside them); the pieces still unseen are then tried from
    /// every other look in range in one go, the rays run as parallel jobs
    /// (RaycastCommand), since a hidden piece has to fail from everywhere.
    /// </summary>
    internal static class VisibilityCull
    {
        // How far each sample point is drawn in from the render box's faces
        // towards its middle, as a share of the half size, so a point lies
        // inside the piece (its collider) rather than on its box's edge.
        private const float Shrink = 0.8f;
        // How far outside a piece's render box a ray's hit may lie and still
        // count as reaching the piece (a merged collider box reaches past
        // the pieces it covers by about this much).
        private const float BoxTolerance = 0.05f;
        // The radius of the check that a look isn't inside something built.
        private const float LookClearance = 0.1f;
        // The group under the generated root whose pieces are never culled.
        private const string LampsGroup = "Lamps";
        // How many of a piece's nearest looks are tried one ray at a time
        // before the rest go to the jobs.
        private const int NearLooks = 8;
        // How many rays the jobs run in one batch.
        private const int Batch = 1 << 18;
        // The fewest rays one job takes.
        private const int RaysPerJob = 64;

        /// <summary>
        /// One placed piece: its root, the group it is under, its render box
        /// in its own space, its sample points in world space and the looks
        /// in range of it, nearest first.
        /// </summary>
        private class Piece
        {
            public Transform Root;
            public string Group;
            public Vector3 Min;
            public Vector3 Max;
            // BoxTolerance in the piece's own units, per axis.
            public Vector3 Tolerance;
            public Vector3[] Points;
            public Bounds World;
            public int[] Looks;
            public bool Seen;
        }

        /// <summary>
        /// Culls the pieces under root, seen from the streets of spec. Adds
        /// one line to log.
        /// </summary>
        internal static void Run(LayoutSpec spec, StreetHeights heights, StoreyPlan plan, Transform root, GeneratorSettings settings,
            List<string> log)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            Physics.SyncTransforms();
            List<Piece> pieces = Gather(root);
            int mask = LayerMask.GetMask("Environment");
            List<Vector3> looks = Looks(spec, heights, plan, settings, mask, out int dropped);
            float range = settings.CullRange;
            SortLooks(pieces, looks, range);

            // The nearest looks, one ray at a time, stopping at the first
            // that sees the piece.
            foreach (Piece piece in pieces) {
                for (int i = 0; i < NearLooks && i < piece.Looks.Length && !piece.Seen; i++) {
                    piece.Seen = SeenFrom(looks[piece.Looks[i]], piece, mask);
                }
            }

            // The rest of the looks for whatever is still unseen, as jobs.
            SeenFromFarLooks(pieces, looks, mask);

            // Removed and total, per group, in the order the groups are met.
            List<string> groups = new();
            Dictionary<string, int> totals = new();
            Dictionary<string, int> removed = new();

            foreach (Piece piece in pieces) {
                if (!totals.ContainsKey(piece.Group)) {
                    groups.Add(piece.Group);
                    totals[piece.Group] = 0;
                    removed[piece.Group] = 0;
                }

                totals[piece.Group]++;

                if (piece.Seen) {
                    continue;
                }

                removed[piece.Group]++;
                Transform parent = piece.Root.parent;
                Undo.DestroyObjectImmediate(piece.Root.gameObject);

                // A building or roof group left empty goes too (a group
                // holding merged colliders stays).
                while (parent != null && parent != root && parent.childCount == 0) {
                    Transform next = parent.parent;
                    Undo.DestroyObjectImmediate(parent.gameObject);
                    parent = next;
                }
            }

            // A railing seen over a gable while the ridge piece under it
            // was hidden, and so removed, would stand on nothing: it goes
            // too.
            Physics.SyncTransforms();
            int railings = 0;

            foreach (Piece piece in pieces) {
                if (!piece.Seen || piece.Root == null || !piece.Root.name.StartsWith("TL_IronFixtures_ArowRailing") || RoofUnder(piece.Root, mask)) {
                    continue;
                }

                Undo.DestroyObjectImmediate(piece.Root.gameObject);
                railings++;
            }

            int removedAll = 0;
            List<string> parts = new();

            foreach (string group in groups) {
                removedAll += removed[group];
                parts.Add($"{group.ToLowerInvariant()} {removed[group]} of {totals[group]}");
            }

            log.Add($"Visibility cull: {removedAll} of {pieces.Count} piece(s) removed, unseen from the streets ({string.Join(", ", parts)}; "
                + $"{looks.Count} looks every {settings.CullStep}m, at {settings.CullEyeHeight}m along the middle and both edges and at {settings.CullHeight}m "
                + $"along the middle, out to {range}m" + (dropped > 0 ? $", {dropped} look(s) inside something built dropped" : "") + $"; {watch.Elapsed.TotalSeconds:0.0}s)"
                + (railings > 0 ? $"; {railings} railing(s) on a removed ridge taken too" : "") + ".");
        }

        /// <summary>
        /// Every look along the streets: at eye height on the middle line
        /// and at each edge (a ball's radius plus the overlap tolerance in
        /// from the street's edge, where the buildings stand), and at the
        /// cull height on the middle line. Eye looks inside something built
        /// are left out and counted in dropped.
        /// </summary>
        private static List<Vector3> Looks(LayoutSpec spec, StreetHeights heights, StoreyPlan plan, GeneratorSettings settings, int mask,
            out int dropped)
        {
            List<Vector3> looks = new();
            int lost = 0;
            float margin = settings.StreetBallRadius + settings.OverlapTolerance;

            foreach (StreetSpec street in spec.streets) {
                StreetTest.Walk(street, heights, plan, settings, settings.CullStep, margin, (surface, segment, travelled) => {
                    looks.Add(surface + Vector3.up * settings.CullHeight);
                    Vector3 eye = surface + Vector3.up * settings.CullEyeHeight;
                    looks.Add(eye);

                    Vector3 a = street.points[segment];
                    Vector3 b = street.points[segment + 1];
                    Vector3 side = Vector3.Cross(Vector3.up, new Vector3(b.x - a.x, 0f, b.z - a.z)).normalized;
                    float offset = street.SegmentWidth(segment) * 0.5f - margin;

                    if (offset <= 0f) {
                        return;
                    }

                    foreach (Vector3 edge in new[] { eye + side * offset, eye - side * offset }) {
                        if (Physics.CheckSphere(edge, LookClearance, mask, QueryTriggerInteraction.Ignore)) {
                            lost++;
                        } else {
                            looks.Add(edge);
                        }
                    }
                });
            }

            dropped = lost;
            return looks;
        }

        /// <summary>
        /// Gives every piece the looks within range of its render box,
        /// nearest first.
        /// </summary>
        private static void SortLooks(List<Piece> pieces, List<Vector3> looks, float range)
        {
            List<KeyValuePair<float, int>> near = new();
            Comparison<KeyValuePair<float, int>> byDistance = (a, b) => a.Key.CompareTo(b.Key);

            foreach (Piece piece in pieces) {
                near.Clear();

                for (int i = 0; i < looks.Count; i++) {
                    float distance = piece.World.SqrDistance(looks[i]);

                    if (distance <= range * range) {
                        near.Add(new KeyValuePair<float, int>(distance, i));
                    }
                }

                near.Sort(byDistance);
                piece.Looks = new int[near.Count];

                for (int i = 0; i < near.Count; i++) {
                    piece.Looks[i] = near[i].Value;
                }
            }
        }

        /// <summary>
        /// Tries every still-unseen piece from its looks past the nearest
        /// NearLooks, all its sample points from each, the rays run as
        /// parallel jobs a batch at a time. A piece's rays come one after
        /// another, so one seen in a batch is dropped from the next.
        /// </summary>
        private static void SeenFromFarLooks(List<Piece> pieces, List<Vector3> looks, int mask)
        {
            QueryParameters query = new(mask, false, QueryTriggerInteraction.Ignore, false);
            NativeArray<RaycastCommand> commands = new(Batch, Allocator.TempJob);
            NativeArray<RaycastHit> hits = new(Batch, Allocator.TempJob);
            int[] pieceOf = new int[Batch];
            // Where the stream of rays has got to: the piece, its look and
            // its sample point.
            int p = 0;
            int l = NearLooks;
            int k = 0;

            while (true) {
                int count = 0;

                while (count < Batch && p < pieces.Count) {
                    Piece piece = pieces[p];

                    if (piece.Seen || l >= piece.Looks.Length) {
                        p++;
                        l = NearLooks;
                        k = 0;
                        continue;
                    }

                    Vector3 from = looks[piece.Looks[l]];
                    Vector3 way = piece.Points[k] - from;
                    float distance = way.magnitude;

                    if (distance < BoxTolerance) {
                        piece.Seen = true;
                        continue;
                    }

                    commands[count] = new RaycastCommand(from, way / distance, query, distance);
                    pieceOf[count] = p;
                    count++;

                    if (++k >= piece.Points.Length) {
                        k = 0;
                        l++;
                    }
                }

                if (count == 0) {
                    break;
                }

                RaycastCommand.ScheduleBatch(commands.GetSubArray(0, count), hits.GetSubArray(0, count), RaysPerJob, 1).Complete();

                for (int i = 0; i < count; i++) {
                    Piece piece = pieces[pieceOf[i]];

                    if (!piece.Seen) {
                        piece.Seen = Reaches(hits[i], piece);
                    }
                }
            }

            commands.Dispose();
            hits.Dispose();
        }

        // How far below a railing's root the roof it stands on may lie.
        private const float RailingDrop = 0.5f;
        // The hits of the ray that checks a railing still has its roof.
        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>
        /// Whether a roof piece (a TL_Roof... variant) is still under a
        /// railing: a ray down from 0.5m above its root reaches one within
        /// RailingDrop below the root.
        /// </summary>
        private static bool RoofUnder(Transform railing, int mask)
        {
            int hits = Physics.RaycastNonAlloc(railing.position + Vector3.up * 0.5f, Vector3.down, _hits, 0.5f + RailingDrop, mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits; i++) {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(_hits[i].collider.gameObject);

                if (root != null && root.name.StartsWith("TL_Roof")) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether any of a piece's sample points can be seen from a point,
        /// one ray at a time.
        /// </summary>
        private static bool SeenFrom(Vector3 from, Piece piece, int mask)
        {
            foreach (Vector3 point in piece.Points) {
                Vector3 way = point - from;
                float distance = way.magnitude;

                if (distance < BoxTolerance) {
                    return true;
                }

                Physics.Raycast(from, way / distance, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore);

                if (Reaches(hit, piece)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a ray to one of a piece's sample points reached the piece:
        /// it hit nothing on the way (no collider), or first hit the piece
        /// itself - its own collider, or anything within BoxTolerance of its
        /// render box (a merged collider box standing in for its own).
        /// </summary>
        private static bool Reaches(RaycastHit hit, Piece piece)
        {
            Collider collider = hit.collider;

            if (collider == null || collider.transform.IsChildOf(piece.Root)) {
                return true;
            }

            Vector3 local = piece.Root.InverseTransformPoint(hit.point);
            return local.x >= piece.Min.x - piece.Tolerance.x && local.x <= piece.Max.x + piece.Tolerance.x
                && local.y >= piece.Min.y - piece.Tolerance.y && local.y <= piece.Max.y + piece.Tolerance.y
                && local.z >= piece.Min.z - piece.Tolerance.z && local.z <= piece.Max.z + piece.Tolerance.z;
        }

        /// <summary>
        /// Every placed piece under the root (an object named like a prefab
        /// variant, TL_..., not inside another, and not under the lamps'
        /// group), with its render box in its own space from its LOD0
        /// renderers (all its renderers if it has no LODs) and its sample
        /// points: the middle, the eight corners and the six face middles of
        /// that box, drawn in by Shrink.
        /// </summary>
        private static List<Piece> Gather(Transform root)
        {
            List<Piece> pieces = new();
            List<MeshRenderer> renderers = new();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
                if (!t.name.StartsWith("TL_") || InsidePiece(t.parent, root)) {
                    continue;
                }

                string group = GroupOf(t, root);

                if (group == LampsGroup) {
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

                // The range check uses the whole box, not the shrunk points.
                for (int c = 0; c < 8; c++) {
                    world.Encapsulate(t.TransformPoint(new Vector3((c & 1) == 0 ? min.x : max.x, (c & 2) == 0 ? min.y : max.y, (c & 4) == 0 ? min.z : max.z)));
                }

                Vector3 scale = t.lossyScale;
                Vector3 tolerance = new(
                    BoxTolerance / Mathf.Max(Mathf.Abs(scale.x), 0.001f),
                    BoxTolerance / Mathf.Max(Mathf.Abs(scale.y), 0.001f),
                    BoxTolerance / Mathf.Max(Mathf.Abs(scale.z), 0.001f));

                pieces.Add(new Piece { Root = t, Group = group, Min = min, Max = max, Tolerance = tolerance, Points = points.ToArray(), World = world });
            }

            return pieces;
        }

        /// <summary>
        /// The name of the group directly under the root an object is in
        /// (Streets, Facades, Lots, Roofs, Foliage, Lamps).
        /// </summary>
        private static string GroupOf(Transform t, Transform root)
        {
            Transform group = t;

            while (group.parent != null && group.parent != root) {
                group = group.parent;
            }

            return group.name;
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
