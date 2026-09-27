using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Mantling: a quick, committed move from gripping a ledge to standing
    /// (or crouching) on top of it.
    ///
    /// A mantle is possible (and the MantleIndicator arrow shows) when:
    /// - a hand is gripping a mantleable ClimbableEdge, and
    /// - the head has been pulled up to within headBelowTopAllowance of the
    ///   ledge top - no mantling from a full arm-hang.
    /// Pushing up on either thumbstick while it's possible starts it.
    ///
    /// Where the mantle lands and whether it ends crouched are set per edge
    /// by the level designer (ClimbableEdge.MantlePointWorld /
    /// MantleEndsCrouched), so every mantle onto a ledge ends in exactly the
    /// same place.
    ///
    /// Once started, the mantle owns the player's body until it finishes:
    /// PlayerController stops ticking climbing, movement, gravity, jump,
    /// turning and Move(), and it can't be cancelled. The rig is moved
    /// directly along an eased path with the CharacterController disabled,
    /// rather than through Move(): the landing point is designer-placed and
    /// always valid, and collision on the way (the lip, the wall) could
    /// only block or deflect the move and stop it landing there.
    ///
    /// Ticked explicitly by PlayerController, after PlayerClimbing.Tick(),
    /// so it sees this frame's grips.
    /// </summary>
    public class PlayerMantling : MonoBehaviour
    {
        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerClimbing playerClimbing;
        [SerializeField] private PlayerLocomotion playerLocomotion;
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private CharacterController characterController;

        // The rig root - the transform the CharacterController is on, and the
        // one the mantle moves.
        [SerializeField] private Transform playerTransform;

        // The head-locked arrow shown while a mantle is possible.
        [SerializeField] private MantleIndicator mantleIndicator;

        [Header("Mantle Position")]

        // How far below the ledge top the head may be for a mantle to be
        // offered, in metres. Smaller means pulling yourself up further.
        [SerializeField] private float headBelowTopAllowance = 0.3f;

        [Header("Trigger")]

        // How far up (0-1) either thumbstick must be pushed to start a mantle.
        [SerializeField] private float stickUpThreshold = 0.7f;

        [Header("Movement")]

        // How long the whole mantle takes, in seconds.
        [SerializeField] private float duration = 0.4f;

        // Fraction of the mantle (0-1) by which the rise is complete. The
        // rise eases out, so it's quick at first and settles at the top.
        [SerializeField] private float riseEndsAt = 0.6f;

        // Fraction of the mantle (0-1) at which moving forward onto the
        // ledge begins. Starting before the rise ends makes the path one
        // smooth curve up and over instead of "up, stop, forward".
        [SerializeField] private float forwardStartsAt = 0.3f;

        // Extra height added to the landing, in metres, so the re-enabled
        // CharacterController never starts slightly inside the floor.
        // Gravity settles the last couple of centimetres on the next frame.
        [SerializeField] private float landingLift = 0.02f;

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
        /// True from the frame a mantle starts until the frame it lands.
        /// PlayerController hands this class the whole body while it's true.
        /// </summary>
        public bool IsMantling { get; private set; }

        // Rig position when the mantle started, and where it must end.
        private Vector3 _startPosition;
        private Vector3 _targetPosition;

        private float _elapsed;

        /// <summary>
        /// Editor-only: fills in references when the component is first
        /// added. Everything except the arrow lives on the Player root
        /// alongside this; the arrow lives under the Main Camera.
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponent<PlayerInputXR>();
            playerClimbing = GetComponent<PlayerClimbing>();
            playerLocomotion = GetComponent<PlayerLocomotion>();
            playerTracking = GetComponent<PlayerTracking>();
            characterController = GetComponent<CharacterController>();
            playerTransform = transform;
            mantleIndicator = GetComponentInChildren<MantleIndicator>();
        }

        /// <summary>
        /// While mantling, advances the mantle. Otherwise works out whether a
        /// mantle is possible, shows or hides the arrow to match, and starts
        /// one if a stick is pushed up.
        /// </summary>
        public void Tick()
        {
            if (IsMantling) {
                AdvanceMantle();
                return;
            }

            MantleEdge = FindMantleEdge();
            CanMantle = MantleEdge is not null;
            mantleIndicator.SetVisible(CanMantle);

            if (CanMantle && IsStickPushedUp()) {
                StartMantle(MantleEdge);
            }
        }

        /// <summary>
        /// Either thumbstick pushed up past stickUpThreshold. The right
        /// stick's Y is otherwise unused, so this doesn't clash with turning
        /// (which only reads X).
        /// </summary>
        private bool IsStickPushedUp()
        {
            return playerInput.MoveAxis.y >= stickUpThreshold
                   || playerInput.TurnAxis.y >= stickUpThreshold;
        }

        /// <summary>
        /// Commits to a mantle onto edge: works out the rig's end position,
        /// lets go of the ledge, hands the body over from locomotion, and
        /// turns off collision for the move.
        /// </summary>
        private void StartMantle(ClimbableEdge edge)
        {
            // The mantle point is where the FEET go. The rig origin isn't the
            // feet - the capsule is re-centred under the headset, so it sits
            // off to the side of the origin - so move the rig by however far
            // the capsule's bottom is from the point, and the capsule lands
            // exactly on it.
            Vector3 feetTarget = edge.MantlePointWorld + Vector3.up * landingLift;
            _startPosition = playerTransform.position;
            _targetPosition = _startPosition + (feetTarget - CapsuleBottom());
            _elapsed = 0f;

            playerClimbing.ReleaseAll();
            playerLocomotion.BeginMantle(edge.MantleEndsCrouched);

            // A disabled CharacterController takes no part in physics, so the
            // rig can be positioned directly. Re-enabling it at the end syncs
            // its collider to wherever the transform has got to.
            characterController.enabled = false;

            IsMantling = true;
            CanMantle = false;
            MantleEdge = null;
            mantleIndicator.SetVisible(false);

            // Move on the starting frame too, so the push feels immediate.
            AdvanceMantle();
        }

        /// <summary>
        /// Moves the rig one frame further along the mantle path, and hands
        /// control back once it arrives.
        /// </summary>
        private void AdvanceMantle()
        {
            _elapsed += Time.deltaTime;
            float t = duration > 0f ? Mathf.Clamp01(_elapsed / duration) : 1f;

            playerTransform.position = PathPosition(t);

            if (t < 1f) {
                return;
            }

            characterController.enabled = true;
            playerLocomotion.EndMantle();
            IsMantling = false;
        }

        /// <summary>
        /// Where the rig is at progress t (0-1). Height and horizontal
        /// movement follow separate, overlapping curves: the rise eases out
        /// and is done by riseEndsAt; the forward move eases in and out from
        /// forwardStartsAt to the end. Separately, each would be "up, then
        /// over"; overlapped, they make one smooth arc that clears the lip.
        /// </summary>
        private Vector3 PathPosition(float t)
        {
            Vector3 offset = _targetPosition - _startPosition;

            float riseT = riseEndsAt > 0f ? Mathf.Clamp01(t / riseEndsAt) : 1f;
            float rise = 1f - (1f - riseT) * (1f - riseT);

            float forwardSpan = 1f - forwardStartsAt;
            float forwardT = forwardSpan > 0f ? Mathf.Clamp01((t - forwardStartsAt) / forwardSpan) : 1f;
            float forward = Mathf.SmoothStep(0f, 1f, forwardT);

            return _startPosition + new Vector3(offset.x * forward, offset.y * rise, offset.z * forward);
        }

        /// <summary>
        /// World position of the bottom of the CharacterController's capsule -
        /// the player's feet. Its centre is re-centred under the headset every
        /// frame (see PlayerLocomotion), so this follows the player's real
        /// body, not the rig origin.
        /// </summary>
        private Vector3 CapsuleBottom()
        {
            Vector3 localBottom = characterController.center + Vector3.down * (characterController.height * 0.5f);
            return playerTransform.TransformPoint(localBottom);
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
