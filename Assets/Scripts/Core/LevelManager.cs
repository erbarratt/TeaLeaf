using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Core
{
    /// <summary>
    /// Owns the level's GameState and what happens when it ends. One per
    /// scene, on its own object.
    ///
    /// The level starts Playing. Two things end it, and each can only happen
    /// while Playing, so the first one wins and nothing can end it twice:
    /// - Caught() - a guard caught the player.
    /// - Win() - called from here, when the player carries the objective
    ///   into an ExitZone.
    /// Either way the view fades to black (ScreenFade) and the scene is
    /// reloaded once nothing can be seen. The reloaded scene's ScreenFade
    /// starts black and fades in by itself, so there's no "fade back in"
    /// code here.
    ///
    /// Other systems report what happened (Caught(), SetObjectiveCarried())
    /// and this class decides what it means - a guard doesn't reload scenes
    /// itself. Anything that needs to react (music, guards standing down)
    /// subscribes to StateChanged rather than checking State every frame.
    ///
    /// Costs nothing most of the time: Update() only runs while the player
    /// is carrying the objective (the component is disabled otherwise),
    /// because that's the only time an exit zone can do anything.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        // The Player root (its position is the player's feet) - checked
        // against the exit zones. A plain Transform, so Core doesn't need to
        // know anything about the Player namespace.
        [SerializeField] private Transform player;

        // How long the fade to black takes after being caught, in seconds.
        [SerializeField] private float caughtFadeDuration = 1f;

        // How long the fade to black takes after winning, in seconds.
        [SerializeField] private float wonFadeDuration = 2f;

        /// The scene's level manager, so other systems (guards, loot, exit
        /// zones) can reach it without a scene search. Null if the scene has
        /// none.
        public static LevelManager Instance { get; private set; }

        /// Where the level is in its play-through.
        public GameState State { get; private set; } = GameState.Playing;

        /// Whether the player is carrying the objective item right now.
        public bool HasObjective { get; private set; }

        /// Raised once each time State changes, with the new state. An
        /// instance event rather than a static one: the manager is destroyed
        /// with the scene on a restart, so its subscribers go with it and
        /// nothing from the old scene is left subscribed.
        public event Action<GameState> StateChanged;

        private void Awake()
        {
            Instance = this;

            if (player == null) {
                Debug.LogWarning("LevelManager: no Player assigned - exit zones can't detect the player, so the level can't be won.", this);
            }

            // Nothing to check until the objective is picked up - see
            // SetObjectiveCarried().
            enabled = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) {
                Instance = null;
            }
        }

        /// <summary>
        /// Tells the level whether the player is carrying the objective
        /// item: true when it's picked up, false if it's dropped or thrown.
        /// Called by the objective item (not built yet - Phase 3/4). While
        /// carried, Update() runs and watches the exit zones.
        /// </summary>
        public void SetObjectiveCarried(bool isCarried)
        {
            if (State != GameState.Playing) {
                return;
            }

            HasObjective = isCarried;

            // Switches Update() on or off - see the class comment.
            enabled = isCarried;
        }

        /// <summary>
        /// A guard caught the player: fade to black, then restart the level.
        /// Ignored unless the level is still being played.
        /// </summary>
        public void Caught()
        {
            EndLevel(GameState.Caught, caughtFadeDuration);
        }

        /// <summary>
        /// Only runs while the objective is carried. Wins the level the
        /// moment the player's feet are inside any exit zone - a few
        /// multiplications per zone, no physics.
        /// </summary>
        private void Update()
        {
            if (player != null && ExitZone.AnyContains(player.position)) {
                Win();
            }
        }

        /// <summary>
        /// The objective reached an exit zone. For now this fades out and
        /// restarts like being caught; Phase 9 replaces the restart with a
        /// "Mission complete" panel.
        /// </summary>
        private void Win()
        {
            EndLevel(GameState.Won, wonFadeDuration);
        }

        /// <summary>
        /// Leaves Playing for the given end state, tells the subscribers,
        /// then fades out and reloads. Does nothing if the level has already
        /// ended, so being caught during the win fade (or the other way
        /// round) can't change the outcome or start a second reload.
        /// </summary>
        private void EndLevel(GameState endState, float fadeDuration)
        {
            if (State != GameState.Playing) {
                return;
            }

            State = endState;
            enabled = false;
            StateChanged?.Invoke(endState);

            ScreenFade fade = ScreenFade.Instance;

            // The reload waits for the fade's callback, so it happens with
            // the view fully black and the loading hitch is never seen.
            if (fade != null) {
                fade.FadeOut(fadeDuration, ReloadLevel);
            } else {
                ReloadLevel();
            }
        }

        /// <summary>
        /// Loads the current scene again from scratch: every object is
        /// destroyed and recreated as saved, so the player, guards and loot
        /// are all back at their start with nothing to reset by hand.
        /// </summary>
        private void ReloadLevel()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// Play Mode test from the component's right-click menu: get caught.
        /// </summary>
        [ContextMenu("Test Caught")]
        private void TestCaught()
        {
            Caught();
        }

        /// <summary>
        /// Play Mode test from the component's right-click menu: pick up the
        /// objective, so walking into an exit zone wins.
        /// </summary>
        [ContextMenu("Test Take Objective")]
        private void TestTakeObjective()
        {
            SetObjectiveCarried(true);
        }
    }
}
