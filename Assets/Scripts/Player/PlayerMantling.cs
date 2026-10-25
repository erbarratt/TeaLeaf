using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Mantling: a quick, committed move from gripping a ledge to standing
    /// (or crouching) on top of it.
    ///
    /// A mantle is possible (and the MantleIndicator arrow shows) when a
    /// hand is gripping something mantleable (IMantleable) that says so:
    /// - a mantleable ClimbableEdge, once the head has been pulled up to
    ///   within headBelowTopAllowance of the ledge top - no mantling from a
    ///   full arm-hang;
    /// - a mantleable Ladder, while a hand is on its top rung.
    /// Pushing up on either thumbstick while it's possible starts it.
    ///
    /// Where the mantle lands and whether it ends crouched are set per
    /// target by the level designer (IMantleable.GetMantleLanding() /
    /// MantleEndsCrouched). For a ledge, by default every mantle onto a ledge ends in
    /// exactly the same place, or, for an edge with "Move Horizontally To
    /// Point" off, straight ahead of the player at that place's height and
    /// distance from the lip.
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
        [Tooltip("The input system on the Player root: a mantle starts when either stick is pushed up.")]
        [SerializeField] private PlayerInputXR playerInput;
        [Tooltip("The climbing on the Player root: what each hand grips, let go of as a mantle starts.")]
        [SerializeField] private PlayerClimbing playerClimbing;
        [Tooltip("The locomotion on the Player root: told when a mantle starts and ends, so it drops its momentum and sets the crouch.")]
        [SerializeField] private PlayerLocomotion playerLocomotion;
        [Tooltip("The tracking on the Player root: the head's height decides whether a ledge can be mantled yet.")]
        [SerializeField] private PlayerTracking playerTracking;
        [Tooltip("The CharacterController on the Player root, disabled for the mantle's move and re-enabled at the end.")]
        [SerializeField] private CharacterController characterController;

        [Tooltip("The Player root: the transform the CharacterController is on, and the one the mantle moves.")]
        [SerializeField] private Transform playerTransform;

        [Tooltip("The head-locked arrow under the Main Camera, shown while a mantle is possible.")]
        [SerializeField] private MantleIndicator mantleIndicator;

        [Header("Mantle Position")]

        [Tooltip("How far below a ledge's top the head may be for a mantle to be offered, in metres. Smaller means pulling yourself up further first.")]
        [SerializeField] private float headBelowTopAllowance = 0.3f;

        [Header("Trigger")]

        [Tooltip("How far up (0-1) either thumbstick must be pushed to start a mantle.")]
        [SerializeField] private float stickUpThreshold = 0.7f;

        [Header("Movement")]

        [Tooltip("How long the whole mantle takes, in seconds.")]
        [SerializeField] private float duration = 0.4f;

        // How far above the edge the feet rise before moving across it.
        [Tooltip("How far above the edge (a ledge's lip, a ladder's top) the feet rise before moving across it, in metres.")]
        [SerializeField] private float edgeClearance = 0.05f;

        // How far before and after each corner of the path (up, across,
        // down) the curve round it starts and ends. Never more than half
        // of either leg it joins, so the two curves can't overlap.
        [Tooltip("How far before and after each corner of the path (up, across, down) the curve round it starts and ends, in metres. Capped at half of the shorter leg it joins; 0 = sharp corners.")]
        [SerializeField] private float cornerRadius = 0.25f;

        // Extra height added to the landing, in metres, so the re-enabled
        // CharacterController never starts slightly inside the floor.
        // Gravity settles the last couple of centimetres on the next frame.
        [Tooltip("Extra height added to the landing, in metres, so the re-enabled CharacterController never starts inside the floor.")]
        [SerializeField] private float landingLift = 0.02f;

        /// <summary>
        /// True while a mantle is possible this frame (the arrow is showing).
        /// </summary>
        public bool CanMantle { get; private set; }

        /// <summary>
        /// What a mantle would go onto this frame (a ledge or a ladder), or
        /// null. Only set while CanMantle.
        /// </summary>
        public IMantleable MantleTarget { get; private set; }

        /// <summary>
        /// True from the frame a mantle starts until the frame it lands.
        /// PlayerController hands this class the whole body while it's true.
        /// </summary>
        public bool IsMantling { get; private set; }

        // Points each curved corner of the path is laid out with.
        private const int CornerSteps = 8;

        // The mantle path as rig positions: the start, each corner's curve
        // (CornerSteps + 1 points) and the end. Worked out once when a
        // mantle starts; made once here, so nothing is allocated in play.
        private readonly Vector3[] _pathPoints = new Vector3[2 + 2 * (CornerSteps + 1)];

        // Distance along the path to each point in _pathPoints, and its
        // whole length.
        private readonly float[] _pathDistances = new float[2 + 2 * (CornerSteps + 1)];
        private float _pathLength;

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

            MantleTarget = FindMantleTarget();
            CanMantle = MantleTarget is not null;
            mantleIndicator.SetVisible(CanMantle);

            if (CanMantle && IsStickPushedUp()) {
                StartMantle(MantleTarget);
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
        /// Commits to a mantle onto target: works out the rig's end position,
        /// lets go of the ledge, hands the body over from locomotion, and
        /// turns off collision for the move.
        /// </summary>
        private void StartMantle(IMantleable target)
        {
            // The mantle point is where the FEET go. The rig origin isn't the
            // feet - the capsule is re-centred under the headset, so it sits
            // off to the side of the origin - so move the rig by however far
            // the capsule's bottom is from the point, and the capsule lands
            // exactly on it.
            Vector3 feetStart = CapsuleBottom();
            Vector3 feetTarget = target.GetMantleLanding(feetStart) + Vector3.up * landingLift;
            Vector3 startPosition = playerTransform.position;
            Vector3 targetPosition = startPosition + (feetTarget - feetStart);

            // How high the feet go before moving across: clear of the edge,
            // and never below the landing (a landing above the edge is then
            // reached on the way up) or below where they already are.
            float feetTop = Mathf.Max(target.GetMantleEdgeHeight(feetStart) + edgeClearance, feetTarget.y, feetStart.y);
            float rise = feetTop - feetStart.y;

            BuildPath(startPosition, targetPosition, rise);
            _elapsed = 0f;

            playerClimbing.ReleaseAll();
            playerLocomotion.BeginMantle(target.MantleEndsCrouched);

            // A disabled CharacterController takes no part in physics, so the
            // rig can be positioned directly. Re-enabling it at the end syncs
            // its collider to wherever the transform has got to.
            characterController.enabled = false;

            IsMantling = true;
            CanMantle = false;
            MantleTarget = null;
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
        /// Lays out the mantle path from start to target (rig positions)
        /// in three legs: straight up by rise, across level to above the
        /// target, then straight down onto it (no length when the target is
        /// at the top). Each of the two corners is rounded off with a curve
        /// (a quadratic Bezier with the corner as its middle control point)
        /// starting and ending cornerRadius from it, so the player sweeps
        /// round the corners instead of stopping and turning. A leg of no
        /// length makes its corner sharp, i.e. no curve.
        /// </summary>
        private void BuildPath(Vector3 start, Vector3 target, float rise)
        {
            Vector3 top = start + Vector3.up * rise;
            Vector3 overTarget = new(target.x, top.y, target.z);

            int count = 0;
            _pathPoints[count++] = start;
            count = AddCorner(start, top, overTarget, count);
            count = AddCorner(top, overTarget, target, count);
            _pathPoints[count] = target;

            // Distances along the path, so it can be travelled at an even
            // speed whatever the legs' lengths.
            _pathDistances[0] = 0f;

            for (int i = 1; i < _pathPoints.Length; i++) {
                _pathDistances[i] = _pathDistances[i - 1] + Vector3.Distance(_pathPoints[i - 1], _pathPoints[i]);
            }

            _pathLength = _pathDistances[_pathPoints.Length - 1];
        }

        /// <summary>
        /// Writes the curve round corner (coming from previous, going on to
        /// next) into _pathPoints from index, and returns the index after
        /// it. The curve starts and ends the same distance from the corner:
        /// cornerRadius, capped at half of each leg.
        /// </summary>
        private int AddCorner(Vector3 previous, Vector3 corner, Vector3 next, int index)
        {
            Vector3 toPrevious = previous - corner;
            Vector3 toNext = next - corner;
            float radius = Mathf.Min(cornerRadius, toPrevious.magnitude * 0.5f, toNext.magnitude * 0.5f);
            radius = Mathf.Max(radius, 0f);

            Vector3 curveStart = corner + toPrevious.normalized * radius;
            Vector3 curveEnd = corner + toNext.normalized * radius;

            for (int step = 0; step <= CornerSteps; step++) {
                float s = (float)step / CornerSteps;
                float u = 1f - s;
                _pathPoints[index++] = u * u * curveStart + 2f * u * s * corner + s * s * curveEnd;
            }

            return index;
        }

        /// <summary>
        /// Where the rig is at progress t (0-1): the point that far along
        /// the path by distance, eased out - quick off the wall, settling
        /// onto the landing.
        /// </summary>
        private Vector3 PathPosition(float t)
        {
            float eased = 1f - (1f - t) * (1f - t);
            float distance = eased * _pathLength;

            for (int i = 1; i < _pathPoints.Length; i++) {
                if (_pathDistances[i] < distance) {
                    continue;
                }

                float span = _pathDistances[i] - _pathDistances[i - 1];
                float along = span > 0f ? (distance - _pathDistances[i - 1]) / span : 1f;
                return Vector3.Lerp(_pathPoints[i - 1], _pathPoints[i], along);
            }

            return _pathPoints[_pathPoints.Length - 1];
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
        /// Something a hand is gripping that can be mantled from here (a
        /// ledge the head has been pulled up to, a ladder gripped by its top
        /// rung), or null. Checks the left hand first - if the two hands are
        /// on different mantleable things, either is fine to mantle.
        /// </summary>
        private IMantleable FindMantleTarget()
        {
            IMantleable left = MantleableFromHere(playerClimbing.LeftGrabbed, playerClimbing.LeftGrabPoint);

            if (left is not null) {
                return left;
            }

            return MantleableFromHere(playerClimbing.RightGrabbed, playerClimbing.RightGrabPoint);
        }

        /// <summary>
        /// grabbed as a mantle target, if it is one and says a mantle is
        /// possible for a hand gripping it at gripPoint - otherwise null
        /// (including when the hand grips nothing). The rule itself belongs
        /// to the target - see IMantleable.CanMantleFrom(). The type check
        /// is cheap, fine every frame.
        /// </summary>
        private IMantleable MantleableFromHere(IClimbable grabbed, Vector3 gripPoint)
        {
            if (grabbed is IMantleable mantleable
                && mantleable.CanMantleFrom(gripPoint, playerTracking.HeadPosition, headBelowTopAllowance)) {
                return mantleable;
            }

            return null;
        }
    }
}
