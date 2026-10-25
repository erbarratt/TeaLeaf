using Interaction;
using Inventory;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The bow, drawn from over a shoulder, and the arrows nocked and
    /// drawn on it.
    ///
    /// - The hand with the lockpicks (the left, for a right-handed player)
    ///   draws the bow: empty, reaching over its own shoulder, pressing
    ///   grip. It holds the bow for as long as grip is held; letting go
    ///   puts it away (never dropped).
    /// - While the bow is held, the other hand reaching over its own
    ///   shoulder takes an arrow of the chosen kind with the trigger, and
    ///   holds it for as long as the trigger is held. Each arrow taken is
    ///   one fewer in the inventory; letting go of one that isn't on the
    ///   string puts it back.
    /// - Brought near the string's middle, the arrow and hand snap onto it:
    ///   nocked. The hand is then held, in the string pose, to a straight
    ///   line back from the bow, and how far along it the hand is, is the
    ///   draw (0 at the string's rest, 1 at maxDrawDistance back). The
    ///   bow's "Drawn" blend shape bends it to match, and the arc fades in
    ///   with the draw (half drawn, half shown).
    /// - Letting go of the trigger shoots: the further drawn, the faster
    ///   the arrow leaves (PlayerArrows flies it). Nothing cancels a shot:
    ///   moving the hand well off the line, while drawn less than
    ///   lockDraw, takes the arrow off the string without shooting, still
    ///   in the hand, to be nocked again; drawn further, it stays on until
    ///   let back. Let go with the string barely drawn, the arrow goes
    ///   back into the inventory.
    /// - The bow hand's upper button (left Y for a right-handed player,
    ///   right B for a left-handed one) moves on to the next kind of arrow
    ///   the player has, and does nothing else. An arrow already in hand
    ///   is swapped for one of the new kind.
    ///
    /// The bow is a model with a "Drawn" blend shape and three empties as
    /// its children: String_Rest (the string's middle at rest),
    /// Draw_Full (where that point is at full draw) and Arrow_Rest (where
    /// the arrow lies on the bow). A drawn arrow lies on the line through
    /// all three. The model's own Z is the way it shoots, Y its top.
    ///
    /// Lives on the Hands object, with PlayerArrows. No Update():
    /// PlayerController calls Tick() (drawing the bow, taking an arrow,
    /// changing kind) before the grab systems, and TickHeld() (nocking,
    /// drawing, shooting, the arc) just before the hand visuals are
    /// placed, so the drawing hand is put on the string where the bow is
    /// this frame.
    /// </summary>
    [RequireComponent(typeof(PlayerArrows))]
    public class PlayerBow : MonoBehaviour
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

        // The arrows once shot, and their meshes. On this same object.
        [SerializeField] private PlayerArrows playerArrows;

        // Optional: a rig with no haptics still shoots.
        [SerializeField] private PlayerHaptics playerHaptics;

        // Optional: the bow and an arrow let go of burn away, and the bow
        // burns into being when drawn. Found in Awake() if not wired (it's
        // on this same object); none = they vanish and appear at once.
        [SerializeField] private ToolBurn toolBurn;

        [Header("The Bow")]

        // The bow model: a mesh with a "Drawn" blend shape, and the
        // String_Rest, Draw_Full and Arrow_Rest empties as its children.
        // Made once at load; with no model there is no bow. Its handle is
        // to the right of the arrow, as a bow held in the left hand: a
        // right-handed player's.
        [Tooltip("The bow held in the left hand (right-handed player): handle to the right of the arrow. A mesh with a \"Drawn\" blend shape and String_Rest, Draw_Full and Arrow_Rest children.")]
        [SerializeField] private GameObject bowModel;

        // The same bow mirrored, handle to the left of the arrow, for a
        // left-handed player holding it in the right hand.
        [Tooltip("The bow held in the right hand (left-handed player): Bow Model mirrored, handle to the left of the arrow. Empty uses Bow Model.")]
        [SerializeField] private GameObject leftHandedBowModel;

        // How the bow hand holds the bow: the hand's offset from the bow's
        // own origin and axes (its grip), and the fingers. Posed with
        // TeaLeaf > Modify Prefab > Pose with Hand.
        [Tooltip("How the bow hand holds the bow (Assets/Data/Hand Poses/BowGrip.asset): each hand's offset from the bow's origin and axes, and the finger pose. Posed with TeaLeaf > Modify Prefab > Pose with Hand. Empty puts the bow's origin on the hand visual's, fingers on their input curl.")]
        [SerializeField] private HandSnapProfile bowProfile;

        [Header("An Arrow In Hand")]

        // How the drawing hand holds an arrow off the string: the hand's
        // offset from the nock (the arrow's back end, its Z pointing to
        // the tip), and the fingers.
        [Tooltip("How the drawing hand holds an arrow before it's on the string (Assets/Data/Hand Poses/ArrowHold.asset): each hand's offset from the nock (Z along the arrow to its tip), and the finger pose. Posed with TeaLeaf > Modify Prefab > Pose with Hand.")]
        [SerializeField] private HandSnapProfile arrowProfile;

        // How the drawing hand holds a nocked arrow on the string: the
        // hand's offset (place and turn) from the nock, and the fingers.
        // The hand is held in this pose on the string, whatever its
        // controller's turn.
        [Tooltip("How the drawing hand holds the string (Assets/Data/Hand Poses/BowString.asset): each hand's offset (place and turn) from the nocked arrow's nock, and the finger pose. The hand is held in this pose on the string, whatever its controller's turn. Posed with TeaLeaf > Modify Prefab > Pose with Hand.")]
        [SerializeField] private HandSnapProfile stringProfile;

        // How far the trigger must be pulled to take an arrow, and how far
        // let out to let go of it (0-1). A gap between, so a trigger held
        // near one point can't take and let go over and over.
        [SerializeField] private float takeTrigger = 0.6f;
        [SerializeField] private float releaseTrigger = 0.3f;

        [Header("Nocking And Drawing")]

        // How near the string's middle an arrow's nock must come to be
        // nocked, in metres.
        [SerializeField] private float nockRadius = 0.08f;

        // How far back from its rest the string can be drawn, in metres:
        // full draw. The bow's Drawn blend shape is scaled to match, so
        // the string stays on the nock (the model's own full draw,
        // String_Rest to Draw_Full, is 0.5m; much further than that
        // bends the limbs past what the shape was made for).
        [Tooltip("How far back from rest the string can be drawn (full draw), m. The Drawn blend shape is scaled so the string follows the nock; the model's own Draw_Full is 0.5m back.")]
        [SerializeField] private float maxDrawDistance = 0.5f;

        // How far the drawing hand may stray from the line it draws along
        // before the arrow comes off the string, in metres. Larger than
        // nockRadius, so a nocked arrow doesn't come straight off again.
        [SerializeField] private float breakDistance = 0.15f;

        // The draw (0-1) from which the arrow stays on the string however
        // far the hand strays from the line: it comes off again only once
        // the string is let back below this.
        [Tooltip("Draw (0-1) at and beyond which straying more than Break Distance off the line no longer takes the arrow off the string; let the string back below it first.")]
        [SerializeField] private float lockDraw = 0.5f;

        // The least draw (0-1) that shoots when the trigger is let go.
        // Less, and the arrow goes back into the inventory.
        [SerializeField] private float minShotDraw = 0.15f;

        // Seconds the drawing hand takes to snap onto the string.
        [SerializeField] private float nockBlendDuration = 0.06f;

        [Header("Haptics")]

        // A hand arriving where it can draw the bow or take an arrow.
        [SerializeField] private float reachAmplitude = 0.15f;
        [SerializeField] private float reachDuration = 0.03f;

        // The bow drawn, an arrow taken or changed, and a try with no
        // arrows left.
        [SerializeField] private float takeAmplitude = 0.4f;
        [SerializeField] private float takeDuration = 0.05f;
        [SerializeField] private float emptyAmplitude = 0.2f;
        [SerializeField] private float emptyDuration = 0.03f;

        // The arrow clicking onto the string, felt in both hands.
        [SerializeField] private float nockAmplitude = 0.5f;
        [SerializeField] private float nockDuration = 0.04f;

        // The string creaking as it's drawn: one tick each of this many
        // steps from rest to full draw, stronger the further drawn.
        [SerializeField] private int drawSteps = 8;
        [SerializeField] private float drawStepMinAmplitude = 0.05f;
        [SerializeField] private float drawStepMaxAmplitude = 0.3f;
        [SerializeField] private float drawStepDuration = 0.02f;

        // A shot, felt in both hands.
        [SerializeField] private float shotAmplitude = 0.9f;
        [SerializeField] private float shotDuration = 0.08f;

        // Where the arrow is.
        private enum ArrowHold
        {
            // No arrow in hand.
            None,
            // In the drawing hand, not on the string.
            InHand,
            // On the string, the hand drawing it.
            Nocked
        }

        // The blend shape that bends the bow, and the bow's empties.
        private const string DrawnShapeName = "Drawn";
        private const string StringRestName = "String_Rest";
        private const string DrawFullName = "Draw_Full";
        private const string ArrowRestName = "Arrow_Rest";

        // How many kinds of arrow there are, counted once from
        // Inventory.BoltType rather than written as a number that could
        // fall behind.
        private static readonly int _arrowKinds = System.Enum.GetValues(typeof(BoltType)).Length;

        private bool _hasHaptics;

        // Whether there's a bow at all: a model with the blend shape and
        // the three empties.
        private bool _hasBow;

        // Which hand holds the bow (the one with the lockpicks, from
        // handedness at startup); the other draws the arrows.
        private bool _isBowLeft;

        // The bow, the renderer its blend shape is set on and the blend
        // shape's number, and the three empties' points in its own space.
        private Transform _bow;
        private SkinnedMeshRenderer _bowRenderer;
        private int _drawnIndex;
        private Vector3 _stringRest;
        private Vector3 _arrowRest;

        // The way the string is drawn back (bow space, unit length), and
        // the blend shape weight per metre drawn: 100 at the model's own
        // full draw.
        private Vector3 _drawDirection;
        private float _drawnWeightPerMetre;

        // The arrow shown in hand or on the string, and its mesh.
        private Transform _arrow;
        private MeshFilter _arrowFilter;

        // The bow and the arrow as ToolBurn knows them. Null without one.
        private ToolBurn.Target _bowBurn;
        private ToolBurn.Target _arrowBurn;

        // The drawing hand's controller, and its visual's resting pose
        // under it: where the visual would be if nothing held it - so
        // where the nock would be, and how the hand is turned, while the
        // visual itself is snapped to the string.
        private Transform _drawController;
        private Vector3 _drawVisualPosition;
        private Quaternion _drawVisualRotation;

        // Whether the bow is out, and the bow hand's grip last frame (it's
        // drawn on the frame grip is pressed, not by a hand that arrives
        // gripping), and whether each hand was in place to draw last frame
        // (for the tap).
        private bool _isOut;
        private bool _wasBowGrabbing;
        private bool _wasBowHandInPlace;
        private bool _wasArrowHandInPlace;

        // The arrow: where it is, its kind, and the kind chosen for the
        // next one taken.
        private ArrowHold _hold;
        private BoltType _arrowKind;
        private BoltType _selected = BoltType.Water;

        // The drawing hand's trigger, with the gap between taking and
        // letting go (see takeTrigger): held now, and last frame.
        private bool _isTriggerHeld;
        private bool _wasTriggerHeld;

        // How far drawn, 0-1, and the creak step it's at.
        private float _draw;
        private int _drawStep;

        /// True while the bow is in a hand.
        public bool IsOut => _isOut;

        /// The kind of arrow chosen: the one taken next.
        public BoltType SelectedArrow => _selected;

        /// True while that hand holds the bow, or is the other hand
        /// holding an arrow. The other hand systems leave it alone
        /// meanwhile (PlayerHandState).
        public bool IsLeftBusy => _isBowLeft ? _isOut : _hold != ArrowHold.None;
        public bool IsRightBusy => _isBowLeft ? _hold != ArrowHold.None : _isOut;

        /// <summary>
        /// Whose finger pose a hand plays for what this holds in it: the
        /// bow's in the bow hand, the arrow's in the drawing hand (on the
        /// string, the snap's string pose wins). Null when it holds
        /// neither. Read by PlayerHandAnimation each frame.
        /// </summary>
        public HandSnapProfile GetHeldProfile(bool isLeftHand)
        {
            if (isLeftHand == _isBowLeft) {
                return _isOut ? bowProfile : null;
            }

            return _hold != ArrowHold.None ? arrowProfile : null;
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
            playerArrows = GetComponent<PlayerArrows>();
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

            if (playerArrows == null) {
                playerArrows = GetComponent<PlayerArrows>();
            }

            if (playerHaptics == null) {
                playerHaptics = GetComponentInParent<PlayerHaptics>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasHaptics = playerHaptics != null;

            if (toolBurn == null) {
                toolBurn = GetComponent<ToolBurn>();
            }

            // A right-handed player wears the lockpicks on the left hand,
            // and holds the bow in it too; a left-handed one the other way
            // round.
            _isBowLeft = !playerInput.IsLeftHanded;
        }

        /// <summary>
        /// Makes the bow and the arrow shown in hand. Start() rather than
        /// Awake(): the arrow's meshes and material are PlayerArrows', made
        /// in its Awake(), and every Awake() has run before the first
        /// Start().
        /// </summary>
        private void Start()
        {
            Transform drawVisual = _isBowLeft ? playerHandVisuals.RightHandVisual : playerHandVisuals.LeftHandVisual;

            // The visual's pose under its controller, before anything has
            // had a chance to snap it elsewhere.
            _drawController = drawVisual.parent;
            _drawVisualPosition = drawVisual.localPosition;
            _drawVisualRotation = drawVisual.localRotation;

            BuildBow();
            BuildArrow();

            if (toolBurn != null) {
                if (_hasBow) {
                    _bowBurn = toolBurn.Register(_bowRenderer);
                }

                _arrowBurn = toolBurn.Register(_arrowFilter.GetComponent<MeshRenderer>());
            }
        }

        private void OnDisable()
        {
            // Null if Start() hasn't run. And if the scene is being
            // unloaded (a level restart), everything here is on its way
            // out and mustn't be touched.
            if (_arrow == null || !gameObject.scene.isLoaded) {
                return;
            }

            if (_isOut) {
                PutAway();
            }
        }

        /// <summary>
        /// Makes the bow from its model, once, kept under this object and
        /// switched off until it's drawn - not on a hand visual, so the
        /// ghost hands never get one. The model may come from its file
        /// with a plain MeshRenderer (Unity imports a mesh with blend
        /// shapes but no bones that way), which can't show a blend shape:
        /// then its mesh is moved onto a SkinnedMeshRenderer, which can.
        /// No collider: it's only to look at. The model is the one for the
        /// hand holding it, so the arrow lies on the handle's outer side.
        /// </summary>
        private void BuildBow()
        {
            GameObject model = _isBowLeft ? bowModel : leftHandedBowModel;

            if (model == null && !_isBowLeft) {
                Debug.LogWarning("PlayerBow: no left-handed bow model set, so the right-handed one is used.", this);
                model = bowModel;
            }

            if (model == null) {
                Debug.LogWarning("PlayerBow: no bow model set, so there is no bow.", this);
                return;
            }

            GameObject bow = Instantiate(model, transform, false);
            bow.name = "Bow";
            _bow = bow.transform;

            _bowRenderer = bow.GetComponentInChildren<SkinnedMeshRenderer>();

            if (_bowRenderer == null) {
                _bowRenderer = MoveOntoSkinnedRenderer(bow);
            }

            Transform stringRest = _bow.Find(StringRestName);
            Transform drawFull = _bow.Find(DrawFullName);
            Transform arrowRest = _bow.Find(ArrowRestName);
            Mesh mesh = _bowRenderer != null ? _bowRenderer.sharedMesh : null;
            _drawnIndex = mesh != null ? mesh.GetBlendShapeIndex(DrawnShapeName) : -1;

            if (stringRest == null || drawFull == null || arrowRest == null || _drawnIndex < 0) {
                Debug.LogWarning($"PlayerBow: the bow model needs a '{DrawnShapeName}' blend shape and '{StringRestName}', '{DrawFullName}' and '{ArrowRestName}' children, so there is no bow.", this);
                bow.SetActive(false);
                return;
            }

            _stringRest = stringRest.localPosition;
            _arrowRest = arrowRest.localPosition;

            Vector3 modelDraw = drawFull.localPosition - _stringRest;
            float modelDrawLength = Mathf.Max(modelDraw.magnitude, 0.01f);
            _drawDirection = modelDraw / modelDrawLength;
            _drawnWeightPerMetre = 100f / modelDrawLength;

            // Unity decides whether to draw a skinned mesh from bounds
            // round its resting shape; the string drawn back goes outside
            // them. Grown to take in the further of the model's full draw
            // and this one, so the bow is never wrongly left undrawn.
            Bounds bounds = mesh.bounds;
            Vector3 furthest = _stringRest + _drawDirection * Mathf.Max(modelDrawLength, maxDrawDistance);
            bounds.Encapsulate(_bowRenderer.transform.InverseTransformPoint(_bow.TransformPoint(furthest)));
            _bowRenderer.localBounds = bounds;
            _bowRenderer.updateWhenOffscreen = false;
            _bowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bowRenderer.receiveShadows = false;

            _hasBow = true;
            bow.SetActive(false);
        }

        /// <summary>
        /// Replaces the model's plain MeshRenderer with a SkinnedMeshRenderer
        /// drawing the same mesh with the same materials, on a new child at
        /// the same place (one object can't have both). Null if the model
        /// has no mesh.
        /// </summary>
        private static SkinnedMeshRenderer MoveOntoSkinnedRenderer(GameObject bow)
        {
            MeshFilter filter = bow.GetComponentInChildren<MeshFilter>();

            if (filter == null) {
                return null;
            }

            MeshRenderer plain = filter.GetComponent<MeshRenderer>();
            Material[] materials = plain != null ? plain.sharedMaterials : null;

            GameObject skinned = new("Bow Mesh");
            skinned.transform.SetParent(filter.transform, false);

            SkinnedMeshRenderer skin = skinned.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = filter.sharedMesh;
            skin.sharedMaterials = materials;

            if (plain != null) {
                Destroy(plain);
            }

            Destroy(filter);
            return skin;
        }

        /// <summary>
        /// Makes the arrow shown in hand and on the string, once: one
        /// object, its model's mesh swapped for the kind held (its own
        /// origin is the tip, as for a shot arrow). Kept under this
        /// object, switched off, until an arrow is taken.
        /// </summary>
        private void BuildArrow()
        {
            GameObject arrow = new("Held Arrow");
            arrow.transform.SetParent(transform, false);
            playerArrows.MakeArrowModel(arrow.transform, _selected, out _arrowFilter);

            arrow.SetActive(false);
            _arrow = arrow.transform;
        }

        /// <summary>
        /// Drawing the bow and putting it away, taking an arrow and
        /// changing its kind. Called by PlayerController before the grab
        /// systems, so a hand that takes the bow or an arrow is already
        /// busy when they look at what its ray is on.
        /// </summary>
        public void Tick()
        {
            if (!_hasBow) {
                return;
            }

            bool isBowGrabbing = _isBowLeft ? playerInput.IsLeftGrabbing : playerInput.IsRightGrabbing;

            if (_isOut) {
                // Held for as long as grip is.
                if (!isBowGrabbing) {
                    PutAway();
                } else {
                    TickArrowReach();
                    TickChangeKind();
                }
            } else {
                TickBowReach(isBowGrabbing);
            }

            _wasBowGrabbing = isBowGrabbing;
        }

        /// <summary>
        /// The bow hand, while the bow is away: a tap as it comes into
        /// place over its shoulder, free, and the draw on the frame its
        /// grip is pressed there.
        /// </summary>
        private void TickBowReach(bool isGrabbing)
        {
            bool isInPlace = !playerHandState.IsBusyExcept(_isBowLeft, HandUse.Bow)
                && bodyZones.IsAtShoulder(_isBowLeft);

            if (isInPlace && !_wasBowHandInPlace) {
                Pulse(_isBowLeft, reachAmplitude, reachDuration);
            }

            _wasBowHandInPlace = isInPlace;

            if (!isInPlace || !isGrabbing || _wasBowGrabbing) {
                return;
            }

            Transform visual = _isBowLeft ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;

            // A child of the hand visual where the bow profile puts it, so
            // nothing places it per frame.
            HandSnapProfile.PlaceInHand(bowProfile, _isBowLeft, _bow, visual, transform);
            _bow.gameObject.SetActive(true);
            SetDraw(0f);
            BurnIn(_bowBurn);

            _isOut = true;
            _wasBowHandInPlace = false;
            _wasArrowHandInPlace = false;

            // A trigger already held as the bow comes out must be let go
            // before it takes an arrow.
            _wasTriggerHeld = true;
            _isTriggerHeld = true;
            Pulse(_isBowLeft, takeAmplitude, takeDuration);
        }

        /// <summary>
        /// The other hand, while the bow is out and it holds no arrow: a
        /// tap as it comes into place over its own shoulder, free, and an
        /// arrow taken on the frame its trigger is pulled there.
        /// </summary>
        private void TickArrowReach()
        {
            bool isArrowLeft = !_isBowLeft;

            if (_hold != ArrowHold.None) {
                return;
            }

            bool isInPlace = !playerHandState.IsBusyExcept(isArrowLeft, HandUse.Bow)
                && bodyZones.IsAtShoulder(isArrowLeft);

            if (isInPlace && !_wasArrowHandInPlace) {
                Pulse(isArrowLeft, reachAmplitude, reachDuration);
            }

            _wasArrowHandInPlace = isInPlace;

            if (!isInPlace || !_isTriggerHeld || _wasTriggerHeld) {
                return;
            }

            if (!TryTakeArrow()) {
                Pulse(isArrowLeft, emptyAmplitude, emptyDuration);
                return;
            }

            HoldArrowInHand();
            _wasArrowHandInPlace = false;
            Pulse(isArrowLeft, takeAmplitude, takeDuration);
        }

        /// <summary>
        /// Reads the drawing hand's trigger, with the gap between taking
        /// and letting go. Once a frame, from TickHeld(), which runs on
        /// every path (an arrow is let go of mid-mantle too); Tick() sees
        /// the press on the frame after.
        /// </summary>
        private void UpdateTrigger(bool isArrowLeft)
        {
            float trigger = isArrowLeft ? playerInput.LeftTrigger : playerInput.RightTrigger;

            _wasTriggerHeld = _isTriggerHeld;

            if (_isTriggerHeld) {
                _isTriggerHeld = trigger > releaseTrigger;
            } else {
                _isTriggerHeld = trigger >= takeTrigger;
            }
        }

        /// <summary>
        /// The bow hand's upper button: on to the next kind of arrow the
        /// player has any of. An arrow already in hand, or on the string,
        /// goes back and one of the new kind takes its place.
        /// </summary>
        private void TickChangeKind()
        {
            // The bow hand's upper button: left Y, or for a left-handed
            // player (bow in the right) right B, which is cancel's button.
            bool isPressed = _isBowLeft ? playerInput.ChangeArrowPressed : playerInput.CancelPressed;

            if (!isPressed) {
                return;
            }

            BoltType next = _selected;

            for (int i = 1; i <= _arrowKinds; i++) {
                BoltType kind = (BoltType)(((int)_selected + i) % _arrowKinds);

                // The arrow in hand counts as one the player has.
                if (HasArrow(kind) || (_hold != ArrowHold.None && kind == _arrowKind)) {
                    next = kind;
                    break;
                }
            }

            if (next == _selected) {
                Pulse(_isBowLeft, emptyAmplitude, emptyDuration);
                return;
            }

            _selected = next;
            Pulse(_isBowLeft, takeAmplitude, takeDuration);

            if (_hold == ArrowHold.None || _arrowKind == _selected) {
                return;
            }

            // Swapped: the one in hand back, one of the new kind out.
            ReturnArrowToInventory();

            if (TryTakeArrow()) {
                _arrowFilter.sharedMesh = playerArrows.GetMesh(_arrowKind);
            } else {
                LetGoOfArrow();
            }
        }

        /// <summary>
        /// Takes one arrow of the chosen kind from the inventory - or, if
        /// there are none of it left, of the next kind there are. False,
        /// and nothing taken, if there are no arrows at all. With no
        /// inventory in the scene (a test scene), arrows never run out.
        /// </summary>
        private bool TryTakeArrow()
        {
            PlayerInventory inventory = PlayerInventory.Instance;

            for (int i = 0; i < _arrowKinds; i++) {
                BoltType kind = (BoltType)(((int)_selected + i) % _arrowKinds);

                if (inventory == null || inventory.TryUseBolt(kind)) {
                    _selected = kind;
                    _arrowKind = kind;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the player has an arrow of a kind. With no inventory in
        /// the scene, always.
        /// </summary>
        private static bool HasArrow(BoltType kind)
        {
            PlayerInventory inventory = PlayerInventory.Instance;
            return inventory == null || inventory.GetBoltCount(kind) > 0;
        }

        /// <summary>
        /// Puts the arrow back into the inventory (it was taken out when it
        /// was drawn over the shoulder). The arrow shown isn't touched.
        /// </summary>
        private void ReturnArrowToInventory()
        {
            PlayerInventory inventory = PlayerInventory.Instance;

            if (inventory != null) {
                inventory.TryAddBolt(_arrowKind);
            }
        }

        /// <summary>
        /// Shows the arrow in the drawing hand, a child of its visual with
        /// the nock where the arrow profile puts it. The arrow's own
        /// origin is its tip, an arrow's length ahead of the nock along
        /// its Z.
        /// </summary>
        private void HoldArrowInHand()
        {
            bool isArrowLeft = !_isBowLeft;
            Transform visual = isArrowLeft ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;
            HandSnapProfile.GripFrameInHand(arrowProfile, isArrowLeft, visual.position, visual.rotation, out Vector3 nock, out Quaternion rotation);

            // World pose under the unscaled Hands first, then kept as it's
            // parented (see HandSnapProfile.PlaceInHand()).
            _arrowFilter.sharedMesh = playerArrows.GetMesh(_arrowKind);
            _arrow.SetParent(transform, false);
            _arrow.localScale = Vector3.one;
            _arrow.SetPositionAndRotation(nock + rotation * (Vector3.forward * playerArrows.ArrowLength), rotation);
            _arrow.SetParent(visual, true);
            _arrow.gameObject.SetActive(true);
            _hold = ArrowHold.InHand;
        }

        /// <summary>
        /// The arrow is gone from the hand (shot, or back in the
        /// inventory): hidden, the hand back on its controller, the string
        /// at rest.
        /// </summary>
        private void LetGoOfArrow()
        {
            if (_hold == ArrowHold.Nocked) {
                DrawSnap().Release();
                SetDraw(0f);
            }

            _hold = ArrowHold.None;
            _arrow.gameObject.SetActive(false);
            _arrow.SetParent(transform, false);
            playerArrows.HideArc();
        }

        /// <summary>
        /// Puts the bow away: any arrow in hand goes back into the
        /// inventory, and the bow back under this object, switched off.
        /// </summary>
        private void PutAway()
        {
            // Burnt where they are, before anything moves: the bow first,
            // still bent by the arrow drawn on it.
            BurnAway(_bowBurn);

            if (_hold != ArrowHold.None) {
                BurnAway(_arrowBurn);
                ReturnArrowToInventory();
                LetGoOfArrow();
            }

            _isOut = false;
            _bow.gameObject.SetActive(false);
            _bow.SetParent(transform, false);
        }

        /// <summary>
        /// The arrow in hand: nocked when brought to the string, then
        /// drawn, shot or taken off again; let go of, back into the
        /// inventory. Also the arc. Called by PlayerController just before
        /// the hand visuals are placed, on every path through its
        /// Update(): canAim is false during a mantle and once the level
        /// has ended - nothing is shot then, and an arrow let go of goes
        /// back into the inventory.
        /// </summary>
        public void TickHeld(bool canAim)
        {
            bool isArrowLeft = !_isBowLeft;
            UpdateTrigger(isArrowLeft);

            if (_hold == ArrowHold.None) {
                return;
            }

            if (_hold == ArrowHold.InHand) {
                if (!_isTriggerHeld) {
                    BurnAway(_arrowBurn);
                    ReturnArrowToInventory();
                    LetGoOfArrow();
                    return;
                }

                TryNock(isArrowLeft);
                return;
            }

            // Nocked. Where the nock would be with the hand on its
            // controller, and the nearest point to it on the line it draws
            // along.
            Vector3 nock = ControllerGripPoint(stringProfile);
            Vector3 restPoint = _bow.TransformPoint(_stringRest);
            Vector3 line = _bow.TransformDirection(_drawDirection) * Mathf.Max(maxDrawDistance, 0.01f);
            float draw = Mathf.Clamp01(Vector3.Dot(nock - restPoint, line) / line.sqrMagnitude);
            Vector3 onLine = restPoint + line * draw;

            // Too far off the line: the arrow comes off the string, still
            // in the hand - unless drawn past lockDraw, where it stays on
            // until the string is let back below it.
            if (draw < lockDraw && (nock - onLine).sqrMagnitude > breakDistance * breakDistance) {
                DrawSnap().Release();
                SetDraw(0f);
                playerArrows.HideArc();
                HoldArrowInHand();
                return;
            }

            SetDraw(draw);
            PlaceNockedArrow();
            SnapHandToNock(onLine);
            TickDrawSteps(isArrowLeft);

            Vector3 tip = _arrow.position;
            Vector3 direction = (_bow.TransformPoint(_arrowRest) - onLine).normalized;

            if (!_isTriggerHeld) {
                if (canAim && _draw >= minShotDraw) {
                    playerArrows.Shoot(_arrowKind, tip, direction, _draw);
                    Pulse(isArrowLeft, shotAmplitude, shotDuration);
                    Pulse(_isBowLeft, shotAmplitude, shotDuration);
                } else {
                    BurnAway(_arrowBurn);
                    ReturnArrowToInventory();
                }

                LetGoOfArrow();
                return;
            }

            // No arc until the draw is enough to shoot; from there it fades
            // in up to full draw, where it shows at the arc colour's own
            // alpha (ThrowArc scales that colour's alpha by this).
            if (canAim && _draw > minShotDraw) {
                float arcAlpha = Mathf.InverseLerp(minShotDraw, 1f, _draw);
                playerArrows.ShowArc(tip, direction, _draw, arcAlpha, playerTracking.HeadPosition);
            } else {
                playerArrows.HideArc();
            }
        }

        /// <summary>
        /// The arrow in hand, its nock brought near the string's middle:
        /// nocked. It moves onto the bow and the hand snaps onto the
        /// string. The nock is where it would be with the hand on its
        /// controller, as the draw is measured - not where the visual has
        /// it: just after the arrow comes off the string, the visual is
        /// still blending back from the string, and measuring from it
        /// would nock the arrow again at once, then take it off, every
        /// other frame.
        /// </summary>
        private void TryNock(bool isArrowLeft)
        {
            Vector3 nock = ControllerGripPoint(arrowProfile);
            Vector3 restPoint = _bow.TransformPoint(_stringRest);

            if ((nock - restPoint).sqrMagnitude > nockRadius * nockRadius) {
                return;
            }

            _hold = ArrowHold.Nocked;
            _drawStep = 0;
            _arrow.SetParent(_bow, false);
            _arrow.localScale = Vector3.one;
            SetDraw(0f);
            PlaceNockedArrow();

            // The string profile's fingers, round the string.
            HandSnapPose pose = HandPoseAtNock(restPoint);
            DrawSnap().Snap(new HandSnapPose(pose.Position, pose.Rotation, stringProfile), nockBlendDuration);

            Pulse(isArrowLeft, nockAmplitude, nockDuration);
            Pulse(_isBowLeft, nockAmplitude, nockDuration);
        }

        /// <summary>
        /// Lays the nocked arrow on the bow: its nock on the string where
        /// the draw has it, pointing through the arrow rest. In the bow's
        /// own space, so it moves with the bow.
        /// </summary>
        private void PlaceNockedArrow()
        {
            Vector3 nock = _stringRest + _drawDirection * (_draw * maxDrawDistance);
            Vector3 direction = (_arrowRest - nock).normalized;

            _arrow.SetLocalPositionAndRotation(nock + direction * playerArrows.ArrowLength, Quaternion.LookRotation(direction, Vector3.up));
        }

        /// <summary>
        /// Holds the drawing hand's visual in the string pose at the nock.
        /// </summary>
        private void SnapHandToNock(Vector3 nock)
        {
            HandSnapPose pose = HandPoseAtNock(nock);
            DrawSnap().SetSnapPose(pose.Position, pose.Rotation);
        }

        /// <summary>
        /// Where the drawing hand's visual goes to hold the nock at a
        /// point: the string profile's pose (place and turn) on the nocked
        /// arrow's frame - the nock, Z along the arrow, Y the bow's up.
        /// The arrow must already be laid on the bow (PlaceNockedArrow()).
        /// The controller's turn plays no part, so the hand stays as posed
        /// on the string however the wrist turns.
        /// </summary>
        private HandSnapPose HandPoseAtNock(Vector3 nock)
        {
            Quaternion arrowRotation = _arrow.rotation;

            if (stringProfile == null) {
                return new HandSnapPose(nock, arrowRotation, default);
            }

            return stringProfile.Apply(!_isBowLeft, nock, arrowRotation);
        }

        /// <summary>
        /// Where a profile's grip frame (here, the nock) would be if the
        /// drawing hand's visual simply followed its controller.
        /// </summary>
        private Vector3 ControllerGripPoint(HandSnapProfile profile)
        {
            Vector3 handPosition = _drawController.TransformPoint(_drawVisualPosition);
            Quaternion handRotation = _drawController.rotation * _drawVisualRotation;
            HandSnapProfile.GripFrameInHand(profile, !_isBowLeft, handPosition, handRotation, out Vector3 grip, out _);
            return grip;
        }

        /// <summary>
        /// The string creaking as it's drawn back: a tick in the drawing
        /// hand at each step further, stronger the further drawn. Easing
        /// back makes none.
        /// </summary>
        private void TickDrawSteps(bool isArrowLeft)
        {
            int step = Mathf.FloorToInt(_draw * Mathf.Max(drawSteps, 1));

            if (step > _drawStep) {
                Pulse(isArrowLeft, Mathf.Lerp(drawStepMinAmplitude, drawStepMaxAmplitude, _draw), drawStepDuration);
            }

            _drawStep = step;
        }

        /// <summary>
        /// Sets how far the bow is drawn, 0-1, and bends it to match.
        /// </summary>
        private void SetDraw(float draw)
        {
            if (Mathf.Approximately(draw, _draw)) {
                return;
            }

            _draw = draw;
            _bowRenderer.SetBlendShapeWeight(_drawnIndex, draw * maxDrawDistance * _drawnWeightPerMetre);
        }

        /// <summary>
        /// The drawing hand's snap, owned by PlayerHandVisuals. Looked up
        /// when needed rather than kept from Awake(): PlayerHandVisuals
        /// makes them in its own Awake(), which may run after this class's.
        /// </summary>
        private HandVisualSnap DrawSnap()
        {
            return _isBowLeft ? playerHandVisuals.RightVisualSnap : playerHandVisuals.LeftVisualSnap;
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
        /// Leaves a copy of the bow or the arrow burning away where it is,
        /// if the rig has a ToolBurn.
        /// </summary>
        private void BurnAway(ToolBurn.Target target)
        {
            if (toolBurn != null) {
                toolBurn.BurnAway(target);
            }
        }

        /// <summary>
        /// Burns the bow into being in the hand, if the rig has a ToolBurn.
        /// </summary>
        private void BurnIn(ToolBurn.Target target)
        {
            if (toolBurn != null) {
                toolBurn.BurnIn(target);
            }
        }
    }
}
