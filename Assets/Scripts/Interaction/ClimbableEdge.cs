using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Marks a designer-placed BoxCollider as a climbable edge - the target
    /// hand rays hit. A hand grabs the edge by holding grip while its ray
    /// (and reticle) is on it. It needs no mesh: the collider is invisible in
    /// game and shown in the Scene view by gizmos. Add a renderer per edge
    /// only if that edge should be visible.
    ///
    /// This class only knows about itself: its own collider and where a hand
    /// snaps onto it. Ray targeting is PlayerHandInteraction's job, and
    /// grabbing is PlayerClimbing's.
    ///
    /// Orientation convention - place every edge so that its local X runs
    /// along the edge, +Y is up, and +Z points out from the wall towards the
    /// player. The lip a hand curls over is therefore the box's top-front
    /// line (top face, +Z face).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class ClimbableEdge : MonoBehaviour, IHandTarget, IClimbable, IDebugDrawable
    {
        // Shared hand offsets/pose for all ledges - see HandSnapProfile.
        [SerializeField] private HandSnapProfile snapProfile;

        [Header("Mantling")]

        // Whether the player can mantle up onto this edge (see
        // PlayerMantling). Only tick this on level edges - mantling assumes
        // the edge and its top face are horizontal.
        [SerializeField] private bool isMantleable;

        // Whether a mantle onto this edge ends with the player crouched -
        // tick it for low shelves and vents with no room to stand.
        [SerializeField] private bool mantleEndsCrouched;

        // Where a mantle always lands the bottom of the player's capsule
        // (their feet), wherever along the edge it started. In metres along
        // this edge's own axes (X along the edge, Y up, Z out from the wall),
        // from its origin - position and rotation apply, scale doesn't, so
        // the numbers stay real distances on a stretched cube. Filled in by
        // Reset() / the "Reset Mantle Point" context menu to sit on the top
        // face, DefaultMantleInset in from the lip.
        [SerializeField] private Vector3 mantlePoint = new(0f, 0.1f, -0.2f);

        // Ticked (the default, and how every mantle worked before this
        // option): a mantle always lands exactly on mantlePoint. Unticked:
        // it lands straight ahead of the player instead - at mantlePoint's
        // height and distance back from the lip, but wherever along the edge
        // the player is (see GetMantleLanding()). For long ledges like roof
        // parapets and walls, where being carried sideways to one fixed spot
        // would feel wrong.
        [SerializeField] private bool moveHorizontallyToPoint = true;

        // How far in from the lip the default mantle point is, in metres.
        private const float DefaultMantleInset = 0.4f;

        // The layer every edge must be on - see OnValidate().
        private const string ClimbableLayerName = "Climbable";

        // Gizmo colours: the box faint, its lip (the top-front line a hand
        // curls over) bright, so which way round the edge faces is obvious.
        private static readonly Color _gizmoBoxColor = new(1f, 0.6f, 0.1f, 0.35f);
        private static readonly Color _gizmoLipColor = new(1f, 0.6f, 0.1f, 1f);

        private BoxCollider _boxCollider;

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
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

        /// Whether the player can mantle onto this edge - see isMantleable.
        public bool IsMantleable => isMantleable;

        /// Whether a mantle onto this edge ends crouched.
        public bool MantleEndsCrouched => mantleEndsCrouched;

        /// World position a mantle lands the player's feet at - mantlePoint
        /// rotated and offset by this edge's transform, but not scaled (see
        /// mantlePoint), which is why this isn't TransformPoint().
        public Vector3 MantlePointWorld => transform.position + transform.rotation * mantlePoint;

        /// <summary>
        /// World position a mantle lands the feet at, when it starts with
        /// them at feetPosition. With moveHorizontallyToPoint ticked, always
        /// MantlePointWorld. Unticked, the same height and distance back from
        /// the lip, but slid along the edge to straight in front of
        /// feetPosition (where it is along the edge, measured square-on),
        /// never past either end. Only called when a mantle starts.
        /// </summary>
        public Vector3 GetMantleLanding(Vector3 feetPosition)
        {
            if (moveHorizontallyToPoint) {
                return MantlePointWorld;
            }

            // mantlePoint's frame: position and rotation, not scale.
            Vector3 local = Quaternion.Inverse(transform.rotation) * (feetPosition - transform.position);
            GetLipExtent(_boxCollider, out float lipStart, out float lipEnd);
            float along = Mathf.Clamp(local.x, lipStart, lipEnd);
            return transform.position + transform.rotation * new Vector3(along, mantlePoint.y, mantlePoint.z);
        }

        /// <summary>
        /// Where the lip starts and ends along the edge (its X), in real
        /// metres in mantlePoint's unscaled frame: the box's X extent times
        /// the scale, lowest first.
        /// </summary>
        private void GetLipExtent(BoxCollider box, out float lipStart, out float lipEnd)
        {
            float scaleX = transform.lossyScale.x;
            float a = (box.center.x - box.size.x * 0.5f) * scaleX;
            float b = (box.center.x + box.size.x * 0.5f) * scaleX;
            lipStart = Mathf.Min(a, b);
            lipEnd = Mathf.Max(a, b);
        }

        /// <summary>
        /// Editor-only: runs when the component is first added. Fills in a
        /// sensible default mantle point for this box.
        /// </summary>
        private void Reset()
        {
            ResetMantlePoint();
        }

        /// <summary>
        /// Editor-only: runs when the scene loads and whenever this component
        /// changes in the Inspector. Warns about an edge set up as solid
        /// geometry rather than a grab volume - easy to do by adding this
        /// straight onto a cube, and silent otherwise: the Climbable layer
        /// collides with nothing, so hands and the player's body pass straight
        /// through it. Clicking the warning selects the edge.
        /// </summary>
        private void OnValidate()
        {
            BoxCollider box = GetComponent<BoxCollider>();

            if (box != null && !box.isTrigger) {
                Debug.LogWarning(
                    $"ClimbableEdge '{name}': its BoxCollider isn't a trigger. An edge is a grab volume, " +
                    "not geometry - tick Is Trigger, and make the ledge itself a separate solid object " +
                    "on the Environment layer, with this box a little larger so hand rays hit it first.",
                    this);
            }

            int climbableLayer = LayerMask.NameToLayer(ClimbableLayerName);

            if (climbableLayer >= 0 && gameObject.layer != climbableLayer) {
                Debug.LogWarning(
                    $"ClimbableEdge '{name}': not on the {ClimbableLayerName} layer, so hand rays may miss " +
                    "it or it may collide with things it shouldn't.",
                    this);
            }
        }

        /// <summary>
        /// Puts the mantle point on this box's top face, DefaultMantleInset
        /// in from the lip, centred along the edge. The box's collider size
        /// is multiplied by the transform's scale here to get real metres,
        /// since mantlePoint itself is unscaled. Also available from the
        /// component's right-click menu, for edges placed before this
        /// existed or after resizing one.
        /// </summary>
        [ContextMenu("Reset Mantle Point")]
        private void ResetMantlePoint()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            Vector3 scale = transform.lossyScale;

            float along = box.center.x * scale.x;
            float top = (box.center.y + box.size.y * 0.5f) * scale.y;
            float lip = (box.center.z + box.size.z * 0.5f) * scale.z;

#if UNITY_EDITOR
            // Lets the change be undone, and marks the object dirty so it's
            // actually saved - a context menu edit isn't otherwise tracked.
            UnityEditor.Undo.RecordObject(this, "Reset Mantle Point");
#endif

            mantlePoint = new Vector3(along, top, lip - DefaultMantleInset);
        }

        /// <summary>
        /// Always draws the edge in the Scene view, so edges are visible
        /// while laying out a level even though they have no mesh - see
        /// DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// While this edge is selected, draws it again with its mantle
        /// landing marker - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// Draws the edge - as Scene view gizmos and, while InHeadsetGizmos
        /// is on, in the headset: the box's outline faintly and its lip
        /// brightly, in the box's own (scaled) local space so it matches the
        /// collider exactly. With detailed on (selected), a mantleable edge
        /// also marks where a mantle lands: a sphere at the feet and a line
        /// up to roughly head height (lower when the mantle ends crouched) -
        /// or, when it lands straight ahead of the player
        /// (moveHorizontallyToPoint off), a line along the edge where the
        /// feet can land, with a head-height line at each end.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            // _boxCollider is only cached by Awake(), which hasn't run in
            // the editor.
            BoxCollider box = _boxCollider != null ? _boxCollider : GetComponent<BoxCollider>();

            if (box == null) {
                return;
            }

            Vector3 centre = box.center;
            Vector3 halfSize = box.size * 0.5f;

            lines.Matrix = transform.localToWorldMatrix;

            lines.Color = _gizmoBoxColor;
            lines.WireCube(centre, box.size);

            lines.Color = _gizmoLipColor;
            Vector3 lipCentre = new(centre.x, centre.y + halfSize.y, centre.z + halfSize.z);
            Vector3 alongLip = new(halfSize.x, 0f, 0f);
            lines.Line(lipCentre - alongLip, lipCentre + alongLip);

            lines.Matrix = Matrix4x4.identity;

            if (!detailed || !isMantleable) {
                return;
            }

            float headHeight = mantleEndsCrouched ? 1f : 1.8f;
            lines.Color = mantleEndsCrouched ? Color.yellow : Color.green;

            if (moveHorizontallyToPoint) {
                Vector3 feet = MantlePointWorld;
                lines.WireSphere(feet, 0.1f);
                lines.Line(feet, feet + Vector3.up * headHeight);
                return;
            }

            // Lands anywhere along the edge: a line where the feet can land,
            // with the head-height marker at each end.
            GetLipExtent(box, out float lipStart, out float lipEnd);
            Vector3 startFeet = transform.position + transform.rotation * new Vector3(lipStart, mantlePoint.y, mantlePoint.z);
            Vector3 endFeet = transform.position + transform.rotation * new Vector3(lipEnd, mantlePoint.y, mantlePoint.z);
            lines.Line(startFeet, endFeet);
            lines.Line(startFeet, startFeet + Vector3.up * headHeight);
            lines.Line(endFeet, endFeet + Vector3.up * headHeight);
        }

        /// <summary>
        /// The point on this edge's lip (its top-front line) nearest to
        /// worldPoint, clamped so it never goes past either end of the edge.
        /// Used both to snap a grabbing hand and to find where a mantle lands.
        /// </summary>
        public Vector3 ClosestLipPoint(Vector3 worldPoint)
        {
            // Work in the box's local space, where the lip is a simple
            // axis-aligned line. BoxCollider.center/size are local values,
            // so this is correct however the cube is scaled or rotated.
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 centre = _boxCollider.center;
            Vector3 halfSize = _boxCollider.size * 0.5f;

            // Slide along the edge to wherever the point is, but never past
            // either end; then pin to the top face and the front face.
            local.x = Mathf.Clamp(local.x, centre.x - halfSize.x, centre.x + halfSize.x);
            local.y = centre.y + halfSize.y;
            local.z = centre.z + halfSize.z;

            return transform.TransformPoint(local);
        }

        /// <summary>
        /// Snaps a grabbing hand onto this edge's lip: the point on the
        /// top-front line nearest to where the ray hit, facing into the wall
        /// with the grip frame's up matching the edge's up. The profile then
        /// offsets that into the hand visual's actual root pose.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint)
        {
            Vector3 gripPosition = ClosestLipPoint(grabPoint);

            // Grip frame: forward points into the wall (-Z), up is the edge's
            // up - the direction the back of a palm-down hand faces.
            Quaternion gripRotation = Quaternion.LookRotation(-transform.forward, transform.up);

            // Without a profile, still snap to the bare grip frame so a
            // missing reference is obvious (hand badly offset), not a crash.
            // == null rather than "is null": in the editor Unity can store an
            // unassigned serialized reference as a "fake null" object that
            // only its overloaded == operator treats as null.
            if (snapProfile == null) {
                return new HandSnapPose(gripPosition, gripRotation, HandPose.LedgeGrip);
            }

            return snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }
    }
}
