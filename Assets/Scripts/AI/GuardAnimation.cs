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
    /// - Speed: the gait, driving a blend tree of idle, walk and run.
    /// - Searching: 0-1, in the walk slot, from the plain walk to the
    ///   search walk (shoulders up, arms out); 1 while in Search.
    ///   Up to walking pace it is the agent's ground speed (idle blended
    ///   into the walk); faster, it is the walk's or the run's own speed,
    ///   and the Animator's playback rate is scaled so the stride covers
    ///   the ground the agent does. A guard waiting at a patrol point
    ///   reads 0 and idles.
    /// - IsLookingAbout: standing looking about at a place in a Search,
    ///   playing the wide look-around clip from its start each time.
    ///
    /// The rest are actions, cross-faded to by name (TickActions), each
    /// state handing back to Locomotion itself or being faded out of
    /// here when it no longer applies:
    /// - Fidgets: idle on patrol for fidgetInterval, one of the bored,
    ///   look-around and neck-stretching clips, once.
    /// - Stunned: the hit clip for the side the bolt came from (again on
    ///   each new stun), which hands on to the dizzy loop until it ends.
    /// - Giving up a search: the shrug, held while the Guard stands
    ///   shrugging.
    /// - Diving at the player (the catch), and collapsing from a sleep
    ///   dart (the being-strangled clip), each held to the end.
    /// Getting up from a knock-out is GuardRagdoll's (it plays Get Up).
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

        // How long the walk takes to blend into the search walk (shoulders
        // up, arms held out) on starting a search, and back after.
        [Tooltip("Seconds the walk takes to blend into the search walk (Guard_SearchWalk: shoulders raised, arms held out, as the wide look-around) when a search starts, and back to the plain walk when it ends. e.g. 0.3 = the arms come up over a step or so. Higher = slower change; 0 = snaps.")]
        [SerializeField] private float searchWalkBlendTime = 0.3f;

        // Walking a search, the spine bends forward this far in all
        // (shared between the spine, chest and upper chest), turned onto
        // the bones after the Animator; the head is kept level by
        // GuardHeadAim's Glance rule.
        [Tooltip("Degrees the guard stoops forward while walking a search, shared between the spine, chest and upper chest bones and turned on after the Animator (the hips and legs stay as the clip has them). Fades in with the search walk and with walking speed, so standing at a search place he straightens. e.g. 12 = a slight hunch. Higher = bends further over; 0 = upright. The head is kept level by GuardHeadAim (Glance rule).")]
        [SerializeField] private float searchStoopAngle = 12f;

        // Standing idle on patrol this long, it plays one of its fidgets
        // once, then idles on.
        [Tooltip("Seconds a guard stands idle on patrol (at its post, or waiting at a route point) before it plays one of its fidgets once - bored, looking around or stretching its neck, picked at random - then idles on and starts counting again. e.g. 20 = a fidget every twenty seconds of standing. Lower = more restless; walking off resets the count.")]
        [SerializeField] private float fidgetInterval = 20f;

        // How long a cross-fade into or out of an action takes.
        [Tooltip("Seconds the cross-fade into or out of an action clip takes (a fidget, a hit, the collapse, back to walking), so the body never snaps. e.g. 0.25 = a quarter-second blend. Higher = softer but mushier changes.")]
        [SerializeField] private float actionFade = 0.25f;

        // Faster fades for the sudden ones: a bolt to the head, the dive.
        [Tooltip("Seconds the cross-fade into a sudden action takes: a stun bolt's hit and the dive at the player. e.g. 0.1 = near instant, as a blow should be. Higher = softer but slower to react.")]
        [SerializeField] private float suddenFade = 0.1f;

        // How close to walking pace a damped Speed must come back down
        // (from the run) before it is set undamped again.
        private const float GaitTolerance = 0.01f;

        private static readonly int _locomotionState = Animator.StringToHash("Locomotion");
        private static readonly int _hitFrontState = Animator.StringToHash("Hit On Front");
        private static readonly int _hitBackState = Animator.StringToHash("Hit On Back Of Head");
        private static readonly int _diveState = Animator.StringToHash("Dive");
        private static readonly int _collapseState = Animator.StringToHash("Being Strangled");
        private static readonly int _shrugState = Animator.StringToHash("Shrug");
        private static readonly int[] _fidgetStates = {
            Animator.StringToHash("Fidget Bored"),
            Animator.StringToHash("Fidget Look Around"),
            Animator.StringToHash("Fidget Neck Stretching")
        };
        private static readonly int _speedParam = Animator.StringToHash("Speed");
        private static readonly int _searchingParam = Animator.StringToHash("Searching");
        private static readonly int _lookingAboutParam = Animator.StringToHash("IsLookingAbout");

        // What the Animator was told to play by name, if anything.
        private enum Action
        {
            None,
            Fidget,
            Stunned,
            Shrug,
            Dive,
            Collapse
        }

        private Guard _guard;
        private NavMeshAgent _agent;

        // What the Animator was last told, so a bool is only set on a
        // change.
        private bool _isLookingAbout;

        // The action playing, the fidget's state if it is one, the stun
        // whose hit clip was played, and seconds stood idle since the
        // last fidget.
        private Action _action;
        private int _fidgetState;
        private int _stunCount;
        private float _idleTime;

        /// <summary>
        /// Playing a stun, the dive or the collapse: the body leaves its
        /// standing stance, so GuardFootLock lets the feet be and
        /// GuardHeadAim leaves the head to the clip.
        /// </summary>
        public bool IsPlayingAction => _action == Action.Stunned || _action == Action.Dive || _action == Action.Collapse;

        /// <summary>
        /// Playing an idle fidget: GuardHeadAim leaves the head to the
        /// clip (looking round, stretching the neck).
        /// </summary>
        public bool IsFidgeting => _action == Action.Fidget;

        // The model's own transform (the Animator's), tilted to lean,
        // and its rotation unleaned. Null if the Animator is on the root,
        // which the agent turns: then there is no lean.
        private Transform _model;
        private Quaternion _modelRotation;

        // Last frame's ground speed, for the deceleration, and the
        // current lean in degrees (positive = back).
        private float _lastSpeed;
        private float _leanAngle;

        // The bones the search stoop bends (UpperChest may be missing from
        // a rig), and the model's drawn renderer: a culled model's bones
        // aren't rewritten, so turning them again would add up.
        private Transform[] _stoopBones;
        private Renderer _bodyRenderer;

        // The upper arms and the head, turned back by the whole stoop so
        // the arms still hang (and swing) and the gaze points as the clip
        // has them.
        private Transform[] _upperArms;
        private Transform _head;

        // This frame's ground speed (Update), for the stoop (LateUpdate).
        private float _groundSpeed;

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

            if (animator.isHuman) {
                var bones = new System.Collections.Generic.List<Transform>(3);

                foreach (HumanBodyBones bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }) {
                    Transform found = animator.GetBoneTransform(bone);

                    if (found != null) {
                        bones.Add(found);
                    }
                }

                _stoopBones = bones.ToArray();
                _upperArms = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), animator.GetBoneTransform(HumanBodyBones.RightUpperArm) };
                _head = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            _bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        }

        /// <summary>
        /// After the Animator has posed the bones (and before GuardHeadAim,
        /// order 50): walking a search, the spine, chest and upper chest
        /// are each turned forward their share of searchStoopAngle about
        /// the body's right, by how far the walk is the search walk
        /// (Searching) and how near walking pace he is; then each upper arm
        /// and the head are turned back by the whole bend, so the arms hang
        /// and the gaze points as the clip draws them (GuardHeadAim then
        /// levels the gaze anyway).
        /// Turned on the bones, not keyed into the clip: a Humanoid clip
        /// keeps the body's overall tilt, so spine muscles bent in a clip
        /// tip the hips back to match (a clip's lean is changed by
        /// rebuilding it from posed bones, as Guard_Walk's was). Only while
        /// drawn: a culled model's bones aren't rewritten each frame, so
        /// the turn would add up.
        /// </summary>
        private void LateUpdate()
        {
            if (_stoopBones == null || _stoopBones.Length == 0 || !animator.enabled
                || _bodyRenderer == null || !_bodyRenderer.isVisible) {
                return;
            }

            float stoop = searchStoopAngle * animator.GetFloat(_searchingParam) * Mathf.Clamp01(_groundSpeed / walkSpeed);

            if (stoop < 0.01f) {
                return;
            }

            // Positive about the right tips the top forward.
            Vector3 right = transform.right;
            Quaternion bend = Quaternion.AngleAxis(stoop / _stoopBones.Length, right);

            foreach (Transform bone in _stoopBones) {
                bone.rotation = bend * bone.rotation;
            }

            // The arms hang below the spine's pivot, so the bend swings
            // them back; turned back by the whole bend, they hang as drawn
            // and only move forward with the shoulders. The head likewise
            // keeps the clip's gaze.
            Quaternion unbend = Quaternion.AngleAxis(-stoop, right);

            foreach (Transform arm in _upperArms) {
                if (arm != null) {
                    arm.rotation = unbend * arm.rotation;
                }
            }

            if (_head != null) {
                _head.rotation = unbend * _head.rotation;
            }
        }

        private void Update()
        {
            // Stopped while the guard lies as a ragdoll. Re-enabled, the
            // Animator starts afresh from its defaults, so what it was
            // last told is forgotten too.
            if (!animator.enabled) {
                _isLookingAbout = false;
                _action = Action.None;
                _idleTime = 0f;
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
            _groundSpeed = speed;
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

            // Searching, the walk is the search walk; damped, so the arms
            // come up and down over searchWalkBlendTime. The two walks
            // share their timing, so the footfalls below hold for both.
            float searching = _guard.State == GuardState.Search ? 1f : 0f;
            animator.SetFloat(_searchingParam, searching, searchWalkBlendTime, Time.deltaTime);

            TickLean(speed);
            TickFootfalls(speed);

            Apply(ref _isLookingAbout, _guard.IsLookingAbout, _lookingAboutParam);
            TickActions();
        }

        /// <summary>
        /// Starts, restarts or ends the action clips from what the Guard
        /// is doing, most pressing first: the dive, the collapse, a stun
        /// (a new stun plays its hit again), the shrug, then the idle
        /// fidgets. An
        /// action that no longer applies fades back to Locomotion. Nothing
        /// while knocked out: the ragdoll owns the Animator (Get Up
        /// included).
        /// </summary>
        private void TickActions()
        {
            if (_guard.IsKnockedOut) {
                return;
            }

            if (_guard.IsDiving) {
                if (_action != Action.Dive) {
                    Play(Action.Dive, _diveState, suddenFade);
                }

                return;
            }

            if (_guard.IsCollapsing) {
                if (_action != Action.Collapse) {
                    Play(Action.Collapse, _collapseState, actionFade);
                }

                return;
            }

            if (_guard.State == GuardState.Stunned) {
                if (_action != Action.Stunned || _stunCount != _guard.StunCount) {
                    _stunCount = _guard.StunCount;
                    Play(Action.Stunned, _guard.IsStunFromBehind ? _hitBackState : _hitFrontState, suddenFade);
                }

                return;
            }

            // Shrugging off a search: in place, so the feet stay locked.
            if (_guard.IsShrugging) {
                if (_action != Action.Shrug) {
                    Play(Action.Shrug, _shrugState, actionFade);
                }

                return;
            }

            if (_action != Action.None && _action != Action.Fidget) {
                Play(Action.None, _locomotionState, actionFade);
            }

            TickFidgets();
        }

        /// <summary>
        /// Idle on patrol: counts the time stood, and every fidgetInterval
        /// plays one of the fidget clips, picked at random; its state hands
        /// back to Locomotion when it ends. No longer idle (walking off,
        /// any other state) cuts a fidget short and starts the count again.
        /// </summary>
        private void TickFidgets()
        {
            if (!_guard.IsIdle) {
                _idleTime = 0f;

                if (_action == Action.Fidget) {
                    Play(Action.None, _locomotionState, actionFade);
                }

                return;
            }

            if (_action == Action.Fidget) {
                // Over once the Animator is neither in the fidget nor
                // fading into it (it has handed back to Locomotion).
                if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash != _fidgetState
                    && animator.GetNextAnimatorStateInfo(0).shortNameHash != _fidgetState) {
                    _action = Action.None;
                }

                return;
            }

            _idleTime += Time.deltaTime;

            if (_idleTime < fidgetInterval) {
                return;
            }

            _idleTime = 0f;
            _fidgetState = _fidgetStates[Random.Range(0, _fidgetStates.Length)];
            Play(Action.Fidget, _fidgetState, actionFade);
        }

        /// <summary>
        /// Cross-fades to a state by name, from the state's start, and
        /// notes the action it stands for.
        /// </summary>
        private void Play(Action action, int state, float fade)
        {
            _action = action;
            animator.CrossFadeInFixedTime(state, fade, 0, 0f);
        }

        /// <summary>
        /// Leans the model back while it slows: the target lean is the
        /// deceleration's share of fullLeanDeceleration, scaled by how
        /// near running pace it still is, eased towards over
        /// leanSmoothing. Speeding up, steady, or stopped dead by an
        /// action, it eases back upright.
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

            // Stopped dead by a stun, the dive or the collapse: the clip
            // has the body, so no lean from the sudden stop.
            bool isStoppedDead = _guard.IsDiving || _guard.IsCollapsing || _guard.State == GuardState.Stunned;

            if (deceleration > 0f && !isStoppedDead) {
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
