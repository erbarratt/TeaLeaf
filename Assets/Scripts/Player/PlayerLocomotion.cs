using System;
using UnityEngine;

namespace Player
{
    /// Handles player movement and turning.
    /// This class will eventually be responsible for:
    /// - Smooth locomotion
    /// - Snap turning
    /// - Smooth turning
    /// - Gravity
    public class PlayerLocomotion : MonoBehaviour
    {
        
        /// CharacterController used to move the player while respecting collisions.
        [SerializeField] private CharacterController characterController;

        /// Movement speed in metres per second.
        [SerializeField] private float moveSpeed = 3f;
        
        /// Reference to the central XR input system.
        /// All gameplay code should get controller input through this class rather than
        /// reading Input Actions directly.
        [SerializeField] private PlayerInputXR playerInput;
        
        /// The transform whose Y rotation defines the player's movement direction.
        /// We use the rig root rather than the headset so that looking left/right does
        /// not change which direction "forward" moves the player.
        [SerializeField] private Transform playerTransform;
        
        //main switch for smooth turn
        [SerializeField] private bool useSmoothTurn;
        
        /// Degrees per second when smooth turning.
        [SerializeField] private float smoothTurnSpeed = 90f;
        
        /// Degrees rotated each time a snap turn occurs.
        [SerializeField] private float snapTurnAngle = 45f;
        
        [SerializeField] private float snapTurnThreshold = 0.8f;
        
        /// Minimum stick movement required before movement input is accepted.
        [SerializeField] private float moveDeadzone = 0.2f;

        /// Minimum stick movement required before turn input is accepted.
        [SerializeField] private float turnDeadzone = 0.2f;
        
        /// Downward acceleration in metres per second squared.
        [SerializeField] private float gravity = -9.81f;

        /// Small downward force applied while grounded to keep the CharacterController
        /// reliably attached to the ground.
        [SerializeField] private float groundedGravity = -2f;
        
        /// Camera used to determine the player's real-world position and height.
        [SerializeField] private Transform cameraTransform;

        /// Minimum allowed CharacterController height.
        [SerializeField] private float minimumHeight = 1f;

        /// Additional skin width applied to prevent the capsule clipping into the floor.
        [SerializeField] private float heightPadding = 0.1f;
        
        /// Prevents repeated snap turns while the stick remains held.
        private bool _snapTurnQueued;
        
        /// Current vertical movement speed in metres per second.
        private float _verticalVelocity;
        
        /// Current movement vector accumulated during this frame.
        private Vector3 _frameMovement;
        
        private void Update()
        {
            
            UpdateCharacterControllerCentre();
            
            _frameMovement = Vector3.zero;
            
            HandleMovement();
            HandleTurning();
            HandleGravity();
            
            characterController.Move(_frameMovement);
            
        }
        
        /// <summary>
        /// Reads movement input from the left thumbstick and moves the player using
        /// the CharacterController. Movement is relative to the PlayerRig's orientation
        /// rather than the headset orientation.
        /// </summary>
        private void HandleMovement()
        {
            Vector2 moveInput = playerInput.MoveAxis;

            // Ignore tiny thumbstick movements and controller noise.
            if (moveInput.magnitude < moveDeadzone)
            {
                return;
            }

            // Convert 2D stick input into a world-space movement direction based on
            // the PlayerRig orientation.
            Vector3 movement =
                playerTransform.forward * moveInput.y +
                playerTransform.right * moveInput.x;

            // Prevent diagonal movement from being faster than straight movement.
            movement = Vector3.ClampMagnitude(movement, 1f);

            _frameMovement += movement * (moveSpeed * Time.deltaTime);
            
        }
        
        /// <summary>
        /// Rotates the player using either smooth turning or snap turning depending
        /// on the current locomotion settings.
        /// </summary>
        private void HandleTurning()
        {
            float turnInput = playerInput.TurnAxis.x;

            // Ignore tiny thumbstick movements and controller noise.
            if (Mathf.Abs(turnInput) < turnDeadzone){
                _snapTurnQueued = false;

                if (!useSmoothTurn) {
                    return;
                }

                turnInput = 0f;
            }

            if (useSmoothTurn){
                
                // Rotate continuously while the stick is held.
                playerTransform.Rotate(
                    Vector3.up,
                    turnInput * smoothTurnSpeed * Time.deltaTime,
                    Space.World);
                
            } else {
                
                // Trigger a single snap turn once the stick passes the threshold.
                if (Mathf.Abs(turnInput) > snapTurnThreshold && !_snapTurnQueued){
                    float angle = turnInput > 0f
                        ? snapTurnAngle
                        : -snapTurnAngle;

                    playerTransform.Rotate(
                        Vector3.up,
                        angle,
                        Space.World);

                    _snapTurnQueued = true;
                }

                // Require the stick to return near centre before allowing another snap.
                if (Mathf.Abs(turnInput) < turnDeadzone){
                    _snapTurnQueued = false;
                }
                
            }
        }
        
        /// <summary>
        /// Applies gravity to the player and moves the CharacterController vertically.
        /// </summary>
        private void HandleGravity()
        {
            if (characterController.isGrounded && _verticalVelocity < 0f) {
                _verticalVelocity = groundedGravity;
            }

            _verticalVelocity += gravity * Time.deltaTime;

            _frameMovement += Vector3.up * (_verticalVelocity * Time.deltaTime);
            
        }
        
        /// <summary>
        /// Keeps the CharacterController centred underneath the player's headset
        /// in the X/Z plane. The controller height is fixed and controlled by
        /// gameplay systems such as crouching.
        /// </summary>
        private void UpdateCharacterControllerCentre()
        {
            Vector3 centre = characterController.center;

            centre.x = cameraTransform.localPosition.x;
            centre.z = cameraTransform.localPosition.z;

            characterController.center = centre;
        }
        
    }
}