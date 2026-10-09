using Interaction;
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
    /// Rotation follows the controller while the hand is free, but is held
    /// at the rotation the hand touched with while it's in contact (decided
    /// 2026-09-27) - otherwise tilting the wrist while pressing into a wall
    /// swings the fingers into it and the push-out backs the whole hand away.
    /// Depenetrate() still pushes the hand out of anything it starts inside,
    /// before the sweep.
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

        // Push-out passes per frame. Pushing out of one surface can push the
        // hand into another (a corner), so the overlap check is repeated -
        // but a hand squeezed somewhere it can't fit gives up here rather
        // than looping forever.
        private const int MaxDepenetrationPasses = 3;

        // Colliders the hand can overlap at once. Shared by both hands (they're
        // ticked one after the other, never at the same time) and allocated
        // once, so the overlap check never allocates. Anything past this many
        // is caught by the next pass.
        private static readonly Collider[] _overlapBuffer = new Collider[8];

        // Where each hand's penetration collider sits, well below the level,
        // for its whole life - see CreatePenetrationCollider().
        private static readonly Vector3 _parkedColliderPosition = new(0f, -1000f, 0f);

        private const string PlayerHandsLayerName = "PlayerHands";

        private readonly Transform _visual;

        // A real collider shaped like the hand capsule, only ever used as the
        // "this is the hand" half of Physics.ComputePenetration (the push-out
        // step). Never moved: each query passes in where the hand actually is.
        private readonly CapsuleCollider _penetrationCollider;

        // The controller the visual normally lives under, and returns to.
        private readonly Transform _restParent;

        // The visual's own local pose under the controller, captured once -
        // "following the controller" means sitting exactly here.
        private readonly Vector3 _restLocalPosition;
        private readonly Quaternion _restLocalRotation;

        // The wrist and middle fingertip relative to the visual's rest
        // position, in the CONTROLLER's local space, measured once from the
        // model's bones at rest. Kept in controller space (not the visual's)
        // so the right hand's mirrored -1 scale needs no special handling -
        // it's already baked into the positions. Stored relative to the
        // visual's root so a single rotation turns each into its world-space
        // offset - see GetCapsuleOffsets().
        private readonly Vector3 _wristFromRoot;
        private readonly Vector3 _fingertipFromRoot;

        // Turns the penetration collider (a capsule along its own Z) to lie
        // along the wrist-to-fingertip line, relative to the controller.
        // Measured once, like the offsets above.
        private readonly Quaternion _handCapsuleRotation;
        private readonly float _handLength;

        // The capsule actually being swept: normally the hand's (the three
        // values above), but while the hand carries a prop it runs from the
        // wrist to the middle of the prop, with the prop's radius - see
        // SetHeldShape(). Same space as _wristFromRoot.
        private Vector3 _capsuleStart;
        private Vector3 _capsuleEnd;
        private Quaternion _capsuleLocalRotation;
        private bool _hasHeldShape;
        private float _heldRadius;

        // Where the visual was placed last frame, in world space - the start
        // of this frame's sweep. Invalid until the first follow tick, and
        // again after snapping or losing tracking (see Suspend()).
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        // The rotation the hand was given last frame, as a controller
        // rotation (the visual's own is this times _restLocalRotation). While
        // the rotation is held it stays here, unchanged, from the first
        // blocked frame - see Tick().
        private Quaternion _handRotation;

        // Whether a surface stopped the hand short of its goal position last
        // frame, and whether its rotation is held at _handRotation. They end
        // separately: the position is free as soon as the hand can reach its
        // goal, the rotation only once the hand also fits turned to the
        // controller's rotation - see Tick().
        private bool _isPositionHeld;
        private bool _isRotationHeld;

        // The "elastic band": once free, the hand eases back to the controller
        // over catchUpDuration instead of jumping there - position and
        // rotation each from the moment they came free. The offset from the
        // controller at that moment is kept in the controller's own space and
        // shrunk to nothing, so the hand keeps following the real hand's
        // movement while it eases back rather than trailing behind it.
        private bool _isReturning;
        private float _returnTime;
        private Vector3 _returnOffset;
        private bool _isRotationReturning;
        private float _rotationReturnTime;
        private Quaternion _returnRotationOffset;

        // Set when the hand snaps back because the controller got too far
        // from it (see Tick()): collision is off while it eases back onto the
        // controller and until the controller is clear of geometry, rather
        // than being pushed out and snapping back again every frame.
        private bool _isPassingThrough;

        /// Debug only, set by PhysicalHandsTrace: logs every sweep of every
        /// frame a hand touches something (what it hit, distances, normals),
        /// without a stack trace, for diagnosing collision bugs from the log.
        /// Allocates strings every frame while on - never leave it on.
        public static bool TraceEnabled { get; set; }

        /// Debug only: while tracing, only frames that hit a collider whose
        /// name contains this are logged. Empty logs every frame with a hit.
        public static string TraceColliderFilter { get; set; } = "";

        // The line being built this frame while tracing, or null when off -
        // SweepAndSlide() is static, so it appends here too.
        private static System.Text.StringBuilder _trace;

        /// True while a surface is holding the hand back from its controller -
        /// stopping its position, or holding its rotation.
        public bool IsInContact { get; private set; }

        /// True on a frame the hand moved something out of its way by
        /// pressing on it (an open door). Such a hand isn't "in contact":
        /// it stays on its controller.
        public bool IsPushing { get; private set; }

        // Set by SweepAndSlide() (which is static) when its push moved
        // something; copied to IsPushing straight after the sweep.
        private static bool _pushedThisSweep;

        /// True while the visual is placed by this class rather than simply
        /// following its controller: held by a surface, or easing back.
        private bool IsPlacedByCode => _isPositionHeld || _isRotationHeld || _isReturning || _isRotationReturning;

        public HandPhysicalFollow(Transform visual, Transform wristBone, Transform fingertipBone)
        {
            _visual = visual;
            _restParent = visual.parent;
            _restLocalPosition = visual.localPosition;
            _restLocalRotation = visual.localRotation;

            // Measured at construction (Awake), while the visual still sits at
            // its rest pose under the controller and the fingers are at their
            // bind pose - so finger curl animation never changes the capsule.
            _wristFromRoot = _restParent.InverseTransformPoint(wristBone.position) - _restLocalPosition;
            _fingertipFromRoot = _restParent.InverseTransformPoint(fingertipBone.position) - _restLocalPosition;

            // A Unity capsule's height is measured end to end, round caps
            // included - so it's simply the wrist-to-fingertip length, and it
            // matches the swept capsule (whose ends are inset by the radius).
            Vector3 handAxis = _fingertipFromRoot - _wristFromRoot;
            _handCapsuleRotation = Quaternion.LookRotation(handAxis);
            _handLength = handAxis.magnitude;
            _penetrationCollider = CreatePenetrationCollider(visual.name, _handLength);

            _capsuleStart = _wristFromRoot;
            _capsuleEnd = _fingertipFromRoot;
            _capsuleLocalRotation = _handCapsuleRotation;
        }

        /// <summary>
        /// Makes the swept shape cover a prop the hand is carrying: a
        /// capsule from the wrist to the middle of the prop, radius thick,
        /// in place of the hand's own. One shape for hand and prop together
        /// is rough - it's round, the prop may not be - but it means the
        /// whole collide-and-slide below works unchanged, and the prop
        /// stops at a wall with the hand instead of going into it.
        /// centreFromVisual is the prop's middle relative to the visual's
        /// root, in the visual's own axes.
        ///
        /// Only called on pick-up, so resizing the penetration collider
        /// (which makes PhysX rebuild its shape) isn't a per-frame cost.
        /// </summary>
        public void SetHeldShape(Vector3 centreFromVisual, float radius)
        {
            // Into the controller's space, like the wrist and fingertip:
            // the visual sits at _restLocalRotation from the controller.
            _capsuleStart = _wristFromRoot;
            _capsuleEnd = _restLocalRotation * centreFromVisual;

            Vector3 axis = _capsuleEnd - _capsuleStart;

            // A prop centred on the wrist leaves no line to lie along -
            // nudge the end so the capsule is (as near as matters) a ball.
            if (axis.sqrMagnitude < 0.0001f) {
                axis = Vector3.forward * 0.01f;
                _capsuleEnd = _capsuleStart + axis;
            }

            _capsuleLocalRotation = Quaternion.LookRotation(axis);
            _heldRadius = radius;
            _hasHeldShape = true;

            // Unlike the hand's capsule, the ends aren't pulled in by the
            // radius (the prop sticks out that far past its middle), so the
            // collider's end-to-end height is the line plus a cap each end.
            _penetrationCollider.radius = radius;
            _penetrationCollider.height = axis.magnitude + radius * 2f;
        }

        /// <summary>
        /// Back to the hand's own shape, when the prop is dropped. The
        /// collider's radius is put back by the next Tick().
        /// </summary>
        public void ClearHeldShape()
        {
            _hasHeldShape = false;
            _capsuleStart = _wristFromRoot;
            _capsuleEnd = _fingertipFromRoot;
            _capsuleLocalRotation = _handCapsuleRotation;

            // Already destroyed if the scene is unloading.
            if (_penetrationCollider != null) {
                _penetrationCollider.height = _handLength;
            }
        }

        /// <summary>
        /// The radius of the shape being swept: the hand's (passed in, as
        /// it's tuned on PlayerHandVisuals), or the carried prop's.
        /// </summary>
        public float ShapeRadius(float handRadius)
        {
            return _hasHeldShape ? _heldRadius : handRadius;
        }

        /// <summary>
        /// Moves the visual towards the controller, stopping and sliding at
        /// anything on collisionLayers, and easing back over catchUpDuration
        /// seconds once free - or easing back through the surface if it holds
        /// the hand more than maxSeparation from the controller. radius is the hand
        /// capsule's radius, skinWidth the gap kept from surfaces and
        /// maxSeparation the snap-back distance, all in metres - all passed in
        /// every frame so Inspector changes apply live.
        /// </summary>
        public void Tick(
            LayerMask collisionLayers,
            float radius,
            float skinWidth,
            float catchUpDuration,
            float maxSeparation,
            float deltaTime)
        {
            // An untracked controller (e.g. the controller is off) has
            // nothing to follow - forget the last position so the hand
            // doesn't sweep in from wherever it was when tracking returns.
            if (!_restParent.gameObject.activeInHierarchy) {
                Suspend();
                return;
            }

            // Carrying a prop: everything below uses its radius instead.
            radius = ShapeRadius(radius);

            // Only written when handRadius changes in the Inspector (or a
            // prop was just dropped) - resizing a collider makes PhysX
            // rebuild its shape, so not every frame.
            if (!Mathf.Approximately(_penetrationCollider.radius, radius)) {
                _penetrationCollider.radius = radius;
            }

            // Where the visual would be if it were simply following the
            // controller this frame - the position the hand is trying to
            // reach. One call fetches both, and the controller is unscaled, so
            // rotating the rest offset and adding it is all TransformPoint()
            // would do.
            _restParent.GetPositionAndRotation(out Vector3 controllerPosition, out Quaternion controllerRotation);
            Vector3 targetPosition = controllerPosition + controllerRotation * _restLocalPosition;

            // After a snap-back: collision is off. The hand eases back onto the
            // controller straight through whatever is in the way, then stays on
            // it (attached) while the controller is still inside something. Once
            // both are done, normal following resumes next frame.
            if (_isPassingThrough) {
                TickPassingThrough(targetPosition, controllerRotation, collisionLayers, radius, catchUpDuration, deltaTime);
                return;
            }

            // Snap-back: a surface is holding the hand further from the
            // controller than maxSeparation - the player has reached well into
            // or through something. A hand left stuck that far from the real
            // one feels broken, so it gives up on the surface and eases back to
            // the controller through it. Only checked in contact: while easing
            // back, the offset only shrinks. 0 = never snap back.
            if (IsInContact && maxSeparation > 0f
                && (_lastPosition - targetPosition).sqrMagnitude > maxSeparation * maxSeparation) {
                SnapBack(targetPosition, controllerRotation);
                return;
            }

            AdvanceEases(targetPosition, controllerRotation, catchUpDuration, deltaTime, out Vector3 goalPosition, out Quaternion handRotation);

            GetCapsuleOffsets(handRotation, radius, out Vector3 wristOffset, out Vector3 fingertipOffset);

            if (!_hasLastPosition) {
                _lastPosition = _visual.position;
                _hasLastPosition = true;
            }

            // Order matters. The hand first turns to this frame's rotation
            // where it was last frame - which can swing the fingers a few mm
            // into a surface - so it's pushed back out there, while that
            // overlap is still shallow and the nearest way out is the side it
            // came from. Only then is the move swept, from a start that's
            // clear of everything, so the sweep can see the surface. Pushing
            // out after the sweep instead could find the hand past the middle
            // of a thin wall, and push it out the far side.
            Vector3 start = Depenetrate(
                _lastPosition, wristOffset, fingertipOffset, handRotation * _capsuleLocalRotation,
                collisionLayers, radius, skinWidth, out bool startsClear);

            if (TraceEnabled) {
                _trace ??= new System.Text.StringBuilder(512);
                _trace.Clear();
                _trace.Append($"[HandTrace] {_visual.name} f{Time.frameCount}"
                    + $" held P{(_isPositionHeld ? 1 : 0)}R{(_isRotationHeld ? 1 : 0)}"
                    + $" ret P{(_isReturning ? 1 : 0)}R{(_isRotationReturning ? 1 : 0)}"
                    + $" clear={(startsClear ? 1 : 0)} depen={start - _lastPosition:F4}"
                    + $" toGoal={goalPosition - start:F4}");
            } else {
                _trace = null;
            }

            _pushedThisSweep = false;

            Vector3 position = SweepAndSlide(
                start, goalPosition,
                wristOffset, fingertipOffset,
                collisionLayers, radius, skinWidth, startsClear);

            IsPushing = _pushedThisSweep;

            // Only frames where something was hit - and, with a filter, only
            // hits on matching colliders - so the log stays readable.
            if (_trace != null && _trace.ToString().Contains("hit '") && _trace.ToString().Contains(TraceColliderFilter)) {
                _trace.Append($" => moved={position - _lastPosition:F4}");
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", _trace.ToString());
            }

            bool isPositionBlocked = (position - goalPosition).sqrMagnitude > ContactThreshold * ContactThreshold;

            if (isPositionBlocked) {
                // Blocked (again) - a hand that was easing back is held once
                // more, and holds the rotation it has now.
                _isReturning = false;
                _isRotationHeld = true;
                _isRotationReturning = false;
            } else {
                if (_isPositionHeld) {
                    // Position just came free. Rather than jumping to the
                    // controller, start easing back from where the hand is now:
                    // it stays put this frame, and its offset from the
                    // controller (in the controller's space) shrinks to nothing
                    // over the next catchUpDuration seconds. This happens even
                    // if the rotation is still held - before position and
                    // rotation were separate, a hand free to move but not to
                    // turn stayed "in contact", and contact movement isn't
                    // eased, so it jumped straight onto the controller.
                    StartPositionReturn(start, targetPosition, controllerRotation);
                    position = start;
                } else if (_isReturning && _returnTime >= catchUpDuration) {
                    // Eased all the way back - on the controller's position.
                    _isReturning = false;
                }

                if (_isRotationHeld) {
                    // Only let the rotation go once the hand also fits, where it
                    // is now, turned to the controller's rotation. Otherwise it
                    // would turn with its fingers going into the wall, be pushed
                    // out, and flicker between the two. Costs one extra query,
                    // and only while the rotation is held.
                    if (!OverlapsAt(position, controllerRotation, collisionLayers, radius)) {
                        StartRotationReturn(controllerRotation);
                        _isRotationHeld = false;
                    }
                } else if (_isRotationReturning && _rotationReturnTime >= catchUpDuration) {
                    // Eased all the way back - at the controller's rotation.
                    _isRotationReturning = false;
                }
            }

            _isPositionHeld = isPositionBlocked;
            IsInContact = _isPositionHeld || _isRotationHeld;

            PlaceVisual(position, handRotation);
        }

        /// <summary>
        /// Works out this frame's goal position and rotation, moving any
        /// elastic band eases on by deltaTime. Position and rotation each have
        /// their own state, because a hand can be free to move before it's
        /// free to turn (see Tick()):
        /// - Position: while held by a surface, the goal is simply the
        ///   controller and the sweep stops the hand at the surface. While
        ///   easing back, part of the way from where it came free to the
        ///   controller. Otherwise, the controller.
        /// - Rotation: while held, the rotation the hand touched with, instead
        ///   of following the controller. Pushing into a wall usually tilts
        ///   the wrist (the arm pivots at the shoulder), and a tilted hand
        ///   swings its fingers into the wall - the push-out would then back
        ///   the whole hand away from it. Holding the rotation keeps the hand
        ///   still against the surface; it still slides along it. While easing
        ///   back, part of the way from the held rotation to the controller's.
        ///   Otherwise, the controller's.
        /// </summary>
        private void AdvanceEases(
            Vector3 targetPosition,
            Quaternion controllerRotation,
            float catchUpDuration,
            float deltaTime,
            out Vector3 goalPosition,
            out Quaternion handRotation)
        {
            goalPosition = targetPosition;

            if (_isReturning) {
                _returnTime += deltaTime;
                float weight = CatchUpWeight(_returnTime, catchUpDuration);
                goalPosition = targetPosition + controllerRotation * (_returnOffset * (1f - weight));
            }

            handRotation = controllerRotation;

            if (_isRotationHeld) {
                handRotation = _handRotation;
            } else if (_isRotationReturning) {
                _rotationReturnTime += deltaTime;
                float weight = CatchUpWeight(_rotationReturnTime, catchUpDuration);
                handRotation = controllerRotation * Quaternion.Slerp(_returnRotationOffset, Quaternion.identity, weight);
            }
        }

        /// <summary>
        /// Starts easing the position back to the controller from position:
        /// its offset from the controller is stored in the controller's space
        /// and shrinks to nothing over catchUpDuration (see AdvanceEases()).
        /// </summary>
        private void StartPositionReturn(Vector3 position, Vector3 targetPosition, Quaternion controllerRotation)
        {
            _returnOffset = Quaternion.Inverse(controllerRotation) * (position - targetPosition);
            _returnTime = 0f;
            _isReturning = true;
        }

        /// <summary>
        /// Starts easing the rotation back to the controller's from the one
        /// the hand has now (_handRotation), the same way as the position.
        /// </summary>
        private void StartRotationReturn(Quaternion controllerRotation)
        {
            _returnRotationOffset = Quaternion.Inverse(controllerRotation) * _handRotation;
            _rotationReturnTime = 0f;
            _isRotationReturning = true;
        }

        /// <summary>
        /// One frame after a snap-back, with collision off: the hand eases back
        /// onto the controller straight through whatever is in the way, then
        /// waits there (attached) until the controller is clear of geometry.
        /// Without the wait, the next frame would push the hand out to the
        /// nearest face of whatever the controller is inside; if that's the
        /// near side, it would be too far away again, snap back again, and
        /// flicker every frame. Costs one overlap query per frame, and only
        /// once the ease has finished.
        /// </summary>
        private void TickPassingThrough(
            Vector3 targetPosition,
            Quaternion controllerRotation,
            LayerMask collisionLayers,
            float radius,
            float catchUpDuration,
            float deltaTime)
        {
            AdvanceEases(targetPosition, controllerRotation, catchUpDuration, deltaTime, out Vector3 position, out Quaternion handRotation);

            if (_isReturning && _returnTime >= catchUpDuration) {
                _isReturning = false;
            }

            if (_isRotationReturning && _rotationReturnTime >= catchUpDuration) {
                _isRotationReturning = false;
            }

            PlaceVisual(position, handRotation);

            if (_isReturning || _isRotationReturning || OverlapsAt(targetPosition, controllerRotation, collisionLayers, radius)) {
                return;
            }

            // Back on the controller, and it's clear - normal following
            // resumes next frame, from exactly where the controller is.
            _isPassingThrough = false;
            _lastPosition = targetPosition;
            _hasLastPosition = true;
        }

        /// <summary>
        /// Puts the visual where this frame's tick decided, and remembers it
        /// for next frame. While placed by code (held or easing back) the
        /// visual is detached - only on the first such frame, so the hierarchy
        /// change isn't a per-frame cost - and positioned directly. Otherwise
        /// it goes back under the controller at exactly its rest pose, so it
        /// gets the Tracked Pose Driver's before-render update again.
        /// </summary>
        private void PlaceVisual(Vector3 position, Quaternion handRotation)
        {
            bool isAttached = _visual.parent == _restParent;

            if (IsPlacedByCode) {
                if (isAttached) {
                    _visual.SetParent(null, true);
                }

                _visual.SetPositionAndRotation(position, handRotation * _restLocalRotation);
            } else if (!isAttached) {
                _visual.SetParent(_restParent, false);
                _visual.SetLocalPositionAndRotation(_restLocalPosition, _restLocalRotation);
            }

            _lastPosition = position;
            _handRotation = handRotation;
        }

        /// <summary>
        /// The pose the controller would need for the visual to sit where it
        /// is now - the controller, shifted to wherever a surface is holding
        /// the hand. Hand rays are cast from this, so they start from the hand
        /// the player sees (and can't reach through a wall the controller has
        /// gone into), while the ray angle offsets tuned against the
        /// controller still apply unchanged. While the visual is on its
        /// controller (free, snapped, passing through) it's just the
        /// controller. Reflects last frame's placement - this is read before
        /// the hand visuals are ticked.
        /// </summary>
        public void GetHandPose(out Vector3 position, out Quaternion rotation)
        {
            if (!IsPlacedByCode) {
                _restParent.GetPositionAndRotation(out position, out rotation);
                return;
            }

            // _lastPosition is where the visual's root was placed, and it sits
            // at _restLocalPosition from the controller - so step back by that.
            rotation = _handRotation;
            position = _lastPosition - _handRotation * _restLocalPosition;
        }

        /// <summary>
        /// Stops following for now - called while something else owns the
        /// visual (e.g. it's snapped to a ledge) or it isn't tracked. The next
        /// Tick() starts fresh from wherever the visual is then.
        /// </summary>
        public void Suspend()
        {
            _hasLastPosition = false;
            _isPassingThrough = false;
            ClearContact();
        }

        /// <summary>
        /// Forgets any contact and easing back, so the hand simply follows
        /// its controller again.
        /// </summary>
        private void ClearContact()
        {
            IsInContact = false;
            IsPushing = false;
            _isPositionHeld = false;
            _isRotationHeld = false;
            _isReturning = false;
            _isRotationReturning = false;
        }

        /// <summary>
        /// How far an elastic band ease has got after time seconds, from 0
        /// (just came free) to 1 (fully back on the controller). SmoothStep
        /// eases in and out, like HandVisualSnap, and clamps, so it stops at 1.
        /// A duration of 0 means no easing at all.
        /// </summary>
        private static float CatchUpWeight(float time, float duration)
        {
            return duration > 0f ? Mathf.SmoothStep(0f, 1f, time / duration) : 1f;
        }

        /// <summary>
        /// Gives up on the surface holding the hand and switches collision off:
        /// from next frame the hand eases back onto its controller - position
        /// and rotation, over catchUpDuration, like any other release - through
        /// whatever is in the way (see TickPassingThrough()). It stays where it
        /// is this frame. Replaced an instant jump (2026-09-28), so every
        /// return to the controller is eased.
        /// </summary>
        private void SnapBack(Vector3 targetPosition, Quaternion controllerRotation)
        {
            ClearContact();
            StartPositionReturn(_lastPosition, targetPosition, controllerRotation);
            StartRotationReturn(controllerRotation);
            _isPassingThrough = true;
        }

        /// <summary>
        /// True if the hand capsule, with its root at position and turned to a
        /// controller rotation, is inside anything on collisionLayers.
        /// </summary>
        private bool OverlapsAt(Vector3 position, Quaternion rotation, LayerMask collisionLayers, float radius)
        {
            GetCapsuleOffsets(rotation, radius, out Vector3 wristOffset, out Vector3 fingertipOffset);

            return Physics.CheckCapsule(
                position + wristOffset,
                position + fingertipOffset,
                radius,
                collisionLayers,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Destroys the penetration collider. It lives at the scene root rather
        /// than under the player, so it isn't cleaned up with its owner -
        /// PlayerHandVisuals calls this from OnDestroy().
        /// </summary>
        public void Destroy()
        {
            // Unity's == null is also true for an already-destroyed object,
            // e.g. when the whole scene is unloading.
            if (_penetrationCollider != null) {
                Object.Destroy(_penetrationCollider.gameObject);
            }
        }

        /// <summary>
        /// Builds the collider Physics.ComputePenetration needs for the hand:
        /// a capsule along its local Z (direction 2), on its own GameObject.
        ///
        /// ComputePenetration only works on enabled colliders, but it takes the
        /// hand's position and rotation as arguments rather than reading the
        /// transform. So the collider is parked far below the level and never
        /// moved (moving a collider isn't free in PhysX), and it's a trigger
        /// on the PlayerHands layer so it can't push, block or be hit by
        /// anything: hand rays and the hand sweep don't include that layer,
        /// and Player-PlayerHands is off in the collision matrix.
        ///
        /// At the scene root, not under the player: a child would be dragged
        /// around by the rig every frame. Unscaled for the same reason the
        /// capsule is measured in controller space - the right hand visual's
        /// mirrored -1 scale never reaches it.
        /// </summary>
        private static CapsuleCollider CreatePenetrationCollider(string handName, float length)
        {
            GameObject colliderObject = new($"{handName} Penetration Collider");
            int layer = LayerMask.NameToLayer(PlayerHandsLayerName);

            if (layer >= 0) {
                colliderObject.layer = layer;
            } else {
                Debug.LogWarning($"HandPhysicalFollow: no '{PlayerHandsLayerName}' layer - {colliderObject.name} left on Default.");
            }

            colliderObject.transform.position = _parkedColliderPosition;

            CapsuleCollider capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 2;
            capsule.height = length;

            return capsule;
        }

        /// <summary>
        /// Gets the world-space ends of the hand capsule twice - where the
        /// controller wants it (target) and where the visual actually is - for
        /// debug gizmos. Uses the same inset as the sweep, so it's exactly the
        /// shape being swept; the visual's uses the rotation it was given
        /// (held or easing back) while it's detached, as the sweep does.
        /// </summary>
        public void GetDebugCapsules(
            float radius,
            out Vector3 targetWrist,
            out Vector3 targetFingertip,
            out Vector3 visualWrist,
            out Vector3 visualFingertip)
        {
            _restParent.GetPositionAndRotation(out Vector3 controllerPosition, out Quaternion controllerRotation);
            Vector3 targetPosition = controllerPosition + controllerRotation * _restLocalPosition;

            GetCapsuleOffsets(controllerRotation, radius, out Vector3 wristOffset, out Vector3 fingertipOffset);
            targetWrist = targetPosition + wristOffset;
            targetFingertip = targetPosition + fingertipOffset;

            Quaternion handRotation = IsPlacedByCode ? _handRotation : controllerRotation;
            GetCapsuleOffsets(handRotation, radius, out wristOffset, out fingertipOffset);
            visualWrist = _visual.position + wristOffset;
            visualFingertip = _visual.position + fingertipOffset;
        }

        /// <summary>
        /// The hand capsule's two ends relative to the visual's root, in world
        /// space, for a hand whose controller is turned to rotation - already
        /// inset by radius. The sweep only translates the capsule, so these
        /// offsets hold wherever along the sweep it is. A rotation alone is
        /// enough (no matrix) because the controllers are unscaled - the
        /// penetration collider relies on that too.
        /// </summary>
        private void GetCapsuleOffsets(Quaternion rotation, float radius, out Vector3 wristOffset, out Vector3 fingertipOffset)
        {
            wristOffset = rotation * _capsuleStart;
            fingertipOffset = rotation * _capsuleEnd;

            // A carried prop's shape isn't pulled in: it reaches its radius
            // past the prop's middle, as the prop does.
            if (!_hasHeldShape) {
                InsetCapsuleEnds(ref wristOffset, ref fingertipOffset, radius);
            }
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
        /// Pushes the hand capsule (root at position, ends at position + the
        /// offsets) out of anything on collisionLayers it overlaps, and returns
        /// where the root ends up - position itself if it's already clear.
        ///
        /// OverlapCapsuleNonAlloc lists what the capsule is inside (into the
        /// shared buffer, no allocation). For each, ComputePenetration gives
        /// the shortest way out: a direction and a distance. The hand is moved
        /// that far plus skinWidth, so it ends a little clear of the surface
        /// rather than exactly touching it. capsuleRotation turns the
        /// penetration collider to lie along the hand. Nearly every frame the
        /// overlap finds nothing and this costs one cheap query.
        ///
        /// isClear says whether it got the hand out of everything. False
        /// means it ran out of passes still moving - e.g. wedged between two
        /// surfaces closer together than the hand, where each push-out goes
        /// into the other one.
        /// </summary>
        private Vector3 Depenetrate(
            Vector3 position,
            Vector3 wristOffset,
            Vector3 fingertipOffset,
            Quaternion capsuleRotation,
            LayerMask collisionLayers,
            float radius,
            float skinWidth,
            out bool isClear)
        {
            isClear = false;

            for (int pass = 0; pass < MaxDepenetrationPasses; pass++) {
                int count = Physics.OverlapCapsuleNonAlloc(
                    position + wristOffset,
                    position + fingertipOffset,
                    radius,
                    _overlapBuffer,
                    collisionLayers,
                    QueryTriggerInteraction.Ignore);

                if (count == 0) {
                    isClear = true;
                    break;
                }

                // The penetration collider is centred on the middle of the
                // capsule. Insetting both ends by the same amount doesn't move
                // the middle, so it's simply halfway between them.
                Vector3 centre = position + (wristOffset + fingertipOffset) * 0.5f;
                bool moved = false;

                for (int i = 0; i < count; i++) {
                    Collider other = _overlapBuffer[i];

                    // Only possible if collisionLayers wrongly includes
                    // PlayerHands - the hand can't push out of itself.
                    if (other == _penetrationCollider) {
                        continue;
                    }

                    other.transform.GetPositionAndRotation(out Vector3 otherPosition, out Quaternion otherRotation);

                    // False if they only touch - nothing to push out of.
                    if (!Physics.ComputePenetration(
                            _penetrationCollider, centre, capsuleRotation,
                            other, otherPosition, otherRotation,
                            out Vector3 direction, out float distance)) {
                        continue;
                    }

                    // Moved immediately rather than summed, so the next
                    // collider is checked from where the hand now is.
                    Vector3 push = direction * (distance + skinWidth);
                    position += push;
                    centre += push;
                    moved = true;
                }

                // Overlapping only by touching - as clear as it gets.
                if (!moved) {
                    isClear = true;
                    break;
                }
            }

            return position;
        }

        /// <summary>
        /// Sweeps the hand capsule (root at from, ends at from + the offsets)
        /// towards to. Each hit stops it skinWidth short of the surface; the
        /// rest of the movement is flattened onto the surface (so pushing
        /// diagonally into a wall slides along it) and swept again, up to
        /// MaxSweeps times. Returns where the root ends up - to itself if
        /// nothing was in the way. No allocations: CapsuleCast returns one hit
        /// through an out parameter. startsClear is Depenetrate()'s isClear
        /// for from.
        /// </summary>
        private static Vector3 SweepAndSlide(
            Vector3 from,
            Vector3 to,
            Vector3 wristOffset,
            Vector3 fingertipOffset,
            LayerMask collisionLayers,
            float radius,
            float skinWidth,
            bool startsClear)
        {
            Vector3 position = from;
            Vector3 remaining = to - from;

            // The surface the previous sweep this frame slid along, for
            // creases - see below.
            Vector3 previousNormal = Vector3.zero;

            // True once this frame's sweep has pushed something - see below.
            bool hasPushed = false;

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

                if (!hit) {
                    _trace?.Append($" | [{i}] clear rem={distance:F4}");
                    position += remaining;
                    break;
                }

                _trace?.Append($" | [{i}] hit '{hitInfo.collider.name}' d={hitInfo.distance:F4} n={hitInfo.normal:F2} dir={direction:F2} rem={distance:F4}");

                // A distance of 0 means the sweep started touching or inside
                // something. Only on the first sweep, when Depenetrate()
                // couldn't get the hand clear, is the hand genuinely stuck
                // inside - treating that as a block would pin it there, so it
                // moves freely instead. Anywhere else it's just touching: a
                // slide sweep starts right up against the surface the last
                // sweep stopped at, or at the one beside it in a crease - so it
                // stops. Letting those through too (before 2026-09-28) moved
                // the hand the whole remaining slide into the block beside it,
                // then the next frame's push-out shoved it back out sideways:
                // at the test area's 5cm gap it jittered left and right, more
                // the deeper the controller went.
                if (hitInfo.distance <= 0f) {
                    if (i == 0 && !startsClear) {
                        _trace?.Append(" LET-THROUGH");
                        position += remaining;
                    }

                    break;
                }

                // Stop skinWidth short of the surface, so next frame's sweep
                // doesn't start touching it.
                float travel = Mathf.Max(hitInfo.distance - skinWidth, 0f);
                position += direction * travel;

                // The slide: the way to the goal from here, minus the part
                // pointing into the surface. Always re-aimed at the goal rather
                // than carrying on with whatever was left of the last move -
                // moving along this can never take the hand further from the
                // goal. Carrying the leftover (before 2026-09-28) could: at the
                // test area's 5cm gap it slid the hand round a block's edge and
                // out, as far to the side as the controller was deep in the gap,
                // then straight back next frame - a jitter that grew with depth
                // (found with PhysicalHandsTrace).
                Vector3 toGoal = to - position;

                // Something a hand can push (an open door): tell it how far
                // the hand was still trying to go straight into it. The dot
                // product is how much of the way to the goal points along the
                // surface's normal; negative means into the surface. One
                // dictionary lookup, only on a frame something was hit.
                //
                // If it moved out of the way, this hit doesn't count: the
                // sweep is tried again from here, straight at the goal, and
                // normally gets there - so a hand pushing a door stays on its
                // controller and is never "in contact". Stopping at the
                // surface instead (how it was first built) flipped the hand
                // between held and easing back every frame or two: a stutter.
                // Once per frame; i-- gives the try back, so the retry doesn't
                // use up one of the slides.
                if (!hasPushed) {
                    IHandPushable pushable = HandPushRegistry.Find(hitInfo.collider);
                    float into = -Vector3.Dot(toGoal, hitInfo.normal);

                    if (pushable is not null && into > 0f) {
                        hasPushed = true;

                        // A skinWidth further than the hand needs, so the
                        // retry isn't left grazing a surface that has turned
                        // (a door's far end moves less than where it was hit).
                        bool moved = pushable.Push(hitInfo.point, hitInfo.normal * -(into + skinWidth));
                        _trace?.Append($" PUSH {into:F4} moved={(moved ? 1 : 0)}");

                        if (moved) {
                            _pushedThisSweep = true;
                            remaining = toGoal;
                            i--;
                            continue;
                        }
                    }
                }

                remaining = Vector3.ProjectOnPlane(toGoal, hitInfo.normal);

                // A crease: two surfaces meeting in a V, like the mouth of a
                // gap narrower than the hand. Sliding along this surface would
                // push back into the one slid along before, and that one's
                // slide into this one, so the hand would bounce between them.
                // Instead, only slide along the line where the two surfaces
                // meet: at the gap that's straight up and down, so pushing
                // straight in holds still.
                if (previousNormal != Vector3.zero && Vector3.Dot(remaining, previousNormal) < 0f) {
                    Vector3 crease = Vector3.Cross(previousNormal, hitInfo.normal);

                    // Parallel surfaces have no crease line - nowhere left to go.
                    if (crease.sqrMagnitude < 0.000001f) {
                        _trace?.Append(" CREASE-PARALLEL");
                        break;
                    }

                    remaining = Vector3.Project(toGoal, crease);
                    _trace?.Append(" CREASE");
                }

                previousNormal = hitInfo.normal;
            }

            return position;
        }
    }
}
