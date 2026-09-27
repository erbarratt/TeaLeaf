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
        private readonly Quaternion _capsuleLocalRotation;

        // Where the visual was placed last frame, in world space - the start
        // of this frame's sweep. Invalid until the first follow tick, and
        // again after snapping or losing tracking (see Suspend()).
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        // The rotation the hand was given last frame, as a controller
        // rotation (the visual's own is this times _restLocalRotation). While
        // in contact it's held here, unchanged, from the first blocked frame -
        // see Tick().
        private Quaternion _handRotation;

        // The "elastic band": after contact ends the hand eases back to the
        // controller over catchUpDuration instead of jumping there. Its offset
        // from the controller at that moment is kept in the controller's own
        // space and shrunk to nothing, so the hand keeps following the real
        // hand's movement while it eases back rather than trailing behind it.
        private bool _isReturning;
        private float _returnTime;
        private Vector3 _returnOffset;
        private Quaternion _returnRotationOffset;

        // Set when the hand snaps back because the controller got too far
        // from it (see Tick()): until the controller is clear of geometry the
        // hand simply stays on it with collision off, rather than being pushed
        // out and snapping back again every frame.
        private bool _isPassingThrough;

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
            _wristFromRoot = _restParent.InverseTransformPoint(wristBone.position) - _restLocalPosition;
            _fingertipFromRoot = _restParent.InverseTransformPoint(fingertipBone.position) - _restLocalPosition;

            // A Unity capsule's height is measured end to end, round caps
            // included - so it's simply the wrist-to-fingertip length, and it
            // matches the swept capsule (whose ends are inset by the radius).
            Vector3 handAxis = _fingertipFromRoot - _wristFromRoot;
            _capsuleLocalRotation = Quaternion.LookRotation(handAxis);
            _penetrationCollider = CreatePenetrationCollider(visual.name, handAxis.magnitude);
        }

        /// <summary>
        /// Moves the visual towards the controller, stopping and sliding at
        /// anything on collisionLayers, and easing back over catchUpDuration
        /// seconds once free - or snapping straight back if a surface holds it
        /// more than maxSeparation from the controller. radius is the hand
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

            // Only written when handRadius changes in the Inspector - resizing
            // a collider makes PhysX rebuild its shape, so not every frame.
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

            // After a snap-back: stay on the controller, collision off, while
            // it's still inside something. The visual is attached, so there's
            // nothing to do but wait. Once it's clear, carry on as normal from
            // exactly where the controller is.
            if (_isPassingThrough) {
                if (OverlapsAt(targetPosition, controllerRotation, collisionLayers, radius)) {
                    return;
                }

                _isPassingThrough = false;
                _lastPosition = targetPosition;
                _hasLastPosition = true;
            }

            // Snap-back: a surface is holding the hand further from the
            // controller than maxSeparation - the player has reached well into
            // or through something. A hand left stuck that far from the real
            // one feels broken, so it jumps straight back to the controller.
            // Only checked in contact: while easing back, the offset only
            // shrinks. 0 = never snap back.
            if (IsInContact && maxSeparation > 0f
                && (_lastPosition - targetPosition).sqrMagnitude > maxSeparation * maxSeparation) {
                SnapBack();
                return;
            }

            // This frame's goal and rotation depend on the hand's state:
            // - In contact: keeps the rotation it touched with instead of
            //   following the controller. Pushing into a wall usually tilts
            //   the wrist (the arm pivots at the shoulder), and a tilted hand
            //   swings its fingers into the wall - the push-out would then back
            //   the whole hand away from it. Holding the rotation keeps the
            //   hand still against the surface; it still slides along it.
            // - Returning: part of the way from where it was released to the
            //   controller. SmoothStep eases in and out, like HandVisualSnap,
            //   and clamps, so the weight stops at 1 (fully back).
            // - Otherwise: simply the controller.
            Vector3 goalPosition = targetPosition;
            Quaternion handRotation = controllerRotation;

            if (IsInContact) {
                handRotation = _handRotation;
            } else if (_isReturning) {
                _returnTime += deltaTime;
                float weight = catchUpDuration > 0f ? Mathf.SmoothStep(0f, 1f, _returnTime / catchUpDuration) : 1f;

                goalPosition = targetPosition + controllerRotation * (_returnOffset * (1f - weight));
                handRotation = controllerRotation * Quaternion.Slerp(_returnRotationOffset, Quaternion.identity, weight);
            }

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
                collisionLayers, radius, skinWidth);

            Vector3 position = SweepAndSlide(
                start, goalPosition,
                wristOffset, fingertipOffset,
                collisionLayers, radius, skinWidth);

            bool isBlocked = (position - goalPosition).sqrMagnitude > ContactThreshold * ContactThreshold;

            // Reached the controller's position while still holding an old
            // rotation: only let go if the hand also fits at the controller's
            // own rotation. Otherwise it would ease back with its fingers
            // going into the wall, be pushed out, and flicker between the two.
            // Costs one extra query, and only while in contact.
            if (!isBlocked && IsInContact) {
                isBlocked = OverlapsAt(targetPosition, controllerRotation, collisionLayers, radius);
            }

            if (isBlocked) {
                // Blocked (again) - a hand that was easing back is in contact
                // once more, and holds the rotation it has now.
                _isReturning = false;
            } else if (IsInContact) {
                // Just came free. Rather than jumping to the controller, start
                // easing back from where the hand is now: it stays put this
                // frame, and its offset from the controller - position and
                // rotation, in the controller's space - shrinks to nothing
                // over the next catchUpDuration seconds.
                Quaternion worldToController = Quaternion.Inverse(controllerRotation);
                _returnOffset = worldToController * (start - targetPosition);
                _returnRotationOffset = worldToController * _handRotation;
                _returnTime = 0f;
                _isReturning = true;
                position = start;
            } else if (_isReturning && _returnTime >= catchUpDuration) {
                // Eased all the way back - the hand is exactly on the
                // controller, so it can be re-attached below.
                _isReturning = false;
            }

            IsInContact = isBlocked;
            bool isAttached = _visual.parent == _restParent;

            if (IsInContact || _isReturning) {
                // Held back by a surface, or easing back - detach (only on the
                // first such frame, so the hierarchy change isn't a per-frame
                // cost) and place the visual ourselves.
                if (isAttached) {
                    _visual.SetParent(null, true);
                }

                _visual.SetPositionAndRotation(position, handRotation * _restLocalRotation);
            } else if (!isAttached) {
                // Back on the controller - under it again, at exactly its rest
                // pose, so it gets the before-render update again.
                _visual.SetParent(_restParent, false);
                _visual.SetLocalPositionAndRotation(_restLocalPosition, _restLocalRotation);
            }

            _lastPosition = position;
            _handRotation = handRotation;
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
            _isReturning = false;
            _isPassingThrough = false;
        }

        /// <summary>
        /// Puts the visual straight back on its controller at its rest pose and
        /// switches collision off until the controller is clear of geometry -
        /// see Tick(). Without that, the next frame would push the hand out to
        /// the nearest face of whatever the controller is inside; if that's the
        /// near side, it would be too far away again, snap back again, and
        /// flicker every frame.
        /// </summary>
        private void SnapBack()
        {
            IsInContact = false;
            _isReturning = false;
            _isPassingThrough = true;

            _visual.SetParent(_restParent, false);
            _visual.SetLocalPositionAndRotation(_restLocalPosition, _restLocalRotation);
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

            Quaternion handRotation = IsInContact || _isReturning ? _handRotation : controllerRotation;
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
            wristOffset = rotation * _wristFromRoot;
            fingertipOffset = rotation * _fingertipFromRoot;

            InsetCapsuleEnds(ref wristOffset, ref fingertipOffset, radius);
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
        /// </summary>
        private Vector3 Depenetrate(
            Vector3 position,
            Vector3 wristOffset,
            Vector3 fingertipOffset,
            Quaternion capsuleRotation,
            LayerMask collisionLayers,
            float radius,
            float skinWidth)
        {
            for (int pass = 0; pass < MaxDepenetrationPasses; pass++) {
                int count = Physics.OverlapCapsuleNonAlloc(
                    position + wristOffset,
                    position + fingertipOffset,
                    radius,
                    _overlapBuffer,
                    collisionLayers,
                    QueryTriggerInteraction.Ignore);

                if (count == 0) {
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

                if (!moved) {
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

                // A distance of 0 means the capsule started inside something.
                // Depenetrate() has already pushed the start clear, so this
                // only happens when it couldn't (squeezed into a gap narrower
                // than the hand). Treating that as a block would pin the hand
                // inside, so let it move freely instead.
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
