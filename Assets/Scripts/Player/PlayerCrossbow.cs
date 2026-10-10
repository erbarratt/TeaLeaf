using Core;
using Interaction;
using Inventory;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The hand crossbow. It is always worn on the back of one hand - the
    /// right for a right-handed player, the left for a left-handed one
    /// (PlayerInputXR.IsLeftHanded); the lockpicks are on the other.
    ///
    /// It becomes active when grip is pressed on that hand while the hand
    /// is empty, isn't pointing at anything it could grab (a grab always
    /// wins), and has its palm facing the floor. It stays active until
    /// grip is let go, however the hand is turned meanwhile.
    ///
    /// Active, it shows the arc a bolt would fly along and where it would
    /// land, like an aimed throw's but much flatter and longer, because a
    /// bolt is much faster. How fast isn't set directly: a range is (how
    /// far a bolt carries over level ground at the best angle), and the
    /// speed that gives it is worked out once, at load. Pulling the
    /// trigger shoots - on the press, not the release. The crossbow is
    /// clockwork: it winds itself again, and is ready after a short wait.
    ///
    /// There are three kinds of bolt (Inventory.BoltType), each with its
    /// own look, and each shot uses one of that kind from the player's
    /// inventory:
    /// - Water puts out any fire (Core.Flame) near where it lands.
    /// - A noisemaker sticks where it lands and makes noise for a while,
    ///   to draw guards.
    /// - A rope bolt that lands in wood hangs a rope to climb.
    /// The kind is chosen with the wheel at the back of the crossbow: the
    /// other hand points at it (the reticle shows), grips, and turns.
    ///
    /// A bolt isn't a physics object. It's moved along the same sum the
    /// arc was drawn from, so it goes exactly where the arc showed, with
    /// one physics ray a frame in case something has moved into its way.
    /// Bolts and ropes come from small pools made at load: nothing is
    /// created or destroyed while playing.
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// TickWheel() (the other hand taking the wheel) before the grab
    /// systems, Tick() (becoming active, and stopping) after them, and
    /// TickHeld() (the wheel, the arc, shooting, and bolts in flight)
    /// after the hand visuals are placed.
    /// </summary>
    public class PlayerCrossbow : MonoBehaviour
    {
        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // What each hand is busy with: a hand climbing, carrying or on a
        // door can't use the crossbow. Found (or made) in Awake() if not
        // wired (it's on this same object).
        [SerializeField] private PlayerHandState playerHandState;

        // Optional: a rig with no haptics still shoots.
        [SerializeField] private PlayerHaptics playerHaptics;

        [Header("Where It Sits")]

        // The middle of the crossbow's stock in the hand visual's own
        // space (its fingers point along -Y, the back of the hand is -X,
        // the thumb side +Z). The right hand visual is the left one
        // mirrored, so the same numbers put it on the back of either
        // hand.
        [SerializeField] private Vector3 onHandPosition = new(-0.035f, -0.05f, 0f);

        // It points along the fingers with its top away from the back of
        // the hand. This turns it from there, in degrees, for lining it up
        // by eye.
        [SerializeField] private Vector3 onHandTilt = Vector3.zero;

        [Header("Its Shape (greybox)")]

        // The stock: the long body the bolt lies on. Width, height and
        // length in metres.
        [SerializeField] private Vector3 stockSize = new(0.025f, 0.018f, 0.16f);

        // The bow: the bar across the front. How wide it is and how thick.
        [SerializeField] private float bowWidth = 0.16f;
        [SerializeField] private float bowThickness = 0.012f;

        // The wheel at the back, towards the forearm. How far across it
        // is.
        [SerializeField] private float wheelSize = 0.035f;

        [SerializeField] private Color stockColor = new(0.35f, 0.25f, 0.16f, 1f);
        [SerializeField] private Color metalColor = new(0.6f, 0.62f, 0.65f, 1f);

        // The least light the crossbow and bolts are ever shown in, 0-1.
        [SerializeField] private float minLight = 0.35f;

        [Header("Becoming Active")]

        // How far the palm may be turned away from straight down, in
        // degrees, and still count as facing the floor when grip is
        // pressed.
        [SerializeField] private float palmDownAngle = 50f;

        [Header("Shooting")]

        // How far a bolt carries over level ground when shot at the best
        // angle, in metres. This is what sets a bolt's speed (worked out
        // once, at load): a longer range is a faster bolt and a flatter
        // arc.
        [SerializeField] private float range = 40f;

        // How far the trigger must be pulled to shoot, and how far it
        // must be let out before it can shoot again (0-1).
        [SerializeField] private float fireTrigger = 0.6f;
        [SerializeField] private float rearmTrigger = 0.3f;

        // Seconds the clockwork takes to wind the crossbow again after a
        // shot.
        [SerializeField] private float reloadTime = 1f;

        // How far ahead of the stock's middle a bolt leaves from, in
        // metres.
        [SerializeField] private float muzzleDistance = 0.1f;

        // The sound of a shot, and of a bolt landing. Optional.
        [SerializeField] private SoundCue fireCue;
        [SerializeField] private SoundCue impactCue;

        [Header("The Wheel (choosing the bolt)")]

        // The radius of the ball round the wheel that the other hand's
        // ray can target, in metres: much bigger than the wheel, so it's
        // easy to aim at.
        [SerializeField] private float wheelGrabRadius = 0.06f;

        // How far the wheel has to be turned, in degrees, to move on to
        // the next kind of bolt.
        [SerializeField] private float wheelStepAngle = 40f;

        // How the hand sits on the wheel. With none, the hand sits
        // exactly on the grip frame.
        [SerializeField] private HandSnapProfile wheelProfile;

        // The hand on the wheel is let go if its controller gets this far
        // from the wheel, in metres.
        [SerializeField] private float wheelBreakDistance = 0.3f;

        [Header("Bolt Kinds")]

        // Each kind's colour: water's tip, the noisemaker's tip, and the
        // rope wound round a rope bolt's shaft.
        [SerializeField] private Color waterColor = new(0.2f, 0.55f, 1f, 1f);
        [SerializeField] private Color noisemakerColor = new(1f, 0.75f, 0.15f, 1f);
        [SerializeField] private Color ropeColor = new(0.75f, 0.62f, 0.4f, 1f);

        // Water: every fire within this many metres of where it lands
        // goes out.
        [SerializeField] private float waterRadius = 0.6f;

        // Noisemaker: how far its noise carries to guards (metres), how
        // often it sounds and for how long (seconds), and the sound it
        // makes (optional: with none it's a noise guards hear and the
        // player doesn't).
        [SerializeField] private float noiseRadius = 12f;
        [SerializeField] private float noiseInterval = 1.5f;
        [SerializeField] private float noiseDuration = 12f;
        [SerializeField] private SoundCue noiseCue;

        // Rope: how many ropes can hang at once (the oldest is reused),
        // the longest a rope can be, what counts as ground beneath it,
        // how thick it's drawn, and how a hand grips it.
        [SerializeField] private int ropeCount = 2;
        [SerializeField] private float ropeMaxLength = 8f;
        [SerializeField] private LayerMask ropeGroundLayers;
        [SerializeField] private float ropeThickness = 0.025f;
        [SerializeField] private HandSnapProfile ropeProfile;

        [Header("Arc")]

        // What a bolt stops at. Filled in from the layer names if left
        // empty - see DefaultArcLayers().
        [SerializeField] private LayerMask arcLayers;

        // Finding where the arc lands: one physics ray per this many
        // seconds of flight. Drawing it: straight pieces about this long,
        // in metres.
        [SerializeField] private float arcCastTimeStep = 0.1f;
        [SerializeField] private float arcSegmentLength = 0.3f;

        // How the arc is drawn, as for an aimed throw.
        [SerializeField] private ThrowArc.Style arcStyle = ThrowArc.Style.Line;
        [SerializeField] private float arcWidth = 0.008f;
        [SerializeField] private float dotRadius = 0.012f;
        [SerializeField] private float dotSpacing = 0.4f;
        [SerializeField] private float landingMarkerRadius = 0.08f;
        [SerializeField] private Color validColor = Color.white;
        [SerializeField] private Color invalidColor = new(1f, 0.2f, 0.2f, 0.5f);

        [Header("Bolts")]

        // How many bolts exist: the most that can be in flight or lying
        // where they landed at once. The oldest is reused after that.
        [SerializeField] private int boltCount = 8;

        // A bolt: a long thin box, in metres.
        [SerializeField] private float boltLength = 0.2f;
        [SerializeField] private float boltThickness = 0.008f;

        // Seconds a bolt lies where it landed before it's put away.
        [SerializeField] private float boltLifetime = 30f;

        [Header("Haptics")]

        // The crossbow becoming active, a shot, a pull with no bolts of
        // the chosen kind left, and the wheel clicking on to the next
        // kind (also felt as a hand takes hold of it).
        [SerializeField] private float activateAmplitude = 0.3f;
        [SerializeField] private float activateDuration = 0.04f;
        [SerializeField] private float fireAmplitude = 0.8f;
        [SerializeField] private float fireDuration = 0.08f;
        [SerializeField] private float emptyAmplitude = 0.2f;
        [SerializeField] private float emptyDuration = 0.03f;
        [SerializeField] private float wheelAmplitude = 0.5f;
        [SerializeField] private float wheelDuration = 0.04f;

        // Seconds after which a bolt that has hit nothing is put away,
        // as a multiple of the time it takes to reach the range.
        private const float FlightTimeMargin = 1.5f;

        // How many kinds of bolt there are (Inventory.BoltType).
        private const int BoltKinds = 3;

        // How far past where a bolt is due to land its last ray reaches,
        // in metres, to be sure of finding what it landed on.
        private const float LandingProbe = 0.05f;

        private bool _hasHaptics;

        // Which hand wears the crossbow, and that hand's visual.
        private bool _isLeftHand;
        private Transform _visual;

        // The crossbow, its wheel, and the bolt lying on it while it's
        // wound.
        private Transform _crossbow;
        private Transform _wheel;
        private MeshFilter _loadedBoltFilter;
        private MeshRenderer _loadedBolt;
        private Mesh _crossbowMesh;
        private Mesh _wheelMesh;
        private Mesh _ropeMesh;
        private Material _material;

        // One bolt mesh per kind, indexed by the kind's number.
        private readonly Mesh[] _boltMeshes = new Mesh[BoltKinds];

        // The arc, and its points this frame (world space). The array is
        // made once and reused.
        private ThrowArc _arc;
        private Vector3[] _points;
        private int _pointCount;

        // A bolt's speed, how long a bolt may fly, and the seconds of
        // flight one drawn piece of the arc covers - all worked out once,
        // from the range.
        private float _speed;
        private float _maxFlightTime;
        private float _arcTimeStep;

        // True while the crossbow is active (grip held since it became
        // so).
        private bool _isActive;

        // Grip as it was last frame: the crossbow becomes active on the
        // frame grip is pressed, not for a hand that arrives gripping.
        private bool _wasGrabbing;

        // True once the trigger has been let out since the last shot (or
        // since becoming active): a held trigger shoots once.
        private bool _isTriggerReady;

        // Seconds until the crossbow is wound again. 0 = ready.
        private float _reloadTimer;

        // The kind of bolt the wheel is set to.
        private BoltType _selected = BoltType.Water;

        // What the other hand's ray targets to take the wheel.
        private CrossbowWheel _wheelTarget;

        // The other hand and the wheel: whether it's holding the wheel,
        // and how its controller was twisted when the wheel last clicked
        // (the next click comes a step's turn from there).
        private bool _isOnWheel;

        // That hand, while it holds the wheel: its controller's rotation
        // last frame (each frame's twist is measured from it), how far it
        // has twisted since it took hold (the hand seen turns by this)
        // and since the last click; and whether it was pulled off and
        // must let go of grip before taking the wheel again.
        private Quaternion _wheelHandRotation;
        private float _wheelHandTurn;
        private float _wheelTurnSinceClick;
        private bool _wheelNeedsRegrip;

        // The pool of bolts. For each: its object and its mesh; its kind;
        // whether it's in flight; where and how fast it left, how long it
        // has flown and when it lands; whether it lands on something, and
        // where; once landed, how much longer it lies there; and for a
        // noisemaker, how much longer it sounds and when it next does.
        private Transform _boltRoot;
        private Transform[] _bolts;
        private MeshFilter[] _boltFilters;
        private BoltType[] _boltTypes;
        private bool[] _isFlying;
        private Vector3[] _boltStarts;
        private Vector3[] _boltVelocities;
        private float[] _boltTimes;
        private float[] _boltEndTimes;
        private bool[] _boltLands;
        private Vector3[] _boltLandings;
        private float[] _boltRestTimes;
        private float[] _boltNoiseTimes;
        private float[] _boltNextNoises;
        private int _nextBolt;

        // The pool of ropes a rope bolt hangs, and each one's visible
        // rope (a box stretched to its length).
        private ClimbableRope[] _ropes;
        private Transform[] _ropeVisuals;
        private int _nextRope;

        /// True while that hand has the crossbow active, or is the other
        /// hand holding its wheel. The other hand systems leave it alone
        /// meanwhile (PlayerHandState).
        public bool IsLeftBusy => _isLeftHand ? _isActive : _isOnWheel;
        public bool IsRightBusy => _isLeftHand ? _isOnWheel : _isActive;

        /// The kind of bolt the wheel is set to.
        public BoltType SelectedBolt => _selected;

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
            playerHandInteraction = GetComponent<PlayerHandInteraction>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
            arcLayers = DefaultArcLayers();
            ropeGroundLayers = LayerMask.GetMask("Environment");
        }

        private void Awake()
        {
            if (playerInput == null) {
                playerInput = GetComponentInParent<PlayerInputXR>();
            }

            if (playerTracking == null) {
                playerTracking = GetComponentInParent<PlayerTracking>();
            }

            if (playerHandInteraction == null) {
                playerHandInteraction = GetComponent<PlayerHandInteraction>();
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

            if (arcLayers.value == 0) {
                arcLayers = DefaultArcLayers();
            }

            if (ropeGroundLayers.value == 0) {
                ropeGroundLayers = LayerMask.GetMask("Environment");
            }

            // A right-handed player wears the crossbow on the right hand
            // (the lockpicks are on the left); a left-handed one the other
            // way round.
            _isLeftHand = playerInput.IsLeftHanded;

            // The speed that carries a bolt exactly `range` metres over
            // level ground when shot at 45 degrees, the best angle: range
            // = speed x speed / gravity, so speed = the square root of
            // range x gravity.
            float gravity = Mathf.Max(Physics.gravity.magnitude, 0.01f);
            _speed = Mathf.Sqrt(Mathf.Max(range, 1f) * gravity);

            // That best shot is in the air for speed x the square root of
            // 2 / gravity seconds. A bolt still flying some way past that
            // has gone into the sky and is given up on.
            _maxFlightTime = _speed * 1.4142f / gravity * FlightTimeMargin;

            // A drawn piece's length as time, and enough slots for the
            // longest arc: one piece per time step, plus the start point
            // and one spare.
            _arcTimeStep = Mathf.Max(arcSegmentLength, 0.05f) / _speed;
            _points = new Vector3[Mathf.CeilToInt(_maxFlightTime / _arcTimeStep) + 2];

            _arc = ThrowArc.Create(transform, "Crossbow Arc", arcWidth, dotRadius, dotSpacing, landingMarkerRadius, validColor, invalidColor);
        }

        /// <summary>
        /// Makes the crossbow, the bolts and the ropes. Start() rather
        /// than Awake(): the crossbow goes on a hand visual, and
        /// PlayerHandVisuals copies each hand visual in its own Awake() to
        /// make the ghost hands - a crossbow already on the hand by then
        /// would be copied onto the ghost too. Every Awake() has run
        /// before the first Start().
        /// </summary>
        private void Start()
        {
            _visual = _isLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;
            _material = LockMeshBuilder.CreateMaterial(false, minLight);

            BuildBoltMeshes();
            BuildCrossbow();
            BuildBolts();
            BuildRopes();
            ShowLoadedBolt();
        }

        private void OnDisable()
        {
            // Null if Awake() hasn't run. And if the scene is being
            // unloaded (a level restart), everything here is on its way
            // out and mustn't be touched.
            if (_arc == null || !gameObject.scene.isLoaded) {
                return;
            }

            // Nothing may be left drawn with nothing ticking it.
            _isActive = false;
            _arc.Hide();

            if (_isOnWheel) {
                LetGoOfWheel(false);
            }
        }

        private void OnDestroy()
        {
            // Meshes and a material made from code aren't cleaned up with
            // their objects, and the bolts and ropes are at the scene
            // root, not under this object.
            DestroyMesh(_crossbowMesh);
            DestroyMesh(_wheelMesh);
            DestroyMesh(_ropeMesh);

            for (int i = 0; i < _boltMeshes.Length; i++) {
                DestroyMesh(_boltMeshes[i]);
            }

            if (_material != null) {
                Destroy(_material);
            }

            if (_boltRoot != null) {
                Destroy(_boltRoot.gameObject);
            }
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh != null) {
                Destroy(mesh);
            }
        }

        /// <summary>
        /// What a bolt can hit: the world, props, guards, and anything
        /// never given a layer. Not the player, the hands (or the props
        /// they carry), or climbables' grab volumes.
        /// </summary>
        private static LayerMask DefaultArcLayers()
        {
            return LayerMask.GetMask("Default", "Environment", "Interactable", "Guard");
        }

        /// <summary>
        /// Makes one bolt mesh per kind, so the kind loaded can be told at
        /// a glance. Every bolt is a thin shaft along Z; water and the
        /// noisemaker have a fat coloured tip, and the rope bolt has rope
        /// wound round the back half of its shaft.
        /// </summary>
        private void BuildBoltMeshes()
        {
            Vector3 shaft = new(boltThickness, boltThickness, boltLength);
            float tip = boltThickness * 2.5f;
            Vector3 tipSize = new(tip, tip, tip * 1.5f);
            Vector3 tipCentre = new(0f, 0f, boltLength * 0.5f);

            LockMeshBuilder water = new();
            water.Box(Vector3.zero, shaft, Quaternion.identity, metalColor);
            water.Box(tipCentre, tipSize, Quaternion.identity, waterColor);
            _boltMeshes[(int)BoltType.Water] = water.ToMesh("Water Bolt");

            LockMeshBuilder noisemaker = new();
            noisemaker.Box(Vector3.zero, shaft, Quaternion.identity, metalColor);
            noisemaker.Box(tipCentre, tipSize, Quaternion.identity, noisemakerColor);
            _boltMeshes[(int)BoltType.Noisemaker] = noisemaker.ToMesh("Noisemaker Bolt");

            LockMeshBuilder rope = new();
            float wound = boltThickness * 2.2f;
            rope.Box(Vector3.zero, shaft, Quaternion.identity, metalColor);
            rope.Box(new Vector3(0f, 0f, -boltLength * 0.2f), new Vector3(wound, wound, boltLength * 0.5f), Quaternion.identity, ropeColor);
            _boltMeshes[(int)BoltType.Rope] = rope.ToMesh("Rope Bolt");
        }

        /// <summary>
        /// Makes the crossbow, once: a stock and a bow across its front as
        /// boxes in one mesh, the wheel at its back as a separate object
        /// (so it can be seen to turn), and the bolt that lies on the
        /// stock while it's wound (so it can be hidden after a shot). The
        /// crossbow's own Z is the way it shoots, its Y its top. No
        /// colliders: it's only to look at.
        /// </summary>
        private void BuildCrossbow()
        {
            LockMeshBuilder builder = new();
            float halfLength = stockSize.z * 0.5f;

            builder.Box(Vector3.zero, stockSize, Quaternion.identity, stockColor);
            builder.Box(new Vector3(0f, 0f, halfLength - bowThickness), new Vector3(bowWidth, bowThickness, bowThickness), Quaternion.identity, metalColor);
            _crossbowMesh = builder.ToMesh("Crossbow");

            // On the back of the hand: pointing along the fingers (the
            // visual's -Y), its top away from the back of the hand (the
            // visual's -X), then turned by the tilt.
            _crossbow = MakePart("Crossbow", _crossbowMesh, _visual, out _).transform;
            _crossbow.SetLocalPositionAndRotation(onHandPosition, Quaternion.LookRotation(Vector3.down, Vector3.left) * Quaternion.Euler(onHandTilt));

            // The wheel: a flat block with a notch of rope colour on one
            // edge, so its turning can be seen.
            LockMeshBuilder wheelBuilder = new();
            wheelBuilder.Box(Vector3.zero, new Vector3(wheelSize, wheelSize, wheelSize * 0.4f), Quaternion.identity, metalColor);
            wheelBuilder.Box(new Vector3(0f, wheelSize * 0.5f, 0f), new Vector3(wheelSize * 0.2f, wheelSize * 0.2f, wheelSize * 0.45f), Quaternion.identity, ropeColor);
            _wheelMesh = wheelBuilder.ToMesh("Crossbow Wheel");

            _wheel = MakePart("Wheel", _wheelMesh, _crossbow, out _).transform;
            _wheel.localPosition = new Vector3(0f, 0f, -halfLength - wheelSize * 0.3f);

            // What a hand ray finds: a trigger ball round the wheel, on
            // its own object (the wheel itself turns) and on the
            // Interactable layer, which hand rays look at. It moves with
            // the hand, and a Rigidbody tells physics it's a moving thing,
            // which is much cheaper than moving a bare collider.
            GameObject grab = new("Wheel Grab");
            int interactable = LayerMask.NameToLayer("Interactable");

            if (interactable >= 0) {
                grab.layer = interactable;
            }

            grab.transform.SetParent(_crossbow, false);
            grab.transform.localPosition = _wheel.localPosition;

            Rigidbody body = grab.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            SphereCollider ball = grab.AddComponent<SphereCollider>();
            ball.isTrigger = true;
            ball.radius = wheelGrabRadius;

            _wheelTarget = grab.AddComponent<CrossbowWheel>();
            _wheelTarget.SetUp(ball, playerTracking, _isLeftHand);

            // The loaded bolt, lying along the top of the stock.
            GameObject loaded = MakePart("Loaded Bolt", _boltMeshes[(int)_selected], _crossbow, out _loadedBoltFilter);
            _loadedBolt = loaded.GetComponent<MeshRenderer>();
            loaded.transform.localPosition = new Vector3(0f, (stockSize.y + boltThickness) * 0.5f, 0f);
        }

        /// <summary>
        /// Makes the pool of bolts, once, under one object at the scene
        /// root (a bolt in flight mustn't move with the player), all
        /// switched off until shot.
        /// </summary>
        private void BuildBolts()
        {
            int count = Mathf.Max(boltCount, 1);

            _boltRoot = new GameObject("Crossbow Bolts").transform;
            _bolts = new Transform[count];
            _boltFilters = new MeshFilter[count];
            _boltTypes = new BoltType[count];
            _isFlying = new bool[count];
            _boltStarts = new Vector3[count];
            _boltVelocities = new Vector3[count];
            _boltTimes = new float[count];
            _boltEndTimes = new float[count];
            _boltLands = new bool[count];
            _boltLandings = new Vector3[count];
            _boltRestTimes = new float[count];
            _boltNoiseTimes = new float[count];
            _boltNextNoises = new float[count];

            for (int i = 0; i < count; i++) {
                GameObject bolt = MakePart($"Bolt {i}", _boltMeshes[0], _boltRoot, out _boltFilters[i]);
                bolt.SetActive(false);
                _bolts[i] = bolt.transform;
            }
        }

        /// <summary>
        /// Makes the pool of ropes, once: each a ClimbableRope (its grab
        /// volume is made by the component itself) on the Climbable layer
        /// with a thin box under it to be seen, a metre long and stretched
        /// to the rope's length when it's hung. All switched off until a
        /// rope bolt lands in wood.
        /// </summary>
        private void BuildRopes()
        {
            int count = Mathf.Max(ropeCount, 1);
            int climbableLayer = LayerMask.NameToLayer("Climbable");

            LockMeshBuilder builder = new();
            builder.Box(new Vector3(0f, -0.5f, 0f), new Vector3(ropeThickness, 1f, ropeThickness), Quaternion.identity, ropeColor);
            _ropeMesh = builder.ToMesh("Bolt Rope");

            _ropes = new ClimbableRope[count];
            _ropeVisuals = new Transform[count];

            for (int i = 0; i < count; i++) {
                GameObject ropeObject = new($"Bolt Rope {i}");

                // The layer first: the rope checks it as it's added.
                if (climbableLayer >= 0) {
                    ropeObject.layer = climbableLayer;
                }

                ropeObject.transform.SetParent(_boltRoot, false);

                ClimbableRope rope = ropeObject.AddComponent<ClimbableRope>();
                rope.SetSnapProfile(ropeProfile);

                _ropeVisuals[i] = MakePart("Rope", _ropeMesh, ropeObject.transform, out _).transform;
                _ropes[i] = rope;
                ropeObject.SetActive(false);
            }
        }

        /// <summary>
        /// One drawn piece: an object under parent with a mesh, the shared
        /// material and no shadows (small things that don't need them).
        /// </summary>
        private GameObject MakePart(string partName, Mesh mesh, Transform parent, out MeshFilter filter)
        {
            GameObject part = new(partName);
            part.transform.SetParent(parent, false);

            filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterial = _material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return part;
        }

        /// <summary>
        /// The other hand taking and letting go of the wheel, the way a
        /// hand takes a door handle: its ray is on the wheel (the reticle
        /// shows it), it's free, and grip is held - then its hand visual
        /// snaps onto the wheel. It keeps hold until grip is let go.
        /// Called by PlayerController after the hand rays and before the
        /// other grab systems.
        /// </summary>
        public void TickWheel()
        {
            bool isOtherLeft = !_isLeftHand;
            bool isGrabbing = isOtherLeft ? playerInput.IsLeftGrabbing : playerInput.IsRightGrabbing;

            if (_isOnWheel) {
                if (!isGrabbing) {
                    LetGoOfWheel(false);
                }

                return;
            }

            // A hand pulled off the wheel can't take it again until its
            // grip has been let go.
            if (!isGrabbing) {
                _wheelNeedsRegrip = false;
                return;
            }

            if (_wheelNeedsRegrip || playerHandState.IsBusyExcept(isOtherLeft, HandUse.Crossbow)) {
                return;
            }

            IHandTarget target = isOtherLeft ? playerHandInteraction.LeftTarget : playerHandInteraction.RightTarget;

            // The same object, not merely an equal one: is this hand's
            // ray on the wheel?
            if (!ReferenceEquals(target, _wheelTarget)) {
                return;
            }

            _isOnWheel = true;
            _wheelHandTurn = 0f;
            _wheelTurnSinceClick = 0f;
            _wheelHandRotation = isOtherLeft ? playerTracking.LeftHandRotation : playerTracking.RightHandRotation;

            WheelSnap().Snap(WheelSnapPose(isOtherLeft));
            Pulse(isOtherLeft, wheelAmplitude, wheelDuration);
        }

        /// <summary>
        /// Takes the other hand off the wheel: its hand visual blends
        /// back to the controller. With byForce, it can't take the wheel
        /// again until its grip has been let go.
        /// </summary>
        private void LetGoOfWheel(bool byForce)
        {
            _isOnWheel = false;
            _wheelNeedsRegrip = byForce;
            WheelSnap().Release();
        }

        /// <summary>
        /// The other hand's snap, owned by PlayerHandVisuals. Looked up
        /// when needed rather than kept from Awake(): PlayerHandVisuals
        /// makes them in its own Awake(), which may run after this
        /// class's.
        /// </summary>
        private HandVisualSnap WheelSnap()
        {
            return _isLeftHand ? playerHandVisuals.RightVisualSnap : playerHandVisuals.LeftVisualSnap;
        }

        /// <summary>
        /// The way the crossbow shoots, in the world: the axis the wheel
        /// turns about. TransformVector() rather than .forward, since the
        /// crossbow may be under the mirrored right hand.
        /// </summary>
        private Vector3 WheelAxis()
        {
            return _crossbow.TransformVector(Vector3.forward).normalized;
        }

        /// <summary>
        /// Where the hand visual goes to hold the wheel: on the wheel,
        /// facing down onto it from the crossbow's top, then turned about
        /// the wheel's axis by as far as the hand has twisted since it
        /// took hold - so the hand seen turns with the real one.
        /// </summary>
        private HandSnapPose WheelSnapPose(bool isLeftHand)
        {
            Vector3 axis = WheelAxis();
            Vector3 top = _crossbow.TransformVector(Vector3.up).normalized;

            Vector3 gripPosition = _wheel.position;
            Quaternion gripRotation = Quaternion.AngleAxis(_wheelHandTurn, axis) * Quaternion.LookRotation(-top, axis);

            return wheelProfile == null
                ? new HandSnapPose(gripPosition, gripRotation, default)
                : wheelProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }

        /// <summary>
        /// The hand on the wheel: how far its wrist has twisted about the
        /// wheel's axis this frame is added up, and each step's worth
        /// moves on to the next kind of bolt (or back to the last), with
        /// a click. The snapped hand follows the wheel as the crossbow
        /// hand moves, and is let go by force if the real hand strays too
        /// far.
        /// </summary>
        private void TickHeldWheel()
        {
            bool isOtherLeft = !_isLeftHand;
            Quaternion rotation = isOtherLeft ? playerTracking.LeftHandRotation : playerTracking.RightHandRotation;
            Vector3 position = isOtherLeft ? playerTracking.LeftHandPosition : playerTracking.RightHandPosition;

            // This frame's turn of the controller, and the part of it
            // that is a twist about the wheel's axis: a rotation's x, y
            // and z are its axis scaled by the sine of half its angle and
            // its w the cosine, so the share along our axis over w is the
            // tangent of half the twist.
            Quaternion frameTurn = rotation * Quaternion.Inverse(_wheelHandRotation);
            Vector3 axis = WheelAxis();
            float along = frameTurn.x * axis.x + frameTurn.y * axis.y + frameTurn.z * axis.z;
            float twist = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(along, frameTurn.w) * Mathf.Rad2Deg);

            _wheelHandRotation = rotation;
            _wheelHandTurn += twist;
            _wheelTurnSinceClick += twist;

            if (Mathf.Abs(_wheelTurnSinceClick) >= wheelStepAngle) {
                int direction = _wheelTurnSinceClick > 0f ? 1 : -1;

                // The next click is measured from where this one happened.
                _wheelTurnSinceClick -= direction * wheelStepAngle;
                _selected = (BoltType)(((int)_selected + direction + BoltKinds) % BoltKinds);

                // A third of a turn of the wheel per kind.
                _wheel.localRotation = Quaternion.Euler(0f, 0f, (int)_selected * (360f / BoltKinds));

                ShowLoadedBolt();
                Pulse(isOtherLeft, wheelAmplitude, wheelDuration);
            }

            HandSnapPose pose = WheelSnapPose(isOtherLeft);
            WheelSnap().SetSnapPose(pose.Position, pose.Rotation);

            if ((position - _wheel.position).sqrMagnitude > wheelBreakDistance * wheelBreakDistance) {
                LetGoOfWheel(true);
            }
        }

        /// <summary>
        /// Shows the chosen kind of bolt on the stock - if the crossbow is
        /// wound and there is one to load.
        /// </summary>
        private void ShowLoadedBolt()
        {
            _loadedBoltFilter.sharedMesh = _boltMeshes[(int)_selected];
            _loadedBolt.enabled = _reloadTimer <= 0f && HasBolt(_selected);
        }

        /// <summary>
        /// Whether the player has a bolt of a kind. With no inventory in
        /// the scene (a test scene), always.
        /// </summary>
        private static bool HasBolt(BoltType kind)
        {
            PlayerInventory inventory = PlayerInventory.Instance;
            return inventory == null || inventory.GetBoltCount(kind) > 0;
        }

        /// <summary>
        /// Becoming active and stopping. Called by PlayerController after
        /// the grab systems (climbing, carrying, doors), so a hand that
        /// grabbed something this frame is already busy: a grab always
        /// wins over the crossbow.
        /// </summary>
        public void Tick()
        {
            bool isGrabbing = _isLeftHand ? playerInput.IsLeftGrabbing : playerInput.IsRightGrabbing;

            if (_isActive) {
                // Active for as long as grip is held, however the hand is
                // turned.
                if (!isGrabbing) {
                    _isActive = false;
                    _arc.Hide();
                }
            } else if (isGrabbing && !_wasGrabbing && CanActivate()) {
                _isActive = true;

                // A trigger already held when the crossbow comes up must
                // be let out before it can shoot.
                _isTriggerReady = false;
                Pulse(_isLeftHand, activateAmplitude, activateDuration);
            }

            _wasGrabbing = isGrabbing;
        }

        /// <summary>
        /// Whether the crossbow may become active now: the hand is doing
        /// nothing else, its ray isn't on anything it could grab, and its
        /// palm faces the floor.
        /// </summary>
        private bool CanActivate()
        {
            if (playerHandState.IsBusyExcept(_isLeftHand, HandUse.Crossbow)) {
                return false;
            }

            IHandTarget target = _isLeftHand ? playerHandInteraction.LeftTarget : playerHandInteraction.RightTarget;

            if (target is not null) {
                return false;
            }

            // The palm faces along the hand visual's own +X. The right
            // visual is the left one mirrored (x scale -1), so its palm
            // faces the other way in the world: TransformVector() takes
            // the scale - and so the mirroring - into account, where
            // TransformDirection() would ignore it and give the back of
            // the right hand. The dot product with straight down is the
            // cosine of the angle between them: 1 when the palm faces the
            // floor.
            Vector3 palm = _visual.TransformVector(Vector3.right).normalized;

            return Vector3.Dot(palm, Vector3.down) >= Mathf.Cos(palmDownAngle * Mathf.Deg2Rad);
        }

        /// <summary>
        /// The wheel, the arc, shooting, the clockwork and bolts in
        /// flight. Called by PlayerController after the hand visuals are
        /// placed, on every path through Update(), so a bolt already shot
        /// keeps flying through a mantle. canAim is false during a mantle
        /// and once the level has ended: nothing is aimed or shot then.
        /// </summary>
        public void TickHeld(bool canAim)
        {
            float deltaTime = Time.deltaTime;

            TickBolts(deltaTime);

            // The clockwork winding: when it's done, the next bolt shows
            // on the stock.
            if (_reloadTimer > 0f) {
                _reloadTimer -= deltaTime;

                if (_reloadTimer <= 0f) {
                    ShowLoadedBolt();
                }
            }

            if (_isOnWheel) {
                TickHeldWheel();
            }

            if (!_isActive) {
                return;
            }

            if (!canAim) {
                _arc.Hide();
                return;
            }

            // The bolt leaves from just ahead of the stock, the way the
            // crossbow points - so what's shown is what's shot.
            // TransformVector() rather than .forward, for the same reason
            // as the palm: under the mirrored right hand it's the one that
            // follows the model as drawn.
            Vector3 forward = _crossbow.TransformVector(Vector3.forward).normalized;
            Vector3 start = _crossbow.position + forward * muzzleDistance;
            Vector3 velocity = forward * _speed;

            // A bolt is moved along this same sum, not by physics, so no
            // allowance for physics' stepped gravity (the 0).
            bool lands = BallisticArc.Compute(start, velocity, arcLayers, arcCastTimeStep, _maxFlightTime, _arcTimeStep, 0f, _points, out _pointCount, out float flightTime, out Vector3 landingPoint, out Vector3 landingNormal);

            // Red with no landing disc when the bolt would come down on
            // nothing. It can still be shot.
            _arc.Show(_points, _pointCount, arcStyle, lands, landingPoint, landingNormal, playerTracking.HeadPosition);

            float trigger = _isLeftHand ? playerInput.LeftTrigger : playerInput.RightTrigger;

            if (!_isTriggerReady) {
                if (trigger <= rearmTrigger) {
                    _isTriggerReady = true;
                }

                return;
            }

            // Shot on the press. Not wound yet: the pull is used up and
            // nothing happens.
            if (trigger >= fireTrigger) {
                _isTriggerReady = false;

                if (_reloadTimer <= 0f) {
                    Fire(start, velocity, flightTime, lands, landingPoint);
                }
            }
        }

        /// <summary>
        /// Shoots, if the player has a bolt of the chosen kind: one is
        /// taken from the inventory, the next bolt in the pool (the
        /// oldest, if all are out) is put at the start of the arc and set
        /// flying, and the clockwork starts winding. With none left, only
        /// a faint click.
        /// </summary>
        private void Fire(Vector3 start, Vector3 velocity, float flightTime, bool lands, Vector3 landingPoint)
        {
            PlayerInventory inventory = PlayerInventory.Instance;

            if (inventory != null && !inventory.TryUseBolt(_selected)) {
                Pulse(_isLeftHand, emptyAmplitude, emptyDuration);
                return;
            }

            int index = _nextBolt;
            _nextBolt = (_nextBolt + 1) % _bolts.Length;

            _boltTypes[index] = _selected;
            _isFlying[index] = true;
            _boltStarts[index] = start;
            _boltVelocities[index] = velocity;
            _boltTimes[index] = 0f;
            _boltEndTimes[index] = flightTime;
            _boltLands[index] = lands;
            _boltLandings[index] = landingPoint;
            _boltRestTimes[index] = 0f;
            _boltNoiseTimes[index] = 0f;

            Transform bolt = _bolts[index];
            _boltFilters[index].sharedMesh = _boltMeshes[(int)_selected];
            bolt.SetPositionAndRotation(start, Quaternion.LookRotation(velocity));
            bolt.gameObject.SetActive(true);

            _reloadTimer = reloadTime;
            _loadedBolt.enabled = false;

            Pulse(_isLeftHand, fireAmplitude, fireDuration);
            PlayCue(fireCue, start);
        }

        /// <summary>
        /// Moves every bolt in flight along its arc; counts down the ones
        /// lying where they landed, and puts them away when their time is
        /// up; and sounds the noisemakers.
        /// </summary>
        private void TickBolts(float deltaTime)
        {
            Vector3 gravity = Physics.gravity;

            for (int i = 0; i < _bolts.Length; i++) {
                Transform bolt = _bolts[i];

                if (!_isFlying[i]) {
                    TickLandedBolt(i, bolt, deltaTime);
                    continue;
                }

                Vector3 previous = bolt.position;
                float time = _boltTimes[i] + deltaTime;
                bool hasArrived = time >= _boltEndTimes[i];

                // The end of the arc worked out when it was shot: where it
                // was going to land, or nowhere.
                if (hasArrived && !_boltLands[i]) {
                    _isFlying[i] = false;
                    bolt.gameObject.SetActive(false);
                    continue;
                }

                Vector3 next = hasArrived
                    ? _boltLandings[i]
                    : BallisticArc.Point(_boltStarts[i], _boltVelocities[i], gravity, time, 0f);

                // Which way it's travelling now: its starting speed plus
                // what gravity has added since.
                Vector3 heading = (_boltVelocities[i] + gravity * Mathf.Min(time, _boltEndTimes[i])).normalized;

                // One ray along this frame's move, in case something has
                // come into its way since the arc was worked out (a door
                // swung shut, a guard walked into it). On arriving it
                // reaches a little further, to be sure of finding what
                // the bolt has landed on.
                Vector3 castEnd = hasArrived ? next + heading * LandingProbe : next;
                Collider landedOn = null;

                if (Physics.Linecast(previous, castEnd, out RaycastHit hit, arcLayers, QueryTriggerInteraction.Ignore)) {
                    next = hit.point;
                    landedOn = hit.collider;
                    hasArrived = true;
                }

                bolt.SetPositionAndRotation(next, Quaternion.LookRotation(heading));
                _boltTimes[i] = time;

                if (hasArrived) {
                    _isFlying[i] = false;
                    _boltRestTimes[i] = boltLifetime;
                    PlayCue(impactCue, next);
                    Land(i, bolt, next, heading, landedOn);
                }
            }
        }

        /// <summary>
        /// A bolt has landed: what its kind does there.
        /// </summary>
        private void Land(int index, Transform bolt, Vector3 point, Vector3 heading, Collider landedOn)
        {
            switch (_boltTypes[index]) {
                case BoltType.Water:
                    // Every fire close by goes out.
                    Flame.ExtinguishNear(point, waterRadius);
                    break;

                case BoltType.Noisemaker:
                    // Sounds at once, then every so often for a while.
                    _boltNoiseTimes[index] = noiseDuration;
                    _boltNextNoises[index] = 0f;
                    break;

                case BoltType.Rope:
                    // Only wood holds it. Anything else, and the bolt is
                    // lost.
                    if (landedOn != null && SurfaceTag.Of(landedOn) == SurfaceType.Wood) {
                        HangRope(point - heading * LandingProbe);
                    } else {
                        _boltRestTimes[index] = 0f;
                        bolt.gameObject.SetActive(false);
                    }

                    break;
            }
        }

        /// <summary>
        /// Hangs the next rope in the pool (the oldest, if all are out)
        /// from a point, down to the ground beneath it or to its longest.
        /// </summary>
        private void HangRope(Vector3 top)
        {
            int index = _nextRope;
            _nextRope = (_nextRope + 1) % _ropes.Length;

            // How far down the ground is: one ray, straight down.
            float length = Physics.Raycast(top, Vector3.down, out RaycastHit ground, ropeMaxLength, ropeGroundLayers, QueryTriggerInteraction.Ignore)
                ? ground.distance
                : ropeMaxLength;

            ClimbableRope rope = _ropes[index];
            rope.transform.SetPositionAndRotation(top, Quaternion.identity);
            rope.gameObject.SetActive(true);
            rope.SetLength(length);

            // The rope seen: the metre-long box, stretched to the length.
            _ropeVisuals[index].localScale = new Vector3(1f, rope.Length, 1f);
        }

        /// <summary>
        /// A bolt lying where it landed: a noisemaker sounds, and any
        /// bolt is put away when its time is up.
        /// </summary>
        private void TickLandedBolt(int index, Transform bolt, float deltaTime)
        {
            if (_boltNoiseTimes[index] > 0f) {
                _boltNoiseTimes[index] -= deltaTime;
                _boltNextNoises[index] -= deltaTime;

                if (_boltNextNoises[index] <= 0f) {
                    _boltNextNoises[index] = noiseInterval;
                    MakeNoise(bolt);
                }
            }

            if (_boltRestTimes[index] > 0f) {
                _boltRestTimes[index] -= deltaTime;

                if (_boltRestTimes[index] <= 0f) {
                    _boltNoiseTimes[index] = 0f;
                    bolt.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// One sound from a noisemaker: its cue if it has one (which also
        /// tells the guards), otherwise just the noise guards hear.
        /// </summary>
        private void MakeNoise(Transform bolt)
        {
            Vector3 position = bolt.position;
            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (noiseCue != null && soundPlayer != null) {
                soundPlayer.Play(noiseCue, position, bolt);
            } else {
                NoiseSystem.Emit(position, noiseRadius, NoiseType.Distraction, bolt);
            }
        }

        /// <summary>
        /// Plays a cue (and emits its noise for guards), if there is one.
        /// </summary>
        private void PlayCue(SoundCue cue, Vector3 position)
        {
            if (cue == null) {
                return;
            }

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(cue, position, transform);
            } else {
                cue.EmitNoise(position, transform);
            }
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
