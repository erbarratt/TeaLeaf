using System.Collections.Generic;
using Core;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The sound rooms and portals over a generated level's streets, so a
    /// sound in one street reaches another round its corners rather than
    /// straight through the buildings between (two points outside every room
    /// always hear each other directly).
    ///
    /// Each straight run of a street - its segments in line with the run's
    /// first, within the branch angle - is one SoundRoom, so a street never
    /// muffles itself where it only changes height. Every point where two or
    /// more runs meet (a bend, a junction) gets a small junction room of its
    /// own, a box round the street corners there, which wins over the street
    /// rooms running into it (priority 1); a SoundPortal stands on the
    /// junction box's face wherever a street leaves it. Each street room has
    /// an open portal lying across its top at the eaves, to the rooftops
    /// (outside every room). The rooms reach from under the street to the
    /// lower side's eaves (the wall tops along each edge, less the roofs'
    /// eave drop), and half a metre past each edge into the walls.
    ///
    /// Then the result is checked: every portal must join the two rooms it
    /// was placed for, and every point of every street (at foot and eye
    /// height) must be in a room. Runs after the moon shafts, under
    /// Generated/Sound Rooms.
    /// </summary>
    internal static class SoundRooms
    {
        private const string GroupName = "Sound Rooms";
        // How far a room reaches past a street's edges, into the walls, so a
        // listener standing against a facade is inside it.
        private const float EdgeMargin = 0.5f;
        // How far a street room runs on past a dead end, into the facade
        // closing it.
        private const float DeadEndReach = 1f;
        // How far a room reaches under the street's lowest surface.
        private const float Below = 1f;
        private const float SampleStep = 0.25f;
        // How far apart along a street's edge the walls are sampled.
        private const float WallSampleSpacing = 2f;
        // A junction's corners are looked for no further from its point than
        // this many times its widest half width: two edges meeting at a sharp
        // angle cross far off.
        private const float CornerReachLimit = 3f;
        private const int JunctionPriority = 1;
        // The check's samples: this far apart along and across each street,
        // held in this far from its edges, at these heights above it.
        private const float CheckStep = 1f;
        private const float CheckInset = 0.3f;
        private static readonly float[] _checkHeights = { 0.2f, 1.6f };
        // A stretch of a street room's top shorter than this gets no portal
        // to the sky.
        private const float MinSkyLength = 1f;
        // How many problems the log names, of each kind.
        private const int MaxListed = 8;

        /// <summary>
        /// One straight run of a street: its segments from point From to
        /// point To, boxed along the line between those two points.
        /// </summary>
        private class Run
        {
            public StreetSpec Street;
            public int From;
            public int To;
            public string Name;
            public Vector2 Start;
            public Vector2 Axis;
            public float Length;
            // The box's middle line, this far to the left of the line
            // between the run's ends, and its half width either side of it
            // (the streets' edges, not yet the margin into the walls).
            public float Side;
            public float HalfWidth;
            public bool StartJoined;
            public bool EndJoined;
            public float Bottom;
            public float Top;
            public SoundRoom Room;
            // Its ways out of the junctions it meets.
            public readonly List<Way> Ways = new();
        }

        /// <summary>
        /// A way out of a junction point along one street segment, and where
        /// it leaves the junction's box.
        /// </summary>
        private class Way
        {
            public Vector2 Direction;
            public float HalfWidth;
            public Run Run;
            // How far along its run's line the junction's point lies.
            public float Along;
            // How far out from the junction's point the way's middle line
            // leaves the box, the face's outward normal there, and the
            // portal's width along that face.
            public float Exit;
            public Vector3 Normal;
            public float PortalWidth;
        }

        /// <summary>
        /// A point where streets meet, with every way out of it, and its box:
        /// turned to its widest street (Along), reaching MinAlong..MaxAlong
        /// and MinAcross..MaxAcross from the point, Bottom to Top.
        /// </summary>
        private class Junction
        {
            public Vector2 At;
            public float Height;
            public readonly List<Way> Ways = new();
            public readonly List<string> Labels = new();
            public Vector2 Along;
            public float MinAlong;
            public float MaxAlong;
            public float MinAcross;
            public float MaxAcross;
            public float Bottom;
            public float Top;
            public bool Alley;
        }

        /// <summary>
        /// Lays the rooms and portals, then checks them. One log line, and
        /// a line for each problem found.
        /// </summary>
        internal static void Build(LayoutSpec spec, StreetHeights heights, GeneratorSettings settings, Transform root, List<string> log)
        {
            Transform group = new GameObject(GroupName).transform;
            group.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Generate");

            // The walls' tops are read from the facades and the lots, not the
            // roofs (the eaves lie below the wall tops by the eave drop).
            List<Bounds> walls = new();
            MoonShafts.GatherBounds(root.Find("Facades"), walls);
            MoonShafts.GatherBounds(root.Find("Lots"), walls);

            List<Run> runs = GatherRuns(spec, heights, settings, walls);
            List<Junction> junctions = GatherJunctions(runs, settings);
            List<(SoundPortal portal, SoundRoom front, SoundRoom back)> portals = new();

            // The junctions' boxes first: a street room stops where its
            // street leaves one, and has no sky over one.
            foreach (Junction junction in junctions) {
                ShapeJunction(junction, settings);
            }

            int skies = 0;

            foreach (Run run in runs) {
                skies += PlaceRun(run, settings, group, portals);
            }

            foreach (Junction junction in junctions) {
                PlaceJunction(junction, settings, group, portals);
            }

            List<string> problems = new();
            int wrongPortals = CheckPortals(portals, problems);
            int missing = CheckCoverage(spec, heights, settings, out int samples, problems);
            log.Add($"Sound rooms: {runs.Count} street room(s), {junctions.Count} junction room(s), {portals.Count} portal(s) ({skies} to the sky); "
                + $"{wrongPortals} portal(s) not joining the rooms meant, {missing} of {samples} street point(s) in no room.");

            foreach (string problem in problems) {
                log.Add($"  {problem}");
            }
        }

        /// <summary>
        /// Every street cut into straight runs, each measured: its box along
        /// the line from its first point to its last, wide enough for its
        /// widest segment and any point off that line, from under its lowest
        /// surface up to the lower side's eaves.
        /// </summary>
        private static List<Run> GatherRuns(LayoutSpec spec, StreetHeights heights, GeneratorSettings settings, List<Bounds> walls)
        {
            List<Run> runs = new();

            foreach (StreetSpec street in spec.streets) {
                int from = 0;

                for (int point = 1; point < street.points.Count; point++) {
                    // A run ends at the street's last point, or where the
                    // next segment turns off the run's first by more than the
                    // branch angle.
                    if (point < street.points.Count - 1 && InLine(street, from, point, settings)) {
                        continue;
                    }

                    Run run = MakeRun(street, from, point, heights, settings, walls);

                    if (run != null) {
                        runs.Add(run);
                    }

                    from = point;
                }
            }

            return runs;
        }

        /// <summary>
        /// Whether the segment leaving a point carries on in line with a
        /// run's first segment (a segment too short to have a direction
        /// counts as in line).
        /// </summary>
        private static bool InLine(StreetSpec street, int runFrom, int point, GeneratorSettings settings)
        {
            Vector2 first = Ground(street.points[runFrom + 1]) - Ground(street.points[runFrom]);
            Vector2 next = Ground(street.points[point + 1]) - Ground(street.points[point]);

            if (first.sqrMagnitude < 1e-6f || next.sqrMagnitude < 1e-6f) {
                return true;
            }

            first.Normalize();
            next.Normalize();
            return Vector2.Dot(first, next) > 0f && Mathf.Abs(Cross(first, next)) < settings.BranchDot;
        }

        /// <summary>
        /// One run measured; null where its ends are at the same place.
        /// </summary>
        private static Run MakeRun(StreetSpec street, int from, int to, StreetHeights heights, GeneratorSettings settings, List<Bounds> walls)
        {
            Vector2 start = Ground(street.points[from]);
            Vector2 chord = Ground(street.points[to]) - start;
            float length = chord.magnitude;

            if (length < 1e-3f) {
                return null;
            }

            Vector2 axis = chord / length;
            float halfWidth = 0f;
            float leftmost = 0f;
            float rightmost = 0f;
            float low = float.MaxValue;
            float high = float.MinValue;
            float leftEaves = float.NaN;
            float rightEaves = float.NaN;

            // How far its points stand off the line between its ends, either
            // side: the box widens only on the side a bend takes it.
            for (int point = from; point <= to; point++) {
                float offLine = Cross(axis, Ground(street.points[point]) - start);
                leftmost = Mathf.Max(leftmost, offLine);
                rightmost = Mathf.Min(rightmost, offLine);
            }

            for (int segment = from; segment < to; segment++) {
                Vector2 a = Ground(street.points[segment]);
                Vector2 way = Ground(street.points[segment + 1]) - a;
                float segmentLength = way.magnitude;
                float segmentHalf = street.SegmentWidth(segment) * 0.5f;
                halfWidth = Mathf.Max(halfWidth, segmentHalf);

                if (segmentLength < 1e-3f) {
                    continue;
                }

                way /= segmentLength;
                Span(street, segment, heights, segmentLength, out float segmentLow, out float segmentHigh);
                low = Mathf.Min(low, segmentLow);
                high = Mathf.Max(high, segmentHigh);

                // The lowest wall top along each edge: where the eaves are
                // lowest, the street opens to the rooftops.
                int samples = Mathf.Max(2, Mathf.CeilToInt(segmentLength / WallSampleSpacing) + 1);

                for (int i = 0; i < samples; i++) {
                    float along = segmentLength * i / (samples - 1);
                    Vector2 middle = a + way * along;
                    float level = heights.SurfaceAt(street, segment, along);
                    leftEaves = Lowest(leftEaves, MoonShafts.WallTop(middle + Left(way) * segmentHalf, level, walls, settings));
                    rightEaves = Lowest(rightEaves, MoonShafts.WallTop(middle + Right(way) * segmentHalf, level, walls, settings));
                }
            }

            if (low > high) {
                return null;
            }

            float eaves = Lowest(leftEaves, rightEaves);
            float top = float.IsNaN(eaves) ? float.MinValue : eaves - settings.RoofEaveDrop;

            return new Run {
                Street = street, From = from, To = to, Name = $"{street.id} {from + 1}-{to + 1}",
                Start = start, Axis = axis, Length = length, Side = (leftmost + rightmost) * 0.5f,
                HalfWidth = halfWidth + (leftmost - rightmost) * 0.5f,
                Bottom = low - Below, Top = Mathf.Max(top, high + settings.SoundRoomMinHeight)
            };
        }

        /// <summary>
        /// Every point where two or more runs meet: a run's ends, and its
        /// points along the way where another street joins it. A run end
        /// that meets no other run is a dead end.
        /// </summary>
        private static List<Junction> GatherJunctions(List<Run> runs, GeneratorSettings settings)
        {
            List<Junction> all = new();

            foreach (Run run in runs) {
                for (int point = run.From; point <= run.To; point++) {
                    Vector2 at = Ground(run.Street.points[point]);
                    Junction junction = null;

                    foreach (Junction existing in all) {
                        if ((existing.At - at).magnitude <= settings.JunctionDistance) {
                            junction = existing;
                            break;
                        }
                    }

                    if (junction == null) {
                        // Streets meet level at their points.
                        junction = new Junction { At = at, Height = run.Street.points[point].y };
                        all.Add(junction);
                    }

                    // Out along the segment after the point, and back along
                    // the one before it.
                    if (point < run.To) {
                        AddWay(junction, run, point, point + 1);
                    }

                    if (point > run.From) {
                        AddWay(junction, run, point, point - 1);
                    }

                    string label = $"{run.Street.id} {point + 1}";

                    if (!junction.Labels.Contains(label)) {
                        junction.Labels.Add(label);
                    }
                }
            }

            List<Junction> junctions = new();

            foreach (Junction junction in all) {
                Run first = junction.Ways.Count > 0 ? junction.Ways[0].Run : null;
                bool shared = false;

                foreach (Way way in junction.Ways) {
                    shared |= way.Run != first;
                }

                if (!shared) {
                    continue;
                }

                junctions.Add(junction);

                foreach (Way way in junction.Ways) {
                    Run run = way.Run;
                    run.Ways.Add(way);
                    way.Along = Vector2.Dot(junction.At - run.Start, run.Axis);

                    if ((Ground(run.Street.points[run.From]) - junction.At).magnitude <= settings.JunctionDistance) {
                        run.StartJoined = true;
                    }

                    if ((Ground(run.Street.points[run.To]) - junction.At).magnitude <= settings.JunctionDistance) {
                        run.EndJoined = true;
                    }
                }
            }

            return junctions;
        }

        /// <summary>
        /// Adds the way out of a junction along the segment from one street
        /// point to a neighbouring one (none if they are at the same place).
        /// </summary>
        private static void AddWay(Junction junction, Run run, int point, int towards)
        {
            Vector2 way = Ground(run.Street.points[towards]) - Ground(run.Street.points[point]);

            if (way.sqrMagnitude < 1e-6f) {
                return;
            }

            junction.Ways.Add(new Way {
                Direction = way.normalized, HalfWidth = run.Street.SegmentWidth(Mathf.Min(point, towards)) * 0.5f, Run = run
            });
        }

        /// <summary>
        /// A run's room and its portals to the sky. The room runs on past a
        /// dead end into the facade closing it, and at a junction stops where
        /// its street leaves the junction's box (its near corner on the box's
        /// face where the street leaves it at a slant, the rest inside the
        /// box, which wins there), so it never reaches into another street
        /// round the corner. A portal lies across its top over each stretch
        /// no junction's box covers, its front (local Z) up to the rooftops.
        /// Returns how many portals to the sky it got.
        /// </summary>
        private static int PlaceRun(Run run, GeneratorSettings settings, Transform group,
            List<(SoundPortal portal, SoundRoom front, SoundRoom back)> portals)
        {
            float halfWidth = run.HalfWidth + EdgeMargin;
            float from = run.StartJoined ? float.MinValue : -DeadEndReach;
            float to = run.EndJoined ? float.MaxValue : run.Length + DeadEndReach;
            List<(float from, float to)> covered = new();

            foreach (Way way in run.Ways) {
                bool forward = Vector2.Dot(way.Direction, run.Axis) > 0f;
                covered.Add(forward ? (way.Along, way.Along + way.Exit) : (way.Along - way.Exit, way.Along));
                float slant = Mathf.Max(Mathf.Abs(Vector2.Dot(way.Direction, new Vector2(way.Normal.x, way.Normal.z))), 0.3f);
                float clear = Mathf.Max(0f, way.Exit - halfWidth * Mathf.Sqrt(1f - slant * slant) / slant);

                if (run.StartJoined && forward && way.Along < settings.JunctionDistance) {
                    from = Mathf.Max(from, way.Along + clear);
                }

                if (run.EndJoined && !forward && way.Along > run.Length - settings.JunctionDistance) {
                    to = Mathf.Min(to, way.Along - clear);
                }
            }

            // A joined end whose way wasn't found: the run's own end point.
            from = from == float.MinValue ? 0f : from;
            to = to == float.MaxValue ? run.Length : to;
            to = Mathf.Max(to, from + 0.1f);

            Vector2 middle = run.Start + run.Axis * ((from + to) * 0.5f) + Left(run.Axis) * run.Side;
            Vector3 axis = new(run.Axis.x, 0f, run.Axis.y);
            float baseWidth = run.Street.width > 0f ? run.Street.width : settings.StreetWidth;
            run.Room = MakeRoom(group, run.Name, new Vector3(middle.x, (run.Bottom + run.Top) * 0.5f, middle.y), Quaternion.LookRotation(axis, Vector3.up),
                new Vector3(halfWidth * 2f, run.Top - run.Bottom, to - from), 0, baseWidth <= settings.SoundAlleyMaxWidth, settings);

            // The stretches of the room's top no junction's box lies under.
            covered.Sort((x, y) => x.from.CompareTo(y.from));
            float free = from;
            int skies = 0;

            for (int i = 0; i <= covered.Count; i++) {
                float until = i < covered.Count ? Mathf.Min(covered[i].from, to) : to;

                if (until - free >= MinSkyLength) {
                    Vector2 skyMiddle = run.Start + run.Axis * ((free + until) * 0.5f) + Left(run.Axis) * run.Side;
                    SoundPortal sky = MakePortal(run.Room.transform, $"Sky {skies + 1}", new Vector3(skyMiddle.x, run.Top, skyMiddle.y),
                        Quaternion.LookRotation(Vector3.up, axis), new Vector2(halfWidth * 2f, until - free), settings.SoundSkyMuffle);
                    portals.Add((sky, null, run.Room));
                    skies++;
                }

                if (i < covered.Count) {
                    free = Mathf.Max(free, covered[i].to);
                }
            }

            return skies;
        }

        /// <summary>
        /// Works out a junction's box - turned to its widest street, round
        /// the corners where each street's edge meets the next one's, from
        /// under the point to the lowest of its streets' rooms' tops - and
        /// where each way out of it leaves the box: through the face its
        /// middle line crosses, the portal as wide as the street is along
        /// that face.
        /// </summary>
        private static void ShapeJunction(Junction junction, GeneratorSettings settings)
        {
            Way widest = junction.Ways[0];
            float top = float.MaxValue;
            junction.Alley = true;

            foreach (Way way in junction.Ways) {
                if (way.HalfWidth > widest.HalfWidth) {
                    widest = way;
                }

                top = Mathf.Min(top, way.Run.Top);
                float baseWidth = way.Run.Street.width > 0f ? way.Run.Street.width : settings.StreetWidth;
                junction.Alley &= baseWidth <= settings.SoundAlleyMaxWidth;
            }

            junction.Bottom = junction.Height - Below;
            junction.Top = Mathf.Max(top, junction.Height + settings.SoundRoomMinHeight);
            Vector2 along = widest.Direction;
            Vector2 across = Left(along);
            junction.Along = along;

            foreach (Vector2 corner in Corners(junction, settings)) {
                Vector2 offset = corner - junction.At;
                float a = Vector2.Dot(offset, along);
                float b = Vector2.Dot(offset, across);
                junction.MinAlong = Mathf.Min(junction.MinAlong, a);
                junction.MaxAlong = Mathf.Max(junction.MaxAlong, a);
                junction.MinAcross = Mathf.Min(junction.MinAcross, b);
                junction.MaxAcross = Mathf.Max(junction.MaxAcross, b);
            }

            junction.MinAlong -= EdgeMargin;
            junction.MaxAlong += EdgeMargin;
            junction.MinAcross -= EdgeMargin;
            junction.MaxAcross += EdgeMargin;
            Vector3 along3 = new(along.x, 0f, along.y);
            Vector3 across3 = new(across.x, 0f, across.y);

            foreach (Way way in junction.Ways) {
                float a = Vector2.Dot(way.Direction, along);
                float b = Vector2.Dot(way.Direction, across);
                way.Exit = float.MaxValue;
                float faceLength = 0f;

                if (Mathf.Abs(a) > 1e-4f) {
                    way.Exit = (a > 0f ? junction.MaxAlong : junction.MinAlong) / a;
                    way.Normal = along3 * Mathf.Sign(a);
                    faceLength = junction.MaxAcross - junction.MinAcross;
                }

                if (Mathf.Abs(b) > 1e-4f) {
                    float exit = (b > 0f ? junction.MaxAcross : junction.MinAcross) / b;

                    if (exit < way.Exit) {
                        way.Exit = exit;
                        way.Normal = across3 * Mathf.Sign(b);
                        faceLength = junction.MaxAlong - junction.MinAlong;
                    }
                }

                float slant = Mathf.Abs(Vector2.Dot(way.Direction, new Vector2(way.Normal.x, way.Normal.z)));
                way.PortalWidth = Mathf.Min(way.HalfWidth * 2f / Mathf.Max(slant, 0.1f), faceLength);
            }
        }

        /// <summary>
        /// A junction's room, and a portal where each way leaves it, on the
        /// box's face, its front (local Z) out into the street.
        /// </summary>
        private static void PlaceJunction(Junction junction, GeneratorSettings settings, Transform group,
            List<(SoundPortal portal, SoundRoom front, SoundRoom back)> portals)
        {
            Vector2 across = Left(junction.Along);
            Vector2 centre = junction.At + junction.Along * ((junction.MinAlong + junction.MaxAlong) * 0.5f)
                + across * ((junction.MinAcross + junction.MaxAcross) * 0.5f);
            float middleY = (junction.Bottom + junction.Top) * 0.5f;
            float height = junction.Top - junction.Bottom;
            string name = $"Junction {string.Join(" / ", junction.Labels)}";
            SoundRoom room = MakeRoom(group, name, new Vector3(centre.x, middleY, centre.y),
                Quaternion.LookRotation(new Vector3(junction.Along.x, 0f, junction.Along.y), Vector3.up),
                new Vector3(junction.MaxAcross - junction.MinAcross, height, junction.MaxAlong - junction.MinAlong), JunctionPriority,
                junction.Alley, settings);

            foreach (Way way in junction.Ways) {
                Vector2 point = junction.At + way.Direction * way.Exit;
                SoundPortal portal = MakePortal(room.transform, $"Portal {way.Run.Name}", new Vector3(point.x, middleY, point.y),
                    Quaternion.LookRotation(way.Normal, Vector3.up), new Vector2(way.PortalWidth, height), settings.SoundStreetMuffle);
                portals.Add((portal, way.Run.Room, room));
            }
        }

        /// <summary>
        /// The points a junction's box must hold: the junction's own point,
        /// and, between each way out of it and the next one round, where the
        /// two streets' facing edges cross - a corner building's corner, or
        /// the outside corner of a bend. Edges nearly parallel (a street
        /// carrying straight on) give the two edge points level with the
        /// junction instead; a crossing far off at a sharp angle is drawn
        /// in to the reach limit.
        /// </summary>
        private static List<Vector2> Corners(Junction junction, GeneratorSettings settings)
        {
            List<Way> ways = new(junction.Ways);
            ways.Sort((x, y) => Mathf.Atan2(x.Direction.y, x.Direction.x).CompareTo(Mathf.Atan2(y.Direction.y, y.Direction.x)));
            List<Vector2> corners = new() { junction.At };
            float widest = 0f;

            foreach (Way way in ways) {
                widest = Mathf.Max(widest, way.HalfWidth);
            }

            float limit = widest * CornerReachLimit;

            for (int i = 0; i < ways.Count; i++) {
                Way first = ways[i];
                Way next = ways[(i + 1) % ways.Count];
                // Going round anticlockwise, the first way's left edge faces
                // the next way's right edge.
                Vector2 firstEdge = junction.At + Left(first.Direction) * first.HalfWidth;
                Vector2 nextEdge = junction.At + Right(next.Direction) * next.HalfWidth;
                float sin = Cross(first.Direction, next.Direction);

                if (Mathf.Abs(sin) < settings.BranchDot) {
                    corners.Add(firstEdge);
                    corners.Add(nextEdge);
                    continue;
                }

                Vector2 crossing = firstEdge + first.Direction * (Cross(nextEdge - firstEdge, next.Direction) / sin);
                Vector2 offset = crossing - junction.At;

                if (offset.magnitude > limit) {
                    crossing = junction.At + offset.normalized * limit;
                }

                corners.Add(crossing);
            }

            return corners;
        }

        /// <summary>
        /// A SoundRoom box, its reverb an alley's or the outdoors', with the
        /// streets' ambience.
        /// </summary>
        private static SoundRoom MakeRoom(Transform group, string name, Vector3 centre, Quaternion turn, Vector3 size, int priority, bool alley,
            GeneratorSettings settings)
        {
            GameObject roomObject = new(name);
            Undo.RegisterCreatedObjectUndo(roomObject, "Generate");
            roomObject.transform.SetParent(group, false);
            roomObject.transform.SetPositionAndRotation(centre, turn);
            SoundRoom room = roomObject.AddComponent<SoundRoom>();
            SerializedObject serialized = new(room);
            serialized.FindProperty("size").vector3Value = size;
            serialized.FindProperty("priority").intValue = priority;
            serialized.FindProperty("reverb").boxedValue = alley ? ReverbSettings.Alley : ReverbSettings.Outdoors;
            serialized.FindProperty("ambience").objectReferenceValue = settings.SoundStreetAmbience;
            serialized.FindProperty("ambienceVolume").floatValue = settings.SoundStreetAmbienceVolume;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return room;
        }

        /// <summary>
        /// An open SoundPortal, under the room it was placed for.
        /// </summary>
        private static SoundPortal MakePortal(Transform room, string name, Vector3 centre, Quaternion turn, Vector2 size, float muffle)
        {
            GameObject portalObject = new(name);
            Undo.RegisterCreatedObjectUndo(portalObject, "Generate");
            portalObject.transform.SetParent(room, false);
            portalObject.transform.SetPositionAndRotation(centre, turn);
            SoundPortal portal = portalObject.AddComponent<SoundPortal>();
            SerializedObject serialized = new(portal);
            serialized.FindProperty("size").vector2Value = size;
            serialized.FindProperty("openMuffle").floatValue = muffle;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return portal;
        }

        /// <summary>
        /// How many portals find other rooms either side than the two they
        /// were placed between; each is named in the problems.
        /// </summary>
        private static int CheckPortals(List<(SoundPortal portal, SoundRoom front, SoundRoom back)> portals, List<string> problems)
        {
            int wrong = 0;

            foreach ((SoundPortal portal, SoundRoom front, SoundRoom back) in portals) {
                portal.ProbeRooms(out SoundRoom foundFront, out SoundRoom foundBack);

                if (foundFront == front && foundBack == back) {
                    continue;
                }

                if (wrong++ < MaxListed) {
                    problems.Add($"portal '{portal.transform.parent.name}/{portal.name}' joins {RoomName(foundFront)} and {RoomName(foundBack)}, "
                        + $"not {RoomName(front)} and {RoomName(back)}.");
                }
            }

            return wrong;
        }

        /// <summary>
        /// How many sample points over the streets, at foot and eye height,
        /// are in no room; the first few are named in the problems.
        /// </summary>
        private static int CheckCoverage(LayoutSpec spec, StreetHeights heights, GeneratorSettings settings, out int samples, List<string> problems)
        {
            int missing = 0;
            samples = 0;

            foreach (StreetSpec street in spec.streets) {
                for (int segment = 0; segment + 1 < street.points.Count; segment++) {
                    Vector2 start = Ground(street.points[segment]);
                    Vector2 way = Ground(street.points[segment + 1]) - start;
                    float length = way.magnitude;

                    if (length < 1e-3f) {
                        continue;
                    }

                    way /= length;
                    float half = Mathf.Max(0f, street.SegmentWidth(segment) * 0.5f - CheckInset);
                    int alongCount = Mathf.CeilToInt(length / CheckStep);
                    int acrossCount = Mathf.CeilToInt(half * 2f / CheckStep);

                    for (int i = 0; i <= alongCount; i++) {
                        float along = length * i / alongCount;
                        float level = heights.SurfaceAt(street, segment, along);

                        for (int j = 0; j <= acrossCount; j++) {
                            float side = acrossCount == 0 ? 0f : Mathf.Lerp(-half, half, (float)j / acrossCount);
                            Vector2 point = start + way * along + Left(way) * side;

                            foreach (float height in _checkHeights) {
                                samples++;
                                Vector3 world = new(point.x, level + height, point.y);

                                if (SoundRoom.Find(world) != null) {
                                    continue;
                                }

                                if (missing++ < MaxListed) {
                                    problems.Add($"{street.id} {segment + 1}-{segment + 2}: ({world.x:0.0}, {world.y:0.0}, {world.z:0.0}) is in no room.");
                                }
                            }
                        }
                    }
                }
            }

            return missing;
        }

        private static string RoomName(SoundRoom room)
        {
            return room == null ? "outside" : $"'{room.name}'";
        }

        /// <summary>
        /// The lowest and highest the street's surface comes along a segment.
        /// </summary>
        private static void Span(StreetSpec street, int segment, StreetHeights heights, float length, out float low, out float high)
        {
            low = float.MaxValue;
            high = float.MinValue;
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / SampleStep));

            for (int i = 0; i <= samples; i++) {
                float height = heights.SurfaceAt(street, segment, length * i / samples);
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
            }
        }

        /// <summary>
        /// The lower of two heights, either of which may be NaN (none found).
        /// </summary>
        private static float Lowest(float a, float b)
        {
            if (float.IsNaN(a)) {
                return b;
            }

            return float.IsNaN(b) ? a : Mathf.Min(a, b);
        }

        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        /// <summary>
        /// The direction a quarter turn to the left of a way (seen from above).
        /// </summary>
        private static Vector2 Left(Vector2 way)
        {
            return new Vector2(-way.y, way.x);
        }

        /// <summary>
        /// The direction a quarter turn to the right of a way (seen from above).
        /// </summary>
        private static Vector2 Right(Vector2 way)
        {
            return new Vector2(way.y, -way.x);
        }

        /// <summary>
        /// The 2D cross product: positive where b lies to the left of a.
        /// </summary>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
