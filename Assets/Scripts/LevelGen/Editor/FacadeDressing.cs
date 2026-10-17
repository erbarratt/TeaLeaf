using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The facades' dressing, placed after the walls and roofs (stage 7):
    /// pipework up and along the buildings, walkways along upper storeys, and
    /// lamp posts at the side of the streets. Everything is read off the laid
    /// modules (ModuleSpot: its wall piece per storey, its depth, its reach),
    /// so it follows every step, jetty and column as built.
    ///
    /// - Pipes (BuildPipes): every building gets at least one run of pipe,
    ///   and runs are added until together they are PipeMinRunShare of its
    ///   width (and further ones at PipeExtraRunShare, up to PipeRunsMax).
    ///   A run is vertical, horizontal or an L, made of straight sections and
    ///   elbows, always level or plumb, standing PipeWallGap off the wall's
    ///   face; it ends by turning into the wall (an elbow) or going into the
    ///   ground, never along the street. It is kept PipeOpeningMargin clear
    ///   of every door and window hole of the walls it passes, and off a wall
    ///   that shows a blind opening. A vertical run keeps to one column (so
    ///   it meets no inset or outset) and, in the small pipe, jogs out under
    ///   a jetty with two elbows (their legs together being the jetty's
    ///   depth); a horizontal run keeps to a stretch of modules at one depth
    ///   and height. Small or large pipe per run (PipeLargeShare).
    /// - Walkways (BuildWalkways): at WalkwayShare of the buildings with an
    ///   upper storey, one row of the jetty piece along the whole facade at
    ///   the base of one upper storey every column reaches, never a jettied
    ///   first storey's (which has the deck already).
    /// - Lamp posts (BuildLamps): along each street frontage, one every
    ///   LampSpacing metres on average, LampEdgeInset into the street from
    ///   its edge before a plain ground-storey wall (never a door or a
    ///   window), facing into the street. Its light is switched off like
    ///   every placed prefab's.
    /// </summary>
    internal static partial class FacadeBuilder
    {
        // The pipe the small corners (root scale 0.6) and the small
        // straights are, and the large; chosen per run by diameter.
        private const float SmallPipe = 0.67f;

        /// <summary>
        /// A pipe elbow as measured: its bend's middle in the piece's own
        /// (root, unscaled) space, the radius, and the two legs' lengths in
        /// world metres - A along the piece's -Z, B along its +X.
        /// </summary>
        private struct Elbow
        {
            public PieceAnnotation Piece;
            public Vector3 Node;
            public float Radius;
            public float LegA;
            public float LegB;
            public Vector3 RootScale;
        }

        /// <summary>
        /// One straight pipe piece: its diameter and length as placed at unit
        /// scale.
        /// </summary>
        private struct Straight
        {
            public PieceAnnotation Piece;
            public float Radius;
            public float Length;
        }

        /// <summary>
        /// The pieces of one pipe size: a straight and an elbow of (nearly)
        /// the same diameter.
        /// </summary>
        private struct PipeKit
        {
            public Straight Straight;
            public Elbow Elbow;
            public float Radius;
        }

        /// <summary>
        /// A rotation that turns a piece's +Z to worldZ and its +X to worldX
        /// (which must be at right angles).
        /// </summary>
        private static Quaternion Orient(Vector3 worldZ, Vector3 worldX)
        {
            return Quaternion.LookRotation(worldZ, Vector3.Cross(worldZ, worldX));
        }

        /// <summary>
        /// The pipe kits available: for each straight, the elbow nearest it
        /// in diameter (within a fifth). None where either is missing.
        /// </summary>
        private static List<PipeKit> PipeKits(PiecePlacer placer)
        {
            List<PipeKit> kits = new();

            if (!_dressing.TryGetValue("pipe-straight", out List<PieceAnnotation> straights) || !_dressing.TryGetValue("pipe-corner", out List<PieceAnnotation> corners)) {
                return kits;
            }

            foreach (PieceAnnotation straight in straights) {
                CatalogueEntry entry = placer.Entry(straight);
                float radius = (entry.renderMax.y - entry.renderMin.y) * 0.5f * entry.rootScale.y;
                float length = (entry.renderMax.z - entry.renderMin.z) * entry.rootScale.z;
                Elbow best = default;
                float nearest = float.MaxValue;

                foreach (PieceAnnotation corner in corners) {
                    CatalogueEntry c = placer.Entry(corner);
                    float r = (c.renderMax.y - c.renderMin.y) * 0.5f;
                    Vector3 node = new(c.renderMin.x + r, 0f, c.renderMax.z - r);
                    float worldRadius = r * c.rootScale.y;

                    if (Mathf.Abs(worldRadius - radius) < nearest) {
                        nearest = Mathf.Abs(worldRadius - radius);
                        best = new Elbow {
                            Piece = corner, Node = node, Radius = worldRadius, LegA = (node.z - c.renderMin.z) * c.rootScale.z,
                            LegB = (c.renderMax.x - node.x) * c.rootScale.x, RootScale = c.rootScale
                        };
                    }
                }

                if (best.Piece != null && nearest <= radius * 0.2f) {
                    kits.Add(new PipeKit { Straight = new Straight { Piece = straight, Radius = radius, Length = length }, Elbow = best, Radius = radius });
                }
            }

            return kits;
        }

        /// <summary>
        /// Places a straight pipe from one point to another (level or
        /// plumb), its pieces by the fitting rule along the length, each
        /// scaled along its own axis to its share; cross is a direction at
        /// right angles to the pipe its +X is turned to (which way the
        /// flanges' seams face).
        /// </summary>
        private static void PlacePipe(PipeKit kit, Vector3 from, Vector3 to, Vector3 cross, GeneratorSettings settings, PiecePlacer placer, Transform group)
        {
            Vector3 way = to - from;
            float length = way.magnitude;

            if (length <= settings.SizeTolerance) {
                return;
            }

            way /= length;
            // The piece runs along its -Z from its pivot: its +Z is turned
            // against the way.
            Quaternion rotation = Orient(-way, cross);
            float laid = 0f;

            foreach (float piece in FitWidths(length, kit.Straight.Length, 0f, settings)) {
                placer.PlaceAt(kit.Straight.Piece, Vector3.zero, from + way * laid, rotation, new Vector3(1f, 1f, piece / kit.Straight.Length), group);
                laid += piece;
            }
        }

        /// <summary>
        /// Places an elbow with its bend's middle at node, leg A pointing
        /// wayA and leg B wayB (at right angles).
        /// </summary>
        private static void PlaceElbow(Elbow elbow, Vector3 node, Vector3 wayA, Vector3 wayB, PiecePlacer placer, Transform group)
        {
            // Leg A is the piece's -Z, leg B its +X; the pivot is placed so
            // the node (in root space, scaled by the root) lands on node.
            placer.PlaceAt(elbow.Piece, Vector3.Scale(elbow.Node, elbow.RootScale), node, Orient(-wayA, wayB), Vector3.one, group);
        }

        /// <summary>
        /// The span along a module's run, from its start, that an opening of
        /// its wall piece covers (the wall placed by its left connection at
        /// the module's left end as seen from the street, scaled to the
        /// module), and the opening's height above the storey's base.
        /// </summary>
        private static bool OpeningSpan(ModuleSpot spot, float spacing, Run run, Opening opening, PieceAnnotation piece, PiecePlacer placer,
            GeneratorSettings settings, out float from, out float to, out float bottom, out float top)
        {
            from = to = bottom = top = 0f;
            CatalogueEntry entry = placer.Entry(piece);

            if (entry == null || !Connection(piece, "left", out Vector3 left)) {
                return false;
            }

            Vector3 front = piece.fronts[0];
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            float scale = (spacing + settings.WallOverlap) / piece.width;
            float a = Vector3.Dot(opening.min - left, right) * scale;
            float b = Vector3.Dot(opening.max - left, right) * scale;
            float nearLeft = Mathf.Min(a, b);
            float farLeft = Mathf.Max(a, b);
            // Whether the module's left end (seen from the street) is the
            // run's start side.
            Vector3 worldRight = Vector3.Cross(Vector3.up, -Level(run.Front));
            bool leftIsStart = Vector3.Dot(Level(run.Along), worldRight) > 0f;
            from = spot.Position + (leftIsStart ? nearLeft : spacing - farLeft);
            to = spot.Position + (leftIsStart ? farLeft : spacing - nearLeft);
            bottom = (opening.min.y - left.y) * 1f;
            top = (opening.max.y - left.y) * 1f;
            return true;
        }

        /// <summary>
        /// Whether a box on a wall (along the run from x0 to x1, from height
        /// y0 to y1 above the street) comes within PipeOpeningMargin of a
        /// door or window hole of the walls it passes, or lies across a wall
        /// that shows a blind opening (except along its very ends). Only the
        /// storeys from storey0 to storey1 are looked at.
        /// </summary>
        private static bool CrossesOpening(List<ModuleSpot> spots, int first, int last, Building building, List<Run> runs, float x0, float x1, float y0, float y1,
            int storey0, int storey1, PiecePlacer placer, GeneratorSettings settings)
        {
            float margin = settings.PipeOpeningMargin;

            for (int k = first; k <= last; k++) {
                ModuleSpot spot = spots[k];
                float spacing = Spacing(spot, building);

                if (spot.Position + spacing < x0 - margin || spot.Position > x1 + margin) {
                    continue;
                }

                for (int s = storey0; s <= storey1 && s < spot.Storeys; s++) {
                    PieceAnnotation piece = spot.Walls[s];
                    CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

                    if (entry == null) {
                        continue;
                    }

                    StoreyLevel level = building.Levels[s];

                    if (level.Base > y1 + margin || level.Base + level.Style.StoreySpacing < y0 - margin) {
                        continue;
                    }

                    // A blind opening fills the middle of its wall: only the
                    // module's ends, and the bands below and above the
                    // storey's middle half, are clear.
                    if (piece.showsOpening) {
                        float edge = 0.45f;
                        bool alongEnds = x1 <= spot.Position + edge || x0 >= spot.Position + spacing - edge;
                        bool inBands = y1 <= level.Base + level.Style.StoreySpacing * 0.25f || y0 >= level.Base + level.Style.StoreySpacing * 0.8f;

                        if (!alongEnds && !inBands) {
                            return true;
                        }
                    }

                    foreach (Opening opening in entry.openings) {
                        if (opening.kind != "door" && opening.kind != "window") {
                            continue;
                        }

                        if (!OpeningSpan(spot, spacing, runs[spot.Run], opening, piece, placer, settings, out float from, out float to, out float bottom, out float top)) {
                            continue;
                        }

                        float oy0 = level.Base + bottom;
                        float oy1 = level.Base + top;

                        if (x1 + margin > from && x0 - margin < to && y1 + margin > oy0 && y0 - margin < oy1) {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// How far forward of the street edge a module's wall face stands at
        /// a storey (its column's depth, the jetty above the ground, the back
        /// line's setback and the wall's own reach); false where no wall was
        /// placed there.
        /// </summary>
        private static bool FaceAt(ModuleSpot spot, Building building, int storey, float setback, out float face)
        {
            face = 0f;

            if (storey >= spot.Storeys || spot.Reach[storey] <= 0f) {
                return false;
            }

            face = spot.Depth + StoreyJetty(building, storey) - setback + spot.Reach[storey];
            return true;
        }

        /// <summary>
        /// Every building's pipes (see the class summary). Logs how many runs
        /// were placed and the buildings that got none.
        /// </summary>
        private static void BuildPipes(List<Frontage> frontages, GeneratorSettings settings, System.Random random, PiecePlacer placer, List<string> log)
        {
            List<PipeKit> kits = PipeKits(placer);

            if (kits.Count == 0) {
                log.Add("Pipes: no pipe-straight and pipe-corner pieces of a matching size are verified; no pipes placed.");
                return;
            }

            PipeKit small = kits[0];
            PipeKit large = kits[0];

            foreach (PipeKit kit in kits) {
                if (kit.Radius < small.Radius) {
                    small = kit;
                }

                if (kit.Radius > large.Radius) {
                    large = kit;
                }
            }

            int runsPlaced = 0;
            List<string> none = new();

            foreach (Frontage frontage in frontages) {
                for (int b = 0; b < frontage.Buildings.Count; b++) {
                    int first = frontage.Spots.FindIndex(spot => spot.Building == b);
                    int last = frontage.Spots.FindLastIndex(spot => spot.Building == b);

                    if (first < 0) {
                        continue;
                    }

                    int placed = PipeBuilding(frontage, b, first, last, small, large, settings, random, placer);
                    runsPlaced += placed;

                    if (placed == 0) {
                        none.Add($"{frontage.Label} building {b}");
                    }
                }
            }

            log.Add($"Pipes: {runsPlaced} run(s)" + (none.Count > 0 ? $"; none fitted on {string.Join(", ", none)}." : "."));
        }

        /// <summary>
        /// One building's runs of pipe: tried at random places and shapes
        /// until the runs together reach PipeMinRunShare of the building's
        /// width and the count is made up (one, then PipeExtraRunShare each
        /// up to PipeRunsMax), or the tries run out. Returns how many were
        /// placed; the rolls are always made, so the random sequence doesn't
        /// depend on what fits.
        /// </summary>
        private static int PipeBuilding(Frontage frontage, int b, int first, int last, PipeKit small, PipeKit large, GeneratorSettings settings,
            System.Random random, PiecePlacer placer)
        {
            Building building = frontage.Buildings[b];
            List<ModuleSpot> spots = frontage.Spots;
            float width = 0f;

            for (int k = first; k <= last; k++) {
                width += Spacing(spots[k], building);
            }

            int wanted = 1;

            for (int i = 1; i < settings.PipeRunsMax; i++) {
                if (random.NextDouble() < settings.PipeExtraRunShare) {
                    wanted++;
                }
            }

            float needed = width * settings.PipeMinRunShare;
            float total = 0f;
            int placed = 0;
            Transform group = null;
            // The runs placed so far, as spans along the run and heights, so
            // two never cross.
            List<(float x0, float x1, float y0, float y1)> taken = new();

            for (int attempt = 0; attempt < 12 && (placed < wanted || total < needed); attempt++) {
                PipeKit kit = random.NextDouble() < settings.PipeLargeShare ? large : small;
                int shape = random.Next(3);
                int spot = first + random.Next(last - first + 1);
                float where = (float)random.NextDouble();
                int storey = random.Next(Mathf.Max(1, building.Levels.Count));
                group ??= Group(building.Group, "Pipes");
                float length = shape switch {
                    0 => VerticalRun(frontage, building, first, last, spot, kit, taken, settings, placer, group),
                    1 => HorizontalRun(frontage, building, first, last, spot, storey, where, kit, taken, settings, placer, group),
                    _ => CornerRun(frontage, building, first, last, spot, storey, where, kit, taken, settings, placer, group)
                };

                if (length > 0f) {
                    placed++;
                    total += length;
                }
            }

            if (group != null && group.childCount == 0) {
                Object.DestroyImmediate(group.gameObject);
            }

            return placed;
        }

        /// <summary>
        /// A plumb run of pipe at the start edge of a module (where two
        /// modules meet), from the top of its column down into the ground:
        /// an elbow turning into the wall at the top, straight sections down
        /// the storeys' faces, and under a jetty (small pipe only) two elbows
        /// jogging the pipe back to the ground storey's face. Nothing where it
        /// would cross a door or window, pass another run, or (large pipe)
        /// cross a jetty. Returns the length placed, 0 for none.
        /// </summary>
        private static float VerticalRun(Frontage frontage, Building building, int first, int last, int at, PipeKit kit,
            List<(float x0, float x1, float y0, float y1)> taken, GeneratorSettings settings, PiecePlacer placer, Transform group)
        {
            List<ModuleSpot> spots = frontage.Spots;
            ModuleSpot spot = spots[at];
            Run run = frontage.Runs[spot.Run];
            float setback = frontage.WallSetback;
            float offset = settings.PipeWallGap + kit.Radius;
            // On the module's start edge, in from it by the pipe, so both
            // the module and the one before are checked.
            float x = spot.Position + kit.Radius + 0.05f;
            int storeys = spot.Storeys;

            if (storeys == 0 || !FaceAt(spot, building, 0, setback, out float groundFace)) {
                return 0f;
            }

            bool jettied = building.Jetty > 0f && storeys > 1;

            if (jettied && kit.Radius > SmallPipe) {
                return 0f;
            }

            float ground = Height(run, x);
            float top = ground + building.Levels[storeys - 1].Base + building.Levels[storeys - 1].Style.StoreySpacing - kit.Elbow.LegA - kit.Radius;
            float x0 = x - kit.Radius;
            float x1 = x + kit.Radius;

            if (CrossesOpening(spots, Mathf.Max(first, at - 1), at, building, frontage.Runs, x0, x1, ground, top, 0, storeys - 1, placer, settings)
                || Taken(taken, x0, x1, ground, top) || !ClearOfLane(frontage, run, x0, x1, groundFace + offset + kit.Radius, 0, settings)) {
                return 0f;
            }

            Vector2 along = run.Along;
            Vector3 front = Level(run.Front);
            Vector3 alongWorld = Level(along);
            Vector3 Point(float d, float y) => new(run.Start.x + along.x * x + run.Front.x * d, y, run.Start.y + along.y * x + run.Front.y * d);
            float upperFace = groundFace;

            if (jettied) {
                FaceAt(spot, building, 1, setback, out upperFace);
            }

            // The top elbow: its leg A down the pipe, leg B into the wall.
            float topFace = jettied ? upperFace : groundFace;
            Vector3 topNode = Point(topFace + offset, top);
            PlaceElbow(kit.Elbow, topNode, Vector3.down, -front, placer, group);
            float length = kit.Elbow.LegA + kit.Elbow.LegB;

            if (!jettied) {
                Vector3 from = topNode + Vector3.down * kit.Elbow.LegA;
                Vector3 to = Point(groundFace + offset, ground - 0.3f);
                PlacePipe(kit, from, to, alongWorld, settings, placer, group);
                length += Vector3.Distance(from, to);
                taken.Add((x0, x1, ground, top));
                return length;
            }

            // Under the jetty: down the upper face to the deck, an elbow
            // turning back towards the wall, one turning down again at the
            // ground face, then down into the ground. The two legs between
            // them span the jetty (the small pipe's legs are made for it).
            float deck = ground + building.Levels[1].Base;
            float jog = upperFace - groundFace;
            float outerY = deck - kit.Radius - kit.Elbow.LegA * 0.5f;
            Vector3 upperFrom = topNode + Vector3.down * kit.Elbow.LegA;
            Vector3 outerNode = Point(upperFace + offset, outerY);
            PlacePipe(kit, upperFrom, outerNode + Vector3.up * kit.Elbow.LegA, alongWorld, settings, placer, group);
            PlaceElbow(kit.Elbow, outerNode, Vector3.up, -front, placer, group);
            Vector3 innerNode = Point(groundFace + offset, outerY - (kit.Elbow.LegB + kit.Elbow.LegA - jog));
            // Where the two legs don't meet the jog exactly, a short straight
            // between them along the way in.
            Vector3 legEnd = outerNode - front * kit.Elbow.LegB;
            Vector3 innerLegEnd = innerNode + Vector3.up * kit.Elbow.LegA;
            innerNode = Point(groundFace + offset, outerY);
            innerLegEnd = innerNode + Vector3.up * 0f;
            // The inner elbow stands at the ground face's offset, level with
            // the outer one: its leg A points back out towards the outer
            // elbow's leg, its leg B down.
            PlaceElbow(kit.Elbow, innerNode, front, Vector3.down, placer, group);
            Vector3 innerLeg = innerNode + front * kit.Elbow.LegA;

            if (Vector3.Distance(legEnd, innerLeg) > settings.SizeTolerance) {
                PlacePipe(kit, legEnd, innerLeg, Vector3.up, settings, placer, group);
            }

            Vector3 down = innerNode + Vector3.down * kit.Elbow.LegB;
            Vector3 bottom = Point(groundFace + offset, ground - 0.3f);
            PlacePipe(kit, down, bottom, alongWorld, settings, placer, group);
            length += Vector3.Distance(upperFrom, outerNode) + kit.Elbow.LegA * 2f + kit.Elbow.LegB * 2f + Vector3.Distance(down, bottom);
            taken.Add((x0, x1, ground, top));
            return length;
        }

        /// <summary>
        /// A level run of pipe along one storey, from a module's start edge
        /// on along as many modules of the same column depth and at least
        /// that storey as it reaches (at least a module), turning into the
        /// wall at both ends, at a height clear of the storey's openings
        /// (below their sills, else above them). Returns the length placed.
        /// </summary>
        private static float HorizontalRun(Frontage frontage, Building building, int first, int last, int at, int storey, float where, PipeKit kit,
            List<(float x0, float x1, float y0, float y1)> taken, GeneratorSettings settings, PiecePlacer placer, Transform group)
        {
            List<ModuleSpot> spots = frontage.Spots;
            float setback = frontage.WallSetback;

            if (storey >= spots[at].Storeys || !FaceAt(spots[at], building, storey, setback, out float face)) {
                return 0f;
            }

            // The stretch: the module and those after it on the same run, at
            // the same depth and height, up to where the building ends.
            int end = at;

            while (end + 1 <= last && spots[end + 1].Run == spots[at].Run && spots[end + 1].Storeys > storey
                && Mathf.Abs(spots[end + 1].Depth - spots[at].Depth) < SameDepth && FaceAt(spots[end + 1], building, storey, setback, out float f)
                && Mathf.Abs(f - face) < SameDepth) {
                end++;
            }

            Run run = frontage.Runs[spots[at].Run];
            float x0 = spots[at].Position + kit.Elbow.Radius + 0.05f;
            float x1 = spots[end].Position + Spacing(spots[end], building) - kit.Elbow.Radius - 0.05f;
            // Shorter than the whole stretch, sometimes: from the start to a
            // module's end chosen by where.
            int stop = at + Mathf.RoundToInt(where * (end - at));
            x1 = Mathf.Min(x1, spots[stop].Position + Spacing(spots[stop], building) - kit.Elbow.Radius - 0.05f);

            if (x1 - x0 < kit.Elbow.LegA * 2f + settings.SizeTolerance) {
                return 0f;
            }

            StoreyLevel level = building.Levels[storey];
            float ground = Height(run, (x0 + x1) * 0.5f);
            float[] heights = { level.Base + 0.45f + kit.Radius, level.Base + level.Style.StoreySpacing - 0.45f - kit.Radius };

            foreach (float h in heights) {
                float y = ground + h;

                if (CrossesOpening(spots, at, end, building, frontage.Runs, x0, x1, y - kit.Radius, y + kit.Radius, storey, storey, placer, settings)
                    || Taken(taken, x0, x1, y - kit.Radius, y + kit.Radius)
                    || !ClearOfLane(frontage, run, x0, x1, face + settings.PipeWallGap + kit.Radius * 2f, storey, settings)) {
                    continue;
                }

                Vector3 front = Level(run.Front);
                Vector3 alongWorld = Level(run.Along);
                Vector3 Point(float x) => new(run.Start.x + run.Along.x * x + run.Front.x * (face + settings.PipeWallGap + kit.Radius), y,
                    run.Start.y + run.Along.y * x + run.Front.y * (face + settings.PipeWallGap + kit.Radius));
                Vector3 a = Point(x0);
                Vector3 bEnd = Point(x1);
                // Both elbows: leg A along the pipe, leg B into the wall.
                PlaceElbow(kit.Elbow, a, alongWorld, -front, placer, group);
                PlaceElbow(kit.Elbow, bEnd, -alongWorld, -front, placer, group);
                PlacePipe(kit, a + alongWorld * kit.Elbow.LegA, bEnd - alongWorld * kit.Elbow.LegA, Vector3.up, settings, placer, group);
                taken.Add((x0, x1, y - kit.Radius, y + kit.Radius));
                return x1 - x0 + kit.Elbow.LegB * 2f;
            }

            return 0f;
        }

        /// <summary>
        /// An L of pipe: up from the ground at a module's start edge to a
        /// height on a storey, an elbow, then level along the storey to an
        /// elbow into the wall. Where the vertical would cross a jetty it is
        /// stopped at the ground storey instead. Returns the length placed.
        /// </summary>
        private static float CornerRun(Frontage frontage, Building building, int first, int last, int at, int storey, float where, PipeKit kit,
            List<(float x0, float x1, float y0, float y1)> taken, GeneratorSettings settings, PiecePlacer placer, Transform group)
        {
            List<ModuleSpot> spots = frontage.Spots;
            float setback = frontage.WallSetback;
            ModuleSpot spot = spots[at];

            // The vertical keeps to the storeys at the ground face: below a
            // jetty, the ground storey only.
            if (building.Jetty > 0f) {
                storey = 0;
            }

            if (storey >= spot.Storeys || !FaceAt(spot, building, storey, setback, out float face) || !FaceAt(spot, building, 0, setback, out float groundFace)
                || Mathf.Abs(face - groundFace) > SameDepth) {
                return 0f;
            }

            int end = at;

            while (end + 1 <= last && spots[end + 1].Run == spot.Run && spots[end + 1].Storeys > storey
                && Mathf.Abs(spots[end + 1].Depth - spot.Depth) < SameDepth && FaceAt(spots[end + 1], building, storey, setback, out float f)
                && Mathf.Abs(f - face) < SameDepth) {
                end++;
            }

            Run run = frontage.Runs[spot.Run];
            float x = spot.Position + kit.Radius + 0.05f;
            int stop = at + Mathf.Max(1, Mathf.RoundToInt(where * (end - at)));

            if (stop > end) {
                return 0f;
            }

            float x1 = spots[stop].Position + Spacing(spots[stop], building) - kit.Elbow.Radius - 0.05f;
            StoreyLevel level = building.Levels[storey];
            float ground = Height(run, x);
            float y = ground + level.Base + level.Style.StoreySpacing - 0.45f - kit.Radius;

            if (x1 - x < kit.Elbow.LegA + kit.Elbow.LegB + settings.SizeTolerance
                || CrossesOpening(spots, Mathf.Max(first, at - 1), at, building, frontage.Runs, x - kit.Radius, x + kit.Radius, ground, y, 0, storey, placer, settings)
                || CrossesOpening(spots, at, end, building, frontage.Runs, x, x1, y - kit.Radius, y + kit.Radius, storey, storey, placer, settings)
                || Taken(taken, x - kit.Radius, x + kit.Radius, ground, y) || Taken(taken, x, x1, y - kit.Radius, y + kit.Radius)
                || !ClearOfLane(frontage, run, x - kit.Radius, x1, face + settings.PipeWallGap + kit.Radius * 2f, 0, settings)) {
                return 0f;
            }

            Vector3 front = Level(run.Front);
            Vector3 alongWorld = Level(run.Along);
            float offset = face + settings.PipeWallGap + kit.Radius;
            Vector3 Point(float px, float py) => new(run.Start.x + run.Along.x * px + run.Front.x * offset, py, run.Start.y + run.Along.y * px + run.Front.y * offset);
            Vector3 cornerNode = Point(x, y);
            Vector3 endNode = Point(x1, y);
            // The corner elbow: leg A down the vertical, leg B along; the end
            // elbow: leg A back along the pipe, leg B into the wall.
            PlaceElbow(kit.Elbow, cornerNode, Vector3.down, alongWorld, placer, group);
            PlaceElbow(kit.Elbow, endNode, -alongWorld, -front, placer, group);
            PlacePipe(kit, cornerNode + Vector3.down * kit.Elbow.LegA, Point(x, ground - 0.3f), alongWorld, settings, placer, group);
            PlacePipe(kit, cornerNode + alongWorld * kit.Elbow.LegB, endNode - alongWorld * kit.Elbow.LegA, Vector3.up, settings, placer, group);
            taken.Add((x - kit.Radius, x + kit.Radius, ground, y));
            taken.Add((x, x1, y - kit.Radius, y + kit.Radius));
            return y - ground + x1 - x;
        }

        /// <summary>
        /// Whether a run of pipe may stand along a run from x0 to x1 at a
        /// storey, as far as the street goes: on the ground storey it must
        /// stay out of the street test's lane (the ball's width about the
        /// middle line) - reach is how far forward of the edge it comes;
        /// across a dead end, where the lane ends against the facade, no
        /// pipe may stand in the lane at any height.
        /// </summary>
        private static bool ClearOfLane(Frontage frontage, Run run, float x0, float x1, float reach, int storey, GeneratorSettings settings)
        {
            float lane = settings.StreetBallRadius + 0.1f;

            if (frontage.Street == null && frontage.Lot == null) {
                // A dead end's run is centred on the street's middle line.
                float middle = run.Length * 0.5f;
                return x1 < middle - lane || x0 > middle + lane;
            }

            if (storey > 0 || run.Street == null) {
                return true;
            }

            return reach <= run.Street.SegmentWidth(run.Segment) * 0.5f - lane;
        }

        /// <summary>
        /// Whether a box along a run (x0-x1, heights y0-y1) overlaps one
        /// already taken by a run of pipe, with a pipe's width to spare.
        /// </summary>
        private static bool Taken(List<(float x0, float x1, float y0, float y1)> taken, float x0, float x1, float y0, float y1)
        {
            foreach ((float tx0, float tx1, float ty0, float ty1) in taken) {
                if (x1 + 0.7f > tx0 && x0 - 0.7f < tx1 && y1 + 0.7f > ty0 && y0 - 0.7f < ty1) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Walkways (see the class summary): under "Building <i>/Walkway".
        /// Logs how many.
        /// </summary>
        private static void BuildWalkways(List<Frontage> frontages, GeneratorSettings settings, System.Random random, PiecePlacer placer, List<string> log)
        {
            int walkways = 0;

            foreach (Frontage frontage in frontages) {
                for (int b = 0; b < frontage.Buildings.Count; b++) {
                    Building building = frontage.Buildings[b];
                    List<ModuleSpot> spots = frontage.Spots.FindAll(spot => spot.Building == b);
                    // Always rolled, so the sequence doesn't depend on fit.
                    bool wanted = random.NextDouble() < settings.WalkwayShare;
                    int lowest = int.MaxValue;

                    foreach (ModuleSpot spot in spots) {
                        lowest = Mathf.Min(lowest, spot.Storeys);
                    }

                    // The storeys every column reaches, above the ground, not
                    // a jettied first storey (its deck is there already).
                    List<int> choices = new();

                    for (int s = 1; s < lowest && s < building.Levels.Count; s++) {
                        if (s != 1 || building.Jetty <= 0f) {
                            choices.Add(s);
                        }
                    }

                    int storey = choices.Count > 0 ? choices[random.Next(choices.Count)] : -1;

                    if (!wanted || storey < 0 || _jetties.Count == 0) {
                        continue;
                    }

                    Transform group = Group(building.Group, "Walkway");
                    PieceAnnotation previous = null;
                    StoreyLevel level = building.Levels[storey];
                    // Its deck's top at the storey's base, or under the beam
                    // band if the storey stands on one.
                    float deckTop = level.BeamHeight > 0f ? level.BeamBottom : level.Base;

                    foreach (ModuleSpot spot in spots) {
                        if (!FaceAt(spot, building, storey - 1, frontage.WallSetback, out float face)) {
                            continue;
                        }

                        PieceAnnotation piece = PiecePlacer.Pick(_jetties, previous, random);
                        CatalogueEntry entry = placer.Entry(piece);
                        previous = piece;
                        Vector3 pieceFront = piece.fronts[0];
                        Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
                        Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
                        Vector3 local = pieceRight * Vector3.Dot(centre, pieceRight) - pieceFront * Extent(entry, -pieceFront) + Vector3.up * entry.renderMax.y;
                        float spacing = Spacing(spot, building);
                        Run run = frontage.Runs[spot.Run];
                        float middle = spot.Position + spacing * 0.5f;
                        Vector2 onFace = run.Start + run.Along * middle + run.Front * face;
                        placer.PlaceAt(piece, local, new Vector3(onFace.x, Height(run, middle) + deckTop, onFace.y), Yaw(Level(run.Front)) - Yaw(pieceFront),
                            PieceScale(piece, (spacing + settings.WallOverlap) / piece.width, 1f), group);
                    }

                    walkways++;
                }
            }

            log.Add($"Walkways: {walkways}.");
        }

        /// <summary>
        /// Lamp posts (see the class summary): under "Lamps/<frontage>".
        /// Logs how many.
        /// </summary>
        private static void BuildLamps(List<Frontage> frontages, Transform parent, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            List<string> log)
        {
            if (!_dressing.TryGetValue("lamp-post", out List<PieceAnnotation> posts) || settings.LampSpacing <= 0f) {
                log.Add("Lamp posts: none (no verified lamp-post piece, or lampSpacing is 0).");
                return;
            }

            int lamps = 0;

            foreach (Frontage frontage in frontages) {
                if (frontage.Street == null || frontage.Lot != null) {
                    continue;
                }

                // The modules a post may stand before: ground-storey walls
                // with no door or window hole and no blind opening.
                List<int> candidates = new();
                float length = 0f;

                for (int k = 0; k < frontage.Spots.Count; k++) {
                    ModuleSpot spot = frontage.Spots[k];
                    Building building = frontage.Buildings[spot.Building];
                    length += Spacing(spot, building);
                    PieceAnnotation piece = spot.Storeys > 0 ? spot.Walls[0] : null;
                    CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

                    if (entry == null || piece.showsOpening || spot.Overrun) {
                        continue;
                    }

                    bool opening = false;

                    foreach (Opening o in entry.openings) {
                        opening |= o.kind == "door" || o.kind == "window";
                    }

                    if (!opening) {
                        candidates.Add(k);
                    }
                }

                int wanted = Mathf.RoundToInt(length / settings.LampSpacing + (float)random.NextDouble() - 0.5f);
                Transform group = null;
                List<Vector2> placed = new();

                for (int i = 0; i < wanted && candidates.Count > 0; i++) {
                    int pick = candidates[random.Next(candidates.Count)];
                    ModuleSpot spot = frontage.Spots[pick];
                    Building building = frontage.Buildings[spot.Building];
                    Run run = frontage.Runs[spot.Run];
                    float middle = spot.Position + Spacing(spot, building) * 0.5f;
                    Vector2 at = run.Start + run.Along * middle + run.Front * settings.LampEdgeInset;
                    bool near = false;

                    foreach (Vector2 other in placed) {
                        near |= Vector2.Distance(other, at) < settings.LampSpacing * 0.5f;
                    }

                    candidates.Remove(pick);

                    if (near) {
                        i--;
                        continue;
                    }

                    PieceAnnotation post = PiecePlacer.Pick(posts, null, random);
                    group ??= Group(parent, frontage.Label);
                    placer.PlaceAt(post, Vector3.zero, new Vector3(at.x, Height(run, middle), at.y), Yaw(Level(run.Front)) - Yaw(post.fronts[0]), group);
                    placed.Add(at);
                    lamps++;
                }
            }

            log.Add($"Lamp posts: {lamps}.");
        }
    }
}
