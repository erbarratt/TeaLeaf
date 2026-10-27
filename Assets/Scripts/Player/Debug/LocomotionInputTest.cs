using UnityEngine;

namespace Player
{
    /// Logs thumbstick input once per second so we can verify
    /// that locomotion actions are wired correctly before we
    /// start moving the player.
    public class LocomotionInputTest : MonoBehaviour
    {
        [Tooltip("The input system whose thumbsticks are logged once a second, e.g. drag the Player root here. Must be set: it isn't looked for.")]
        [SerializeField] private PlayerInputXR playerInput;

        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;

            if (_timer < 1f)
            {
                return;
            }

            _timer = 0f;

            Debug.Log(
                $"Move: {playerInput.MoveAxis} | Turn: {playerInput.TurnAxis}");
        }
    }
}