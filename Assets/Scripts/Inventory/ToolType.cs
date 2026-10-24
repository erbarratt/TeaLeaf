namespace Inventory
{
    /// <summary>
    /// The tools the player can own. The lockpicks aren't here: once owned
    /// they're always on the back of one hand. Stored as its number: add
    /// new values at the end.
    /// </summary>
    public enum ToolType
    {
        // From-behind takedowns on unaware guards.
        Blackjack,
        // The bow: shoots whichever kind of arrow is chosen.
        Bow
    }
}
