using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Something solid a hand can push out of its way by pressing on it -
    /// an open door now, perhaps shutters or hanging things later. The
    /// physical hands (Player.HandPhysicalFollow) stop at every solid
    /// surface; when the surface belongs to one of these, they also tell
    /// it how hard they were pressing, and it moves itself.
    ///
    /// Implementers must register their solid Collider(s) with
    /// HandPushRegistry - the hand sweep looks hit Colliders up there
    /// rather than calling GetComponent.
    /// </summary>
    public interface IHandPushable
    {
        /// <summary>
        /// A hand pressed on this at point (world space) and was stopped
        /// displacement short of where it was going: the part of its
        /// movement that went into the surface, straight in, in metres.
        /// Called from inside the hand's sweep, at most a few times a
        /// frame, only on frames a hand is stopped by it.
        ///
        /// Returns true if it moved out of the way (and physics knows it
        /// has - see Door.MoveTo()): the hand then tries its move again
        /// in the same frame instead of stopping at the surface. False,
        /// and the hand is held there like at any wall.
        /// </summary>
        bool Push(Vector3 point, Vector3 displacement);
    }
}
