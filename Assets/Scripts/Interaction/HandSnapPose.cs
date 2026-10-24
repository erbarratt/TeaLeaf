using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Where a hand visual goes when it snaps to a grab target: a world-space
    /// position and rotation for the hand visual's root, plus the profile
    /// whose finger pose the hand plays (null: the fingers keep their input
    /// curl). A readonly struct so passing one around never allocates and
    /// nothing can change it after the target has built it.
    /// </summary>
    public readonly struct HandSnapPose
    {
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public HandSnapProfile Profile { get; }

        public HandSnapPose(Vector3 position, Quaternion rotation, HandSnapProfile profile)
        {
            Position = position;
            Rotation = rotation;
            Profile = profile;
        }
    }
}
