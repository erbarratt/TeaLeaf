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
    /// Four parameters in Guard.controller:
    /// - Speed: how fast the agent is actually moving along the ground,
    ///   driving a blend tree of idle, walk and run. A guard waiting at
    ///   a patrol point, stunned or knocked out reads 0 and idles.
    /// - IsLookingAbout: standing and looking (Suspicious, or at a
    ///   place in a Search), playing the look-around clip.
    /// - IsStunned, IsKnockedOut: the two states with a clip of their
    ///   own. The knocked-out clip is a fall that holds its last frame,
    ///   so the body lies there until the guard is woken, when the
    ///   Animator blends back to standing.
    ///
    /// The clips carry the walk and run's forward travel as root
    /// motion, which the Animator discards (applyRootMotion is off), so
    /// they play in place under the agent. Speed is set every frame -
    /// it moves nearly every frame anyway, and the damping needs the
    /// calls - but the three bools only on a change.
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

        private static readonly int _speedParam = Animator.StringToHash("Speed");
        private static readonly int _lookingAboutParam = Animator.StringToHash("IsLookingAbout");
        private static readonly int _stunnedParam = Animator.StringToHash("IsStunned");
        private static readonly int _knockedOutParam = Animator.StringToHash("IsKnockedOut");

        private Guard _guard;
        private NavMeshAgent _agent;

        // What the Animator was last told, so a bool is only set on a
        // change.
        private bool _isLookingAbout;
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

            animator.SetFloat(_speedParam, speed, speedDamping, Time.deltaTime);

            GuardState state = _guard.State;
            Apply(ref _isLookingAbout, _guard.IsLookingAbout, _lookingAboutParam);
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
