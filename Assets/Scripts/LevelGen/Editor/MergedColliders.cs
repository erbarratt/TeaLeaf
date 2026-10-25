using System;
using System.Collections.Generic;
using Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LevelGen
{
    /// <summary>
    /// Replaces the box colliders of a set of placed pieces with as few boxes
    /// as possible (LevelGenerator): the foundations the facades, corners,
    /// lots and dead ends placed (RunFoundations) and the walkways along the
    /// facades (RunWalkways), once everything is built; the walls of every
    /// storey (RunWalls) and the roofs' slope panels (RunRoofs), after the
    /// visibility cull and the roof edge pass, so nothing is boxed that was
    /// taken out. The pieces' box colliders are taken as boxes in the world,
    /// sorted into frames (the same three axes, each either way, the same
    /// layer and surface), and within a frame two boxes become one wherever
    /// the box round both stands out past them by no more than the
    /// tolerance: one holds the other, or they match on two axes and meet or
    /// overlap on the third. Merging repeats until no two boxes in a frame
    /// can merge. The new box always covers both, so no gap opens; it reaches
    /// past them by at most the tolerance. A frame is any turn: a roof
    /// panel's lies along its slope.
    ///
    /// The pieces' own colliders are removed from the placed instances (an
    /// override, the prefabs untouched). The boxes go under a child of the
    /// generated root named for the set ("Foundation Colliders", "Walkway
    /// Colliders", "Wall Colliders", "Roof Colliders"), on the pieces' layer
    /// with their static flags and a copy of their SurfaceTag if they have
    /// one. The foundations, walkways and walls take upright boxes only: a
    /// collider not upright (a walkway's tilted brace, a door surround's
    /// arch), or not a box, is left where it is.
    /// </summary>
    internal static class MergedColliders
    {
        // How close two directions (degrees) must be to count as the same
        // axis of a frame: the pieces of one run are turned exactly alike,
        // so only rounding.
        private const float SameTurn = 0.05f;
        private static readonly float _sameAxis = Mathf.Cos(SameTurn * Mathf.Deg2Rad);
        // The name FacadeDressing gives a building's walkway group.
        private const string WalkwayGroup = "Walkway";
        // The names FacadeBuilder gives a building's storey groups
        // ("Storey <s>") and its corner's walls ("Corner").
        private const string StoreyGroupPrefix = "Storey ";
        private const string CornerGroup = "Corner";
        // The annotation categories of the roofs' slope panels, laid in rows
        // by FacadeRoofs: not the gables, hip pieces, chimneys, dormers or
        // railings.
        private static readonly string[] _panelCategories = { "roof-middle", "roof-end", "roof-end-half", "roof-ridge-end", "roof-ridge-end-half" };
        private static readonly string[] _foundationCategories = { "foundation", "foundation-corner" };

        /// <summary>
        /// One box in a frame: its extent along the frame's three axes, and
        /// the collider it copies its layer, flags and tag from.
        /// </summary>
        private class Box
        {
            public Vector3 Min;
            public Vector3 Max;
            public GameObject Source;
        }

        /// <summary>
        /// The boxes of one frame: its three axes (a collider's right, up
        /// and forward, orthonormal), and what every box in it shares.
        /// </summary>
        private class Frame
        {
            public Vector3 AxisX;
            public Vector3 AxisY;
            public Vector3 AxisZ;
            public int Layer;
            public SurfaceTag Tag;
            public readonly List<Box> Boxes = new();
        }

        /// <summary>
        /// Merges the colliders of the foundation pieces (annotation category
        /// foundation or foundation-corner) placed anywhere under root. Adds
        /// one line to log.
        /// </summary>
        internal static void RunFoundations(Transform root, PieceAnnotations annotations, GeneratorSettings settings, List<string> log)
        {
            Run(root, PiecesOfCategories(root, annotations, _foundationCategories), "Foundation", settings.FoundationColliderTolerance, SurfaceType.Stone, true, log);
        }

        /// <summary>
        /// Merges the colliders of the walkway pieces (every piece under a
        /// building's "Walkway" group, FacadeDressing.BuildWalkways) placed
        /// anywhere under root: their flat decks become one box per run,
        /// their tilted braces stay as they are. Adds one line to log.
        /// </summary>
        internal static void RunWalkways(Transform root, GeneratorSettings settings, List<string> log)
        {
            Run(root, PiecesUnderGroups(root, group => group.name == WalkwayGroup), "Walkway", settings.FoundationColliderTolerance, SurfaceType.Wood, true, log);
        }

        /// <summary>
        /// Merges the colliders of the walls (every piece under a building's
        /// "Storey n" and "Corner" groups, FacadeBuilder: walls, door and
        /// window walls, and the windows and doors set in them) placed
        /// anywhere under root: a storey's upright boxes become one box per
        /// run, storeys with their fronts in line one box together; a door
        /// surround's slanted boxes stay as they are. A group in unmerged (a
        /// storey an open window stands in, OpenWindowInteriors.CutWalls)
        /// keeps its pieces' own colliders, so no box closes the window.
        /// Adds one line to log.
        /// </summary>
        internal static void RunWalls(Transform root, HashSet<Transform> unmerged, GeneratorSettings settings, List<string> log)
        {
            Run(root, PiecesUnderGroups(root, group => IsWallGroup(group.name) && !unmerged.Contains(group)),
                "Wall", settings.WallColliderTolerance, SurfaceType.Stone, true, log);
        }

        /// <summary>
        /// Whether a group's name is one of a building's wall groups ("Storey
        /// n" or "Corner") whose pieces RunWalls merges.
        /// </summary>
        internal static bool IsWallGroup(string name)
        {
            return name == CornerGroup || name.StartsWith(StoreyGroupPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Merges the colliders of the roofs' slope panels (annotation
        /// categories roof-middle, roof-end and roof-ridge-end, and their
        /// halves; FacadeRoofs lays them in rows up each slope) placed
        /// anywhere under root: a row's tilted boxes become one box per run,
        /// and the ridge row and the eave row one box together where their
        /// ends line up. Adds one line to log.
        /// </summary>
        internal static void RunRoofs(Transform root, PieceAnnotations annotations, GeneratorSettings settings, List<string> log)
        {
            Run(root, PiecesOfCategories(root, annotations, _panelCategories), "Roof", settings.RoofColliderTolerance, SurfaceType.Tile, false, log);
        }

        /// <summary>
        /// Every placed piece under root (an outermost prefab instance) whose
        /// annotation has one of the categories.
        /// </summary>
        private static List<GameObject> PiecesOfCategories(Transform root, PieceAnnotations annotations, string[] categories)
        {
            HashSet<string> guids = new();

            foreach (PieceAnnotation piece in annotations.pieces) {
                if (Array.IndexOf(categories, piece.category) >= 0) {
                    guids.Add(piece.guid);
                }
            }

            List<GameObject> pieces = new();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) {
                    continue;
                }

                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);

                if (guids.Contains(AssetDatabase.AssetPathToGUID(path))) {
                    pieces.Add(t.gameObject);
                }
            }

            return pieces;
        }

        /// <summary>
        /// Every placed piece (an outermost prefab instance) under any group
        /// below root whose name the test accepts; each piece once.
        /// </summary>
        private static List<GameObject> PiecesUnderGroups(Transform root, Func<Transform, bool> isGroup)
        {
            List<GameObject> pieces = new();
            HashSet<GameObject> seen = new();

            foreach (Transform group in root.GetComponentsInChildren<Transform>(true)) {
                if (!isGroup(group) || PrefabUtility.IsPartOfPrefabInstance(group)) {
                    continue;
                }

                foreach (Transform t in group.GetComponentsInChildren<Transform>(true)) {
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) && seen.Add(t.gameObject)) {
                        pieces.Add(t.gameObject);
                    }
                }
            }

            return pieces;
        }

        /// <summary>
        /// Replaces the box colliders of the pieces (only the upright ones,
        /// if uprightOnly) with merged boxes under "&lt;what&gt; Colliders"
        /// in root, each named "&lt;what&gt; &lt;n&gt;", tagged with the
        /// surface the pieces are (fallback where a piece has no tag). Adds
        /// one line to log.
        /// </summary>
        private static void Run(Transform root, List<GameObject> pieces, string what, float tolerance, SurfaceType fallback, bool uprightOnly, List<string> log)
        {
            List<Frame> frames = new();
            int removed = 0;

            foreach (GameObject piece in pieces) {
                foreach (BoxCollider collider in piece.GetComponentsInChildren<BoxCollider>(true)) {
                    if (!collider.enabled || collider.isTrigger || (uprightOnly && Vector3.Dot(collider.transform.up, Vector3.up) < 0.9999f)) {
                        continue;
                    }

                    Frame frame = FrameFor(frames, collider);
                    frame.Boxes.Add(BoxIn(frame, collider));
                    Object.DestroyImmediate(collider);
                    removed++;
                }
            }

            Transform parent = null;
            int made = 0;

            foreach (Frame frame in frames) {
                Merge(frame.Boxes, tolerance);

                foreach (Box box in frame.Boxes) {
                    if (parent == null) {
                        parent = new GameObject($"{what} Colliders").transform;
                        parent.SetParent(root, false);
                    }

                    MakeBox(frame, box, parent, $"{what} {made}", fallback);
                    made++;
                }
            }

            log.Add($"{what} colliders: {removed} collider(s) on {pieces.Count} piece(s) replaced by {made} box(es), tolerance {tolerance}m.");
        }

        /// <summary>
        /// The frame a collider's box belongs to: one already made whose
        /// axes its own lie along (each either way), with its layer and
        /// surface, else a new one on its own axes.
        /// </summary>
        private static Frame FrameFor(List<Frame> frames, BoxCollider collider)
        {
            Transform t = collider.transform;
            int layer = collider.gameObject.layer;
            SurfaceTag tag = SurfaceTag.Find(collider);

            foreach (Frame frame in frames) {
                if (frame.Layer == layer && SameSurface(frame.Tag, tag) && Along(frame, t.right) && Along(frame, t.up) && Along(frame, t.forward)) {
                    return frame;
                }
            }

            Frame made = new() {
                AxisX = t.right,
                AxisY = t.up,
                AxisZ = t.forward,
                Layer = layer,
                Tag = tag
            };
            frames.Add(made);
            return made;
        }

        /// <summary>
        /// Whether a direction lies along one of the frame's axes, either
        /// way, to SameTurn.
        /// </summary>
        private static bool Along(Frame frame, Vector3 direction)
        {
            return Mathf.Abs(Vector3.Dot(direction, frame.AxisX)) >= _sameAxis
                || Mathf.Abs(Vector3.Dot(direction, frame.AxisY)) >= _sameAxis
                || Mathf.Abs(Vector3.Dot(direction, frame.AxisZ)) >= _sameAxis;
        }

        /// <summary>
        /// A box collider as a box in a frame: the extent of its eight
        /// corners in the world along the frame's axes.
        /// </summary>
        private static Box BoxIn(Frame frame, BoxCollider collider)
        {
            Vector3 half = collider.size * 0.5f;
            Box box = new() {
                Min = Vector3.positiveInfinity,
                Max = Vector3.negativeInfinity,
                Source = collider.gameObject
            };

            for (int i = 0; i < 8; i++) {
                Vector3 sign = new((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 corner = collider.transform.TransformPoint(collider.center + Vector3.Scale(half, sign));
                Vector3 inFrame = new(Vector3.Dot(corner, frame.AxisX), Vector3.Dot(corner, frame.AxisY), Vector3.Dot(corner, frame.AxisZ));
                box.Min = Vector3.Min(box.Min, inFrame);
                box.Max = Vector3.Max(box.Max, inFrame);
            }

            return box;
        }

        /// <summary>
        /// Merges a frame's boxes two at a time, each into the box round
        /// both, until no two can merge.
        /// </summary>
        private static void Merge(List<Box> boxes, float tolerance)
        {
            bool merged = true;

            while (merged) {
                merged = false;

                for (int i = 0; i < boxes.Count; i++) {
                    for (int j = boxes.Count - 1; j > i; j--) {
                        if (!CanMerge(boxes[i], boxes[j], tolerance)) {
                            continue;
                        }

                        boxes[i].Min = Vector3.Min(boxes[i].Min, boxes[j].Min);
                        boxes[i].Max = Vector3.Max(boxes[i].Max, boxes[j].Max);
                        boxes.RemoveAt(j);
                        merged = true;
                    }
                }
            }
        }

        /// <summary>
        /// Whether the box round two boxes stands out past them by no more
        /// than the tolerance anywhere: one holds the other, or they match
        /// on two axes and meet (or overlap) on the third.
        /// </summary>
        private static bool CanMerge(Box a, Box b, float tolerance)
        {
            if (Holds(a, b, tolerance) || Holds(b, a, tolerance)) {
                return true;
            }

            int matching = 0;
            bool meet = true;

            for (int axis = 0; axis < 3; axis++) {
                if (Mathf.Abs(a.Min[axis] - b.Min[axis]) <= tolerance && Mathf.Abs(a.Max[axis] - b.Max[axis]) <= tolerance) {
                    matching++;
                } else if (a.Min[axis] > b.Max[axis] + tolerance || b.Min[axis] > a.Max[axis] + tolerance) {
                    meet = false;
                }
            }

            return meet && matching >= 2;
        }

        /// <summary>
        /// Whether box a holds box b, to the tolerance.
        /// </summary>
        private static bool Holds(Box a, Box b, float tolerance)
        {
            for (int axis = 0; axis < 3; axis++) {
                if (b.Min[axis] < a.Min[axis] - tolerance || b.Max[axis] > a.Max[axis] + tolerance) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Makes one merged box's collider under parent, turned to its frame,
        /// on its source's layer with its static flags, and a SurfaceTag: the
        /// source's copied, else one set to the fallback (Stone for the
        /// foundations and walls, Wood for the walkways, Tile for the roofs).
        /// </summary>
        private static void MakeBox(Frame frame, Box box, Transform parent, string name, SurfaceType fallback)
        {
            Vector3 middle = (box.Min + box.Max) * 0.5f;
            GameObject made = new(name) {
                layer = frame.Layer
            };
            GameObjectUtility.SetStaticEditorFlags(made, GameObjectUtility.GetStaticEditorFlags(box.Source));
            made.transform.SetParent(parent, false);
            made.transform.SetPositionAndRotation(frame.AxisX * middle.x + frame.AxisY * middle.y + frame.AxisZ * middle.z,
                Quaternion.LookRotation(frame.AxisZ, frame.AxisY));
            made.AddComponent<BoxCollider>().size = box.Max - box.Min;

            // What it is made of: the source's tag copied, else the fallback
            // for this kind of piece made explicit.
            SurfaceTag tag = made.AddComponent<SurfaceTag>();

            if (frame.Tag != null) {
                EditorUtility.CopySerialized(frame.Tag, tag);
            } else {
                SerializedObject serialized = new(tag);
                serialized.FindProperty("surface").enumValueIndex = (int)fallback;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Whether two colliders' tags make the same surface (no tag on
        /// either is the same).
        /// </summary>
        private static bool SameSurface(SurfaceTag a, SurfaceTag b)
        {
            if (a == null || b == null) {
                return a == b;
            }

            return a.Surface == b.Surface && a.SupportsRopeBolt == b.SupportsRopeBolt;
        }
    }
}
