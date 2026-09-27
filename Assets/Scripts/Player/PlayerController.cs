using UnityEngine;

namespace Player
{
    /// <summary>
    /// The player's "tick orchestrator" - the only player script with an
    /// Update(). Every other player system exposes Tick()-style methods
    /// instead, and this class calls them in one fixed, readable order each
    /// frame. Unity doesn't guarantee the order different components'
    /// Update() calls run in, so leaving each system to its own Update()
    /// would let e.g. hand animation read last frame's grab state on some
    /// frames and this frame's on others.
    ///
    /// Also owns the single characterController.Move() call per frame:
    /// every movement-contributing system adds to _frameMovement, then it's
    /// applied once at the end. Two Move() calls in one frame would each do
    /// their own collision pass and could disagree with each other.
    ///
    /// Deliberately contains no gameplay logic of its own - only sequencing
    /// and wiring between systems. If a decision needs making (should the
    /// player be able to jump right now?), it belongs in the system that
    /// owns that behaviour, not here.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerLocomotion playerLocomotion;
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [SerializeField] private PlayerClimbing playerClimbing;
        [SerializeField] private PlayerHandAnimation playerHandAnimation;
        [SerializeField] private CharacterController characterController;

        // The rig root that characterController.Move() displaces. Read
        // before and after Move() to measure how far the player actually
        // moved, which PlayerClimbing needs - see ReportAppliedMovement().
        [SerializeField] private Transform playerTransform;

        // Everything the player should move by this frame, summed from each
        // contributing system, then applied in one Move() call. Zeroed at
        // the start of every frame.
        private Vector3 _frameMovement;

        /// <summary>
        /// Editor-only convenience: Unity calls Reset() when the component is
        /// first added (or via the Inspector's Reset menu item), so every
        /// reference below is filled in automatically. Body systems live on
        /// the Player root alongside this component; hand systems live on
        /// the Hands child (under Camera Offset), hence GetComponentInChildren
        /// for those. Never runs in a build, so it costs nothing at runtime.
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponent<PlayerInputXR>();
            playerLocomotion = GetComponent<PlayerLocomotion>();
            playerClimbing = GetComponent<PlayerClimbing>();
            characterController = GetComponent<CharacterController>();
            playerTransform = transform;

            playerHandInteraction = GetComponentInChildren<PlayerHandInteraction>();
            playerHandAnimation = GetComponentInChildren<PlayerHandAnimation>();
        }

        private void Update()
        {
            // 1. Input first, so every system below reads this frame's
            // values - see PlayerInputXR.Tick().
            playerInput.Tick();

            // 2. Body shape: re-centre the capsule under the headset and
            // apply crouch height. Before the hand systems, because crouch
            // moves the whole tracked hierarchy (hands included) down.
            playerLocomotion.TickBody();

            // 3. Hand rays and highlighting.
            playerHandInteraction.Tick();

            // 4. Grab/release and this frame's climb movement.
            playerClimbing.Tick();

            // 5. Gather this frame's movement. Climbing and ground movement
            // are exclusive: while climbing, the hands move the player and
            // walking/gravity are suspended.
            bool isClimbing = playerClimbing.IsClimbing;

            _frameMovement = Vector3.zero;
            _frameMovement += playerLocomotion.TickMovement(isClimbing);

            if (isClimbing) {
                _frameMovement += playerClimbing.FrameMovement;
            }

            // Turning always works, even mid-climb.
            playerLocomotion.TickTurning();

            // 6. Apply everything in one Move() call.
            Vector3 positionBeforeMove = playerTransform.position;
            characterController.Move(_frameMovement);

            // How far the player really moved - Move() doesn't always apply
            // the full amount requested, since collisions can absorb or
            // redirect part of it.
            Vector3 appliedMovement = playerTransform.position - positionBeforeMove;

            // Only while climbing - otherwise this frame's actual movement
            // came from walking/gravity, not FrameMovement, and reporting it
            // back would corrupt PlayerClimbing's own tracking. PlayerClimbing
            // needs to know exactly how much of its movement landed to stop
            // the grab point drifting - see ReportAppliedMovement().
            if (isClimbing) {
                playerClimbing.ReportAppliedMovement(appliedMovement);
            }

            // 7. Movement state - after Move(), since it needs this frame's
            // real movement and the isGrounded that Move() just updated.
            playerLocomotion.TickState(isClimbing, appliedMovement);

            // 8. Hand visuals - after Move() and turning, since the visuals
            // are children of the rig: a snap pose placed any earlier would
            // be dragged along by this frame's movement until next frame.
            playerClimbing.TickHandVisuals();

            // 9. Hand animation - last, so the finger snap pose reads this
            // frame's snap weight from step 8. Animators evaluate after all
            // Update() calls anyway, so nothing is lost by running it here.
            playerHandAnimation.Tick();
        }
    }
}
