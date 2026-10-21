using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Streets that climb gently. Where a segment's two points stand at
    /// different heights and the grade (the rise over the segment's length)
    /// is no steeper than the settings' StreetSlopeMaxGrade, the change is
    /// made by a slope rather than by a flight of steps (StreetSteps): the
    /// street's ground mesh rises evenly from the low point to the high
    /// point, its vertices on the slope's surface. Only a street of ground
    /// (cobbles) can change height.
    ///
    /// The facades beside a slope stand level, each building at the highest
    /// the street comes under it, their foundations stacked down to the
    /// street (FacadeBuilder). Geometry and choice only; StreetGround builds
    /// the mesh and the slope's tilted collider box.
    /// </summary>
    internal static class StreetSlopes
    {
        /// <summary>
        /// One segment's slope, in the segment's frame: distances are along
        /// it from its first point.
        /// </summary>
        internal class Slope
        {
            public StreetSpec Street;
            public int Segment;
            // The segment's first point, its way and length, and its width.
            public Vector2 Start;
            public Vector2 Axis;
            public float Length;
            public float Width;
            // +1 where the street climbs from the first point, -1 where it falls.
            public float Up;
            public float Low;
            public float High;
            // Where the slope starts (its foot, at the low point) and ends
            // (its top, at the high point) along the segment.
            public float Foot;
            public float Top;

            /// <summary>
            /// The rise over the slope's length.
            /// </summary>
            public float Grade => (High - Low) / Mathf.Abs(Top - Foot);

            /// <summary>
            /// The slope's angle from level, in degrees.
            /// </summary>
            public float Tilt => Mathf.Atan(Grade) * Mathf.Rad2Deg;

            /// <summary>
            /// The way up the slope on the ground.
            /// </summary>
            public Vector2 Uphill => Axis * Up;

            /// <summary>
            /// The slope's surface normal: up, tilted back down the slope.
            /// </summary>
            public Vector3 Normal
            {
                get
                {
                    Vector3 uphill = new(Uphill.x, 0f, Uphill.y);
                    Vector3 along = (uphill + Vector3.up * Grade).normalized;
                    return Vector3.Cross(along, Vector3.Cross(Vector3.up, uphill)).normalized;
                }
            }

            /// <summary>
            /// The turn that lays a level piece on the slope: about the
            /// segment's across axis, so its way up the segment points up
            /// the slope.
            /// </summary>
            public Quaternion TiltRotation
            {
                get
                {
                    Vector3 uphill = new(Uphill.x, 0f, Uphill.y);
                    return Quaternion.FromToRotation(uphill, (uphill + Vector3.up * Grade).normalized);
                }
            }

            /// <summary>
            /// The height of the street's surface at a distance along the
            /// segment: the low level before the foot, rising evenly up the
            /// slope, the high level from the top on. The ground mesh follows
            /// it exactly.
            /// </summary>
            public float SurfaceAt(float along)
            {
                float share = Mathf.Abs(Top - Foot) > 0.0001f ? Mathf.Clamp01((along - Foot) / (Top - Foot)) : 1f;
                return Mathf.Lerp(Low, High, share);
            }

            /// <summary>
            /// Whether a distance along the segment lies on the slope (not
            /// beyond either end).
            /// </summary>
            public bool OnSlope(float along)
            {
                return (along - Foot) * Up >= 0f && (along - Top) * Up <= 0f;
            }
        }

        /// <summary>
        /// The grade a rising segment would slope at: its rise over its
        /// length. 0 if it doesn't rise; infinite for a segment of no length.
        /// </summary>
        internal static float Grade(StreetSpec street, int segment, GeneratorSettings settings)
        {
            if (!StreetSteps.Rises(street, segment, settings)) {
                return 0f;
            }

            float rise = Mathf.Abs(street.points[segment + 1].y - street.points[segment].y);
            Vector2 a = new(street.points[segment].x, street.points[segment].z);
            Vector2 b = new(street.points[segment + 1].x, street.points[segment + 1].z);
            float length = (b - a).magnitude;
            return length >= settings.MinSegmentLength ? rise / length : float.PositiveInfinity;
        }

        /// <summary>
        /// Whether a street's segment changes height by a slope: it rises,
        /// its street is of ground, and its grade is no steeper than
        /// StreetSlopeMaxGrade. Any other rise is made by steps.
        /// </summary>
        internal static bool IsSlope(StreetSpec street, int segment, BuildingStyle style, GeneratorSettings settings)
        {
            if (style == null || style.TileCategory != "ground" || !StreetSteps.Rises(street, segment, settings)) {
                return false;
            }

            return Grade(street, segment, settings) <= settings.StreetSlopeMaxGrade;
        }

        /// <summary>
        /// Every slope the spec's streets need, in the spec's order: each
        /// rising segment of a street of ground gentle enough to slope
        /// (IsSlope), from its low point to its high point.
        /// </summary>
        internal static List<Slope> Find(List<StreetSpec> streets, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings)
        {
            List<Slope> slopes = new();

            foreach (StreetSpec street in streets) {
                if (!styles.TryGetValue(street.style ?? "", out BuildingStyle style)) {
                    continue;
                }

                for (int i = 0; i + 1 < street.points.Count; i++) {
                    if (!IsSlope(street, i, style, settings)) {
                        continue;
                    }

                    float from = street.points[i].y;
                    float to = street.points[i + 1].y;
                    Vector2 a = new(street.points[i].x, street.points[i].z);
                    Vector2 b = new(street.points[i + 1].x, street.points[i + 1].z);
                    float length = (b - a).magnitude;
                    float up = to > from ? 1f : -1f;
                    slopes.Add(new Slope {
                        Street = street,
                        Segment = i,
                        Start = a,
                        Axis = (b - a) / length,
                        Length = length,
                        Width = street.SegmentWidth(i),
                        Up = up,
                        Low = Mathf.Min(from, to),
                        High = Mathf.Max(from, to),
                        Foot = up > 0f ? 0f : length,
                        Top = up > 0f ? length : 0f
                    });
                }
            }

            return slopes;
        }

        /// <summary>
        /// The slope of a street's segment, or null if it has none.
        /// </summary>
        internal static Slope Of(List<Slope> slopes, StreetSpec street, int segment)
        {
            foreach (Slope slope in slopes) {
                if (slope.Street == street && slope.Segment == segment) {
                    return slope;
                }
            }

            return null;
        }
    }
}
