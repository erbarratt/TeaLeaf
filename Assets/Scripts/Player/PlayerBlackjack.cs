using Core;
using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The blackjack: a short club, drawn from over a shoulder. The hand
    /// without the lockpicks (the right, for a right-handed player),
    /// empty and reaching over its own shoulder, draws it by pressing
    /// grip, and holds it for as long as grip is held. Letting go puts it
    /// away - it isn't dropped, it's simply gone until it's drawn again.
    /// Like the bow, it burns into being when drawn and burns away where
    /// it was let go of (ToolBurn).
    /// Only that hand draws it; the other shoulder is the bow's.
    ///
    /// Where the shoulder is, is PlayerBodyZones' business. The blackjack
    /// is taken by reaching, not by the hand rays, so the hand feels a
    /// light tap when it's in the right place.
    ///
    /// In hand, a swing that brings its head against a guard's head is a
    /// blow: the guard goes down if it was unaware and struck from
    /// behind (AI.Guard decides).
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// Tick() before the grab systems, so a hand that draws the blackjack
    /// is already busy when they look at what its ray is on.
    /// </summary>
    public class PlayerBlackjack : MonoBehaviour, IDebugDrawable
    {
        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // What each hand is busy with. Found (or made) in Awake() if not
        // wired (it's on this same object).
        [SerializeField] private PlayerHandState playerHandState;

        // Where the shoulders are. Found (or made) in Awake() if not
        // wired (it's on this same object).
        [SerializeField] private PlayerBodyZones bodyZones;

        // Optional: a rig with no haptics still draws it.
        [SerializeField] private PlayerHaptics playerHaptics;

        // Burns the blackjack into being when drawn and away when put
        // away. Optional: found in Awake() if not wired (it's on this same
        // object); none = it appears and vanishes at once.
        [SerializeField] private ToolBurn toolBurn;

        [Header("In The Hand")]

        // The blackjack's model (the leather-wrapped cudgel): lying along
        // its own X with the head at +X, at its real size.
        [SerializeField] private GameObject blackjackModel;

        // Where the hand holds it, from the model's origin, and how the
        // fingers close round it (posed with TeaLeaf > Modify Prefab >
        // Pose with Hand). Empty: the model's origin on the hand's.
        [SerializeField] private HandSnapProfile blackjackProfile;

        [Header("Hitting")]

        // How fast the blackjack's head must be moving, in metres a
        // second (measured against the player's body, so walking isn't a
        // swing), for a touch on a guard's head to count as a blow.
        [SerializeField] private float swingSpeed = 1.5f;

        // The blackjack's head as a capsule, in the blackjack's own space
        // (the model's, with its import turn): the middles of its two
        // ends, and its radius, in metres. Measured from the cudgel's
        // mesh: the head runs from x 0.08 to 0.2 along a line a little
        // off the shaft's, 4cm round at its widest. Re-measure if the
        // model changes.
        [SerializeField] private Vector3 headStart = new(0.12f, 0.038f, -0.015f);
        [SerializeField] private Vector3 headEnd = new(0.16f, 0.038f, -0.015f);
        [SerializeField] private float hitRadius = 0.04f;

        // The layer guards' heads are on. Filled in from the layer name
        // if left empty.
        [SerializeField] private LayerMask guardHeadLayers;

        // The thud of a blow. Optional. Its noise is what other guards
        // hear.
        [SerializeField] private SoundCue hitCue;

        [Header("Haptics")]

        // A blow landing.
        [SerializeField] private float hitAmplitude = 1f;
        [SerializeField] private float hitDuration = 0.1f;

        // A hand arriving where it can draw, and the draw itself.
        [SerializeField] private float reachAmplitude = 0.15f;
        [SerializeField] private float reachDuration = 0.03f;
        [SerializeField] private float drawAmplitude = 0.4f;
        [SerializeField] private float drawDuration = 0.05f;

        private bool _hasHaptics;

        // The blackjack: the model under an object of its own, whose
        // origin and axes are the model's (the profile's grip frame).
        private Transform _blackjack;

        // The model's renderer, and the blackjack as ToolBurn knows it
        // (null without one).
        private Renderer _renderer;
        private ToolBurn.Target _burn;

        // Whether it's drawn, and which hand draws it (the one without
        // the lockpicks, from handedness at startup).
        private bool _isOut;
        private bool _isInLeftHand;

        // The drawing hand's grip last frame (it's drawn on the frame
        // grip is pressed, not by a hand that arrives gripping), and
        // whether the hand was in place last frame (for the tap).
        private bool _wasGrabbing;
        private bool _wasInPlace;

        // Hitting. The player's root (speeds are measured in its space);
        // where the middle of the head was last frame, in that space, and
        // whether that is known yet; and whether the head is still
        // against the guard it last hit - one touch is one blow.
        private Transform _playerRoot;
        private Vector3 _lastHeadPoint;
        private bool _hasLastHeadPoint;
        private bool _isTouching;

        // What the blow's overlap check finds. Made once and reused.
        private static readonly Collider[] _hits = new Collider[2];

        /// True while the blackjack is in a hand.
        public bool IsOut => _isOut;

        /// Which hand it's in while it's out: always the hand without the
        /// lockpicks.
        public bool IsInLeftHand => _isInLeftHand;

        /// True while that hand holds the blackjack. The other hand
        /// systems leave it alone meanwhile (PlayerHandState).
        public bool IsLeftBusy => _isOut && _isInLeftHand;
        public bool IsRightBusy => _isOut && !_isInLeftHand;

        /// <summary>
        /// Whose finger pose a hand plays for the blackjack in it: its
        /// profile's while that hand holds it, else null. Read by
        /// PlayerHandAnimation each frame.
        /// </summary>
        public HandSnapProfile GetHeldProfile(bool isLeftHand)
        {
            return _isOut && isLeftHand == _isInLeftHand ? blackjackProfile : null;
        }

        /// <summary>
        /// Editor-only: fills in the references when the component is
        /// added (on the Hands object; the body systems are on the Player
        /// root above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerTracking = GetComponentInParent<PlayerTracking>();
            playerHaptics = GetComponentInParent<PlayerHaptics>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
            toolBurn = GetComponent<ToolBurn>();
        }

        private void Awake()
        {
            if (playerInput == null) {
                playerInput = GetComponentInParent<PlayerInputXR>();
            }

            if (playerTracking == null) {
                playerTracking = GetComponentInParent<PlayerTracking>();
            }

            if (playerHandVisuals == null) {
                playerHandVisuals = GetComponent<PlayerHandVisuals>();
            }

            if (playerHandState == null) {
                playerHandState = PlayerHandState.GetOrAdd(playerHandVisuals);
            }

            if (bodyZones == null) {
                bodyZones = PlayerBodyZones.GetOrAdd(playerHandVisuals);
            }

            // A right-handed player wears the lockpicks on the left hand,
            // so draws the blackjack with the right; a left-handed one the
            // other way round.
            _isInLeftHand = playerInput.IsLeftHanded;

            if (playerHaptics == null) {
                playerHaptics = GetComponentInParent<PlayerHaptics>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasHaptics = playerHaptics != null;
            _playerRoot = playerInput.transform;

            if (guardHeadLayers.value == 0) {
                guardHeadLayers = LayerMask.GetMask("GuardHead");
            }

            if (toolBurn == null) {
                toolBurn = GetComponent<ToolBurn>();
            }

            BuildBlackjack();
        }

        /// <summary>
        /// Registers the blackjack with ToolBurn. Start() rather than
        /// Awake(): ToolBurn finds its shader in its own Awake(), and every
        /// Awake() has run before the first Start().
        /// </summary>
        private void Start()
        {
            if (toolBurn != null && _renderer != null) {
                _burn = toolBurn.Register(_renderer);
            }
        }

        private void OnEnable()
        {
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            DebugDrawRegistry.Unregister(this);

            // Null if Awake() hasn't run. And if the scene is being
            // unloaded (a level restart), everything here is on its way
            // out and mustn't be touched.
            if (_blackjack == null || !gameObject.scene.isLoaded) {
                return;
            }

            PutAway();
        }

        /// <summary>
        /// Makes the blackjack, once: the model under an object of its
        /// own, so the model keeps its import turn while that object is
        /// placed in the hand. Kept under this object, switched off, until
        /// it's drawn - not on a hand visual, so the ghost hands (which
        /// copy the visuals as the game starts) never get one. No
        /// Collider: a blow is an overlap check of the head's capsule
        /// (headStart, headEnd, hitRadius).
        /// </summary>
        private void BuildBlackjack()
        {
            if (blackjackModel == null) {
                Debug.LogWarning("PlayerBlackjack: no blackjack model set, so there is no blackjack.", this);
                return;
            }

            GameObject blackjack = new("Blackjack");
            GameObject model = Instantiate(blackjackModel, blackjack.transform, false);

            foreach (Renderer modelRenderer in model.GetComponentsInChildren<Renderer>()) {
                modelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                modelRenderer.receiveShadows = false;

                // The first is the one that burns (the cudgel has only
                // the one).
                if (_renderer == null) {
                    _renderer = modelRenderer;
                }
            }

            blackjack.transform.SetParent(transform, false);
            blackjack.SetActive(false);
            _blackjack = blackjack.transform;
        }

        /// <summary>
        /// Drawing the blackjack and putting it away. Called by
        /// PlayerController before the grab systems.
        /// </summary>
        public void Tick()
        {
            if (_blackjack == null) {
                return;
            }

            bool isGrabbing = _isInLeftHand ? playerInput.IsLeftGrabbing : playerInput.IsRightGrabbing;

            if (_isOut) {
                // Held for as long as grip is.
                if (!isGrabbing) {
                    PutAway();
                } else {
                    TickSwing();
                }
            } else {
                TickReach(isGrabbing);
            }

            _wasGrabbing = isGrabbing;
        }

        /// <summary>
        /// The drawing hand, while the blackjack is away: a tap as it
        /// comes into place over its shoulder, free, and the draw on the
        /// frame its grip is pressed there.
        /// </summary>
        private void TickReach(bool isGrabbing)
        {
            bool isInPlace = !playerHandState.IsBusyExcept(_isInLeftHand, HandUse.Blackjack)
                && bodyZones.IsAtShoulder(_isInLeftHand);

            if (isInPlace && !_wasInPlace) {
                Pulse(_isInLeftHand, reachAmplitude, reachDuration);
            }

            _wasInPlace = isInPlace;

            if (!isInPlace || !isGrabbing || _wasGrabbing) {
                return;
            }

            Transform visual = _isInLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;

            // A child of the hand visual where the profile puts it, so
            // nothing places it per frame; never drawn mirrored.
            HandSnapProfile.PlaceInHand(blackjackProfile, _isInLeftHand, _blackjack, visual, transform);
            _blackjack.gameObject.SetActive(true);

            if (toolBurn != null) {
                toolBurn.BurnIn(_burn);
            }

            _isOut = true;
            _hasLastHeadPoint = false;
            _isTouching = false;
            _wasInPlace = false;
            Pulse(_isInLeftHand, drawAmplitude, drawDuration);
        }

        /// <summary>
        /// The blackjack in hand: is its head moving fast enough, and
        /// against a guard's head? Then it's a blow, and the guard says
        /// what comes of it (AI.Guard.TryKnockOut(): down if it was
        /// unaware and struck from behind, otherwise it turns on the
        /// player). One small capsule overlap check a frame, round the
        /// head, on the guards' head layer only.
        /// </summary>
        private void TickSwing()
        {
            Vector3 start = _blackjack.TransformPoint(headStart);
            Vector3 end = _blackjack.TransformPoint(headEnd);
            Vector3 head = (start + end) * 0.5f;

            // Measured in the player root's space, so walking, turning
            // and climbing aren't a swing: only the arm's movement is.
            Vector3 headPoint = _playerRoot.InverseTransformPoint(head);
            float speed = 0f;

            if (_hasLastHeadPoint) {
                speed = (headPoint - _lastHeadPoint).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            }

            _lastHeadPoint = headPoint;
            _hasLastHeadPoint = true;

            int count = Physics.OverlapCapsuleNonAlloc(start, end, hitRadius, _hits, guardHeadLayers, QueryTriggerInteraction.Collide);

            if (count == 0) {
                _isTouching = false;
                return;
            }

            // Still against the head it hit, or only resting on one.
            if (_isTouching || speed < swingSpeed) {
                return;
            }

            AI.Guard guard = AI.Guard.FindByHead(_hits[0]);

            if (guard == null) {
                return;
            }

            _isTouching = true;
            guard.TryKnockOut(playerTracking.HeadPosition);
            Pulse(_isInLeftHand, hitAmplitude, hitDuration);

            if (hitCue == null) {
                return;
            }

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(hitCue, head, _playerRoot);
            } else {
                hitCue.EmitNoise(head, _playerRoot);
            }
        }

        /// <summary>
        /// Puts the blackjack away: back under this object, switched off,
        /// leaving a copy burning away where it was let go of (ToolBurn
        /// does nothing if it wasn't showing).
        /// </summary>
        private void PutAway()
        {
            if (toolBurn != null) {
                toolBurn.BurnAway(_burn);
            }

            _isOut = false;
            _blackjack.gameObject.SetActive(false);
            _blackjack.SetParent(transform, false);
        }

        /// <summary>
        /// Editor-only: the hit capsule in the Scene view while playing.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// While the blackjack is in hand: the capsule a blow is checked
        /// with, round its head - orange, red while it's against the head
        /// it last hit.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!_isOut || _blackjack == null) {
                return;
            }

            lines.Color = _isTouching ? Color.red : new Color(1f, 0.5f, 0f);
            lines.WireCapsule(_blackjack.TransformPoint(headStart), _blackjack.TransformPoint(headEnd), hitRadius);
        }

        /// <summary>
        /// Buzzes one controller, if the rig has haptics.
        /// </summary>
        private void Pulse(bool isLeftHand, float amplitude, float duration)
        {
            if (_hasHaptics) {
                playerHaptics.Pulse(isLeftHand, amplitude, duration);
            }
        }
    }
}
