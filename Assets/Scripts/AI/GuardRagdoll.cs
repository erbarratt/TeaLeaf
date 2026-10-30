using UnityEngine;
using UnityEngine.AI;

namespace AI
{
    /// <summary>
    /// The guard's ragdoll: a Rigidbody and a collider on each main bone
    /// (hips, chest, head, upper and lower arms and legs), joined by
    /// ConfigurableJoints with soft limits, on the Ragdoll layer. Built
    /// onto the prefab by TeaLeaf > Modify Prefab > Build Guard Ragdoll.
    ///
    /// Awake, the bodies are kinematic and ride the animated bones, so
    /// their colliders are the guard's hit boxes: a bolt's ray finds the
    /// limb it really hit (Guard.FindByBody), and the head's is the one
    /// a stun bolt must hit (Guard.IsHead). The root's capsule stays for
    /// the player's body and the agent; nothing but bolts looks at these.
    ///
    /// Knocked out (GoLimp), the Animator stops and the bodies fall
    /// under physics, carrying on at the speed the guard was moving,
    /// each joint pulled weakly towards the neutral pose the builder
    /// gave it (knees, elbows, hips a little bent), so the body falls
    /// and lies curled like a person, not with straight limbs. Woken
    /// (StandUp), the root is moved just behind where the hips lie,
    /// facing the way the head lies, the bodies are held again and the
    /// Animator restarts in the get-up clip (Guard_PushUpToIdle: lying
    /// face down along the root's forward, hips getUpHipsAhead in front
    /// of the root, pushing up to standing over the root in the idle's
    /// first pose); for standUpTime every bone eases from the pose it lay
    /// in to the clip's, so the guard rises without a jump, and it is
    /// standing up until the clip has handed over to Locomotion.
    /// Called by Guard, which waits out the stand-up before it does
    /// anything else.
    /// </summary>
    [RequireComponent(typeof(Guard))]
    public class GuardRagdoll : MonoBehaviour
    {
        // The model's Animator, stopped while limp. Found in the children
        // if not assigned.
        [Tooltip("The model's Animator, stopped while the guard lies limp and started again as it stands up, e.g. the Animator on the prefab's Model object. Left empty, it is found in the children.")]
        [SerializeField] private Animator animator;

        // The bone bodies, the hips first; their colliders, in the same
        // order; and the head's, what a stun bolt must hit. Filled in by
        // the builder.
        [Tooltip("One entry per ragdoll bone's Rigidbody, the hips first, e.g. Hips, Chest, Head, then the limbs: kinematic riding the animation, dynamic while limp. Filled in by TeaLeaf > Modify Prefab > Build Guard Ragdoll; rerun it rather than editing by hand.")]
        [SerializeField] private Rigidbody[] bodies;
        [Tooltip("One entry per bone collider, in the same order as Bodies: the guard's hit boxes for bolts, e.g. the hips' box or a forearm's capsule. Filled in by Build Guard Ragdoll.")]
        [SerializeField] private Collider[] hitboxes;
        [Tooltip("The head bone's hit box collider, e.g. the sphere on the Head bone: a stun arrow only stuns when it hits this one. Filled in by Build Guard Ragdoll.")]
        [SerializeField] private Collider headHitbox;

        // Seconds the pose it lay in takes to blend into the get-up
        // clip, which then plays on to standing.
        [Tooltip("Seconds each bone takes to blend from the pose the guard lay in into the get-up clip (Guard_PushUpToIdle, which then plays on to standing). e.g. 0.4 = the limbs gather under it as it starts to push up. Higher = softer but the clip's start is hidden longer; 0 = snaps into the clip.")]
        [SerializeField] private float standUpTime = 0.4f;

        // How far ahead of the root the get-up clip's first frame has the
        // hips, in metres: the clip ends standing over the root, so it
        // starts lying this far forward of it. Measured from the clip.
        [Tooltip("How far ahead of the root the get-up clip (Guard_PushUpToIdle) starts with the hips, in metres; standing up, the root is put this far behind where the hips lie, so the clip starts where the body lies and ends standing over the root. e.g. 0.11 = the clip's own first frame. Wrong = the body slides along the ground as it starts to push up; remeasure if the clip changes.")]
        [SerializeField] private float getUpHipsAhead = 0.11f;

        // Going limp, the chest and head are given this much extra speed
        // forwards, in metres a second, so a guard knocked out standing
        // topples over instead of folding up onto its knees.
        [Tooltip("Extra forward speed given to the chest and head on going limp, in metres a second, so a standing guard topples over rather than folding onto its knees. e.g. 1.5 = falls face down. Higher = pitches forward harder; 0 = slumps straight down.")]
        [SerializeField] private float topplePush = 1.5f;

        // While limp, how hard each joint pulls towards its neutral pose,
        // per kg of the limb's body: weak, so gravity and the floor still
        // win and the pull only curls what is free to move.
        [Tooltip("While limp, how hard each joint pulls its limb towards the relaxed neutral pose (knees, elbows, hips a little bent), in N m per radian per kg of the limb. e.g. 20 = limbs curl as they fall and lie bent. Higher = stiffer, more posed body that can hold limbs off the floor; 0 = straight, floppy limbs.")]
        [SerializeField] private float neutralStiffness = 20f;
        [Tooltip("While limp, how much each joint's pull towards the neutral pose is damped, in N m s per radian per kg of the limb. e.g. 1 = limbs ease into the curl without wobbling. Higher = slower, heavier-feeling limbs; 0 = they spring and swing about it.")]
        [SerializeField] private float neutralDamping = 1f;

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

        // Each body's joint to its parent (null for the hips), in the
        // order of bodies.
        private ConfigurableJoint[] _joints;

        private static readonly int _getUpState = Animator.StringToHash("Get Up");

        private float _standUpTimer;

        // True from StandUp until the get-up clip has handed over to
        // Locomotion.
        private bool _isGettingUp;

        // The Animator's own culling, put back once standing: while
        // rising it animates even unseen, or a guard woken out of sight
        // would be left posed as it lay.
        private AnimatorCullingMode _cullingMode;

        /// True while lying under physics.
        public bool IsLimp { get; private set; }

        /// True while rising from where it lay: the blend into the get-up
        /// clip and the clip itself.
        public bool IsStandingUp => _isGettingUp;

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
            _chestBody = _head.TryGetComponent(out ConfigurableJoint neck) ? neck.connectedBody : null;

            _joints = new ConfigurableJoint[bodies.Length];

            for (int i = 0; i < bodies.Length; i++) {
                bodies[i].TryGetComponent(out _joints[i]);
            }

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
        /// speed when it went down), each joint pulling towards its
        /// neutral pose. topple: the upper body is tipped forwards (a
        /// guard knocked out standing); false for one the animation has
        /// already brought down (a sleep dart's collapse).
        /// </summary>
        public void GoLimp(Vector3 velocity, bool topple)
        {
            if (_isGettingUp) {
                _isGettingUp = false;
                _standUpTimer = 0f;
                animator.cullingMode = _cullingMode;
            }

            animator.enabled = false;
            SetKinematic(false);
            SetNeutralDrives();

            Vector3 upperBodyVelocity = topple ? velocity + transform.forward * topplePush : velocity;

            for (int i = 0; i < bodies.Length; i++) {
                bool isUpperBody = bodies[i] == _headBody || bodies[i] == _chestBody;
                bodies[i].linearVelocity = isUpperBody ? upperBodyVelocity : velocity;
                bodies[i].angularVelocity = Vector3.zero;
            }

            IsLimp = true;
        }

        /// <summary>
        /// Woken: puts the root on the NavMesh getUpHipsAhead behind where
        /// the hips lie, facing from the hips to the head (so it rises
        /// along the way it lay - the get-up clip starts lying just so,
        /// face down, hips that far ahead of the root), holds the bodies again and restarts the
        /// Animator in the get-up clip. The pose it lay in is noted, and
        /// LateUpdate eases from it to the clip's over standUpTime. A
        /// guard lying face up is turned over by that blend.
        /// </summary>
        public void StandUp()
        {
            SetKinematic(true);
            IsLimp = false;

            // Moving the root drags every bone with it, so where the hips
            // lie is noted first and put back after.
            _hips.GetPositionAndRotation(out Vector3 hipsPosition, out Quaternion hipsRotation);

            Vector3 along = _head.position - hipsPosition;
            along.y = 0f;
            Quaternion facing = along.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(along) : transform.rotation;

            // The clip starts with the hips a little ahead of the root, so
            // the root goes that far back from where they lie.
            Vector3 under = hipsPosition - facing * Vector3.forward * getUpHipsAhead;
            Vector3 place = under;
            place.y = transform.position.y;

            if (NavMesh.SamplePosition(under, out NavMeshHit hit, StandUpSampleDistance, NavMesh.AllAreas)) {
                place = hit.position;
            }

            transform.SetPositionAndRotation(place, facing);
            _hips.SetPositionAndRotation(hipsPosition, hipsRotation);

            // Every other bone hangs off the hips by its local rotation,
            // which moving the root didn't change.
            for (int i = 0; i < _bones.Length; i++) {
                _lyingRotations[i] = _bones[i].localRotation;
            }

            _lyingHipsPosition = _hips.localPosition;

            // Re-enabled, the Animator starts from its default state;
            // it is sent straight to the start of the get-up clip, which
            // it plays from its update this same frame.
            _cullingMode = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            animator.Play(_getUpState, 0, 0f);
            _standUpTimer = standUpTime;
            _isGettingUp = true;
        }

        /// <summary>
        /// After the Animator has posed the bones: while the blend runs,
        /// each bone is put part way from where it lay to where the
        /// get-up clip has it, eased in and out. Then the clip plays on
        /// alone; once it has handed over to Locomotion (left the Get Up
        /// state, transition included) the guard is standing.
        /// </summary>
        private void LateUpdate()
        {
            if (!_isGettingUp) {
                return;
            }

            if (_standUpTimer > 0f) {
                _standUpTimer -= Time.deltaTime;
                float t = standUpTime > 0f ? Mathf.SmoothStep(0f, 1f, 1f - Mathf.Max(_standUpTimer, 0f) / standUpTime) : 1f;

                for (int i = 0; i < _bones.Length; i++) {
                    Transform bone = _bones[i];
                    bone.localRotation = Quaternion.Slerp(_lyingRotations[i], bone.localRotation, t);
                }

                _hips.localPosition = Vector3.Lerp(_lyingHipsPosition, _hips.localPosition, t);
                return;
            }

            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == _getUpState) {
                return;
            }

            _isGettingUp = false;
            animator.cullingMode = _cullingMode;
        }

        /// <summary>
        /// Gives each joint's drive towards its neutral pose its strength,
        /// scaled by the limb's mass. Set on going limp (the drives do
        /// nothing while the bodies are held), so Play Mode tuning shows
        /// at the next knock-out.
        /// </summary>
        private void SetNeutralDrives()
        {
            for (int i = 0; i < _joints.Length; i++) {
                ConfigurableJoint joint = _joints[i];

                if (joint == null) {
                    continue;
                }

                float mass = bodies[i].mass;
                joint.slerpDrive = new JointDrive { positionSpring = neutralStiffness * mass, positionDamper = neutralDamping * mass, maximumForce = float.MaxValue };
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
