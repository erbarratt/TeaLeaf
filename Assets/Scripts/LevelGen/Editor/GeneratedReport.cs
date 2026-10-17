using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Stage 6: the report on what the generator built, made after the
    /// facades, lots, dead ends and roofs are placed (LevelGenerator) and on
    /// demand for a Generated root already in the scene (TeaLeaf > Level Gen
    /// > Report Generated). It finds the problems a walk through the level
    /// would find, lists them in the Console and puts a marker at each on the
    /// GeneratedRoot (drawn in the Scene view and the headset):
    ///
    /// - Free wall ends (FreeEnds): every front wall's two vertical ends (the
    ///   storeys' walls, a corner's fillers, a dead end's; side walls only
    ///   cover - a side wall's front end is a real wall end at its step, its
    ///   back end open by design, the buildings having no back walls or
    ///   interiors) are sampled just outside the end face, across its
    ///   thickness and up its height. A sample is bare when nothing else
    ///   placed stands against it (everything but doors, window inserts and
    ///   roofs covers, by its oriented render box) and it can be seen from a
    ///   street: a straight line from it to one of the nearest few street
    ///   tiles, at eye height, that no covering piece crosses. An end with
    ///   enough bare samples is reported with its bare share.
    /// - Problems the builders meet as they go (Add): a piece refused for
    ///   standing in a street, a side wall with no room before a street, a
    ///   corner side that carries nothing.
    ///
    /// It only finds and lists; nothing is filled in afterwards. Each find is
    /// fixed where the piece is placed.
    /// </summary>
    internal static class GeneratedReport
    {
        // How far outside an end face a sample is taken, and how far a piece
        // is grown before a sample counts as inside it (the walls' overlap is
        // 4cm and a side wall stands FaceClearance behind a face, so touching
        // pieces always cover each other's samples).
        private const float SampleOut = 0.02f;
        private const float CoverTolerance = 0.08f;
        // The share of an end's samples that may be bare before it is listed:
        // a third, so a thin strip isn't a find, a half-open end is.
        private const float BareShare = 0.34f;
        // Samples across an end: thickness fractions by height fractions.
        private static readonly float[] _across = { 0.1f, 0.5f, 0.9f };
        private static readonly float[] _up = { 0.15f, 0.5f, 0.85f };
        // Where a sample may be seen from: street tiles within this distance,
        // at this height above them, up to this many spread round the sample
        // (at least this many degrees apart). A tile under or right beside a
        // wall (the ground tiles overrun the street's edges under the
        // facades) is no place to stand: a tile within StandClear of a wall's
        // footprint is left out.
        private const float ViewDistance = 14f;
        private const float EyeHeight = 1.6f;
        private const int ViewPoints = 6;
        private const float ViewSpread = 30f;
        private const float StandClear = 0.75f;
        // The spatial grid's cell size, in metres.
        private const float Cell = 4f;
        // How many of the worst finds are written to the Console.
        private const int Listed = 40;

        // Found while building (Add); taken by Run.
        private static readonly List<GeneratedRoot.Marker> _building = new();

        /// <summary>
        /// One placed piece: its root, its category, and its render box in
        /// the root's own space (so turned and scaled with it).
        /// </summary>
        private class Piece
        {
            public Transform Root;
            public string Path;
            public string Category;
            public Vector3 Min;
            public Vector3 Max;
            public Bounds World;
        }

        /// <summary>
        /// Pieces indexed by the ground cells their world bounds cover.
        /// </summary>
        private class Grid
        {
            public readonly List<Piece> Pieces = new();
            private readonly Dictionary<(int, int), List<int>> _cells = new();

            public void Add(Piece piece)
            {
                int index = Pieces.Count;
                Pieces.Add(piece);
                Bounds b = piece.World;
                b.Expand(CoverTolerance * 2f);

                for (int x = Mathf.FloorToInt(b.min.x / Cell); x <= Mathf.FloorToInt(b.max.x / Cell); x++) {
                    for (int z = Mathf.FloorToInt(b.min.z / Cell); z <= Mathf.FloorToInt(b.max.z / Cell); z++) {
                        if (!_cells.TryGetValue((x, z), out List<int> cell)) {
                            _cells[(x, z)] = cell = new List<int>();
                        }

                        cell.Add(index);
                    }
                }
            }

            public List<int> At(Vector3 point)
            {
                return _cells.TryGetValue((Mathf.FloorToInt(point.x / Cell), Mathf.FloorToInt(point.z / Cell)), out List<int> cell) ? cell : null;
            }
        }

        /// <summary>
        /// Empties the problems found by builders, before a build.
        /// </summary>
        internal static void Begin()
        {
            _building.Clear();
        }

        /// <summary>
        /// Records a problem a builder met: a marker of the kind at a place,
        /// with a note for the Console.
        /// </summary>
        internal static void Add(GeneratedRoot.MarkerKind kind, Vector3 position, Vector3 size, string note)
        {
            _building.Add(new GeneratedRoot.Marker { kind = kind, position = position, size = size, note = note });
        }

        [MenuItem("TeaLeaf/Level Gen/Report Generated")]
        private static void ReportSelected()
        {
            if (!LayoutSpecValidator.LoadInputs(out PrefabCatalogue catalogue, out PieceAnnotations annotations)) {
                return;
            }

            GeneratedRoot[] roots = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentsInParent<GeneratedRoot>(true)
                : Object.FindObjectsByType<GeneratedRoot>(FindObjectsSortMode.None);

            if (roots.Length == 0) {
                Debug.LogWarning("GeneratedReport: select something under a Generated root, or have one in the scene.");
                return;
            }

            GeneratorSettings settings = GeneratorSettings.Load();

            foreach (GeneratedRoot root in roots) {
                // The spec it was built from, its street widths resolved
                // again from the same seed, for where one can stand.
                LayoutSpec spec = null;

                if (!string.IsNullOrEmpty(root.SpecPath) && System.IO.File.Exists(root.SpecPath)) {
                    LayoutSpecValidator.ValidateFile(root.SpecPath, catalogue, annotations, settings, out spec, out _);
                }

                Begin();
                List<string> lines = Run(root.transform, annotations, spec, out List<GeneratedRoot.Marker> markers);
                Undo.RecordObject(root, "Report Generated");
                root.SetMarkers(markers);
                EditorUtility.SetDirty(root);
                Debug.Log($"GeneratedReport: '{root.transform.parent.parent.name}':\n{string.Join("\n", lines)}");
            }
        }

        /// <summary>
        /// Reports on everything under a Generated root: the free wall ends
        /// found now and the problems the builders added. Returns the
        /// Console lines (a summary first, then the worst finds) and every
        /// find as a marker.
        /// </summary>
        internal static List<string> Run(Transform root, PieceAnnotations annotations, LayoutSpec spec, out List<GeneratedRoot.Marker> markers)
        {
            markers = new List<GeneratedRoot.Marker>(_building);
            List<string> lines = new();
            Dictionary<string, string> categories = new();

            foreach (PieceAnnotation piece in annotations.pieces) {
                categories[piece.name] = piece.category;
            }

            Gather(root, categories, out Grid pieces, out Grid tiles);
            Grid standing = StandingTiles(tiles, pieces, spec);
            List<(float share, GeneratedRoot.Marker marker)> ends = FreeEnds(pieces, standing);
            ends.Sort((a, b) => b.share.CompareTo(a.share));
            int walls = 0;

            foreach (Piece piece in pieces.Pieces) {
                if (IsWall(piece.Category)) {
                    walls++;
                }
            }

            lines.Add($"Report: {pieces.Pieces.Count} piece(s) checked, {walls} wall(s), {standing.Pieces.Count} of {tiles.Pieces.Count} street tile(s) "
                + $"stood on; {ends.Count} free wall end(s) (over {BareShare:P0} bare and in view of a street); {_building.Count} problem(s) met while building.");

            for (int i = 0; i < ends.Count; i++) {
                markers.Add(ends[i].marker);

                if (i < Listed) {
                    lines.Add("    " + ends[i].marker.note);
                }
            }

            if (ends.Count > Listed) {
                lines.Add($"    ... and {ends.Count - Listed} more (every one has a marker on the Generated root).");
            }

            foreach (GeneratedRoot.Marker marker in _building) {
                lines.Add("    " + marker.note);
            }

            return lines;
        }

        /// <summary>
        /// Every placed piece under the root: the outermost objects named
        /// like a prefab variant (TL_...), each with its render box in its
        /// own space from its LOD0 renderers (all its renderers if it has no
        /// LODs). The streets' ground tiles go in tiles (what a wall may be
        /// seen from), the roofs are left out, everything else goes in
        /// pieces.
        /// </summary>
        private static void Gather(Transform root, Dictionary<string, string> categories, out Grid pieces, out Grid tiles)
        {
            pieces = new Grid();
            tiles = new Grid();
            List<MeshRenderer> renderers = new();

            foreach (Transform child in root) {
                if (child.name == "Roofs") {
                    continue;
                }

                bool street = child.name == "Streets";

                foreach (Transform t in child.GetComponentsInChildren<Transform>(true)) {
                    if (!t.name.StartsWith("TL_") || (t.parent != null && IsInsidePiece(t.parent, root))) {
                        continue;
                    }

                    renderers.Clear();
                    t.GetComponentsInChildren(true, renderers);
                    bool lods = renderers.Exists(r => r.name.EndsWith("_LOD0"));
                    Vector3 min = Vector3.positiveInfinity;
                    Vector3 max = Vector3.negativeInfinity;
                    Bounds world = default;
                    bool any = false;

                    foreach (MeshRenderer renderer in renderers) {
                        if (lods && !renderer.name.EndsWith("_LOD0")) {
                            continue;
                        }

                        MeshFilter filter = renderer.GetComponent<MeshFilter>();

                        if (filter == null || filter.sharedMesh == null) {
                            continue;
                        }

                        Bounds mesh = filter.sharedMesh.bounds;

                        for (int c = 0; c < 8; c++) {
                            Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                            Vector3 inWorld = renderer.transform.TransformPoint(corner);
                            Vector3 local = t.InverseTransformPoint(inWorld);
                            min = Vector3.Min(min, local);
                            max = Vector3.Max(max, local);

                            if (!any) {
                                world = new Bounds(inWorld, Vector3.zero);
                                any = true;
                            } else {
                                world.Encapsulate(inWorld);
                            }
                        }
                    }

                    if (!any) {
                        continue;
                    }

                    categories.TryGetValue(t.name, out string category);
                    Piece piece = new() { Root = t, Path = PathUnder(t, root), Category = category ?? "", Min = min, Max = max, World = world };
                    (street ? tiles : pieces).Add(piece);
                }
            }
        }

        /// <summary>
        /// The tiles one can stand on to look at the walls: those whose
        /// middle lies in a street (within a segment's rectangle at its
        /// width; the ground tiles overrun the edges under and behind the
        /// facades) and at least StandClear from every wall's footprint.
        /// With no spec, every tile clear of the walls.
        /// </summary>
        private static Grid StandingTiles(Grid tiles, Grid pieces, LayoutSpec spec)
        {
            Grid standing = new();

            foreach (Piece tile in tiles.Pieces) {
                Vector3 at = tile.World.center;

                if (spec != null && !InStreet(new Vector2(at.x, at.z), spec)) {
                    continue;
                }

                List<int> cell = pieces.At(at);
                bool clear = true;

                if (cell != null) {
                    foreach (int k in cell) {
                        Piece wall = pieces.Pieces[k];

                        if (!IsWall(wall.Category)) {
                            continue;
                        }

                        // The point's distance from the wall's footprint, in
                        // the wall's own space on the ground.
                        Vector3 local = wall.Root.InverseTransformPoint(at);
                        Vector3 scale = wall.Root.lossyScale;
                        float dx = Mathf.Max(wall.Min.x - local.x, local.x - wall.Max.x, 0f) * Mathf.Abs(scale.x);
                        float dz = Mathf.Max(wall.Min.z - local.z, local.z - wall.Max.z, 0f) * Mathf.Abs(scale.z);

                        if (dx * dx + dz * dz < StandClear * StandClear) {
                            clear = false;
                            break;
                        }
                    }
                }

                if (clear) {
                    standing.Add(tile);
                }
            }

            return standing;
        }

        /// <summary>
        /// Whether a ground point lies in some street segment's rectangle at
        /// its resolved width.
        /// </summary>
        private static bool InStreet(Vector2 point, LayoutSpec spec)
        {
            foreach (StreetSpec street in spec.streets) {
                for (int i = 0; i + 1 < street.points.Count; i++) {
                    Vector2 a = new(street.points[i].x, street.points[i].z);
                    Vector2 b = new(street.points[i + 1].x, street.points[i + 1].z);
                    Vector2 axis = b - a;
                    float length = axis.magnitude;

                    if (length < 0.0001f) {
                        continue;
                    }

                    axis /= length;
                    Vector2 offset = point - a;
                    float along = Vector2.Dot(offset, axis);
                    float across = Mathf.Abs(offset.x * axis.y - offset.y * axis.x);

                    if (along >= 0f && along <= length && across <= street.SegmentWidth(i) * 0.5f) {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether an object is inside a placed piece (one of its ancestors
        /// below the root is a piece).
        /// </summary>
        private static bool IsInsidePiece(Transform t, Transform root)
        {
            for (Transform p = t; p != null && p != root; p = p.parent) {
                if (p.name.StartsWith("TL_")) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// An object's path below the root, for the Console.
        /// </summary>
        private static string PathUnder(Transform t, Transform root)
        {
            string path = t.name;

            for (Transform p = t.parent; p != null && p != root; p = p.parent) {
                path = p.name + "/" + path;
            }

            return path;
        }

        private static bool IsWall(string category)
        {
            return category == "wall" || category == "window-wall" || category == "door-wall";
        }

        /// <summary>
        /// Whether a piece can cover another's end, or hide it from the
        /// street: anything placed but doors, window inserts, roof pieces
        /// and the dressing (pipes, lamp posts), which stands in front of the
        /// walls and hides nothing.
        /// </summary>
        private static bool Covers(string category)
        {
            return category != "door" && category != "window" && !category.StartsWith("roof") && !category.StartsWith("pipe-") && category != "lamp-post";
        }

        /// <summary>
        /// The free ends among the front walls: for each, its two end faces
        /// (across the longer horizontal side of its render box) sampled
        /// SampleOut outside, across the thickness and up the height; a
        /// sample inside no other covering piece (grown by CoverTolerance)
        /// and in view of a street is bare. Returns each end with more than
        /// BareShare bare, with its marker (at the end face's middle, the
        /// face's size).
        /// </summary>
        private static List<(float, GeneratedRoot.Marker)> FreeEnds(Grid pieces, Grid tiles)
        {
            List<(float, GeneratedRoot.Marker)> found = new();
            List<Vector3> views = new();

            for (int i = 0; i < pieces.Pieces.Count; i++) {
                Piece wall = pieces.Pieces[i];
                Transform group = wall.Root.parent;

                if (!IsWall(wall.Category) || group == null || group.name.StartsWith("Side ") || group.name.StartsWith("End ")) {
                    continue;
                }

                Vector3 size = wall.Max - wall.Min;
                // Its width runs along the longer horizontal side.
                int along = size.x >= size.z ? 0 : 2;
                int across = along == 0 ? 2 : 0;

                for (int end = 0; end < 2; end++) {
                    float face = end == 0 ? wall.Min[along] : wall.Max[along];
                    Vector3 axis = Vector3.zero;
                    axis[along] = end == 0 ? -1f : 1f;
                    Vector3 outward = wall.Root.TransformDirection(axis).normalized;
                    int bare = 0;
                    int total = 0;
                    Vector3 middle = Vector3.zero;
                    views.Clear();

                    foreach (float a in _across) {
                        foreach (float u in _up) {
                            Vector3 local = Vector3.zero;
                            local[along] = face;
                            local[across] = Mathf.Lerp(wall.Min[across], wall.Max[across], a);
                            local.y = Mathf.Lerp(wall.Min.y, wall.Max.y, u);
                            Vector3 sample = wall.Root.TransformPoint(local) + outward * SampleOut;
                            middle += sample;
                            total++;

                            if (Covered(sample, i, pieces)) {
                                continue;
                            }

                            if (views.Count == 0) {
                                ViewsOf(wall.World.center, tiles, views);
                            }

                            if (Seen(sample, pieces, views)) {
                                bare++;
                            }
                        }
                    }

                    float share = (float)bare / total;

                    if (share > BareShare) {
                        middle /= total;
                        Vector3 faceSize = wall.Root.TransformVector(new Vector3(along == 0 ? 0.1f : size.x, size.y, along == 2 ? 0.1f : size.z));
                        faceSize = new Vector3(Mathf.Abs(faceSize.x), Mathf.Abs(faceSize.y), Mathf.Abs(faceSize.z));
                        // Where it is seen from, for finding it: the first
                        // view with a clear line to the end's middle.
                        string seenFrom = "";

                        foreach (Vector3 view in views) {
                            if (!Blocked(middle + outward * SampleOut, view, pieces)) {
                                seenFrom = $", seen from ({view.x:0.0}, {view.z:0.0})";
                                break;
                            }
                        }

                        found.Add((share, new GeneratedRoot.Marker {
                            kind = GeneratedRoot.MarkerKind.FreeEnd,
                            position = middle,
                            size = faceSize,
                            note = $"Free end: {wall.Path}, {(end == 0 ? "first" : "second")} end at ({middle.x:0.0}, {middle.y:0.0}, {middle.z:0.0}), "
                                + $"{share:P0} bare{seenFrom}."
                        }));
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Whether a world point is inside some covering piece other than the
        /// one at self (its render box grown by CoverTolerance).
        /// </summary>
        private static bool Covered(Vector3 point, int self, Grid pieces)
        {
            List<int> cell = pieces.At(point);

            if (cell == null) {
                return false;
            }

            foreach (int k in cell) {
                if (k == self) {
                    continue;
                }

                Piece other = pieces.Pieces[k];

                if (!Covers(other.Category)) {
                    continue;
                }

                Bounds world = other.World;
                world.Expand(CoverTolerance * 2f);

                if (!world.Contains(point)) {
                    continue;
                }

                Vector3 local = other.Root.InverseTransformPoint(point);
                Vector3 scale = other.Root.lossyScale;
                bool inside = true;

                for (int axis = 0; axis < 3 && inside; axis++) {
                    float grow = CoverTolerance / Mathf.Max(0.0001f, Mathf.Abs(scale[axis]));
                    inside = local[axis] >= other.Min[axis] - grow && local[axis] <= other.Max[axis] + grow;
                }

                if (inside) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Where a wall may be seen from: up to ViewPoints street tiles
        /// within ViewDistance of a point, nearest first, each at least
        /// ViewSpread degrees round from those already chosen, EyeHeight
        /// above the tile's top.
        /// </summary>
        private static void ViewsOf(Vector3 from, Grid tiles, List<Vector3> views)
        {
            List<(float distance, Vector3 at)> near = new();
            int reach = Mathf.CeilToInt(ViewDistance / Cell);
            int cx = Mathf.FloorToInt(from.x / Cell);
            int cz = Mathf.FloorToInt(from.z / Cell);
            HashSet<int> seen = new();

            for (int x = cx - reach; x <= cx + reach; x++) {
                for (int z = cz - reach; z <= cz + reach; z++) {
                    List<int> cell = tiles.At(new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell));

                    if (cell == null) {
                        continue;
                    }

                    foreach (int k in cell) {
                        if (!seen.Add(k)) {
                            continue;
                        }

                        Bounds b = tiles.Pieces[k].World;
                        Vector3 at = new(b.center.x, b.max.y + EyeHeight, b.center.z);
                        float distance = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(from.x, from.z));

                        if (distance <= ViewDistance) {
                            near.Add((distance, at));
                        }
                    }
                }
            }

            near.Sort((a, b) => a.distance.CompareTo(b.distance));

            foreach ((float _, Vector3 at) in near) {
                Vector2 way = new Vector2(at.x - from.x, at.z - from.z).normalized;
                bool apart = true;

                foreach (Vector3 chosen in views) {
                    if (Vector2.Angle(way, new Vector2(chosen.x - from.x, chosen.z - from.z)) < ViewSpread) {
                        apart = false;
                        break;
                    }
                }

                if (apart) {
                    views.Add(at);

                    if (views.Count >= ViewPoints) {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Whether a point can be seen from any of the views: the straight
        /// line to it crosses no covering piece's render box - the wall's
        /// own included, since its end face is only seen from in front of
        /// it, never through its body. A point with no view in reach is
        /// unseen.
        /// </summary>
        private static bool Seen(Vector3 point, Grid pieces, List<Vector3> views)
        {
            foreach (Vector3 view in views) {
                if (!Blocked(point, view, pieces)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether some covering piece lies across the line from a to b: the
        /// cells along the line are gathered, and each piece's render box
        /// tested against the line in the piece's own space.
        /// </summary>
        private static bool Blocked(Vector3 a, Vector3 b, Grid pieces)
        {
            HashSet<int> tested = new();
            float length = Vector3.Distance(a, b);
            // Every half metre, so no cell the line clips is skipped.
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.5f));

            for (int s = 0; s <= steps; s++) {
                List<int> cell = pieces.At(Vector3.Lerp(a, b, (float)s / steps));

                if (cell == null) {
                    continue;
                }

                foreach (int k in cell) {
                    if (!tested.Add(k)) {
                        continue;
                    }

                    Piece piece = pieces.Pieces[k];

                    // A Ray's direction is normalised, so the distance it
                    // gives is in metres.
                    if (!Covers(piece.Category) || !piece.World.IntersectRay(new Ray(a, b - a), out float along) || along > length) {
                        continue;
                    }

                    if (Crosses(piece, a, b)) {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the line from a to b passes through a piece's render box
        /// (the slab test in the piece's own space).
        /// </summary>
        private static bool Crosses(Piece piece, Vector3 a, Vector3 b)
        {
            Vector3 from = piece.Root.InverseTransformPoint(a);
            Vector3 to = piece.Root.InverseTransformPoint(b);
            float enter = 0f;
            float leave = 1f;

            for (int axis = 0; axis < 3; axis++) {
                float d = to[axis] - from[axis];

                if (Mathf.Abs(d) < 0.000001f) {
                    if (from[axis] < piece.Min[axis] || from[axis] > piece.Max[axis]) {
                        return false;
                    }

                    continue;
                }

                float t0 = (piece.Min[axis] - from[axis]) / d;
                float t1 = (piece.Max[axis] - from[axis]) / d;

                if (t0 > t1) {
                    (t0, t1) = (t1, t0);
                }

                enter = Mathf.Max(enter, t0);
                leave = Mathf.Min(leave, t1);

                if (enter > leave) {
                    return false;
                }
            }

            return true;
        }
    }
}
