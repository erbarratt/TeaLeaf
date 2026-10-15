using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Stage 5a: builds every frontage's facades - foundations, then each
    /// storey's wall modules - along its street's edge.
    ///
    /// A frontage is laid as straight runs, one per street segment along its
    /// edge (the facade breaks at every bend). On the inside of a bend a run
    /// stops where the two edges meet, so the walls don't run into each
    /// other; on the outside the runs leave a gap, and where a street
    /// branches off at a frontage's end the run stops at the branch's edge.
    /// The buildings are laid in order from the frontage's first point,
    /// module after module; a module that would pass the end of its run
    /// starts the next run instead.
    ///
    /// Within a frontage every wall lines up by its flat back plane, set back
    /// from the street edge just far enough that the deepest trim of any wall
    /// it could use reaches the edge. Each module has a foundation centred on
    /// it, its top GeneratorSettings.FoundationHeight above the street and
    /// its front FoundationFrontOffset out past the walls' front line; the
    /// ground storey's base stands GroundStoreyHeight above the street, set
    /// apart from the foundations. A storey's modules are its style's
    /// module-sized walls: a door wall at the spec's door module or at
    /// GroundDoorShare (ground storey), else a window wall at
    /// GroundWindowShare or UpperWindowShare, else a plain wall, each chosen
    /// at random but never the same piece as the module before it. Every
    /// storey's modules are spaced by the ground storey's module, so the
    /// storeys stand in line.
    ///
    /// Columns: every building is cut end to end into columns of a few
    /// modules, each with its own depth and storey count. On a straight run
    /// of at least ColumnMinRunModules modules, some columns (never one
    /// holding the run's first or last module) stand forward of or back from
    /// the line for their full height, foundations with them; some stand a
    /// storey or more taller or lower than their building. Where two columns
    /// meet, side walls (plain walls of any width showing no window or door,
    /// turned side-on and running back) fill the step from the forward side,
    /// a module's length per storey and a foundation - in the recess,
    /// piercing the inset column's front walls, when the other column is
    /// inset - and each storey of a column above the other's top gets
    /// SideWallMultiplier times the column's width of them, so it seems to
    /// go on behind (FillSides).
    ///
    /// Jetties: at JettyShare a building's upper storeys all stand forward by
    /// the depth of its jetty pieces, the first on a row of jetty supports
    /// (BuildJetty) in place of a beam band; side walls treat the jettied
    /// storeys as steps.
    ///
    /// A placed wall's window holes get window inserts that fit them
    /// (PlaceInserts).
    ///
    /// A street end that meets no other street is closed by a facade across
    /// it (StoreyPlan.DeadEnds), built the same way along one run.
    ///
    /// Corners (stage 5b): every frontage is laid out before any is built.
    /// On the inside of a bend the last module may overrun, hidden behind the
    /// next run. On the outside of a bend, and where two frontages of
    /// different streets meet at a street corner, each side is carried
    /// storey by storey to where the lines meet (a scaled plain wall) and the
    /// joint is covered by an outer corner piece (near a right angle) or a
    /// column. A frontage end at a junction that meets no other frontage
    /// turns a one-module side return into the branching street. The length
    /// left at a frontage's end extends its last building.
    /// </summary>
    internal static class FacadeBuilder
    {
        // Two depths closer than this are one: no step between them.
        private const float SameDepth = 0.01f;
        // How far behind a front wall's face a side wall in a recess starts,
        // so the two faces never lie in one plane and flicker.
        private const float FaceClearance = 0.01f;

        // The usable window inserts (verified, not excluded, catalogued, of
        // the window category), set for the length of one Build call (editor
        // code, one call at a time).
        private static readonly List<PieceAnnotation> _inserts = new();
        // The usable jetty supports (any style), set the same way.
        private static readonly List<PieceAnnotation> _jetties = new();

        /// <summary>
        /// One straight stretch of a frontage's street edge.
        /// </summary>
        private struct Run
        {
            // Where the run starts on the edge, the way along it, and its length.
            public Vector2 Start;
            public Vector2 Along;
            public float Length;
            // The way the facade faces: into the street.
            public Vector2 Front;
            // The street's height at the run's start and end.
            public float StartHeight;
            public float EndHeight;
        }

        /// <summary>
        /// Where one module stands: its run, its distance along it, how far
        /// it stands forward of the line (back, below 0), and which building
        /// of the frontage it belongs to.
        /// </summary>
        private struct ModuleSpot
        {
            public int Run;
            public float Position;
            public float Depth;
            public int Building;
            // The column it is in (of the frontage) and how many storeys
            // that column stands.
            public int Column;
            public int Storeys;
            // Per storey, how far the front wall placed there stands out in
            // front of the back line (0 where none was placed). Shared by
            // copies of the spot, so the storeys' build fills it in.
            public float[] Reach;
            // Its index among its building's spec modules, or -1 for a module
            // the generator added (filling a frontage's end, an inside bend's
            // overrun, a side return).
            public int ModuleIndex;
            // Whether a corner joint closes its run's start or end here, so
            // nothing else needs to.
            public bool JoinedStart;
            public bool JoinedEnd;
        }

        /// <summary>
        /// A frontage laid out but not yet built: its runs, modules,
        /// buildings and columns, and whether a street branches off at its
        /// first or last point (on its side).
        /// </summary>
        private class Frontage
        {
            public string Label;
            public StreetSpec Street;
            public FrontageSpec Spec;
            public List<Run> Runs;
            public List<ModuleSpot> Spots;
            public List<Building> Buildings;
            public List<Column> Columns;
            public float WallSetback;
            public bool LeftToRight;
            public bool BranchAtStart;
            public bool BranchAtEnd;
        }

        /// <summary>
        /// One end of a run of modules: the frontage, the module at that end
        /// and whether it is the run's start (else its end).
        /// </summary>
        private struct RunEnd
        {
            public Frontage Frontage;
            public int Spot;
            public bool AtStart;
        }

        /// <summary>
        /// A corner where two runs' ends meet on the outside: a bend in one
        /// frontage, or two frontages at a street corner.
        /// </summary>
        private struct Joint
        {
            public RunEnd A;
            public RunEnd B;
        }

        /// <summary>
        /// A column: modules First to Last of the frontage (in one building and
        /// one run), standing at one depth (its storey count is on its
        /// modules).
        /// </summary>
        private struct Column
        {
            public int First;
            public int Last;
            public float Depth;
        }

        /// <summary>
        /// One storey of a building: its style and the height of its base
        /// above the street, and the beam band below it (BeamHeight 0 for
        /// none): the beam's bottom above the street and its height.
        /// </summary>
        private struct StoreyLevel
        {
            public BuildingStyle Style;
            public float Base;
            public float BeamBottom;
            public float BeamHeight;
        }

        /// <summary>
        /// A building being built: its spec, ground style, storey count, its
        /// storeys (as many as its tallest column may stand) and group, and
        /// how far its upper storeys jut out (0 for none) on which jetty
        /// pieces.
        /// </summary>
        private class Building
        {
            public BuildingSpec Spec;
            public BuildingStyle Ground;
            public int Storeys;
            public List<StoreyLevel> Levels;
            public Transform Group;
            public float Jetty;
            public List<PieceAnnotation> Jetties;
        }

        /// <summary>
        /// Builds every frontage in the spec, in the spec's order, each under
        /// a group of its own in parent, then every dead end. Adds a line per
        /// frontage and dead end to log.
        /// </summary>
        internal static void Build(LayoutSpec spec, Dictionary<string, BuildingStyle> styles, PieceAnnotations annotations, StoreyPlan plan,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform parent, List<string> log)
        {
            _inserts.Clear();
            _jetties.Clear();

            foreach (PieceAnnotation piece in annotations.pieces) {
                if (!piece.verified || piece.excluded || piece.fronts.Count != 1 || placer.Entry(piece) == null) {
                    continue;
                }

                if (piece.category == "window") {
                    _inserts.Add(piece);
                } else if (piece.category == "jetty") {
                    _jetties.Add(piece);
                }
            }

            // Every frontage is laid out first, so corners can join frontages
            // of different streets, then the corners found, then all built.
            List<Frontage> frontages = new();

            foreach (StreetSpec street in spec.streets) {
                foreach (FrontageSpec frontage in street.frontages) {
                    Frontage laid = LayFrontage(street, frontage, spec.streets, styles, plan, settings, random, placer, parent, log);

                    if (laid != null) {
                        frontages.Add(laid);
                    }
                }
            }

            List<Joint> joints = FindJoints(frontages, spec.streets, settings, log);

            foreach (Frontage frontage in frontages) {
                BuildFrontage(frontage, settings, random, placer, log);
            }

            HashSet<string> missing = new();

            foreach (Joint joint in joints) {
                BuildJoint(joint, settings, random, placer, missing);
            }

            foreach (string message in missing) {
                log.Add(message);
            }

            foreach (StoreyPlan.DeadEnd deadEnd in plan.DeadEnds) {
                BuildDeadEnd(deadEnd, styles, plan, settings, random, placer, parent, log);
            }
        }

        /// <summary>
        /// Lays one frontage out: every building's modules along its runs
        /// (with an overrun module where a run ends on the inside of a bend),
        /// the length left at its end, and its columns. Null if it can't be
        /// built (logged).
        /// </summary>
        private static Frontage LayFrontage(StreetSpec street, FrontageSpec frontage, List<StreetSpec> streets, Dictionary<string, BuildingStyle> styles,
            StoreyPlan plan, GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform parent, List<string> log)
        {
            string label = $"Street '{street.id}', {frontage.side} frontage {frontage.from}-{frontage.to}";
            float sideSign = frontage.side == "left" ? 1f : frontage.side == "right" ? -1f : 0f;

            if (sideSign == 0f || frontage.from < 0 || frontage.to >= street.points.Count || frontage.from >= frontage.to) {
                log.Add($"{label} not built: it isn't a stretch of the street on a side.");
                return null;
            }

            List<Run> runs = Runs(street, frontage, sideSign, streets, settings);

            if (runs.Count == 0) {
                log.Add($"{label} not built: no stretch of its edge is long enough.");
                return null;
            }

            // Seen from the street, a left frontage's buildings run left to
            // right along the street, a right frontage's right to left.
            bool leftToRight = sideSign > 0f;
            float wallSetback = Setback(frontage.buildings, styles, placer);
            Transform frontageGroup = Group(parent, $"{street.id} {frontage.side} {frontage.from}-{frontage.to}");
            List<ModuleSpot> spots = new();
            List<Building> buildings = new();
            int runIndex = 0;
            float position = 0f;

            // Where every module stands, in order along the street.
            for (int b = 0; b < frontage.buildings.Count; b++) {
                BuildingSpec spec = frontage.buildings[b];
                int storeys = plan.Storeys(spec);
                BuildingStyle ground = Find(styles, spec.storeys.Count > 0 ? spec.storeys[0] : null);

                if (storeys == 0 || ground == null || ground.ModuleSpacing <= 0f) {
                    log.Add($"{label}: building {b} and the rest not built: its ground storey's style has no module yet.");
                    break;
                }

                // Storeys for its tallest possible column; a style without a
                // module may stop the list short of even its own count.
                int most = Mathf.Max(storeys, Mathf.Min(storeys + settings.ColumnStoreyVariation, settings.MaxStoreys));
                // Its upper storeys jut out at JettyShare (always drawn), where
                // it may have an upper storey and a jetty piece fits its module.
                bool jettyDrawn = random.NextDouble() < settings.JettyShare;
                List<PieceAnnotation> jetties = _jetties.FindAll(piece => ground.Fits(piece.width, ground.ModuleWidth));
                bool jettied = jettyDrawn && most > 1 && jetties.Count > 0;
                List<StoreyLevel> levels = StoreyLevels(spec, most, styles, settings, jettied, $"{label}, building {b}", log);

                for (int k = 0; k < spec.widthModules; k++) {
                    if (position + ground.ModuleSpacing > runs[runIndex].Length + settings.SizeTolerance && runIndex < runs.Count - 1) {
                        NextRun(spots, runs, ref runIndex, ref position, buildings.Count, levels.Count, settings);
                    }

                    spots.Add(new ModuleSpot {
                        Run = runIndex, Position = position, Building = buildings.Count, Reach = new float[levels.Count], ModuleIndex = k
                    });
                    position += ground.ModuleSpacing;
                }

                buildings.Add(new Building {
                    Spec = spec,
                    Ground = ground,
                    Storeys = Mathf.Min(storeys, levels.Count),
                    Levels = levels,
                    Group = Group(frontageGroup, $"Building {b}"),
                    Jetty = jettied && levels.Count > 1 ? JettyDepth(jetties, placer) : 0f,
                    Jetties = jetties
                });
            }

            if (buildings.Count == frontage.buildings.Count) {
                ExtendLastBuilding(spots, buildings, runs, runIndex, position, plan.DeadEnds.Exists(end => end.Street == street && end.Point == frontage.to),
                    settings);
            }

            if (spots.Count == 0) {
                log.Add($"{label} not built: no building could be laid.");
                return null;
            }

            List<Column> columns = SplitColumns(spots, buildings, MaxColumnDepth(buildings, settings), settings, random);
            EdgeLine(street, frontage.from, sideSign, settings, out _, out _, out _, out Vector2 firstOutward);
            EdgeLine(street, frontage.to - 1, sideSign, settings, out _, out _, out _, out Vector2 lastOutward);

            return new Frontage {
                Label = label,
                Street = street,
                Spec = frontage,
                Runs = runs,
                Spots = spots,
                Buildings = buildings,
                Columns = columns,
                WallSetback = wallSetback,
                LeftToRight = leftToRight,
                BranchAtStart = LayoutSpecValidator.BranchWidth(street, frontage.from, firstOutward, streets, settings) > 0f,
                BranchAtEnd = LayoutSpecValidator.BranchWidth(street, frontage.to, lastOutward, streets, settings) > 0f
            };
        }

        /// <summary>
        /// Builds a laid-out frontage: its buildings, then the side walls
        /// between its columns (and the ends of its jettied storeys that no
        /// corner closes).
        /// </summary>
        private static void BuildFrontage(Frontage frontage, GeneratorSettings settings, System.Random random, PiecePlacer placer, List<string> log)
        {
            int pieces = placer.Placed;

            for (int b = 0; b < frontage.Buildings.Count; b++) {
                List<ModuleSpot> own = frontage.Spots.FindAll(spot => spot.Building == b);
                BuildBuilding(frontage.Buildings[b], own, frontage.Runs, frontage.WallSetback, frontage.LeftToRight, settings, random, placer,
                    $"{frontage.Label}, building {b}", log);
            }

            FillSides(frontage.Spots, frontage.Columns, frontage.Buildings, frontage.Runs, frontage.WallSetback, settings, random, placer, frontage.Label, log);
            int stepped = frontage.Columns.FindAll(column => column.Depth != 0f).Count;
            log.Add($"{frontage.Label}: {frontage.Buildings.Count} building(s), {frontage.Runs.Count} run(s), {frontage.Columns.Count} column(s) "
                + $"({stepped} off the line), {placer.Placed - pieces} piece(s).");
        }

        /// <summary>
        /// Every corner where runs meet on the outside: each bend of a
        /// frontage that turns away from its street, and each pair of
        /// frontage ends of different streets at a junction that can meet at
        /// a street corner (each carried no further than CornerMaxReach, the
        /// shortest pairs first). A frontage end at a junction left unpaired
        /// turns a side return into the branching street, joined to it.
        /// Marks the ends that are joined. Logs a line with the counts.
        /// </summary>
        private static List<Joint> FindJoints(List<Frontage> frontages, List<StreetSpec> streets, GeneratorSettings settings, List<string> log)
        {
            List<Joint> joints = new();
            int bends = 0;

            foreach (Frontage frontage in frontages) {
                for (int r = 0; r + 1 < frontage.Runs.Count; r++) {
                    int a = frontage.Spots.FindLastIndex(spot => spot.Run == r);
                    int b = frontage.Spots.FindIndex(spot => spot.Run == r + 1);

                    if (a < 0 || b < 0 || InsideBend(frontage.Runs, r)) {
                        continue;
                    }

                    joints.Add(Join(new RunEnd { Frontage = frontage, Spot = a }, new RunEnd { Frontage = frontage, Spot = b, AtStart = true }));
                    bends++;
                }
            }

            // The frontage ends that stop at a junction.
            List<RunEnd> ends = new();

            foreach (Frontage frontage in frontages) {
                if (frontage.BranchAtStart) {
                    ends.Add(new RunEnd { Frontage = frontage, Spot = 0, AtStart = true });
                }

                if (frontage.BranchAtEnd) {
                    ends.Add(new RunEnd { Frontage = frontage, Spot = frontage.Spots.Count - 1 });
                }
            }

            List<(float reach, int a, int b)> pairs = new();

            for (int i = 0; i < ends.Count; i++) {
                for (int j = i + 1; j < ends.Count; j++) {
                    if (ends[i].Frontage.Street == ends[j].Frontage.Street || !Meet(ends[i], 0f, ends[j], 0f, out float reachA, out float reachB, out _)) {
                        continue;
                    }

                    if (reachA >= -settings.SizeTolerance && reachB >= -settings.SizeTolerance && reachA <= settings.CornerMaxReach
                        && reachB <= settings.CornerMaxReach) {
                        pairs.Add((reachA + reachB, i, j));
                    }
                }
            }

            pairs.Sort((x, y) => x.reach.CompareTo(y.reach));
            bool[] used = new bool[ends.Count];
            int corners = 0;

            foreach ((float _, int a, int b) in pairs) {
                if (used[a] || used[b]) {
                    continue;
                }

                used[a] = used[b] = true;
                joints.Add(Join(ends[a], ends[b]));
                corners++;
            }

            int returns = 0;

            for (int i = 0; i < ends.Count; i++) {
                if (!used[i] && AddSideReturn(ends[i], streets, settings, out RunEnd turned)) {
                    joints.Add(Join(ends[i], turned));
                    returns++;
                }
            }

            log.Add($"Corners: {bends} bend(s), {corners} street corner(s), {returns} side return(s).");
            return joints;
        }

        /// <summary>
        /// A joint between two run ends, marking both as joined.
        /// </summary>
        private static Joint Join(RunEnd a, RunEnd b)
        {
            Mark(a);
            Mark(b);
            return new Joint { A = a, B = b };
        }

        /// <summary>
        /// Marks a run end as closed by a corner.
        /// </summary>
        private static void Mark(RunEnd end)
        {
            ModuleSpot spot = end.Frontage.Spots[end.Spot];

            if (end.AtStart) {
                spot.JoinedStart = true;
            } else {
                spot.JoinedEnd = true;
            }

            end.Frontage.Spots[end.Spot] = spot;
        }

        /// <summary>
        /// Where a run end's line, at a depth forward of the street edge,
        /// starts: the end of its module (its run's start or end side), the
        /// way out of the run from there, the run and the position along it.
        /// </summary>
        private static void EndLine(RunEnd end, float depth, out Vector2 point, out Vector2 outward, out Run run, out float position)
        {
            ModuleSpot spot = end.Frontage.Spots[end.Spot];
            run = end.Frontage.Runs[spot.Run];
            position = end.AtStart ? spot.Position : spot.Position + end.Frontage.Buildings[spot.Building].Ground.ModuleSpacing;
            outward = end.AtStart ? -run.Along : run.Along;
            point = run.Start + run.Along * position + run.Front * depth;
        }

        /// <summary>
        /// Where two run ends' lines (each at a depth forward of its street
        /// edge) meet: how far each must be carried out of its end to reach
        /// that point (below 0 if it is behind the end), and the point. False
        /// for (nearly) parallel lines.
        /// </summary>
        private static bool Meet(RunEnd a, float depthA, RunEnd b, float depthB, out float reachA, out float reachB, out Vector2 point)
        {
            EndLine(a, depthA, out Vector2 pointA, out Vector2 outA, out _, out _);
            EndLine(b, depthB, out Vector2 pointB, out Vector2 outB, out _, out _);
            reachB = 0f;
            point = pointA;

            if (!Intersect(pointA, outA, pointB, outB, out reachA)) {
                return false;
            }

            point = pointA + outA * reachA;
            reachB = Vector2.Dot(point - pointB, outB);
            return true;
        }

        /// <summary>
        /// A side return for a frontage end at a junction that meets no other
        /// frontage: a new run of one added module along the branching
        /// street's near edge, from where the frontage's line meets that edge,
        /// facing into the branching street (its last building's, its
        /// column's height and on the line). The branching street is the
        /// widest leaving the junction towards the frontage's side, as for the
        /// run's end. False if there is none or the lines don't meet ahead of
        /// the end.
        /// </summary>
        private static bool AddSideReturn(RunEnd end, List<StreetSpec> streets, GeneratorSettings settings, out RunEnd turned)
        {
            turned = default;
            Frontage frontage = end.Frontage;
            Vector2 junction = Ground(frontage.Street.points[end.AtStart ? frontage.Spec.from : frontage.Spec.to]);
            EndLine(end, 0f, out Vector2 point, out Vector2 outward, out Run run, out float position);
            float bestReach = float.MaxValue;
            Run best = default;

            foreach (StreetSpec other in streets) {
                if (other == frontage.Street) {
                    continue;
                }

                for (int j = 0; j < other.points.Count; j++) {
                    if ((Ground(other.points[j]) - junction).magnitude > settings.JunctionDistance) {
                        continue;
                    }

                    for (int n = j - 1; n <= j + 1; n += 2) {
                        if (n < 0 || n >= other.points.Count) {
                            continue;
                        }

                        Vector2 leaving = Ground(other.points[n]) - junction;

                        if (leaving.magnitude < settings.MinSegmentLength || Vector2.Dot(leaving.normalized, -run.Front) <= settings.BranchDot) {
                            continue;
                        }

                        Vector2 along = leaving.normalized;
                        Vector2 normal = new(-along.y, along.x);
                        float halfWidth = other.SegmentWidth(Mathf.Min(j, n)) * 0.5f;

                        // The edge on either side: the nearer one ahead of the end.
                        for (int sign = -1; sign <= 1; sign += 2) {
                            Vector2 edge = junction + normal * (sign * halfWidth);

                            if (!Intersect(point, outward, edge, along, out float reach) || reach < -settings.SizeTolerance || reach >= bestReach) {
                                continue;
                            }

                            bestReach = reach;
                            best = new Run {
                                Start = point + outward * reach,
                                Along = along,
                                Length = 0f,
                                Front = -normal * sign,
                                StartHeight = Height(run, position),
                                EndHeight = Height(run, position)
                            };
                        }
                    }
                }
            }

            if (bestReach > settings.CornerMaxReach) {
                return false;
            }

            ModuleSpot spot = frontage.Spots[end.Spot];
            Building building = frontage.Buildings[spot.Building];
            best.Length = building.Ground.ModuleSpacing;
            frontage.Runs.Add(best);
            frontage.Columns.Add(new Column { First = frontage.Spots.Count, Last = frontage.Spots.Count, Depth = 0f });
            frontage.Spots.Add(new ModuleSpot {
                Run = frontage.Runs.Count - 1,
                Position = 0f,
                Depth = 0f,
                Building = spot.Building,
                Column = frontage.Columns.Count - 1,
                Storeys = spot.Storeys,
                Reach = new float[building.Levels.Count],
                ModuleIndex = -1
            });
            turned = new RunEnd { Frontage = frontage, Spot = frontage.Spots.Count - 1, AtStart = true };
            return true;
        }

        /// <summary>
        /// Builds a corner: each side carried to where the two lines meet,
        /// storey by storey, then the joint covered (from the first side).
        /// </summary>
        private static void BuildJoint(Joint joint, GeneratorSettings settings, System.Random random, PiecePlacer placer, HashSet<string> missing)
        {
            BuildCornerSide(joint.A, joint.B, true, settings, random, placer, missing);
            BuildCornerSide(joint.B, joint.A, false, settings, random, placer, missing);
        }

        /// <summary>
        /// One side of a corner, storey by storey (each storey's line at its
        /// column's depth plus its building's jetty, meeting the other side's
        /// storey at the same index, or its top storey): a filler carries the
        /// storey from its end to the meeting point - a plain wall of the
        /// storey's height scaled to the length (the nearest in width), with a
        /// foundation under the ground storey, the beam band below and the
        /// jetty supports under the first upper storey scaled the same way. A
        /// storey above the other side's top is closed by side walls facing
        /// out of the corner; any other storey (on the covering side) gets the
        /// cover over the joint (PlaceCover). Under "Corner" of its building.
        /// </summary>
        private static void BuildCornerSide(RunEnd me, RunEnd other, bool cover, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            HashSet<string> missing)
        {
            Frontage frontage = me.Frontage;
            ModuleSpot spot = frontage.Spots[me.Spot];
            Building building = frontage.Buildings[spot.Building];
            ModuleSpot otherSpot = other.Frontage.Spots[other.Spot];
            Building otherBuilding = other.Frontage.Buildings[otherSpot.Building];
            float otherTop = Top(otherBuilding, otherSpot.Storeys);
            float setback = frontage.WallSetback;
            Transform group = Group(building.Group, "Corner");
            PieceAnnotation previous = null;

            for (int s = 0; s < spot.Storeys; s++) {
                int otherStorey = Mathf.Min(s, otherSpot.Storeys - 1);
                float depth = spot.Depth + StoreyJetty(building, s);
                float otherDepth = otherSpot.Depth + StoreyJetty(otherBuilding, otherStorey);

                if (!Meet(me, depth, other, otherDepth, out float reach, out _, out Vector2 point) || reach < -settings.SizeTolerance) {
                    continue;
                }

                reach = Mathf.Max(0f, reach);
                EndLine(me, depth, out _, out Vector2 outward, out Run run, out float position);
                StoreyLevel level = building.Levels[s];
                float from = me.AtStart ? position - reach : position;
                float height = Height(run, position);

                if (reach > settings.SizeTolerance) {
                    PlaceFiller(building, s, spot, run, from, reach, depth, setback, settings, random, placer, group, missing);
                }

                if (level.Base + level.Style.StoreySpacing > otherTop + settings.SizeTolerance) {
                    // Above the other side: closed by itself, facing out of
                    // the corner from its back line back past its own line.
                    List<PieceAnnotation> choices = SideWallChoices(building, s, building);

                    if (choices.Count == 0) {
                        missing.Add($"No side wall of the height of style '{level.Style.Name}' to close a corner.");
                        continue;
                    }

                    Vector2 onEnd = run.Start + run.Along * (me.AtStart ? from : from + reach);
                    float length = Mathf.Max(level.Style.ModuleSpacing, depth - spot.Depth + setback);
                    LaySideStorey(building, s, choices, ref previous, onEnd, run, depth - setback, length, height, outward, false, settings, random,
                        placer, group);
                } else if (cover && s < otherSpot.Storeys) {
                    Run otherRun = other.Frontage.Runs[otherSpot.Run];
                    PlaceCover(level, otherBuilding.Levels[otherStorey].Style, point, height + level.Base, run.Front, otherRun.Front, settings, random,
                        placer, group, missing);
                }
            }
        }

        /// <summary>
        /// A corner's filler for one storey: from along the run for a length,
        /// at the storey's depth. Its wall is a plain wall of the storey's
        /// height (borrowed as for side walls), the one nearest the length in
        /// width, scaled to it; the ground storey gets a foundation under it,
        /// a storey above a beam band the band's beams, and a jettied
        /// building's first upper storey a jetty support, each scaled to the
        /// length.
        /// </summary>
        private static void PlaceFiller(Building building, int storey, ModuleSpot spot, Run run, float from, float length, float depth, float setback,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group, HashSet<string> missing)
        {
            StoreyLevel level = building.Levels[storey];
            float middle = from + length * 0.5f;
            float width = length + settings.WallOverlap;
            List<PieceAnnotation> choices = SideWallChoices(building, storey, building);

            if (choices.Count == 0) {
                missing.Add($"No plain wall of the height of style '{level.Style.Name}' to fill a corner.");
            } else {
                PieceAnnotation wall = Nearest(choices, width);
                ModuleSpot filler = spot;
                filler.Position = from;
                filler.Depth = depth;
                PlaceWall(wall, level.Style, run, filler, length, level.Base, setback, settings.WallOverlap, placer, group, width);
            }

            if (storey == 0) {
                PieceAnnotation foundation = Nearest(building.Ground.Pieces("foundation"), length);

                if (foundation != null) {
                    PlaceInRun(foundation, placer, run, middle, depth + settings.FoundationFrontOffset, false, settings.FoundationHeight, true,
                        length / foundation.width, 1f, group);
                }
            }

            if (level.BeamHeight > 0f) {
                PieceAnnotation beam = PiecePlacer.Pick(building.Levels[storey - 1].Style.Pieces("beam"), null, random);

                if (beam != null) {
                    PlaceInRun(beam, placer, run, middle, depth - setback, true, level.BeamBottom, false, width / beam.width,
                        level.BeamHeight / beam.height, group);
                }
            }

            if (storey == 1 && building.Jetty > 0f) {
                PieceAnnotation support = Nearest(building.Jetties, width);
                PlaceInRun(support, placer, run, middle, spot.Depth - setback + spot.Reach[0], true, level.Base, true, width / support.width, 1f, group);
            }
        }

        /// <summary>
        /// The piece whose width is nearest a length (null for none).
        /// </summary>
        private static PieceAnnotation Nearest(IReadOnlyList<PieceAnnotation> pieces, float width)
        {
            PieceAnnotation nearest = null;

            foreach (PieceAnnotation piece in pieces) {
                if (nearest == null || Mathf.Abs(piece.width - width) < Mathf.Abs(nearest.width - width)) {
                    nearest = piece;
                }
            }

            return nearest;
        }

        /// <summary>
        /// A piece facing the run's street, centred at middle along it and
        /// scaled along its width and height: the back (backOnLine) or front of
        /// its render box on the line line forward of the street edge, and the
        /// top (fromTop) or bottom of it height above the street.
        /// </summary>
        private static void PlaceInRun(PieceAnnotation piece, PiecePlacer placer, Run run, float middle, float line, bool backOnLine, float height,
            bool fromTop, float widthScale, float heightScale, Transform group)
        {
            CatalogueEntry entry = placer.Entry(piece);

            if (entry == null || piece.fronts.Count != 1) {
                return;
            }

            Vector3 pieceFront = piece.fronts[0];
            Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
            Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
            Vector3 local = pieceRight * Vector3.Dot(centre, pieceRight)
                + (backOnLine ? -pieceFront * Extent(entry, -pieceFront) : pieceFront * Extent(entry, pieceFront))
                + Vector3.up * (fromTop ? entry.renderMax.y : entry.renderMin.y);
            Vector2 onLine = run.Start + run.Along * middle + run.Front * line;

            placer.PlaceAt(piece, local, new Vector3(onLine.x, Height(run, middle) + height, onLine.y), Yaw(Level(run.Front)) - Yaw(pieceFront),
                PieceScale(piece, widthScale, heightScale), group);
        }

        /// <summary>
        /// The cover over a corner's joint for one storey, at the point where
        /// the two lines meet: within CornerAngleTolerance of a right angle an
        /// outer corner piece, its outer edge on the point (FaceClearance out)
        /// and turned to sit between the two facades, else a column piece,
        /// the middle of its front there facing between them. From the
        /// storey's style, else the other side's, of the storey's height
        /// (scaled to it as walls are); none is logged in missing.
        /// </summary>
        private static void PlaceCover(StoreyLevel level, BuildingStyle otherStyle, Vector2 point, float height, Vector2 frontA, Vector2 frontB,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group, HashSet<string> missing)
        {
            bool corner = Mathf.Abs(Vector2.Angle(frontA, frontB) - 90f) <= settings.CornerAngleTolerance;
            string category = corner ? "outer-corner" : "column";
            int fronts = corner ? 2 : 1;
            float target = level.Style.ModuleHeight;
            List<PieceAnnotation> choices = new List<PieceAnnotation>(level.Style.Pieces(category)).FindAll(piece =>
                piece.fronts.Count == fronts && level.Style.Fits(piece.height, target));

            if (choices.Count == 0) {
                choices = new List<PieceAnnotation>(otherStyle.Pieces(category)).FindAll(piece => piece.fronts.Count == fronts && level.Style.Fits(piece.height, target));
            }

            PieceAnnotation cover = PiecePlacer.Pick(choices, null, random);
            CatalogueEntry entry = cover != null ? placer.Entry(cover) : null;

            if (entry == null) {
                missing.Add($"No usable {category} piece of the height of style '{level.Style.Name}' (or '{otherStyle.Name}'): those corner joints are left bare.");
                return;
            }

            Vector2 between = (frontA + frontB).normalized;
            Vector3 local;
            Vector3 facing;

            if (corner) {
                Vector3 front0 = cover.fronts[0];
                Vector3 front1 = cover.fronts[1];
                local = front0 * Extent(entry, front0) + front1 * Extent(entry, front1) + Vector3.up * entry.renderMin.y;
                facing = front0 + front1;
            } else {
                Vector3 front = cover.fronts[0];
                Vector3 right = Vector3.Cross(Vector3.up, -front);
                Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
                local = right * Vector3.Dot(centre, right) + front * Extent(entry, front) + Vector3.up * entry.renderMin.y;
                facing = front;
            }

            Vector2 at = point + between * FaceClearance;
            placer.PlaceAt(cover, local, new Vector3(at.x, height, at.y), Yaw(Level(between)) - Yaw(facing),
                new Vector3(1f, level.Style.FitScale(cover.height, target), 1f), group);
        }

        /// <summary>
        /// Moves the laying of modules on to the next run. Where the run ends
        /// on the inside of a bend with length left, an added module of the
        /// building being laid goes there first: its overrun stands behind the
        /// next run's walls, so the corner is closed.
        /// </summary>
        private static void NextRun(List<ModuleSpot> spots, List<Run> runs, ref int runIndex, ref float position, int building, int levels,
            GeneratorSettings settings)
        {
            if (InsideBend(runs, runIndex) && runs[runIndex].Length - position > settings.SizeTolerance) {
                spots.Add(new ModuleSpot { Run = runIndex, Position = position, Building = building, Reach = new float[levels], ModuleIndex = -1 });
            }

            runIndex++;
            position = 0f;
        }

        /// <summary>
        /// Whether a run and the next meet on the inside of a bend (the next
        /// heads into the street the run faces), where the walls can overlap
        /// behind their fronts; else they meet on the outside.
        /// </summary>
        private static bool InsideBend(List<Run> runs, int run)
        {
            return run + 1 < runs.Count && Vector2.Dot(runs[run + 1].Along, runs[run].Front) > 0f;
        }

        /// <summary>
        /// The length left at a frontage's end, after its buildings: its last
        /// building goes on along the runs with whole modules while they fit
        /// (moving to the next run as the buildings do). At a dead end one
        /// more module is added if any length is left, its overrun standing
        /// behind the dead end's facade, so the frontage always meets it.
        /// Elsewhere what is left under a module is filled by its corner.
        /// </summary>
        private static void ExtendLastBuilding(List<ModuleSpot> spots, List<Building> buildings, List<Run> runs, int runIndex, float position,
            bool deadEnd, GeneratorSettings settings)
        {
            if (buildings.Count == 0) {
                return;
            }

            int last = buildings.Count - 1;
            float spacing = buildings[last].Ground.ModuleSpacing;
            int levels = buildings[last].Levels.Count;

            while (true) {
                bool fits = position + spacing <= runs[runIndex].Length + settings.SizeTolerance;

                if (!fits && runIndex < runs.Count - 1) {
                    NextRun(spots, runs, ref runIndex, ref position, last, levels, settings);
                    continue;
                }

                if (!fits && !(deadEnd && runs[runIndex].Length - position > settings.SizeTolerance)) {
                    return;
                }

                spots.Add(new ModuleSpot { Run = runIndex, Position = position, Building = last, Reach = new float[levels], ModuleIndex = -1 });
                position += spacing;

                if (!fits) {
                    return;
                }
            }
        }

        /// <summary>
        /// The facade across a street's dead end: one straight run through the
        /// end point, square to the end segment, facing back up the street,
        /// centred on the middle line and as many modules long as cover the
        /// width (the rest running on behind the side facades). Its building
        /// (styles, modules, storeys) is made by StoreyPlan. No columns.
        /// </summary>
        private static void BuildDeadEnd(StoreyPlan.DeadEnd deadEnd, Dictionary<string, BuildingStyle> styles, StoreyPlan plan,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform parent, List<string> log)
        {
            StreetSpec street = deadEnd.Street;
            BuildingSpec spec = deadEnd.Building;
            string label = $"Street '{street.id}', dead end at point {deadEnd.Point}";
            BuildingStyle ground = Find(styles, spec.storeys[0]);
            int storeys = plan.Storeys(spec);

            if (storeys == 0 || ground == null || ground.ModuleSpacing <= 0f) {
                log.Add($"{label} not built: its style has no module yet.");
                return;
            }

            // The way out of the street at its end; the facade faces back.
            int inner = deadEnd.Point == 0 ? 1 : deadEnd.Point - 1;
            Vector3 endPoint = street.points[deadEnd.Point];
            Vector2 outward = (Ground(endPoint) - Ground(street.points[inner])).normalized;
            Vector3 right3 = Vector3.Cross(Vector3.up, Level(outward));
            Vector2 right = new(right3.x, right3.z);
            float length = spec.widthModules * ground.ModuleSpacing;

            List<Run> runs = new() {
                new Run {
                    Start = Ground(endPoint) - right * (length * 0.5f),
                    Along = right,
                    Length = length,
                    Front = -outward,
                    StartHeight = endPoint.y,
                    EndHeight = endPoint.y
                }
            };

            int pieces = placer.Placed;
            List<StoreyLevel> levels = StoreyLevels(spec, storeys, styles, settings, false, label, log);
            Building building = new() {
                Spec = spec,
                Ground = ground,
                Storeys = levels.Count,
                Levels = levels,
                Group = Group(Group(parent, $"{street.id} dead end {deadEnd.Point}"), "Building 0")
            };
            // One column, the building's full height.
            List<ModuleSpot> spots = new();

            for (int k = 0; k < spec.widthModules; k++) {
                spots.Add(new ModuleSpot { Run = 0, Position = k * ground.ModuleSpacing, Storeys = levels.Count, Reach = new float[levels.Count], ModuleIndex = k });
            }

            BuildBuilding(building, spots, runs, Setback(new[] { spec }, styles, placer), true, settings, random, placer, label, log);
            log.Add($"{label}: {placer.Placed - pieces} piece(s).");
        }

        /// <summary>
        /// A building's storeys from the ground up, each with its style and
        /// base height; a storey whose style has no module yet ends the list
        /// there (logged). Each storey stands at its style's storey spacing on
        /// the one below, unless both their styles have beam pieces: then a
        /// band as tall as the lower style's tallest beam lies between them,
        /// overlapping the walls below and above by StoreyBeamOverlap. In a
        /// jettied building the first upper storey stands instead on the jetty
        /// supports' deck, whose top lies StoreyBeamOverlap below the top of
        /// the ground storey's walls.
        /// </summary>
        private static List<StoreyLevel> StoreyLevels(BuildingSpec spec, int storeys, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings,
            bool jettied, string label, List<string> log)
        {
            List<StoreyLevel> levels = new();

            for (int s = 0; s < storeys; s++) {
                BuildingStyle style = Find(styles, StoreyStyle(spec, s));

                if (style == null || style.StoreySpacing <= 0f) {
                    log.Add($"{label} stops below storey {s}: style '{StoreyStyle(spec, s)}' has no module yet.");
                    break;
                }

                StoreyLevel level = new() { Style = style, Base = settings.GroundStoreyHeight };

                if (s > 0) {
                    StoreyLevel below = levels[s - 1];
                    level.BeamHeight = jettied && s == 1 ? 0f : BeamHeight(below.Style, style);

                    if (jettied && s == 1) {
                        level.Base = below.Base + below.Style.ModuleHeight - settings.StoreyBeamOverlap;
                    } else if (level.BeamHeight > 0f) {
                        // The beam's bottom overlaps the top of the walls
                        // below, the walls above overlap its top.
                        level.BeamBottom = below.Base + below.Style.ModuleHeight - settings.StoreyBeamOverlap;
                        level.Base = level.BeamBottom + level.BeamHeight - settings.StoreyBeamOverlap;
                    } else {
                        level.Base = below.Base + below.Style.StoreySpacing;
                    }
                }

                levels.Add(level);
            }

            return levels;
        }

        /// <summary>
        /// How far a building's upper storeys jut out on its jetty pieces: the
        /// deepest piece's depth front to back (its render box along its front).
        /// </summary>
        private static float JettyDepth(List<PieceAnnotation> jetties, PiecePlacer placer)
        {
            float deepest = 0f;

            foreach (PieceAnnotation piece in jetties) {
                CatalogueEntry entry = placer.Entry(piece);
                deepest = Mathf.Max(deepest, Extent(entry, piece.fronts[0]) + Extent(entry, -piece.fronts[0]));
            }

            return deepest;
        }

        /// <summary>
        /// The height of the beam band between a storey of one style and one
        /// of another above it: the lower style's tallest beam piece where both
        /// styles have beam pieces, else 0 (no beam).
        /// </summary>
        private static float BeamHeight(BuildingStyle lower, BuildingStyle upper)
        {
            if (upper.Pieces("beam").Count == 0) {
                return 0f;
            }

            float tallest = 0f;

            foreach (PieceAnnotation piece in lower.Pieces("beam")) {
                tallest = Mathf.Max(tallest, piece.height);
            }

            return tallest;
        }

        /// <summary>
        /// One building at its module spots: its foundations, then its
        /// storeys (the upper ones jutting out by its jetty), then the jetty
        /// supports and the beams between storeys.
        /// </summary>
        private static void BuildBuilding(Building building, List<ModuleSpot> spots, List<Run> runs, float wallSetback, bool leftToRight,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, string label, List<string> log)
        {
            BuildFoundations(building.Ground, spots, runs, settings, random, placer, Group(building.Group, "Foundations"));

            for (int s = 0; s < building.Levels.Count; s++) {
                BuildStorey(building.Spec, s, building.Levels[s], spots, runs, building.Ground.ModuleSpacing, StoreyJetty(building, s), wallSetback,
                    leftToRight, settings, random, placer, Group(building.Group, $"Storey {s}"), label, log);
            }

            BuildJetty(building, spots, runs, wallSetback, settings, random, placer);
            BuildBeams(building, spots, runs, wallSetback, settings, random, placer);
        }

        /// <summary>
        /// How far a building's storey stands forward of its modules' depth:
        /// its jetty above the ground storey, else 0.
        /// </summary>
        private static float StoreyJetty(Building building, int storey)
        {
            return storey > 0 ? building.Jetty : 0f;
        }

        /// <summary>
        /// The supports under a jettied building's first upper storey: one per
        /// module whose column stands above the ground storey, from its jetty
        /// pieces at random (never the one before it), facing the street,
        /// centred on the module along it and scaled along its width to the
        /// module as the walls are, its back against the face of the ground
        /// storey's wall there and the top of its deck at the upper storey's
        /// base (so its brackets hang in front of the ground walls' top). Under
        /// "Jetty".
        /// </summary>
        private static void BuildJetty(Building building, List<ModuleSpot> spots, List<Run> runs, float wallSetback, GeneratorSettings settings,
            System.Random random, PiecePlacer placer)
        {
            if (building.Jetty <= 0f) {
                return;
            }

            float spacing = building.Ground.ModuleSpacing;
            float deckTop = building.Levels[1].Base;
            Transform group = null;
            PieceAnnotation previous = null;

            foreach (ModuleSpot spot in spots) {
                if (spot.Storeys < 2) {
                    continue;
                }

                PieceAnnotation piece = PiecePlacer.Pick(building.Jetties, previous, random);
                CatalogueEntry entry = placer.Entry(piece);
                previous = piece;
                // The middle of the top of its back, in its own space.
                Vector3 pieceFront = piece.fronts[0];
                Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
                Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
                Vector3 local = pieceRight * Vector3.Dot(centre, pieceRight) - pieceFront * Extent(entry, -pieceFront) + Vector3.up * entry.renderMax.y;
                float widthScale = building.Ground.FitScale(piece.width, spacing + settings.WallOverlap);

                Run run = runs[spot.Run];
                float middle = spot.Position + spacing * 0.5f;
                Vector2 onWallFace = run.Start + run.Along * middle + run.Front * (spot.Depth - wallSetback + spot.Reach[0]);
                float height = Height(run, middle) + deckTop;

                group ??= Group(building.Group, "Jetty");
                placer.PlaceAt(piece, local, new Vector3(onWallFace.x, height, onWallFace.y), Yaw(Level(run.Front)) - Yaw(pieceFront),
                    PieceScale(piece, widthScale, 1f), group);
            }
        }

        /// <summary>
        /// The beams in every beam band between two storeys (StoreyLevels): one
        /// per module whose column stands above the band, from the lower
        /// storey's style at random (never the one before it), in the band's
        /// own space - its back on the walls' back line, its bottom on the
        /// band's bottom, centred on the module along the street - scaled
        /// along its width to the module as the walls are and in height to the
        /// band. Under "Beams".
        /// </summary>
        private static void BuildBeams(Building building, List<ModuleSpot> spots, List<Run> runs, float wallSetback, GeneratorSettings settings,
            System.Random random, PiecePlacer placer)
        {
            float spacing = building.Ground.ModuleSpacing;
            Transform group = null;

            for (int s = 1; s < building.Levels.Count; s++) {
                StoreyLevel level = building.Levels[s];

                if (level.BeamHeight <= 0f) {
                    continue;
                }

                BuildingStyle lower = building.Levels[s - 1].Style;
                PieceAnnotation previous = null;

                foreach (ModuleSpot spot in spots) {
                    if (spot.Storeys <= s) {
                        continue;
                    }

                    PieceAnnotation piece = PiecePlacer.Pick(lower.Pieces("beam"), previous, random);
                    CatalogueEntry entry = placer.Entry(piece);
                    previous = piece;

                    if (entry == null || piece.fronts.Count != 1) {
                        continue;
                    }

                    // The middle of the bottom of its back, in its own space.
                    Vector3 pieceFront = piece.fronts[0];
                    Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
                    Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
                    Vector3 local = pieceRight * Vector3.Dot(centre, pieceRight) - pieceFront * Extent(entry, -pieceFront) + Vector3.up * entry.renderMin.y;
                    float widthScale = lower.FitScale(piece.width, spacing + settings.WallOverlap);

                    Run run = runs[spot.Run];
                    float middle = spot.Position + spacing * 0.5f;
                    Vector2 onBackLine = run.Start + run.Along * middle + run.Front * (spot.Depth + StoreyJetty(building, s) - wallSetback);
                    float height = Height(run, middle) + level.BeamBottom;

                    group ??= Group(building.Group, "Beams");
                    placer.PlaceAt(piece, local, new Vector3(onBackLine.x, height, onBackLine.y), Yaw(Level(run.Front)) - Yaw(pieceFront),
                        PieceScale(piece, widthScale, level.BeamHeight / (entry.renderMax.y - entry.renderMin.y)), group);
                }
            }
        }

        /// <summary>
        /// The deepest a column may stand forward or back: ColumnDepthMax, but
        /// no deeper than half the narrowest module of any storey of the
        /// frontage, so two neighbouring columns differ by at most a module
        /// and one side wall, running back from the forward one's back line,
        /// reaches the recessed one's.
        /// </summary>
        private static float MaxColumnDepth(List<Building> buildings, GeneratorSettings settings)
        {
            float depth = settings.ColumnDepthMax;

            foreach (Building building in buildings) {
                foreach (StoreyLevel level in building.Levels) {
                    depth = Mathf.Min(depth, level.Style.ModuleWidth * 0.5f);
                }
            }

            return depth;
        }

        /// <summary>
        /// Cuts every building into columns of ColumnWidthMin to ColumnWidthMax
        /// modules (a column never crosses a building's end or a bend; the
        /// last of each takes what is left), and gives each a depth and storey
        /// count, written into its modules. Off the line at ColumnShare -
        /// only on a run of at least ColumnMinRunModules modules and never
        /// holding the run's first or last module - ColumnDepthMin to the most
        /// it may be deep, forward at ColumnOutShare else back. Taller or lower
        /// than its building at ColumnHeightShare (either equally likely), by
        /// 1 to ColumnStoreyVariation storeys, kept between MinStoreys and the
        /// storeys the building has. Every draw is made for every column, so
        /// the sequence doesn't depend on which apply.
        /// </summary>
        private static List<Column> SplitColumns(List<ModuleSpot> spots, List<Building> buildings, float maxDepth, GeneratorSettings settings,
            System.Random random)
        {
            List<Column> columns = new();
            int first = 0;

            while (first < spots.Count) {
                // The run's first and last module.
                int runFirst = first;
                int runLast = first;

                while (runFirst > 0 && spots[runFirst - 1].Run == spots[first].Run) {
                    runFirst--;
                }

                while (runLast + 1 < spots.Count && spots[runLast + 1].Run == spots[first].Run) {
                    runLast++;
                }

                int width = random.Next(settings.ColumnWidthMin, settings.ColumnWidthMax + 1);
                int last = first;

                while (last - first + 1 < width && last < runLast && spots[last + 1].Building == spots[first].Building) {
                    last++;
                }

                bool offLine = random.NextDouble() < settings.ColumnShare;
                bool forward = random.NextDouble() < settings.ColumnOutShare;
                float depth = Mathf.Min(Mathf.Lerp(settings.ColumnDepthMin, settings.ColumnDepthMax, (float)random.NextDouble()), maxDepth);
                bool varied = random.NextDouble() < settings.ColumnHeightShare;
                bool taller = random.NextDouble() < 0.5f;
                int change = random.Next(1, settings.ColumnStoreyVariation + 1);

                if (!offLine || runLast - runFirst + 1 < settings.ColumnMinRunModules || first == runFirst || last == runLast
                    || maxDepth < settings.ColumnDepthMin) {
                    depth = 0f;
                } else if (!forward) {
                    depth = -depth;
                }

                Building building = buildings[spots[first].Building];
                int storeys = building.Storeys;

                if (varied) {
                    storeys = Mathf.Clamp(storeys + (taller ? change : -change), Mathf.Min(settings.MinStoreys, building.Storeys), building.Levels.Count);
                }

                for (int k = first; k <= last; k++) {
                    ModuleSpot spot = spots[k];
                    spot.Depth = depth;
                    spot.Column = columns.Count;
                    spot.Storeys = storeys;
                    spots[k] = spot;
                }

                columns.Add(new Column { First = first, Last = last, Depth = depth });
                first = last + 1;
            }

            return columns;
        }

        /// <summary>
        /// The side walls between every two neighbouring columns of a run (in
        /// one building or two), plain walls of any width that show no window
        /// or door, turned side-on to face out of their column's end towards
        /// the other. A column's storeys that reach above the other column's
        /// top get SideWallLength of them each, end to end from its back line,
        /// so it seems to go on behind. Where the two stand at different
        /// depths, the forward one's other storeys get a module's length each
        /// and the step a foundation: in the recess, backed against the forward
        /// column's end, when the other column is inset (so they run back
        /// through its front walls); otherwise in the forward column, behind
        /// its front walls. Adds a line to log for each storey style that has
        /// no side wall to give.
        /// </summary>
        private static void FillSides(List<ModuleSpot> spots, List<Column> columns, List<Building> buildings, List<Run> runs, float wallSetback,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, string label, List<string> log)
        {
            HashSet<string> missing = new();

            for (int i = 0; i + 1 < spots.Count; i++) {
                ModuleSpot a = spots[i];
                ModuleSpot b = spots[i + 1];

                if (a.Run != b.Run || a.Column == b.Column) {
                    continue;
                }

                Run run = runs[a.Run];
                // The columns meet where the second one starts.
                Vector2 onJoin = run.Start + run.Along * b.Position;
                float height = Height(run, b.Position);
                // a's walls face out of its end along the run, b's back.
                FillSide(a, columns[a.Column], b, run.Along, onJoin, height, run, buildings, wallSetback, settings, random, placer, $"Side {i}", missing);
                FillSide(b, columns[b.Column], a, -run.Along, onJoin, height, run, buildings, wallSetback, settings, random, placer, $"Side {i}", missing);
            }

            // A run's two ends (a frontage's ends and its bends) meet no
            // column of the run: a jettied storey there is closed by itself.
            for (int i = 0; i < spots.Count; i++) {
                ModuleSpot spot = spots[i];
                Building building = buildings[spot.Building];
                Run run = runs[spot.Run];

                if ((i == 0 || spots[i - 1].Run != spot.Run) && !spot.JoinedStart) {
                    CloseJetty(spot, building, run, -run.Along, spot.Position, wallSetback, settings, random, placer, $"End {i}", missing);
                }

                if ((i == spots.Count - 1 || spots[i + 1].Run != spot.Run) && !spot.JoinedEnd) {
                    CloseJetty(spot, building, run, run.Along, spot.Position + building.Ground.ModuleSpacing, wallSetback, settings, random, placer,
                        $"End {i} far", missing);
                }
            }

            foreach (string style in missing) {
                log.Add($"{label}: no side wall of the height of style '{style}' (in it, the neighbouring column's style or the storeys above); "
                    + "those side walls are left out. Tag a plain wall of that height with the style in Review Pieces.");
            }
        }

        /// <summary>
        /// One column's side walls where it meets another (FillSides), storey
        /// by storey, each storey's depth being its column's plus its
        /// building's jetty above the ground (so a jettied storey beside one
        /// that isn't is a step too). A step's walls are as long as a module
        /// or, if the step is deeper, enough to reach back past the other's
        /// back line. Under a group of the column's building, made only if
        /// anything is placed. Adds the name of each storey style with nothing
        /// to give to missing.
        /// </summary>
        private static void FillSide(ModuleSpot spot, Column column, ModuleSpot other, Vector2 face, Vector2 onJoin, float height, Run run,
            List<Building> buildings, float wallSetback, GeneratorSettings settings, System.Random random, PiecePlacer placer, string name,
            HashSet<string> missing)
        {
            Building building = buildings[spot.Building];
            Building otherBuilding = buildings[other.Building];
            float otherTop = Top(otherBuilding, other.Storeys);
            float raised = SideWallLength(column, building, settings);
            float groundThickness = 0f;
            Transform group = null;
            PieceAnnotation previous = null;

            for (int s = 0; s < spot.Storeys; s++) {
                StoreyLevel level = building.Levels[s];
                float depth = spot.Depth + StoreyJetty(building, s);
                float otherDepth = other.Depth + StoreyJetty(otherBuilding, s);
                bool forward = depth - otherDepth >= SameDepth;
                // The step's walls stand in the recess when the other column is
                // inset there, backed against this column's end.
                bool inRecess = forward && otherDepth < 0f;
                bool above = level.Base + level.Style.StoreySpacing > otherTop + settings.SizeTolerance;
                float length = above ? raised : forward ? Mathf.Max(level.Style.ModuleSpacing, depth - otherDepth + wallSetback) : 0f;

                if (length <= 0f) {
                    continue;
                }

                List<PieceAnnotation> choices = SideWallChoices(building, s, otherBuilding);

                if (choices.Count == 0) {
                    missing.Add(level.Style.Name);
                    continue;
                }

                bool backOnJoin = !above && inRecess;
                // Own walls start at the column's back line, its front walls
                // hiding their ends (from the front line they would stand out
                // past every front wall shallower than the deepest). In the
                // recess they start just behind the face of this column's
                // front wall beside the join, covering its end.
                float start = depth - wallSetback + (backOnJoin ? Mathf.Max(0f, spot.Reach[s] - FaceClearance) : 0f);
                group ??= Group(building.Group, name);
                float thickness = LaySideStorey(building, s, choices, ref previous, onJoin, run, start, length, height, face, backOnJoin, settings, random,
                    placer, group);

                if (s == 0) {
                    groundThickness = backOnJoin ? thickness : 0f;
                }
            }

            // Only a step at the ground storey (columns, not jetties) stands
            // on a foundation.
            if (spot.Depth - other.Depth < SameDepth) {
                return;
            }

            // The step's foundation, its front out past the ground storey's
            // side wall (which in the recess stands its thickness in from the
            // join).
            PieceAnnotation foundation = PiecePlacer.Pick(building.Ground.Pieces("foundation"), null, random);

            if (foundation != null) {
                group ??= Group(building.Group, name);
                Vector2 corner = onJoin + run.Front * (spot.Depth + settings.FoundationFrontOffset) + face * (groundThickness + settings.FoundationFrontOffset);
                PlaceSideFoundation(foundation, corner, height + settings.FoundationHeight, face, -run.Front, placer, group);
            }
        }

        /// <summary>
        /// One storey's side walls along a run back from a join (or a run's
        /// end): the beam band below it, if it has one, the same length and
        /// the same way round, so it closes the gap to the storey below; then
        /// walls from choices end to end from start until length is covered,
        /// never the piece before, each scaled in height to the storey.
        /// Returns the first wall's thickness (0 if none was placed).
        /// </summary>
        private static float LaySideStorey(Building building, int storey, List<PieceAnnotation> choices, ref PieceAnnotation previous, Vector2 onJoin,
            Run run, float start, float length, float height, Vector2 face, bool backOnJoin, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform group)
        {
            StoreyLevel level = building.Levels[storey];
            float first = 0f;
            float laid = 0f;

            if (level.BeamHeight > 0f) {
                LaySideBeams(building.Levels[storey - 1].Style.Pieces("beam"), onJoin, run, start, length, height + level.BeamBottom, level.BeamHeight,
                    face, backOnJoin, settings, random, placer, group);
            }

            while (laid < length - settings.SizeTolerance) {
                PieceAnnotation wall = PiecePlacer.Pick(choices, previous, random);
                float step = wall.width - settings.WallOverlap;

                if (step <= 0f) {
                    break;
                }

                previous = wall;
                Vector2 corner = onJoin + run.Front * (start - laid);
                float heightScale = level.Style.FitScale(wall.height, level.Style.ModuleHeight);
                float thickness = PlaceSideWall(wall, corner, height + level.Base, heightScale, face, -run.Front, backOnJoin, placer, group);

                if (laid == 0f) {
                    first = thickness;
                }

                laid += step;
            }

            return first;
        }

        /// <summary>
        /// Closes a jettied building's upper storeys at a run's end (a
        /// frontage's end or a bend), where no column of the run stands
        /// beside it: each storey above the ground gets side walls facing out
        /// of the end, from its back line back past its column's own line (a
        /// module, or the jetty plus the walls' setback if longer), with the
        /// beam band below it as at a join. Under "End <name>" of its building.
        /// </summary>
        private static void CloseJetty(ModuleSpot spot, Building building, Run run, Vector2 face, float position, float wallSetback,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, string name, HashSet<string> missing)
        {
            if (building.Jetty <= 0f) {
                return;
            }

            Vector2 onEnd = run.Start + run.Along * position;
            float height = Height(run, position);
            Transform group = null;
            PieceAnnotation previous = null;

            for (int s = 1; s < spot.Storeys; s++) {
                StoreyLevel level = building.Levels[s];
                List<PieceAnnotation> choices = SideWallChoices(building, s, building);

                if (choices.Count == 0) {
                    missing.Add(level.Style.Name);
                    continue;
                }

                float start = spot.Depth + building.Jetty - wallSetback;
                float length = Mathf.Max(level.Style.ModuleSpacing, building.Jetty + wallSetback);
                group ??= Group(building.Group, name);
                LaySideStorey(building, s, choices, ref previous, onEnd, run, start, length, height, face, false, settings, random, placer, group);
            }
        }

        /// <summary>
        /// Beams laid end to end along a side wall run, at random (never the
        /// one before it), from start (along the frontage's front, from the
        /// street edge) back until length is covered: turned side-on to face
        /// face, the end of each one's render box at its place along the run,
        /// its bottom at bottom and scaled in height to the band. Its front on
        /// the join (its depth behind it), or with backOnJoin its back (its
        /// depth in front), as the side walls beside it.
        /// </summary>
        private static void LaySideBeams(IReadOnlyList<PieceAnnotation> beams, Vector2 onJoin, Run run, float start, float length, float bottom,
            float bandHeight, Vector2 face, bool backOnJoin, GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group)
        {
            Vector3 front = Level(face);
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            bool runsRight = Vector3.Dot(right, Level(-run.Front)) > 0f;
            PieceAnnotation previous = null;
            float laid = 0f;

            while (laid < length - settings.SizeTolerance) {
                PieceAnnotation piece = PiecePlacer.Pick(beams, previous, random);
                CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;
                float step = piece != null ? piece.width - settings.WallOverlap : 0f;

                if (entry == null || piece.fronts.Count != 1 || step <= 0f) {
                    return;
                }

                previous = piece;
                // In its own space: the end it starts from (its left end, seen
                // from in front, if it runs to its right), at the bottom of its
                // back.
                Vector3 pieceFront = piece.fronts[0];
                Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
                float endAlong = runsRight ? -Extent(entry, -pieceRight) : Extent(entry, pieceRight);
                Vector3 local = pieceRight * endAlong - pieceFront * Extent(entry, -pieceFront) + Vector3.up * entry.renderMin.y;
                float depth = Extent(entry, pieceFront) + Extent(entry, -pieceFront);
                Vector2 corner = onJoin + run.Front * (start - laid);
                Vector2 onBack = backOnJoin ? corner : corner - face * depth;

                placer.PlaceAt(piece, local, new Vector3(onBack.x, bottom, onBack.y), Yaw(front) - Yaw(pieceFront),
                    PieceScale(piece, 1f, bandHeight / (entry.renderMax.y - entry.renderMin.y)), group);
                laid += step;
            }
        }

        /// <summary>
        /// The plain walls a column's storey may build its side walls from:
        /// those of the storey's height (as they are or scaled to it; the
        /// ground-storey-only ones only on the ground storey), from its own
        /// style if it has any, else from the neighbouring column's style at
        /// the same storey, else from the styles of its building's storeys
        /// above. Empty if none has one.
        /// </summary>
        private static List<PieceAnnotation> SideWallChoices(Building building, int storey, Building other)
        {
            StoreyLevel level = building.Levels[storey];
            List<PieceAnnotation> choices = SideWallsOfHeight(level.Style, storey, level.Style.ModuleHeight);

            if (choices.Count == 0) {
                choices = SideWallsOfHeight(other.Levels[Mathf.Min(storey, other.Levels.Count - 1)].Style, storey, level.Style.ModuleHeight);
            }

            for (int s = storey + 1; choices.Count == 0 && s < building.Levels.Count; s++) {
                choices = SideWallsOfHeight(building.Levels[s].Style, storey, level.Style.ModuleHeight);
            }

            return choices;
        }

        /// <summary>
        /// A style's side walls that may stand on a storey (0 = the ground)
        /// and fit a height (BuildingStyle.Fits: as they are or scaled).
        /// </summary>
        private static List<PieceAnnotation> SideWallsOfHeight(BuildingStyle style, int storey, float height)
        {
            return new List<PieceAnnotation>(style.SideWalls()).FindAll(piece =>
                (storey == 0 || !piece.groundStoreyOnly) && style.Fits(piece.height, height));
        }

        /// <summary>
        /// How far back a column's storey's side walls run where it stands
        /// above its neighbour: SideWallMultiplier times its width in modules,
        /// at least one module (none when the multiplier is 0).
        /// </summary>
        private static float SideWallLength(Column column, Building building, GeneratorSettings settings)
        {
            if (settings.SideWallMultiplier <= 0f) {
                return 0f;
            }

            int modules = Mathf.Max(1, Mathf.RoundToInt(settings.SideWallMultiplier * (column.Last - column.First + 1)));
            return modules * building.Ground.ModuleSpacing;
        }

        /// <summary>
        /// The height above the street of the top of a building's first
        /// storeys.
        /// </summary>
        private static float Top(Building building, int storeys)
        {
            StoreyLevel level = building.Levels[storeys - 1];
            return level.Base + level.Style.StoreySpacing;
        }

        /// <summary>
        /// One storey's wall modules (those whose column stands that high): a
        /// door wall at the spec's door module or at GroundDoorShare (ground
        /// storey only), else a window wall at the storey's window share, else
        /// a plain wall, never the same piece as the module before it. Where
        /// the storey's plain walls hold one marked fillsStorey, that wall
        /// stands at every module but the spec's door (no random doors or
        /// window walls). The walls stand jetty forward of their modules'
        /// depth (a jettied building's upper storeys).
        /// </summary>
        private static void BuildStorey(BuildingSpec spec, int storey, StoreyLevel level, List<ModuleSpot> spots, List<Run> runs, float spacing, float jetty,
            float wallSetback, bool leftToRight, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            Transform group, string label, List<string> log)
        {
            BuildingStyle style = level.Style;
            float windowShare = storey == 0 ? settings.GroundWindowShare : settings.UpperWindowShare;
            PieceAnnotation previous = null;
            // A plain wall that fills its storey (one at random if there are
            // several) stands at every module but the spec's door.
            List<PieceAnnotation> fillers = new List<PieceAnnotation>(style.Modules("wall", storey)).FindAll(piece => piece.fillsStorey);
            PieceAnnotation filler = PiecePlacer.Pick(fillers, null, random);
            IReadOnlyList<PieceAnnotation> filled = filler != null ? new[] { filler } : null;

            for (int k = 0; k < spots.Count; k++) {
                // Counted over the spec's modules (seen from the street), so
                // modules the generator added never take or move the spec's door.
                int index = spots[k].ModuleIndex;
                int fromLeft = index < 0 ? -1 : leftToRight ? index : spec.widthModules - 1 - index;
                // Both drawn for every module, so the sequence doesn't depend
                // on which styles have door or window walls.
                bool doorDrawn = random.NextDouble() < settings.GroundDoorShare;
                bool windowDrawn = random.NextDouble() < windowShare;
                bool door = storey == 0 && (fromLeft == spec.doorModule || (doorDrawn && filler == null));

                // Above its column's top.
                if (storey >= spots[k].Storeys) {
                    continue;
                }

                IReadOnlyList<PieceAnnotation> choices;

                if (door && style.Modules("door-wall", storey).Count > 0) {
                    choices = style.Modules("door-wall", storey);
                } else if (filled != null) {
                    choices = filled;
                } else if (windowDrawn && style.Modules("window-wall", storey).Count > 0) {
                    choices = style.Modules("window-wall", storey);
                } else {
                    choices = style.Modules("wall", storey);
                }

                PieceAnnotation piece = PiecePlacer.Pick(choices, previous, random);

                if (piece == null) {
                    log.Add($"{label}: style '{style.Name}' has no wall that fits its module.");
                    return;
                }

                previous = piece;
                ModuleSpot placed = spots[k];
                placed.Depth += jetty;
                GameObject wall = PlaceWall(piece, style, runs[placed.Run], placed, spacing, level.Base, wallSetback, settings.WallOverlap, placer, group);

                if (wall != null) {
                    spots[k].Reach[storey] = Reach(piece, placer);
                    PlaceInserts(piece, wall.transform, settings, random, placer, group);
                }
            }
        }

        /// <summary>
        /// Window inserts in a placed wall's window holes (the catalogue's
        /// window openings): each hole at WindowInsertShare gets one of the
        /// inserts that fit it - at least the hole's width and height, and no
        /// more than WindowInsertMaxMargin bigger on each side - chosen at
        /// random, never the one before it in this wall. It faces the way the
        /// wall does, its middle on the hole's middle, at the middle of the
        /// wall's depth moved forward by WindowInsertDepth. Both draws are
        /// made for every hole, so the sequence doesn't depend on what fits.
        /// </summary>
        private static void PlaceInserts(PieceAnnotation piece, Transform wall, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform group)
        {
            CatalogueEntry entry = placer.Entry(piece);

            if (entry == null) {
                return;
            }

            Vector3 front = piece.fronts[0];
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            PieceAnnotation previous = null;

            foreach (Opening opening in entry.openings) {
                if (opening.kind != "window") {
                    continue;
                }

                bool fill = random.NextDouble() < settings.WindowInsertShare;
                Vector3 size = opening.max - opening.min;
                float holeWidth = Mathf.Abs(Vector3.Dot(size, right));
                float holeHeight = size.y;
                List<PieceAnnotation> fitting = new();

                foreach (PieceAnnotation insert in _inserts) {
                    if (insert.width >= holeWidth && insert.width - holeWidth <= 2f * settings.WindowInsertMaxMargin
                        && insert.height >= holeHeight && insert.height - holeHeight <= 2f * settings.WindowInsertMaxMargin) {
                        fitting.Add(insert);
                    }
                }

                PieceAnnotation chosen = PiecePlacer.Pick(fitting, previous, random);

                if (!fill || chosen == null) {
                    continue;
                }

                previous = chosen;
                // The hole is on the back plane: move to the middle of the
                // wall's depth (back plane to the front of its render box).
                Vector3 centre = (opening.min + opening.max) * 0.5f;
                float back = Vector3.Dot(centre, front);
                Vector3 local = centre + front * ((Extent(entry, front) - back) * 0.5f + settings.WindowInsertDepth);
                CatalogueEntry insertEntry = placer.Entry(chosen);
                Vector3 insertCentre = (insertEntry.renderMin + insertEntry.renderMax) * 0.5f;
                float yaw = wall.eulerAngles.y + Yaw(front) - Yaw(chosen.fronts[0]);

                placer.PlaceAt(chosen, insertCentre, wall.TransformPoint(local), yaw, group);
            }
        }

        /// <summary>
        /// A wall in a module: its left connection point (the base of its
        /// back plane's left end, seen from in front) at the module's left
        /// end on the frontage's back line (moved forward or back by the
        /// module's depth), facing the street. A wall beyond the size
        /// tolerance of its style's module is first scaled to it (width and
        /// height each). A wall wider or narrower than the module (a storey of
        /// another width than the ground storey) is centred in it, so the
        /// overlap or gap is shared between its sides. Returns the placed
        /// wall, or null.
        /// </summary>
        private static GameObject PlaceWall(PieceAnnotation piece, BuildingStyle style, Run run, ModuleSpot spot, float spacing, float storeyBase,
            float wallSetback, float wallOverlap, PiecePlacer placer, Transform group, float fitWidth = 0f)
        {
            if (piece.fronts.Count != 1 || !Connection(piece, "left", out Vector3 left)) {
                return null;
            }

            // A corner's filler gives its own width, to scale the wall to.
            float widthScale = fitWidth > 0f ? fitWidth / piece.width : style.FitScale(piece.width, style.ModuleWidth);
            float heightScale = style.FitScale(piece.height, style.ModuleHeight);
            float width = piece.width * widthScale;

            Vector2 start = run.Start + run.Along * spot.Position;
            Vector2 end = start + run.Along * spacing;
            // Seen from the street, the viewer's right is up x the way they look.
            Vector3 front = Level(run.Front);
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            Vector2 leftEnd = Vector3.Dot(Level(end - start), right) > 0f ? start : end;
            // A wall of the module's width fills spacing + overlap exactly.
            float centring = (spacing + wallOverlap - width) * 0.5f;
            Vector2 onBackLine = leftEnd + new Vector2(right.x, right.z) * centring + run.Front * (spot.Depth - wallSetback);
            float height = Height(run, spot.Position + spacing * 0.5f) + storeyBase;

            return placer.PlaceAt(piece, left, new Vector3(onBackLine.x, height, onBackLine.y), Yaw(front) - Yaw(piece.fronts[0]),
                PieceScale(piece, widthScale, heightScale), group);
        }

        /// <summary>
        /// A wall's scale along its own axes: widthScale along its width (its
        /// right, seen from in front), heightScale up, its depth unscaled.
        /// </summary>
        private static Vector3 PieceScale(PieceAnnotation piece, float widthScale, float heightScale)
        {
            Vector3 right = Vector3.Cross(Vector3.up, -piece.fronts[0]);
            return Mathf.Abs(right.x) >= Mathf.Abs(right.z) ? new Vector3(widthScale, heightScale, 1f) : new Vector3(1f, heightScale, widthScale);
        }

        /// <summary>
        /// A wall turned side-on where two columns meet: facing face, the end
        /// of its render box (not its connection point, which some pieces'
        /// trim stands past) at corner and the rest running the way back; its
        /// base at height. Its render box's front is on the join (its
        /// thickness behind it, in the column it faces out of), or with
        /// backOnJoin its back plane is (its thickness in front, in the column
        /// it faces into). Scaled up by heightScale (to its storey's height).
        /// Returns its thickness, or 0 if not placed.
        /// </summary>
        private static float PlaceSideWall(PieceAnnotation piece, Vector2 corner, float height, float heightScale, Vector2 face, Vector2 back,
            bool backOnJoin, PiecePlacer placer, Transform group)
        {
            Vector3 front = Level(face);
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            bool runsRight = Vector3.Dot(right, Level(back)) > 0f;
            CatalogueEntry entry = placer.Entry(piece);

            if (entry == null || piece.fronts.Count != 1 || !Connection(piece, runsRight ? "left" : "right", out Vector3 end)) {
                return 0f;
            }

            // In its own space: the connection point's base on the back
            // plane, moved along the wall to the end of its render box (its
            // left end, seen from in front, if it runs to its right).
            Vector3 pieceFront = piece.fronts[0];
            Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
            float endAlong = runsRight ? -Extent(entry, -pieceRight) : Extent(entry, pieceRight);
            Vector3 local = end + pieceRight * (endAlong - Vector3.Dot(end, pieceRight));
            // How far its front stands out from its back plane: with its
            // front on the join, the back plane goes that far in from it.
            float thickness = Extent(entry, pieceFront) - Vector3.Dot(end, pieceFront);
            Vector2 onBackPlane = backOnJoin ? corner : corner - face * thickness;
            placer.PlaceAt(piece, local, new Vector3(onBackPlane.x, height, onBackPlane.y), Yaw(front) - Yaw(pieceFront),
                PieceScale(piece, 1f, heightScale), group);
            return thickness;
        }

        /// <summary>
        /// A foundation turned side-on in a step: facing face, the top of its
        /// front face at height, one end of it at corner and the rest running
        /// the way back.
        /// </summary>
        private static void PlaceSideFoundation(PieceAnnotation piece, Vector2 corner, float height, Vector2 face, Vector2 back,
            PiecePlacer placer, Transform group)
        {
            CatalogueEntry entry = placer.Entry(piece);

            if (entry == null || piece.fronts.Count != 1) {
                return;
            }

            Vector3 front = Level(face);
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            bool runsRight = Vector3.Dot(right, Level(back)) > 0f;
            // In its own space: the end it starts from, along its right
            // (seen from in front), at the top of its front face.
            Vector3 pieceFront = piece.fronts[0];
            Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
            float endAlong = runsRight ? -Extent(entry, -pieceRight) : Extent(entry, pieceRight);
            Vector3 local = pieceRight * endAlong + pieceFront * Extent(entry, pieceFront) + Vector3.up * entry.renderMax.y;

            placer.PlaceAt(piece, local, new Vector3(corner.x, height, corner.y), Yaw(front) - Yaw(pieceFront), group);
        }

        /// <summary>
        /// One foundation per module, centred on it along the street, its
        /// top FoundationHeight above the street and its front
        /// FoundationFrontOffset out past the walls' front line (the edge,
        /// moved forward or back by the module's depth).
        /// </summary>
        private static void BuildFoundations(BuildingStyle ground, List<ModuleSpot> spots, List<Run> runs, GeneratorSettings settings,
            System.Random random, PiecePlacer placer, Transform group)
        {
            PieceAnnotation previous = null;
            float spacing = ground.ModuleSpacing;

            foreach (ModuleSpot spot in spots) {
                PieceAnnotation piece = PiecePlacer.Pick(ground.Pieces("foundation"), previous, random);

                if (piece == null) {
                    return;
                }

                previous = piece;
                CatalogueEntry entry = placer.Entry(piece);

                if (entry == null || piece.fronts.Count != 1) {
                    continue;
                }

                // The middle of the top of its front face, in its own space.
                Vector3 pieceFront = piece.fronts[0];
                Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
                Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
                Vector3 local = pieceRight * Vector3.Dot(centre, pieceRight)
                    + pieceFront * Extent(entry, pieceFront)
                    + Vector3.up * entry.renderMax.y;

                Run run = runs[spot.Run];
                float middle = spot.Position + spacing * 0.5f;
                Vector2 onEdge = run.Start + run.Along * middle + run.Front * (spot.Depth + settings.FoundationFrontOffset);
                float height = Height(run, middle) + settings.FoundationHeight;

                placer.PlaceAt(piece, local, new Vector3(onEdge.x, height, onEdge.y), Yaw(Level(run.Front)) - Yaw(pieceFront), group);
            }
        }

        /// <summary>
        /// The frontage's straight runs, one per segment along its edge: the
        /// edge pushed out from the middle line by half the segment's width,
        /// stopped where it meets the next or last segment's edge on the
        /// inside of a bend, and at half a branching street's width at the
        /// frontage's ends.
        /// </summary>
        private static List<Run> Runs(StreetSpec street, FrontageSpec frontage, float sideSign, List<StreetSpec> streets, GeneratorSettings settings)
        {
            List<Run> runs = new();

            for (int j = frontage.from; j < frontage.to; j++) {
                if (!EdgeLine(street, j, sideSign, settings, out Vector2 lineStart, out Vector2 along, out float length, out Vector2 outward)) {
                    continue;
                }

                float start = 0f;
                float end = length;

                // Inside a bend the edges cross before the point: stop there.
                if (j > frontage.from && EdgeLine(street, j - 1, sideSign, settings, out Vector2 before, out Vector2 beforeAlong, out _, out _)
                    && Intersect(lineStart, along, before, beforeAlong, out float t) && t > 0f) {
                    start = Mathf.Min(t, length);
                }

                if (j < frontage.to - 1 && EdgeLine(street, j + 1, sideSign, settings, out Vector2 after, out Vector2 afterAlong, out _, out _)
                    && Intersect(lineStart, along, after, afterAlong, out t) && t < length) {
                    end = Mathf.Max(t, start);
                }

                // A street branching off at either end of the frontage.
                if (j == frontage.from) {
                    start += LayoutSpecValidator.BranchWidth(street, j, outward, streets, settings) * 0.5f;
                }

                if (j == frontage.to - 1) {
                    end -= LayoutSpecValidator.BranchWidth(street, j + 1, outward, streets, settings) * 0.5f;
                }

                if (end - start < settings.MinSegmentLength) {
                    continue;
                }

                runs.Add(new Run {
                    Start = lineStart + along * start,
                    Along = along,
                    Length = end - start,
                    Front = -outward,
                    StartHeight = Mathf.Lerp(street.points[j].y, street.points[j + 1].y, start / length),
                    EndHeight = Mathf.Lerp(street.points[j].y, street.points[j + 1].y, end / length)
                });
            }

            return runs;
        }

        /// <summary>
        /// A segment's street edge on one side: its start (level with the
        /// segment's first point), direction and length, and the way out from
        /// the street. False for a segment too short to count.
        /// </summary>
        private static bool EdgeLine(StreetSpec street, int segment, float sideSign, GeneratorSettings settings,
            out Vector2 start, out Vector2 along, out float length, out Vector2 outward)
        {
            Vector2 a = Ground(street.points[segment]);
            Vector2 b = Ground(street.points[segment + 1]);
            length = (b - a).magnitude;

            if (length < settings.MinSegmentLength) {
                start = along = outward = Vector2.zero;
                return false;
            }

            along = (b - a) / length;
            outward = new Vector2(-along.y, along.x) * sideSign;
            start = a + outward * (street.SegmentWidth(segment) * 0.5f);
            return true;
        }

        /// <summary>
        /// Where line p + d t meets line q + e u, as t along the first. False
        /// for (nearly) parallel lines.
        /// </summary>
        private static bool Intersect(Vector2 p, Vector2 d, Vector2 q, Vector2 e, out float t)
        {
            float cross = d.x * e.y - d.y * e.x;

            if (Mathf.Abs(cross) < 0.0001f) {
                t = 0f;
                return false;
            }

            Vector2 offset = q - p;
            t = (offset.x * e.y - offset.y * e.x) / cross;
            return true;
        }

        /// <summary>
        /// How far back from the street edge a row of buildings' back line stands:
        /// the deepest any wall it could use stands out in front of its back
        /// plane, so that wall's trim just reaches the edge.
        /// </summary>
        private static float Setback(IEnumerable<BuildingSpec> buildings, Dictionary<string, BuildingStyle> styles, PiecePlacer placer)
        {
            float deepest = 0f;
            HashSet<string> seen = new();

            foreach (BuildingSpec building in buildings) {
                foreach (string name in building.storeys) {
                    BuildingStyle style = Find(styles, name);

                    if (style == null || !seen.Add(name)) {
                        continue;
                    }

                    foreach (string category in BuildingStyle.WallCategories) {
                        foreach (PieceAnnotation piece in style.Modules(category)) {
                            deepest = Mathf.Max(deepest, Reach(piece, placer));
                        }
                    }
                }
            }

            return deepest;
        }

        /// <summary>
        /// How far a wall stands out in front of its back plane (the front
        /// of its render box less its left point's depth), or 0 if it can't
        /// be measured.
        /// </summary>
        private static float Reach(PieceAnnotation piece, PiecePlacer placer)
        {
            CatalogueEntry entry = placer.Entry(piece);

            if (entry == null || piece.fronts.Count != 1 || !Connection(piece, "left", out Vector3 left)) {
                return 0f;
            }

            return Extent(entry, piece.fronts[0]) - Vector3.Dot(left, piece.fronts[0]);
        }

        /// <summary>
        /// How far a piece's render box reaches along a level axis.
        /// </summary>
        private static float Extent(CatalogueEntry entry, Vector3 axis)
        {
            return axis.x * (axis.x > 0f ? entry.renderMax.x : entry.renderMin.x)
                + axis.z * (axis.z > 0f ? entry.renderMax.z : entry.renderMin.z);
        }

        /// <summary>
        /// A piece's connection point by name, in its own space.
        /// </summary>
        private static bool Connection(PieceAnnotation piece, string name, out Vector3 position)
        {
            foreach (ConnectionPoint point in piece.connections) {
                if (point.name == name) {
                    position = point.position;
                    return true;
                }
            }

            position = Vector3.zero;
            return false;
        }

        /// <summary>
        /// A storey's style name (the last listed repeating).
        /// </summary>
        private static string StoreyStyle(BuildingSpec building, int storey)
        {
            return StoreyPlan.StyleOf(building.storeys, storey);
        }

        /// <summary>
        /// A style by name, or null.
        /// </summary>
        private static BuildingStyle Find(Dictionary<string, BuildingStyle> styles, string name)
        {
            return name != null && styles.TryGetValue(name, out BuildingStyle style) ? style : null;
        }

        /// <summary>
        /// The street's height at a distance along a run.
        /// </summary>
        private static float Height(Run run, float position)
        {
            return Mathf.Lerp(run.StartHeight, run.EndHeight, run.Length > 0f ? Mathf.Clamp01(position / run.Length) : 0f);
        }

        /// <summary>
        /// The yaw (degrees clockwise from +Z) of a level direction.
        /// </summary>
        private static float Yaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// A ground direction (x, z) as a level 3D one.
        /// </summary>
        private static Vector3 Level(Vector2 direction)
        {
            return new Vector3(direction.x, 0f, direction.y);
        }

        /// <summary>
        /// A point on the ground: its x and z.
        /// </summary>
        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        /// <summary>
        /// A new empty group under parent.
        /// </summary>
        private static Transform Group(Transform parent, string name)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }
    }
}
