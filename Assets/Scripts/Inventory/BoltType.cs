namespace Inventory
{
    /// <summary>
    /// The kinds of crossbow bolt the player carries a count of. Stored as
    /// its number, and used as an index into the counts: add new values at
    /// the end, and never give them explicit numbers.
    /// </summary>
    public enum BoltType
    {
        // Puts out a torch.
        Water,
        // Sticks where it lands and makes noise, to draw guards.
        Noisemaker,
        // Sticks into wood and hangs a rope to climb.
        Rope,
        // Stuns a guard it hits on the head, for a while.
        Stun,
        // A dart: knocks out a guard it hits anywhere, after a wait.
        Sleep
    }
}
