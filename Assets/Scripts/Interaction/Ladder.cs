using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A climbable ladder. One BoxCollider covers the whole ladder and is the
    /// hand-ray target, exactly like ClimbableEdge - there are no per-rung
    /// colliders, and no mesh is needed (gizmos show it in the Scene view;
    /// add a renderer per ladder only if it should be visible). The rungs are pure
    /// designer data (first rung height + spacing), so a grab just snaps the
    /// hand to whichever rung is nearest the ray hit. That keeps a ladder to
    /// a single collider however tall it is.
    ///
    /// Climbing it is PlayerClimbing's job, and it moves the player just like
    /// a ledge (the pull isn't locked to the ladder's axis). Getting off the
    /// top is a mantle: either the ladder's own (isMantleable - the arrow
    /// shows with a hand on the top rung, landing on mantlePoint) or a
    /// mantleable ClimbableEdge placed on the lip above it. Getting off the
    /// bottom is letting go.
    ///
    /// Orientation convention (matches ClimbableEdge) - local X runs across
    /// the ladder (along the rungs), +Y is up the ladder, and +Z points out
    /// towards the player climbing it.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Ladder : MonoBehaviour, IHandTarget, IClimbable, IMantleable, IDebugDrawable
    {
        // Shared hand offsets/pose for all ladders - see HandSnapProfile.
        [SerializeField] private HandSnapProfile snapProfile;

        // Whether the ladder can also be grabbed from behind (its -Z side),
        // e.g. an open ladder standing free in a room. Off: only from the
        // front, so hand rays from behind find nothing to grab - no reticle.
        [SerializeField] private bool climbableFromBack;

        [Header("Rungs")]

        // Height of the lowest rung above the bottom of the box, in metres.
        [SerializeField] private float firstRungHeight = 0.3f;

        // Distance between rungs, in metres. Real ladders are ~0.25-0.3m.
        [SerializeField] private float rungSpacing = 0.3f;

        [Header("Mantling")]

        // Whether the player can mantle off the top of this ladder (see
        // PlayerMantling): with a hand on the top rung the mantle arrow
        // shows, and pushing a stick up moves them to mantlePoint. On by
        // default - every ladder is mantleable unless unticked (e.g. one
        // that tops out under a ceiling, or whose top exit is a separate
        // mantleable ClimbableEdge on the lip above).
        [SerializeField] private bool isMantleable = true;

        // Whether the mantle ends with the player crouched - tick it where
        // there's no room to stand at the top.
        [SerializeField] private bool mantleEndsCrouched;

        // Where the mantle lands the bottom of the player's capsule (their
        // feet). In metres along this ladder's own axes (X across, Y up, Z
        // out towards the climber), from its origin - position and rotation
        // apply, scale doesn't, the same as ClimbableEdge's mantlePoint.
        // Filled in by Reset() / the "Reset Mantle Point" context menu: level
        // with the top of the box, DefaultMantleInset behind its back face -
        // on top of whatever the ladder leans against. Move it if the ladder
        // tops out somewhere else.
        [SerializeField] private Vector3 mantlePoint = new(0f, 2f, -0.5f);

        // How far behind the box's back face the default mantle point is, in
        // metres.
        private const float DefaultMantleInset = 0.4f;

        // Smallest allowed rungSpacing - stops a typo like 0 from producing
        // a divide by zero or thousands of gizmo lines.
        private const float MinRungSpacing = 0.05f;

        // Gizmo colours: faint when the ladder isn't selected, bright when it is.
        private static readonly Color _gizmoColor = new(0.3f, 0.9f, 1f, 0.35f);
        private static readonly Color _gizmoSelectedColor = new(0.3f, 0.9f, 1f, 1f);

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

        /// Whether a mantle off the top of this ladder ends crouched.
        public bool MantleEndsCrouched => mantleEndsCrouched;

        /// World position a mantle lands the player's feet at - mantlePoint
        /// rotated and offset by this ladder's transform, but not scaled.
        public Vector3 MantlePointWorld => UnscaledLocalToWorld(mantlePoint);

        /// <summary>
        /// A mantle is possible on a mantleable ladder while a hand is on
        /// its top rung: gripPoint is where the hand grabbed, so this finds
        /// the rung the same way GetSnapPose() did when it snapped the hand
        /// there. The head's height doesn't matter - reaching the top rung is
        /// the whole rule.
        /// </summary>
        public bool CanMantleFrom(Vector3 gripPoint, Vector3 headPosition, float headBelowTopAllowance)
        {
            if (!isMantleable) {
                return false;
            }

            MeasureRungs(_boxCollider, out Vector3 bottomCentre, out _, out int rungCount);
            Vector3 local = Quaternion.Inverse(transform.rotation) * (gripPoint - transform.position);
            return NearestRung(local, bottomCentre, rungCount) == rungCount - 1;
        }

        /// <summary>
        /// Every mantle off this ladder lands on its mantle point, wherever
        /// the feet started.
        /// </summary>
        public Vector3 GetMantleLanding(Vector3 feetPosition)
        {
            return MantlePointWorld;
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
        /// Puts the mantle point level with the top of this box,
        /// DefaultMantleInset behind its back face, centred across the
        /// ladder. Also available from the component's right-click menu, for
        /// ladders placed before this existed or after resizing one.
        /// </summary>
        [ContextMenu("Reset Mantle Point")]
        private void ResetMantlePoint()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            Vector3 scale = transform.lossyScale;
            Vector3 centre = Vector3.Scale(box.center, scale);
            Vector3 size = Vector3.Scale(box.size, scale);

#if UNITY_EDITOR
            // Lets the change be undone, and marks the object dirty so it's
            // actually saved - a context menu edit isn't otherwise tracked.
            UnityEditor.Undo.RecordObject(this, "Reset Mantle Point");
#endif

            mantlePoint = new Vector3(centre.x, centre.y + size.y * 0.5f, centre.z - size.z * 0.5f - DefaultMantleInset);
        }

        /// <summary>
        /// Editor-only: runs whenever a value is changed in the Inspector.
        /// Keeps the rung values sane.
        /// </summary>
        private void OnValidate()
        {
            rungSpacing = Mathf.Max(rungSpacing, MinRungSpacing);
            firstRungHeight = Mathf.Max(firstRungHeight, 0f);
        }

        /// <summary>
        /// Measures the box in this ladder's "unscaled local" frame - its
        /// position and rotation, but real metres instead of scaled units, the
        /// same frame ClimbableEdge's mantlePoint uses. Works like that
        /// because the rung spacing is in metres, and a ladder box is usually
        /// a stretched cube. Outputs the centre of the box's bottom face, half
        /// the ladder's width, and how many rungs fit.
        ///
        /// Takes the collider as a parameter because the gizmos call this in
        /// the editor, where Awake() (which caches _boxCollider) hasn't run.
        /// </summary>
        private void MeasureRungs(BoxCollider box, out Vector3 bottomCentre, out float halfWidth, out int rungCount)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 centre = Vector3.Scale(box.center, scale);
            Vector3 size = Vector3.Scale(box.size, scale);

            bottomCentre = new Vector3(centre.x, centre.y - size.y * 0.5f, centre.z);
            halfWidth = size.x * 0.5f;

            // How many whole spacings fit above the first rung, plus the first
            // rung itself. The small epsilon stops a rung that sits exactly on
            // the top face being dropped by float rounding.
            float spacing = Mathf.Max(rungSpacing, MinRungSpacing);
            rungCount = Mathf.FloorToInt((size.y - firstRungHeight) / spacing + 0.001f) + 1;
            rungCount = Mathf.Max(rungCount, 1);
        }

        /// <summary>
        /// Which rung (0 = the lowest) is nearest to local, a point in this
        /// ladder's unscaled local frame: how many spacings above the first
        /// rung it is, rounded to a whole rung and kept on the ladder.
        /// </summary>
        private int NearestRung(Vector3 local, Vector3 bottomCentre, int rungCount)
        {
            float heightAboveFirstRung = local.y - bottomCentre.y - firstRungHeight;
            return Mathf.Clamp(Mathf.RoundToInt(heightAboveFirstRung / rungSpacing), 0, rungCount - 1);
        }

        /// <summary>
        /// Converts a point from this ladder's unscaled local frame (see
        /// MeasureRungs()) to world space - position and rotation apply,
        /// scale doesn't, which is why this isn't TransformPoint().
        /// </summary>
        private Vector3 UnscaledLocalToWorld(Vector3 local)
        {
            return transform.position + transform.rotation * local;
        }

        /// <summary>
        /// Refuses hand rays from behind the ladder, unless
        /// climbableFromBack is on. "Behind" means the ray starts on the -Z
        /// side of the rung line (the box's centre front to back) - measured
        /// from the hand, not from where the ray hit, so a hand in front
        /// reaching round the side still counts as in front. Runs for each
        /// hand ray that hits the ladder, so it's kept to one dot product.
        /// </summary>
        public bool CanBeTargetedFrom(Vector3 rayOrigin)
        {
            if (climbableFromBack) {
                return true;
            }

            Vector3 rungLine = transform.TransformPoint(_boxCollider.center);
            return Vector3.Dot(rayOrigin - rungLine, transform.forward) >= 0f;
        }

        /// <summary>
        /// Snaps a grabbing hand onto the rung nearest to where the ray hit,
        /// at the same point across the rung (clamped to the ladder's width),
        /// facing into the ladder with the grip frame's up matching the
        /// ladder's up. Rungs sit on the box's centre line front to back.
        /// A grab from behind (only possible with climbableFromBack on) faces
        /// the other way. Only called on grab, so the maths here isn't a
        /// per-frame cost.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint, Vector3 headPosition, Vector3 headForward)
        {
            MeasureRungs(_boxCollider, out Vector3 bottomCentre, out float halfWidth, out int rungCount);

            // The grab point in the unscaled local frame - the inverse of
            // UnscaledLocalToWorld().
            Vector3 local = Quaternion.Inverse(transform.rotation) * (grabPoint - transform.position);

            int rung = NearestRung(local, bottomCentre, rungCount);

            // The ray hit the box's surface on the hand's side, so a hit
            // behind the rung line means the hand is at the back.
            bool fromBack = local.z < bottomCentre.z;

            local.x = Mathf.Clamp(local.x, bottomCentre.x - halfWidth, bottomCentre.x + halfWidth);
            local.y = bottomCentre.y + firstRungHeight + rung * rungSpacing;
            local.z = bottomCentre.z;

            Vector3 gripPosition = UnscaledLocalToWorld(local);

            // Grip frame: forward points into the ladder (-Z from the front,
            // +Z from behind), up is the ladder's up - the same frame a ledge
            // uses, so a ledge-style profile is a sensible starting point.
            Vector3 intoLadder = fromBack ? transform.forward : -transform.forward;
            Quaternion gripRotation = Quaternion.LookRotation(intoLadder, transform.up);

            // See ClimbableEdge.GetSnapPose() for why == null.
            if (snapProfile == null) {
                return new HandSnapPose(gripPosition, gripRotation, HandPose.RungGrip);
            }

            return snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }

        /// <summary>
        /// Always draws the ladder faintly in the Scene view, so its size and
        /// rung placement are visible while laying out a level even though
        /// it has no mesh - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// While selected, draws the ladder brightly with its extra markers -
        /// see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// Draws the ladder - as Scene view gizmos and, while InHeadsetGizmos
        /// is on, in the headset: the box's outline, the rails (the box's
        /// left and right edges on the rung line) and a line per rung, faint.
        /// With detailed on (selected), bright, plus a sphere at the middle
        /// of each rung and an arrow along +Z from the bottom rung.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            // _boxCollider is only cached by Awake(), which hasn't run in
            // the editor.
            BoxCollider box = _boxCollider != null ? _boxCollider : GetComponent<BoxCollider>();

            if (box == null) {
                return;
            }

            MeasureRungs(box, out Vector3 bottomCentre, out float halfWidth, out int rungCount);

            lines.Color = detailed ? _gizmoSelectedColor : _gizmoColor;

            // The box outline is drawn in its own scaled local space, so it
            // matches the collider exactly.
            lines.Matrix = transform.localToWorldMatrix;
            lines.WireCube(box.center, box.size);

            // Everything else in the unscaled local frame: every point below
            // is in real metres along the ladder's own axes.
            lines.Matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            Vector3 across = new(halfWidth, 0f, 0f);
            Vector3 top = bottomCentre + Vector3.up * Vector3.Scale(box.size, transform.lossyScale).y;

            lines.Line(bottomCentre - across, top - across);
            lines.Line(bottomCentre + across, top + across);

            for (int i = 0; i < rungCount; i++) {
                Vector3 rungCentre = bottomCentre + Vector3.up * (firstRungHeight + i * rungSpacing);
                lines.Line(rungCentre - across, rungCentre + across);

                if (detailed) {
                    lines.WireSphere(rungCentre, 0.03f);
                }
            }

            if (detailed) {
                // An arrow out of each climbing side, from the bottom rung.
                Vector3 start = bottomCentre + Vector3.up * firstRungHeight;
                DrawSideArrow(lines, start, 1f);

                if (climbableFromBack) {
                    DrawSideArrow(lines, start, -1f);
                }
            }

            lines.Matrix = Matrix4x4.identity;

            // Where a mantle off the top lands: a sphere at the feet and a
            // line up to roughly head height (lower when it ends crouched),
            // like ClimbableEdge's.
            if (detailed && isMantleable) {
                Vector3 feet = MantlePointWorld;
                lines.Color = mantleEndsCrouched ? Color.yellow : Color.green;
                lines.WireSphere(feet, 0.1f);
                lines.Line(feet, feet + Vector3.up * (mantleEndsCrouched ? 1f : 1.8f));
            }
        }

        /// <summary>
        /// Draws a 0.4m arrow from start along +Z (side 1) or -Z (side -1),
        /// in the unscaled local frame - marks a side the ladder can be
        /// climbed from.
        /// </summary>
        private static void DrawSideArrow(DebugLines lines, Vector3 start, float side)
        {
            Vector3 end = start + Vector3.forward * (0.4f * side);
            lines.Line(start, end);
            lines.Line(end, end + new Vector3(-0.06f, 0f, -0.08f * side));
            lines.Line(end, end + new Vector3(0.06f, 0f, -0.08f * side));
        }
    }
}
