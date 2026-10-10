using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// The front of a chest's lid: where a hand takes hold to lift it. A
    /// trigger grab volume on the Interactable layer, a child of the lid
    /// at the middle of its front edge (so it rises with the lid), with
    /// the chest's axes. Targeted by the hand ray at the short reach, like
    /// a door handle; the hand snaps onto it.
    ///
    /// It only marks the place. The lid itself is the Chest's, and the
    /// hand's half is Player.PlayerHandDoors.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ChestLid : MonoBehaviour, IHandTarget, IHandSnapTarget
    {
        // The chest this is the lid of. Found on a parent if left empty.
        [SerializeField] private Chest chest;

        // How the hand sits on the lid. With none, the hand sits exactly
        // on the grip frame.
        [SerializeField] private HandSnapProfile snapProfile;

        private BoxCollider _boxCollider;

        public Chest Chest => chest;

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();

            if (chest == null) {
                chest = GetComponentInParent<Chest>();
            }
        }

        private void OnEnable()
        {
            HandTargetRegistry.Register(_boxCollider, this);
        }

        private void OnDisable()
        {
            HandTargetRegistry.Unregister(_boxCollider);
        }

        /// <summary>
        /// Where the hand grips: this object's position, on the lid's
        /// front edge.
        /// </summary>
        public Vector3 GetGripPoint()
        {
            return transform.position;
        }

        /// <summary>
        /// Where the hand visual goes to hold the lid: on the grip point,
        /// facing into the chest, turned with the lid. The interface's
        /// version - where the hand or head is makes no difference here.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint, Vector3 headPosition, Vector3 headForward)
        {
            return GetSnapPose(isLeftHand);
        }

        /// <summary>
        /// The same, for the holder to call every frame as the lid moves.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand)
        {
            Vector3 gripPosition = GetGripPoint();
            Quaternion gripRotation = Quaternion.LookRotation(-transform.forward, transform.up);

            return snapProfile == null
                ? new HandSnapPose(gripPosition, gripRotation, default)
                : snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }
    }
}
