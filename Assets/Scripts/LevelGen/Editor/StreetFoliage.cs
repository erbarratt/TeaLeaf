using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The foliage the generator puts on the streets: clusters of grass on
    /// the ground and nettles at the foot of the foundations.
    ///
    /// Grass (PlaceCluster, called by StreetGround as it builds the
    /// ground): a share of the ground's patches (GrassCover, GrassPatch
    /// square) gets a cluster - a few tufts (GrassClusterMin to Max) of the
    /// verified grass pieces (category "grass"), each turned and scaled at
    /// random, scattered within GrassClusterRadius of a random point in the
    /// patch. StreetGround only asks for a cluster where that point stands
    /// in the open street:
    /// GrassEdgeMargin in from the street's edges and from every lot's
    /// footprint (clear of the foundations and of columns standing forward
    /// into the street) and from a flight of steps.
    ///
    /// Nettles (BuildNettles, once everything is built): each street gets a
    /// few (NettlesPerStreetMin to Max) at the visible joint between the
    /// foundation stones and the ground - at the foot of a placed
    /// foundation piece's front face, where that face looks onto the street,
    /// NettleOut in front of the face so the plant's back grows out of the
    /// corner. Spots are drawn at random along the street's fronts (weighted
    /// by length), never within NettleSpacing of another nettle nor
    /// NettleDoorClear of a doorway's sides, and the ground under each is
    /// found with a ray down onto the ground's colliders.
    ///
    /// The foliage draws from a random sequence of its own, seeded from the
    /// spec's seed (RandomFor), so adding or tuning it changes nothing else
    /// that is built. Every piece is placed by its pivot (on its base), sunk
    /// FoliageSink into the ground so its roots hide in the uneven stones.
    /// </summary>
    internal static class StreetFoliage
    {
        // A nettle stands at least this far from a front's ends.
        private const float EndMargin = 0.3f;
        // The ray for the ground under a nettle starts this far above the
        // foundation's top and reaches this far down.
        private const float RayAbove = 1f;
        private const float RayDepth = 6f;
        // How far above the street level in front of a foundation (its top
        // less FoundationHeight) the ground found by the ray may be: more,
        // and something else stands there.
        private const float GroundTolerance = 0.15f;
        // How far past a segment's end a foundation may stand and still be
        // beside it (a corner's foundations), and how far beyond it a dead
        // end's foundation, which stands across the end, may be.
        private const float EndSlack = 0.5f;
        private const float DeadEndReach = 1.5f;
        // How many spots are tried per nettle wanted before giving up.
        private const int Attempts = 10;
        // The name MergedColliders gives the group of the foundations' merged
        // boxes.
        private const string FoundationColliders = "Foundation Colliders";

        private static readonly List<PieceAnnotation> _grass = new();
        private static readonly List<PieceAnnotation> _nettles = new();
        private static readonly RaycastHit[] _hits = new RaycastHit[32];
        private static PieceAnnotation _previousGrass;

        /// <summary>
        /// A placed foundation's front at its foot: the face's two ends on
        /// the ground (x, z), its way out, the world height of the piece's
        /// top, and the piece itself (ignored by the ground ray).
        /// </summary>
        private class Front
        {
            public Vector2 A;
            public Vector2 B;
            public Vector2 Out;
            public float Top;
            public Transform Piece;
            public float Length => (B - A).magnitude;
        }

        /// <summary>
        /// A doorway in a placed door wall: its middle on the ground (x, z)
        /// and half its width.
        /// </summary>
        private class Doorway
        {
            public Vector2 Centre;
            public float HalfWidth;
        }

        /// <summary>
        /// Whether there is any grass piece to place.
        /// </summary>
        internal static bool HasGrass => _grass.Count > 0;

        /// <summary>
        /// The foliage's own random sequence for a spec: from its seed, apart
        /// from the generator's, so the foliage changes nothing else.
        /// </summary>
        internal static System.Random RandomFor(LayoutSpec spec)
        {
            return new System.Random(unchecked(spec.seed * 31 + 7));
        }

        /// <summary>
        /// Collects the verified, catalogued grass and nettle pieces, before
        /// anything is laid.
        /// </summary>
        internal static void Gather(PieceAnnotations annotations, PiecePlacer placer)
        {
            _grass.Clear();
            _nettles.Clear();
            _previousGrass = null;

            foreach (PieceAnnotation piece in annotations.pieces) {
                if (!piece.verified || piece.excluded || placer.Entry(piece) == null) {
                    continue;
                }

                if (piece.category == "grass") {
                    _grass.Add(piece);
                } else if (piece.category == "nettle") {
                    _nettles.Add(piece);
                }
            }
        }

        /// <summary>
        /// Places one cluster of grass about a point on the ground: the
        /// first tuft at the point, the rest scattered at random within
        /// GrassClusterRadius of it, each at the ground height groundAt gives
        /// for its spot. Returns how many tufts were placed.
        /// </summary>
        internal static int PlaceCluster(Vector2 centre, System.Func<Vector2, float> groundAt, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform group)
        {
            int count = random.Next(settings.GrassClusterMin, settings.GrassClusterMax + 1);
            int placed = 0;

            for (int i = 0; i < count; i++) {
                Vector2 at = centre;

                if (i > 0) {
                    // Even over the disc: the radius grows as the square root.
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float radius = Mathf.Sqrt((float)random.NextDouble()) * settings.GrassClusterRadius;
                    at += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }

                _previousGrass = PiecePlacer.Pick(_grass, _previousGrass, random);

                if (Place(_previousGrass, at, groundAt(at), settings, random, placer, group) != null) {
                    placed++;
                }
            }

            return placed;
        }

        /// <summary>
        /// Puts a few nettles on each street, at the foot of the foundations
        /// (placed anywhere under root: the facades', corners', lots' and
        /// dead ends') whose fronts look onto it, under a "Nettles - street"
        /// group of parent. Adds one line to log.
        /// </summary>
        internal static void BuildNettles(LayoutSpec spec, PieceAnnotations annotations, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform root, Transform parent, List<string> log)
        {
            if (_nettles.Count == 0 || settings.NettlesPerStreetMax <= 0) {
                log.Add("Nettles: none (no verified nettle piece, or nettlesPerStreetMax is 0).");
                return;
            }

            Dictionary<string, PieceAnnotation> byGuid = new();

            foreach (PieceAnnotation piece in annotations.pieces) {
                byGuid[piece.guid] = piece;
            }

            List<Front> fronts = new();
            List<Doorway> doorways = new();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) {
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject));

                if (!byGuid.TryGetValue(guid, out PieceAnnotation piece)) {
                    continue;
                }

                CatalogueEntry entry = placer.Entry(piece);

                if (entry == null) {
                    continue;
                }

                if (piece.category == "foundation" || piece.category == "foundation-corner") {
                    AddFronts(t, piece, entry, fronts);
                } else if (piece.category == "door-wall") {
                    AddDoorways(t, piece, entry, doorways);
                }
            }

            // Each front to the street it looks onto, if any.
            Dictionary<StreetSpec, List<Front>> byStreet = new();

            foreach (Front front in fronts) {
                StreetSpec street = Faces(front, spec.streets, settings);

                if (street == null) {
                    continue;
                }

                if (!byStreet.TryGetValue(street, out List<Front> list)) {
                    byStreet[street] = list = new List<Front>();
                }

                list.Add(front);
            }

            // The ground rays need the colliders placed this generation.
            Physics.SyncTransforms();
            List<Vector2> placed = new();
            int count = 0;
            int streetsWith = 0;

            foreach (StreetSpec street in spec.streets) {
                if (!byStreet.TryGetValue(street, out List<Front> facing)) {
                    continue;
                }

                int wanted = random.Next(settings.NettlesPerStreetMin, settings.NettlesPerStreetMax + 1);
                float total = 0f;

                foreach (Front front in facing) {
                    total += front.Length;
                }

                Transform group = null;
                PieceAnnotation previous = null;
                int here = 0;

                for (int attempt = 0; attempt < wanted * Attempts && here < wanted; attempt++) {
                    Front front = PickFront(facing, total, random);

                    if (front.Length <= 2f * EndMargin) {
                        continue;
                    }

                    float along = Mathf.Lerp(EndMargin, front.Length - EndMargin, (float)random.NextDouble());
                    Vector2 at = front.A + (front.B - front.A) * (along / front.Length) + front.Out * settings.NettleOut;

                    if (TooClose(at, placed, settings.NettleSpacing) || NearDoorway(at, doorways, settings.NettleDoorClear)
                        || !GroundUnder(at, front, settings, out float ground)) {
                        continue;
                    }

                    if (group == null) {
                        group = new GameObject($"Nettles - {street.id}").transform;
                        group.SetParent(parent, false);
                    }

                    previous = PiecePlacer.Pick(_nettles, previous, random);

                    if (Place(previous, at, ground, settings, random, placer, group) != null) {
                        placed.Add(at);
                        here++;
                    }
                }

                count += here;

                if (here > 0) {
                    streetsWith++;
                }
            }

            log.Add($"Nettles: {count} on {streetsWith} street(s), from {fronts.Count} foundation front(s).");
        }

        /// <summary>
        /// Places one piece of foliage by its pivot at a point on the ground,
        /// turned and scaled at random, sunk FoliageSink. Null if it could
        /// not be placed.
        /// </summary>
        private static GameObject Place(PieceAnnotation piece, Vector2 at, float ground, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform group)
        {
            float yaw = (float)random.NextDouble() * 360f;
            float scale = Mathf.Lerp(settings.FoliageScaleMin, settings.FoliageScaleMax, (float)random.NextDouble());
            return placer.PlaceAt(piece, Vector3.zero, new Vector3(at.x, ground - settings.FoliageSink, at.y), yaw, Vector3.one * scale, group);
        }

        /// <summary>
        /// Adds a placed foundation's front(s): for each front axis of its
        /// annotation, the top edge of its render box's face on that axis,
        /// taken to the world (its scale and turn applied) and dropped to the
        /// ground plane, with the face's way out.
        /// </summary>
        private static void AddFronts(Transform placed, PieceAnnotation piece, CatalogueEntry entry, List<Front> fronts)
        {
            Vector3 min = entry.renderMin;
            Vector3 max = entry.renderMax;
            float top = placed.TransformPoint(new Vector3(0f, max.y, 0f)).y;

            foreach (Vector3 front in piece.fronts) {
                Vector3 a;
                Vector3 b;

                if (Mathf.Abs(front.x) > Mathf.Abs(front.z)) {
                    float x = front.x > 0f ? max.x : min.x;
                    a = new Vector3(x, max.y, min.z);
                    b = new Vector3(x, max.y, max.z);
                } else {
                    float z = front.z > 0f ? max.z : min.z;
                    a = new Vector3(min.x, max.y, z);
                    b = new Vector3(max.x, max.y, z);
                }

                fronts.Add(new Front {
                    A = Ground(placed.TransformPoint(a)),
                    B = Ground(placed.TransformPoint(b)),
                    Out = Level(placed.TransformDirection(front)),
                    Top = top,
                    Piece = placed
                });
            }
        }

        /// <summary>
        /// Adds a placed door wall's doorway(s): the middle of each door
        /// opening on the wall's back plane, on the ground, and half its width
        /// across the wall.
        /// </summary>
        private static void AddDoorways(Transform placed, PieceAnnotation piece, CatalogueEntry entry, List<Doorway> doorways)
        {
            if (piece.fronts.Count == 0) {
                return;
            }

            Vector3 right = Vector3.Cross(Vector3.up, -piece.fronts[0]);

            foreach (Opening opening in entry.openings) {
                if (opening.kind != "door") {
                    continue;
                }

                float width = Mathf.Abs(Vector3.Dot(opening.max - opening.min, right));
                doorways.Add(new Doorway {
                    Centre = Ground(placed.TransformPoint((opening.min + opening.max) * 0.5f)),
                    HalfWidth = placed.TransformVector(right * width).magnitude * 0.5f
                });
            }
        }

        /// <summary>
        /// The street a front looks onto: the nearest street segment that it
        /// stands beside - within the segment's half width plus the furthest
        /// a column and its foundation stand forward and the ground laid
        /// round a lot - with its way out towards the segment's middle line;
        /// or that it stands across the end of, looking back along it (a
        /// dead end's). Null if there is none.
        /// </summary>
        private static StreetSpec Faces(Front front, List<StreetSpec> streets, GeneratorSettings settings)
        {
            Vector2 middle = (front.A + front.B) * 0.5f;
            StreetSpec best = null;
            float nearest = float.MaxValue;

            foreach (StreetSpec street in streets) {
                for (int i = 1; i < street.points.Count; i++) {
                    Vector2 a = Ground(street.points[i - 1]);
                    Vector2 b = Ground(street.points[i]);
                    float length = (b - a).magnitude;

                    if (length < settings.MinSegmentLength) {
                        continue;
                    }

                    Vector2 axis = (b - a) / length;
                    Vector2 perp = new(-axis.y, axis.x);
                    float along = Vector2.Dot(middle - a, axis);
                    float across = Vector2.Dot(middle - a, perp);
                    float reach = street.SegmentWidth(i - 1) * 0.5f + settings.ColumnDepthMax + settings.FoundationFrontOffset
                        + settings.LotGroundMargin + settings.OverlapTolerance;

                    if (Mathf.Abs(across) > reach) {
                        continue;
                    }

                    bool beside = along >= -EndSlack && along <= length + EndSlack
                        && Vector2.Dot(front.Out, across > 0f ? -perp : perp) > 0.5f;
                    bool acrossEnd = (along > length - EndSlack && along <= length + DeadEndReach && Vector2.Dot(front.Out, -axis) > 0.5f)
                        || (along < EndSlack && along >= -DeadEndReach && Vector2.Dot(front.Out, axis) > 0.5f);

                    if (!beside && !acrossEnd) {
                        continue;
                    }

                    float distance = Mathf.Abs(across) + Mathf.Max(0f, Mathf.Max(-along, along - length));

                    if (distance < nearest) {
                        nearest = distance;
                        best = street;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// One of the fronts at random, the longer the likelier.
        /// </summary>
        private static Front PickFront(List<Front> fronts, float total, System.Random random)
        {
            float draw = (float)random.NextDouble() * total;

            foreach (Front front in fronts) {
                draw -= front.Length;

                if (draw <= 0f) {
                    return front;
                }
            }

            return fronts[fronts.Count - 1];
        }

        /// <summary>
        /// Whether a spot is within spacing of a nettle already placed.
        /// </summary>
        private static bool TooClose(Vector2 at, List<Vector2> placed, float spacing)
        {
            foreach (Vector2 other in placed) {
                if ((other - at).sqrMagnitude < spacing * spacing) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a spot is within clear of a doorway's sides.
        /// </summary>
        private static bool NearDoorway(Vector2 at, List<Doorway> doorways, float clear)
        {
            foreach (Doorway doorway in doorways) {
                float reach = doorway.HalfWidth + clear;

                if ((doorway.Centre - at).sqrMagnitude < reach * reach) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The ground under a spot in front of a foundation: the highest
        /// thing a ray down from above the foundation's top hits there, the
        /// foundation itself and the merged foundation boxes ignored, if it
        /// lies no higher than the street level in front of the foundation
        /// (its top less FoundationHeight) plus a little. False if nothing
        /// is there, or something else stands on the spot.
        /// </summary>
        private static bool GroundUnder(Vector2 at, Front front, GeneratorSettings settings, out float ground)
        {
            Vector3 from = new(at.x, front.Top + RayAbove, at.y);
            int hits = Physics.RaycastNonAlloc(from, Vector3.down, _hits, RayAbove + RayDepth, ~0, QueryTriggerInteraction.Ignore);
            ground = float.MinValue;

            for (int i = 0; i < hits; i++) {
                Transform hit = _hits[i].collider.transform;

                if (hit.IsChildOf(front.Piece) || (hit.parent != null && hit.parent.name == FoundationColliders)) {
                    continue;
                }

                ground = Mathf.Max(ground, _hits[i].point.y);
            }

            return ground > float.MinValue && ground <= front.Top - settings.FoundationHeight + GroundTolerance;
        }

        /// <summary>
        /// A world point on the ground plane (x, z).
        /// </summary>
        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        /// <summary>
        /// A direction levelled and normalised, on the ground plane.
        /// </summary>
        private static Vector2 Level(Vector3 direction)
        {
            return new Vector2(direction.x, direction.z).normalized;
        }
    }
}
