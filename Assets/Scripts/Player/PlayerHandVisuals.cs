using System;
using System.Collections.Generic;
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
        [Tooltip("The left hand model (Left Hand Visual, a child of the tracked Left Hand), not the controller itself. e.g. Hands/Left Hand/Left Hand Visual. Filled in by name when this component is added; must be set.")]
        [SerializeField] private Transform leftHandVisual;
        [Tooltip("The right hand model (Right Hand Visual, the mirrored copy), not the controller itself. e.g. Hands/Right Hand/Right Hand Visual. Filled in by name when this component is added; must be set.")]
        [SerializeField] private Transform rightHandVisual;

        // Seconds for a hand visual to blend onto its snap pose when grabbing
        // (and back to the controller on release) - short enough to feel
        // instant, long enough not to pop.
        [Tooltip("Seconds for a hand to blend onto what it grabs (a ledge, a handle) and back to the controller on release. e.g. 0.08 = feels instant without popping. Higher = a slower, visible slide onto the grip; 0 = jumps straight there.")]
        [SerializeField] private float snapBlendDuration = 0.08f;

        [Header("Physical Hands")]

        // What the hands stop at. Should be Environment + Interactable only:
        // never Player (the hands start inside the body's capsule) or
        // Climbable (grab volumes, not surfaces). Nothing is selected by
        // default, which simply turns hand collision off.
        [Tooltip("Layers the hands stop at instead of passing through. Should be Environment + Interactable: the hand stops on a wall and pushes an open door. Never Player, Climbable or PlayerHands; Nothing turns hand collision off.")]
        [SerializeField] private LayerMask collisionLayers;

        // Sizes of the pieces each hand collides with (palm box, thumb and
        // finger capsules) that the bones don't give - see HandShapeFitter.
        // Read once, in Awake().
        [Tooltip("Sizes of each hand's collision pieces the bones don't give (palm thickness, finger radii), read once at load. e.g. a bigger finger radius makes the fingers stop further from a wall.")]
        [SerializeField] private HandShapeSizes handShapes = new();

        // Where the trigger and grip that curl the fingers come from: the
        // fingers' collision switches between open and fist with them. Found
        // on the Player root if empty.
        [Tooltip("The input system on the Player root: the trigger and grip switch each finger's collision between open and fist. Empty looks for it on a parent; none = fingers always collide open.")]
        [SerializeField] private PlayerInputXR playerInput;

        // Gap kept between the hand and a surface, in metres, so the next
        // frame's sweep doesn't start already touching it.
        [Tooltip("Gap kept between a hand and the surface it stops at, in metres, so the next sweep doesn't start touching it. e.g. 0.005 = 5mm. Higher = hands stop visibly short of walls; lower = they may catch and stutter.")]
        [SerializeField] private float skinWidth = 0.005f;

        // Seconds for a hand visual to ease back onto its controller once a
        // surface stops holding it (the "elastic band"). Short enough to feel
        // attached, long enough that the return doesn't pop. 0 = instant.
        [Tooltip("Seconds for a hand to ease back onto its controller once a surface stops holding it (the elastic band). e.g. 0.1 = a quick catch-up. Higher = a lazier return; 0 = instant.")]
        [SerializeField] private float catchUpDuration = 0.1f;

        // How far, in metres, a surface may hold a hand visual away from its
        // controller before it gives up and snaps straight back - reaching
        // well into or through something. 0 = never snap back.
        [Tooltip("How far a surface may hold a hand away from its controller before it gives up and goes straight back through, in metres. e.g. 0.4 = reaching 40cm into a wall snaps the hand back to the controller. Higher = the hand stays held longer; 0 = never.")]
        [SerializeField] private float maxSeparation = 0.4f;

        // Seconds a hand must have gone without pushing anything (an open
        // door) before its next push counts as a new touch - see
        // HandContactStarted.
        [Tooltip("Seconds a hand must go without pushing anything (an open door) before its next push counts as a new touch with a haptic tap. e.g. 0.5 = pushing a door along steadily taps once. Lower = more taps.")]
        [SerializeField] private float pushRestTime = 0.5f;

        [Header("Ghost Hands")]

        // Whether a faint ghost hand shows at the real controller while a
        // surface holds the hand visual away from it (see HandGhost). A
        // likely player setting later, like PlayerHaptics.HapticsEnabled.
        [Tooltip("On: a faint ghost hand shows where the real controller is while a wall holds the hand visual off it. Off: no ghost.")]
        [SerializeField] private bool showGhostHands = true;

        // How far, in metres, the visual must be held from the controller
        // before the ghost appears (it hides again within half this), so
        // just brushing a surface doesn't flash it up.
        [Tooltip("How far the hand must be held off its controller before the ghost shows, in metres (it hides again within half this). e.g. 0.03 = brushing a wall shows no ghost, pressing into it does. Higher = only deep pushes show it.")]
        [SerializeField] private float ghostShowDistance = 0.03f;

        // The ghost's flat colour; keep the alpha low. Read once, in Awake().
        [Tooltip("The ghost hand's flat colour, read once at load; keep the alpha low. e.g. white at 0.2 alpha = a faint outline. Higher alpha = a more solid ghost.")]
        [SerializeField] private Color ghostColor = new(1f, 1f, 1f, 0.2f);

        // Debug shape colours (gizmos and in-headset): where the controller
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

        // The Edit Mode gizmo's pieces, measured from the bones each time
        // it's drawn - see DrawDebug().
        private HandShapeGeometry[] _editModeShapes;

        // Time.time each hand last pushed something. Start well in the
        // past, so the first push counts.
        private float _leftLastPushTime = -10f;
        private float _rightLastPushTime = -10f;

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
        /// Adds a prop one hand is carrying to its collision shape - a piece
        /// per collider - so the prop stops at surfaces with the hand:
        /// handPosition and handRotation are where the visual holds it from,
        /// the colliders measured as they lie now. See
        /// HandPhysicalFollow.SetHeldShape().
        /// </summary>
        public void SetHeldShape(bool isLeftHand, IReadOnlyList<Collider> colliders, Vector3 handPosition, Quaternion handRotation)
        {
            (isLeftHand ? _leftFollow : _rightFollow).SetHeldShape(colliders, handPosition, handRotation);
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
        /// debug capsule turns red) or starts pushing something out of its
        /// way (an open door), with true for the left hand - e.g. for
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
        /// hand visuals under this object by name and the player's input, so
        /// they don't need dragging in by hand.
        /// </summary>
        private void Reset()
        {
            leftHandVisual = transform.Find("Left Hand/Left Hand Visual");
            rightHandVisual = transform.Find("Right Hand/Right Hand Visual");
            playerInput = GetComponentInParent<PlayerInputXR>();
        }

        private void Awake()
        {
            if (playerInput == null) {
                playerInput = GetComponentInParent<PlayerInputXR>();
            }

            // Created once here (capturing each visual's rest pose and
            // measuring the hand's collision pieces from its bones), never
            // per frame, so neither allocates.
            LeftVisualSnap = new HandVisualSnap(leftHandVisual, snapBlendDuration);
            RightVisualSnap = new HandVisualSnap(rightHandVisual, snapBlendDuration);
            _leftFollow = new HandPhysicalFollow(leftHandVisual, handShapes);
            _rightFollow = new HandPhysicalFollow(rightHandVisual, handShapes);

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
            // Each follow's parked colliders live at the scene root, so they
            // aren't destroyed along with this object. Null if Awake() never ran.
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

            // The same for pushing something out of the way (an open
            // door): that hand never counts as in contact, but touching
            // the door should be felt all the same. A pushed door swings
            // ahead of the hand and is caught up with again, so "started"
            // means no push for pushRestTime, not just none last frame.
            float lastPushTime = isLeftHand ? _leftLastPushTime : _rightLastPushTime;

            // Timed under its own name in the Profiler (search for it in the
            // CPU module's Hierarchy view), so the physical hands' cost can
            // be read directly. Auto() returns a struct that ends the sample
            // when the using block closes - no allocation, and compiled out
            // of non-development builds.
            using (_physicalFollowMarker.Auto()) {
                // The fingers collide open or curled into a fist as the hand
                // Animator shows them. No input (PC Play without it): open.
                if (playerInput != null) {
                    follow.SetFingerCurl(
                        isLeftHand ? playerInput.LeftTrigger : playerInput.RightTrigger,
                        isLeftHand ? playerInput.LeftGrip : playerInput.RightGrip);
                }

                follow.Tick(collisionLayers, skinWidth, catchUpDuration, maxSeparation, Time.deltaTime);
            }

            bool startedPushing = false;

            if (follow.IsPushing) {
                float now = Time.time;
                startedPushing = now - lastPushTime > pushRestTime;

                if (isLeftHand) {
                    _leftLastPushTime = now;
                } else {
                    _rightLastPushTime = now;
                }
            }

            if ((!wasInContact && follow.IsInContact) || startedPushing) {
                HandContactStarted?.Invoke(isLeftHand);
            }

            // After the follow, so it measures where the visual ended up.
            ghost.Tick(showGhostHands, ghostShowDistance);
        }

        /// <summary>
        /// While selected, draws each hand's collision pieces in the Scene
        /// view - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// Draws each hand's collision pieces - in the Scene view's gizmos
        /// and, while InHeadsetGizmos is on, in the headset. In Play Mode:
        /// where the controller wants the hand (faint) and where the visual
        /// actually is (green, red while a surface holds it back), with a
        /// carried prop's pieces and the fingers in the pose they collide in.
        /// In Edit Mode: the open hand's pieces from the bones, for checking
        /// handShapes against the model. The same either way, so detailed is
        /// ignored.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (Application.isPlaying && _leftFollow != null) {
                _leftFollow.DrawDebug(lines, _gizmoTargetColor, _leftFollow.IsInContact ? _gizmoContactColor : _gizmoFreeColor);
                _rightFollow.DrawDebug(lines, _gizmoTargetColor, _rightFollow.IsInContact ? _gizmoContactColor : _gizmoFreeColor);
                return;
            }

            lines.Color = _gizmoFreeColor;
            DrawEditModeShapes(lines, leftHandVisual);
            DrawEditModeShapes(lines, rightHandVisual);
        }

        /// <summary>
        /// Draws one hand's open pieces, measured from its bones as they are
        /// now (editor only, so measuring each time doesn't matter). Skipped
        /// if the visual or a bone is missing.
        /// </summary>
        private void DrawEditModeShapes(DebugLines lines, Transform visual)
        {
            if (visual == null || visual.parent == null || handShapes == null) {
                return;
            }

            _editModeShapes ??= new HandShapeGeometry[HandShapeFitter.ShapeCount];

            if (!HandShapeFitter.FitOpen(visual, handShapes, _editModeShapes)) {
                return;
            }

            Quaternion controllerRotation = visual.parent.rotation;

            for (int i = 0; i < _editModeShapes.Length; i++) {
                _editModeShapes[i].Draw(lines, visual.position, controllerRotation);
            }
        }
    }
}
