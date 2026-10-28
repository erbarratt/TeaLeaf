using System.Collections.Generic;
using UnityEditor;
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
    /// - Pipes (BuildPipes): PipeBuildingShare of the buildings get pipes,
    ///   the rest none. One that does gets at least one run, and runs are
    ///   added until together they are PipeMinRunShare of its width (and
    ///   further ones at PipeExtraRunShare, up to PipeRunsMax).
    ///   A run is vertical, horizontal or an L, made of straight sections and
    ///   elbows, always level or plumb, standing PipeWallGap off the frontmost
    ///   face of the walls it passes (their timbers; a plumb run stands on
    ///   the plaster past a wall's end post, PipePanelInset, else over the
    ///   post); it ends by turning into the wall (an elbow, with a hidden
    ///   straight on into the wall behind it where its leg falls short of
    ///   the plaster) or going into the ground, never along the street. A
    ///   vertical run's top elbow keeps under the eaves (Ceiling); an L may
    ///   end on any storey. A level stretch's ends that turn
    ///   into the wall land near the middle of a module (PipeEndSpread),
    ///   never at a joint between walls. It is kept PipeOpeningMargin clear
    ///   of every door and window hole of the walls it passes, and off a wall
    ///   that shows a blind opening. A vertical run keeps to one column (so
    ///   it meets no inset or outset) and, in the small pipe, jogs out under
    ///   a jetty with two elbows (their legs together being the jetty's
    ///   depth); a horizontal run keeps to a stretch of modules at one depth
    ///   and height. Small or large pipe per run (PipeLargeShare). At
    ///   ValveShare a run gets a valve: a T-junction in a straight stretch,
    ///   its branch pointing out into the street, the valve on the branch.
    ///   Where a level straight stretch goes into a wall, a capped end sits on
    ///   the wall's drawn surface round it, flange to the wall. A run with
    ///   fewer than two straight pieces is taken out.
    /// - Vents (BuildVents): up to VentsMax per building, each at VentShare,
    ///   on a smooth piece of wall (WallProbe), clear of openings and pipes.
    /// - Walkways (BuildWalkways): at WalkwayShare of the buildings with an
    ///   upper storey, one row of the jetty piece along the whole facade at
    ///   the base of one upper storey every column reaches, never a jettied
    ///   first storey's (which has the deck already), the supports' backs in
    ///   one line in front of the storey below's back line (SupportBack, as
    ///   a jetty's).
    /// - Lamp posts (BuildLamps): along each street frontage, one every
    ///   LampSpacing metres on average, LampEdgeInset into the street from
    ///   its edge before a plain ground-storey wall (never a door or a
    ///   window), facing into the street, lowered LampSink into the ground.
    ///   Its light is a gameplay light (a LightSource, Mixed) and stays on.
    ///   Over every street, no two posts stand within LampSpacing of each
    ///   other and no two lights overlap (LampAllowed).
    /// </summary>
    internal static partial class FacadeBuilder
    {
        // The widest pipe (its diameter, in metres) that jogs round a jetty:
        // the thin pipe (the small pieces at half their size) and the small
        // one; the large pipe never crosses a jetty. Kits are chosen per run
        // by diameter.
        private const float SmallPipe = 0.67f;

        /// <summary>
        /// A pipe elbow from its annotated ends a and b (PipeEnds.Bend): its
        /// bend in the piece's own (root, unscaled) space, the ways its legs
        /// run out from the bend (towards a and b) in that space, the radius,
        /// the two legs' lengths in world metres, and how far its body
        /// reaches past the bend away from leg A (Up: above the bend when
        /// leg A points down).
        /// </summary>
        private struct Elbow
        {
            public PieceAnnotation Piece;
            public Vector3 Node;
            public Vector3 DirA;
            public Vector3 DirB;
            public float Radius;
            public float LegA;
            public float LegB;
            public float Up;
            public Vector3 RootScale;
        }

        /// <summary>
        /// One straight pipe piece from its annotated ends: end a in the
        /// piece's own space, the way from a to b there, its radius, and its
        /// length from a to b in world metres at unit scale.
        /// </summary>
        private struct Straight
        {
            public PieceAnnotation Piece;
            public Vector3 Start;
            public Vector3 Axis;
            public float Radius;
            public float Length;
            public Vector3 RootScale;
        }

        /// <summary>
        /// A T-junction from its annotated ends: a and b along the pipe, c
        /// the branch at right angles. Start is end a and Axis the way from a
        /// to b, Branch the way c points, all in the piece's own space;
        /// Length (a to b), ToBranch (a to where the branch leaves the axis)
        /// and Reach (the axis out to c) in world metres.
        /// </summary>
        private struct Tee
        {
            public PieceAnnotation Piece;
            public Vector3 Start;
            public Vector3 Axis;
            public Vector3 Branch;
            public float Length;
            public float ToBranch;
            public float Reach;
            public Vector3 RootScale;
        }

        /// <summary>
        /// A piece fixed by one end (a valve on a T's branch, a capped end on
        /// a pipe, a vent on a wall): that end and the way out of it in the
        /// piece's own space, and how far the piece stands out from it along
        /// that way, in world metres at its own scale.
        /// </summary>
        private struct Fitting
        {
            public PieceAnnotation Piece;
            public Vector3 End;
            public Vector3 Way;
            public float Depth;
            public Vector3 RootScale;
        }

        /// <summary>
        /// The pieces of one pipe size: a straight and an elbow of (nearly)
        /// the same diameter, and the T-junction and capped end of that size
        /// where there are any (Piece null where not).
        /// </summary>
        private struct PipeKit
        {
            public Straight Straight;
            public Elbow Elbow;
            public Tee Tee;
            public Fitting Cap;
            public float Radius;
        }

        // The valve (on a T's branch) and the vents of this generation, from
        // PipeKits; Piece null where there are none.
        private static Fitting _valve;
        private static readonly List<Fitting> _vents = new();
        // What the dressing placed, for the log.
        private static int _valves;
        private static int _caps;
        // Straight pieces placed, so a run with fewer than two is taken out.
        private static int _straights;
        // Runs given a valve, and how many of those had a stretch to put it on.
        private static int _valveRuns;
        private static int _valveTries;
        private static int _runsDropped;

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
        /// in diameter (within a fifth). Pieces come from their annotated
        /// ends a and b (marked in Review Pieces); one without them is left
        /// out and logged. None where either kind is missing.
        /// </summary>
        private static List<PipeKit> PipeKits(PiecePlacer placer, List<string> log)
        {
            List<PipeKit> kits = new();

            if (!_dressing.TryGetValue("pipe-straight", out List<PieceAnnotation> straights) || !_dressing.TryGetValue("pipe-corner", out List<PieceAnnotation> corners)) {
                return kits;
            }

            List<Elbow> elbows = new();

            foreach (PieceAnnotation corner in corners) {
                ConnectionPoint a = PipeEnds.End(corner, "a");
                ConnectionPoint b = PipeEnds.End(corner, "b");

                if (!PipeEnds.Bend(a, b, out Vector3 node)) {
                    log.Add($"Pipes: {corner.name} has no ends a and b at right angles; left out.");
                    continue;
                }

                CatalogueEntry c = placer.Entry(corner);
                elbows.Add(new Elbow {
                    Piece = corner, Node = node, DirA = a.direction, DirB = b.direction,
                    Radius = PipeRadius(c, Vector3.Cross(a.direction, b.direction)),
                    LegA = Vector3.Scale(a.position - node, c.rootScale).magnitude,
                    LegB = Vector3.Scale(b.position - node, c.rootScale).magnitude,
                    Up = BoxReach(c, node, -a.direction) * Vector3.Scale(a.direction, c.rootScale).magnitude,
                    RootScale = c.rootScale
                });
            }

            foreach (PieceAnnotation straight in straights) {
                ConnectionPoint a = PipeEnds.End(straight, "a");
                ConnectionPoint b = PipeEnds.End(straight, "b");

                if (a == null || b == null || Vector3.Distance(a.position, b.position) < 0.01f) {
                    log.Add($"Pipes: {straight.name} has no ends a and b; left out.");
                    continue;
                }

                CatalogueEntry entry = placer.Entry(straight);
                Vector3 axis = (b.position - a.position).normalized;
                // Any direction across the pipe measures its thickness.
                float radius = PipeRadius(entry, Mathf.Abs(axis.y) > 0.5f ? Vector3.right : Vector3.up);
                Elbow best = default;
                float nearest = float.MaxValue;

                foreach (Elbow elbow in elbows) {
                    if (Mathf.Abs(elbow.Radius - radius) < nearest) {
                        nearest = Mathf.Abs(elbow.Radius - radius);
                        best = elbow;
                    }
                }

                if (best.Piece != null && nearest <= radius * 0.2f) {
                    kits.Add(new PipeKit {
                        Straight = new Straight {
                            Piece = straight, Start = a.position, Axis = axis, Radius = radius,
                            Length = Vector3.Scale(b.position - a.position, entry.rootScale).magnitude, RootScale = entry.rootScale
                        },
                        Elbow = best, Tee = TeeFor(radius, placer, log), Cap = CapFor(radius, placer, log), Radius = radius
                    });
                }
            }

            return kits;
        }

        /// <summary>
        /// The valve and the vents, fixed by one end each: the valve by a
        /// (into a T's branch), a vent by b (onto the wall).
        /// </summary>
        private static void ValveAndVents(PiecePlacer placer, List<string> log)
        {
            _valve = default;
            _vents.Clear();

            if (_dressing.TryGetValue("pipe-valve", out List<PieceAnnotation> valves)) {
                foreach (PieceAnnotation valve in valves) {
                    if (MakeFitting(valve, "a", placer, log, out Fitting fitting)) {
                        _valve = fitting;
                        break;
                    }
                }
            }

            if (_dressing.TryGetValue("pipe-vent", out List<PieceAnnotation> vents)) {
                foreach (PieceAnnotation vent in vents) {
                    if (MakeFitting(vent, "b", placer, log, out Fitting fitting)) {
                        _vents.Add(fitting);
                    }
                }
            }
        }

        /// <summary>
        /// The T-junction nearest a pipe's radius (within a fifth), from its
        /// ends a, b (along the pipe) and c (the branch, at right angles to
        /// them); none (Piece null) where there is no such piece.
        /// </summary>
        private static Tee TeeFor(float radius, PiecePlacer placer, List<string> log)
        {
            Tee best = default;
            float nearest = float.MaxValue;

            if (!_dressing.TryGetValue("pipe-tee", out List<PieceAnnotation> tees)) {
                return best;
            }

            foreach (PieceAnnotation tee in tees) {
                ConnectionPoint a = PipeEnds.End(tee, "a");
                ConnectionPoint b = PipeEnds.End(tee, "b");
                ConnectionPoint c = PipeEnds.End(tee, "c");

                if (a == null || b == null || c == null || Vector3.Distance(a.position, b.position) < 0.01f) {
                    log.Add($"Pipes: {tee.name} has no ends a, b and c; left out.");
                    continue;
                }

                Vector3 axis = (b.position - a.position).normalized;

                if (Mathf.Abs(Vector3.Dot(axis, c.direction)) > 0.1f) {
                    log.Add($"Pipes: {tee.name}'s end c is not at right angles to a-b; left out.");
                    continue;
                }

                CatalogueEntry entry = placer.Entry(tee);
                float r = PipeRadius(entry, Vector3.Cross(axis, c.direction));
                float along = Vector3.Dot(c.position - a.position, axis);
                Vector3 onAxis = a.position + axis * along;

                if (Mathf.Abs(r - radius) < nearest) {
                    nearest = Mathf.Abs(r - radius);
                    best = new Tee {
                        Piece = tee, Start = a.position, Axis = axis, Branch = c.direction,
                        Length = Vector3.Scale(b.position - a.position, entry.rootScale).magnitude,
                        ToBranch = Vector3.Scale(onAxis - a.position, entry.rootScale).magnitude,
                        Reach = Vector3.Scale(c.position - onAxis, entry.rootScale).magnitude,
                        RootScale = entry.rootScale
                    };
                }
            }

            return nearest <= radius * 0.2f ? best : default;
        }

        /// <summary>
        /// The capped end nearest a pipe's radius (within a fifth), fixed by
        /// its end b (the flange the pipe meets; its end a is the cap's
        /// face); none (Piece null) where there is no such piece.
        /// </summary>
        private static Fitting CapFor(float radius, PiecePlacer placer, List<string> log)
        {
            Fitting best = default;
            float nearest = float.MaxValue;

            if (!_dressing.TryGetValue("pipe-end", out List<PieceAnnotation> caps)) {
                return best;
            }

            foreach (PieceAnnotation cap in caps) {
                if (!MakeFitting(cap, "b", placer, log, out Fitting fitting)) {
                    continue;
                }

                // Across the cap: any axis at right angles to its way out.
                Vector3 across = Mathf.Abs(fitting.Way.x) > 0.5f ? Vector3.up : Vector3.right;
                float r = PipeRadius(placer.Entry(cap), across);

                if (Mathf.Abs(r - radius) < nearest) {
                    nearest = Mathf.Abs(r - radius);
                    best = fitting;
                }
            }

            return nearest <= radius * 0.2f ? best : default;
        }

        /// <summary>
        /// A piece fixed by one named end: the end, the way it points (into
        /// what the piece is fixed to), and how far the piece reaches back
        /// from it the other way (its render box along that axis). False,
        /// logged, if the end isn't marked.
        /// </summary>
        private static bool MakeFitting(PieceAnnotation piece, string endName, PiecePlacer placer, List<string> log, out Fitting fitting)
        {
            fitting = default;
            ConnectionPoint end = PipeEnds.End(piece, endName);

            if (end == null) {
                log.Add($"Pipes: {piece.name} has no end {endName}; left out.");
                return false;
            }

            CatalogueEntry entry = placer.Entry(piece);
            Vector3 way = end.direction.normalized;
            // From the end back across the piece: the render box's far side
            // against the way out, measured from the end.
            float far = Vector3.Dot(way, end.position) - Mathf.Min(Vector3.Dot(way, entry.renderMin), Vector3.Dot(way, entry.renderMax));
            fitting = new Fitting {
                Piece = piece, End = end.position, Way = way,
                Depth = far * Mathf.Abs(Vector3.Dot(entry.rootScale, new Vector3(Mathf.Abs(way.x), Mathf.Abs(way.y), Mathf.Abs(way.z)))),
                RootScale = entry.rootScale
            };
            return true;
        }

        /// <summary>
        /// Half a pipe piece's render box across one axis of its own, in world
        /// metres (its root's scale applied).
        /// </summary>
        private static float PipeRadius(CatalogueEntry entry, Vector3 across)
        {
            Vector3 size = Vector3.Scale(entry.renderMax - entry.renderMin, entry.rootScale);
            return Mathf.Abs(Vector3.Dot(size, across)) * 0.5f;
        }

        /// <summary>
        /// A rotation that turns the piece's own directions fromA and fromB
        /// (at right angles) to the world's toA and toB.
        /// </summary>
        private static Quaternion Turn(Vector3 fromA, Vector3 fromB, Vector3 toA, Vector3 toB)
        {
            return Orient(toA, toB) * Quaternion.Inverse(Orient(fromA, fromB));
        }

        /// <summary>
        /// Places a straight pipe from one point to another (level or
        /// plumb), its pieces by the fitting rule along the length, each
        /// scaled along its own axis to its share; cross is a direction at
        /// right angles to the pipe its +X is turned to (which way the
        /// flanges' seams face). No capped ends for a stretch hidden inside
        /// a wall (caps false).
        /// </summary>
        private static void PlacePipe(PipeKit kit, Vector3 from, Vector3 to, Vector3 cross, GeneratorSettings settings, PiecePlacer placer, Transform group,
            bool caps = true)
        {
            Vector3 way = to - from;
            float length = way.magnitude;

            if (length <= settings.SizeTolerance) {
                return;
            }

            way /= length;
            Straight straight = kit.Straight;
            // The piece's a-to-b axis turned to the way, and one of its own
            // axes across the pipe (X, or Y for a pipe along X) to cross.
            Vector3 side = Mathf.Abs(straight.Axis.x) > 0.5f ? Vector3.up : Vector3.right;
            Quaternion rotation = Turn(straight.Axis, side, way, cross);
            Vector3 along = new(Mathf.Abs(straight.Axis.x), Mathf.Abs(straight.Axis.y), Mathf.Abs(straight.Axis.z));
            float laid = 0f;

            foreach (float piece in FitWidths(length, straight.Length, 0f, settings)) {
                // Stretched along its axis only; end a stands at this
                // piece's start.
                Vector3 scale = Vector3.one + along * (piece / straight.Length - 1f);
                if (placer.PlaceAt(straight.Piece, Vector3.Scale(straight.Start, straight.RootScale), from + way * laid, rotation, scale, group) != null) {
                    _straights++;
                }

                laid += piece;
            }

            // A level stretch running into a wall (not one it runs along):
            // a capped end where it goes in, from either end.
            if (caps && Mathf.Abs(way.y) < 0.5f) {
                // From the far end too, unless that meets the same thin piece
                // (a beam) the first did.
                bool first = PierceCap(kit, from, way, length, null, placer, group, out Vector3 met);
                PierceCap(kit, to, -way, length, first ? met : (Vector3?)null, placer, group, out _);
            }
        }

        /// <summary>
        /// Places an elbow with its bend at node, leg A pointing wayA and leg
        /// B wayB (at right angles).
        /// </summary>
        private static void PlaceElbow(Elbow elbow, Vector3 node, Vector3 wayA, Vector3 wayB, PiecePlacer placer, Transform group)
        {
            // The pivot is placed so the bend (in root space, scaled by the
            // root) lands on node.
            placer.PlaceAt(elbow.Piece, Vector3.Scale(elbow.Node, elbow.RootScale), node, Turn(elbow.DirA, elbow.DirB, wayA, wayB), Vector3.one, group);
        }

        // How far past the wall's drawn surface the hidden straight behind an
        // elbow's leg runs, and how far a wall is looked for behind the leg.
        private const float HiddenLegRun = 0.35f;
        private const float HiddenLegReach = 1.5f;
        // How far up from a pipe the eaves or anything else over it is looked
        // for, for its top elbow.
        private const float CeilingReach = 3f;

        /// <summary>
        /// Places an elbow whose leg B turns into the wall (its bend at node,
        /// leg A pointing wayA along the pipe, leg B pointing wayIn), and,
        /// where that leg ends short of the wall's drawn surface (the pipe
        /// stands off the timbers, and the plaster lies further back), a
        /// straight from its flange on into the wall, HiddenLegRun past the
        /// surface, so the pipe is seen to go in. Nothing where no surface is
        /// met within HiddenLegReach. The hidden straight is not counted
        /// among the run's straight pieces.
        /// </summary>
        private static void PlaceWallElbow(PipeKit kit, Vector3 node, Vector3 wayA, Vector3 wayIn, GeneratorSettings settings, PiecePlacer placer, Transform group)
        {
            PlaceElbow(kit.Elbow, node, wayA, wayIn, placer, group);

            if (!WallProbe.Cast(node, wayIn, HiddenLegReach, out float surface) || surface <= kit.Elbow.LegB + settings.SizeTolerance) {
                return;
            }

            int straights = _straights;
            PlacePipe(kit, node + wayIn * kit.Elbow.LegB, node + wayIn * (surface + HiddenLegRun), Vector3.up, settings, placer, group, false);
            _straights = straights;
        }

        /// <summary>
        /// The highest a plumb pipe's top elbow may have its bend, for a
        /// point on the pipe's axis: below whatever is met going up from
        /// there (the eaves' underside) by the elbow's reach above its bend
        /// and PipeWallGap, looked for from the axis and from the pipe's
        /// back (nearer the wall, where a soffit comes lower); unbounded
        /// where nothing is met within CeilingReach.
        /// </summary>
        private static float Ceiling(Vector3 axisPoint, Vector3 towardsWall, PipeKit kit, GeneratorSettings settings)
        {
            float lowest = float.MaxValue;

            if (WallProbe.Cast(axisPoint, Vector3.up, CeilingReach, out float hit)) {
                lowest = axisPoint.y + hit;
            }

            if (WallProbe.Cast(axisPoint + towardsWall * kit.Radius, Vector3.up, CeilingReach, out hit)) {
                lowest = Mathf.Min(lowest, axisPoint.y + hit);
            }

            return lowest == float.MaxValue ? float.MaxValue : lowest - kit.Elbow.Up - settings.PipeWallGap;
        }

        /// <summary>
        /// How far a piece's render box reaches from a point of its own space
        /// along a direction (the furthest corner's distance that way).
        /// </summary>
        private static float BoxReach(CatalogueEntry entry, Vector3 from, Vector3 direction)
        {
            float far = float.MinValue;

            for (int i = 0; i < 8; i++) {
                Vector3 corner = new((i & 1) == 0 ? entry.renderMin.x : entry.renderMax.x, (i & 2) == 0 ? entry.renderMin.y : entry.renderMax.y,
                    (i & 4) == 0 ? entry.renderMin.z : entry.renderMax.z);
                far = Mathf.Max(far, Vector3.Dot(corner - from, direction));
            }

            return far;
        }

        // The shortest straight left either side of a valve's T-junction (a
        // stub shorter than this would squash its flanges).
        private const float MinValveStraight = 0.4f;

        /// <summary>
        /// How far out from a pipe's middle a valve on it reaches: its T's
        /// branch and the valve on that (0 where the kit has no T or there
        /// is no valve).
        /// </summary>
        private static float ValveReach(PipeKit kit)
        {
            return kit.Tee.Piece == null || _valve.Piece == null ? 0f : kit.Tee.Reach + _valve.Depth * ValveScale(kit);
        }

        /// <summary>
        /// The valve's scale on a kit's T: the valve is made for the T at its
        /// own size, so it is scaled with the T's root.
        /// </summary>
        private static float ValveScale(PipeKit kit)
        {
            return Mathf.Abs(kit.Tee.RootScale.x) / Mathf.Max(0.001f, Mathf.Abs(_valve.RootScale.x));
        }

        /// <summary>
        /// A straight pipe from one point to another with a valve on it: a
        /// T-junction whose branch leaves the pipe at, metres from the start
        /// and points outward (out from the facade, into the street), the
        /// valve on the branch facing that way, and straight pipe either side.
        /// False, nothing placed, where the kit has no T, there is no valve or
        /// it doesn't fit with MinValveStraight of pipe either side.
        /// </summary>
        private static bool PlaceValvedPipe(PipeKit kit, Vector3 from, Vector3 to, Vector3 cross, Vector3 outward, float at, GeneratorSettings settings,
            PiecePlacer placer, Transform group)
        {
            Tee tee = kit.Tee;
            float length = Vector3.Distance(from, to);

            if (tee.Piece == null || _valve.Piece == null || length <= settings.SizeTolerance) {
                return false;
            }

            // The T's start along the stretch: where asked, slid along as
            // needed so the pipe either side is none at all (the T's flange
            // meets the next piece's) or at least MinValveStraight.
            float room = length - tee.Length;

            if (room < -settings.SizeTolerance) {
                return false;
            }

            float start = NearestStart(at - tee.ToBranch, Mathf.Max(0f, room));
            at = start + tee.ToBranch;
            float end = start + tee.Length;

            Vector3 way = (to - from) / length;
            PlacePipe(kit, from, from + way * start, cross, settings, placer, group);
            placer.PlaceAt(tee.Piece, Vector3.Scale(tee.Start, tee.RootScale), from + way * start, Turn(tee.Axis, tee.Branch, way, outward), Vector3.one, group);
            PlacePipe(kit, from + way * end, to, cross, settings, placer, group);
            // The valve's end a goes into the branch's end c, pointing back at
            // the pipe; its own up stays up where the branch is level.
            Vector3 branchEnd = from + way * at + outward * tee.Reach;
            Vector3 valveUp = Mathf.Abs(_valve.Way.y) > 0.5f ? Vector3.forward : Vector3.up;
            Vector3 worldUp = Mathf.Abs(outward.y) > 0.5f ? way : Vector3.up;
            float scale = ValveScale(kit);
            placer.PlaceAt(_valve.Piece, Vector3.Scale(_valve.End, _valve.RootScale), branchEnd, Turn(_valve.Way, valveUp, -outward, worldUp), Vector3.one * scale, group);
            _valves++;
            return true;
        }

        /// <summary>
        /// Where a T-junction can start along a stretch with room metres to
        /// spare, nearest the wanted start: the pipe left before it and after
        /// it must each be none at all or at least MinValveStraight. So the
        /// allowed starts are 0, room, and MinValveStraight to room less
        /// MinValveStraight.
        /// </summary>
        private static float NearestStart(float wanted, float room)
        {
            float best = 0f;
            float bestOff = Mathf.Abs(wanted);

            if (Mathf.Abs(wanted - room) < bestOff) {
                best = room;
                bestOff = Mathf.Abs(wanted - room);
            }

            float lo = MinValveStraight;
            float hi = room - MinValveStraight;

            if (hi >= lo) {
                float clamped = Mathf.Clamp(wanted, lo, hi);

                if (Mathf.Abs(wanted - clamped) < bestOff) {
                    best = clamped;
                }
            }

            return best;
        }

        /// <summary>
        /// A straight stretch of a run, with a valve on it at metres from the
        /// start when the run was given one (valveAt 0 or more) and it fits;
        /// otherwise plain pipe.
        /// </summary>
        private static void PlaceRunPipe(PipeKit kit, Vector3 from, Vector3 to, Vector3 cross, Vector3 outward, float valveAt, GeneratorSettings settings,
            PiecePlacer placer, Transform group)
        {
            if (valveAt >= 0f) {
                _valveTries++;
            }

            if (valveAt >= 0f && PlaceValvedPipe(kit, from, to, cross, outward, valveAt, settings, placer, group)) {
                return;
            }

            PlacePipe(kit, from, to, cross, settings, placer, group);
        }

        // How far from a stretch's own end a wall must be met for the
        // stretch to count as going into it (nearer is the piece it starts
        // from, or its own end's flange).
        private const float PierceMargin = 0.05f;

        /// <summary>
        /// Where a level straight stretch of pipe (from start, along way,
        /// length long) goes into a wall - the wall's drawn surface met
        /// within its length (WallProbe) - a capped end on the surface round
        /// the pipe: its flange (end b) flat on the wall, its cap side facing
        /// out along the pipe, so the pipe seems to end there. Elbow legs that
        /// turn into the wall get none. Nothing where the kit has no capped
        /// end, or where besides (a cap already placed from the stretch's other
        /// end) is within two caps' depth. True, with the point met, where a
        /// wall was met.
        /// </summary>
        private static bool PierceCap(PipeKit kit, Vector3 start, Vector3 way, float length, Vector3? besides, PiecePlacer placer, Transform group, out Vector3 met)
        {
            Fitting cap = kit.Cap;
            met = default;

            if (cap.Piece == null || length <= PierceMargin * 2f) {
                return false;
            }

            Vector3 origin = start + way * PierceMargin;

            if (!WallProbe.Cast(origin, way, length - PierceMargin * 2f, out float surface)) {
                return false;
            }

            // End b on the wall, its way out (b's direction) pointing into
            // the wall along the pipe.
            Vector3 flange = origin + way * surface;
            met = flange;

            if (besides.HasValue && Vector3.Distance(besides.Value, flange) < cap.Depth * 2f) {
                return true;
            }

            Vector3 capUp = Mathf.Abs(cap.Way.y) > 0.5f ? Vector3.forward : Vector3.up;
            placer.PlaceAt(cap.Piece, Vector3.Scale(cap.End, cap.RootScale), flange, Turn(cap.Way, capUp, way, Vector3.up), Vector3.one, group);
            _caps++;
            return true;
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
        /// How far forward of the street edge the frontmost wall face a plumb
        /// pipe passes stands, over a module's storeys from first to last
        /// (FaceAt: the walls' fronts, the timbers, which stand proudest), so
        /// a pipe PipeWallGap off it clears every post and beam it passes;
        /// false where none of those storeys has a wall.
        /// </summary>
        private static bool FrontmostFace(ModuleSpot spot, Building building, int first, int last, float setback, out float face)
        {
            face = float.MinValue;

            for (int s = first; s <= last; s++) {
                if (FaceAt(spot, building, s, setback, out float storeyFace)) {
                    face = Mathf.Max(face, storeyFace);
                }
            }

            return face > float.MinValue;
        }

        // How far in front of the walls' fronts a probe for the drawn surface
        // starts, and how far in front of a storey's wall front (FaceAt) a hit
        // may lie and still count as the wall (its timbers, beams and stone
        // frames stand within it; a deck, a bracket or the eaves stand
        // further out).
        private const float ProbeOut = 0.6f;
        private const float FaceSlack = 0.25f;
        private const float ProbeStep = 0.25f;
        private const float BandStart = 0.5f;

        /// <summary>
        /// The storey of a building whose band holds a height above the
        /// street (first where below them all), from first to last.
        /// </summary>
        private static int StoreyAt(Building building, float height, int first, int last)
        {
            for (int s = last; s > first; s--) {
                if (height >= building.Levels[s].Base) {
                    return s;
                }
            }

            return first;
        }

        /// <summary>
        /// Where a ray from in front of a module's walls back towards them
        /// meets the drawn surface (WallProbe), as a distance forward of the
        /// street edge, at a point along the run and a height: false where
        /// nothing is met within reach, or the hit stands more than FaceSlack
        /// in front of the storey's wall front (a deck, a bracket, the eaves).
        /// </summary>
        private static bool ProbeFront(Run run, float x, float y, float start, float storeyFace, out float hit)
        {
            Vector3 origin = new(run.Start.x + run.Along.x * x + run.Front.x * start, y, run.Start.y + run.Along.y * x + run.Front.y * start);
            hit = 0f;

            if (!WallProbe.Cast(origin, -Level(run.Front), start + 1.5f, out float distance)) {
                return false;
            }

            hit = start - distance;
            return hit <= storeyFace + FaceSlack;
        }

        /// <summary>
        /// How far forward of the street edge the drawn wall surface a plumb
        /// pipe at x stands against reaches, over a band of storeys (first
        /// to last) between two heights above the street: the frontmost
        /// drawn surface met every ProbeStep up the band (from BandStart
        /// above its foot, over a foundation's plinth or a deck's planks) at
        /// x and the pipe's two sides (ProbeFront, so timbers, beams and
        /// stone frames count and the pipe clears them all, and a deck or
        /// the eaves don't); the walls' frontmost front (FrontmostFace)
        /// where nothing is met. False where no storey of the band has a
        /// wall.
        /// </summary>
        private static bool PlumbFace(Frontage frontage, ModuleSpot spot, Building building, float x, int first, int last, float y0, float y1, PipeKit kit,
            out float face)
        {
            float setback = frontage.WallSetback;

            if (!FrontmostFace(spot, building, first, last, setback, out face)) {
                return false;
            }

            Run run = frontage.Runs[spot.Run];
            float ground = Height(run, x);
            float start = face + ProbeOut;
            float best = float.MinValue;

            for (float y = ground + y0 + BandStart; y <= ground + y1; y += ProbeStep) {
                if (!FaceAt(spot, building, StoreyAt(building, y - ground, first, last), setback, out float storeyFace)) {
                    continue;
                }

                for (int side = -1; side <= 1; side++) {
                    if (ProbeFront(run, x + side * kit.Radius, y, start, storeyFace, out float hit)) {
                        best = Mathf.Max(best, hit);
                    }
                }
            }

            if (best > float.MinValue) {
                face = best;
            }

            return true;
        }

        /// <summary>
        /// How far forward of the street edge the drawn wall surface a level
        /// pipe along a storey stands against reaches, from x0 to x1 over
        /// the modules from first to last, at a height above the street: the
        /// frontmost drawn surface met every ProbeStep along it at the
        /// pipe's middle, top and bottom (ProbeFront); face (the walls'
        /// front) where nothing is met.
        /// </summary>
        private static float LevelFace(Frontage frontage, Building building, int first, int last, int storey, float x0, float x1, float h, PipeKit kit, float face)
        {
            List<ModuleSpot> spots = frontage.Spots;
            Run run = frontage.Runs[spots[first].Run];
            float start = face + ProbeOut;
            float best = float.MinValue;
            int k = first;

            for (float x = x0; x <= x1; x += ProbeStep) {
                while (k < last && x >= spots[k].Position + Spacing(spots[k], building)) {
                    k++;
                }

                if (!FaceAt(spots[k], building, storey, frontage.WallSetback, out float storeyFace)) {
                    continue;
                }

                float ground = Height(run, x);

                for (int side = -1; side <= 1; side++) {
                    if (ProbeFront(run, x, ground + h + side * kit.Radius, start, storeyFace, out float hit)) {
                        best = Mathf.Max(best, hit);
                    }
                }
            }

            return best > float.MinValue ? best : face;
        }

        /// <summary>
        /// Where along its run a plumb run of pipe may stand in a module, in
        /// order of preference, each with the first and last module whose
        /// openings it must keep clear of: PipePanelInset from the module's
        /// left end as seen from the street, on the plaster past the wall's
        /// end post (the post stands at every wall's left end); then at the
        /// module's start edge, over the post, where both it and the module
        /// before are checked.
        /// </summary>
        private static IEnumerable<(float x, int firstModule, int lastModule)> PlumbPlaces(Frontage frontage, Building building, int first, int at, PipeKit kit,
            GeneratorSettings settings)
        {
            ModuleSpot spot = frontage.Spots[at];
            Run run = frontage.Runs[spot.Run];
            // Seen from the street, the viewer's right is up x the way they look.
            Vector3 worldRight = Vector3.Cross(Vector3.up, -Level(run.Front));
            bool leftIsStart = Vector3.Dot(Level(run.Along), worldRight) > 0f;
            float spacing = Spacing(spot, building);
            float inset = Mathf.Min(settings.PipePanelInset, spacing - kit.Radius - settings.PipeWallGap);
            yield return (leftIsStart ? spot.Position + inset : spot.Position + spacing - inset, at, at);
            yield return (spot.Position + kit.Radius + 0.05f, Mathf.Max(first, at - 1), at);
        }

        /// <summary>
        /// The pipes of the buildings that get any (see the class summary):
        /// each building is rolled against PipeBuildingShare before anything
        /// else, always, so the random sequence doesn't depend on what fits.
        /// Logs how many buildings got pipes, how many runs were placed and
        /// the buildings given pipes that could fit none.
        /// </summary>
        private static void BuildPipes(List<Frontage> frontages, GeneratorSettings settings, System.Random random, PiecePlacer placer, List<string> log)
        {
            WallProbe.Begin();
            _valves = 0;
            _valveRuns = 0;
            _valveTries = 0;
            _runsDropped = 0;
            _caps = 0;
            ValveAndVents(placer, log);
            List<PipeKit> kits = PipeKits(placer, log);

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
            int buildings = 0;
            int piped = 0;
            List<string> none = new();

            foreach (Frontage frontage in frontages) {
                for (int b = 0; b < frontage.Buildings.Count; b++) {
                    buildings++;

                    // Rolled for every building, so skipping one changes
                    // nothing else in the sequence.
                    if (random.NextDouble() >= settings.PipeBuildingShare) {
                        continue;
                    }

                    piped++;
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

            log.Add($"Pipes: {piped} of {buildings} building(s) given pipes, {runsPlaced} run(s), {_valves} valve(s) ({_valveRuns} run(s) given one, {_valveTries} with a stretch clear of the lane), {_caps} capped end(s) where a straight goes into a wall, {_runsDropped} run(s) dropped for fewer than two straight pieces"
                + (none.Count > 0 ? $"; none fitted on {string.Join(", ", none)}." : "."));
        }

        /// <summary>
        /// Every building's vents (see the class summary): up to VentsMax, each
        /// at VentShare, large at VentLargeShare. A vent is tried at random
        /// places on the facade until one fits: on one module's wall within a
        /// storey, PipeOpeningMargin clear of every door and window, out of the
        /// street's lane, apart from the pipes and other vents, and on a
        /// smooth piece of wall - the drawn surface at its middle and four
        /// corners within VentFlatTolerance in depth. It is seated on the
        /// frontmost of those points, its end b on the wall and its face out
        /// into the street. Under "Building i/Vents". Logs the count.
        /// </summary>
        private static void BuildVents(List<Frontage> frontages, GeneratorSettings settings, System.Random random, PiecePlacer placer, List<string> log)
        {
            if (_vents.Count == 0 || settings.VentsMax == 0) {
                log.Add("Vents: none (no verified pipe-vent piece with an end b, or ventsMax is 0).");
                return;
            }

            // The large vent is the widest, the small the narrowest.
            Fitting large = _vents[0];
            Fitting small = _vents[0];

            foreach (Fitting vent in _vents) {
                if (VentSize(vent, placer).x > VentSize(large, placer).x) {
                    large = vent;
                }

                if (VentSize(vent, placer).x < VentSize(small, placer).x) {
                    small = vent;
                }
            }

            WallProbe.Begin();
            int placed = 0;
            int wantedCount = 0;

            foreach (Frontage frontage in frontages) {
                for (int b = 0; b < frontage.Buildings.Count; b++) {
                    int first = frontage.Spots.FindIndex(spot => spot.Building == b);
                    int last = frontage.Spots.FindLastIndex(spot => spot.Building == b);

                    if (first < 0) {
                        continue;
                    }

                    for (int i = 0; i < settings.VentsMax; i++) {
                        bool wanted = random.NextDouble() < settings.VentShare;
                        Fitting vent = random.NextDouble() < settings.VentLargeShare ? large : small;

                        if (!wanted) {
                            continue;
                        }

                        wantedCount++;

                        if (PlaceVent(frontage, b, first, last, vent, settings, random, placer)) {
                            placed++;
                        }
                    }
                }
            }

            log.Add($"Vents: {placed} of {wantedCount} wanted (the rest found no smooth piece of wall clear of openings and pipes).");
        }

        /// <summary>
        /// A vent's size as it stands on a wall: across (its own X), up (its
        /// own Z, turned to stand upright) and out (along its way off the
        /// wall), in world metres.
        /// </summary>
        private static Vector3 VentSize(Fitting vent, PiecePlacer placer)
        {
            CatalogueEntry entry = placer.Entry(vent.Piece);
            Vector3 size = Vector3.Scale(entry.renderMax - entry.renderMin, entry.rootScale);
            Vector3 up = VentUp(vent);
            Vector3 across = Vector3.Cross(up, vent.Way);
            return new Vector3(Mathf.Abs(Vector3.Dot(size, Abs(across))), Mathf.Abs(Vector3.Dot(size, Abs(up))), vent.Depth);
        }

        /// <summary>
        /// The vent's own axis that stands upright on the wall: Z, or Y for a
        /// vent whose way off the wall is along Z.
        /// </summary>
        private static Vector3 VentUp(Fitting vent)
        {
            return Mathf.Abs(vent.Way.z) > 0.5f ? Vector3.up : Vector3.forward;
        }

        /// <summary>
        /// A vector's components made positive.
        /// </summary>
        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        // The spacing of the points a vent's wall is probed at for
        // smoothness: under the narrowest timber (about 0.15m across).
        private const float VentProbeSpacing = 0.1f;
        // How many random places one vent is tried at.
        private const int VentTries = 40;
        // The grid points a vent's wall is probed at, in the order probed.
        private static readonly List<(int, int)> _ventProbes = new();

        /// <summary>
        /// Tries one vent at up to VentTries random places on a building's
        /// facade (see BuildVents); true once one is placed.
        /// </summary>
        private static bool PlaceVent(Frontage frontage, int b, int first, int last, Fitting vent, GeneratorSettings settings, System.Random random,
            PiecePlacer placer)
        {
            Building building = frontage.Buildings[b];
            List<ModuleSpot> spots = frontage.Spots;
            Vector3 size = VentSize(vent, placer);
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;

            for (int attempt = 0; attempt < VentTries; attempt++) {
                int k = first + random.Next(last - first + 1);
                ModuleSpot spot = spots[k];
                int storey = random.Next(Mathf.Max(1, spot.Storeys));
                float alongShare = (float)random.NextDouble();
                float upShare = (float)random.NextDouble();

                if (storey >= building.Levels.Count || !FaceAt(spot, building, storey, frontage.WallSetback, out float face)) {
                    continue;
                }

                float spacing = Spacing(spot, building);
                StoreyLevel level = building.Levels[storey];
                // Inside the module and the storey, a little in from their
                // edges (the trim along them).
                float room = spacing - size.x - 0.3f;
                float headroom = level.Style.StoreySpacing - size.y - 0.4f;

                if (room <= 0f || headroom <= 0f) {
                    continue;
                }

                Run run = frontage.Runs[spot.Run];
                float x = spot.Position + 0.15f + halfWidth + room * alongShare;
                float h = level.Base + 0.2f + halfHeight + headroom * upShare;
                float ground = Height(run, x);

                if (CrossesOpening(spots, k, k, building, frontage.Runs, x - halfWidth, x + halfWidth, h - halfHeight, h + halfHeight, storey, storey, placer, settings)
                    || !ClearOfLane(frontage, run, x - halfWidth, x + halfWidth, face + size.z, storey, settings)) {
                    continue;
                }

                Vector3 front = Level(run.Front);
                Vector3 alongWorld = Level(run.Along);
                Vector3 Point(float px, float py, float d) => new(run.Start.x + run.Along.x * px + run.Front.x * d, ground + py,
                    run.Start.y + run.Along.y * px + run.Front.y * d);

                // Apart from the pipes and other vents: nothing of the
                // dressing's in the box the vent would fill (grown a little).
                Vector3 middle = Point(x, h, face + size.z * 0.5f);

                if (DressingWithin(middle, new Vector3(halfWidth + 0.1f, halfHeight + 0.1f, size.z * 0.5f + 0.3f), Quaternion.LookRotation(front))) {
                    continue;
                }

                // Smooth: the drawn surface over a grid of points across the
                // vent, close enough that no timber passes between them, lies
                // within VentFlatTolerance in depth.
                float reach = face + 1f;
                float nearest = float.MaxValue;
                float farthest = float.MinValue;
                bool smooth = true;
                int across = Mathf.CeilToInt(size.x / VentProbeSpacing) + 1;
                int up = Mathf.CeilToInt(size.y / VentProbeSpacing) + 1;
                // The middle and the four corners first, so most places that
                // aren't smooth are turned down after a probe or two.
                _ventProbes.Clear();
                _ventProbes.Add((across / 2, up / 2));
                _ventProbes.Add((0, 0));
                _ventProbes.Add((across - 1, up - 1));
                _ventProbes.Add((0, up - 1));
                _ventProbes.Add((across - 1, 0));

                for (int i = 0; i < across; i++) {
                    for (int j = 0; j < up; j++) {
                        _ventProbes.Add((i, j));
                    }
                }

                foreach ((int i, int j) in _ventProbes) {
                    float px = -halfWidth + size.x * i / (across - 1);
                    float py = -halfHeight + size.y * j / (up - 1);

                    if (!WallProbe.Cast(Point(x + px, h + py, reach), -front, 2.5f, out float d)) {
                        smooth = false;
                        break;
                    }

                    nearest = Mathf.Min(nearest, d);
                    farthest = Mathf.Max(farthest, d);

                    if (farthest - nearest > settings.VentFlatTolerance) {
                        smooth = false;
                        break;
                    }
                }

                if (!smooth || farthest - nearest > settings.VentFlatTolerance) {
                    continue;
                }

                // Seated on the frontmost point: end b on the wall, pointing
                // into it.
                Vector3 seat = Point(x, h, reach) - front * nearest;
                Transform group = Group(building.Group, "Vents");
                GameObject placed = placer.PlaceAt(vent.Piece, Vector3.Scale(vent.End, vent.RootScale), seat, Turn(vent.Way, VentUp(vent), -front, Vector3.up), Vector3.one, group);
                // Steam out of its face, at VentSteamShare (a draw every time).
                bool steams = random.NextDouble() < settings.VentSteamShare;

                if (steams && placed != null && settings.VentSteamPrefab != null) {
                    PlaceEffect(settings.VentSteamPrefab, placed.transform, seat + front * size.z, front);
                }
                Physics.SyncTransforms();
                return true;
            }

            return false;
        }

        /// <summary>
        /// An effect prefab (the smoke or steam particle system) placed at a
        /// world point, its forward (the way its particles set off) a way,
        /// as a child of the piece it rises from, so it goes wherever the
        /// piece goes (the roof cull, a hidden piece) and stays an instance
        /// of its own prefab.
        /// </summary>
        private static void PlaceEffect(GameObject prefab, Transform piece, Vector3 point, Vector3 forward)
        {
            GameObject effect = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, piece);
            // Straight up needs an up vector of its own.
            Vector3 up = Mathf.Abs(forward.y) > 0.9f ? Vector3.forward : Vector3.up;
            effect.transform.SetPositionAndRotation(point, Quaternion.LookRotation(forward, up));
        }

        /// <summary>
        /// Whether any of the dressing (a pipe or a vent: a TL_Pipe piece)
        /// has a collider in a box.
        /// </summary>
        private static bool DressingWithin(Vector3 centre, Vector3 halfExtents, Quaternion rotation)
        {
            Collider[] hits = Physics.OverlapBox(centre, halfExtents, rotation, ~0, QueryTriggerInteraction.Collide);

            foreach (Collider hit in hits) {
                GameObject root = UnityEditor.PrefabUtility.GetOutermostPrefabInstanceRoot(hit.gameObject);

                if ((root != null ? root : hit.gameObject).name.StartsWith("TL_Pipe")) {
                    return true;
                }
            }

            return false;
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
                bool valve = random.NextDouble() < settings.ValveShare;
                int shape = random.Next(3);
                int spot = first + random.Next(last - first + 1);
                float where = (float)random.NextDouble();
                int storey = random.Next(Mathf.Max(1, building.Levels.Count));
                // Where in their modules a level stretch's two ends land.
                float startRoll = (float)random.NextDouble();
                float endRoll = (float)random.NextDouble();
                group ??= Group(building.Group, "Pipes");
                // Each run is built in a group of its own, so one with fewer
                // than two straight pieces can be taken out whole.
                Transform runGroup = Group(group, "Run");
                int straights = _straights;
                int valves = _valves;
                int caps = _caps;
                int valveTries = _valveTries;
                int takenCount = taken.Count;
                float length = shape switch {
                    0 => VerticalRun(frontage, building, first, last, spot, kit, valve, taken, settings, placer, runGroup),
                    1 => HorizontalRun(frontage, building, first, last, spot, storey, where, startRoll, endRoll, kit, valve, taken, settings, placer,
                        runGroup),
                    _ => CornerRun(frontage, building, first, last, spot, storey, where, endRoll, kit, valve, taken, settings, placer, runGroup)
                };

                if (length > 0f && _straights - straights < 2) {
                    _straights = straights;
                    _valves = valves;
                    _caps = caps;
                    _valveTries = valveTries;
                    taken.RemoveRange(takenCount, taken.Count - takenCount);
                    _runsDropped++;
                    length = 0f;
                }

                // A kept run's pieces go straight under Pipes.
                while (length > 0f && runGroup.childCount > 0) {
                    runGroup.GetChild(0).SetParent(group, true);
                }

                Object.DestroyImmediate(runGroup.gameObject);

                if (length > 0f) {
                    _valveRuns += valve ? 1 : 0;
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
        /// A plumb run of pipe in a module (PlumbPlaces: on the plaster past
        /// the wall's end post, else over the post at the module's start
        /// edge), from the top of its column down into the ground: an elbow
        /// turning into the wall at the top, under the eaves (Ceiling),
        /// straight sections down the storeys' faces, and under a jetty
        /// (small pipe only) two elbows jogging the pipe back to the ground
        /// storey's face. Nothing where it would cross a door or window, pass
        /// another run, or (large pipe) cross a jetty. Returns the length
        /// placed, 0 for none.
        /// </summary>
        private static float VerticalRun(Frontage frontage, Building building, int first, int last, int at, PipeKit kit, bool valve,
            List<(float x0, float x1, float y0, float y1)> taken, GeneratorSettings settings, PiecePlacer placer, Transform group)
        {
            List<ModuleSpot> spots = frontage.Spots;
            ModuleSpot spot = spots[at];
            Run run = frontage.Runs[spot.Run];
            float setback = frontage.WallSetback;
            float offset = settings.PipeWallGap + kit.Radius;
            int storeys = spot.Storeys;

            if (storeys == 0 || !FaceAt(spot, building, 0, setback, out float groundFace)) {
                return 0f;
            }

            bool jettied = building.Jetty > 0f && storeys > 1;

            if (jettied && kit.Radius * 2f > SmallPipe) {
                return 0f;
            }

            Vector2 along = run.Along;
            Vector3 front = Level(run.Front);
            Vector3 alongWorld = Level(along);
            StoreyLevel topLevel = building.Levels[storeys - 1];
            float storeyTop = topLevel.Base + topLevel.Style.StoreySpacing;
            float deckHeight = jettied ? JettyDeckTop(building.Levels[1], settings) : 0f;

            foreach ((float x, int firstModule, int lastModule) in PlumbPlaces(frontage, building, first, at, kit, settings)) {
                float ground = Height(run, x);
                float x0 = x - kit.Radius;
                float x1 = x + kit.Radius;
                Vector3 Point(float d, float y) => new(run.Start.x + along.x * x + run.Front.x * d, y, run.Start.y + along.y * x + run.Front.y * d);
                // The top elbow's bend: under the top storey's top, and under
                // the eaves over the pipe - first off the walls' fronts, to
                // know how high the straight pipe goes, then off where the
                // pipe stands.
                FrontmostFace(spot, building, jettied ? 1 : 0, storeys - 1, setback, out float frontGuess);
                float top = ground + storeyTop - kit.Elbow.LegA - kit.Radius;
                top = Mathf.Min(top, Ceiling(Point(frontGuess + offset, top - 1.5f), -front, kit, settings));
                // A plumb pipe stands at one offset down every storey it
                // passes: off the frontmost drawn surface along it (PlumbFace:
                // timbers, beams, stone frames; up to the straight pipe's top,
                // short of the eaves), so it clears them all and hangs free
                // before a storey standing further back (the ground storey's
                // masonry). Under a jetty the ground storey has its own.
                float upperFace = groundFace;
                float straightTop = top - ground - kit.Radius;

                if (jettied) {
                    PlumbFace(frontage, spot, building, x, 1, storeys - 1, deckHeight, straightTop, kit, out upperFace);
                    PlumbFace(frontage, spot, building, x, 0, 0, 0f, deckHeight, kit, out groundFace);
                } else {
                    PlumbFace(frontage, spot, building, x, 0, storeys - 1, 0f, straightTop, kit, out groundFace);
                }

                float topFace = jettied ? upperFace : groundFace;
                top = Mathf.Min(top, Ceiling(Point(topFace + offset, top - 1.5f), -front, kit, settings));

                if (top - ground < kit.Elbow.LegA + kit.Radius + 1f
                    || CrossesOpening(spots, firstModule, lastModule, building, frontage.Runs, x0, x1, 0f, top - ground, 0, storeys - 1, placer, settings)
                    || Taken(taken, x0, x1, ground, top) || !ClearOfLane(frontage, run, x0, x1, groundFace + offset + kit.Radius, 0, settings)) {
                    continue;
                }

                // A valve at ValveHeight on the stretch down to the ground,
                // where it stays out of the street's lane.
                bool withValve = valve && ClearOfLane(frontage, run, x0, x1, groundFace + offset + ValveReach(kit), 0, settings);
                float valveY = ground + settings.ValveHeight;

                // The top elbow: its leg A down the pipe, leg B into the wall.
                Vector3 topNode = Point(topFace + offset, top);
                PlaceWallElbow(kit, topNode, Vector3.down, -front, settings, placer, group);
                float length = kit.Elbow.LegA + kit.Elbow.LegB;

                if (!jettied) {
                    Vector3 from = topNode + Vector3.down * kit.Elbow.LegA;
                    Vector3 to = Point(groundFace + offset, ground - 0.3f);
                    PlaceRunPipe(kit, from, to, alongWorld, front, withValve ? from.y - valveY : -1f, settings, placer, group);
                    length += Vector3.Distance(from, to);
                    taken.Add((x0, x1, ground, top));
                    return length;
                }

                // Under the jetty: down the upper face to the deck, an elbow
                // turning back towards the wall, one turning down again at
                // the ground face, then down into the ground. The two legs
                // between them span the jetty (the small pipe's legs are made
                // for it); where they don't meet the jog exactly, a short
                // straight between them along the way in.
                float deck = ground + deckHeight;
                float outerY = deck - kit.Radius - kit.Elbow.LegA * 0.5f;
                Vector3 upperFrom = topNode + Vector3.down * kit.Elbow.LegA;
                Vector3 outerNode = Point(upperFace + offset, outerY);
                PlacePipe(kit, upperFrom, outerNode + Vector3.up * kit.Elbow.LegA, alongWorld, settings, placer, group);
                PlaceElbow(kit.Elbow, outerNode, Vector3.up, -front, placer, group);
                Vector3 legEnd = outerNode - front * kit.Elbow.LegB;
                // The inner elbow stands at the ground face's offset, level
                // with the outer one: its leg A points back out towards the
                // outer elbow's leg, its leg B down.
                Vector3 innerNode = Point(groundFace + offset, outerY);
                PlaceElbow(kit.Elbow, innerNode, front, Vector3.down, placer, group);
                Vector3 innerLeg = innerNode + front * kit.Elbow.LegA;

                if (Vector3.Distance(legEnd, innerLeg) > settings.SizeTolerance) {
                    PlacePipe(kit, legEnd, innerLeg, Vector3.up, settings, placer, group);
                }

                Vector3 down = innerNode + Vector3.down * kit.Elbow.LegB;
                Vector3 bottom = Point(groundFace + offset, ground - 0.3f);
                PlaceRunPipe(kit, down, bottom, alongWorld, front, withValve ? down.y - valveY : -1f, settings, placer, group);
                length += Vector3.Distance(upperFrom, outerNode) + kit.Elbow.LegA * 2f + kit.Elbow.LegB * 2f + Vector3.Distance(down, bottom);
                taken.Add((x0, x1, ground, top));
                return length;
            }

            return 0f;
        }

        /// <summary>
        /// A level run of pipe along one storey, from near the middle of a
        /// module on along as many modules of the same column depth and at
        /// least that storey as it reaches, to near the middle of another
        /// (PipeEndInModule, at startRoll and endRoll; within one module where
        /// the stretch is one module), turning into the wall at both ends, at
        /// a height clear of the storey's openings (below their sills, else
        /// above them). Returns the length placed.
        /// </summary>
        private static float HorizontalRun(Frontage frontage, Building building, int first, int last, int at, int storey, float where, float startRoll,
            float endRoll, PipeKit kit, bool valve, List<(float x0, float x1, float y0, float y1)> taken, GeneratorSettings settings, PiecePlacer placer,
            Transform group)
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
            // Shorter than the whole stretch, sometimes: to a module chosen
            // by where, at least the next one where there is one. Within one
            // module, the start in its first half and the end in its second.
            int stop = end > at ? at + Mathf.Max(1, Mathf.RoundToInt(where * (end - at))) : at;
            float x0 = PipeEndInModule(spots[at], building, stop == at ? startRoll * 0.5f : startRoll, settings);
            float x1 = PipeEndInModule(spots[stop], building, stop == at ? 0.5f + endRoll * 0.5f : endRoll, settings);

            if (x1 - x0 < kit.Elbow.LegA * 2f + settings.SizeTolerance) {
                return 0f;
            }

            StoreyLevel level = building.Levels[storey];
            float ground = Height(run, (x0 + x1) * 0.5f);
            float[] heights = { level.Base + 0.45f + kit.Radius, level.Base + level.Style.StoreySpacing - 0.45f - kit.Radius };

            foreach (float h in heights) {
                float y = ground + h;

                if (CrossesOpening(spots, at, end, building, frontage.Runs, x0, x1, h - kit.Radius, h + kit.Radius, storey, storey, placer, settings)
                    || Taken(taken, x0, x1, y - kit.Radius, y + kit.Radius)) {
                    continue;
                }

                // Off the frontmost drawn surface along the stretch (timbers,
                // beams), so the pipe clears them.
                float offset = LevelFace(frontage, building, at, end, storey, x0, x1, h, kit, face) + settings.PipeWallGap + kit.Radius;

                if (!ClearOfLane(frontage, run, x0, x1, offset + kit.Radius, storey, settings)) {
                    continue;
                }

                // A valve in the middle of the level stretch, where it stays
                // out of the street's lane.
                bool withValve = valve && ClearOfLane(frontage, run, x0, x1, offset + ValveReach(kit), storey, settings);

                Vector3 front = Level(run.Front);
                Vector3 alongWorld = Level(run.Along);
                Vector3 Point(float x) => new(run.Start.x + run.Along.x * x + run.Front.x * offset, y, run.Start.y + run.Along.y * x + run.Front.y * offset);
                Vector3 a = Point(x0);
                Vector3 bEnd = Point(x1);
                // Both elbows: leg A along the pipe, leg B into the wall.
                PlaceWallElbow(kit, a, alongWorld, -front, settings, placer, group);
                PlaceWallElbow(kit, bEnd, -alongWorld, -front, settings, placer, group);
                Vector3 from = a + alongWorld * kit.Elbow.LegA;
                Vector3 to = bEnd - alongWorld * kit.Elbow.LegA;
                PlaceRunPipe(kit, from, to, Vector3.up, front, withValve ? Vector3.Distance(from, to) * 0.5f : -1f, settings, placer, group);
                taken.Add((x0, x1, y - kit.Radius, y + kit.Radius));
                return x1 - x0 + kit.Elbow.LegB * 2f;
            }

            return 0f;
        }

        /// <summary>
        /// An L of pipe: up from the ground in a module (PlumbPlaces) to a
        /// height on a storey, an elbow, then level along the storey to an
        /// elbow into the wall near the middle of that module or a later one
        /// (PipeEndInModule, at endRoll; where picks the module over the
        /// stretch the level part may reach) - at the storey's middle
        /// height, else near its top, else near its base, whichever first
        /// clears the openings. The whole L stands off the frontmost face of
        /// the storeys the vertical passes. Where the vertical would cross a
        /// jetty it is stopped at the ground storey instead. Returns the
        /// length placed.
        /// </summary>
        private static float CornerRun(Frontage frontage, Building building, int first, int last, int at, int storey, float where, float endRoll, PipeKit kit,
            bool valve, List<(float x0, float x1, float y0, float y1)> taken, GeneratorSettings settings, PiecePlacer placer, Transform group)
        {
            List<ModuleSpot> spots = frontage.Spots;
            float setback = frontage.WallSetback;
            ModuleSpot spot = spots[at];

            // The vertical keeps to the storeys at the ground face: below a
            // jetty, the ground storey only.
            if (building.Jetty > 0f) {
                storey = 0;
            }

            if (storey >= spot.Storeys || !FaceAt(spot, building, storey, setback, out float storeyFace)
                || !FrontmostFace(spot, building, 0, storey, setback, out float face)) {
                return 0f;
            }

            int end = at;

            while (end + 1 <= last && spots[end + 1].Run == spot.Run && spots[end + 1].Storeys > storey
                && Mathf.Abs(spots[end + 1].Depth - spot.Depth) < SameDepth && FaceAt(spots[end + 1], building, storey, setback, out float f)
                && Mathf.Abs(f - storeyFace) < SameDepth) {
                end++;
            }

            Run run = frontage.Runs[spot.Run];
            int stop = at + Mathf.RoundToInt(where * (end - at));
            float x1 = PipeEndInModule(spots[stop], building, endRoll, settings);
            StoreyLevel level = building.Levels[storey];
            float[] heights = {
                level.Base + level.Style.StoreySpacing * 0.5f, level.Base + level.Style.StoreySpacing - 0.45f - kit.Radius, level.Base + 0.45f + kit.Radius
            };
            Vector3 front = Level(run.Front);
            Vector3 alongWorld = Level(run.Along);

            foreach ((float x, int firstModule, int lastModule) in PlumbPlaces(frontage, building, first, at, kit, settings)) {
                if (x1 - x < kit.Elbow.LegA + kit.Elbow.LegB + settings.SizeTolerance) {
                    continue;
                }

                float ground = Height(run, x);

                foreach (float h in heights) {
                    float y = ground + h;

                    if (CrossesOpening(spots, firstModule, lastModule, building, frontage.Runs, x - kit.Radius, x + kit.Radius, 0f, y - ground, 0, storey, placer, settings)
                        || CrossesOpening(spots, at, end, building, frontage.Runs, x, x1, h - kit.Radius, h + kit.Radius, storey, storey, placer, settings)
                        || Taken(taken, x - kit.Radius, x + kit.Radius, ground, y) || Taken(taken, x, x1, y - kit.Radius, y + kit.Radius)) {
                        continue;
                    }

                    // The whole L at one offset: off the frontmost drawn
                    // surface the upright and the level stretch pass.
                    PlumbFace(frontage, spot, building, x, 0, storey, 0f, h, kit, out float plumbFace);
                    float offset = Mathf.Max(plumbFace, LevelFace(frontage, building, at, end, storey, x, x1, h, kit, face)) + settings.PipeWallGap + kit.Radius;

                    if (!ClearOfLane(frontage, run, x - kit.Radius, x1, offset + kit.Radius, 0, settings)) {
                        continue;
                    }

                    Vector3 Point(float px, float py) => new(run.Start.x + run.Along.x * px + run.Front.x * offset, py, run.Start.y + run.Along.y * px + run.Front.y * offset);
                    Vector3 cornerNode = Point(x, y);
                    Vector3 endNode = Point(x1, y);
                    // The corner elbow: leg A down the vertical, leg B along;
                    // the end elbow: leg A back along the pipe, leg B into the
                    // wall.
                    PlaceElbow(kit.Elbow, cornerNode, Vector3.down, alongWorld, placer, group);
                    PlaceWallElbow(kit, endNode, -alongWorld, -front, settings, placer, group);
                    // A valve at ValveHeight on the upright, where it stays
                    // out of the street's lane.
                    Vector3 upFrom = cornerNode + Vector3.down * kit.Elbow.LegA;
                    bool withValve = valve && ClearOfLane(frontage, run, x - kit.Radius, x + kit.Radius, offset + ValveReach(kit), 0, settings);
                    PlaceRunPipe(kit, upFrom, Point(x, ground - 0.3f), alongWorld, front, withValve ? upFrom.y - (ground + settings.ValveHeight) : -1f, settings,
                        placer, group);
                    PlacePipe(kit, cornerNode + alongWorld * kit.Elbow.LegB, endNode - alongWorld * kit.Elbow.LegA, Vector3.up, settings, placer, group);
                    taken.Add((x - kit.Radius, x + kit.Radius, ground, y));
                    taken.Add((x, x1, y - kit.Radius, y + kit.Radius));
                    return y - ground + x1 - x;
                }
            }

            return 0f;
        }

        /// <summary>
        /// Where along its run a level stretch of pipe ends in a module, by
        /// roll (0-1): within PipeEndSpread of the module's width about its
        /// middle, so the elbow turning into the wall never lands at a joint.
        /// </summary>
        private static float PipeEndInModule(ModuleSpot spot, Building building, float roll, GeneratorSettings settings)
        {
            return spot.Position + Spacing(spot, building) * (0.5f + (roll - 0.5f) * settings.PipeEndSpread);
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
                    float deckTop = DeckTop(level);
                    // The supports' backs in one line in front of the back
                    // line of the storey below, as a jetty's are, whichever
                    // walls stand there.
                    float supportBack = SupportBack(building.Levels[storey - 1].Style, storey - 1, settings, placer);

                    foreach (ModuleSpot spot in spots) {
                        if (!FaceAt(spot, building, storey - 1, frontage.WallSetback, out _)) {
                            continue;
                        }

                        float face = spot.Depth + StoreyJetty(building, storey - 1) - frontage.WallSetback + supportBack;

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
            int tooClose = 0;

            // Every lamp placed so far, on any street: where its post stands,
            // where its light is and how far that reaches. A new lamp keeps
            // LampSpacing from every post and its light clear of every
            // other's, so no two stand across a narrow street from each other
            // and no spot is lit by two lamps.
            List<Vector3> postsPlaced = new();
            List<Vector3> lightsPlaced = new();
            List<float> lightRanges = new();

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

                for (int i = 0; i < wanted && candidates.Count > 0; i++) {
                    int pick = candidates[random.Next(candidates.Count)];
                    candidates.Remove(pick);
                    ModuleSpot spot = frontage.Spots[pick];
                    Building building = frontage.Buildings[spot.Building];
                    Run run = frontage.Runs[spot.Run];
                    float middle = spot.Position + Spacing(spot, building) * 0.5f;
                    Vector2 at = run.Start + run.Along * middle + run.Front * settings.LampEdgeInset;
                    PieceAnnotation post = PiecePlacer.Pick(posts, null, random);

                    // The post is lowered LampSink into the ground, so its base
                    // is hidden in the tiles rather than resting on them.
                    Vector3 position = new(at.x, Surface(run, middle) - settings.LampSink, at.y);
                    float yaw = Yaw(Level(run.Front)) - Yaw(post.fronts[0]);
                    float range = LampLight(post, out Vector3 lightOffset);
                    Vector3 lightAt = position + Quaternion.Euler(0f, yaw, 0f) * lightOffset;

                    if (!LampAllowed(position, lightAt, range, postsPlaced, lightsPlaced, lightRanges, settings.LampSpacing)) {
                        tooClose++;
                        i--;
                        continue;
                    }

                    // With Place Lamps off every post is still worked out (the
                    // same random draws, so what comes after - building
                    // colours - stays as it was), just never placed.
                    if (settings.PlaceLamps) {
                        group ??= Group(parent, frontage.Label);
                        placer.PlaceAt(post, Vector3.zero, position, yaw, group);
                    }
                    postsPlaced.Add(position);
                    lightsPlaced.Add(lightAt);
                    lightRanges.Add(range);
                    lamps++;
                }
            }

            if (!settings.PlaceLamps) {
                log.Add($"Lamp posts: none (Place Lamps is off; {lamps} would have been placed).");
                return;
            }

            log.Add($"Lamp posts: {lamps} ({tooClose} left out within {settings.LampSpacing}m of another post, or where its light would overlap another's).");
        }

        /// <summary>
        /// The range of the first Light in a piece's prefab (0 if it has
        /// none), and where it stands from the pivot of the piece placed
        /// unturned (the placer sets the root's rotation, keeping its scale).
        /// </summary>
        private static float LampLight(PieceAnnotation piece, out Vector3 offset)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(piece.guid));
            Light light = prefab != null ? prefab.GetComponentInChildren<Light>(true) : null;

            if (light == null) {
                offset = Vector3.zero;
                return 0f;
            }

            Transform root = prefab.transform;
            offset = Vector3.Scale(root.localScale, root.InverseTransformPoint(light.transform.position));
            return light.range;
        }

        /// <summary>
        /// Whether a lamp post at position, its light at lightAt reaching
        /// range, stands at least spacing (measured level) from every post
        /// placed so far, with its light clear of every other's: two lights
        /// overlap where their centres are closer than their ranges added.
        /// </summary>
        private static bool LampAllowed(Vector3 position, Vector3 lightAt, float range, List<Vector3> postsPlaced, List<Vector3> lightsPlaced,
            List<float> lightRanges, float spacing)
        {
            for (int i = 0; i < postsPlaced.Count; i++) {
                Vector3 toPost = postsPlaced[i] - position;
                toPost.y = 0f;

                if (toPost.magnitude < spacing || Vector3.Distance(lightsPlaced[i], lightAt) < range + lightRanges[i]) {
                    return false;
                }
            }

            return true;
        }
    }
}
