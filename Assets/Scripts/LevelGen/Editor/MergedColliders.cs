using System.Collections.Generic;
using Core;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Replaces the box colliders of a set of placed pieces with as few boxes
    /// as possible, once everything is built (LevelGenerator): the
    /// foundations the facades, corners, lots and dead ends placed
    /// (RunFoundations), and the walkways along the facades (RunWalkways).
    /// The pieces' upright box colliders are taken as boxes in the world,
    /// sorted into frames (the same turn about the world's up, to a quarter
    /// turn, the same layer and surface), and within a frame two boxes become
    /// one wherever the box round both stands out past them by no more than
    /// the tolerance: one holds the other, or they match on two axes and meet
    /// or overlap on the third. Merging repeats until no two boxes in a frame
    /// can merge. The new box always covers both, so no gap opens; it reaches
    /// past them by at most the tolerance.
    ///
    /// The pieces' own colliders are removed from the placed instances (an
    /// override, the prefabs untouched). The boxes go under a child of the
    /// generated root named for the set ("Foundation Colliders", "Walkway
    /// Colliders"), on the pieces' layer with their static flags and a copy
    /// of their SurfaceTag if they have one. A collider not upright (a
    /// walkway's tilted brace, say), or not a box, is left where it is.
    /// </summary>
    internal static class MergedColliders
    {
        // How close two turns (degrees, to a quarter turn) must be to share a
        // frame: the pieces of one run are turned exactly alike, so only
        // rounding.
        private const float SameTurn = 0.05f;
        // The name FacadeDressing gives a building's walkway group.
        private const string WalkwayGroup = "Walkway";

        /// <summary>
        /// One box in a frame: its extent along the frame's X, up and along
        /// its Z, and the collider it copies its layer, flags and tag from.
        /// </summary>
        private class Box
        {
            public Vector3 Min;
            public Vector3 Max;
            public GameObject Source;
        }

        /// <summary>
        /// The boxes of one frame: its turn about the world's up, its axes
        /// on the ground, and what every box in it shares.
        /// </summary>
        private class Frame
        {
            public float Turn;
            public Vector3 AxisX;
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
            HashSet<string> foundations = new();

            foreach (PieceAnnotation piece in annotations.pieces) {
                if (piece.category == "foundation" || piece.category == "foundation-corner") {
                    foundations.Add(piece.guid);
                }
            }

            List<GameObject> pieces = new();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) {
                    continue;
                }

                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);

                if (foundations.Contains(AssetDatabase.AssetPathToGUID(path))) {
                    pieces.Add(t.gameObject);
                }
            }

            Run(root, pieces, "Foundation", settings.FoundationColliderTolerance, SurfaceType.Stone, log);
        }

        /// <summary>
        /// Merges the colliders of the walkway pieces (every piece under a
        /// building's "Walkway" group, FacadeDressing.BuildWalkways) placed
        /// anywhere under root: their flat decks become one box per run,
        /// their tilted braces stay as they are. Adds one line to log.
        /// </summary>
        internal static void RunWalkways(Transform root, GeneratorSettings settings, List<string> log)
        {
            List<GameObject> pieces = new();

            foreach (Transform group in root.GetComponentsInChildren<Transform>(true)) {
                if (group.name != WalkwayGroup) {
                    continue;
                }

                foreach (Transform t in group.GetComponentsInChildren<Transform>(true)) {
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) {
                        pieces.Add(t.gameObject);
                    }
                }
            }

            Run(root, pieces, "Walkway", settings.FoundationColliderTolerance, SurfaceType.Wood, log);
        }

        /// <summary>
        /// Replaces the upright box colliders of the pieces with merged boxes
        /// under "&lt;what&gt; Colliders" in root, each named "&lt;what&gt; &lt;n&gt;",
        /// tagged with the surface the pieces are (fallback where a piece has no tag).
        /// Adds one line to log.
        /// </summary>
        private static void Run(Transform root, List<GameObject> pieces, string what, float tolerance, SurfaceType fallback, List<string> log)
        {
            List<Frame> frames = new();
            int removed = 0;

            foreach (GameObject piece in pieces) {
                foreach (BoxCollider collider in piece.GetComponentsInChildren<BoxCollider>(true)) {
                    if (!collider.enabled || collider.isTrigger || Vector3.Dot(collider.transform.up, Vector3.up) < 0.9999f) {
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
        /// The frame a collider's box belongs to: one already made with its
        /// turn (to a quarter turn), layer and surface, else a new one turned
        /// as it is.
        /// </summary>
        private static Frame FrameFor(List<Frame> frames, BoxCollider collider)
        {
            Vector3 right = collider.transform.right;
            float turn = Mathf.Repeat(Mathf.Atan2(right.z, right.x) * Mathf.Rad2Deg, 90f);
            int layer = collider.gameObject.layer;
            SurfaceTag tag = SurfaceTag.Find(collider);

            foreach (Frame frame in frames) {
                float apart = Mathf.Abs(turn - frame.Turn);

                if (Mathf.Min(apart, 90f - apart) <= SameTurn && frame.Layer == layer && SameSurface(frame.Tag, tag)) {
                    return frame;
                }
            }

            float radians = turn * Mathf.Deg2Rad;
            Frame made = new() {
                Turn = turn,
                AxisX = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)),
                Layer = layer,
                Tag = tag
            };
            made.AxisZ = Vector3.Cross(made.AxisX, Vector3.up);
            frames.Add(made);
            return made;
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
                Vector3 inFrame = new(Vector3.Dot(corner, frame.AxisX), corner.y, Vector3.Dot(corner, frame.AxisZ));
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
        /// foundations, Wood for the walkways).
        /// </summary>
        private static void MakeBox(Frame frame, Box box, Transform parent, string name, SurfaceType fallback)
        {
            Vector3 middle = (box.Min + box.Max) * 0.5f;
            GameObject made = new(name) {
                layer = frame.Layer
            };
            GameObjectUtility.SetStaticEditorFlags(made, GameObjectUtility.GetStaticEditorFlags(box.Source));
            made.transform.SetParent(parent, false);
            made.transform.SetPositionAndRotation(frame.AxisX * middle.x + Vector3.up * middle.y + frame.AxisZ * middle.z,
                Quaternion.LookRotation(frame.AxisZ, Vector3.up));
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
