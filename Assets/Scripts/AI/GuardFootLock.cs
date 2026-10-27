using Core;
using UnityEngine;
using UnityEngine.AI;

namespace AI
{
    /// <summary>
    /// Keeps a guard's planted foot still on the ground while the body
    /// moves over it, so the feet don't slide.
    ///
    /// GuardAnimation already plays the walk and run at the rate that
    /// makes the stride match the agent's speed, so on a steady walk a
    /// planted foot hardly drifts. What's left - setting off, stopping,
    /// a turn, a small mismatch in the clip - is cleaned up here: when
    /// the animated foot comes down (its ankle drops below plantHeight
    /// above the guard's root) its world position is remembered, and
    /// until it lifts again the leg is bent by two-bone IK so the ankle
    /// stays there. When it lifts, the lock fades out over releaseTime
    /// and the foot rejoins the animation. A lock that would drag the
    /// foot more than maxSlip from the animated one lets go at once
    /// (turning on the spot, a gait change) and the foot doesn't lock
    /// again until it has lifted.
    ///
    /// Standing (the agent slower than Guard.StandingSpeed), the feet are
    /// stepped by code instead: the idle and look-around clips don't
    /// step, and the guard's body is turned on the spot under them -
    /// facing what it noticed, looking about, or turning to set off. Each foot is held where it was put, angle
    /// and all; its home is where the animation would draw it, which
    /// turns with the body. When a held foot is more than stepDistance
    /// or stepAngle from its home, it lifts and swings in a low arc onto
    /// it over stepTime. One foot steps at a time - the one furthest
    /// out - so a long turn takes alternate steps. Each step sounds as
    /// the foot comes down (Guard.PlayFootstep, at the Guard's quieter
    /// standingStepVolume); while the feet aren't stepped (unseen, far)
    /// a turn on the spot sounds a step each half stepAngle turned, at
    /// most one each stepTime.
    ///
    /// Runs in LateUpdate, after the Animator has posed the bones and
    /// the agent has moved the root, and only while the model is drawn
    /// and near the camera: with CullUpdateTransforms the Animator
    /// doesn't pose an unseen model, and bending its legs again would
    /// add up frame on frame. Off while the guard is stunned or knocked
    /// out (those clips aren't stepping). The foot keeps its animated
    /// rotation; no rays, so the foot isn't fitted to slopes or steps.
    ///
    /// Gizmo (also in the headset): a ball where each locked foot is
    /// held, green while planted, yellow while the lock fades out and
    /// orange while stepping, with a line to where the animation put the
    /// foot - the length of that line is the slide the lock is taking
    /// out. Standing, a small cyan ball marks each foot's home.
    /// </summary>
    [RequireComponent(typeof(Guard))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class GuardFootLock : MonoBehaviour, IDebugDrawable
    {
        // The leg bones, found through the model's Humanoid rig if not
        // assigned.
        [Tooltip("Left thigh bone, the top of the leg the IK bends. Left empty, the Humanoid rig's left upper leg is found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform leftUpperLeg;
        [Tooltip("Left shin bone (the knee is its pivot). Left empty, the Humanoid rig's left lower leg is found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform leftLowerLeg;
        [Tooltip("Left foot bone (the ankle): the point held still while planted. Left empty, the Humanoid rig's left foot is found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform leftFoot;
        [Tooltip("Right thigh bone, the top of the leg the IK bends. Left empty, the Humanoid rig's right upper leg is found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform rightUpperLeg;
        [Tooltip("Right shin bone (the knee is its pivot). Left empty, the Humanoid rig's right lower leg is found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform rightLowerLeg;
        [Tooltip("Right foot bone (the ankle): the point held still while planted. Left empty, the Humanoid rig's right foot is found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform rightFoot;

        // The model's skinned mesh: the lock only runs while it is drawn.
        // Found in the children if not assigned.
        [Tooltip("The model's skinned mesh, e.g. the guard_1 SkinnedMeshRenderer: feet are locked only while it is drawn (a culled model isn't re-posed). Left empty, it is found in the children; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Renderer bodyRenderer;

        // An animated ankle lower than this above the root is planted.
        // The clips' planted ankles sit at 0.11-0.13m; a lifted one is
        // above 0.135 within a frame or two.
        [Tooltip("An animated ankle lower than this above the root, in metres, is planted and held still in the world. e.g. 0.135: the clips' planted ankles sit at 0.11-0.13m. Higher = feet plant earlier and lift later (may drag); lower = some planted feet never lock and slide.")]
        [SerializeField] private float plantHeight = 0.135f;

        // How long a lifted foot takes to blend back to the animation.
        [Tooltip("Seconds a lifted foot takes to blend from where it was held back to the animation. e.g. 0.12 = a couple of frames' worth of easing. Higher = smoother but the foot lags its stride; 0 = snaps back.")]
        [SerializeField] private float releaseTime = 0.12f;

        // How far a locked foot may be from the animated one before the
        // lock lets go.
        [Tooltip("How far, in metres, a locked foot may be from the animated one before the lock lets go at once (e.g. on a change between walk and run), not relocking until the foot lifts. e.g. 0.15. Higher = holds through bigger gaps (legs may stretch); lower = lets go sooner.")]
        [SerializeField] private float maxSlip = 0.15f;

        // Beyond this distance from the camera the feet aren't locked.
        [Tooltip("Beyond this distance from the camera, in metres, the feet aren't locked (the animation plays as it is, saving the IK). e.g. 20 = across a street. Higher = locked further off, more CPU.")]
        [SerializeField] private float maxDistance = 20f;

        // Standing: how far (m, along the ground) and how far round
        // (degrees) a held foot may get from its home before it steps.
        [Tooltip("Standing: how far, in metres along the ground, a held foot may get from where the animation draws it (its home) before it steps there. e.g. 0.12 = a short shuffle. Higher = fewer, bigger steps (more twisted legs); lower = more fidgety steps.")]
        [SerializeField] private float stepDistance = 0.12f;
        [Tooltip("Standing: how far round, in degrees, a held foot may turn from its home before it steps; unseen, a quiet step sounds every half this turned. e.g. 25 = a half turn takes several steps. Higher = fewer steps while turning; lower = more.")]
        [SerializeField] private float stepAngle = 25f;

        // How long a step takes, and how high the ankle lifts at its
        // middle.
        [Tooltip("Seconds a standing step takes, eased; also the least time between two standing steps. e.g. 0.25 = a brisk shuffle. Higher = slower steps that may fall behind a fast turn; lower = quicker, twitchier.")]
        [SerializeField] private float stepTime = 0.25f;
        [Tooltip("How high the ankle lifts at the middle of a standing step, in metres. e.g. 0.08 = a low shuffle. Higher = a more marked step; 0 = the foot slides along the ground.")]
        [SerializeField] private float stepHeight = 0.08f;

        private class Leg
        {
            public Transform Upper;
            public Transform Lower;
            public Transform Foot;
            // Where the foot is held, in world space.
            public Vector3 LockPosition;
            // Standing only: the foot's held rotation (walking, a held
            // foot keeps its animated rotation).
            public Quaternion LockRotation;
            // Standing only: a step under way, where it set off from and
            // how far through it is (0-1).
            public bool IsStepping;
            public Vector3 StepFromPosition;
            public Quaternion StepFromRotation;
            public float StepProgress;
            // Where the ankle was drawn this frame.
            public Vector3 DrawnPosition;
            // Where the animation put the ankle this frame, for the gizmo.
            public Vector3 AnimatedPosition;
            // 1 = at LockPosition, 0 = where the animation put it.
            public float Weight;
            public bool IsPlanted;
            // False after a lock let go by slipping, until the foot lifts.
            public bool CanPlant = true;
        }

        // How far forwards (m) the knee is pushed when finding the plane it
        // bends in: well above a straight leg's wobble, well below a bent
        // knee's 5-20cm.
        private const float KneeForwardBias = 0.02f;

        private static readonly Color _plantedColor = new(0.2f, 1f, 0.3f, 1f);
        private static readonly Color _releasingColor = new(1f, 0.9f, 0.2f, 1f);
        private static readonly Color _steppingColor = new(1f, 0.5f, 0.1f, 1f);
        private static readonly Color _homeColor = new(0.2f, 0.9f, 1f, 1f);

        private Guard _guard;
        private NavMeshAgent _agent;
        private Leg _left;
        private Leg _right;
        private Transform _camera;
        private bool _isValid;
        // Whether last frame's feet were stepped by code (standing).
        private bool _isStanding;
        // Unseen or far: last frame's heading, and degrees turned on the
        // spot since the last step sounded.
        private float _unseenYaw;
        private float _unseenTurned;
        // When the last standing step sounded, seen or unseen: no two
        // come closer than stepTime, as a stepped foot can't.
        private float _lastStandingStepTime = float.NegativeInfinity;

        private void Awake()
        {
            _guard = GetComponent<Guard>();
            _agent = GetComponent<NavMeshAgent>();

            if (leftUpperLeg == null) leftUpperLeg = FindBone(HumanBodyBones.LeftUpperLeg);
            if (leftLowerLeg == null) leftLowerLeg = FindBone(HumanBodyBones.LeftLowerLeg);
            if (leftFoot == null) leftFoot = FindBone(HumanBodyBones.LeftFoot);
            if (rightUpperLeg == null) rightUpperLeg = FindBone(HumanBodyBones.RightUpperLeg);
            if (rightLowerLeg == null) rightLowerLeg = FindBone(HumanBodyBones.RightLowerLeg);
            if (rightFoot == null) rightFoot = FindBone(HumanBodyBones.RightFoot);

            if (bodyRenderer == null) {
                bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            }

            _isValid = leftUpperLeg != null && leftLowerLeg != null && leftFoot != null
                && rightUpperLeg != null && rightLowerLeg != null && rightFoot != null
                && bodyRenderer != null;

            if (!_isValid) {
                Debug.LogError($"GuardFootLock '{name}': leg bones or body renderer not found.", this);
                enabled = false;
                return;
            }

            _left = new Leg { Upper = leftUpperLeg, Lower = leftLowerLeg, Foot = leftFoot };
            _right = new Leg { Upper = rightUpperLeg, Lower = rightLowerLeg, Foot = rightFoot };
        }

        private void OnEnable()
        {
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            DebugDrawRegistry.Unregister(this);
        }

        private void LateUpdate()
        {
            if (!IsActive()) {
                // Nothing held across a spell unseen, stunned or down.
                Clear(_left);
                Clear(_right);
                _isStanding = false;
                TickUnseenStandingSteps();
                return;
            }

            _unseenYaw = transform.eulerAngles.y;
            _unseenTurned = 0f;

            bool isStanding = IsStanding();

            if (isStanding != _isStanding) {
                _isStanding = isStanding;

                if (isStanding) {
                    StartStanding(_left);
                    StartStanding(_right);
                } else {
                    StopStanding(_left);
                    StopStanding(_right);
                }
            }

            if (isStanding) {
                TickStanding(Time.deltaTime);
                return;
            }

            float fade = releaseTime > 0f ? Time.deltaTime / releaseTime : 1f;
            Tick(_left, fade);
            Tick(_right, fade);
        }

        /// <summary>
        /// While the feet aren't stepped (model unseen or far), a guard
        /// turning on the spot still has to be heard: a step each half
        /// stepAngle turned, about as often as the two feet would step
        /// (each steps when it is stepAngle out, alternately), but never
        /// more often than once each stepTime: a fast turn (720°/s goes
        /// through half stepAngle in under two frames) would otherwise
        /// sound a step nearly every frame. Nothing while stunned or down.
        /// </summary>
        private void TickUnseenStandingSteps()
        {
            float yaw = transform.eulerAngles.y;
            float turned = Mathf.Abs(Mathf.DeltaAngle(_unseenYaw, yaw));
            _unseenYaw = yaw;

            GuardState state = _guard.State;

            if (state == GuardState.Stunned || state == GuardState.KnockedOut || !IsStanding()) {
                _unseenTurned = 0f;
                return;
            }

            _unseenTurned += turned;

            if (_unseenTurned >= stepAngle * 0.5f && Time.time - _lastStandingStepTime >= stepTime) {
                // Turning built up while waiting is dropped, not owed.
                _unseenTurned = 0f;
                PlayStandingStep();
            }
        }

        /// <summary>
        /// A step taken standing sounds (quieter, Guard.standingStepVolume)
        /// and its time is noted for the unseen steps' spacing.
        /// </summary>
        private void PlayStandingStep()
        {
            _lastStandingStepTime = Time.time;
            _guard.PlayFootstep(true);
        }

        /// <summary>
        /// Whether the guard is standing, its feet stepped by code: the
        /// agent (nearly) still, turning on the spot or not.
        /// </summary>
        private bool IsStanding()
        {
            Vector3 velocity = _agent.velocity;
            velocity.y = 0f;
            return velocity.sqrMagnitude < Guard.StandingSpeed * Guard.StandingSpeed;
        }

        /// <summary>
        /// Coming to a stand: a foot already held stays where it is, now
        /// holding its angle too; one that isn't is held where it is drawn
        /// (a step corrects it if that is far from home).
        /// </summary>
        private static void StartStanding(Leg leg)
        {
            if (!leg.IsPlanted) {
                leg.LockPosition = Vector3.Lerp(leg.Foot.position, leg.LockPosition, leg.Weight);
                leg.IsPlanted = true;
            }

            leg.LockRotation = leg.Foot.rotation;
            leg.Weight = 1f;
            leg.IsStepping = false;
        }

        /// <summary>
        /// Setting off: a held foot stays held until the walk lifts it, as
        /// on any other plant; one caught mid-step lets go from where it
        /// is and blends into the walk over releaseTime.
        /// </summary>
        private static void StopStanding(Leg leg)
        {
            if (!leg.IsStepping) {
                return;
            }

            leg.IsStepping = false;
            leg.IsPlanted = false;
            leg.CanPlant = true;
            leg.LockPosition = leg.DrawnPosition;
            leg.Weight = 1f;
        }

        /// <summary>
        /// Standing: starts a step for the foot furthest from home if it
        /// is out far enough and no foot is stepping, then poses both.
        /// </summary>
        private void TickStanding(float deltaTime)
        {
            _left.AnimatedPosition = _left.Foot.position;
            _right.AnimatedPosition = _right.Foot.position;

            if (!_left.IsStepping && !_right.IsStepping) {
                float leftOut = HowFarOut(_left);
                float rightOut = HowFarOut(_right);

                if (leftOut > 1f || rightOut > 1f) {
                    BeginStep(leftOut >= rightOut ? _left : _right);
                }
            }

            PoseStanding(_left, deltaTime);
            PoseStanding(_right, deltaTime);
        }

        /// <summary>
        /// How far a held foot is from its home (where the animation draws
        /// it) as a share of what makes it step: over 1 = time to step.
        /// Whichever is further out, distance along the ground or angle.
        /// </summary>
        private float HowFarOut(Leg leg)
        {
            Vector3 offset = leg.AnimatedPosition - leg.LockPosition;
            offset.y = 0f;
            float distance = offset.magnitude / stepDistance;
            float angle = Quaternion.Angle(leg.LockRotation, leg.Foot.rotation) / stepAngle;
            return Mathf.Max(distance, angle);
        }

        /// <summary>
        /// Lifts a held foot to step it home.
        /// </summary>
        private static void BeginStep(Leg leg)
        {
            leg.IsStepping = true;
            leg.IsPlanted = false;
            leg.StepProgress = 0f;
            leg.StepFromPosition = leg.LockPosition;
            leg.StepFromRotation = leg.LockRotation;
        }

        /// <summary>
        /// Standing: puts the ankle where it is held, or along its step -
        /// eased from where it set off to its home (which moves as the
        /// body keeps turning), lifted in an arc - and plants it at the
        /// end of the step.
        /// </summary>
        private void PoseStanding(Leg leg, float deltaTime)
        {
            Vector3 home = leg.AnimatedPosition;
            Quaternion homeRotation = leg.Foot.rotation;
            Vector3 target = leg.LockPosition;
            Quaternion rotation = leg.LockRotation;

            if (leg.IsStepping) {
                leg.StepProgress = Mathf.Min(1f, leg.StepProgress + deltaTime / stepTime);
                float eased = Mathf.SmoothStep(0f, 1f, leg.StepProgress);
                target = Vector3.Lerp(leg.StepFromPosition, home, eased)
                    + Vector3.up * (stepHeight * Mathf.Sin(leg.StepProgress * Mathf.PI));
                rotation = Quaternion.Slerp(leg.StepFromRotation, homeRotation, eased);

                if (leg.StepProgress >= 1f) {
                    // The foot is down: its (quieter) footstep.
                    PlayStandingStep();
                    leg.IsStepping = false;
                    leg.IsPlanted = true;
                    leg.LockPosition = home;
                    leg.LockRotation = homeRotation;
                    target = home;
                    rotation = homeRotation;
                }
            }

            leg.DrawnPosition = target;
            SolveTwoBone(leg.Upper, leg.Lower, leg.Foot, target);
            leg.Foot.rotation = rotation;
        }

        /// <summary>
        /// Whether the feet are locked this frame: the model drawn, near
        /// the camera, and the guard on its feet.
        /// </summary>
        private bool IsActive()
        {
            if (!bodyRenderer.isVisible) {
                return false;
            }

            GuardState state = _guard.State;

            if (state == GuardState.Stunned || state == GuardState.KnockedOut) {
                return false;
            }

            if (_camera == null) {
                Camera main = Camera.main;

                if (main == null) {
                    return false;
                }

                _camera = main.transform;
            }

            return (_camera.position - transform.position).sqrMagnitude <= maxDistance * maxDistance;
        }

        /// <summary>
        /// Plants, holds or releases one foot, then bends the leg to put
        /// the ankle where it should be this frame.
        /// </summary>
        private void Tick(Leg leg, float fade)
        {
            Vector3 animated = leg.Foot.position;
            leg.AnimatedPosition = animated;
            bool isDown = animated.y - transform.position.y < plantHeight;

            if (!isDown) {
                leg.CanPlant = true;
            }

            if (isDown && leg.CanPlant && !leg.IsPlanted) {
                // Lock where the foot is drawn now: the animated position,
                // or part way back from a lock still fading out.
                leg.LockPosition = Vector3.Lerp(animated, leg.LockPosition, leg.Weight);
                leg.Weight = 1f;
                leg.IsPlanted = true;
            } else if (leg.IsPlanted && !isDown) {
                leg.IsPlanted = false;
            }

            if (leg.IsPlanted) {
                Vector3 slip = leg.LockPosition - animated;
                slip.y = 0f;

                if (slip.sqrMagnitude > maxSlip * maxSlip) {
                    leg.IsPlanted = false;
                    leg.CanPlant = false;
                }
            }

            if (!leg.IsPlanted) {
                leg.Weight = Mathf.MoveTowards(leg.Weight, 0f, fade);
            }

            if (leg.Weight <= 0f) {
                return;
            }

            Vector3 target = Vector3.Lerp(animated, leg.LockPosition, leg.Weight);
            Quaternion footRotation = leg.Foot.rotation;
            SolveTwoBone(leg.Upper, leg.Lower, leg.Foot, target);
            // The foot keeps the angle the animation gave it.
            leg.Foot.rotation = footRotation;
        }

        /// <summary>
        /// Forgets a foot's lock.
        /// </summary>
        private static void Clear(Leg leg)
        {
            leg.IsPlanted = false;
            leg.CanPlant = true;
            leg.IsStepping = false;
            leg.Weight = 0f;
        }

        /// <summary>
        /// Two-bone IK: turns the thigh and the shin so the ankle reaches
        /// target (or as near as a straight leg gets), keeping the knee
        /// bending the way the animation bent it.
        /// </summary>
        private void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target)
        {
            Vector3 a = upper.position;
            Vector3 b = lower.position;
            Vector3 c = end.position;

            float lengthAB = (b - a).magnitude;
            float lengthBC = (c - b).magnitude;
            // The hip-to-target distance, kept inside what the leg can
            // reach so the angles below stay defined.
            float lengthAT = Mathf.Clamp((target - a).magnitude, 0.01f, lengthAB + lengthBC - 0.001f);

            // The angles at the hip and the knee now, and the ones that
            // make the hip-to-ankle distance lengthAT (law of cosines).
            float hipAngle = Angle(c - a, b - a);
            float kneeAngle = Angle(a - b, c - b);
            float hipAngleWanted = Mathf.Acos(Mathf.Clamp(
                (lengthAB * lengthAB + lengthAT * lengthAT - lengthBC * lengthBC) / (2f * lengthAB * lengthAT), -1f, 1f));
            float kneeAngleWanted = Mathf.Acos(Mathf.Clamp(
                (lengthAB * lengthAB + lengthBC * lengthBC - lengthAT * lengthAT) / (2f * lengthAB * lengthBC), -1f, 1f));

            // The axis the knee bends about: across the plane of the hip,
            // the ankle and the knee pushed a little forwards. The walk
            // straightens a leg fully at the back of its stride, where the
            // animated knee wobbles either side of the hip-ankle line by a
            // fraction of a millimetre; its plane alone would then flip
            // frame to frame and the knee bend one way, then the other.
            // The forward push keeps a straight leg bending forwards and
            // barely moves the plane of a bent one.
            Vector3 line = (c - a).normalized;
            Vector3 knee = (b - a) - Vector3.Dot(b - a, line) * line;
            Vector3 forward = transform.forward - Vector3.Dot(transform.forward, line) * line;
            Vector3 bendAxis = Vector3.Cross(line, knee + forward * KneeForwardBias);

            if (bendAxis.sqrMagnitude < 1e-8f) {
                bendAxis = -transform.right;
            }

            bendAxis.Normalize();

            // Open or close the knee and turn the thigh to match, which
            // leaves the ankle on the same line from the hip...
            lower.rotation = Quaternion.AngleAxis((kneeAngleWanted - kneeAngle) * Mathf.Rad2Deg, bendAxis) * lower.rotation;
            upper.rotation = Quaternion.AngleAxis((hipAngleWanted - hipAngle) * Mathf.Rad2Deg, bendAxis) * upper.rotation;
            // ...then swing the whole leg about the hip onto the target.
            upper.rotation = Quaternion.FromToRotation(end.position - a, target - a) * upper.rotation;
        }

        /// <summary>
        /// The angle between two vectors, in radians.
        /// </summary>
        private static float Angle(Vector3 from, Vector3 to)
        {
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(from.normalized, to.normalized), -1f, 1f));
        }

        /// <summary>
        /// Editor-only: the locked feet, always.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// Draws each locked foot: a ball where it is held (green planted,
        /// yellow fading out) and a line to where the animation put it.
        /// Nothing while not playing, or for a foot that isn't locked.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!_isValid) {
                return;
            }

            DrawLeg(lines, _left, _isStanding);
            DrawLeg(lines, _right, _isStanding);
        }

        /// <summary>
        /// Draws one foot's lock, if it has one, and standing its home.
        /// </summary>
        private static void DrawLeg(DebugLines lines, Leg leg, bool isStanding)
        {
            if (leg.Weight <= 0f) {
                return;
            }

            if (isStanding) {
                lines.Color = _homeColor;
                lines.WireSphere(leg.AnimatedPosition, 0.03f);
            }

            if (leg.IsStepping) {
                lines.Color = _steppingColor;
                lines.WireSphere(leg.DrawnPosition, 0.06f);
                lines.Line(leg.DrawnPosition, leg.AnimatedPosition);
                return;
            }

            lines.Color = leg.IsPlanted ? _plantedColor : _releasingColor;
            lines.WireSphere(leg.LockPosition, 0.06f);
            lines.Line(leg.LockPosition, leg.AnimatedPosition);
        }

        /// <summary>
        /// Finds a bone of the model's Humanoid rig (any model whose avatar
        /// maps it, whatever its bones are called); null without a
        /// Humanoid Animator under the guard.
        /// </summary>
        private Transform FindBone(HumanBodyBones bone)
        {
            Animator animator = GetComponentInChildren<Animator>(true);
            return animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
        }
    }
}
