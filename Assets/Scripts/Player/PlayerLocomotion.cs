using System;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Handles the player's own locomotion: thumbstick movement, gravity,
    /// snap/smooth turning, crouch height, and keeping the
    /// CharacterController centred under the headset.
    ///
    /// Doesn't run its own Update() and never calls
    /// characterController.Move() itself. PlayerController calls TickBody(),
    /// TickMovement() and TickTurning() at fixed points in its frame order,
    /// and applies the movement TickMovement() returns in its single Move()
    /// call alongside every other system's contribution.
    /// </summary>
    public class PlayerLocomotion : MonoBehaviour
    {

        /// CharacterController used to move the player while respecting collisions.
        [SerializeField] private CharacterController characterController;

        /// Camera used to determine the player's real-world position and height.
        [SerializeField] private Transform cameraTransform;

        /// The XR Origin's "Camera Offset" transform - the parent of the tracked
        /// head/hand transforms. Crouching here is button-driven rather than
        /// detected from the player's real-world headset height, so we lower
        /// this transform to move the whole tracked hierarchy down with the
        /// CharacterController rather than trying to move the camera itself,
        /// which is driven by tracking and can't be positioned directly.
        [SerializeField] private Transform cameraOffsetTransform;

        /// Reference to the central XR input system.
        /// All gameplay code should get controller input through this class rather than
        /// reading Input Actions directly.
        [SerializeField] private PlayerInputXR playerInput;

        /// The transform whose Y rotation defines the player's movement direction.
        /// We use the rig root rather than the headset so that looking left/right does
        /// not change which direction "forward" moves the player.
        [SerializeField] private Transform playerTransform;

        [Header("Turning")]

        /// main switch for smooth turn
        [SerializeField] private bool useSmoothTurn;

        /// Degrees per second when smooth turning.
        [SerializeField] private float smoothTurnSpeed = 90f;

        /// Degrees rotated each time a snap turn occurs.
        [SerializeField] private float snapTurnAngle = 45f;

        [SerializeField] private float snapTurnThreshold = 0.8f;

        /// Minimum stick movement required before turn input is accepted.
        [SerializeField] private float turnDeadzone = 0.2f;

        [Header("Movement")]

        /// Movement speed in metres per second.
        [SerializeField] private float moveSpeed = 3f;

        /// Minimum stick movement required before movement input is accepted.
        [SerializeField] private float moveDeadzone = 0.2f;


        [Header("Gravity")]

        /// Downward acceleration in metres per second squared.
        [SerializeField] private float gravity = -9.81f;

        /// Small downward force applied while grounded to keep the CharacterController
        /// reliably attached to the ground.
        [SerializeField] private float groundedGravity = -2f;

        [Header("Height")]

        /// Minimum allowed CharacterController height. Doubles as the crouched
        /// height target, since the fully-crouched height should never be
        /// shorter than the CharacterController can safely be.
        [SerializeField] private float minimumHeight = 1f;

        /// Additional skin width applied to prevent the capsule clipping into the floor.
        [SerializeField] private float heightPadding = 0.1f;

        [Header("Crouch")]

        /// Metres per second the CharacterController's height changes by while
        /// transitioning between standing and crouching.
        [SerializeField] private float crouchTransitionSpeed = 2f;

        /// Prevents repeated snap turns while the stick remains held.
        private bool _snapTurnQueued;

        /// Current vertical movement speed in metres per second.
        private float _verticalVelocity;

        /// Whether the crouch button has been toggled into the crouched state.
        private bool _isCrouching;

        /// CharacterController height while standing, captured at startup so the
        /// crouch system always has a standing height to return to, whatever
        /// height was set up in the Inspector.
        private float _standingHeight;

        /// Whether the player was climbing last frame, so we can detect the
        /// moment a climb starts and reset vertical velocity - see
        /// TickMovement().
        private bool _wasClimbing;

        private void Awake()
        {
            _standingHeight = characterController.height;
        }

        /// <summary>
        /// Updates the CharacterController's shape for this frame: re-centres
        /// it under the headset and applies any crouch height transition.
        /// Called by PlayerController before the hand systems tick, since
        /// crouching moves the whole tracked hierarchy (hands included).
        /// </summary>
        public void TickBody()
        {
            UpdateCharacterControllerCentre();
            HandleCrouch();
        }

        /// <summary>
        /// Returns this frame's locomotion movement (thumbstick + gravity)
        /// for PlayerController to add to its frame movement. Returns zero
        /// while climbing, since the hands move the player then - but still
        /// needs to be called every frame so it can spot the moment a climb
        /// starts.
        /// </summary>
        public Vector3 TickMovement(bool isClimbing)
        {
            bool climbStarted = isClimbing && !_wasClimbing;
            _wasClimbing = isClimbing;

            if (isClimbing) {

                // A climb starting mid-fall shouldn't carry the fall speed
                // through to whenever the player lets go again - resetting
                // here means release always resumes falling from rest.
                if (climbStarted) {
                    _verticalVelocity = 0f;
                }

                return Vector3.zero;
            }

            return HandleMovement() + HandleGravity();
        }

        /// <summary>
        /// Applies snap or smooth turning for this frame. Called by
        /// PlayerController every frame, including while climbing.
        /// </summary>
        public void TickTurning()
        {
            HandleTurning();
        }

        /// <summary>
        /// Reads movement input from the left thumbstick and returns this frame's
        /// horizontal movement. Movement is relative to the Player root's orientation
        /// rather than the headset orientation.
        /// </summary>
        private Vector3 HandleMovement()
        {
            Vector2 moveInput = playerInput.MoveAxis;

            // Ignore tiny thumbstick movements and controller noise.
            if (moveInput.magnitude < moveDeadzone) {
                return Vector3.zero;
            }

            // Convert 2D stick input into a world-space movement direction based on
            // the Player root orientation.
            Vector3 movement =
                playerTransform.forward * moveInput.y +
                playerTransform.right * moveInput.x;

            // Prevent diagonal movement from being faster than straight movement.
            movement = Vector3.ClampMagnitude(movement, 1f);

            return movement * (moveSpeed * Time.deltaTime);
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
        /// Applies gravity to the vertical velocity and returns this frame's
        /// vertical movement.
        /// </summary>
        private Vector3 HandleGravity()
        {
            if (characterController.isGrounded && _verticalVelocity < 0f) {
                _verticalVelocity = groundedGravity;
            }

            _verticalVelocity += gravity * Time.deltaTime;

            return Vector3.up * (_verticalVelocity * Time.deltaTime);
        }

        /// <summary>
        /// Toggles crouching on each crouch button press, then smoothly moves the
        /// CharacterController's height towards the standing or crouched target
        /// height. The controller's vertical centre and the camera offset are
        /// both kept in sync with the current height every frame, so the
        /// transition reads as continuous rather than snapping at the end.
        /// </summary>
        private void HandleCrouch()
        {
            if (playerInput.CrouchPressed) {
                _isCrouching = !_isCrouching;
            }

            float targetHeight = _isCrouching ? minimumHeight : _standingHeight;

            // Already at the target height - nothing to transition, so skip the
            // centre/camera offset work below until the next crouch toggle.
            if (Mathf.Approximately(characterController.height, targetHeight)) {
                return;
            }

            float previousHeight = characterController.height;

            characterController.height = Mathf.MoveTowards(
                characterController.height,
                targetHeight,
                crouchTransitionSpeed * Time.deltaTime);

            Vector3 centre = characterController.center;
            centre.y = characterController.height / 2f + heightPadding;
            characterController.center = centre;

            // Nudge the tracked head/hands down by however much the collider
            // height just changed, on top of whatever base offset is already
            // there (e.g. the XR Origin's Camera Y Offset for Device tracking
            // mode) - rather than overwriting it - so the player's view drops
            // even though they haven't physically crouched.
            float heightDelta = characterController.height - previousHeight;
            cameraOffsetTransform.localPosition += Vector3.up * heightDelta;
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
