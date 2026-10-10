using Core;
using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The blackjack: a short club, drawn from the chest. A hand that is
    /// empty, close to the chest and has its palm towards the chest draws
    /// it by pressing grip, and holds it for as long as grip is held.
    /// Letting go puts it away - it isn't dropped, it's simply gone until
    /// it's drawn again. Either hand can draw it; one at a time.
    ///
    /// The chest is a ball a little below the head, measured with the
    /// head's tilt ignored (so looking down doesn't move it). The
    /// blackjack is taken by reaching, not by the hand rays, so a hand
    /// feels a light tap when it's in the right place.
    ///
    /// This is only the drawing and putting away. Knocking a guard out
    /// with it comes with the guards.
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

        // Optional: a rig with no haptics still draws it.
        [SerializeField] private PlayerHaptics playerHaptics;

        [Header("Drawing It")]

        // The middle of the chest from the head, in metres: to the
        // player's right, up, and ahead - with the head's tilt ignored.
        [SerializeField] private Vector3 chestOffset = new(0f, -0.3f, 0.05f);

        // How close to that point a hand must be, in metres.
        [SerializeField] private float chestRadius = 0.22f;

        // How far the palm may be turned away from pointing straight at
        // the chest, in degrees, and still count as facing it.
        [SerializeField] private float palmAngle = 60f;

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

        [Header("Haptics")]

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

        // Whether it's drawn, and into which hand.
        private bool _isOut;
        private bool _isInLeftHand;

        // Per hand: grip last frame (it's drawn on the frame grip is
        // pressed, not by a hand that arrives gripping), and whether the
        // hand was in place last frame (for the tap).
        private bool _wasLeftGrabbing;
        private bool _wasRightGrabbing;
        private bool _wasLeftInPlace;
        private bool _wasRightInPlace;

        /// True while the blackjack is in a hand.
        public bool IsOut => _isOut;

        /// Which hand it's in, while it's out.
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

            if (playerHaptics == null) {
                playerHaptics = GetComponentInParent<PlayerHaptics>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasHaptics = playerHaptics != null;

            BuildBlackjack();
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
            bool isLeftGrabbing = playerInput.IsLeftGrabbing;
            bool isRightGrabbing = playerInput.IsRightGrabbing;

            if (_isOut) {
                // Held for as long as grip is.
                if (!(_isInLeftHand ? isLeftGrabbing : isRightGrabbing)) {
                    PutAway();
                }
            } else {
                TickReach(true, isLeftGrabbing, _wasLeftGrabbing, ref _wasLeftInPlace);

                if (!_isOut) {
                    TickReach(false, isRightGrabbing, _wasRightGrabbing, ref _wasRightInPlace);
                }
            }

            _wasLeftGrabbing = isLeftGrabbing;
            _wasRightGrabbing = isRightGrabbing;
        }

        /// <summary>
        /// One hand, while the blackjack is away: a tap as it comes into
        /// place at the chest, and the draw on the frame its grip is
        /// pressed there.
        /// </summary>
        private void TickReach(bool isLeftHand, bool isGrabbing, bool wasGrabbing, ref bool wasInPlace)
        {
            bool isInPlace = IsInPlace(isLeftHand);

            if (isInPlace && !wasInPlace) {
                Pulse(isLeftHand, reachAmplitude, reachDuration);
            }

            wasInPlace = isInPlace;

            if (!isInPlace || !isGrabbing || wasGrabbing) {
                return;
            }

            Transform visual = isLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;

            // A child of the hand visual, so nothing places it per frame.
            // Scale 1 under the mirrored right hand: it's the same shape
            // mirrored.
            _blackjack.SetParent(visual, false);
            _blackjack.SetLocalPositionAndRotation(inHandPosition, Quaternion.Euler(inHandRotation));
            _blackjack.localScale = Vector3.one;
            _blackjack.gameObject.SetActive(true);

            _isOut = true;
            _isInLeftHand = isLeftHand;
            wasInPlace = false;
            Pulse(isLeftHand, drawAmplitude, drawDuration);
        }

        /// <summary>
        /// Whether a hand could draw now: it's doing nothing else, it's
        /// at the chest, and its palm faces the chest.
        /// </summary>
        private bool IsInPlace(bool isLeftHand)
        {
            if (playerHandState.IsBusyExcept(isLeftHand, HandUse.Blackjack)) {
                return false;
            }

            Vector3 hand = isLeftHand ? playerTracking.LeftHandPosition : playerTracking.RightHandPosition;
            Vector3 toChest = ChestPoint() - hand;

            // Squared distances: the same answer as comparing the
            // distances, without a square root.
            if (toChest.sqrMagnitude > chestRadius * chestRadius) {
                return false;
            }

            Transform visual = isLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;

            // The palm faces along the hand visual's own +X.
            // TransformVector(), not TransformDirection(): the right
            // visual is the left one mirrored, and only TransformVector()
            // takes the mirroring into account.
            Vector3 palm = visual.TransformVector(Vector3.right).normalized;

            // The chest is a point on the body's middle line, so a hand
            // in front of it has it straight behind the palm. A hand
            // right on the point has no direction to it: then it's the
            // way the player's back is.
            Vector3 direction = toChest.sqrMagnitude > 0.0001f ? toChest.normalized : HeadYaw() * Vector3.back;

            return Vector3.Dot(palm, direction) >= Mathf.Cos(palmAngle * Mathf.Deg2Rad);
        }

        /// <summary>
        /// The middle of the chest, in the world.
        /// </summary>
        private Vector3 ChestPoint()
        {
            return playerTracking.HeadPosition + HeadYaw() * chestOffset;
        }

        /// <summary>
        /// The head's rotation with its tilt taken out: only which way it
        /// faces round the vertical.
        /// </summary>
        private Quaternion HeadYaw()
        {
            return Quaternion.Euler(0f, playerTracking.HeadRotation.eulerAngles.y, 0f);
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

        /// <summary>
        /// Editor-only: the chest ball while this object is selected.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// In the detailed view (selected, or in the headset with detail
        /// on) while playing: the ball a hand must be in to draw - yellow,
        /// green while the blackjack is out.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!detailed || !Application.isPlaying || playerTracking == null) {
                return;
            }

            lines.Color = _isOut ? Color.green : Color.yellow;
            lines.WireSphere(ChestPoint(), chestRadius);
        }
    }
}
