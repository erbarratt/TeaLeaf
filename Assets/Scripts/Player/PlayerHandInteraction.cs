using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// General-purpose hand-pointing interaction. Casts one ray from each
    /// hand every frame and highlights whatever IHighlightable it hits first
    /// (if any). Any object implementing IHighlightable is supported
    /// without this class needing to know about it specifically.
    ///
    /// This class does not run its own Update(). Instead PlayerLocomotion
    /// calls Tick() explicitly once per frame, the same way it already
    /// sequences PlayerClimbing.Tick() - see PlayerClimbing's class comment
    /// for why.
    /// </summary>
    public class PlayerHandInteraction : MonoBehaviour
    {
        [SerializeField] private PlayerTracking playerTracking;

        [Header("Hand Rays")]

        // How far each hand's ray reaches, in metres. Shared by both hands -
        // if one hand ever needs a different reach, split this into
        // leftHandRayLength/rightHandRayLength.
        [SerializeField] private float rayLength = 0.5f;

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
        // hits an IHighlightable - see HandRayReticle.
        [SerializeField] private HandRayReticle leftReticle;
        [SerializeField] private HandRayReticle rightReticle;

        // Whatever each hand's ray is currently hitting, or null. Tracked
        // separately per hand - rather than a single "currently highlighted"
        // field like the old head-raycast version - because both hands can
        // now highlight different objects (or the same one) at once.
        private IHighlightable _leftHighlighted;
        private IHighlightable _rightHighlighted;

        /// Whatever the left hand's ray is currently pointing at, or null.
        /// Exposed so other systems (e.g. a future generic "interact"
        /// button) can act on whatever the hand is aimed at.
        public IHighlightable LeftTarget => _leftHighlighted;

        /// Whatever the right hand's ray is currently pointing at, or null.
        public IHighlightable RightTarget => _rightHighlighted;

        /// <summary>
        /// Casts both hand rays, updates highlighting for whatever they hit,
        /// and moves each hand's reticle to wherever its ray currently
        /// lands (hiding it if the ray isn't hitting an IHighlightable).
        /// </summary>
        public void Tick()
        {
            IHighlightable leftHit = RaycastForHighlightable(
                playerTracking.LeftHandPosition,
                RayDirection(playerTracking.LeftHand, leftHandRayAngleOffset),
                out Vector3 leftPoint);

            IHighlightable rightHit = RaycastForHighlightable(
                playerTracking.RightHandPosition,
                RayDirection(playerTracking.RightHand, rightHandRayAngleOffset),
                out Vector3 rightPoint);

            // Both hits are computed above before either hand's highlight
            // state is updated, so each UpdateHighlighted() call below can
            // check what the OTHER hand is pointing at this frame - not what
            // it was highlighting last frame - to avoid un-highlighting an
            // edge that's still targeted by the other hand.
            UpdateHighlighted(ref _leftHighlighted, leftHit, rightHit);
            UpdateHighlighted(ref _rightHighlighted, rightHit, leftHit);

            leftReticle.Tick(leftHit is not null, leftPoint, playerTracking.HeadPosition);
            rightReticle.Tick(rightHit is not null, rightPoint, playerTracking.HeadPosition);
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
        /// IHighlightable it hits first (with point set to where the ray hit
        /// it), or null if it hits nothing (or hits something that isn't
        /// registered as an IHighlightable) - point is undefined in that case.
        ///
        /// Looks the hit Collider up in HighlightableRegistry rather than
        /// calling hit.collider.GetComponent<IHighlightable>() - GetComponent
        /// with an interface type has to walk every component on the hit
        /// GameObject checking each one's type, since Unity's fast native
        /// per-type lookup only works for concrete Component types. That
        /// runs up to twice a frame here, so a plain dictionary lookup
        /// against IHighlightables that self-register on enable is cheaper.
        /// </summary>
        private IHighlightable RaycastForHighlightable(Vector3 origin, Vector3 direction, out Vector3 point)
        {
            bool rayHit = Physics.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                rayLength,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            point = rayHit ? hit.point : default;
            return rayHit ? HighlightableRegistry.Find(hit.collider) : null;
        }

        /// <summary>
        /// Un-highlights whatever this hand was highlighting last frame (if
        /// it's changed and isn't still targeted by the other hand),
        /// highlights whatever it's hitting now, and stores the new hit for
        /// next frame. Returns immediately if the target hasn't changed, so
        /// SetHighlighted() is only ever called on an actual transition, not
        /// every frame a ray happens to still be resting on the same edge -
        /// touching a Renderer's material every frame forces the SRP
        /// Batcher to rebuild that draw call's GPU state instead of reusing
        /// its cached batch, which is exactly the kind of redundant per-
        /// frame cost that shows up as stutter while the player is also
        /// moving.
        /// </summary>
        private static void UpdateHighlighted(
            ref IHighlightable highlighted,
            IHighlightable newHit,
            IHighlightable otherHandHit)
        {
            if (ReferenceEquals(highlighted, newHit)) {
                return;
            }

            if (highlighted is not null && !ReferenceEquals(highlighted, otherHandHit)) {
                highlighted.SetHighlighted(false);
            }

            if (newHit is not null) {
                newHit.SetHighlighted(true);
            }

            highlighted = newHit;
        }
    }
}
