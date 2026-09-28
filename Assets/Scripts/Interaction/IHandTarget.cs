using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Marks anything a hand ray can target - currently ClimbableEdge,
    /// Ladder and ClimbableRope, later doors, loot, locks, props, ... The
    /// hand's reticle shows where the ray hits one, and other systems decide
    /// what targeting it means (e.g. PlayerClimbing grabs it if it's also
    /// IClimbable). Nothing is highlighted: the reticle alone tells the
    /// player they can interact.
    ///
    /// It only lets PlayerHandInteraction find targets without knowing each
    /// concrete type. Implementers must register their Collider with
    /// HandTargetRegistry (Register in OnEnable, Unregister in OnDisable) -
    /// see ClimbableEdge for the pattern. PlayerHandInteraction looks hit
    /// Colliders up there rather than calling GetComponent, so an IHandTarget
    /// that never registers will just never be found.
    /// </summary>
    public interface IHandTarget
    {
        /// <summary>
        /// Whether a hand ray cast from rayOrigin may target this - e.g. a
        /// ladder only from its climbing side. A refused ray still stops at
        /// the target; it just finds nothing to interact with, so there's no
        /// reticle and no grab.
        ///
        /// Has a default body (true), so only targets that care override it:
        /// a C# "default interface method" - implementers that don't define
        /// it simply use this one.
        /// </summary>
        bool CanBeTargetedFrom(Vector3 rayOrigin)
        {
            return true;
        }
    }
}
