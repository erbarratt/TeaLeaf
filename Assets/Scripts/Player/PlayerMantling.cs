using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Mantling: a quick, committed move from gripping a ledge to standing
    /// (or crouching) on top of it. This class decides when a mantle is
    /// possible and shows the MantleIndicator arrow while it is; the stick
    /// trigger and the mantle movement itself come next.
    ///
    /// A mantle is possible when:
    /// - a hand is gripping a mantleable ClimbableEdge, and
    /// - the head has been pulled up to within headBelowTopAllowance of the
    ///   ledge top - no mantling from a full arm-hang.
    ///
    /// Where the mantle lands and whether it ends crouched are set per edge
    /// by the level designer (ClimbableEdge.MantlePointWorld /
    /// MantleEndsCrouched) rather than worked out here - so every mantle onto
    /// a given ledge always ends in exactly the same place.
    ///
    /// Ticked explicitly by PlayerController, after PlayerClimbing.Tick(),
    /// so it sees this frame's grips. No physics queries - just a couple of
    /// field reads and a comparison.
    /// </summary>
    public class PlayerMantling : MonoBehaviour
    {
        [SerializeField] private PlayerClimbing playerClimbing;
        [SerializeField] private PlayerTracking playerTracking;

        // The head-locked arrow shown while a mantle is possible.
        [SerializeField] private MantleIndicator mantleIndicator;

        // How far below the ledge top the head may be for a mantle to be
        // offered, in metres. Smaller means pulling yourself up further.
        [SerializeField] private float headBelowTopAllowance = 0.3f;

        /// <summary>
        /// True while a mantle is possible this frame (the arrow is showing).
        /// </summary>
        public bool CanMantle { get; private set; }

        /// <summary>
        /// The edge a mantle would go onto this frame, or null. Only set
        /// while CanMantle.
        /// </summary>
        public ClimbableEdge MantleEdge { get; private set; }

        /// <summary>
        /// Editor-only: fills in references when the component is first
        /// added. PlayerClimbing and PlayerTracking live on the Player root
        /// alongside this; the arrow lives under the Main Camera.
        /// </summary>
        private void Reset()
        {
            playerClimbing = GetComponent<PlayerClimbing>();
            playerTracking = GetComponent<PlayerTracking>();
            mantleIndicator = GetComponentInChildren<MantleIndicator>();
        }

        /// <summary>
        /// Works out whether a mantle is possible this frame and shows or
        /// hides the arrow to match.
        /// </summary>
        public void Tick()
        {
            MantleEdge = FindMantleEdge();
            CanMantle = MantleEdge is not null;
            mantleIndicator.SetVisible(CanMantle);
        }

        /// <summary>
        /// A gripped, mantleable edge the head has been pulled up close
        /// enough to, or null. Checks the left hand first - if the two hands
        /// are on different mantleable edges, either is a fine ledge to
        /// mantle.
        /// </summary>
        private ClimbableEdge FindMantleEdge()
        {
            ClimbableEdge left = playerClimbing.LeftGrabbedEdge;

            if (IsMantleableFromHere(left)) {
                return left;
            }

            ClimbableEdge right = playerClimbing.RightGrabbedEdge;

            return IsMantleableFromHere(right) ? right : null;
        }

        /// <summary>
        /// Whether edge is gripped (not null), mantleable, and the head is
        /// high enough relative to its top. The lip point's height is the
        /// ledge top, since mantleable edges are always horizontal.
        /// </summary>
        private bool IsMantleableFromHere(ClimbableEdge edge)
        {
            if (edge is null || !edge.IsMantleable) {
                return false;
            }

            Vector3 headPosition = playerTracking.HeadPosition;
            float ledgeTop = edge.ClosestLipPoint(headPosition).y;

            return headPosition.y >= ledgeTop - headBelowTopAllowance;
        }
    }
}
