using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{

    public class VRDebugInput : MonoBehaviour
    {
        [Tooltip("The left trigger action, logged whenever its value moves by more than 0.05 since last logged, e.g. the Player/LeftTrigger action. Must be set.")]
        [SerializeField] private InputActionReference leftTrigger;
        [Tooltip("The right trigger action, logged whenever its value moves by more than 0.05 since last logged, e.g. the Player/RightTrigger action. Must be set.")]
        [SerializeField] private InputActionReference rightTrigger;

        private float _lastLeft;
        private float _lastRight;

        private void Update()
        {
            float left = leftTrigger.action.ReadValue<float>();
            float right = rightTrigger.action.ReadValue<float>();

            if (Mathf.Abs(left - _lastLeft) > 0.05f)
            {
                Debug.Log($"Left Trigger: {left:F2}");
                _lastLeft = left;
            }

            if (Mathf.Abs(right - _lastRight) > 0.05f)
            {
                Debug.Log($"Right Trigger: {right:F2}");
                _lastRight = right;
            }
        }
    }
    
}