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
    /// Set up: a trigger BoxCollider on the Interactable layer, a child of
    /// the Door, unscaled, sticking further out of each face than anything
    /// solid so hand rays reach it first.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class DoorHandle : MonoBehaviour, IHandTarget, IHandSnapTarget, IDebugDrawable
    {
        // The door this handle opens. Left empty, found on a parent.
        [SerializeField] private Door door;

        // The part that turns with the hand: the levers on both faces, as
        // children of one object on the spindle. Optional.
        [SerializeField] private Transform lever;

        [Header("Grip")]

        // Shared hand offsets and finger pose for every door handle - see
        // HandSnapProfile.
        [SerializeField] private HandSnapProfile snapProfile;

        // How far the lever's own axis is out from this object (the middle
        // of the door's thickness), in metres - the same on both faces.
        [SerializeField] private float standOff = 0.075f;

        // Where along the lever the hand grips, in metres along local X
        // from the spindle. Negative = towards the hinge, the way a lever
        // normally points.
        [SerializeField] private float gripAlong = -0.07f;

        // The lever's radius, in metres: the grip frame sits on its
        // surface, on the player's side.
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
        /// door, with the lever turned leverAngle degrees. Called every
        /// frame the handle is held, since the handle moves with the door
        /// and turns with the hand.
        ///
        /// The grip frame is a hand held up flat towards the door, then
        /// closed round the bar: it sits on the lever's surface on the
        /// player's side, facing into the door, with its up along the
        /// lever towards that hand's thumb. The thumb points across the
        /// player's body - to their left for the right hand, their right
        /// for the left - which is opposite ways along the door from its
        /// two sides.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, bool isFront, float leverAngle)
        {
            transform.GetPositionAndRotation(out Vector3 spindle, out Quaternion rotation);

            Vector3 outwards = rotation * (isFront ? Vector3.forward : Vector3.back);
            Vector3 alongDoor = rotation * Vector3.right;

            // Seen from the front, the player's left is the door's +X.
            Vector3 thumb = isLeftHand == isFront ? -alongDoor : alongDoor;

            Vector3 gripPosition = spindle + alongDoor * gripAlong + outwards * (standOff + gripRadius);
            Quaternion gripRotation = Quaternion.LookRotation(-outwards, thumb);

            // Without a profile, still snap to the bare grip frame so a
            // missing reference is obvious (hand badly offset), not a crash.
            HandSnapPose pose = snapProfile == null
                ? new HandSnapPose(gripPosition, gripRotation, default)
                : snapProfile.Apply(isLeftHand, gripPosition, gripRotation);

            // The lever turns about the spindle, and the hand on it goes
            // round with it: turn the whole pose about the spindle's axis.
            Quaternion turn = Quaternion.AngleAxis(leverAngle, rotation * Vector3.forward);
            return new HandSnapPose(spindle + turn * (pose.Position - spindle), turn * pose.Rotation, pose.Pose);
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
