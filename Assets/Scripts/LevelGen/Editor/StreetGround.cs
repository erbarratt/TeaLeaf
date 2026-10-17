using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Stage 4: lays every street's ground with its style's tiles.
    ///
    /// A style with ground pieces (cobbles) is laid on one grid per street
    /// area - the streets of that style joined at shared points - turned at
    /// random, never squared to the world's axes (GroundGridMinAngle). A
    /// tile is laid wherever it overlaps the area's outline (each segment's
    /// rectangle, a disc at every bend and junction to fill the wedges, and
    /// the ground round each lot nearest the area: a ring from its footprint
    /// out to LotGroundMargin, at the lot's street level, never under the
    /// building) by at least GeneratorSettings.MinTileCoverage, so there are no gaps at
    /// bends, junctions or edges and no tile lies on another; what stands
    /// out past the street's edge is under the buildings lining it. A
    /// segment that changes height is climbed by a flight of steps with a
    /// landing at its top (StreetSteps, LayFlight), the ground level either
    /// side of it.
    ///
    /// A style with only floor pieces (plank decking: wooden streets,
    /// walkways, bridges) is laid along each segment in that segment's own
    /// frame, so the boards follow the walkway; at a bend each segment runs
    /// on to meet the next at the outside edge.
    ///
    /// Either way the tiles are laid in rows, each row slid from the one
    /// before it and each tile turned from the one before it along its row
    /// (by the settings), neighbouring tiles overlapping slightly, and the walking surfaces (collider tops) at the street's
    /// height. Where several tiles fit, one is chosen at random, never the
    /// same as the one before it along the row. The tiles' colliders are
    /// replaced by one box per run of tiles along a row at one height
    /// (FloorColliders).
    /// </summary>
    internal static class StreetGround
    {
        // A tile less this far from one row or column to the next (after the
        // overlap) can't be laid.
        private const float MinPitch = 0.1f;

        /// <summary>
        /// A rectangle on the ground (x, z).
        /// </summary>
        private struct GroundRect
        {
            public Vector2 Centre;
            public Vector2 AxisX;
            public Vector2 AxisZ;
            public float HalfX;
            public float HalfZ;
        }

        /// <summary>
        /// A disc on the ground (x, z): fills the wedge at a bend or junction.
        /// </summary>
        private struct GroundDisc
        {
            public Vector2 Centre;
            public float Radius;
        }

        /// <summary>
        /// Lays every street in the spec, in the spec's order (so the random
        /// choices come in a fixed order), each area or street under a group
        /// of its own in parent. Adds a line per area or street to log.
        /// </summary>
        internal static void Build(LayoutSpec spec, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings,
            System.Random random, PiecePlacer placer, Transform parent, List<string> log)
        {
            HashSet<StreetSpec> done = new();
            // Each street in order: a ground area (its first street, style and
            // streets) or a decked street, so the lots can be shared out
            // before anything is laid.
            List<(StreetSpec First, BuildingStyle Style, List<StreetSpec> Area)> laid = new();

            foreach (StreetSpec street in spec.streets) {
                if (!done.Add(street)) {
                    continue;
                }

                BuildingStyle style = TileStyle(street, styles, log);

                if (style == null) {
                    continue;
                }

                if (style.TileCategory == "ground") {
                    List<StreetSpec> area = ConnectedArea(street, spec.streets, settings);

                    foreach (StreetSpec member in area) {
                        done.Add(member);
                    }

                    laid.Add((street, style, area));
                } else {
                    laid.Add((street, style, null));
                }
            }

            List<GroundLot>[] lotGround = LotGround(spec, styles, laid.ConvertAll(entry => entry.Area), settings);
            List<StreetSteps.Flight> flights = StreetSteps.Find(spec.streets, styles, placer, settings);

            for (int i = 0; i < laid.Count; i++) {
                (StreetSpec first, BuildingStyle style, List<StreetSpec> area) = laid[i];

                if (area != null) {
                    List<StreetSteps.Flight> areaFlights = flights.FindAll(flight => area.Contains(flight.Street));
                    LayArea(area, lotGround[i], areaFlights, style, settings, random, placer, Group(parent, $"Ground - {first.id}"), log);
                } else {
                    LayDecking(first, style, settings, random, placer, Group(parent, $"Decking - {first.id}"), log);
                }
            }
        }

        /// <summary>
        /// The ground round a lot: a ring from its footprint (the line its
        /// walls' fronts reach; convex) out to LotGroundMargin, at the lot's
        /// street level. Nothing is laid inside the footprint, under the
        /// building.
        /// </summary>
        private class GroundLot
        {
            public List<Vector2> Corners;
            // Each side's way out of the footprint (side i runs from corner i).
            public List<Vector2> Outward;
            public float Margin;
            public float Height;

            /// <summary>
            /// How far a point stands out past the footprint's sides (the
            /// furthest past any one; below 0 inside it).
            /// </summary>
            public float Outside(Vector2 point)
            {
                float furthest = float.MinValue;

                for (int i = 0; i < Corners.Count; i++) {
                    furthest = Mathf.Max(furthest, Vector2.Dot(point - Corners[i], Outward[i]));
                }

                return furthest;
            }
        }

        /// <summary>
        /// The ground round each lot, given to the ground area (of those
        /// given; null entries are decked streets) with a street segment
        /// nearest its footprint's middle: a ring from the footprint out to
        /// LotGroundMargin (GroundLot), at the lot's street level. A lot whose
        /// ground style has no module yet is left out.
        /// </summary>
        private static List<GroundLot>[] LotGround(LayoutSpec spec, Dictionary<string, BuildingStyle> styles, List<List<StreetSpec>> areas,
            GeneratorSettings settings)
        {
            List<GroundLot>[] ground = new List<GroundLot>[areas.Count];

            for (int i = 0; i < areas.Count; i++) {
                ground[i] = new List<GroundLot>();
            }

            foreach (LotSpec lot in spec.lots) {
                if (lot.storeys.Count == 0 || !styles.TryGetValue(lot.storeys[0], out BuildingStyle style) || style.ModuleSpacing <= 0f) {
                    continue;
                }

                List<Vector2> corners = lot.Corners(style.ModuleSpacing, settings.WallOverlap);

                if (corners.Count < 3) {
                    continue;
                }

                Vector2 centre = Vector2.zero;
                float area = 0f;

                for (int i = 0; i < corners.Count; i++) {
                    Vector2 a = corners[i];
                    Vector2 b = corners[(i + 1) % corners.Count];
                    centre += a / corners.Count;
                    area += a.x * b.y - b.x * a.y;
                }

                // Out of each side, from the winding (the area's sign).
                List<Vector2> outward = new();

                for (int i = 0; i < corners.Count; i++) {
                    Vector2 side = (corners[(i + 1) % corners.Count] - corners[i]).normalized;
                    Vector2 normal = new(side.y, -side.x);
                    outward.Add(area > 0f ? normal : -normal);
                }

                int nearestArea = -1;
                float nearest = float.MaxValue;

                for (int i = 0; i < areas.Count; i++) {
                    if (areas[i] == null) {
                        continue;
                    }

                    foreach (StreetSpec street in areas[i]) {
                        for (int k = 1; k < street.points.Count; k++) {
                            Vector2 a = Ground(street.points[k - 1]);
                            Vector2 ab = Ground(street.points[k]) - a;
                            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(centre - a, ab) / ab.sqrMagnitude) : 0f;
                            float distance = (a + ab * t - centre).magnitude;

                            if (distance < nearest) {
                                nearest = distance;
                                nearestArea = i;
                            }
                        }
                    }
                }

                if (nearestArea < 0) {
                    continue;
                }

                ground[nearestArea].Add(new GroundLot {
                    Corners = corners,
                    Outward = outward,
                    Margin = settings.LotGroundMargin,
                    Height = lot.origin.y - settings.GroundStoreyHeight
                });
            }

            return ground;
        }

        /// <summary>
        /// The style a street is laid with, or null (logged) if the street
        /// can't be laid: too few points, or no usable tiles.
        /// </summary>
        private static BuildingStyle TileStyle(StreetSpec street, Dictionary<string, BuildingStyle> styles, List<string> log)
        {
            if (street.points.Count < 2) {
                log.Add($"Street '{street.id}' not laid: it needs at least two points.");
                return null;
            }

            if (!styles.TryGetValue(street.style ?? "", out BuildingStyle style) || style.Tiles.Count == 0) {
                log.Add($"Street '{street.id}' not laid: style '{street.style}' has no usable ground or floor tiles.");
                return null;
            }

            return style;
        }

        /// <summary>
        /// A street and every street of the same style joined to it through
        /// shared points, in the spec's order.
        /// </summary>
        private static List<StreetSpec> ConnectedArea(StreetSpec start, List<StreetSpec> streets, GeneratorSettings settings)
        {
            List<StreetSpec> area = new() { start };

            // Each pass adds the streets touching one already in the area,
            // until a pass adds none.
            bool added = true;

            while (added) {
                added = false;

                foreach (StreetSpec street in streets) {
                    if (area.Contains(street) || street.style != start.style || street.points.Count < 2) {
                        continue;
                    }

                    foreach (StreetSpec member in area) {
                        if (SharesPoint(street, member, settings)) {
                            area.Add(street);
                            added = true;
                            break;
                        }
                    }
                }
            }

            // Back into the spec's order, so the outline is gathered the same
            // way every time.
            area.Sort((a, b) => streets.IndexOf(a).CompareTo(streets.IndexOf(b)));
            return area;
        }

        /// <summary>
        /// Lays one area of ground tiles on one grid: the area's streets and
        /// the ground under the lots given to it, each tile at the level of
        /// its side of any flight of steps; then the area's flights of steps
        /// and their landings (LayFlight). A grid tile is left out where it
        /// would lie over a flight or its landing (OverFlight).
        /// </summary>
        private static void LayArea(List<StreetSpec> area, List<GroundLot> lots, List<StreetSteps.Flight> flights, BuildingStyle style,
            GeneratorSettings settings, System.Random random, PiecePlacer placer, Transform group, List<string> log)
        {
            StreetSpec first = area[0];

            if (!FirstDirection(first, settings, out _)) {
                log.Add($"Street '{first.id}' not laid: all its segments are too short.");
                return;
            }

            // The grid's frame, from the street's first point: X turned at
            // random (always drawn) so the rows lie at least
            // GroundGridMinAngle off the world's X and Z axes, Z to its left.
            float angle = Mathf.Lerp(settings.GroundGridMinAngle, 90f - settings.GroundGridMinAngle, (float)random.NextDouble()) * Mathf.Deg2Rad;
            Vector2 axisX = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 origin = Ground(first.points[0]);
            Vector2 axisZ = new(-axisX.y, axisX.x);
            float gridYaw = Yaw(axisZ);

            List<GroundRect> rects = new();
            List<GroundDisc> discs = new();

            foreach (StreetSpec street in area) {
                Outline(street, area, settings, rects, discs);
            }

            // The outline's extent in the grid's frame.
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);

            foreach (GroundRect rect in rects) {
                for (int corner = 0; corner < 4; corner++) {
                    Vector2 point = rect.Centre + rect.AxisX * (corner < 2 ? rect.HalfX : -rect.HalfX)
                        + rect.AxisZ * (corner % 2 == 0 ? rect.HalfZ : -rect.HalfZ);
                    Encapsulate(ToFrame(point, origin, axisX, axisZ), Vector2.zero, ref min, ref max);
                }
            }

            foreach (GroundDisc disc in discs) {
                Encapsulate(ToFrame(disc.Centre, origin, axisX, axisZ), Vector2.one * disc.Radius, ref min, ref max);
            }

            foreach (GroundLot lot in lots) {
                foreach (Vector2 corner in lot.Corners) {
                    Encapsulate(ToFrame(corner, origin, axisX, axisZ), Vector2.one * lot.Margin, ref min, ref max);
                }
            }

            float pitchX = style.TileWidth - settings.GroundOverlap;
            float pitchZ = style.TileDepth - settings.GroundOverlap;

            if (pitchX < MinPitch || pitchZ < MinPitch) {
                log.Add($"Street '{first.id}' not laid: the ground overlap is nearly as big as the tile.");
                return;
            }

            // Only a square tile can be turned a quarter turn and still fit
            // its row.
            bool square = Mathf.Abs(style.TileWidth - style.TileDepth) <= settings.SizeTolerance;
            int firstRow = Mathf.FloorToInt(min.y / pitchZ);
            int lastRow = Mathf.CeilToInt(max.y / pitchZ);
            float slide = 0f;
            int placed = 0;
            FloorColliders floors = new(group);

            for (int row = firstRow; row <= lastRow; row++) {
                slide = RowSlide(slide, row == firstRow, pitchX, settings.GroundSlideMin, settings.GroundSlideMax, random);
                float z = row * pitchZ;
                int firstColumn = Mathf.FloorToInt((min.x - slide) / pitchX);
                int lastColumn = Mathf.CeilToInt((max.x - slide) / pitchX);
                PieceAnnotation previous = null;
                int turns = 0;
                floors.BeginRow(axisX);

                for (int column = firstColumn; column <= lastColumn; column++) {
                    Vector2 centre = origin + axisX * (column * pitchX + slide) + axisZ * z;
                    GroundRect tile = new() {
                        Centre = centre,
                        AxisX = axisX,
                        AxisZ = axisZ,
                        HalfX = style.TileWidth * 0.5f,
                        HalfZ = style.TileDepth * 0.5f
                    };
                    float coverage = Mathf.Max(Coverage(tile, rects, discs), LotCoverage(tile, lots, settings.MinTileCoverage));

                    if (coverage <= 0f || coverage < settings.MinTileCoverage || OverFlight(centre, flights, style)) {
                        floors.Break();
                        continue;
                    }

                    turns = TileTurns(turns, previous == null, square, settings.GroundTileTurns, random);
                    previous = PiecePlacer.Pick(style.Tiles, previous, random);
                    Vector3 surface = new(centre.x, HeightAt(area, lots, flights, centre, settings), centre.y);
                    GameObject placedTile = placer.PlaceTile(previous, surface, gridYaw + turns * 90f, group);
                    floors.Add(placedTile);

                    if (placedTile != null) {
                        placed++;
                    }
                }
            }

            floors.Break();
            int steps = 0;

            foreach (StreetSteps.Flight flight in flights) {
                steps += LayFlight(flight, style, settings, random, placer, group, floors, ref placed);
            }

            log.Add($"Ground '{style.Name}' for {Names(area)}: {placed} tile(s), {lastRow - firstRow + 1} row(s), {floors.Boxes} floor box(es) for {floors.Removed} tile collider(s)"
                + (flights.Count > 0 ? $", {flights.Count} flight(s) of steps ({steps} piece(s))." : "."));
        }

        /// <summary>
        /// Whether a grid tile centred at a point would lie over a flight of
        /// steps or the near part of its landing: on the high side of halfway
        /// up the flight and short of the landing's far edge less half a tile's
        /// diagonal, within the street's width plus half a diagonal of its
        /// middle line. A tile kept beyond that never reaches back over the
        /// steps, and the landing covers what it leaves; a tile on the low side
        /// lies under the steps or the landing.
        /// </summary>
        private static bool OverFlight(Vector2 centre, List<StreetSteps.Flight> flights, BuildingStyle style)
        {
            float halfDiagonal = StreetSteps.TileDiagonal(style) * 0.5f;

            foreach (StreetSteps.Flight flight in flights) {
                Vector2 offset = centre - flight.Start;
                float along = Vector2.Dot(offset, flight.Axis);
                float across = Mathf.Abs(Vector2.Dot(offset, new Vector2(-flight.Axis.y, flight.Axis.x)));
                float keptFrom = flight.Top + flight.Up * (flight.Landing - halfDiagonal);

                if (across <= flight.Width * 0.5f + halfDiagonal && (along - flight.Middle) * flight.Up >= 0f && (along - keptFrom) * flight.Up < 0f) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Lays one flight of steps across its street and its landing. The
        /// steps: Count flights of the piece stacked up the segment, each one's
        /// render box starting where the one below ends, side by side across
        /// the width (as many as round to it, overlapping by GroundOverlap and
        /// scaled along their width to fill it exactly), scaled in height so they make the rise exactly, each
        /// one's ramp foot at its level. The landing: LandingRows rows of tiles
        /// in the segment's own frame from the top step's edge, each row slid
        /// and each tile turned as on the grid, LandingDrop below the high
        /// level, their colliders replaced by floors' boxes (the steps keep
        /// their ramps). Returns how many steps pieces were placed; adds the
        /// landing's tiles to tiles.
        /// </summary>
        private static int LayFlight(StreetSteps.Flight flight, BuildingStyle style, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform parent, FloorColliders floors, ref int tiles)
        {
            Transform group = Group(parent, $"Steps - {flight.Street.id} {flight.Segment}-{flight.Segment + 1}");
            CatalogueEntry entry = flight.Entry;
            Vector3 way = StreetSteps.Way(entry);
            Vector3 sideways = Vector3.Cross(Vector3.up, way);
            StreetSteps.Reach(entry, way, out float lowAlong, out _);
            StreetSteps.Reach(entry, sideways, out float lowAcross, out float highAcross);
            float depth = StreetSteps.Span(entry, way);
            float pieceWidth = highAcross - lowAcross;
            int across = Mathf.Max(1, Mathf.RoundToInt(flight.Width / pieceWidth));
            // Side by side they overlap by the ground's overlap, so no crack
            // shows between them, and together span the width exactly.
            float overlap = settings.GroundOverlap;
            float placedWidth = (flight.Width + (across - 1) * overlap) / across;
            float widthScale = placedWidth / pieceWidth;
            float heightScale = (flight.High - flight.Low) / (flight.Count * flight.Piece.height);
            Vector3 scale = new(Mathf.Abs(sideways.x) * widthScale + Mathf.Abs(way.x), heightScale, Mathf.Abs(sideways.z) * widthScale + Mathf.Abs(way.z));
            // In its own space: the middle of its first step's front, at its
            // ramp's foot.
            Vector3 local = way * lowAlong + sideways * ((lowAcross + highAcross) * 0.5f) + Vector3.up * entry.ramps[0].foot.y;
            Vector2 uphill = flight.Axis * flight.Up;
            Vector2 normal = new(-flight.Axis.y, flight.Axis.x);
            float yaw = Yaw(uphill) - Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg;
            int placed = 0;

            for (int k = 0; k < flight.Count; k++) {
                Vector2 front = flight.Start + flight.Axis * (flight.Front + flight.Up * k * depth);
                float height = flight.Low + k * flight.Piece.height * heightScale;

                for (int c = 0; c < across; c++) {
                    Vector2 point = front + normal * (-flight.Width * 0.5f + placedWidth * 0.5f + c * (placedWidth - overlap));

                    if (placer.PlaceAt(flight.Piece, local, new Vector3(point.x, height, point.y), yaw, scale, group) != null) {
                        placed++;
                    }
                }
            }

            // The landing, rows across the street from the top step's edge.
            float pitchAlong = style.TileDepth - settings.GroundOverlap;
            float pitchAcross = style.TileWidth - settings.GroundOverlap;
            bool square = Mathf.Abs(style.TileWidth - style.TileDepth) <= settings.SizeTolerance;
            float tileYaw = Yaw(flight.Axis);
            float slide = 0f;

            for (int row = 0; row < flight.LandingRows; row++) {
                slide = RowSlide(slide, row == 0, pitchAcross, settings.GroundSlideMin, settings.GroundSlideMax, random);
                float along = flight.Top + flight.Up * (row * pitchAlong + style.TileDepth * 0.5f);
                // Tiles from one edge (less the slide) until one reaches the other.
                int count = Mathf.Max(1, Mathf.CeilToInt((flight.Width + slide - settings.GroundOverlap) / pitchAcross));
                PieceAnnotation previous = null;
                int turns = 0;
                // A landing row runs across the street.
                floors.BeginRow(normal);

                for (int k = 0; k < count; k++) {
                    float offset = -flight.Width * 0.5f - slide + style.TileWidth * 0.5f + k * pitchAcross;
                    Vector2 centre = flight.Start + flight.Axis * along + normal * offset;
                    turns = TileTurns(turns, previous == null, square, settings.GroundTileTurns, random);
                    previous = PiecePlacer.Pick(style.Tiles, previous, random);
                    GameObject placedTile = placer.PlaceTile(previous, new Vector3(centre.x, flight.High - settings.LandingDrop, centre.y), tileYaw + turns * 90f, group);
                    floors.Add(placedTile);

                    if (placedTile != null) {
                        tiles++;
                    }
                }
            }

            floors.Break();
            return placed;
        }

        /// <summary>
        /// Lays one street's decking, segment by segment, each in its own frame.
        /// </summary>
        private static void LayDecking(StreetSpec street, BuildingStyle style, GeneratorSettings settings, System.Random random,
            PiecePlacer placer, Transform group, List<string> log)
        {
            // A quarter turn swaps which of the tile's sides runs along the street.
            bool quarter = settings.DeckingQuarterTurn;
            float alongSize = quarter ? style.TileDepth : style.TileWidth;
            float acrossSize = quarter ? style.TileWidth : style.TileDepth;
            float pitchAlong = alongSize - settings.DeckingOverlap;
            float pitchAcross = acrossSize - settings.DeckingOverlap;

            if (pitchAlong < MinPitch || pitchAcross < MinPitch) {
                log.Add($"Street '{street.id}' not laid: the decking overlap is nearly as big as the tile.");
                return;
            }

            int placed = 0;
            FloorColliders floors = new(group);

            for (int i = 1; i < street.points.Count; i++) {
                Vector2 a = Ground(street.points[i - 1]);
                Vector2 b = Ground(street.points[i]);
                float length = (b - a).magnitude;

                if (length < settings.MinSegmentLength) {
                    continue;
                }

                Vector2 axisX = (b - a) / length;
                Vector2 axisZ = new(-axisX.y, axisX.x);
                float yaw = Yaw(axisZ) + (quarter ? 90f : 0f);
                // Enough rows to cover the segment's width, centred on the
                // middle line.
                float width = street.SegmentWidth(i - 1);
                int rows = Mathf.Max(1, Mathf.CeilToInt((width - settings.DeckingOverlap) / pitchAcross));
                float firstRowZ = -(rows - 1) * pitchAcross * 0.5f;
                // At a bend the segment runs on to meet the next one at the
                // outside edge.
                float start = -BendExtension(street, i - 1, settings);
                float end = length + BendExtension(street, i, settings);
                float slide = 0f;

                for (int row = 0; row < rows; row++) {
                    slide = RowSlide(slide, row == 0, pitchAlong, settings.DeckingSlideMin, settings.DeckingSlideMax, random);
                    float z = firstRowZ + row * pitchAcross;
                    // Tiles from the start (less the slide) until one reaches the end.
                    int count = Mathf.Max(1, Mathf.CeilToInt((end - start + slide - settings.DeckingOverlap) / pitchAlong));
                    PieceAnnotation previous = null;
                    int turns = 0;
                    floors.BeginRow(axisX);

                    for (int k = 0; k < count; k++) {
                        float x = start - slide + alongSize * 0.5f + k * pitchAlong;
                        Vector2 centre = a + axisX * x + axisZ * z;
                        float height = Mathf.Lerp(street.points[i - 1].y, street.points[i].y, Mathf.Clamp01(x / length));
                        // Decking only ever takes half turns, so the boards keep their direction.
                        turns = TileTurns(turns, previous == null, false, true, random);
                        previous = PiecePlacer.Pick(style.Tiles, previous, random);
                        GameObject placedTile = placer.PlaceTile(previous, new Vector3(centre.x, height, centre.y), yaw + turns * 90f, group);
                        floors.Add(placedTile);

                        if (placedTile != null) {
                            placed++;
                        }
                    }
                }
            }

            floors.Break();
            log.Add($"Decking '{style.Name}' for street '{street.id}': {placed} tile(s), {floors.Boxes} floor box(es) for {floors.Removed} tile collider(s).");
        }

        /// <summary>
        /// A tile's turn in quarter turns, from the tile before it along the
        /// row: a square tile turns a quarter, half or three-quarter turn from
        /// it, so no two neighbours lie the same way; any other tile takes a
        /// random half turn (so it still fits the row). The first tile of a
        /// row takes any turn. 0 for every tile if tiles aren't turned.
        /// </summary>
        private static int TileTurns(int previous, bool first, bool square, bool turnTiles, System.Random random)
        {
            if (!turnTiles) {
                return 0;
            }

            if (square) {
                return first ? random.Next(4) : (previous + random.Next(1, 4)) % 4;
            }

            return random.Next(2) * 2;
        }

        /// <summary>
        /// A row's slide along itself, from the row before it: a random share
        /// of a tile's pitch between min and max, kept within one pitch. The
        /// first row isn't slid.
        /// </summary>
        private static float RowSlide(float previous, bool first, float pitch, float min, float max, System.Random random)
        {
            if (first) {
                return 0f;
            }

            return Mathf.Repeat(previous + Mathf.Lerp(min, max, (float)random.NextDouble()) * pitch, pitch);
        }

        /// <summary>
        /// Adds a street's outline: a rectangle per segment at the segment's
        /// width, and a disc at every bend and at an end shared with another
        /// street of the area, as wide as the wider segment meeting there, to
        /// fill the wedge where segments meet at an angle (or step in width).
        /// </summary>
        private static void Outline(StreetSpec street, List<StreetSpec> area, GeneratorSettings settings,
            List<GroundRect> rects, List<GroundDisc> discs)
        {
            for (int i = 1; i < street.points.Count; i++) {
                Vector2 a = Ground(street.points[i - 1]);
                Vector2 b = Ground(street.points[i]);
                float length = (b - a).magnitude;

                if (length < settings.MinSegmentLength) {
                    continue;
                }

                Vector2 axis = (b - a) / length;
                rects.Add(new GroundRect {
                    Centre = (a + b) * 0.5f,
                    AxisX = axis,
                    AxisZ = new Vector2(-axis.y, axis.x),
                    HalfX = length * 0.5f,
                    HalfZ = street.SegmentWidth(i - 1) * 0.5f
                });
            }

            int last = street.points.Count - 1;

            for (int k = 0; k <= last; k++) {
                bool bend = k > 0 && k < last;

                if (bend || JoinsOther(street, k, area, settings)) {
                    discs.Add(new GroundDisc { Centre = Ground(street.points[k]), Radius = street.WidthAt(k) * 0.5f });
                }
            }
        }

        /// <summary>
        /// How far a tile overlaps the outline: the deepest it reaches into
        /// any of its rectangles or discs (0 or less when it doesn't touch).
        /// </summary>
        private static float Coverage(GroundRect tile, List<GroundRect> rects, List<GroundDisc> discs)
        {
            float deepest = float.MinValue;

            foreach (GroundRect rect in rects) {
                // Separating axes: the overlap is the least along any of them.
                float depth = Mathf.Min(
                    Mathf.Min(DepthAlong(tile, rect, tile.AxisX), DepthAlong(tile, rect, tile.AxisZ)),
                    Mathf.Min(DepthAlong(tile, rect, rect.AxisX), DepthAlong(tile, rect, rect.AxisZ)));
                deepest = Mathf.Max(deepest, depth);
            }

            foreach (GroundDisc disc in discs) {
                // The disc's centre's distance from the tile (0 inside it).
                Vector2 offset = disc.Centre - tile.Centre;
                float outX = Mathf.Max(Mathf.Abs(Vector2.Dot(offset, tile.AxisX)) - tile.HalfX, 0f);
                float outZ = Mathf.Max(Mathf.Abs(Vector2.Dot(offset, tile.AxisZ)) - tile.HalfZ, 0f);
                deepest = Mathf.Max(deepest, disc.Radius - Mathf.Sqrt(outX * outX + outZ * outZ));
            }

            return deepest;
        }

        /// <summary>
        /// How far a tile reaches into the ground round any of the lots: into
        /// the footprint grown by its margin (separating axes: the tile's and
        /// the footprint's sides), but nothing for a tile lying wholly inside
        /// the footprint - every corner more than minCoverage in - since
        /// that is under the building.
        /// </summary>
        private static float LotCoverage(GroundRect tile, List<GroundLot> lots, float minCoverage)
        {
            float deepest = float.MinValue;

            foreach (GroundLot lot in lots) {
                bool under = true;

                for (int corner = 0; corner < 4 && under; corner++) {
                    Vector2 point = tile.Centre + tile.AxisX * (corner < 2 ? tile.HalfX : -tile.HalfX)
                        + tile.AxisZ * (corner % 2 == 0 ? tile.HalfZ : -tile.HalfZ);
                    under = lot.Outside(point) < -minCoverage;
                }

                if (under) {
                    continue;
                }

                float depth = Mathf.Min(DepthIntoLot(tile, lot, tile.AxisX), DepthIntoLot(tile, lot, tile.AxisZ));

                foreach (Vector2 axis in lot.Outward) {
                    depth = Mathf.Min(depth, DepthIntoLot(tile, lot, axis));
                }

                deepest = Mathf.Max(deepest, depth);
            }

            return deepest;
        }

        /// <summary>
        /// How far a tile's shadow on one axis overlaps that of a lot's
        /// footprint grown by its margin.
        /// </summary>
        private static float DepthIntoLot(GroundRect tile, GroundLot lot, Vector2 axis)
        {
            float low = float.MaxValue;
            float high = float.MinValue;

            foreach (Vector2 corner in lot.Corners) {
                float along = Vector2.Dot(corner, axis);
                low = Mathf.Min(low, along);
                high = Mathf.Max(high, along);
            }

            float centre = Vector2.Dot(tile.Centre, axis);
            float radius = Radius(tile, axis);
            return Mathf.Min(centre + radius, high + lot.Margin) - Mathf.Max(centre - radius, low - lot.Margin);
        }

        /// <summary>
        /// How far two rectangles' shadows on one axis overlap.
        /// </summary>
        private static float DepthAlong(GroundRect a, GroundRect b, Vector2 axis)
        {
            float distance = Mathf.Abs(Vector2.Dot(b.Centre - a.Centre, axis));
            return Radius(a, axis) + Radius(b, axis) - distance;
        }

        /// <summary>
        /// Half a rectangle's extent along an axis.
        /// </summary>
        private static float Radius(GroundRect rect, Vector2 axis)
        {
            return Mathf.Abs(Vector2.Dot(rect.AxisX, axis)) * rect.HalfX + Mathf.Abs(Vector2.Dot(rect.AxisZ, axis)) * rect.HalfZ;
        }

        /// <summary>
        /// The ground height under a point: a lot's street level inside its
        /// ground, else that of the nearest point on any of the area's middle
        /// lines - on a segment with a flight of steps, the level of the
        /// flight's side that point is on.
        /// </summary>
        private static float HeightAt(List<StreetSpec> area, List<GroundLot> lots, List<StreetSteps.Flight> flights, Vector2 point,
            GeneratorSettings settings)
        {
            foreach (GroundLot lot in lots) {
                if (lot.Outside(point) <= lot.Margin) {
                    return lot.Height;
                }
            }

            float nearest = float.MaxValue;
            float height = area[0].points[0].y;

            foreach (StreetSpec street in area) {
                for (int i = 1; i < street.points.Count; i++) {
                    Vector2 a = Ground(street.points[i - 1]);
                    Vector2 b = Ground(street.points[i]);
                    Vector2 ab = b - a;

                    if (ab.magnitude < settings.MinSegmentLength) {
                        continue;
                    }

                    float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
                    float distance = (a + ab * t - point).sqrMagnitude;

                    if (distance < nearest) {
                        nearest = distance;
                        StreetSteps.Flight flight = StreetSteps.Of(flights, street, i - 1);
                        height = flight != null ? flight.HeightAt(t * ab.magnitude) : Mathf.Lerp(street.points[i - 1].y, street.points[i].y, t);
                    }
                }
            }

            return height;
        }

        /// <summary>
        /// How far a segment runs on past a bend at point k to meet the next
        /// segment at the outside edge: half the width times the tangent of
        /// half the turn (at most the width, for a sharp turn), the width
        /// being the wider of the two segments', so the wider edge is met
        /// too. 0 at an end.
        /// </summary>
        private static float BendExtension(StreetSpec street, int k, GeneratorSettings settings)
        {
            if (k <= 0 || k >= street.points.Count - 1) {
                return 0f;
            }

            Vector2 inward = Ground(street.points[k]) - Ground(street.points[k - 1]);
            Vector2 outward = Ground(street.points[k + 1]) - Ground(street.points[k]);

            if (inward.magnitude < settings.MinSegmentLength || outward.magnitude < settings.MinSegmentLength) {
                return 0f;
            }

            float turn = Vector2.Angle(inward, outward) * Mathf.Deg2Rad;
            float width = street.WidthAt(k);
            return Mathf.Min(width * 0.5f * Mathf.Tan(turn * 0.5f), width);
        }

        /// <summary>
        /// The direction of a street's first segment long enough to count.
        /// </summary>
        private static bool FirstDirection(StreetSpec street, GeneratorSettings settings, out Vector2 direction)
        {
            for (int i = 1; i < street.points.Count; i++) {
                Vector2 along = Ground(street.points[i]) - Ground(street.points[i - 1]);

                if (along.magnitude >= settings.MinSegmentLength) {
                    direction = along.normalized;
                    return true;
                }
            }

            direction = Vector2.zero;
            return false;
        }

        /// <summary>
        /// Whether a street's point k is at a point of another street in the area.
        /// </summary>
        private static bool JoinsOther(StreetSpec street, int k, List<StreetSpec> area, GeneratorSettings settings)
        {
            Vector2 point = Ground(street.points[k]);

            foreach (StreetSpec other in area) {
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
        /// Whether two streets share a point (within the junction distance).
        /// </summary>
        private static bool SharesPoint(StreetSpec a, StreetSpec b, GeneratorSettings settings)
        {
            foreach (Vector3 pointA in a.points) {
                foreach (Vector3 pointB in b.points) {
                    if ((Ground(pointA) - Ground(pointB)).magnitude <= settings.JunctionDistance) {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A point in a frame's coordinates (x along axisX, y along axisZ).
        /// </summary>
        private static Vector2 ToFrame(Vector2 point, Vector2 origin, Vector2 axisX, Vector2 axisZ)
        {
            Vector2 offset = point - origin;
            return new Vector2(Vector2.Dot(offset, axisX), Vector2.Dot(offset, axisZ));
        }

        /// <summary>
        /// Grows a min/max box to hold a point give or take an extent.
        /// </summary>
        private static void Encapsulate(Vector2 point, Vector2 extent, ref Vector2 min, ref Vector2 max)
        {
            min = Vector2.Min(min, point - extent);
            max = Vector2.Max(max, point + extent);
        }

        /// <summary>
        /// The yaw (degrees clockwise from +Z) that turns +Z to a level direction.
        /// </summary>
        private static float Yaw(Vector2 forward)
        {
            return Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;
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

        /// <summary>
        /// The streets' ids, for the log.
        /// </summary>
        private static string Names(List<StreetSpec> streets)
        {
            List<string> names = new();

            foreach (StreetSpec street in streets) {
                names.Add($"'{street.id}'");
            }

            return string.Join(", ", names);
        }

        /// <summary>
        /// A point on the ground: its x and z.
        /// </summary>
        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }
    }
}
