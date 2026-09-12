using UnityEngine;
using UnityEngine.InputSystem;

//comment

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

        public float LeftGrip => leftGrip.action.ReadValue<float>();
        public float LeftTrigger => leftTrigger.action.ReadValue<float>();

        public float RightGrip => rightGrip.action.ReadValue<float>();
        public float RightTrigger => rightTrigger.action.ReadValue<float>();

        public bool IsLeftGrabbing => LeftGrip > 0.5f;
        public bool IsRightGrabbing => RightGrip > 0.5f;

        public bool IsLeftUsing => LeftTrigger > 0.5f;
        public bool IsRightUsing => RightTrigger > 0.5f;
        
        // Returns the left thumbstick movement vector.
        // X = left/right strafe.
        // Y = forward/backward movement.
            public Vector2 MoveAxis => moveAction.action.ReadValue<Vector2>();

        // Returns the right thumbstick turning vector.
        // X = horizontal turning.
        // Y is unused for now.
            public Vector2 TurnAxis => turnAction.action.ReadValue<Vector2>();

        // True for exactly one frame when the crouch button is pressed.
        public bool CrouchPressed => crouchAction.action.WasPressedThisFrame();

    }
}