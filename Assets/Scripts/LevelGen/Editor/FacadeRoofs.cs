using System.Collections.Generic;
using UnityEditor;
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
        // How far forward of its pivot each dormer piece's front wall stands
        // (DormerFront), by guid, read once per Build call.
        private static readonly Dictionary<string, float> _dormerFronts = new();
        // The lowest vertices of a dormer piece: within this of the bottom
        // of its render box.
        private const float DormerBottom = 0.05f;
        // Gable pieces whose shape (height over width) is within this of
        // the one nearest a roof end's are all as good for it.
        private const float GableShapeTolerance = 0.1f;

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
        /// in (a valley in front, a hip behind), the angle between the two
        /// eaves there (the building's corner for a convex one; the angle
        /// the back slopes' eaves meet at behind a concave one) and whether
        /// a hip strip fits it (within the hip piece's angle range).
        /// </summary>
        private class RoofCorner
        {
            public RoofStretch A;
            public RoofStretch B;
            public Vector2 Corner;
            public bool Convex;
            public float Angle;
            public bool Hipped;
        }

        /// <summary>
        /// Gathers the usable roof pieces by category.
        /// </summary>
        private static void GatherRoofPieces(PieceAnnotations annotations, PiecePlacer placer)
        {
            _roofs.Clear();
            _dormerFronts.Clear();

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

            BuildRoofFeatures(stretches, settings, random, placer, log);
            BuildRoofRailings(stretches, settings, random, placer, log);
            log.Add($"Roofs: {stretches.Count} roof(s), {corners.Count} corner(s), {strips} hip strip(s), {placer.Placed - pieces} piece(s).");
        }

        /// <summary>
        /// Gives each building a wall colour and, separately, a roof colour:
        /// the plain one (ColourVariants.Default: the maintainer's unsuffixed
        /// copy of each piece's material, else the piece's own) or a colour
        /// variant (ColourVariants: a material in the variant folders under
        /// Assets/Art/Materials/Environment named after a piece's material with a colour
        /// suffix), each chosen at random from the variants the pieces
        /// concerned have - so a roof may take a colour no wall has. The
        /// wall colour goes to every piece under the building's group (walls,
        /// beams, side walls, corners, dressing), the roof colour to every
        /// piece under its roof group (tiles, hips, dormers), for each of its
        /// materials that has that variant. Run last of all, over every
        /// frontage, lot and dead end. Buildings joined by corners are one
        /// building to the eye, so each such set (CornerLeaders) takes one
        /// wall colour and one roof colour, chosen from the variants of every
        /// member's materials and applied to them all. Two draws are made per
        /// building (walls, then roof) whether or not there are variants (a
        /// set uses its first building's), so no other draw moves when
        /// variant materials are added. Logs the colours used (per building),
        /// and any material met that has variants but none for a colour
        /// chosen (the one to clone).
        /// </summary>
        private static void ColourBuildings(List<Frontage> frontages, List<Joint> joints, System.Random random, List<string> log)
        {
            Dictionary<Building, Building> leaders = CornerLeaders(frontages, joints);
            // Every building's own groups (its facade group and, if it got
            // a roof, its roof group - Child() would make an empty one),
            // gathered per set under its first building.
            Dictionary<Building, List<Transform>> wallSets = new();
            Dictionary<Building, List<Transform>> roofSets = new();
            Dictionary<Building, int> members = new();

            foreach (Frontage frontage in frontages) {
                foreach (Building building in frontage.Buildings) {
                    Building first = leaders[building];

                    if (!wallSets.TryGetValue(first, out List<Transform> walls)) {
                        wallSets[first] = walls = new List<Transform>();
                        roofSets[first] = new List<Transform>();
                        members[first] = 0;
                    }

                    members[first]++;
                    walls.Add(building.Group);
                    Transform roofOwner = _roofParent.Find(building.Group.parent.name);
                    Transform roof = roofOwner?.Find(building.Group.name);

                    if (roof != null) {
                        roofSets[first].Add(roof);
                    }
                }
            }

            ColourVariants variants = new();
            HashSet<string> missing = new();
            Dictionary<string, int> wallsUsed = new();
            Dictionary<string, int> roofsUsed = new();
            int plain = 0;

            foreach (Frontage frontage in frontages) {
                foreach (Building building in frontage.Buildings) {
                    double wallDraw = random.NextDouble();
                    double roofDraw = random.NextDouble();

                    if (leaders[building] != building) {
                        continue;
                    }

                    string wallColour = Colour(variants, wallSets[building], wallDraw, missing);
                    string roofColour = Colour(variants, roofSets[building], roofDraw, missing);

                    if (wallColour == null && roofColour == null) {
                        plain += members[building];
                        continue;
                    }

                    if (wallColour != null) {
                        wallsUsed[wallColour] = wallsUsed.TryGetValue(wallColour, out int count) ? count + members[building] : members[building];
                    }

                    if (roofColour != null) {
                        roofsUsed[roofColour] = roofsUsed.TryGetValue(roofColour, out int count) ? count + members[building] : members[building];
                    }
                }
            }

            if (wallsUsed.Count == 0 && roofsUsed.Count == 0) {
                return;
            }

            string line = $"Building colours: walls {ColourCounts(wallsUsed)}; roofs {ColourCounts(roofsUsed)}";

            if (plain > 0) {
                line += $"; {plain} building(s) with no variant";
            }

            if (missing.Count > 0) {
                List<string> names = new(missing);
                names.Sort();
                line += $"; no variant for some colours: {string.Join(", ", names)}";
            }

            log.Add(line + ".");
        }

        /// <summary>
        /// Colours some groups (a set's facade groups, or its roof groups) by
        /// one draw: the suffix the draw picks among the colours their pieces
        /// have variants for (ColourVariants.Suffixes, sorted), applied to
        /// every renderer under them. Null, and nothing applied, when none
        /// of their materials has a variant.
        /// </summary>
        private static string Colour(ColourVariants variants, List<Transform> groups, double draw, HashSet<string> missing)
        {
            List<string> suffixes = variants.Suffixes(groups);

            if (suffixes.Count == 0) {
                return null;
            }

            string suffix = suffixes[Mathf.Min((int)(draw * suffixes.Count), suffixes.Count - 1)];

            foreach (Transform group in groups) {
                variants.Apply(group, suffix, missing);
            }

            return suffix;
        }

        /// <summary>
        /// The colours used and how many buildings took each, for the log:
        /// "brown x12, default x10, grey x8", or "none".
        /// </summary>
        private static string ColourCounts(Dictionary<string, int> used)
        {
            if (used.Count == 0) {
                return "none";
            }

            List<string> parts = new();

            foreach (KeyValuePair<string, int> pair in used) {
                parts.Add($"{pair.Key} x{pair.Value}");
            }

            parts.Sort();
            return string.Join(", ", parts);
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
        /// run whose tops (in metres) and top storeys' faces line up, each
        /// ending at the outer face of the end wall closing its top storey
        /// there (EndOut) - at a run's ends, with no such wall, as far as a
        /// corner carried the top storey. A lot's slopes rise from every side
        /// to the footprint's middle. Adds them to stretches.
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
                    From = spot.Position - EndOut(spot.SideFaceStart, runStart ? spot.CarriedStart : 0f),
                    To = end.Position + Spacing(end, frontage.Buildings) + EndOut(end.SideFaceEnd, runEnd ? end.CarriedEnd : 0f),
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
        /// How far out of a column's end its roof runs: to the outer face of
        /// the side walls closing its top storey there (the end wall its
        /// gable stands on, ModuleSpot.SideFaceStart / SideFaceEnd), or with
        /// none laid as far as a corner carried the top storey.
        /// </summary>
        private static float EndOut(float sideFace, float carried)
        {
            return sideFace > 0f ? sideFace : carried;
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
        /// taken where its angle lies within RoofHipAngleMin to Max, the range
        /// the hip piece can be stretched to cover (a wider one has the other
        /// side's roof run on over it, a sharper one its gables).
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
                float angle = Vector2.Angle(outA, outB);
                bool hipped = angle >= settings.RoofHipAngleMin && angle <= settings.RoofHipAngleMax;
                // Where the two front eaves cross.
                Vector2 eaveA = a.Run.Start + a.Run.Front * (a.Face + settings.RoofEaveOverhang);
                Vector2 eaveB = b.Run.Start + b.Run.Front * (b.Face + settings.RoofEaveOverhang);

                // No corner between a lot and a facade, roofs of different
                // heights, one folded back on itself (no line to meet on), a
                // convex corner outside the hip piece's range, or parallel eaves -
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
                    || (convex && !hipped) || !meet || !near) {
                    SetGable(a, joint.A.AtStart);
                    SetGable(b, joint.B.AtStart);
                    continue;
                }

                MarkCorner(a, joint.A.AtStart, alongA, slantA);
                MarkCorner(b, joint.B.AtStart, alongB, slantB);
                corners.Add(new RoofCorner { A = a, B = b, Corner = corner, Convex = convex, Angle = angle, Hipped = hipped });
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
        /// The hip strips of a corner within the hip piece's angle range, while
        /// the piece is there: on the outside of a convex corner, from where the
        /// two front eaves cross up to where the ridges cross; behind a
        /// valley, where the two back slopes meet in a hip, from where their
        /// eaves cross up to the same point (only with a back slope on both
        /// sides). The ridges cross as far in from both eaves as the shorter
        /// slope reaches (a lot's sides may differ), at the top of its ridge
        /// cap. Returns how many strips were placed.
        /// </summary>
        private static int HipStrips(RoofCorner corner, GeneratorSettings settings, PiecePlacer placer)
        {
            if (!corner.Hipped || !HipPiece(out PieceAnnotation hip)) {
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
                PlaceStrip(hip, corner.Corner, a.Run.Front + b.Run.Front, corner.Angle, eaveHeight, ridge, settings, placer, group);
                return 1;
            }

            if (!a.BackSlope || !b.BackSlope) {
                return 0;
            }

            PlaceStrip(hip, corner.Corner + inward * (2f * ridgeBack), backA + backB, corner.Angle, eaveHeight, ridge, settings, placer, group);
            return 1;
        }

        /// <summary>
        /// A hip strip: copies of the hip piece stacked up a hip, from where
        /// the two eaves cross (the corner, the two slopes' facing directions
        /// outward from it) to where the ridges cross. Each piece's outer
        /// corner faces out between the slopes, leaned back RoofHipLean so
        /// its faces lie flatter, on the slopes. The piece is made for a
        /// right-angled corner (its two faces a quarter turn apart in plan),
        /// so for the angle the two eaves meet at it is stretched across the
        /// hip by tan(angle / 2), which turns its faces to lie along the two
        /// slopes, and in height by what keeps their pitch as it was. That
        /// stretch runs across the hip, not along the piece's own axes, so
        /// the strip's pieces stand under one "Hip" empty turned to the hip
        /// and holding the scale, each piece turned inside it; later passes
        /// find pieces by name under the building's roof group, so the empty
        /// is nothing to them. The first piece's eave corner stands
        /// RoofHipOut out of the corner along each slope's front and
        /// RoofHipRaise above the eaves; the last piece's top is at the
        /// ridges' crossing; between them the pieces step evenly, each rising
        /// no more than RoofHipStep above the one below, so they overlap like
        /// scales and together cover where the two slopes' rows cross.
        /// </summary>
        private static void PlaceStrip(PieceAnnotation hip, Vector2 corner, Vector2 fronts, float angle, float eaveHeight, Vector3 ridge,
            GeneratorSettings settings, PiecePlacer placer, Transform group)
        {
            Vector3 outward = Level(fronts).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, outward);
            // The strip's frame: z out between the slopes along the hip, x
            // across it, the whole leaned back.
            Quaternion frame = Quaternion.AngleAxis(-settings.RoofHipLean, right) * Quaternion.LookRotation(outward, Vector3.up);
            // The piece turned within the frame so its own corner, between
            // its two fronts, faces the frame's z.
            Vector3 own = hip.fronts[0] + hip.fronts[1];
            own.y = 0f;
            Quaternion inFrame = Quaternion.Inverse(Quaternion.LookRotation(own.normalized, Vector3.up));
            // Across the hip by tan(angle / 2): a face at 45 degrees to the hip
            // in plan turns to angle / 2 from it, along its slope's eave. Its
            // level normal shrinks by sqrt((1 + 1 / across^2) / 2), so the
            // height is scaled by the inverse to keep the face's pitch.
            float across = Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad);
            float up = across * Mathf.Sqrt(2f / (1f + across * across));
            Vector3 scale = new(across, up, 1f);
            Quaternion turn = frame * inFrame;
            Vector3 eaveLocal = Point(hip, "eave").position;
            // From a piece's eave corner to its top (where the next would stand).
            Vector3 rise = frame * Vector3.Scale(scale, inFrame * (Point(hip, "next").position - eaveLocal));
            Vector2 moved = corner + fronts * settings.RoofHipOut;
            Vector3 first = new(moved.x, eaveHeight + settings.RoofHipRaise, moved.y);
            Vector3 last = ridge - rise;
            int count = last.y > first.y ? 1 + Mathf.CeilToInt((last.y - first.y) / Mathf.Max(0.1f, settings.RoofHipStep) - 0.001f) : 1;
            Transform strip = Group(group, "Hip");
            strip.SetPositionAndRotation(first, frame);
            strip.localScale = scale;

            for (int i = 0; i < count; i++) {
                Vector3 eave = count == 1 ? last : Vector3.Lerp(first, last, i / (float)(count - 1));
                // Placed in world space under the scaled empty: the placer sets
                // the world pose, and Unity works the local one out.
                placer.PlaceAt(hip, Vector3.zero, eave - frame * Vector3.Scale(scale, inFrame * eaveLocal), turn, Vector3.one, strip);
            }
        }

        /// <summary>
        /// Lays one stretch's roof (RoofShape set its rows): its front slope,
        /// its back slope if it has one, and a gable at each end asked for.
        /// Its ends run RoofVergeOverhang past the stretch,
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
                RoofGable(run, stretch.From, -run.Along, stretch.Top, eaveLine - ridgeBack, ridgeBack, ridgeHeight, settings, random, placer, group);
            }

            if (stretch.GableTo) {
                RoofGable(run, stretch.To, run.Along, stretch.Top, eaveLine - ridgeBack, ridgeBack, ridgeHeight, settings, random, placer, group);
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
        /// are the ridge pieces, except that a top row of one panel takes the
        /// plain end piece, never a ridge piece. An end at a corner (cornerStart at start,
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
                // The top row's ends are the ridge pieces - but a top row of
                // one panel takes the plain end piece, never a ridge piece.
                bool ridge = row == rows - 1 && panels > 1;
                List<PieceAnnotation> ends = RoofPieces(ridge ? "roof-ridge-end" : "roof-end");
                List<PieceAnnotation> halves = RoofPieces(ridge ? "roof-ridge-end-half" : "roof-end-half");
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
        /// The gable closing a roof at its end (a distance along its run,
        /// outward the way out of the end): one roof-gable piece, a whole
        /// triangle, facing out of the end with its pivot plane on it (the
        /// outer face of the end wall the roof stops at: the pieces' wall
        /// face is on their pivot plane, their beam ends running on past
        /// it under the tiles' verge), its apex under the
        /// ridge (ridgeLine forward of the street edge, at ridgeHeight) and
        /// its base on the top of the walls, sunk RoofGableSink into them,
        /// spanning from where one slope's underside comes down to the
        /// walls' top to where the other's does; scaled to fit (GablePiece
        /// picks the piece whose own shape is nearest). A roof with one
        /// slope (a lot's hipped side) gets the same whole piece, its back
        /// half inside the building under the other sides' slopes.
        /// </summary>
        private static void RoofGable(Run run, float position, Vector2 outward, float top, float ridgeLine, float ridgeBack, float ridgeHeight,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group)
        {
            // Where a slope's underside comes down to the walls' top, in from
            // its eave.
            float under = settings.RoofEaveDrop / Mathf.Tan(settings.RoofPitch * Mathf.Deg2Rad);
            float width = 2f * (ridgeBack - under);
            float height = ridgeHeight - top + settings.RoofGableSink;

            if (width <= settings.SizeTolerance || height <= settings.SizeTolerance) {
                return;
            }

            PieceAnnotation piece = GablePiece(height / width, random, placer);
            CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

            if (entry == null) {
                return;
            }

            // Its pivot plane (the wall's face, in every gable piece; its
            // beam ends run on half a metre past it, under the tiles' verge)
            // on the end.
            Vector2 onRidge = run.Start + run.Along * position + run.Front * ridgeLine;
            Vector3 scale = new(width / (entry.renderMax.x - entry.renderMin.x), height / (entry.renderMax.y - entry.renderMin.y), 1f);
            placer.PlaceAt(piece, new Vector3((entry.renderMin.x + entry.renderMax.x) * 0.5f, entry.renderMin.y, 0f),
                new Vector3(onRidge.x, top - settings.RoofGableSink, onRidge.y), Quaternion.LookRotation(Level(outward), Vector3.up), scale, group);
        }

        /// <summary>
        /// The gable piece for a triangle of the shape given (its height over
        /// its width): of the usable roof-gable pieces, the one whose own
        /// shape is nearest it (so its timbers are stretched least), drawn at
        /// random among those within GableShapeTolerance of the nearest (the
        /// A and B of one size). Null with none.
        /// </summary>
        private static PieceAnnotation GablePiece(float shape, System.Random random, PiecePlacer placer)
        {
            List<PieceAnnotation> gables = RoofPieces("roof-gable");
            float nearest = float.MaxValue;

            foreach (PieceAnnotation gable in gables) {
                nearest = Mathf.Min(nearest, Mathf.Abs(GableShape(gable, placer) - shape));
            }

            List<PieceAnnotation> near = gables.FindAll(gable => Mathf.Abs(GableShape(gable, placer) - shape) <= nearest + GableShapeTolerance);
            return PiecePlacer.Pick(near, null, random);
        }

        /// <summary>
        /// A gable piece's own shape: the height of its render box over its
        /// width.
        /// </summary>
        private static float GableShape(PieceAnnotation gable, PiecePlacer placer)
        {
            CatalogueEntry entry = placer.Entry(gable);
            return (entry.renderMax.y - entry.renderMin.y) / Mathf.Max(0.01f, entry.renderMax.x - entry.renderMin.x);
        }

        /// <summary>
        /// A chimney or a dormer on each building's roof: for every building
        /// a stretch's roof runs over, where the roof over that building is
        /// at least RoofFeatureMinPanels tile panels long (counted as the top
        /// row counts them), one piece, placed once per building (its first
        /// roof long enough). Which kind is drawn at random where both are
        /// verified: a chimney (roof-chimney) or a dormer (roof-dormer), both
        /// set into the front slope, the chimney higher up; one that has no
        /// room on the slope (a scaled-down one) gives way to the other. Along the roof the
        /// piece stands within RoofFeatureSpread of the building's length
        /// about its middle, never nearer an end than half its own width.
        /// Logs how many of each were placed and the buildings left without.
        /// </summary>
        private static void BuildRoofFeatures(List<RoofStretch> stretches, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            List<string> log)
        {
            List<PieceAnnotation> chimneys = RoofPieces("roof-chimney");
            List<PieceAnnotation> dormers = RoofPieces("roof-dormer");

            if (chimneys.Count == 0 && dormers.Count == 0) {
                log.Add("Roof features: none (no verified roof-chimney or roof-dormer piece).");
                return;
            }

            float panelWidth = Middles("roof-end")[0].width;
            HashSet<Building> seen = new();
            HashSet<Building> placed = new();
            int chimneysPlaced = 0;
            int dormersPlaced = 0;
            int unfit = 0;

            foreach (RoofStretch stretch in stretches) {
                List<ModuleSpot> spots = stretch.Frontage.Spots;
                List<Building> buildings = stretch.Frontage.Buildings;
                int first = stretch.First;

                // The roof over each building the stretch runs across, in turn.
                while (first <= stretch.Last) {
                    int last = first;

                    while (last + 1 <= stretch.Last && spots[last + 1].Building == spots[first].Building) {
                        last++;
                    }

                    Building building = buildings[spots[first].Building];
                    float from = spots[first].Position;
                    float to = spots[last].Position + Spacing(spots[last], buildings);
                    first = last + 1;
                    seen.Add(building);

                    if (placed.Contains(building) || Mathf.RoundToInt((to - from) / panelWidth) < settings.RoofFeatureMinPanels) {
                        continue;
                    }

                    // Either kind, the other where the slope has no room
                    // for the one drawn.
                    bool chimney = chimneys.Count > 0 && (dormers.Count == 0 || random.NextDouble() < 0.5);
                    bool done = false;

                    for (int attempt = 0; attempt < 2 && !done; attempt++) {
                        if (chimney && chimneys.Count > 0) {
                            done = PlaceChimney(stretch, building, from, to, PiecePlacer.Pick(chimneys, null, random), settings, random, placer);
                            chimneysPlaced += done ? 1 : 0;
                        } else if (!chimney && dormers.Count > 0) {
                            done = PlaceDormer(stretch, building, from, to, PiecePlacer.Pick(dormers, null, random), settings, random, placer);
                            dormersPlaced += done ? 1 : 0;
                        }

                        chimney = !chimney;
                    }

                    if (done) {
                        placed.Add(building);
                    } else {
                        unfit++;
                    }
                }
            }

            string line = $"Roof features: {chimneysPlaced} chimney(s), {dormersPlaced} dormer(s)";
            int narrow = seen.Count - placed.Count - unfit;

            if (narrow > 0) {
                line += $"; {narrow} building(s) with no roof {settings.RoofFeatureMinPanels} panel(s) long";
            }

            if (unfit > 0) {
                line += $"; {unfit} with no room for one";
            }

            log.Add(line + ".");
        }

        /// <summary>
        /// Iron railings along the ridge of some buildings' roofs, as the
        /// pack's example buildings have them: at RoofRailingShare of the
        /// buildings (one draw each, always made), on the first roof over
        /// the building, along the part of its ridge over the building -
        /// kept RoofRailingInset in from the roof's ends and, at a corner,
        /// from where the ridge meets the other roof's - as many
        /// roof-railing pieces end to end as fit with RoofRailingGap
        /// between, the longest kind first and shorter ones in what is
        /// left, the row centred along the ridge. Each stands upright on
        /// the ridge, its feet RoofRailingSink below the ridge cap's top,
        /// leaning at random up to RoofRailingLean along the ridge and a
        /// third as much across it. Logs how many railings on how many
        /// buildings.
        /// </summary>
        private static void BuildRoofRailings(List<RoofStretch> stretches, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            List<string> log)
        {
            List<PieceAnnotation> railings = new(RoofPieces("roof-railing"));

            if (railings.Count == 0 || settings.RoofRailingShare <= 0f) {
                log.Add("Roof railings: none (no verified roof-railing piece, or roofRailingShare is 0).");
                return;
            }

            // The longest kind first.
            railings.Sort((a, b) => RailingWidth(b, placer).CompareTo(RailingWidth(a, placer)));
            // The roofs were placed this generation: their colliders must be
            // in place for the rays that seat the railings.
            Physics.SyncTransforms();
            HashSet<Building> seen = new();
            int buildings = 0;
            int pieces = 0;

            foreach (RoofStretch stretch in stretches) {
                List<ModuleSpot> spots = stretch.Frontage.Spots;
                int first = stretch.First;

                // The roof over each building the stretch runs across.
                while (first <= stretch.Last) {
                    int last = first;

                    while (last + 1 <= stretch.Last && spots[last + 1].Building == spots[first].Building) {
                        last++;
                    }

                    Building building = stretch.Frontage.Buildings[spots[first].Building];
                    float from = spots[first].Position;
                    float to = spots[last].Position + Spacing(spots[last], stretch.Frontage.Buildings);
                    first = last + 1;

                    if (!seen.Add(building)) {
                        continue;
                    }

                    bool wanted = random.NextDouble() < settings.RoofRailingShare;

                    if (!wanted) {
                        continue;
                    }

                    int placed = PlaceRailings(stretch, building, from, to, railings, settings, random, placer);
                    buildings += placed > 0 ? 1 : 0;
                    pieces += placed;
                }
            }

            log.Add($"Roof railings: {pieces} on {buildings} building(s).");
        }

        /// <summary>
        /// A railing piece's length along the ridge (its own X).
        /// </summary>
        private static float RailingWidth(PieceAnnotation piece, PiecePlacer placer)
        {
            CatalogueEntry entry = placer.Entry(piece);
            return entry.renderMax.x - entry.renderMin.x;
        }

        /// <summary>
        /// Lays a row of railings along a stretch's ridge over a building
        /// (from to to along the run; see BuildRoofRailings), the pieces
        /// given longest first. Returns how many were placed: none where
        /// the ridge is too short for the shortest.
        /// </summary>
        private static int PlaceRailings(RoofStretch stretch, Building building, float from, float to, List<PieceAnnotation> railings,
            GeneratorSettings settings, System.Random random, PiecePlacer placer)
        {
            Run run = stretch.Run;
            float pitch = settings.RoofPitch * Mathf.Deg2Rad;
            float slopeLength = SlopeLength(stretch, settings);
            float ridgeBack = slopeLength * Mathf.Cos(pitch);
            float ridgeLine = stretch.Face + settings.RoofEaveOverhang - ridgeBack;
            float ridgeTop = stretch.Top - settings.RoofEaveDrop + slopeLength * Mathf.Sin(pitch) + RidgeCap(stretch, settings, placer);
            // The ridge over the building: in from the roof's ends, and at
            // a corner from where its ridge meets the other roof's (as far
            // in from the eaves' crossing as the ridge lies back).
            float ridgeStart = stretch.CornerFrom ? stretch.CornerFromAt + Mathf.Abs(stretch.CornerFromSlant) * ridgeBack : stretch.From;
            float ridgeEnd = stretch.CornerTo ? stretch.CornerToAt - Mathf.Abs(stretch.CornerToSlant) * ridgeBack : stretch.To;
            float start = Mathf.Max(from, ridgeStart) + settings.RoofRailingInset;
            float end = Mathf.Min(to, ridgeEnd) - settings.RoofRailingInset;
            float room = end - start;

            if (room <= 0f) {
                return 0;
            }

            // As many of the longest as fit, then shorter ones in what is
            // left.
            List<PieceAnnotation> row = new();
            float used = 0f;

            foreach (PieceAnnotation piece in railings) {
                float width = RailingWidth(piece, placer);

                while (used + width + (row.Count > 0 ? settings.RoofRailingGap : 0f) <= room) {
                    used += width + (row.Count > 0 ? settings.RoofRailingGap : 0f);
                    row.Add(piece);
                }
            }

            if (row.Count == 0) {
                return 0;
            }

            Quaternion upright = Quaternion.LookRotation(Level(run.Front), Vector3.up);
            Transform group = RoofGroup(building);
            float along = start + (room - used) * 0.5f;
            Vector3 alongWorld = Level(run.Along);
            int placed = 0;

            foreach (PieceAnnotation piece in row) {
                CatalogueEntry entry = placer.Entry(piece);
                float width = entry.renderMax.x - entry.renderMin.x;
                Vector2 onRidge = run.Start + run.Along * (along + width * 0.5f) + run.Front * ridgeLine;
                Vector3 centre = new(onRidge.x, ridgeTop, onRidge.y);
                float top = RoofTopAt(centre);
                Vector3 seat = new(onRidge.x, top - settings.RoofRailingSink, onRidge.y);
                along += width + settings.RoofRailingGap;

                // Not where the ridge is buried under another roof (the
                // roof cull takes the hidden ridge away, and a railing
                // would stand on nothing, inside the other roof).
                if (Physics.Raycast(seat + Vector3.up * 0.3f, Vector3.up, 3f, LayerMask.GetMask("Environment"), QueryTriggerInteraction.Ignore)) {
                    continue;
                }

                // Nor where the roof under either end isn't the level ridge
                // under its middle (a hip, a corner, past a gable).
                float reach = width * 0.5f - 0.1f;

                if (Mathf.Abs(RoofTopAt(centre + alongWorld * reach) - top) > RailingLevel || Mathf.Abs(RoofTopAt(centre - alongWorld * reach) - top) > RailingLevel) {
                    continue;
                }

                float lean = ((float)random.NextDouble() * 2f - 1f) * settings.RoofRailingLean;
                float tilt = ((float)random.NextDouble() * 2f - 1f) * settings.RoofRailingLean / 3f;
                // Its feet (the middle of its bottom) on the seat, leaning
                // about them.
                Vector3 feet = new((entry.renderMin.x + entry.renderMax.x) * 0.5f, entry.renderMin.y, (entry.renderMin.z + entry.renderMax.z) * 0.5f);
                placer.PlaceAt(piece, feet, seat, upright * Quaternion.Euler(tilt, 0f, lean), Vector3.one, group);
                placed++;
            }

            return placed;
        }

        // How much the roof under a railing's ends may differ in height from
        // that under its middle for the ridge to count as level there.
        private const float RailingLevel = 0.3f;
        // The hits of the ray that seats a ridge railing.
        private static readonly RaycastHit[] _roofHits = new RaycastHit[16];

        /// <summary>
        /// The top of the roof under a point on a ridge line (the point at
        /// the ridge's worked-out height): the highest roof piece (a
        /// TL_Roof... variant on Environment) a ray down from 2m above it
        /// hits within 4m, else the worked-out height itself. The ridge cap
        /// stands a little above the worked-out height, and by less than the
        /// formula says, so the railings are seated on what is there.
        /// </summary>
        private static float RoofTopAt(Vector3 onRidge)
        {
            int hits = Physics.RaycastNonAlloc(onRidge + Vector3.up * 2f, Vector3.down, _roofHits, 4f, LayerMask.GetMask("Environment"),
                QueryTriggerInteraction.Ignore);
            float top = float.MinValue;

            for (int i = 0; i < hits; i++) {
                GameObject root = UnityEditor.PrefabUtility.GetOutermostPrefabInstanceRoot(_roofHits[i].collider.gameObject);

                if (root != null && root.name.StartsWith("TL_Roof") && _roofHits[i].point.y > top) {
                    top = _roofHits[i].point.y;
                }
            }

            return top > float.MinValue ? top : onRidge.y;
        }

        /// <summary>
        /// Where along a run a roof feature stands over a building (from to
        /// to along the run): at random within RoofFeatureSpread of the
        /// building's length about its middle, kept half the feature's width
        /// in from either end. False if the building is too short for it.
        /// </summary>
        private static bool FeatureAlong(float from, float to, float halfWidth, GeneratorSettings settings, System.Random random, out float along)
        {
            float length = to - from;
            along = from + length * (0.5f + ((float)random.NextDouble() - 0.5f) * settings.RoofFeatureSpread);
            along = Mathf.Clamp(along, from + halfWidth, to - halfWidth);
            return length >= 2f * halfWidth;
        }

        /// <summary>
        /// How far the tiles' surface stands above the plane the panels are
        /// laid on (the panel's thickness in front of its pivot).
        /// </summary>
        private static float TileTop(PiecePlacer placer)
        {
            return placer.Entry(Middles("roof-end")[0]).renderMax.z;
        }

        /// <summary>
        /// A chimney set into a stretch's front slope over a building,
        /// facing the street, as a dormer is but higher up: the bottom of
        /// its front (down-slope) face a distance drawn between RoofChimneyUp
        /// and RoofChimneyUpMax up the slope from the eave (so they sit at
        /// different heights on their roofs) and RoofChimneySink below the
        /// tiles' surface, its back
        /// buried in the slope, which rises by its depth across it. It is
        /// stretched up (never shrunk) so its top stands a rise drawn
        /// between RoofChimneyRise and RoofChimneyRiseMax above the tiles at
        /// its back face, else the slope would cut off its cap; the tallest
        /// stand clear above the ridge. Its back face stops short of the
        /// ridge: nothing if the slope is shorter than its depth (a lot's
        /// scaled-down slope).
        /// </summary>
        private static bool PlaceChimney(RoofStretch stretch, Building building, float from, float to, PieceAnnotation piece, GeneratorSettings settings,
            System.Random random, PiecePlacer placer)
        {
            CatalogueEntry entry = placer.Entry(piece);
            float halfWidth = Mathf.Max(-entry.renderMin.x, entry.renderMax.x);

            if (!FeatureAlong(from, to, halfWidth, settings, random, out float along)) {
                return false;
            }

            Run run = stretch.Run;
            float pitch = settings.RoofPitch * Mathf.Deg2Rad;
            float cos = Mathf.Cos(pitch);
            float sin = Mathf.Sin(pitch);
            float eaveHeight = stretch.Top - settings.RoofEaveDrop;
            float eaveLine = stretch.Face + settings.RoofEaveOverhang;
            float slopeLength = SlopeLength(stretch, settings);
            float depth = entry.renderMax.z - entry.renderMin.z;
            // How far up the slope it sits, drawn for this chimney between
            // RoofChimneyUp and RoofChimneyUpMax; its back face stays below
            // the ridge.
            float upMost = slopeLength - depth / cos;

            if (upMost < 0f) {
                return false;
            }

            float up = Mathf.Min(Mathf.Lerp(settings.RoofChimneyUp, Mathf.Max(settings.RoofChimneyUp, settings.RoofChimneyUpMax), (float)random.NextDouble()), upMost);

            Vector3 front = Level(run.Front);
            Vector3 normal = front * sin + Vector3.up * cos;
            Vector3 upSlope = -front * cos + Vector3.up * sin;
            Vector2 onEave = run.Start + run.Along * along + run.Front * eaveLine;
            Vector3 seat = new Vector3(onEave.x, eaveHeight, onEave.y) + upSlope * up + normal * TileTop(placer) - Vector3.up * settings.RoofChimneySink;
            // Tall enough to clear the tiles at its back, which stand the
            // slope's rise across its depth above those at its front, by
            // a rise drawn for this chimney between RoofChimneyRise and
            // RoofChimneyRiseMax, so the tallest stand clear above the ridge.
            float rise = Mathf.Lerp(settings.RoofChimneyRise, Mathf.Max(settings.RoofChimneyRise, settings.RoofChimneyRiseMax), (float)random.NextDouble());
            float height = entry.renderMax.y - entry.renderMin.y;
            float stretchUp = Mathf.Max(1f, (settings.RoofChimneySink + depth * Mathf.Tan(pitch) + rise) / height);
            Vector3 frontBottom = new((entry.renderMin.x + entry.renderMax.x) * 0.5f, entry.renderMin.y, entry.renderMax.z);
            GameObject chimney = placer.PlaceAt(piece, frontBottom, seat, Quaternion.LookRotation(front, Vector3.up), new Vector3(1f, stretchUp, 1f), RoofGroup(building));
            // Smoke from its top, at ChimneySmokeShare (a draw every time).
            bool smokes = random.NextDouble() < settings.ChimneySmokeShare;

            if (smokes && chimney != null && settings.ChimneySmokePrefab != null) {
                // The middle of the top of its iron chimney pot (the
                // TL_IronFixtures_Chimney inside the piece), as drawn and
                // stretched with it; the piece's own top if it has none.
                Transform pot = chimney.transform.Find("TL_IronFixtures_Chimney");
                Renderer potRenderer = pot != null ? pot.GetComponentInChildren<Renderer>() : null;
                Bounds box = (potRenderer != null ? potRenderer : chimney.GetComponentInChildren<Renderer>()).bounds;
                PlaceEffect(settings.ChimneySmokePrefab, chimney.transform, new Vector3(box.center.x, box.max.y, box.center.z), Vector3.up);
            }

            return true;
        }

        /// <summary>
        /// A dormer on a stretch's front slope over a building, facing the
        /// street: a box with a window in its front (its +Z) and a little
        /// roof falling to its back, whose front wall stands plumb on the
        /// slope - the bottom of that wall (DormerFront: how far forward of
        /// the pivot the piece's lowest vertices reach) RoofDormerUp up the
        /// slope from the eave and RoofDormerSink below the tiles' surface -
        /// and whose back is sunk into the slope behind it. False, nothing
        /// placed, if it would rise above the ridge or its back would pass
        /// it (a lot's scaled-down slope).
        /// </summary>
        private static bool PlaceDormer(RoofStretch stretch, Building building, float from, float to, PieceAnnotation piece, GeneratorSettings settings,
            System.Random random, PiecePlacer placer)
        {
            CatalogueEntry entry = placer.Entry(piece);
            float halfWidth = Mathf.Max(-entry.renderMin.x, entry.renderMax.x);

            if (!FeatureAlong(from, to, halfWidth, settings, random, out float along)) {
                return false;
            }

            Run run = stretch.Run;
            float pitch = settings.RoofPitch * Mathf.Deg2Rad;
            float cos = Mathf.Cos(pitch);
            float sin = Mathf.Sin(pitch);
            float eaveHeight = stretch.Top - settings.RoofEaveDrop;
            float eaveLine = stretch.Face + settings.RoofEaveOverhang;
            float slopeLength = SlopeLength(stretch, settings);
            float ridgeBack = slopeLength * cos;
            float ridgeHeight = eaveHeight + slopeLength * sin;
            float up = Mathf.Min(settings.RoofDormerUp, slopeLength);
            float frontDepth = DormerFront(piece, entry);
            Vector3 front = Level(run.Front);
            Vector3 normal = front * sin + Vector3.up * cos;
            Vector3 upSlope = -front * cos + Vector3.up * sin;
            Vector2 onEave = run.Start + run.Along * along + run.Front * eaveLine;
            Vector3 seat = new Vector3(onEave.x, eaveHeight, onEave.y) + upSlope * up + normal * TileTop(placer) - Vector3.up * settings.RoofDormerSink;

            if (seat.y + entry.renderMax.y - entry.renderMin.y > ridgeHeight || up * cos + frontDepth - entry.renderMin.z > ridgeBack) {
                return false;
            }

            Vector3 wallBottom = new((entry.renderMin.x + entry.renderMax.x) * 0.5f, entry.renderMin.y, frontDepth);
            placer.PlaceAt(piece, wallBottom, seat, Quaternion.LookRotation(front, Vector3.up), Vector3.one, RoofGroup(building));
            return true;
        }

        /// <summary>
        /// How far forward of a dormer piece's pivot the bottom of its front
        /// wall stands: the furthest +Z its rendered meshes' lowest vertices
        /// (within DormerBottom of the render box's bottom) reach, in the
        /// prefab root's space. Read from the prefab once per build; the
        /// render box's front where it can't be read.
        /// </summary>
        private static float DormerFront(PieceAnnotation piece, CatalogueEntry entry)
        {
            if (_dormerFronts.TryGetValue(piece.guid, out float front)) {
                return front;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(piece.guid));
            front = float.MinValue;

            if (prefab != null) {
                foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true)) {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();

                    if (filter == null || filter.sharedMesh == null) {
                        continue;
                    }

                    foreach (Vector3 vertex in filter.sharedMesh.vertices) {
                        Vector3 local = prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));

                        if (local.y <= entry.renderMin.y + DormerBottom) {
                            front = Mathf.Max(front, local.z);
                        }
                    }
                }
            }

            if (front == float.MinValue) {
                front = entry.renderMax.z;
            }

            _dormerFronts[piece.guid] = front;
            return front;
        }
    }
}
