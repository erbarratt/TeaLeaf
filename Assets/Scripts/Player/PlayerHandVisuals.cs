using System;
using Core;
using Unity.Profiling;
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
    /// It also owns each hand's HandGhost: a faint copy at the real
    /// controller, shown while the physical follow holds the visual away.
    ///
    /// Lives on the Hands object with the other hand systems. No Update() of
    /// its own: PlayerController calls Tick() after Move() and turning - see
    /// Tick().
    /// </summary>
    public class PlayerHandVisuals : MonoBehaviour, IDebugDrawable
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

        // Seconds for a hand visual to ease back onto its controller once a
        // surface stops holding it (the "elastic band"). Short enough to feel
        // attached, long enough that the return doesn't pop. 0 = instant.
        [SerializeField] private float catchUpDuration = 0.1f;

        // How far, in metres, a surface may hold a hand visual away from its
        // controller before it gives up and snaps straight back - reaching
        // well into or through something. 0 = never snap back.
        [SerializeField] private float maxSeparation = 0.4f;

        [Header("Ghost Hands")]

        // Whether a faint ghost hand shows at the real controller while a
        // surface holds the hand visual away from it (see HandGhost). A
        // likely player setting later, like PlayerHaptics.HapticsEnabled.
        [SerializeField] private bool showGhostHands = true;

        // How far, in metres, the visual must be held from the controller
        // before the ghost appears (it hides again within half this), so
        // just brushing a surface doesn't flash it up.
        [SerializeField] private float ghostShowDistance = 0.03f;

        // The ghost's flat colour; keep the alpha low. Read once, in Awake().
        [SerializeField] private Color ghostColor = new(1f, 1f, 1f, 0.2f);

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

        // Debug capsule colours (gizmos and in-headset): where the controller
        // wants the hand, where the visual is, and the visual while a surface
        // is holding it back.
        private static readonly Color _gizmoTargetColor = new(1f, 1f, 1f, 0.3f);
        private static readonly Color _gizmoFreeColor = new(0.3f, 1f, 0.4f, 1f);
        private static readonly Color _gizmoContactColor = new(1f, 0.3f, 0.2f, 1f);

        // Profiler label for one hand's physical follow tick - see TickHand().
        private static readonly ProfilerMarker _physicalFollowMarker = new("PhysicalHands.Follow");

        // TeaLeaf/Ghost - see FindGhostShader().
        private const string GhostShaderName = "TeaLeaf/Ghost";

        private HandPhysicalFollow _leftFollow;
        private HandPhysicalFollow _rightFollow;
        private HandGhost _leftGhost;
        private HandGhost _rightGhost;
        private Material _ghostMaterial;

        /// Blends the left hand visual onto a snap pose and back. Other
        /// systems call Snap()/Release() on it (e.g. PlayerClimbing on grab)
        /// and read its Weight/SnapPose (e.g. PlayerHandAnimation for the
        /// finger pose); only this class ticks it.
        public HandVisualSnap LeftVisualSnap { get; private set; }

        /// The right hand's snap - mirrors LeftVisualSnap.
        public HandVisualSnap RightVisualSnap { get; private set; }

        /// The hand model transforms, for systems that follow where a hand
        /// is drawn (a carried prop). Read only - this class places them.
        public Transform LeftHandVisual => leftHandVisual;
        public Transform RightHandVisual => rightHandVisual;

        /// <summary>
        /// Enlarges one hand's collision shape to cover a prop it's
        /// carrying, so the prop stops at surfaces with the hand:
        /// centreFromVisual is the middle of the prop relative to the hand
        /// visual (in the visual's own axes), radius its size. See
        /// HandPhysicalFollow.SetHeldShape().
        /// </summary>
        public void SetHeldShape(bool isLeftHand, Vector3 centreFromVisual, float radius)
        {
            (isLeftHand ? _leftFollow : _rightFollow).SetHeldShape(centreFromVisual, radius);
        }

        /// <summary>
        /// Puts one hand's collision shape back to just the hand.
        /// </summary>
        public void ClearHeldShape(bool isLeftHand)
        {
            (isLeftHand ? _leftFollow : _rightFollow)?.ClearHeldShape();
        }

        /// True while a surface is holding the left hand visual back from its
        /// controller - e.g. for a haptic pulse or debugging.
        public bool IsLeftHandInContact => _leftFollow.IsInContact;

        /// True while a surface is holding the right hand visual back.
        public bool IsRightHandInContact => _rightFollow.IsInContact;

        /// Raised on the frame a hand visual starts touching a surface (its
        /// debug capsule turns red), with true for the left hand - e.g. for
        /// PlayerHaptics' contact tap. Not raised by snapping onto a grab
        /// target. A plain C# event, invoked from inside Tick().
        public event Action<bool> HandContactStarted;

        /// <summary>
        /// The left controller's pose, shifted to where its hand visual
        /// actually is while a surface holds it back - what the left hand ray
        /// is cast from. See HandPhysicalFollow.GetHandPose().
        /// </summary>
        public void GetLeftHandPose(out Vector3 position, out Quaternion rotation)
        {
            _leftFollow.GetHandPose(out position, out rotation);
        }

        /// <summary>
        /// The right hand's version of GetLeftHandPose().
        /// </summary>
        public void GetRightHandPose(out Vector3 position, out Quaternion rotation)
        {
            _rightFollow.GetHandPose(out position, out rotation);
        }

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

            // One material shared by both ghosts. The ghosts copy the visuals,
            // so this must run while the visuals are still on their
            // controllers at their rest pose - true in Awake().
            _ghostMaterial = new Material(FindGhostShader()) { color = ghostColor };
            _leftGhost = new HandGhost(leftHandVisual, _ghostMaterial);
            _rightGhost = new HandGhost(rightHandVisual, _ghostMaterial);
        }

        /// <summary>
        /// The TeaLeaf/Ghost shader (Art/Shaders/Resources, so it's in builds
        /// and Shader.Find() works on the Quest too), or Unity's pink error
        /// shader with an error logged if it's missing.
        /// </summary>
        private Shader FindGhostShader()
        {
            Shader shader = Shader.Find(GhostShaderName);

            if (shader == null) {
                Debug.LogError($"PlayerHandVisuals: shader '{GhostShaderName}' not found - ghost hands will be pink.", this);
                shader = Shader.Find("Hidden/InternalErrorShader");
            }

            return shader;
        }

        private void OnEnable()
        {
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            DebugDrawRegistry.Unregister(this);
        }

        private void OnDestroy()
        {
            // Each follow's penetration collider lives at the scene root, so it
            // isn't destroyed along with this object. Null if Awake() never ran.
            _leftFollow?.Destroy();
            _rightFollow?.Destroy();

            // A material made in code isn't cleaned up with the scene. The
            // ghost objects themselves are children of the controllers, so
            // they go with the rig.
            if (_ghostMaterial != null) {
                Destroy(_ghostMaterial);
            }
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
            TickHand(LeftVisualSnap, _leftFollow, _leftGhost, true);
            TickHand(RightVisualSnap, _rightFollow, _rightGhost, false);
        }

        /// <summary>
        /// Runs one hand: its snap first, then - only if the snap isn't using
        /// the visual (not snapped, and fully blended back) - its physical
        /// follow. Snapping always wins: a hand gripping a ledge sits exactly
        /// on the ledge, even if the controller is inside the wall.
        /// </summary>
        private void TickHand(HandVisualSnap snap, HandPhysicalFollow follow, HandGhost ghost, bool isLeftHand)
        {
            snap.Tick(Time.deltaTime);

            if (snap.IsSnapped || snap.Weight > 0f) {
                follow.Suspend();
                // A snapped hand is meant to be off the controller (it's on
                // the ledge), so no ghost.
                ghost.Tick(false, ghostShowDistance);
                return;
            }

            // Contact only counts as "started" going from free to touching
            // within a tick. A snapped hand's follow is suspended, which
            // clears contact without raising anything.
            bool wasInContact = follow.IsInContact;

            // Timed under its own name in the Profiler (search for it in the
            // CPU module's Hierarchy view), so the physical hands' cost can
            // be read directly. Auto() returns a struct that ends the sample
            // when the using block closes - no allocation, and compiled out
            // of non-development builds.
            using (_physicalFollowMarker.Auto()) {
                follow.Tick(collisionLayers, handRadius, skinWidth, catchUpDuration, maxSeparation, Time.deltaTime);
            }

            if (!wasInContact && follow.IsInContact) {
                HandContactStarted?.Invoke(isLeftHand);
            }

            // After the follow, so it measures where the visual ended up.
            ghost.Tick(showGhostHands, ghostShowDistance);
        }

        /// <summary>
        /// While selected, draws each hand's collision capsule in the Scene
        /// view - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// Draws each hand's collision capsule - in the Scene view's gizmos
        /// and, while InHeadsetGizmos is on, in the headset. In Play Mode:
        /// where the controller wants the hand (faint) and where the visual
        /// actually is (green, red while a surface holds it back). In Edit
        /// Mode: the capsule between the bones, for checking handRadius
        /// against the model. The same either way, so detailed is ignored.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (Application.isPlaying && _leftFollow != null) {
                DrawFollowCapsules(lines, _leftFollow);
                DrawFollowCapsules(lines, _rightFollow);
                return;
            }

            lines.Color = _gizmoFreeColor;
            DrawBoneCapsule(lines, leftWristBone, leftFingertipBone);
            DrawBoneCapsule(lines, rightWristBone, rightFingertipBone);
        }

        /// <summary>
        /// Draws one hand's target and actual capsules in Play Mode.
        /// </summary>
        private void DrawFollowCapsules(DebugLines lines, HandPhysicalFollow follow)
        {
            follow.GetDebugCapsules(
                handRadius,
                out Vector3 targetWrist, out Vector3 targetFingertip,
                out Vector3 visualWrist, out Vector3 visualFingertip);

            // Bigger than the hand's while it carries a prop.
            float radius = follow.ShapeRadius(handRadius);

            lines.Color = _gizmoTargetColor;
            lines.WireCapsule(targetWrist, targetFingertip, radius);

            lines.Color = follow.IsInContact ? _gizmoContactColor : _gizmoFreeColor;
            lines.WireCapsule(visualWrist, visualFingertip, radius);
        }

        /// <summary>
        /// Draws the capsule between two bones as the sweep would build it
        /// (ends pulled in by the radius). Skipped if a bone is missing.
        /// </summary>
        private void DrawBoneCapsule(DebugLines lines, Transform wrist, Transform fingertip)
        {
            if (wrist == null || fingertip == null) {
                return;
            }

            Vector3 a = wrist.position;
            Vector3 b = fingertip.position;
            Vector3 axis = b - a;
            float inset = Mathf.Min(handRadius, axis.magnitude * 0.5f);
            Vector3 step = axis.normalized * inset;

            lines.WireCapsule(a + step, b - step, handRadius);
        }
    }
}
