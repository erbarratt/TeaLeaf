using UnityEngine;
using UnityEngine.AI;

namespace AI
{
    /// <summary>
    /// The guard's ragdoll: a Rigidbody and a collider on each main bone
    /// (hips, chest, head, upper and lower arms and legs), joined by
    /// CharacterJoints, on the Ragdoll layer. Built onto the prefab by
    /// TeaLeaf > Modify Prefab > Build Guard Ragdoll.
    ///
    /// Awake, the bodies are kinematic and ride the animated bones, so
    /// their colliders are the guard's hit boxes: a bolt's ray finds the
    /// limb it really hit (Guard.FindByBody), and the head's is the one
    /// a stun bolt must hit (Guard.IsHead). The root's capsule stays for
    /// the player's body and the agent; nothing but bolts looks at these.
    ///
    /// Knocked out (GoLimp), the Animator stops and the bodies fall
    /// under physics, carrying on at the speed the guard was moving.
    /// Woken (StandUp), the root is moved under where the hips lie,
    /// facing the way the head lies, the bodies are held again and the
    /// Animator restarts; for standUpTime every bone eases from the pose
    /// it lay in to the animated one, so the guard rises without a jump.
    /// Called by Guard, which waits out the stand-up before it does
    /// anything else.
    /// </summary>
    [RequireComponent(typeof(Guard))]
    public class GuardRagdoll : MonoBehaviour
    {
        // The model's Animator, stopped while limp. Found in the children
        // if not assigned.
        [Tooltip("The model's Animator, stopped while the guard lies limp. Found in the children if not assigned.")]
        [SerializeField] private Animator animator;

        // The bone bodies, the hips first; their colliders, in the same
        // order; and the head's, what a stun bolt must hit. Filled in by
        // the builder.
        [Tooltip("The ragdoll's bone Rigidbodies, the hips first. Filled in by TeaLeaf > Modify Prefab > Build Guard Ragdoll.")]
        [SerializeField] private Rigidbody[] bodies;
        [Tooltip("The bone colliders, in the same order as the bodies: the guard's hit boxes for bolts.")]
        [SerializeField] private Collider[] hitboxes;
        [Tooltip("The head bone's collider: what a stun bolt must hit.")]
        [SerializeField] private Collider headHitbox;

        // Seconds the guard takes to rise from where it lay into the
        // animated pose.
        [Tooltip("Seconds the guard takes to rise from where it lay into its standing pose.")]
        [SerializeField] private float standUpTime = 0.8f;

        // Going limp, the chest and head are given this much extra speed
        // forwards, in metres a second, so a guard knocked out standing
        // topples over instead of folding up onto its knees.
        [Tooltip("Extra forward speed given to the chest and head on going limp, in metres a second, so a standing guard topples over rather than folding onto its knees.")]
        [SerializeField] private float topplePush = 1.5f;

        // How far from where the hips lie the root may be put back on the
        // NavMesh, in metres.
        private const float StandUpSampleDistance = 1.5f;

        // Every bone under the Animator, parents before children, and
        // the local rotation each had as the guard lay; the hips' local
        // position likewise.
        private Transform[] _bones;
        private Quaternion[] _lyingRotations;
        private Vector3 _lyingHipsPosition;
        private Transform _hips;
        private Transform _head;

        // The head's body and the one it hangs from (the chest): the
        // upper body, tipped forwards on going limp.
        private Rigidbody _headBody;
        private Rigidbody _chestBody;

        private float _standUpTimer;

        // The Animator's own culling, put back once standing: while
        // rising it animates even unseen, or a guard woken out of sight
        // would be left posed as it lay.
        private AnimatorCullingMode _cullingMode;

        /// True while lying under physics.
        public bool IsLimp { get; private set; }

        /// True while rising from where it lay into the animated pose.
        public bool IsStandingUp => _standUpTimer > 0f;

        /// The colliders on the bones: the guard's hit boxes.
        public Collider[] Hitboxes => hitboxes;

        /// The head bone's collider.
        public Collider HeadHitbox => headHitbox;

        /// Where the body is: the hips, wherever they have fallen.
        public Vector3 HipsPosition => _hips.position;

        private void Awake()
        {
            if (animator == null) {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator == null || bodies == null || bodies.Length == 0 || headHitbox == null) {
                Debug.LogError($"GuardRagdoll '{name}': no Animator or no ragdoll bodies. Run TeaLeaf > Modify Prefab > Build Guard Ragdoll.", this);
                enabled = false;
                return;
            }

            _hips = bodies[0].transform;
            _head = headHitbox.transform;
            _headBody = headHitbox.attachedRigidbody;
            _chestBody = _head.TryGetComponent(out CharacterJoint neck) ? neck.connectedBody : null;

            // GetComponentsInChildren lists parents before their children,
            // which the stand-up blend relies on. The Animator's own
            // transform is left out: GuardAnimation leans it.
            Transform[] all = animator.GetComponentsInChildren<Transform>();
            _bones = new Transform[all.Length - 1];
            System.Array.Copy(all, 1, _bones, 0, _bones.Length);
            _lyingRotations = new Quaternion[_bones.Length];

            IgnoreOverlappingPairs();

            // Awake, the bodies ride the animation.
            SetKinematic(true);
        }

        /// <summary>
        /// Hit boxes that already overlap in the standing pose (the
        /// thighs, a forearm by a thigh, the upper arms by the hips) are
        /// set never to collide with each other: once limp, physics would
        /// push them apart for ever and the body would never lie still.
        /// Joined pairs never collide anyway. Once, at load, while the
        /// bones are still in the prefab's standing pose.
        /// </summary>
        private void IgnoreOverlappingPairs()
        {
            for (int i = 0; i < hitboxes.Length; i++) {
                Collider a = hitboxes[i];
                a.transform.GetPositionAndRotation(out Vector3 aPosition, out Quaternion aRotation);

                for (int j = i + 1; j < hitboxes.Length; j++) {
                    Collider b = hitboxes[j];
                    b.transform.GetPositionAndRotation(out Vector3 bPosition, out Quaternion bRotation);

                    if (Physics.ComputePenetration(a, aPosition, aRotation, b, bPosition, bRotation, out _, out _)) {
                        Physics.IgnoreCollision(a, b);
                    }
                }
            }
        }

        /// <summary>
        /// Knocked out: the Animator stops where it is and the bodies
        /// fall under physics, moving on at velocity (the guard's own
        /// speed when it went down), the upper body tipped forwards.
        /// </summary>
        public void GoLimp(Vector3 velocity)
        {
            if (_standUpTimer > 0f) {
                _standUpTimer = 0f;
                animator.cullingMode = _cullingMode;
            }

            animator.enabled = false;
            SetKinematic(false);

            Vector3 topple = velocity + transform.forward * topplePush;

            for (int i = 0; i < bodies.Length; i++) {
                bool isUpperBody = bodies[i] == _headBody || bodies[i] == _chestBody;
                bodies[i].linearVelocity = isUpperBody ? topple : velocity;
                bodies[i].angularVelocity = Vector3.zero;
            }

            IsLimp = true;
        }

        /// <summary>
        /// Woken: puts the root on the NavMesh under where the hips lie,
        /// facing from the hips to the head (so it rises along the way it
        /// lay), holds the bodies again and restarts the Animator. The
        /// pose it lay in is noted, and LateUpdate eases from it to the
        /// animated pose over standUpTime.
        /// </summary>
        public void StandUp()
        {
            SetKinematic(true);
            IsLimp = false;

            // Moving the root drags every bone with it, so where the hips
            // lie is noted first and put back after.
            _hips.GetPositionAndRotation(out Vector3 hipsPosition, out Quaternion hipsRotation);

            Vector3 place = hipsPosition;
            place.y = transform.position.y;

            if (NavMesh.SamplePosition(hipsPosition, out NavMeshHit hit, StandUpSampleDistance, NavMesh.AllAreas)) {
                place = hit.position;
            }

            Vector3 along = _head.position - hipsPosition;
            along.y = 0f;
            Quaternion facing = along.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(along) : transform.rotation;

            transform.SetPositionAndRotation(place, facing);
            _hips.SetPositionAndRotation(hipsPosition, hipsRotation);

            // Every other bone hangs off the hips by its local rotation,
            // which moving the root didn't change.
            for (int i = 0; i < _bones.Length; i++) {
                _lyingRotations[i] = _bones[i].localRotation;
            }

            _lyingHipsPosition = _hips.localPosition;

            // Re-enabled, the Animator starts again from its default
            // state: standing.
            _cullingMode = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            _standUpTimer = standUpTime;
        }

        /// <summary>
        /// After the Animator has posed the bones: while standing up,
        /// each bone is put part way from where it lay to where the
        /// animation has it, eased in and out.
        /// </summary>
        private void LateUpdate()
        {
            if (_standUpTimer <= 0f) {
                return;
            }

            _standUpTimer -= Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Max(_standUpTimer, 0f) / standUpTime);

            for (int i = 0; i < _bones.Length; i++) {
                Transform bone = _bones[i];
                bone.localRotation = Quaternion.Slerp(_lyingRotations[i], bone.localRotation, t);
            }

            _hips.localPosition = Vector3.Lerp(_lyingHipsPosition, _hips.localPosition, t);

            if (_standUpTimer <= 0f) {
                animator.cullingMode = _cullingMode;
            }
        }

        /// <summary>
        /// Holds every body to its bone (true) or lets physics move it.
        /// Moved by physics, a body is interpolated: physics steps 50
        /// times a second, slower than the headset draws, so the limbs
        /// would otherwise move in visible steps. Held, the animation
        /// places the bones and interpolation would only cost.
        /// </summary>
        private void SetKinematic(bool isKinematic)
        {
            RigidbodyInterpolation interpolation = isKinematic ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;

            for (int i = 0; i < bodies.Length; i++) {
                bodies[i].isKinematic = isKinematic;
                bodies[i].interpolation = interpolation;
            }
        }
    }
}
