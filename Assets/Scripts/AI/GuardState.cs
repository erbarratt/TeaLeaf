namespace AI
{
    /// What a guard is doing. One at a time: the guard's whole behaviour
    /// is a switch on this.
    public enum GuardState
    {
        // Walking its route, or standing at its post.
        Patrol,
        // Stopped and turned towards something it half saw or heard.
        Suspicious,
        // Gone to look: at the place, then at a few places round it.
        Search,
        // Has seen the player and is running at them.
        Chase,
        // Hit on the head by a stun bolt: stands senseless for a while,
        // then searches.
        Stunned,
        // Knocked out. It stays down until another guard wakes it.
        KnockedOut
    }
}
