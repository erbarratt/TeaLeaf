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
    /// The free wall ends it finds are closed by GapFiller before the street
    /// test and the cull; what the report lists afterwards is what that left.
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
        private static readonly float[] _up = { 1f / 12f, 3f / 12f, 5f / 12f, 7f / 12f, 9f / 12f, 11f / 12f };
        // An end another wall meets over only its lower or upper part (a
        // column standing on a higher level beside a lower one, at a flight
        // of steps) is free over the rest where at least this many rows of
        // samples at its top or bottom touch nothing.
        private const int PartRows = 2;
        // A sheared wall's foot and head: its vertices within this of its
        // bottom, and of its top, in its root's space.
        private const float EndBand = 0.05f;
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
        // The ground mesh is dealt out in cells this big for standing spots.
        private const float GroundSpot = 3f;
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
            // Its front in its own space (zero where it has none or no
            // annotation), for telling its trimmed end.
            public Vector3 Front;
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
        /// One front wall end nothing stands against, in view of a street:
        /// what GapFiller closes and the report lists.
        /// </summary>
        internal struct FreeEnd
        {
            // The wall whose end it is (the placed piece's root).
            public Transform Wall;
            // The middle of the end face, in world space.
            public Vector3 Centre;
            // Out of the end along the wall, and the wall's front: level
            // unit vectors.
            public Vector3 Outward;
            public Vector3 Front;
            // The face's bottom and top heights, and its thickness, in metres.
            public float Bottom;
            public float Top;
            public float Thickness;
            // The share of its samples bare.
            public float Share;
            public GeneratedRoot.Marker Marker;
        }

        /// <summary>
        /// The free wall ends under a Generated root as it stands now, for
        /// closing (GapFiller): the same finds the report lists.
        /// </summary>
        internal static List<FreeEnd> FindFreeEnds(Transform root, PieceAnnotations annotations, LayoutSpec spec)
        {
            Dictionary<string, PieceAnnotation> byName = new();

            foreach (PieceAnnotation piece in annotations.pieces) {
                byName[piece.name] = piece;
            }

            Gather(root, byName, out Grid pieces, out Grid tiles);
            return FreeEnds(pieces, StandingTiles(tiles, pieces, spec));
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
            Dictionary<string, PieceAnnotation> byName = new();

            foreach (PieceAnnotation piece in annotations.pieces) {
                byName[piece.name] = piece;
            }

            Gather(root, byName, out Grid pieces, out Grid tiles);
            Grid standing = StandingTiles(tiles, pieces, spec);
            List<FreeEnd> ends = FreeEnds(pieces, standing);
            ends.Sort((a, b) => b.Share.CompareTo(a.Share));
            int walls = 0;

            foreach (Piece piece in pieces.Pieces) {
                if (IsWall(piece.Category)) {
                    walls++;
                }
            }

            lines.Add($"Report: {pieces.Pieces.Count} piece(s) checked, {walls} wall(s), {standing.Pieces.Count} of {tiles.Pieces.Count} street tile(s) "
                + $"stood on; {ends.Count} free wall end(s) (over {BareShare:P0} bare and in view of a street); {_building.Count} problem(s) met while building.");

            for (int i = 0; i < ends.Count; i++) {
                markers.Add(ends[i].Marker);

                if (i < Listed) {
                    lines.Add("    " + ends[i].Marker.note);
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
        private static void Gather(Transform root, Dictionary<string, PieceAnnotation> byName, out Grid pieces, out Grid tiles)
        {
            pieces = new Grid();
            tiles = new Grid();
            List<MeshRenderer> renderers = new();
            List<Transform> trees = new();

            foreach (Transform child in root) {
                if (child.name != "Roofs") {
                    trees.Add(child);
                }
            }

            // The open windows placed by hand in place of wall modules, outside
            // the root: their pieces cover the neighbouring walls' ends.
            foreach (OpenWindowInterior window in OpenWindowInteriors.InSector(root.parent)) {
                trees.Add(window.transform);
            }

            foreach (Transform child in trees) {

                bool street = child.name == "Streets";
                // An open window's pieces only cover; their own ends are
                // never looked at (nor closed by the gap filler).
                bool window = child.parent != root;

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

                    byName.TryGetValue(t.name, out PieceAnnotation annotation);
                    Piece piece = new() {
                        Root = t, Path = PathUnder(t, root), Category = window ? "" : annotation?.category ?? "",
                        Front = annotation != null && annotation.fronts.Count == 1 ? annotation.fronts[0] : Vector3.zero, Min = min, Max = max, World = world
                    };
                    (street ? tiles : pieces).Add(piece);
                }

                if (street) {
                    GroundSpots(child, root, tiles);
                }
            }
        }

        /// <summary>
        /// Standing spots on the streets' ground meshes (the chunks under
        /// the "Ground ..." region objects of the streets group, not inside
        /// any placed piece): one tile-sized spot per GroundSpot-square cell
        /// of the world that holds a mesh vertex, at that vertex, so the
        /// ground mesh can be stood on like the decking tiles.
        /// </summary>
        private static void GroundSpots(Transform streets, Transform root, Grid tiles)
        {
            HashSet<(int, int)> taken = new();

            foreach (MeshFilter filter in streets.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh == null || IsInsidePiece(filter.transform, root) || filter.transform.parent == null
                    || !filter.transform.parent.name.StartsWith("Ground ")) {
                    continue;
                }

                Vector3[] vertices = filter.sharedMesh.vertices;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;

                foreach (Vector3 vertex in vertices) {
                    Vector3 at = toWorld.MultiplyPoint3x4(vertex);
                    (int, int) cell = (Mathf.FloorToInt(at.x / GroundSpot), Mathf.FloorToInt(at.z / GroundSpot));

                    if (!taken.Add(cell)) {
                        continue;
                    }

                    Vector3 half = new(GroundSpot * 0.5f, 0.01f, GroundSpot * 0.5f);
                    tiles.Add(new Piece {
                        Root = filter.transform, Path = PathUnder(filter.transform, root), Category = "ground", Front = Vector3.zero,
                        Min = -half, Max = half, World = new Bounds(at, half * 2f)
                    });
                }
            }
        }

        /// <summary>
        /// The tiles one can stand on to look at the walls: those whose
        /// middle lies in a street (within a segment's rectangle at its
        /// width; the ground tiles overrun the edges under and behind the
        /// facades) and at least StandClear from the footprint of every wall
        /// standing below eye height there (a jettied storey overhead
        /// doesn't count). With no spec, every tile clear of the walls.
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

                        // A wall overhead (a jettied storey over the street)
                        // stands nobody off: the street under it is walked.
                        if (!IsWall(wall.Category) || wall.World.min.y > tile.World.max.y + EyeHeight) {
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
            return category != "door" && category != "window" && !category.StartsWith("roof") && !category.StartsWith("pipe-") && category != "lamp-post"
                && category != "grass" && category != "nettle";
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
        private static List<FreeEnd> FreeEnds(Grid pieces, Grid tiles)
        {
            List<FreeEnd> found = new();
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
                // A sheared wall's ends slant (ShearedMeshes): each followed
                // from its foot to its head rather than its box's end.
                bool sheared = ShearedEnds(wall, along, out Vector2 foot, out Vector2 head);

                for (int end = 0; end < 2; end++) {
                    float face = end == 0 ? wall.Min[along] : wall.Max[along];
                    Vector3 axis = Vector3.zero;
                    axis[along] = end == 0 ? -1f : 1f;
                    Vector3 outward = wall.Root.TransformDirection(axis).normalized;
                    // Seen from in front a wall's trim runs down its left
                    // side, a closed post where its other end is hollow.
                    bool trimmed = Vector3.Dot(axis, Vector3.Cross(Vector3.up, wall.Front)) > 0.5f;
                    int bare = 0;
                    int covered = 0;
                    int total = 0;
                    Vector3 middle = Vector3.zero;
                    // Per row of samples up the end: how many are bare.
                    int[] rowBare = new int[_up.Length];
                    // Per row, whether its sample at the wall's back is
                    // covered: the back sample is the first across where the
                    // wall's front lies the other way.
                    bool[] backCovered = new bool[_up.Length];
                    int backSample = Vector3.Dot(wall.Front, AxisVector(across)) >= 0f ? 0 : _across.Length - 1;
                    views.Clear();

                    for (int sampleAcross = 0; sampleAcross < _across.Length; sampleAcross++) {
                        float a = _across[sampleAcross];

                        for (int row = 0; row < _up.Length; row++) {
                            Vector3 local = Vector3.zero;
                            local[along] = sheared ? Mathf.Lerp(foot[end], head[end], _up[row]) : face;
                            local[across] = Mathf.Lerp(wall.Min[across], wall.Max[across], a);
                            local.y = Mathf.Lerp(wall.Min.y, wall.Max.y, _up[row]);
                            Vector3 sample = wall.Root.TransformPoint(local) + outward * SampleOut;
                            middle += sample;
                            total++;

                            if (Covered(sample, i, pieces)) {
                                covered++;
                                backCovered[row] |= sampleAcross == backSample;
                                continue;
                            }

                            if (views.Count == 0) {
                                ViewsOf(wall.World.center, tiles, views);
                            }

                            if (Seen(sample, pieces, views)) {
                                bare++;
                                rowBare[row]++;
                            }
                        }
                    }

                    float share = (float)bare / total;
                    bool partly = false;

                    // An end that another wall meets is a corner, not a
                    // hole: at a corner the two walls run into each other
                    // (CornerOverrun), each end inside the other wall's
                    // depth, and the strip of either end left between the
                    // two posts is by design: open at the front, closed at
                    // the back. Unless the end's back stands open over its
                    // upper or lower part (a column on a higher level beside
                    // a lower one, at a flight of steps, whose front only a
                    // jettied storey or its supports touch): then those rows
                    // are judged on their own.
                    if (covered > 0) {
                        share = Mathf.Max(PartShare(backCovered, rowBare, false), PartShare(backCovered, rowBare, true));
                        partly = true;
                    }

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

                        Vector3 front = wall.Root.TransformDirection(wall.Front);
                        front.y = 0f;
                        found.Add(new FreeEnd {
                            Wall = wall.Root,
                            Centre = middle,
                            Outward = outward,
                            Front = front.sqrMagnitude > 0.0001f ? front.normalized : Vector3.Cross(Vector3.up, outward),
                            Bottom = wall.Root.TransformPoint(new Vector3(0f, wall.Min.y, 0f)).y,
                            Top = wall.Root.TransformPoint(new Vector3(0f, wall.Max.y, 0f)).y,
                            Thickness = (wall.Max[across] - wall.Min[across]) * Mathf.Abs(wall.Root.lossyScale[across]),
                            Share = share,
                            Marker = new GeneratedRoot.Marker {
                                kind = GeneratedRoot.MarkerKind.FreeEnd,
                                position = middle,
                                size = faceSize,
                                note = $"Free end: {wall.Path}, {(end == 0 ? "first" : "second")} end at ({middle.x:0.0}, {middle.y:0.0}, {middle.z:0.0}), "
                                    + $"{share:P0} bare{(partly ? " where no wall meets it" : "")}{seenFrom}."
                            }
                        });
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// For a wall wearing a sheared mesh (ShearedMeshes: a side wall
        /// following a leaning facade), where its two ends stand along its
        /// width at its foot and at its head, in its root's space (x the
        /// first end, y the second): the furthest its vertices reach each way
        /// within EndBand of its bottom, and of its top. Its box's end is the
        /// furthest either reaches, so sampled there the rest of a slanting
        /// end would hang in the air. False (nothing set) for an unsheared
        /// wall.
        /// </summary>
        private static bool ShearedEnds(Piece wall, int along, out Vector2 foot, out Vector2 head)
        {
            foot = new Vector2(float.PositiveInfinity, float.NegativeInfinity);
            head = foot;
            bool sheared = false;
            List<MeshFilter> filters = new();
            wall.Root.GetComponentsInChildren(true, filters);
            bool lods = filters.Exists(f => f.name.EndsWith("_LOD0"));

            foreach (MeshFilter filter in filters) {
                if (lods && !filter.name.EndsWith("_LOD0") || filter.sharedMesh == null) {
                    continue;
                }

                sheared |= ShearedMeshes.IsSheared(filter.sharedMesh);

                foreach (Vector3 vertex in filter.sharedMesh.vertices) {
                    Vector3 local = wall.Root.InverseTransformPoint(filter.transform.TransformPoint(vertex));

                    if (local.y <= wall.Min.y + EndBand) {
                        foot = new Vector2(Mathf.Min(foot.x, local[along]), Mathf.Max(foot.y, local[along]));
                    }

                    if (local.y >= wall.Max.y - EndBand) {
                        head = new Vector2(Mathf.Min(head.x, local[along]), Mathf.Max(head.y, local[along]));
                    }
                }
            }

            return sheared && !float.IsInfinity(foot.x) && !float.IsInfinity(head.x);
        }

        /// <summary>
        /// One of the three axes as a unit vector.
        /// </summary>
        private static Vector3 AxisVector(int axis)
        {
            Vector3 vector = Vector3.zero;
            vector[axis] = 1f;
            return vector;
        }

        /// <summary>
        /// The bare share of the rows of an end's samples at its top (or
        /// bottom) whose back sample touches nothing, counted in from that
        /// edge until a row whose back is covered: 0 where fewer than
        /// PartRows rows are left.
        /// </summary>
        private static float PartShare(bool[] backCovered, int[] rowBare, bool fromTop)
        {
            int rows = 0;
            int bare = 0;

            for (int k = 0; k < backCovered.Length; k++) {
                int row = fromTop ? backCovered.Length - 1 - k : k;

                if (backCovered[row]) {
                    break;
                }

                rows++;
                bare += rowBare[row];
            }

            return rows >= PartRows ? (float)bare / (rows * _across.Length) : 0f;
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
