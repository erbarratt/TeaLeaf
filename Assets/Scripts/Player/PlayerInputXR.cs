using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>
    /// The single source of truth for controller input. Every reference
    /// below points at an action in the project-owned, project-wide
    /// InputSystem_Actions asset (Player map) - never at XRI's sample
    /// "XRI Default Input Actions", which a package update could overwrite
    /// and whose bindings carry XRI-specific interactions (see turnAction).
    /// The Player map holds an action for every Quest controller input:
    /// function-named ones for inputs gameplay reads, and button-named
    /// placeholders (ButtonX, ButtonY, Menu, RightStickClick) for unused
    /// buttons, to be renamed when they get a job.
    /// </summary>
    public class PlayerInputXR : MonoBehaviour
    {
        [Header("Handedness")]
        // Which way round the things worn on the hands are. Off (a
        // right-handed player): the lockpicks on the back of the left
        // hand, the crossbow on the back of the right. On (left-handed):
        // the other way round. Read once, at startup, by the systems that
        // put things on the hands. A player setting, like
        // PlayerLocomotion's useSmoothTurn.
        [SerializeField] private bool leftHanded;

        [Header("Left Hand")]
        // Player/LeftGrip and Player/LeftTrigger - analogue 0-1.
        [SerializeField] private InputActionReference leftGrip;
        [SerializeField] private InputActionReference leftTrigger;

        [Header("Right Hand")]
        // Player/RightGrip and Player/RightTrigger - analogue 0-1.
        [SerializeField] private InputActionReference rightGrip;
        [SerializeField] private InputActionReference rightTrigger;

        [Header("Locomotion")]
        // Reference to the movement thumbstick action - Player/Move (left
        // stick).
        [SerializeField] private InputActionReference moveAction;

        // Reference to the turning thumbstick action - the project-wide
        // InputSystem_Actions Player/Turn (right stick, no interactions).
        // Not XRI's Turn or Snap Turn: their bindings carry Sector
        // interactions that only report a value when the stick is pushed
        // straight from centre into the left/right sector, and read (0, 0)
        // otherwise - so pushing forward first and then sweeping round to
        // the side never turned, and the stick's Y (used to trigger a
        // mantle) never came through at all.
        [SerializeField] private InputActionReference turnAction;

        [Header("Stance")]
        // Reference to the crouch action. No controller button is bound to
        // it: on a controller, crouch is the right stick pushed down (see
        // crouchStickThreshold). The action still gives the keyboard's
        // crouch key. A single press either way, since crouch is a toggle
        // - see PlayerLocomotion.
        [SerializeField] private InputActionReference crouchAction;

        // How far down the right stick must be pushed to toggle crouch
        // (0-1), and how far it must come back up before another push
        // counts. Two values with a gap between them, so a stick held
        // near one point can't toggle over and over.
        [SerializeField, Range(0.1f, 1f)] private float crouchStickThreshold = 0.7f;
        [SerializeField, Range(0f, 1f)] private float crouchStickRearm = 0.4f;

        // Reference to the sprint button action (left stick click). Read
        // as a single press, since sprint is a click-to-toggle that ends
        // by itself - see PlayerLocomotion.UpdateSprint().
        [SerializeField] private InputActionReference sprintAction;

        // Reference to the jump button action (right A). A single press -
        // PlayerLocomotion buffers it briefly so an early press still jumps
        // on landing.
        [SerializeField] private InputActionReference jumpAction;

        // An action found by name in the Player map (the map the actions
        // above are in), so the scene needs no wiring for it: the
        // universal cancel button (right B).
        private const string CancelActionName = "Cancel";

        // The InputActions behind the references above, resolved once in
        // OnEnable(). InputActionReference.action is a property that does a
        // lookup (and a null/validity check) on every access - reading
        // through it 9 times a frame is wasted work when the action it
        // points at never changes while playing.
        private InputAction _leftGrip;
        private InputAction _leftTrigger;
        private InputAction _rightGrip;
        private InputAction _rightTrigger;
        private InputAction _move;
        private InputAction _turn;
        private InputAction _crouch;
        private InputAction _sprint;
        private InputAction _jump;

        // Null if the Player map has no action of that name: then its
        // property is simply never true.
        private InputAction _cancel;

        // True from the right stick being pushed down far enough to toggle
        // crouch until it has come back up past crouchStickRearm.
        private bool _isCrouchStickDown;

        // Time.frameCount of the last Tick(), so a second call in the same
        // frame (PlayerController's explicit one plus this class' own
        // Update() fallback) returns straight away instead of reading
        // every action again.
        private int _lastTickFrame = -1;

        // All properties below are cached once per frame in Tick() rather
        // than reading the Input System live on every access - several
        // systems (PlayerLocomotion, PlayerClimbing, PlayerHandAnimation)
        // read the same value more than once per frame, and ReadValue<T>()
        // isn't free to call repeatedly.

        public float LeftGrip { get; private set; }
        public float LeftTrigger { get; private set; }

        public float RightGrip { get; private set; }
        public float RightTrigger { get; private set; }

        public bool IsLeftGrabbing => LeftGrip > 0.5f;
        public bool IsRightGrabbing => RightGrip > 0.5f;

        public bool IsLeftUsing => LeftTrigger > 0.5f;
        public bool IsRightUsing => RightTrigger > 0.5f;

        // Left thumbstick movement vector.
        // X = left/right strafe.
        // Y = forward/backward movement.
        public Vector2 MoveAxis { get; private set; }

        // Right thumbstick turning vector.
        // X = horizontal turning.
        // Y = up starts a mantle (PlayerMantling), down toggles crouch
        // (CrouchPressed).
        public Vector2 TurnAxis { get; private set; }

        // True for exactly one frame when crouch is asked for: the right
        // stick pushed down, or the keyboard's crouch key.
        public bool CrouchPressed { get; private set; }

        // True for exactly one frame when the sprint button is pressed.
        public bool SprintPressed { get; private set; }

        // True for exactly one frame when the jump button is pressed.
        public bool JumpPressed { get; private set; }

        // True for exactly one frame when the cancel button (right B) is
        // pressed: the one button that backs out of whatever is under way
        // (an aimed throw, for now).
        public bool CancelPressed { get; private set; }

        // Whether the player is left-handed: see leftHanded.
        public bool IsLeftHanded => leftHanded;

        /// <summary>
        /// Makes sure every action this class reads is enabled. Unity enables
        /// the project-wide InputSystem_Actions at startup, but this class
        /// enables what it needs itself rather than relying on that (or on
        /// which asset a reference happens to point at). Enabling an
        /// already-enabled action does nothing.
        /// </summary>
        private void OnEnable()
        {
            _leftGrip = leftGrip.action;
            _leftTrigger = leftTrigger.action;
            _rightGrip = rightGrip.action;
            _rightTrigger = rightTrigger.action;
            _move = moveAction.action;
            _turn = turnAction.action;
            _crouch = crouchAction.action;
            _sprint = sprintAction.action;
            _jump = jumpAction.action;

            _leftGrip.Enable();
            _leftTrigger.Enable();
            _rightGrip.Enable();
            _rightTrigger.Enable();
            _move.Enable();
            _turn.Enable();
            _crouch.Enable();
            _sprint.Enable();
            _jump.Enable();

            _cancel = FindByName(CancelActionName);
        }

        /// <summary>
        /// Finds an action by name in the map the wired actions are in,
        /// and enables it. Null, with a warning, if there is none.
        /// </summary>
        private InputAction FindByName(string actionName)
        {
            InputAction action = _leftGrip.actionMap?.FindAction(actionName);

            if (action != null) {
                action.Enable();
            } else {
                Debug.LogWarning($"PlayerInputXR: no '{actionName}' action found in the Player map, so that button does nothing.", this);
            }

            return action;
        }

        private void Update()
        {
            // Self-sufficient fallback for anything that reads this class
            // without explicitly calling Tick() itself - e.g. the
            // standalone Debug/ input test scripts, which are meant to work
            // without a full PlayerController set up. With a PlayerController
            // present, whichever of the two calls runs first this frame does
            // the reading and the other returns immediately - see Tick().
            Tick();
        }

        /// <summary>
        /// Caches this frame's raw input values. Called explicitly, and
        /// first, by PlayerController.Update() - before anything else reads
        /// this frame's input - so every Tick()-sequenced system
        /// (PlayerLocomotion, PlayerClimbing, PlayerHandInteraction,
        /// PlayerHandAnimation) is guaranteed fresh values regardless of
        /// Unity's own (unspecified) Update() order between this component
        /// and PlayerController.
        ///
        /// Only reads once per frame: the Input System's values don't change
        /// between Update() calls within a frame, so a second call would just
        /// cache identical values again.
        /// </summary>
        public void Tick()
        {
            int frame = Time.frameCount;

            if (frame == _lastTickFrame) {
                return;
            }

            _lastTickFrame = frame;

            LeftGrip = _leftGrip.ReadValue<float>();
            LeftTrigger = _leftTrigger.ReadValue<float>();

            RightGrip = _rightGrip.ReadValue<float>();
            RightTrigger = _rightTrigger.ReadValue<float>();

            MoveAxis = _move.ReadValue<Vector2>();
            TurnAxis = _turn.ReadValue<Vector2>();

            CrouchPressed = _crouch.WasPressedThisFrame() || ReadCrouchStick();
            SprintPressed = _sprint.WasPressedThisFrame();
            JumpPressed = _jump.WasPressedThisFrame();

            CancelPressed = _cancel != null && _cancel.WasPressedThisFrame();
        }

        /// <summary>
        /// True on the frame the right stick is pushed down far enough to
        /// toggle crouch. "Down" means mostly down: further down than it is
        /// to either side, so a turn with the stick a little low doesn't
        /// count. The stick has to come back up before it counts again.
        /// </summary>
        private bool ReadCrouchStick()
        {
            Vector2 stick = TurnAxis;

            if (_isCrouchStickDown) {
                if (stick.y > -crouchStickRearm) {
                    _isCrouchStickDown = false;
                }

                return false;
            }

            if (stick.y < -crouchStickThreshold && -stick.y > Mathf.Abs(stick.x)) {
                _isCrouchStickDown = true;
                return true;
            }

            return false;
        }
    }
}