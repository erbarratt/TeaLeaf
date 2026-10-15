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

            foreach (LotSpec lot in spec.lots) {
                CheckLot(lot, styles, ids, usedStyles, lotRects, report);
            }

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

                float stretch = StretchLength(street, frontage, sideSign, streets, label, report);

                if (!lengthKnown || stretch <= 0f) {
                    continue;
                }

                if (length > stretch + _settings.OverlapTolerance) {
                    report.Errors.Add($"{label}: its buildings are {length:0.##}m long, {length - stretch:0.##}m more than its {stretch:0.##}m stretch of street.");
                } else if (stretch - length >= smallestSpacing) {
                    report.Warnings.Add($"{label}: {stretch - length:0.##}m is left at its end, more than a module; the generator fills it, but another building may suit.");
                }
            }
        }

        /// <summary>
        /// About how much of the street's edge a frontage can fill: the
        /// length of the street's middle line between its two points, less
        /// half the width of any street branching off on its side at either
        /// end. Reports a street branching off on its side part-way along it
        /// (an error: the frontage must end there).
        /// </summary>
        private static float StretchLength(StreetSpec street, FrontageSpec frontage, float sideSign, List<StreetSpec> streets,
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
                float branchWidth = BranchWidth(street, k, sideNormal, streets, _settings);

                if (branchWidth <= 0f) {
                    continue;
                }

                if (k == frontage.from || k == frontage.to) {
                    length -= branchWidth * 0.5f;
                } else {
                    report.Errors.Add($"{label}: a street branches off on its side at point {k}, so the frontage must end there.");
                }
            }

            return length;
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
        /// A lot's id, footprint and storeys, and that the building floor
        /// style has floors for its upper storeys; adds its footprint for the
        /// overlap tests once its module width is known.
        /// </summary>
        private static void CheckLot(LotSpec lot, Dictionary<string, BuildingStyle> styles, HashSet<string> ids,
            HashSet<string> usedStyles, List<GroundRect> rects, Report report)
        {
            string label = CheckId("Lot", lot.id, ids, report);

            if (lot.widthModules < 1 || lot.depthModules < 1) {
                report.Errors.Add($"{label} needs at least one module along its front and its depth.");
            }

            float moduleWidth = CheckStoreys(label, lot.storeys, lot.doorModule, lot.widthModules, styles, usedStyles, report);

            // Every whole building's upper floors come from one style,
            // whatever its walls. (A facade has no floors.)
            if (_settings.MaxStoreys > 1) {
                usedStyles.Add(StyleRules.BuildingFloors);

                if (!styles.TryGetValue(StyleRules.BuildingFloors, out BuildingStyle floors) || floors.Pieces("floor").Count == 0) {
                    report.Warnings.Add($"{label}: style '{StyleRules.BuildingFloors}' has no usable floor piece, so any upper storeys it is given will have no floor.");
                }
            }

            if (moduleWidth > 0f && lot.widthModules >= 1 && lot.depthModules >= 1) {
                // Modules are laid at the module less the overlap; the last
                // one's overlap stands out at the far end.
                float spacing = moduleWidth - _settings.WallOverlap;
                Vector3 front = lot.Front;
                Vector2 axisZ = new(front.x, front.z);
                rects.Add(new GroundRect {
                    Label = label,
                    Centre = Ground(lot.origin),
                    AxisX = new Vector2(axisZ.y, -axisZ.x),
                    AxisZ = axisZ,
                    HalfX = (lot.widthModules * spacing + _settings.WallOverlap) * 0.5f,
                    HalfZ = (lot.depthModules * spacing + _settings.WallOverlap) * 0.5f
                });
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

                // Every storey is walls with an outer corner where two sides
                // meet (a facade turns one where it ends at a branching
                // street); the ground storey stands on foundations and holds
                // the door.
                RequireModules(storeyLabel, style, "wall", storey, report);

                // The last listed style repeats above it: it needs walls that
                // may stand above the ground too.
                if (storey == 0 && storeys.Count == 1 && _settings.MaxStoreys > 1 && !groundOnly) {
                    RequireModules($"{storeyLabel} (repeated above)", style, "wall", 1, report);
                }

                RequirePieces(storeyLabel, style, "outer-corner", report);

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
