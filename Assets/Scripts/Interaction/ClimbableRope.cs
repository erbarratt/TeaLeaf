using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A climbable rope: a static line running from this object's position
    /// along its local -Y for `length` metres. Unrotated, that's a rope
    /// hanging straight down; turn the object and it runs any way - on its
    /// side it's a rope strung between two buildings. A CapsuleCollider
    /// around the line is the hand-ray target - sized by this component
    /// itself, so a rope can be placed by hand in a level or spawned at
    /// runtime (Phase 5's rope bolt) and given its length with SetLength().
    ///
    /// A strung rope is easier placed by its two ends: give it an end point
    /// (any transform) and it runs from this object's position to that one,
    /// whatever this object's rotation and `length`. It can also droop:
    /// `sag` is how far its middle hangs below the straight line between
    /// its ends (0 = perfectly straight, e.g. a zip line). The object's own
    /// capsule can't bend or point at an end point, so either kind's grab
    /// volume is a chain of capsules along the rope, made in Awake() - see
    /// BuildSegments().
    ///
    /// Climbing it is PlayerClimbing's job and moves the player exactly like
    /// a ledge or ladder (unconstrained - the body hangs off the grab point).
    /// No mesh: gizmos show it in the Scene view, and a visible rope adds its
    /// own renderer (e.g. the rope-bolt prefab's).
    ///
    /// Unlike ClimbableEdge and Ladder there's no front or back: a rope can
    /// be grabbed from any side, and the hand always wraps round it the
    /// same way relative to the player - see GetSnapPose().
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public class ClimbableRope : MonoBehaviour, IHandTarget, IClimbable, IZipLine, IDebugDrawable
    {
        // Shared hand offsets/pose for all ropes - see HandSnapProfile.
        [SerializeField] private HandSnapProfile snapProfile;

        // How far the rope runs from this object's position along its local
        // -Y, in metres - the rope's size is set here, never on the (locked)
        // CapsuleCollider. The top stays put: the object's position is the
        // top (start) of the rope. For a sagging rope this is the straight
        // distance between its two ends, not the length of the curve.
        [SerializeField] private float length = 4f;

        // Radius of the grab collider around the rope, in metres. Much
        // thicker than a real rope on purpose: a 2cm target is almost
        // impossible to land a hand ray on.
        [SerializeField] private float grabRadius = 0.08f;

        // On a hanging rope, how far round the rope each hand's grip is
        // turned from pointing its wrist straight at the player, in degrees,
        // so the wrist points at that hand's shoulder instead - see
        // GetSnapPose(). No effect on a strung rope.
        [SerializeField] private float shoulderAngle = 20f;

        [Header("Strung Ropes (between two points)")]

        // Optional: where the rope ends. Set, the rope runs from this
        // object's position straight to this transform's - place the two
        // ends and the rope joins them, with no rotating this object or
        // working out `length` (both are then ignored). Empty, the rope runs
        // along local -Y for `length` as usual. Any transform will do: an
        // empty child of the rope, or an object on the other building. Like
        // sag, it's read when Play starts - the rope doesn't follow an end
        // point that moves afterwards.
        [SerializeField] private Transform endPoint;

        // How far the middle of the rope droops below the straight line
        // between its ends, in metres, straight down in the world. 0 = a
        // perfectly straight rope (hanging ropes, zip lines). Only meant for
        // ropes strung sideways - leave it at 0 on a hanging rope. Set
        // before Play: the grab volumes of a sagging rope are made in
        // Awake(). A sagging rope's object should be left at scale 1.
        [SerializeField] private float sag;

        // How many straight pieces a sagging rope's curve is made of - its
        // grab volumes, its gizmo, and where hands snap. More is smoother;
        // 8 is within about a centimetre of the true curve for a 0.5m sag.
        [SerializeField] private int sagSegments = 8;

        [Header("Zip Line")]

        // Ticked, the rope is a zip wire: a hand that grabs it anywhere
        // slides along it, carrying the player, until they let go or reach
        // the end - then they drop. It slides downhill; on a level rope,
        // the way the player was facing when they grabbed it. A zip line
        // can't be climbed hand over hand - every grab slides.
        [SerializeField] private bool isZipLine;

        // Top sliding speed, in metres per second.
        [SerializeField] private float zipSpeed = 6f;

        // How quickly a slide gets up to zipSpeed, in metres per second per
        // second - a short run-up rather than an instant jerk to full speed.
        [SerializeField] private float zipAcceleration = 8f;

        // Smallest allowed length - keeps the capsule valid.
        private const float MinLength = 0.1f;

        // A stretch of rope counts as strung (sideways) rather than hanging
        // when the Y of its direction is under this: 0.7 is about 45 degrees
        // from level. See GetSnapPose().
        private const float StrungMaxUpY = 0.7f;

        // A zip line whose ends differ in height by less than this, in
        // metres, counts as level: it has no downhill, so a slide goes the
        // way the player faces. See GetZipDirection().
        private const float ZipLevelTolerance = 0.05f;

        // The layer the rope (its grab volume) must be on - see WarnAboutSetup().
        private const string ClimbableLayerName = "Climbable";

        // Gizmo colours: faint when the rope isn't selected, bright when it is.
        private static readonly Color _gizmoColor = new(0.9f, 0.8f, 0.4f, 0.35f);
        private static readonly Color _gizmoSelectedColor = new(0.9f, 0.8f, 0.4f, 1f);

        private CapsuleCollider _capsuleCollider;

        // The grab volumes of a rope that sags or has an end point, one per
        // straight piece, each on its own child object - null for a plain
        // rope, which only needs _capsuleCollider. See BuildSegments().
        private CapsuleCollider[] _segmentColliders;

        /// How far the rope runs from its top, in metres - the straight
        /// distance to the end point when it has one.
        public float Length => endPoint != null ? Vector3.Distance(transform.position, endPoint.position) : length;

        /// Whether the grab volume is the chain of child capsules rather
        /// than this object's own capsule: any rope that sags (a capsule
        /// can't bend) or has an end point (its line no longer follows this
        /// object's local -Y, the only way its own capsule can point).
        private bool UsesSegments => sag > 0f || endPoint != null;

        /// Whether this rope is a zip wire - see isZipLine.
        public bool IsZipLine => isZipLine;

        /// Top sliding speed of a zip line, in metres per second.
        public float ZipSpeed => zipSpeed;

        /// How quickly a slide reaches ZipSpeed, in metres per second squared.
        public float ZipAcceleration => zipAcceleration;

        /// How many straight pieces the rope is made of: 1 for a straight
        /// rope, sagSegments for a sagging one. Piece i runs from
        /// GetPoint(i / SegmentCount) to GetPoint((i + 1) / SegmentCount).
        public int SegmentCount => sag > 0f ? sagSegments : 1;

        private void Awake()
        {
            _capsuleCollider = GetComponent<CapsuleCollider>();
            FitCollider(_capsuleCollider);

            if (UsesSegments) {
                BuildSegments();
            }
        }

        private void OnEnable()
        {
            HandTargetRegistry.Register(_capsuleCollider, this);

            // Every piece of a sagging rope leads back to this one rope, so
            // the hand code never knows it hit a piece.
            if (_segmentColliders != null) {
                for (int i = 0; i < _segmentColliders.Length; i++) {
                    HandTargetRegistry.Register(_segmentColliders[i], this);
                }
            }

            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            HandTargetRegistry.Unregister(_capsuleCollider);

            if (_segmentColliders != null) {
                for (int i = 0; i < _segmentColliders.Length; i++) {
                    HandTargetRegistry.Unregister(_segmentColliders[i]);
                }
            }

            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// World position of the point a fraction t along the rope (0 = this
        /// object's position, 1 = the far end: the end point if there is
        /// one, otherwise `length` along local -Y). A straight rope is the
        /// line between the two; sag lowers each point by a parabola -
        /// nothing at the ends, the full sag in the middle - which is very
        /// close to how a real rope hangs when the droop is small next to
        /// its length.
        /// </summary>
        public Vector3 GetPoint(float t)
        {
            Vector3 start = transform.position;
            Vector3 end = endPoint != null ? endPoint.position : start - transform.up * length;
            Vector3 point = Vector3.LerpUnclamped(start, end, t);

            if (sag > 0f) {
                // 4t(1 - t) is 0 at both ends and 1 at t = 0.5.
                point += Vector3.down * (sag * 4f * t * (1f - t));
            }

            return point;
        }

        /// <summary>
        /// Editor-only: runs whenever a value is changed in the Inspector,
        /// and when the scene loads. Keeps the values sane and the collider
        /// matching them.
        ///
        /// Also locks the CapsuleCollider (greyed out in the Inspector):
        /// `length` and `grabRadius` here are the only controls. Editing the
        /// collider's Height directly grows it around its centre rather than
        /// down from the top, and the gizmo and snapping would never hear
        /// about it - so it isn't allowed.
        /// </summary>
        private void OnValidate()
        {
            length = Mathf.Max(length, MinLength);
            grabRadius = Mathf.Max(grabRadius, 0.01f);
            sag = Mathf.Max(sag, 0f);
            sagSegments = Mathf.Max(sagSegments, 2);
            zipSpeed = Mathf.Max(zipSpeed, 0.1f);
            zipAcceleration = Mathf.Max(zipAcceleration, 0.1f);

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();

            if (capsule != null) {
                capsule.hideFlags |= HideFlags.NotEditable;
            }

            FitCollider(capsule);
            FitSegments();
            WarnAboutSetup(capsule);
        }

        /// <summary>
        /// Editor-only, from OnValidate(): warns (clickable, selecting the
        /// problem object) about set-ups that stop the rope being grabbed.
        /// The intended set-up is this object's CapsuleCollider as the grab
        /// volume - a trigger (FitCollider() makes sure) on the Climbable
        /// layer - plus, optionally, a thinner solid collider on a child as
        /// the rope itself (Environment, so the physical hands stop against
        /// it). Hands inside the grab volume can still grab
        /// (PlayerHandInteraction), so the solid rope only has to fit inside:
        /// - this object not on the Climbable layer;
        /// - a child collider reaching as far as grabRadius or further - hand
        ///   rays would hit it before (or tied with) the grab volume, so the
        ///   rope couldn't be aimed at reliably. Found 2026-09-28: both were
        ///   0.04m, and the reticle flickered as rays hit one or the other.
        /// </summary>
        private void WarnAboutSetup(CapsuleCollider capsule)
        {
            int climbableLayer = LayerMask.NameToLayer(ClimbableLayerName);

            if (climbableLayer >= 0 && gameObject.layer != climbableLayer) {
                Debug.LogWarning(
                    $"ClimbableRope '{name}': not on the {ClimbableLayerName} layer, so hand rays may miss " +
                    "it or it may collide with things it shouldn't.",
                    this);
            }

            // The check below measures from a straight axis, so it can't
            // judge the pieces of a visible rope that follows a sag.
            if (sag > 0f) {
                return;
            }

            // Allocates, but OnValidate only runs in the editor.
            foreach (Collider child in GetComponentsInChildren<Collider>(true)) {
                if (child == capsule || child.isTrigger) {
                    continue;
                }

                if (FurthestFromAxis(child) >= grabRadius) {
                    Debug.LogWarning(
                        $"ClimbableRope '{name}': the collider on '{child.name}' reaches as far from the rope " +
                        $"as grabRadius ({grabRadius}m) or further, so hand rays can hit it instead of the grab volume. " +
                        "Make the solid rope thinner than the grab volume, or raise grabRadius.",
                        child);
                }
            }
        }

        /// <summary>
        /// How far the furthest corner of a collider's box is from the
        /// rope's axis, sideways. A box's corners reach further than a round
        /// rope inside it, so this errs towards warning - fine for a check
        /// that only has to catch a clearly oversized collider.
        ///
        /// The box is the collider's own, turned with its object
        /// (LocalBox()), not Collider.bounds: that one is lined up with the
        /// world's axes, so around a long rope running at an angle it is
        /// enormous and its corners are metres from the rope. Fixed
        /// 2026-10-03 - the angled zip line's 2cm visible rope was warned
        /// about.
        /// </summary>
        private float FurthestFromAxis(Collider collider)
        {
            // From the rope's two ends rather than this object's own up, so
            // a rope with an end point is measured along its real line.
            Vector3 top = GetPoint(0f);
            Vector3 axis = (top - GetPoint(1f)).normalized;
            Bounds box = LocalBox(collider);
            Transform colliderTransform = collider.transform;
            float furthest = 0f;

            for (int i = 0; i < 8; i++) {
                Vector3 localCorner = box.center + Vector3.Scale(box.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));

                Vector3 corner = colliderTransform.TransformPoint(localCorner);
                Vector3 sideways = Vector3.ProjectOnPlane(corner - top, axis);
                furthest = Mathf.Max(furthest, sideways.magnitude);
            }

            return furthest;
        }

        /// <summary>
        /// The box around a collider in its own object's local space, so it
        /// turns and scales with the object. Editor-only (from OnValidate()).
        /// </summary>
        private static Bounds LocalBox(Collider collider)
        {
            switch (collider) {
                case BoxCollider box:
                    return new Bounds(box.center, box.size);

                case SphereCollider sphere:
                    return new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));

                case CapsuleCollider capsule: {
                    // Round across, and at least that long: a capsule's
                    // height can't be less than its width. direction is the
                    // local axis it runs along (0 = X, 1 = Y, 2 = Z).
                    float diameter = capsule.radius * 2f;
                    Vector3 size = Vector3.one * diameter;
                    size[capsule.direction] = Mathf.Max(capsule.height, diameter);
                    return new Bounds(capsule.center, size);
                }

                case MeshCollider mesh when mesh.sharedMesh != null:
                    return mesh.sharedMesh.bounds;

                default: {
                    // Anything else: the world-aligned box, brought into
                    // local space by its centre and size only. Over-sized
                    // if the object is rotated, so it can still warn falsely.
                    Transform colliderTransform = collider.transform;
                    Vector3 scale = colliderTransform.lossyScale;
                    Vector3 size = collider.bounds.size;
                    return new Bounds(
                        colliderTransform.InverseTransformPoint(collider.bounds.center),
                        new Vector3(size.x / scale.x, size.y / scale.y, size.z / scale.z));
                }
            }
        }

        /// <summary>
        /// Sets how far the rope hangs down and resizes its collider to
        /// match - e.g. for the rope bolt, once it knows the distance to the
        /// ground. Only call on spawn/change, not every frame.
        /// </summary>
        public void SetLength(float newLength)
        {
            length = Mathf.Max(newLength, MinLength);
            FitCollider(_capsuleCollider);
            FitSegments();
        }

        /// <summary>
        /// Sets the hand snap profile, for a rope made from code (the rope
        /// bolt's), which can't be given one in the Inspector.
        /// </summary>
        public void SetSnapProfile(HandSnapProfile profile)
        {
            snapProfile = profile;
        }

        /// <summary>
        /// Makes the grab volumes of a rope that sags or has an end point:
        /// one trigger capsule per straight piece (a single piece for a
        /// straight rope to an end point), each on its own child object,
        /// since a capsule can only point along one of its object's own axes
        /// and every piece tilts differently. Called once, from Awake(), so
        /// the scene itself holds nothing but the rope object.
        /// </summary>
        private void BuildSegments()
        {
            _segmentColliders = new CapsuleCollider[SegmentCount];

            for (int i = 0; i < _segmentColliders.Length; i++) {
                GameObject segment = new("Rope Segment") { layer = gameObject.layer };
                segment.transform.SetParent(transform, false);

                CapsuleCollider capsule = segment.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.direction = 1;
                _segmentColliders[i] = capsule;
            }

            FitSegments();
        }

        /// <summary>
        /// Places and sizes each piece's capsule along the curve: centred on
        /// the middle of its piece, its Y axis turned along it. Each is a
        /// grabRadius longer at both ends than its piece, so neighbours
        /// overlap and there's no gap to aim through at the joints. Does
        /// nothing for a straight rope (no pieces).
        /// </summary>
        private void FitSegments()
        {
            if (_segmentColliders == null) {
                return;
            }

            int count = _segmentColliders.Length;
            Vector3 from = GetPoint(0f);

            for (int i = 0; i < count; i++) {
                Vector3 to = GetPoint((i + 1f) / count);
                Vector3 along = to - from;

                CapsuleCollider capsule = _segmentColliders[i];
                capsule.transform.SetPositionAndRotation(
                    (from + to) * 0.5f,
                    Quaternion.FromToRotation(Vector3.up, along));
                capsule.height = along.magnitude + grabRadius * 2f;
                capsule.radius = grabRadius;

                from = to;
            }
        }

        /// <summary>
        /// Sizes the capsule to run from this object's position down local
        /// -Y for `length` metres with `grabRadius` thickness. The collider's
        /// values are in scaled local units, so the real-metre values are
        /// divided by the scale here - the rope stays the right size even if
        /// someone scales the object (though scale 1 is simplest).
        /// </summary>
        private void FitCollider(CapsuleCollider capsule)
        {
            if (capsule == null) {
                return;
            }

            Vector3 scale = transform.lossyScale;
            float scaleY = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
            float scaleXZ = Mathf.Max(Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)), 0.0001f);

            // The grab volume is always a trigger: it's something to aim at,
            // not a surface. Set here because the collider is locked in the
            // Inspector, so it can't be ticked there.
            capsule.isTrigger = true;

            // A rope that sags or has an end point is grabbed by its pieces
            // instead (see BuildSegments()) - this capsule would be a
            // second, wrong target along local -Y.
            capsule.enabled = !UsesSegments;
            capsule.direction = 1;
            capsule.center = new Vector3(0f, -length * 0.5f / scaleY, 0f);
            capsule.height = length / scaleY;
            capsule.radius = grabRadius / scaleXZ;
        }

        /// <summary>
        /// How far along the rope (0 at its top, 1 at the far end) the point
        /// nearest to worldPoint is. Only called on grab.
        /// </summary>
        public float GetClosestT(Vector3 worldPoint)
        {
            ClosestPointOnRope(worldPoint, out _, out _, out float ropeT);
            return ropeT;
        }

        /// <summary>
        /// Which way a hand slides along this rope as a zip line: +1 towards
        /// the far end, -1 back towards the top. Downhill when one end is
        /// lower than the other; on a level rope there's no downhill, so it
        /// goes whichever way along the rope the player is facing. Only
        /// called when a slide starts.
        /// </summary>
        public int GetZipDirection(Vector3 headForward)
        {
            Vector3 toEnd = GetPoint(1f) - GetPoint(0f);

            if (Mathf.Abs(toEnd.y) > ZipLevelTolerance) {
                return toEnd.y < 0f ? 1 : -1;
            }

            return Vector3.Dot(headForward, toEnd) >= 0f ? 1 : -1;
        }

        /// <summary>
        /// The point on the rope nearest to worldPoint (never past either
        /// end), which way is "up" along the rope there - back towards its
        /// top - and how far along the rope it is (ropeT, 0-1). Checks each
        /// straight piece in turn: one for a straight rope, SegmentCount for
        /// a sagging one. Only called on grab.
        /// </summary>
        private void ClosestPointOnRope(Vector3 worldPoint, out Vector3 closest, out Vector3 ropeUp, out float ropeT)
        {
            int count = SegmentCount;
            float closestSqrDistance = float.MaxValue;
            closest = transform.position;
            ropeUp = transform.up;
            ropeT = 0f;

            Vector3 from = GetPoint(0f);

            for (int i = 0; i < count; i++) {
                Vector3 to = GetPoint((i + 1f) / count);
                Vector3 along = to - from;

                // A piece with no length (an end point sitting on the rope's
                // own position) has no direction to measure along.
                if (along.sqrMagnitude < 0.000001f) {
                    continue;
                }

                // How far along this piece the point is, 0-1, by projecting
                // it onto the piece and stopping at its ends.
                float t = Mathf.Clamp01(Vector3.Dot(worldPoint - from, along) / along.sqrMagnitude);
                Vector3 candidate = from + along * t;
                float sqrDistance = (worldPoint - candidate).sqrMagnitude;

                if (sqrDistance < closestSqrDistance) {
                    closestSqrDistance = sqrDistance;
                    closest = candidate;
                    ropeUp = -along.normalized;

                    // Pieces are equal steps of t, so piece i covers
                    // i / count to (i + 1) / count.
                    ropeT = (i + t) / count;
                }

                from = to;
            }
        }

        /// <summary>
        /// Snaps a grabbing hand onto the rope: the point on the rope nearest
        /// to where the ray hit (never past either end), with the grip frame
        /// facing the rope from a fixed side - straight below on a strung
        /// rope, the player's side (turned towards that hand's shoulder) on
        /// a hanging one, so the grip looks the same every time - and its up
        /// running along the rope - towards its top on a hanging rope, back
        /// towards the player (against the way they face) on a strung one. The profile then offsets
        /// that into the hand visual's actual root pose. The frame is built
        /// from the rope's own direction at that point, so a rope on its
        /// side - or the tilted pieces of a sagging one - gets the hanging
        /// rope's pose turned to match, with nothing else to set up. Only
        /// called on grab.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint, Vector3 headPosition, Vector3 headForward)
        {
            ClosestPointOnRope(grabPoint, out Vector3 gripPosition, out Vector3 ropeUp, out _);

            // A hanging rope has a real up, so the hand always goes on the
            // same way round. A strung rope doesn't: its "up" is just whichever
            // end is its top, and that's only right for a player facing that
            // end - facing the other, the hand would be back to front (thumb
            // pointing away). So where the rope runs more sideways than up
            // and down, its up becomes whichever way along it points back
            // towards the player - the opposite of the way they're facing
            // (found in the headset: up along the facing direction put the
            // thumb away from the player both ways).
            bool isStrung = Mathf.Abs(ropeUp.y) < StrungMaxUpY;

            if (isStrung && Vector3.Dot(headForward, ropeUp) > 0f) {
                ropeUp = -ropeUp;
            }

            // Which side of the rope the hand goes on - the way the wrist
            // points away from it. Not where the hand happened to be when it
            // grabbed: that gave a different-looking grip every time.
            Vector3 side;

            if (isStrung) {
                // Strung: always from straight below, wrist hanging down, as
                // if the hand ray had come up from directly underneath.
                side = Vector3.ProjectOnPlane(Vector3.down, ropeUp);
            } else {
                // Hanging: towards the player, then turned round the rope by
                // shoulderAngle so the wrist points at that hand's shoulder
                // rather than the middle of the chest. Seen from above, that's
                // anticlockwise for the right hand and clockwise for the
                // left; Unity's positive angles about an upward axis are
                // clockwise from above, so the right hand's is negative. The
                // axis must point up for that to hold, even if the rope
                // object is upside down.
                Vector3 upAlongRope = ropeUp.y >= 0f ? ropeUp : -ropeUp;
                Vector3 towardsPlayer = Vector3.ProjectOnPlane(headPosition - gripPosition, ropeUp);
                float angle = isLeftHand ? shoulderAngle : -shoulderAngle;
                side = Quaternion.AngleAxis(angle, upAlongRope) * towardsPlayer;
            }

            // Fall back to the side the hand grabbed from, then the rope's
            // own +Z, then +X, if that came out as (almost) nothing - e.g.
            // the head directly above a hanging rope.
            if (side.sqrMagnitude < 0.000001f) {
                side = Vector3.ProjectOnPlane(grabPoint - gripPosition, ropeUp);
            }

            if (side.sqrMagnitude < 0.000001f) {
                side = Vector3.ProjectOnPlane(transform.forward, ropeUp);
            }

            if (side.sqrMagnitude < 0.000001f) {
                side = Vector3.ProjectOnPlane(transform.right, ropeUp);
            }

            // Grip frame: forward points from the hand's side into the rope,
            // up runs along the rope - the same shape as a ledge's frame,
            // with "into the wall" replaced by "into the rope".
            Quaternion gripRotation = Quaternion.LookRotation(-side.normalized, ropeUp);

            // See ClimbableEdge.GetSnapPose() for why == null.
            if (snapProfile == null) {
                return new HandSnapPose(gripPosition, gripRotation, HandPose.RopeGrip);
            }

            return snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }

        /// <summary>
        /// Always draws the rope's line faintly in the Scene view, so it's
        /// visible while laying out a level even though it has no mesh - see
        /// DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// While selected, draws the rope brightly with its grab thickness -
        /// see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// Draws the rope - as Scene view gizmos and, while InHeadsetGizmos
        /// is on, in the headset: its line, faint - one straight piece, or
        /// the pieces of its curve when it sags. With detailed on (selected),
        /// bright, plus the grab thickness around each piece: a sphere at
        /// each end and four lines down the capsule's sides. Drawn in world
        /// space from GetPoint(), so it matches the grab volumes and where
        /// hands snap.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            lines.Color = detailed ? _gizmoSelectedColor : _gizmoColor;

            int count = SegmentCount;
            Vector3 from = GetPoint(0f);

            for (int i = 0; i < count; i++) {
                Vector3 to = GetPoint((i + 1f) / count);
                lines.Line(from, to);

                if (detailed) {
                    lines.WireCapsule(from, to, grabRadius);
                }

                from = to;
            }
        }
    }
}
