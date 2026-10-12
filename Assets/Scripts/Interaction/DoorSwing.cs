namespace Interaction
{
    /// <summary>
    /// Which way a door opens from closed: the side of the doorway its
    /// leaf swings out to, along the door's local Z.
    ///
    /// Add new values at the end: a door stores its kind as the number.
    /// </summary>
    public enum DoorSwing
    {
        // Either way, as far as maxOpenAngle each side.
        BothWays,
        // Only out towards the door's local +Z.
        TowardsPlusZ,
        // Only out towards the door's local -Z.
        TowardsMinusZ
    }
}
