using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A climbable the player can mantle off the top of - a ledge
    /// (ClimbableEdge) or the top of a ladder (Ladder). PlayerMantling asks
    /// whatever each hand is gripping whether a mantle is possible right now,
    /// shows the arrow if so, and on a stick push moves the player to where
    /// the target says the mantle lands. Each target has its own rule for
    /// "possible" and its own designer-set landing, so PlayerMantling never
    /// needs to know what kind of thing is being mantled.
    /// </summary>
    public interface IMantleable
    {
        /// <summary>
        /// Whether a mantle onto this ends with the player crouched - for
        /// landings with no room to stand.
        /// </summary>
        bool MantleEndsCrouched { get; }

        /// <summary>
        /// Whether a hand gripping this at gripPoint (world space - where
        /// the hand grabbed it) can mantle now, with the head at
        /// headPosition. headBelowTopAllowance is PlayerMantling's setting
        /// for how far below the top the head may be, in metres; targets
        /// with a different kind of rule (a ladder: the hand is on the top
        /// rung) ignore it. Called every frame a hand grips this, so keep it
        /// cheap.
        /// </summary>
        bool CanMantleFrom(Vector3 gripPoint, Vector3 headPosition, float headBelowTopAllowance);

        /// <summary>
        /// World position a mantle lands the player's feet at, when it
        /// starts with them at feetPosition. Only called when a mantle
        /// starts.
        /// </summary>
        Vector3 GetMantleLanding(Vector3 feetPosition);
    }
}
