using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>
    /// The single source of truth for controller input. Every reference
    /// below points at an action in the project-owned, project-wide
    /// InputSystem_Actions asset (Player map) - never at XRI's sample
    /// "XRI Default Input Actions", which a package update could overwrite
    /// and whose bindings carry XRI-specific interactions (see turnAction).
    /// The Player map holds an action for every Quest controller input:
    /// function-named ones for inputs gameplay reads, and button-named
    /// placeholders (ButtonX, ButtonY, Menu, RightStickClick) for unused
    /// buttons, to be renamed when they get a job.
    /// </summary>
    public class PlayerInputXR : MonoBehaviour
    {
        [Header("Left Hand")]
        // Player/LeftGrip and Player/LeftTrigger - analogue 0-1.
        [SerializeField] private InputActionReference leftGrip;
        [SerializeField] private InputActionReference leftTrigger;

        [Header("Right Hand")]
        // Player/RightGrip and Player/RightTrigger - analogue 0-1.
        [SerializeField] private InputActionReference rightGrip;
        [SerializeField] private InputActionReference rightTrigger;

        [Header("Locomotion")]
        // Reference to the movement thumbstick action - Player/Move (left
        // stick).
        [SerializeField] private InputActionReference moveAction;

        // Reference to the turning thumbstick action - the project-wide
        // InputSystem_Actions Player/Turn (right stick, no interactions).
        // Not XRI's Turn or Snap Turn: their bindings carry Sector
        // interactions that only report a value when the stick is pushed
        // straight from centre into the left/right sector, and read (0, 0)
        // otherwise - so pushing forward first and then sweeping round to
        // the side never turned, and the stick's Y (used to trigger a
        // mantle) never came through at all.
        [SerializeField] private InputActionReference turnAction;

        [Header("Stance")]
        // Reference to the crouch button action. Read as a single press rather
        // than a held value, since crouch is a toggle - see PlayerLocomotion.
        [SerializeField] private InputActionReference crouchAction;

        // Reference to the sprint button action (left stick click). Also read
        // as a single press, since sprint is a click-to-toggle that ends by
        // itself - see PlayerLocomotion.UpdateSprint().
        [SerializeField] private InputActionReference sprintAction;

        // Reference to the jump button action (right B). A single press -
        // PlayerLocomotion buffers it briefly so an early press still jumps
        // on landing.
        [SerializeField] private InputActionReference jumpAction;

        // The InputActions behind the references above, resolved once in
        // OnEnable(). InputActionReference.action is a property that does a
        // lookup (and a null/validity check) on every access - reading
        // through it 9 times a frame is wasted work when the action it
        // points at never changes while playing.
        private InputAction _leftGrip;
        private InputAction _leftTrigger;
        private InputAction _rightGrip;
        private InputAction _rightTrigger;
        private InputAction _move;
        private InputAction _turn;
        private InputAction _crouch;
        private InputAction _sprint;
        private InputAction _jump;

        // Time.frameCount of the last Tick(), so a second call in the same
        // frame (PlayerController's explicit one plus this class' own
        // Update() fallback) returns straight away instead of reading
        // every action again.
        private int _lastTickFrame = -1;

        // All properties below are cached once per frame in Tick() rather
        // than reading the Input System live on every access - several
        // systems (PlayerLocomotion, PlayerClimbing, PlayerHandAnimation)
        // read the same value more than once per frame, and ReadValue<T>()
        // isn't free to call repeatedly.

        public float LeftGrip { get; private set; }
        public float LeftTrigger { get; private set; }

        public float RightGrip { get; private set; }
        public float RightTrigger { get; private set; }

        public bool IsLeftGrabbing => LeftGrip > 0.5f;
        public bool IsRightGrabbing => RightGrip > 0.5f;

        public bool IsLeftUsing => LeftTrigger > 0.5f;
        public bool IsRightUsing => RightTrigger > 0.5f;

        // Left thumbstick movement vector.
        // X = left/right strafe.
        // Y = forward/backward movement.
        public Vector2 MoveAxis { get; private set; }

        // Right thumbstick turning vector.
        // X = horizontal turning.
        // Y is unused for now.
        public Vector2 TurnAxis { get; private set; }

        // True for exactly one frame when the crouch button is pressed.
        public bool CrouchPressed { get; private set; }

        // True for exactly one frame when the sprint button is pressed.
        public bool SprintPressed { get; private set; }

        // True for exactly one frame when the jump button is pressed.
        public bool JumpPressed { get; private set; }

        /// <summary>
        /// Makes sure every action this class reads is enabled. Unity enables
        /// the project-wide InputSystem_Actions at startup, but this class
        /// enables what it needs itself rather than relying on that (or on
        /// which asset a reference happens to point at). Enabling an
        /// already-enabled action does nothing.
        /// </summary>
        private void OnEnable()
        {
            _leftGrip = leftGrip.action;
            _leftTrigger = leftTrigger.action;
            _rightGrip = rightGrip.action;
            _rightTrigger = rightTrigger.action;
            _move = moveAction.action;
            _turn = turnAction.action;
            _crouch = crouchAction.action;
            _sprint = sprintAction.action;
            _jump = jumpAction.action;

            _leftGrip.Enable();
            _leftTrigger.Enable();
            _rightGrip.Enable();
            _rightTrigger.Enable();
            _move.Enable();
            _turn.Enable();
            _crouch.Enable();
            _sprint.Enable();
            _jump.Enable();
        }

        private void Update()
        {
            // Self-sufficient fallback for anything that reads this class
            // without explicitly calling Tick() itself - e.g. the
            // standalone Debug/ input test scripts, which are meant to work
            // without a full PlayerController set up. With a PlayerController
            // present, whichever of the two calls runs first this frame does
            // the reading and the other returns immediately - see Tick().
            Tick();
        }

        /// <summary>
        /// Caches this frame's raw input values. Called explicitly, and
        /// first, by PlayerController.Update() - before anything else reads
        /// this frame's input - so every Tick()-sequenced system
        /// (PlayerLocomotion, PlayerClimbing, PlayerHandInteraction,
        /// PlayerHandAnimation) is guaranteed fresh values regardless of
        /// Unity's own (unspecified) Update() order between this component
        /// and PlayerController.
        ///
        /// Only reads once per frame: the Input System's values don't change
        /// between Update() calls within a frame, so a second call would just
        /// cache identical values again.
        /// </summary>
        public void Tick()
        {
            int frame = Time.frameCount;

            if (frame == _lastTickFrame) {
                return;
            }

            _lastTickFrame = frame;

            LeftGrip = _leftGrip.ReadValue<float>();
            LeftTrigger = _leftTrigger.ReadValue<float>();

            RightGrip = _rightGrip.ReadValue<float>();
            RightTrigger = _rightTrigger.ReadValue<float>();

            MoveAxis = _move.ReadValue<Vector2>();
            TurnAxis = _turn.ReadValue<Vector2>();

            CrouchPressed = _crouch.WasPressedThisFrame();
            SprintPressed = _sprint.WasPressedThisFrame();
            JumpPressed = _jump.WasPressedThisFrame();
        }
    }
}