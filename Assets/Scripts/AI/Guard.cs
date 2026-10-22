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
    /// What moves it between them is one number, the detection meter
    /// (0-1). Seeing the player fills it - faster the better lit they
    /// are (PlayerVisibility) and the nearer - and hearing a noise adds
    /// to it at once. It drains when there's nothing to notice. At the
    /// suspicious threshold the guard becomes Suspicious; full, it
    /// chases.
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

        // Where it sees and hears from: an object at head height whose
        // forward is the way the guard faces.
        [SerializeField] private Transform eye;

        // Optional: greybox shapes under one object, laid flat when the
        // guard is knocked out. Left empty on the animated guard, whose
        // Animator plays the fall instead (GuardAnimation).
        [SerializeField] private Transform visual;

        // The body's collider (Guard layer) and the head's (GuardHead
        // layer, what the blackjack hits).
        [SerializeField] private Collider bodyCollider;
        [SerializeField] private Collider headCollider;

        // Optional: a renderer tinted by state, so the state can be read
        // at a glance on a greybox guard. Left empty on the animated
        // guard: the tint would darken its texture.
        [SerializeField] private Renderer stateRenderer;

        [Header("Patrol")]

        // The route it walks. With none, it stands where it was placed.
        [SerializeField] private PatrolRoute patrolRoute;

        [Header("Speeds")]

        // Metres a second walking (patrolling and searching) and running
        // (chasing), and how fast it turns on the spot, in degrees a second.
        [SerializeField] private float patrolSpeed = 1.07f;
        [SerializeField] private float chaseSpeed = 3.6f;
        [SerializeField] private float turnSpeed = 240f;

        [Header("Sight")]

        // How far it can see, and how wide, in degrees (the whole cone).
        [SerializeField] private float viewRange = 14f;
        [SerializeField] private float viewAngle = 110f;

        // Within this distance the player is seen even in full shadow -
        // as if lit this well (0-1).
        [SerializeField] private float closeRange = 2.5f;
        [SerializeField] private float closeVisibility = 0.5f;

        // A player whose body is within this of the guard's, level, has
        // bumped into it: the guard turns on them whichever way it was
        // facing. A little more than the two bodies' radii together.
        // Behind a guard and further off than this, only a noise gives
        // the player away.
        [SerializeField] private float bumpRange = 0.5f;

        // Beyond the close range, a player less visible than this (0-1)
        // isn't seen at all.
        [SerializeField] private float minVisibility = 0.05f;

        // Seconds to go from unaware to chasing with the player close
        // and in plain sight, and how much slower (a fraction) at the
        // far end of the view range.
        [SerializeField] private float detectionTime = 1f;
        [SerializeField] private float farDetectionScale = 0.25f;

        // Seconds a full meter takes to drain with nothing to notice.
        [SerializeField] private float decayTime = 6f;

        // What blocks its sight. Filled in from the layer names if left
        // empty (Environment and Interactable: walls, props, doors).
        [SerializeField] private LayerMask sightLayers;

        [Header("Hearing")]

        // How much of a noise's loudness (0-1 at the ears) is added to
        // the meter, and how much more a noisemaker bolt counts.
        [SerializeField] private float hearingGain = 0.7f;
        [SerializeField] private float distractionGain = 1.5f;

        [Header("Thresholds (the meter, 0-1)")]

        // Becomes Suspicious at the first; a suspicion that reached the
        // second is worth going to look at.
        [SerializeField] private float suspiciousThreshold = 0.3f;
        [SerializeField] private float investigateThreshold = 0.6f;

        [Header("Looking (how often)")]

        // Seconds between looks, and between looks while the player is
        // further away than it could see them anyway.
        [SerializeField] private float lookInterval = 0.15f;
        [SerializeField] private float farLookInterval = 0.5f;

        [Header("Suspicious")]

        // Seconds it stands looking before deciding.
        [SerializeField] private float suspiciousTime = 3f;

        [Header("Search")]

        // Seconds it looks about at each place, how many places round
        // the first it tries and how far from it, and the longest a
        // search lasts.
        [SerializeField] private float searchLookTime = 2.5f;
        [SerializeField] private int searchPointCount = 3;
        [SerializeField] private float searchRadius = 4f;
        [SerializeField] private float searchTimeout = 30f;

        // How much longer every guard searches once a body has been
        // found.
        [SerializeField] private float alertedSearchScale = 2f;

        [Header("Chase")]

        // Seconds without seeing the player before a chase becomes a
        // search, and how close is caught, in metres.
        [SerializeField] private float chaseLostTime = 4f;
        [SerializeField] private float catchRange = 1.1f;

        [Header("Knocking Out")]

        // How squarely behind the guard the attacker must be: the least
        // dot product of the guard's facing and the way from attacker to
        // guard (1 = dead behind, 0 = level with the shoulders).
        [SerializeField] private float behindDot = 0.2f;

        // A body lying in at least this much light (0-1) can be seen by
        // another guard.
        [SerializeField] private float bodyLightThreshold = 0.15f;

        // Seconds a guard spends over a knocked-out guard to wake it.
        [SerializeField] private float wakeTime = 3f;

        [Header("Sounds")]

        // Its footsteps, by the floor it's on. Optional (empty = silent).
        [SerializeField] private SurfaceSounds surfaceSounds;

        // Metres between steps walking and running, and what counts as
        // floor for finding the surface.
        [SerializeField] private float walkStride = 1.4f;
        [SerializeField] private float runStride = 1.8f;
        [SerializeField] private LayerMask groundLayers;

        // What it says as it changes state - how the player reads what a
        // guard is thinking. All optional.
        [SerializeField] private SoundCue suspiciousBark;
        [SerializeField] private SoundCue searchBark;
        [SerializeField] private SoundCue gaveUpBark;
        [SerializeField] private SoundCue spottedBark;
        [SerializeField] private SoundCue foundBodyBark;

        // How close counts as having arrived, on top of the agent's own
        // stopping distance, in metres.
        private const float ArriveMargin = 0.15f;

        // Setting off from standing, it turns on the spot until it faces
        // the first leg of its path to within this many degrees, then
        // walks; the agent turns it the rest of the way as it goes.
        private const float WalkOffAngle = 15f;

        // Slower than this, in metres a second, counts as standing.
        private const float StandingSpeed = 0.1f;

        // How fast it turns while looking about during a search, in
        // degrees a second.
        private const float LookAboutSpeed = 70f;

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
        // its head collider (for the blackjack) and by its body collider
        // (for bolts). They add and remove themselves, so nothing
        // searches the scene.
        private static readonly List<Guard> _all = new();
        private static readonly Dictionary<Transform, Guard> _byRoot = new();
        private static readonly Dictionary<Collider, Guard> _byHead = new();
        private static readonly Dictionary<Collider, Guard> _byBody = new();

        // True once any guard has found a body: every search is longer.
        private static bool _isAlerted;

        private NavMeshAgent _agent;
        private MaterialPropertyBlock _tint;

        // True while it stands turning to face the way it is about to
        // walk, the agent held still (TickTurningToWalk).
        private bool _isTurningToWalk;

        // Where it stands if it has no route.
        private Vector3 _postPosition;
        private Quaternion _postRotation;

        // The cosine of half the view angle, worked out once: a point is
        // inside the cone when the dot product is at least this.
        private float _viewCos;

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
        private bool _isWaiting;

        // Suspicious: seconds left, and the highest the meter got.
        private float _suspiciousTimer;
        private float _peakMeter;

        // Search: where it began, how many places it has tried, seconds
        // left looking about at this one, and seconds left altogether.
        private Vector3 _searchOrigin;
        private int _searchPointsDone;
        private float _searchLookTimer;
        private float _searchTimer;
        private bool _isLookingAbout;

        // Chase: seconds since the player was last seen.
        private float _lostTimer;

        // Stunned: seconds until it comes round.
        private float _stunTimer;

        // A sleep dart: seconds until it knocks the guard out. 0 = none
        // in it.
        private float _sleepTimer;

        // Footsteps: where it was last frame, the distance walked since
        // the last step, the last surface found, and its heel-and-toe
        // pattern.
        private Vector3 _lastPosition;
        private float _stepDistance;
        private SurfaceType _lastSurface;
        private HeelAndToeCadence _cadence;

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

        /// How aware of the player it is: 0 = not at all, 1 = chasing.
        public float Detection { get; private set; }

        public bool IsKnockedOut => State == GuardState.KnockedOut;

        // Standing and looking about: turned towards what it noticed
        // (Suspicious), or at a place in a search. Read by
        // GuardAnimation for the look-around clip.
        public bool IsLookingAbout => State == GuardState.Suspicious || (State == GuardState.Search && _isLookingAbout);

        // Turning on the spot before setting off, and how far it has
        // turned so far, in degrees. Read by GuardAnimation for the turn
        // clip.
        public bool IsTurningToWalk => _isTurningToWalk;
        public float TurnedDegrees { get; private set; }

        /// Where its ears are (INoiseListener).
        public Vector3 EarPosition => eye.position;

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
        /// rays find the body. A dictionary lookup, no GetComponent.
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

            _viewCos = Mathf.Cos(viewAngle * 0.5f * Mathf.Deg2Rad);
            _postPosition = transform.position;
            _postRotation = transform.rotation;
            _lastPosition = transform.position;

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

            TickFootsteps();
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
            Vector3 head = player.HeadPosition;
            float distance = Vector3.Distance(eyePosition, head);

            // Too far to be seen whatever happens: no rays, and the next
            // look can wait longer.
            if (distance > viewRange + 2f) {
                _lookTimer = farLookInterval;
                Drain(elapsed);
                return;
            }

            // Two points on the player: the head and the middle of the
            // body. Seeing one of the two is seeing half the player.
            float seen = 0f;

            if (CanSeePoint(eyePosition, head)) {
                seen += 0.5f;
            }

            if (CanSeePoint(eyePosition, player.BodyPosition)) {
                seen += 0.5f;
            }

            // How well lit they are - or, close up, at least as if lit
            // a fair amount: nobody is invisible at arm's length.
            float visibility = player.Visibility;

            if (distance <= closeRange) {
                visibility = Mathf.Max(visibility, closeVisibility);
            }

            if (seen > 0f && visibility >= minVisibility) {
                _isPlayerSeen = true;
                _stimulus = player.FeetPosition;

                // Slower the further away: full speed close up, down to
                // a fraction of it at the edge of the view range.
                float far = Mathf.InverseLerp(closeRange, viewRange, distance);
                float rate = seen * visibility * Mathf.Lerp(1f, farDetectionScale, far) / Mathf.Max(detectionTime, 0.01f);

                Detection = Mathf.Min(Detection + rate * elapsed, 1f);
            } else {
                Drain(elapsed);
            }

            if (State != GuardState.Chase && HasBumped(player)) {
                // Walked into: it knows something is there and where,
                // and turns to look. Seeing them is then up to its eyes.
                _stimulus = player.FeetPosition;
                Detection = Mathf.Max(Detection, investigateThreshold);

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
            } else if (State == GuardState.Patrol && Detection >= suspiciousThreshold) {
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
        /// The meter draining with nothing to notice. Not while chasing:
        /// a chase ends by its own timer.
        /// </summary>
        private void Drain(float elapsed)
        {
            if (State != GuardState.Chase) {
                Detection = Mathf.Max(Detection - elapsed / Mathf.Max(decayTime, 0.01f), 0f);
            }
        }

        /// <summary>
        /// Whether a point is in view: within range, inside the cone,
        /// with nothing solid in the way - one physics ray.
        /// </summary>
        private bool CanSeePoint(Vector3 eyePosition, Vector3 point)
        {
            Vector3 toPoint = point - eyePosition;
            float distance = toPoint.magnitude;

            if (distance > viewRange) {
                return false;
            }

            // The dot product of two unit vectors is the cosine of the
            // angle between them: at least the cosine of half the view
            // angle means inside the cone.
            if (distance > 0.001f && Vector3.Dot(toPoint / distance, eye.forward) < _viewCos) {
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

                Vector3 body = other.transform.position + Vector3.up * 0.3f;

                if (!CanSeePoint(eyePosition, body) || SceneLight.LevelAt(body) < bodyLightThreshold) {
                    continue;
                }

                other._isBodyFound = true;
                _isAlerted = true;
                _stimulus = other.transform.position;
                Detection = Mathf.Max(Detection, investigateThreshold);

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
        /// A noise has reached this guard (NoiseSystem): it adds to the
        /// meter by how loud it was here, and is somewhere to look.
        /// Another guard's noises are ignored, and so is everything
        /// during a chase or while stunned.
        /// </summary>
        public void OnNoiseHeard(in Noise noise, float loudness, Vector3 heardFrom)
        {
            if (State == GuardState.Chase || State == GuardState.Stunned || State == GuardState.KnockedOut) {
                return;
            }

            if (noise.Source != null && _byRoot.ContainsKey(noise.Source)) {
                return;
            }

            float gain = noise.Type == NoiseType.Distraction ? distractionGain : hearingGain;

            Detection = Mathf.Min(Detection + loudness * gain, 1f);
            _stimulus = heardFrom;

            switch (State) {
                case GuardState.Patrol:
                    if (Detection >= suspiciousThreshold) {
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
            _agent.speed = patrolSpeed;
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
            // and the meter is emptied - left part full, the very next
            // look would make it suspicious again, and it would stand
            // there for as long as the meter took to drain.
            if (_peakMeter >= investigateThreshold) {
                EnterSearch(true);
            } else {
                Detection = 0f;
                EnterPatrol();
            }
        }

        // ---- Search ------------------------------------------------------

        private void EnterSearch(bool withBark)
        {
            SetState(GuardState.Search);
            _agent.speed = patrolSpeed;
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

            // At a place: looks about for a while, turning slowly.
            if (!_isLookingAbout) {
                _isLookingAbout = true;
                _searchLookTimer = searchLookTime;
            }

            transform.Rotate(0f, LookAboutSpeed * deltaTime, 0f);
            _searchLookTimer -= deltaTime;

            if (_searchLookTimer > 0f) {
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
        /// Standing over the knocked-out guard it came for: after the
        /// wake time, that guard gets up. Returns whether it is still
        /// busy with it.
        /// </summary>
        private bool TickWaking(float deltaTime)
        {
            Guard body = _wakeTarget;
            Vector3 toBody = body.transform.position - transform.position;
            toBody.y = 0f;

            // As near as it can get and still out of reach (the body is
            // somewhere it can't walk to): it gives the body up. The
            // body stays marked as found, or every guard that saw it
            // would set off for it again at each look.
            if (toBody.sqrMagnitude > WakeReach * WakeReach) {
                _wakeTarget = null;
                return false;
            }

            FaceTowards(body.transform.position, deltaTime);
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
            Detection = 0f;
            Bark(gaveUpBark);
            EnterPatrol();
        }

        // ---- Chase -------------------------------------------------------

        private void EnterChase()
        {
            SetState(GuardState.Chase);
            Detection = 1f;
            _agent.speed = chaseSpeed;
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
                Detection = investigateThreshold;
                EnterSearch(true);
            }
        }

        // ---- Bolts -------------------------------------------------------

        /// <summary>
        /// Whether a line passes through this guard's head: one ray
        /// tested against the head's collider alone. The head is inside
        /// the body's capsule, so a bolt's own ray always finds the body
        /// first; this asks whether it was on its way through the head.
        /// </summary>
        public bool IsHeadOnLine(Vector3 from, Vector3 direction, float distance)
        {
            return headCollider != null && headCollider.Raycast(new Ray(from, direction), out _, distance);
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
            if (State == GuardState.Chase || State == GuardState.Stunned || State == GuardState.KnockedOut) {
                return;
            }

            _stimulus = shotFrom;
            Detection = Mathf.Max(Detection, investigateThreshold);

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
            Detection = investigateThreshold;
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

            if (State != GuardState.Chase) {
                _stimulus = attackerPosition;
                EnterChase();
            }

            return false;
        }

        /// <summary>
        /// Down: it stops, its colliders go (so it can be stepped over),
        /// it lies down, and the component switches off - no more
        /// looking, hearing or Update() until another guard wakes it.
        /// </summary>
        private void KnockOut()
        {
            SetState(GuardState.KnockedOut);
            Detection = 0f;
            _sleepTimer = 0f;
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

            enabled = false;
        }

        /// <summary>
        /// Woken by another guard: everything KnockOut() switched off
        /// comes back, it stands up, and it searches round where it lay.
        /// </summary>
        private void WakeUp()
        {
            _isBodyFound = false;
            enabled = true;
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
            // spent looking, or walked.
            _lastLookTime = Time.time;
            _lookTimer = lookInterval;
            _lastPosition = transform.position;
            _isPlayerSeen = false;

            _stimulus = transform.position;
            Detection = investigateThreshold;
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
                TurnedDegrees = 0f;
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

            Quaternion before = transform.rotation;
            TurnTowards(Quaternion.LookRotation(toTarget), deltaTime);
            TurnedDegrees += Quaternion.Angle(before, transform.rotation);
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

        private void TurnTowards(Quaternion rotation, float deltaTime)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, turnSpeed * deltaTime);
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
        /// A footstep each stride walked, on whatever floor is under it
        /// (one ray down, only when a step sounds). Sound only: guards
        /// ignore each other's steps anyway.
        /// </summary>
        private void TickFootsteps()
        {
            Vector3 position = transform.position;
            Vector3 moved = position - _lastPosition;
            _lastPosition = position;
            moved.y = 0f;
            _stepDistance += moved.magnitude;

            float stride = State == GuardState.Chase ? runStride : walkStride;

            if (_stepDistance < stride) {
                return;
            }

            _stepDistance -= stride;

            if (surfaceSounds == null) {
                return;
            }

            if (SurfaceTag.TryFindBelow(position + Vector3.up * 0.3f, 1f, groundLayers, out SurfaceType surface)) {
                _lastSurface = surface;
            }

            // A little above the floor, so it's inside the sound room
            // being walked through.
            surfaceSounds.PlayStep(_lastSurface, position + Vector3.up * 0.1f, transform, 0f, 1f, ref _cadence);
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
        /// Draws the two edges of the view cone in the state's colour, a
        /// bar above the head filled by the detection meter and, in the
        /// detailed view, the view range and a line to what it last saw
        /// or heard.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (eye == null) {
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
            Vector3 forward = eye.forward;
            float half = viewAngle * 0.5f;
            Vector3 left = Quaternion.AngleAxis(-half, Vector3.up) * forward * viewRange;
            Vector3 right = Quaternion.AngleAxis(half, Vector3.up) * forward * viewRange;

            lines.Line(eyePosition, eyePosition + left);
            lines.Line(eyePosition, eyePosition + right);
            lines.Line(eyePosition + left, eyePosition + forward * viewRange);
            lines.Line(eyePosition + right, eyePosition + forward * viewRange);

            // The meter: a line above the head, as long as it is full.
            Vector3 barStart = eyePosition + Vector3.up * 0.35f - eye.right * 0.25f;
            lines.Line(barStart, barStart + eye.right * (0.5f * Detection));

            if (!detailed) {
                return;
            }

            lines.Circle(transform.position, Vector3.right * closeRange, Vector3.forward * closeRange);

            if (Application.isPlaying && State != GuardState.Patrol) {
                lines.Line(eyePosition, _stimulus);
                lines.WireSphere(_stimulus, 0.15f);
            }
        }
    }
}
