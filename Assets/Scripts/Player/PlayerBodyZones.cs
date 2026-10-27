using Core;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The places on the body a hand reaches to for something carried
    /// there: the chest (the pack) and the two shoulders (the blackjack
    /// over one, and later the bow over the other). Only the shapes and
    /// the tests - what comes out of each is the business of the system
    /// that asks.
    ///
    /// - The chest is a box a little below the head. A hand counts as at
    ///   the chest when it's inside the box with its palm towards the
    ///   chest.
    /// - Each shoulder is a ball beside and behind the head. A hand counts
    ///   as at a shoulder only when it's in the ball over its OWN shoulder
    ///   (left hand, left shoulder): reaching across the body counts for
    ///   nothing.
    ///
    /// Both are placed and turned with the head's tilt ignored, so looking
    /// down doesn't swing them up behind the head. Hand positions are the
    /// controllers', not the hand visuals' (a visual held off by a wall
    /// is still reaching).
    ///
    /// Lives on the Hands object, one per rig, so every system reaching
    /// for the body agrees where the body is. No Update(): it only answers
    /// when asked, from the systems' own ticks.
    /// </summary>
    public class PlayerBodyZones : MonoBehaviour, IDebugDrawable
    {
        [Tooltip("The tracking on the Player root: head and controller positions the zones are measured from, e.g. drag the Player root here. Found on the Player root above if left empty.")]
        [SerializeField] private PlayerTracking playerTracking;
        [Tooltip("The hand visuals on this Hands object: which way each palm faces, for the chest check, e.g. drag the Hands object here. Found on this object if left empty.")]
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        [Header("Chest")]

        // The middle of the chest from the head, in metres: to the
        // player's right, up, and ahead - with the head's tilt ignored.
        [Tooltip("The middle of the chest from the head, in metres: x to the player's right, y up, z ahead, head tilt ignored, e.g. (0, -0.36, -0.12) = 36 cm below the eyes and 12 cm back. Where a hand brings the pack out.")]
        [SerializeField] private Vector3 chestOffset = new(0f, -0.36f, -0.12f);

        // The box round that point a hand must be in, in metres: its
        // width (side to side), height and depth (front to back). It
        // turns with the head, tilt ignored, so it stays square to the
        // body.
        [Tooltip("The box round the chest's middle a hand must be in, in metres: width (side to side), height, depth (front to back); turns with the head's facing, e.g. (0.4, 0.35, 0.4). Bigger = easier to reach the pack, but more often by accident.")]
        [SerializeField] private Vector3 chestSize = new(0.4f, 0.35f, 0.4f);

        // How far the palm may be turned away from pointing straight at
        // the chest, in degrees, and still count as facing it.
        [Tooltip("How far, in degrees, the palm may be turned away from pointing straight at the chest's middle and still count as facing it, e.g. 60 = a palm turned up to 60° off still brings the pack out. Higher = more forgiving; lower = the palm must face the chest squarely.")]
        [SerializeField] private float palmAngle = 60f;

        [Header("Shoulders")]

        // The middle of the ball over the right shoulder, from the head in
        // metres - x to the right, y up, z ahead (so a negative z is
        // behind), with the head's tilt ignored. The left shoulder's is
        // the mirror image.
        [Tooltip("The middle of the ball over the right shoulder, from the head in metres: x to the right, y up, z ahead (negative = behind), head tilt ignored; the left shoulder's is its mirror, e.g. (0.2, -0.05, -0.12) = 20 cm right, just below and behind the eyes. Where the bow, blackjack and arrows are taken.")]
        [SerializeField] private Vector3 shoulderOffset = new(0.2f, -0.05f, -0.12f);

        // The ball's radius, in metres.
        [Tooltip("Radius of each shoulder ball, in metres, e.g. 0.2 = the hand must be within 20 cm of the shoulder point. Higher = easier to draw, but more often by accident; lower = a more exact reach.")]
        [SerializeField] private float shoulderRadius = 0.2f;

        /// <summary>
        /// The body zones on the Hands object (the object hands is on),
        /// added there if it has none - so a rig set up before this class
        /// existed needs no change. Called by each system that reaches for
        /// the body, in its Awake().
        /// </summary>
        public static PlayerBodyZones GetOrAdd(PlayerHandVisuals hands)
        {
            PlayerBodyZones zones = hands.GetComponent<PlayerBodyZones>();

            if (zones == null) {
                zones = hands.gameObject.AddComponent<PlayerBodyZones>();
            }

            return zones;
        }

        /// <summary>
        /// Editor-only: fills in the references when the component is
        /// added (on the Hands object; tracking is on the Player root
        /// above it).
        /// </summary>
        private void Reset()
        {
            playerTracking = GetComponentInParent<PlayerTracking>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
        }

        private void Awake()
        {
            if (playerTracking == null) {
                playerTracking = GetComponentInParent<PlayerTracking>();
            }

            if (playerHandVisuals == null) {
                playerHandVisuals = GetComponent<PlayerHandVisuals>();
            }
        }

        private void OnEnable()
        {
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// Whether a hand is at the chest: inside the chest box, with its
        /// palm towards the chest's middle.
        /// </summary>
        public bool IsAtChest(bool isLeftHand)
        {
            Vector3 hand = isLeftHand ? playerTracking.LeftHandPosition : playerTracking.RightHandPosition;
            Quaternion headYaw = HeadYaw();
            Vector3 toChest = ChestPoint() - hand;

            // The same offset seen from the box: turned back by the
            // head's yaw, its x is side to side, y up and z front to
            // back, so each can be compared with half the box's size.
            Vector3 inBox = Quaternion.Inverse(headYaw) * toChest;

            if (Mathf.Abs(inBox.x) > chestSize.x * 0.5f || Mathf.Abs(inBox.y) > chestSize.y * 0.5f || Mathf.Abs(inBox.z) > chestSize.z * 0.5f) {
                return false;
            }

            Transform visual = isLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;

            // The palm faces along the hand visual's own +X.
            // TransformVector(), not TransformDirection(): the right
            // visual is the left one mirrored, and only TransformVector()
            // takes the mirroring into account.
            Vector3 palm = visual.TransformVector(Vector3.right).normalized;

            // The chest is a point on the body's middle line, so a hand
            // in front of it has it straight behind the palm. A hand
            // right on the point has no direction to it: then it's the
            // way the player's back is.
            Vector3 direction = toChest.sqrMagnitude > 0.0001f ? toChest.normalized : headYaw * Vector3.back;

            return Vector3.Dot(palm, direction) >= Mathf.Cos(palmAngle * Mathf.Deg2Rad);
        }

        /// <summary>
        /// Whether a hand is at its own shoulder: the left hand in the
        /// ball over the left shoulder, the right hand in the one over the
        /// right.
        /// </summary>
        public bool IsAtShoulder(bool isLeftHand)
        {
            Vector3 hand = isLeftHand ? playerTracking.LeftHandPosition : playerTracking.RightHandPosition;
            return (hand - ShoulderPoint(isLeftHand)).sqrMagnitude <= shoulderRadius * shoulderRadius;
        }

        /// <summary>
        /// The middle of the chest, in the world.
        /// </summary>
        private Vector3 ChestPoint()
        {
            return playerTracking.HeadPosition + HeadYaw() * chestOffset;
        }

        /// <summary>
        /// The middle of one shoulder's ball, in the world: the right
        /// one's offset, mirrored side to side for the left.
        /// </summary>
        private Vector3 ShoulderPoint(bool isLeftShoulder)
        {
            Vector3 offset = isLeftShoulder ? new Vector3(-shoulderOffset.x, shoulderOffset.y, shoulderOffset.z) : shoulderOffset;
            return playerTracking.HeadPosition + HeadYaw() * offset;
        }

        /// <summary>
        /// The head's rotation with its tilt taken out: only which way it
        /// faces round the vertical.
        /// </summary>
        private Quaternion HeadYaw()
        {
            return Quaternion.Euler(0f, playerTracking.HeadRotation.eulerAngles.y, 0f);
        }

        /// <summary>
        /// Editor-only: the zones while this object is selected.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// In the detailed view (selected, or in the headset with detail
        /// on) while playing: the chest box and the two shoulder balls -
        /// yellow, green while a hand is at that zone.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!detailed || !Application.isPlaying || playerTracking == null || playerHandVisuals == null) {
                return;
            }

            lines.Color = IsAtChest(true) || IsAtChest(false) ? Color.green : Color.yellow;

            // The box's three half-edges in the world: with the centre,
            // every corner is the centre plus or minus each of them.
            Quaternion headYaw = HeadYaw();
            Vector3 centre = ChestPoint();
            Vector3 right = headYaw * Vector3.right * (chestSize.x * 0.5f);
            Vector3 up = Vector3.up * (chestSize.y * 0.5f);
            Vector3 ahead = headYaw * Vector3.forward * (chestSize.z * 0.5f);

            // Twelve edges: for each pair of signs, one edge along each
            // of the three directions.
            for (int a = -1; a <= 1; a += 2) {
                for (int b = -1; b <= 1; b += 2) {
                    lines.Line(centre - right + up * a + ahead * b, centre + right + up * a + ahead * b);
                    lines.Line(centre + right * a - up + ahead * b, centre + right * a + up + ahead * b);
                    lines.Line(centre + right * a + up * b - ahead, centre + right * a + up * b + ahead);
                }
            }

            lines.Color = IsAtShoulder(true) ? Color.green : Color.yellow;
            lines.WireSphere(ShoulderPoint(true), shoulderRadius);

            lines.Color = IsAtShoulder(false) ? Color.green : Color.yellow;
            lines.WireSphere(ShoulderPoint(false), shoulderRadius);
        }
    }
}
