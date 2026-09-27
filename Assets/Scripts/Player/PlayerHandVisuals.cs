using UnityEngine;

namespace Player
{
    /// <summary>
    /// The single owner of where each hand VISUAL (the hand model) is placed -
    /// never the tracked controller, which always follows real tracking.
    /// Anything that wants to put a hand visual somewhere other than on its
    /// controller goes through here, so two systems can never fight over the
    /// same transform.
    ///
    /// Today that's only snapping (a hand blended onto a grab target's
    /// HandSnapPose - see HandVisualSnap), requested by PlayerClimbing and
    /// later by grabbing and tools. Physical hands (the visual stopping at
    /// surfaces instead of passing through them) will be added here too, with
    /// snapping taking priority over it.
    ///
    /// Lives on the Hands object with the other hand systems. No Update() of
    /// its own: PlayerController calls Tick() after Move() and turning - see
    /// Tick().
    /// </summary>
    public class PlayerHandVisuals : MonoBehaviour
    {
        // The hand model transforms (children of the tracked Left/Right Hand
        // controllers), NOT the controllers themselves.
        [SerializeField] private Transform leftHandVisual;
        [SerializeField] private Transform rightHandVisual;

        // Seconds for a hand visual to blend onto its snap pose when grabbing
        // (and back to the controller on release) - short enough to feel
        // instant, long enough not to pop.
        [SerializeField] private float snapBlendDuration = 0.08f;

        /// Blends the left hand visual onto a snap pose and back. Other
        /// systems call Snap()/Release() on it (e.g. PlayerClimbing on grab)
        /// and read its Weight/SnapPose (e.g. PlayerHandAnimation for the
        /// finger pose); only this class ticks it.
        public HandVisualSnap LeftVisualSnap { get; private set; }

        /// The right hand's snap - mirrors LeftVisualSnap.
        public HandVisualSnap RightVisualSnap { get; private set; }

        /// <summary>
        /// Editor-only: runs when the component is first added. Finds the two
        /// hand visuals under this object by their names in Main.unity, so
        /// they don't need dragging in by hand.
        /// </summary>
        private void Reset()
        {
            leftHandVisual = transform.Find("Left Hand/Left Hand Visual");
            rightHandVisual = transform.Find("Right Hand/Right Hand Visual");
        }

        private void Awake()
        {
            // Created once here (capturing each visual's rest pose), never
            // per grab, so snapping doesn't allocate.
            LeftVisualSnap = new HandVisualSnap(leftHandVisual, snapBlendDuration);
            RightVisualSnap = new HandVisualSnap(rightHandVisual, snapBlendDuration);
        }

        /// <summary>
        /// Places both hand visuals for this frame (snapped, blending, or
        /// following the controller). Called by PlayerController after Move()
        /// and turning: the visuals are children of the rig, so placing them
        /// any earlier would let this frame's movement drag a world-space
        /// snap pose away until next frame.
        /// </summary>
        public void Tick()
        {
            LeftVisualSnap.Tick(Time.deltaTime);
            RightVisualSnap.Tick(Time.deltaTime);
        }
    }
}
