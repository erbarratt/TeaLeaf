namespace Inventory
{
    /// <summary>
    /// The tools the player can own and equip from the wrist menu. The
    /// lockpicks aren't here: once owned they're always on the back of the
    /// left hand. Stored as its number: add new values at the end.
    /// </summary>
    public enum ToolType
    {
        // From-behind takedowns on unaware guards.
        Blackjack,
        // The hand crossbow: fires whichever bolt type is selected.
        Crossbow
    }
}
