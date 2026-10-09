using UnityEngine;

namespace Player
{
    /// <summary>
    /// Debug-only: writes to the Console each time what either hand is
    /// busy with changes (PlayerHandState) - "Left: None -> Climbing" -
    /// for checking that the hand systems hand over to each other
    /// cleanly, and that a hand is never left busy with nothing.
    ///
    /// It asks the hand state every frame, which a debug script may; the
    /// game itself never does. On the Hands object, with the hand state.
    /// </summary>
    public class HandStateDebug : MonoBehaviour
    {
        [SerializeField] private PlayerHandState playerHandState;

        private HandUse _lastLeft = HandUse.None;
        private HandUse _lastRight = HandUse.None;

        /// <summary>
        /// Editor-only: runs when the component is added.
        /// </summary>
        private void Reset()
        {
            playerHandState = GetComponent<PlayerHandState>();
        }

        /// <summary>
        /// Start() rather than Awake(): a scene set up before the hand
        /// state existed has one added by the first hand system's Awake().
        /// </summary>
        private void Start()
        {
            if (playerHandState == null) {
                playerHandState = GetComponent<PlayerHandState>();
            }

            if (playerHandState == null) {
                Debug.LogWarning("HandStateDebug: no PlayerHandState on this object.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            Report("Left", playerHandState.LeftUse, ref _lastLeft);
            Report("Right", playerHandState.RightUse, ref _lastRight);
        }

        /// <summary>
        /// Logs one hand's change, if it has changed. The string is only
        /// built then.
        /// </summary>
        private static void Report(string hand, HandUse use, ref HandUse last)
        {
            if (use == last) {
                return;
            }

            Debug.Log($"[HandState] {hand}: {last} -> {use}");
            last = use;
        }
    }
}
