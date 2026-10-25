using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A door's handle: what a hand ray targets and a hand snaps onto, like
    /// a ledge. One handle serves both sides of the door - its grab volume
    /// goes right through the door and sticks out of both faces, and the
    /// hand snaps onto the lever on whichever side the player's head is.
    ///
    /// This object sits where the handle's spindle passes through the
    /// door, in the middle of the door's thickness, with the same axes as
    /// the door: X along the door (towards the edge away from the hinge),
    /// Y up, Z out of the door's front face. The lever on each side sticks
    /// out standOff along Z and runs along X; the hand grips it gripAlong
    /// from the spindle.
    ///
    /// The handle only describes itself - where a hand goes, and showing
    /// the lever turned. Reading the hand's twist and freeing the door is
    /// Player.PlayerHandDoors' job; the Door owns its lock and latch.
    ///
    /// A fixed handle (turns off) is a pull fixed to the door, a bar
    /// standing upright: nothing turns, and taking hold frees the latch
    /// at once. The hand grips it thumb up, gripAlong along X still
    /// placing it (usually 0, the bar straight out from this object).
    ///
    /// Set up: a trigger BoxCollider on the Interactable layer, a child of
    /// the Door, unscaled, sticking further out of each face than anything
    /// solid so hand rays reach it first.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class DoorHandle : MonoBehaviour, IHandTarget, IHandSnapTarget, IDebugDrawable
    {
        [Tooltip("The door this handle opens. Left empty, the Door on a parent object is used.")]
        [SerializeField] private Door door;

        [Tooltip("The part shown turning with the hand: the levers on both faces, as children of one object on the spindle, turned about its local Z. Optional: empty, nothing visible turns.")]
        [SerializeField] private Transform lever;

        [Tooltip("On: a lever turned by the wrist to free the latch. Off: a fixed pull, an upright bar that doesn't turn - taking hold of it frees the latch at once, and the hand grips it thumb up.")]
        [SerializeField] private bool turns = true;

        [Header("Grip")]

        [Tooltip("The hand offsets and finger pose for a hand on the lever, shared by every door handle. Empty, the hand sits exactly on the grip frame with the default pose.")]
        [SerializeField] private HandSnapProfile snapProfile;

        [Tooltip("How far the lever's own axis stands out from this object (the middle of the door's thickness) along Z, in metres. The same on both faces.")]
        [SerializeField] private float standOff = 0.075f;

        [Tooltip("Where along the lever the hand grips, in metres along local X from the spindle. Negative is towards the hinge, the way a lever normally points.")]
        [SerializeField] private float gripAlong = -0.07f;

        [Tooltip("The lever's radius, in metres. The grip frame sits on the lever's surface, this far out from its axis on the player's side.")]
        [SerializeField] private float gripRadius = 0.011f;

        // The layer the handle must be on - see OnValidate().
        private const string InteractableLayerName = "Interactable";

        private static readonly Color _gizmoBoxColor = new(0.3f, 0.8f, 1f, 0.35f);
        private static readonly Color _gizmoGripColor = new(0.3f, 0.8f, 1f, 1f);

        private BoxCollider _boxCollider;

        // The lever's local rotation at rest, which a turn is added to.
        private Quaternion _leverRestRotation;
        private bool _hasLever;

        /// The door this handle opens.
        public Door Door => door;

        /// True for a lever turned by the wrist; false for a fixed pull,
        /// which frees the latch as soon as it's taken.
        public bool Turns => turns;

        /// The hand offsets and finger pose shared by handles like this.
        public HandSnapProfile SnapProfile => snapProfile;

        /// How far the lever is shown turned from rest, in degrees about
        /// the spindle (local Z).
        public float LeverAngle { get; private set; }

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();

            if (door == null) {
                door = GetComponentInParent<Door>();
            }

            _hasLever = lever != null;

            if (_hasLever) {
                _leverRestRotation = lever.localRotation;
            }
        }

        private void OnEnable()
        {
            HandTargetRegistry.Register(_boxCollider, this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            HandTargetRegistry.Unregister(_boxCollider);
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// Editor-only: warns about a handle set up as something solid, or
        /// on a layer hand rays may not hit. Clicking the warning selects
        /// the handle.
        /// </summary>
        private void OnValidate()
        {
            BoxCollider box = GetComponent<BoxCollider>();

            if (box != null && !box.isTrigger) {
                Debug.LogWarning(
                    $"DoorHandle '{name}': its BoxCollider isn't a trigger. It's a grab volume, not " +
                    "geometry - tick Is Trigger; the lever the player sees needs no collider.",
                    this);
            }

            int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);

            if (interactableLayer >= 0 && gameObject.layer != interactableLayer) {
                Debug.LogWarning($"DoorHandle '{name}': not on the {InteractableLayerName} layer, so hand rays may miss it.", this);
            }
        }

        /// <summary>
        /// Whether worldPoint is on the door's front side (its +Z face) -
        /// asked about the player's head, to choose which lever a hand
        /// takes.
        /// </summary>
        public bool IsInFront(Vector3 worldPoint)
        {
            return Vector3.Dot(worldPoint - transform.position, transform.forward) >= 0f;
        }

        /// <summary>
        /// The middle of the part of the lever a hand grips, on the front
        /// or the back of the door - for measuring how far the real hand
        /// has strayed from it.
        /// </summary>
        public Vector3 GetGripPoint(bool isFront)
        {
            Vector3 local = new(gripAlong, 0f, isFront ? standOff : -standOff);
            return transform.position + transform.rotation * local;
        }

        /// <summary>
        /// The hand snap pose for a hand taking the handle now: on the
        /// side the player's head is on, with the lever as it's currently
        /// turned. grabPoint and headForward aren't needed.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint, Vector3 headPosition, Vector3 headForward)
        {
            return GetSnapPose(isLeftHand, IsInFront(headPosition), LeverAngle);
        }

        /// <summary>
        /// The hand snap pose for a hand on the lever on one side of the
        /// door, with the lever turned leverAngle degrees (0 for a fixed
        /// handle). Called every frame the handle is held, since the
        /// handle moves with the door and turns with the hand.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, bool isFront, float leverAngle)
        {
            GetGripFrame(isLeftHand, isFront, out Vector3 gripPosition, out Quaternion gripRotation);
            Vector3 spindle = transform.position;

            // Without a profile, still snap to the bare grip frame so a
            // missing reference is obvious (hand badly offset), not a crash.
            HandSnapPose pose = snapProfile == null
                ? new HandSnapPose(gripPosition, gripRotation, default)
                : snapProfile.Apply(isLeftHand, gripPosition, gripRotation);

            // The lever turns about the spindle, and the hand on it goes
            // round with it: turn the whole pose about the spindle's axis.
            Quaternion turn = Quaternion.AngleAxis(leverAngle, transform.forward);
            return new HandSnapPose(spindle + turn * (pose.Position - spindle), turn * pose.Rotation, pose.Profile);
        }

        /// <summary>
        /// The grip frame on one side of the door with the lever at rest,
        /// before the profile's offsets - what a pose is measured from (the
        /// game, and Pose with Hand).
        ///
        /// A hand held up flat towards the door, then closed round the bar:
        /// it sits on the bar's surface on the player's side, facing into
        /// the door, with its up along the bar towards that hand's thumb.
        /// On a lever the thumb points across the player's body - to their
        /// left for the right hand, their right for the left - which is
        /// opposite ways along the door from its two sides. On a fixed
        /// pull the bar stands upright and the thumb points up.
        /// </summary>
        public void GetGripFrame(bool isLeftHand, bool isFront, out Vector3 position, out Quaternion rotation)
        {
            transform.GetPositionAndRotation(out Vector3 spindle, out Quaternion handleRotation);

            Vector3 outwards = handleRotation * (isFront ? Vector3.forward : Vector3.back);
            Vector3 alongDoor = handleRotation * Vector3.right;

            // Seen from the front, the player's left is the door's +X.
            Vector3 thumb = !turns ? handleRotation * Vector3.up
                : isLeftHand == isFront ? -alongDoor : alongDoor;

            position = spindle + alongDoor * gripAlong + outwards * (standOff + gripRadius);
            rotation = Quaternion.LookRotation(-outwards, thumb);
        }

        /// <summary>
        /// Shows the lever turned angle degrees from rest about the
        /// spindle. Only touches the transform when the angle changes.
        /// </summary>
        public void SetLeverAngle(float angle)
        {
            if (angle == LeverAngle) {
                return;
            }

            LeverAngle = angle;

            if (_hasLever) {
                lever.localRotation = Quaternion.AngleAxis(angle, Vector3.forward) * _leverRestRotation;
            }
        }

        /// <summary>
        /// Always draws the grab volume in the Scene view - see
        /// DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// The grab volume's outline, faintly; in the detailed view also
        /// the grip point on each face, joined through the spindle, for
        /// checking standOff and gripAlong against the lever model.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            // _boxCollider is only cached by Awake(), which hasn't run in
            // the editor.
            BoxCollider box = _boxCollider != null ? _boxCollider : GetComponent<BoxCollider>();

            if (box == null) {
                return;
            }

            lines.Matrix = transform.localToWorldMatrix;
            lines.Color = _gizmoBoxColor;
            lines.WireCube(box.center, box.size);
            lines.Matrix = Matrix4x4.identity;

            if (!detailed) {
                return;
            }

            Vector3 front = GetGripPoint(true);
            Vector3 back = GetGripPoint(false);

            lines.Color = _gizmoGripColor;
            lines.WireSphere(front, gripRadius * 2f);
            lines.WireSphere(back, gripRadius * 2f);
            lines.Line(front, transform.position + transform.forward * standOff);
            lines.Line(back, transform.position - transform.forward * standOff);
            lines.Line(transform.position + transform.forward * standOff, transform.position - transform.forward * standOff);
        }
    }
}
