using UnityEngine;

//littlecomment
//ANother

namespace Player
{
    public class InputTest : MonoBehaviour
    {
        [Tooltip("The input system whose right trigger is watched: each time it passes half way (either way) a line is logged, e.g. \"Right Using: True\"; drag the Player root here. Must be set: it isn't looked for.")]
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