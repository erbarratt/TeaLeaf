using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace LevelGen
{
    /// <summary>
    /// The NavMesh the guards walk on in a generated level, bound to its
    /// streets. A NavMeshSurface on the Generated root collects its own
    /// children's Environment colliders, everything Walkable, and
    /// NavMeshModifierVolume boxes marked Not Walkable carve away all that
    /// isn't street: a box outside each edge of every street segment (the
    /// floor the ground lays under the buildings, their foundations,
    /// walkways and roofs), a box across each dead end, and boxes over the
    /// street itself from head height up (walkways overhanging it). An edge
    /// box ends where its edge meets the edge of the next street round the
    /// point: short of the point on the inside of a corner, past it on the
    /// outside, where it runs on further still, so the floor under the
    /// corner building is covered too; it never reaches into another
    /// street. A Walkable volume can't do it the other way round (Unity
    /// marks Not Walkable geometry as nothing, which no volume brings back),
    /// so the floor stays Walkable and the rest is taken away. The agent's
    /// own radius keeps the baked edge off the walls. The baked data is
    /// saved beside the scene, one asset per sector.
    /// </summary>
    internal static class StreetNavMesh
    {
        private const string GroupName = "NavMesh Volumes";
        private const int WalkableArea = 0;
        private const int NotWalkableArea = 1;
        // How far outside a street's edge the carve reaches: further than
        // the ground's tiles reach under the buildings (a turned 3m tile's
        // diagonal at a corner; a slope's or a landing's row, whole tiles
        // wide, up to 4.8m past the edge). Two streets' edges nearer than
        // this, back to back with no street between, would have the floor
        // along the other's edge carved.
        private const float EdgeReach = 6f;
        // A carve reaches this far under the street's lowest surface and
        // this far above its highest: the buildings' full height.
        private const float Below = 1f;
        private const float Tall = 30f;
        // A segment is measured in stretches this long; neighbouring
        // stretches at the same heights merge into one box over the street,
        // so a level segment gets a single one.
        private const float StretchLength = 2f;
        private const float SampleStep = 0.25f;
        private const float SameHeight = 0.01f;
        // The bake's voxel, half the agent's default (its radius / 3, 0.1m):
        // at 0.1m the polygons over a flight of steps bake as long slivers
        // standing up to 0.6m off the treads and the street below. With the
        // height mesh as well (agents placed on the ground's real height, not
        // the polygons'), the surface keeps within a few centimetres of the
        // ground. Both together bake in a fraction of a second.
        private const float VoxelSize = 0.05f;

        /// <summary>
        /// One street segment on the ground plane, with how high its
        /// surface comes.
        /// </summary>
        private class Segment
        {
            public StreetSpec Street;
            public int Index;
            public Vector2 Start;
            public Vector2 End;
            public Vector2 Along;
            public float Length;
            public float HalfWidth;
            public float Low;
            public float High;
        }

        /// <summary>
        /// Lays the volumes, bakes the NavMesh and saves its data. One log line.
        /// </summary>
        internal static void Build(LayoutSpec spec, StreetHeights heights, GeneratorSettings settings, GameObject root, List<string> log)
        {
            int layer = LayerMask.NameToLayer("Environment");
            Transform group = new GameObject(GroupName).transform;
            group.gameObject.layer = layer;
            group.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Generate");

            List<Segment> segments = Gather(spec, heights);
            int boxes = 0;

            foreach (Segment segment in segments) {
                boxes += LayEdges(segment, segments, settings, group, layer);
                boxes += LayOverhead(segment, segments, heights, settings, group, layer);
            }

            NavMeshSurface surface = Undo.AddComponent<NavMeshSurface>(root);
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            // The volumes must be on a collected layer too, or the bake
            // leaves them out.
            surface.layerMask = 1 << layer;
            surface.defaultArea = WalkableArea;
            surface.overrideVoxelSize = true;
            surface.voxelSize = VoxelSize;
            surface.buildHeightMesh = true;

            // Physics must know where everything is before its colliders
            // are collected.
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            NavMeshData data = surface.navMeshData;
            string laid = $"NavMesh: {boxes} volume(s) carving round {segments.Count} segment(s)";

            if (data == null) {
                log.Add($"{laid}; the bake made nothing.");
                return;
            }

            string path = DataPath(root.scene, spec.sector);

            if (path == null) {
                log.Add($"{laid}; baked, but not saved: the scene has no path yet. Save the scene and generate again.");
                return;
            }

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            Bounds bounds = data.sourceBounds;
            log.Add($"{laid}; baked {bounds.size.x:0} x {bounds.size.z:0}m, saved to {path}.");
        }

        /// <summary>
        /// Every segment of every street, with the lowest and highest its
        /// surface comes along it.
        /// </summary>
        private static List<Segment> Gather(LayoutSpec spec, StreetHeights heights)
        {
            List<Segment> segments = new();

            foreach (StreetSpec street in spec.streets) {
                for (int i = 0; i + 1 < street.points.Count; i++) {
                    Vector2 start = Ground(street.points[i]);
                    Vector2 way = Ground(street.points[i + 1]) - start;
                    float length = way.magnitude;

                    if (length < 1e-3f) {
                        continue;
                    }

                    Span(street, i, heights, 0f, length, out float low, out float high);
                    segments.Add(new Segment {
                        Street = street, Index = i, Start = start, End = start + way, Along = way / length, Length = length,
                        HalfWidth = street.SegmentWidth(i) * 0.5f, Low = low, High = high
                    });
                }
            }

            return segments;
        }

        /// <summary>
        /// The box outside each edge of a segment, and the box across its
        /// end where it is a dead end. Returns how many were laid.
        /// </summary>
        private static int LayEdges(Segment segment, List<Segment> all, GeneratorSettings settings, Transform group, int layer)
        {
            string name = $"{segment.Street.id} {segment.Index + 1}-{segment.Index + 2}";
            Quaternion turn = Quaternion.LookRotation(new Vector3(segment.Along.x, 0f, segment.Along.y), Vector3.up);
            float bottom = segment.Low - Below;
            float top = segment.High + Tall;
            float middleY = (bottom + top) * 0.5f;
            List<(Vector2 way, float halfWidth)> atStart = OthersAt(segment.Start, segment, all, settings);
            List<(Vector2 way, float halfWidth)> atEnd = OthersAt(segment.End, segment, all, settings);
            int laid = 0;

            foreach (bool left in new[] { true, false }) {
                Vector2 side = left ? Left(segment.Along) : Right(segment.Along);
                // At the start the segment is seen arriving the other way,
                // so the box runs back from the start by the reach there.
                float from = -CornerReach(segment, false, side, atStart, settings);
                float to = segment.Length + CornerReach(segment, true, side, atEnd, settings);

                if (to - from < 1e-3f) {
                    continue;
                }

                Vector2 middle = segment.Start + segment.Along * ((from + to) * 0.5f) + side * (segment.HalfWidth + EdgeReach * 0.5f);
                Place(group, layer, $"{name} {(left ? "left" : "right")}", new Vector3(middle.x, middleY, middle.y), turn,
                    new Vector3(EdgeReach, top - bottom, to - from));
                laid++;
            }

            // A dead end: a box across the street beyond its end, the edge
            // boxes already running on past it.
            foreach (bool atTheStart in new[] { true, false }) {
                if ((atTheStart ? atStart : atEnd).Count > 0) {
                    continue;
                }

                Vector2 point = atTheStart ? segment.Start : segment.End;
                Vector2 outward = atTheStart ? -segment.Along : segment.Along;
                Vector2 middle = point + outward * (EdgeReach * 0.5f);
                Place(group, layer, $"{name} {(atTheStart ? "start" : "end")}", new Vector3(middle.x, middleY, middle.y), turn,
                    new Vector3(segment.HalfWidth * 2f + EdgeReach * 2f, top - bottom, EdgeReach));
                laid++;
            }

            return laid;
        }

        /// <summary>
        /// The boxes over a segment from head height up: one per stretch
        /// of the surface's height profile, each from the street's headroom
        /// above the lowest surface under it, the first and last running on
        /// past a shared point by the widest street's half width there (the
        /// streets meet level, so nothing walkable is that high). Returns
        /// how many were laid.
        /// </summary>
        private static int LayOverhead(Segment segment, List<Segment> all, StreetHeights heights, GeneratorSettings settings, Transform group,
            int layer)
        {
            string name = $"{segment.Street.id} {segment.Index + 1}-{segment.Index + 2} over";
            Quaternion turn = Quaternion.LookRotation(new Vector3(segment.Along.x, 0f, segment.Along.y), Vector3.up);
            float startExtension = JunctionHalfWidth(segment.Start, segment, all, settings);
            float endExtension = JunctionHalfWidth(segment.End, segment, all, settings);

            // The surface is measured stretch by stretch; stretches at the
            // same lowest and highest surface are one box.
            int stretches = Mathf.Max(1, Mathf.RoundToInt(segment.Length / StretchLength));
            float stretch = segment.Length / stretches;
            List<(float from, float to, float low, float high)> runs = new();

            for (int s = 0; s < stretches; s++) {
                float from = s * stretch;
                float to = s == stretches - 1 ? segment.Length : from + stretch;
                Span(segment.Street, segment.Index, heights, from, to, out float low, out float high);

                if (runs.Count > 0) {
                    (float runFrom, _, float runLow, float runHigh) = runs[^1];

                    if (Mathf.Abs(low - runLow) <= SameHeight && Mathf.Abs(high - runHigh) <= SameHeight) {
                        runs[^1] = (runFrom, to, runLow, runHigh);
                        continue;
                    }
                }

                runs.Add((from, to, low, high));
            }

            for (int r = 0; r < runs.Count; r++) {
                (float from, float to, float low, float high) = runs[r];

                if (r == 0) {
                    from -= startExtension;
                }

                if (r == runs.Count - 1) {
                    to += endExtension;
                }

                float bottom = low + settings.StreetHeadroom;
                float top = high + Tall;
                Vector2 middle = segment.Start + segment.Along * ((from + to) * 0.5f);
                Place(group, layer, runs.Count > 1 ? $"{name} ({r + 1})" : name, new Vector3(middle.x, (bottom + top) * 0.5f, middle.y), turn,
                    new Vector3(segment.HalfWidth * 2f, top - bottom, to - from));
            }

            return runs.Count;
        }

        /// <summary>
        /// A Not Walkable volume box.
        /// </summary>
        private static void Place(Transform group, int layer, string name, Vector3 centre, Quaternion turn, Vector3 size)
        {
            GameObject box = new(name);
            box.layer = layer;
            box.transform.SetParent(group, false);
            box.transform.SetPositionAndRotation(centre, turn);
            NavMeshModifierVolume volume = box.AddComponent<NavMeshModifierVolume>();
            volume.center = Vector3.zero;
            volume.size = size;
            volume.area = NotWalkableArea;
        }

        /// <summary>
        /// The other segments meeting a segment at a point: the way each
        /// leaves the point, and its half width. Empty at a dead end.
        /// </summary>
        private static List<(Vector2 way, float halfWidth)> OthersAt(Vector2 at, Segment self, List<Segment> all, GeneratorSettings settings)
        {
            List<(Vector2, float)> others = new();

            foreach (Segment other in all) {
                if (other == self) {
                    continue;
                }

                if ((other.Start - at).magnitude <= settings.JunctionDistance) {
                    others.Add((other.Along, other.HalfWidth));
                } else if ((other.End - at).magnitude <= settings.JunctionDistance) {
                    others.Add((-other.Along, other.HalfWidth));
                }
            }

            return others;
        }

        /// <summary>
        /// The half width of the widest segment at a point, this one
        /// included; 0 at a dead end.
        /// </summary>
        private static float JunctionHalfWidth(Vector2 at, Segment self, List<Segment> all, GeneratorSettings settings)
        {
            List<(Vector2 way, float halfWidth)> others = OthersAt(at, self, all, settings);

            if (others.Count == 0) {
                return 0f;
            }

            float widest = self.HalfWidth;

            foreach ((_, float halfWidth) in others) {
                widest = Mathf.Max(widest, halfWidth);
            }

            return widest;
        }

        /// <summary>
        /// How far a segment's edge box on one side runs on past one of its
        /// points (negative: stops short of it). The edge meets the edge of
        /// the next street round the point on that side - the first met
        /// turning from the way out of the point towards the side - where
        /// the two edge lines cross: short of the point on the inside of a
        /// corner, where the box stops (beyond it the other street begins);
        /// past the point on the outside, where it runs on by the reach as
        /// well, covering the floor laid under the corner building; at the
        /// point where the next street carries straight on within the
        /// branch angle (the two boxes meet there with no wedge between
        /// them); and by the reach at a dead end.
        /// </summary>
        private static float CornerReach(Segment self, bool atEnd, Vector2 side, List<(Vector2 way, float halfWidth)> others,
            GeneratorSettings settings)
        {
            if (others.Count == 0) {
                return EdgeReach;
            }

            // The way this segment travels into the point, and out of it
            // again; seen arriving at its start, its left is on the right.
            Vector2 arriving = atEnd ? self.Along : -self.Along;
            Vector2 leaving = -arriving;
            bool sideIsLeft = Cross(arriving, side) > 0f;
            Vector2 otherWay = Vector2.zero;
            float otherHalfWidth = 0f;
            float bestTurn = float.MaxValue;

            // The edge on the left of the arriving direction lies clockwise
            // from the way back out, so its neighbour is the first street
            // clockwise from there; on the right, the first anticlockwise.
            foreach ((Vector2 way, float halfWidth) in others) {
                float signed = Vector2.SignedAngle(leaving, way);
                float turn = sideIsLeft ? (signed <= 0f ? -signed : 360f - signed) : (signed >= 0f ? signed : 360f + signed);

                if (turn < bestTurn) {
                    bestTurn = turn;
                    otherWay = way;
                    otherHalfWidth = halfWidth;
                }
            }

            float sin = Mathf.Abs(Cross(arriving, otherWay));

            if (sin < settings.BranchDot) {
                return 0f;
            }

            // This edge: the point moved out by the half width, along the
            // arriving direction; the other's: moved out by its half width
            // on the same side of its own way, along it. Where they cross,
            // measured along the arriving direction from the point.
            Vector2 otherSide = sideIsLeft ? Left(otherWay) : Right(otherWay);
            float reach = Cross(otherSide * otherHalfWidth - side * self.HalfWidth, otherWay) / Cross(arriving, otherWay);
            return reach > 0f ? reach + EdgeReach : reach;
        }

        /// <summary>
        /// The lowest and highest the street's surface comes between two
        /// distances along a segment.
        /// </summary>
        private static void Span(StreetSpec street, int segment, StreetHeights heights, float from, float to, out float low, out float high)
        {
            low = float.MaxValue;
            high = float.MinValue;
            int samples = Mathf.Max(1, Mathf.CeilToInt((to - from) / SampleStep));

            for (int i = 0; i <= samples; i++) {
                float height = heights.SurfaceAt(street, segment, Mathf.Lerp(from, to, (float)i / samples));
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
            }
        }

        /// <summary>
        /// Where the sector's NavMesh data is saved: in the scene's own
        /// folder beside it (made if missing); null for a scene never saved.
        /// </summary>
        private static string DataPath(Scene scene, string sector)
        {
            if (string.IsNullOrEmpty(scene.path)) {
                return null;
            }

            string folder = scene.path.Substring(0, scene.path.Length - ".unity".Length);

            if (!AssetDatabase.IsValidFolder(folder)) {
                int slash = folder.LastIndexOf('/');
                AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
            }

            return $"{folder}/NavMesh - {sector.Trim()}.asset";
        }

        /// <summary>
        /// A point on the ground plane (x, z).
        /// </summary>
        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        /// <summary>
        /// The direction a quarter turn to the left of a way (seen from above).
        /// </summary>
        private static Vector2 Left(Vector2 way)
        {
            return new Vector2(-way.y, way.x);
        }

        /// <summary>
        /// The direction a quarter turn to the right of a way (seen from above).
        /// </summary>
        private static Vector2 Right(Vector2 way)
        {
            return new Vector2(way.y, -way.x);
        }

        /// <summary>
        /// The 2D cross product: positive where b lies to the left of a.
        /// </summary>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
