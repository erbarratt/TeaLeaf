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
    /// top is a mantleable ClimbableEdge placed on the lip above the ladder;
    /// getting off the bottom is letting go.
    ///
    /// Orientation convention (matches ClimbableEdge) - local X runs across
    /// the ladder (along the rungs), +Y is up the ladder, and +Z points out
    /// towards the player climbing it.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Ladder : MonoBehaviour, IHandTarget, IClimbable, IDebugDrawable
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
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint)
        {
            MeasureRungs(_boxCollider, out Vector3 bottomCentre, out float halfWidth, out int rungCount);

            // The grab point in the unscaled local frame - the inverse of
            // UnscaledLocalToWorld().
            Vector3 local = Quaternion.Inverse(transform.rotation) * (grabPoint - transform.position);

            // Nearest rung: how many spacings above the first rung the grab
            // is, rounded to a whole rung and kept on the ladder.
            float heightAboveFirstRung = local.y - bottomCentre.y - firstRungHeight;
            int rung = Mathf.Clamp(Mathf.RoundToInt(heightAboveFirstRung / rungSpacing), 0, rungCount - 1);

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
                return new HandSnapPose(gripPosition, gripRotation, HandPose.LedgeGrip);
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
