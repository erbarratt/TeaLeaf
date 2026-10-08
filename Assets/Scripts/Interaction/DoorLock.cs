namespace Interaction
{
    /// <summary>
    /// What kind of lock a door has. The kind never changes while playing;
    /// whether the door is locked right now is Door.IsLocked.
    ///
    /// Add new values at the end: a door stores its kind as the number.
    /// </summary>
    public enum DoorLock
    {
        // No lock at all: the handle always opens it.
        None,
        // A lock that can be picked.
        Simple,
        // A lock that only its own key opens (Door's keyId).
        Keyed
    }
}
