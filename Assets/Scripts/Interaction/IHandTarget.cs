namespace Interaction
{
    /// <summary>
    /// Marks anything a hand ray can target - currently ClimbableEdge and
    /// Ladder, later doors, loot, locks, props, ... The hand's reticle shows
    /// where the ray hits one, and other systems decide what targeting it
    /// means (e.g. PlayerClimbing grabs it if it's also IClimbable). Nothing
    /// is highlighted: the reticle alone tells the player they can interact.
    ///
    /// It has no members - it only lets PlayerHandInteraction find targets
    /// without knowing each concrete type. Implementers must register their
    /// Collider with HandTargetRegistry (Register in OnEnable, Unregister in
    /// OnDisable) - see ClimbableEdge for the pattern. PlayerHandInteraction
    /// looks hit Colliders up there rather than calling GetComponent, so an
    /// IHandTarget that never registers will just never be found.
    /// </summary>
    public interface IHandTarget
    {
    }
}
