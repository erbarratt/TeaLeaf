namespace Player
{
    /// <summary>
    /// What the player's body is doing this frame, as one value. Systems that
    /// care about how the player is moving - footstep noise, shadow-volume
    /// visibility, guard perception, the wrist visibility gem - switch on
    /// this rather than each re-deriving it from crouch/sprint/climb flags,
    /// so they can never disagree with each other.
    ///
    /// Crouch is folded in (CrouchStill/CrouchWalking) rather than kept as a
    /// separate flag, since crouched-and-still and crouched-and-moving need
    /// different noise and visibility values anyway.
    ///
    /// Set once per frame by PlayerLocomotion.TickState(), after the
    /// CharacterController has moved.
    /// </summary>
    public enum MovementState
    {
        Still,
        Walking,
        Sprinting,
        CrouchStill,
        CrouchWalking,
        Climbing,
        Airborne,
        Mantling
    }
}
