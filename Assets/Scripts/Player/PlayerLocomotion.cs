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

        [Header("Sprint")]

        /// Movement speed in metres per second while sprinting. Replaces
        /// moveSpeed rather than adding to it, so the two can be tuned
        /// independently.
        [SerializeField] private float sprintSpeed = 4.5f;

        [Header("Jump")]

        // Peak height of a jump in metres. Tuned as a height rather than a
        // launch speed because height is what matters for level design (can
        // the player reach that crate?) - the launch speed is derived from
        // it and gravity in TryJump().
        [SerializeField] private float jumpHeight = 0.5f;

        // Seconds after walking off an edge during which a jump still works
        // ("coyote time") - it feels unfair when a press a hair too late
        // does nothing.
        [SerializeField] private float coyoteTime = 0.1f;

        // Seconds a jump press is remembered while it can't jump yet (e.g.
        // pressed just before landing), so it fires the moment it can.
        [SerializeField] private float jumpBufferTime = 0.15f;

        [Header("Air Movement")]

        // How quickly, in metres per second squared, the thumbstick can
        // steer horizontal velocity while airborne. Low, so momentum
        // dominates: a jump can be corrected, not reversed.
        [SerializeField] private float airControlAcceleration = 3f;

        // How quickly, in metres per second squared, airborne horizontal
        // velocity bleeds away while the stick is centred.
        [SerializeField] private float airDrag = 0.5f;

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

        [Header("Movement State")]

        /// Horizontal speed, in metres per second, the player must actually be
        /// moving at to count as Walking/Sprinting/CrouchWalking rather than
        /// Still/CrouchStill. Measured from real applied movement, so pushing
        /// the stick into a wall still counts as still.
        [SerializeField] private float movingSpeedThreshold = 0.1f;

        /// How long, in seconds, the CharacterController must be off the
        /// ground before the state becomes Airborne. isGrounded briefly drops
        /// out when walking down steps and slopes, and without this grace
        /// period the state would flicker to Airborne mid-walk.
        [SerializeField] private float airborneGraceTime = 0.15f;

        /// Prevents repeated snap turns while the stick remains held.
        private bool _snapTurnQueued;

        /// Current vertical movement speed in metres per second.
        private float _verticalVelocity;

        // Current horizontal velocity in metres per second (world space, y
        // always 0). Set straight from the thumbstick while grounded; kept
        // while airborne, so running jumps and walking off edges carry their
        // speed. Only steered or dragged in the air - see HandleMovement().
        // Climb release momentum will seed this too.
        private Vector3 _horizontalVelocity;

        // Counts down from jumpBufferTime after a jump press; a jump fires
        // while it's above zero and jumping is allowed.
        private float _jumpBufferTimer;

        // True from takeoff until landing, so coyote time can't allow a
        // second jump while still rising, and MovementState can report
        // Airborne immediately instead of after airborneGraceTime.
        private bool _hasJumped;

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

        /// <summary>
        /// True while the sprint toggle is on. Most systems should read
        /// MovementState instead, which is only Sprinting while the player
        /// is actually moving at sprint speed.
        /// </summary>
        public bool IsSprinting { get; private set; }

        /// <summary>
        /// True while the crouch toggle is on (including mid-way through the
        /// height transition).
        /// </summary>
        public bool IsCrouching => _isCrouching;

        /// <summary>
        /// What the player's body is doing this frame - see MovementState.
        /// Updated by TickState() after the CharacterController has moved.
        /// </summary>
        public MovementState MovementState { get; private set; }

        /// <summary>
        /// Raised on the frame the player lands after being Airborne, with the
        /// downward speed in metres per second at impact - for the landing
        /// noise event (Phase 2) and a haptic thump. A plain C# event invoked
        /// with a float, so raising it doesn't allocate.
        /// </summary>
        public event Action<float> Landed;

        /// Seconds since the CharacterController was last grounded - see
        /// airborneGraceTime.
        private float _timeSinceGrounded;

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

                // A climb starting mid-fall (or mid-jump) shouldn't carry
                // that speed through to whenever the player lets go again -
                // resetting here means release always resumes falling from
                // rest. Any buffered jump press is dropped too, so letting
                // go doesn't fire a stale jump.
                if (climbStarted) {
                    _verticalVelocity = 0f;
                    _horizontalVelocity = Vector3.zero;
                    _jumpBufferTimer = 0f;
                }

                // Grabbing a ledge ends a sprint - letting go again shouldn't
                // drop you straight back into sprint speed.
                IsSprinting = false;

                return Vector3.zero;
            }

            // Jump before gravity: HandleGravity() only snaps vertical
            // velocity to groundedGravity while it's negative, so a fresh
            // upward launch speed set here survives it.
            HandleJump();

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
        /// Reacts to what this frame's Move() actually did, then works out
        /// this frame's MovementState. Called by PlayerController straight
        /// after characterController.Move(), because it needs things only
        /// known then: the up-to-date isGrounded (Move() is what updates it),
        /// appliedMovement - how far the player actually moved, rather than
        /// how far they asked to - and the collisionFlags Move() returned.
        /// </summary>
        public void TickState(bool isClimbing, Vector3 appliedMovement, CollisionFlags collisionFlags)
        {
            bool isGrounded = characterController.isGrounded;

            if (isGrounded) {
                _timeSinceGrounded = 0f;

                // Only once actually falling or resting - on the takeoff
                // frame itself the controller can still report grounded.
                if (_verticalVelocity <= 0f) {
                    _hasJumped = false;
                }
            } else {
                _timeSinceGrounded += Time.deltaTime;
            }

            if (!isClimbing) {
                ApplyCollisionResponse(appliedMovement, collisionFlags, isGrounded);
            }

            bool wasAirborne = MovementState == MovementState.Airborne;

            // Horizontal only - gravity's small constant downward push while
            // grounded shouldn't count as moving. Compared as squared
            // lengths, which avoids a square root: "distance > threshold *
            // time" rearranged from "speed > threshold".
            appliedMovement.y = 0f;
            float minimumDistance = movingSpeedThreshold * Time.deltaTime;
            bool isMoving = appliedMovement.sqrMagnitude > minimumDistance * minimumDistance;

            // Checked in priority order - e.g. crouching while falling is
            // still Airborne, and sprinting into a wall is still Still. A
            // jump counts as Airborne straight away; the grace period is only
            // for isGrounded flickering on steps and slopes, which a
            // deliberate jump isn't.
            if (isClimbing) {
                MovementState = MovementState.Climbing;
            } else if (_timeSinceGrounded > airborneGraceTime || (_hasJumped && !isGrounded)) {
                MovementState = MovementState.Airborne;
            } else if (_isCrouching) {
                MovementState = isMoving ? MovementState.CrouchWalking : MovementState.CrouchStill;
            } else if (!isMoving) {
                MovementState = MovementState.Still;
            } else {
                MovementState = IsSprinting ? MovementState.Sprinting : MovementState.Walking;
            }

            // Landing: was Airborne, now touching the ground. _verticalVelocity
            // still holds the speed this frame's Move() fell at - it's only
            // reset to groundedGravity in next frame's HandleGravity().
            if (wasAirborne && isGrounded) {
                Landed?.Invoke(Mathf.Max(0f, -_verticalVelocity));
            }
        }

        /// <summary>
        /// Stops momentum pushing into whatever Move() just hit. Without this,
        /// a jump into a low ceiling would stay pressed against it until
        /// gravity eventually turned the velocity around, and airborne
        /// momentum into a wall would keep "pushing" into it for the whole
        /// fall - and slide the player along it at full speed once clear.
        /// </summary>
        private void ApplyCollisionResponse(Vector3 appliedMovement, CollisionFlags collisionFlags, bool isGrounded)
        {
            if ((collisionFlags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) {
                _verticalVelocity = 0f;
            }

            // Grounded horizontal velocity is re-set from the stick every
            // frame anyway, so this only matters in the air.
            if (isGrounded || (collisionFlags & CollisionFlags.Sides) == 0 || Time.deltaTime <= 0f) {
                return;
            }

            // Replace momentum with what the controller actually managed.
            // Hitting a wall head-on leaves ~zero; hitting it at an angle
            // leaves the part that slid along it - so momentum is redirected
            // along the wall rather than just killed. Only ever reduces it.
            Vector3 appliedVelocity = appliedMovement / Time.deltaTime;
            appliedVelocity.y = 0f;

            if (appliedVelocity.sqrMagnitude < _horizontalVelocity.sqrMagnitude) {
                _horizontalVelocity = appliedVelocity;
            }
        }

        /// <summary>
        /// Handles the jump button: stands up if crouched (no jump, so a
        /// sneak is never broken by accident), otherwise buffers the press
        /// and jumps as soon as it's allowed - grounded or within coyote
        /// time, not already mid-jump, and fully standing.
        /// </summary>
        private void HandleJump()
        {
            if (playerInput.JumpPressed) {
                if (_isCrouching) {
                    // HandleCrouch() raises the capsule smoothly from next
                    // frame; a second press jumps once fully standing.
                    _isCrouching = false;
                } else {
                    _jumpBufferTimer = jumpBufferTime;
                }
            }

            if (_jumpBufferTimer <= 0f) {
                return;
            }

            _jumpBufferTimer -= Time.deltaTime;

            // _timeSinceGrounded is from last frame's Move() - the latest
            // grounded information available before this frame's Move().
            bool withinCoyoteTime = _timeSinceGrounded <= coyoteTime;

            // Still rising out of a crouch - wait, so the capsule isn't
            // growing into a ceiling during takeoff.
            bool isFullyStanding = Mathf.Approximately(characterController.height, _standingHeight);

            if (_hasJumped || !withinCoyoteTime || !isFullyStanding) {
                return;
            }

            // Launch speed for a given peak height, from v² = u² + 2as with
            // v = 0 at the top: u = sqrt(2 * height * -gravity). gravity is
            // negative, hence the minus.
            _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            _hasJumped = true;
            _jumpBufferTimer = 0f;
        }

        /// <summary>
        /// Reads movement input from the left thumbstick and returns this frame's
        /// horizontal movement. Movement is relative to the Player root's orientation
        /// rather than the headset orientation.
        ///
        /// On the ground the stick sets _horizontalVelocity directly, exactly
        /// as before. In the air that velocity is kept (momentum), and the
        /// stick can only nudge it towards where it points at
        /// airControlAcceleration - or, with the stick centred, it slowly
        /// bleeds away at airDrag. isGrounded flickering for a frame on a
        /// step just holds the current velocity, so it's invisible.
        /// </summary>
        private Vector3 HandleMovement()
        {
            Vector2 moveInput = playerInput.MoveAxis;

            // Ignore tiny thumbstick movements and controller noise.
            bool isMoving = moveInput.magnitude >= moveDeadzone;

            UpdateSprint(isMoving);

            // The velocity the stick is asking for this frame.
            Vector3 targetVelocity = Vector3.zero;

            if (isMoving) {
                // Convert 2D stick input into a world-space movement direction based on
                // the Player root orientation.
                Vector3 direction =
                    playerTransform.forward * moveInput.y +
                    playerTransform.right * moveInput.x;

                // Prevent diagonal movement from being faster than straight movement.
                direction = Vector3.ClampMagnitude(direction, 1f);

                targetVelocity = direction * (IsSprinting ? sprintSpeed : moveSpeed);
            }

            if (characterController.isGrounded) {
                _horizontalVelocity = targetVelocity;
            } else {
                float acceleration = isMoving ? airControlAcceleration : airDrag;

                _horizontalVelocity = Vector3.MoveTowards(
                    _horizontalVelocity,
                    targetVelocity,
                    acceleration * Time.deltaTime);
            }

            return _horizontalVelocity * Time.deltaTime;
        }

        /// <summary>
        /// Click-to-toggle sprint. Clicking the sprint button while moving
        /// starts a sprint; clicking again stops it. It also ends on its own
        /// when the stick returns to centre, so a sprint only lasts for one
        /// "run" and the player never starts walking again already sprinting.
        /// Crouching and climbing also end it (see HandleCrouch() and
        /// TickMovement()), and it can't be started while crouched.
        /// </summary>
        private void UpdateSprint(bool isMoving)
        {
            if (!isMoving) {
                IsSprinting = false;
                return;
            }

            if (playerInput.SprintPressed && !_isCrouching) {
                IsSprinting = !IsSprinting;
            }
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

                // Crouching cancels a sprint. (Standing back up doesn't
                // restart it - that needs a fresh click.)
                if (_isCrouching) {
                    IsSprinting = false;
                }
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
