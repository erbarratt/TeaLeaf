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
        public const int CurrentFormat = 1;

        internal const string AnnotationsPath = PrefabCatalogueScanner.DataFolder + "/annotations.json";

        public int format = CurrentFormat;
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

            if (annotations == null || annotations.format != CurrentFormat) {
                Debug.LogWarning($"PieceAnnotations: '{AnnotationsPath}' is not format {CurrentFormat}; it is left as it is.");
                return null;
            }

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
            "roof", "detail"
        };

        public string guid;
        public string name;
        public string path;

        public string category = "";
        // The way(s) the piece faces, each a level axis (+X, -X, +Z, -Z): one
        // for a wall, two for a corner, none for a floor or ground tile.
        public List<Vector3> fronts = new();
        // The module's size in metres: width along the piece's right (as seen
        // from in front), height, and depth along its front. For a piece with
        // no front, width is along X and depth along Z.
        public float width;
        public float height;
        public float depth;
        public List<ConnectionPoint> connections = new();

        public bool excluded;
        public bool verified;
        public string note = "";

        /// <summary>
        /// A first annotation for a piece, filled in from what the catalogue
        /// measured: its suggested category, front(s) (measured, or +Z
        /// assumed), module size and connection points. Never verified.
        /// </summary>
        internal static PieceAnnotation Suggest(CatalogueEntry entry)
        {
            PieceAnnotation annotation = new() {
                guid = entry.guid,
                name = entry.name,
                path = entry.path,
                category = Array.IndexOf(Categories, entry.suggestedCategory) >= 0 ? entry.suggestedCategory : "",
                fronts = new List<Vector3>(entry.suggestedFronts),
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
