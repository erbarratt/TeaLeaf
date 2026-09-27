using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// General-purpose hand-pointing interaction. Casts one ray from each
    /// hand every frame, records whatever IHandTarget it hits first (if any)
    /// and shows that hand's reticle there. Any object implementing
    /// IHandTarget is supported without this class needing to know about it
    /// specifically. Nothing is highlighted - the reticle alone shows the
    /// player what they can interact with.
    ///
    /// This class does not run its own Update(). Instead PlayerController
    /// calls Tick() (rays) and later TickReticles() explicitly once per
    /// frame - see PlayerController's class comment for why.
    /// </summary>
    public class PlayerHandInteraction : MonoBehaviour
    {
        [SerializeField] private PlayerTracking playerTracking;

        [Header("Hand Rays")]

        // How far each hand's ray reaches, in metres. Shared by both hands -
        // if one hand ever needs a different reach, split this into
        // leftHandRayLength/rightHandRayLength.
        [SerializeField] private float rayLength = 0.5f;

        // Layers hand rays can hit. Defaults to everything so behaviour is
        // unchanged until this is narrowed in the Inspector - restricting
        // it to just an "Interactable"-style layer avoids an unrelated
        // trigger volume (AI perception, item pickup, etc.) silently
        // blocking the ray before it reaches the intended IHandTarget.
        [SerializeField] private LayerMask interactableLayers = ~0;

        // Euler angle applied on top of the left hand's own rotation before
        // casting its ray, so the ray can point somewhere other than
        // straight out of the controller model (e.g. angled down along a
        // pointing finger). Tune per hand since the two controller models
        // are mirrored.
        [SerializeField] private Vector3 leftHandRayAngleOffset;

        // Same as leftHandRayAngleOffset, for the right hand.
        [SerializeField] private Vector3 rightHandRayAngleOffset;

        [Header("Reticles")]

        // Small billboard markers shown wherever each hand's ray currently
        // hits an IHandTarget - see HandRayReticle.
        [SerializeField] private HandRayReticle leftReticle;
        [SerializeField] private HandRayReticle rightReticle;

        // Where each hand ray is cast from (the hand visual, not the
        // controller - see Tick()), and read to hide a hand's reticle while
        // that hand is holding something - see TickReticles().
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // Each hand's ray direction in its own local space - the angle
        // offset already applied to Vector3.forward. The offsets never
        // change during play, so the Quaternion.Euler() (six sin/cos calls)
        // behind this is done once in Awake() rather than every frame for
        // both hands; OnValidate() redoes it when an offset is edited in the
        // Inspector, so tuning still updates live.
        private Vector3 _leftLocalRayDirection;
        private Vector3 _rightLocalRayDirection;

        /// Whatever the left hand's ray is currently pointing at, or null.
        /// Exposed so other systems (e.g. PlayerClimbing, a future generic
        /// "interact" button) can act on whatever the hand is aimed at.
        public IHandTarget LeftTarget { get; private set; }

        /// Whatever the right hand's ray is currently pointing at, or null.
        public IHandTarget RightTarget { get; private set; }

        /// World-space point where the left hand's ray hit LeftTarget this
        /// frame - e.g. where PlayerClimbing grabs a ledge. Only meaningful
        /// while LeftTarget isn't null.
        public Vector3 LeftTargetPoint { get; private set; }

        /// World-space point where the right hand's ray hit RightTarget this
        /// frame. Only meaningful while RightTarget isn't null.
        public Vector3 RightTargetPoint { get; private set; }

        /// World-space origin of the left hand's ray this frame - exposed
        /// for HandRayDebug (Scripts/Player/Debug) to visualize while
        /// tuning leftHandRayAngleOffset.
        public Vector3 LeftRayOrigin { get; private set; }

        /// Direction (unit length, not scaled by rayLength) of the left
        /// hand's ray this frame.
        public Vector3 LeftRayDirection { get; private set; }

        /// World-space origin of the right hand's ray this frame.
        public Vector3 RightRayOrigin { get; private set; }

        /// Direction (unit length, not scaled by rayLength) of the right
        /// hand's ray this frame.
        public Vector3 RightRayDirection { get; private set; }

        /// Configured length of each hand's ray, in metres - exposed so
        /// HandRayDebug can draw the ray at its true length.
        public float RayLength => rayLength;

        private void Awake()
        {
            CacheLocalRayDirections();
        }

        /// <summary>
        /// Editor-only: runs whenever a value is changed in the Inspector.
        /// Re-caches the local ray directions so angle offset tuning in Play
        /// Mode still takes effect immediately.
        /// </summary>
        private void OnValidate()
        {
            CacheLocalRayDirections();
        }

        /// <summary>
        /// Rotates Vector3.forward by each hand's angle offset, giving the
        /// direction its ray points in, relative to the hand.
        /// </summary>
        private void CacheLocalRayDirections()
        {
            _leftLocalRayDirection = Quaternion.Euler(leftHandRayAngleOffset) * Vector3.forward;
            _rightLocalRayDirection = Quaternion.Euler(rightHandRayAngleOffset) * Vector3.forward;
        }

        /// <summary>
        /// Casts both hand rays and records whatever they hit. The reticles
        /// are placed later, in TickReticles().
        /// </summary>
        public void Tick()
        {
            // Cast from the hand the player sees, not the tracked controller:
            // when a surface holds the hand visual back, the ray starts from
            // there, so a controller pushed through a wall can't target (and
            // grab) whatever is behind it. Normally the two are the same.
            //
            // Cached on the public Left/RightRay* properties below as well
            // as passed straight into the raycasts, so HandRayDebug can
            // visualize exactly the ray actually being cast, not a
            // recomputed approximation of it.
            playerHandVisuals.GetLeftHandPose(out Vector3 leftPosition, out Quaternion leftRotation);
            LeftRayOrigin = leftPosition;
            LeftRayDirection = leftRotation * _leftLocalRayDirection;

            playerHandVisuals.GetRightHandPose(out Vector3 rightPosition, out Quaternion rightRotation);
            RightRayOrigin = rightPosition;
            RightRayDirection = rightRotation * _rightLocalRayDirection;

            LeftTarget = RaycastForTarget(LeftRayOrigin, LeftRayDirection, out Vector3 leftPoint);
            RightTarget = RaycastForTarget(RightRayOrigin, RightRayDirection, out Vector3 rightPoint);

            LeftTargetPoint = leftPoint;
            RightTargetPoint = rightPoint;
        }

        /// <summary>
        /// Shows each hand's reticle where its ray hit a target this frame,
        /// or hides it if the ray missed - or if that hand is holding
        /// something (its visual is snapped onto a ledge, rung, rope, ...):
        /// the hand is already on it, so a marker saying "you can grab this"
        /// is just noise.
        ///
        /// Separate from Tick() so PlayerController can run it after grabs
        /// have been handled and the hand visuals placed - run inside Tick()
        /// (before climbing), it would read last frame's grab state and show
        /// the reticle for one frame after every grab.
        /// </summary>
        public void TickReticles()
        {
            bool leftHolding = playerHandVisuals.LeftVisualSnap.IsSnapped;
            bool rightHolding = playerHandVisuals.RightVisualSnap.IsSnapped;

            // Read once for both reticles - HeadPosition is a call into the
            // engine each time, and the head doesn't move between the two.
            Vector3 headPosition = playerTracking.HeadPosition;

            leftReticle.Tick(LeftTarget is not null && !leftHolding, LeftTargetPoint, headPosition);
            rightReticle.Tick(RightTarget is not null && !rightHolding, RightTargetPoint, headPosition);
        }

        /// <summary>
        /// Casts a ray from origin in direction and returns whichever
        /// IHandTarget it hits first (with point set to where the ray hit
        /// it), or null if it hits nothing (or hits something that isn't
        /// registered as an IHandTarget) - point is undefined in that case.
        ///
        /// Looks the hit Collider up in HandTargetRegistry rather than
        /// calling hit.collider.GetComponent<IHandTarget>() - GetComponent
        /// with an interface type has to walk every component on the hit
        /// GameObject checking each one's type, since Unity's fast native
        /// per-type lookup only works for concrete Component types. That
        /// runs up to twice a frame here, so a plain dictionary lookup
        /// against IHandTargets that self-register on enable is cheaper.
        /// </summary>
        private IHandTarget RaycastForTarget(Vector3 origin, Vector3 direction, out Vector3 point)
        {
            bool rayHit = Physics.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                rayLength,
                interactableLayers,
                QueryTriggerInteraction.Collide);

            point = rayHit ? hit.point : default;
            return rayHit ? HandTargetRegistry.Find(hit.collider) : null;
        }
    }
}
