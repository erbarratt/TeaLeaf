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
        /// How fast one way of moving goes on the ground, and how quickly it
        /// gets up to speed and stops.
        [Serializable]
        private struct MoveGait
        {
            [Tooltip("Speed with the left stick fully pushed forward, in metres per second, e.g. 2.4 = across a 6m room in two and a half seconds. Higher moves faster, lower slower.")]
            public float speed;

            [Tooltip("How quickly the speed builds towards what the stick asks for, in metres per second squared, e.g. 15 = from standing to 2.4 m/s in about a sixth of a second. Higher starts more sharply, lower eases in.")]
            public float acceleration;

            [Tooltip("How quickly the speed falls when the stick asks for less (let go, or pushed less far), in metres per second squared, e.g. 10 = from 2.4 m/s to a stop in about a quarter of a second. Higher stops more sharply, lower glides on.")]
            public float deceleration;
        }

        [Tooltip("The CharacterController on the Player root that moves the player through collisions. Its height and centre are changed here for crouching and re-centring. e.g. drag the Player root's CharacterController here.")]
        [SerializeField] private CharacterController characterController;

        [Tooltip("The Main Camera: where the headset is. The capsule is kept centred under it, e.g. step sideways in the room and the capsule follows. Drag the Main Camera here.")]
        [SerializeField] private Transform cameraTransform;

        /// The XR Origin's "Camera Offset" transform - the parent of the tracked
        /// head/hand transforms. Crouching here is button-driven rather than
        /// detected from the player's real-world headset height, so we lower
        /// this transform to move the whole tracked hierarchy down with the
        /// CharacterController rather than trying to move the camera itself,
        /// which is driven by tracking and can't be positioned directly.
        [Tooltip("The Camera Offset object, the parent of the tracked head and hands. Crouching lowers it, so the whole tracked view drops with the capsule, e.g. a crouch from 1.8m to 1m drops it 0.8m. Drag Camera Offset here.")]
        [SerializeField] private Transform cameraOffsetTransform;

        [Tooltip("The input system on the Player root: the thumbsticks and buttons are read through it, e.g. the left stick's Move Axis drives walking. Drag the Player root's PlayerInputXR here.")]
        [SerializeField] private PlayerInputXR playerInput;

        /// The transform whose Y rotation defines the player's movement direction.
        /// We use the rig root rather than the headset so that looking left/right does
        /// not change which direction "forward" moves the player.
        [Tooltip("The Player root. Movement is relative to its facing, not the headset's, and turning rotates it, e.g. looking left while pushing the stick forward still walks the body's forward. Drag the Player root here.")]
        [SerializeField] private Transform playerTransform;

        [Header("Turning")]

        [Tooltip("On, the right stick turns the player continuously at Smooth Turn Speed. Off, it snap turns by Snap Turn Angle each time it is pushed past Snap Turn Threshold, e.g. off with 45: each flick turns the view 45 degrees at once.")]
        [SerializeField] private bool useSmoothTurn;

        // Thief VR's smooth turn speed.
        [Tooltip("Turning speed with the right stick fully over while smooth turning, in degrees per second, e.g. 120 = a full turn in three seconds. Higher turns faster (more likely to cause sickness), lower slower. Only used with Use Smooth Turn on.")]
        [SerializeField] private float smoothTurnSpeed = 120f;

        [Tooltip("How far one snap turn rotates the player, in degrees, e.g. 45 = eight flicks turn the player all the way round. Higher turns further per flick, lower gives finer control. Only used with Use Smooth Turn off.")]
        [SerializeField] private float snapTurnAngle = 45f;

        [Tooltip("How far (0-1) the right stick must be pushed sideways to fire a snap turn. It must come back inside Turn Deadzone before another, e.g. 0.8 = the stick must go four fifths of the way over. Higher needs a firmer flick, lower fires on a lighter push.")]
        [SerializeField] private float snapTurnThreshold = 0.8f;

        [Tooltip("Sideways stick movement (0-1) below this is ignored, so a resting stick never turns. Also how far it must come back for a snap turn to re-arm, e.g. 0.2 = a fifth of the way over does nothing. Higher ignores more stick drift but feels less responsive; 0 reacts to any touch.")]
        [SerializeField] private float turnDeadzone = 0.2f;

        [Header("Movement")]

        // The four gaits, the direction slow-down and their numbers are
        // Thief VR's own (its player rig).
        [Tooltip("Walking: standing, sprint off.")]
        [SerializeField] private MoveGait walk = new MoveGait { speed = 2.4f, acceleration = 15f, deceleration = 10f };

        [Tooltip("Sprinting: standing, sprint on.")]
        [SerializeField] private MoveGait sprint = new MoveGait { speed = 3.8f, acceleration = 20f, deceleration = 10f };

        [Tooltip("Crouched, sprint off: a slow creep that eases in and stops sharply.")]
        [SerializeField] private MoveGait crouch = new MoveGait { speed = 1.4f, acceleration = 5f, deceleration = 15f };

        [Tooltip("Crouched with sprint on: a faster crouched scurry, still slower than walking.")]
        [SerializeField] private MoveGait crouchSprint = new MoveGait { speed = 1.8f, acceleration = 15f, deceleration = 10f };

        [Tooltip("Left stick movement (0-1) below this is ignored, so a resting stick never moves the player, e.g. 0.2 = a fifth of the way pushed does nothing. Higher ignores more stick drift but needs a bigger push to creep; 0 moves on any touch.")]
        [SerializeField] private float moveDeadzone = 0.2f;

        [Header("Direction")]

        // Moving sideways and backwards is slower, as a body is: full speed
        // up to fullSpeedAngle off forward, falling in a straight line to
        // backwardSpeedScale at slowestAngle, and that on round to straight
        // back.
        [Tooltip("How far the stick can point off straight forward, in degrees, and still move at full speed, e.g. 25 = a slight diagonal is full speed. Higher keeps full speed further round, lower slows sooner.")]
        [SerializeField] private float fullSpeedAngle = 25f;

        [Tooltip("How far off forward, in degrees, the speed reaches its slowest (Backward Speed Scale), e.g. 160 = almost straight back. Must be above Full Speed Angle. Lower reaches the slowest sooner (sideways slower too), higher later.")]
        [SerializeField] private float slowestAngle = 160f;

        [Tooltip("Speed multiplier moving backwards (from Slowest Angle round to straight back), e.g. 0.5 = backwards at half speed. 1 = every direction the same speed, lower slower backwards.")]
        [Range(0f, 1f)]
        [SerializeField] private float backwardSpeedScale = 0.5f;

        [Header("Jump")]

        // Peak height of a jump in metres. Tuned as a height rather than a
        // launch speed because height is what matters for level design (can
        // the player reach that crate?) - the launch speed is derived from
        // it and gravity in CacheJumpLaunchSpeed().
        [Tooltip("The peak height of a jump, in metres. The launch speed is worked out from this and Gravity, e.g. 0.5 = the feet clear a half-metre crate. Higher jumps higher, lower less; 0 gives no jump.")]
        [SerializeField] private float jumpHeight = 0.5f;

        // Seconds after walking off an edge during which a jump still works
        // ("coyote time") - it feels unfair when a press a hair too late
        // does nothing.
        [Tooltip("Seconds after walking off an edge in which a jump still works. e.g. 0.1 = a press a tenth of a second after stepping off still jumps. Higher is more forgiving, lower stricter; 0 means the player must be on the ground.")]
        [SerializeField] private float coyoteTime = 0.1f;

        // Seconds a jump press is remembered while it can't jump yet (e.g.
        // pressed just before landing), so it fires the moment it can.
        [Tooltip("Seconds a jump press is remembered while it cannot jump yet, so a press just before landing still jumps. e.g. 0.15 = pressing A up to 0.15s before landing jumps on landing. Higher remembers presses longer, lower forgets sooner; 0 means only a press while able to jump counts.")]
        [SerializeField] private float jumpBufferTime = 0.15f;

        [Header("Air Movement")]

        // How quickly, in metres per second squared, the thumbstick can
        // steer horizontal velocity while airborne. Low, so momentum
        // dominates: a jump can be corrected, not reversed.
        [Tooltip("How quickly the stick can steer the horizontal velocity while airborne, in metres per second squared, e.g. 3 = a mid-air push can change the speed by 3 m/s each second, enough to correct a jump but not reverse it. Higher steers more in the air, lower less; 0 gives no air control.")]
        [SerializeField] private float airControlAcceleration = 3f;

        [Tooltip("How quickly airborne horizontal velocity bleeds away while the stick is centred, in metres per second squared, e.g. 0.5 = a 3 m/s running jump loses half a metre per second each second in the air. Higher slows jumps sooner, lower carries them further; 0 keeps all momentum.")]
        [SerializeField] private float airDrag = 0.5f;

        [Header("Gravity")]

        [Tooltip("Downward acceleration, in metres per second squared, so normally negative. Also sets the jump's launch speed. e.g. -9.81 = real Earth gravity. More negative falls faster and jumps feel snappier, nearer 0 floatier.")]
        [SerializeField] private float gravity = -9.81f;

        [Tooltip("A small downward speed, in metres per second, applied while grounded so the CharacterController stays attached to slopes and steps. Negative, e.g. -2 keeps the feet on the ground walking down a ramp. More negative sticks harder; too near 0 and the player hops down slopes.")]
        [SerializeField] private float groundedGravity = -2f;

        [Header("Height")]

        /// Minimum allowed CharacterController height. Doubles as the crouched
        /// height target, since the fully-crouched height should never be
        /// shorter than the CharacterController can safely be.
        [Tooltip("The capsule's height while crouched, in metres, and the shortest it is ever allowed to be, e.g. 1 = crouched, the player fits under a 1.1m shelf. Lower crouches deeper, higher less.")]
        [SerializeField] private float minimumHeight = 1f;

        [Tooltip("Added to half the height when placing the capsule's centre, in metres, so the capsule never clips into the floor, e.g. 0.1 lifts the centre 10cm above half the height. Higher lifts the capsule more, 0 lets it touch the floor exactly.")]
        [SerializeField] private float heightPadding = 0.1f;

        /// How far, in metres, the headset must drift horizontally from the
        /// capsule's centre before the capsule is re-centred under it.
        /// Writing CharacterController.center updates its physics shape, and
        /// the tracked head is never perfectly still, so without this it's
        /// rewritten every frame for sub-millimetre jitter. Small enough that
        /// the capsule is never visibly off-centre.
        [Tooltip("How far the headset must drift horizontally from the capsule's centre, in metres, before the capsule is moved back under it, e.g. 0.002 = 2mm, so head jitter doesn't rewrite the capsule every frame. Higher re-centres less often but lets the capsule sit further off; 0 re-centres every frame.")]
        [SerializeField] private float recentreThreshold = 0.002f;

        [Header("Crouch")]

        [Tooltip("How fast the capsule's height changes between standing and crouched, in metres per second, e.g. 2 = crouching from 1.8m to 1m takes 0.4s. Higher crouches faster, lower eases down more slowly.")]
        [SerializeField] private float crouchTransitionSpeed = 2f;

        [Header("Movement State")]

        /// Horizontal speed, in metres per second, the player must actually be
        /// moving at to count as Walking/Sprinting/CrouchWalking rather than
        /// Still/CrouchStill. Measured from real applied movement, so pushing
        /// the stick into a wall still counts as still.
        [Tooltip("The real horizontal speed, in metres per second, above which the player counts as moving (Walking, Sprinting or Crouch Walking) rather than still, e.g. 0.1 = pushing the stick into a wall still counts as Still. Higher needs faster movement to count as moving, lower counts the slightest creep.")]
        [SerializeField] private float movingSpeedThreshold = 0.1f;

        /// How long, in seconds, the CharacterController must be off the
        /// ground before the state becomes Airborne. isGrounded briefly drops
        /// out when walking down steps and slopes, and without this grace
        /// period the state would flicker to Airborne mid-walk.
        [Tooltip("How long the capsule must be off the ground, in seconds, before the state becomes Airborne. A jump is Airborne at once. e.g. 0.15 = walking down steps never flickers to Airborne. Higher hides longer drops, lower reports a fall sooner.")]
        [SerializeField] private float airborneGraceTime = 0.15f;

        /// Prevents repeated snap turns while the stick remains held.
        private bool _snapTurnQueued;

        /// Current vertical movement speed in metres per second.
        private float _verticalVelocity;

        // Current horizontal velocity in metres per second (world space, y
        // always 0). Eased towards the thumbstick while grounded; kept while
        // airborne, so running jumps and walking off edges carry their
        // speed, only steered or dragged in the air - see HandleMovement().
        private Vector3 _horizontalVelocity;

        // Counts down from jumpBufferTime after a jump press; a jump fires
        // while it's above zero and jumping is allowed.
        private float _jumpBufferTimer;

        // True from takeoff until landing, so coyote time can't allow a
        // second jump while still rising, and MovementState can report
        // Airborne immediately instead of after airborneGraceTime.
        private bool _hasJumped;

        // True between BeginMantle() and EndMantle(). PlayerController
        // doesn't tick movement or turning at all during a mantle; this flag
        // covers what still runs - the crouch button is ignored, and
        // MovementState reports Mantling.
        private bool _isMantling;

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

        // How far in from a ground contact, in metres, the check for the
        // real surface starts (away from the capsule), and from how high
        // above it the ray comes down - see TryGetSurfaceNormal(). The
        // inset is small so even a near-vertical slope is still below the
        // ray's start.
        private const float SurfaceProbeInset = 0.005f;
        private const float SurfaceProbeHeight = 0.1f;

        // Gathered by OnControllerColliderHit() during each Move(), reset in
        // TickMovement() just before it: whether the capsule's bottom touched
        // ground it can stand on, ground too steep to stand on, and that
        // steep surface's normal.
        private bool _touchedWalkableGround;
        private bool _touchedSteepGround;
        private Vector3 _touchedSteepNormal;

        // True when the last Move() left the capsule resting only on ground
        // steeper than the CharacterController's Slope Limit (a roof, say).
        // That doesn't count as ground: the player slides down it as if in
        // the air and can't jump. Set in TickState(), used by next frame's
        // TickMovement().
        private bool _isOnSteepGround;
        private Vector3 _steepGroundNormal;

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
        /// Full-stick forward speed in metres per second of the gait in use
        /// (walk, sprint, crouch or crouch sprint) - for systems that compare
        /// the player's real speed against it, e.g. footsteps getting
        /// quieter at a creep. Backwards is slower than this.
        /// </summary>
        public float GaitSpeed => CurrentGait.speed;

        /// The gait the toggles choose: crouch and sprint are independent,
        /// so crouched with sprint on is the crouch sprint.
        private MoveGait CurrentGait => _isCrouching
            ? (IsSprinting ? crouchSprint : crouch)
            : (IsSprinting ? sprint : walk);

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

        /// Upward speed a jump launches at, in metres per second - derived
        /// from jumpHeight and gravity, so it's worked out whenever either
        /// changes (Awake(), and OnValidate() for Inspector edits) rather
        /// than on every jump. See CacheJumpLaunchSpeed().
        private float _jumpLaunchSpeed;

        private void Awake()
        {
            _standingHeight = characterController.height;
            CacheJumpLaunchSpeed();
        }

        /// <summary>
        /// Editor-only: runs whenever a value is changed in the Inspector, so
        /// tuning jumpHeight or gravity in Play Mode still applies at once.
        /// </summary>
        private void OnValidate()
        {
            CacheJumpLaunchSpeed();
        }

        /// <summary>
        /// Launch speed for a given peak height, from v² = u² + 2as with
        /// v = 0 at the top: u = sqrt(2 * height * -gravity). gravity is
        /// negative, hence the minus.
        /// </summary>
        private void CacheJumpLaunchSpeed()
        {
            _jumpLaunchSpeed = Mathf.Sqrt(2f * jumpHeight * -gravity);
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
            // Fresh for this frame's Move(), which PlayerController calls
            // next; OnControllerColliderHit() fills them in during it.
            _touchedWalkableGround = false;
            _touchedSteepGround = false;

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

            // Read once and shared: isGrounded is a call into the engine, and
            // it can't change until PlayerController's Move() anyway. It's
            // last frame's Move() result - the latest available here. Ground
            // too steep to stand on doesn't count: on it the player keeps
            // momentum, has only air control, and gravity builds up.
            bool isGrounded = characterController.isGrounded && !_isOnSteepGround;

            // Jump before gravity: HandleGravity() only snaps vertical
            // velocity to groundedGravity while it's negative, so a fresh
            // upward launch speed set here survives it.
            HandleJump();

            HandleMovement(isGrounded);
            HandleGravity(isGrounded);

            if (_isOnSteepGround) {
                SlideAlongSteepGround();
            }

            return (_horizontalVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime;
        }

        /// <summary>
        /// On ground too steep to stand on, takes away the part of the
        /// velocity going into the slope, like a frictionless surface: what's
        /// left runs along it, so gravity (built up each frame by
        /// HandleGravity()) turns into a slide down the slope that speeds up
        /// the steeper it is, and pushing the stick up the slope only pushes
        /// into it. Done here rather than left to the CharacterController,
        /// which stops the player climbing a steep slope but never slides
        /// them down one.
        /// </summary>
        private void SlideAlongSteepGround()
        {
            Vector3 velocity = _horizontalVelocity + Vector3.up * _verticalVelocity;
            float intoGround = Vector3.Dot(velocity, _steepGroundNormal);

            if (intoGround < 0f) {
                velocity -= _steepGroundNormal * intoGround;
            }

            _horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            _verticalVelocity = velocity.y;
        }

        /// <summary>
        /// Called by Unity during characterController.Move() for each
        /// collider the capsule touches (PlayerBodyPushing gets the same
        /// call). Sorts contacts on the capsule's bottom into ground it can
        /// stand on and ground too steep for its Slope Limit, for TickState().
        /// No physics query unless a contact looks too steep.
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Only contacts below the centre of the capsule's bottom sphere
            // are ground; higher ones are walls and ceilings.
            float bottomSphereCentreY = characterController.transform.position.y
                + characterController.center.y
                - characterController.height * 0.5f
                + characterController.radius;

            if (hit.point.y >= bottomSphereCentreY || hit.normal.y <= 0f) {
                return;
            }

            float slopeLimit = characterController.slopeLimit;

            if (Vector3.Angle(hit.normal, Vector3.up) <= slopeLimit) {
                _touchedWalkableGround = true;
                return;
            }

            // A contact normal can be steep just because the capsule is on
            // the corner of a step or ledge, so the steepness is checked
            // against the real surface there.
            if (!TryGetSurfaceNormal(hit, out Vector3 surfaceNormal)) {
                return;
            }

            if (Vector3.Angle(surfaceNormal, Vector3.up) <= slopeLimit) {
                _touchedWalkableGround = true;
            } else {
                _touchedSteepGround = true;
                _touchedSteepNormal = surfaceNormal;
            }
        }

        /// <summary>
        /// The normal of the surface under a contact point: one ray straight
        /// down against only the touched collider, starting a hair in from
        /// the contact (away from the capsule, so on a corner it lands on
        /// the top face the capsule rests on, not off the edge). False if
        /// the ray misses - a corner over nothing.
        /// </summary>
        private static bool TryGetSurfaceNormal(ControllerColliderHit hit, out Vector3 normal)
        {
            Vector3 away = hit.normal;
            away.y = 0f;

            Vector3 origin = hit.point - away.normalized * SurfaceProbeInset + Vector3.up * SurfaceProbeHeight;
            Ray ray = new Ray(origin, Vector3.down);

            if (hit.collider.Raycast(ray, out RaycastHit surfaceHit, SurfaceProbeHeight * 2f)) {
                normal = surfaceHit.normal;
                return true;
            }

            normal = Vector3.up;
            return false;
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
            // The CharacterController is disabled during a mantle, so none of
            // its grounded/collision information below means anything.
            if (_isMantling) {
                MovementState = MovementState.Mantling;
                return;
            }

            // Resting only on ground too steep to stand on counts as off the
            // ground: coyote time runs out, the state goes Airborne after the
            // grace time, and reaching flat ground fires Landed.
            _isOnSteepGround = !isClimbing && _touchedSteepGround && !_touchedWalkableGround;
            _steepGroundNormal = _touchedSteepNormal;

            bool isGrounded = characterController.isGrounded && !_isOnSteepGround;

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
                ApplyCollisionResponse(appliedMovement, collisionFlags);
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
        /// Called by PlayerMantling as a mantle starts. Drops everything this
        /// class was carrying - velocity, momentum, sprint, any buffered jump -
        /// so nothing leaks out after the mantle, and sets the crouch to
        /// whatever the ledge says the mantle ends in. The crouch height
        /// transition then plays out during the mantle itself (TickBody()
        /// keeps running), so the player arrives already at the right height.
        /// </summary>
        public void BeginMantle(bool endsCrouched)
        {
            _isMantling = true;
            _isCrouching = endsCrouched;
            IsSprinting = false;
            _verticalVelocity = 0f;
            _horizontalVelocity = Vector3.zero;
            _jumpBufferTimer = 0f;
            _hasJumped = false;
            _isOnSteepGround = false;
        }

        /// <summary>
        /// Called by PlayerMantling once the player is at the mantle point.
        /// Hands control back as if standing still on the ground.
        /// </summary>
        public void EndMantle()
        {
            _isMantling = false;
            _verticalVelocity = 0f;

            // Counts as grounded: the mantle placed the feet on the surface,
            // but isGrounded won't be true until the next Move(). Without
            // this, a long climb's worth of "time since grounded" would read
            // as Airborne (and fire Landed) on the first frame back.
            _timeSinceGrounded = 0f;

            // The mantle replaced climbing - don't let TickMovement() treat
            // the next frame as "a climb just ended".
            _wasClimbing = false;

            // The mantle was triggered by pushing a stick up. If the right
            // stick is still held at a diagonal, that must not fire a snap
            // turn the moment control returns - wait for it to re-centre.
            _snapTurnQueued = true;
        }

        /// <summary>
        /// Stops momentum pushing into whatever Move() just hit. Without this,
        /// a jump into a low ceiling would stay pressed against it until
        /// gravity eventually turned the velocity around, and momentum into a
        /// wall would keep "pushing" into it - and slide the player along it
        /// at full speed once clear.
        /// </summary>
        private void ApplyCollisionResponse(Vector3 appliedMovement, CollisionFlags collisionFlags)
        {
            if ((collisionFlags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) {
                _verticalVelocity = 0f;
            }

            // On the ground too: speed now builds and falls off gradually, so
            // without this, speed into a wall would linger and turning away
            // from it would first have to brake that unseen speed.
            if ((collisionFlags & CollisionFlags.Sides) == 0 || Time.deltaTime <= 0f) {
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
        /// time, not already mid-jump, fully standing, and not on ground too
        /// steep to stand on.
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

            // Steep ground is checked as well as coyote time, which would
            // otherwise still allow a jump just after stepping onto it.
            if (_hasJumped || !withinCoyoteTime || !isFullyStanding || _isOnSteepGround) {
                return;
            }

            _verticalVelocity = _jumpLaunchSpeed;
            _hasJumped = true;
            _jumpBufferTimer = 0f;
        }

        /// <summary>
        /// Reads movement input from the left thumbstick and sets this frame's
        /// horizontal velocity. Movement is relative to the Player root's orientation
        /// rather than the headset orientation.
        ///
        /// On the ground _horizontalVelocity eases towards what the stick
        /// asks for at the gait's acceleration or deceleration, the asked-for
        /// speed slowed for sideways and backwards (DirectionSpeedScale()).
        /// In the air that velocity is kept (momentum), and the
        /// stick can only nudge it towards where it points at
        /// airControlAcceleration - or, with the stick centred, it slowly
        /// bleeds away at airDrag. isGrounded flickering for a frame on a
        /// step just holds the current velocity, so it's invisible.
        /// </summary>
        private void HandleMovement(bool isGrounded)
        {
            Vector2 moveInput = playerInput.MoveAxis;

            // Ignore tiny thumbstick movements and controller noise. Compared
            // as squared lengths, which skips the square root magnitude
            // needs: both sides are positive, so a >= b exactly when
            // a² >= b².
            bool isMoving = moveInput.sqrMagnitude >= moveDeadzone * moveDeadzone;

            UpdateSprint(isMoving);

            MoveGait gait = CurrentGait;

            // The velocity the stick is asking for this frame.
            Vector3 targetVelocity = Vector3.zero;

            if (isMoving) {
                // Convert 2D stick input into a world-space movement direction based on
                // the Player root orientation. Rotating the stick vector (x
                // right, y forward) by the root's rotation is the same as
                // forward * y + right * x, but reads the rotation once
                // instead of fetching forward and right separately.
                Vector3 direction = playerTransform.rotation * new Vector3(moveInput.x, 0f, moveInput.y);

                // Prevent diagonal movement from being faster than straight movement.
                direction = Vector3.ClampMagnitude(direction, 1f);

                targetVelocity = direction * (gait.speed * DirectionSpeedScale(moveInput));
            }

            if (isGrounded) {
                // Eased towards what the stick asks for rather than set
                // straight to it: accelerating while asking for more speed
                // than there is, decelerating while asking for less (let go,
                // or eased off). Turning at full speed asks for the same
                // speed, so it swings round at the acceleration.
                bool speedingUp = isMoving && targetVelocity.sqrMagnitude >= _horizontalVelocity.sqrMagnitude;
                float rate = speedingUp ? gait.acceleration : gait.deceleration;

                _horizontalVelocity = Vector3.MoveTowards(
                    _horizontalVelocity,
                    targetVelocity,
                    rate * Time.deltaTime);
            } else {
                float acceleration = isMoving ? airControlAcceleration : airDrag;

                _horizontalVelocity = Vector3.MoveTowards(
                    _horizontalVelocity,
                    targetVelocity,
                    acceleration * Time.deltaTime);
            }
        }

        /// <summary>
        /// The speed multiplier for the way the stick points: 1 within
        /// fullSpeedAngle of straight forward, falling in a straight line to
        /// backwardSpeedScale at slowestAngle, and that from there round to
        /// straight back. The stick's own angle, so the same whether the
        /// player faces where they move or not.
        /// </summary>
        private float DirectionSpeedScale(Vector2 moveInput)
        {
            float angle = Vector2.Angle(Vector2.up, moveInput);
            float t = Mathf.InverseLerp(fullSpeedAngle, slowestAngle, angle);
            return Mathf.Lerp(1f, backwardSpeedScale, t);
        }

        /// <summary>
        /// Click-to-toggle sprint. Clicking the sprint button while moving
        /// starts a sprint; clicking again stops it. It also ends on its own
        /// when the stick returns to centre, so a sprint only lasts for one
        /// "run" and the player never starts walking again already sprinting.
        /// Climbing also ends it (see TickMovement()). Crouching doesn't:
        /// sprint on while crouched is the crouch sprint, and standing up
        /// from it carries on sprinting.
        /// </summary>
        private void UpdateSprint(bool isMoving)
        {
            if (!isMoving) {
                IsSprinting = false;
                return;
            }

            if (playerInput.SprintPressed) {
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
        /// Applies gravity to the vertical velocity.
        /// </summary>
        private void HandleGravity(bool isGrounded)
        {
            if (isGrounded && _verticalVelocity < 0f) {
                _verticalVelocity = groundedGravity;
            }

            _verticalVelocity += gravity * Time.deltaTime;
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
            // A mantle's crouch is set by the ledge (BeginMantle()) and can't
            // be changed mid-mantle.
            // Sprint is left as it is: crouching mid-sprint drops to the
            // crouch sprint, standing from it sprints again.
            if (playerInput.CrouchPressed && !_isMantling) {
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
        ///
        /// Skipped while the headset is within recentreThreshold of the
        /// current centre, so the physics shape isn't rewritten every frame
        /// for tracking jitter. Measured against the capsule's actual centre
        /// (not last frame's head position), so slow drift still adds up and
        /// triggers a re-centre - the capsule is never more than the
        /// threshold away from the head.
        /// </summary>
        private void UpdateCharacterControllerCentre()
        {
            Vector3 centre = characterController.center;
            Vector3 headLocal = cameraTransform.localPosition;

            float dx = headLocal.x - centre.x;
            float dz = headLocal.z - centre.z;

            if (dx * dx + dz * dz < recentreThreshold * recentreThreshold) {
                return;
            }

            centre.x = headLocal.x;
            centre.z = headLocal.z;

            characterController.center = centre;
        }

    }
}
