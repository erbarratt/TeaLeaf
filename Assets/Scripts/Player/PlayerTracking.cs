using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

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

        // Filled by SubsystemManager.GetSubsystems() in Start() - a reused
        // list, so the lookup doesn't allocate a new one.
        private static readonly List<XRInputSubsystem> _inputSubsystems = new();

        /// <summary>
        /// Puts XR tracking in Device mode (replaced XROrigin 2026-09-30,
        /// which did only this for us, plus setting the height below). Device
        /// mode measures the head from where the headset was at startup (or
        /// the last recentre) rather than from the real floor, and Camera
        /// Offset - saved 1.6m up in the scene - lifts that to standing eye
        /// height. So every player stands at the same height whatever their
        /// real height, while real crouching still lowers the head. Once, at
        /// startup: XR is initialised before the first scene loads, so the
        /// subsystem is already running by Start().
        /// </summary>
        private void Start()
        {
            SubsystemManager.GetSubsystems(_inputSubsystems);
            bool isSet = false;

            foreach (XRInputSubsystem subsystem in _inputSubsystems) {
                isSet |= subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
            }

            if (!isSet) {
                Debug.LogWarning("PlayerTracking: couldn't set Device tracking mode - is a headset connected and XR initialised on startup? Head height may be wrong.", this);
            }
        }

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
        /// Position of the Player root in world space.
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
        /// Rotation of the Player root in world space.
        /// </summary>
        public Quaternion PlayerRotation => transform.rotation;
        
    }
}