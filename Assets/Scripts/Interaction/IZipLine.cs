using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A climbable that can carry a gripping hand along itself - a zip wire
    /// (ClimbableRope with isZipLine ticked). PlayerClimbing checks whatever
    /// a hand grabs for this: if it's a zip line, the grip slides along the
    /// line - and the player with it - until they let go or it reaches the
    /// end. The line only describes itself (its shape, speed and which way
    /// is "down"); the sliding is PlayerClimbing's job.
    ///
    /// Positions along the line are a fraction t: 0 at its start, 1 at its
    /// far end.
    /// </summary>
    public interface IZipLine
    {
        /// <summary>
        /// Whether this one actually is a zip line - a rope implements the
        /// interface either way, and most aren't.
        /// </summary>
        bool IsZipLine { get; }

        /// <summary>
        /// Top speed of a grip sliding along the line, in metres per second.
        /// </summary>
        float ZipSpeed { get; }

        /// <summary>
        /// How quickly a grip gets up to ZipSpeed from rest, in metres per
        /// second per second.
        /// </summary>
        float ZipAcceleration { get; }

        /// <summary>
        /// The line's length from end to end, in metres.
        /// </summary>
        float Length { get; }

        /// <summary>
        /// World position of the point a fraction t along the line.
        /// </summary>
        Vector3 GetPoint(float t);

        /// <summary>
        /// How far along the line (0-1) the point nearest to worldPoint is.
        /// </summary>
        float GetClosestT(Vector3 worldPoint);

        /// <summary>
        /// Which way a grip slides: +1 towards the far end (t rising), -1
        /// back towards the start. Downhill if the line slopes; on a level
        /// line, whichever way the player is facing (headForward).
        /// </summary>
        int GetZipDirection(Vector3 headForward);
    }
}
