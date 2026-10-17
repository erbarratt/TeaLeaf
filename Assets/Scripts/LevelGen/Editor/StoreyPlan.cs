using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// How many storeys each building in a spec gets, decided before anything
    /// is built.
    ///
    /// A facade's count is rolled between GeneratorSettings.MinStoreys and
    /// MaxStoreys, leaning towards one end by StoreyBias. A lot's whole
    /// building matches its neighbours: the average height in metres of the
    /// facades within LotNeighbourDistance of its footprint, turned into
    /// storeys of its own styles, plus LotStoreyBias, kept between min and
    /// max (rolled like a facade if no facade is that near, or a height isn't
    /// known yet).
    ///
    /// A building's styles are listed from the ground up and the last one
    /// repeats for every storey above the list; storeys past the count are
    /// dropped (StyleOf).
    /// </summary>
    internal class StoreyPlan
    {
        // How far apart the points along a facade's frontage are taken when
        // measuring its distance from a lot.
        private const float SampleSpacing = 1f;

        private readonly Dictionary<BuildingSpec, int> _facades = new();
        private readonly Dictionary<LotSpec, int> _lots = new();

        /// <summary>
        /// A facade closing a dead end: a street end that meets no other
        /// street. Its building is made here, not in the spec.
        /// </summary>
        internal class DeadEnd
        {
            internal StreetSpec Street;
            // The end point's index (0 or the last).
            internal int Point;
            internal BuildingSpec Building;
        }

        // Every dead end's facade, in the spec's order (each street's first
        // point, then its last).
        internal List<DeadEnd> DeadEnds { get; } = new();

        /// <summary>
        /// A frontage building's storey count.
        /// </summary>
        internal int Storeys(BuildingSpec building)
        {
            return _facades.TryGetValue(building, out int count) ? count : 0;
        }

        /// <summary>
        /// A lot's storey count.
        /// </summary>
        internal int Storeys(LotSpec lot)
        {
            return _lots.TryGetValue(lot, out int count) ? count : 0;
        }

        /// <summary>
        /// The style of one storey (from 0 at the ground): the listed one, or
        /// the last listed for any storey above the list.
        /// </summary>
        internal static string StyleOf(List<string> storeys, int storey)
        {
            return storeys[Mathf.Min(storey, storeys.Count - 1)];
        }

        /// <summary>
        /// Rolls every facade's count, in the spec's order, then matches every
        /// lot to its neighbours. Adds a line per frontage and lot to log.
        /// </summary>
        internal static StoreyPlan Make(LayoutSpec spec, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings,
            System.Random random, List<string> log)
        {
            StoreyPlan plan = new();
            // Each facade's height and the points along the street edge it stands on.
            List<(float Height, List<Vector2> Points)> facades = new();

            foreach (StreetSpec street in spec.streets) {
                foreach (FrontageSpec frontage in street.frontages) {
                    float sideSign = frontage.side == "left" ? 1f : frontage.side == "right" ? -1f : 0f;

                    if (sideSign == 0f || frontage.from < 0 || frontage.to >= street.points.Count || frontage.from >= frontage.to) {
                        continue;
                    }

                    List<string> counts = new();
                    float along = 0f;

                    foreach (BuildingSpec building in frontage.buildings) {
                        if (building.storeys.Count == 0) {
                            continue;
                        }

                        int count = Roll(settings, random);
                        plan._facades[building] = count;
                        counts.Add(count.ToString());

                        // Its stretch of the street's edge, for the lots'
                        // neighbour test; length from its ground storey's modules.
                        float length = building.widthModules * Spacing(styles, building.storeys[0]);
                        float height = Height(building.storeys, count, styles, settings);

                        if (length > 0f && height > 0f) {
                            facades.Add((height, EdgePoints(street, frontage.from, frontage.to, sideSign, along, along + length)));
                        }

                        along += length;
                    }

                    log.Add($"Street '{street.id}', {frontage.side} frontage {frontage.from}-{frontage.to}: storeys {string.Join(", ", counts)}.");
                }
            }

            foreach (StreetSpec street in spec.streets) {
                int last = street.points.Count - 1;

                if (last < 1) {
                    continue;
                }

                for (int end = 0; end <= last; end += last) {
                    plan.AddDeadEnd(street, end, spec.streets, styles, settings, random, log);
                }
            }

            foreach (LotSpec lot in spec.lots) {
                if (lot.storeys.Count == 0) {
                    continue;
                }

                int count = MatchNeighbours(lot, facades, styles, settings, out int neighbours);

                if (count > 0) {
                    log.Add($"Lot '{lot.id}': {count} storey(s), from {neighbours} facade(s) nearby.");
                } else {
                    count = Roll(settings, random);
                    log.Add($"Lot '{lot.id}': {count} storey(s), rolled (no facade of known height within {settings.LotNeighbourDistance:0.#}m).");
                }

                plan._lots[lot] = count;
            }

            return plan;
        }

        /// <summary>
        /// Makes the facade across a street's end if no other street meets
        /// it there: the styles of the building beside it at that end (the
        /// left side's, else the right's), no door, enough modules of its
        /// ground style to span the end segment's width and reach behind the
        /// side facades' back lines on both sides, and a rolled storey
        /// count. Logged; skipped (logged) if no building stands at that end
        /// to take styles from, or its ground style has no module yet.
        /// </summary>
        private void AddDeadEnd(StreetSpec street, int end, List<StreetSpec> streets, Dictionary<string, BuildingStyle> styles,
            GeneratorSettings settings, System.Random random, List<string> log)
        {
            Vector2 point = new(street.points[end].x, street.points[end].z);

            foreach (StreetSpec other in streets) {
                if (other == street) {
                    continue;
                }

                foreach (Vector3 otherPoint in other.points) {
                    if ((new Vector2(otherPoint.x, otherPoint.z) - point).magnitude <= settings.JunctionDistance) {
                        return;
                    }
                }
            }

            string label = $"Street '{street.id}', dead end at point {end}";
            List<string> storeys = NeighbourStoreys(street, end);

            if (storeys == null) {
                log.Add($"{label} not closed: no frontage building stands at that end to take its styles from.");
                return;
            }

            BuildingStyle ground = Find(styles, storeys[0]);

            if (ground == null || ground.ModuleSpacing <= 0f) {
                log.Add($"{label} not closed: style '{storeys[0]}' has no module yet.");
                return;
            }

            // Wide enough that both its ends stand behind the side facades'
            // back lines (the deepest wall of its styles, and a column
            // stepped forward by the most it may), so the side facades hide
            // its ends and their overrun modules end behind it.
            float width = street.SegmentWidth(end == 0 ? 0 : end - 1);
            float wallDepth = 0f;

            foreach (string name in storeys) {
                BuildingStyle style = Find(styles, name);

                if (style == null) {
                    continue;
                }

                foreach (string category in BuildingStyle.WallCategories) {
                    foreach (PieceAnnotation piece in style.Modules(category)) {
                        wallDepth = Mathf.Max(wallDepth, piece.depth);
                    }
                }
            }

            float span = width + 2f * (settings.ColumnDepthMax + wallDepth);
            BuildingSpec building = new() {
                widthModules = Mathf.Max(1, Mathf.CeilToInt(span / ground.ModuleSpacing)),
                storeys = new List<string>(storeys),
                doorModule = -1
            };
            int count = Roll(settings, random);
            _facades[building] = count;
            DeadEnds.Add(new DeadEnd { Street = street, Point = end, Building = building });
            log.Add($"{label}: closed by a facade of {building.widthModules} module(s), {count} storey(s).");
        }

        /// <summary>
        /// The styles of the frontage building at a street's end point: the
        /// left frontage's first building there (or last, at a frontage's
        /// end), else the right's; null if neither side has one.
        /// </summary>
        private static List<string> NeighbourStoreys(StreetSpec street, int point)
        {
            foreach (string side in new[] { "left", "right" }) {
                foreach (FrontageSpec frontage in street.frontages) {
                    if (frontage.side != side || frontage.buildings.Count == 0) {
                        continue;
                    }

                    BuildingSpec building = frontage.from == point ? frontage.buildings[0]
                        : frontage.to == point ? frontage.buildings[frontage.buildings.Count - 1] : null;

                    if (building != null && building.storeys.Count > 0) {
                        return building.storeys;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// A storey count between min and max: a random share of the range,
        /// raised to a power so the bias leans it towards one end (bias +1
        /// takes the fourth root, leaning high; -1 the fourth power, leaning
        /// low; 0 leaves every count equally likely).
        /// </summary>
        private static int Roll(GeneratorSettings settings, System.Random random)
        {
            float share = Mathf.Pow((float)random.NextDouble(), Mathf.Pow(4f, -settings.StoreyBias));
            int range = settings.MaxStoreys - settings.MinStoreys + 1;
            return Mathf.Min(settings.MinStoreys + Mathf.FloorToInt(share * range), settings.MaxStoreys);
        }

        /// <summary>
        /// A lot's storeys from the facades near it: their average height
        /// turned into storeys of the lot's own styles (fractional, so the
        /// bias can tip it), plus the bias, rounded and kept between min and
        /// max. 0 if no facade of known height is near enough, or the lot's
        /// own storey heights aren't known.
        /// </summary>
        private static int MatchNeighbours(LotSpec lot, List<(float Height, List<Vector2> Points)> facades,
            Dictionary<string, BuildingStyle> styles, GeneratorSettings settings, out int neighbours)
        {
            // The lot's footprint, as its bounding rectangle.
            float spacing = Spacing(styles, lot.storeys[0]);
            lot.Bounds(spacing, settings.WallOverlap, out Vector2 origin, out Vector2 axisX, out Vector2 axisZ, out float halfWidth, out float halfDepth);

            float total = 0f;
            neighbours = 0;

            foreach ((float height, List<Vector2> points) in facades) {
                foreach (Vector2 point in points) {
                    Vector2 offset = point - origin;
                    float outX = Mathf.Max(Mathf.Abs(Vector2.Dot(offset, axisX)) - halfWidth, 0f);
                    float outZ = Mathf.Max(Mathf.Abs(Vector2.Dot(offset, axisZ)) - halfDepth, 0f);

                    if (outX * outX + outZ * outZ <= settings.LotNeighbourDistance * settings.LotNeighbourDistance) {
                        total += height;
                        neighbours++;
                        break;
                    }
                }
            }

            if (neighbours == 0) {
                return 0;
            }

            // Climb the lot's storeys until they reach the average height;
            // the storey it reaches part-way up counts as a share.
            float target = total / neighbours - settings.WallOverlap;
            float climbed = 0f;
            float storeys = -1f;

            for (int storey = 0; storey < settings.MaxStoreys + 3; storey++) {
                BuildingStyle style = Find(styles, StyleOf(lot.storeys, storey));

                if (style == null || style.StoreySpacing <= 0f) {
                    return 0;
                }

                if (climbed + style.StoreySpacing >= target) {
                    storeys = storey + (target - climbed) / style.StoreySpacing;
                    break;
                }

                climbed += style.StoreySpacing;
            }

            if (storeys < 0f) {
                storeys = settings.MaxStoreys;
            }

            return Mathf.Clamp(Mathf.RoundToInt(storeys + settings.LotStoreyBias), settings.MinStoreys, settings.MaxStoreys);
        }

        /// <summary>
        /// A building's height in metres: its storeys at their styles' storey
        /// spacing, plus the top wall's overlap. 0 if a storey's style has no
        /// known height.
        /// </summary>
        private static float Height(List<string> storeys, int count, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings)
        {
            float height = settings.WallOverlap;

            for (int storey = 0; storey < count; storey++) {
                BuildingStyle style = Find(styles, StyleOf(storeys, storey));

                if (style == null || style.StoreySpacing <= 0f) {
                    return 0f;
                }

                height += style.StoreySpacing;
            }

            return height;
        }

        /// <summary>
        /// Points every SampleSpacing along a frontage's street edge, between
        /// two distances along the street's middle line from the frontage's
        /// first point (each pushed out to the edge on the frontage's side).
        /// </summary>
        private static List<Vector2> EdgePoints(StreetSpec street, int from, int to, float sideSign, float start, float end)
        {
            List<Vector2> points = new();
            float walked = 0f;

            for (int i = from + 1; i <= to; i++) {
                Vector2 a = new(street.points[i - 1].x, street.points[i - 1].z);
                Vector2 b = new(street.points[i].x, street.points[i].z);
                float length = (b - a).magnitude;

                if (length <= 0f) {
                    continue;
                }

                Vector2 along = (b - a) / length;
                Vector2 side = new Vector2(-along.y, along.x) * (sideSign * street.SegmentWidth(i - 1) * 0.5f);

                for (float d = Mathf.Max(start - walked, 0f); d <= Mathf.Min(end - walked, length); d += SampleSpacing) {
                    points.Add(a + along * d + side);
                }

                walked += length;
            }

            return points;
        }

        /// <summary>
        /// The module spacing of a style (0 if unknown).
        /// </summary>
        private static float Spacing(Dictionary<string, BuildingStyle> styles, string name)
        {
            BuildingStyle style = Find(styles, name);
            return style != null ? style.ModuleSpacing : 0f;
        }

        /// <summary>
        /// A style by name, or null.
        /// </summary>
        private static BuildingStyle Find(Dictionary<string, BuildingStyle> styles, string name)
        {
            return name != null && styles.TryGetValue(name, out BuildingStyle style) ? style : null;
        }
    }
}
