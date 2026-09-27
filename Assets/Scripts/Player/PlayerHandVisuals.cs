using UnityEngine;

namespace Player
{
    /// <summary>
    /// The single owner of where each hand VISUAL (the hand model) is placed -
    /// never the tracked controller, which always follows real tracking.
    /// Anything that wants to put a hand visual somewhere other than on its
    /// controller goes through here, so two systems can never fight over the
    /// same transform.
    ///
    /// Two things move a visual, in priority order:
    /// - Snapping (HandVisualSnap): the hand blended onto a grab target's
    ///   HandSnapPose, requested by PlayerClimbing and later by grabbing and
    ///   tools. While a hand is snapped or blending, it owns that visual.
    /// - Physical follow (HandPhysicalFollow): otherwise, the visual follows
    ///   its controller but stops at surfaces instead of passing through them.
    ///
    /// Lives on the Hands object with the other hand systems. No Update() of
    /// its own: PlayerController calls Tick() after Move() and turning - see
    /// Tick().
    /// </summary>
    public class PlayerHandVisuals : MonoBehaviour
    {
        // The hand model transforms (children of the tracked Left/Right Hand
        // controllers), NOT the controllers themselves.
        [SerializeField] private Transform leftHandVisual;
        [SerializeField] private Transform rightHandVisual;

        // Seconds for a hand visual to blend onto its snap pose when grabbing
        // (and back to the controller on release) - short enough to feel
        // instant, long enough not to pop.
        [SerializeField] private float snapBlendDuration = 0.08f;

        [Header("Physical Hands")]

        // What the hands stop at. Should be Environment + Interactable only:
        // never Player (the hands start inside the body's capsule) or
        // Climbable (grab volumes, not surfaces). Nothing is selected by
        // default, which simply turns hand collision off.
        [SerializeField] private LayerMask collisionLayers;

        // Radius of each hand's capsule, in metres. A capsule is round and a
        // hand is flat, so this is a compromise between the hand's width
        // (~0.09m) and thickness (~0.03m).
        [SerializeField] private float handRadius = 0.035f;

        // Gap kept between the hand and a surface, in metres, so the next
        // frame's sweep doesn't start already touching it.
        [SerializeField] private float skinWidth = 0.005f;

        // The bones the hand capsule is measured between (wrist to the tip of
        // the middle finger) - once, at Awake, at the bind pose. Filled in by
        // Reset() or the "Find Hand Bones" context menu.
        [SerializeField] private Transform leftWristBone;
        [SerializeField] private Transform leftFingertipBone;
        [SerializeField] private Transform rightWristBone;
        [SerializeField] private Transform rightFingertipBone;

        // Bone names in hand.fbx - the right hand is the same model mirrored,
        // so its bones have the same (Left) names.
        private const string WristBoneName = "J_Left_Hand";
        private const string FingertipBoneName = "J_Left_HandMiddle4";

        // Gizmo colours: where the controller wants the hand, where the visual
        // is, and the visual while a surface is holding it back.
        private static readonly Color _gizmoTargetColor = new(1f, 1f, 1f, 0.3f);
        private static readonly Color _gizmoFreeColor = new(0.3f, 1f, 0.4f, 1f);
        private static readonly Color _gizmoContactColor = new(1f, 0.3f, 0.2f, 1f);

        private HandPhysicalFollow _leftFollow;
        private HandPhysicalFollow _rightFollow;

        /// Blends the left hand visual onto a snap pose and back. Other
        /// systems call Snap()/Release() on it (e.g. PlayerClimbing on grab)
        /// and read its Weight/SnapPose (e.g. PlayerHandAnimation for the
        /// finger pose); only this class ticks it.
        public HandVisualSnap LeftVisualSnap { get; private set; }

        /// The right hand's snap - mirrors LeftVisualSnap.
        public HandVisualSnap RightVisualSnap { get; private set; }

        /// True while a surface is holding the left hand visual back from its
        /// controller - e.g. for a haptic pulse or debugging.
        public bool IsLeftHandInContact => _leftFollow.IsInContact;

        /// True while a surface is holding the right hand visual back.
        public bool IsRightHandInContact => _rightFollow.IsInContact;

        /// <summary>
        /// Editor-only: runs when the component is first added. Finds the two
        /// hand visuals and their bones under this object by name, so they
        /// don't need dragging in by hand.
        /// </summary>
        private void Reset()
        {
            leftHandVisual = transform.Find("Left Hand/Left Hand Visual");
            rightHandVisual = transform.Find("Right Hand/Right Hand Visual");
            FindHandBones();
        }

        /// <summary>
        /// Fills in the wrist and fingertip bones of both hand visuals by name.
        /// Also on the component's right-click menu, since Reset() only runs
        /// when the component is first added.
        /// </summary>
        [ContextMenu("Find Hand Bones")]
        private void FindHandBones()
        {
#if UNITY_EDITOR
            // Lets the change be undone, and marks the object dirty so it's
            // actually saved - a context menu edit isn't otherwise tracked.
            UnityEditor.Undo.RecordObject(this, "Find Hand Bones");
#endif

            leftWristBone = FindChildByName(leftHandVisual, WristBoneName);
            leftFingertipBone = FindChildByName(leftHandVisual, FingertipBoneName);
            rightWristBone = FindChildByName(rightHandVisual, WristBoneName);
            rightFingertipBone = FindChildByName(rightHandVisual, FingertipBoneName);
        }

        /// <summary>
        /// Searches everything under root (at any depth) for a transform
        /// called name. Editor-only use, so the allocation doesn't matter.
        /// </summary>
        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) {
                return null;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
                if (child.name == name) {
                    return child;
                }
            }

            return null;
        }

        private void Awake()
        {
            // Created once here (capturing each visual's rest pose and the
            // hand capsule), never per frame, so neither allocates.
            LeftVisualSnap = new HandVisualSnap(leftHandVisual, snapBlendDuration);
            RightVisualSnap = new HandVisualSnap(rightHandVisual, snapBlendDuration);
            _leftFollow = new HandPhysicalFollow(leftHandVisual, leftWristBone, leftFingertipBone);
            _rightFollow = new HandPhysicalFollow(rightHandVisual, rightWristBone, rightFingertipBone);
        }

        /// <summary>
        /// Places both hand visuals for this frame (snapped, blending, or
        /// following the controller). Called by PlayerController after Move()
        /// and turning: the visuals are children of the rig, so placing them
        /// any earlier would let this frame's movement drag a world-space
        /// snap pose away until next frame - and the physical sweep needs the
        /// controller's final position this frame.
        /// </summary>
        public void Tick()
        {
            TickHand(LeftVisualSnap, _leftFollow);
            TickHand(RightVisualSnap, _rightFollow);
        }

        /// <summary>
        /// Runs one hand: its snap first, then - only if the snap isn't using
        /// the visual (not snapped, and fully blended back) - its physical
        /// follow. Snapping always wins: a hand gripping a ledge sits exactly
        /// on the ledge, even if the controller is inside the wall.
        /// </summary>
        private void TickHand(HandVisualSnap snap, HandPhysicalFollow follow)
        {
            snap.Tick(Time.deltaTime);

            if (snap.IsSnapped || snap.Weight > 0f) {
                follow.Suspend();
                return;
            }

            follow.Tick(collisionLayers, handRadius, skinWidth);
        }

        /// <summary>
        /// While selected, draws each hand's collision capsule. In Play Mode:
        /// where the controller wants the hand (faint) and where the visual
        /// actually is (green, red while a surface holds it back). In Edit
        /// Mode: the capsule between the bones, for checking handRadius
        /// against the model. Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying && _leftFollow != null) {
                DrawFollowGizmos(_leftFollow);
                DrawFollowGizmos(_rightFollow);
                return;
            }

            Gizmos.color = _gizmoFreeColor;
            DrawBoneCapsule(leftWristBone, leftFingertipBone);
            DrawBoneCapsule(rightWristBone, rightFingertipBone);
        }

        /// <summary>
        /// Draws one hand's target and actual capsules in Play Mode.
        /// </summary>
        private void DrawFollowGizmos(HandPhysicalFollow follow)
        {
            follow.GetDebugCapsules(
                handRadius,
                out Vector3 targetWrist, out Vector3 targetFingertip,
                out Vector3 visualWrist, out Vector3 visualFingertip);

            Gizmos.color = _gizmoTargetColor;
            DrawWireCapsule(targetWrist, targetFingertip, handRadius);

            Gizmos.color = follow.IsInContact ? _gizmoContactColor : _gizmoFreeColor;
            DrawWireCapsule(visualWrist, visualFingertip, handRadius);
        }

        /// <summary>
        /// Draws the capsule between two bones as the sweep would build it
        /// (ends pulled in by the radius). Skipped if a bone is missing.
        /// </summary>
        private void DrawBoneCapsule(Transform wrist, Transform fingertip)
        {
            if (wrist == null || fingertip == null) {
                return;
            }

            Vector3 a = wrist.position;
            Vector3 b = fingertip.position;
            Vector3 axis = b - a;
            float inset = Mathf.Min(handRadius, axis.magnitude * 0.5f);
            Vector3 step = axis.normalized * inset;

            DrawWireCapsule(a + step, b - step, handRadius);
        }

        /// <summary>
        /// Gizmos has no capsule, so this draws one from a sphere at each end
        /// and four lines down its sides.
        /// </summary>
        private static void DrawWireCapsule(Vector3 a, Vector3 b, float radius)
        {
            Gizmos.DrawWireSphere(a, radius);
            Gizmos.DrawWireSphere(b, radius);

            Vector3 axis = b - a;

            if (axis.sqrMagnitude < 0.000001f) {
                return;
            }

            // Two directions at right angles to the capsule's axis, for the
            // side lines.
            Vector3 side = Vector3.Cross(axis, Vector3.up);

            if (side.sqrMagnitude < 0.000001f) {
                side = Vector3.Cross(axis, Vector3.right);
            }

            side = side.normalized * radius;
            Vector3 other = Vector3.Cross(axis.normalized, side);

            Gizmos.DrawLine(a + side, b + side);
            Gizmos.DrawLine(a - side, b - side);
            Gizmos.DrawLine(a + other, b + other);
            Gizmos.DrawLine(a - other, b - other);
        }
    }
}
