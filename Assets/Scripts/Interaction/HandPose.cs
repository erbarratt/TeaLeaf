namespace Interaction
{
    /// <summary>
    /// Which finger pose a hand plays while snapped to something. Grab
    /// targets only name the pose they want; PlayerHandAnimation owns how
    /// each one maps onto the hand Animator, so animation details stay in
    /// one place. Add new values (RungGrip, RopeGrip, HoldObject, ...) as
    /// new snap targets need them.
    /// </summary>
    public enum HandPose
    {
        LedgeGrip
    }
}
