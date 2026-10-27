using Player;
using UnityEngine;
using UnityEngine.AI;

namespace AI
{
    /// <summary>
    /// Turns a guard's neck and head after the Animator has posed them,
    /// as the shared GuardHeadLook asset says for its state: the clip as
    /// it is, straight ahead of the body (no turn to the side, the
    /// clip's nod kept), or towards the stimulus or the player.
    ///
    /// The gaze is measured by the Guard's eye (on the head bone,
    /// forward = gaze) as a yaw and pitch from the body's facing. Each
    /// frame the rule gives the yaw and pitch wanted; the aim held here
    /// moves towards it at turnSpeed, so a change of rule never snaps
    /// the head; and the neck then the head are turned about the body's
    /// up (yaw) and the gaze's right (pitch) by the difference between
    /// the clip's gaze and the aim - the neck its share, the head the
    /// rest. With no rule the aim eases back onto the clip's gaze; once
    /// there, nothing is turned until a rule starts again.
    ///
    /// The eye rides the head, so the guard's sight follows the turned
    /// head, not the clip's. Runs whether or not the model is drawn:
    /// the turn is worked out from the pose each frame, so on a culled
    /// model (bones not rewritten) it finds the head already aimed and
    /// turns nothing more. Off while knocked out (limp or standing up:
    /// the ragdoll owns the bones).
    /// </summary>
    [RequireComponent(typeof(Guard))]
    [RequireComponent(typeof(NavMeshAgent))]
    [DefaultExecutionOrder(50)]
    public class GuardHeadAim : MonoBehaviour
    {
        // The rules, shared by every guard.
        [Tooltip("The head rules for each state, e.g. Assets/Data/AI/GuardHeadLook.asset. Empty = the head always plays the clip (this does nothing).")]
        [SerializeField] private GuardHeadLook look;

        // Neck and head, found through the model's Humanoid rig if not
        // assigned.
        [Tooltip("Neck bone, turned by the neck's share of each turn. Left empty, the Humanoid rig's neck is found; with none, the head turns alone.")]
        [SerializeField] private Transform neck;
        [Tooltip("Head bone, turned by the rest of each turn (and the eye on it). Left empty, the Humanoid rig's head is found; with none, head aiming logs an error and switches off.")]
        [SerializeField] private Transform head;

        // A gaze this close to the clip's, in degrees, has eased back
        // onto it: the clip then plays untouched.
        private const float SettledAngle = 0.5f;

        private Guard _guard;
        private NavMeshAgent _agent;
        private Transform _eye;

        // The aim held, as yaw and pitch from the body's facing in
        // degrees, and whether it is being applied at all.
        private float _aimYaw;
        private float _aimPitch;
        private bool _isAiming;

        private void Awake()
        {
            _guard = GetComponent<Guard>();
            _agent = GetComponent<NavMeshAgent>();
            _eye = _guard.Eye;

            Animator animator = GetComponentInChildren<Animator>(true);

            if (animator != null && animator.isHuman) {
                if (neck == null) neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                if (head == null) head = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (head == null || _eye == null) {
                Debug.LogError($"GuardHeadAim '{name}': head bone or the Guard's eye not found.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (look == null || _guard.IsKnockedOut) {
                _isAiming = false;
                return;
            }

            GetGaze(out float clipYaw, out float clipPitch);

            // What the rule wants; no rule = the clip's own gaze.
            float wantYaw = clipYaw;
            float wantPitch = clipPitch;
            bool hasRule = GetWanted(clipPitch, ref wantYaw, ref wantPitch);

            if (!_isAiming) {
                if (!hasRule) {
                    return;
                }

                // Starting: the aim begins where the clip has the head.
                _aimYaw = clipYaw;
                _aimPitch = clipPitch;
                _isAiming = true;
            }

            float step = look.TurnSpeed * Time.deltaTime;
            _aimYaw = Mathf.MoveTowardsAngle(_aimYaw, wantYaw, step);
            _aimPitch = Mathf.MoveTowards(_aimPitch, wantPitch, step);

            // Back on the clip with no rule: let it play untouched.
            if (!hasRule && Mathf.Abs(Mathf.DeltaAngle(_aimYaw, clipYaw)) < SettledAngle
                && Mathf.Abs(_aimPitch - clipPitch) < SettledAngle) {
                _isAiming = false;
                return;
            }

            if (neck != null) {
                TurnBone(neck, look.NeckShare);
            }

            TurnBone(head, 1f);
        }

        /// <summary>
        /// The yaw and pitch this state's rule wants (pitch kept as the
        /// clip's for Forward). False if no rule applies, or a Look At
        /// has nothing to look at: the clip plays.
        /// </summary>
        private bool GetWanted(float clipPitch, ref float yaw, ref float pitch)
        {
            Vector3 velocity = _agent.velocity;
            bool isStanding = velocity.sqrMagnitude < Guard.StandingSpeed * Guard.StandingSpeed;

            if (!look.TryGetRule(_guard.State, isStanding, out GuardHeadLook.Rule rule)) {
                return false;
            }

            switch (rule.mode) {
                case GuardHeadLook.Mode.Forward:
                    yaw = 0f;
                    pitch = clipPitch;
                    return true;
                case GuardHeadLook.Mode.LookAt:
                    Vector3 target;

                    if (rule.target == GuardHeadLook.Target.Player) {
                        PlayerVisibility player = PlayerVisibility.Instance;

                        if (player == null) {
                            return false;
                        }

                        target = player.HeadPosition;
                    } else {
                        target = _guard.Stimulus + Vector3.up * look.LookHeight;
                    }

                    ToYawPitch(target - _eye.position, out yaw, out pitch);
                    yaw = Mathf.Clamp(yaw, -look.MaxYaw, look.MaxYaw);
                    pitch = Mathf.Clamp(pitch, -look.MaxPitchDown, look.MaxPitchUp);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Turns a bone so the gaze moves the given share of the way from
        /// where it is now to the aim: yaw about the body's up, then
        /// pitch about the gaze's right. Turning the neck carries the
        /// head and eye with it, so the head (share 1) then takes up
        /// whatever is left.
        /// </summary>
        private void TurnBone(Transform bone, float share)
        {
            GetGaze(out float yaw, out float pitch);
            float yawTurn = Mathf.DeltaAngle(yaw, _aimYaw) * share;
            float pitchTurn = (_aimPitch - pitch) * share;

            Vector3 up = transform.up;
            Quaternion yawRotation = Quaternion.AngleAxis(yawTurn, up);
            Vector3 right = Vector3.Cross(up, yawRotation * _eye.forward);

            // Looking straight up or down there is no right to tip about.
            if (right.sqrMagnitude < 1e-6f) {
                bone.rotation = yawRotation * bone.rotation;
                return;
            }

            // A turn about the right tips the gaze down, so raising the
            // pitch turns the other way.
            Quaternion pitchRotation = Quaternion.AngleAxis(-pitchTurn, right.normalized);
            bone.rotation = pitchRotation * yawRotation * bone.rotation;
        }

        /// <summary>
        /// The eye's gaze as yaw (right positive) and pitch (up positive)
        /// in degrees from the body's facing.
        /// </summary>
        private void GetGaze(out float yaw, out float pitch)
        {
            ToYawPitch(_eye.forward, out yaw, out pitch);
        }

        /// <summary>
        /// A world direction as yaw and pitch in degrees from the body's
        /// facing.
        /// </summary>
        private void ToYawPitch(Vector3 direction, out float yaw, out float pitch)
        {
            Vector3 local = transform.InverseTransformDirection(direction).normalized;
            yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
