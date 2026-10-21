using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Shared tuning for one kind of hand snap (ledge grip, ladder rung, ...).
    /// A grab target works out a "grip frame" - where the grip is and which
    /// way it faces, in world terms that know nothing about the hand model -
    /// and this profile converts that into where the hand visual's root
    /// actually goes. The offsets belong to the hand model and pose, not to
    /// any one ledge, so every ClimbableEdge references the same asset: tune
    /// it once in the Inspector (in Play Mode, while gripping) and every
    /// ledge updates.
    ///
    /// Left and right offsets are separate because the right hand visual is
    /// the left hand mirrored (scale.x -1), so one set of numbers can't line
    /// both up.
    /// </summary>
    [CreateAssetMenu(fileName = "HandSnapProfile", menuName = "TeaLeaf/Hand Snap Profile")]
    public class HandSnapProfile : ScriptableObject
    {
        [Tooltip("The finger pose the hand plays while snapped to a target that uses this profile. Every target of one kind shares the profile, so they all show the same pose.")]
        [SerializeField] private HandPose pose;

        [Header("Left Hand")]

        // Offset of the hand visual's root from the grip frame, in the grip
        // frame's own axes (x along the edge, y up, z into the wall for a
        // ledge) - so it stays correct whichever way a ledge faces.
        [Tooltip("Where the left hand visual's root sits relative to the target's grip frame, in metres along the frame's own axes (X along the edge, Y up, Z into the wall for a ledge). Zero puts the root exactly on the grip frame.")]
        [SerializeField] private Vector3 leftPositionOffset;

        [Tooltip("How the left hand visual's root is turned from the grip frame, as Euler angles in degrees about the frame's own axes. Zero leaves it facing the way the frame does.")]
        [SerializeField] private Vector3 leftRotationOffset;

        [Header("Right Hand")]
        [Tooltip("The same offset for the right hand visual, in metres along the grip frame's axes. Separate from the left hand's because the right hand visual is the left one mirrored, so one set of numbers can't line both up.")]
        [SerializeField] private Vector3 rightPositionOffset;
        [Tooltip("The same rotation for the right hand visual, as Euler angles in degrees about the grip frame's axes. Separate from the left hand's because the right hand visual is mirrored.")]
        [SerializeField] private Vector3 rightRotationOffset;

        /// <summary>
        /// Converts a target's grip frame (world position + rotation) into
        /// the final snap pose for the given hand. Only called on grab, so
        /// Quaternion.Euler here isn't a per-frame cost.
        /// </summary>
        public HandSnapPose Apply(bool isLeftHand, Vector3 gripPosition, Quaternion gripRotation)
        {
            Vector3 positionOffset = isLeftHand ? leftPositionOffset : rightPositionOffset;
            Vector3 rotationOffset = isLeftHand ? leftRotationOffset : rightRotationOffset;

            // Rotating the offset by gripRotation is what makes it "local" to
            // the grip frame: +z always means "into the wall", not world +z.
            Vector3 position = gripPosition + gripRotation * positionOffset;
            Quaternion rotation = gripRotation * Quaternion.Euler(rotationOffset);

            return new HandSnapPose(position, rotation, pose);
        }
    }
}
