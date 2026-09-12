namespace Interaction
{
    /// <summary>
    /// Implemented by anything that can be highlighted when a hand ray points
    /// at it - currently just ClimbableEdge, but any future interactable
    /// (levers, loot, doors, ...) can opt into hand-pointing highlighting by
    /// implementing this instead of PlayerHandInteraction needing to know
    /// about each concrete type.
    ///
    /// Implementers must also register their Collider with
    /// HighlightableRegistry (Register in OnEnable, Unregister in
    /// OnDisable) - see ClimbableEdge for the pattern. PlayerHandInteraction
    /// looks hit Colliders up there rather than calling GetComponent, so an
    /// IHighlightable that never registers will just never be found.
    /// </summary>
    public interface IHighlightable
    {
        /// <summary>
        /// Switches this object's highlighted appearance on or off.
        /// </summary>
        void SetHighlighted(bool highlighted);
    }
}
