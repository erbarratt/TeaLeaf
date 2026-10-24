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
    public class PlayerBlackjack : MonoBehaviour
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

        [Header("In The Hand")]

        // Where the blackjack's grip sits in the hand visual's own space
        // (its fingers point along -Y, the palm faces +X, the thumb side
        // is +Z), and how it's turned from lying along the thumb side, in
        // degrees. The right hand visual is the left one mirrored, so the
        // same numbers serve both.
        [SerializeField] private Vector3 inHandPosition = new(0.03f, -0.08f, 0f);
        [SerializeField] private Vector3 inHandRotation = Vector3.zero;

        [Header("Its Shape (greybox)")]

        // The whole length, the handle's thickness, and the head: how
        // long it is and how thick. Metres.
        [SerializeField] private float length = 0.35f;
        [SerializeField] private float handleThickness = 0.025f;
        [SerializeField] private float headLength = 0.12f;
        [SerializeField] private float headThickness = 0.045f;

        [SerializeField] private Color handleColor = new(0.3f, 0.2f, 0.12f, 1f);
        [SerializeField] private Color headColor = new(0.12f, 0.12f, 0.13f, 1f);

        // The least light it's ever shown in, 0-1.
        [SerializeField] private float minLight = 0.35f;

        [Header("Hitting")]

        // How fast the blackjack's head must be moving, in metres a
        // second (measured against the player's body, so walking isn't a
        // swing), for a touch on a guard's head to count as a blow.
        [SerializeField] private float swingSpeed = 1.5f;

        // How near the middle of the blackjack's head a guard's head must
        // come, in metres.
        [SerializeField] private float hitRadius = 0.07f;

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

        // How much of the handle is behind the grip: the hand holds it a
        // little way up from its end.
        private const float GripFraction = 0.2f;

        private bool _hasHaptics;

        // The blackjack, its mesh and its material.
        private Transform _blackjack;
        private Mesh _mesh;
        private Material _material;

        // Whether it's drawn, and which hand draws it (the one without
        // the lockpicks, from handedness at startup).
        private bool _isOut;
        private bool _isInLeftHand;

        // The drawing hand's grip last frame (it's drawn on the frame
        // grip is pressed, not by a hand that arrives gripping), and
        // whether the hand was in place last frame (for the tap).
        private bool _wasGrabbing;
        private bool _wasInPlace;

        // Hitting. The middle of the blackjack's head in its own space;
        // the player's root (speeds are measured in its space); where
        // the head was last frame, in that space, and whether that is
        // known yet; and whether the head is still against the guard it
        // last hit - one touch is one blow.
        private Vector3 _headLocal;
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

            BuildBlackjack();
        }

        private void OnDisable()
        {
            // Null if Awake() hasn't run. And if the scene is being
            // unloaded (a level restart), everything here is on its way
            // out and mustn't be touched.
            if (_blackjack == null || !gameObject.scene.isLoaded) {
                return;
            }

            PutAway();
        }

        private void OnDestroy()
        {
            // A mesh and a material made from code aren't cleaned up with
            // their objects.
            if (_mesh != null) {
                Destroy(_mesh);
            }

            if (_material != null) {
                Destroy(_material);
            }
        }

        /// <summary>
        /// Makes the blackjack, once: a handle and a thicker head as two
        /// boxes in one mesh, lying along its own Z with the grip at its
        /// origin. Kept under this object, switched off, until it's
        /// drawn - not on a hand visual, so the ghost hands (which copy
        /// the visuals as the game starts) never get one. No collider:
        /// it's only to look at, for now.
        /// </summary>
        private void BuildBlackjack()
        {
            float handleLength = Mathf.Max(length - headLength, 0.01f);
            float back = -length * GripFraction;

            LockMeshBuilder builder = new();
            builder.Box(new Vector3(0f, 0f, back + handleLength * 0.5f), new Vector3(handleThickness, handleThickness, handleLength), Quaternion.identity, handleColor);
            builder.Box(new Vector3(0f, 0f, back + handleLength + headLength * 0.5f), new Vector3(headThickness, headThickness, headLength), Quaternion.identity, headColor);
            _mesh = builder.ToMesh("Blackjack");
            _headLocal = new Vector3(0f, 0f, back + handleLength + headLength * 0.5f);
            _material = LockMeshBuilder.CreateMaterial(false, minLight);

            GameObject blackjack = new("Blackjack");
            blackjack.transform.SetParent(transform, false);
            blackjack.AddComponent<MeshFilter>().sharedMesh = _mesh;

            MeshRenderer blackjackRenderer = blackjack.AddComponent<MeshRenderer>();
            blackjackRenderer.sharedMaterial = _material;
            blackjackRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            blackjackRenderer.receiveShadows = false;

            blackjack.SetActive(false);
            _blackjack = blackjack.transform;
        }

        /// <summary>
        /// Drawing the blackjack and putting it away. Called by
        /// PlayerController before the grab systems.
        /// </summary>
        public void Tick()
        {
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

            // A child of the hand visual, so nothing places it per frame.
            // Scale 1 under the mirrored right hand: it's the same shape
            // mirrored.
            _blackjack.SetParent(visual, false);
            _blackjack.SetLocalPositionAndRotation(inHandPosition, Quaternion.Euler(inHandRotation));
            _blackjack.localScale = Vector3.one;
            _blackjack.gameObject.SetActive(true);

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
        /// player). One small overlap check a frame, on the guards' head
        /// layer only.
        /// </summary>
        private void TickSwing()
        {
            Vector3 head = _blackjack.TransformPoint(_headLocal);

            // Measured in the player root's space, so walking, turning
            // and climbing aren't a swing: only the arm's movement is.
            Vector3 headPoint = _playerRoot.InverseTransformPoint(head);
            float speed = 0f;

            if (_hasLastHeadPoint) {
                speed = (headPoint - _lastHeadPoint).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            }

            _lastHeadPoint = headPoint;
            _hasLastHeadPoint = true;

            int count = Physics.OverlapSphereNonAlloc(head, hitRadius, _hits, guardHeadLayers, QueryTriggerInteraction.Collide);

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
        /// Puts the blackjack away: back under this object, switched off.
        /// </summary>
        private void PutAway()
        {
            _isOut = false;
            _blackjack.gameObject.SetActive(false);
            _blackjack.SetParent(transform, false);
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
