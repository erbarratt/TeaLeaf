using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Keeps the buildings out of the streets. Each street segment is a
    /// convex shape: its rectangle at its resolved width, from point to point,
    /// mitred where it meets other segments at a point (its own street's next
    /// segment at a bend, other streets at a junction) - cut, on each side,
    /// along the line from the point through the corner where its edge meets
    /// the neighbouring street's edge on that side (Mitre). So the segments fit
    /// together like mitred joints, filling a bend's or a junction's corners
    /// and never poking a square end out past a street leaving at a sharp
    /// angle. Everything that keeps a building out of the streets works from
    /// these shapes:
    ///
    /// - a run of facade is cut to the stretch where its walls and foundations
    ///   (from the street edge back to the deepest of them) stand in no other
    ///   street (Clear), so a frontage never reaches across a branching
    ///   street, nor over one passing close behind it;
    /// - a run of side walls stops where it would reach
    ///   into a street (Reach);
    /// - and, last, every piece the facades place is checked as it is placed
    ///   (Allows, through PiecePlacer): one standing in a street is not placed
    ///   at all, and logged. A piece high enough above a street (a jetty, an
    ///   upper storey) may overhang it; lower down, into the street its facade
    ///   faces it may come as far as a column steps forward, into any other
    ///   street only as far as a foundation's front stands out.
    /// </summary>
    internal class StreetClearance
    {
        /// <summary>
        /// One street segment's shape.
        /// </summary>
        private class Segment
        {
            public StreetSpec Street;
            public int Index;
            // Its first point, way and length, for heights along it.
            public Vector2 Start;
            public Vector2 Axis;
            public float Length;
            public float StartHeight;
            public float EndHeight;
            // Its flight of steps, if it has one: the street is level either
            // side of it rather than sloping.
            public StreetSteps.Flight Flight;
            // Its corners, anticlockwise seen from above (x right, z up): cut
            // through the point on the outside of a corner (for cutting runs,
            // which are carried to that corner themselves), and filled out
            // to it (for the side walls, roofs and pieces, which must not
            // stand there).
            public List<Vector2> Corners;
            public List<Vector2> Filled;

            /// <summary>
            /// The street's surface at a distance along the segment.
            /// </summary>
            public float SurfaceAt(float along)
            {
                return Flight != null ? Flight.SurfaceAt(along) : Mathf.Lerp(StartHeight, EndHeight, Mathf.Clamp01(along / Length));
            }
        }

        private readonly List<Segment> _segments = new();
        private readonly GeneratorSettings _settings;
        // The corners of a placed piece's render box, on the ground.
        private readonly Vector2[] _corners = new Vector2[8];

        // The street the facade being built faces; null for a lot, which
        // faces whichever streets are round it.
        internal StreetSpec Facing { get; set; }

        // How far off a street's edge line a lot's side may lie and still
        // count as lining that street (LiningSegment).
        private const float LiningTolerance = 0.1f;

        // The pieces refused, by name and where, for the log.
        internal List<string> Refused { get; } = new();

        /// <summary>
        /// The clearance for a spec's streets at their resolved widths, and
        /// their flights of steps.
        /// </summary>
        internal StreetClearance(List<StreetSpec> streets, List<StreetSteps.Flight> flights, GeneratorSettings settings)
        {
            _settings = settings;

            foreach (StreetSpec street in streets) {
                for (int i = 0; i + 1 < street.points.Count; i++) {
                    Vector2 a = Ground(street.points[i]);
                    Vector2 b = Ground(street.points[i + 1]);
                    float length = (b - a).magnitude;

                    if (length < 0.0001f) {
                        continue;
                    }

                    Vector2 axis = (b - a) / length;
                    Vector2 normal = new(-axis.y, axis.x);
                    float half = street.SegmentWidth(i) * 0.5f;
                    List<Vector2> square = new() { a - normal * half, b - normal * half, b + normal * half, a + normal * half };
                    List<Vector2> corners = Mitre(Mitre(new List<Vector2>(square), a, axis, half, streets, false), b, -axis, half, streets, false);
                    List<Vector2> filled = Mitre(Mitre(square, a, axis, half, streets, true), b, -axis, half, streets, true);
                    _segments.Add(new Segment {
                        Street = street,
                        Index = i,
                        Start = a,
                        Axis = axis,
                        Length = length,
                        StartHeight = street.points[i].y,
                        EndHeight = street.points[i + 1].y,
                        Flight = StreetSteps.Of(flights, street, i),
                        Corners = corners,
                        Filled = filled
                    });
                }
            }
        }

        /// <summary>
        /// A segment's shape mitred at one of its ends (point, the segment
        /// leaving it the way away, half wide): on each side, the nearest
        /// other way leaving the point turning to that side (any street's
        /// segment, its own street's included), and the shape cut along the
        /// line from the point through where the segment's edge on that side
        /// meets that segment's facing edge, keeping the segment's side (with
        /// fill, on the outside of a corner, where the edges meet past the
        /// point, the end is lengthened to there first, so the corner is
        /// filled; without, the outside is cut through the point). A
        /// point no other segment leaves (a dead end) is left square, as is a
        /// side where the next way is nearly straight on (within the branch
        /// angle of a half turn: the edges would meet far off).
        /// </summary>
        private List<Vector2> Mitre(List<Vector2> corners, Vector2 point, Vector2 away, float half, List<StreetSpec> streets, bool fill)
        {
            float nearestLeft = float.MaxValue;
            float nearestRight = float.MaxValue;
            Vector2 leftWay = Vector2.zero;
            Vector2 rightWay = Vector2.zero;
            float leftHalf = 0f;
            float rightHalf = 0f;

            foreach (StreetSpec street in streets) {
                for (int j = 0; j < street.points.Count; j++) {
                    if ((Ground(street.points[j]) - point).magnitude > _settings.JunctionDistance) {
                        continue;
                    }

                    for (int n = j - 1; n <= j + 1; n += 2) {
                        if (n < 0 || n >= street.points.Count) {
                            continue;
                        }

                        Vector2 leaving = Ground(street.points[n]) - point;

                        if (leaving.magnitude < _settings.MinSegmentLength) {
                            continue;
                        }

                        Vector2 way = leaving.normalized;
                        // Its angle from this segment's way, anticlockwise.
                        float angle = Mathf.Atan2(away.x * way.y - away.y * way.x, Vector2.Dot(away, way)) * Mathf.Rad2Deg;

                        // This segment itself.
                        if (Mathf.Abs(angle) < 0.01f) {
                            continue;
                        }

                        float left = angle > 0f ? angle : angle + 360f;
                        float right = 360f - left;
                        float otherHalf = street.SegmentWidth(Mathf.Min(j, n)) * 0.5f;

                        if (left < nearestLeft) {
                            nearestLeft = left;
                            leftWay = way;
                            leftHalf = otherHalf;
                        }

                        if (right < nearestRight) {
                            nearestRight = right;
                            rightWay = way;
                            rightHalf = otherHalf;
                        }
                    }
                }
            }

            Vector2 normal = new(-away.y, away.x);
            // Nearly straight on (within the branch angle of a half turn) the
            // edges meet far off: the end stays square on that side.
            float straight = Mathf.Asin(_settings.BranchDot) * Mathf.Rad2Deg;
            nearestLeft = Mathf.Abs(nearestLeft - 180f) < straight ? float.MaxValue : nearestLeft;
            nearestRight = Mathf.Abs(nearestRight - 180f) < straight ? float.MaxValue : nearestRight;
            // This segment's left edge meets the other's right edge, and its
            // right edge the other's left.
            Vector2 leftEdge = point - new Vector2(-leftWay.y, leftWay.x) * leftHalf;
            Vector2 rightEdge = point + new Vector2(-rightWay.y, rightWay.x) * rightHalf;
            // On the outside of a bend or a junction's corner the edges meet
            // past the point: the end is first lengthened to there, so the
            // cut fills the corner rather than leaving it out of every shape.
            float beyond = 0f;

            if (nearestLeft < float.MaxValue && Meet(point + normal * half, away, leftEdge, leftWay, out Vector2 meetLeft)) {
                beyond = Mathf.Max(beyond, -Vector2.Dot(meetLeft - point, away));
            }

            if (nearestRight < float.MaxValue && Meet(point - normal * half, away, rightEdge, rightWay, out Vector2 meetRight)) {
                beyond = Mathf.Max(beyond, -Vector2.Dot(meetRight - point, away));
            }

            if (fill && beyond > 0f) {
                for (int k = 0; k < corners.Count; k++) {
                    // The end's two corners, square across the point.
                    if (Mathf.Abs(Vector2.Dot(corners[k] - point, away)) < 0.0001f) {
                        corners[k] -= away * beyond;
                    }
                }
            }

            if (nearestLeft < float.MaxValue) {
                corners = CutAt(corners, point, away, point + normal * half, away, leftEdge, leftWay);
            }

            if (nearestRight < float.MaxValue) {
                corners = CutAt(corners, point, away, point - normal * half, away, rightEdge, rightWay);
            }

            return corners;
        }

        /// <summary>
        /// Where two lines (each a point on it and its way) meet; false if
        /// they are parallel.
        /// </summary>
        private static bool Meet(Vector2 edgeA, Vector2 wayA, Vector2 edgeB, Vector2 wayB, out Vector2 meet)
        {
            float cross = wayA.x * wayB.y - wayA.y * wayB.x;
            meet = edgeA;

            if (Mathf.Abs(cross) < 0.0001f) {
                return false;
            }

            Vector2 offset = edgeB - edgeA;
            meet = edgeA + wayA * ((offset.x * wayB.y - offset.y * wayB.x) / cross);
            return true;
        }

        /// <summary>
        /// A shape cut along the line from point through where two edges (each
        /// a point on it and its way) meet, keeping the side the way away lies
        /// on; unchanged if the edges never meet.
        /// </summary>
        private static List<Vector2> CutAt(List<Vector2> corners, Vector2 point, Vector2 away, Vector2 edgeA, Vector2 wayA, Vector2 edgeB, Vector2 wayB)
        {
            if (!Meet(edgeA, wayA, edgeB, wayB, out Vector2 meet)) {
                return corners;
            }

            Vector2 line = meet - point;

            if (line.sqrMagnitude < 0.000001f) {
                return corners;
            }

            Vector2 keep = new(-line.y, line.x);

            if (Vector2.Dot(keep, away) < 0f) {
                keep = -keep;
            }

            return Clip(corners, corner => Vector2.Dot(corner - point, keep));
        }

        /// <summary>
        /// Cuts a run of facade along a street's edge (from lineStart, the way
        /// along, between start and end) to the longest stretch where a band
        /// from front forward of the line to back behind it stands in no
        /// street but the segment it lines and its neighbours (which it only
        /// touches), kept margin clear of each (how far a piece may stand out
        /// past its module's end). False if no stretch is left.
        /// </summary>
        internal bool Clear(StreetSpec street, int segment, Vector2 lineStart, Vector2 along, Vector2 outward, float front, float back,
            float margin, ref float start, ref float end)
        {
            List<(float from, float to)> blocked = new();
            Vector2 inward = -outward;

            foreach (Segment other in _segments) {
                if (other.Street == street && Mathf.Abs(other.Index - segment) <= 1) {
                    continue;
                }

                // The shape's corners in the run's frame (u along it, v into
                // its street), clipped to the band's depth.
                List<Vector2> polygon = new();

                foreach (Vector2 corner in other.Corners) {
                    Vector2 offset = corner - lineStart;
                    polygon.Add(new Vector2(Vector2.Dot(offset, along), Vector2.Dot(offset, inward)));
                }

                polygon = Clip(polygon, point => front - point.y);
                polygon = Clip(polygon, point => point.y + back);

                if (polygon.Count == 0) {
                    continue;
                }

                float from = float.MaxValue;
                float to = float.MinValue;

                foreach (Vector2 point in polygon) {
                    from = Mathf.Min(from, point.x);
                    to = Mathf.Max(to, point.x);
                }

                if (to - from > 0.0001f) {
                    blocked.Add((from - margin - _settings.OverlapTolerance, to + margin + _settings.OverlapTolerance));
                }
            }

            // The longest stretch between start and end that nothing blocks.
            blocked.Sort((x, y) => x.from.CompareTo(y.from));
            float bestFrom = start;
            float bestTo = start;
            float cursor = start;

            foreach ((float from, float to) in blocked) {
                if (from > cursor && Mathf.Min(from, end) - cursor > bestTo - bestFrom) {
                    bestFrom = cursor;
                    bestTo = Mathf.Min(from, end);
                }

                cursor = Mathf.Max(cursor, to);
            }

            if (end - cursor > bestTo - bestFrom) {
                bestFrom = cursor;
                bestTo = end;
            }

            start = bestFrom;
            end = bestTo;
            return end - start > _settings.MinSegmentLength;
        }

        /// <summary>
        /// The street segment whose edge a line (from a to b, facing front)
        /// runs along, as a lot's side does where its block was taken from
        /// the streets: both ends within LiningTolerance of one of the
        /// segment's edge lines, the segment on the line's front side, the
        /// two overlapping along the line - where several segments in line
        /// do, the one overlapping most. False, with null, if no segment's
        /// edge lines it (a lot's side in open ground).
        /// </summary>
        internal bool LiningSegment(Vector2 a, Vector2 b, Vector2 front, out StreetSpec street, out int segment)
        {
            street = null;
            segment = -1;
            Vector2 along = (b - a).normalized;
            float length = (b - a).magnitude;
            float bestOverlap = 0f;

            foreach (Segment candidate in _segments) {
                for (int i = 0; i < candidate.Corners.Count; i++) {
                    Vector2 p = candidate.Corners[i];
                    Vector2 q = candidate.Corners[(i + 1) % candidate.Corners.Count];

                    if ((q - p).sqrMagnitude < 0.000001f) {
                        continue;
                    }

                    Vector2 edgeAlong = (q - p).normalized;
                    // The corners run anticlockwise seen from above, so the
                    // street lies to the left of each edge.
                    Vector2 inward = new(-edgeAlong.y, edgeAlong.x);

                    if (Mathf.Abs(Vector2.Dot(edgeAlong, along)) < 0.999f || Vector2.Dot(inward, front) < 0.9f
                        || Mathf.Abs(Vector2.Dot(a - p, inward)) > LiningTolerance || Mathf.Abs(Vector2.Dot(b - p, inward)) > LiningTolerance) {
                        continue;
                    }

                    float from = Mathf.Min(Vector2.Dot(p - a, along), Vector2.Dot(q - a, along));
                    float to = Mathf.Max(Vector2.Dot(p - a, along), Vector2.Dot(q - a, along));
                    float overlap = Mathf.Min(to, length) - Mathf.Max(from, 0f);

                    if (overlap > bestOverlap) {
                        bestOverlap = overlap;
                        street = candidate.Street;
                        segment = candidate.Index;
                    }
                }
            }

            return street != null;
        }

        /// <summary>
        /// How far a line from start can run the given way before it enters a
        /// street (any segment's shape but one it starts in: it is leaving
        /// that one), the outside of every corner counting as street
        /// (Segment.Filled). float.MaxValue if it enters none.
        /// </summary>
        internal float Reach(Vector2 start, Vector2 way)
        {
            float nearest = float.MaxValue;

            foreach (Segment segment in _segments) {
                // Where it enters and leaves the shape, edge by edge.
                float enter = 0f;
                float leave = float.MaxValue;
                bool inside = true;
                bool misses = false;
                List<Vector2> corners = segment.Filled;

                for (int k = 0; k < corners.Count && !misses; k++) {
                    Vector2 a = corners[k];
                    Vector2 edge = corners[(k + 1) % corners.Count] - a;
                    // Outward from the shape (its corners go anticlockwise).
                    Vector2 outward = new(edge.y, -edge.x);
                    float distance = Vector2.Dot(outward, a - start);
                    float closing = Vector2.Dot(outward, way);

                    inside &= distance >= 0f;

                    if (Mathf.Abs(closing) < 0.000001f) {
                        misses = distance < 0f;
                        continue;
                    }

                    // Crossing this edge inwards (closing on it from outside)
                    // or outwards.
                    float t = distance / closing;

                    if (closing < 0f) {
                        enter = Mathf.Max(enter, t);
                    } else {
                        leave = Mathf.Min(leave, t);
                    }
                }

                if (!inside && !misses && enter <= leave) {
                    nearest = Mathf.Min(nearest, enter);
                }
            }

            return nearest;
        }

        /// <summary>
        /// Whether a placed piece (its render box, as placed) keeps out of the
        /// streets: one standing StreetHeadroom or more above a street (a
        /// jetty, an upper storey) may overhang it; lower down it may come into
        /// the street its facade faces (every street, for a lot) no further
        /// than a column may step forward plus a foundation's front, and into
        /// any other street no further than a foundation's front. Records a
        /// refused piece in Refused.
        /// </summary>
        internal bool Allows(Transform placed, CatalogueEntry entry)
        {
            float lowest = float.MaxValue;
            int n = 0;

            for (int x = 0; x < 2; x++) {
                for (int y = 0; y < 2; y++) {
                    for (int z = 0; z < 2; z++) {
                        Vector3 local = new(x == 0 ? entry.renderMin.x : entry.renderMax.x, y == 0 ? entry.renderMin.y : entry.renderMax.y,
                            z == 0 ? entry.renderMin.z : entry.renderMax.z);
                        Vector3 world = placed.TransformPoint(local);
                        _corners[n++] = new Vector2(world.x, world.z);
                        lowest = Mathf.Min(lowest, world.y);
                    }
                }
            }

            Vector2 centre = Vector2.zero;

            foreach (Vector2 corner in _corners) {
                centre += corner / _corners.Length;
            }

            Vector2 right = Level(placed.right);
            Vector2 forward = Level(placed.forward);
            float tolerance = _settings.FoundationFrontOffset + _settings.OverlapTolerance;

            foreach (Segment segment in _segments) {
                // High enough over the street beside it to be out of the way:
                // over the street its facade faces, above the level the
                // facade stands on (beside a flight of steps, its low side -
                // the jetty of a building beside the steps hangs over them).
                bool facing = Facing == null || segment.Street == Facing;
                float street = facing && segment.Flight != null
                    ? Mathf.Min(segment.StartHeight, segment.EndHeight)
                    : segment.SurfaceAt(Vector2.Dot(centre - segment.Start, segment.Axis));

                if (lowest >= street + _settings.StreetHeadroom) {
                    continue;
                }
                float allowed = facing ? tolerance + _settings.ColumnDepthMax : tolerance;
                float depth = Penetration(segment, right, forward);

                if (depth > allowed) {
                    string note = $"{placed.name} at ({centre.x:0.0}, {centre.y:0.0}) under '{Path(placed)}', "
                        + $"{depth:0.00}m into street '{segment.Street.id}' segment {segment.Index}";
                    Refused.Add(note);
                    Vector3 size = placed.TransformVector(entry.renderMax - entry.renderMin);
                    GeneratedReport.Add(GeneratedRoot.MarkerKind.Refused, placed.TransformPoint((entry.renderMin + entry.renderMax) * 0.5f),
                        new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)), "Refused: " + note);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// How deep the placed piece's corners reach into a segment's shape:
        /// the least overlap along any separating axis (the shape's edges'
        /// normals and the piece's two axes), 0 or less when apart.
        /// </summary>
        private float Penetration(Segment segment, Vector2 right, Vector2 forward)
        {
            float depth = float.MaxValue;
            List<Vector2> shape = segment.Filled;

            for (int k = 0; k < shape.Count + 2; k++) {
                Vector2 axis;

                if (k < shape.Count) {
                    Vector2 edge = shape[(k + 1) % shape.Count] - shape[k];
                    axis = new Vector2(edge.y, -edge.x);
                } else {
                    axis = k == shape.Count ? right : forward;
                }

                if (axis.sqrMagnitude < 0.000001f) {
                    continue;
                }

                axis.Normalize();
                Project(_corners, axis, out float low, out float high);
                Project(shape, axis, out float shapeLow, out float shapeHigh);
                depth = Mathf.Min(depth, Mathf.Min(high, shapeHigh) - Mathf.Max(low, shapeLow));
            }

            return depth;
        }

        /// <summary>
        /// The lowest and highest points project to along an axis.
        /// </summary>
        private static void Project(IReadOnlyList<Vector2> points, Vector2 axis, out float low, out float high)
        {
            low = float.MaxValue;
            high = float.MinValue;

            foreach (Vector2 point in points) {
                float value = Vector2.Dot(point, axis);
                low = Mathf.Min(low, value);
                high = Mathf.Max(high, value);
            }
        }

        /// <summary>
        /// A convex polygon clipped to where a linear function of its points
        /// is at least 0 (one Sutherland-Hodgman pass).
        /// </summary>
        private static List<Vector2> Clip(List<Vector2> polygon, System.Func<Vector2, float> inside)
        {
            List<Vector2> clipped = new();

            for (int i = 0; i < polygon.Count; i++) {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                float da = inside(a);
                float db = inside(b);

                if (da >= 0f) {
                    clipped.Add(a);
                }

                if ((da >= 0f) != (db >= 0f)) {
                    clipped.Add(a + (b - a) * (da / (da - db)));
                }
            }

            return clipped;
        }

        /// <summary>
        /// Where a placed piece is, for the log: its frontage (or lot), its
        /// building and its group.
        /// </summary>
        private static string Path(Transform placed)
        {
            string path = "";
            Transform current = placed.parent;

            for (int i = 0; i < 3 && current != null; i++, current = current.parent) {
                path = i == 0 ? current.name : current.name + "/" + path;
            }

            return path;
        }

        /// <summary>
        /// A point on the ground: its x and z.
        /// </summary>
        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }

        /// <summary>
        /// A direction's level part on the ground (x, z).
        /// </summary>
        private static Vector2 Level(Vector3 direction)
        {
            return new Vector2(direction.x, direction.z);
        }
    }
}
