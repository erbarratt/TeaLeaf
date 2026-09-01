using UnityEngine;

namespace Player
{
    /// <summary>
    /// Central access point for all VR tracked transforms.
    ///
    /// Other systems should ask THIS class for head and hand positions,
    /// rather than searching through the XR Rig hierarchy themselves.
    ///
    /// This means if Unity changes its XR hierarchy later,
    /// we only need to update one script.
    /// </summary>
    public class PlayerTracking : MonoBehaviour
    {
        [Header("Tracked XR Objects")]

        // Reference to the player's VR headset camera.
        // This transform moves and rotates with the user's head.
        [SerializeField] private Transform head;

        // Reference to the left controller transform.
        // Updated automatically by OpenXR every frame.
        [SerializeField] private Transform leftHand;

        // Reference to the right controller transform.
        // Updated automatically by OpenXR every frame.
        [SerializeField] private Transform rightHand;

        // TRANSFORM ACCESS
        //
        // Sometimes another system needs the entire Transform object.
        // For example:
        //
        // sword.transform.position = tracking.RightHand.position;
        //
        // So we expose the transforms directly.

            public Transform Head => head;
            public Transform LeftHand => leftHand;
            public Transform RightHand => rightHand;
        
        // POSITION ACCESS
        // Most gameplay code only needs positions.
        
        // Example:
        //
        // Distance between hand and loot item.
        //
        // float distance =
        //     Vector3.Distance(
        //         tracking.RightHandPosition,
        //         loot.transform.position);

            public Vector3 HeadPosition => head.position;
            public Vector3 LeftHandPosition => leftHand.position;
            public Vector3 RightHandPosition => rightHand.position;
        
        /// <summary>
        /// Position of the PlayerRig in world space.
        ///
        /// Useful for:
        /// - AI perception
        /// - Footstep sounds
        /// - Save/load systems
        /// - Trigger volumes
        /// </summary>
            public Vector3 PlayerPosition => transform.position;

        // ROTATION ACCESS
        //
        // Useful for:
        //
        // - Bow aiming
        // - Flashlight direction
        // - Throwing calculations
        // - Door handle orientation

            public Quaternion HeadRotation => head.rotation;
            public Quaternion LeftHandRotation => leftHand.rotation;
            public Quaternion RightHandRotation => rightHand.rotation;
        
        /// <summary>
        /// Rotation of the PlayerRig in world space.
        /// </summary>
        public Quaternion PlayerRotation => transform.rotation;
        
    }
}