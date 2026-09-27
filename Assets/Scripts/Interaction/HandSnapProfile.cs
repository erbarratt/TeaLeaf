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
        [SerializeField] private HandPose pose;

        [Header("Left Hand")]

        // Offset of the hand visual's root from the grip frame, in the grip
        // frame's own axes (x along the edge, y up, z into the wall for a
        // ledge) - so it stays correct whichever way a ledge faces.
        [SerializeField] private Vector3 leftPositionOffset;

        // Euler rotation from the grip frame to the hand visual's root.
        [SerializeField] private Vector3 leftRotationOffset;

        [Header("Right Hand")]
        [SerializeField] private Vector3 rightPositionOffset;
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
