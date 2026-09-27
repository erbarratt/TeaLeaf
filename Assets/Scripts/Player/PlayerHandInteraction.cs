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
    /// calls Tick() explicitly once per frame - see PlayerController's class
    /// comment for why.
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

        /// <summary>
        /// Casts both hand rays, records whatever they hit, and moves each
        /// hand's reticle to wherever its ray currently lands (hiding it if
        /// the ray isn't hitting an IHandTarget).
        /// </summary>
        public void Tick()
        {
            // Cached on the public Left/RightRay* properties below as well
            // as passed straight into the raycasts, so HandRayDebug can
            // visualize exactly the ray actually being cast, not a
            // recomputed approximation of it.
            LeftRayOrigin = playerTracking.LeftHandPosition;
            LeftRayDirection = RayDirection(playerTracking.LeftHand, leftHandRayAngleOffset);

            RightRayOrigin = playerTracking.RightHandPosition;
            RightRayDirection = RayDirection(playerTracking.RightHand, rightHandRayAngleOffset);

            LeftTarget = RaycastForTarget(LeftRayOrigin, LeftRayDirection, out Vector3 leftPoint);
            RightTarget = RaycastForTarget(RightRayOrigin, RightRayDirection, out Vector3 rightPoint);

            LeftTargetPoint = leftPoint;
            RightTargetPoint = rightPoint;

            leftReticle.Tick(LeftTarget is not null, leftPoint, playerTracking.HeadPosition);
            rightReticle.Tick(RightTarget is not null, rightPoint, playerTracking.HeadPosition);
        }

        /// <summary>
        /// Rotates a hand's forward direction by its configured angle
        /// offset, giving the direction the hand's ray should be cast in.
        /// </summary>
        private static Vector3 RayDirection(Transform hand, Vector3 angleOffset)
        {
            return hand.rotation * Quaternion.Euler(angleOffset) * Vector3.forward;
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
