namespace Interaction
{
    /// <summary>
    /// Anything the player can grab and climb by pulling on it - ledges now,
    /// ladders next, ropes later. PlayerClimbing grabs whatever IClimbable a
    /// hand ray is on, so a new kind of climbable needs no player code.
    ///
    /// It adds nothing to IHandSnapTarget yet: every climbable moves the
    /// player the same way (freely, by the inverse of the hand's movement),
    /// so all it says is "grabbing this starts a climb". That's still worth
    /// its own type - door handles, tools and props will be snap targets too,
    /// and grabbing one of those must not start a climb. If a climbable ever
    /// needs to shape the movement (e.g. locking a rope to its axis), that
    /// method belongs here.
    /// </summary>
    public interface IClimbable : IHandSnapTarget
    {
    }
}
