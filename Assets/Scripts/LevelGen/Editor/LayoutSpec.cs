using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// A layout spec: one generated area's streets, the buildings lining
    /// them and the whole buildings on lots inside loops, as kept in a JSON
    /// file in Assets/Data/LevelGen/Layouts. Written from a brief by Claude
    /// in the conversation, and read by the generator.
    ///
    /// Pieces are never named here: a street or a storey names a style, and
    /// the pieces come from the annotations tagged with it (BuildingStyle).
    /// All positions are in world space, in metres; a yaw is in degrees
    /// clockwise from +Z (north) seen from above, as Unity turns about Y.
    /// </summary>
    [Serializable]
    public class LayoutSpec
    {
        // Bumped when the format changes; a spec in another format is refused.
        public const int CurrentFormat = 2;

        internal const string LayoutsFolder = PrefabCatalogueScanner.DataFolder + "/Layouts";

        public int format;
        // Every random choice the generator makes comes from one
        // System.Random made from this, in a fixed order.
        public int seed;
        // The sector the area is built in: its pieces go under
        // Environment/Sectors/<sector>/Geometry/Generated, which a
        // regeneration replaces whole.
        public string sector;
        public List<StreetSpec> streets = new();
        public List<LotSpec> lots = new();

        /// <summary>
        /// Gives every street segment its width, the first random choices
        /// made from the seed (so the validator, which makes only these, sees
        /// the same widths as the generator): the street's width - its
        /// spec's, or GeneratorSettings.StreetWidth if that is 0 - made up to
        /// StreetWidthVariance wider or narrower, and at least MinStreetWidth.
        /// One draw per segment, in the spec's order, whatever the segment.
        /// </summary>
        internal void ResolveWidths(GeneratorSettings settings, System.Random random)
        {
            foreach (StreetSpec street in streets) {
                float baseWidth = street.width > 0f ? street.width : settings.StreetWidth;
                street.SegmentWidths = new List<float>();

                for (int i = 1; i < street.points.Count; i++) {
                    float variance = Mathf.Lerp(-settings.StreetWidthVariance, settings.StreetWidthVariance, (float)random.NextDouble());
                    street.SegmentWidths.Add(Mathf.Max(settings.MinStreetWidth, baseWidth + variance));
                }
            }
        }

        /// <summary>
        /// Gives every lot with neither an outline nor modules the block its
        /// streets enclose (BlockOutline, after the widths are resolved), its
        /// front the side facing nearest the lot's yaw. A lot no streets
        /// enclose is left without one (the validator reports it).
        /// </summary>
        internal void ResolveLots(GeneratorSettings settings)
        {
            foreach (LotSpec lot in lots) {
                lot.FromStreets = false;

                if (lot.HasOutline || lot.widthModules > 0 || lot.depthModules > 0) {
                    continue;
                }

                List<Vector2> block = BlockOutline(new Vector2(lot.origin.x, lot.origin.z), settings);

                if (block == null) {
                    continue;
                }

                // The front: the side whose outward way is nearest the yaw's.
                Vector2 average = Vector2.zero;

                foreach (Vector2 corner in block) {
                    average += corner / block.Count;
                }

                Vector2 facing = new(lot.Front.x, lot.Front.z);
                int front = 0;
                float best = float.MinValue;

                for (int i = 0; i < block.Count; i++) {
                    Vector2 a = block[i];
                    Vector2 b = block[(i + 1) % block.Count];
                    Vector2 outward = new Vector2(-(b - a).y, (b - a).x).normalized;

                    if (Vector2.Dot(outward, (a + b) * 0.5f - average) < 0f) {
                        outward = -outward;
                    }

                    if (Vector2.Dot(outward, facing) > best) {
                        best = Vector2.Dot(outward, facing);
                        front = i;
                    }
                }

                lot.outline = new List<Vector3>();

                for (int i = 0; i < block.Count; i++) {
                    Vector2 corner = block[(front + i) % block.Count];
                    lot.outline.Add(new Vector3(corner.x, lot.origin.y, corner.y));
                }

                lot.FromStreets = true;
            }
        }

        /// <summary>
        /// The block the streets enclose round a point: the street edges seen
        /// from it (rays cast all round it, each taking the nearest segment's
        /// near edge it crosses - so a street that bends away, or a side
        /// street leaving the block's far side, never counts), then the convex
        /// region on the point's side of each of those edges, its corners in
        /// order round it, sides in line merged. A side shorter than
        /// MinBlockSide (a sliver where a street's wedge clips a corner) is
        /// dropped and its neighbours left to meet. Null if the point is on a
        /// street or the streets don't close round it.
        /// </summary>
        internal List<Vector2> BlockOutline(Vector2 point, GeneratorSettings settings)
        {
            // Each segment's near edge: its two ends and the way into the block.
            List<(Vector2 A, Vector2 B, Vector2 Inward)> edges = new();

            foreach (StreetSpec street in streets) {
                for (int i = 1; i < street.points.Count; i++) {
                    Vector2 a = new(street.points[i - 1].x, street.points[i - 1].z);
                    Vector2 b = new(street.points[i].x, street.points[i].z);
                    float length = (b - a).magnitude;

                    if (length < settings.MinSegmentLength) {
                        continue;
                    }

                    Vector2 normal = new Vector2(-(b - a).y, (b - a).x) / length;
                    float side = Vector2.Dot(point - a, normal);
                    float halfWidth = street.SegmentWidth(i - 1) * 0.5f;
                    float t = Vector2.Dot(point - a, (b - a) / length);

                    if (Mathf.Abs(side) <= halfWidth && t >= 0f && t <= length) {
                        return null;
                    }

                    Vector2 inward = side >= 0f ? normal : -normal;
                    edges.Add((a + inward * halfWidth, b + inward * halfWidth, inward));
                }
            }

            // The edges seen from the point.
            bool[] seen = new bool[edges.Count];

            for (int ray = 0; ray < BlockRays; ray++) {
                float angle = ray * Mathf.PI * 2f / BlockRays;
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
                int nearest = -1;
                float nearestDistance = float.MaxValue;

                for (int k = 0; k < edges.Count; k++) {
                    Vector2 edge = edges[k].B - edges[k].A;
                    float denominator = Cross(direction, edge);

                    if (Mathf.Abs(denominator) < 0.000001f) {
                        continue;
                    }

                    Vector2 offset = edges[k].A - point;
                    float distance = Cross(offset, edge) / denominator;
                    float along = Cross(offset, direction) / denominator;

                    if (distance > 0f && along >= 0f && along <= 1f && distance < nearestDistance) {
                        nearestDistance = distance;
                        nearest = k;
                    }
                }

                if (nearest >= 0) {
                    seen[nearest] = true;
                }
            }

            for (int pass = 0; pass <= edges.Count; pass++) {
                // Clip a large square round the point by every seen edge,
                // keeping which edge each side comes from (-1: the square).
                const float far = 5000f;
                List<Vector2> corners = new() {
                    point + new Vector2(-far, -far), point + new Vector2(far, -far), point + new Vector2(far, far), point + new Vector2(-far, far)
                };
                List<int> sources = new() { -1, -1, -1, -1 };

                for (int k = 0; k < edges.Count && corners.Count > 0; k++) {
                    if (seen[k]) {
                        Clip(corners, sources, edges[k].A, edges[k].Inward, k);
                    }
                }

                if (corners.Count < 3 || sources.Contains(-1)) {
                    return null;
                }

                List<Vector2> block = MergeInLine(corners);

                if (block == null) {
                    return null;
                }

                // A sliver side: drop its edge and find the region again.
                int sliver = -1;

                for (int i = 0; i < corners.Count && sliver < 0; i++) {
                    float length = (corners[(i + 1) % corners.Count] - corners[i]).magnitude;

                    if (length > 0.01f && length < MinBlockSide) {
                        sliver = sources[i];
                    }
                }

                if (sliver < 0) {
                    return block;
                }

                seen[sliver] = false;
            }

            return null;
        }

        // How many rays BlockOutline casts round its point (every half degree).
        private const int BlockRays = 720;
        // A block side shorter than this is a sliver, dropped.
        private const float MinBlockSide = 1f;

        /// <summary>
        /// The 2D cross product (z of a x b).
        /// </summary>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>
        /// Clips a convex polygon to the side of a line its inward way points
        /// to, keeping for each corner which plane the side from it comes from
        /// (the new side along the line from plane).
        /// </summary>
        private static void Clip(List<Vector2> corners, List<int> sources, Vector2 edge, Vector2 inward, int plane)
        {
            List<Vector2> kept = new();
            List<int> keptSources = new();

            for (int i = 0; i < corners.Count; i++) {
                Vector2 current = corners[i];
                Vector2 next = corners[(i + 1) % corners.Count];
                float currentSide = Vector2.Dot(current - edge, inward);
                float nextSide = Vector2.Dot(next - edge, inward);

                if (currentSide >= 0f) {
                    kept.Add(current);
                    keptSources.Add(sources[i]);
                }

                if (currentSide >= 0f != nextSide >= 0f) {
                    Vector2 crossing = current + (next - current) * (currentSide / (currentSide - nextSide));
                    kept.Add(crossing);
                    // Leaving: the new side runs along the line; entering:
                    // the old side goes on from the crossing.
                    keptSources.Add(currentSide >= 0f ? plane : sources[i]);
                }
            }

            corners.Clear();
            corners.AddRange(kept);
            sources.Clear();
            sources.AddRange(keptSources);
        }

        /// <summary>
        /// A polygon without corners where it goes straight on (within a
        /// degree) or that sit on the corner before (within a centimetre).
        /// </summary>
        private static List<Vector2> MergeInLine(List<Vector2> corners)
        {
            List<Vector2> merged = new();

            for (int i = 0; i < corners.Count; i++) {
                Vector2 before = corners[(i + corners.Count - 1) % corners.Count];
                Vector2 corner = corners[i];
                Vector2 after = corners[(i + 1) % corners.Count];

                if ((corner - before).magnitude < 0.01f || Vector2.Angle(corner - before, after - corner) < 1f) {
                    continue;
                }

                merged.Add(corner);
            }

            return merged.Count >= 3 ? merged : null;
        }
    }

    /// <summary>
    /// A street: a polyline along its middle, at any angle (a curve is several
    /// short segments). Two streets meet where they share a point. Its sides
    /// are lined by frontages.
    /// </summary>
    [Serializable]
    public class StreetSpec
    {
        public string id;
        // The style its ground (or plank decking) comes from.
        public string style;
        // Width from edge to edge, in metres; 0 uses GeneratorSettings.StreetWidth.
        // Each segment is varied from it (LayoutSpec.ResolveWidths).
        public float width;
        // The middle line, each point with its ground height in y.
        public List<Vector3> points = new();
        public List<FrontageSpec> frontages = new();

        // Each segment's width once resolved (segment i runs from point i to
        // point i + 1). Not in the file.
        internal List<float> SegmentWidths { get; set; }

        /// <summary>
        /// A segment's resolved width (the spec's width before it is resolved).
        /// </summary>
        internal float SegmentWidth(int segment)
        {
            return SegmentWidths != null && segment >= 0 && segment < SegmentWidths.Count ? SegmentWidths[segment] : width;
        }

        /// <summary>
        /// The widest of the segments meeting at a point (one at an end).
        /// </summary>
        internal float WidthAt(int point)
        {
            return Mathf.Max(SegmentWidth(point - 1 >= 0 ? point - 1 : point), SegmentWidth(point < points.Count - 1 ? point : point - 1));
        }
    }

    /// <summary>
    /// One side of a street, from one of its points to a later one, lined
    /// with buildings standing side by side along the street's edge, in
    /// order from the first point. A frontage runs past bends; it ends only
    /// where another street branches off on its side (or the street ends),
    /// since a street has no gaps between buildings but there. The buildings
    /// are facades: only what is seen from the street is built. Gaps at
    /// bends and whatever length is left at the end are filled by the
    /// generator, so a frontage needn't fit its stretch exactly.
    /// </summary>
    [Serializable]
    public class FrontageSpec
    {
        // "left" or "right", looking along the street from its first point.
        public string side;
        // The point indices the frontage runs between (from < to).
        public int from;
        public int to;
        public List<BuildingSpec> buildings = new();
    }

    /// <summary>
    /// One building in a frontage: its facade along the street.
    /// </summary>
    [Serializable]
    public class BuildingSpec
    {
        // Modules along the street.
        public int widthModules;
        // Styles from the ground storey up; the last repeats for every storey
        // above the list, and storeys past the count rolled are dropped
        // (the count comes from GeneratorSettings, StoreyPlan).
        public List<string> storeys = new();
        // Which module along the front holds the door, counted from the left
        // as seen from the street, from 0; -1 for none.
        public int doorModule = -1;
    }

    /// <summary>
    /// A lot: a whole building, seen from every side, where streets close a
    /// loop round it. Square in itself, at its own position and yaw; its
    /// footprint is a whole number of wall modules along its front and along
    /// its depth.
    /// </summary>
    [Serializable]
    public class LotSpec
    {
        public string id;
        // The middle of the footprint, at the height of the ground storey's floor.
        public Vector3 origin;
        // The way the front faces, in degrees clockwise from +Z.
        public float yaw;
        // Modules along the front, and from the front to the back.
        public int widthModules;
        public int depthModules;
        // Styles from the ground storey up; the last repeats for every storey
        // above the list, and storeys past the count rolled are dropped
        // (the count comes from GeneratorSettings, StoreyPlan).
        public List<string> storeys = new();
        // Which module along the front holds the door, counted from the left
        // as seen from in front, from 0; -1 for none.
        public int doorModule = -1;
        // A footprint of any shape: its corners (world x and z; y unused),
        // three or more, in order round it, convex; the side from the first
        // corner to the second is the front. When given, widthModules,
        // depthModules and yaw are not used (origin's height still is).
        // With neither an outline nor modules, the lot takes the block its
        // streets enclose round origin, its front the side facing nearest
        // yaw (LayoutSpec.ResolveLots).
        public List<Vector3> outline = new();
        // Whether the outline was found from the streets round the lot
        // (LayoutSpec.ResolveLots), not given in the spec.
        [NonSerialized] internal bool FromStreets;

        /// <summary>
        /// The direction the front faces, level.
        /// </summary>
        internal Vector3 Front => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        /// <summary>
        /// Whether the footprint is the outline rather than the rectangle.
        /// </summary>
        internal bool HasOutline => outline != null && outline.Count >= 3;

        /// <summary>
        /// The footprint's corners (x, z) in order round it, the front side
        /// first (corner 0 to corner 1): the outline, or else the rectangle of
        /// widthModules by depthModules modules (laid at spacing, plus the
        /// walls' overlap) round origin facing yaw, from its front-left
        /// corner as seen from in front.
        /// </summary>
        internal List<Vector2> Corners(float spacing, float overlap)
        {
            List<Vector2> corners = new();

            if (HasOutline) {
                foreach (Vector3 point in outline) {
                    corners.Add(new Vector2(point.x, point.z));
                }

                return corners;
            }

            Vector2 front = new(Front.x, Front.z);
            // Seen from in front, the viewer's right.
            Vector2 right = new(-front.y, front.x);
            Vector2 middle = new(origin.x, origin.z);
            float halfWidth = (widthModules * spacing + overlap) * 0.5f;
            float halfDepth = (depthModules * spacing + overlap) * 0.5f;
            corners.Add(middle + front * halfDepth - right * halfWidth);
            corners.Add(middle + front * halfDepth + right * halfWidth);
            corners.Add(middle - front * halfDepth + right * halfWidth);
            corners.Add(middle - front * halfDepth - right * halfWidth);
            return corners;
        }

        /// <summary>
        /// The footprint's bounding rectangle squared to its front side: its
        /// centre, axisX along the front side (first corner to second),
        /// axisZ out of the front, and its half sizes along them.
        /// </summary>
        internal void Bounds(float spacing, float overlap, out Vector2 centre, out Vector2 axisX, out Vector2 axisZ, out float halfX, out float halfZ)
        {
            List<Vector2> corners = Corners(spacing, overlap);
            Vector2 average = Vector2.zero;

            foreach (Vector2 corner in corners) {
                average += corner / corners.Count;
            }

            axisX = (corners[1] - corners[0]).normalized;
            axisZ = new Vector2(-axisX.y, axisX.x);

            if (Vector2.Dot(axisZ, (corners[0] + corners[1]) * 0.5f - average) < 0f) {
                axisZ = -axisZ;
            }

            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);

            foreach (Vector2 corner in corners) {
                Vector2 local = new(Vector2.Dot(corner, axisX), Vector2.Dot(corner, axisZ));
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }

            Vector2 middle = (min + max) * 0.5f;
            centre = axisX * middle.x + axisZ * middle.y;
            halfX = (max.x - min.x) * 0.5f;
            halfZ = (max.y - min.y) * 0.5f;
        }
    }
}
