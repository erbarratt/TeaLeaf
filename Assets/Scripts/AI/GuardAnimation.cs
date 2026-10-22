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
    /// Six parameters in Guard.controller:
    /// - Speed: the gait, driving a blend tree of idle, walk and run.
    ///   Up to walking pace it is the agent's ground speed (idle blended
    ///   into the walk); faster, it is the walk's or the run's own speed,
    ///   and the Animator's playback rate is scaled so the stride covers
    ///   the ground the agent does. A guard waiting at a patrol point,
    ///   stunned or knocked out reads 0 and idles.
    /// - IsLookingAbout: standing and looking (Suspicious, or at a
    ///   place in a Search), playing the look-around clip.
    /// - IsTurning, TurnProgress: turning on the spot before setting off.
    ///   The turn clip is posed by how far the guard has turned, not by
    ///   time (TurnProgress drives the state's normalized time), so its
    ///   step keeps pace with the body whatever the angle. The clip only
    ///   steps round to the right; a left turn plays the same step.
    /// - IsStunned, IsKnockedOut: the two states with a clip of their
    ///   own. The knocked-out clip is a fall that holds its last frame,
    ///   so the body lies there until the guard is woken, when the
    ///   Animator blends back to standing.
    ///
    /// The clips carry the walk and run's forward travel as root
    /// motion, which the Animator discards (applyRootMotion is off), so
    /// they play in place under the agent. Speed is set every frame -
    /// it moves nearly every frame anyway, and the damping needs the
    /// calls - but the bools only on a change.
    ///
    /// Its own Update(), like the Guard's: it keeps running after the
    /// Guard switches itself off when knocked out, so the fall still
    /// plays.
    /// </summary>
    [RequireComponent(typeof(Guard))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class GuardAnimation : MonoBehaviour
    {
        // The model's Animator, a child of the guard's root. Found in
        // the children if not assigned.
        [SerializeField] private Animator animator;

        // How long Speed takes to settle on a new value, so the blend
        // between idle, walk and run doesn't snap when the agent sets
        // off or stops.
        [SerializeField] private float speedDamping = 0.1f;

        // How fast a planted foot travels back along the ground in the
        // walk and run clips at normal playback (measured from the clips'
        // feet, not their hips, which surge). Must match the blend tree's
        // walk and run thresholds in Guard.controller.
        [SerializeField] private float walkSpeed = 0.97f;
        [SerializeField] private float runSpeed = 3.1f;

        // How far the body turns over the whole turn clip, in degrees
        // (Guard_Turn: the take's turn, re-timed so its time follows the
        // angle).
        [SerializeField] private float turnClipAngle = 192f;

        private static readonly int _speedParam = Animator.StringToHash("Speed");
        private static readonly int _lookingAboutParam = Animator.StringToHash("IsLookingAbout");
        private static readonly int _turningParam = Animator.StringToHash("IsTurning");
        private static readonly int _turnProgressParam = Animator.StringToHash("TurnProgress");
        private static readonly int _stunnedParam = Animator.StringToHash("IsStunned");
        private static readonly int _knockedOutParam = Animator.StringToHash("IsKnockedOut");

        private Guard _guard;
        private NavMeshAgent _agent;

        // What the Animator was last told, so a bool is only set on a
        // change.
        private bool _isLookingAbout;
        private bool _isTurning;
        private bool _isStunned;
        private bool _isKnockedOut;

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
        }

        private void Update()
        {
            // The agent's real speed along the ground: zero when it is
            // off (knocked out) or off the NavMesh.
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

            animator.SetFloat(_speedParam, gaitSpeed, speedDamping, Time.deltaTime);
            animator.speed = playbackRate;

            GuardState state = _guard.State;
            Apply(ref _isLookingAbout, _guard.IsLookingAbout, _lookingAboutParam);
            Apply(ref _isTurning, _guard.IsTurningToWalk && state != GuardState.KnockedOut, _turningParam);

            if (_isTurning) {
                animator.SetFloat(_turnProgressParam, Mathf.Clamp01(_guard.TurnedDegrees / turnClipAngle));
            }

            Apply(ref _isStunned, state == GuardState.Stunned, _stunnedParam);
            Apply(ref _isKnockedOut, state == GuardState.KnockedOut, _knockedOutParam);
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
