using UnityEngine;

namespace Player
{
    /// Logs thumbstick input once per second so we can verify
    /// that locomotion actions are wired correctly before we
    /// start moving the player.
    public class LocomotionInputTest : MonoBehaviour
    {
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