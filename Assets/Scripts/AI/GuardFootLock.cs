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
    /// add up frame on frame. Off while the guard is knocked out or
    /// GuardAnimation plays an action (a stun, the dive, the collapse):
    /// those clips place the feet themselves.
    ///
    /// The clips are drawn on flat ground at the root's height, so each
    /// time a foot plants (or a standing step sets off) two rays look
    /// down for the ground: one under the ankle, which sits over the
    /// heel, and one under the toe tip. The foot is moved up or down to
    /// the ground under the heel and tilted to its slope there; if the
    /// toes would still be under the ground under them (heel in the road,
    /// toes on a kerb) they are tipped up onto it, and whatever a tilt of
    /// maxFootTilt can't reach lifts the whole foot. The tilt is laid over
    /// the animated rotation, so the foot keeps the clip's own roll from
    /// heel to toe. A foot whose ground is below the root couldn't reach
    /// it with a straight leg, so the hips are lowered by the lower
    /// foot's drop (eased over hipsSmoothing). Rays only when a foot
    /// plants, never per frame: a planted foot doesn't move. A foot in
    /// the air follows the animation, and stairs are fitted as their
    /// ramp collider (no collider follows the steps).
    ///
    /// Gizmo (also in the headset): a ball where each locked foot is
    /// held, green while planted, yellow while the lock fades out and
    /// orange while stepping, with a line to where the animation put the
    /// foot - the length of that line is the slide the lock is taking
    /// out. Standing, a small cyan ball marks each foot's home. Magenta
    /// dots: where each foot's last heel and toe rays found the ground.
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
        [Tooltip("Tip of the left toes, where the left foot's toe ray looks down for the ground. Left empty, the end bone under the Humanoid rig's left toes is found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform leftToesEnd;
        [Tooltip("Tip of the right toes, where the right foot's toe ray looks down for the ground. Left empty, the end bone under the Humanoid rig's right toes is found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform rightToesEnd;
        [Tooltip("Hips bone, lowered so a foot on ground below the root can reach it. Left empty, the Humanoid rig's hips are found; with none, foot locking logs an error and switches off.")]
        [SerializeField] private Transform hips;

        // The model's skinned mesh: the lock only runs while it is drawn.
        // Found in the children if not assigned.
        [Tooltip("The model's skinned mesh, e.g. the guard model's Body SkinnedMeshRenderer: feet are locked only while it is drawn (a culled model isn't re-posed). Left empty, it is found in the children; with none, foot locking logs an error and switches off.")]
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

        // Ground fitting: how far above and below the root the heel and
        // toe rays look, the most a foot is tilted, and how far and how
        // smoothly the hips go down to let a foot reach lower ground.
        [Tooltip("How far above and below the root, in metres, a foot's heel and toe rays look for the ground. e.g. 0.4 = the NavMesh's step height. Higher = fits taller kerbs and steeper ground (a foot may climb onto a ledge it is beside); lower = more feet left at the root's height.")]
        [SerializeField] private float groundSearch = 0.4f;
        [Tooltip("The most a foot is tilted to fit the ground, in degrees; what this can't reach lifts the foot instead. e.g. 30 = steep street slopes. Higher = feet follow steeper ground (ankles bend further); lower = flatter feet, more lifted toes or heels.")]
        [SerializeField] private float maxFootTilt = 30f;
        [Tooltip("The most the hips are lowered, in metres, so a foot on ground below the root can reach it. e.g. 0.3 = a kerb or a steep slope. Higher = longer drops reached (the body sinks further); 0 = never lowered (a foot below the root floats on a straight leg).")]
        [SerializeField] private float maxHipsDrop = 0.3f;
        [Tooltip("Seconds the hips take to ease most of the way to a new height. e.g. 0.15 = settles within a step. Higher = smoother but feet may float or a knee overbend for a moment; lower = the body bobs with each plant.")]
        [SerializeField] private float hipsSmoothing = 0.15f;

        private class Leg
        {
            public Transform Upper;
            public Transform Lower;
            public Transform Foot;
            public Transform ToesEnd;
            // Ankle to toe tip in the foot bone's rotation frame (fixed by
            // the rig): turned by the foot's held rotation, it says which
            // way the toes point, and so the knee.
            public Vector3 ToeDirection;
            // The ground under the foot from its last plant: how far up
            // from the clip's ankle height (ground at the root) the ankle
            // goes, and the tilt laid over its animated rotation. Kept
            // while the foot is in the air, for the hips.
            public float GroundOffset;
            public Quaternion GroundTilt = Quaternion.identity;
            // Where the last heel and toe rays met the ground, for the
            // gizmo.
            public Vector3 HeelGround;
            public Vector3 ToeGround;
            public bool HasHeelGround;
            public bool HasToeGround;
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

        private static readonly Color _plantedColor = new(0.2f, 1f, 0.3f, 1f);
        private static readonly Color _releasingColor = new(1f, 0.9f, 0.2f, 1f);
        private static readonly Color _steppingColor = new(1f, 0.5f, 0.1f, 1f);
        private static readonly Color _homeColor = new(0.2f, 0.9f, 1f, 1f);
        private static readonly Color _groundColor = new(1f, 0.3f, 1f, 1f);

        // What the heel and toe rays stand a foot on (as GuardShadow's
        // floor rays): the guard's own capsule and hit boxes aren't in it.
        private static int _groundLayers;

        private Guard _guard;
        private NavMeshAgent _agent;
        // Optional: without it, only the guard's state turns the lock off.
        private GuardAnimation _animation;
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
        // How far the hips are lowered this frame (0 or below).
        private float _hipsOffset;

        private void Awake()
        {
            _guard = GetComponent<Guard>();
            _agent = GetComponent<NavMeshAgent>();
            _animation = GetComponent<GuardAnimation>();

            if (leftUpperLeg == null) leftUpperLeg = FindBone(HumanBodyBones.LeftUpperLeg);
            if (leftLowerLeg == null) leftLowerLeg = FindBone(HumanBodyBones.LeftLowerLeg);
            if (leftFoot == null) leftFoot = FindBone(HumanBodyBones.LeftFoot);
            if (rightUpperLeg == null) rightUpperLeg = FindBone(HumanBodyBones.RightUpperLeg);
            if (rightLowerLeg == null) rightLowerLeg = FindBone(HumanBodyBones.RightLowerLeg);
            if (rightFoot == null) rightFoot = FindBone(HumanBodyBones.RightFoot);
            if (leftToesEnd == null) leftToesEnd = FindToesEnd(HumanBodyBones.LeftToes);
            if (rightToesEnd == null) rightToesEnd = FindToesEnd(HumanBodyBones.RightToes);
            if (hips == null) hips = FindBone(HumanBodyBones.Hips);

            if (bodyRenderer == null) {
                bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            }

            _isValid = leftUpperLeg != null && leftLowerLeg != null && leftFoot != null
                && rightUpperLeg != null && rightLowerLeg != null && rightFoot != null
                && leftToesEnd != null && rightToesEnd != null && hips != null
                && bodyRenderer != null;

            if (!_isValid) {
                Debug.LogError($"GuardFootLock '{name}': leg bones or body renderer not found.", this);
                enabled = false;
                return;
            }

            if (_groundLayers == 0) {
                _groundLayers = LayerMask.GetMask("Environment", "Interactable");
            }

            _left = new Leg { Upper = leftUpperLeg, Lower = leftLowerLeg, Foot = leftFoot, ToesEnd = leftToesEnd };
            _right = new Leg { Upper = rightUpperLeg, Lower = rightLowerLeg, Foot = rightFoot, ToesEnd = rightToesEnd };
            _left.ToeDirection = Quaternion.Inverse(leftFoot.rotation) * (leftToesEnd.position - leftFoot.position);
            _right.ToeDirection = Quaternion.Inverse(rightFoot.rotation) * (rightToesEnd.position - rightFoot.position);
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
                // Nothing held across a spell unseen, in an action or down.
                Clear(_left);
                Clear(_right);
                _isStanding = false;
                _hipsOffset = 0f;
                TickUnseenStandingSteps();
                return;
            }

            _unseenYaw = transform.eulerAngles.y;
            _unseenTurned = 0f;

            // Hips first, so every foot below is posed from the lowered
            // body (the ground offsets are last plant's: a frame late).
            LowerHips(Time.deltaTime);

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
        /// sound a step nearly every frame. Nothing in an action or down.
        /// </summary>
        private void TickUnseenStandingSteps()
        {
            float yaw = transform.eulerAngles.y;
            float turned = Mathf.Abs(Mathf.DeltaAngle(_unseenYaw, yaw));
            _unseenYaw = yaw;

            if (IsOffFeet() || !IsStanding()) {
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
        /// holding its angle too; one that isn't is held where it is drawn,
        /// on the ground there (a step corrects it if that is far from
        /// home).
        /// </summary>
        private void StartStanding(Leg leg)
        {
            if (!leg.IsPlanted) {
                Vector3 drawn = leg.Foot.position - Vector3.up * _hipsOffset;
                FitGround(leg, drawn, leg.ToesEnd.position - Vector3.up * _hipsOffset, out float offset, out Quaternion tilt);
                leg.LockPosition = Vector3.Lerp(drawn + Vector3.up * offset, leg.LockPosition, leg.Weight);
                leg.GroundOffset = Mathf.Lerp(offset, leg.GroundOffset, leg.Weight);
                leg.GroundTilt = Quaternion.Slerp(tilt, leg.GroundTilt, leg.Weight);
                leg.IsPlanted = true;
            }

            leg.LockRotation = leg.GroundTilt * leg.Foot.rotation;
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
        /// Whichever is further out, distance along the ground or angle
        /// (measured with the held foot's ground tilt, so a slope alone
        /// doesn't count).
        /// </summary>
        private float HowFarOut(Leg leg)
        {
            Vector3 offset = leg.AnimatedPosition - leg.LockPosition;
            offset.y = 0f;
            float distance = offset.magnitude / stepDistance;
            float angle = Quaternion.Angle(leg.LockRotation, leg.GroundTilt * leg.Foot.rotation) / stepAngle;
            return Mathf.Max(distance, angle);
        }

        /// <summary>
        /// Lifts a held foot to step it home, finding the ground there as
        /// the step sets off (home moves on a little as the body turns; the
        /// ground under it is taken to be the same).
        /// </summary>
        private void BeginStep(Leg leg)
        {
            leg.IsStepping = true;
            leg.IsPlanted = false;
            leg.StepProgress = 0f;
            leg.StepFromPosition = leg.LockPosition;
            leg.StepFromRotation = leg.LockRotation;

            FitGround(leg, leg.AnimatedPosition - Vector3.up * _hipsOffset,
                leg.ToesEnd.position - Vector3.up * _hipsOffset, out float offset, out Quaternion tilt);
            leg.GroundOffset = offset;
            leg.GroundTilt = tilt;
        }

        /// <summary>
        /// Standing: puts the ankle where it is held, or along its step -
        /// eased from where it set off to its home (which moves as the
        /// body keeps turning), lifted in an arc - and plants it at the
        /// end of the step.
        /// </summary>
        private void PoseStanding(Leg leg, float deltaTime)
        {
            // Home on the ground found as the step set off: the drawn foot
            // (from the lowered body) taken back up by the hips' drop, then
            // moved to that ground and tilted to it.
            Vector3 home = leg.AnimatedPosition + Vector3.up * (leg.GroundOffset - _hipsOffset);
            Quaternion homeRotation = leg.GroundTilt * leg.Foot.rotation;
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
            SolveTwoBone(leg.Upper, leg.Lower, leg.Foot, target, rotation * leg.ToeDirection);
            leg.Foot.rotation = rotation;
        }

        /// <summary>
        /// Whether the clip, not stepping, has the feet: stunned, knocked
        /// out (limp or getting up), or GuardAnimation playing an action
        /// (the dive, the collapse). Looking about at a search place keeps
        /// the feet: the wide look-around swings the body round over them,
        /// so they are held and stepped as in any other turn on the spot.
        /// </summary>
        private bool IsOffFeet()
        {
            GuardState state = _guard.State;

            return state == GuardState.Stunned || state == GuardState.KnockedOut
                || (_animation != null && _animation.IsPlayingAction);
        }

        /// <summary>
        /// Whether the feet are locked this frame: the model drawn, near
        /// the camera, and the guard on its feet.
        /// </summary>
        private bool IsActive()
        {
            if (!bodyRenderer.isVisible || IsOffFeet()) {
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
            // Where the clip draws the ankle, before the hips were lowered:
            // its height over the root says whether it's down.
            Vector3 drawn = animated - Vector3.up * _hipsOffset;
            bool isDown = drawn.y - transform.position.y < plantHeight;

            if (!isDown) {
                leg.CanPlant = true;
            }

            if (isDown && leg.CanPlant && !leg.IsPlanted) {
                // Lock where the foot is drawn now, moved onto the ground
                // under it - or part way back from a lock still fading out.
                FitGround(leg, drawn, leg.ToesEnd.position - Vector3.up * _hipsOffset, out float offset, out Quaternion tilt);
                leg.LockPosition = Vector3.Lerp(drawn + Vector3.up * offset, leg.LockPosition, leg.Weight);
                leg.GroundOffset = Mathf.Lerp(offset, leg.GroundOffset, leg.Weight);
                leg.GroundTilt = Quaternion.Slerp(tilt, leg.GroundTilt, leg.Weight);
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
            // The foot keeps the angle the animation gave it, tilted to the
            // ground as far as it is locked.
            Quaternion footRotation = Quaternion.Slerp(Quaternion.identity, leg.GroundTilt, leg.Weight) * leg.Foot.rotation;
            SolveTwoBone(leg.Upper, leg.Lower, leg.Foot, target, footRotation * leg.ToeDirection);
            leg.Foot.rotation = footRotation;
        }

        /// <summary>
        /// Forgets a foot's lock and the ground under it.
        /// </summary>
        private static void Clear(Leg leg)
        {
            leg.IsPlanted = false;
            leg.CanPlant = true;
            leg.IsStepping = false;
            leg.Weight = 0f;
            leg.GroundOffset = 0f;
            leg.GroundTilt = Quaternion.identity;
            leg.HasHeelGround = false;
            leg.HasToeGround = false;
        }

        /// <summary>
        /// Lowers the hips by the lower foot's ground below the root (up to
        /// maxHipsDrop), eased: a foot on lower ground couldn't otherwise
        /// reach it, the leg drawn nearly straight as it plants. Never
        /// raised: a foot on higher ground just bends its knee. The
        /// Animator rewrites the hips each frame, so this never adds up.
        /// </summary>
        private void LowerHips(float deltaTime)
        {
            float wanted = Mathf.Clamp(Mathf.Min(_left.GroundOffset, _right.GroundOffset), -maxHipsDrop, 0f);
            float ease = hipsSmoothing > 0f ? 1f - Mathf.Exp(-deltaTime / hipsSmoothing) : 1f;
            _hipsOffset = Mathf.Lerp(_hipsOffset, wanted, ease);
            hips.position += Vector3.up * _hipsOffset;
        }

        /// <summary>
        /// Finds the ground under a foot drawn at ankle (toe tip at toe),
        /// both as the clip draws them over flat ground at the root's
        /// height: how far up the ankle moves (offset) and the tilt to lay
        /// over the foot's animated rotation. The ankle goes onto the
        /// ground under the heel (the ankle sits over it) and the foot
        /// tilts to that ground's slope; if the toes would then be under
        /// the ground under them they tip up onto it, and whatever
        /// maxFootTilt leaves lifts the foot. No ground under the heel: the
        /// foot stays as drawn.
        /// </summary>
        private void FitGround(Leg leg, Vector3 ankle, Vector3 toe, out float offset, out Quaternion tilt)
        {
            float rootHeight = transform.position.y;
            offset = 0f;
            tilt = Quaternion.identity;
            leg.HasToeGround = false;
            leg.HasHeelGround = CastDown(ankle, rootHeight, out RaycastHit heelHit);

            if (!leg.HasHeelGround) {
                return;
            }

            leg.HeelGround = heelHit.point;
            offset = heelHit.point.y - rootHeight;
            tilt = Quaternion.RotateTowards(Quaternion.identity,
                Quaternion.FromToRotation(Vector3.up, heelHit.normal), maxFootTilt);

            leg.HasToeGround = CastDown(toe, rootHeight, out RaycastHit toeHit);

            if (!leg.HasToeGround) {
                return;
            }

            leg.ToeGround = toeHit.point;
            // How far the toe tip, tilted with the foot, is under where it
            // should be: its drawn height over the ground under it.
            Vector3 toeOffset = tilt * (toe - ankle);
            float wantedHeight = toe.y + (toeHit.point.y - rootHeight);
            float sunk = wantedHeight - (ankle.y + offset + toeOffset.y);

            if (sunk <= 0f) {
                return;
            }

            // Tip the toes up about the foot's side, as far as the tilt
            // left allows (about forward x up: positive raises the toes).
            Vector3 along = new Vector3(toeOffset.x, 0f, toeOffset.z);
            float length = along.magnitude;

            if (length > 0.01f) {
                float tiltLeft = Mathf.Max(0f, maxFootTilt - Quaternion.Angle(Quaternion.identity, tilt));
                float pitch = Mathf.Min(Mathf.Asin(Mathf.Clamp01(sunk / length)) * Mathf.Rad2Deg, tiltLeft);
                Quaternion raise = Quaternion.AngleAxis(pitch, Vector3.Cross(along / length, Vector3.up));
                tilt = raise * tilt;
                sunk = wantedHeight - (ankle.y + offset + (raise * toeOffset).y);
            }

            if (sunk > 0f) {
                offset += sunk;
            }
        }

        /// <summary>
        /// A ray straight down through point, from groundSearch above the
        /// root to groundSearch below it, onto the ground layers.
        /// </summary>
        private bool CastDown(Vector3 point, float rootHeight, out RaycastHit hit)
        {
            Vector3 origin = new Vector3(point.x, rootHeight + groundSearch, point.z);
            return Physics.Raycast(origin, Vector3.down, out hit, groundSearch * 2f, _groundLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Two-bone IK: turns the thigh and the shin so the ankle reaches
        /// target (or as near as a straight leg gets), the knee bending
        /// only towards the toes (toeDirection, ankle to toe tip of the
        /// foot as it will be held).
        /// </summary>
        private void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 toeDirection)
        {
            Vector3 a = upper.position;
            Vector3 c = end.position;
            Vector3 line = (c - a).normalized;

            // The way the knee may point: the toes' direction, square to
            // the hip-ankle line (a knee goes out over its toes). The
            // animated knee points where the clip's body faces; with a
            // foot held while the body turns over it (looking about, a turn
            // on the spot) that can be well off the toes, and bent that way
            // the legs bow out. So the thigh is first turned about the
            // hip-ankle line - the ankle stays put - until the knee lies
            // over the toes; a straight leg (no knee offset to measure)
            // just bends that way below.
            Vector3 kneeForward = toeDirection - Vector3.Dot(toeDirection, line) * line;

            if (kneeForward.sqrMagnitude < 1e-8f) {
                kneeForward = transform.forward - Vector3.Dot(transform.forward, line) * line;
            }

            kneeForward.Normalize();
            Vector3 kneeNow = (lower.position - a) - Vector3.Dot(lower.position - a, line) * line;

            if (kneeNow.sqrMagnitude > 1e-8f) {
                float twist = Vector3.SignedAngle(kneeNow, kneeForward, line);
                upper.rotation = Quaternion.AngleAxis(twist, line) * upper.rotation;
            }

            Vector3 b = lower.position;

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
            // the ankle and the toes' way. Fixed by the toes, not the
            // animated knee, so it never flips: the walk straightens a leg
            // fully at the back of its stride, where the animated knee
            // wobbles either side of the hip-ankle line by a fraction of a
            // millimetre, and its plane alone would bend the knee one way,
            // then the other.
            Vector3 bendAxis = Vector3.Cross(line, kneeForward).normalized;

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
            lines.Color = _groundColor;

            if (leg.HasHeelGround) {
                lines.WireSphere(leg.HeelGround, 0.015f);
            }

            if (leg.HasToeGround) {
                lines.WireSphere(leg.ToeGround, 0.015f);
            }

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

        /// <summary>
        /// Finds the tip of the toes: the end bone under the Humanoid toe
        /// bone (a rig has no Humanoid bone for it), or the toe bone itself
        /// if it has no child.
        /// </summary>
        private Transform FindToesEnd(HumanBodyBones toes)
        {
            Transform toeBone = FindBone(toes);

            if (toeBone == null) {
                return null;
            }

            return toeBone.childCount > 0 ? toeBone.GetChild(0) : toeBone;
        }
    }
}
