using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class PlayerInputXR : MonoBehaviour
    {
        [Header("Left Hand")]
        [SerializeField] private InputActionReference leftGrip;
        [SerializeField] private InputActionReference leftTrigger;

        [Header("Right Hand")]
        [SerializeField] private InputActionReference rightGrip;
        [SerializeField] private InputActionReference rightTrigger;

        [Header("Locomotion")]
        // Reference to the movement thumbstick action.
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
        /// Makes sure every action this class reads is enabled. The actions
        /// come from two different assets - XRI's Default Input Actions
        /// (enabled by the Input Action Manager on Player) and the
        /// project-wide InputSystem_Actions (enabled by Unity at startup) -
        /// so rather than relying on either of those, this class enables what
        /// it needs itself. Enabling an already-enabled action does nothing.
        /// </summary>
        private void OnEnable()
        {
            leftGrip.action.Enable();
            leftTrigger.action.Enable();
            rightGrip.action.Enable();
            rightTrigger.action.Enable();
            moveAction.action.Enable();
            turnAction.action.Enable();
            crouchAction.action.Enable();
            sprintAction.action.Enable();
            jumpAction.action.Enable();
        }

        private void Update()
        {
            // Self-sufficient fallback for anything that reads this class
            // without explicitly calling Tick() itself - e.g. the
            // standalone Debug/ input test scripts, which are meant to work
            // without a full PlayerController set up. Redundant with, but
            // harmless alongside, the explicit call PlayerController makes
            // below - both just cache the same live Input System state
            // again in the same frame.
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
        /// </summary>
        public void Tick()
        {
            LeftGrip = leftGrip.action.ReadValue<float>();
            LeftTrigger = leftTrigger.action.ReadValue<float>();

            RightGrip = rightGrip.action.ReadValue<float>();
            RightTrigger = rightTrigger.action.ReadValue<float>();

            MoveAxis = moveAction.action.ReadValue<Vector2>();
            TurnAxis = turnAction.action.ReadValue<Vector2>();

            CrouchPressed = crouchAction.action.WasPressedThisFrame();
            SprintPressed = sprintAction.action.WasPressedThisFrame();
            JumpPressed = jumpAction.action.WasPressedThisFrame();
        }
    }
}