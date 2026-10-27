using AI;
using Core;
using Interaction;
using Inventory;
using UnityEngine;
using UnityEngine.Serialization;

namespace Player
{
    /// <summary>
    /// The bow's arrows once they leave the string, and the arc showing
    /// where one would go. PlayerBow decides when an arrow is shot, which
    /// way and how hard; everything after that is here.
    ///
    /// There are six kinds of arrow (Inventory.BoltType), each with its
    /// own model (Assets/TripoModels/arrows_3d_model). None of them kills:
    /// - Water (blue glass head) puts out any fire (Core.Flame) near where
    ///   it lands.
    /// - A noisemaker (bells) makes noise where it lands for a while, to
    ///   draw guards.
    /// - A rope arrow (grapple wound with rope) that sticks hangs a rope
    ///   to climb.
    /// - A stun arrow (blunt metal head) that hits a guard on the head
    ///   leaves it stunned for a while; then it comes round and searches.
    /// - A sleep dart (green vial) that hits a guard anywhere knocks it
    ///   out after a wait.
    /// - A moss arrow (head overgrown with moss) lays a patch of moss on
    ///   the floor where it lands, or below it if it hits a wall: the
    ///   player's footsteps on it are silent (PlayerFootsteps asks
    ///   IsOnMoss()). Patches stay for the rest of the level.
    ///
    /// Water, noisemaker and moss arrows are used up where they land. The rest
    /// stick into what holds an arrow (wood, plaster, carpet, grass - see
    /// Core.SurfaceTag.HoldsBolt()) and bounce off everything else, a
    /// guard included: an arrow that bounces becomes a real physics object
    /// for as long as it takes to fall and settle. A stun arrow, or a
    /// sleep dart that missed, can then be picked up again - a hand
    /// points at it and grips, and it goes straight back into the
    /// inventory. A rope arrow never can: stuck in something marked as
    /// supporting one, its rope is climbed as usual; stuck in anything
    /// else, it pulls out and the rope falls as soon as a hand takes
    /// hold.
    ///
    /// An arrow isn't a physics object in flight. It's moved along the
    /// same sum the arc was drawn from, so it goes exactly where the arc
    /// showed, with one physics ray a frame in case something has moved
    /// into its way. An arrow's own point (its origin) is its tip: that's
    /// what flies the arc and what lands. Arrows, ropes and moss come from
    /// small pools made at load: nothing is created or destroyed while
    /// playing.
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// TickPickup() (either hand picking an arrow up) before the grab
    /// systems, and TickFlight() (arrows in flight, settling, sounding,
    /// and ropes) on every path through its Update().
    /// </summary>
    public class PlayerArrows : MonoBehaviour
    {
        [Tooltip("The input system on the Player root: grip presses are read through it to pick a lying arrow up, e.g. drag the Player root here. Found on the Player root above if left empty.")]
        [SerializeField] private PlayerInputXR playerInput;
        [Tooltip("The hand rays on this Hands object: a hand picks up the resting arrow its ray is on, e.g. drag the Hands object here. Found on this object if left empty.")]
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [Tooltip("The hand visuals on this Hands object, used to find the Player Hand State, e.g. drag the Hands object here. Found on this object if left empty.")]
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // What each hand is busy with: only a free hand picks an arrow up.
        // Found (or made) in Awake() if not wired (it's on this same
        // object).
        [Tooltip("What each hand is busy with: only a free hand picks an arrow up, e.g. a hand carrying a crate ignores arrows. Found (or added) on this object if left empty.")]
        [SerializeField] private PlayerHandState playerHandState;

        // Optional: a rig with no haptics still shoots.
        [Tooltip("The controller vibration on the Player root, e.g. for the buzz when an arrow is picked up. Found on the Player root above if left empty; with none, arrows still work, just without buzzes.")]
        [SerializeField] private PlayerHaptics playerHaptics;

        // Optional: tells when a hand has taken hold of a rope that won't
        // bear weight, and makes it let go.
        [Tooltip("The climbing on the Player root: tells when a hand has taken hold of a rope whose arrow won't bear weight, and makes it let go, e.g. a rope arrow in stone falls as soon as it's grabbed. Found on the Player root above if left empty; with none, such ropes never fall.")]
        [SerializeField] private PlayerClimbing playerClimbing;

        // Optional: an arrow picked up burns away where it lay. Without
        // one it just vanishes. Found in Awake() if not wired (it's on this
        // same object).
        [Tooltip("The tool burn effect on this Hands object: an arrow picked up burns away where it lay, e.g. drag the Hands object here. Found on this object if left empty; with none, a picked-up arrow just vanishes.")]
        [SerializeField] private ToolBurn toolBurn;

        [Header("Shooting")]

        // How fast an arrow leaves the bow at full draw, in metres a second
        // (a real bow shoots at about 50-70). The draw alone sets a shot's
        // speed: one drawn half way leaves at half this, whatever the
        // angle it's shot at.
        [Tooltip("Speed of an arrow shot at full draw, in metres per second (a real bow: about 50-70). A shot's speed is this times how far the string was drawn (0-1), whatever the angle, e.g. 60 = an arrow drawn half way leaves at 30 m/s. Higher = flatter, longer shots; lower = more of a lob.")]
        [SerializeField] private float fullDrawSpeed = 60f;

        // Seconds an arrow may fly before it's taken to have gone into the
        // sky and is put away. Also how far ahead the arc looks, so it
        // bounds the arc's ray casts per frame.
        [Tooltip("Seconds an arrow may fly before it's given up on (gone into the sky) and put away; also how far ahead the arc is worked out each frame, e.g. 4 = an arrow still flying after 4 s vanishes. Higher = longer shots can land, but more ray casts a frame while aiming; lower = cheaper, but long shots are lost. At least 0.5.")]
        [SerializeField] private float maxFlightTime = 4f;

        // The sound of a shot, and of an arrow landing. Optional.
        [Tooltip("The sound played (and noise made for guards) where the arrow leaves the bow, e.g. a bowstring twang cue. Left empty, shots are silent.")]
        [SerializeField] private SoundCue shotCue;
        [Tooltip("The sound played (and noise made for guards) where an arrow lands, e.g. a thud cue. Left empty, landings are silent.")]
        [SerializeField] private SoundCue impactCue;

        [Header("Arrow Kinds")]

        // Each kind's model: a mesh lying along its own Z with its tip at
        // +Z and its middle on its origin (the arrows_3d_model_* files),
        // and the one material they all share.
        [Tooltip("The water arrow's mesh: along its own Z, tip at +Z, middle on its origin, e.g. the mesh in arrows_3d_model_water.fbx. Left empty, that kind can't be seen (a warning is logged).")]
        [SerializeField] private Mesh waterArrow;
        [Tooltip("The noisemaker arrow's mesh: along its own Z, tip at +Z, middle on its origin, e.g. the mesh in arrows_3d_model_noisemaker.fbx. Left empty, that kind can't be seen (a warning is logged).")]
        [SerializeField] private Mesh noisemakerArrow;
        [Tooltip("The rope arrow's mesh: along its own Z, tip at +Z, middle on its origin, e.g. the mesh in arrows_3d_model_rope.fbx. Left empty, that kind can't be seen (a warning is logged).")]
        [SerializeField] private Mesh ropeArrow;
        [Tooltip("The stun arrow's mesh: along its own Z, tip at +Z, middle on its origin, e.g. the mesh in arrows_3d_model_stun.fbx. Left empty, that kind can't be seen (a warning is logged).")]
        [SerializeField] private Mesh stunArrow;
        [Tooltip("The sleep dart's mesh: along its own Z, tip at +Z, middle on its origin, e.g. the mesh in arrows_3d_model_sleep.fbx. Left empty, that kind can't be seen (a warning is logged).")]
        [SerializeField] private Mesh sleepDart;
        [Tooltip("The moss arrow's mesh: along its own Z, tip at +Z, middle on its origin, e.g. the mesh in arrows_3d_model_moss.fbx. Left empty, that kind can't be seen (a warning is logged).")]
        [SerializeField] private Mesh mossArrow;
        [Tooltip("The one material every arrow kind is drawn with, e.g. arrows_3d_model.mat (URP Lit). Left empty, a warning is logged.")]
        [SerializeField] private Material arrowMaterial;

        // Stun: seconds a guard hit on the head stays stunned.
        [Tooltip("Seconds a guard hit on the head by a stun arrow stays stunned before it comes round and searches, e.g. 10 = out for ten seconds. Higher = more time to slip past.")]
        [SerializeField] private float stunTime = 10f;

        // Sleep dart: seconds after it hits a guard before the guard is
        // knocked out.
        [Tooltip("Seconds after a sleep dart hits a guard (anywhere) before the guard is knocked out, e.g. 10 = it walks on for ten seconds, then drops. Higher = a longer wait in which it may still raise the alarm; lower = it drops sooner.")]
        [SerializeField] private float sleepDelay = 10f;

        // Water: every fire within this many metres of where it lands goes
        // out.
        [Tooltip("Metres round where a water arrow lands within which every fire goes out, e.g. 0.6 = it must hit within 60 cm of a torch's flame. Higher = easier to douse; lower = needs a closer hit.")]
        [SerializeField] private float waterRadius = 0.6f;

        // Noisemaker: how far its noise carries to guards (metres), how
        // often it sounds and for how long (seconds), and the sound it
        // makes (optional: with none it's a noise guards hear and the
        // player doesn't).
        [Tooltip("Metres a noisemaker's noise carries to guards, used only when no Noise Cue is set (a cue makes its own noise), e.g. 12 = guards within 12 m come to look. Higher = draws guards from further away.")]
        [SerializeField] private float noiseRadius = 12f;
        [Tooltip("Seconds between a landed noisemaker's sounds, e.g. 1.5 = it jingles every one and a half seconds. Lower = more often (more noise events).")]
        [SerializeField] private float noiseInterval = 1.5f;
        [Tooltip("Seconds a landed noisemaker keeps sounding, e.g. 12 = it jingles for twelve seconds, then falls silent. Higher = holds guards' attention longer.")]
        [SerializeField] private float noiseDuration = 12f;
        [Tooltip("The sound a landed noisemaker plays each time (its cue also tells the guards), e.g. a bells cue. Left empty, it makes a noise guards hear (Noise Radius) and the player doesn't.")]
        [SerializeField] private SoundCue noiseCue;

        // Rope: the longest a rope can be, how thick it's drawn and in
        // what colour, and how a hand grips it.
        [Tooltip("The longest rope a rope arrow hangs, in metres; shorter if the ground is nearer, e.g. 8 = an arrow 12 m up a wall hangs a rope ending 4 m above the ground. Higher = reaches the ground from higher up.")]
        [SerializeField] private float ropeMaxLength = 8f;
        [Tooltip("How thick a rope is drawn, in metres, e.g. 0.025 = 2.5 cm. Looks only: doesn't change how it's grabbed.")]
        [SerializeField] private float ropeThickness = 0.025f;
        [Tooltip("The colour a rope is drawn in, e.g. a light hemp brown.")]
        [SerializeField] private Color ropeColor = new(0.75f, 0.62f, 0.4f, 1f);
        [Tooltip("How a hand grips a rope (snap offset and finger pose), e.g. RopeGrip.asset in Assets/Data/Hand Poses.")]
        [SerializeField] private HandSnapProfile ropeProfile;

        // Moss: the radius of the patch an arrow lays, in metres, how far
        // below where it lands the floor may be for a patch to be laid
        // there, and the patch's colour.
        [Tooltip("Radius of the moss patch a moss arrow lays, in metres; footsteps within it are silent, e.g. 0.75 = a patch 1.5 m across. Higher = more floor silenced per arrow.")]
        [SerializeField] private float mossRadius = 0.75f;
        [Tooltip("How far below where a moss arrow lands the floor may be for a patch to be laid there, in metres, e.g. 4 = an arrow hitting a wall 3 m up still lays moss at its foot. Higher = moss from higher wall hits; no floor within it = no patch.")]
        [SerializeField] private float mossMaxDrop = 4f;
        [Tooltip("The moss patch's colour (its edges are drawn a little darker), e.g. a dark mossy green.")]
        [SerializeField] private Color mossColor = new(0.22f, 0.32f, 0.12f, 1f);

        // What counts as the floor beneath a rope or a moss patch, and the
        // least light ropes and moss are ever shown in (0-1).
        [Tooltip("The layers that count as the floor beneath a rope (where it ends) or a moss patch (where it's laid), e.g. Environment. Set to Environment if left as Nothing.")]
        [SerializeField, FormerlySerializedAs("ropeGroundLayers")] private LayerMask groundLayers;
        [Tooltip("The least light ropes and moss patches are ever drawn in, 0-1, e.g. 0.35 = never darker than about a third lit, even in full shadow. Higher = easier to see in the dark; 0 = can go fully black.")]
        [SerializeField] private float minLight = 0.35f;

        [Header("Arc")]

        // What an arrow stops at. Filled in from the layer names if left
        // empty - see DefaultArcLayers().
        [Tooltip("The layers an arrow (and the arc) stops at, e.g. Default, Environment, Interactable and Ragdoll (guards' limb hit boxes, never the Guard capsule). Filled in with those if left as Nothing.")]
        [SerializeField] private LayerMask arcLayers;

        // Finding where the arc lands: one physics ray per this many
        // seconds of flight. Drawing it: straight pieces about this long,
        // in metres.
        [Tooltip("Seconds of flight covered by each physics ray when finding where the arc lands, e.g. 0.1 = one ray per tenth of a second of flight (40 rays for a 4 s arc). Lower = more exact landing on thin things, more rays; higher = cheaper, may cut corners.")]
        [SerializeField] private float arcCastTimeStep = 0.1f;
        [Tooltip("Length of each straight piece the arc is drawn in at full draw, in metres (at least 0.05), e.g. 0.3 = a piece every 30 cm. Lower = a smoother curve with more points; higher = cheaper, more angular.")]
        [SerializeField] private float arcSegmentLength = 0.3f;

        // How the arc is drawn, as for an aimed throw. Every shot is a good
        // one, so there's one colour only.
        [Tooltip("How the arc is drawn, as for an aimed throw, e.g. Line = one continuous line; Dots = a row of dots.")]
        [SerializeField] private ThrowArc.Style arcStyle = ThrowArc.Style.Line;
        [Tooltip("The arc line's width in metres, for the Line style, e.g. 0.008 = 8 mm. Higher = bolder line.")]
        [SerializeField] private float arcWidth = 0.008f;
        [Tooltip("Each dot's radius in metres, for the Dots style, e.g. 0.012 = dots 2.4 cm across. Higher = bigger dots.")]
        [SerializeField] private float dotRadius = 0.012f;
        [Tooltip("Metres between dots, for the Dots style, e.g. 0.4 = a dot every 40 cm. Lower = denser dots.")]
        [SerializeField] private float dotSpacing = 0.4f;
        [Tooltip("Radius of the disc shown where the arc lands, in metres, e.g. 0.08 = a disc 16 cm across. An arc landing on nothing shows no disc.")]
        [SerializeField] private float landingMarkerRadius = 0.08f;
        [Tooltip("The arc's colour; its alpha is the arc's strength at full draw (it fades in from the least draw that shoots), e.g. white. Every shot is good, so there is no invalid colour.")]
        [SerializeField] private Color arcColor = Color.white;

        [Header("Arrows")]

        // How far a sticking arrow's tip goes in past the surface, in
        // metres.
        [Tooltip("How far a sticking arrow's tip goes in past the surface, in metres, e.g. 0.03 = 3 cm buried in the wood. Higher = sunk deeper.")]
        [SerializeField] private float stickDepth = 0.03f;

        // Seconds an arrow that can't be picked up again (a rope arrow
        // with no rope on it) lies where it ended up before it's put away.
        [Tooltip("Seconds an arrow that can't be picked up again (e.g. a rope arrow with no rope on it) lies where it ended up before it's put away, e.g. 30 = gone after half a minute. Higher = stays longer, but holds a pool slot.")]
        [SerializeField] private float arrowLifetime = 30f;

        [Header("Arrows That Bounce Off")]

        // How fast an arrow comes back off what it hit, level, in metres a
        // second, and how fast it turns end over end, in radians a second.
        // Small: it drops near where it struck.
        [Tooltip("How fast a bounced arrow comes back off what it hit, level, in metres per second, e.g. 0.8 = it drops just short of the wall it struck. Higher = flies further back; 0 = drops straight down.")]
        [SerializeField] private float reboundSpeed = 0.8f;
        [Tooltip("How fast a bounced arrow turns end over end, in radians per second, e.g. 3 = about half a turn a second. Higher = tumbles faster; 0 = no spin.")]
        [SerializeField] private float reboundSpin = 3f;

        // How hard a bounced arrow is slowed once it first touches
        // something on its way down - its movement and its turning. High,
        // so it stops where it comes down instead of skittering away.
        [Tooltip("Rigidbody drag given to a bounced arrow once it first touches an upward-facing surface, e.g. 6 = it stops near where it comes down. Higher = stops sooner; lower = skitters further.")]
        [SerializeField] private float landedDamping = 6f;
        [Tooltip("Rigidbody angular drag given to a bounced arrow once it first touches an upward-facing surface, e.g. 8 = it stops rolling almost at once. Higher = settles sooner; lower = rolls further.")]
        [SerializeField] private float landedAngularDamping = 8f;

        [Header("Picking Arrows Up")]

        // The radius of the ball round an arrow's middle that a hand ray
        // can target, in metres: bigger than the shaft, so it's easy to
        // aim at on the floor.
        [Tooltip("Radius of the ball round a resting arrow's middle that a hand ray can target to pick it up, in metres, e.g. 0.1 = aim within 10 cm of the shaft's middle. Higher = easier to aim at; lower = more exact.")]
        [SerializeField] private float pickupRadius = 0.1f;

        [Header("Haptics")]

        // An arrow picked up, and a pick-up with no room for it.
        [Tooltip("Strength of the buzz when an arrow is picked up, 0-1, e.g. 0.4 = a firm tap. 0 = no buzz.")]
        [SerializeField] private float pickupAmplitude = 0.4f;
        [Tooltip("Length of the buzz when an arrow is picked up, in seconds, e.g. 0.04 = a short tap. Higher = longer buzz.")]
        [SerializeField] private float pickupDuration = 0.04f;
        [Tooltip("Strength of the buzz when an arrow can't be picked up because the inventory has no room for it, 0-1, e.g. 0.2 = a faint tap. 0 = no buzz.")]
        [SerializeField] private float noRoomAmplitude = 0.2f;
        [Tooltip("Length of the no-room buzz, in seconds, e.g. 0.03 = a very short tap. Higher = longer buzz.")]
        [SerializeField] private float noRoomDuration = 0.03f;

        // Where a pooled arrow is in its life.
        private enum ArrowState
        {
            // Put away: not in the world.
            Away,
            // Being moved along its arc.
            Flying,
            // Bounced off something: physics has it until it settles.
            Tumbling,
            // Stuck in a surface, or lying where it settled.
            Resting
        }

        // How many kinds of arrow there are, counted once from
        // Inventory.BoltType rather than written as a number that could
        // fall behind.
        private static readonly int _arrowKinds = System.Enum.GetValues(typeof(BoltType)).Length;

        // How far past where an arrow is due to land its last ray reaches,
        // in metres, to be sure of finding what it landed on.
        private const float LandingProbe = 0.05f;

        // How many arrows exist. The player carries three of each of the
        // five kinds at most, so this many can never all be in the world
        // at once.
        private const int ArrowPoolSize = 16;

        // How many ropes can hang at once: one per rope arrow carried.
        private const int RopePoolSize = 3;

        // How many moss patches can be down at once: more than can be
        // carried, so picking up more moss arrows rarely lifts an old one.
        private const int MossPoolSize = 6;

        // Moss patches: how far above the floor one is drawn (so it
        // doesn't flicker against it), and how far above or below a patch
        // the feet may be and still be on it, in metres.
        private const float MossLift = 0.01f;
        private const float MossHeightTolerance = 0.3f;

        // A rope shorter than this isn't hung (a rope arrow shot into the
        // floor), in metres.
        private const float MinRopeLength = 0.5f;

        // Seconds after which a bounced arrow is taken as settled wherever
        // it is, in case it never comes to rest.
        private const float TumbleTimeout = 5f;

        // How far an arrow's tip is held off a surface as it starts to
        // tumble, in metres, so it doesn't start inside it.
        private const float TumbleClearance = 0.01f;

        // What a tumbling arrow weighs, in kilograms.
        private const float ArrowMass = 0.03f;

        // The shaft's radius, in metres, the same on every model: a
        // tumbling arrow's solid capsule is this thick.
        private const float ShaftRadius = 0.007f;

        // Each kind's head, measured from its model, in metres back from
        // the model's tip, indexed by the kind's number (water,
        // noisemaker, rope, stun, sleep, moss): where the head ends and
        // the bare shaft begins, and the middle and radius of the solid
        // ball round its widest part. Measure again if a model changes.
        private static readonly float[] _headEnds = { 0.16f, 0.18f, 0.175f, 0.15f, 0.16f, 0.175f };
        private static readonly float[] _headCentres = { 0.07f, 0.08f, 0.085f, 0.04f, 0.078f, 0.078f };
        private static readonly float[] _headRadii = { 0.042f, 0.072f, 0.07f, 0.041f, 0.034f, 0.057f };

        private bool _hasHaptics;
        private bool _hasClimbing;

        // Grip last frame, per hand: an arrow is picked up on the frame
        // grip is pressed.
        private bool _wasLeftGripping;
        private bool _wasRightGripping;

        // One arrow mesh per kind, indexed by the kind's number, and an
        // arrow's length from nock to tip (the longest model's). The
        // rope's and the moss patch's meshes, and the plain vertex-coloured
        // material they share, made at load.
        private readonly Mesh[] _arrowMeshes = new Mesh[_arrowKinds];
        private float _arrowLength;

        // Each kind's solid shapes in arrow space (Z along it, tip of the
        // longest model at 0), set once at load: the shaft capsule's
        // middle and length (nock to head), and the head ball's middle.
        private readonly float[] _shaftMiddles = new float[_arrowKinds];
        private readonly float[] _shaftLengths = new float[_arrowKinds];
        private readonly float[] _headMiddles = new float[_arrowKinds];
        private Mesh _ropeMesh;
        private Mesh _mossMesh;
        private Material _plainMaterial;

        // The arc, and its points this frame (world space). The array is
        // made once and reused.
        private ThrowArc _arc;
        private Vector3[] _points;
        private int _pointCount;

        // A full-draw arrow's speed, how long an arrow may fly, and the
        // seconds of flight one drawn piece of the arc covers at that
        // speed - all set once, at load.
        private float _fullSpeed;
        private float _maxFlightTime;
        private float _arcTimeStep;

        // The pool of arrows. For each: its object, its mesh and its
        // physics parts; its kind; where it is in its life; where and how
        // fast it left, how long it has flown (or tumbled) and when it
        // lands; whether it lands on something, and where; at rest, how
        // much longer before it's put away (0 = it stays); for a
        // noisemaker, how much longer it sounds and when it next does; and
        // which rope hangs from it (-1 = none).
        private Transform _arrowRoot;
        private Transform[] _arrows;
        private MeshFilter[] _arrowFilters;
        private CapsuleCollider[] _arrowShafts;
        private SphereCollider[] _arrowHeads;
        private Arrow[] _arrowParts;
        private ToolBurn.Target[] _arrowBurns;
        private BoltType[] _arrowTypes;
        private ArrowState[] _arrowStates;
        private int[] _arrowRopes;
        private PhysicsMaterial _arrowPhysicsMaterial;
        private Vector3[] _arrowStarts;
        private Vector3[] _arrowVelocities;
        private float[] _arrowTimes;
        private float[] _arrowEndTimes;
        private bool[] _arrowLands;
        private Vector3[] _arrowLandings;
        private float[] _arrowRestTimes;
        private float[] _arrowNoiseTimes;
        private float[] _arrowNextNoises;
        private int _nextArrow;

        // The pool of ropes a rope arrow hangs. For each: the rope, its
        // grab volume and its visible rope (a box stretched to its
        // length); the arrow it hangs from (-1 = none); whether what the
        // arrow is in bears weight; where its top was and how long it was
        // when hung; and how long it has been falling (-1 = it isn't).
        private ClimbableRope[] _ropes;
        private Collider[] _ropeColliders;
        private Transform[] _ropeVisuals;
        private int[] _ropeArrows;
        private bool[] _ropeHolds;
        private Vector3[] _ropeTops;
        private float[] _ropeLengths;
        private float[] _ropeFallTimes;
        private int _nextRope;

        // The pool of moss patches. For each: its visible patch, the
        // middle of the floor it covers (world space), and whether it's
        // laid. The oldest is lifted to lay a new one when all are down.
        private Transform[] _mossPatches;
        private Vector3[] _mossCentres;
        private bool[] _mossLaid;
        private int _nextMoss;

        /// An arrow's length from nock to tip, in metres.
        public float ArrowLength => _arrowLength;

        /// <summary>
        /// The mesh of one kind of arrow: lying along its own Z, its tip
        /// at +Z and its middle on its origin. Drawn on a model made by
        /// MakeArrowModel(), which puts the tip on the arrow's own origin.
        /// </summary>
        public Mesh GetMesh(BoltType kind)
        {
            return _arrowMeshes[(int)kind];
        }

        /// <summary>
        /// Makes an arrow's model under arrow: a child drawing the mesh of
        /// a kind with the arrows' material and no shadows, half an
        /// arrow's length back, so the arrow's own origin is its tip and
        /// it points along its Z. For the arrows in the pool and the one
        /// PlayerBow shows in hand; the mesh is swapped through filter.
        /// </summary>
        public void MakeArrowModel(Transform arrow, BoltType kind, out MeshFilter filter)
        {
            GameObject model = MakePart("Model", _arrowMeshes[(int)kind], arrowMaterial, arrow, out filter);
            model.transform.localPosition = new Vector3(0f, 0f, -_arrowLength * 0.5f);
        }

        /// <summary>
        /// Whether a point on the floor (the feet) is on a moss patch: level
        /// distance from a patch's middle within its radius, and not far
        /// above or below it. A few sums per patch, no physics; asked by
        /// PlayerFootsteps only when a step or a landing is about to sound.
        /// </summary>
        public bool IsOnMoss(Vector3 point)
        {
            // Nothing laid if this hasn't woken (Hands switched off).
            if (_mossLaid == null) {
                return false;
            }

            float radiusSquared = mossRadius * mossRadius;

            for (int i = 0; i < _mossCentres.Length; i++) {
                if (!_mossLaid[i]) {
                    continue;
                }

                Vector3 offset = point - _mossCentres[i];

                if (Mathf.Abs(offset.y) > MossHeightTolerance) {
                    continue;
                }

                offset.y = 0f;

                if (offset.sqrMagnitude <= radiusSquared) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Editor-only: fills in the references when the component is
        /// added (on the Hands object; the body systems are on the Player
        /// root above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerHaptics = GetComponentInParent<PlayerHaptics>();
            playerClimbing = GetComponentInParent<PlayerClimbing>();
            playerHandInteraction = GetComponent<PlayerHandInteraction>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
            toolBurn = GetComponent<ToolBurn>();
            arcLayers = DefaultArcLayers();
            groundLayers = LayerMask.GetMask("Environment");
        }

        private void Awake()
        {
            if (playerInput == null) {
                playerInput = GetComponentInParent<PlayerInputXR>();
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

            if (playerClimbing == null) {
                playerClimbing = GetComponentInParent<PlayerClimbing>();
            }

            if (toolBurn == null) {
                toolBurn = GetComponent<ToolBurn>();
            }

            // Looked up once, so the per-frame code tests plain bools.
            _hasHaptics = playerHaptics != null;
            _hasClimbing = playerClimbing != null;

            if (arcLayers.value == 0) {
                arcLayers = DefaultArcLayers();
            }

            if (groundLayers.value == 0) {
                groundLayers = LayerMask.GetMask("Environment");
            }

            _fullSpeed = Mathf.Max(fullDrawSpeed, 1f);
            _maxFlightTime = Mathf.Max(maxFlightTime, 0.5f);

            // A drawn piece's length as time, and enough slots for the
            // longest arc: one piece per time step, plus the start point
            // and one spare.
            _arcTimeStep = Mathf.Max(arcSegmentLength, 0.05f) / _fullSpeed;
            _points = new Vector3[Mathf.CeilToInt(_maxFlightTime / _arcTimeStep) + 2];

            // The same colour for an arc that lands and one that doesn't:
            // an arrow can always be shot. One that comes down on nothing
            // just has no landing disc.
            _arc = ThrowArc.Create(transform, "Arrow Arc", arcWidth, dotRadius, dotSpacing, landingMarkerRadius, arcColor, arcColor);
            _plainMaterial = LockMeshBuilder.CreateMaterial(false, minLight);

            // In Awake(), not Start(): PlayerBow asks for the meshes in its
            // own Start(), and every Awake() has run before the first
            // Start().
            SetUpArrowMeshes();
            BuildArrows();
            BuildRopes();
            BuildMoss();
        }

        /// <summary>
        /// Registers each pooled arrow's model with ToolBurn, so one picked
        /// up can burn away where it lay. Start() rather than Awake():
        /// ToolBurn finds its shader in its own Awake(), and every Awake()
        /// has run before the first Start().
        /// </summary>
        private void Start()
        {
            if (toolBurn == null) {
                return;
            }

            _arrowBurns = new ToolBurn.Target[_arrows.Length];

            for (int i = 0; i < _arrows.Length; i++) {
                _arrowBurns[i] = toolBurn.Register(_arrowFilters[i].GetComponent<MeshRenderer>());
            }
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
            _arc.Hide();
        }

        private void OnDestroy()
        {
            // A mesh and a material made from code aren't cleaned up with
            // their objects, and the arrows and ropes are at the scene
            // root, not under this object. The arrow meshes are assets.
            if (_ropeMesh != null) {
                Destroy(_ropeMesh);
            }

            if (_mossMesh != null) {
                Destroy(_mossMesh);
            }

            if (_plainMaterial != null) {
                Destroy(_plainMaterial);
            }

            if (_arrowPhysicsMaterial != null) {
                Destroy(_arrowPhysicsMaterial);
            }

            if (_arrowRoot != null) {
                Destroy(_arrowRoot.gameObject);
            }
        }

        /// <summary>
        /// What an arrow can hit: the world, props, guards' limbs (the
        /// ragdoll hit boxes on the Ragdoll layer, not the body capsule on
        /// Guard) and anything never given a layer. Not the player, the
        /// hands (or the props they carry), or climbables' grab volumes.
        /// </summary>
        private static LayerMask DefaultArcLayers()
        {
            return LayerMask.GetMask("Default", "Environment", "Interactable", "Ragdoll");
        }

        /// <summary>
        /// Puts each kind's model mesh in its slot and takes the arrows'
        /// length from the longest (the models are all nearly the same;
        /// a shorter one's tip sits a few millimetres behind the arrow's
        /// origin). A kind with no mesh, or no material, gets a warning.
        /// </summary>
        private void SetUpArrowMeshes()
        {
            _arrowMeshes[(int)BoltType.Water] = waterArrow;
            _arrowMeshes[(int)BoltType.Noisemaker] = noisemakerArrow;
            _arrowMeshes[(int)BoltType.Rope] = ropeArrow;
            _arrowMeshes[(int)BoltType.Stun] = stunArrow;
            _arrowMeshes[(int)BoltType.Sleep] = sleepDart;
            _arrowMeshes[(int)BoltType.Moss] = mossArrow;

            for (int i = 0; i < _arrowKinds; i++) {
                if (_arrowMeshes[i] == null) {
                    Debug.LogWarning($"PlayerArrows: no mesh set for the {(BoltType)i} arrow, so it can't be seen.", this);
                    continue;
                }

                _arrowLength = Mathf.Max(_arrowLength, _arrowMeshes[i].bounds.size.z);
            }

            // Each kind's solid shapes, from where its model's tip and nock
            // fall once MakeArrowModel() has put it half the longest
            // length back (a kind with no mesh: the longest's).
            for (int i = 0; i < _arrowKinds; i++) {
                float tip = 0f;
                float nock = -_arrowLength;

                if (_arrowMeshes[i] != null) {
                    Bounds bounds = _arrowMeshes[i].bounds;
                    tip = bounds.max.z - _arrowLength * 0.5f;
                    nock = bounds.min.z - _arrowLength * 0.5f;
                }

                float shaftFront = tip - _headEnds[i];
                _shaftMiddles[i] = (nock + shaftFront) * 0.5f;
                _shaftLengths[i] = shaftFront - nock;
                _headMiddles[i] = tip - _headCentres[i];
            }

            if (arrowMaterial == null) {
                Debug.LogWarning("PlayerArrows: no arrow material set.", this);
            }
        }

        /// <summary>
        /// Makes the pool of arrows, once, under one object at the scene
        /// root (an arrow in flight mustn't move with the player), all
        /// switched off until shot. Each has what it needs to bounce - a
        /// Rigidbody, a solid capsule the shaft's length and thickness and
        /// a solid ball round the head, on the Projectile layer, all idle
        /// until it does - and, on a child on the Interactable layer
        /// (which hand rays look at), the trigger ball it's picked up by,
        /// round the shaft's middle, behind the tip at the arrow's origin.
        /// </summary>
        private void BuildArrows()
        {
            int count = ArrowPoolSize;
            int projectileLayer = LayerMask.NameToLayer("Projectile");
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            Vector3 middle = new(0f, 0f, -_arrowLength * 0.5f);

            // No bounce and plenty of grip: an arrow that comes down stays
            // down.
            _arrowPhysicsMaterial = new PhysicsMaterial("Arrow") {
                bounciness = 0f,
                dynamicFriction = 0.8f,
                staticFriction = 0.9f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            _arrowRoot = new GameObject("Arrows").transform;
            _arrows = new Transform[count];
            _arrowFilters = new MeshFilter[count];
            _arrowShafts = new CapsuleCollider[count];
            _arrowHeads = new SphereCollider[count];
            _arrowParts = new Arrow[count];
            _arrowTypes = new BoltType[count];
            _arrowStates = new ArrowState[count];
            _arrowRopes = new int[count];
            _arrowStarts = new Vector3[count];
            _arrowVelocities = new Vector3[count];
            _arrowTimes = new float[count];
            _arrowEndTimes = new float[count];
            _arrowLands = new bool[count];
            _arrowLandings = new Vector3[count];
            _arrowRestTimes = new float[count];
            _arrowNoiseTimes = new float[count];
            _arrowNextNoises = new float[count];

            for (int i = 0; i < count; i++) {
                GameObject arrow = new($"Arrow {i}");
                arrow.transform.SetParent(_arrowRoot, false);
                MakeArrowModel(arrow.transform, (BoltType)0, out _arrowFilters[i]);

                if (projectileLayer >= 0) {
                    arrow.layer = projectileLayer;
                }

                Rigidbody body = arrow.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.mass = ArrowMass;

                // Sized to the kind shot in SetSolidShape().
                CapsuleCollider shaft = arrow.AddComponent<CapsuleCollider>();
                shaft.direction = 2;
                shaft.radius = ShaftRadius;
                shaft.sharedMaterial = _arrowPhysicsMaterial;
                _arrowShafts[i] = shaft;

                SphereCollider head = arrow.AddComponent<SphereCollider>();
                head.sharedMaterial = _arrowPhysicsMaterial;
                _arrowHeads[i] = head;
                SetSolidShape(i, (BoltType)0);

                GameObject grab = new("Arrow Grab");

                if (interactableLayer >= 0) {
                    grab.layer = interactableLayer;
                }

                grab.transform.SetParent(arrow.transform, false);
                grab.transform.localPosition = middle;

                SphereCollider ball = grab.AddComponent<SphereCollider>();
                ball.isTrigger = true;
                ball.radius = pickupRadius;

                _arrowParts[i] = arrow.AddComponent<Arrow>();
                _arrowParts[i].SetUp(i, body, shaft, head, ball, landedDamping, landedAngularDamping);

                arrow.SetActive(false);
                _arrows[i] = arrow.transform;
                _arrowRopes[i] = -1;
            }
        }

        /// <summary>
        /// Makes the pool of ropes, once: each a ClimbableRope (its grab
        /// volume is made by the component itself) on the Climbable layer
        /// with a thin box under it to be seen, a metre long and stretched
        /// to the rope's length when it's hung. All switched off until a
        /// rope arrow sticks.
        /// </summary>
        private void BuildRopes()
        {
            int count = RopePoolSize;
            int climbableLayer = LayerMask.NameToLayer("Climbable");

            LockMeshBuilder builder = new();
            builder.Box(new Vector3(0f, -0.5f, 0f), new Vector3(ropeThickness, 1f, ropeThickness), Quaternion.identity, ropeColor);
            _ropeMesh = builder.ToMesh("Arrow Rope");

            _ropes = new ClimbableRope[count];
            _ropeColliders = new Collider[count];
            _ropeVisuals = new Transform[count];
            _ropeArrows = new int[count];
            _ropeHolds = new bool[count];
            _ropeTops = new Vector3[count];
            _ropeLengths = new float[count];
            _ropeFallTimes = new float[count];

            for (int i = 0; i < count; i++) {
                GameObject ropeObject = new($"Arrow Rope {i}");

                // The layer first: the rope checks it as it's added.
                if (climbableLayer >= 0) {
                    ropeObject.layer = climbableLayer;
                }

                ropeObject.transform.SetParent(_arrowRoot, false);

                ClimbableRope rope = ropeObject.AddComponent<ClimbableRope>();
                rope.SetSnapProfile(ropeProfile);

                _ropeVisuals[i] = MakePart("Rope", _ropeMesh, _plainMaterial, ropeObject.transform, out _).transform;
                _ropes[i] = rope;
                _ropeColliders[i] = ropeObject.GetComponent<CapsuleCollider>();
                _ropeArrows[i] = -1;
                _ropeFallTimes[i] = -1f;
                ropeObject.SetActive(false);
            }
        }

        /// <summary>
        /// Makes the pool of moss patches, once, all switched off until a
        /// moss arrow lands. A patch is one flat mesh lying across its own
        /// XY with its face up its Z: a middle disc ringed by smaller
        /// overlapping ones, so its edge is ragged rather than a circle,
        /// reaching about mossRadius from the middle. Only to look at: no
        /// collider (footsteps ask IsOnMoss()).
        /// </summary>
        private void BuildMoss()
        {
            const int blobs = 6;
            const float depth = 0.004f;
            Color sideColor = new(mossColor.r * 0.7f, mossColor.g * 0.7f, mossColor.b * 0.7f, 1f);

            LockMeshBuilder builder = new();
            builder.Cylinder(mossRadius * 0.7f, depth, 12, mossColor, sideColor);

            for (int i = 0; i < blobs; i++) {
                // Round the edge, each a little different in size and
                // distance out, so the outline doesn't look regular.
                float angle = i * Mathf.PI * 2f / blobs;
                float size = mossRadius * (i % 2 == 0 ? 0.35f : 0.28f);
                float reach = mossRadius - size;
                Vector3 centre = new(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach, 0f);
                builder.Cylinder(size, depth, 8, mossColor, sideColor, centre, i * 17f);
            }

            _mossMesh = builder.ToMesh("Moss Patch");

            _mossPatches = new Transform[MossPoolSize];
            _mossCentres = new Vector3[MossPoolSize];
            _mossLaid = new bool[MossPoolSize];

            for (int i = 0; i < MossPoolSize; i++) {
                GameObject patch = MakePart($"Moss Patch {i}", _mossMesh, _plainMaterial, _arrowRoot, out _);
                patch.SetActive(false);
                _mossPatches[i] = patch.transform;
            }
        }

        /// <summary>
        /// A moss arrow has landed at point: lays a patch on the floor
        /// straight below it (where it is, if it hit the floor; below, if
        /// it hit a wall or a guard), lying on that floor. None if there's
        /// no floor within mossMaxDrop. The oldest patch is lifted if all
        /// are down.
        /// </summary>
        private void LayMoss(Vector3 point, Vector3 heading)
        {
            // From a little back along the flight and up, so the ray
            // starts clear of what was hit.
            Vector3 from = point - heading * LandingProbe + Vector3.up * LandingProbe;

            if (!Physics.Raycast(from, Vector3.down, out RaycastHit floor, mossMaxDrop + LandingProbe, groundLayers, QueryTriggerInteraction.Ignore)) {
                return;
            }

            int index = _nextMoss;
            _nextMoss = (_nextMoss + 1) % MossPoolSize;

            // Turned to the floor's slope and to the way the arrow flew,
            // so no two patches line up the same.
            Vector3 along = Vector3.ProjectOnPlane(heading, floor.normal);

            if (along.sqrMagnitude < 0.0001f) {
                along = Vector3.ProjectOnPlane(Vector3.forward, floor.normal);
            }

            Transform patch = _mossPatches[index];
            patch.SetPositionAndRotation(floor.point + floor.normal * MossLift, Quaternion.LookRotation(floor.normal, along));
            patch.gameObject.SetActive(true);

            _mossCentres[index] = floor.point;
            _mossLaid[index] = true;
        }

        /// <summary>
        /// One drawn piece: an object under parent with a mesh, a material
        /// and no shadows (small things that don't need them).
        /// </summary>
        private static GameObject MakePart(string partName, Mesh mesh, Material material, Transform parent, out MeshFilter filter)
        {
            GameObject part = new(partName);
            part.transform.SetParent(parent, false);

            filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterial = material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return part;
        }

        /// <summary>
        /// Shows the arc an arrow would fly along if shot now from tip,
        /// pointing along direction, drawn draw of the way (0-1), faded to
        /// alpha (0-1). No landing disc when it would come down on nothing,
        /// but never shown as a bad shot.
        /// </summary>
        public void ShowArc(Vector3 tip, Vector3 direction, float draw, float alpha, Vector3 viewerPosition)
        {
            // An arrow is moved along this same sum, not by physics, so no
            // allowance for physics' stepped gravity (the 0).
            bool lands = BallisticArc.Compute(tip, direction * (_fullSpeed * draw), arcLayers, arcCastTimeStep, _maxFlightTime, _arcTimeStep, 0f, _points, out _pointCount, out _, out Vector3 landingPoint, out Vector3 landingNormal);
            _arc.Show(_points, _pointCount, arcStyle, lands, landingPoint, landingNormal, viewerPosition, alpha);
        }

        /// <summary>
        /// Hides the arc. Free to call when it's already hidden.
        /// </summary>
        public void HideArc()
        {
            _arc.Hide();
        }

        /// <summary>
        /// Shoots an arrow of a kind from tip, pointing along direction,
        /// drawn draw of the way (0-1): the next arrow in the pool (the
        /// oldest, if all are out) is put there and set flying along the
        /// arc. The arrow was taken from the inventory when it was drawn
        /// over the shoulder, so none is taken here.
        /// </summary>
        public void Shoot(BoltType kind, Vector3 tip, Vector3 direction, float draw)
        {
            Vector3 velocity = direction * (_fullSpeed * draw);

            // Where it will come down, worked out once now; in flight it
            // only checks nothing has moved into its way.
            bool lands = BallisticArc.Compute(tip, velocity, arcLayers, arcCastTimeStep, _maxFlightTime, _arcTimeStep, 0f, _points, out _pointCount, out float flightTime, out Vector3 landingPoint, out _);

            int index = TakeArrowSlot();

            _arrowTypes[index] = kind;
            _arrowStates[index] = ArrowState.Flying;
            _arrowStarts[index] = tip;
            _arrowVelocities[index] = velocity;
            _arrowTimes[index] = 0f;
            _arrowEndTimes[index] = flightTime;
            _arrowLands[index] = lands;
            _arrowLandings[index] = landingPoint;
            _arrowRestTimes[index] = 0f;
            _arrowNoiseTimes[index] = 0f;

            Transform arrow = _arrows[index];
            _arrowFilters[index].sharedMesh = _arrowMeshes[(int)kind];
            SetSolidShape(index, kind);
            arrow.SetPositionAndRotation(tip, Quaternion.LookRotation(velocity));
            arrow.gameObject.SetActive(true);

            PlayCue(shotCue, tip);
        }

        /// <summary>
        /// Fits a pooled arrow's solid capsule and head ball to a kind's
        /// model (measured at load in SetUpArrowMeshes()). Called as it's
        /// shot, while both are switched off.
        /// </summary>
        private void SetSolidShape(int index, BoltType kind)
        {
            int k = (int)kind;
            CapsuleCollider shaft = _arrowShafts[index];
            shaft.center = new Vector3(0f, 0f, _shaftMiddles[k]);
            shaft.height = _shaftLengths[k];

            SphereCollider head = _arrowHeads[index];
            head.center = new Vector3(0f, 0f, _headMiddles[k]);
            head.radius = _headRadii[k];
        }

        /// <summary>
        /// The pooled arrow to shoot next, cleared out ready: one that is
        /// put away and silent if there is one (there always should be -
        /// see ArrowPoolSize); failing that one lying about that can't be
        /// picked up; failing that, simply the next in turn.
        /// </summary>
        private int TakeArrowSlot()
        {
            int count = _arrows.Length;
            int chosen = -1;

            for (int i = 0; i < count && chosen < 0; i++) {
                int index = (_nextArrow + i) % count;

                if (_arrowStates[index] == ArrowState.Away && _arrowNoiseTimes[index] <= 0f) {
                    chosen = index;
                }
            }

            for (int i = 0; i < count && chosen < 0; i++) {
                int index = (_nextArrow + i) % count;

                if (_arrowStates[index] == ArrowState.Resting && !CanBePickedUp(_arrowTypes[index])) {
                    chosen = index;
                }
            }

            if (chosen < 0) {
                chosen = _nextArrow;
            }

            _nextArrow = (chosen + 1) % count;
            PutAway(chosen);
            return chosen;
        }

        /// <summary>
        /// Either hand picking an arrow up: on the frame its grip is
        /// pressed, free, with its ray on an arrow at rest, the arrow goes
        /// straight back into the inventory and is put away, burning away
        /// where it lay (ToolBurn) - it is never carried, so it never
        /// touches anything. With no room for it (the
        /// player already carries the most of that kind) it stays where it
        /// is, and the hand feels only a faint tap. Called by
        /// PlayerController after the hand rays and before the grab
        /// systems.
        /// </summary>
        public void TickPickup()
        {
            TickPickup(true);
            TickPickup(false);
        }

        private void TickPickup(bool isLeftHand)
        {
            bool isGripping = isLeftHand ? playerInput.IsLeftGrabbing : playerInput.IsRightGrabbing;
            bool wasGripping = isLeftHand ? _wasLeftGripping : _wasRightGripping;

            if (isLeftHand) {
                _wasLeftGripping = isGripping;
            } else {
                _wasRightGripping = isGripping;
            }

            if (!isGripping || wasGripping || playerHandState.IsBusyExcept(isLeftHand, HandUse.None)) {
                return;
            }

            IHandTarget target = isLeftHand ? playerHandInteraction.LeftTarget : playerHandInteraction.RightTarget;

            if (target is not Arrow part) {
                return;
            }

            int index = part.Index;

            if (_arrowStates[index] != ArrowState.Resting) {
                return;
            }

            // A scene with no inventory has unlimited arrows: the arrow is
            // simply cleared away.
            PlayerInventory inventory = PlayerInventory.Instance;

            if (inventory != null && !inventory.TryAddBolt(_arrowTypes[index])) {
                Pulse(isLeftHand, noRoomAmplitude, noRoomDuration);
                return;
            }

            // A copy is left burning away where the arrow lay (before it's
            // switched off, while it still shows), and the arrow itself
            // goes at once.
            if (_arrowBurns != null) {
                toolBurn.BurnAway(_arrowBurns[index]);
            }

            PutAway(index);
            Pulse(isLeftHand, pickupAmplitude, pickupDuration);
        }

        /// <summary>
        /// Whether an arrow of this kind that ends up lying or stuck
        /// somewhere can be picked up and shot again: a stun arrow always,
        /// a sleep dart that hit no guard (one that did never gets this
        /// far). Water and noisemakers are used up where they land, and a
        /// rope arrow is never recovered.
        /// </summary>
        private static bool CanBePickedUp(BoltType kind)
        {
            return kind == BoltType.Stun || kind == BoltType.Sleep;
        }

        /// <summary>
        /// Takes an arrow out of the world: out of physics, no longer a
        /// hand target, unhooked from any rope hanging from it (the rope
        /// stays), and switched off. A noisemaker's sounding isn't touched
        /// - it carries on from where the arrow was.
        /// </summary>
        private void PutAway(int index)
        {
            _arrowStates[index] = ArrowState.Away;
            _arrowRestTimes[index] = 0f;
            _arrowParts[index].EndTumble();
            _arrowParts[index].SetTargetable(false);
            _arrows[index].gameObject.SetActive(false);

            int rope = _arrowRopes[index];

            if (rope >= 0) {
                _arrowRopes[index] = -1;
                _ropeArrows[rope] = -1;
            }
        }

        /// <summary>
        /// An arrow comes to rest - stuck where it hit, or settled after
        /// bouncing off. One that can be picked up becomes a hand target
        /// and stays; any other is put away after a while.
        /// </summary>
        private void Rest(int index)
        {
            bool canBePickedUp = CanBePickedUp(_arrowTypes[index]);

            _arrowStates[index] = ArrowState.Resting;
            _arrowParts[index].EndTumble();
            _arrowParts[index].SetTargetable(canBePickedUp);
            _arrowRestTimes[index] = canBePickedUp ? 0f : arrowLifetime;
        }

        /// <summary>
        /// Hands an arrow to physics: pulled back along its own length
        /// first, so its tip is just clear of what it hit rather than
        /// buried in it, then set moving. From here it falls and settles
        /// by itself (Arrow), and TickFlight() waits for that.
        /// </summary>
        private void Tumble(int index, Vector3 velocity, Vector3 angularVelocity)
        {
            Transform arrow = _arrows[index];

            arrow.position -= arrow.forward * (stickDepth + TumbleClearance);

            _arrowStates[index] = ArrowState.Tumbling;
            _arrowTimes[index] = 0f;
            _arrowRestTimes[index] = 0f;
            _arrowParts[index].SetTargetable(false);
            _arrowParts[index].BeginTumble(velocity, angularVelocity);
        }

        /// <summary>
        /// Moves every arrow in flight along its arc; waits for the ones
        /// that bounced off something to settle; counts down the ones at
        /// rest that can't be picked up, and puts them away when their
        /// time is up; sounds the noisemakers; and looks after the ropes.
        /// Called by PlayerController on every path through its Update(),
        /// so an arrow already shot keeps flying through a mantle.
        /// </summary>
        public void TickFlight()
        {
            float deltaTime = Time.deltaTime;
            Vector3 gravity = Physics.gravity;

            for (int i = 0; i < _arrows.Length; i++) {
                Transform arrow = _arrows[i];

                if (_arrowStates[i] == ArrowState.Tumbling) {
                    _arrowTimes[i] += deltaTime;

                    if (_arrowParts[i].HasSettled || _arrowTimes[i] >= TumbleTimeout) {
                        Rest(i);
                    }

                    continue;
                }

                if (_arrowStates[i] != ArrowState.Flying) {
                    TickLandedArrow(i, arrow, deltaTime);
                    continue;
                }

                Vector3 previous = arrow.position;
                float time = _arrowTimes[i] + deltaTime;
                bool hasArrived = time >= _arrowEndTimes[i];

                // The end of the arc worked out when it was shot: where it
                // was going to land, or nowhere.
                if (hasArrived && !_arrowLands[i]) {
                    PutAway(i);
                    continue;
                }

                Vector3 next = hasArrived
                    ? _arrowLandings[i]
                    : BallisticArc.Point(_arrowStarts[i], _arrowVelocities[i], gravity, time, 0f);

                // Which way it's travelling now: its starting speed plus
                // what gravity has added since.
                Vector3 heading = (_arrowVelocities[i] + gravity * Mathf.Min(time, _arrowEndTimes[i])).normalized;

                // One ray along this frame's move, in case something has
                // come into its way since the arc was worked out (a door
                // swung shut, a guard walked into it). On arriving it
                // reaches a little further, to be sure of finding what the
                // arrow has landed on.
                Vector3 castEnd = hasArrived ? next + heading * LandingProbe : next;
                Collider landedOn = null;

                if (Physics.Linecast(previous, castEnd, out RaycastHit hit, arcLayers, QueryTriggerInteraction.Ignore)) {
                    next = hit.point;
                    landedOn = hit.collider;
                    hasArrived = true;
                }

                arrow.SetPositionAndRotation(next, Quaternion.LookRotation(heading));
                _arrowTimes[i] = time;

                if (hasArrived) {
                    PlayCue(impactCue, next);
                    Land(i, next, heading, landedOn);
                }
            }

            TickRopes(deltaTime);
        }

        /// <summary>
        /// An arrow has landed, its tip at point: what its kind does there.
        /// </summary>
        private void Land(int index, Vector3 point, Vector3 heading, Collider landedOn)
        {
            BoltType kind = _arrowTypes[index];

            // A guard's body: a dictionary lookup, no GetComponent.
            Guard guard = landedOn != null ? Guard.FindByBody(landedOn) : null;

            if (guard != null && HitGuard(index, guard, landedOn)) {
                return;
            }

            // Water, noisemakers and moss are used up where they land,
            // whatever that is, with their effect set off.
            if (kind == BoltType.Moss) {
                LayMoss(point, heading);
                PutAway(index);
                return;
            }

            if (kind == BoltType.Water) {
                Flame.ExtinguishNear(point, waterRadius);
                PutAway(index);
                return;
            }

            if (kind == BoltType.Noisemaker) {
                // Sounds at once, then every so often for a while, from
                // where the arrow was.
                PutAway(index);
                _arrowNoiseTimes[index] = noiseDuration;
                _arrowNextNoises[index] = 0f;
                return;
            }

            // The rest stick or bounce. Only level geometry that says it
            // holds an arrow does: not a guard, and not anything that
            // moves (a door, a prop), which would leave the arrow hanging
            // in the air behind it.
            SurfaceTag surface = guard == null && landedOn != null && landedOn.attachedRigidbody == null
                ? SurfaceTag.Find(landedOn)
                : null;

            if (surface != null && SurfaceTag.HoldsBolt(surface.Surface)) {
                // In a little way: the tip is buried, the shaft stands
                // out.
                _arrows[index].position = point + heading * stickDepth;
                Rest(index);

                if (kind == BoltType.Rope) {
                    HangRope(index, point - heading * LandingProbe, surface.SupportsRopeBolt);
                }

                return;
            }

            Rebound(index, heading);
        }

        /// <summary>
        /// An arrow has hit a guard. A stun arrow to the head stuns it and
        /// a sleep dart anywhere puts it to sleep after a wait; any other
        /// hit does nothing to the guard but tell it that it was shot at,
        /// and where from. Returns whether that is the end of the arrow:
        /// only a sleep dart, which stays in the guard. The rest go on to
        /// be used up or to bounce off.
        /// </summary>
        private bool HitGuard(int index, Guard guard, Collider hitbox)
        {
            BoltType kind = _arrowTypes[index];

            if (kind == BoltType.Sleep) {
                guard.Sedate(sleepDelay);
                PutAway(index);
                return true;
            }

            // The ray found the hit box of the limb it really hit.
            if (kind == BoltType.Stun && guard.IsHead(hitbox)) {
                guard.Stun(stunTime);
            } else {
                guard.HitByBolt(_arrowStarts[index]);
            }

            return false;
        }

        /// <summary>
        /// An arrow bounces off what it hit: it becomes a physics object
        /// with a little speed straight back the way it came, level, and a
        /// slow end-over-end turn, and drops from there.
        /// </summary>
        private void Rebound(int index, Vector3 heading)
        {
            Vector3 back = -heading;
            back.y = 0f;

            // Shot straight down (or up): no way back to speak of, so it
            // just drops.
            back = back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.zero;

            Tumble(index, back * reboundSpeed, Vector3.Cross(Vector3.up, back) * reboundSpin);
        }

        /// <summary>
        /// Hangs the next rope in the pool (the oldest, if all are out)
        /// from a rope arrow stuck at a point, down to the ground beneath
        /// it or to its longest. holds is whether what the arrow is in
        /// bears a climber's weight; a rope that doesn't looks the same,
        /// and comes away when a hand takes hold (TickRopes()). No rope is
        /// hung with too little drop beneath the arrow for one.
        /// </summary>
        private void HangRope(int arrowIndex, Vector3 top, bool holds)
        {
            // How far down the ground is: one ray, straight down.
            float length = Physics.Raycast(top, Vector3.down, out RaycastHit ground, ropeMaxLength, groundLayers, QueryTriggerInteraction.Ignore)
                ? ground.distance
                : ropeMaxLength;

            if (length < MinRopeLength) {
                return;
            }

            int index = _nextRope;
            _nextRope = (_nextRope + 1) % _ropes.Length;

            // A rope being reused takes its old arrow with it.
            if (_ropeArrows[index] >= 0) {
                PutAway(_ropeArrows[index]);
            }

            ClimbableRope rope = _ropes[index];
            rope.transform.SetPositionAndRotation(top, Quaternion.identity);
            rope.gameObject.SetActive(true);
            rope.SetLength(length);

            // Its grab volume is off if it was last seen falling.
            _ropeColliders[index].enabled = true;

            // The rope seen: the metre-long box, stretched to the length.
            _ropeVisuals[index].localScale = new Vector3(1f, rope.Length, 1f);

            _ropeArrows[index] = arrowIndex;
            _ropeHolds[index] = holds;
            _ropeTops[index] = top;
            _ropeLengths[index] = rope.Length;
            _ropeFallTimes[index] = -1f;

            // The arrow stays for as long as its rope does.
            _arrowRopes[arrowIndex] = index;
            _arrowRestTimes[arrowIndex] = 0f;
        }

        /// <summary>
        /// The ropes: one that won't bear weight comes away the moment a
        /// hand takes hold of it, and one that has come away falls - its
        /// top drops under gravity and the rope gathers on the ground
        /// beneath, until there is none left and it is put away.
        /// </summary>
        private void TickRopes(float deltaTime)
        {
            for (int i = 0; i < _ropes.Length; i++) {
                ClimbableRope rope = _ropes[i];

                if (!rope.gameObject.activeSelf) {
                    continue;
                }

                if (_ropeFallTimes[i] < 0f) {
                    // The same object, not merely an equal one: is a hand
                    // on this rope?
                    if (!_ropeHolds[i] && _hasClimbing && (ReferenceEquals(playerClimbing.LeftGrabbed, rope) || ReferenceEquals(playerClimbing.RightGrabbed, rope))) {
                        DropRope(i);
                    }

                    continue;
                }

                _ropeFallTimes[i] += deltaTime;

                // How far something falls from rest in a time: half
                // gravity x the time squared.
                float fallen = 0.5f * Physics.gravity.magnitude * _ropeFallTimes[i] * _ropeFallTimes[i];
                float remaining = _ropeLengths[i] - fallen;

                if (remaining <= 0f) {
                    _ropeFallTimes[i] = -1f;
                    rope.gameObject.SetActive(false);
                    continue;
                }

                rope.transform.position = _ropeTops[i] + Vector3.down * fallen;
                _ropeVisuals[i].localScale = new Vector3(1f, remaining, 1f);
            }
        }

        /// <summary>
        /// A rope comes away from what its arrow was stuck in: the hands on
        /// it are made to let go, it can no longer be grabbed, it starts to
        /// fall, and its arrow drops out as a physics object.
        /// </summary>
        private void DropRope(int index)
        {
            playerClimbing.Release(_ropes[index]);

            _ropeColliders[index].enabled = false;
            _ropeFallTimes[index] = 0f;

            int arrow = _ropeArrows[index];

            if (arrow >= 0) {
                _ropeArrows[index] = -1;
                _arrowRopes[arrow] = -1;
                Tumble(arrow, Vector3.zero, Vector3.zero);
            }
        }

        /// <summary>
        /// An arrow not in flight: a noisemaker (already put away) sounds
        /// from where it landed, and an arrow at rest that can't be picked
        /// up is put away when its time is up.
        /// </summary>
        private void TickLandedArrow(int index, Transform arrow, float deltaTime)
        {
            if (_arrowNoiseTimes[index] > 0f) {
                _arrowNoiseTimes[index] -= deltaTime;
                _arrowNextNoises[index] -= deltaTime;

                if (_arrowNextNoises[index] <= 0f) {
                    _arrowNextNoises[index] = noiseInterval;
                    MakeNoise(arrow);
                }
            }

            if (_arrowRestTimes[index] > 0f) {
                _arrowRestTimes[index] -= deltaTime;

                if (_arrowRestTimes[index] <= 0f) {
                    PutAway(index);
                }
            }
        }

        /// <summary>
        /// One sound from a noisemaker: its cue if it has one (which also
        /// tells the guards), otherwise just the noise guards hear.
        /// </summary>
        private void MakeNoise(Transform arrow)
        {
            Vector3 position = arrow.position;
            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (noiseCue != null && soundPlayer != null) {
                soundPlayer.Play(noiseCue, position, arrow);
            } else {
                NoiseSystem.Emit(position, noiseRadius, NoiseType.Distraction, arrow);
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
