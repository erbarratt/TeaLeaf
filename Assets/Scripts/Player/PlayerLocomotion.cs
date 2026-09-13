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

        /// Handles grabbing and climb movement. Ticked explicitly from
        /// Update() below, rather than running its own Update(), so it's
        /// clear exactly when climbing runs each frame.
        [SerializeField] private PlayerClimbing playerClimbing;

        /// Handles hand-ray highlighting of interactable objects. Ticked
        /// explicitly for the same reason as playerClimbing.
        [SerializeField] private PlayerHandInteraction playerHandInteraction;

        /// Drives hand finger-curl animation. Ticked explicitly, after
        /// playerClimbing, so it sees this frame's grab state rather than
        /// last frame's.
        [SerializeField] private PlayerHandAnimation playerHandAnimation;

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

        /// Current movement vector accumulated during this frame.
        private Vector3 _frameMovement;

        /// Whether the crouch button has been toggled into the crouched state.
        private bool _isCrouching;

        /// CharacterController height while standing, captured at startup so the
        /// crouch system always has a standing height to return to, whatever
        /// height was set up in the Inspector.
        private float _standingHeight;

        /// Whether the player was climbing last frame, so we can detect the
        /// moment a climb starts and reset vertical velocity - see Update().
        private bool _wasClimbing;

        private void Awake()
        {
            _standingHeight = characterController.height;
        }

        private void Update()
        {

            UpdateCharacterControllerCentre();
            HandleCrouch();
            playerHandInteraction.Tick();
            playerClimbing.Tick();
            playerHandAnimation.Tick();

            bool isClimbing = playerClimbing.IsClimbing;

            _frameMovement = Vector3.zero;

            if (isClimbing) {

                // A climb starting mid-fall shouldn't carry the fall speed
                // through to whenever the player lets go again - resetting
                // here means release always resumes falling from rest.
                if (!_wasClimbing) {
                    _verticalVelocity = 0f;
                }

                _frameMovement += playerClimbing.FrameMovement;

            } else {
                HandleMovement();
                HandleGravity();
            }

            // Turning always works, even mid-climb.
            HandleTurning();

            _wasClimbing = isClimbing;

            Vector3 positionBeforeMove = playerTransform.position;
            characterController.Move(_frameMovement);

            // Only while climbing - otherwise this frame's actual movement
            // came from HandleMovement()/HandleGravity(), not FrameMovement,
            // and reporting it back would corrupt PlayerClimbing's own
            // tracking. See PlayerClimbing.ReportAppliedMovement() for why
            // this matters: characterController.Move() doesn't always apply
            // the full amount requested, and PlayerClimbing needs to know
            // exactly how much of it landed to avoid drifting.
            if (isClimbing) {
                playerClimbing.ReportAppliedMovement(playerTransform.position - positionBeforeMove);
            }

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
