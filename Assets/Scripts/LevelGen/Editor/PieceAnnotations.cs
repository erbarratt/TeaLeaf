using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The maintainer's facts about each prefab, as kept in
    /// Assets/Data/LevelGen/annotations.json and edited in the Review Pieces
    /// window (PieceReviewWindow). Kept apart from the catalogue, so a
    /// rescan never overwrites what was judged by eye.
    ///
    /// Keyed by the prefab's GUID, which survives a rename or a move; the
    /// name and path are copied in only so the file can be read by hand.
    /// A prefab with no entry here has not been looked at. The generator
    /// places only pieces that are verified and not excluded.
    /// </summary>
    [Serializable]
    public class PieceAnnotations
    {
        // Bumped when the format changes, so the later stages can refuse an
        // old file rather than misread it.
        public const int CurrentFormat = 2;
        // Format 1 is format 2 without styles, so it is read as it is and
        // written back as format 2 with the next change.
        private const int UpgradableFormat = 1;

        internal const string AnnotationsPath = PrefabCatalogueScanner.DataFolder + "/annotations.json";

        [Tooltip("The annotations file's format number, e.g. 2 (the current format); format 1 is read and written back as 2, any other is left alone and not used.")]
        public int format = CurrentFormat;
        [Tooltip("The annotated prefabs: one entry is one prefab's facts as judged in Review Pieces, keyed by its GUID, e.g. a plaster wall's category, front, size, points and styles. A prefab with no entry has not been looked at.")]
        public List<PieceAnnotation> pieces = new();

        /// <summary>
        /// Reads the annotations file, or returns an empty set if there is
        /// none yet. Returns null (with a warning) for a file in another
        /// format, so it is never overwritten by mistake.
        /// </summary>
        internal static PieceAnnotations Load()
        {
            if (!File.Exists(AnnotationsPath)) {
                return new PieceAnnotations();
            }

            PieceAnnotations annotations = JsonUtility.FromJson<PieceAnnotations>(File.ReadAllText(AnnotationsPath));

            if (annotations == null || (annotations.format != CurrentFormat && annotations.format != UpgradableFormat)) {
                Debug.LogWarning($"PieceAnnotations: '{AnnotationsPath}' is not format {CurrentFormat}; it is left as it is.");
                return null;
            }

            annotations.format = CurrentFormat;
            return annotations;
        }

        /// <summary>
        /// Writes the annotations, sorted by path so the file's order is
        /// stable, only if the text differs from the file already there.
        /// </summary>
        internal void Save()
        {
            pieces.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            PrefabCatalogueScanner.EnsureFolder(PrefabCatalogueScanner.DataFolder);
            string json = PrefabCatalogueScanner.ShortenNumbers(JsonUtility.ToJson(this, true));

            if (File.Exists(AnnotationsPath) && File.ReadAllText(AnnotationsPath) == json) {
                return;
            }

            File.WriteAllText(AnnotationsPath, json);
            AssetDatabase.ImportAsset(AnnotationsPath);
        }
    }

    /// <summary>
    /// One prefab's annotation. All positions and directions are in the
    /// prefab root's own space, as in the catalogue.
    /// </summary>
    [Serializable]
    public class PieceAnnotation
    {
        // PieceAnnotation.category values. Empty = not yet chosen.
        internal static readonly string[] Categories = {
            "", "wall", "door-wall", "window-wall", "outer-corner", "inner-corner",
            "foundation", "foundation-corner", "floor", "ground", "steps", "column", "beam",
            "roof", "detail", "window", "jetty", "door",
            "roof-end", "roof-end-half", "roof-ridge-end", "roof-ridge-end-half", "roof-middle", "roof-gable",
            "roof-hip-base", "roof-hip-middle", "roof-hip-top", "roof-chimney", "roof-dormer", "roof-railing",
            "pipe-straight", "pipe-corner", "pipe-end", "pipe-fitting", "pipe-tee", "pipe-valve", "pipe-vent", "lamp-post",
            "grass", "nettle"
        };

        [Tooltip("The prefab's GUID, the annotation's key: survives a rename or move of the prefab, e.g. the 32-hex-digit id from its .meta file.")]
        public string guid;
        [Tooltip("The prefab's name, copied in only so the file can be read by hand, e.g. \"TL_PlasterWalls_A_Cross\". Not used to find the prefab.")]
        public string name;
        [Tooltip("The prefab's asset path, copied in so the file can be read by hand and sorted, e.g. Assets/Prefabs/Environment/Walls/TL_PlasterWalls_A_Cross.prefab. Not used to find the prefab.")]
        public string path;

        [Tooltip("What kind of piece it is, which decides where the generator uses it, e.g. \"wall\", \"door-wall\", \"foundation\", \"roof-end\", \"lamp-post\". Empty = not yet chosen: never placed, and can't be verified.")]
        public string category = "";
        // The way(s) the piece faces, each a level axis (+X, -X, +Z, -Z): one
        // for a wall, two for a corner, none for a floor or ground tile.
        [Tooltip("The way(s) the piece faces, each a level axis of its root (+X, -X, +Z, -Z): one entry per front, e.g. one (0, 0, 1) for a wall facing +Z, two for an outer corner, none for a floor or steps.")]
        public List<Vector3> fronts = new();
        // The module's size in metres: width along the piece's right (as seen
        // from in front), height, and depth along its front. For a piece with
        // no front, width is along X and depth along Z.
        [Tooltip("The piece's module width in metres, along its right as seen from in front (along X with no front), e.g. 3 for a full plaster wall's back plane. Compared with its style's module to decide whether it fits.")]
        public float width;
        [Tooltip("The piece's module height in metres, e.g. 3 for a plaster wall, 4.5 for a wall-tall one. Compared with its style's module height and storeys' heights.")]
        public float height;
        [Tooltip("The piece's depth in metres along its front (along Z with no front), e.g. 0.6 for a wall. Used to set facade depth lines and interior wall thickness.")]
        public float depth;
        [Tooltip("The piece's named points in its root's space: one entry is one point (name, position, way out), e.g. \"left\" at a wall's base left end, \"a\"/\"b\" at a pipe's flanges, \"window\" for a blind-window insert spot. The generator places pieces by these.")]
        public List<ConnectionPoint> connections = new();
        // The building styles the piece belongs to (e.g. "plaster"), which a
        // layout spec names per storey or street. A piece can be in several:
        // a foundation is tagged with every style that stands on it.
        [Tooltip("The building styles the piece belongs to, which a layout spec names per storey or street: one entry is one style, lower case with hyphens, e.g. \"plaster\", \"masonry-cellar\". A foundation is tagged with every style that stands on it; none = used by no style.")]
        public List<string> styles = new();

        // Placed only on a building's ground storey (a wall too heavy or
        // grand for an upper one), whatever its style allows.
        [Tooltip("On: placed only on a building's ground storey, whatever its style allows (a wall too heavy or grand for an upper one), e.g. WallTall_StoneWindowBase. Off: any storey its style is on.")]
        public bool groundStoreyOnly;
        // A plain wall that shows a window or door (a blind window, framed but
        // not see through, or half a door): never used where a wall must be
        // plain, such as a side wall.
        [Tooltip("On: a plain wall that shows a window or door (a blind window, or half a door), never used where a wall must be plain, such as a side wall, while another will do, e.g. PlasterWalls_A_WindowBase. Off: a truly plain wall.")]
        public bool showsOpening;
        // A plain wall that, on a storey whose style offers it, stands at
        // every module but the spec's door (e.g. the masonry ground storey's
        // MasonaryWall).
        [Tooltip("On: a plain wall that, on a storey whose style offers it, stands at every module but the spec's door, with no random doors or windows there, e.g. the masonry ground storey's MasonaryWall. Off: mixed with the style's other walls.")]
        public bool fillsStorey;
        // A plain wall that never mixes on a storey with another so marked:
        // each storey draws one of them and every plain wall on it - its
        // modules, corner fillers and side walls - is that one (e.g.
        // wall-tall's WindowBase and StoneWindowBase).
        [Tooltip("On: a plain wall that never mixes on a storey with another so marked - each storey draws one, and every plain wall on it (modules, corner fillers, side walls) is that one, e.g. wall-tall's WindowBase and StoneWindowBase. Off: mixes freely.")]
        public bool onePerStorey;
        // A window insert whose panes glow as if lit from behind (a lit
        // material): put into a share of the window holes filled
        // (GeneratorSettings.LitWindowShare), the unlit inserts in the rest.
        [Tooltip("On: a window insert whose panes glow as if lit from behind, put into Lit Window Share of the filled holes, e.g. TL_Window_A_Lit. Off: an unlit insert, used in the rest.")]
        public bool lit;
        // A plain wall the open window interiors may build their rooms from
        // (OpenWindowInteriors), scaled thin; any style it is tagged with
        // matches a building's, "default" stands in where none does.
        [Tooltip("On: a plain wall the open window interiors may build their rooms from, scaled thin; its styles match a building's, and \"default\" stands in where none does (still needs Verified). Off: never used inside.")]
        public bool openWindowInterior;
        [Tooltip("On: the piece is never placed by the generator, e.g. a piece with gameplay scripts, an assembly or one with gaps. Off: placed if verified.")]
        public bool excluded;
        [Tooltip("On: the maintainer has confirmed its front, pivot, footprint and points, so the generator may place it (unless Excluded). Off: never placed.")]
        public bool verified;
        [Tooltip("A free note about the piece for the maintainer, e.g. \"pivot mid-height, check by eye\". Not read by the generator.")]
        public string note = "";

        /// <summary>
        /// A first annotation for a piece, filled in from what the catalogue
        /// measured: its suggested category, front(s) (measured, or +Z
        /// assumed), styles (by name, StyleRules), module size and connection
        /// points. Never verified.
        /// </summary>
        internal static PieceAnnotation Suggest(CatalogueEntry entry)
        {
            PieceAnnotation annotation = new() {
                guid = entry.guid,
                name = entry.name,
                path = entry.path,
                // A window insert (Windows folder) measures as a detail; it
                // is the window category the generator puts into wall holes.
                category = entry.suggestedCategory == "detail" && entry.folder.EndsWith("/Windows", StringComparison.Ordinal) ? "window"
                    : Array.IndexOf(Categories, entry.suggestedCategory) >= 0 ? entry.suggestedCategory : "",
                fronts = new List<Vector3>(entry.suggestedFronts),
                styles = StyleRules.Suggest(entry.name),
                // The generator never places a piece with gameplay scripts, and
                // an assembly only once it is taken in by hand.
                excluded = entry.flags.Contains(PrefabCatalogueScanner.FlagGameplayScripts)
                    || entry.flags.Contains(PrefabCatalogueScanner.FlagAssembly)
            };

            annotation.SuggestSize(entry);

            foreach (ConnectionPoint point in entry.suggestedConnections) {
                annotation.connections.Add(new ConnectionPoint {
                    name = point.name,
                    position = point.position,
                    direction = point.direction
                });
            }

            return annotation;
        }

        /// <summary>
        /// Sets width, height and depth from the measurements, for the first
        /// front (or the root's X and Z if there is none). A wall's width and
        /// height come from its back plane, which leaves out the trim standing
        /// out from it; a floor's width and depth from its walking surface
        /// (the collider tops, so floors laid by them meet); steps' from their
        /// ramp (width across, run along the flight, rise).
        /// </summary>
        internal void SuggestSize(CatalogueEntry entry)
        {
            if (entry.ramps.Count > 0 && fronts.Count == 0) {
                RampInfo ramp = entry.ramps[0];
                Vector3 run = ramp.top - ramp.foot;
                width = Round(ramp.width);
                height = Round(run.y);
                depth = Round(new Vector2(run.x, run.z).magnitude);
                return;
            }

            if (entry.hasWalkSurface && fronts.Count == 0 && (category == "floor" || category == "ground")) {
                width = Round(entry.walkMax.x - entry.walkMin.x);
                height = Round(entry.renderSize.y);
                depth = Round(entry.walkMax.z - entry.walkMin.z);
                return;
            }

            Vector3 extents = (entry.renderMax - entry.renderMin) * 0.5f;
            Vector3 front = fronts.Count > 0 ? fronts[0] : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, -front);

            width = Round(2f * Along(extents, right));
            height = Round(2f * extents.y);
            depth = Round(2f * Along(extents, front));

            if (entry.hasBackPlane && fronts.Count == 1) {
                Vector3 planeExtents = entry.backPlaneSize * 0.5f;
                width = Round(2f * Along(planeExtents, right));
                height = Round(entry.backPlaneSize.y);
            }
        }

        /// <summary>
        /// Sets left, right, top and bottom connection points for a piece with
        /// one front, on the back face of its back plane (or, without one, of
        /// its render box): left and right at the base of its two ends as
        /// seen from in front, top and bottom at the middle of its upper and
        /// lower edges, each with the way out of the piece. Gives the same
        /// points as the catalogue's suggestion for a wall.
        /// </summary>
        internal void SuggestConnections(CatalogueEntry entry)
        {
            connections.Clear();

            if (fronts.Count != 1) {
                return;
            }

            Vector3 min = entry.hasBackPlane ? entry.backPlaneMin : entry.renderMin;
            Vector3 max = entry.hasBackPlane ? entry.backPlaneMax : entry.renderMax;
            Vector3 centre = (min + max) * 0.5f;
            Vector3 extents = (max - min) * 0.5f;

            Vector3 front = fronts[0];
            // Seen from in front, looking back at the piece (along -front), the
            // viewer's right is up x -front in Unity's left-handed axes.
            Vector3 right = Vector3.Cross(Vector3.up, -front);
            Vector3 back = centre - front * Along(extents, front);
            Vector3 baseCentre = new(back.x, min.y, back.z);
            Vector3 topCentre = new(back.x, max.y, back.z);
            float halfWidth = Along(extents, right);

            AddConnection("left", baseCentre - right * halfWidth, -right);
            AddConnection("right", baseCentre + right * halfWidth, right);
            AddConnection("top", topCentre, Vector3.up);
            AddConnection("bottom", baseCentre, Vector3.down);
        }

        /// <summary>
        /// Adds one rounded connection point.
        /// </summary>
        private void AddConnection(string pointName, Vector3 position, Vector3 direction)
        {
            connections.Add(new ConnectionPoint {
                name = pointName,
                position = Round(position),
                direction = direction
            });
        }

        /// <summary>
        /// A box's half-size along an axis direction (one of the six axes).
        /// </summary>
        private static float Along(Vector3 extents, Vector3 axis)
        {
            return Mathf.Abs(extents.x * axis.x) + Mathf.Abs(extents.y * axis.y) + Mathf.Abs(extents.z * axis.z);
        }

        /// <summary>
        /// A style name as it is stored: trimmed, lower case, with spaces
        /// as hyphens, so "Plaster A" and "plaster-a" are one style.
        /// </summary>
        internal static string NormaliseStyle(string style)
        {
            return string.Join("-", style.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Rounds to the millimetre, as the catalogue does.
        /// </summary>
        internal static float Round(float value)
        {
            return Mathf.Round(value * 1000f) / 1000f;
        }

        /// <summary>
        /// Rounds each axis to the millimetre.
        /// </summary>
        internal static Vector3 Round(Vector3 value)
        {
            return new Vector3(Round(value.x), Round(value.y), Round(value.z));
        }
    }
}
