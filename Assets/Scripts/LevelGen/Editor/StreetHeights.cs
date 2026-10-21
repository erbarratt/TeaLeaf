using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// How the streets change height: their flights of steps (StreetSteps)
    /// and their slopes (StreetSlopes), found once a generation and handed to
    /// everything that needs the street's surface - the ground, the facades,
    /// the clearance checks, the street test and the visibility cull - so
    /// they all agree on where the street is.
    /// </summary>
    internal class StreetHeights
    {
        internal List<StreetSteps.Flight> Flights { get; }
        internal List<StreetSlopes.Slope> Slopes { get; }

        private StreetHeights(List<StreetSteps.Flight> flights, List<StreetSlopes.Slope> slopes)
        {
            Flights = flights;
            Slopes = slopes;
        }

        /// <summary>
        /// Every flight of steps and every slope the spec's streets need.
        /// </summary>
        internal static StreetHeights Find(List<StreetSpec> streets, Dictionary<string, BuildingStyle> styles, PiecePlacer placer,
            GeneratorSettings settings)
        {
            return new StreetHeights(StreetSteps.Find(streets, styles, placer, settings), StreetSlopes.Find(streets, styles, settings));
        }

        /// <summary>
        /// Nothing: every street level (for a builder working without a spec).
        /// </summary>
        internal static StreetHeights None => new(new List<StreetSteps.Flight>(), new List<StreetSlopes.Slope>());

        /// <summary>
        /// The flight of steps on a street's segment, or null.
        /// </summary>
        internal StreetSteps.Flight FlightOf(StreetSpec street, int segment)
        {
            return StreetSteps.Of(Flights, street, segment);
        }

        /// <summary>
        /// The slope on a street's segment, or null.
        /// </summary>
        internal StreetSlopes.Slope SlopeOf(StreetSpec street, int segment)
        {
            return StreetSlopes.Of(Slopes, street, segment);
        }

        /// <summary>
        /// The height of the street's surface at a distance along a segment,
        /// as walked: up a flight's steps, up a slope, else level between the
        /// segment's points (a segment that neither steps nor slopes but
        /// still rises is the validator's error; its height is interpolated).
        /// </summary>
        internal float SurfaceAt(StreetSpec street, int segment, float along)
        {
            StreetSteps.Flight flight = FlightOf(street, segment);

            if (flight != null) {
                return flight.SurfaceAt(along);
            }

            StreetSlopes.Slope slope = SlopeOf(street, segment);

            if (slope != null) {
                return slope.SurfaceAt(along);
            }

            return Interpolated(street, segment, along);
        }

        /// <summary>
        /// The height the ground mesh is laid at, at a distance along a
        /// segment: either side of a flight the level of that side (the
        /// flight's own pieces make the rise), else the surface.
        /// </summary>
        internal float GroundAt(StreetSpec street, int segment, float along)
        {
            StreetSteps.Flight flight = FlightOf(street, segment);
            return flight != null ? flight.HeightAt(along) : SurfaceAt(street, segment, along);
        }

        /// <summary>
        /// The height between a segment's two points at a distance along it.
        /// </summary>
        private static float Interpolated(StreetSpec street, int segment, float along)
        {
            Vector3 a = street.points[segment];
            Vector3 b = street.points[segment + 1];
            float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            return Mathf.Lerp(a.y, b.y, length > 0f ? Mathf.Clamp01(along / length) : 0f);
        }
    }
}
