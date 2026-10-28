using System.Collections.Generic;
using Core;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Stage 4: lays every street's ground.
    ///
    /// A street of ground (any style with no floor pieces: cobbles) is
    /// meshes built by GroundMesh per street area - the streets of that
    /// style joined at shared points - on one grid turned at random, never
    /// squared to the world's axes (GroundGridMinAngle). The area's outline
    /// is each segment's rectangle, a disc at every bend and junction to
    /// fill the wedges, and the ground round each lot nearest the area: a
    /// ring from its footprint out to LotGroundMargin, at the lot's street
    /// level, never under the building. A grid cell is kept where any of
    /// its corners or its middle lies in the outline grown by
    /// GroundEdgeMargin, so the ground runs on under the buildings lining
    /// the street and there are no gaps at bends or junctions. Every vertex
    /// stands at the street's surface (StreetHeights.GroundAt). The ground
    /// is one mesh per level and per slope (RegionOf, CutsLevel): each
    /// sloping segment's cells - those whose nearest segment it is - are a
    /// region of their own, the rest are joined into one region per
    /// connected level, and a flight of steps (StreetSteps, LayFlight)
    /// parts two levels: no cell lies wholly under its steps, and the
    /// vertices that would are moved to just inside the flight's front or
    /// top line (PlaceVertex), so the lower level runs on under the lowest
    /// step and the upper one starts under the top step's tread, sunk out
    /// of sight (HeightAt): no slit shows at either edge. The
    /// vertices are built plain dirt and painted once the facades stand
    /// (Paint, after FacadeBuilder.Build): a dirt border GroundDirtBorder
    /// wide out from the foundations actually placed, following every
    /// column stepped forward or back, cobbles beyond, the edge between them
    /// wobbled by noise, and puddles of wet dirt wherever a second noise
    /// rises above GroundPuddleCover; the shader blends the three by their
    /// height maps.
    ///
    /// The ground collides by its own meshes (GroundMesh: a MeshCollider
    /// per chunk, with a Stone SurfaceTag), so what is walked on is what
    /// is seen. The steps keep their own ramps, and a box under each
    /// flight's top step (LayColliders) closes the gap between the ramp's
    /// top and the upper level.
    ///
    /// A style with floor pieces (plank decking: wooden streets, walkways,
    /// bridges) is laid along each segment in that segment's own frame, so
    /// the boards follow the walkway; at a bend each segment runs on to
    /// meet the next at the outside edge. The tiles are laid in rows, each
    /// row slid from the one before it and each tile turned a half turn
    /// from the one before it along its row, neighbours overlapping
    /// slightly; the tiles' colliders are replaced by one box per run of
    /// tiles along a row at one height (FloorColliders).
    ///
    /// As an area's ground is built, a share of its GrassPatch-square
    /// patches (GrassCover) get a cluster of grass
    /// (StreetFoliage.PlaceCluster) about a random point in the patch,
    /// where that point stands in the open street (InOpenStreet:
    /// GrassEdgeMargin in from the street's edges, from every lot's
    /// footprint and from a flight of steps), under a "Grass - street"
    /// group of the foliage parent; the foliage's own random sequence is
    /// drawn from, so the ground comes out the same with or without it.
    /// Decking gets no grass.
    /// </summary>
    internal static class StreetGround
    {
        // A tile less this far from one row or column to the next (after the
        // overlap) can't be laid.
        private const float MinPitch = 0.1f;
        // How far the ground runs on under a lot's walls, so the floor meets
        // the foundations with no gap.
        private const float UnderLotWalls = 0.5f;
        // A paint value this small is nothing.
        private const float PaintEpsilon = 0.004f;

        /// <summary>
        /// A rectangle on the ground (x, z).
        /// </summary>
        internal struct GroundRect
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
        internal struct GroundDisc
        {
            public Vector2 Centre;
            public float Radius;
        }

        /// <summary>
        /// One ground area's meshes and outline, kept from Build for Paint.
        /// </summary>
        internal class PaintArea
        {
            public List<Mesh> Meshes;
            public List<GroundRect> Rects;
            public List<GroundDisc> Discs;
        }

        /// <summary>
        /// What Build laid that Paint colours once the facades stand: every
        /// ground area's meshes and outline, and the spec's seed for the
        /// paint's noise.
        /// </summary>
        internal class Painting
        {
            public int Seed;
            public readonly List<PaintArea> Areas = new();
        }

        /// <summary>
        /// Lays every street in the spec, in the spec's order (so the random
        /// choices come in a fixed order), each area or street under a group
        /// of its own in parent. Adds a line per area or street to log.
        /// Returns the unpainted ground, for Paint once the facades stand.
        /// </summary>
        internal static Painting Build(LayoutSpec spec, Dictionary<string, BuildingStyle> styles, StreetHeights heights, GeneratorSettings settings,
            System.Random random, System.Random foliageRandom, PiecePlacer placer, Transform parent, Transform foliage, List<string> log)
        {
            // The paint's noise fields come from the spec's seed, so the
            // same spec paints the same ground.
            Painting painting = new() { Seed = spec.seed };
            HashSet<StreetSpec> done = new();
            // Each street in order: a ground area (its first street, style and
            // streets) or a decked street, so the lots can be shared out
            // before anything is laid.
            List<(StreetSpec First, BuildingStyle Style, List<StreetSpec> Area)> laid = new();

            foreach (StreetSpec street in spec.streets) {
                if (!done.Add(street)) {
                    continue;
                }

                BuildingStyle style = StreetStyle(street, styles, log);

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

            for (int i = 0; i < laid.Count; i++) {
                (StreetSpec first, BuildingStyle style, List<StreetSpec> area) = laid[i];

                if (area != null) {
                    LayArea(spec, area, lotGround[i], heights, style, settings, random, foliageRandom, placer, Group(parent, $"Ground - {first.id}"),
                        foliage, painting, log);
                } else {
                    LayDecking(first, style, settings, random, placer, Group(parent, $"Decking - {first.id}"), log);
                }
            }

            return painting;
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
        /// can't be laid: too few points, no style, or a decked style with
        /// no usable floor tiles.
        /// </summary>
        private static BuildingStyle StreetStyle(StreetSpec street, Dictionary<string, BuildingStyle> styles, List<string> log)
        {
            if (street.points.Count < 2) {
                log.Add($"Street '{street.id}' not laid: it needs at least two points.");
                return null;
            }

            if (!styles.TryGetValue(street.style ?? "", out BuildingStyle style)) {
                log.Add($"Street '{street.id}' not laid: no style '{street.style}'.");
                return null;
            }

            if (style.TileCategory == "floor" && style.Tiles.Count == 0) {
                log.Add($"Street '{street.id}' not laid: style '{street.style}' has no usable floor tiles.");
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
        /// Builds one area's ground: its mesh (GroundMesh) over the area's
        /// streets and the ground under the lots given to it, every vertex
        /// at the street's level there, plain dirt (its meshes and outline
        /// added to painting, for Paint); then its colliders, its flights of
        /// steps (LayFlight) and its grass.
        /// </summary>
        private static void LayArea(LayoutSpec spec, List<StreetSpec> area, List<GroundLot> lots, StreetHeights heights, BuildingStyle style,
            GeneratorSettings settings, System.Random random, System.Random foliageRandom, PiecePlacer placer, Transform group, Transform foliage,
            Painting painting, List<string> log)
        {
            StreetSpec first = area[0];
            // The area's own flights of steps and slopes.
            List<StreetSteps.Flight> flights = heights.Flights.FindAll(flight => area.Contains(flight.Street));
            List<StreetSlopes.Slope> slopes = heights.Slopes.FindAll(slope => area.Contains(slope.Street));

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

            List<GroundRect> rects = new();
            List<GroundDisc> discs = new();

            foreach (StreetSpec street in area) {
                Outline(street, area, settings, rects, discs);
            }

            // The outline's extent in the grid's frame, grown by the edge
            // margin the ground runs on past it.
            float margin = settings.GroundEdgeMargin;
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);

            foreach (GroundRect rect in rects) {
                for (int corner = 0; corner < 4; corner++) {
                    Vector2 point = rect.Centre + rect.AxisX * (corner < 2 ? rect.HalfX : -rect.HalfX)
                        + rect.AxisZ * (corner % 2 == 0 ? rect.HalfZ : -rect.HalfZ);
                    Encapsulate(ToFrame(point, origin, axisX, axisZ), Vector2.one * margin, ref min, ref max);
                }
            }

            foreach (GroundDisc disc in discs) {
                Encapsulate(ToFrame(disc.Centre, origin, axisX, axisZ), Vector2.one * (disc.Radius + margin), ref min, ref max);
            }

            foreach (GroundLot lot in lots) {
                foreach (Vector2 corner in lot.Corners) {
                    Encapsulate(ToFrame(corner, origin, axisX, axisZ), Vector2.one * (lot.Margin + margin), ref min, ref max);
                }
            }

            GroundMesh.Frame frame = new() { Origin = origin, AxisX = axisX, AxisZ = axisZ, Min = min, Max = max };
            GroundMesh.Result mesh = GroundMesh.Build(spec.sector, first.id, frame, settings.GroundCell,
                (corners, centre) => RegionOf(corners, centre, area, rects, discs, lots, heights, flights, slopes, settings, margin),
                (a, b) => CutsLevel(a, b, area, heights, settings),
                point => PlaceVertex(point, flights, settings),
                point => HeightAt(area, lots, heights, point, settings),
                (region, point) => RegionName(region, point, area, lots, heights, slopes, settings),
                group, log);
            painting.Areas.Add(new PaintArea { Meshes = mesh.Meshes, Rects = rects, Discs = discs });

            int boxes = LayColliders(area, heights, flights, settings, group);
            int steps = 0;

            foreach (StreetSteps.Flight flight in flights) {
                steps += LayFlight(flight, settings, placer, group);
            }

            // Grass: a cluster per patch of the area at GrassCover, about a
            // random point in the patch, where that point stands in the open
            // street.
            int clusters = 0;
            int tufts = 0;
            Transform grass = null;
            float patch = Mathf.Max(settings.GrassPatch, 0.5f);

            if (StreetFoliage.HasGrass) {
                for (float x = Mathf.Floor(min.x / patch) * patch; x < max.x; x += patch) {
                    for (float z = Mathf.Floor(min.y / patch) * patch; z < max.y; z += patch) {
                        if (!Inside(frame.Point(x + patch * 0.5f, z + patch * 0.5f), rects, discs, lots, 0f)) {
                            continue;
                        }

                        if (foliageRandom.NextDouble() >= settings.GrassCover) {
                            continue;
                        }

                        Vector2 at = frame.Point(x + (float)foliageRandom.NextDouble() * patch, z + (float)foliageRandom.NextDouble() * patch);

                        if (!InOpenStreet(at, rects, discs, lots, flights, settings.GrassEdgeMargin)) {
                            continue;
                        }

                        if (grass == null) {
                            grass = Group(foliage, $"Grass - {first.id}");
                        }

                        tufts += StreetFoliage.PlaceCluster(at, point => HeightAt(area, lots, heights, point, settings), settings, foliageRandom,
                            placer, grass);
                        clusters++;
                    }
                }
            }

            log.Add($"Ground '{style.Name}' for {Names(area)}: {mesh.Regions} mesh region(s) in {mesh.Chunks} chunk(s), {mesh.Vertices} vertices, {mesh.Triangles} triangles, "
                + $"{boxes} top-step collider box(es)"
                + (flights.Count > 0 ? $", {flights.Count} flight(s) of steps ({steps} piece(s))" : "")
                + (slopes.Count > 0 ? $", {slopes.Count} slope(s)" : "")
                + $", {clusters} grass cluster(s) ({tufts} tuft(s)).");
        }

        /// <summary>
        /// Whether a point on the ground is within the area's outline grown
        /// by margin: in a segment's rectangle or a bend's disc, or in a
        /// lot's ring (from a little under its walls out to its margin).
        /// </summary>
        private static bool Inside(Vector2 point, List<GroundRect> rects, List<GroundDisc> discs, List<GroundLot> lots, float margin)
        {
            if (Depth(point, rects, discs) >= -margin) {
                return true;
            }

            foreach (GroundLot lot in lots) {
                float outside = lot.Outside(point);

                if (outside >= -UnderLotWalls && outside <= lot.Margin + margin) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Paints the ground Build laid, once the facades stand (straight
        /// after FacadeBuilder.Build): every vertex of every chunk gets its
        /// colour (PaintAt) from its distance to the foundations placed under
        /// root (MergedColliders.Foundations), each foundation's footprint
        /// the box round its meshes on the ground. The meshes are saved.
        /// Adds one line to log.
        /// </summary>
        internal static void Paint(Painting painting, Transform root, PieceAnnotations annotations, GeneratorSettings settings, List<string> log)
        {
            List<GroundRect> footprints = new();

            foreach (GameObject piece in MergedColliders.Foundations(root, annotations)) {
                foreach (MeshFilter filter in piece.GetComponentsInChildren<MeshFilter>(true)) {
                    if (filter.sharedMesh != null) {
                        footprints.Add(Footprint(filter));
                    }
                }
            }

            // Past this far from every foundation a point is all cobbles
            // whatever the noise, so only the footprints this near a chunk
            // are looked at for its vertices.
            float reach = settings.GroundDirtBorder + settings.GroundPaintWobble + settings.GroundPaintFade;
            List<GroundRect> near = new();
            List<Vector3> vertices = new();
            List<Color32> colours = new();
            int chunks = 0;
            int painted = 0;

            foreach (PaintArea area in painting.Areas) {
                foreach (Mesh mesh in area.Meshes) {
                    Bounds bounds = mesh.bounds;
                    near.Clear();

                    foreach (GroundRect footprint in footprints) {
                        if (Outside(new Vector2(bounds.center.x, bounds.center.z), footprint)
                            <= new Vector2(bounds.extents.x, bounds.extents.z).magnitude + reach) {
                            near.Add(footprint);
                        }
                    }

                    mesh.GetVertices(vertices);
                    colours.Clear();

                    foreach (Vector3 vertex in vertices) {
                        colours.Add(PaintAt(new Vector2(vertex.x, vertex.z), near, area, settings, painting.Seed));
                    }

                    mesh.SetColors(colours);
                    EditorUtility.SetDirty(mesh);
                    chunks++;
                    painted += vertices.Count;
                }
            }

            AssetDatabase.SaveAssets();
            log.Add($"Ground paint: {painted} vertices in {chunks} chunk(s), dirt border from {footprints.Count} foundation footprint(s).");
        }

        /// <summary>
        /// A foundation mesh's footprint on the ground: the rectangle round
        /// its bounds' corners, along the mesh's own right and forward
        /// (foundations stand upright), so a turned foundation keeps its
        /// true front.
        /// </summary>
        private static GroundRect Footprint(MeshFilter filter)
        {
            Transform t = filter.transform;
            Vector3 right = t.right;
            Vector2 axisX = new Vector2(right.x, right.z).sqrMagnitude > 0.0001f
                ? new Vector2(right.x, right.z).normalized
                : new Vector2(t.forward.x, t.forward.z).normalized;
            Vector2 axisZ = new(-axisX.y, axisX.x);
            Bounds bounds = filter.sharedMesh.bounds;
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);

            for (int corner = 0; corner < 8; corner++) {
                Vector3 local = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                Vector3 world = t.TransformPoint(local);
                Vector2 along = new(world.x * axisX.x + world.z * axisX.y, world.x * axisZ.x + world.z * axisZ.y);
                min = Vector2.Min(min, along);
                max = Vector2.Max(max, along);
            }

            Vector2 middle = (min + max) * 0.5f;
            return new GroundRect {
                Centre = axisX * middle.x + axisZ * middle.y,
                AxisX = axisX,
                AxisZ = axisZ,
                HalfX = (max.x - min.x) * 0.5f,
                HalfZ = (max.y - min.y) * 0.5f
            };
        }

        /// <summary>
        /// How far a point on the ground lies outside a rectangle (0 inside).
        /// </summary>
        private static float Outside(Vector2 point, GroundRect rect)
        {
            Vector2 offset = point - rect.Centre;
            float x = Mathf.Max(Mathf.Abs(Vector2.Dot(offset, rect.AxisX)) - rect.HalfX, 0f);
            float z = Mathf.Max(Mathf.Abs(Vector2.Dot(offset, rect.AxisZ)) - rect.HalfZ, 0f);
            return Mathf.Sqrt(x * x + z * z);
        }

        /// <summary>
        /// The vertex colour at a point: red the cobbles, blue the wet dirt,
        /// nothing the dirt beneath. A dirt border GroundDirtBorder wide runs
        /// out from the nearest foundation's footprint, cobbles beyond it, so
        /// it steps out and back with every column; its inner edge is moved
        /// in and out by up to GroundPaintWobble by a noise of
        /// GroundPaintScale and fades over GroundPaintFade. Where no
        /// foundation stands (a gap, an open street end) the street's edge
        /// stands in, set back by ColumnDepthMax as if the deepest-set column
        /// stood there: a nearer foundation always wins, so a column set back
        /// still measures from its own foundation, and the outline never
        /// paints a column's recess all dirt. Puddles lie wherever a second
        /// noise of GroundPuddleScale rises into its top GroundPuddleCover.
        /// </summary>
        private static Color32 PaintAt(Vector2 point, List<GroundRect> footprints, PaintArea area, GeneratorSettings settings, int seed)
        {
            // How far the point lies out from the nearest foundation.
            float fromFoundation = Depth(point, area.Rects, area.Discs) - settings.FoundationFrontOffset + settings.ColumnDepthMax;

            foreach (GroundRect footprint in footprints) {
                fromFoundation = Mathf.Min(fromFoundation, Outside(point, footprint));
            }

            float wobble = (GroundMesh.Noise(point / Mathf.Max(settings.GroundPaintScale, 0.1f), seed) - 0.5f) * 2f * settings.GroundPaintWobble;
            float edge = settings.GroundDirtBorder + wobble;
            float cobbles = Mathf.Clamp01((fromFoundation - edge) / Mathf.Max(settings.GroundPaintFade, 0.01f) + 0.5f);

            float puddle = GroundMesh.Noise(point / Mathf.Max(settings.GroundPuddleScale, 0.1f) + new Vector2(41.3f, 17.9f), seed + 7);
            float threshold = 1f - settings.GroundPuddleCover;
            float wet = Mathf.Clamp01((puddle - threshold) / 0.12f);

            if (cobbles < PaintEpsilon) {
                cobbles = 0f;
            }

            if (wet < PaintEpsilon) {
                wet = 0f;
            }

            return new Color32((byte)Mathf.RoundToInt(cobbles * 255f), 0, (byte)Mathf.RoundToInt(wet * 255f), 255);
        }

        /// <summary>
        /// The one piece of the ground's collision its meshes don't give: a
        /// box under each flight's top step. The steps' ramp collider stops
        /// a little short of the top step's back edge, where the upper
        /// level's mesh begins, so a box GroundStepsOverlap long runs back
        /// from that edge under the step's flat tread, its top at the upper
        /// level, across the street and GroundEdgeMargin past each edge;
        /// GroundThickness deep, under a "Colliders" child of the group.
        /// Returns how many boxes.
        /// </summary>
        private static int LayColliders(List<StreetSpec> area, StreetHeights heights, List<StreetSteps.Flight> flights, GeneratorSettings settings,
            Transform group)
        {
            Transform parent = null;
            float thickness = settings.GroundThickness;
            int boxes = 0;

            foreach (StreetSpec street in area) {
                for (int i = 1; i < street.points.Count; i++) {
                    StreetSteps.Flight flight = StreetSteps.Of(flights, street, i - 1);

                    if (flight == null) {
                        continue;
                    }

                    Vector2 a = Ground(street.points[i - 1]);
                    Vector2 b = Ground(street.points[i]);
                    float length = (b - a).magnitude;
                    Vector2 axis = (b - a) / length;
                    // The strip runs from the top step's back edge back down
                    // the flight, whichever way it climbs; the upper level is
                    // the segment's higher point.
                    float overlap = settings.GroundStepsOverlap;
                    float from = flight.Up > 0f ? flight.Top - overlap : flight.Top;
                    float to = flight.Up > 0f ? flight.Top : flight.Top + overlap;
                    float height = heights.GroundAt(street, i - 1, flight.Up > 0f ? length : 0f);
                    float width = street.SegmentWidth(i - 1) + 2f * settings.GroundEdgeMargin;
                    Vector2 at = a + axis * ((from + to) * 0.5f);
                    parent ??= Group(group, "Colliders");
                    PlaceBox(parent, $"{street.id} {i - 1}-{i} top step", new Vector3(at.x, height - thickness * 0.5f, at.y),
                        Quaternion.Euler(0f, Yaw(axis), 0f), new Vector3(width, thickness, to - from), settings);
                    boxes++;
                }
            }

            return boxes;
        }

        /// <summary>
        /// One ground collider box: local X across the street, Z along it,
        /// Y up, on the Environment layer, static, with a Stone SurfaceTag.
        /// </summary>
        private static void PlaceBox(Transform parent, string name, Vector3 centre, Quaternion rotation, Vector3 size, GeneratorSettings settings)
        {
            GameObject box = new($"Ground {name}") {
                layer = LayerMask.NameToLayer("Environment")
            };
            GameObjectUtility.SetStaticEditorFlags(box, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.BatchingStatic);
            box.transform.SetParent(parent, false);
            box.transform.SetPositionAndRotation(centre, rotation);
            box.AddComponent<BoxCollider>().size = size;

            SurfaceTag tag = box.AddComponent<SurfaceTag>();
            SerializedObject serialized = new(tag);
            serialized.FindProperty("surface").enumValueIndex = (int)SurfaceType.Stone;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Whether a point on the ground stands in the open street: at least
        /// margin inside the area's outline (its segments' rectangles and the
        /// discs at their bends and junctions), at least margin outside every
        /// lot's footprint, and more than margin from any flight of steps -
        /// so a cluster of grass never stands under a building, against its
        /// foundations or on the steps.
        /// </summary>
        private static bool InOpenStreet(Vector2 point, List<GroundRect> rects, List<GroundDisc> discs, List<GroundLot> lots,
            List<StreetSteps.Flight> flights, float margin)
        {
            if (Depth(point, rects, discs) < margin) {
                return false;
            }

            foreach (GroundLot lot in lots) {
                if (lot.Outside(point) < margin) {
                    return false;
                }
            }

            foreach (StreetSteps.Flight flight in flights) {
                Vector2 offset = point - flight.Start;
                float along = Vector2.Dot(offset, flight.Axis);
                float across = Mathf.Abs(Vector2.Dot(offset, new Vector2(-flight.Axis.y, flight.Axis.x)));
                float low = Mathf.Min(flight.Front, flight.Top);
                float high = Mathf.Max(flight.Front, flight.Top);

                if (across <= flight.Width * 0.5f + margin && along >= low - margin && along <= high + margin) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Lays one flight of steps across its street: Count flights of the
        /// piece stacked up the segment, each one's render box starting
        /// where the one below ends, side by side across the width (as many
        /// as round to it, overlapping by GroundOverlap and scaled along
        /// their width to fill it exactly), scaled in height so they make
        /// the rise exactly, each one's ramp foot at its level. The ground
        /// mesh stands at the high level from the top step's edge on.
        /// Returns how many pieces were placed.
        /// </summary>
        private static int LayFlight(StreetSteps.Flight flight, GeneratorSettings settings, PiecePlacer placer, Transform parent)
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
                        turns = TileTurns(random);
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
        /// A decking tile's turn in quarter turns: a random half turn (so it
        /// still fits the row and its boards keep their direction).
        /// </summary>
        private static int TileTurns(System.Random random)
        {
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
        /// How deep a point lies in the outline: the deepest it reaches into
        /// any of its rectangles or discs (0 on an edge, below 0 outside).
        /// </summary>
        private static float Depth(Vector2 point, List<GroundRect> rects, List<GroundDisc> discs)
        {
            float deepest = float.MinValue;

            foreach (GroundRect rect in rects) {
                Vector2 offset = point - rect.Centre;
                float depth = Mathf.Min(rect.HalfX - Mathf.Abs(Vector2.Dot(offset, rect.AxisX)), rect.HalfZ - Mathf.Abs(Vector2.Dot(offset, rect.AxisZ)));
                deepest = Mathf.Max(deepest, depth);
            }

            foreach (GroundDisc disc in discs) {
                deepest = Mathf.Max(deepest, disc.Radius - (disc.Centre - point).magnitude);
            }

            return deepest;
        }

        /// <summary>
        /// The ground height under a point: a lot's street level inside its
        /// ground, else that of the nearest point on any of the area's middle
        /// lines - on a segment with a flight of steps, the level of the
        /// flight's side that point is on, sunk GroundThickness under the
        /// top step's tread (where PlaceVertex runs the upper ground's edge
        /// on under it, out of sight); on a slope, the slope's surface.
        /// </summary>
        private static float HeightAt(List<StreetSpec> area, List<GroundLot> lots, StreetHeights heights, Vector2 point,
            GeneratorSettings settings)
        {
            foreach (GroundLot lot in lots) {
                if (lot.Outside(point) <= lot.Margin) {
                    return lot.Height;
                }
            }

            if (!NearestSegment(area, point, settings, out StreetSpec nearest, out int segment, out float along)) {
                return area[0].points[0].y;
            }

            float height = heights.GroundAt(nearest, segment, along);
            StreetSteps.Flight flight = heights.FlightOf(nearest, segment);

            if (flight != null && UnderSteps(flight, point) && (along - flight.Middle) * flight.Up >= 0f && (flight.Top - along) * flight.Up > 0.001f) {
                height -= settings.GroundThickness;
            }

            return height;
        }

        /// <summary>
        /// The area's segment whose middle line passes nearest a point, and
        /// how far along it the nearest point of the line lies. False if the
        /// area has no segment long enough to count.
        /// </summary>
        private static bool NearestSegment(List<StreetSpec> area, Vector2 point, GeneratorSettings settings, out StreetSpec nearest, out int segment,
            out float along)
        {
            float best = float.MaxValue;
            nearest = null;
            segment = 0;
            along = 0f;

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

                    if (distance < best) {
                        best = distance;
                        nearest = street;
                        segment = i - 1;
                        along = t * ab.magnitude;
                    }
                }
            }

            return nearest != null;
        }

        /// <summary>
        /// Where a point stands against a flight of steps: its distance along
        /// the flight's segment and its distance across from the middle line.
        /// </summary>
        private static void AgainstFlight(StreetSteps.Flight flight, Vector2 point, out float along, out float across)
        {
            Vector2 offset = point - flight.Start;
            along = Vector2.Dot(offset, flight.Axis);
            across = Vector2.Dot(offset, new Vector2(-flight.Axis.y, flight.Axis.x));
        }

        /// <summary>
        /// Whether a point lies under a flight's steps: between its front and
        /// its top along the segment, within the street's width (what the
        /// steps pieces span).
        /// </summary>
        private static bool UnderSteps(StreetSteps.Flight flight, Vector2 point)
        {
            AgainstFlight(flight, point, out float along, out float across);
            return along >= Mathf.Min(flight.Front, flight.Top) && along <= Mathf.Max(flight.Front, flight.Top) && Mathf.Abs(across) <= flight.Width * 0.5f;
        }

        /// <summary>
        /// Which region of the area's ground a grid cell belongs to, from its
        /// corners and its middle: none (-1) where it touches nothing of the
        /// area or lies wholly under a flight's steps; a slope's own region
        /// (1 + its index) where the segment nearest its middle - the one
        /// its heights come from - is that slope's; else a level (0), which
        /// GroundMesh joins into one region per connected level, never
        /// across a flight (CutsLevel). A lot's ring is a level.
        /// </summary>
        private static int RegionOf(Vector2[] corners, Vector2 centre, List<StreetSpec> area, List<GroundRect> rects, List<GroundDisc> discs,
            List<GroundLot> lots, StreetHeights heights, List<StreetSteps.Flight> flights, List<StreetSlopes.Slope> slopes, GeneratorSettings settings,
            float margin)
        {
            bool touches = Inside(centre, rects, discs, lots, margin);

            foreach (Vector2 corner in corners) {
                touches |= Inside(corner, rects, discs, lots, margin);
            }

            if (!touches) {
                return -1;
            }

            foreach (StreetSteps.Flight flight in flights) {
                if (!UnderSteps(flight, centre)) {
                    continue;
                }

                bool whole = true;

                foreach (Vector2 corner in corners) {
                    whole &= UnderSteps(flight, corner);
                }

                if (whole) {
                    return -1;
                }
            }

            foreach (GroundLot lot in lots) {
                if (lot.Outside(centre) <= lot.Margin) {
                    return 0;
                }
            }

            if (NearestSegment(area, centre, settings, out StreetSpec street, out int segment, out _)) {
                StreetSlopes.Slope slope = heights.SlopeOf(street, segment);

                if (slope != null) {
                    return 1 + slopes.IndexOf(slope);
                }
            }

            return 0;
        }

        /// <summary>
        /// Whether two neighbouring level cells (by their middles) lie on
        /// opposite sides of a flight of steps, so they belong to different
        /// levels: both nearest the flight's segment, one before the
        /// flight's middle and one past it.
        /// </summary>
        private static bool CutsLevel(Vector2 a, Vector2 b, List<StreetSpec> area, StreetHeights heights, GeneratorSettings settings)
        {
            if (!NearestSegment(area, a, settings, out StreetSpec streetA, out int segmentA, out float alongA)
                || !NearestSegment(area, b, settings, out StreetSpec streetB, out int segmentB, out float alongB)
                || streetA != streetB || segmentA != segmentB) {
                return false;
            }

            StreetSteps.Flight flight = heights.FlightOf(streetA, segmentA);
            return flight != null && (alongA - flight.Middle) * (alongB - flight.Middle) < 0f;
        }

        /// <summary>
        /// Where a ground vertex stands: a vertex under a flight's steps is
        /// moved along the segment to GroundStepsOverlap inside the flight's
        /// front (on the low side of its middle) or its top (on the high
        /// side), so the ground's edge runs on under the lowest step and
        /// under the top step's tread (where HeightAt sinks it), and no slit
        /// shows under the steps' rounded edges. Any other vertex stays put.
        /// </summary>
        private static Vector2 PlaceVertex(Vector2 point, List<StreetSteps.Flight> flights, GeneratorSettings settings)
        {
            foreach (StreetSteps.Flight flight in flights) {
                if (!UnderSteps(flight, point)) {
                    continue;
                }

                AgainstFlight(flight, point, out float along, out float across);
                float overlap = settings.GroundStepsOverlap * flight.Up;
                float edge = (along - flight.Middle) * flight.Up < 0f ? flight.Front + overlap : flight.Top - overlap;
                return flight.Start + flight.Axis * edge + new Vector2(-flight.Axis.y, flight.Axis.x) * across;
            }

            return point;
        }

        /// <summary>
        /// A region's name for its object: a slope's segment, or a level's
        /// height at a point in it.
        /// </summary>
        private static string RegionName(int region, Vector2 point, List<StreetSpec> area, List<GroundLot> lots, StreetHeights heights,
            List<StreetSlopes.Slope> slopes, GeneratorSettings settings)
        {
            if (region > 0 && region - 1 < slopes.Count) {
                StreetSlopes.Slope slope = slopes[region - 1];
                return $"slope {slope.Street.id} {slope.Segment}-{slope.Segment + 1}";
            }

            return $"level {HeightAt(area, lots, heights, point, settings):0.0}m";
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
