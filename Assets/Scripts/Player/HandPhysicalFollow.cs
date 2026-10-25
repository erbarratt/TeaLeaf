using System.Collections.Generic;
using Core;
using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Makes one hand visual stop at surfaces instead of passing through
    /// them - the "physical hands" collision response. A kinematic sweep, no
    /// Rigidbody: each frame, the hand's collision shape is swept from where
    /// the visual was last frame towards where the controller wants it. On a
    /// hit the hand stops just short of the surface and slides along it
    /// ("collide and slide", the same idea CharacterController uses), so it
    /// never goes into the wall.
    ///
    /// The shape is several pieces (HandCollisionShape) moved together: a box
    /// for the palm and a capsule each for the thumb and fingers, measured
    /// from the model's bones by HandShapeFitter in two poses - open and
    /// fist. The index finger switches pose with the trigger and the other
    /// three with the grip (SetFingerCurl()), as the hand Animator curls
    /// them; the palm and thumb never change. A carried prop adds a piece per
    /// collider (SetHeldShape()), so both the hand and the prop stop at
    /// surfaces. Each sweep casts every piece and keeps the nearest hit.
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

        // Colliders one piece of the hand can overlap at once. Shared by both
        // hands (they're ticked one after the other, never at the same time)
        // and allocated once, so the overlap check never allocates. Anything
        // past this many is caught by the next pass.
        private static readonly Collider[] _overlapBuffer = new Collider[8];

        // Where each hand's pieces' colliders sit, well below the level, for
        // their whole life - see HandCollisionShape.
        private static readonly Vector3 _parkedColliderPosition = new(0f, -1000f, 0f);

        // Most colliders of a carried prop that get a piece each. A prop with
        // more is carried as one box round all of them (see SetHeldShape()).
        private const int MaxPropShapes = 4;

        // Trigger or grip past this curls the fingers it drives into the
        // fist pose; back under ReleaseCurl opens them. The gap stops a
        // half-pressed trigger flicking the pose (and resizing colliders)
        // every frame.
        private const float CloseCurl = 0.6f;
        private const float ReleaseCurl = 0.4f;

        private readonly Transform _visual;

        // The controller the visual normally lives under, and returns to.
        private readonly Transform _restParent;

        // The visual's own local pose under the controller, captured once -
        // "following the controller" means sitting exactly here.
        private readonly Vector3 _restLocalPosition;
        private readonly Quaternion _restLocalRotation;

        // Every piece: the hand's own first (HandShapeFitter.ShapeCount),
        // then MaxPropShapes slots for a carried prop's colliders, empty
        // while nothing is carried. The parent their colliders are parked
        // under, destroyed with them.
        private readonly HandCollisionShape[] _shapes;
        private readonly GameObject _shapesParent;

        // The hand's own pieces in each pose, measured once at load (see
        // HandShapeFitter). Empty-handed if the bones weren't found.
        private readonly HandShapeGeometry[] _openShapes = new HandShapeGeometry[HandShapeFitter.ShapeCount];
        private readonly HandShapeGeometry[] _closedShapes = new HandShapeGeometry[HandShapeFitter.ShapeCount];
        private readonly bool _hasHandShapes;

        // Whether the index finger (trigger) and the other three (grip) are
        // in the fist pose - see SetFingerCurl().
        private bool _isIndexClosed;
        private bool _isGripClosed;

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

        /// <summary>
        /// Captures the visual's rest pose and measures the hand's pieces
        /// (HandShapeFitter, with sizes) - at construction (Awake), while the
        /// visual still sits at its rest pose under the controller and the
        /// fingers are at their rest pose.
        /// </summary>
        public HandPhysicalFollow(Transform visual, HandShapeSizes sizes)
        {
            _visual = visual;
            _restParent = visual.parent;
            _restLocalPosition = visual.localPosition;
            _restLocalRotation = visual.localRotation;

            _shapes = HandCollisionShape.Create(
                $"{visual.name} Collision Shapes",
                HandShapeFitter.ShapeCount + MaxPropShapes,
                _parkedColliderPosition,
                out _shapesParent);

            _hasHandShapes = HandShapeFitter.FitHand(visual, sizes, _openShapes, _closedShapes);
            ApplyHandShapes();
        }

        /// <summary>
        /// Switches the fingers' pieces between open and fist as the hand
        /// Animator curls them: the index finger by trigger, the other three
        /// by grip (each 0-1). Called every frame before Tick(); colliders are
        /// only resized on the frame a pose switches. Opening against a wall
        /// pushes the hand back out next Tick(), as turning into it does.
        /// </summary>
        public void SetFingerCurl(float trigger, float grip)
        {
            bool isIndexClosed = _isIndexClosed ? trigger > ReleaseCurl : trigger > CloseCurl;
            bool isGripClosed = _isGripClosed ? grip > ReleaseCurl : grip > CloseCurl;

            if (isIndexClosed == _isIndexClosed && isGripClosed == _isGripClosed) {
                return;
            }

            _isIndexClosed = isIndexClosed;
            _isGripClosed = isGripClosed;
            ApplyHandShapes();
        }

        /// <summary>
        /// Puts the hand's own pieces in the pose each finger is in. The
        /// palm and thumb are the same in both poses.
        /// </summary>
        private void ApplyHandShapes()
        {
            if (!_hasHandShapes) {
                return;
            }

            for (int i = 0; i < HandShapeFitter.ShapeCount; i++) {
                bool isClosed = i == HandShapeFitter.IndexShape ? _isIndexClosed : i > HandShapeFitter.IndexShape && _isGripClosed;
                _shapes[i].Set(isClosed ? _closedShapes[i] : _openShapes[i]);
            }
        }

        /// <summary>
        /// Adds a carried prop to the swept shape: a piece the same shape as
        /// each of its solid colliders, alongside the hand's own, so the
        /// prop stops at a wall with the hand instead of going into it.
        /// handPosition and handRotation are where the visual holds the prop
        /// from (the pose it's snapping to); the colliders are measured where
        /// they lie now, which is where they'll be relative to the hand. A
        /// prop with more than MaxPropShapes solid colliders is carried as
        /// one box round them all.
        ///
        /// Only called on pick-up, so resizing the pieces' colliders (which
        /// makes PhysX rebuild them) isn't a per-frame cost.
        /// </summary>
        public void SetHeldShape(IReadOnlyList<Collider> colliders, Vector3 handPosition, Quaternion handRotation)
        {
            ClearHeldShape();
            int slot = HandShapeFitter.ShapeCount;

            for (int i = 0; i < colliders.Count; i++) {
                if (!HandShapeFitter.FitCollider(colliders[i], handPosition, handRotation, _restLocalRotation, out HandShapeGeometry geometry)) {
                    continue;
                }

                if (slot == _shapes.Length) {
                    SetHeldBox(colliders, handPosition, handRotation);
                    return;
                }

                _shapes[slot++].Set(geometry);
            }
        }

        /// <summary>
        /// The many-collider fallback for SetHeldShape(): one level box round
        /// every solid collider's world bounds, as they lie now.
        /// </summary>
        private void SetHeldBox(IReadOnlyList<Collider> colliders, Vector3 handPosition, Quaternion handRotation)
        {
            ClearHeldShape();
            Bounds bounds = default;
            bool hasBounds = false;

            for (int i = 0; i < colliders.Count; i++) {
                Collider collider = colliders[i];

                if (collider.isTrigger || !collider.enabled) {
                    continue;
                }

                if (hasBounds) {
                    bounds.Encapsulate(collider.bounds);
                } else {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
            }

            Quaternion fromWorld = _restLocalRotation * Quaternion.Inverse(handRotation);
            _shapes[HandShapeFitter.ShapeCount].Set(HandShapeGeometry.Box(
                fromWorld * (bounds.center - handPosition), fromWorld, bounds.extents));
        }

        /// <summary>
        /// Back to just the hand's own pieces, when the prop is dropped.
        /// </summary>
        public void ClearHeldShape()
        {
            for (int i = HandShapeFitter.ShapeCount; i < _shapes.Length; i++) {
                _shapes[i].Clear();
            }
        }

        /// <summary>
        /// Moves the visual towards the controller, stopping and sliding at
        /// anything on collisionLayers, and easing back over catchUpDuration
        /// seconds once free - or easing back through the surface if it holds
        /// the hand more than maxSeparation from the controller. skinWidth is
        /// the gap kept from surfaces and maxSeparation the snap-back
        /// distance, both in metres - all passed in every frame so Inspector
        /// changes apply live.
        /// </summary>
        public void Tick(
            LayerMask collisionLayers,
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
                TickPassingThrough(targetPosition, controllerRotation, collisionLayers, catchUpDuration, deltaTime);
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

            OrientShapes(handRotation);

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
            Vector3 start = Depenetrate(_lastPosition, collisionLayers, skinWidth, out bool startsClear);

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

            Vector3 position = SweepAndSlide(start, goalPosition, collisionLayers, skinWidth, startsClear);

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
                    if (!OverlapsAt(position, controllerRotation, collisionLayers)) {
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

            if (_isReturning || _isRotationReturning || OverlapsAt(targetPosition, controllerRotation, collisionLayers)) {
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
        /// True if the hand's pieces, with its root at position and turned to
        /// a controller rotation, are inside anything on collisionLayers.
        /// </summary>
        private bool OverlapsAt(Vector3 position, Quaternion rotation, LayerMask collisionLayers)
        {
            OrientShapes(rotation);

            for (int i = 0; i < _shapes.Length; i++) {
                if (_shapes[i].IsActive && _shapes[i].Check(position, collisionLayers)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Turns every piece to a hand whose controller is turned to
        /// rotation, as an offset from the visual's root. The sweep only moves
        /// the hand, never turns it, so this holds all along it. A rotation
        /// alone is enough (no matrix) because the controllers are unscaled -
        /// the parked colliders rely on that too.
        /// </summary>
        private void OrientShapes(Quaternion rotation)
        {
            for (int i = 0; i < _shapes.Length; i++) {
                if (_shapes[i].IsActive) {
                    _shapes[i].Orient(rotation);
                }
            }
        }

        /// <summary>
        /// Destroys the pieces' colliders. They live at the scene root rather
        /// than under the player, so they aren't cleaned up with their owner -
        /// PlayerHandVisuals calls this from OnDestroy().
        /// </summary>
        public void Destroy()
        {
            HandCollisionShape.Destroy(_shapesParent);
        }

        /// <summary>
        /// Draws every piece twice - where the controller wants the hand
        /// (targetColor) and where the visual actually is (visualColor) - for
        /// debug gizmos. The visual's uses the rotation it was given (held or
        /// easing back) while it's detached, as the sweep does, so it's
        /// exactly the shape being swept.
        /// </summary>
        public void DrawDebug(DebugLines lines, Color targetColor, Color visualColor)
        {
            _restParent.GetPositionAndRotation(out Vector3 controllerPosition, out Quaternion controllerRotation);
            Vector3 targetPosition = controllerPosition + controllerRotation * _restLocalPosition;
            Quaternion handRotation = IsPlacedByCode ? _handRotation : controllerRotation;

            for (int i = 0; i < _shapes.Length; i++) {
                if (!_shapes[i].IsActive) {
                    continue;
                }

                lines.Color = targetColor;
                _shapes[i].Geometry.Draw(lines, targetPosition, controllerRotation);
                lines.Color = visualColor;
                _shapes[i].Geometry.Draw(lines, _visual.position, handRotation);
            }
        }

        /// <summary>
        /// Pushes the hand (root at position, pieces where OrientShapes() put
        /// them) out of anything on collisionLayers it overlaps, and returns
        /// where the root ends up - position itself if it's already clear.
        ///
        /// Each piece's overlap lists what it's inside (into the shared
        /// buffer, no allocation). For each, ComputePenetration gives the
        /// shortest way out: a direction and a distance. The whole hand is
        /// moved that far plus skinWidth, so it ends a little clear of the
        /// surface rather than exactly touching it. Nearly every frame the
        /// overlaps find nothing and this costs one cheap query per piece.
        ///
        /// isClear says whether it got the hand out of everything. False
        /// means it ran out of passes still moving - e.g. wedged between two
        /// surfaces closer together than the hand, where each push-out goes
        /// into the other one.
        /// </summary>
        private Vector3 Depenetrate(Vector3 position, LayerMask collisionLayers, float skinWidth, out bool isClear)
        {
            isClear = false;

            for (int pass = 0; pass < MaxDepenetrationPasses; pass++) {
                bool overlaps = false;
                bool moved = false;

                for (int s = 0; s < _shapes.Length; s++) {
                    HandCollisionShape shape = _shapes[s];

                    if (!shape.IsActive) {
                        continue;
                    }

                    int count = shape.Overlap(position, _overlapBuffer, collisionLayers);

                    for (int i = 0; i < count; i++) {
                        Collider other = _overlapBuffer[i];

                        // Only possible if collisionLayers wrongly includes
                        // PlayerHands - the hand can't push out of itself.
                        if (shape.Owns(other)) {
                            continue;
                        }

                        overlaps = true;

                        // False if they only touch - nothing to push out of.
                        if (!shape.ComputePenetration(position, other, out Vector3 direction, out float distance)) {
                            continue;
                        }

                        // Moved immediately rather than summed, so the next
                        // collider (and piece) is checked from where the
                        // hand now is.
                        position += direction * (distance + skinWidth);
                        moved = true;
                    }
                }

                // Inside nothing, or overlapping only by touching - as clear
                // as it gets.
                if (!overlaps || !moved) {
                    isClear = true;
                    break;
                }
            }

            return position;
        }

        /// <summary>
        /// Sweeps every piece from position along direction for distance and
        /// gives the nearest hit of them all - where the hand as a whole
        /// stops. No allocations: each cast returns one hit through an out
        /// parameter.
        /// </summary>
        private bool CastShapes(Vector3 position, Vector3 direction, float distance, LayerMask collisionLayers, out RaycastHit nearest)
        {
            nearest = default;
            bool hasHit = false;

            for (int i = 0; i < _shapes.Length; i++) {
                if (!_shapes[i].IsActive || !_shapes[i].Cast(position, direction, distance, collisionLayers, out RaycastHit hit)) {
                    continue;
                }

                if (!hasHit || hit.distance < nearest.distance) {
                    nearest = hit;
                    hasHit = true;
                }
            }

            return hasHit;
        }

        /// <summary>
        /// Sweeps the hand (root at from, pieces where OrientShapes() put
        /// them) towards to. Each hit stops it skinWidth short of the surface;
        /// the rest of the movement is flattened onto the surface (so pushing
        /// diagonally into a wall slides along it) and swept again, up to
        /// MaxSweeps times. Returns where the root ends up - to itself if
        /// nothing was in the way. startsClear is Depenetrate()'s isClear for
        /// from.
        /// </summary>
        private Vector3 SweepAndSlide(
            Vector3 from,
            Vector3 to,
            LayerMask collisionLayers,
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
                bool hit = CastShapes(position, direction, distance + skinWidth, collisionLayers, out RaycastHit hitInfo);

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
