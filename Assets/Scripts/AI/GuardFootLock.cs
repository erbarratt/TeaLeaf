using Core;
using UnityEngine;

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
    /// Runs in LateUpdate, after the Animator has posed the bones and
    /// the agent has moved the root, and only while the model is drawn
    /// and near the camera: with CullUpdateTransforms the Animator
    /// doesn't pose an unseen model, and bending its legs again would
    /// add up frame on frame. Off while the guard is stunned or knocked
    /// out (those clips aren't stepping). The foot keeps its animated
    /// rotation; no rays, so the foot isn't fitted to slopes or steps.
    ///
    /// Gizmo (also in the headset): a ball where each locked foot is
    /// held, green while planted and yellow while the lock fades out,
    /// with a line to where the animation put the foot - the length of
    /// that line is the slide the lock is taking out.
    /// </summary>
    [RequireComponent(typeof(Guard))]
    public class GuardFootLock : MonoBehaviour, IDebugDrawable
    {
        // The leg bones, found under the guard by name if not assigned.
        [SerializeField] private Transform leftUpperLeg;
        [SerializeField] private Transform leftLowerLeg;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightUpperLeg;
        [SerializeField] private Transform rightLowerLeg;
        [SerializeField] private Transform rightFoot;

        // The model's skinned mesh: the lock only runs while it is drawn.
        // Found in the children if not assigned.
        [SerializeField] private Renderer bodyRenderer;

        // An animated ankle lower than this above the root is planted.
        // The clips' planted ankles sit at 0.11-0.13m; a lifted one is
        // above 0.135 within a frame or two.
        [SerializeField] private float plantHeight = 0.135f;

        // How long a lifted foot takes to blend back to the animation.
        [SerializeField] private float releaseTime = 0.12f;

        // How far a locked foot may be from the animated one before the
        // lock lets go.
        [SerializeField] private float maxSlip = 0.15f;

        // Beyond this distance from the camera the feet aren't locked.
        [SerializeField] private float maxDistance = 20f;

        private class Leg
        {
            public Transform Upper;
            public Transform Lower;
            public Transform Foot;
            // Where the foot is held, in world space.
            public Vector3 LockPosition;
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

        private Guard _guard;
        private Leg _left;
        private Leg _right;
        private Transform _camera;
        private bool _isValid;

        private void Awake()
        {
            _guard = GetComponent<Guard>();

            if (leftUpperLeg == null) leftUpperLeg = FindBone("Left_UpperLeg");
            if (leftLowerLeg == null) leftLowerLeg = FindBone("Left_LowerLeg");
            if (leftFoot == null) leftFoot = FindBone("Left_Foot");
            if (rightUpperLeg == null) rightUpperLeg = FindBone("Right_UpperLeg");
            if (rightLowerLeg == null) rightLowerLeg = FindBone("Right_LowerLeg");
            if (rightFoot == null) rightFoot = FindBone("Right_Foot");

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
                return;
            }

            float fade = releaseTime > 0f ? Time.deltaTime / releaseTime : 1f;
            Tick(_left, fade);
            Tick(_right, fade);
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

            DrawLeg(lines, _left);
            DrawLeg(lines, _right);
        }

        /// <summary>
        /// Draws one foot's lock, if it has one.
        /// </summary>
        private static void DrawLeg(DebugLines lines, Leg leg)
        {
            if (leg.Weight <= 0f) {
                return;
            }

            lines.Color = leg.IsPlanted ? _plantedColor : _releasingColor;
            lines.WireSphere(leg.LockPosition, 0.06f);
            lines.Line(leg.LockPosition, leg.AnimatedPosition);
        }

        /// <summary>
        /// Finds a bone under the guard by name.
        /// </summary>
        private Transform FindBone(string boneName)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);

            foreach (Transform child in children) {
                if (child.name == boneName) {
                    return child;
                }
            }

            return null;
        }
    }
}
