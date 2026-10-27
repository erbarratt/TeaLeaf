using UnityEngine;
using UnityEngine.AI;

namespace AI
{
    /// <summary>
    /// Drives the guard model's Animator from what the Guard is doing.
    /// The Guard decides and the NavMeshAgent moves it; this only reads
    /// them and tells the Animator, so the model matches without the
    /// guard's logic knowing anything about animation.
    ///
    /// Three parameters in Guard.controller:
    /// - Speed: the gait, driving a blend tree of idle (the look-around clip), walk and run.
    ///   Up to walking pace it is the agent's ground speed (idle blended
    ///   into the walk); faster, it is the walk's or the run's own speed,
    ///   and the Animator's playback rate is scaled so the stride covers
    ///   the ground the agent does. A guard waiting at a patrol point,
    ///   or stunned reads 0 and idles.
    /// - IsLookingAbout: standing and looking (Suspicious, or at a
    ///   place in a Search), playing the look-around clip.
    /// - IsStunned: the stunned clip.
    ///
    /// Knocked out, the guard is a ragdoll (GuardRagdoll), which stops
    /// the Animator; this does nothing while it is stopped.
    ///
    /// Slowing down, the model leans back from its feet (a tilt of the
    /// whole model about its root, between the feet), more the harder it
    /// slows and the faster it still goes: a run braking to a stop leans
    /// back and comes upright as it stops; a walk stopping barely leans.
    ///
    /// The clips carry the walk and run's forward travel as root
    /// motion, which the Animator discards (applyRootMotion is off), so
    /// they play in place under the agent. Speed is set every frame -
    /// it moves nearly every frame anyway, and the damping needs the
    /// calls - but the bools only on a change.
    ///
    /// Walking and running, it also says when a foot comes down in the
    /// clip, and the Guard plays the footstep (TickFootfalls).
    ///
    /// Its own Update(), like the Guard's.
    /// </summary>
    [RequireComponent(typeof(Guard))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class GuardAnimation : MonoBehaviour
    {
        // The model's Animator, a child of the guard's root. Found in
        // the children if not assigned.
        [Tooltip("The model's Animator, a child of the guard's root, e.g. the Animator on the prefab's Model object. Left empty, it is found in the children.")]
        [SerializeField] private Animator animator;

        // How long Speed takes to settle when the gait changes between
        // the walk and the run, so that blend doesn't snap. Up to walking
        // pace Speed follows the agent undamped.
        [Tooltip("Seconds the Animator's Speed parameter takes to settle when the gait changes between walk and run, so the blend doesn't snap. e.g. 0.1 = a quick but smooth change. Higher = slower blend (feet may slide); 0 = snaps. Up to walking pace it follows the agent undamped.")]
        [SerializeField] private float speedDamping = 0.1f;

        // How fast a planted foot travels back along the ground in the
        // walk and run clips at normal playback (measured from the clips'
        // feet, not their hips, which surge). Must match the blend tree's
        // walk and run thresholds in Guard.controller.
        [Tooltip("How fast a planted foot travels along the ground in the walk clip at normal playback, in metres a second; faster ground speeds play the clip faster to match. e.g. 0.97: a guard walking at 1.07 plays the walk at 1.1x. Must match the walk threshold in Guard.controller's blend tree.")]
        [SerializeField] private float walkSpeed = 0.97f;
        [Tooltip("How fast a planted foot travels along the ground in the run clip at normal playback, in metres a second; faster ground speeds play the clip faster to match. e.g. 3.1: a guard chasing at 3.6 plays the run at 1.16x. Must match the run threshold in Guard.controller's blend tree.")]
        [SerializeField] private float runSpeed = 3.1f;

        // Leaning back while slowing: the most it leans, in degrees; the
        // deceleration, in metres a second per second, that gives the
        // whole lean at running pace; and how long the lean takes to
        // follow a change, in seconds, so it rises and settles smoothly.
        [Tooltip("The most the model leans back while slowing down, in degrees. e.g. 8 = a visible lean braking from a run. Higher = more lean; 0 = never leans.")]
        [SerializeField] private float maxLeanAngle = 8f;
        [Tooltip("The deceleration, in metres a second per second, that gives the whole Max Lean Angle at running pace; slower speeds and gentler braking lean less. e.g. 3 = the guard's usual braking from a run leans fully. Lower = leans fully on softer braking.")]
        [SerializeField] private float fullLeanDeceleration = 3f;
        [Tooltip("Seconds the lean takes to follow a change, so it rises and settles smoothly. e.g. 0.15 = eases in over a step or so. Higher = softer, laggier lean; 0 = jumps straight to it.")]
        [SerializeField] private float leanSmoothing = 0.15f;

        // Footfalls: where in the walk and run clips (0-1 through the
        // clip) each heel comes down - the moment the ankle drops below
        // 0.15m above the root, measured by sampling the clips. Both
        // clips are two strides long, so four steps each. Remeasure if the
        // clips change.
        [Tooltip("One entry per heel strike: where in Guard_Walk (0-1 through the clip) that heel comes down and a footstep sounds. e.g. 0.3 = a step 30% of the way through; the clip is two strides, so four entries. Measured from the clip: remeasure if it changes.")]
        [SerializeField] private float[] walkFootfalls = { 0.045f, 0.3f, 0.555f, 0.8f };
        [Tooltip("One entry per heel strike: where in Guard_Run (0-1 through the clip) that heel comes down and a footstep sounds. e.g. 0.445 = a step just before halfway; the clip is two strides, so four entries. Measured from the clip: remeasure if it changes.")]
        [SerializeField] private float[] runFootfalls = { 0.175f, 0.445f, 0.685f, 0.95f };

        // How close to walking pace a damped Speed must come back down
        // (from the run) before it is set undamped again.
        private const float GaitTolerance = 0.01f;

        private static readonly int _locomotionState = Animator.StringToHash("Locomotion");
        private static readonly int _speedParam = Animator.StringToHash("Speed");
        private static readonly int _lookingAboutParam = Animator.StringToHash("IsLookingAbout");
        private static readonly int _stunnedParam = Animator.StringToHash("IsStunned");

        private Guard _guard;
        private NavMeshAgent _agent;

        // What the Animator was last told, so a bool is only set on a
        // change.
        private bool _isLookingAbout;
        private bool _isStunned;

        // The model's own transform (the Animator's), tilted to lean,
        // and its rotation unleaned. Null if the Animator is on the root,
        // which the agent turns: then there is no lean.
        private Transform _model;
        private Quaternion _modelRotation;

        // Last frame's ground speed, for the deceleration, and the
        // current lean in degrees (positive = back).
        private float _lastSpeed;
        private float _leanAngle;

        // Where the Locomotion blend tree was last frame (0-1 through its
        // cycle), for spotting a footfall passed since; -1 = not playing
        // it last frame.
        private float _lastPhase = -1f;

        private void Awake()
        {
            _guard = GetComponent<Guard>();
            _agent = GetComponent<NavMeshAgent>();

            if (animator == null) {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator == null) {
                Debug.LogError($"GuardAnimation '{name}': no Animator on the guard or under it.", this);
                enabled = false;
                return;
            }

            // The agent moves the guard; the clips' travel is thrown away.
            animator.applyRootMotion = false;

            if (animator.transform != transform) {
                _model = animator.transform;
                _modelRotation = _model.localRotation;
            }
        }

        private void Update()
        {
            // Stopped while the guard lies as a ragdoll. Re-enabled, the
            // Animator starts afresh from its defaults, so what it was
            // last told is forgotten too.
            if (!animator.enabled) {
                _isLookingAbout = false;
                _isStunned = false;
                _lastPhase = -1f;
                return;
            }

            // The agent's real speed along the ground: zero when it is
            // off or off the NavMesh.
            float speed = 0f;

            if (_agent.enabled && _agent.isOnNavMesh) {
                Vector3 velocity = _agent.velocity;
                velocity.y = 0f;
                speed = velocity.magnitude;
            }

            // Up to walking pace the blend tree blends idle into the walk,
            // which already moves the feet at about the agent's speed.
            // Faster than that, the guard plays one gait, never a blend of
            // walk and run (their feet are out of step, so a blend
            // shuffles), and the playback rate is scaled so the stride
            // covers exactly the ground the agent does.
            float gaitSpeed = speed;
            float playbackRate = 1f;

            if (speed > walkSpeed) {
                gaitSpeed = speed < (walkSpeed + runSpeed) * 0.5f ? walkSpeed : runSpeed;
                playbackRate = speed / gaitSpeed;
            }

            // Up to walking pace Speed is set as it is, undamped: the
            // idle-walk blend only keeps the feet planted while it matches
            // the ground speed exactly, and damping would make it lag behind
            // an agent that reaches walking pace in about a tenth of a
            // second, so the body would glide off before the legs got
            // going. Damping is kept only for the jump between the walk and
            // the run (and back down from the run).
            if (gaitSpeed <= walkSpeed && animator.GetFloat(_speedParam) <= walkSpeed + GaitTolerance) {
                animator.SetFloat(_speedParam, gaitSpeed);
            } else {
                animator.SetFloat(_speedParam, gaitSpeed, speedDamping, Time.deltaTime);
            }

            animator.speed = playbackRate;

            TickLean(speed);
            TickFootfalls(speed);

            GuardState state = _guard.State;
            Apply(ref _isLookingAbout, _guard.IsLookingAbout, _lookingAboutParam);
            Apply(ref _isStunned, state == GuardState.Stunned, _stunnedParam);
        }

        /// <summary>
        /// Leans the model back while it slows: the target lean is the
        /// deceleration's share of fullLeanDeceleration, scaled by how
        /// near running pace it still is, eased towards over
        /// leanSmoothing. Speeding up or steady, it eases back upright.
        /// The model's root sits between the feet, so the tilt pivots
        /// there and GuardFootLock (after this, in LateUpdate) keeps the
        /// planted feet in place.
        /// </summary>
        private void TickLean(float speed)
        {
            float deltaTime = Time.deltaTime;

            if (_model == null || deltaTime <= 0f) {
                return;
            }

            float deceleration = (_lastSpeed - speed) / deltaTime;
            _lastSpeed = speed;

            float target = 0f;

            if (deceleration > 0f) {
                target = maxLeanAngle * Mathf.Clamp01(deceleration / fullLeanDeceleration) * Mathf.Clamp01(speed / runSpeed);
            }

            // Frame-rate independent ease: closes the same share of the
            // gap per second whatever the frame rate.
            _leanAngle = Mathf.Lerp(_leanAngle, target, 1f - Mathf.Exp(-deltaTime / leanSmoothing));

            // Negative about the model's right axis tips its head back.
            _model.localRotation = _modelRotation * Quaternion.Euler(-_leanAngle, 0f, 0f);
        }

        /// <summary>
        /// A footstep each time the walk or run passes a heel coming
        /// down. The blend tree keeps its clips in step, so how far
        /// through its cycle the Locomotion state is (normalizedTime) is
        /// how far through the walk or run clip the feet are. The step
        /// timing therefore follows the playback rate set above from the
        /// agent's speed: change patrolSpeed or chaseSpeed and the steps
        /// keep falling where the feet land. Read in Update, the time is
        /// the Animator's last evaluation, one frame behind - unnoticeable
        /// at a footstep. The state machine keeps time while the model is
        /// culled (CullUpdateTransforms), so an unseen guard still steps.
        /// Standing (slower than Guard.StandingSpeed) it is idling, and
        /// GuardFootLock sounds its steps instead.
        /// </summary>
        private void TickFootfalls(float speed)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.shortNameHash != _locomotionState) {
                _lastPhase = -1f;
                return;
            }

            float phase = stateInfo.normalizedTime - Mathf.Floor(stateInfo.normalizedTime);
            float lastPhase = _lastPhase;
            _lastPhase = phase;

            if (lastPhase < 0f || speed < Guard.StandingSpeed) {
                return;
            }

            // The gait the blend tree is nearest to: at most one is ever
            // playing for more than the short damped change between them.
            bool isRunning = animator.GetFloat(_speedParam) > (walkSpeed + runSpeed) * 0.5f;
            float[] footfalls = isRunning ? runFootfalls : walkFootfalls;

            foreach (float footfall in footfalls) {
                // Passed since last frame, allowing for the cycle wrapping
                // round from 1 to 0 in between.
                bool passed = phase >= lastPhase
                    ? footfall > lastPhase && footfall <= phase
                    : footfall > lastPhase || footfall <= phase;

                if (passed) {
                    _guard.PlayFootstep(false);
                }
            }
        }

        /// <summary>
        /// Sets a bool parameter only when its value has changed.
        /// </summary>
        private void Apply(ref bool applied, bool value, int parameter)
        {
            if (applied == value) {
                return;
            }

            applied = value;
            animator.SetBool(parameter, value);
        }
    }
}
