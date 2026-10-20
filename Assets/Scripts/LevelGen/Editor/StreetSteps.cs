using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Streets that change height. Where a segment's two points stand at
    /// different heights (by more than the size tolerance), the change is made
    /// by a flight of steps across the whole street: whole flights of one of
    /// the street style's usable steps pieces (a RampCollider in it), stacked
    /// until they make the rise, which must be a whole number of them (within
    /// the size tolerance; the flights are scaled in height to make it
    /// exactly). The ground on either side stays flat, at each point's height,
    /// so streets always meet level at their shared points. Only a street of
    /// ground tiles (cobbles) can change height.
    ///
    /// The flights and, at their top, a landing of ground tiles laid in the
    /// segment's own frame (so its edge meets the top step in a straight line,
    /// which the street's turned grid can't) are centred on the segment. The
    /// landing is deep enough that no tile of the grid kept beyond it reaches
    /// back over the steps. Geometry and choice only; StreetGround lays them
    /// and FacadeBuilder stands the buildings beside them on the right level.
    /// </summary>
    internal static class StreetSteps
    {
        /// <summary>
        /// One segment's flight of steps, in the segment's frame: distances are
        /// along it from its first point.
        /// </summary>
        internal class Flight
        {
            public StreetSpec Street;
            public int Segment;
            public PieceAnnotation Piece;
            public CatalogueEntry Entry;
            // How many flights are stacked to make the rise.
            public int Count;
            // The segment's first point, its way and length, and its width.
            public Vector2 Start;
            public Vector2 Axis;
            public float Length;
            public float Width;
            // +1 where the street climbs from the first point, -1 where it falls.
            public float Up;
            public float Low;
            public float High;
            // Where the lowest flight's front (its first step) and the top
            // flight's back (its top step's edge) stand along the segment.
            public float Front;
            public float Top;
            // The landing's depth beyond the top, and its rows of tiles.
            public float Landing;
            public int LandingRows;

            /// <summary>
            /// Halfway up the flights: ground before it is at the low level,
            /// after it at the high level.
            /// </summary>
            public float Middle => (Front + Top) * 0.5f;

            /// <summary>
            /// The ground's height at a distance along the segment: the low
            /// level up to halfway up the flights, the high level beyond.
            /// </summary>
            public float HeightAt(float along)
            {
                return (along - Middle) * Up >= 0f ? High : Low;
            }

            /// <summary>
            /// The height of the street's surface at a distance along the
            /// segment: the low level before the flights, rising evenly up
            /// them (the steps' line, as walked), the high level beyond.
            /// </summary>
            public float SurfaceAt(float along)
            {
                float share = Mathf.Abs(Top - Front) > 0.0001f ? Mathf.Clamp01((along - Front) / (Top - Front)) : 1f;
                return Mathf.Lerp(Low, High, share);
            }
        }

        /// <summary>
        /// Whether a street's segment rises or falls (by more than the size
        /// tolerance), so it needs steps.
        /// </summary>
        internal static bool Rises(StreetSpec street, int segment, GeneratorSettings settings)
        {
            return Mathf.Abs(street.points[segment + 1].y - street.points[segment].y) > settings.SizeTolerance;
        }

        /// <summary>
        /// The style's first usable steps piece of which a whole number of
        /// flights makes the rise (within the size tolerance), and how many;
        /// null if none does.
        /// </summary>
        internal static PieceAnnotation Choose(BuildingStyle style, float rise, GeneratorSettings settings, out int count)
        {
            foreach (PieceAnnotation piece in style.Pieces("steps")) {
                if (piece.height <= 0f) {
                    continue;
                }

                int flights = Mathf.RoundToInt(rise / piece.height);

                if (flights >= 1 && Mathf.Abs(rise - flights * piece.height) <= settings.SizeTolerance) {
                    count = flights;
                    return piece;
                }
            }

            count = 0;
            return null;
        }

        /// <summary>
        /// How many rows of tiles a landing needs so that no tile of the
        /// street's grid kept beyond it reaches back over the steps: its depth
        /// must be at least a tile's diagonal (a grid tile reaches half a
        /// diagonal from its middle, and the grid's tiles are dropped up to half
        /// a diagonal short of the landing's far edge).
        /// </summary>
        internal static int LandingRows(BuildingStyle style, GeneratorSettings settings)
        {
            float pitch = style.TileDepth - settings.GroundOverlap;
            return pitch > 0f ? Mathf.Max(1, Mathf.CeilToInt((TileDiagonal(style) - settings.GroundOverlap) / pitch)) : 1;
        }

        /// <summary>
        /// A landing's depth along the street: its rows less their overlaps.
        /// </summary>
        internal static float LandingDepth(BuildingStyle style, GeneratorSettings settings)
        {
            return LandingRows(style, settings) * (style.TileDepth - settings.GroundOverlap) + settings.GroundOverlap;
        }

        /// <summary>
        /// The diagonal of the style's tile.
        /// </summary>
        internal static float TileDiagonal(BuildingStyle style)
        {
            return Mathf.Sqrt(style.TileWidth * style.TileWidth + style.TileDepth * style.TileDepth);
        }

        /// <summary>
        /// Every flight of steps the spec's streets need, in the spec's order:
        /// each rising segment of a street of ground tiles too steep to slope
        /// (StreetSlopes.IsSlope) whose style has a steps piece that makes the
        /// rise, with a RampCollider measured in the catalogue, and that is
        /// long enough for the flights and their landing (the validator
        /// reports the rest).
        /// </summary>
        internal static List<Flight> Find(List<StreetSpec> streets, Dictionary<string, BuildingStyle> styles, PiecePlacer placer,
            GeneratorSettings settings)
        {
            List<Flight> flights = new();

            foreach (StreetSpec street in streets) {
                if (!styles.TryGetValue(street.style ?? "", out BuildingStyle style) || style.TileCategory != "ground") {
                    continue;
                }

                for (int i = 0; i + 1 < street.points.Count; i++) {
                    // A rise gentle enough to slope is a slope, never steps.
                    if (!Rises(street, i, settings) || StreetSlopes.IsSlope(street, i, style, settings)) {
                        continue;
                    }

                    float from = street.points[i].y;
                    float to = street.points[i + 1].y;
                    PieceAnnotation piece = Choose(style, Mathf.Abs(to - from), settings, out int count);
                    CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

                    if (entry == null || entry.ramps.Count == 0) {
                        continue;
                    }

                    Vector2 a = new(street.points[i].x, street.points[i].z);
                    Vector2 b = new(street.points[i + 1].x, street.points[i + 1].z);
                    float length = (b - a).magnitude;
                    // Each flight's depth along the street: its render box
                    // along the way up, so the stacked flights' steps meet.
                    float depth = Span(entry, Way(entry));
                    float landing = LandingDepth(style, settings);
                    float block = count * depth + landing;

                    if (length < settings.MinSegmentLength || block > length) {
                        continue;
                    }

                    float up = to > from ? 1f : -1f;
                    // The flights and the landing together, centred on the segment.
                    float front = up > 0f ? (length - block) * 0.5f : length - (length - block) * 0.5f;
                    flights.Add(new Flight {
                        Street = street,
                        Segment = i,
                        Piece = piece,
                        Entry = entry,
                        Count = count,
                        Start = a,
                        Axis = (b - a) / length,
                        Length = length,
                        Width = street.SegmentWidth(i),
                        Up = up,
                        Low = Mathf.Min(from, to),
                        High = Mathf.Max(from, to),
                        Front = front,
                        Top = front + up * count * depth,
                        Landing = landing,
                        LandingRows = LandingRows(style, settings)
                    });
                }
            }

            return flights;
        }

        /// <summary>
        /// The flight of a street's segment, or null if it has none.
        /// </summary>
        internal static Flight Of(List<Flight> flights, StreetSpec street, int segment)
        {
            foreach (Flight flight in flights) {
                if (flight.Street == street && flight.Segment == segment) {
                    return flight;
                }
            }

            return null;
        }

        /// <summary>
        /// The way up a steps piece's flight, level, in its own space.
        /// </summary>
        internal static Vector3 Way(CatalogueEntry entry)
        {
            Vector3 way = entry.ramps[0].direction;
            way.y = 0f;
            return way.normalized;
        }

        /// <summary>
        /// The lowest and highest a piece's render box reaches along a level
        /// axis of its own space.
        /// </summary>
        internal static void Reach(CatalogueEntry entry, Vector3 axis, out float low, out float high)
        {
            low = Mathf.Min(axis.x * entry.renderMin.x, axis.x * entry.renderMax.x) + Mathf.Min(axis.z * entry.renderMin.z, axis.z * entry.renderMax.z);
            high = Mathf.Max(axis.x * entry.renderMin.x, axis.x * entry.renderMax.x) + Mathf.Max(axis.z * entry.renderMin.z, axis.z * entry.renderMax.z);
        }

        /// <summary>
        /// How far a piece's render box spans along a level axis of its own space.
        /// </summary>
        internal static float Span(CatalogueEntry entry, Vector3 axis)
        {
            Reach(entry, axis, out float low, out float high);
            return high - low;
        }
    }
}
