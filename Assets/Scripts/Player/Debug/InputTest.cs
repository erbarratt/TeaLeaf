using UnityEngine;

//littlecomment
//ANother

namespace Player
{
    public class InputTest : MonoBehaviour
    {
        [Tooltip("The input system whose right trigger is watched and logged when it passes half way.")]
        [SerializeField] private PlayerInputXR playerInput;

        private bool _lastState;

        private void Update()
        {
            bool state = playerInput.IsRightUsing;

            if (state != _lastState)
            {
                Debug.Log($"Right Using: {state}");

                _lastState = state;
            }
        }
    }
} 