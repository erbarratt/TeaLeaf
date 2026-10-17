using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The roofs over the facades, dead ends and lots: FacadeBuilder's last
    /// step, after every wall and corner is built. The pack's own way, a
    /// slope whose ridge runs along the street: rows of tile panels (verge
    /// pieces at the ends, plain ones between, the top row carrying the
    /// ridge cap) tilted to RoofPitch, the eave RoofEaveOverhang in front of
    /// the top storey's face and RoofEaveDrop below the top of its walls, and
    /// a back slope mirroring it, so the roof reads whole from behind.
    /// Neighbouring columns of a run whose tops and faces line up share one
    /// roof; every other roof end gets a gable standing on the walls, except
    /// where two roofs meet at a corner: on the outside of a right-angled
    /// corner (within tolerance) their slopes meet along the hip, each row
    /// ending on it, under a strip of the pack's Roof_PeakedBase pieces
    /// stacked up the hip; on the inside of any bend their slopes run on
    /// into each other and meet in a valley, their back slopes in a hip
    /// behind. A corner wider than a right angle has the other side's roof
    /// run on over it. A small lot's sides each slope up to its middle (a
    /// hipped roof); a bigger lot's sides are roofed like facades, round a
    /// courtyard; a lot's right-angled corners get hip strips too.
    /// </summary>
    internal static partial class FacadeBuilder
    {
        // The usable roof pieces (verified, not excluded, catalogued; any
        // style), by category, set for the length of one Build call.
        private static readonly Dictionary<string, List<PieceAnnotation>> _roofs = new();

        /// <summary>
        /// One roof over a stretch of a run, worked out before anything is
        /// laid so the corners can be joined: its columns (First to Last of
        /// the frontage), its run, how far along it (From, To), its walls' top
        /// and their face forward of the street edge, the gables asked for,
        /// its rows and how far they are scaled up the slope, whether it has a
        /// back slope, and at each end whether a corner closes it (a hip or a
        /// valley with the other roof): where along the run the two roofs'
        /// front eaves cross, and the slant of the line the rows end on (how
        /// far past that crossing, towards the corner, a row ends for each
        /// metre its middle lies back from the eave; negative: short of it).
        /// </summary>
        private class RoofStretch
        {
            public Frontage Frontage;
            public int First;
            public int Last;
            public Run Run;
            public float From;
            public float To;
            public float Top;
            public float Face;
            public bool GableFrom;
            public bool GableTo;
            public int Rows;
            public float RowScale;
            public bool BackSlope;
            public bool CornerFrom;
            public bool CornerTo;
            public float CornerFromAt;
            public float CornerToAt;
            public float CornerFromSlant;
            public float CornerToSlant;
        }

        /// <summary>
        /// A corner where two roofs meet: the two (and whether at each one's
        /// start), the point where their front eaves cross, whether the
        /// building's corner points out (a hip in front, a valley behind) or
        /// in (a valley in front, a hip behind), and whether it is near enough
        /// a right angle for a hip strip to fit.
        /// </summary>
        private class RoofCorner
        {
            public RoofStretch A;
            public RoofStretch B;
            public Vector2 Corner;
            public bool Convex;
            public bool Square;
        }

        /// <summary>
        /// Gathers the usable roof pieces by category.
        /// </summary>
        private static void GatherRoofPieces(PieceAnnotations annotations, PiecePlacer placer)
        {
            _roofs.Clear();

            foreach (PieceAnnotation piece in annotations.pieces) {
                if (!piece.verified || piece.excluded || !piece.category.StartsWith("roof-") || placer.Entry(piece) == null) {
                    continue;
                }

                if (!_roofs.TryGetValue(piece.category, out List<PieceAnnotation> pieces)) {
                    pieces = new List<PieceAnnotation>();
                    _roofs.Add(piece.category, pieces);
                }

                pieces.Add(piece);
            }
        }

        /// <summary>
        /// The usable roof pieces of a category (empty if there are none).
        /// </summary>
        private static List<PieceAnnotation> RoofPieces(string category)
        {
            return _roofs.TryGetValue(category, out List<PieceAnnotation> pieces) ? pieces : new List<PieceAnnotation>();
        }

        /// <summary>
        /// The panels between a row's two ends: the usable roof-middle pieces,
        /// or with none (the pack's middle panel is open at its edges, so it
        /// is excluded) the row's end pieces of the category given.
        /// </summary>
        private static List<PieceAnnotation> Middles(string endCategory)
        {
            List<PieceAnnotation> middles = RoofPieces("roof-middle");
            return middles.Count > 0 ? middles : RoofPieces(endCategory);
        }

        /// <summary>
        /// Roofs every frontage, lot and dead end given, in order: every
        /// stretch worked out first, then the corners where two roofs meet
        /// found and the roofs' ends set to meet there, then everything laid,
        /// the hip strips last. Logs how many roofs, corners, strips and
        /// pieces, or why there are none.
        /// </summary>
        private static void BuildRoofs(List<Frontage> frontages, List<Joint> joints, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            List<string> log)
        {
            if (RoofPieces("roof-end").Count == 0 || RoofPieces("roof-ridge-end").Count == 0) {
                log.Add("No roofs: verify a roof-end and a roof-ridge-end piece in Review Pieces.");
                return;
            }

            int pieces = placer.Placed;
            List<RoofStretch> stretches = new();
            Dictionary<Frontage, Vector2> middles = new();

            foreach (Frontage frontage in frontages) {
                middles[frontage] = LotMiddle(frontage);
                RoofStretches(frontage, middles[frontage], settings, stretches);
            }

            List<RoofCorner> corners = RoofCorners(stretches, RoofJoints(frontages, joints), settings);

            foreach (RoofStretch stretch in stretches) {
                BuildRoof(stretch, middles[stretch.Frontage], settings, random, placer);
            }

            int strips = 0;

            foreach (RoofCorner corner in corners) {
                strips += HipStrips(corner, settings, placer);
            }

            log.Add($"Roofs: {stretches.Count} roof(s), {corners.Count} corner(s), {strips} hip strip(s), {placer.Placed - pieces} piece(s).");
        }

        /// <summary>
        /// The middle of a lot's footprint (the mean of its sides' starts);
        /// zero for a facade.
        /// </summary>
        private static Vector2 LotMiddle(Frontage frontage)
        {
            Vector2 middle = Vector2.zero;

            if (frontage.Lot != null) {
                foreach (Run run in frontage.Runs) {
                    middle += run.Start / frontage.Runs.Count;
                }
            }

            return middle;
        }

        /// <summary>
        /// One frontage's roofs: one per stretch of neighbouring columns of a
        /// run whose tops (in metres) and top storeys' faces line up, carried
        /// at a run's ends as far as a corner carried the top storey. A lot's
        /// slopes rise from every side to the footprint's middle. Adds them
        /// to stretches.
        /// </summary>
        private static void RoofStretches(Frontage frontage, Vector2 middle, GeneratorSettings settings, List<RoofStretch> stretches)
        {
            List<ModuleSpot> spots = frontage.Spots;
            int first = 0;

            while (first < spots.Count) {
                ModuleSpot spot = spots[first];

                if (spot.Storeys == 0) {
                    first++;
                    continue;
                }

                float top = RoofTop(spot, frontage);
                float face = RoofFace(spot, frontage);
                int last = first;

                while (last + 1 < spots.Count && spots[last + 1].Run == spot.Run && spots[last + 1].Storeys > 0
                    && Mathf.Abs(RoofTop(spots[last + 1], frontage) - top) <= settings.SizeTolerance
                    && Mathf.Abs(RoofFace(spots[last + 1], frontage) - face) < SameDepth) {
                    last++;
                }

                ModuleSpot end = spots[last];
                bool runStart = first == 0 || spots[first - 1].Run != spot.Run;
                bool runEnd = last == spots.Count - 1 || spots[last + 1].Run != spot.Run;
                RoofStretch stretch = new() {
                    Frontage = frontage,
                    First = first,
                    Last = last,
                    Run = frontage.Runs[spot.Run],
                    From = spot.Position - (runStart ? spot.CarriedStart : 0f),
                    To = end.Position + Spacing(end, frontage.Buildings) + (runEnd ? end.CarriedEnd : 0f),
                    Top = top,
                    Face = face,
                    // A gable closes every end but a corner's that the other
                    // side's roof runs on over.
                    GableFrom = !(runStart && spot.JoinedStart) || spot.GableStart,
                    GableTo = !(runEnd && end.JoinedEnd) || end.GableEnd
                };
                RoofShape(stretch, middle, settings);
                stretches.Add(stretch);
                first = last + 1;
            }
        }

        /// <summary>
        /// The height of the top of a module's column: its street level plus
        /// the top of its top storey.
        /// </summary>
        private static float RoofTop(ModuleSpot spot, Frontage frontage)
        {
            return SpotGround(spot, frontage.Runs, frontage.Buildings) + Top(frontage.Buildings[spot.Building], spot.Storeys);
        }

        /// <summary>
        /// How far forward of the street edge the face of a module's top
        /// storey stands: its column's depth plus its building's jetty.
        /// </summary>
        private static float RoofFace(ModuleSpot spot, Frontage frontage)
        {
            return spot.Depth + StoreyJetty(frontage.Buildings[spot.Building], spot.Storeys - 1);
        }

        /// <summary>
        /// A stretch's rows and how far they are scaled up the slope. Over a
        /// facade, two slopes of RoofRows rows meeting at the ridge, the back
        /// one the front's mirror, both shortened up the slope where the back
        /// one would reach over another street. Over a lot, one slope rising
        /// towards the footprint's middle: where that is nearer than RoofRows
        /// rows reach, as many rows as reach it, scaled up the slope to end
        /// there (the sides' slopes meet as a hipped roof); else both slopes as
        /// over a facade, round a courtyard, scaled down so the inner eave
        /// never passes the middle.
        /// </summary>
        private static void RoofShape(RoofStretch stretch, Vector2 middle, GeneratorSettings settings)
        {
            Run run = stretch.Run;
            float cos = Mathf.Cos(settings.RoofPitch * Mathf.Deg2Rad);
            float centre = (stretch.From + stretch.To) * 0.5f;
            float eaveLine = stretch.Face + settings.RoofEaveOverhang;
            Vector2 eaveMiddle = run.Start + run.Along * centre + run.Front * eaveLine;
            float rowHeight = Middles("roof-end")[0].height;
            float overlap = settings.RoofRowOverlap;
            bool lot = stretch.Frontage.Lot != null;
            stretch.Rows = settings.RoofRows;
            stretch.RowScale = 1f;
            stretch.BackSlope = !lot;

            if (lot) {
                // Up to the footprint's middle, where it is nearer than a
                // facade roof's slope reaches (a hipped roof); a bigger lot's
                // sides get both slopes, round a courtyard, the inner one's
                // eave never past the middle.
                float slope = Vector2.Dot(middle - eaveMiddle, -run.Front) / cos;
                float full = stretch.Rows * rowHeight - (stretch.Rows - 1) * overlap;

                if (slope < full) {
                    stretch.Rows = Mathf.Max(1, Mathf.CeilToInt((slope - overlap) / (rowHeight - overlap)));
                    stretch.RowScale = (slope + (stretch.Rows - 1) * overlap) / (stretch.Rows * rowHeight);
                } else {
                    stretch.BackSlope = true;
                    stretch.RowScale = Mathf.Min(1f, slope / (2f * full));
                }
            }

            // A facade's roof has both slopes (the back one the front's
            // mirror, so the roof reads whole from behind too), always full
            // size: a stretch scaled apart from its neighbours would stand at
            // another height and open a gap between them.
        }

        /// <summary>
        /// How far a stretch's slope runs up from its eave (its rows, scaled,
        /// less their overlaps).
        /// </summary>
        private static float SlopeLength(RoofStretch stretch, GeneratorSettings settings)
        {
            return stretch.Rows * Middles("roof-end")[0].height * stretch.RowScale - (stretch.Rows - 1) * settings.RoofRowOverlap;
        }

        /// <summary>
        /// The pairs of run ends whose roofs meet at a corner: every joint
        /// (bends on the outside, street corners, side returns, a lot's last
        /// side back to its first) and every bend on the inside, which has
        /// no joint (its runs overrun each other instead) but whose roofs
        /// still meet.
        /// </summary>
        private static List<Joint> RoofJoints(List<Frontage> frontages, List<Joint> joints)
        {
            List<Joint> all = new(joints);

            foreach (Frontage frontage in frontages) {
                for (int r = 0; r + 1 < frontage.Runs.Count; r++) {
                    if (!InsideBend(frontage.Runs, r)) {
                        continue;
                    }

                    int a = frontage.Spots.FindLastIndex(spot => spot.Run == r);
                    int b = frontage.Spots.FindIndex(spot => spot.Run == r + 1);

                    if (a >= 0 && b >= 0) {
                        all.Add(new Joint { A = new RunEnd { Frontage = frontage, Spot = a }, B = new RunEnd { Frontage = frontage, Spot = b, AtStart = true } });
                    }
                }
            }

            return all;
        }

        /// <summary>
        /// The corners where two roofs meet: every joint between two roofs
        /// that end there (facades' or a lot's sides) whose walls' tops are
        /// within SizeTolerance of each other. The building's corner points
        /// out (the slant below is negative) or in; one pointing out is only
        /// taken within CornerAngleTolerance of a right angle (a wider one has
        /// the other side's roof run on over it, a sharper one its gables).
        /// Each roof's end there is set to meet the other along the hip or
        /// valley line (no gables): the line equally far back from both
        /// eaves, so a row whose middle lies b back from the eave ends
        /// slant x b past the point where the front eaves cross, towards the
        /// corner, where slant = (the other's back direction . the way out
        /// of this end) / (1 + the two back directions' dot). Two facade
        /// roofs take the lower of their row scales (repeated until none
        /// changes, since a roof can end at two corners), so their ridges
        /// meet at one height; a lot's sides keep theirs.
        /// </summary>
        private static List<RoofCorner> RoofCorners(List<RoofStretch> stretches, List<Joint> joints, GeneratorSettings settings)
        {
            List<RoofCorner> corners = new();

            foreach (Joint joint in joints) {
                RoofStretch a = StretchAt(stretches, joint.A);
                RoofStretch b = StretchAt(stretches, joint.B);

                if (a == null || b == null || a == b) {
                    continue;
                }

                EndLine(joint.A, 0f, out _, out Vector2 outA, out _, out _);
                EndLine(joint.B, 0f, out _, out Vector2 outB, out _, out _);
                Vector2 backA = -a.Run.Front;
                Vector2 backB = -b.Run.Front;
                float spread = 1f + Vector2.Dot(backA, backB);
                float slantA = Vector2.Dot(backB, outA) / spread;
                float slantB = Vector2.Dot(backA, outB) / spread;
                bool convex = slantA < 0f;
                bool square = Mathf.Abs(Vector2.Angle(outA, outB) - 90f) <= settings.CornerAngleTolerance;
                // Where the two front eaves cross.
                Vector2 eaveA = a.Run.Start + a.Run.Front * (a.Face + settings.RoofEaveOverhang);
                Vector2 eaveB = b.Run.Start + b.Run.Front * (b.Face + settings.RoofEaveOverhang);

                // No corner between a lot and a facade, roofs of different
                // heights, one folded back on itself (no line to meet on), a
                // convex corner wide of a right angle, or parallel eaves -
                // nearly parallel ones too, whose crossing lies more than
                // CornerMaxReach from either roof's end (two roofs in line
                // would otherwise run on to a point far off): the two roofs
                // end on their own, each closed by a gable.
                bool meet = Intersect(eaveA, a.Run.Along, eaveB, b.Run.Along, out float alongA);
                Vector2 corner = eaveA + a.Run.Along * alongA;
                float alongB = Vector2.Dot(corner - b.Run.Start, b.Run.Along);
                bool near = Mathf.Abs(alongA - (joint.A.AtStart ? a.From : a.To)) <= settings.CornerMaxReach
                    && Mathf.Abs(alongB - (joint.B.AtStart ? b.From : b.To)) <= settings.CornerMaxReach;

                if ((a.Frontage.Lot == null) != (b.Frontage.Lot == null) || Mathf.Abs(a.Top - b.Top) > settings.SizeTolerance || spread < 0.1f
                    || (convex && !square) || !meet || !near) {
                    SetGable(a, joint.A.AtStart);
                    SetGable(b, joint.B.AtStart);
                    continue;
                }

                MarkCorner(a, joint.A.AtStart, alongA, slantA);
                MarkCorner(b, joint.B.AtStart, alongB, slantB);
                corners.Add(new RoofCorner { A = a, B = b, Corner = corner, Convex = convex, Square = square });
            }

            bool changed = true;

            while (changed) {
                changed = false;

                foreach (RoofCorner corner in corners) {
                    if (corner.A.Frontage.Lot != null) {
                        continue;
                    }

                    float scale = Mathf.Min(corner.A.RowScale, corner.B.RowScale);
                    int rows = Mathf.Min(corner.A.Rows, corner.B.Rows);
                    changed |= corner.A.RowScale != scale || corner.B.RowScale != scale || corner.A.Rows != rows || corner.B.Rows != rows;
                    corner.A.RowScale = corner.B.RowScale = scale;
                    corner.A.Rows = corner.B.Rows = rows;
                }
            }

            return corners;
        }

        /// <summary>
        /// Asks for a gable at one end of a stretch's roof.
        /// </summary>
        private static void SetGable(RoofStretch stretch, bool atStart)
        {
            if (atStart) {
                stretch.GableFrom = true;
            } else {
                stretch.GableTo = true;
            }
        }

        /// <summary>
        /// The stretch whose roof ends at a run end (its first column at a
        /// run's start, its last at a run's end), or null.
        /// </summary>
        private static RoofStretch StretchAt(List<RoofStretch> stretches, RunEnd end)
        {
            foreach (RoofStretch stretch in stretches) {
                if (stretch.Frontage == end.Frontage && (end.AtStart ? stretch.First == end.Spot : stretch.Last == end.Spot)) {
                    return stretch;
                }
            }

            return null;
        }

        /// <summary>
        /// Marks a stretch's end (its start, else its end) as closed by a
        /// corner: where along its run the two roofs' front eaves cross, the
        /// slant of the line its rows end on, and no gable.
        /// </summary>
        private static void MarkCorner(RoofStretch stretch, bool atStart, float cornerAt, float slant)
        {
            if (atStart) {
                stretch.CornerFrom = true;
                stretch.CornerFromAt = cornerAt;
                stretch.CornerFromSlant = slant;
                stretch.GableFrom = false;
            } else {
                stretch.CornerTo = true;
                stretch.CornerToAt = cornerAt;
                stretch.CornerToSlant = slant;
                stretch.GableTo = false;
            }
        }

        /// <summary>
        /// The hip strip's piece (the first usable roof-hip-base), and whether
        /// it is there with its two fronts and its eave and next points.
        /// </summary>
        private static bool HipPiece(out PieceAnnotation hip)
        {
            List<PieceAnnotation> bases = RoofPieces("roof-hip-base");
            hip = bases.Count > 0 ? bases[0] : null;
            return hip != null && hip.fronts.Count == 2 && Point(hip, "eave") != null && Point(hip, "next") != null;
        }

        /// <summary>
        /// A piece's connection point by name, or null.
        /// </summary>
        private static ConnectionPoint Point(PieceAnnotation piece, string name)
        {
            return piece.connections.Find(point => point.name == name);
        }

        /// <summary>
        /// The turn that points a hip piece's outer corner (between its two
        /// fronts) the way given.
        /// </summary>
        private static Quaternion HipRotation(PieceAnnotation piece, Vector3 outward)
        {
            Vector3 own = piece.fronts[0] + piece.fronts[1];
            own.y = 0f;
            return Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(own.normalized, Vector3.up));
        }

        /// <summary>
        /// How far the top of a stretch's ridge cap stands above the top edge
        /// of its slope: the top row's render box, tilted to the slope,
        /// stands above it.
        /// </summary>
        private static float RidgeCap(RoofStretch stretch, GeneratorSettings settings, PiecePlacer placer)
        {
            float pitch = settings.RoofPitch * Mathf.Deg2Rad;
            CatalogueEntry ridge = placer.Entry(RoofPieces("roof-ridge-end")[0]);
            float rowHeight = Middles("roof-end")[0].height * stretch.RowScale;
            return Mathf.Max(0f, Mathf.Sin(pitch) * (ridge.renderMax.y * stretch.RowScale - rowHeight * 0.5f) + Mathf.Cos(pitch) * ridge.renderMax.z);
        }

        /// <summary>
        /// The hip strips of a corner near enough a right angle, while the hip
        /// piece is there: on the outside of a convex corner, from where the
        /// two front eaves cross up to where the ridges cross; behind a
        /// valley, where the two back slopes meet in a hip, from where their
        /// eaves cross up to the same point (only with a back slope on both
        /// sides). The ridges cross as far in from both eaves as the shorter
        /// slope reaches (a lot's sides may differ), at the top of its ridge
        /// cap. Returns how many strips were placed.
        /// </summary>
        private static int HipStrips(RoofCorner corner, GeneratorSettings settings, PiecePlacer placer)
        {
            if (!corner.Square || !HipPiece(out PieceAnnotation hip)) {
                return 0;
            }

            RoofStretch a = corner.A;
            RoofStretch b = corner.B;
            float pitch = settings.RoofPitch * Mathf.Deg2Rad;
            RoofStretch fit = SlopeLength(a, settings) <= SlopeLength(b, settings) ? a : b;
            float slope = SlopeLength(fit, settings);
            float ridgeBack = slope * Mathf.Cos(pitch);
            Vector2 backA = -a.Run.Front;
            Vector2 backB = -b.Run.Front;
            // A metre further from both eaves is this far from where they cross.
            Vector2 inward = (backA + backB) / (1f + Vector2.Dot(backA, backB));
            float eaveHeight = a.Top - settings.RoofEaveDrop;
            Vector2 crossing = corner.Corner + inward * ridgeBack;
            Vector3 ridge = new(crossing.x, eaveHeight + slope * Mathf.Sin(pitch) + RidgeCap(fit, settings, placer), crossing.y);
            ModuleSpot spot = a.Frontage.Spots[a.First];
            Transform group = RoofGroup(a.Frontage.Buildings[spot.Building]);

            if (corner.Convex) {
                PlaceStrip(hip, corner.Corner, a.Run.Front + b.Run.Front, eaveHeight, ridge, settings, placer, group);
                return 1;
            }

            if (!a.BackSlope || !b.BackSlope) {
                return 0;
            }

            PlaceStrip(hip, corner.Corner + inward * (2f * ridgeBack), backA + backB, eaveHeight, ridge, settings, placer, group);
            return 1;
        }

        /// <summary>
        /// A hip strip: copies of the hip piece stacked up a hip, from where
        /// the two eaves cross (the corner, the two slopes' facing directions
        /// outward from it) to where the ridges cross. Each piece's outer
        /// corner faces out between the slopes, leaned back RoofHipLean so
        /// its faces lie flatter, on the slopes. The first piece's eave corner
        /// stands RoofHipOut out of the corner along each slope's front and
        /// RoofHipRaise above the eaves; the last piece's top is at the
        /// ridges' crossing; between them the pieces step evenly, each rising
        /// no more than RoofHipStep above the one below, so they overlap like
        /// scales and together cover where the two slopes' rows cross.
        /// </summary>
        private static void PlaceStrip(PieceAnnotation hip, Vector2 corner, Vector2 fronts, float eaveHeight, Vector3 ridge, GeneratorSettings settings,
            PiecePlacer placer, Transform group)
        {
            Vector3 outward = Level(fronts).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, outward);
            Quaternion turn = Quaternion.AngleAxis(-settings.RoofHipLean, right) * HipRotation(hip, outward);
            Vector3 eaveLocal = Point(hip, "eave").position;
            // From a piece's eave corner to its top (where the next would stand).
            Vector3 rise = turn * (Point(hip, "next").position - eaveLocal);
            Vector2 moved = corner + fronts * settings.RoofHipOut;
            Vector3 first = new(moved.x, eaveHeight + settings.RoofHipRaise, moved.y);
            Vector3 last = ridge - rise;
            int count = last.y > first.y ? 1 + Mathf.CeilToInt((last.y - first.y) / Mathf.Max(0.1f, settings.RoofHipStep) - 0.001f) : 1;

            for (int i = 0; i < count; i++) {
                Vector3 eave = count == 1 ? last : Vector3.Lerp(first, last, i / (float)(count - 1));
                placer.PlaceAt(hip, Vector3.zero, eave - turn * eaveLocal, turn, Vector3.one, group);
            }
        }

        /// <summary>
        /// Lays one stretch's roof (RoofShape set its rows): its front slope,
        /// its back slope if it has one, and at each end asked for a gable of
        /// two half gables. Its ends run RoofVergeOverhang past the stretch,
        /// but at a corner, where each row ends on the hip or valley line: a
        /// row of the front slope lying b back from the eave ends slant x b
        /// past where the front eaves cross, towards the corner; a row of the
        /// back slope, whose eave is twice the ridge's distance behind the
        /// front's, slant x (2 ridgeBack - b) past it (LaySlope takes b at
        /// the row's top edge into a valley, at its middle at a hip).
        /// </summary>
        private static void BuildRoof(RoofStretch stretch, Vector2 middle, GeneratorSettings settings, System.Random random, PiecePlacer placer)
        {
            Run run = stretch.Run;
            float pitch = settings.RoofPitch * Mathf.Deg2Rad;
            float eaveHeight = stretch.Top - settings.RoofEaveDrop;
            float eaveLine = stretch.Face + settings.RoofEaveOverhang;
            float slopeLength = SlopeLength(stretch, settings);
            float ridgeBack = slopeLength * Mathf.Cos(pitch);
            float ridgeHeight = eaveHeight + slopeLength * Mathf.Sin(pitch);
            ModuleSpot spot = stretch.Frontage.Spots[stretch.First];
            Transform group = RoofGroup(stretch.Frontage.Buildings[spot.Building]);

            if (stretch.RowScale <= 0f) {
                return;
            }

            float frontStart = stretch.From - settings.RoofVergeOverhang;
            float frontEnd = stretch.To + settings.RoofVergeOverhang;
            float frontStartSlant = 0f;
            float frontEndSlant = 0f;
            float backStart = frontStart;
            float backEnd = frontEnd;
            float backStartSlant = 0f;
            float backEndSlant = 0f;

            if (stretch.CornerFrom) {
                float slant = stretch.CornerFromSlant;
                frontStart = stretch.CornerFromAt;
                frontStartSlant = -slant;
                backStart = stretch.CornerFromAt - 2f * ridgeBack * slant;
                backStartSlant = slant;
            }

            if (stretch.CornerTo) {
                float slant = stretch.CornerToSlant;
                frontEnd = stretch.CornerToAt;
                frontEndSlant = slant;
                backEnd = stretch.CornerToAt + 2f * ridgeBack * slant;
                backEndSlant = -slant;
            }

            LaySlope(run, frontStart, frontStartSlant, frontEnd, frontEndSlant, stretch.CornerFrom, stretch.CornerTo, eaveLine, eaveHeight, run.Front,
                stretch.Rows, stretch.RowScale, settings, random, placer, group);

            if (stretch.BackSlope) {
                LaySlope(run, backStart, backStartSlant, backEnd, backEndSlant, stretch.CornerFrom, stretch.CornerTo, eaveLine - 2f * ridgeBack, eaveHeight,
                    -run.Front, stretch.Rows, stretch.RowScale, settings, random, placer, group);
            }

            if (stretch.GableFrom) {
                RoofGable(run, stretch.From, -run.Along, run.Front, stretch.Top, eaveLine, ridgeBack, ridgeHeight, settings, random, placer, group);

                if (stretch.BackSlope) {
                    RoofGable(run, stretch.From, -run.Along, -run.Front, stretch.Top, eaveLine - 2f * ridgeBack, ridgeBack, ridgeHeight, settings, random,
                        placer, group);
                }
            }

            if (stretch.GableTo) {
                RoofGable(run, stretch.To, run.Along, run.Front, stretch.Top, eaveLine, ridgeBack, ridgeHeight, settings, random, placer, group);

                if (stretch.BackSlope) {
                    RoofGable(run, stretch.To, run.Along, -run.Front, stretch.Top, eaveLine - 2f * ridgeBack, ridgeBack, ridgeHeight, settings, random,
                        placer, group);
                }
            }
        }

        /// <summary>
        /// One slope of a roof: rows of panels from its eave (a line eaveLine
        /// forward of the run's street edge, at eaveHeight) up towards the
        /// ridge, the slope facing towards (the run's front for the street
        /// side, its back for the back slope). Each row runs from start to
        /// end along the run, each moved by its slant for every metre the
        /// row lies back from the eave (so the rows end on a hip or valley
        /// line: its top edge on a valley, its middle on a hip). Along it as
        /// many panels as round to its length,
        /// scaled to fill it: the verge pieces at the two ends, mirrored at
        /// the end their verge isn't on, plain panels between, or two
        /// mirrored half pieces for a slope of one panel; the top row's ends
        /// are the ridge pieces. An end at a corner (cornerStart at start,
        /// cornerEnd at end) runs under the hip strip or into the valley,
        /// its verge hidden there; a one-panel row at a corner is a whole
        /// panel, not two halves. (A verge end's top tiles are set back, so
        /// wherever one faces the next panel - every plain panel's does -
        /// RoofPanelOverlap must cover the set-back, or a slit shows.)
        /// Nothing for a row under RoofPanelOverlap long.
        /// </summary>
        private static void LaySlope(Run run, float start, float startSlant, float end, float endSlant, bool cornerStart, bool cornerEnd, float eaveLine,
            float eaveHeight, Vector2 towards, int rows, float rowScale, GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group)
        {
            float pitch = settings.RoofPitch * Mathf.Deg2Rad;
            float cos = Mathf.Cos(pitch);
            float sin = Mathf.Sin(pitch);
            PieceAnnotation sample = Middles("roof-end")[0];
            float rowHeight = sample.height;
            float step = rowHeight * rowScale - settings.RoofRowOverlap;
            Vector3 front = Level(towards);
            Vector3 normal = front * sin + Vector3.up * cos;
            Vector3 upSlope = -front * cos + Vector3.up * sin;
            Quaternion rotation = Quaternion.LookRotation(normal, upSlope);
            bool plusAlong = Vector3.Dot(rotation * Vector3.right, Level(run.Along)) > 0f;
            // Which of the roof's ends, by the panels' own X, is at a corner.
            bool plusCorner = plusAlong ? cornerEnd : cornerStart;
            bool minusCorner = plusAlong ? cornerStart : cornerEnd;

            for (int row = 0; row < rows; row++) {
                bool ridge = row == rows - 1;
                List<PieceAnnotation> ends = RoofPieces(ridge ? "roof-ridge-end" : "roof-end");
                List<PieceAnnotation> halves = RoofPieces(ridge ? "roof-ridge-end-half" : "roof-end-half");
                float up = row * step + rowHeight * rowScale * 0.5f;
                // The row's ends on the lines slanting from the roof's: a row
                // runs into a valley (an end moved towards the corner the
                // further back it lies) until its top edge is RoofPanelOverlap
                // past the valley line, so the valley is closed all the way
                // up even by a verge end's set-back tiles, the overrun and
                // its bottom corner hidden under the other roof; it stops
                // short of a hip (an end moved the other way) where its
                // middle meets the hip line, so its two corners straddle it,
                // under the hip strip.
                float middle = up * cos;
                float top = (up + rowHeight * rowScale * 0.5f) * cos;
                float rowStart = start + (startSlant < 0f ? startSlant * top - settings.RoofPanelOverlap : startSlant * middle);
                float rowEnd = end + (endSlant > 0f ? endSlant * top + settings.RoofPanelOverlap : endSlant * middle);
                float length = rowEnd - rowStart;

                if (length <= settings.RoofPanelOverlap) {
                    continue;
                }

                // Along the roof: panels round to its length, overlapping.
                int panels = Mathf.Max(1, Mathf.RoundToInt(length / sample.width));
                float slot = (length + (panels - 1) * settings.RoofPanelOverlap) / panels;
                PieceAnnotation previous = null;

                for (int j = 0; j < panels; j++) {
                    float along = rowStart + slot * 0.5f + j * (slot - settings.RoofPanelOverlap);
                    Vector2 onEave = run.Start + run.Along * along + run.Front * eaveLine;
                    Vector3 centreOfPanel = new Vector3(onEave.x, eaveHeight, onEave.y) + upSlope * up;

                    if (panels == 1 && halves.Count > 0 && !plusCorner && !minusCorner) {
                        // Two half pieces, each with its verge at its end.
                        PieceAnnotation half = PiecePlacer.Pick(halves, null, random);
                        float halfScale = slot * 0.5f / Mathf.Max(0.01f, placer.Entry(half).renderMax.x);
                        placer.PlaceAt(half, Vector3.zero, centreOfPanel, rotation, new Vector3(halfScale, rowScale, 1f), group);
                        placer.PlaceAt(half, Vector3.zero, centreOfPanel, rotation, new Vector3(-halfScale, rowScale, 1f), group);
                        continue;
                    }

                    // The panel at the roof's +X end (its own +X, where the
                    // verge pieces carry their verge) is a verge piece as it
                    // is, the one at the other end a mirrored one - at a
                    // corner too: its verge is hidden there, under the hip
                    // strip or the other roof, while turned inward it would
                    // meet the next panel's verge (every plain panel's faces
                    // one way) and the two set-back verge ends leave a slit.
                    bool plusEnd = j == (plusAlong ? panels - 1 : 0);
                    bool minusEnd = j == (plusAlong ? 0 : panels - 1);
                    PieceAnnotation piece = PiecePlacer.Pick(plusEnd || minusEnd ? ends : Middles(ridge ? "roof-ridge-end" : "roof-end"), previous, random);
                    previous = piece;
                    float widthScale = slot / piece.width;
                    bool mirrored = minusEnd && !plusEnd;
                    placer.PlaceAt(piece, Vector3.zero, centreOfPanel, rotation, new Vector3(mirrored ? -widthScale : widthScale, rowScale, 1f),
                        group);
                }
            }
        }

        /// <summary>
        /// A half gable closing one slope of a roof at its end (a distance
        /// along its run, outward the way out of the end): a roof-gable-half
        /// piece (a triangle with one upright edge) facing out of the end, its
        /// upright edge under the ridge, its sloping edge under the slope (which
        /// faces towards, its eave a line eaveLine forward of the street edge)
        /// from where the slope's underside comes down to the top of the walls,
        /// its base RoofGableSink into them; scaled to fit, its thickness inside
        /// the roof's end. The two slopes' halves make a whole gable.
        /// </summary>
        private static void RoofGable(Run run, float position, Vector2 outward, Vector2 towards, float top, float eaveLine, float ridgeBack,
            float ridgeHeight, GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group)
        {
            PieceAnnotation piece = PiecePlacer.Pick(RoofPieces("roof-gable-half"), null, random);
            CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

            if (entry == null) {
                return;
            }

            // Where the roof's underside comes down to the walls' top.
            float under = settings.RoofEaveDrop / Mathf.Tan(settings.RoofPitch * Mathf.Deg2Rad);
            float width = ridgeBack - under;
            float height = ridgeHeight - top + settings.RoofGableSink;

            if (width <= settings.SizeTolerance || height <= settings.SizeTolerance) {
                return;
            }

            Vector3 out3 = Level(outward);
            Quaternion rotation = Quaternion.LookRotation(out3, Vector3.up);
            // Its +X runs from the upright edge down the slope, the way the slope faces.
            float sign = Vector3.Dot(rotation * Vector3.right, Level(towards)) > 0f ? 1f : -1f;
            float thickness = Mathf.Max(-entry.renderMin.z, entry.renderMax.z);
            Vector3 scale = new(sign * width / (entry.renderMax.x - entry.renderMin.x), height / (entry.renderMax.y - entry.renderMin.y), 1f);
            float ridgeLine = eaveLine - Vector2.Dot(towards, run.Front) * ridgeBack;
            Vector2 onRidge = run.Start + run.Along * position - outward * thickness + run.Front * ridgeLine;
            placer.PlaceAt(piece, new Vector3(entry.renderMin.x, entry.renderMin.y, 0f), new Vector3(onRidge.x, top - settings.RoofGableSink, onRidge.y), rotation,
                scale, group);
        }
    }
}
