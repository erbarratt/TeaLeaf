using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Anything a hand visual snaps onto when grabbed - ledges now, later
    /// ladder rungs, ropes, door handles, tools and props. Each target
    /// answers "given this hand and where it grabbed me, where does the hand
    /// go, how is it rotated, and which pose does it play?", so the hand code
    /// never special-cases what it's holding.
    /// </summary>
    public interface IHandSnapTarget
    {
        /// <summary>
        /// Builds the snap pose for a hand grabbing this target at grabPoint
        /// (world space, e.g. where the hand ray hit). isLeftHand matters
        /// because the right hand visual is the left one mirrored, so the two
        /// hands need different offsets to line up.
        /// </summary>
        HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint);
    }
}
