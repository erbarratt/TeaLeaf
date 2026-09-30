namespace Interaction
{
    /// <summary>
    /// Which finger pose a hand plays while snapped to something. Grab
    /// targets only name the pose they want; PlayerHandAnimation owns how
    /// each one maps onto the hand Animator, so animation details stay in
    /// one place. Add new values (HoldObject, ...) as new snap targets need
    /// them. Always add new values at the END and never give them explicit
    /// numbers: HandSnapProfile assets store the pose as its number (0, 1,
    /// 2, ...), and PlayerHandAnimation uses the number as an array index,
    /// so inserting or reordering values would silently change every
    /// profile's pose.
    /// </summary>
    public enum HandPose
    {
        // Fingers hooked over the top of a ledge (ClimbableEdge).
        LedgeGrip,
        // Fingers wrapped round a thin bar (Ladder rungs).
        RungGrip,
        // Fingers wrapped round a thicker, vertical rope (ClimbableRope).
        RopeGrip
    }
}
