namespace Core
{
    /// <summary>
    /// Where the level is in its play-through. The single value other
    /// systems read (via LevelManager.State) to know whether the game is
    /// still being played, rather than each keeping its own "is it over?"
    /// flag.
    /// </summary>
    public enum GameState
    {
        // The level is running normally.
        Playing,

        // A guard caught the player: the level fades out and restarts.
        Caught,

        // The objective was carried into an exit zone.
        Won
    }
}
