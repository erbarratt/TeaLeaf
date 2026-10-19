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
    /// storey by storey to where the lines meet (a scaled plain wall, or the
    /// end wall widened): the side ending in its walls' trim runs on to the
    /// other's front, the other into its depth, so no corner piece is used.
    /// A frontage end at a junction that meets no other frontage
    /// turns a one-module side return into the branching street. The length
    /// left at a frontage's end extends its last building.
    ///
    /// Lots (stage 5d): a lot's whole building is laid as a closed frontage
    /// of a run per side of its footprint, joined at its corners (LayLot).
    /// No floors: interiors are made by hand.
    /// </summary>
    internal static partial class FacadeBuilder
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
        // The usable plain doors for doorways (any style), set the same way.
        private static readonly List<PieceAnnotation> _doors = new();
        // Each door wall's doorways as seen through it (ThroughOpening), by
        // piece and doorway, measured once per generation.
        private static readonly Dictionary<string, (float min, float max)> _throughOpenings = new();
        // The dressing's pieces by category (pipe-straight, pipe-corner,
        // pipe-end, pipe-fitting, lamp-post): verified and catalogued.
        private static readonly Dictionary<string, List<PieceAnnotation>> _dressing = new();
        // The streets the buildings keep out of, set the same way: runs are
        // cut to where they clear them, side walls and roofs stop short of
        // them, and every piece placed is checked against them.
        private static StreetClearance _clearance;
        // The clearance of the last build, for passes that place after it
        // (GapFiller), checked against every street alike.
        internal static StreetClearance Clearance => _clearance;
        // Where the roofs go, set the same way: a tree of their own beside the
        // facades and lots (Roofs/<frontage or lot>/<building>), so they can
        // be hidden in one go while working on the level.
        private static Transform _roofParent;
        // The streets' flights of steps, set the same way: a run beside one
        // stands its modules on the level of their side of it.
        private static List<StreetSteps.Flight> _flights = new();

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
            // Beside a flight of steps the street is level on either side of
            // it: the run is at StartHeight before StepAt (a distance along
            // it, on a module's end once the modules are laid, StepFlights)
            // and EndHeight from there, instead of sloping between them.
            public bool Stepped;
            public float StepAt;
            // That flight, and how far along its segment the run starts, so
            // the street's surface beside the run can be found (Surface).
            public StreetSteps.Flight Flight;
            public float FlightOffset;
            // The street and segment whose edge it lines (null for a dead end,
            // and for a lot's side that lines no street), for clearance checks.
            public StreetSpec Street;
            public int Segment;
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
            // Its width along the run, where it isn't its building's ground
            // module spacing (0): the last module of a run stretched to its
            // end, or a narrower one added there (FillStretchShare).
            public float Width;
            public float Depth;
            public int Building;
            // The column it is in (of the frontage) and how many storeys
            // that column stands.
            public int Column;
            public int Storeys;
            // Per storey, how far the front wall placed there stands out in
            // front of the back line (0 where none was placed), and the wall
            // piece itself (null where none): shared by copies of the spot,
            // so the storeys' build fills them in and the dressing (pipes,
            // walkways, lamps) reads them.
            public float[] Reach;
            public PieceAnnotation[] Walls;
            // Per storey, how far a corner carries its wall past the run's
            // start or end where that is too short for a filler of its own
            // (WidenCornerEnds): the wall is laid that much wider instead.
            // Null where no corner widens it.
            public float[] WidenStart;
            public float[] WidenEnd;
            // Its index among its building's spec modules, or -1 for a module
            // the generator added (filling a frontage's end, an inside bend's
            // overrun, a side return).
            public int ModuleIndex;
            // Whether it overruns behind another facade (at an inside bend,
            // an inside L or a closed end), part of it hidden: always a plain
            // wall, so no door or window is cut off by the walls in front.
            public bool Overrun;
            // Whether a corner joint closes its run's start or end here, so
            // nothing else needs to.
            public bool JoinedStart;
            public bool JoinedEnd;
            // How far its column's top storey was carried past the run's start
            // or end by a corner (to where the two sides meet), so its roof
            // goes on over it.
            public float CarriedStart;
            public float CarriedEnd;
            // Whether the roof gets a gable at that corner: a facade's corner
            // no wider than a right angle (within CornerAngleTolerance), where
            // the other side's roof doesn't run on over the end.
            public bool GableStart;
            public bool GableEnd;
            // How far out of its start or end the outer face of the side
            // walls closing its top storey stands - the end wall its roof's
            // gable sits on: a corner's, a step's between two columns or a
            // jettied end's. 0 where none was laid there.
            public float SideFaceStart;
            public float SideFaceEnd;
        }

        /// <summary>
        /// A frontage laid out but not yet built: its runs, modules,
        /// buildings and columns, and whether its first or last point is a
        /// junction (another street has a point there).
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
            public bool JunctionAtStart;
            public bool JunctionAtEnd;
            // A lot's whole building: four runs round its footprint, the
            // last joined back to the first (no street, no frontage spec).
            public LotSpec Lot;
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
            // The plain wall marked onePerStorey this storey drew (null for
            // none): its only wall of those so marked (PlainWalls).
            public PieceAnnotation OnePerStorey;
        }

        /// <summary>
        /// A building being built: its spec, ground style, storey count, its
        /// storeys (as many as its tallest column may stand) and group, and
        /// how far its upper storeys jut out (0 for none) on which jetty
        /// pieces, and how far in front of the walls' back line the
        /// supports' backs stand (SetJetty).
        /// </summary>
        private class Building
        {
            public BuildingSpec Spec;
            public BuildingStyle Ground;
            public int Storeys;
            public List<StoreyLevel> Levels;
            public Transform Group;
            public float Jetty;
            public float SupportBack;
            public List<PieceAnnotation> Jetties;
        }

        /// <summary>
        /// Builds every frontage in the spec, in the spec's order, each under
        /// a group of its own in parent, every lot's whole building under
        /// lotParent, then every dead end, and last the roofs over them all
        /// under roofParent. No piece but a roof is placed where it would
        /// stand in a street (StreetClearance). Adds a line per frontage, lot
        /// and dead end to log, and one listing any piece refused.
        /// </summary>
        internal static void Build(LayoutSpec spec, Dictionary<string, BuildingStyle> styles, PieceAnnotations annotations, StoreyPlan plan,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform parent, Transform lotParent, Transform roofParent,
            List<string> log)
        {
            _inserts.Clear();
            _jetties.Clear();
            _doors.Clear();
            _throughOpenings.Clear();
            _dressing.Clear();
            _flights = StreetSteps.Find(spec.streets, styles, placer, settings);
            _clearance = new StreetClearance(spec.streets, _flights, settings);
            _roofParent = roofParent;

            foreach (PieceAnnotation piece in annotations.pieces) {
                if (!piece.verified || piece.excluded || placer.Entry(piece) == null) {
                    continue;
                }

                // The dressing's pieces (pipes, lamp posts), by category.
                if (piece.category.StartsWith("pipe-") || piece.category == "lamp-post") {
                    if (!_dressing.TryGetValue(piece.category, out List<PieceAnnotation> list)) {
                        _dressing[piece.category] = list = new List<PieceAnnotation>();
                    }

                    list.Add(piece);
                    continue;
                }

                if (piece.fronts.Count != 1) {
                    continue;
                }

                if (piece.category == "window") {
                    _inserts.Add(piece);
                } else if (piece.category == "jetty") {
                    _jetties.Add(piece);
                } else if (piece.category == "door" && placer.Entry(piece).gameplayScripts.Count == 0) {
                    // Plain doors only: a gameplay door (Door, locks) is placed by hand.
                    _doors.Add(piece);
                }
            }

            GatherRoofPieces(annotations, placer);

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

            // Lots after the streets' frontages, laid the same way.
            foreach (LotSpec lot in spec.lots) {
                Frontage laid = LayLot(lot, styles, plan, settings, random, placer, lotParent, log);

                if (laid != null) {
                    frontages.Add(laid);
                }
            }

            List<Joint> joints = FindJoints(frontages, spec.streets, settings, log);
            MatchCornerStoreys(frontages, joints, styles, settings, placer, log);
            EqualCornerHeights(joints);
            TrimCornerEnds(joints, settings, placer);
            WidenCornerEnds(joints, settings, placer);
            // From here every piece is checked against the streets as it is
            // placed, against the street its facade faces (Facing) more
            // loosely than the others.
            placer.Clearance = _clearance;

            foreach (Frontage frontage in frontages) {
                _clearance.Facing = frontage.Street;
                BuildFrontage(frontage, settings, random, placer, log);
            }

            HashSet<string> missing = new();

            foreach (Joint joint in joints) {
                BuildJoint(joint, settings, random, placer, missing);
            }

            foreach (string message in missing) {
                log.Add(message);
            }

            List<Frontage> deadEnds = new();

            foreach (StoreyPlan.DeadEnd deadEnd in plan.DeadEnds) {
                _clearance.Facing = deadEnd.Street;
                Frontage built = BuildDeadEnd(deadEnd, styles, plan, settings, random, placer, parent, log);

                if (built != null) {
                    deadEnds.Add(built);
                }
            }

            // Roofs last, over everything built (the corners have carried the
            // top storeys out to where they meet). They overhang the streets
            // above head height, so they aren't checked; their back slopes
            // stop short of other streets themselves.
            placer.Clearance = null;
            frontages.AddRange(deadEnds);
            BuildRoofs(frontages, joints, settings, random, placer, log);
            // The dressing stands in the street side of the walls (a pipe's
            // thickness, a lamp post in the street itself) by design, so it
            // is not held to the clearance; the street test still judges it.
            placer.Clearance = null;
            BuildWalkways(frontages, settings, random, placer, log);
            BuildPipes(frontages, settings, random, placer, log);
            BuildVents(frontages, settings, random, placer, log);
            BuildLamps(frontages, Group(parent.parent, "Lamps"), settings, random, placer, log);
            // Last of all, with every piece of every building in place: one
            // colour per building (per set of corner-joined buildings),
            // walls and roof.
            ColourBuildings(frontages, joints, random, log);

            if (_clearance.Refused.Count > 0) {
                log.Add($"Street clearance: {_clearance.Refused.Count} piece(s) not placed because they would stand in a street:\n    "
                    + string.Join("\n    ", _clearance.Refused));
            }
        }

        /// <summary>
        /// The group a building's roofs go under, in the roofs' own tree:
        /// Roofs/&lt;its frontage, lot or dead end&gt;/&lt;the building&gt;,
        /// each made the first time it is asked for.
        /// </summary>
        private static Transform RoofGroup(Building building)
        {
            Transform owner = Child(_roofParent, building.Group.parent.name);
            return Child(owner, building.Group.name);
        }

        /// <summary>
        /// A parent's child by name, made if it has none.
        /// </summary>
        private static Transform Child(Transform parent, string name)
        {
            foreach (Transform child in parent) {
                if (child.name == name) {
                    return child;
                }
            }

            return Group(parent, name);
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

            // How far the walls reach back from the street edge; the runs are
            // cut to where that, or the foundations if they reach further
            // back, stands in no other street.
            float wallSetback = Setback(frontage.buildings, styles, placer);
            float reachBack = Mathf.Max(wallSetback, FoundationBack(frontage.buildings, styles, settings));
            List<Run> runs = Runs(street, frontage, sideSign, wallSetback, reachBack, Overhang(frontage.buildings, styles, placer), streets, settings,
                out bool insideStart, out bool insideEnd);

            if (runs.Count == 0) {
                log.Add($"{label} not built: no stretch of its edge is long enough.");
                return null;
            }

            // Seen from the street, a left frontage's buildings run left to
            // right along the street, a right frontage's right to left.
            bool leftToRight = sideSign > 0f;
            Transform frontageGroup = Group(parent, $"{street.id} {frontage.side} {frontage.from}-{frontage.to}");
            List<ModuleSpot> spots = new();
            List<Building> buildings = new();
            int runIndex = 0;
            float position = 0f;
            bool full = false;

            // Where every module stands, in order along the street.
            for (int b = 0; b < frontage.buildings.Count && !full; b++) {
                BuildingSpec spec = frontage.buildings[b];
                Building building = MakeBuilding(spec, plan.Storeys(spec), styles, settings, random, placer, frontageGroup, $"Building {b}",
                    $"{label}, building {b}", log);

                if (building == null) {
                    log.Add($"{label}: building {b} and the rest not built: its ground storey's style has no module yet.");
                    break;
                }

                float spacing = building.Ground.ModuleSpacing;
                int laid = 0;

                for (int k = 0; k < spec.widthModules; k++) {
                    if (position + spacing > runs[runIndex].Length + settings.SizeTolerance) {
                        // The length left on this run is closed by the rule
                        // before the modules go on along the next.
                        FillRunEnd(spots, runs[runIndex], runIndex, ref position, buildings.Count, building.Levels.Count, spacing, buildings, settings);

                        if (runIndex == runs.Count - 1) {
                            // Past the frontage's end: the rest would stand in
                            // the street beyond it.
                            log.Add($"{label}: building {b} stops at module {k} of {spec.widthModules} and the rest are left out: "
                                + "the frontage's stretch of street is full.");
                            full = true;
                            break;
                        }

                        NextRun(ref runIndex, ref position);
                    }

                    spots.Add(new ModuleSpot {
                        Run = runIndex, Position = position, Building = buildings.Count, Reach = new float[building.Levels.Count], Walls = new PieceAnnotation[building.Levels.Count], ModuleIndex = k
                    });
                    position += spacing;
                    laid++;
                }

                if (laid > 0) {
                    buildings.Add(building);
                }
            }

            if (!full && buildings.Count == frontage.buildings.Count) {
                bool closedEnd = insideEnd || plan.DeadEnds.Exists(end => end.Street == street && end.Point == frontage.to);
                ExtendLastBuilding(spots, buildings, runs, runIndex, position, closedEnd, settings);
            }

            if (spots.Count == 0) {
                log.Add($"{label} not built: no building could be laid.");
                return null;
            }

            // Where the first run starts at an inside corner, or at a dead
            // end, the first building overruns back past it by a module,
            // behind the other street's facade or the dead end's, so the
            // corner behind the two is closed (as ExtendLastBuilding does at
            // a closed end).
            bool closedStart = insideStart || plan.DeadEnds.Exists(end => end.Street == street && end.Point == frontage.from);

            if (closedStart && spots[0].Run == 0) {
                Building first = buildings[0];
                spots.Insert(0, new ModuleSpot {
                    Run = 0, Position = -first.Ground.ModuleSpacing, Building = 0, Reach = new float[first.Levels.Count], Walls = new PieceAnnotation[first.Levels.Count], ModuleIndex = -1,
                    Overrun = true
                });
            }

            StepFlights(spots, runs, buildings, settings);
            List<Column> columns = SplitColumns(spots, runs, buildings, MaxColumnDepth(buildings, settings), settings, random);

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
                JunctionAtStart = AtJunction(street, frontage.from, streets, settings),
                JunctionAtEnd = AtJunction(street, frontage.to, streets, settings)
            };
        }

        /// <summary>
        /// A building of a spec with its rolled storey count, under a group
        /// of its own: its storeys made for its tallest possible column, and
        /// its jetty rolled at JettyShare (always drawn) - jettied where it
        /// may have an upper storey and a jetty piece fits its module. Null if
        /// its ground storey's style has no module yet.
        /// </summary>
        private static Building MakeBuilding(BuildingSpec spec, int storeys, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings,
            System.Random random, PiecePlacer placer, Transform parent, string name, string label, List<string> log)
        {
            BuildingStyle ground = Find(styles, spec.storeys.Count > 0 ? spec.storeys[0] : null);

            if (storeys == 0 || ground == null || ground.ModuleSpacing <= 0f) {
                return null;
            }

            // A style without a module may stop the list short of even its
            // own count.
            int most = Mathf.Max(storeys, Mathf.Min(storeys + settings.ColumnStoreyVariation, settings.MaxStoreys));
            bool jettyDrawn = random.NextDouble() < settings.JettyShare;
            List<PieceAnnotation> jetties = _jetties.FindAll(piece => ground.Fits(piece.width, ground.ModuleWidth));
            bool jettied = jettyDrawn && most > 1 && jetties.Count > 0;
            List<StoreyLevel> levels = StoreyLevels(spec, most, styles, settings, jettied, label, log);

            Building building = new() {
                Spec = spec,
                Ground = ground,
                Storeys = Mathf.Min(storeys, levels.Count),
                Levels = levels,
                Group = Group(parent, name),
                Jetties = jetties
            };
            SetJetty(building, jettied, settings, placer);
            return building;
        }

        /// <summary>
        /// Lays a lot's whole building out as a closed frontage: one building
        /// of the lot's styles and rolled storeys, and a run along each side
        /// of its footprint (LotSpec.Corners: the outline, or the rectangle of
        /// its modules), in order round it from the front side, each facing
        /// out; the footprint is the line the walls' fronts reach. Along each
        /// side as many whole modules as fit are laid from its start (the
        /// corners fill the rest), the spec's door counted along the front
        /// from the left as seen from in front; then its columns. Its base
        /// stands at the lot's origin height. Null if it can't be built
        /// (logged).
        /// </summary>
        private static Frontage LayLot(LotSpec lot, Dictionary<string, BuildingStyle> styles, StoreyPlan plan, GeneratorSettings settings,
            System.Random random, PiecePlacer placer, Transform parent, List<string> log)
        {
            string label = $"Lot '{lot.id}'";
            BuildingSpec spec = new() { widthModules = lot.widthModules, storeys = lot.storeys, doorModule = lot.doorModule };
            Building building = MakeBuilding(spec, plan.Storeys(lot), styles, settings, random, placer, Group(parent, $"Lot {lot.id}"), "Building 0",
                label, log);

            if (building == null || (!lot.HasOutline && (lot.widthModules < 1 || lot.depthModules < 1))) {
                log.Add($"{label} not built: its ground storey's style has no module yet, or it has no modules.");
                return null;
            }

            float spacing = building.Ground.ModuleSpacing;
            List<Vector2> corners = lot.Corners(spacing, settings.WallOverlap);
            // Twice the signed area: above 0 the corners go anticlockwise
            // (seen from above, x right and z up), so outward is to the right
            // of each side.
            float area = 0f;

            for (int i = 0; i < corners.Count; i++) {
                Vector2 a = corners[i];
                Vector2 b = corners[(i + 1) % corners.Count];
                area += a.x * b.y - b.x * a.y;
            }

            // The street level the ground storey's base stands above.
            float street = lot.origin.y - settings.GroundStoreyHeight;
            List<Run> runs = new();
            List<ModuleSpot> spots = new();

            for (int i = 0; i < corners.Count; i++) {
                Vector2 side = corners[(i + 1) % corners.Count] - corners[i];
                Vector2 along = side.normalized;
                Vector2 outward = area > 0f ? new Vector2(along.y, -along.x) : new Vector2(-along.y, along.x);
                int modules = Mathf.FloorToInt((side.magnitude - settings.WallOverlap) / spacing + 0.001f);

                if (modules < 1) {
                    log.Add($"{label} not built: side {i} is too short for one module.");
                    return null;
                }

                // The street the side lines, where it lines one: its columns
                // may step forward into that street and no other, as a
                // facade's may (ColumnClear).
                _clearance.LiningSegment(corners[i], corners[(i + 1) % corners.Count], outward, out StreetSpec lined, out int segment);
                runs.Add(new Run {
                    Start = corners[i], Along = along, Length = side.magnitude, Front = outward, StartHeight = street, EndHeight = street,
                    Street = lined, Segment = segment
                });

                for (int k = 0; k < modules; k++) {
                    spots.Add(new ModuleSpot {
                        Run = i, Position = k * spacing, Building = 0, Reach = new float[building.Levels.Count], Walls = new PieceAnnotation[building.Levels.Count], ModuleIndex = i == 0 ? k : -1
                    });
                }

                if (i == 0) {
                    spec.widthModules = modules;
                }
            }

            List<Building> buildings = new() { building };
            List<Column> columns = SplitColumns(spots, runs, buildings, MaxColumnDepth(buildings, settings), settings, random);
            // Seen from in front of the front side, the viewer's right.
            Vector2 viewerRight = new(-runs[0].Front.y, runs[0].Front.x);

            return new Frontage {
                Label = label,
                Runs = runs,
                Spots = spots,
                Buildings = buildings,
                Columns = columns,
                WallSetback = Setback(new[] { spec }, styles, placer),
                LeftToRight = Vector2.Dot(runs[0].Along, viewerRight) > 0f,
                Lot = lot
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
            // A lot's sides line the streets round it or stand in open ground
            // (its outline given by hand); the count shows which.
            string lining = frontage.Lot != null ? $" ({frontage.Runs.FindAll(run => run.Street != null).Count} lining a street)" : "";
            log.Add($"{frontage.Label}: {frontage.Buildings.Count} building(s), {frontage.Runs.Count} run(s){lining}, {frontage.Columns.Count} column(s) "
                + $"({stepped} off the line), {placer.Placed - pieces} piece(s).");
        }

        /// <summary>
        /// Every corner where runs meet on the outside: each bend of a
        /// frontage that turns away from its street, and each pair of
        /// frontage ends of different streets at a junction that can meet at
        /// a street corner (each carried no further than CornerMaxReach, the
        /// shortest pairs first). A frontage end at a junction left unpaired
        /// turns a side return into a street branching off towards its side, if
        /// one does, joined to it. Lots join their last side to their first.
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

                // A lot's last side turns back into its first.
                if (frontage.Lot != null) {
                    int last = frontage.Spots.FindLastIndex(spot => spot.Run == frontage.Runs.Count - 1);
                    int first = frontage.Spots.FindIndex(spot => spot.Run == 0);

                    if (last >= 0 && first >= 0) {
                        joints.Add(Join(new RunEnd { Frontage = frontage, Spot = last }, new RunEnd { Frontage = frontage, Spot = first, AtStart = true }));
                        bends++;
                    }
                }
            }

            // The frontage ends that stop at a junction.
            List<RunEnd> ends = new();

            foreach (Frontage frontage in frontages) {

                if (frontage.JunctionAtStart) {
                    ends.Add(new RunEnd { Frontage = frontage, Spot = 0, AtStart = true });
                }

                if (frontage.JunctionAtEnd) {
                    ends.Add(new RunEnd { Frontage = frontage, Spot = frontage.Spots.Count - 1 });
                }
            }

            List<(float reach, int a, int b)> pairs = new();

            for (int i = 0; i < ends.Count; i++) {
                for (int j = i + 1; j < ends.Count; j++) {
                    if (ends[i].Frontage.Street == ends[j].Frontage.Street) {
                        continue;
                    }

                    if (Outside(ends[i], ends[j]) && Meet(ends[i], 0f, ends[j], 0f, out float reachA, out float reachB, out _)
                        && reachA >= -settings.SizeTolerance && reachB >= -settings.SizeTolerance && reachA <= settings.CornerMaxReach
                        && reachB <= settings.CornerMaxReach) {
                        pairs.Add((reachA + reachB, i, j));
                    } else if (InLine(ends[i], ends[j], settings, out float gap)) {
                        // One street running on nearly straight into another:
                        // the ends are joined across the gap between them.
                        pairs.Add((gap, i, j));
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
        /// Whether two run ends meet on the outside of a corner (the way out
        /// of one end points the way the other's facade faces); an inside
        /// corner is closed by its runs overrunning instead.
        /// </summary>
        private static bool Outside(RunEnd a, RunEnd b)
        {
            EndLine(a, 0f, out _, out Vector2 outA, out _, out _);
            Run runB = b.Frontage.Runs[b.Frontage.Spots[b.Spot].Run];
            return Vector2.Dot(outA, runB.Front) > 0f;
        }

        /// <summary>
        /// The sets of buildings joined by corners: for every building, the
        /// first (in the spec's order) of the set it belongs to - itself when
        /// no corner joins it to an earlier building. A set shares its
        /// storeys (MatchCornerStoreys) and its colour (ColourBuildings).
        /// </summary>
        private static Dictionary<Building, Building> CornerLeaders(List<Frontage> frontages, List<Joint> joints)
        {
            // Every building in the spec's order, each its own set at first.
            List<Building> order = new();

            foreach (Frontage frontage in frontages) {
                order.AddRange(frontage.Buildings);
            }

            Dictionary<Building, Building> leader = new();

            foreach (Building building in order) {
                leader[building] = building;
            }

            Building Root(Building building)
            {
                while (leader[building] != building) {
                    building = leader[building];
                }

                return building;
            }

            foreach (Joint joint in joints) {
                Building a = Root(joint.A.Frontage.Buildings[joint.A.Frontage.Spots[joint.A.Spot].Building]);
                Building b = Root(joint.B.Frontage.Buildings[joint.B.Frontage.Spots[joint.B.Spot].Building]);

                if (a == b) {
                    continue;
                }

                // The one earlier in the spec leads the set.
                if (order.IndexOf(b) < order.IndexOf(a)) {
                    leader[a] = b;
                } else {
                    leader[b] = a;
                }
            }

            // Flattened: each building straight to its set's first.
            Dictionary<Building, Building> firsts = new();

            foreach (Building building in order) {
                firsts[building] = Root(building);
            }

            return firsts;
        }

        /// <summary>
        /// A corner is the corner of one building, so the buildings either
        /// side of every joint take the same storeys: each set of buildings
        /// joined by corners takes the storey styles and jetty of its first
        /// (in the spec's order), so their storeys stand at the same heights.
        /// A building keeps its own module spacing, foundations and door; one
        /// whose ground storey's module is more than MaxStoreyWidthMismatch
        /// off the first's keeps its own storeys (logged). Its storeys are
        /// made again (StoreyLevels), no more of them than it had, and its
        /// modules' storey counts kept within them.
        /// </summary>
        private static void MatchCornerStoreys(List<Frontage> frontages, List<Joint> joints, Dictionary<string, BuildingStyle> styles,
            GeneratorSettings settings, PiecePlacer placer, List<string> log)
        {
            Dictionary<Building, Building> leaders = CornerLeaders(frontages, joints);
            int matched = 0;
            List<string> kept = new();

            foreach (Frontage frontage in frontages) {
                for (int i = 0; i < frontage.Buildings.Count; i++) {
                    Building building = frontage.Buildings[i];
                    Building first = leaders[building];

                    if (first == building) {
                        continue;
                    }

                    if (Mathf.Abs(first.Ground.ModuleSpacing - building.Ground.ModuleSpacing) > settings.MaxStoreyWidthMismatch) {
                        kept.Add($"{frontage.Label}, building {i}");
                        continue;
                    }

                    bool jettied = first.Jetty > 0f && building.Jetties != null && building.Jetties.Count > 0;
                    BuildingSpec spec = new() { widthModules = building.Spec.widthModules, storeys = first.Spec.storeys, doorModule = building.Spec.doorModule };
                    List<StoreyLevel> levels = StoreyLevels(spec, building.Levels.Count, styles, settings, jettied, $"{frontage.Label}, building {i}", log);

                    if (levels.Count == 0) {
                        kept.Add($"{frontage.Label}, building {i}");
                        continue;
                    }

                    building.Levels = levels;
                    building.Storeys = Mathf.Min(building.Storeys, levels.Count);
                    SetJetty(building, jettied, settings, placer);

                    for (int s = 0; s < frontage.Spots.Count; s++) {
                        ModuleSpot spot = frontage.Spots[s];

                        if (spot.Building == i && spot.Storeys > levels.Count) {
                            spot.Storeys = levels.Count;
                            frontage.Spots[s] = spot;
                        }
                    }

                    matched++;
                }
            }

            log.Add($"Corner storeys: {matched} building(s) given the storeys of the building across a corner"
                + (kept.Count > 0 ? $"; kept their own (module too far off): {string.Join(", ", kept)}." : "."));
        }

        /// <summary>
        /// Convex corners stand level: the columns either side of every joint
        /// (bends, street corners, a lot's corners) are given the same storey
        /// count, the lower of the two, every module of each column with it -
        /// repeated until no joint changes, since a column can end at two.
        /// </summary>
        private static void EqualCornerHeights(List<Joint> joints)
        {
            bool changed = true;

            while (changed) {
                changed = false;

                foreach (Joint joint in joints) {
                    int storeys = Mathf.Min(joint.A.Frontage.Spots[joint.A.Spot].Storeys, joint.B.Frontage.Spots[joint.B.Spot].Storeys);
                    changed |= SetColumnStoreys(joint.A, storeys);
                    changed |= SetColumnStoreys(joint.B, storeys);
                }
            }
        }

        /// <summary>
        /// Where a corner's two sides already reach past each other - the
        /// run ends on the bend's bisector, but the walls' lines at their
        /// columns' depths meet behind an end (a shallow outside bend with a
        /// column stepped forward) - the walls would cross, each end face
        /// poking out through the other's face. So before anything is built
        /// each end module is cut back to where its side should stop (as the
        /// corner's fillers carry it forward otherwise, BuildCornerSide): the
        /// covering side to the corner, the other just past the covering
        /// side's back line; never narrower than FillStretchShare of a
        /// module. Measured on the ground storey (the jetty's storeys stand
        /// further forward).
        /// </summary>
        private static void TrimCornerEnds(List<Joint> joints, GeneratorSettings settings, PiecePlacer placer)
        {
            foreach (Joint joint in joints) {
                bool firstCovers = FirstCovers(joint);
                TrimCornerEnd(joint.A, joint.B, firstCovers, settings, placer);
                TrimCornerEnd(joint.B, joint.A, !firstCovers, settings, placer);
            }
        }

        /// <summary>
        /// Which side of a corner covers it (runs on to the other side's
        /// front): the side whose walls end there in their trim - seen from
        /// the street a wall's trim runs down its left side, and a trimmed
        /// end is closed where a plain end is hollow - so the trim stands as
        /// the corner's post. The first side where both or neither do.
        /// </summary>
        private static bool FirstCovers(Joint joint)
        {
            return TrimmedEnd(joint.A) || !TrimmedEnd(joint.B);
        }

        /// <summary>
        /// Whether a run end is its walls' left end seen from the street,
        /// where their trim is.
        /// </summary>
        private static bool TrimmedEnd(RunEnd end)
        {
            EndLine(end, 0f, out _, out Vector2 outward, out Run run, out _);
            return Vector2.Dot(outward, new Vector2(run.Front.y, -run.Front.x)) > 0f;
        }

        /// <summary>
        /// A corner side's walls carried less than FillStretchShare of a
        /// module past its end are laid as the end module's own wall widened
        /// to reach there (ModuleSpot.WidenStart / WidenEnd, read as the
        /// storeys are built), not as a sliver of a filler beside it: per
        /// storey, since each storey's line meets the other side elsewhere.
        /// </summary>
        private static void WidenCornerEnds(List<Joint> joints, GeneratorSettings settings, PiecePlacer placer)
        {
            foreach (Joint joint in joints) {
                bool firstCovers = FirstCovers(joint);
                WidenCornerEnd(joint.A, joint.B, firstCovers, settings, placer);
                WidenCornerEnd(joint.B, joint.A, !firstCovers, settings, placer);
            }
        }

        private static void WidenCornerEnd(RunEnd me, RunEnd other, bool cover, GeneratorSettings settings, PiecePlacer placer)
        {
            List<ModuleSpot> spots = me.Frontage.Spots;
            ModuleSpot spot = spots[me.Spot];
            Building building = me.Frontage.Buildings[spot.Building];

            for (int s = 0; s < spot.Storeys; s++) {
                float walls = CornerWalls(me, other, cover, s, settings, placer);

                if (walls <= settings.SizeTolerance || walls > building.Levels[s].Style.ModuleSpacing * settings.FillStretchShare) {
                    continue;
                }

                if (me.AtStart) {
                    spot.WidenStart ??= new float[building.Levels.Count];
                    spot.WidenStart[s] = walls;
                } else {
                    spot.WidenEnd ??= new float[building.Levels.Count];
                    spot.WidenEnd[s] = walls;
                }
            }

            spots[me.Spot] = spot;
        }

        /// <summary>
        /// Whether a corner widens a run end's wall at a storey (so it gets
        /// no filler wall).
        /// </summary>
        private static bool Widened(RunEnd end, int storey)
        {
            ModuleSpot spot = end.Frontage.Spots[end.Spot];
            float[] widen = end.AtStart ? spot.WidenStart : spot.WidenEnd;
            return widen != null && widen[storey] > 0f;
        }

        /// <summary>
        /// How far a corner side's walls are carried out of its end at a
        /// storey (BuildCornerSide's carry, for its walls): nearly in line to
        /// its run's end and half the gap on (InLineCarry); else as far as
        /// WallTarget takes it, no further than the corner, short of it where
        /// the corner is sharp (CarryToBack); 0 where the lines meet behind
        /// the end.
        /// </summary>
        private static float CornerWalls(RunEnd me, RunEnd other, bool cover, int s, GeneratorSettings settings, PiecePlacer placer)
        {
            ModuleSpot spot = me.Frontage.Spots[me.Spot];
            ModuleSpot otherSpot = other.Frontage.Spots[other.Spot];
            Building building = me.Frontage.Buildings[spot.Building];
            Building otherBuilding = other.Frontage.Buildings[otherSpot.Building];
            int otherStorey = Mathf.Min(s, otherSpot.Storeys - 1);
            float depth = spot.Depth + StoreyJetty(building, s);
            float otherDepth = otherSpot.Depth + StoreyJetty(otherBuilding, otherStorey);

            bool met = Meet(me, depth, other, otherDepth, out float reach, out float otherReach, out _);

            if (!met || Mathf.Abs(reach) > settings.CornerMaxReach || Mathf.Abs(otherReach) > settings.CornerMaxReach) {
                return InLineCarry(me, depth, other, otherDepth);
            }

            if (reach < -settings.SizeTolerance) {
                return 0f;
            }

            WallTarget(me, s, depth, other, otherStorey, otherDepth, cover, settings, placer, out _, out float wallReach);

            if (CarryToBack(me, depth, me.Frontage.WallSetback, other, otherDepth, reach, settings, out float backReach)) {
                reach = backReach;
                wallReach = backReach;
            }

            return Mathf.Max(0f, Mathf.Min(wallReach, Mathf.Max(0f, reach)));
        }

        private static void TrimCornerEnd(RunEnd me, RunEnd other, bool cover, GeneratorSettings settings, PiecePlacer placer)
        {
            List<ModuleSpot> spots = me.Frontage.Spots;
            ModuleSpot spot = spots[me.Spot];
            ModuleSpot otherSpot = other.Frontage.Spots[other.Spot];
            Building building = me.Frontage.Buildings[spot.Building];
            Building otherBuilding = other.Frontage.Buildings[otherSpot.Building];
            // The storey that must stop soonest sets the module's end; the
            // others are carried on from it by the corner's fillers.
            float soonest = float.MaxValue;

            for (int s = 0; s < spot.Storeys; s++) {
                int otherStorey = Mathf.Min(s, otherSpot.Storeys - 1);
                float depth = spot.Depth + StoreyJetty(building, s);
                float otherDepth = otherSpot.Depth + StoreyJetty(otherBuilding, otherStorey);

                if (WallTarget(me, s, depth, other, otherStorey, otherDepth, cover, settings, placer, out float reach, out float wallReach)
                    && Mathf.Abs(reach) <= settings.CornerMaxReach) {
                    soonest = Mathf.Min(soonest, wallReach);
                }
            }

            if (soonest >= -settings.SizeTolerance) {
                return;
            }

            float width = Spacing(spot, building);
            float trimmed = Mathf.Max(width + soonest, building.Ground.ModuleSpacing * settings.FillStretchShare);

            if (trimmed >= width - settings.SizeTolerance) {
                return;
            }

            if (me.AtStart) {
                spot.Position += width - trimmed;
            }

            spot.Width = trimmed;
            spots[me.Spot] = spot;
        }

        /// <summary>
        /// How far out of its end a corner side's walls go at one storey
        /// (each side's line at its depth): reach is to the corner, where
        /// the two lines meet (below 0 behind the end), and wallReach where
        /// the walls stop. Each side's end stops CornerOverrun of the way
        /// from the other side's back line to its face (FaceLine), less the
        /// overlap its last wall reaches past its module: hidden inside the
        /// other wall - behind its plaster, in front of its back - so the two
        /// walls run into each other and their end posts meet at the corner
        /// (below 0 where the end module already passes that, so it is
        /// trimmed). A wall's end face runs
        /// from its back line to its face, and off a right angle one corner
        /// of it reaches the line first: the end stops where that corner
        /// does (EndReach), so no corner of it passes the line. False for
        /// parallel lines.
        /// </summary>
        private static bool WallTarget(RunEnd me, int storey, float depth, RunEnd other, int otherStorey, float otherDepth, bool cover,
            GeneratorSettings settings, PiecePlacer placer, out float reach, out float wallReach)
        {
            wallReach = 0f;

            if (!Meet(me, depth, other, otherDepth, out reach, out _, out _)) {
                return false;
            }

            // The walls' fronts stand behind the frontages' lines where a
            // storey's walls are shallower than the deepest a frontage could
            // use (FaceLine).
            float face = FaceLine(me, storey, depth, placer);
            float back = depth - me.Frontage.WallSetback;
            float otherBack = otherDepth - other.Frontage.WallSetback;
            float line = otherBack + (FaceLine(other, otherStorey, otherDepth, placer) - otherBack) * settings.CornerOverrun;

            if (!EndReach(me, face, back, other, line, out wallReach)) {
                wallReach = reach;
                return true;
            }

            // Its last wall reaches the overlap past where it stops, as every
            // wall does past its module.
            wallReach -= settings.WallOverlap;
            return true;
        }

        /// <summary>
        /// How far the other side's end wall at a storey stands out from its
        /// back line (Reach), so a corner's filler can be brought forward to
        /// the same face; 0 where no wall stands there.
        /// </summary>
        private static float CornerFaceReach(RunEnd other, int otherStorey, PiecePlacer placer)
        {
            ModuleSpot spot = other.Frontage.Spots[other.Spot];
            PieceAnnotation wall = spot.Walls != null && otherStorey < spot.Walls.Length ? spot.Walls[otherStorey] : null;
            return wall != null ? Reach(wall, placer) : 0f;
        }

        /// <summary>
        /// How far a run end's walls are carried out of it for their end
        /// face - from the back line to the face - to reach a line of the
        /// other side's: the sooner of its two corners' reaches (the front
        /// corner's at a corner wider than a right angle, the back corner's
        /// at a sharper one). False for parallel lines.
        /// </summary>
        private static bool EndReach(RunEnd me, float face, float back, RunEnd other, float line, out float reach)
        {
            if (!Meet(me, face, other, line, out reach, out _, out _)) {
                return false;
            }

            if (Meet(me, back, other, line, out float backReach, out _, out _)) {
                reach = Mathf.Min(reach, backReach);
            }

            return true;
        }

        /// <summary>
        /// A run end's line at a storey where its walls' fronts stand (depth
        /// is the frontage's line, which the deepest wall it could use
        /// reaches): its back line plus the shallowest of the storey style's
        /// walls (Reach), so a corner's walls stop at or a little short of
        /// the face they meet, never past it. The frontage's line if none can
        /// be measured.
        /// </summary>
        private static float FaceLine(RunEnd end, int storey, float depth, PiecePlacer placer)
        {
            Building building = end.Frontage.Buildings[end.Frontage.Spots[end.Spot].Building];
            float face = float.MaxValue;

            foreach (string category in BuildingStyle.WallCategories) {
                foreach (PieceAnnotation piece in building.Levels[storey].Style.Modules(category, storey)) {
                    float reach = Reach(piece, placer);

                    if (reach > 0f) {
                        face = Mathf.Min(face, reach);
                    }
                }
            }

            return face < float.MaxValue ? depth - end.Frontage.WallSetback + Mathf.Min(face, end.Frontage.WallSetback) : depth;
        }

        /// <summary>
        /// Gives every module of a run end's column a storey count; whether
        /// anything changed.
        /// </summary>
        private static bool SetColumnStoreys(RunEnd end, int storeys)
        {
            List<ModuleSpot> spots = end.Frontage.Spots;
            int column = spots[end.Spot].Column;
            bool changed = false;

            for (int i = 0; i < spots.Count; i++) {
                if (spots[i].Column != column || spots[i].Storeys == storeys) {
                    continue;
                }

                ModuleSpot spot = spots[i];
                spot.Storeys = storeys;
                spots[i] = spot;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Whether two run ends face each other nearly in line (one street
        /// running on nearly straight into another, within the branch angle):
        /// their ways out nearly opposite, their facades facing the same way,
        /// each end ahead of the other and no further apart than
        /// CornerMaxReach. Gap is how far apart they are.
        /// </summary>
        private static bool InLine(RunEnd a, RunEnd b, GeneratorSettings settings, out float gap)
        {
            EndLine(a, 0f, out Vector2 pointA, out Vector2 outA, out Run runA, out _);
            EndLine(b, 0f, out Vector2 pointB, out Vector2 outB, out Run runB, out _);
            float straight = Mathf.Cos(Mathf.Asin(settings.BranchDot));
            gap = (pointB - pointA).magnitude;
            return Vector2.Dot(outA, outB) < -straight && Vector2.Dot(runA.Front, runB.Front) > straight
                && Vector2.Dot(pointB - pointA, outA) > -settings.SizeTolerance && Vector2.Dot(pointA - pointB, outB) > -settings.SizeTolerance
                && gap <= settings.CornerMaxReach;
        }

        /// <summary>
        /// Whether a street's point is a junction: another street has a point
        /// within JunctionDistance of it.
        /// </summary>
        private static bool AtJunction(StreetSpec street, int pointIndex, List<StreetSpec> streets, GeneratorSettings settings)
        {
            Vector2 point = Ground(street.points[pointIndex]);

            foreach (StreetSpec other in streets) {
                if (other == street) {
                    continue;
                }

                foreach (Vector3 otherPoint in other.points) {
                    if ((Ground(otherPoint) - point).magnitude <= settings.JunctionDistance) {
                        return true;
                    }
                }
            }

            return false;
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
        /// A module's width along its run: its own, or its building's ground
        /// module spacing.
        /// </summary>
        private static float Spacing(ModuleSpot spot, Building building)
        {
            return spot.Width > 0f ? spot.Width : building.Ground.ModuleSpacing;
        }

        private static float Spacing(ModuleSpot spot, List<Building> buildings)
        {
            return Spacing(spot, buildings[spot.Building]);
        }

        /// <summary>
        /// The widths of the pieces that cover a length end to end, each
        /// overlapping the next by overlap, when a piece is normally normal
        /// wide (FillStretchShare): as many whole pieces as fit, then the
        /// length left - under the share of a whole piece's advance, the last
        /// piece is widened to take it; otherwise one more piece, scaled down
        /// to it, is added. A length under one piece gets one piece of that
        /// width. Nothing for a length under SizeTolerance.
        /// </summary>
        private static List<float> FitWidths(float length, float normal, float overlap, GeneratorSettings settings)
        {
            List<float> widths = new();

            if (length <= settings.SizeTolerance) {
                return widths;
            }

            float step = normal - overlap;
            // Each piece advances its width less the overlap; the last one's
            // overlap is not shared, so the advances sum to the length less
            // one overlap.
            float advance = length - overlap;

            if (step <= 0f || advance <= step * settings.FillStretchShare) {
                widths.Add(length);
                return widths;
            }

            int whole = Mathf.FloorToInt(advance / step + 0.0001f);
            float left = Mathf.Max(0f, advance - whole * step);

            for (int i = 0; i < whole; i++) {
                widths.Add(normal);
            }

            if (left <= settings.SizeTolerance) {
                return widths;
            }

            if (left < step * settings.FillStretchShare) {
                widths[whole - 1] = normal + left;
            } else {
                widths.Add(left + overlap);
            }

            return widths;
        }

        /// <summary>
        /// Closes the length left at the end of a run after its modules
        /// (from position to the run's end) by the rule (FitWidths): under
        /// FillStretchShare of a module, the run's last module is widened to
        /// the end; otherwise a narrower module is added there (a plain wall,
        /// of the same building as the module before it, or of building when
        /// the run has none - its storey arrays sized to that building's
        /// levels, levels standing in for a building not in the list yet).
        /// Moves position to the run's end.
        /// </summary>
        private static void FillRunEnd(List<ModuleSpot> spots, Run run, int runIndex, ref float position, int building, int levels, float spacing,
            List<Building> buildings, GeneratorSettings settings)
        {
            float left = run.Length - position;

            if (left <= settings.SizeTolerance) {
                return;
            }

            int last = spots.FindLastIndex(spot => spot.Run == runIndex);

            if (last >= 0 && left < (spacing - settings.WallOverlap) * settings.FillStretchShare) {
                ModuleSpot widened = spots[last];
                widened.Width = Spacing(widened, spacing) + left;
                spots[last] = widened;
            } else {
                int owner = last >= 0 ? spots[last].Building : building;
                int ownerLevels = owner < buildings.Count ? buildings[owner].Levels.Count : levels;
                spots.Add(new ModuleSpot {
                    Run = runIndex, Position = position, Width = left, Building = owner,
                    Reach = new float[ownerLevels], Walls = new PieceAnnotation[ownerLevels], ModuleIndex = -1
                });
            }

            position = run.Length;
        }

        private static float Spacing(ModuleSpot spot, float spacing)
        {
            return spot.Width > 0f ? spot.Width : spacing;
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
            position = end.AtStart ? spot.Position : spot.Position + Spacing(spot, end.Frontage.Buildings);
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
                Reach = new float[building.Levels.Count], Walls = new PieceAnnotation[building.Levels.Count],
                ModuleIndex = -1
            });
            turned = new RunEnd { Frontage = frontage, Spot = frontage.Spots.Count - 1, AtStart = true };
            return true;
        }

        /// <summary>
        /// Builds a corner: each side carried to where the two lines meet,
        /// storey by storey, the covering side (FirstCovers) to the corner.
        /// </summary>
        private static void BuildJoint(Joint joint, GeneratorSettings settings, System.Random random, PiecePlacer placer, HashSet<string> missing)
        {
            bool firstCovers = FirstCovers(joint);
            _clearance.Facing = joint.A.Frontage.Street;
            BuildCornerSide(joint.A, joint.B, firstCovers, settings, random, placer, missing);
            _clearance.Facing = joint.B.Frontage.Street;
            BuildCornerSide(joint.B, joint.A, !firstCovers, settings, random, placer, missing);
        }

        /// <summary>
        /// One side of a corner, storey by storey (each storey's line at its
        /// column's depth plus its building's jetty, meeting the other side's
        /// storey at the same index, or its top storey): a filler carries the
        /// storey from its end to the meeting point - a plain wall of the
        /// storey's height scaled to the length (the nearest in width; none
        /// where the end module's wall is widened instead, WidenCornerEnds),
        /// with a foundation under the ground storey, the beam band below and
        /// the jetty supports under the first upper storey scaled the same
        /// way. A storey above the other side's top is closed by side walls
        /// facing out of the corner; at a sharp corner the covering side puts
        /// a post across the notch (PlaceCover). Under "Corner" of its
        /// building.
        /// Records on the end's module how far its top storey was carried
        /// (ModuleSpot.CarriedStart / CarriedEnd), for its roof.
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
            // How far the top storey is carried, for the roof over it, and
            // how far out its side walls' outer face stands.
            float carried = 0f;
            float sideFace = 0f;

            for (int s = 0; s < spot.Storeys; s++) {
                int otherStorey = Mathf.Min(s, otherSpot.Storeys - 1);
                float depth = spot.Depth + StoreyJetty(building, s);
                float otherDepth = otherSpot.Depth + StoreyJetty(otherBuilding, otherStorey);

                bool met = Meet(me, depth, other, otherDepth, out float reach, out float otherReach, out Vector2 point);
                WallTarget(me, s, depth, other, otherStorey, otherDepth, cover, settings, placer, out _, out float wallReach);
                EndLine(me, depth, out Vector2 mine, out Vector2 outward, out Run run, out float position);
                StoreyLevel level = building.Levels[s];

                if (!met || Mathf.Abs(reach) > settings.CornerMaxReach || Mathf.Abs(otherReach) > settings.CornerMaxReach) {
                    // Nearly in line (a slight bend between segments of
                    // different widths, or one street running on nearly
                    // straight into another): the lines meet far off, so each
                    // side is carried to its own run's end (on a bend's
                    // bisector) and on halfway across any gap left between the
                    // two (InLineCarry). The side standing further into the street
                    // closes the step with side walls facing out of the
                    // corner; with no step the joint is covered between the
                    // two ends.
                    float toEnd = InLineCarry(me, depth, other, otherDepth);

                    if (s == spot.Storeys - 1) {
                        carried = toEnd;
                    }

                    if (toEnd > settings.SizeTolerance) {
                        PlaceFiller(building, s, spot, run, position, me.AtStart, Widened(me, s) ? 0f : toEnd, toEnd, depth, 0f, setback, settings,
                            random, placer, group, missing);
                    }

                    position += me.AtStart ? -toEnd : toEnd;
                    mine += outward * toEnd;
                    EndLine(other, otherDepth, out Vector2 theirs, out Vector2 theirOutward, out _, out _);
                    theirs += theirOutward * InLineCarry(other, otherDepth, me, depth);
                    float step = Vector2.Dot(mine - theirs, run.Front);

                    if (step > settings.SizeTolerance) {
                        List<PieceAnnotation> stepWalls = SideWallChoices(building, s, building);

                        if (stepWalls.Count == 0) {
                            missing.Add($"No side wall of the height of style '{level.Style.Name}' to close a corner.");
                            continue;
                        }

                        // Outside the end, its back plane on it, from just
                        // behind the front wall's face: it covers the wall's
                        // end and runs on through the other side's walls.
                        float thickness = LaySideStorey(building, s, stepWalls, ref previous, run.Start + run.Along * position, run,
                            SideWallFace(spot, s, depth, setback, placer), Mathf.Max(level.Style.ModuleSpacing, step + setback),
                            Height(run, position), outward, true, settings, random, placer, group);

                        if (s == spot.Storeys - 1 && thickness > 0f) {
                            sideFace = toEnd + thickness;
                        }
                    } else if (cover && step >= -settings.SizeTolerance && s < otherSpot.Storeys) {
                        Run otherRun = other.Frontage.Runs[otherSpot.Run];
                        PlaceCover(level, otherBuilding.Levels[otherStorey].Style, (mine + theirs) * 0.5f, Height(run, position) + level.Base, run.Front,
                            otherRun.Front, random, placer, group, missing);
                    }

                    continue;
                }

                if (reach < -settings.SizeTolerance) {
                    // The lines meet behind this end: its module already
                    // reaches past the other side's line, so nothing is
                    // carried; reported so a bare end there can be traced.
                    GeneratedReport.Add(GeneratedRoot.MarkerKind.Other, new Vector3(mine.x, Height(run, position) + level.Base + 1f, mine.y),
                        new Vector3(0.5f, 2f, 0.5f),
                        $"Corner: {frontage.Label}, module {me.Spot} storey {s} meets the other side {-reach:0.00}m behind its end, nothing carried.");
                    continue;
                }

                // How far the walls go (WallTarget): the covering side to the
                // other side's face, the other side into the covering side's
                // wall - so the two never share a plane in view. The beam
                // band and the jetty supports go with the walls; the
                // foundation and the roof still go to the corner.

                // A sharp corner: carried all the way to the tip, the walls'
                // backs would cross the other side's front into its street.
                // Each side stops where its back line meets the other's front
                // line instead, and the cover goes across the notch between
                // the two stops.
                bool sharp = CarryToBack(me, depth, setback, other, otherDepth, reach, settings, out float backReach);

                if (sharp) {
                    reach = backReach;
                    wallReach = backReach;
                    Vector2 mineStop = mine + outward * reach;
                    float theirSetback = other.Frontage.WallSetback;
                    EndLine(other, otherDepth, out Vector2 theirs, out Vector2 theirOutward, out _, out _);
                    float theirReach = otherReach;

                    if (CarryToBack(other, otherDepth, theirSetback, me, depth, otherReach, settings, out float theirBack)) {
                        theirReach = theirBack;
                    }

                    point = (mineStop + theirs + theirOutward * Mathf.Max(0f, theirReach)) * 0.5f;
                }

                reach = Mathf.Max(0f, reach);
                wallReach = Mathf.Max(0f, wallReach);
                float from = me.AtStart ? position - reach : position;

                if (s == spot.Storeys - 1) {
                    carried = reach;
                }
                float height = Height(run, position);

                bool above = level.Base + level.Style.StoreySpacing > otherTop + settings.SizeTolerance;
                float walls = Widened(me, s) ? 0f : CornerWalls(me, other, cover, s, settings, placer);

                if (reach > settings.SizeTolerance) {
                    PlaceFiller(building, s, spot, run, position, me.AtStart, walls > settings.SizeTolerance ? walls : 0f,
                        FoundationCarry(me, depth, other, otherDepth, reach, settings), depth, CornerFaceReach(other, otherStorey, placer), setback, settings,
                        random, placer, group, missing);
                }

                if (above) {
                    // Above the other side: closed by itself, facing out of
                    // the corner from its back line back past its own line.
                    List<PieceAnnotation> choices = SideWallChoices(building, s, building);

                    if (choices.Count == 0) {
                        missing.Add($"No side wall of the height of style '{level.Style.Name}' to close a corner.");
                        continue;
                    }

                    // Outside the carried end, its back plane on it, from
                    // just behind the front wall's face (above the other
                    // side's top, that is free).
                    Vector2 onEnd = run.Start + run.Along * (me.AtStart ? from : from + reach);
                    float length = Mathf.Max(level.Style.ModuleSpacing, depth - spot.Depth + setback);
                    float thickness = LaySideStorey(building, s, choices, ref previous, onEnd, run, SideWallFace(spot, s, depth, setback, placer),
                        length, height, outward, true, settings, random, placer, group);

                    if (s == spot.Storeys - 1 && thickness > 0f) {
                        sideFace = reach + thickness;
                    }
                } else if (cover && sharp && s < otherSpot.Storeys) {
                    PlaceCover(level, otherBuilding.Levels[otherStorey].Style, point, height + level.Base, run.Front,
                        other.Frontage.Runs[otherSpot.Run].Front, random, placer, group, missing);
                }
            }

            ModuleSpot marked = frontage.Spots[me.Spot];
            EndLine(me, 0f, out _, out Vector2 myWay, out _, out _);
            EndLine(other, 0f, out _, out Vector2 otherWay, out _, out _);
            bool gable = frontage.Lot == null && Vector2.Angle(myWay, otherWay) <= 90f + settings.CornerAngleTolerance;

            if (me.AtStart) {
                marked.CarriedStart = carried;
                marked.GableStart = gable;
                marked.SideFaceStart = sideFace;
            } else {
                marked.CarriedEnd = carried;
                marked.GableEnd = gable;
                marked.SideFaceEnd = sideFace;
            }

            frontage.Spots[me.Spot] = marked;
        }

        /// <summary>
        /// How far a corner side's foundations are carried out of its end:
        /// to the corner (reach), short of it at a corner sharper than a
        /// right angle, where their back (the deepest foundation's depth
        /// behind their front, FoundationFrontOffset past the side's line)
        /// meets the other side's foundations' front, so their back corner
        /// stays out of the other street.
        /// </summary>
        private static float FoundationCarry(RunEnd me, float depth, RunEnd other, float otherDepth, float reach, GeneratorSettings settings)
        {
            Building building = me.Frontage.Buildings[me.Frontage.Spots[me.Spot].Building];
            float deepest = 0f;

            foreach (PieceAnnotation piece in building.Ground.Pieces("foundation")) {
                deepest = Mathf.Max(deepest, piece.depth);
            }

            float front = depth + settings.FoundationFrontOffset;
            return Meet(me, front - deepest, other, otherDepth + settings.FoundationFrontOffset, out float toBack, out _, out _)
                ? Mathf.Clamp(toBack, 0f, reach)
                : reach;
        }

        /// <summary>
        /// How far a nearly-in-line corner side (at depth) is carried out of
        /// its end: to its run's end, then on halfway across whatever gap is
        /// left to the other side's run end (the other side carries the other
        /// half).
        /// </summary>
        private static float InLineCarry(RunEnd me, float depth, RunEnd other, float otherDepth)
        {
            EndLine(me, depth, out Vector2 mine, out Vector2 outward, out Run run, out float position);
            EndLine(other, otherDepth, out Vector2 theirs, out Vector2 theirOutward, out Run theirRun, out float theirPosition);
            float toEnd = Mathf.Max(0f, me.AtStart ? position : run.Length - position);
            float theirToEnd = Mathf.Max(0f, other.AtStart ? theirPosition : theirRun.Length - theirPosition);
            float gap = Vector2.Dot(theirs + theirOutward * theirToEnd - (mine + outward * toEnd), outward);
            return toEnd + Mathf.Max(0f, gap * 0.5f);
        }

        /// <summary>
        /// Whether a corner side must stop short of where the two fronts meet
        /// (reach out of its end): true when its back line (setback behind
        /// its front, at depth) meets the other side's front line sooner - a
        /// corner sharper than a right angle by more than CornerAngleTolerance
        /// (a squarer one is met as a right angle is, the covering side's
        /// post standing a little proud) - with how far to carry it then
        /// (never below 0).
        /// </summary>
        private static bool CarryToBack(RunEnd me, float depth, float setback, RunEnd other, float otherDepth, float reach, GeneratorSettings settings,
            out float backReach)
        {
            EndLine(me, 0f, out _, out Vector2 outward, out _, out _);
            EndLine(other, 0f, out _, out Vector2 otherOutward, out _, out _);
            bool square = Vector2.Angle(outward, otherOutward) >= 90f - settings.CornerAngleTolerance;

            if (square || !Meet(me, depth - setback, other, otherDepth, out backReach, out _, out _) || backReach >= reach - settings.SizeTolerance) {
                backReach = reach;
                return false;
            }

            backReach = Mathf.Max(0f, backReach);
            return true;
        }

        /// <summary>
        /// A corner's filler for one storey, carried out of a run's end (at
        /// position along it; atStart, back towards its start) at the
        /// storey's depth: plain walls of the storey's height (borrowed as
        /// for side walls) covering length by the rule (FitWidths: whole
        /// modules, the last widened or one more narrowed), with the beam
        /// band's beams below a banded storey and a jettied building's first
        /// upper storey's jetty supports covering it the same way, on the
        /// same joints - none of them where it is 0 (the end module's wall,
        /// beam and support widened to reach instead, never a sliver beside
        /// them); and on the ground storey foundations covering
        /// foundationLength (on to the corner). The walls are brought
        /// forward so their fronts reach as far as faceReach from the back
        /// line (the other side's end wall at the corner, so a shallower
        /// filler's post meets it face to face; 0 for none), the beams with
        /// them; the foundations and supports stay.
        /// </summary>
        private static void PlaceFiller(Building building, int storey, ModuleSpot spot, Run run, float position, bool atStart, float length,
            float foundationLength, float depth, float faceReach, float setback, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            Transform group, HashSet<string> missing)
        {
            StoreyLevel level = building.Levels[storey];
            float overlap = settings.WallOverlap;
            float shift = 0f;

            if (storey == 0 && foundationLength > 0f) {
                IReadOnlyList<PieceAnnotation> foundations = building.Ground.Pieces("foundation");
                PieceAnnotation previous = null;
                float laid = 0f;

                foreach (float width in FitWidths(foundationLength + overlap, building.Ground.ModuleWidth, overlap, settings)) {
                    PieceAnnotation foundation = PickForWidth(foundations, width, previous, random, settings);
                    previous = foundation;
                    float advance = width - overlap;

                    if (foundation != null) {
                        float middle = position + (atStart ? -(laid + advance * 0.5f) : laid + advance * 0.5f);
                        PlaceInRun(foundation, placer, run, middle, depth + settings.FoundationFrontOffset, false, settings.FoundationHeight, true,
                            width / foundation.width, 1f, group);
                    }

                    laid += advance;
                }
            }

            if (length > 0f) {
                List<PieceAnnotation> choices = SideWallChoices(building, storey, building);

                if (choices.Count == 0) {
                    missing.Add($"No plain wall of the height of style '{level.Style.Name}' to fill a corner.");
                } else {
                    float from = atStart ? position - length : position;
                    PieceAnnotation previous = null;
                    float laid = 0f;

                    foreach (float width in FitWidths(length + overlap, level.Style.ModuleWidth, overlap, settings)) {
                        PieceAnnotation wall = PickForWidth(choices, width, previous, random, settings);
                        previous = wall;
                        ModuleSpot filler = spot;
                        filler.Position = from + laid;
                        float forward = wall != null ? Mathf.Max(0f, faceReach - Reach(wall, placer)) : 0f;
                        shift = Mathf.Max(shift, forward);
                        filler.Depth = depth + forward;
                        PlaceWall(wall, level.Style, run, filler, width - overlap, level.Base, setback, overlap, placer, group, width);
                        laid += width - overlap;
                    }
                }
            }

            if (length <= 0f) {
                return;
            }

            IReadOnlyList<PieceAnnotation> beams = level.BeamHeight > 0f ? building.Levels[storey - 1].Style.Pieces("beam") : null;
            bool supports = storey == 1 && building.Jetty > 0f;

            if (beams == null && !supports) {
                return;
            }

            float bandFrom = atStart ? position - length : position;
            PieceAnnotation previousBeam = null;
            PieceAnnotation previousSupport = null;
            float bandLaid = 0f;

            foreach (float width in FitWidths(length + overlap, level.Style.ModuleWidth, overlap, settings)) {
                float advance = width - overlap;
                float middle = bandFrom + bandLaid + advance * 0.5f;

                if (beams != null) {
                    PieceAnnotation beam = PiecePlacer.Pick(beams, previousBeam, random);
                    previousBeam = beam;

                    if (beam != null) {
                        PlaceInRun(beam, placer, run, middle, depth + shift - setback, true, level.BeamBottom, false, width / beam.width,
                            level.BeamHeight / beam.height, group);
                    }
                }

                if (supports) {
                    PieceAnnotation support = PickForWidth(building.Jetties, width, previousSupport, random, settings);
                    previousSupport = support;

                    if (support != null) {
                        PlaceInRun(support, placer, run, middle, spot.Depth - setback + building.SupportBack, true, JettyDeckTop(level, settings), true, width / support.width, 1f,
                            group);
                    }
                }

                bandLaid += advance;
            }
        }

        /// <summary>
        /// A piece for a width: one at random among those within
        /// WallScaleLimit (or SizeTolerance) of it, never the one before;
        /// else the nearest in width. Null for no pieces.
        /// </summary>
        private static PieceAnnotation PickForWidth(IReadOnlyList<PieceAnnotation> pieces, float width, PieceAnnotation previous, System.Random random,
            GeneratorSettings settings)
        {
            float within = Mathf.Max(settings.SizeTolerance, width * settings.WallScaleLimit);
            List<PieceAnnotation> near = new();

            foreach (PieceAnnotation piece in pieces) {
                if (Mathf.Abs(piece.width - width) <= within) {
                    near.Add(piece);
                }
            }

            return near.Count > 0 ? PiecePlacer.Pick(near, previous, random) : Nearest(pieces, width);
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
        /// The post over a sharp corner's notch for one storey, at the middle
        /// of the two stops: a column piece, the middle of its fronts (one, or
        /// two) facing out between the two facades, its render box's furthest
        /// reach that way on the point. From the storey's style, else the
        /// other side's, of the storey's height (scaled to it as walls are);
        /// none is logged in missing.
        /// </summary>
        private static void PlaceCover(StoreyLevel level, BuildingStyle otherStyle, Vector2 point, float height, Vector2 frontA, Vector2 frontB,
            System.Random random, PiecePlacer placer, Transform group, HashSet<string> missing)
        {
            float target = level.Style.ModuleHeight;
            List<PieceAnnotation> choices = new List<PieceAnnotation>(level.Style.Pieces("column")).FindAll(piece =>
                piece.fronts.Count is 1 or 2 && level.Style.Fits(piece.height, target));

            if (choices.Count == 0) {
                choices = new List<PieceAnnotation>(otherStyle.Pieces("column")).FindAll(piece =>
                    piece.fronts.Count is 1 or 2 && level.Style.Fits(piece.height, target));
            }

            PieceAnnotation cover = PiecePlacer.Pick(choices, null, random);
            CatalogueEntry entry = cover != null ? placer.Entry(cover) : null;

            if (entry == null) {
                missing.Add($"No usable column piece of the height of style '{level.Style.Name}' (or '{otherStyle.Name}'): those corner notches are left bare.");
                return;
            }

            Vector2 between = (frontA + frontB).normalized;
            Vector3 facing = Vector3.zero;

            foreach (Vector3 front in cover.fronts) {
                facing += front;
            }

            facing.Normalize();
            Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
            centre.y = 0f;
            Vector3 local = centre + facing * (Extent(entry, facing) - Vector3.Dot(centre, facing)) + Vector3.up * entry.renderMin.y;
            Vector2 at = point + between * FaceClearance;
            placer.PlaceAt(cover, local, new Vector3(at.x, height, at.y), Yaw(Level(between)) - Yaw(facing),
                new Vector3(1f, level.Style.FitScale(cover.height, target), 1f), group);
        }

        /// <summary>
        /// Moves the laying of modules on to the next run (the length left on
        /// this one already closed by FillRunEnd; on the inside of a bend the
        /// runs meet at their back lines' crossing, so the corner is closed
        /// by that alone).
        /// </summary>
        private static void NextRun(ref int runIndex, ref float position)
        {
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
        /// (moving to the next run as the buildings do), and what is left
        /// under a module at a run's end is closed by the rule (FillRunEnd).
        /// Where the end is closed by something standing across it - a dead
        /// end, or the other street's facade at an inside L corner - one more
        /// module is always added instead, its overrun standing behind that,
        /// so the frontage always meets it and the corner behind is closed.
        /// </summary>
        private static void ExtendLastBuilding(List<ModuleSpot> spots, List<Building> buildings, List<Run> runs, int runIndex, float position,
            bool closedEnd, GeneratorSettings settings)
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
                    FillRunEnd(spots, runs[runIndex], runIndex, ref position, last, levels, spacing, buildings, settings);
                    NextRun(ref runIndex, ref position);
                    continue;
                }

                if (!fits && !closedEnd) {
                    // The end is closed by the rule: the last module widened
                    // to it, or a narrower one added.
                    FillRunEnd(spots, runs[runIndex], runIndex, ref position, last, levels, spacing, buildings, settings);
                    return;
                }

                spots.Add(new ModuleSpot {
                    Run = runIndex, Position = position, Building = last, Reach = new float[levels], Walls = new PieceAnnotation[levels], ModuleIndex = -1, Overrun = !fits
                });
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
        /// (styles, modules, storeys) is made by StoreyPlan. No columns. Returns
        /// it as a frontage of one run (for its roof), or null if not built.
        /// </summary>
        private static Frontage BuildDeadEnd(StoreyPlan.DeadEnd deadEnd, Dictionary<string, BuildingStyle> styles, StoreyPlan plan,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform parent, List<string> log)
        {
            StreetSpec street = deadEnd.Street;
            BuildingSpec spec = deadEnd.Building;
            string label = $"Street '{street.id}', dead end at point {deadEnd.Point}";
            BuildingStyle ground = Find(styles, spec.storeys[0]);
            int storeys = plan.Storeys(spec);

            if (storeys == 0 || ground == null || ground.ModuleSpacing <= 0f) {
                log.Add($"{label} not built: its style has no module yet.");
                return null;
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
                spots.Add(new ModuleSpot { Run = 0, Position = k * ground.ModuleSpacing, Storeys = levels.Count, Reach = new float[levels.Count], Walls = new PieceAnnotation[levels.Count], ModuleIndex = k });
            }

            BuildBuilding(building, spots, runs, Setback(new[] { spec }, styles, placer), true, settings, random, placer, label, log);
            log.Add($"{label}: {placer.Placed - pieces} piece(s).");
            return new Frontage { Label = label, Runs = runs, Spots = spots, Buildings = new List<Building> { building } };
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
        /// the ground storey's walls, with the beam band (where both styles
        /// have beams) standing on the deck as it would on the walls - the
        /// band and the walls JettyStoreyDrop further down, cut into the deck
        /// (JettyDeckTop gives the deck back).
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
                    level.BeamHeight = BeamHeight(below.Style, style);

                    // On a jetty the first upper storey stands on the deck,
                    // dropped into it so the beam and walls cut the planks.
                    float drop = jettied && s == 1 ? settings.JettyStoreyDrop : 0f;

                    if (level.BeamHeight > 0f) {
                        // The beam's bottom overlaps the top of the walls
                        // below (on a jetty, it stands on the deck there),
                        // the walls above overlap its top.
                        level.BeamBottom = below.Base + below.Style.ModuleHeight - settings.StoreyBeamOverlap - drop;
                        level.Base = level.BeamBottom + level.BeamHeight - settings.StoreyBeamOverlap;
                    } else if (jettied && s == 1) {
                        level.Base = below.Base + below.Style.ModuleHeight - settings.StoreyBeamOverlap - drop;
                    } else {
                        level.Base = below.Base + below.Style.StoreySpacing;
                    }
                }

                // No storey whose top would pass the tallest a building may
                // stand (the ground storey always stands).
                if (s > 0 && level.Base + style.StoreySpacing > settings.MaxBuildingHeight) {
                    break;
                }

                levels.Add(level);
            }

            return levels;
        }

        /// <summary>
        /// Where a jettied building's supports and upper storeys stand (0 for
        /// both where it isn't jettied, or has no upper storey). The
        /// supports' backs stand JettyInset behind the shallowest main front
        /// surface of the ground storey's walls (FrontPlane: the plaster or
        /// stone between the timbers, which stand proud of it), so every
        /// support sinks a little into the wall behind it; their fronts are
        /// the deepest piece's depth further out. The upper storeys stand
        /// forward by as much as brings the front of their typical wall
        /// (the median reach of the first upper storey's walls) level with
        /// the supports' front.
        /// </summary>
        private static void SetJetty(Building building, bool jettied, GeneratorSettings settings, PiecePlacer placer)
        {
            building.Jetty = 0f;
            building.SupportBack = 0f;

            if (!jettied || building.Levels.Count < 2) {
                return;
            }

            float depth = 0f;

            foreach (PieceAnnotation piece in building.Jetties) {
                CatalogueEntry entry = placer.Entry(piece);
                depth = Mathf.Max(depth, Extent(entry, piece.fronts[0]) + Extent(entry, -piece.fronts[0]));
            }

            List<float> upperReaches = new();

            // Door walls stand only on the ground storey.
            foreach (string category in new[] { "wall", "window-wall" }) {
                foreach (PieceAnnotation piece in building.Levels[1].Style.Modules(category, 1)) {
                    upperReaches.Add(Reach(piece, placer));
                }
            }

            // The middle (lower middle) of the upper walls' reaches, so a
            // typical wall's front meets the supports' front and the
            // deepest trims stand only a little past it.
            upperReaches.Sort();
            float upperReach = upperReaches.Count > 0 ? upperReaches[(upperReaches.Count - 1) / 2] : 0f;
            building.SupportBack = SupportBack(building.Ground, 0, settings, placer);
            building.Jetty = Mathf.Max(0.01f, building.SupportBack + depth - upperReach);
        }

        /// <summary>
        /// How far in front of the walls' back line the backs of supports
        /// (a jetty's, a walkway's) under a storey of a style stand: JettyInset
        /// behind the shallowest main front surface of its walls (FrontPlane),
        /// so every support sinks a little into the wall behind it, in one
        /// line whichever walls stand there.
        /// </summary>
        private static float SupportBack(BuildingStyle style, int storey, GeneratorSettings settings, PiecePlacer placer)
        {
            float face = float.MaxValue;

            foreach (string category in new[] { "wall", "window-wall", "door-wall" }) {
                foreach (PieceAnnotation piece in style.Modules(category, storey)) {
                    face = Mathf.Min(face, FrontPlane(piece, placer));
                }
            }

            return (face == float.MaxValue ? 0f : face) - settings.JettyInset;
        }

        /// <summary>
        /// How far a wall's main front surface stands out in front of its
        /// back plane: the plane holding most of the area of its front-facing
        /// triangles (the scan's side planes), less its left point's depth.
        /// Its Reach where that side wasn't measured.
        /// </summary>
        private static float FrontPlane(PieceAnnotation piece, PiecePlacer placer)
        {
            CatalogueEntry entry = placer.Entry(piece);

            if (entry == null || piece.fronts.Count != 1 || !Connection(piece, "left", out Vector3 left)) {
                return 0f;
            }

            Vector3 front = piece.fronts[0];
            string side = Mathf.Abs(front.x) > 0.5f ? (front.x > 0f ? "+X" : "-X") : (front.z > 0f ? "+Z" : "-Z");

            foreach (SideStats stats in entry.sides) {
                if (stats.side == side && stats.area > 0f) {
                    return stats.planeDepth * (front.x + front.z) - Vector3.Dot(left, front);
                }
            }

            return Reach(piece, placer);
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
                BuildStorey(building, s, building.Levels[s], spots, runs, StoreyJetty(building, s), wallSetback,
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
        /// The height of a deck under a storey (a jetty's supports, a
        /// walkway): under its beam band if it stands on one, else at its
        /// base.
        /// </summary>
        private static float DeckTop(StoreyLevel level)
        {
            return level.BeamHeight > 0f ? level.BeamBottom : level.Base;
        }

        /// <summary>
        /// The height of the top of the jetty supports' deck under a jettied
        /// building's first upper storey: the storey stands JettyStoreyDrop
        /// down into it (StoreyLevels), so the deck is that much above the
        /// storey's bottom.
        /// </summary>
        private static float JettyDeckTop(StoreyLevel level, GeneratorSettings settings)
        {
            return DeckTop(level) + settings.JettyStoreyDrop;
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

            float deckTop = JettyDeckTop(building.Levels[1], settings);
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
                // Widened with the first upper storey's wall where a corner
                // widens that (WidenCornerEnds), so the deck runs on to the
                // corner with the storey it carries.
                float widenStart = spot.WidenStart != null ? spot.WidenStart[1] : 0f;
                float widenEnd = spot.WidenEnd != null ? spot.WidenEnd[1] : 0f;
                float spacing = Spacing(spot, building) + widenStart + widenEnd;
                float widthScale = (spacing + settings.WallOverlap) / piece.width;

                Run run = runs[spot.Run];
                float middle = spot.Position - widenStart + spacing * 0.5f;
                Vector2 onWallFace = run.Start + run.Along * middle + run.Front * (spot.Depth - wallSetback + building.SupportBack);
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
        /// own space - centred in depth on the wall above it (its back on the
        /// walls' back line where that isn't known), its bottom on the band's
        /// bottom, centred on the module along the street - scaled
        /// along its width to the module as the walls are and in height to the
        /// band. Under "Beams".
        /// </summary>
        private static void BuildBeams(Building building, List<ModuleSpot> spots, List<Run> runs, float wallSetback, GeneratorSettings settings,
            System.Random random, PiecePlacer placer)
        {
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

                    // The middle of the bottom of its back, in its own space,
                    // moved to the middle of its depth when the wall above it
                    // is known: the beam is centred in depth on that wall.
                    Vector3 pieceFront = piece.fronts[0];
                    Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
                    Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
                    float wallMiddle = DepthMiddle(spot.Walls[s], placer);
                    float inDepth = wallMiddle >= 0f ? (Extent(entry, pieceFront) - Extent(entry, -pieceFront)) * 0.5f : -Extent(entry, -pieceFront);
                    Vector3 local = pieceRight * Vector3.Dot(centre, pieceRight) + pieceFront * inDepth + Vector3.up * entry.renderMin.y;
                    // Widened with the wall above it where a corner widens
                    // that (WidenCornerEnds), so the band runs on to the
                    // corner with the wall.
                    float widenStart = spot.WidenStart != null ? spot.WidenStart[s] : 0f;
                    float widenEnd = spot.WidenEnd != null ? spot.WidenEnd[s] : 0f;
                    float spacing = Spacing(spot, building) + widenStart + widenEnd;
                    float widthScale = (spacing + settings.WallOverlap) / piece.width;

                    Run run = runs[spot.Run];
                    float middle = spot.Position - widenStart + spacing * 0.5f;
                    Vector2 onBackLine = run.Start + run.Along * middle
                        + run.Front * (spot.Depth + StoreyJetty(building, s) - wallSetback + Mathf.Max(0f, wallMiddle));
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
        /// modules (a column never crosses a building's end, a bend or a
        /// change of the street's level beside a flight of steps; the last of
        /// each takes what is left), and gives each a depth and storey
        /// count, written into its modules. Off the line at ColumnShare -
        /// only on a run of at least ColumnMinRunModules modules and never
        /// holding the run's first or last module - ColumnDepthMin to the most
        /// it may be deep, forward at ColumnOutShare else back; forward only
        /// where the step stays out of every other street (ColumnClear). Taller or lower
        /// than its building at ColumnHeightShare (either equally likely), by
        /// 1 to ColumnStoreyVariation storeys, kept between MinStoreys and the
        /// storeys the building has. Every draw is made for every column, so
        /// the sequence doesn't depend on which apply.
        /// </summary>
        private static List<Column> SplitColumns(List<ModuleSpot> spots, List<Run> runs, List<Building> buildings, float maxDepth,
            GeneratorSettings settings, System.Random random)
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

                while (last - first + 1 < width && last < runLast && spots[last + 1].Building == spots[first].Building
                    && Mathf.Abs(SpotGround(spots[last + 1], runs, buildings) - SpotGround(spots[first], runs, buildings)) <= settings.SizeTolerance) {
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
                } else if (!ColumnClear(runs[spots[first].Run], spots[first].Position,
                    spots[last].Position + Spacing(spots[last], buildings), depth, settings)) {
                    depth = 0f;
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
        /// Whether a column stepping forward into its street by depth, from
        /// along its run to to, stays out of every other street: its front
        /// (and foundations' front) a band from the street edge forward
        /// (StreetClearance.Clear, cutting nothing). A run lining no street (a
        /// lot's side in open ground) must stay out of every street.
        /// </summary>
        private static bool ColumnClear(Run run, float from, float to, float depth, GeneratorSettings settings)
        {
            float start = from;
            float end = to;
            return _clearance.Clear(run.Street, run.Segment, run.Start, run.Along, -run.Front, depth + settings.FoundationFrontOffset, 0f, 0f,
                ref start, ref end) && start <= from + 0.0001f && end >= to - 0.0001f;
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
                // The columns meet where the second one starts, each standing
                // on its own level (they differ beside a flight of steps).
                Vector2 onJoin = run.Start + run.Along * b.Position;
                float heightA = SpotGround(a, runs, buildings);
                float heightB = SpotGround(b, runs, buildings);
                // a's walls face out of its end along the run, b's back. Each
                // records where its top storey's end wall stands, for its roof.
                a.SideFaceEnd = FillSide(a, columns[a.Column], b, run.Along, onJoin, heightA, heightB, run, buildings, wallSetback, settings, random,
                    placer, $"Side {i}", missing);
                b.SideFaceStart = FillSide(b, columns[b.Column], a, -run.Along, onJoin, heightB, heightA, run, buildings, wallSetback, settings, random,
                    placer, $"Side {i}", missing);
                spots[i] = a;
                spots[i + 1] = b;
            }

            // A run's two ends (a frontage's ends and its bends) meet no
            // column of the run: a jettied storey there is closed by itself.
            for (int i = 0; i < spots.Count; i++) {
                ModuleSpot spot = spots[i];
                Building building = buildings[spot.Building];
                Run run = runs[spot.Run];

                if ((i == 0 || spots[i - 1].Run != spot.Run) && !spot.JoinedStart) {
                    spot.SideFaceStart = CloseJetty(spot, building, run, -run.Along, spot.Position, wallSetback, settings, random, placer, $"End {i}",
                        missing);
                }

                if ((i == spots.Count - 1 || spots[i + 1].Run != spot.Run) && !spot.JoinedEnd) {
                    spot.SideFaceEnd = CloseJetty(spot, building, run, run.Along, spot.Position + Spacing(spot, building), wallSetback, settings, random,
                        placer, $"End {i} far", missing);
                }

                spots[i] = spot;
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
        /// back line. Heights compare in metres above the street level each
        /// column stands on (height, otherHeight), so beside a flight of steps
        /// the higher column's storeys above the lower one's top are closed.
        /// Under a group of the column's building, made only if anything is
        /// placed. Adds the name of each storey style with nothing to give to
        /// missing. Returns how far out of the column's end the outer face
        /// of its top storey's side walls stands (0 with none laid), where
        /// its roof's gable goes.
        /// </summary>
        private static float FillSide(ModuleSpot spot, Column column, ModuleSpot other, Vector2 face, Vector2 onJoin, float height, float otherHeight,
            Run run, List<Building> buildings, float wallSetback, GeneratorSettings settings, System.Random random, PiecePlacer placer, string name,
            HashSet<string> missing)
        {
            Building building = buildings[spot.Building];
            Building otherBuilding = buildings[other.Building];
            // The other column's top, measured from this column's street level.
            float otherTop = Top(otherBuilding, other.Storeys) + otherHeight - height;
            float raised = SideWallLength(column, building, settings);
            float groundThickness = 0f;
            float sideFace = 0f;
            Transform group = null;
            PieceAnnotation previous = null;

            for (int s = 0; s < spot.Storeys; s++) {
                StoreyLevel level = building.Levels[s];
                float depth = spot.Depth + StoreyJetty(building, s);
                float otherDepth = other.Depth + StoreyJetty(otherBuilding, s);
                bool forward = depth - otherDepth >= SameDepth;
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

                // At the column's right end the side wall stands in the other
                // column's space, its back plane on the join, from just behind
                // the face of this column's front wall: so it covers that
                // wall's plain end; at its left end it is tucked in behind the
                // front wall's trimmed end (SideWallStart). Behind the other
                // column's face it runs on through that column's front wall,
                // which hides the rest.
                Vector2 from = onJoin;
                float start = SideWallStart(spot, s, depth, wallSetback, run, face, settings, placer, ref from);
                group ??= Group(building.Group, name);
                float thickness = LaySideStorey(building, s, choices, ref previous, from, run, start, length, height, face, true, settings, random,
                    placer, group);

                if (s == 0) {
                    groundThickness = thickness;
                }

                // The top storey's end wall: its outer face past the join
                // (tucked back behind a trimmed front wall end, SideWallStart).
                if (s == spot.Storeys - 1 && thickness > 0f) {
                    sideFace = Vector2.Dot(from - onJoin, face) + thickness;
                }
            }

            // Only a step at the ground storey (columns, not jetties) stands
            // on a foundation.
            if (spot.Depth - other.Depth < SameDepth) {
                return sideFace;
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

            return sideFace;
        }

        /// <summary>
        /// One storey's side walls along a run back from a join (or a run's
        /// end): the length is cut short where the walls would reach into a
        /// street (StreetClearance.Reach, from either face of them), then
        /// covered by the rule (FitWidths: whole walls of the storey's
        /// module width, the last widened or one more narrowed), each piece
        /// one near that width from choices, never the piece before, scaled
        /// to it and in height to the storey; the beam band below it, if it
        /// has one, laid first on the same joints, so it closes the gap to
        /// the storey below. Returns the first wall's thickness (0 if none
        /// was placed).
        /// </summary>
        private static float LaySideStorey(Building building, int storey, List<PieceAnnotation> choices, ref PieceAnnotation previous, Vector2 onJoin,
            Run run, float start, float length, float height, Vector2 face, bool backOnJoin, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform group)
        {
            StoreyLevel level = building.Levels[storey];
            float first = 0f;
            float laid = 0f;
            // Cut short where the walls (either face of them) would run into a
            // street - near a sharp corner, the other street.
            float thick = 0f;

            foreach (PieceAnnotation choice in choices) {
                thick = Mathf.Max(thick, choice.depth);
            }

            Vector2 from = onJoin + run.Front * start;
            // A storey standing StreetHeadroom above the street may run over
            // it (as the clearance lets a jetty overhang); lower ones stop at
            // a street.
            float room = level.Base >= settings.StreetHeadroom
                ? float.MaxValue
                : Mathf.Min(_clearance.Reach(from, -run.Front), _clearance.Reach(from + (backOnJoin ? face : -face) * thick, -run.Front));
            length = Mathf.Min(length, room);
            // The pieces that cover the length by the rule (FitWidths), so
            // the last is never a sliver nor an overrun past the length.
            float overlap = settings.WallOverlap;
            List<float> widths = FitWidths(length + overlap, level.Style.ModuleWidth, overlap, settings);

            if (widths.Count == 0) {
                // A street lies right behind: no side wall fits. Reported,
                // since the wall it would have covered is left bare.
                GeneratedReport.Add(GeneratedRoot.MarkerKind.Other, new Vector3(from.x, height + level.Base + level.Style.ModuleHeight * 0.5f, from.y),
                    new Vector3(0.3f, level.Style.ModuleHeight, 0.3f), $"Side wall: {group.parent.name}/{group.name} storey {storey} has no room "
                    + $"({room:0.00}m to a street) and is left out.");
                return 0f;
            }

            if (level.BeamHeight > 0f) {
                // The beams centred in depth on the walls above them: the
                // middle (lower middle) of the choices' depth middles.
                List<float> middles = new();

                foreach (PieceAnnotation choice in choices) {
                    float middle = DepthMiddle(choice, placer);

                    if (middle >= 0f) {
                        middles.Add(middle);
                    }
                }

                middles.Sort();
                float wallMiddle = middles.Count > 0 ? middles[(middles.Count - 1) / 2] : -1f;
                LaySideBeams(building.Levels[storey - 1].Style.Pieces("beam"), onJoin, run, start, widths, height + level.BeamBottom, level.BeamHeight,
                    face, backOnJoin, wallMiddle, settings, random, placer, group);
            }

            foreach (float width in widths) {
                PieceAnnotation wall = PickForWidth(choices, width, previous, random, settings);
                previous = wall;
                Vector2 corner = onJoin + run.Front * (start - laid);
                float heightScale = level.Style.FitScale(wall.height, level.Style.ModuleHeight);
                float thickness = PlaceSideWall(wall, corner, height + level.Base, width / wall.width, heightScale, face, -run.Front, backOnJoin, placer,
                    group);

                if (thickness <= 0f) {
                    // Not placed (no entry, no connection point, or refused
                    // for standing in a street): reported, the run goes on.
                    GeneratedReport.Add(GeneratedRoot.MarkerKind.Other, new Vector3(corner.x, height + level.Base + level.Style.ModuleHeight * 0.5f, corner.y),
                        new Vector3(0.3f, level.Style.ModuleHeight, 0.3f),
                        $"Side wall: {group.parent.name}/{group.name} storey {storey}: '{wall.name}' could not be placed {laid:0.00}m along.");
                }

                if (laid == 0f) {
                    first = thickness;
                }

                laid += width - overlap;
            }

            return first;
        }

        /// <summary>
        /// Closes a jettied building's upper storeys at a run's end (a
        /// frontage's end or a bend), where no column of the run stands
        /// beside it: each storey above the ground gets side walls facing out
        /// of the end, outside it (a storey that high may overhang a street),
        /// from just behind its front wall's face back past its column's own
        /// line (a module, or the jetty plus the walls' setback if longer),
        /// with the beam band below it as at a join. Under "End <name>" of
        /// its building. Returns how far out of the end the outer face of
        /// the top storey's side walls stands (0 with none laid), where the
        /// roof's gable goes.
        /// </summary>
        private static float CloseJetty(ModuleSpot spot, Building building, Run run, Vector2 face, float position, float wallSetback,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, string name, HashSet<string> missing)
        {
            if (building.Jetty <= 0f) {
                return 0f;
            }

            Vector2 onEnd = run.Start + run.Along * position;
            float height = Height(run, position);
            float sideFace = 0f;
            Transform group = null;
            PieceAnnotation previous = null;

            for (int s = 1; s < spot.Storeys; s++) {
                StoreyLevel level = building.Levels[s];
                List<PieceAnnotation> choices = SideWallChoices(building, s, building);

                if (choices.Count == 0) {
                    missing.Add(level.Style.Name);
                    continue;
                }

                // Outside the end, its back plane on it, from just behind the
                // front wall's face, or tucked in behind a left end's trim
                // (SideWallStart): the walls are hollow, so an end face left
                // in front of anything shows black - the side wall must cover
                // it. A storey this high may overhang a street there.
                Vector2 from = onEnd;
                float start = SideWallStart(spot, s, spot.Depth + building.Jetty, wallSetback, run, face, settings, placer, ref from);
                float length = Mathf.Max(level.Style.ModuleSpacing, building.Jetty + wallSetback);
                group ??= Group(building.Group, name);
                float thickness = LaySideStorey(building, s, choices, ref previous, from, run, start, length, height, face, true, settings, random, placer,
                    group);

                if (s == spot.Storeys - 1 && thickness > 0f) {
                    sideFace = Vector2.Dot(from - onEnd, face) + thickness;
                }
            }

            return sideFace;
        }

        /// <summary>
        /// Where a storey's side walls at a column's end start, as far forward
        /// of the street edge as their street end comes (depth is the storey's:
        /// its column's plus any jetty), the way the pack's walls go together.
        /// At the column's right end (seen from the street; face points out of
        /// it), where its front wall's end is plain, they stand outside it,
        /// their back plane on the join, from halfway up the front wall's
        /// timbers (SideWallFace), covering its end. At its left end, where
        /// the front wall's end carries its trim, they are tucked in behind
        /// that end instead: the join moved SideWallTuck back into the
        /// column, and the street end on the front wall's main surface
        /// (FrontPlane: the plaster or stone between the timbers), so the trim
        /// covers them. onJoin is moved accordingly.
        /// </summary>
        private static float SideWallStart(ModuleSpot spot, int storey, float depth, float wallSetback, Run run, Vector2 face, GeneratorSettings settings,
            PiecePlacer placer, ref Vector2 onJoin)
        {
            float atFace = SideWallFace(spot, storey, depth, wallSetback, placer);
            // Seen from the street, the viewer's right is up x the way they look.
            Vector3 right = Vector3.Cross(Vector3.up, -Level(run.Front));
            PieceAnnotation wall = spot.Walls[storey];

            if (Vector3.Dot(Level(face), right) >= 0f || wall == null) {
                return atFace;
            }

            onJoin -= face * settings.SideWallTuck;
            return Mathf.Min(atFace, depth - wallSetback + FrontPlane(wall, placer));
        }

        /// <summary>
        /// How far forward of the street edge a storey's side walls start
        /// where they stand outside a column's end, backed against its plain
        /// front wall end (a column's right end, a corner): halfway between
        /// the front wall's main surface (FrontPlane: the plaster between the
        /// timbers) and its front (Reach: the timbers), less FaceClearance -
        /// so the side wall's post covers the timbers' ends without standing
        /// proud of them, or leaving a slot behind it against the plaster.
        /// The wall's front where its surface can't be measured; the back
        /// line where no wall stands there.
        /// </summary>
        private static float SideWallFace(ModuleSpot spot, int storey, float depth, float wallSetback, PiecePlacer placer)
        {
            PieceAnnotation wall = spot.Walls[storey];
            float reach = spot.Reach[storey];
            float surface = wall != null ? FrontPlane(wall, placer) : reach;
            return depth - wallSetback + Mathf.Max(0f, (surface + reach) * 0.5f - FaceClearance);
        }

        /// <summary>
        /// Beams laid end to end along a side wall run, at random (never the
        /// one before it), from start (along the frontage's front, from the
        /// street edge) back, one per side wall on the walls' joints (widths,
        /// each scaled along its width to the wall's): turned side-on to face
        /// face, the end of each one's render box at its place along the run,
        /// its bottom at bottom and scaled in height to the band. Its front on
        /// the join (its depth behind it), or with backOnJoin centred in depth
        /// on the side walls above it, whose back planes are on the join
        /// (wallMiddle: how far in front of it their middle stands; below 0
        /// unknown, the beam's back then on the join).
        /// </summary>
        private static void LaySideBeams(IReadOnlyList<PieceAnnotation> beams, Vector2 onJoin, Run run, float start, List<float> widths, float bottom,
            float bandHeight, Vector2 face, bool backOnJoin, float wallMiddle, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            Transform group)
        {
            Vector3 front = Level(face);
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            bool runsRight = Vector3.Dot(right, Level(-run.Front)) > 0f;
            PieceAnnotation previous = null;
            float laid = 0f;

            // One beam per side wall, on the walls' joints and scaled along
            // its width to the wall's.
            foreach (float width in widths) {
                PieceAnnotation piece = PiecePlacer.Pick(beams, previous, random);
                CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;
                float step = width - settings.WallOverlap;

                if (entry == null || piece.fronts.Count != 1 || step <= 0f) {
                    // No usable beam: its place is left and the band goes on.
                    laid += Mathf.Max(0f, step);
                    continue;
                }

                float widthScale = width / piece.width;
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
                Vector2 onBack = !backOnJoin ? corner - face * depth : wallMiddle >= 0f ? corner + face * (wallMiddle - depth * 0.5f) : corner;

                placer.PlaceAt(piece, local, new Vector3(onBack.x, bottom, onBack.y), Yaw(front) - Yaw(pieceFront),
                    PieceScale(piece, widthScale, bandHeight / (entry.renderMax.y - entry.renderMin.y)), group);
                laid += step;
            }
        }

        /// <summary>
        /// The plain walls a column's storey may build its side walls from:
        /// those of the storey's height (as they are or scaled to it; the
        /// ground-storey-only ones only on the ground storey), from its own
        /// style if it has any, else from the neighbouring column's style at
        /// the same storey, else from the styles of its building's storeys
        /// above, else its own style's plain walls that show a blind window
        /// (marked showsOpening: wall-tall has no other). Empty if none has one.
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

            // None anywhere (wall-tall has only blind-window plain walls, and
            // the other styles' walls are far shorter): its own plain walls
            // that show a blind window.
            if (choices.Count == 0) {
                choices = new List<PieceAnnotation>(level.Style.Pieces("wall")).FindAll(piece =>
                    piece.showsOpening && (storey == 0 || !piece.groundStoreyOnly) && level.Style.Fits(piece.height, level.Style.ModuleHeight));
            }

            return PlainWalls(choices, level);
        }

        /// <summary>
        /// Plain walls for a storey, without the onePerStorey walls other
        /// than the one it drew (all of them before it has drawn one).
        /// </summary>
        private static List<PieceAnnotation> PlainWalls(IReadOnlyList<PieceAnnotation> walls, StoreyLevel level)
        {
            return new List<PieceAnnotation>(walls).FindAll(piece => !piece.onePerStorey || level.OnePerStorey == null || piece == level.OnePerStorey);
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
        /// window walls). Of the plain walls marked onePerStorey it draws one
        /// and uses no other so marked (StoreyLevel.OnePerStorey, kept for
        /// its corners and side walls). The walls stand jetty forward of their modules'
        /// depth (a jettied building's upper storeys).
        /// </summary>
        private static void BuildStorey(Building building, int storey, StoreyLevel level, List<ModuleSpot> spots, List<Run> runs, float jetty,
            float wallSetback, bool leftToRight, GeneratorSettings settings, System.Random random, PiecePlacer placer,
            Transform group, string label, List<string> log)
        {
            BuildingSpec spec = building.Spec;
            BuildingStyle style = level.Style;
            float windowShare = storey == 0 ? settings.GroundWindowShare : settings.UpperWindowShare;
            PieceAnnotation previous = null;
            // A plain wall that fills its storey (one at random if there are
            // several) stands at every module but the spec's door.
            List<PieceAnnotation> fillers = new List<PieceAnnotation>(style.Modules("wall", storey)).FindAll(piece => piece.fillsStorey);
            PieceAnnotation filler = PiecePlacer.Pick(fillers, null, random);
            IReadOnlyList<PieceAnnotation> filled = filler != null ? new[] { filler } : null;
            // Of the plain walls marked onePerStorey, the storey draws one and
            // keeps to it, for its corners and side walls too.
            level.OnePerStorey = PiecePlacer.Pick(new List<PieceAnnotation>(style.Modules("wall", storey)).FindAll(piece => piece.onePerStorey), null,
                random);
            building.Levels[storey] = level;
            IReadOnlyList<PieceAnnotation> plain = PlainWalls(style.Modules("wall", storey), level);

            for (int k = 0; k < spots.Count; k++) {
                // Counted over the spec's modules (seen from the street), so
                // modules the generator added never take or move the spec's door.
                int index = spots[k].ModuleIndex;
                int fromLeft = index < 0 ? -1 : leftToRight ? index : spec.widthModules - 1 - index;
                // Both drawn for every module, so the sequence doesn't depend
                // on which styles have door or window walls.
                bool doorDrawn = random.NextDouble() < settings.GroundDoorShare;
                bool windowDrawn = random.NextDouble() < windowShare;
                // An overrun module, partly hidden, is always a plain wall;
                // so is a narrow one added to close a run's end (a door or
                // window squeezed into it would show).
                float spacing = Spacing(spots[k], building);
                bool overrun = spots[k].Overrun || spacing < building.Ground.ModuleSpacing - settings.SizeTolerance;
                bool door = storey == 0 && !overrun && (fromLeft == spec.doorModule || (doorDrawn && filler == null));

                // Above its column's top.
                if (storey >= spots[k].Storeys) {
                    continue;
                }

                IReadOnlyList<PieceAnnotation> choices;

                if (door && style.Modules("door-wall", storey).Count > 0) {
                    choices = style.Modules("door-wall", storey);
                } else if (filled != null) {
                    choices = filled;
                } else if (windowDrawn && !overrun && style.Modules("window-wall", storey).Count > 0) {
                    choices = style.Modules("window-wall", storey);
                } else {
                    choices = plain;
                }

                PieceAnnotation piece = PiecePlacer.Pick(choices, previous, random);

                // No wall of the kind drawn: a plain one, else the module is
                // left (reported) and the storey goes on.
                if (piece == null) {
                    piece = PiecePlacer.Pick(plain, previous, random);
                }

                if (piece == null) {
                    Run bare = runs[spots[k].Run];
                    Vector2 at = bare.Start + bare.Along * (spots[k].Position + Spacing(spots[k], building) * 0.5f);
                    GeneratedReport.Add(GeneratedRoot.MarkerKind.Other, new Vector3(at.x, Height(bare, spots[k].Position) + level.Base + 1.5f, at.y),
                        new Vector3(1f, 3f, 1f), $"Wall: {label}, storey {storey} module {k}: style '{style.Name}' has no wall that fits its module.");
                    continue;
                }

                previous = piece;
                ModuleSpot placed = spots[k];
                placed.Depth += jetty;
                // Widened at a corner, out past the run's start or end.
                float widenStart = placed.WidenStart != null ? placed.WidenStart[storey] : 0f;
                float widenEnd = placed.WidenEnd != null ? placed.WidenEnd[storey] : 0f;
                placed.Position -= widenStart;
                GameObject wall = PlaceWall(piece, style, runs[placed.Run], placed, spacing + widenStart + widenEnd, level.Base, wallSetback,
                    settings.WallOverlap, placer, group);

                if (wall != null) {
                    spots[k].Reach[storey] = Reach(piece, placer);
                    spots[k].Walls[storey] = piece;
                    PlaceInserts(piece, wall.transform, windowDrawn && !overrun, settings, random, placer, group);
                }
            }
        }

        /// <summary>
        /// Window inserts in a placed wall's window holes (the catalogue's
        /// window openings): each hole at WindowInsertShare gets one of the
        /// inserts that fit it - at least the hole's width and height, and no
        /// more than WindowInsertMaxMargin bigger on each side - a lit one
        /// (marked lit, its panes glowing) at LitWindowShare, else an unlit
        /// one (either kind when none of the other fits), chosen at random,
        /// never the one before it in this wall. It faces the way the wall
        /// does, its middle on the hole's middle, at the middle of the wall's
        /// depth moved forward by WindowInsertDepth. All three draws are made
        /// for every hole, so the sequence doesn't depend on what fits.
        /// A blind window wall (no hole) with a window point in its annotation
        /// gets an insert on its face at that point when faceWindow (the
        /// module's window draw) is set. Every doorway gets a plain door
        /// (PlaceDoor).
        /// </summary>
        private static void PlaceInserts(PieceAnnotation piece, Transform wall, bool faceWindow, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform group)
        {
            CatalogueEntry entry = placer.Entry(piece);

            if (entry == null) {
                return;
            }

            Vector3 front = piece.fronts[0];
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            PieceAnnotation previous = null;
            PieceAnnotation previousDoor = null;

            foreach (Opening opening in entry.openings) {
                if (opening.kind == "door") {
                    previousDoor = PlaceDoor(opening, piece, entry, wall, previousDoor, settings, random, placer, group);
                }

                if (opening.kind != "window") {
                    continue;
                }

                bool fill = random.NextDouble() < settings.WindowInsertShare;
                bool lit = random.NextDouble() < settings.LitWindowShare;
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

                // The kind drawn, lit or unlit, where one of it fits; else
                // whatever fits, so a hole is never left open for want of
                // one kind.
                List<PieceAnnotation> ofKind = fitting.FindAll(insert => insert.lit == lit);
                PieceAnnotation chosen = PiecePlacer.Pick(ofKind.Count > 0 ? ofKind : fitting, previous, random);

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

            // A blind window wall has no hole, so the scan gives it no
            // opening: a window point in its annotation marks where an insert
            // sits on its face (the pack's own placement: WallTall_WindowBase
            // takes one on its flat panel, PlasterWalls_B_WindiwRoof likewise)
            // - the insert's middle on the point, at the point's depth, no
            // hole to fit, so any usable insert. Filled only where the
            // module's window draw fell (faceWindow: the draw that gives a
            // plaster module a window wall), so a storey of blind walls
            // shows windows at the same share as one of plain and window
            // walls, not on every module.
            if (entry.openings.Exists(opening => opening.kind == "window")) {
                return;
            }

            foreach (ConnectionPoint point in piece.connections) {
                if (!point.name.StartsWith("window")) {
                    continue;
                }

                bool lit = random.NextDouble() < settings.LitWindowShare;
                List<PieceAnnotation> ofKind = _inserts.FindAll(insert => insert.lit == lit);
                PieceAnnotation chosen = PiecePlacer.Pick(ofKind.Count > 0 ? ofKind : _inserts, previous, random);

                if (!faceWindow || chosen == null) {
                    continue;
                }

                previous = chosen;
                Vector3 local = point.position + front * settings.WindowInsertDepth;
                CatalogueEntry insertEntry = placer.Entry(chosen);
                Vector3 insertCentre = (insertEntry.renderMin + insertEntry.renderMax) * 0.5f;
                float yaw = wall.eulerAngles.y + Yaw(front) - Yaw(chosen.fronts[0]);

                placer.PlaceAt(chosen, insertCentre, wall.TransformPoint(local), yaw, group);
            }
        }

        /// <summary>
        /// A plain door in a placed wall's doorway: one of the usable door
        /// pieces at random (never the one before it in this wall), facing the
        /// way the wall does, scaled along its width to the doorway as seen
        /// through the wall (ThroughOpening; the wall's own scale included)
        /// plus DoorSideMargin on each side, and in height to the doorway plus
        /// DoorInsertMargin at the top, so its edges tuck into the frame; the
        /// middle of its bottom on the sill below the middle of the doorway as
        /// seen through, at the middle of the wall's depth moved forward by
        /// DoorInsertDepth. Returns the door placed (the previous one if none
        /// could be).
        /// </summary>
        private static PieceAnnotation PlaceDoor(Opening opening, PieceAnnotation piece, CatalogueEntry entry, Transform wall, PieceAnnotation previous,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group)
        {
            PieceAnnotation door = PiecePlacer.Pick(_doors, previous, random);
            CatalogueEntry doorEntry = door != null ? placer.Entry(door) : null;

            if (doorEntry == null || door.width <= 0f || door.height <= 0f) {
                return previous;
            }

            Vector3 front = piece.fronts[0];
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            Vector3 size = opening.max - opening.min;
            // The wall's own scale along its width and height (its prefab
            // root is unrotated, so these are its local X/Z and Y).
            float wallWidthScale = Mathf.Abs(right.x) >= Mathf.Abs(right.z) ? wall.localScale.x : wall.localScale.z;
            (float throughMin, float throughMax) = ThroughOpening(opening, piece, wall);
            float width = (throughMax - throughMin) * wallWidthScale + 2f * settings.DoorSideMargin;
            float height = size.y * wall.localScale.y + settings.DoorInsertMargin;
            // The middle of the sill below the doorway seen through, moved to
            // the middle of the wall's depth (the doorway is on the back plane).
            Vector3 sill = (opening.min + opening.max) * 0.5f;
            sill += right * ((throughMin + throughMax) * 0.5f - Vector3.Dot(sill, right));
            sill.y = opening.min.y;
            float back = Vector3.Dot(sill, front);
            Vector3 local = sill + front * ((Extent(entry, front) - back) * 0.5f + settings.DoorInsertDepth);
            // The door's own bottom middle.
            Vector3 doorCentre = (doorEntry.renderMin + doorEntry.renderMax) * 0.5f;
            Vector3 doorBottom = new(doorCentre.x, doorEntry.renderMin.y, doorCentre.z);
            float yaw = wall.eulerAngles.y + Yaw(front) - Yaw(door.fronts[0]);

            placer.PlaceAt(door, doorBottom, wall.TransformPoint(local), yaw, PieceScale(door, width / door.width, height / door.height), group);
            return door;
        }

        /// <summary>
        /// A doorway as seen through its wall, along the wall's right (in the
        /// piece's own space, unscaled): rays straight through the wall's LOD0
        /// meshes, at a quarter, half and three quarters of the doorway's
        /// height, stepping out from its middle each way until one meets the
        /// mesh - the narrowest at any height, so a stone frame narrower than
        /// the hole in the back plane (the scan's opening) counts. The scan's
        /// opening where nothing can be seen through its middle. Measured once
        /// per piece and doorway, from the first placed wall.
        /// </summary>
        private static (float min, float max) ThroughOpening(Opening opening, PieceAnnotation piece, Transform wall)
        {
            Vector3 front = piece.fronts[0];
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            float scanMin = Mathf.Min(Vector3.Dot(opening.min, right), Vector3.Dot(opening.max, right));
            float scanMax = Mathf.Max(Vector3.Dot(opening.min, right), Vector3.Dot(opening.max, right));
            string key = $"{piece.guid}/{opening.min}";

            if (_throughOpenings.TryGetValue(key, out (float min, float max) known)) {
                return known;
            }

            // The wall's LOD0 triangles in its own space (its scale undone).
            List<Vector3> triangles = new();
            Matrix4x4 toPiece = wall.worldToLocalMatrix;

            foreach (MeshFilter filter in wall.GetComponentsInChildren<MeshFilter>()) {
                if (filter.sharedMesh == null || WallProbe.IsLowerLodOrCollision(filter.name)) {
                    continue;
                }

                Matrix4x4 toLocal = toPiece * filter.transform.localToWorldMatrix;
                Vector3[] vertices = filter.sharedMesh.vertices;

                foreach (int index in filter.sharedMesh.triangles) {
                    triangles.Add(toLocal.MultiplyPoint3x4(vertices[index]));
                }
            }

            float depthFront = Mathf.Max(Vector3.Dot(opening.min, front), Vector3.Dot(opening.max, front)) + 2f;
            float middle = (scanMin + scanMax) * 0.5f;
            const float Step = 0.01f;
            float min = scanMin - 1f;
            float max = scanMax + 1f;

            // Whether a ray straight back through the wall at a place along
            // it and a height meets the mesh.
            bool Blocked(float along, float y)
            {
                Vector3 origin = right * along + front * depthFront + Vector3.up * y;

                for (int i = 0; i + 2 < triangles.Count; i += 3) {
                    if (WallProbe.RayTriangle(origin, -front, triangles[i], triangles[i + 1], triangles[i + 2], out float hit) && hit < 6f) {
                        return true;
                    }
                }

                return false;
            }

            foreach (float share in new[] { 0.25f, 0.5f, 0.75f }) {
                float y = opening.min.y + (opening.max.y - opening.min.y) * share;

                if (Blocked(middle, y)) {
                    _throughOpenings[key] = (scanMin, scanMax);
                    return (scanMin, scanMax);
                }

                float low = middle;
                float high = middle;

                while (low > min && !Blocked(low - Step, y)) {
                    low -= Step;
                }

                while (high < max && !Blocked(high + Step, y)) {
                    high += Step;
                }

                min = Mathf.Max(min, low);
                max = Mathf.Min(max, high);
            }

            _throughOpenings[key] = (min, max);
            return (min, max);
        }

        /// <summary>
        /// A wall in a module: its left connection point (the base of its
        /// back plane's left end, seen from in front) at the module's left
        /// end on the frontage's back line (moved forward or back by the
        /// module's depth), facing the street. A wall beyond the size
        /// tolerance of its style's module is first scaled to it (width and
        /// height each). A wall wider or narrower than the module (a storey of
        /// another width than the ground storey, a module widened or
        /// narrowed to close a run's end) is scaled along its width to fill
        /// the module exactly (spacing plus the overlap), so the storeys'
        /// joints always line up with no gap. Returns the placed wall, or
        /// null.
        /// </summary>
        private static GameObject PlaceWall(PieceAnnotation piece, BuildingStyle style, Run run, ModuleSpot spot, float spacing, float storeyBase,
            float wallSetback, float wallOverlap, PiecePlacer placer, Transform group, float fitWidth = 0f)
        {
            if (piece.fronts.Count != 1 || !Connection(piece, "left", out Vector3 left)) {
                return null;
            }

            // A corner's filler gives its own width to scale the wall to;
            // a module's wall fills its module.
            float width = fitWidth > 0f ? fitWidth : spacing + wallOverlap;
            float widthScale = width / piece.width;
            float heightScale = style.FitScale(piece.height, style.ModuleHeight);

            Vector2 start = run.Start + run.Along * spot.Position;
            Vector2 end = start + run.Along * spacing;
            // Seen from the street, the viewer's right is up x the way they look.
            Vector3 front = Level(run.Front);
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            Vector2 leftEnd = Vector3.Dot(Level(end - start), right) > 0f ? start : end;
            Vector2 onBackLine = leftEnd + run.Front * (spot.Depth - wallSetback);
            float height = Height(run, spot.Position + spacing * 0.5f) + storeyBase;

            return placer.PlaceAt(piece, left, new Vector3(onBackLine.x, height, onBackLine.y), Yaw(front) - Yaw(piece.fronts[0]),
                PieceScale(piece, widthScale, heightScale), group);
        }

        /// <summary>
        /// A wall's scale along its own axes: widthScale along its width (its
        /// right, seen from in front), heightScale up, its depth unscaled.
        /// </summary>
        internal static Vector3 PieceScale(PieceAnnotation piece, float widthScale, float heightScale)
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
        internal static float PlaceSideWall(PieceAnnotation piece, Vector2 corner, float height, float widthScale, float heightScale, Vector2 face,
            Vector2 back, bool backOnJoin, PiecePlacer placer, Transform group)
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
                PieceScale(piece, widthScale, heightScale), group);
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
        /// One foundation per module, centred on it along the street and
        /// scaled along its width to it, its top FoundationHeight above the
        /// street and its front FoundationFrontOffset out past the walls'
        /// front line (the edge, moved forward or back by the module's
        /// depth).
        /// </summary>
        private static void BuildFoundations(BuildingStyle ground, List<ModuleSpot> spots, List<Run> runs, GeneratorSettings settings,
            System.Random random, PiecePlacer placer, Transform group)
        {
            PieceAnnotation previous = null;

            foreach (ModuleSpot spot in spots) {
                PieceAnnotation piece = PiecePlacer.Pick(ground.Pieces("foundation"), previous, random);
                CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

                if (entry == null || piece.fronts.Count != 1) {
                    // No foundation in the style (or one no longer
                    // catalogued): reported once per building, the walls
                    // still built.
                    Run bare = runs[spot.Run];
                    Vector2 at = bare.Start + bare.Along * (spot.Position + Spacing(spot, ground.ModuleSpacing) * 0.5f);
                    GeneratedReport.Add(GeneratedRoot.MarkerKind.Other, new Vector3(at.x, Height(bare, spot.Position), at.y), new Vector3(1f, 0.5f, 1f),
                        $"Foundation: {group.parent.name}: style '{ground.Name}' has no usable foundation piece.");
                    return;
                }

                previous = piece;

                // The middle of the top of its front face, in its own space.
                Vector3 pieceFront = piece.fronts[0];
                Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
                Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
                Vector3 local = pieceRight * Vector3.Dot(centre, pieceRight)
                    + pieceFront * Extent(entry, pieceFront)
                    + Vector3.up * entry.renderMax.y;

                Run run = runs[spot.Run];
                float spacing = Spacing(spot, ground.ModuleSpacing);
                float middle = spot.Position + spacing * 0.5f;
                Vector2 onEdge = run.Start + run.Along * middle + run.Front * (spot.Depth + settings.FoundationFrontOffset);
                float height = Height(run, middle) + settings.FoundationHeight;

                // Scaled along its width to its module, as the walls are, so
                // neighbours neither gap nor overlap.
                placer.PlaceAt(piece, local, new Vector3(onEdge.x, height, onEdge.y), Yaw(Level(run.Front)) - Yaw(pieceFront),
                    PieceScale(piece, (spacing + settings.WallOverlap) / piece.width, 1f), group);
                previous = StackBesideSteps(ground, run, spot, spacing, height, onEdge, previous, settings, random, placer, group);
            }
        }

        /// <summary>
        /// The highest the street's surface comes in front of a module beside
        /// a flight of steps (StreetSteps.Flight.SurfaceAt), else its level.
        /// </summary>
        private static float SurfaceInFront(Run run, ModuleSpot spot, float spacing)
        {
            if (run.Flight == null) {
                return Height(run, spot.Position + spacing * 0.5f);
            }

            float from = run.FlightOffset + spot.Position;
            return Mathf.Max(run.Flight.SurfaceAt(from), run.Flight.SurfaceAt(from + spacing));
        }

        /// <summary>
        /// Whether the street rises in front of a module above the level it
        /// stands on (a module on the low level beside steps or their landing).
        /// </summary>
        private static bool RisesInFront(Run run, ModuleSpot spot, float spacing, GeneratorSettings settings)
        {
            return SurfaceInFront(run, spot, spacing) > Height(run, spot.Position + spacing * 0.5f) + settings.SizeTolerance;
        }

        /// <summary>
        /// Beside a flight of steps a module standing on the low level has the
        /// street rising in front of it: the gap between the steps or their
        /// landing and its walls is filled with more of its style's
        /// foundations stacked on its own, the top one's top FoundationHeight
        /// above the highest the street's surface comes in front of the module
        /// (StreetSteps.Flight.SurfaceAt), each below it overlapping the one
        /// above by WallOverlap, down to the module's own foundation (whose top
        /// is at baseTop). Placed like it, centred on the module, its front at
        /// onEdge; at random, never the one before. Returns the last placed.
        /// </summary>
        private static PieceAnnotation StackBesideSteps(BuildingStyle ground, Run run, ModuleSpot spot, float spacing, float baseTop, Vector2 onEdge,
            PieceAnnotation previous, GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group)
        {
            if (!RisesInFront(run, spot, spacing, settings)) {
                return previous;
            }

            float top = SurfaceInFront(run, spot, spacing) + settings.FoundationHeight;

            while (top > baseTop + settings.SizeTolerance) {
                PieceAnnotation piece = PiecePlacer.Pick(ground.Pieces("foundation"), previous, random);
                CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

                if (entry == null || piece.fronts.Count != 1 || piece.height <= settings.WallOverlap) {
                    break;
                }

                previous = piece;
                Vector3 pieceFront = piece.fronts[0];
                Vector3 pieceRight = Vector3.Cross(Vector3.up, -pieceFront);
                Vector3 centre = (entry.renderMin + entry.renderMax) * 0.5f;
                Vector3 local = pieceRight * Vector3.Dot(centre, pieceRight) + pieceFront * Extent(entry, pieceFront) + Vector3.up * entry.renderMax.y;
                placer.PlaceAt(piece, local, new Vector3(onEdge.x, top, onEdge.y), Yaw(Level(run.Front)) - Yaw(pieceFront), group);
                top -= entry.renderMax.y - entry.renderMin.y - settings.WallOverlap;
            }

            return previous;
        }

        /// <summary>
        /// The frontage's straight runs, one per segment along its edge: the
        /// edge pushed out from the middle line by half the segment's width,
        /// ending at a bend on the bend's bisector: where the edge meets it on
        /// the street's inside of the bend, where the walls' back line
        /// (setback behind the edge) meets it on its outside. Where the
        /// street itself ends at a frontage's end and another street goes on
        /// from there turning to the facade's inside (an L, with no street
        /// branching off on the facade's side), the run is carried to where
        /// the two edges cross (InsideCorner; back past the point where the block
        /// corner there is wider than a half turn): insideStart and insideEnd say
        /// so for the first and last point, so the first or last building can
        /// overrun behind the other street's facade. Then each run is cut to the longest stretch where the band
        /// from the foundations' front back to depth behind the edge stands in
        /// no other street (StreetClearance.Clear), so it stops clear of a
        /// street branching off at its ends and of any passing close behind; a
        /// run with no such stretch is left out.
        /// </summary>
        private static List<Run> Runs(StreetSpec street, FrontageSpec frontage, float sideSign, float setback, float depth, float margin,
            List<StreetSpec> streets,
            GeneratorSettings settings, out bool insideStart, out bool insideEnd)
        {
            List<Run> runs = new();
            insideStart = false;
            insideEnd = false;

            for (int j = frontage.from; j < frontage.to; j++) {
                if (!EdgeLine(street, j, sideSign, settings, out Vector2 lineStart, out Vector2 along, out float length, out Vector2 outward)) {
                    continue;
                }

                float start = 0f;
                float end = length;

                // At a bend the two runs end at one point (BendEnd): on the
                // street's inside (the facade's outer corner) where the two
                // edges cross, short of the point, and a corner joint carries
                // the walls on from there; on the street's outside (the
                // facade's inside corner, InsideBend) where the walls' back
                // lines cross, past the point: the walls' fronts stand back
                // from the edge by differing amounts, and wherever they stand
                // they meet before the back lines do, so the corner is
                // closed. Never more than CornerMaxReach from the point;
                // nearly straight edges of different widths cross far off,
                // and such a run ends on the bend's bisector instead.
                // On the facade's inside corner the two runs' back lines are
                // crossed with each other, not each with the bisector: with
                // segments of different widths those are two different
                // points, and the runs would overlap short of each other.
                if (j > frontage.from && EdgeLine(street, j - 1, sideSign, settings, out Vector2 beforeStart, out Vector2 beforeAlong, out _, out Vector2 beforeOut)) {
                    float back = Vector2.Dot(along, -beforeOut) > 0f ? setback : 0f;

                    if (BendEnd(lineStart, along, beforeStart, beforeAlong, outward, beforeOut, back, Ground(street.points[j]), 0f, settings, out float t)) {
                        start = Mathf.Clamp(t, -settings.CornerMaxReach, length);
                    }
                }

                if (j < frontage.to - 1 && EdgeLine(street, j + 1, sideSign, settings, out Vector2 afterStart, out Vector2 afterAlong, out _, out Vector2 afterOut)) {
                    float back = Vector2.Dot(afterAlong, -outward) > 0f ? setback : 0f;

                    if (BendEnd(lineStart, along, afterStart, afterAlong, outward, afterOut, back, Ground(street.points[j + 1]), length, settings, out float t)) {
                        end = Mathf.Max(Mathf.Min(t, length + settings.CornerMaxReach), start);
                    }
                }

                // An inside L where the street ends and another goes on: the
                // run is carried to where the two edges cross.
                float crossing = 0f;
                bool insideAtStart = j == 0 && j == frontage.from
                    && InsideCorner(street, 0, lineStart, along, outward, -along, length, true, streets, settings, out crossing);

                if (insideAtStart) {
                    start = crossing;
                }

                bool insideAtEnd = j == street.points.Count - 2 && j == frontage.to - 1
                    && InsideCorner(street, j + 1, lineStart, along, outward, along, length, false, streets, settings, out crossing);

                if (insideAtEnd) {
                    end = crossing;
                }

                // Cut to where its walls and foundations, from the
                // foundations' front back to the deepest of them, stand in
                // no other street: a street branching off at an end, or one
                // passing close behind.
                float clearStart = start;
                float clearEnd = end;
                bool cleared = end - start >= settings.MinSegmentLength
                    && _clearance.Clear(street, j, lineStart, along, outward, settings.FoundationFrontOffset, depth, margin, ref start, ref end);

                if (!cleared) {
                    continue;
                }

                // The first or last building overruns behind the other
                // street's facade only if nothing cut the run short of the
                // corner.
                insideStart |= insideAtStart && start <= clearStart + settings.SizeTolerance;
                insideEnd = insideAtEnd && end >= clearEnd - settings.SizeTolerance;

                // Beside a flight of steps each side is level, changing at the
                // flight's top (moved onto a module's end once they are laid).
                StreetSteps.Flight flight = StreetSteps.Of(_flights, street, j);
                runs.Add(new Run {
                    Start = lineStart + along * start,
                    Along = along,
                    Length = end - start,
                    Front = -outward,
                    StartHeight = flight != null ? street.points[j].y : Mathf.Lerp(street.points[j].y, street.points[j + 1].y, start / length),
                    EndHeight = flight != null ? street.points[j + 1].y : Mathf.Lerp(street.points[j].y, street.points[j + 1].y, end / length),
                    Stepped = flight != null,
                    StepAt = flight != null ? flight.Top - start : 0f,
                    Flight = flight,
                    FlightOffset = start,
                    Street = street,
                    Segment = j
                });
            }

            return runs;
        }

        /// <summary>
        /// Where a run ends at a bend, as a distance along its edge line
        /// (from lineStart along along): where its line (the edge, or the
        /// back line back behind it) crosses the neighbouring segment's -
        /// the one point both runs end at, whatever the two segments' widths
        /// - when that is within CornerMaxReach of the point's foot (at
        /// foot along the line), else where the edge meets the bend's
        /// bisector through the point. False for parallel edges.
        /// </summary>
        private static bool BendEnd(Vector2 lineStart, Vector2 along, Vector2 otherStart, Vector2 otherAlong, Vector2 outward, Vector2 otherOut,
            float back, Vector2 point, float foot, GeneratorSettings settings, out float t)
        {
            if (Intersect(lineStart + outward * back, along, otherStart + otherOut * back, otherAlong, out t) && Mathf.Abs(t - foot) <= settings.CornerMaxReach) {
                return true;
            }

            return Intersect(lineStart + outward * back, along, point, Bisector(outward, otherOut), out t);
        }

        /// <summary>
        /// Where a street ends at a point that another street also starts or ends
        /// at (an L), going on from it turning into the street the facade faces
        /// (the inside of the corner): how far along the facade's edge line
        /// (from lineStart along along) it crosses the other street's edge on
        /// the facade's side of the corner - the edge whose outward way points
        /// on in the direction towards the point (toPoint). The other street is
        /// the one bounding the facade's corner: of every street leaving the
        /// point, the first met turning from the way the run leaves it towards
        /// the facade's side. Where the block's corner there is wider than a
        /// half turn the edges cross beyond the point, and the run is carried
        /// on past it, no further than CornerMaxReach. False if that street
        /// passes through the point (a branch), doesn't turn into the street
        /// the facade faces (a near-straight run on, or a street branching off
        /// on the facade's side), or the edges cross out of reach.
        /// </summary>
        private static bool InsideCorner(StreetSpec street, int pointIndex, Vector2 lineStart, Vector2 along, Vector2 outward, Vector2 toPoint,
            float length, bool atStart, List<StreetSpec> streets, GeneratorSettings settings, out float crossing)
        {
            crossing = 0f;
            Vector2 point = Ground(street.points[pointIndex]);
            // The way the run leaves the point; each street's angle is
            // measured from it turning towards the facade's side.
            Vector2 away = atStart ? along : -along;
            float nearest = float.MaxValue;
            StreetSpec neighbour = null;
            int neighbourPoint = 0;
            Vector2 neighbourWay = Vector2.zero;
            int neighbourSegment = 0;

            foreach (StreetSpec other in streets) {
                if (other == street) {
                    continue;
                }

                for (int j = 0; j < other.points.Count; j++) {
                    if ((Ground(other.points[j]) - point).magnitude > settings.JunctionDistance) {
                        continue;
                    }

                    for (int n = j - 1; n <= j + 1; n += 2) {
                        if (n < 0 || n >= other.points.Count) {
                            continue;
                        }

                        Vector2 leaving = Ground(other.points[n]) - point;

                        if (leaving.magnitude < settings.MinSegmentLength) {
                            continue;
                        }

                        Vector2 way = leaving.normalized;
                        float angle = Mathf.Atan2(Vector2.Dot(way, outward), Vector2.Dot(way, away)) * Mathf.Rad2Deg;
                        angle = angle < 0f ? angle + 360f : angle;

                        if (angle < nearest) {
                            nearest = angle;
                            neighbour = other;
                            neighbourPoint = j;
                            neighbourWay = way;
                            neighbourSegment = Mathf.Min(j, n);
                        }
                    }
                }
            }

            // An L only: the neighbour starts or ends here (one passing
            // through is a branch) and turns into the facade's street.
            if (neighbour == null || (neighbourPoint != 0 && neighbourPoint != neighbour.points.Count - 1)
                || Vector2.Dot(neighbourWay, -outward) <= settings.BranchDot) {
                return false;
            }

            Vector2 normal = new(-neighbourWay.y, neighbourWay.x);

            if (Vector2.Dot(normal, toPoint) < 0f) {
                normal = -normal;
            }

            Vector2 edge = point + normal * (neighbour.SegmentWidth(neighbourSegment) * 0.5f);
            float from = atStart ? -settings.CornerMaxReach : 0f;
            float to = atStart ? length : length + settings.CornerMaxReach;
            return Intersect(lineStart, along, edge, neighbourWay, out crossing) && crossing >= from && crossing <= to;
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
        /// The way along a bend's bisector: between the two segments' ways out
        /// from the street on one side (square to the street when they are in
        /// line).
        /// </summary>
        private static Vector2 Bisector(Vector2 outwardA, Vector2 outwardB)
        {
            Vector2 sum = outwardA + outwardB;
            return sum.sqrMagnitude > 0.0001f ? sum.normalized : outwardA;
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
        /// How far a row of buildings' pieces may stand out past the end of
        /// their module along the street: the most any wall's render box is
        /// wider than its module (trim standing past its back plane), or a
        /// foundation's half its excess over the ground storey's spacing.
        /// </summary>
        private static float Overhang(IEnumerable<BuildingSpec> buildings, Dictionary<string, BuildingStyle> styles, PiecePlacer placer)
        {
            float most = 0f;

            foreach (BuildingSpec building in buildings) {
                for (int s = 0; s < building.storeys.Count; s++) {
                    BuildingStyle style = Find(styles, building.storeys[s]);

                    if (style == null) {
                        continue;
                    }

                    foreach (string category in BuildingStyle.WallCategories) {
                        foreach (PieceAnnotation piece in style.Modules(category)) {
                            CatalogueEntry entry = placer.Entry(piece);

                            if (entry != null && piece.fronts.Count == 1) {
                                Vector3 right = Vector3.Cross(Vector3.up, -piece.fronts[0]);
                                most = Mathf.Max(most, Extent(entry, right) + Extent(entry, -right) - piece.width);
                            }
                        }
                    }

                    if (s > 0) {
                        continue;
                    }

                    foreach (PieceAnnotation piece in style.Pieces("foundation")) {
                        most = Mathf.Max(most, (piece.width - style.ModuleSpacing) * 0.5f);
                    }
                }
            }

            return most;
        }

        /// <summary>
        /// How far back from the street edge a row of buildings' foundations
        /// reach: the deepest foundation of their ground storeys' styles,
        /// less how far its front stands out past the edge.
        /// </summary>
        private static float FoundationBack(IEnumerable<BuildingSpec> buildings, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings)
        {
            float deepest = 0f;

            foreach (BuildingSpec building in buildings) {
                BuildingStyle ground = Find(styles, building.storeys.Count > 0 ? building.storeys[0] : null);

                if (ground == null) {
                    continue;
                }

                foreach (PieceAnnotation piece in ground.Pieces("foundation")) {
                    deepest = Mathf.Max(deepest, piece.depth - settings.FoundationFrontOffset);
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
        /// How far the middle of a wall's render box (in depth) stands out in
        /// front of its back plane (its left point's depth), or -1 if it can't
        /// be told. A beam is centred on it in depth.
        /// </summary>
        internal static float DepthMiddle(PieceAnnotation piece, PiecePlacer placer)
        {
            CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

            if (entry == null || piece.fronts.Count != 1 || !Connection(piece, "left", out Vector3 left)) {
                return -1f;
            }

            Vector3 front = piece.fronts[0];
            return (Extent(entry, front) - Extent(entry, -front)) * 0.5f - Vector3.Dot(left, front);
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
        internal static bool Connection(PieceAnnotation piece, string name, out Vector3 position)
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
        /// The street's height at a distance along a run: either side of a
        /// flight of steps, the level of that side.
        /// </summary>
        private static float Height(Run run, float position)
        {
            if (run.Stepped) {
                return position < run.StepAt ? run.StartHeight : run.EndHeight;
            }

            return Mathf.Lerp(run.StartHeight, run.EndHeight, run.Length > 0f ? Mathf.Clamp01(position / run.Length) : 0f);
        }

        /// <summary>
        /// The street's height a module stands on: at its middle along its run.
        /// </summary>
        private static float SpotGround(ModuleSpot spot, List<Run> runs, List<Building> buildings)
        {
            return Height(runs[spot.Run], spot.Position + Spacing(spot, buildings) * 0.5f);
        }

        /// <summary>
        /// Puts each stepped run's change of level on a module's end, so no
        /// module stands over the flight of steps: climbing along the run, at
        /// the start of the first module wholly past the flight's top (the
        /// modules before it stand on the low level, the street rising against
        /// their fronts); falling along it, at the end of the last module
        /// wholly before the flight's top. Run's end or start if there is none.
        /// </summary>
        private static void StepFlights(List<ModuleSpot> spots, List<Run> runs, List<Building> buildings, GeneratorSettings settings)
        {
            for (int r = 0; r < runs.Count; r++) {
                Run run = runs[r];

                if (!run.Stepped) {
                    continue;
                }

                bool climbing = run.EndHeight > run.StartHeight;
                float step = climbing ? run.Length : 0f;

                foreach (ModuleSpot spot in spots) {
                    if (spot.Run != r) {
                        continue;
                    }

                    float end = spot.Position + Spacing(spot, buildings);

                    if (climbing && spot.Position >= run.StepAt - settings.SizeTolerance) {
                        step = Mathf.Min(step, spot.Position);
                    } else if (!climbing && end <= run.StepAt + settings.SizeTolerance) {
                        step = Mathf.Max(step, end);
                    }
                }

                run.StepAt = step;
                runs[r] = run;
            }
        }

        /// <summary>
        /// The yaw (degrees clockwise from +Z) of a level direction.
        /// </summary>
        internal static float Yaw(Vector3 direction)
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
