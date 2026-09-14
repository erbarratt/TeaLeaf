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

        // Reference to the turning thumbstick action.
        // Using Snap Turn rather than Turn because the HP Reverb G2 with Oasis
        // drivers does not currently populate the Turn action correctly.
        [SerializeField] private InputActionReference turnAction;

        [Header("Crouch")]
        // Reference to the crouch button action. Read as a single press rather
        // than a held value, since crouch is a toggle - see PlayerLocomotion.
        [SerializeField] private InputActionReference crouchAction;

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

        private void Update()
        {
            // Self-sufficient fallback for anything that reads this class
            // without explicitly calling Tick() itself - e.g. the
            // standalone Debug/ input test scripts, which are meant to work
            // without a full PlayerLocomotion set up. Redundant with, but
            // harmless alongside, the explicit call PlayerLocomotion makes
            // below - both just cache the same live Input System state
            // again in the same frame.
            Tick();
        }

        /// <summary>
        /// Caches this frame's raw input values. Called explicitly, and
        /// first, by PlayerLocomotion.Update() - before anything else reads
        /// this frame's input - so every Tick()-sequenced system
        /// (PlayerClimbing, PlayerHandInteraction, PlayerHandAnimation) is
        /// guaranteed fresh values regardless of Unity's own (unspecified)
        /// Update() order between this component and PlayerLocomotion.
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
        }
    }
}