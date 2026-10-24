using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Shared tuning for one kind of hand snap (ledge grip, ladder rung, a
    /// tankard, ...). A grab target works out a "grip frame" - where the grip
    /// is and which way it faces, in world terms that know nothing about the
    /// hand model - and this profile converts that into where the hand
    /// visual's root actually goes, and how its fingers are posed. The
    /// offsets belong to the hand model and pose, not to any one ledge, so
    /// every ClimbableEdge references the same asset: tune it once and every
    /// ledge updates. TeaLeaf > Modify Prefab > Pose with Hand poses a prop's
    /// profile in the editor; the rest are tuned in the Inspector.
    ///
    /// Left and right offsets are separate because the right hand visual is
    /// the left hand mirrored (scale.x -1), so one set of numbers can't line
    /// both up. The finger pose is shared: the mirrored hand's bones have the
    /// same local rotations as the left's.
    /// </summary>
    [CreateAssetMenu(fileName = "HandSnapProfile", menuName = "TeaLeaf/Hand Snap Profile")]
    public class HandSnapProfile : ScriptableObject
    {
        /// <summary>
        /// The bones a finger pose holds, in the order fingerRotations
        /// stores them: thumb (four joints), then index, middle, ring and
        /// pinky (three each). Named as in hand.fbx; the mirrored right hand
        /// visual has the same names.
        /// </summary>
        public static readonly string[] FingerBoneNames = {
            "J_Left_HandThumb1", "J_Left_HandThumb2", "J_Left_HandThumb3", "J_Left_HandThumb4",
            "J_Left_HandIndex1", "J_Left_HandIndex2", "J_Left_HandIndex3",
            "J_Left_HandMiddle1", "J_Left_HandMiddle2", "J_Left_HandMiddle3",
            "J_Left_HandRing1", "J_Left_HandRing2", "J_Left_HandRing3",
            "J_Left_HandPinky1", "J_Left_HandPinky2", "J_Left_HandPinky3"
        };

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

        [Header("Fingers")]
        [Tooltip("The finger pose, one local rotation per bone in FingerBoneNames order (thumb, index, middle, ring, pinky). Set by TeaLeaf > Modify Prefab > Pose with Hand. Empty leaves the fingers on their input curl.")]
        [SerializeField] private Quaternion[] fingerRotations = new Quaternion[0];

        /// <summary>
        /// True if the profile holds a whole finger pose: one rotation for
        /// every bone in FingerBoneNames.
        /// </summary>
        public bool HasFingerPose => fingerRotations != null && fingerRotations.Length == FingerBoneNames.Length;

        /// <summary>
        /// The local rotation of finger bone i (FingerBoneNames order). Only
        /// valid while HasFingerPose.
        /// </summary>
        public Quaternion FingerRotation(int i)
        {
            return fingerRotations[i];
        }

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

            return new HandSnapPose(position, rotation, this);
        }

        /// <summary>
        /// Apply() the other way round: where the grip frame is for a hand
        /// visual at a pose - for something held in the hand (bow, arrow,
        /// pack) that the hand places, rather than the hand being placed
        /// by it. A null profile puts the grip frame on the hand.
        /// Quaternion.Euler here is cheap enough to call every frame.
        /// </summary>
        public static void GripFrameInHand(HandSnapProfile profile, bool isLeftHand, Vector3 handPosition, Quaternion handRotation, out Vector3 gripPosition, out Quaternion gripRotation)
        {
            if (profile == null) {
                gripPosition = handPosition;
                gripRotation = handRotation;
                return;
            }

            Vector3 positionOffset = isLeftHand ? profile.leftPositionOffset : profile.rightPositionOffset;
            Vector3 rotationOffset = isLeftHand ? profile.leftRotationOffset : profile.rightRotationOffset;

            // Apply() has hand = grip + gripRotation * offset and
            // handRotation = gripRotation * turn; solved for the grip.
            gripRotation = handRotation * Quaternion.Inverse(Quaternion.Euler(rotationOffset));
            gripPosition = handPosition - gripRotation * positionOffset;
        }

        /// <summary>
        /// Puts a held thing on a hand visual where its profile says (its
        /// own origin and axes are the grip frame), as a child of the
        /// visual so nothing places it per frame. Its world pose is set
        /// first under an unscaled parent, then kept as it's parented: under
        /// the mirrored right visual Unity gives it a cancelling mirrored
        /// local scale, so it's never drawn back to front (as carried props).
        /// </summary>
        public static void PlaceInHand(HandSnapProfile profile, bool isLeftHand, Transform held, Transform handVisual, Transform unscaledParent)
        {
            GripFrameInHand(profile, isLeftHand, handVisual.position, handVisual.rotation, out Vector3 position, out Quaternion rotation);
            held.SetParent(unscaledParent, false);
            held.localScale = Vector3.one;
            held.SetPositionAndRotation(position, rotation);
            held.SetParent(handVisual, true);
        }

        /// <summary>
        /// Finds a hand visual's finger bones by name, in FingerBoneNames
        /// order (an entry is null if the model lacks that bone). Searches
        /// the whole hierarchy, so call it once at load, never per frame.
        /// </summary>
        public static Transform[] FindFingerBones(Transform handVisual)
        {
            Transform[] all = handVisual.GetComponentsInChildren<Transform>(true);
            Transform[] bones = new Transform[FingerBoneNames.Length];

            for (int i = 0; i < bones.Length; i++) {
                for (int j = 0; j < all.Length; j++) {
                    if (all[j].name == FingerBoneNames[i]) {
                        bones[i] = all[j];
                        break;
                    }
                }
            }

            return bones;
        }
    }
}
