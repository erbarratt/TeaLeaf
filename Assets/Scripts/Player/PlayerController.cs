using Core;
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
    /// their own collision pass and could disagree with each other. The one
    /// exception is a mantle: PlayerMantling positions the rig directly
    /// (CharacterController disabled) and this class skips Move() entirely
    /// until it finishes.
    ///
    /// Deliberately contains no gameplay logic of its own - only sequencing
    /// and wiring between systems. If a decision needs making (should the
    /// player be able to jump right now?), it belongs in the system that
    /// owns that behaviour, not here.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerLocomotion playerLocomotion;
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [SerializeField] private PlayerClimbing playerClimbing;
        [SerializeField] private PlayerMantling playerMantling;
        [SerializeField] private PlayerFootsteps playerFootsteps;
        [SerializeField] private PlayerVisibility playerVisibility;
        [SerializeField] private PlayerHandHolding playerHandHolding;
        [SerializeField] private PlayerHandThrowing playerHandThrowing;
        [SerializeField] private PlayerHandDoors playerHandDoors;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;
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

        // The level manager subscribed to in Start(), kept so the same one
        // is unsubscribed from in OnDestroy(). Null if the scene has none.
        private LevelManager _levelManager;

        // True once the level has ended (caught or won): the body stays
        // where it is while the view fades out - see Update().
        private bool _isLevelOver;

        // Whether the rig has a PlayerHandHolding, and a PlayerHandThrowing
        // (which needs the holding) - see Awake().
        private bool _hasHandHolding;
        private bool _hasHandThrowing;

        // Whether the rig has a PlayerHandDoors - see Awake().
        private bool _hasHandDoors;

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
            playerTracking = GetComponent<PlayerTracking>();
            playerLocomotion = GetComponent<PlayerLocomotion>();
            playerClimbing = GetComponent<PlayerClimbing>();
            playerMantling = GetComponent<PlayerMantling>();
            playerFootsteps = GetComponent<PlayerFootsteps>();
            playerVisibility = GetComponent<PlayerVisibility>();
            characterController = GetComponent<CharacterController>();
            playerTransform = transform;

            playerHandInteraction = GetComponentInChildren<PlayerHandInteraction>();
            playerHandHolding = GetComponentInChildren<PlayerHandHolding>();
            playerHandThrowing = GetComponentInChildren<PlayerHandThrowing>();
            playerHandDoors = GetComponentInChildren<PlayerHandDoors>();
            playerHandVisuals = GetComponentInChildren<PlayerHandVisuals>();
            playerHandAnimation = GetComponentInChildren<PlayerHandAnimation>();
        }

        /// <summary>
        /// Fills in playerTracking, playerFootsteps, playerVisibility and
        /// playerHandHolding if the scene hasn't got them wired: the fields
        /// were added after this component was set up in Main.unity, and
        /// Reset() only runs when a component is first added. One lookup
        /// each at startup (the first three on this object, holding on the
        /// Hands object below it).
        /// </summary>
        private void Awake()
        {
            if (playerTracking == null) {
                playerTracking = GetComponent<PlayerTracking>();
            }

            if (playerFootsteps == null) {
                playerFootsteps = GetComponent<PlayerFootsteps>();
            }

            if (playerVisibility == null) {
                playerVisibility = GetComponent<PlayerVisibility>();
            }

            if (playerHandHolding == null) {
                playerHandHolding = GetComponentInChildren<PlayerHandHolding>();
            }

            // Looked up once: Update() tests this plain bool rather than
            // asking Unity whether the component exists several times a
            // frame. Carrying is optional, like footsteps.
            _hasHandHolding = playerHandHolding != null;

            if (playerHandThrowing == null) {
                playerHandThrowing = GetComponentInChildren<PlayerHandThrowing>();
            }

            // Aimed throwing is optional too, and is nothing without a
            // prop to throw.
            _hasHandThrowing = _hasHandHolding && playerHandThrowing != null;

            if (playerHandDoors == null) {
                playerHandDoors = GetComponentInChildren<PlayerHandDoors>();
            }

            // Opening doors is optional as well.
            _hasHandDoors = playerHandDoors != null;
        }

        /// <summary>
        /// Listens for the level ending. Start() rather than Awake():
        /// LevelManager sets Instance in its own Awake(), and every Awake()
        /// has run before the first Start(). A scene with no LevelManager
        /// (a test scene) simply never freezes.
        /// </summary>
        private void Start()
        {
            _levelManager = LevelManager.Instance;

            if (_levelManager != null) {
                _levelManager.StateChanged += OnGameStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (_levelManager != null) {
                _levelManager.StateChanged -= OnGameStateChanged;
            }
        }

        /// <summary>
        /// Called by LevelManager once, when the level ends. Told by an
        /// event rather than asking the manager every frame.
        /// </summary>
        private void OnGameStateChanged(GameState state)
        {
            _isLevelOver = state != GameState.Playing;
        }

        private void Update()
        {
            // 1. Input first, so every system below reads this frame's
            // values - see PlayerInputXR.Tick().
            playerInput.Tick();

            // 1b. Tracking check: if the headset was recentred since last
            // frame, put the view upright and at the right height again -
            // before anything below reads the head or hands.
            playerTracking.Tick();

            // The level has ended (caught or won) and the view is fading
            // out: the body is frozen where it is - no walking, turning,
            // gravity, crouch, grabbing, climbing or mantling, and a mantle
            // or fall in progress just stops. Only the hands carry on, so
            // they still follow the controllers and stop against surfaces
            // (the head is tracked regardless - freezing the view itself
            // would be nauseating). A hand gripping something stays snapped
            // to it, since nothing runs that could release it.
            if (_isLevelOver) {
                playerHandInteraction.Tick();
                TickHandVisuals();
                TickHeldProps(false);
                playerHandInteraction.TickReticles();
                playerHandAnimation.Tick();
                return;
            }

            // 2. Body shape: re-centre the capsule under the headset and
            // apply crouch height. Before the hand systems, because crouch
            // moves the whole tracked hierarchy (hands included) down.
            playerLocomotion.TickBody();

            // 3. Hand rays and targets (reticles are placed later, at 8b).
            playerHandInteraction.Tick();

            // 4. Grab/release and this frame's climb movement - skipped while
            // mantling, since the mantle has already let go of the ledge and
            // nothing may grab or climb until it finishes.
            if (!playerMantling.IsMantling) {
                playerClimbing.Tick();

                // 4a. Picking up and dropping props - after climbing, so it
                // sees this frame's climbing grips (a hand does one or the
                // other). A prop carried into a mantle just stays in hand.
                if (_hasHandHolding) {
                    playerHandHolding.Tick();
                }

                // 4a (continued). Taking and letting go of door handles -
                // last of the three, so it sees this frame's climbing grips
                // and carried props.
                if (_hasHandDoors) {
                    playerHandDoors.Tick();
                }
            }

            // 4b. Mantling - after climbing, so it sees this frame's grips.
            // Detects whether a mantle is possible (arrow), starts one on a
            // stick push, or advances the one in progress.
            playerMantling.Tick();

            // While a mantle is running it owns the whole body: it has
            // already placed the rig itself this frame (with the
            // CharacterController disabled), so skip everything that would
            // move or turn the player - locomotion, turning, Move() - and run
            // only what's still needed: movement state and the hands.
            if (playerMantling.IsMantling) {
                playerLocomotion.TickState(false, Vector3.zero, CollisionFlags.None);

                // A guard can still see someone climbing over a ledge.
                if (playerVisibility != null) {
                    playerVisibility.Tick();
                }

                TickHandVisuals();
                TickHeldProps(false);
                playerHandInteraction.TickReticles();
                playerHandAnimation.Tick();
                return;
            }

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
            // Move() reports which sides of the capsule hit something -
            // PlayerLocomotion uses it to stop momentum pushing into walls
            // and ceilings.
            CollisionFlags collisionFlags = characterController.Move(_frameMovement);

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

            // 7. Collision response + movement state - after Move(), since
            // it needs this frame's real movement, collision flags, and the
            // isGrounded that Move() just updated.
            playerLocomotion.TickState(isClimbing, appliedMovement, collisionFlags);

            // 7b. Footsteps - after TickState(), since a step depends on
            // this frame's movement state and real movement. Optional: a
            // Player without the component is simply silent.
            if (playerFootsteps != null) {
                playerFootsteps.Tick(appliedMovement);
            }

            // 7c. Visibility - after TickState() too: it uses where the
            // body ended up and this frame's movement state. Optional, like
            // footsteps.
            if (playerVisibility != null) {
                playerVisibility.Tick();
            }

            // 8. Hand visuals - after Move() and turning, since the visuals
            // are children of the rig: a snap pose placed any earlier would
            // be dragged along by this frame's movement until next frame.
            TickHandVisuals();

            // 8a. Carried props - attached once the hand visual has reached
            // them, so straight after the visuals; then aiming a throw,
            // which starts its arc from where the prop now is.
            TickHeldProps(true);

            // 8b. Reticles - after grabs (step 4) and hand visuals, so a hand
            // that grabbed something this frame already hides its reticle.
            playerHandInteraction.TickReticles();

            // 9. Hand animation - last, so the finger snap pose reads this
            // frame's snap weight from step 8. Animators evaluate after all
            // Update() calls anyway, so nothing is lost by running it here.
            playerHandAnimation.Tick();
        }

        /// <summary>
        /// Held door handles, then the hand visuals. Called on every path
        /// through Update() - normal, mid-mantle and after the level has
        /// ended. The doors go first: a door follows the hand on its
        /// handle, and the hand visual is then snapped onto the handle
        /// where the door has ended up.
        /// </summary>
        private void TickHandVisuals()
        {
            if (_hasHandDoors) {
                playerHandDoors.TickHeld();
            }

            playerHandVisuals.Tick();
        }

        /// <summary>
        /// Carried props, then aimed throwing. Called straight after
        /// playerHandVisuals.Tick() on every path through Update() -
        /// normal, mid-mantle and after the level has ended. canAim is
        /// false on the last two: no throw is aimed or started then, but
        /// one already leaving the hand still finishes.
        /// </summary>
        private void TickHeldProps(bool canAim)
        {
            if (_hasHandHolding) {
                playerHandHolding.TickHeld();
            }

            if (_hasHandThrowing) {
                playerHandThrowing.Tick(canAim);
            }
        }
    }
}
