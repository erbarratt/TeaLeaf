using UnityEngine;

namespace Player
{
    /// <summary>
    /// The path of something thrown or shot: where it is at each moment,
    /// and where it comes down. Shared by aimed throwing
    /// (PlayerHandThrowing) and the crossbow (PlayerCrossbow), which both
    /// show the player the arc before anything leaves the hand.
    ///
    /// Only arithmetic and a few physics rays. Nothing is kept between
    /// calls and nothing is created: the caller owns the array of points.
    /// </summary>
    public static class BallisticArc
    {
        /// <summary>
        /// Where a body is t seconds after leaving start at velocity. The
        /// textbook answer is start + velocity x t + half gravity x t
        /// squared. A body moved by Unity's physics really falls a little
        /// further, because physics moves in steps (fixedStep seconds
        /// each) and adds gravity to the speed BEFORE each move: t x (t +
        /// fixedStep) in place of t squared - about 10cm of difference a
        /// second into the flight. Pass Time.fixedDeltaTime for a body
        /// that physics will move (a thrown prop), so the line is where it
        /// will actually go; pass 0 for one moved along this same formula
        /// (a crossbow bolt).
        /// </summary>
        public static Vector3 Point(Vector3 start, Vector3 velocity, Vector3 gravity, float t, float fixedStep)
        {
            return start + velocity * t + gravity * (0.5f * t * (t + fixedStep));
        }

        /// <summary>
        /// Works out the arc from start at velocity into points, stopping
        /// at the first thing on layers it hits. True if it lands on
        /// something within maxTime seconds of flight; flightTime is then
        /// when, and landingPoint and landingNormal where. Two jobs, at
        /// two levels of detail:
        ///
        /// 1. Finding where it lands: follow the arc in a FEW long
        /// straight pieces (castStep seconds of flight each), one physics
        /// ray per piece. A straight piece cuts the corner of the curve,
        /// but by very little - gravity x step squared / 8, about 1cm for
        /// a 0.1s piece - so long pieces find the landing nearly as
        /// exactly as short ones, with far fewer rays.
        ///
        /// 2. Drawing it: MANY short pieces (drawStep seconds each),
        /// straight from the formula up to the moment it lands - no rays,
        /// just arithmetic, so the line can be as smooth as it likes.
        /// </summary>
        public static bool Compute(Vector3 start, Vector3 velocity, LayerMask layers, float castStep, float maxTime, float drawStep, float fixedStep, Vector3[] points, out int pointCount, out float flightTime, out Vector3 landingPoint, out Vector3 landingNormal)
        {
            landingPoint = default;
            landingNormal = Vector3.up;

            Vector3 gravity = Physics.gravity;
            castStep = Mathf.Max(castStep, 0.02f);
            flightTime = maxTime;

            bool hasLanded = false;
            Vector3 previous = start;

            for (float previousTime = 0f; previousTime < maxTime;) {
                float t = Mathf.Min(previousTime + castStep, maxTime);
                Vector3 point = Point(start, velocity, gravity, t, fixedStep);

                if (Physics.Linecast(previous, point, out RaycastHit hit, layers, QueryTriggerInteraction.Ignore)) {
                    // When it lands: the hit's share of the way along this
                    // piece, as the same share of the piece's time.
                    float pieceLength = (point - previous).magnitude;
                    float share = pieceLength > 0f ? hit.distance / pieceLength : 0f;

                    flightTime = previousTime + (t - previousTime) * share;
                    landingPoint = hit.point;
                    landingNormal = hit.normal;
                    hasLanded = true;
                    break;
                }

                previous = point;
                previousTime = t;
            }

            int last = Mathf.Min(Mathf.CeilToInt(flightTime / drawStep), points.Length - 1);

            for (int i = 0; i < last; i++) {
                points[i] = Point(start, velocity, gravity, i * drawStep, fixedStep);
            }

            // The line ends exactly on what it hit (or, with no landing,
            // wherever the arc had got to when it was given up on).
            points[last] = hasLanded ? landingPoint : Point(start, velocity, gravity, flightTime, fixedStep);
            pointCount = last + 1;

            return hasLanded;
        }
    }
}
