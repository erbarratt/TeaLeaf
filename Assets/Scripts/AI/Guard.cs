using System.Collections.Generic;
using Core;
using Player;
using UnityEngine;
using UnityEngine.AI;

namespace AI
{
    /// <summary>
    /// A guard: it walks a patrol, notices the player by sight and by
    /// sound, and goes after them. There is no fighting - a guard that
    /// reaches the player has caught them, and the level restarts.
    ///
    /// Everything it does is one of six states (GuardState), and the
    /// whole behaviour is a switch on that:
    /// - Patrol: walks its PatrolRoute, or stands at its post.
    /// - Suspicious: stops and turns towards what it half saw or heard.
    ///   After a few seconds it goes to look (Search) if there was
    ///   enough to go on, or back to its patrol if there wasn't.
    /// - Search: walks to the place, looks about, tries a few places
    ///   round it, then gives up and goes back to its patrol.
    /// - Chase: has seen the player and runs at them; close enough, the
    ///   player is caught. If it loses them for long enough it searches
    ///   where it last saw them.
    /// - Stunned: hit on the head by a stun bolt. It stands where it is,
    ///   seeing and hearing nothing, and when it comes round it searches.
    /// - KnockedOut: down, until another guard sees it lying there,
    ///   walks over and wakes it.
    ///
    /// Nothing kills a guard. A sleep dart is not a state: the guard
    /// carries on with whatever it was doing, and a timer knocks it out
    /// when it runs down.
    ///
    /// What moves it between them is two meters (0-1), as in Thief VR:
    /// suspicion ("was that something?") and detection ("that's a
    /// person"). Seeing the player fills both, at rates set by Thief
    /// VR's vision zones (GuardSight): which zone and how far, lit or
    /// dark, and how the player moves. Hearing a noise adds to both at
    /// once. Each holds a while after it was last raised, then drains -
    /// suspicion soon, detection slowly. Suspicion full, the guard
    /// becomes Suspicious; detection full, it chases.
    ///
    /// Looking is not done every frame: it costs physics rays, so it's
    /// done a few times a second, each guard on its own timer so they
    /// don't all look in the same frame, and less often when the player
    /// is far away. Walking is Unity's NavMeshAgent, which runs natively.
    ///
    /// On the guard's root object, with a NavMeshAgent. It has its own
    /// Update(): guards aren't part of the player's tick order.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Guard : MonoBehaviour, INoiseListener, IDebugDrawable
    {
        [Header("Parts")]

        // Where it sees and hears from: an object on the model's head
        // bone whose forward is the way the head faces, so the view
        // turns and dips with the head as it is animated.
        [Tooltip("Where the guard sees and hears from: an object on the model's head bone whose forward is the way the head faces.")]
        [SerializeField] private Transform eye;

        // Optional: greybox shapes under one object, laid flat when the
        // guard is knocked out. Left empty on the animated guard, whose
        // Animator plays the fall instead (GuardAnimation).
        [Tooltip("Optional: greybox shapes under one object, laid flat when the guard is knocked out. Leave empty on the animated guard, whose Animator plays the fall instead.")]
        [SerializeField] private Transform visual;

        // The body's collider (Guard layer) and the head's (GuardHead
        // layer, what the blackjack hits).
        [Tooltip("The body's collider, on the Guard layer.")]
        [SerializeField] private Collider bodyCollider;
        [Tooltip("The head's collider, on the GuardHead layer: what the blackjack and stun bolts hit.")]
        [SerializeField] private Collider headCollider;

        // Optional: a renderer tinted by state, so the state can be read
        // at a glance on a greybox guard. Left empty on the animated
        // guard: the tint would darken its texture.
        [Tooltip("Optional: a renderer tinted by state, so a greybox guard's state can be read at a glance. Leave empty on the animated guard (the tint darkens its texture).")]
        [SerializeField] private Renderer stateRenderer;

        [Header("Testing")]

        // Play Mode only: ticking it knocks the guard out, clearing it
        // wakes it as another guard would. Follows the guard's own
        // state, so it shows whether it is down.
        [Tooltip("Play Mode only: tick to knock the guard out, clear to wake it up. Shows whether it is down.")]
        [SerializeField] private bool knockedOut;

        // Off: the guard notices nothing of the player - not seen, not
        // bumped, not heard, not shot at, a failed blackjack swing
        // ignored - so it never suspects, chases or catches them. Bodies
        // are still found. Can be switched during Play Mode.
        [Tooltip("Off: the guard never sees, bumps, hears or notices the player (bolts, failed blackjack swings), so it never suspects, chases or catches them. Bodies are still found and woken.")]
        [SerializeField] private bool detectsPlayer = true;

        [Header("Patrol")]

        // The route it walks. With none, it stands where it was placed.
        [Tooltip("The route it walks. With none, it stands where it was placed, facing the way it was put.")]
        [SerializeField] private PatrolRoute patrolRoute;

        [Header("Speeds")]

        // Metres a second walking (patrolling and searching) and running
        // (chasing), and how fast it turns on the spot, in degrees a second.
        [Tooltip("Metres a second while patrolling and searching.")]
        [SerializeField] private float patrolSpeed = 1.07f;
        [Tooltip("Metres a second while chasing.")]
        [SerializeField] private float chaseSpeed = 3.6f;
        [Tooltip("Top speed of a turn on the spot, in degrees a second.")]
        [SerializeField] private float turnSpeed = 240f;

        // How quickly a turn on the spot speeds up and slows down, in
        // degrees a second per second: it eases in from still and out to
        // a stop on the way it faces. At four times turnSpeed a half
        // turn just reaches turnSpeed at its middle.
        [Tooltip("How quickly a turn on the spot speeds up and slows down, in degrees a second per second. At four times Turn Speed a half turn just reaches Turn Speed at its middle.")]
        [SerializeField] private float turnAcceleration = 2880f;

        // How hard it slows coming up to where it is going, in metres a
        // second per second, so it eases to a stop instead of the agent
        // braking hard at the last moment. From a run at 3.6 that takes
        // about 1.2s over 2.2m; from a walk barely a third of a second.
        // Never while chasing: it runs right up to the player.
        [Tooltip("How hard it slows coming up to where it is going, in metres a second per second (lower = longer, gentler stop). From a run at 3.6 about 1.2s over 2.2m. Not while chasing.")]
        [SerializeField] private float brakeDeceleration = 3f;

        [Header("Sight")]

        // The vision zones and how fast each fills the meters, shared by
        // every guard (Thief VR's, in Assets/Data/AI).
        [Tooltip("The vision zones and their fill times, shared by every guard (Assets/Data/AI/GuardSight.asset). Changes made in Play Mode are kept.")]
        [SerializeField] private GuardSight sight;

        // A player whose body is within this of the guard's, level, has
        // bumped into it: the guard turns on them whichever way it was
        // facing. A little more than the two bodies' radii together.
        // Behind a guard and further off than this, only a noise gives
        // the player away.
        [Tooltip("A player whose feet come within this of the guard's, in metres, level, has bumped into it and is noticed whichever way it faces. A little more than the two bodies' radii together.")]
        [SerializeField] private float bumpRange = 0.5f;

        // What blocks its sight. Filled in from the layer names if left
        // empty (Environment and Interactable: walls, props, doors).
        [Tooltip("What blocks its sight. Left empty, Environment and Interactable (walls, props, doors).")]
        [SerializeField] private LayerMask sightLayers;

        [Header("Hearing")]

        // How much of a noise's loudness (0-1 at the ears) is added to
        // the detection meter, and how much a noisemaker bolt's is.
        [Tooltip("How much of a noise's loudness at the ears (0-1) is added to the detection meter.")]
        [SerializeField] private float hearingGain = 0.7f;
        [Tooltip("How much a noisemaker bolt's noise is added to the detection meter, in place of Hearing Gain.")]
        [SerializeField] private float distractionGain = 1.5f;

        // How many times more a noise adds to the suspicion meter than to
        // the detection meter: at 3.33 a footstep of 0.45 at the ears
        // (x 0.7) fills suspicion and the guard turns to look.
        [Tooltip("How many times more a noise adds to the suspicion meter than to the detection meter. At 3.33 one ordinary footstep at the ears (0.45 x 0.7) fills suspicion.")]
        [SerializeField] private float hearingSuspicionScale = 3.33f;

        [Header("Meters (0-1)")]

        // Two meters, as Thief VR's: suspicion ("was that something?";
        // full = Suspicious) fades fast, detection ("that's a person";
        // full = Chase) lingers. Each holds for a while after the last
        // thing that raised it, then drains at its rate per second.
        [Tooltip("Seconds the suspicion meter holds after the last thing that raised it, before draining.")]
        [SerializeField] private float suspicionHoldTime = 1f;
        [Tooltip("How much of the suspicion meter drains each second once it stops holding.")]
        [SerializeField] private float suspicionDecayRate = 1f;
        [Tooltip("Seconds the detection meter holds after the last thing that raised it, before draining (not while chasing).")]
        [SerializeField] private float detectionHoldTime = 4f;
        [Tooltip("How much of the detection meter drains each second once it stops holding.")]
        [SerializeField] private float detectionDecayRate = 0.25f;

        // A suspicion whose detection reached this is worth going to look
        // at; the level a bump, a bolt or a body sets detection to.
        [Tooltip("Detection level (0-1) a suspicion must reach for the guard to go and search; also what a bump, a bolt or a found body sets it to.")]
        [SerializeField] private float investigateThreshold = 0.5f;

        [Header("Looking (how often)")]

        // Seconds between looks, and between looks while the player is
        // further away than any zone reaches.
        [Tooltip("Seconds between looks.")]
        [SerializeField] private float lookInterval = 0.15f;
        [Tooltip("Seconds between looks while the player is further away than any vision zone reaches.")]
        [SerializeField] private float farLookInterval = 0.5f;

        [Header("Suspicious")]

        // Seconds it stands looking before deciding.
        [Tooltip("Seconds it stands looking before deciding to search or go back to its patrol.")]
        [SerializeField] private float suspiciousTime = 3f;

        [Header("Search")]

        // Seconds it looks about at each place, how many places round
        // the first it tries and how far from it, and the longest a
        // search lasts.
        [Tooltip("Seconds it looks about at each place it searches.")]
        [SerializeField] private float searchLookTime = 2.5f;
        [Tooltip("How many random places round the first it searches before giving up.")]
        [SerializeField] private int searchPointCount = 3;
        [Tooltip("How far from the first place those random places can be, in metres.")]
        [SerializeField] private float searchRadius = 4f;
        [Tooltip("The longest a search lasts, in seconds.")]
        [SerializeField] private float searchTimeout = 30f;

        // How much longer every guard searches once a body has been
        // found.
        [Tooltip("How many times longer every guard searches once a body has been found.")]
        [SerializeField] private float alertedSearchScale = 2f;

        [Header("Chase")]

        // Seconds without seeing the player before a chase becomes a
        // search, and how close is caught, in metres.
        [Tooltip("Seconds without seeing the player before a chase becomes a search.")]
        [SerializeField] private float chaseLostTime = 4f;
        [Tooltip("How close, in metres, a seen player is caught.")]
        [SerializeField] private float catchRange = 1.1f;

        [Header("Knocking Out")]

        // How squarely behind the guard the attacker must be: the least
        // dot product of the guard's facing and the way from attacker to
        // guard (1 = dead behind, 0 = level with the shoulders).
        [Tooltip("How squarely behind the guard a blackjack swing must come from to knock it out: least dot of its facing and the way from attacker to guard (1 = dead behind, 0 = level with the shoulders).")]
        [SerializeField] private float behindDot = 0.2f;

        // A body lying in at least this much light (0-1) can be seen by
        // another guard.
        [Tooltip("A knocked-out body lying in at least this much light (0-1) can be seen by another guard.")]
        [SerializeField] private float bodyLightThreshold = 0.15f;

        // Seconds a guard spends over a knocked-out guard to wake it.
        [Tooltip("Seconds a guard spends over a knocked-out guard to wake it.")]
        [SerializeField] private float wakeTime = 3f;

        [Header("Sounds")]

        // Its footsteps, by the floor it's on. Optional (empty = silent).
        [Tooltip("Its footsteps, by the floor it's on. Optional (empty = silent).")]
        [SerializeField] private SurfaceSounds surfaceSounds;

        // How loud a step taken standing (turning on the spot, or
        // shuffling a foot back under it) is, against a walking step's 1.
        // When steps fall is the animation's: walking and running steps
        // come from GuardAnimation, standing ones from GuardFootLock.
        [Tooltip("Volume of a step taken standing (turning on the spot), against a walking step's 1. Step timing comes from the animation (GuardAnimation, GuardFootLock).")]
        [Range(0f, 1f)]
        [SerializeField] private float standingStepVolume = 0.35f;

        // How far a walking step's noise carries, as a share of the
        // footstep cue's noise radius (as the player's walking step, 1).
        // Guards ignore guards' noises: this is for the noise circles.
        [Tooltip("How far a footstep's noise carries, as a share of the footstep cue's noise radius (standing steps scaled by Standing Step Volume). Guards ignore guards' noises; shows as noise circles.")]
        [SerializeField] private float footstepNoiseScale = 1f;

        // What counts as floor for finding the surface.
        [Tooltip("What counts as floor when finding the surface underfoot for footstep sounds.")]
        [SerializeField] private LayerMask groundLayers;

        // What it says as it changes state - how the player reads what a
        // guard is thinking. All optional.
        [Tooltip("Said on becoming Suspicious. Optional.")]
        [SerializeField] private SoundCue suspiciousBark;
        [Tooltip("Said on starting a search. Optional.")]
        [SerializeField] private SoundCue searchBark;
        [Tooltip("Said on giving up a search. Optional.")]
        [SerializeField] private SoundCue gaveUpBark;
        [Tooltip("Said on spotting the player and starting a chase. Optional.")]
        [SerializeField] private SoundCue spottedBark;
        [Tooltip("Said on finding a knocked-out guard. Optional.")]
        [SerializeField] private SoundCue foundBodyBark;

        // How close counts as having arrived, on top of the agent's own
        // stopping distance, in metres.
        private const float ArriveMargin = 0.15f;

        // Setting off from standing, it turns on the spot until it faces
        // the first leg of its path to within this many degrees, then
        // walks; the agent turns it the rest of the way as it goes.
        private const float WalkOffAngle = 15f;

        // Slower than this, in metres a second, counts as standing.
        public const float StandingSpeed = 0.1f;

        // Looking about during a search: seconds it holds each way it
        // looks, how far round each new look is, in degrees, and how
        // close to it counts as there.
        private const float LookAboutHold = 0.8f;
        private const float LookAboutMinAngle = 60f;
        private const float LookAboutMaxAngle = 120f;
        private const float LookAboutSettledAngle = 0.5f;

        // How far a search point may be moved to land on the NavMesh.
        private const float SampleDistance = 2f;

        // A noise heard during a search counts as coming from the place
        // already being searched if it's within this of it, in metres.
        private const float SamePlaceDistance = 1.5f;

        // How far above or below the guard the player can be and still
        // be caught, in metres (not through a floor).
        private const float CatchHeight = 1.5f;

        // How near a guard must get to a knocked-out guard to wake it,
        // in metres, level.
        private const float WakeReach = 1.5f;

        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly Color _patrolColor = new(0.45f, 0.5f, 0.6f, 1f);
        private static readonly Color _suspiciousColor = new(1f, 0.9f, 0.2f, 1f);
        private static readonly Color _searchColor = new(1f, 0.55f, 0.1f, 1f);
        private static readonly Color _chaseColor = new(1f, 0.15f, 0.1f, 1f);
        private static readonly Color _stunnedColor = new(0.7f, 0.3f, 1f, 1f);
        private static readonly Color _knockedOutColor = new(0.15f, 0.15f, 0.18f, 1f);

        // Every guard in the scene, knocked out or not; each by its root
        // object (to tell a guard's own sounds from the player's), by
        // its head collider (for the blackjack) and by its body
        // colliders - the capsule and the ragdoll's hit boxes (for
        // bolts). They add and remove themselves, so nothing searches
        // the scene.
        private static readonly List<Guard> _all = new();
        private static readonly Dictionary<Transform, Guard> _byRoot = new();
        private static readonly Dictionary<Collider, Guard> _byHead = new();
        private static readonly Dictionary<Collider, Guard> _byBody = new();

        // True once any guard has found a body: every search is longer.
        private static bool _isAlerted;

        private NavMeshAgent _agent;
        private MaterialPropertyBlock _tint;

        // The ragdoll on the model's bones, if it has one: the hit boxes
        // bolts find, and the body that falls when knocked out.
        private GuardRagdoll _ragdoll;

        // True while woken and rising (GuardRagdoll.StandUp): it does
        // nothing else until it stands.
        private bool _isGettingUp;

        // True while it stands turning to face the way it is about to
        // walk, the agent held still (TickTurningToWalk).
        private bool _isTurningToWalk;

        // How fast it is turning on the spot now, in degrees a second,
        // and the frame it last turned (TurnTowards): a turn not carried
        // on from the frame before starts again from still.
        private float _turnVelocity;
        private int _lastTurnFrame = -1;

        // Where it stands if it has no route.
        private Vector3 _postPosition;
        private Quaternion _postRotation;

        // How far the furthest vision zone reaches, worked out once.
        private float _sightReach;

        // When each meter was last raised: it holds until its hold time
        // has passed since.
        private float _suspicionRaisedTime;
        private float _detectionRaisedTime;

        // Looking: seconds until the next look, when the last one was,
        // and whether the player was seen in it.
        private float _lookTimer;
        private float _lastLookTime;
        private bool _isPlayerSeen;

        // Where the player was last seen or a noise last heard from.
        private Vector3 _stimulus;

        // Patrol: which point it's heading for, which way along the
        // route, and seconds left to wait at a point.
        private int _routeIndex;
        private int _routeStep = 1;
        private float _waitTimer;

        // The speed of the current state (patrolSpeed or chaseSpeed); the
        // agent is given this, or less while braking to arrive.
        private float _cruiseSpeed;
        private bool _isWaiting;

        // Suspicious: seconds left, and the highest the detection meter
        // got.
        private float _suspiciousTimer;
        private float _peakMeter;

        // Search: where it began, how many places it has tried, seconds
        // left looking about at this one, and seconds left altogether.
        private Vector3 _searchOrigin;
        private int _searchPointsDone;
        private float _searchLookTimer;
        private float _searchTimer;
        private bool _isLookingAbout;

        // Looking about: the way it is turning to or holding, seconds
        // left holding it, and which side it turned last (+1 / -1).
        private Quaternion _lookAboutRotation;
        private float _lookAboutHoldTimer;
        private float _lookAboutSide;

        // Chase: seconds since the player was last seen.
        private float _lostTimer;

        // Stunned: seconds until it comes round.
        private float _stunTimer;

        // A sleep dart: seconds until it knocks the guard out. 0 = none
        // in it.
        private float _sleepTimer;

        // Footsteps: the last surface found.
        private SurfaceType _lastSurface;

        // True while another guard is on its way to wake this body, so
        // no second guard sets off for it.
        private bool _isBodyFound;

        // The knocked-out guard this one is going to wake, and seconds
        // left over it once there.
        private Guard _wakeTarget;
        private float _wakeTimer;

        // How the drawn parts sat before it was laid down, to stand it
        // up again.
        private Vector3 _visualPosition;
        private Quaternion _visualRotation;

        /// What it's doing.
        public GuardState State { get; private set; } = GuardState.Patrol;

        /// How sure it is something is there: full = Suspicious.
        public float Suspicion { get; private set; }

        /// How sure it is that's a person: 0 = not at all, 1 = chasing.
        public float Detection { get; private set; }

        public bool IsKnockedOut => State == GuardState.KnockedOut;

        // Standing and looking about: turned towards what it noticed
        // (Suspicious), or at a place in a search. Read by
        // GuardAnimation for the look-around clip.
        public bool IsLookingAbout => State == GuardState.Suspicious || (State == GuardState.Search && _isLookingAbout);

        /// Where its ears are (INoiseListener).
        public Vector3 EarPosition => eye.position;

        // Where its body is, for another guard looking for it: the
        // ragdoll's hips while it lies limp (they fall away from the
        // root), else a little above the root.
        public Vector3 BodyPosition => _ragdoll != null && _ragdoll.IsLimp ? _ragdoll.HipsPosition : transform.position + Vector3.up * 0.3f;

        /// <summary>
        /// The guard whose head a collider is, or null - for the
        /// blackjack. A dictionary lookup, no GetComponent.
        /// </summary>
        public static Guard FindByHead(Collider collider)
        {
            return _byHead.TryGetValue(collider, out Guard guard) ? guard : null;
        }

        /// <summary>
        /// The guard whose body a collider is, or null - for bolts, whose
        /// rays find the ragdoll's hit boxes. A dictionary lookup, no
        /// GetComponent.
        /// </summary>
        public static Guard FindByBody(Collider collider)
        {
            return _byBody.TryGetValue(collider, out Guard guard) ? guard : null;
        }

        /// <summary>
        /// Editor-only: fills in what it can when the component is added.
        /// </summary>
        private void Reset()
        {
            bodyCollider = GetComponent<Collider>();
            sightLayers = LayerMask.GetMask("Environment", "Interactable");
            groundLayers = LayerMask.GetMask("Environment", "Interactable");
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: the Testing toggle. Acted on just after the
        /// Inspector change (delayCall), not inside OnValidate(), where
        /// physics and the Animator mustn't be changed.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying) {
                UnityEditor.EditorApplication.delayCall += ApplyKnockedOutToggle;
            }
        }

        private void ApplyKnockedOutToggle()
        {
            if (this == null || !Application.isPlaying || _agent == null) {
                return;
            }

            if (knockedOut && !IsKnockedOut) {
                KnockOut();
            } else if (!knockedOut && IsKnockedOut) {
                WakeUp();
            }
        }
#endif

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _tint = new MaterialPropertyBlock();

            if (sightLayers.value == 0) {
                sightLayers = LayerMask.GetMask("Environment", "Interactable");
            }

            if (groundLayers.value == 0) {
                groundLayers = LayerMask.GetMask("Environment", "Interactable");
            }

            if (eye == null) {
                Debug.LogError($"Guard '{name}': no Eye assigned.", this);
                enabled = false;
                return;
            }

            if (sight == null) {
                Debug.LogError($"Guard '{name}': no Sight assigned (Assets/Data/AI/GuardSight.asset).", this);
                enabled = false;
                return;
            }

            _sightReach = sight.Reach;
            _postPosition = transform.position;
            _postRotation = transform.rotation;

            if (visual != null) {
                visual.GetLocalPositionAndRotation(out _visualPosition, out _visualRotation);
            }

            // Each guard starts its looking at a different moment, so
            // they never all look in the same frame.
            _lookTimer = Random.Range(0f, lookInterval);
            _lastLookTime = Time.time;

            _all.Add(this);
            _byRoot[transform] = this;

            if (headCollider != null) {
                _byHead[headCollider] = this;
            }

            if (bodyCollider != null) {
                _byBody[bodyCollider] = this;
            }

            _ragdoll = GetComponent<GuardRagdoll>();

            if (_ragdoll != null && _ragdoll.Hitboxes != null) {
                foreach (Collider hitbox in _ragdoll.Hitboxes) {
                    _byBody[hitbox] = this;
                }
            }
        }

        private void Start()
        {
            if (!_agent.isOnNavMesh) {
                Debug.LogWarning($"Guard '{name}' isn't on a NavMesh, so it can't walk. Bake one (TeaLeaf > Dev Room > Set Up Test Guard).", this);
            }

            EnterPatrol();
        }

        private void OnEnable()
        {
            NoiseSystem.Register(this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            NoiseSystem.Unregister(this);
            DebugDrawRegistry.Unregister(this);
        }

        private void OnDestroy()
        {
            _all.Remove(this);
            _byRoot.Remove(transform);

            if (headCollider != null) {
                _byHead.Remove(headCollider);
            }

            if (bodyCollider != null) {
                _byBody.Remove(bodyCollider);
            }

            if (_ragdoll != null && _ragdoll.Hitboxes != null) {
                foreach (Collider hitbox in _ragdoll.Hitboxes) {
                    _byBody.Remove(hitbox);
                }
            }

            // The last guard of a scene going puts the alert back, so a
            // restarted level starts calm.
            if (_all.Count == 0) {
                _isAlerted = false;
            }
        }

        private void Update()
        {
            // The level has ended (caught or won): stand still through
            // the fade.
            LevelManager levelManager = LevelManager.Instance;

            if (levelManager != null && levelManager.State != GameState.Playing) {
                Stop();
                return;
            }

            float deltaTime = Time.deltaTime;

            // Woken and rising: nothing else until it stands.
            if (_isGettingUp) {
                if (_ragdoll.IsStandingUp) {
                    return;
                }

                _isGettingUp = false;
                GetUp();
            }

            // A sleep dart works whatever the guard is doing, stunned
            // included.
            if (_sleepTimer > 0f) {
                _sleepTimer -= deltaTime;

                if (_sleepTimer <= 0f) {
                    KnockOut();
                    return;
                }
            }

            // Stunned: no looking, no walking, only the wait.
            if (State == GuardState.Stunned) {
                TickStunned(deltaTime);
                return;
            }

            _lookTimer -= deltaTime;

            if (_lookTimer <= 0f) {
                Look();
            }

            TickTurningToWalk(deltaTime);

            switch (State) {
                case GuardState.Patrol:
                    TickPatrol(deltaTime);
                    break;
                case GuardState.Suspicious:
                    TickSuspicious(deltaTime);
                    break;
                case GuardState.Search:
                    TickSearch(deltaTime);
                    break;
                case GuardState.Chase:
                    TickChase(deltaTime);
                    break;
            }

            TickArrivalBraking();
        }

        // ---- Sight -------------------------------------------------------

        /// <summary>
        /// One look: is the player in view, and how much does that add
        /// to (or, unseen, take from) the detection meter? Then whatever
        /// the new meter means for the state. Also notices bodies.
        /// </summary>
        private void Look()
        {
            float now = Time.time;
            float elapsed = now - _lastLookTime;
            _lastLookTime = now;
            _lookTimer = lookInterval;
            _isPlayerSeen = false;

            PlayerVisibility player = PlayerVisibility.Instance;

            if (player == null) {
                return;
            }

            Vector3 eyePosition = eye.position;

            // Blind to the player: the meter only drains (a chase runs
            // out by its own timer), and bodies are still noticed.
            if (!detectsPlayer) {
                Drain(elapsed);

                if (State != GuardState.Chase) {
                    LookForBodies(eyePosition);
                }

                return;
            }

            // Too far to be seen whatever happens: no rays, and the next
            // look can wait longer.
            if (Vector3.Distance(eyePosition, player.HeadPosition) > _sightReach + 2f) {
                _lookTimer = farLookInterval;
                Drain(elapsed);
                return;
            }

            // Five points on the player, each with a weight (head sides,
            // top, under the head, waist). A point counts if a zone can
            // see it lit or dark as the player is, and nothing is in the
            // way - the zone test first, so a point no zone can see
            // costs no ray. Enough weight showing is the player seen,
            // and the fastest fill of any point showing is the rate.
            Quaternion toEye = Quaternion.Inverse(eye.rotation);
            bool isLit = player.Visibility >= sight.LitThreshold;
            float speed = player.Speed;
            bool isCrouched = player.IsCrouched;
            float weight = 0f;
            float suspicionTime = -1f;
            float detectionTime = -1f;

            for (int i = 0; i < PlayerVisibility.SightPointCount; i++) {
                Vector3 point = player.GetSightPoint(i);
                Vector3 local = toEye * (point - eyePosition);

                if (!sight.TryGetFillTimes(local, isLit, speed, isCrouched, out float pointSuspicion, out float pointDetection)) {
                    continue;
                }

                if (Physics.Linecast(eyePosition, point, sightLayers, QueryTriggerInteraction.Ignore)) {
                    continue;
                }

                weight += player.GetSightWeight(i);
                suspicionTime = FastestTime(suspicionTime, pointSuspicion);
                detectionTime = FastestTime(detectionTime, pointDetection);
            }

            if (weight >= sight.SeenWeight) {
                _isPlayerSeen = true;
                _stimulus = player.FeetPosition;

                // Each meter fills in its own time; a meter this view
                // can't fill (never) drains as it would unseen.
                if (suspicionTime >= 0f) {
                    RaiseSuspicion(elapsed / Mathf.Max(suspicionTime, 0.001f));
                } else {
                    DrainSuspicion(elapsed);
                }

                if (detectionTime >= 0f) {
                    RaiseDetection(elapsed / Mathf.Max(detectionTime, 0.001f));
                } else {
                    DrainDetection(elapsed);
                }
            } else {
                Drain(elapsed);
            }

            if (State != GuardState.Chase && HasBumped(player)) {
                // Walked into: it knows something is there and where,
                // and turns to look. Seeing them is then up to its eyes.
                _stimulus = player.FeetPosition;
                RaiseToInvestigate();

                if (State != GuardState.Suspicious) {
                    EnterSuspicious();
                }
            }

            if (State == GuardState.Chase) {
                // Running at where the player is while they're in view;
                // asked for here, a few times a second, not every frame.
                if (_isPlayerSeen) {
                    GoTo(_stimulus);
                }

                return;
            }

            if (Detection >= 1f) {
                EnterChase();
            } else if (State == GuardState.Patrol && Suspicion >= 1f) {
                EnterSuspicious();
            } else if (State != GuardState.Chase) {
                LookForBodies(eyePosition);
            }
        }

        /// <summary>
        /// Whether the player's body is against the guard's: within the
        /// bump range of it, level, and on the same floor. Two numbers
        /// compared, no physics.
        /// </summary>
        private bool HasBumped(PlayerVisibility player)
        {
            Vector3 toPlayer = player.FeetPosition - transform.position;
            float height = Mathf.Abs(toPlayer.y);
            toPlayer.y = 0f;

            return toPlayer.sqrMagnitude <= bumpRange * bumpRange && height <= CatchHeight;
        }

        /// <summary>
        /// The faster of two fill times, where below zero is never.
        /// </summary>
        private static float FastestTime(float a, float b)
        {
            if (a < 0f) {
                return b;
            }

            return b < 0f ? a : Mathf.Min(a, b);
        }

        /// <summary>
        /// Both meters draining with nothing to notice.
        /// </summary>
        private void Drain(float elapsed)
        {
            DrainSuspicion(elapsed);
            DrainDetection(elapsed);
        }

        /// <summary>
        /// The suspicion meter draining: nothing until its hold time has
        /// passed since it was last raised, then at its decay rate.
        /// </summary>
        private void DrainSuspicion(float elapsed)
        {
            float draining = Mathf.Min(elapsed, Time.time - _suspicionRaisedTime - suspicionHoldTime);

            if (draining > 0f) {
                Suspicion = Mathf.Max(Suspicion - draining * suspicionDecayRate, 0f);
            }
        }

        /// <summary>
        /// The detection meter draining, as suspicion but on its own
        /// hold and rate. Not while chasing: a chase ends by its own
        /// timer.
        /// </summary>
        private void DrainDetection(float elapsed)
        {
            if (State == GuardState.Chase) {
                return;
            }

            float draining = Mathf.Min(elapsed, Time.time - _detectionRaisedTime - detectionHoldTime);

            if (draining > 0f) {
                Detection = Mathf.Max(Detection - draining * detectionDecayRate, 0f);
            }
        }

        /// <summary>
        /// Adds to the suspicion meter (capped at full) and starts its
        /// hold again.
        /// </summary>
        private void RaiseSuspicion(float amount)
        {
            Suspicion = Mathf.Min(Suspicion + amount, 1f);
            _suspicionRaisedTime = Time.time;
        }

        /// <summary>
        /// Adds to the detection meter (capped at full) and starts its
        /// hold again.
        /// </summary>
        private void RaiseDetection(float amount)
        {
            Detection = Mathf.Min(Detection + amount, 1f);
            _detectionRaisedTime = Time.time;
        }

        /// <summary>
        /// Something it knows is there (a bump, a bolt, a body, coming
        /// round): suspicion full, detection at least enough to go and
        /// search.
        /// </summary>
        private void RaiseToInvestigate()
        {
            RaiseSuspicion(1f);
            RaiseDetection(Mathf.Max(investigateThreshold - Detection, 0f));
        }

        /// <summary>
        /// Both meters emptied: it has decided it was nothing.
        /// </summary>
        private void ClearMeters()
        {
            Suspicion = 0f;
            Detection = 0f;
        }

        /// <summary>
        /// Whether a point is in view whatever the light: inside a vision
        /// zone, with nothing solid in the way - one physics ray. For
        /// bodies, which bring their own light test.
        /// </summary>
        private bool CanSeePoint(Vector3 eyePosition, Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(eye.rotation) * (point - eyePosition);

            if (!sight.Contains(local)) {
                return false;
            }

            return !Physics.Linecast(eyePosition, point, sightLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Notices a knocked-out guard lying in view and in enough light:
        /// goes to it to wake it, and from then on every guard searches
        /// for longer.
        /// </summary>
        private void LookForBodies(Vector3 eyePosition)
        {
            for (int i = 0; i < _all.Count; i++) {
                Guard other = _all[i];

                if (!other.IsKnockedOut || other._isBodyFound) {
                    continue;
                }

                Vector3 body = other.BodyPosition;

                if (!CanSeePoint(eyePosition, body) || SceneLight.LevelAt(body) < bodyLightThreshold) {
                    continue;
                }

                other._isBodyFound = true;
                _isAlerted = true;
                _stimulus = other.BodyPosition;
                RaiseToInvestigate();

                Bark(foundBodyBark);
                EnterSearch(false);

                // Set after the search has begun: beginning one drops
                // whichever body it was going to before.
                _wakeTarget = other;
                _wakeTimer = wakeTime;
                return;
            }
        }

        // ---- Hearing -----------------------------------------------------

        /// <summary>
        /// A noise has reached this guard (NoiseSystem): it adds to both
        /// meters by how loud it was here, and is somewhere to look.
        /// Another guard's noises are ignored, and so is everything
        /// during a chase or while stunned.
        /// </summary>
        public void OnNoiseHeard(in Noise noise, float loudness, Vector3 heardFrom)
        {
            if (!detectsPlayer || State == GuardState.Chase || State == GuardState.Stunned || State == GuardState.KnockedOut) {
                return;
            }

            if (noise.Source != null && _byRoot.ContainsKey(noise.Source)) {
                return;
            }

            float gain = noise.Type == NoiseType.Distraction ? distractionGain : hearingGain;

            RaiseDetection(loudness * gain);
            RaiseSuspicion(loudness * gain * hearingSuspicionScale);
            _stimulus = heardFrom;

            switch (State) {
                case GuardState.Patrol:
                    if (Suspicion >= 1f) {
                        EnterSuspicious();
                    }

                    break;

                case GuardState.Suspicious:
                    _peakMeter = Mathf.Max(_peakMeter, Detection);
                    break;

                case GuardState.Search:
                    // A new noise worth looking at moves the search - but
                    // only one from somewhere else. More noise from the
                    // place already being searched would start that search
                    // again each time, and the guard would never move on.
                    if (Detection >= investigateThreshold && (_stimulus - _searchOrigin).sqrMagnitude > SamePlaceDistance * SamePlaceDistance) {
                        BeginSearchAt(_stimulus);
                    }

                    break;
            }
        }

        // ---- Patrol ------------------------------------------------------

        private void EnterPatrol()
        {
            SetState(GuardState.Patrol);
            _cruiseSpeed = patrolSpeed;
            _isWaiting = false;

            if (patrolRoute != null && patrolRoute.Count > 0) {
                // Takes the route up again at its nearest point.
                _routeIndex = patrolRoute.NearestIndex(transform.position);
                GoTo(patrolRoute.GetPoint(_routeIndex));
            } else {
                GoTo(_postPosition);
            }
        }

        private void TickPatrol(float deltaTime)
        {
            if (!HasArrived()) {
                return;
            }

            // No route: stands at its post, facing the way it was put.
            if (patrolRoute == null || patrolRoute.Count == 0) {
                TurnTowards(_postRotation, deltaTime);
                return;
            }

            if (!_isWaiting) {
                _isWaiting = true;
                _waitTimer = patrolRoute.WaitTime;
            }

            _waitTimer -= deltaTime;

            if (_waitTimer > 0f) {
                return;
            }

            _isWaiting = false;
            AdvanceRoute();
            GoTo(patrolRoute.GetPoint(_routeIndex));
        }

        /// <summary>
        /// The next point of the route: round again from the end if it
        /// loops, otherwise turning back at each end.
        /// </summary>
        private void AdvanceRoute()
        {
            int count = patrolRoute.Count;

            if (count < 2) {
                return;
            }

            if (patrolRoute.Loops) {
                _routeIndex = (_routeIndex + 1) % count;
                return;
            }

            if (_routeIndex + _routeStep >= count || _routeIndex + _routeStep < 0) {
                _routeStep = -_routeStep;
            }

            _routeIndex += _routeStep;
        }

        // ---- Suspicious --------------------------------------------------

        private void EnterSuspicious()
        {
            SetState(GuardState.Suspicious);
            Stop();
            _suspiciousTimer = suspiciousTime;
            _peakMeter = Detection;
            Bark(suspiciousBark);
        }

        private void TickSuspicious(float deltaTime)
        {
            _peakMeter = Mathf.Max(_peakMeter, Detection);
            FaceTowards(_stimulus, deltaTime);

            _suspiciousTimer -= deltaTime;

            if (_suspiciousTimer > 0f) {
                return;
            }

            // Enough to go on: goes to look. Otherwise it was nothing,
            // and the meters are emptied - left full, the very next look
            // would make it suspicious again, and it would stand there
            // for as long as the meters took to drain.
            if (_peakMeter >= investigateThreshold) {
                EnterSearch(true);
            } else {
                ClearMeters();
                EnterPatrol();
            }
        }

        // ---- Search ------------------------------------------------------

        private void EnterSearch(bool withBark)
        {
            SetState(GuardState.Search);
            _cruiseSpeed = patrolSpeed;
            _searchTimer = searchTimeout * (_isAlerted ? alertedSearchScale : 1f);

            if (withBark) {
                Bark(searchBark);
            }

            BeginSearchAt(_stimulus);
        }

        /// <summary>
        /// Starts (or restarts) the search round a place: goes there
        /// first, then tries places round it.
        /// </summary>
        private void BeginSearchAt(Vector3 place)
        {
            DropWakeTarget();
            _searchOrigin = place;
            _searchPointsDone = 0;
            _isLookingAbout = false;
            GoTo(place);
        }

        private void TickSearch(float deltaTime)
        {
            _searchTimer -= deltaTime;

            if (_searchTimer <= 0f) {
                GiveUp();
                return;
            }

            if (!HasArrived()) {
                return;
            }

            // At a knocked-out guard: wakes it before anything else.
            if (_wakeTarget != null && TickWaking(deltaTime)) {
                return;
            }

            // At a place: looks about for a while - stands looking one
            // way, then turns to look another, alternating sides.
            if (!_isLookingAbout) {
                _isLookingAbout = true;
                _searchLookTimer = searchLookTime;
                _lookAboutHoldTimer = LookAboutHold;
                _lookAboutRotation = transform.rotation;
                _lookAboutSide = Random.value < 0.5f ? -1f : 1f;
            }

            _searchLookTimer -= deltaTime;
            bool isTurning = TickLookAbout(deltaTime);

            // Time up: moves on, but never in the middle of a turn.
            if (_searchLookTimer > 0f || isTurning) {
                return;
            }

            _isLookingAbout = false;

            if (_searchPointsDone >= searchPointCount) {
                GiveUp();
                return;
            }

            _searchPointsDone++;

            // A place somewhere round where the search began, moved onto
            // the nearest walkable ground. If there is none near it, the
            // next try will pick another.
            Vector2 offset = Random.insideUnitCircle * searchRadius;
            Vector3 candidate = _searchOrigin + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, SampleDistance, NavMesh.AllAreas)) {
                GoTo(hit.position);
            }
        }

        /// <summary>
        /// One step of looking about at a search place: turns to the
        /// heading it is looking for with the same eased turn as every
        /// other turn on the spot (TurnTowards), holds it LookAboutHold
        /// seconds, then picks the next heading LookAboutMinAngle to
        /// LookAboutMaxAngle round, the other way from the last. Returns
        /// whether it is still turning.
        /// </summary>
        private bool TickLookAbout(float deltaTime)
        {
            if (Quaternion.Angle(transform.rotation, _lookAboutRotation) > LookAboutSettledAngle) {
                TurnTowards(_lookAboutRotation, deltaTime);
                return true;
            }

            _lookAboutHoldTimer -= deltaTime;

            if (_lookAboutHoldTimer <= 0f) {
                _lookAboutSide = -_lookAboutSide;
                float angle = _lookAboutSide * Random.Range(LookAboutMinAngle, LookAboutMaxAngle);
                _lookAboutRotation = transform.rotation * Quaternion.Euler(0f, angle, 0f);
                _lookAboutHoldTimer = LookAboutHold;
            }

            return false;
        }

        /// <summary>
        /// Standing over the knocked-out guard it came for: after the
        /// wake time, that guard gets up. Returns whether it is still
        /// busy with it.
        /// </summary>
        private bool TickWaking(float deltaTime)
        {
            Guard body = _wakeTarget;
            Vector3 toBody = body.BodyPosition - transform.position;
            toBody.y = 0f;

            // As near as it can get and still out of reach (the body is
            // somewhere it can't walk to): it gives the body up. The
            // body stays marked as found, or every guard that saw it
            // would set off for it again at each look.
            if (toBody.sqrMagnitude > WakeReach * WakeReach) {
                _wakeTarget = null;
                return false;
            }

            FaceTowards(body.BodyPosition, deltaTime);
            _wakeTimer -= deltaTime;

            if (_wakeTimer > 0f) {
                return true;
            }

            _wakeTarget = null;
            body.WakeUp();
            return false;
        }

        /// <summary>
        /// Gives up the body it was going to wake, if any, leaving it
        /// for any guard to find again. Called on every change of state
        /// and whenever a search moves somewhere else.
        /// </summary>
        private void DropWakeTarget()
        {
            if (_wakeTarget != null) {
                _wakeTarget._isBodyFound = false;
                _wakeTarget = null;
            }
        }

        private void GiveUp()
        {
            ClearMeters();
            Bark(gaveUpBark);
            EnterPatrol();
        }

        // ---- Chase -------------------------------------------------------

        private void EnterChase()
        {
            SetState(GuardState.Chase);
            RaiseSuspicion(1f);
            RaiseDetection(1f);
            _cruiseSpeed = chaseSpeed;
            _lostTimer = 0f;
            Bark(spottedBark);
            GoTo(_stimulus);
        }

        private void TickChase(float deltaTime)
        {
            PlayerVisibility player = PlayerVisibility.Instance;

            if (player == null) {
                return;
            }

            if (_isPlayerSeen) {
                _lostTimer = 0f;

                // Caught: close enough, on the same floor. Ending the
                // level is the level manager's business.
                Vector3 toPlayer = player.FeetPosition - transform.position;
                float height = Mathf.Abs(toPlayer.y);
                toPlayer.y = 0f;

                if (toPlayer.sqrMagnitude <= catchRange * catchRange && height <= CatchHeight) {
                    Stop();

                    LevelManager levelManager = LevelManager.Instance;

                    if (levelManager != null) {
                        levelManager.Caught();
                    }
                }

                return;
            }

            // Lost them: runs on to where they were last seen, and after
            // a while starts searching from there.
            _lostTimer += deltaTime;

            if (_lostTimer >= chaseLostTime) {
                Detection = 0f;
                RaiseToInvestigate();
                EnterSearch(true);
            }
        }

        // ---- Bolts -------------------------------------------------------

        /// <summary>
        /// Whether a hit box a bolt found is this guard's head: the
        /// ragdoll's head bone collider, which moves with the animation.
        /// </summary>
        public bool IsHead(Collider hitbox)
        {
            return _ragdoll != null && hitbox == _ragdoll.HeadHitbox;
        }

        /// <summary>
        /// Hit by a bolt that does nothing to it (water, a noisemaker,
        /// a rope bolt, a stun bolt to the body): it knows it was shot
        /// at and roughly where from. On patrol it stops and turns that
        /// way, then goes to search there; already searching, the search
        /// moves there. Nothing new to a guard that is chasing, stunned
        /// or down.
        /// </summary>
        public void HitByBolt(Vector3 shotFrom)
        {
            if (!detectsPlayer || State == GuardState.Chase || State == GuardState.Stunned || State == GuardState.KnockedOut) {
                return;
            }

            _stimulus = shotFrom;
            RaiseToInvestigate();

            switch (State) {
                case GuardState.Patrol:
                    // The meter is high enough that the suspicion ends
                    // in a search, not a shrug.
                    EnterSuspicious();
                    break;

                case GuardState.Suspicious:
                    _peakMeter = Mathf.Max(_peakMeter, Detection);
                    break;

                case GuardState.Search:
                    BeginSearchAt(shotFrom);
                    break;
            }
        }

        /// <summary>
        /// A stun bolt to the head: the guard stops whatever it was
        /// doing - a chase included - and stands senseless for this many
        /// seconds, then searches. Stunned again meanwhile, the wait
        /// starts again.
        /// </summary>
        public void Stun(float seconds)
        {
            if (State == GuardState.KnockedOut) {
                return;
            }

            SetState(GuardState.Stunned);
            Stop();

            // Stopped dead, not left to slow down from a run.
            if (_agent.isOnNavMesh) {
                _agent.velocity = Vector3.zero;
            }

            _stunTimer = seconds;
            _isPlayerSeen = false;
        }

        private void TickStunned(float deltaTime)
        {
            _stunTimer -= deltaTime;

            if (_stunTimer > 0f) {
                return;
            }

            // Comes round knowing something happened but not where from:
            // it searches round where it stands. The look timer starts
            // afresh, or the first look would count the whole stun as
            // time spent seeing (or not seeing) the player.
            _stimulus = transform.position;
            Detection = 0f;
            RaiseToInvestigate();
            _lastLookTime = Time.time;
            _lookTimer = lookInterval;
            EnterSearch(true);
        }

        /// <summary>
        /// A sleep dart, anywhere on the body: the guard carries on as
        /// it was, and is knocked out after this many seconds. A second
        /// dart doesn't start the wait again.
        /// </summary>
        public void Sedate(float seconds)
        {
            if (State != GuardState.KnockedOut && _sleepTimer <= 0f) {
                _sleepTimer = Mathf.Max(seconds, 0.01f);
            }
        }

        // ---- Knocking out ------------------------------------------------

        /// <summary>
        /// A blow to the head from attackerPosition. It knocks the guard
        /// out if the guard is stunned, or if it isn't already chasing
        /// and the attacker is behind it; otherwise the guard turns on
        /// them. Returns whether it went down.
        /// </summary>
        public bool TryKnockOut(Vector3 attackerPosition)
        {
            if (State == GuardState.KnockedOut) {
                return false;
            }

            // The way from the attacker to the guard, level. If that is
            // the way the guard faces, the attacker is behind it.
            Vector3 fromAttacker = transform.position - attackerPosition;
            fromAttacker.y = 0f;

            bool isBehind = Vector3.Dot(transform.forward, fromAttacker.normalized) >= behindDot;

            // A stunned guard can't turn on anyone: it goes down from any
            // side.
            if (State == GuardState.Stunned || (State != GuardState.Chase && isBehind)) {
                KnockOut();
                return true;
            }

            if (State != GuardState.Chase && detectsPlayer) {
                _stimulus = attackerPosition;
                EnterChase();
            }

            return false;
        }

        /// <summary>
        /// Down: it stops, its colliders go (so it can be stepped over),
        /// the ragdoll falls carrying on at the speed it was moving (or
        /// a greybox visual is laid flat), and the component switches
        /// off - no more looking, hearing or Update() until another
        /// guard wakes it.
        /// </summary>
        private void KnockOut()
        {
            SetState(GuardState.KnockedOut);
            ClearMeters();
            _sleepTimer = 0f;

            if (_ragdoll != null) {
                _ragdoll.GoLimp(_agent.enabled ? _agent.velocity : Vector3.zero);
            }

            _agent.enabled = false;

            if (bodyCollider != null) {
                bodyCollider.enabled = false;
            }

            if (headCollider != null) {
                headCollider.enabled = false;
            }

            // Laid flat on its front, along the way it was facing.
            if (visual != null) {
                visual.SetLocalPositionAndRotation(new Vector3(0f, 0.3f, 0f), Quaternion.Euler(90f, 0f, 0f));
            }

            knockedOut = true;
            enabled = false;
        }

        /// <summary>
        /// Woken by another guard (or the Testing toggle). With a ragdoll
        /// it first rises from where it lay (GuardRagdoll.StandUp, which
        /// also moves the root there), still down until it stands; then
        /// GetUp(). Nothing if it isn't down, or is already rising.
        /// </summary>
        private void WakeUp()
        {
            if (!IsKnockedOut || _isGettingUp) {
                return;
            }

            enabled = true;

            if (_ragdoll != null) {
                _ragdoll.StandUp();
                _isGettingUp = true;
                return;
            }

            GetUp();
        }

        /// <summary>
        /// Standing again: everything KnockOut() switched off comes back,
        /// and it searches round where it lay.
        /// </summary>
        private void GetUp()
        {
            knockedOut = false;
            _isBodyFound = false;
            _agent.enabled = true;

            if (bodyCollider != null) {
                bodyCollider.enabled = true;
            }

            if (headCollider != null) {
                headCollider.enabled = true;
            }

            if (visual != null) {
                visual.SetLocalPositionAndRotation(_visualPosition, _visualRotation);
            }

            // Its senses start afresh: the time it lay there isn't time
            // spent looking.
            _lastLookTime = Time.time;
            _lookTimer = lookInterval;
            _isPlayerSeen = false;

            _stimulus = transform.position;
            ClearMeters();
            RaiseToInvestigate();
            EnterSearch(true);
        }

        // ---- Moving ------------------------------------------------------

        /// <summary>
        /// Asks the agent to walk to a place. Nothing if the guard isn't
        /// on a NavMesh. From standing, the agent is held still until
        /// the guard has turned to face the way (TickTurningToWalk);
        /// already walking, it carries straight on, so a chase renewing
        /// its destination never stops to turn.
        /// </summary>
        private void GoTo(Vector3 place)
        {
            if (!_agent.isOnNavMesh) {
                return;
            }

            if (!_isTurningToWalk && _agent.velocity.sqrMagnitude < StandingSpeed * StandingSpeed) {
                _isTurningToWalk = true;
            }

            _agent.isStopped = _isTurningToWalk;
            _agent.SetDestination(place);
        }

        private void Stop()
        {
            _isTurningToWalk = false;

            if (_agent.isOnNavMesh && _agent.hasPath) {
                _agent.ResetPath();
            }
        }

        /// <summary>
        /// Setting off from standing: turns on the spot towards the
        /// path's first corner (the agent's steering target) and lets the
        /// agent walk once it faces it to within WalkOffAngle. Waits for
        /// a path still being worked out; lets go if there is none, or
        /// the guard is already there.
        /// </summary>
        private void TickTurningToWalk(float deltaTime)
        {
            if (!_isTurningToWalk || _agent.pathPending) {
                return;
            }

            // HasArrived() first: it also covers an agent off the NavMesh,
            // which has no steering target to read.
            Vector3 toTarget = Vector3.zero;

            if (!HasArrived()) {
                toTarget = _agent.steeringTarget - transform.position;
                toTarget.y = 0f;
            }

            if (toTarget.sqrMagnitude < 0.0001f || Vector3.Angle(transform.forward, toTarget) <= WalkOffAngle) {
                _isTurningToWalk = false;

                if (_agent.isOnNavMesh) {
                    _agent.isStopped = false;
                }

                return;
            }

            TurnTowards(Quaternion.LookRotation(toTarget), deltaTime);
        }

        /// <summary>
        /// Eases the guard to a stop at the end of its path: the agent's
        /// speed is capped at sqrt(2 x brakeDeceleration x distance left),
        /// the fastest it can go and still stop on the spot at that
        /// deceleration, so it slows evenly over the last stretch (the
        /// same profile as the end of a turn on the spot). Further out the
        /// cap is above the cruise speed and changes nothing. The agent's
        /// own braking (at its much higher acceleration) only covers the
        /// last few centimetres. A chase is left at full speed.
        /// </summary>
        private void TickArrivalBraking()
        {
            float speed = _cruiseSpeed;

            if (State != GuardState.Chase && _agent.isOnNavMesh && _agent.hasPath && !_agent.pathPending) {
                float distanceLeft = Mathf.Max(0f, _agent.remainingDistance - _agent.stoppingDistance);
                speed = Mathf.Min(speed, Mathf.Sqrt(2f * brakeDeceleration * distanceLeft));
            }

            if (_agent.speed != speed) {
                _agent.speed = speed;
            }
        }

        /// <summary>
        /// Whether the agent has reached where it was sent (or can't get
        /// any nearer). A path still being worked out isn't an arrival.
        /// </summary>
        private bool HasArrived()
        {
            if (!_agent.isOnNavMesh) {
                return true;
            }

            if (_agent.pathPending) {
                return false;
            }

            return !_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + ArriveMargin;
        }

        /// <summary>
        /// Turns on the spot to face a place, level.
        /// </summary>
        private void FaceTowards(Vector3 place, float deltaTime)
        {
            Vector3 toPlace = place - transform.position;
            toPlace.y = 0f;

            if (toPlace.sqrMagnitude > 0.01f) {
                TurnTowards(Quaternion.LookRotation(toPlace), deltaTime);
            }
        }

        /// <summary>
        /// Turns on the spot towards a rotation, eased in and out: the
        /// turn speeds up at turnAcceleration to at most turnSpeed, and
        /// slows at the same rate to stop on the target, as a body
        /// swinging round and settling does (a trapezoid speed profile,
        /// or a triangle when the turn is too short to reach full speed).
        /// Speed rather than time based, so a target that moves while
        /// turning (a stimulus, the path's first corner) needs nothing
        /// restarted.
        /// </summary>
        private void TurnTowards(Quaternion rotation, float deltaTime)
        {
            // Not turned last frame: starting a new turn, from still.
            if (_lastTurnFrame != Time.frameCount - 1) {
                _turnVelocity = 0f;
            }

            _lastTurnFrame = Time.frameCount;

            // The fastest it can be going and still stop in the angle left
            // (v² = 2as), so it slows down in time to land on the target.
            float remaining = Quaternion.Angle(transform.rotation, rotation);
            float stoppingSpeed = Mathf.Sqrt(2f * turnAcceleration * remaining);

            _turnVelocity = Mathf.Min(_turnVelocity + turnAcceleration * deltaTime, turnSpeed, stoppingSpeed);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, _turnVelocity * deltaTime);
        }

        // ---- State, sounds -----------------------------------------------

        /// <summary>
        /// Changes state, and the tint that shows it. The renderer is
        /// only touched here, on a change.
        /// </summary>
        private void SetState(GuardState state)
        {
            DropWakeTarget();
            State = state;

            if (stateRenderer == null) {
                return;
            }

            Color color = state switch {
                GuardState.Suspicious => _suspiciousColor,
                GuardState.Search => _searchColor,
                GuardState.Chase => _chaseColor,
                GuardState.Stunned => _stunnedColor,
                GuardState.KnockedOut => _knockedOutColor,
                _ => _patrolColor
            };

            _tint.SetColor(_baseColorId, color);
            stateRenderer.SetPropertyBlock(_tint);
        }

        /// <summary>
        /// Says something: a cue played from the head, as sound only -
        /// other guards don't react to a guard talking.
        /// </summary>
        private void Bark(SoundCue cue)
        {
            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (cue != null && soundPlayer != null) {
                soundPlayer.Play(cue, eye.position, transform, 0f);
            }
        }

        /// <summary>
        /// A footstep, on whatever floor is under it (one ray down, only
        /// when a step sounds), called when a foot comes down in the
        /// animation: GuardAnimation for walking and running steps,
        /// GuardFootLock for steps taken standing, which play at
        /// standingStepVolume. Also a noise (footstepNoiseScale of the
        /// cue's radius, scaled with the volume): guards ignore each
        /// other's noises, so it changes nothing they do, but it shows
        /// as a noise circle like any other. Nothing while stunned or down.
        /// </summary>
        public void PlayFootstep(bool isStandingStep)
        {
            if (surfaceSounds == null || State == GuardState.Stunned || State == GuardState.KnockedOut) {
                return;
            }

            Vector3 position = transform.position;

            if (SurfaceTag.TryFindBelow(position + Vector3.up * 0.3f, 1f, groundLayers, out SurfaceType surface)) {
                _lastSurface = surface;
            }

            // A little above the floor, so it's inside the sound room
            // being walked through.
            float volume = isStandingStep ? standingStepVolume : 1f;
            surfaceSounds.PlayStep(_lastSurface, position + Vector3.up * 0.1f, transform, footstepNoiseScale * volume, volume);
        }

        // ---- Debug -------------------------------------------------------

        /// <summary>
        /// Editor-only: the view cone, always.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// Draws the vision zones in the state's colour and two bars above
        /// the head (suspicion yellow, detection red, each as long as its
        /// meter is full); in the detailed view, a line to what it last
        /// saw or heard.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (eye == null || sight == null) {
                return;
            }

            lines.Color = State switch {
                GuardState.Suspicious => _suspiciousColor,
                GuardState.Search => _searchColor,
                GuardState.Chase => _chaseColor,
                GuardState.Stunned => _stunnedColor,
                _ => _patrolColor
            };

            Vector3 eyePosition = eye.position;
            sight.DrawZones(lines, eyePosition, eye.rotation);

            // The meters: a line each above the head, as long as it is
            // full.
            Vector3 barStart = eyePosition + Vector3.up * 0.35f - eye.right * 0.25f;
            lines.Color = _suspiciousColor;
            lines.Line(barStart, barStart + eye.right * (0.5f * Suspicion));
            barStart += Vector3.up * 0.05f;
            lines.Color = _chaseColor;
            lines.Line(barStart, barStart + eye.right * (0.5f * Detection));

            if (!detailed) {
                return;
            }

            if (Application.isPlaying && State != GuardState.Patrol) {
                lines.Line(eyePosition, _stimulus);
                lines.WireSphere(_stimulus, 0.15f);
            }
        }
    }
}
