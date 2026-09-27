using UnityEngine;

namespace Player
{
    /// <summary>
    /// Makes one hand visual stop at surfaces instead of passing through
    /// them - the "physical hands" collision response. A kinematic sweep, no
    /// Rigidbody: each frame, a capsule the shape of the hand (wrist to
    /// middle fingertip) is swept from where the visual was last frame
    /// towards where the controller wants it. On a hit the hand stops just
    /// short of the surface and slides along it ("collide and slide", the
    /// same idea CharacterController uses), so it never goes into the wall.
    ///
    /// While nothing is in the way the visual stays a child of its controller
    /// and this class doesn't touch it at all. The hands' Tracked Pose
    /// Drivers move the controller once more just before rendering, so a
    /// child visual gets the freshest tracking for free. While blocked, the
    /// visual is detached (like HandVisualSnap does) - otherwise that
    /// before-render update would drag it back into the wall.
    ///
    /// Rotation always follows the controller (decided 2026-09-27); if that
    /// rotates the hand into a surface, a later step pushes it back out.
    ///
    /// A plain C# class owned and ticked by PlayerHandVisuals, like
    /// HandVisualSnap. Whether the visual is attached is always read from its
    /// actual parent rather than tracked in a flag here, since HandVisualSnap
    /// also detaches and re-attaches the same transform.
    /// </summary>
    public class HandPhysicalFollow
    {
        // Collide-and-slide gives up after this many sweeps in one frame -
        // one to hit the surface, one to slide along it, one more for a
        // corner. More is rarely needed and each costs a sweep.
        private const int MaxSweeps = 3;

        // Movement shorter than this isn't worth a sweep, in metres.
        private const float MinMoveDistance = 0.0001f;

        // How far the visual must be from the controller to count as held
        // back by a surface, in metres. Anything closer is float noise.
        private const float ContactThreshold = 0.001f;

        private readonly Transform _visual;

        // The controller the visual normally lives under, and returns to.
        private readonly Transform _restParent;

        // The visual's own local pose under the controller, captured once -
        // "following the controller" means sitting exactly here.
        private readonly Vector3 _restLocalPosition;
        private readonly Quaternion _restLocalRotation;

        // The wrist and middle fingertip, in the CONTROLLER's local space,
        // measured once from the model's bones at rest. Kept in controller
        // space (not the visual's) so the right hand's mirrored -1 scale
        // needs no special handling - it's already baked into the positions.
        private readonly Vector3 _wristInController;
        private readonly Vector3 _fingertipInController;

        // Where the visual was placed last frame, in world space - the start
        // of this frame's sweep. Invalid until the first follow tick, and
        // again after snapping or losing tracking (see Suspend()).
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        /// True while a surface is holding the hand back from its controller.
        public bool IsInContact { get; private set; }

        public HandPhysicalFollow(Transform visual, Transform wristBone, Transform fingertipBone)
        {
            _visual = visual;
            _restParent = visual.parent;
            _restLocalPosition = visual.localPosition;
            _restLocalRotation = visual.localRotation;

            // Measured at construction (Awake), while the visual still sits at
            // its rest pose under the controller and the fingers are at their
            // bind pose - so finger curl animation never changes the capsule.
            _wristInController = _restParent.InverseTransformPoint(wristBone.position);
            _fingertipInController = _restParent.InverseTransformPoint(fingertipBone.position);
        }

        /// <summary>
        /// Moves the visual towards the controller, stopping and sliding at
        /// anything on collisionLayers. radius is the hand capsule's radius
        /// and skinWidth the gap kept from surfaces, both in metres - passed
        /// in every frame so Inspector changes apply live.
        /// </summary>
        public void Tick(LayerMask collisionLayers, float radius, float skinWidth)
        {
            // An untracked controller (e.g. the controller is off) has
            // nothing to follow - forget the last position so the hand
            // doesn't sweep in from wherever it was when tracking returns.
            if (!_restParent.gameObject.activeInHierarchy) {
                Suspend();
                return;
            }

            // Where the visual would be if it were simply following the
            // controller this frame - the pose the hand is trying to reach.
            Vector3 targetPosition = _restParent.TransformPoint(_restLocalPosition);
            Quaternion targetRotation = _restParent.rotation * _restLocalRotation;

            if (!_hasLastPosition) {
                _lastPosition = _visual.position;
                _hasLastPosition = true;
            }

            // The capsule's ends relative to the visual's root, at this
            // frame's rotation. The sweep only translates the capsule, so
            // these offsets hold wherever along the sweep it is.
            Vector3 wristOffset = _restParent.TransformPoint(_wristInController) - targetPosition;
            Vector3 fingertipOffset = _restParent.TransformPoint(_fingertipInController) - targetPosition;
            InsetCapsuleEnds(ref wristOffset, ref fingertipOffset, radius);

            Vector3 position = SweepAndSlide(
                _lastPosition, targetPosition,
                wristOffset, fingertipOffset,
                collisionLayers, radius, skinWidth);

            IsInContact = (position - targetPosition).sqrMagnitude > ContactThreshold * ContactThreshold;
            bool isAttached = _visual.parent == _restParent;

            if (IsInContact) {
                // Held back by a surface - detach (only on the first blocked
                // frame, so the hierarchy change isn't a per-frame cost) and
                // place the visual ourselves.
                if (isAttached) {
                    _visual.SetParent(null, true);
                }

                _visual.SetPositionAndRotation(position, targetRotation);
            } else if (!isAttached) {
                // Path clear again - back under the controller, at exactly
                // its rest pose, so it gets the before-render update again.
                _visual.SetParent(_restParent, false);
                _visual.SetLocalPositionAndRotation(_restLocalPosition, _restLocalRotation);
            }

            _lastPosition = position;
        }

        /// <summary>
        /// Stops following for now - called while something else owns the
        /// visual (e.g. it's snapped to a ledge) or it isn't tracked. The next
        /// Tick() starts fresh from wherever the visual is then.
        /// </summary>
        public void Suspend()
        {
            _hasLastPosition = false;
            IsInContact = false;
        }

        /// <summary>
        /// Gets the world-space ends of the hand capsule twice - where the
        /// controller wants it (target) and where the visual actually is - for
        /// debug gizmos. Uses the same inset as the sweep, so it's exactly the
        /// shape being swept.
        /// </summary>
        public void GetDebugCapsules(
            float radius,
            out Vector3 targetWrist,
            out Vector3 targetFingertip,
            out Vector3 visualWrist,
            out Vector3 visualFingertip)
        {
            Vector3 targetPosition = _restParent.TransformPoint(_restLocalPosition);
            Vector3 wristOffset = _restParent.TransformPoint(_wristInController) - targetPosition;
            Vector3 fingertipOffset = _restParent.TransformPoint(_fingertipInController) - targetPosition;
            InsetCapsuleEnds(ref wristOffset, ref fingertipOffset, radius);

            targetWrist = targetPosition + wristOffset;
            targetFingertip = targetPosition + fingertipOffset;
            visualWrist = _visual.position + wristOffset;
            visualFingertip = _visual.position + fingertipOffset;
        }

        /// <summary>
        /// A capsule's rounded ends stick out radius past its two points, so
        /// both points are pulled in by radius - the capsule then ends at the
        /// wrist and fingertip themselves rather than beyond them. Never past
        /// the middle, for a radius bigger than half the hand.
        /// </summary>
        private static void InsetCapsuleEnds(ref Vector3 a, ref Vector3 b, float radius)
        {
            Vector3 axis = b - a;
            float length = axis.magnitude;

            if (length < 0.0001f) {
                return;
            }

            float inset = Mathf.Min(radius, length * 0.5f);
            Vector3 step = axis / length * inset;
            a += step;
            b -= step;
        }

        /// <summary>
        /// Sweeps the hand capsule (root at from, ends at from + the offsets)
        /// towards to. Each hit stops it skinWidth short of the surface; the
        /// rest of the movement is flattened onto the surface (so pushing
        /// diagonally into a wall slides along it) and swept again, up to
        /// MaxSweeps times. Returns where the root ends up - to itself if
        /// nothing was in the way. No allocations: CapsuleCast returns one hit
        /// through an out parameter.
        /// </summary>
        private static Vector3 SweepAndSlide(
            Vector3 from,
            Vector3 to,
            Vector3 wristOffset,
            Vector3 fingertipOffset,
            LayerMask collisionLayers,
            float radius,
            float skinWidth)
        {
            Vector3 position = from;
            Vector3 remaining = to - from;

            for (int i = 0; i < MaxSweeps; i++) {
                float distance = remaining.magnitude;

                if (distance < MinMoveDistance) {
                    break;
                }

                Vector3 direction = remaining / distance;

                // Triggers are ignored: ledges, ladders and ropes are trigger
                // volumes for the hand rays, not surfaces to stop at.
                bool hit = Physics.CapsuleCast(
                    position + wristOffset,
                    position + fingertipOffset,
                    radius,
                    direction,
                    out RaycastHit hitInfo,
                    distance + skinWidth,
                    collisionLayers,
                    QueryTriggerInteraction.Ignore);

                // A distance of 0 means the capsule started inside something
                // (e.g. the hand rotated into a wall). Treating that as a
                // block would pin the hand inside it, so let it move freely -
                // pushing it back out is the depenetration step's job.
                if (!hit || hitInfo.distance <= 0f) {
                    position += remaining;
                    break;
                }

                // Stop skinWidth short of the surface, so next frame's sweep
                // doesn't start touching it.
                float travel = Mathf.Max(hitInfo.distance - skinWidth, 0f);
                position += direction * travel;

                // Whatever movement is left, minus the part pointing into the
                // surface: that's the slide along it.
                remaining = Vector3.ProjectOnPlane(remaining - direction * travel, hitInfo.normal);
            }

            return position;
        }
    }
}
