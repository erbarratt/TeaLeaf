using UnityEngine;
using UnityEngine.InputSystem;

namespace Core
{
    /// <summary>
    /// Debug-only: drives the LevelManager from the keyboard, for testing
    /// the caught/win/restart loop in the headset before guards and the
    /// objective item exist. The Game view needs keyboard focus (click it).
    /// Also logs every state change. Put it on the Debug object.
    /// </summary>
    public class GameStateDebug : MonoBehaviour
    {
        // Get caught: fade out and restart.
        [SerializeField] private Key caughtKey = Key.C;

        // Pick up / put down the objective. While it's carried, walking into
        // an exit zone wins.
        [SerializeField] private Key objectiveKey = Key.O;

        // The manager subscribed to in Start(), kept so the same one is
        // unsubscribed from in OnDestroy().
        private LevelManager _levelManager;

        /// <summary>
        /// Start() rather than Awake(): LevelManager sets Instance in its
        /// Awake(), and every Awake() has run before the first Start().
        /// </summary>
        private void Start()
        {
            _levelManager = LevelManager.Instance;

            if (_levelManager == null) {
                Debug.LogWarning("GameStateDebug: the scene has no LevelManager.", this);
                enabled = false;
                return;
            }

            _levelManager.StateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            if (_levelManager != null) {
                _levelManager.StateChanged -= OnStateChanged;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null) {
                return;
            }

            if (keyboard[caughtKey].wasPressedThisFrame) {
                _levelManager.Caught();
            }

            if (keyboard[objectiveKey].wasPressedThisFrame) {
                _levelManager.SetObjectiveCarried(!_levelManager.HasObjective);
                Debug.Log($"GameStateDebug: objective carried = {_levelManager.HasObjective}", this);
            }
        }

        /// <summary>
        /// Logs each state change as it happens.
        /// </summary>
        private void OnStateChanged(GameState state)
        {
            Debug.Log($"GameStateDebug: game state -> {state}", this);
        }
    }
}
