using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Stage 3: checks a layout spec against the catalogue and the
    /// annotations before anything is built (menu TeaLeaf > Level Gen >
    /// Validate Layout Spec, on the spec(s) selected in the Project window).
    ///
    /// An error means the spec can't be built as it stands: a malformed
    /// street, frontage or lot, a style without the pieces it needs, or a
    /// frontage longer than its stretch of street. A warning means it can,
    /// but something should be looked at: pieces tagged with a style the
    /// spec uses that aren't verified yet, walls that don't fit their
    /// style's module, more than a module left at a frontage's end, lots
    /// overlapping each other or a street. The results go to the Console as
    /// one entry per spec.
    /// </summary>
    internal static class LayoutSpecValidator
    {
        // The tolerances and overlaps the checks use, set for the length of
        // one Validate call (editor code, one call at a time).
        private static GeneratorSettings _settings;

        /// <summary>
        /// What a check found.
        /// </summary>
        internal class Report
        {
            internal readonly List<string> Errors = new();
            internal readonly List<string> Warnings = new();
        }

        /// <summary>
        /// An oriented rectangle on the ground (x, z), for the overlap tests.
        /// </summary>
        private struct GroundRect
        {
            public string Label;
            public Vector2 Centre;
            public Vector2 AxisX;
            public Vector2 AxisZ;
            public float HalfX;
            public float HalfZ;
        }

        [MenuItem("TeaLeaf/Level Gen/Validate Layout Spec")]
        private static void ValidateSelected()
        {
            List<string> paths = new();

            foreach (UnityEngine.Object selected in Selection.objects) {
                string path = AssetDatabase.GetAssetPath(selected);

                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && path.StartsWith(LayoutSpec.LayoutsFolder + "/", StringComparison.Ordinal)) {
                    paths.Add(path);
                }
            }

            if (paths.Count == 0) {
                Debug.LogWarning($"LayoutSpecValidator: select one or more layout specs (.json in {LayoutSpec.LayoutsFolder}) in the Project window first.");
                return;
            }

            if (!LoadInputs(out PrefabCatalogue catalogue, out PieceAnnotations annotations)) {
                return;
            }

            GeneratorSettings settings = GeneratorSettings.Load();

            foreach (string path in paths) {
                Log(path, ValidateFile(path, catalogue, annotations, settings, out _, out _));
            }
        }

        /// <summary>
        /// Reads the catalogue and the annotations the checks need, logging
        /// why if either can't be read.
        /// </summary>
        internal static bool LoadInputs(out PrefabCatalogue catalogue, out PieceAnnotations annotations)
        {
            catalogue = null;
            annotations = null;

            if (!File.Exists(PrefabCatalogueScanner.CataloguePath)) {
                Debug.LogError("LayoutSpecValidator: there is no catalogue yet: run TeaLeaf > Level Gen > Scan Prefab Catalogue.");
                return false;
            }

            catalogue = JsonUtility.FromJson<PrefabCatalogue>(File.ReadAllText(PrefabCatalogueScanner.CataloguePath));

            if (catalogue == null || catalogue.format != PrefabCatalogue.CurrentFormat) {
                Debug.LogError("LayoutSpecValidator: the catalogue is in an old format: scan it again.");
                return false;
            }

            annotations = PieceAnnotations.Load();
            return annotations != null;
        }

        /// <summary>
        /// Reads a spec file and checks it. A file that isn't readable JSON
        /// in the current format gives one error and no further checks, and
        /// a null spec; otherwise the spec read is handed back with its
        /// street widths resolved, and the random sequence made from its seed,
        /// at the point after the widths, for the generator to go on with.
        /// </summary>
        internal static Report ValidateFile(string path, PrefabCatalogue catalogue, PieceAnnotations annotations,
            GeneratorSettings settings, out LayoutSpec spec, out System.Random random)
        {
            Report report = new();
            random = null;

            try {
                spec = JsonUtility.FromJson<LayoutSpec>(File.ReadAllText(path));
            } catch (Exception exception) {
                report.Errors.Add($"Not readable as a layout spec: {exception.Message}");
                spec = null;
                return report;
            }

            if (spec == null || spec.format != LayoutSpec.CurrentFormat) {
                report.Errors.Add($"Not a format {LayoutSpec.CurrentFormat} layout spec.");
                spec = null;
                return report;
            }

            random = new System.Random(spec.seed);
            spec.ResolveWidths(settings, random);
            spec.ResolveLots(settings);
            Validate(spec, BuildingStyle.Gather(annotations, catalogue, settings), settings, report);
            return report;
        }

        /// <summary>
        /// Every check on a spec, given the styles gathered from the annotations.
        /// </summary>
        internal static void Validate(LayoutSpec spec, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings, Report report)
        {
            _settings = settings;

            if (string.IsNullOrWhiteSpace(spec.sector)) {
                report.Errors.Add("The spec names no sector to build in.");
            }

            HashSet<string> ids = new(StringComparer.Ordinal);
            HashSet<string> usedStyles = new(StringComparer.Ordinal);
            List<GroundRect> streetRects = new();
            List<GroundRect> lotRects = new();

            foreach (StreetSpec street in spec.streets) {
                CheckStreet(street, styles, ids, usedStyles, streetRects, report);
            }

            foreach (StreetSpec street in spec.streets) {
                CheckFrontages(street, spec.streets, styles, usedStyles, report);
            }

            CheckSharpCorners(spec.streets, report);
            CheckBranchAngles(spec.streets, report);

            foreach (LotSpec lot in spec.lots) {
                CheckLot(lot, styles, ids, usedStyles, lotRects, report);
            }

            CheckLotsBesideSlopes(spec, styles, report);

            for (int i = 0; i < lotRects.Count; i++) {
                for (int j = i + 1; j < lotRects.Count; j++) {
                    if (Overlap(lotRects[i], lotRects[j])) {
                        report.Warnings.Add($"{lotRects[i].Label} overlaps {lotRects[j].Label}.");
                    }
                }

                foreach (GroundRect street in streetRects) {
                    if (Overlap(lotRects[i], street)) {
                        report.Warnings.Add($"{lotRects[i].Label} overlaps {street.Label}.");
                    }
                }
            }

            foreach (string name in usedStyles) {
                if (styles.TryGetValue(name, out BuildingStyle style)) {
                    CheckStyle(style, report);
                }
            }
        }

        /// <summary>
        /// A street's id, width, points and style; adds one ground rectangle
        /// per segment for the overlap tests.
        /// </summary>
        private static void CheckStreet(StreetSpec street, Dictionary<string, BuildingStyle> styles, HashSet<string> ids,
            HashSet<string> usedStyles, List<GroundRect> rects, Report report)
        {
            string label = CheckId("Street", street.id, ids, report);

            if (street.width < 0f) {
                report.Errors.Add($"{label} has a negative width (0 uses the settings' street width).");
            }

            if (street.points.Count < 2) {
                report.Errors.Add($"{label} needs at least two points.");
            }

            for (int i = 1; i < street.points.Count; i++) {
                Vector2 a = Ground(street.points[i - 1]);
                Vector2 b = Ground(street.points[i]);
                float length = (b - a).magnitude;

                if (length < _settings.MinSegmentLength) {
                    report.Errors.Add($"{label}: points {i - 1} and {i} are at the same place.");
                    continue;
                }

                Vector2 axis = (b - a) / length;
                rects.Add(new GroundRect {
                    Label = $"{label} (segment {i - 1}-{i})",
                    Centre = (a + b) * 0.5f,
                    AxisX = axis,
                    AxisZ = new Vector2(-axis.y, axis.x),
                    HalfX = length * 0.5f,
                    HalfZ = street.SegmentWidth(i - 1) * 0.5f
                });
            }

            BuildingStyle style = FindStyle(label, street.style, styles, usedStyles, report);

            // A street is laid with ground pieces (cobbles) or floor pieces
            // (plank decking, for a wooden street, walkway or bridge).
            if (style != null && style.Pieces("ground").Count == 0 && style.Pieces("floor").Count == 0) {
                report.Errors.Add($"{label}: style '{style.Name}' has no usable ground or floor piece.");
            }

            if (style != null) {
                CheckRises(label, street, style, report);
            }
        }

        /// <summary>
        /// Each segment that rises or falls: only a street of ground tiles
        /// can, by a slope where its grade over the segment less the landing
        /// is no steeper than StreetSlopeMaxGrade (StreetSlopes), else by a
        /// whole number of flights of one of its style's steps pieces
        /// (StreetSteps), on a segment long enough for those flights (at the
        /// piece's ramp run) and their landing.
        /// </summary>
        private static void CheckRises(string label, StreetSpec street, BuildingStyle style, Report report)
        {
            for (int i = 1; i < street.points.Count; i++) {
                if (!StreetSteps.Rises(street, i - 1, _settings)) {
                    continue;
                }

                float rise = Mathf.Abs(street.points[i].y - street.points[i - 1].y);
                string segment = $"{label}, segment {i - 1}-{i}";

                if (style.TileCategory != "ground") {
                    report.Errors.Add($"{segment} rises {rise:0.##}m, but only a street of ground tiles (cobbles) can change height: "
                        + $"style '{style.Name}' is decked.");
                    continue;
                }

                if (StreetSlopes.IsSlope(street, i - 1, style, _settings)) {
                    continue;
                }

                PieceAnnotation piece = StreetSteps.Choose(style, rise, _settings, out int count);

                if (piece == null) {
                    List<string> rises = new();

                    foreach (PieceAnnotation steps in style.Pieces("steps")) {
                        rises.Add($"{steps.height:0.##}m");
                    }

                    float grade = StreetSlopes.Grade(street, i - 1, style, _settings);
                    string slope = float.IsInfinity(grade)
                        ? $"too short to slope (its landing takes {StreetSteps.LandingDepth(style, _settings):0.##}m)"
                        : $"too steep to slope (1 in {1f / grade:0.#} over the segment less its landing; 1 in {1f / _settings.StreetSlopeMaxGrade:0.#} at most)";
                    report.Errors.Add(rises.Count == 0
                        ? $"{segment} rises {rise:0.##}m, {slope}, and style '{style.Name}' has no usable steps piece."
                        : $"{segment} rises {rise:0.##}m, {slope}, and not a whole number of flights of style '{style.Name}''s steps ({string.Join(", ", rises)} each).");
                    continue;
                }

                float length = (Ground(street.points[i]) - Ground(street.points[i - 1])).magnitude;
                float needed = count * piece.depth + StreetSteps.LandingDepth(style, _settings);

                if (needed > length) {
                    report.Errors.Add($"{segment} is {length:0.##}m long, too short for its {count} flight(s) of steps and their landing ({needed:0.##}m).");
                }
            }
        }

        /// <summary>
        /// A street's frontages: each one's side and stretch, that no two on
        /// a side overlap, that no street branches off part-way along one
        /// (the street has a gap there, so the frontage must end), its
        /// buildings' storeys, and how its buildings' length compares with
        /// its stretch of street.
        /// </summary>
        private static void CheckFrontages(StreetSpec street, List<StreetSpec> streets, Dictionary<string, BuildingStyle> styles,
            HashSet<string> usedStyles, Report report)
        {
            string streetLabel = string.IsNullOrEmpty(street.id) ? "Street" : $"Street '{street.id}'";
            List<FrontageSpec> checkedFrontages = new();

            foreach (FrontageSpec frontage in street.frontages) {
                string label = $"{streetLabel}, {frontage.side} frontage {frontage.from}-{frontage.to}";
                float sideSign = frontage.side switch { "left" => 1f, "right" => -1f, _ => 0f };

                if (sideSign == 0f) {
                    report.Errors.Add($"{label}: the side must be \"left\" or \"right\".");
                    continue;
                }

                if (frontage.from < 0 || frontage.to >= street.points.Count || frontage.from >= frontage.to) {
                    report.Errors.Add($"{label} isn't a stretch of the street (points 0 to {street.points.Count - 1}, from before to).");
                    continue;
                }

                foreach (FrontageSpec other in checkedFrontages) {
                    if (other.side == frontage.side && frontage.from < other.to && other.from < frontage.to) {
                        report.Errors.Add($"{label} overlaps the {other.side} frontage {other.from}-{other.to}.");
                    }
                }

                checkedFrontages.Add(frontage);

                if (frontage.buildings.Count == 0) {
                    report.Errors.Add($"{label} has no buildings.");
                    continue;
                }

                // The buildings' length along the street: each at its module
                // spacing, the last one's overlap standing out at the end.
                float length = _settings.WallOverlap;
                float smallestSpacing = float.MaxValue;
                bool lengthKnown = true;

                for (int i = 0; i < frontage.buildings.Count; i++) {
                    BuildingSpec building = frontage.buildings[i];
                    string buildingLabel = $"{label}, building {i}";

                    if (building.widthModules < 1) {
                        report.Errors.Add($"{buildingLabel} needs at least one module.");
                    }

                    float moduleWidth = CheckStoreys(buildingLabel, building.storeys, building.doorModule, building.widthModules, styles, usedStyles, report);

                    if (moduleWidth <= 0f) {
                        lengthKnown = false;
                        continue;
                    }

                    float spacing = moduleWidth - _settings.WallOverlap;
                    length += building.widthModules * spacing;
                    smallestSpacing = Mathf.Min(smallestSpacing, spacing);
                }

                float stretch = StretchLength(street, frontage, sideSign, WallDepth(frontage, styles), streets, label, report);

                if (!lengthKnown || stretch <= 0f) {
                    continue;
                }

                if (length > stretch + _settings.OverlapTolerance) {
                    report.Errors.Add($"{label}: its buildings are {length:0.##}m long, {length - stretch:0.##}m more than its {stretch:0.##}m stretch of street.");
                } else if (stretch - length >= smallestSpacing) {
                    report.Warnings.Add($"{label}: {stretch - length:0.##}m is left at its end, more than a module; the generator fills it, but another building may suit.");
                }

                CheckSlopeDrops(label, street, frontage, styles, report);
            }
        }

        /// <summary>
        /// A building beside a slope stands level at the highest the street
        /// comes under it, so the street falls below its level by as much of
        /// its width as lies on the slope times the grade: a warning where
        /// that passes SlopeDropMax (a shorter building suits a slope). Where
        /// each building stands is taken roughly, laid end to end from the
        /// frontage's first point along the street's middle line.
        /// </summary>
        private static void CheckSlopeDrops(string label, StreetSpec street, FrontageSpec frontage, Dictionary<string, BuildingStyle> styles, Report report)
        {
            if (!styles.TryGetValue(street.style ?? "", out BuildingStyle ground)) {
                return;
            }

            // Each sloped segment's stretch along the frontage, and its grade.
            List<(float From, float To, float Grade)> slopes = new();
            float along = 0f;

            for (int i = frontage.from; i < frontage.to && i + 1 < street.points.Count; i++) {
                float length = (Ground(street.points[i + 1]) - Ground(street.points[i])).magnitude;

                if (StreetSlopes.IsSlope(street, i, ground, _settings)) {
                    slopes.Add((along, along + length, StreetSlopes.Grade(street, i, ground, _settings)));
                }

                along += length;
            }

            if (slopes.Count == 0) {
                return;
            }

            float start = 0f;

            for (int i = 0; i < frontage.buildings.Count; i++) {
                BuildingSpec building = frontage.buildings[i];

                if (building.storeys.Count == 0 || !styles.TryGetValue(building.storeys[0], out BuildingStyle style) || style.ModuleSpacing <= 0f) {
                    continue;
                }

                float end = start + building.widthModules * style.ModuleSpacing;
                float drop = 0f;
                float grade = 0f;

                foreach ((float from, float to, float slopeGrade) in slopes) {
                    float onSlope = Mathf.Min(end, to) - Mathf.Max(start, from);

                    if (onSlope > 0f && onSlope * slopeGrade > drop) {
                        drop = onSlope * slopeGrade;
                        grade = slopeGrade;
                    }
                }

                if (drop > _settings.SlopeDropMax) {
                    report.Warnings.Add($"{label}, building {i}: beside a slope of 1 in {1f / grade:0.#} the street falls about {drop:0.##}m below its level "
                        + $"(more than {_settings.SlopeDropMax:0.##}m); fewer modules would suit.");
                }

                start = end;
            }
        }

        /// <summary>
        /// A lot's ground is a ring at its one street level, so no slope may
        /// come near it: an error for a lot whose footprint comes within
        /// LotGroundMargin plus half a tile of a sloping segment's rectangle.
        /// </summary>
        private static void CheckLotsBesideSlopes(LayoutSpec spec, Dictionary<string, BuildingStyle> styles, Report report)
        {
            foreach (LotSpec lot in spec.lots) {
                if (lot.storeys.Count == 0 || !styles.TryGetValue(lot.storeys[0], out BuildingStyle style) || style.ModuleSpacing <= 0f
                    || (!lot.HasOutline && (lot.widthModules < 1 || lot.depthModules < 1))) {
                    continue;
                }

                // The footprint itself (a block's bounding rectangle would
                // cross the streets at its corners).
                List<Vector2> footprint = lot.Corners(style.ModuleSpacing, _settings.WallOverlap);
                string label = string.IsNullOrEmpty(lot.id) ? "A lot" : $"Lot '{lot.id}'";

                foreach (StreetSpec street in spec.streets) {
                    if (!styles.TryGetValue(street.style ?? "", out BuildingStyle ground)) {
                        continue;
                    }

                    float grow = _settings.LotGroundMargin + StreetSteps.TileDiagonal(ground) * 0.5f + _settings.OverlapTolerance;

                    for (int i = 0; i + 1 < street.points.Count; i++) {
                        if (!StreetSlopes.IsSlope(street, i, ground, _settings)) {
                            continue;
                        }

                        // The sloped stretch only (the landing is level with
                        // the high point), and not its first metres from the
                        // foot, where the surface is still within a lip of
                        // the low point's level: a lane sloping away from a
                        // lot's ring starts at the ring's middle line, and
                        // its foot would otherwise reach the lot.
                        Vector2 a = Ground(street.points[i]);
                        Vector2 b = Ground(street.points[i + 1]);
                        float length = (b - a).magnitude;
                        Vector2 axis = (b - a) / length;
                        float landing = StreetSteps.LandingDepth(ground, _settings);
                        bool climbs = street.points[i + 1].y > street.points[i].y;
                        float from = climbs ? grow : landing;
                        float to = climbs ? length - landing : length - grow;

                        if (to <= from) {
                            continue;
                        }

                        GroundRect segment = new() {
                            Centre = a + axis * ((from + to) * 0.5f), AxisX = axis, AxisZ = new Vector2(-axis.y, axis.x),
                            HalfX = (to - from) * 0.5f + grow, HalfZ = street.SegmentWidth(i) * 0.5f + grow
                        };

                        if (OverlapConvex(footprint, segment)) {
                            report.Errors.Add($"{label} stands beside street '{street.id}' segment {i}-{i + 1}, which slopes: a lot's ground is laid at one level, "
                                + "so its streets must stay level (steps are fine).");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// About how much of the street's edge a frontage can fill: the
        /// length of the street's middle line between its two points, less
        /// how far short its edge stops of any street branching off on its
        /// side at either end (BranchCut, its walls reaching depth back from
        /// the edge). Reports a street branching off on its side part-way
        /// along it (an error: the frontage must end there).
        /// </summary>
        private static float StretchLength(StreetSpec street, FrontageSpec frontage, float sideSign, float depth, List<StreetSpec> streets,
            string label, Report report)
        {
            float length = 0f;

            for (int i = frontage.from + 1; i <= frontage.to; i++) {
                length += (Ground(street.points[i]) - Ground(street.points[i - 1])).magnitude;
            }

            for (int k = frontage.from; k <= frontage.to; k++) {
                // The side's direction at the point, from the segment of the
                // frontage that reaches it (the first one at its start).
                int segmentEnd = Mathf.Max(k, frontage.from + 1);
                Vector2 along = Ground(street.points[segmentEnd]) - Ground(street.points[segmentEnd - 1]);

                if (along.magnitude < _settings.MinSegmentLength) {
                    continue;
                }

                along.Normalize();
                Vector2 sideNormal = new Vector2(-along.y, along.x) * sideSign;

                if (k == frontage.from || k == frontage.to) {
                    length -= BranchCut(street, k, segmentEnd - 1, k == frontage.from ? along : -along, sideNormal, depth, streets, _settings);
                } else if (BranchWidth(street, k, sideNormal, streets, _settings) > 0f) {
                    report.Errors.Add($"{label}: a street branches off on its side at point {k}, so the frontage must end there.");
                }
            }

            return length;
        }

        /// <summary>
        /// Branches meet square: every street ending at a junction where at
        /// least two other ways leave the point (another street running on
        /// through it, or more streets meeting) must meet the street it
        /// branches off - the line through the two other ways most nearly
        /// opposite - within BranchSquareTolerance of a right angle (an error
        /// otherwise). Two streets meeting end to end are a bend, not a branch.
        /// </summary>
        private static void CheckBranchAngles(List<StreetSpec> streets, Report report)
        {
            foreach (StreetSpec street in streets) {
                for (int end = 0; end < 2; end++) {
                    int pointIndex = end == 0 ? 0 : street.points.Count - 1;
                    int nextIndex = end == 0 ? 1 : street.points.Count - 2;

                    if (street.points.Count < 2) {
                        continue;
                    }

                    Vector2 point = Ground(street.points[pointIndex]);
                    Vector2 branch = Ground(street.points[nextIndex]) - point;

                    if (branch.magnitude < _settings.MinSegmentLength) {
                        continue;
                    }

                    branch.Normalize();
                    // Every other way leaving the point, from the other streets.
                    List<Vector2> others = new();

                    foreach (StreetSpec other in streets) {
                        if (other == street) {
                            continue;
                        }

                        for (int j = 0; j < other.points.Count; j++) {
                            if ((Ground(other.points[j]) - point).magnitude > _settings.JunctionDistance) {
                                continue;
                            }

                            for (int n = j - 1; n <= j + 1; n += 2) {
                                if (n >= 0 && n < other.points.Count && (Ground(other.points[n]) - point).magnitude >= _settings.MinSegmentLength) {
                                    others.Add((Ground(other.points[n]) - point).normalized);
                                }
                            }
                        }
                    }

                    // One other way only: two streets meeting end to end (a
                    // bend), not a branch.
                    if (others.Count < 2) {
                        continue;
                    }

                    // The street it branches off: the two other ways most
                    // nearly opposite, as one line through the point.
                    Vector2 line = Vector2.zero;
                    float straightest = float.MaxValue;

                    for (int a = 0; a < others.Count; a++) {
                        for (int b = a + 1; b < others.Count; b++) {
                            float dot = Vector2.Dot(others[a], others[b]);

                            if (dot < straightest) {
                                straightest = dot;
                                line = (others[b] - others[a]).normalized;
                            }
                        }
                    }

                    float angle = Vector2.Angle(branch, line);
                    float offSquare = Mathf.Abs(Mathf.Min(angle, 180f - angle) - 90f);

                    if (offSquare > _settings.BranchSquareTolerance + 0.01f) {
                        report.Errors.Add($"Street '{street.id}' meets the street it branches off at point {pointIndex} {90f - offSquare:0.#} degrees from it; "
                            + $"a branch must meet within {_settings.BranchSquareTolerance:0.#} degrees of square.");
                    }
                }
            }
        }

        /// <summary>
        /// The share of the layout's street corners that are sharp, against
        /// SharpCornerShare: at every point (a bend, or streets meeting), the
        /// ways the streets leave it are sorted round it, and each wedge
        /// between two neighbouring ways is a corner (unless within the branch
        /// angle of straight on, or wider), sharp below SharpCornerAngle. A warning when the spec is more than one
        /// corner off the share asked for.
        /// </summary>
        private static void CheckSharpCorners(List<StreetSpec> streets, Report report)
        {
            // Every point, with the streets' ways out of it, merged where
            // points of several streets are one junction.
            List<(Vector2 Point, List<float> Angles)> points = new();

            foreach (StreetSpec street in streets) {
                for (int k = 0; k < street.points.Count; k++) {
                    Vector2 point = Ground(street.points[k]);
                    int index = points.FindIndex(entry => (entry.Point - point).magnitude <= _settings.JunctionDistance);

                    if (index < 0) {
                        points.Add((point, new List<float>()));
                        index = points.Count - 1;
                    }

                    for (int n = k - 1; n <= k + 1; n += 2) {
                        if (n < 0 || n >= street.points.Count) {
                            continue;
                        }

                        Vector2 leaving = Ground(street.points[n]) - point;

                        if (leaving.magnitude >= _settings.MinSegmentLength) {
                            points[index].Angles.Add(Mathf.Atan2(leaving.y, leaving.x) * Mathf.Rad2Deg);
                        }
                    }
                }
            }

            int corners = 0;
            int sharp = 0;

            foreach ((Vector2 _, List<float> angles) in points) {
                if (angles.Count < 2) {
                    continue;
                }

                angles.Sort();

                for (int i = 0; i < angles.Count; i++) {
                    float wedge = i + 1 < angles.Count ? angles[i + 1] - angles[i] : angles[0] + 360f - angles[i];

                    // Nearly straight on (within the branch angle of a half
                    // turn) is no corner.
                    if (wedge >= 180f - Mathf.Asin(_settings.BranchDot) * Mathf.Rad2Deg) {
                        continue;
                    }

                    corners++;

                    if (wedge < _settings.SharpCornerAngle) {
                        sharp++;
                    }
                }
            }

            if (corners == 0) {
                return;
            }

            float wanted = _settings.SharpCornerShare * corners;

            if (Mathf.Abs(sharp - wanted) > 1f) {
                report.Warnings.Add($"{sharp} of the layout's {corners} street corners are sharp (under {_settings.SharpCornerAngle:0.#} degrees); "
                    + $"the settings ask for about {Mathf.RoundToInt(wanted)} ({_settings.SharpCornerShare:0.##} of them).");
            }
        }

        /// <summary>
        /// About how far a frontage's walls reach back from the street edge:
        /// the deepest usable wall of any of its buildings' storey styles (the
        /// generator measures it exactly, from the walls' connection points).
        /// </summary>
        private static float WallDepth(FrontageSpec frontage, Dictionary<string, BuildingStyle> styles)
        {
            float deepest = 0f;

            foreach (BuildingSpec building in frontage.buildings) {
                foreach (string name in building.storeys) {
                    if (!styles.TryGetValue(name ?? "", out BuildingStyle style)) {
                        continue;
                    }

                    foreach (string category in BuildingStyle.WallCategories) {
                        foreach (PieceAnnotation piece in style.Modules(category)) {
                            deepest = Mathf.Max(deepest, piece.depth);
                        }
                    }
                }
            }

            return deepest;
        }

        /// <summary>
        /// The width of the widest other street that shares a street's point
        /// and leaves it towards a side, or 0 if none does.
        /// </summary>
        internal static float BranchWidth(StreetSpec street, int pointIndex, Vector2 sideNormal, List<StreetSpec> streets,
            GeneratorSettings settings)
        {
            Vector2 point = Ground(street.points[pointIndex]);
            float width = 0f;

            foreach (StreetSpec other in streets) {
                if (other == street) {
                    continue;
                }

                for (int j = 0; j < other.points.Count; j++) {
                    if ((Ground(other.points[j]) - point).magnitude > settings.JunctionDistance) {
                        continue;
                    }

                    // The other street leaves the junction towards each of
                    // its neighbouring points.
                    for (int n = j - 1; n <= j + 1; n += 2) {
                        if (n < 0 || n >= other.points.Count) {
                            continue;
                        }

                        Vector2 leaving = Ground(other.points[n]) - point;

                        if (leaving.magnitude >= settings.MinSegmentLength && Vector2.Dot(leaving.normalized, sideNormal) > settings.BranchDot) {
                            // The width of the other street's segment that leaves here.
                            width = Mathf.Max(width, other.SegmentWidth(Mathf.Min(j, n)));
                        }
                    }
                }
            }

            return width;
        }

        /// <summary>
        /// How far a frontage's street edge must stop short of a point where
        /// streets branch off on its side: measured along the edge from level
        /// with the point, going into the frontage (into), to where the edge
        /// crosses the near edge of each street leaving towards the side (the
        /// edge facing the frontage) - the furthest of them, so at a sharp
        /// angle the frontage stops well short and at a square one half the
        /// branch's width. 0 if none branches off there. The segment is the
        /// frontage's one reaching the point (its width sets the edge). With a
        /// depth (how far the facade's walls reach back from the edge) the line
        /// that far back must clear the branch too, which at a sharp angle
        /// stops the frontage further short still.
        /// </summary>
        internal static float BranchCut(StreetSpec street, int pointIndex, int segment, Vector2 into, Vector2 sideNormal, float depth,
            List<StreetSpec> streets, GeneratorSettings settings)
        {
            Vector2 point = Ground(street.points[pointIndex]);
            Vector2 edge = point + sideNormal * (street.SegmentWidth(segment) * 0.5f);
            float cut = 0f;

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

                        if (leaving.magnitude < settings.MinSegmentLength || Vector2.Dot(leaving.normalized, sideNormal) <= settings.BranchDot) {
                            continue;
                        }

                        Vector2 along = leaving.normalized;
                        float halfWidth = other.SegmentWidth(Mathf.Min(j, n)) * 0.5f;
                        // The branch's edge on the frontage's side of it.
                        Vector2 normal = new(-along.y, along.x);

                        if (Vector2.Dot(normal, into) < 0f) {
                            normal = -normal;
                        }

                        float cross = into.x * along.y - into.y * along.x;

                        // Along the frontage's edge, and the line depth behind
                        // it, to that edge (half the width if the two run side
                        // by side).
                        foreach (float back in new[] { 0f, depth }) {
                            Vector2 offset = point + normal * halfWidth - (edge + sideNormal * back);
                            float distance = Mathf.Abs(cross) < 0.0001f ? halfWidth : (offset.x * along.y - offset.y * along.x) / cross;
                            cut = Mathf.Max(cut, distance);
                        }
                    }
                }
            }

            return cut;
        }

        /// <summary>
        /// A lot's id, footprint and storeys; adds its footprint for the
        /// overlap tests once its module width is known.
        /// </summary>
        private static void CheckLot(LotSpec lot, Dictionary<string, BuildingStyle> styles, HashSet<string> ids,
            HashSet<string> usedStyles, List<GroundRect> rects, Report report)
        {
            string label = CheckId("Lot", lot.id, ids, report);

            if (!lot.HasOutline && lot.widthModules < 1 && lot.depthModules < 1) {
                report.Errors.Add($"{label} has no outline and no modules, and no streets close round its origin to give it a block.");
            } else if (!lot.HasOutline && (lot.widthModules < 1 || lot.depthModules < 1)) {
                report.Errors.Add($"{label} needs at least one module along its front and its depth.");
            }

            if (lot.outline != null && lot.outline.Count > 0 && lot.outline.Count < 3) {
                report.Errors.Add($"{label}: an outline needs at least three corners.");
            }

            // The door is counted along the front: with an outline, the
            // modules that fit its first side are only known with the module.
            float moduleWidth = CheckStoreys(label, lot.storeys, lot.HasOutline ? -1 : lot.doorModule, lot.widthModules, styles, usedStyles, report);

            if (lot.HasOutline && moduleWidth > 0f) {
                CheckOutline(label, lot, moduleWidth - _settings.WallOverlap, report);
            }

            // A block found from the streets fits them by its making, and
            // its bounding rectangle would cross them at its corners.
            if (moduleWidth > 0f && !lot.FromStreets && (lot.HasOutline || (lot.widthModules >= 1 && lot.depthModules >= 1))) {
                // Modules are laid at the module less the overlap; the last
                // one's overlap stands out at the far end. An outline is
                // tested by its bounding rectangle.
                lot.Bounds(moduleWidth - _settings.WallOverlap, _settings.WallOverlap, out Vector2 centre, out Vector2 axisX, out Vector2 axisZ,
                    out float halfX, out float halfZ);
                rects.Add(new GroundRect { Label = label, Centre = centre, AxisX = axisX, AxisZ = axisZ, HalfX = halfX, HalfZ = halfZ });
            }
        }

        /// <summary>
        /// A lot's outline: convex (every corner turning the same way), and
        /// every side long enough for at least one module; the door, if any,
        /// within the modules that fit the front side.
        /// </summary>
        private static void CheckOutline(string label, LotSpec lot, float spacing, Report report)
        {
            List<Vector2> corners = lot.Corners(spacing, _settings.WallOverlap);
            int turning = 0;

            for (int i = 0; i < corners.Count; i++) {
                Vector2 a = corners[i];
                Vector2 b = corners[(i + 1) % corners.Count];
                Vector2 c = corners[(i + 2) % corners.Count];
                float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                int sign = cross > 0.0001f ? 1 : cross < -0.0001f ? -1 : 0;

                if (sign != 0 && turning != 0 && sign != turning) {
                    report.Errors.Add($"{label}: its outline isn't convex (it turns both ways), so its corners can't be closed.");
                    break;
                }

                turning = sign != 0 ? sign : turning;
                float length = (b - a).magnitude;

                if (length < spacing + _settings.WallOverlap - _settings.SizeTolerance) {
                    report.Errors.Add($"{label}: outline side {i} is {length:0.##}m, too short for one {spacing + _settings.WallOverlap:0.##}m module.");
                }
            }

            int frontModules = Mathf.FloorToInt(((corners[1] - corners[0]).magnitude - _settings.WallOverlap) / spacing + 0.001f);

            if (lot.doorModule >= frontModules) {
                report.Errors.Add($"{label}: door module {lot.doorModule} isn't along its front side (0 to {frontModules - 1}, or -1 for none).");
            }
        }

        /// <summary>
        /// A building's storeys (a facade's or a whole one's) and door: that
        /// each storey's style has the pieces that storey needs and that the
        /// storeys' modules match. Returns the module width, or 0 if it isn't
        /// known (no storeys, or a style without usable walls).
        /// </summary>
        private static float CheckStoreys(string label, List<string> storeys, int doorModule, int widthModules,
            Dictionary<string, BuildingStyle> styles, HashSet<string> usedStyles, Report report)
        {
            if (storeys.Count == 0) {
                report.Errors.Add($"{label} has no storeys.");
                return 0f;
            }

            if (doorModule < -1 || doorModule >= widthModules) {
                report.Errors.Add($"{label}: door module {doorModule} isn't along its front (0 to {widthModules - 1}, or -1 for none).");
            }

            float moduleWidth = 0f;
            bool known = true;

            for (int storey = 0; storey < storeys.Count; storey++) {
                string storeyLabel = $"{label}, storey {storey}";
                BuildingStyle style = FindStyle(storeyLabel, storeys[storey], styles, usedStyles, report);

                if (style == null) {
                    known = false;
                    continue;
                }

                bool groundOnly = Array.IndexOf(StyleRules.GroundStoreyOnly, style.Name) >= 0;

                if (storey > 0 && groundOnly) {
                    report.Errors.Add($"{storeyLabel}: style '{style.Name}' can only be a ground storey.");
                }

                // The last listed style repeats for every storey above the
                // list, up to the most storeys a building can be given.
                if (storey == storeys.Count - 1 && groundOnly && _settings.MaxStoreys > storeys.Count) {
                    report.Errors.Add($"{storeyLabel}: style '{style.Name}' can only be a ground storey, but as the last style listed it would repeat for storeys above it (up to {_settings.MaxStoreys}); list a style for the storeys above.");
                }

                // Every storey is walls (its corners are its walls meeting);
                // the ground storey stands on foundations and holds the door.
                RequireModules(storeyLabel, style, "wall", storey, report);

                // The last listed style repeats above it: it needs walls that
                // may stand above the ground too.
                if (storey == 0 && storeys.Count == 1 && _settings.MaxStoreys > 1 && !groundOnly) {
                    RequireModules($"{storeyLabel} (repeated above)", style, "wall", 1, report);
                }

                if (storey == 0) {
                    RequirePieces(storeyLabel, style, "foundation", report);
                    RequirePieces(storeyLabel, style, "foundation-corner", report);

                    if (doorModule >= 0) {
                        RequireModules(storeyLabel, style, "door-wall", 0, report);
                    }
                }

                if (style.ModuleWidth <= 0f) {
                    known = false;
                    continue;
                }

                if (moduleWidth <= 0f) {
                    moduleWidth = style.ModuleWidth;
                } else if (Mathf.Abs(style.ModuleWidth - moduleWidth) > _settings.MaxStoreyWidthMismatch) {
                    // Every storey is laid at the ground storey's spacing, so a
                    // small difference only overlaps or gaps a little.
                    report.Errors.Add($"{storeyLabel}: style '{style.Name}' has a {style.ModuleWidth:0.###}m module, the ground storey a {moduleWidth:0.###}m one: more than {_settings.MaxStoreyWidthMismatch:0.##}m apart, so its walls can't stand on it.");
                }
            }

            return known ? moduleWidth : 0f;
        }

        /// <summary>
        /// What a style used by the spec should be told about: tagged pieces
        /// not usable yet, and walls that don't fit its module.
        /// </summary>
        private static void CheckStyle(BuildingStyle style, Report report)
        {
            if (style.NotUsable.Count > 0) {
                List<string> names = new();

                foreach (PieceAnnotation piece in style.NotUsable) {
                    names.Add(piece.name);
                }

                report.Warnings.Add($"Style '{style.Name}': {names.Count} tagged piece(s) not verified (or excluded) yet, so not placed: {string.Join(", ", names)}.");
            }

            if (style.TileCategory != "") {
                foreach (PieceAnnotation piece in style.Pieces(style.TileCategory)) {
                    if (!style.Tiles.Contains(piece)) {
                        report.Warnings.Add($"Style '{style.Name}': {piece.name} is {piece.width:0.###} x {piece.depth:0.###}m, the tile {style.TileWidth:0.###} x {style.TileDepth:0.###}m, so it won't be laid as a tile.");
                    }
                }
            }

            if (style.ModuleWidth <= 0f) {
                return;
            }

            // Sizes within the wall scale limit are scaled to fit; a plain
            // wall of the storey's height but another width is a side wall.
            foreach (string category in BuildingStyle.WallCategories) {
                foreach (PieceAnnotation piece in style.Pieces(category)) {
                    bool fitsHeight = style.Fits(piece.height, style.ModuleHeight);
                    bool sideWall = category == "wall" && !piece.showsOpening && fitsHeight;

                    if (!style.Fits(piece.width, style.ModuleWidth) && !sideWall) {
                        report.Warnings.Add($"Style '{style.Name}': {piece.name} is {piece.width:0.###}m wide, the module {style.ModuleWidth:0.###}m, so it won't be used as a module.");
                    }

                    if (!fitsHeight) {
                        report.Warnings.Add($"Style '{style.Name}': {piece.name} is {piece.height:0.###}m tall, the storey {style.ModuleHeight:0.###}m, so it won't be used.");
                    }
                }
            }
        }

        /// <summary>
        /// Checks an id is set and not used before; returns a label for the
        /// messages ("Lot 'a'", or "Lot #3" without an id).
        /// </summary>
        private static string CheckId(string kind, string id, HashSet<string> ids, Report report)
        {
            if (string.IsNullOrEmpty(id)) {
                string unnamed = $"{kind} #{ids.Count + 1}";
                report.Errors.Add($"{unnamed} has no id.");
                return unnamed;
            }

            if (!ids.Add(id)) {
                report.Errors.Add($"The id '{id}' is used more than once.");
            }

            return $"{kind} '{id}'";
        }

        /// <summary>
        /// The style a street or storey names, or null (with an error) if it
        /// names none or one no piece is tagged with.
        /// </summary>
        private static BuildingStyle FindStyle(string label, string name, Dictionary<string, BuildingStyle> styles,
            HashSet<string> usedStyles, Report report)
        {
            if (string.IsNullOrEmpty(name)) {
                report.Errors.Add($"{label} names no style.");
                return null;
            }

            usedStyles.Add(name);

            if (!styles.TryGetValue(name, out BuildingStyle style)) {
                report.Errors.Add($"{label}: no piece is tagged with the style '{name}' (tag pieces in TeaLeaf > Level Gen > Review Pieces).");
                return null;
            }

            return style;
        }

        /// <summary>
        /// An error if a style has no usable piece of a category.
        /// </summary>
        private static void RequirePieces(string label, BuildingStyle style, string category, Report report)
        {
            if (style.Pieces(category).Count == 0) {
                report.Errors.Add($"{label}: style '{style.Name}' has no usable {category} piece.");
            }
        }

        /// <summary>
        /// An error if a style has no usable piece of a wall category that
        /// fits its module and may stand on the storey (the pieces the
        /// facades are built from; above the ground, none marked ground
        /// storey only).
        /// </summary>
        private static void RequireModules(string label, BuildingStyle style, string category, int storey, Report report)
        {
            if (style.Pieces(category).Count == 0) {
                RequirePieces(label, style, category, report);
            } else if (style.Modules(category).Count == 0) {
                report.Errors.Add($"{label}: style '{style.Name}' has no usable {category} piece that fits its {style.ModuleWidth:0.###} x {style.ModuleHeight:0.###}m module.");
            } else if (style.Modules(category, storey).Count == 0) {
                report.Errors.Add($"{label}: style '{style.Name}' has no {category} piece that fits its module and isn't ground storey only.");
            }
        }

        /// <summary>
        /// A point on the ground: its x and z.
        /// </summary>
        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        /// <summary>
        /// Whether two ground rectangles overlap by more than the tolerance:
        /// the separating axis test, on the four edge directions.
        /// </summary>
        private static bool Overlap(GroundRect a, GroundRect b)
        {
            return OverlapAlong(a, b, a.AxisX) && OverlapAlong(a, b, a.AxisZ)
                && OverlapAlong(a, b, b.AxisX) && OverlapAlong(a, b, b.AxisZ);
        }

        /// <summary>
        /// Whether two rectangles' shadows on one axis overlap by more than
        /// the tolerance.
        /// </summary>
        private static bool OverlapAlong(GroundRect a, GroundRect b, Vector2 axis)
        {
            float distance = Mathf.Abs(Vector2.Dot(b.Centre - a.Centre, axis));
            return distance < Radius(a, axis) + Radius(b, axis) - _settings.OverlapTolerance;
        }

        /// <summary>
        /// Whether a convex polygon (its corners in order round it) and a
        /// ground rectangle overlap by more than the tolerance: the
        /// separating axis test on the polygon's edge normals and the
        /// rectangle's axes.
        /// </summary>
        private static bool OverlapConvex(List<Vector2> polygon, GroundRect rect)
        {
            List<Vector2> axes = new() { rect.AxisX, rect.AxisZ };

            for (int i = 0; i < polygon.Count; i++) {
                Vector2 edge = polygon[(i + 1) % polygon.Count] - polygon[i];

                if (edge.sqrMagnitude > 0f) {
                    axes.Add(new Vector2(-edge.y, edge.x).normalized);
                }
            }

            foreach (Vector2 axis in axes) {
                float min = float.MaxValue;
                float max = float.MinValue;

                foreach (Vector2 corner in polygon) {
                    float along = Vector2.Dot(corner, axis);
                    min = Mathf.Min(min, along);
                    max = Mathf.Max(max, along);
                }

                float centre = Vector2.Dot(rect.Centre, axis);
                float radius = Radius(rect, axis);

                if (max <= centre - radius + _settings.OverlapTolerance || min >= centre + radius - _settings.OverlapTolerance) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Half a rectangle's extent along an axis.
        /// </summary>
        private static float Radius(GroundRect rect, Vector2 axis)
        {
            return Mathf.Abs(Vector2.Dot(rect.AxisX, axis)) * rect.HalfX + Mathf.Abs(Vector2.Dot(rect.AxisZ, axis)) * rect.HalfZ;
        }

        /// <summary>
        /// One Console entry for a spec: an error if it can't be built, a
        /// warning if it has warnings, otherwise a plain message.
        /// </summary>
        internal static void Log(string path, Report report)
        {
            StringBuilder text = new();
            text.Append($"LayoutSpecValidator: '{path}': {report.Errors.Count} error(s), {report.Warnings.Count} warning(s).");

            foreach (string error in report.Errors) {
                text.Append("\nError: ").Append(error);
            }

            foreach (string warning in report.Warnings) {
                text.Append("\nWarning: ").Append(warning);
            }

            if (report.Errors.Count > 0) {
                Debug.LogError(text.ToString());
            } else if (report.Warnings.Count > 0) {
                Debug.LogWarning(text.ToString());
            } else {
                Debug.Log(text.ToString());
            }
        }
    }
}
