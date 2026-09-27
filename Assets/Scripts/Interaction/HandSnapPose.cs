using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Where a hand visual goes when it snaps to a grab target: a world-space
    /// position and rotation for the hand visual's root, plus the finger pose
    /// to play. A readonly struct so passing one around never allocates and
    /// nothing can change it after the target has built it.
    /// </summary>
    public readonly struct HandSnapPose
    {
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public HandPose Pose { get; }

        public HandSnapPose(Vector3 position, Quaternion rotation, HandPose pose)
        {
            Position = position;
            Rotation = rotation;
            Pose = pose;
        }
    }
}
