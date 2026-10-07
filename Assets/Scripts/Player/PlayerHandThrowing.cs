using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Aimed throwing: the accurate way to throw a carried prop (the quick,
    /// natural way - moving the hand and letting go of grip - is
    /// PlayerHandHolding's). While a hand carries a prop, holding that
    /// hand's trigger shows the arc the prop would fly along and where it
    /// would land; letting go of the trigger throws it along exactly that
    /// arc. Each hand aims and throws what it holds with its own trigger.
    ///
    /// The throw always leaves at the same speed (launchSpeed), in the
    /// direction the controller points - so how far it goes is set by how high
    /// the hand is tilted, as with a teleport arc: furthest at about 45
    /// degrees.
    ///
    /// Cancelling is aiming at nothing: point the hand steeply up or down,
    /// or anywhere the arc doesn't come down on something, and the arc
    /// turns red - letting go of the trigger then throws nothing, and the
    /// prop stays in the hand.
    ///
    /// The launch is a short move of the hand visual, not an animation
    /// clip: the hand is snapped a little way forward along the throw
    /// (HandVisualSnap, as a grab snaps it onto a ledge), the prop is let
    /// go when it gets there, and the hand comes back. The arc is drawn
    /// from where that move ends, so the prop leaves from the start of the
    /// line the player saw.
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// Tick() after the hand visuals and carried props are placed, so the
    /// arc starts from where the prop is this frame.
    /// </summary>
    public class PlayerHandThrowing : MonoBehaviour
    {
        /// One hand's aiming state. A class, made once per hand in Awake().
        private class HandThrow
        {
            public bool isLeftHand;

            // The hand model, whose pose the launch move starts from.
            public Transform visual;

            // The line and landing disc this hand draws.
            public ThrowArc arc;

            // The arc's points this frame (world space) and how many of
            // the array's slots are in use. Made once, reused every frame.
            public Vector3[] points;
            public int pointCount;

            // True while the trigger is held with a prop in hand.
            public bool isAiming;

            // True while the hand is making its launch move, and the
            // velocity and spin the prop is to leave with at the end of it.
            public bool isLaunching;
            public Vector3 launchVelocity;
            public Vector3 launchSpin;
        }

        [SerializeField] private PlayerInputXR playerInput;

        // For the head's position, which the arc's dots turn to face.
        // Optional: found in Awake() if not wired.
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerHandHolding playerHandHolding;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        [Header("Aiming")]

        // How far the trigger must be pulled to start aiming, and how far
        // it must be let out to throw (0-1). Two values with a gap between
        // them, so a finger hovering at one point can't flicker between
        // aiming and throwing.
        [SerializeField] private float aimStartTrigger = 0.6f;
        [SerializeField] private float aimEndTrigger = 0.3f;

        // The hand's tilt above and below level, in degrees, beyond which
        // the throw is cancelled (red arc): the "aim at nothing" gesture.
        [SerializeField] private float maxAimPitch = 75f;
        [SerializeField] private float minAimPitch = -60f;

        [Header("Throw")]

        // The speed every aimed throw leaves at, in metres a second. The
        // furthest throw is roughly speed x speed / 9.81 metres.
        [SerializeField] private float launchSpeed = 9f;

        // How fast the prop tumbles end over end as it flies, in radians a
        // second (6.28 = one turn a second). 0 = no spin.
        [SerializeField] private float launchSpin = 5f;

        // The launch move: how far forward the hand goes (metres), how
        // long it takes to get there, and how long to come back (seconds).
        [SerializeField] private float launchReach = 0.15f;
        [SerializeField] private float launchDuration = 0.08f;
        [SerializeField] private float launchReturnDuration = 0.2f;

        [Header("Arc")]

        // What the arc stops at. Filled in from the layer names if left
        // empty - see DefaultArcLayers().
        [SerializeField] private LayerMask arcLayers;

        // The line is drawn as a row of straight pieces, each about this
        // long as it leaves the hand, in metres (they lengthen a little as
        // the prop speeds up on the way down). Drawing only - no rays - so
        // short pieces are nearly free.
        [SerializeField] private float arcSegmentLength = 0.1f;

        // Finding where the arc lands is done separately, in longer
        // pieces of this many seconds of flight, one physics ray each -
        // see ComputeArc(). The arc is given up on (no landing = cancel)
        // after arcMaxTime seconds of flight.
        [SerializeField] private float arcCastTimeStep = 0.1f;
        [SerializeField] private float arcMaxTime = 2.5f;

        // How the arc is drawn: a solid line or a row of dots. Can be
        // changed while playing, to compare the two.
        [SerializeField] private ThrowArc.Style arcStyle = ThrowArc.Style.Line;

        // The line's width; and each dot's radius and the distance from
        // one dot to the next along the arc. All in metres, and read once
        // at startup.
        [SerializeField] private float arcWidth = 0.01f;
        [SerializeField] private float dotRadius = 0.012f;
        [SerializeField] private float dotSpacing = 0.12f;
        [SerializeField] private float landingMarkerRadius = 0.08f;
        [SerializeField] private Color validColor = Color.white;
        [SerializeField] private Color invalidColor = new(1f, 0.2f, 0.2f, 0.5f);

        private HandThrow _left;
        private HandThrow _right;

        // Seconds of flight each piece of the arc covers - see Awake().
        private float _arcTimeStep;

        /// <summary>
        /// Editor-only: fills in the references when the component is added
        /// (on the Hands object; input is on the Player root above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerTracking = GetComponentInParent<PlayerTracking>();
            playerHandHolding = GetComponent<PlayerHandHolding>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
            arcLayers = DefaultArcLayers();
        }

        private void Awake()
        {
            if (playerTracking == null) {
                playerTracking = GetComponentInParent<PlayerTracking>();
            }

            if (arcLayers.value == 0) {
                arcLayers = DefaultArcLayers();
            }

            // A piece's length as time: at launchSpeed, how long the prop
            // takes to cover it. Worked out once - the Max() calls keep a
            // zero typed in the Inspector from dividing by zero.
            _arcTimeStep = Mathf.Max(arcSegmentLength, 0.02f) / Mathf.Max(launchSpeed, 0.1f);

            // Enough slots for the longest arc: one piece per time step,
            // plus the start point and one spare.
            int capacity = Mathf.CeilToInt(arcMaxTime / _arcTimeStep) + 2;

            _left = MakeHand(true, playerHandVisuals.LeftHandVisual, "Left Throw Arc", capacity);
            _right = MakeHand(false, playerHandVisuals.RightHandVisual, "Right Throw Arc", capacity);
        }

        /// <summary>
        /// One hand's state, with its arc object and its points array.
        /// </summary>
        private HandThrow MakeHand(bool isLeftHand, Transform visual, string arcName, int capacity)
        {
            return new HandThrow {
                isLeftHand = isLeftHand,
                visual = visual,
                points = new Vector3[capacity],
                arc = ThrowArc.Create(transform, arcName, arcWidth, dotRadius, dotSpacing, landingMarkerRadius, validColor, invalidColor)
            };
        }

        /// <summary>
        /// What a thrown prop can land on or hit: the world, other props,
        /// guards, and anything never given a layer. Not the player, the
        /// hands (or the props they carry), or climbables' grab volumes.
        /// </summary>
        private static LayerMask DefaultArcLayers()
        {
            return LayerMask.GetMask("Default", "Environment", "Interactable", "Guard");
        }

        private void OnDisable()
        {
            // Null if Awake() hasn't run. And if the scene is being
            // unloaded (a level restart), everything here is on its way
            // out and mustn't be touched.
            if (_left == null || !gameObject.scene.isLoaded) {
                return;
            }

            // Nothing may be left drawn, or half-thrown, with nothing
            // ticking it.
            StopAiming(_left);
            StopAiming(_right);
            FinishLaunchNow(_left);
            FinishLaunchNow(_right);
        }

        /// <summary>
        /// Ends a launch move at once, wherever the hand has got to: the
        /// prop is thrown from there and the hand comes back.
        /// </summary>
        private void FinishLaunchNow(HandThrow hand)
        {
            if (!hand.isLaunching) {
                return;
            }

            hand.isLaunching = false;
            playerHandHolding.ReleaseLaunched(hand.isLeftHand, hand.launchVelocity, hand.launchSpin);
            SnapFor(hand).Release(launchReturnDuration);
        }

        /// <summary>
        /// Aiming and throwing for both hands. Called by PlayerController
        /// every frame, after the hand visuals and carried props are
        /// placed. canAim is false during a mantle and once the level has
        /// ended: nothing new is aimed or thrown then, but a launch move
        /// already under way still finishes.
        /// </summary>
        public void Tick(bool canAim)
        {
            // The aim is straight out of the front of the controller (its
            // own forward axis), as a menu pointer would be - not the hand
            // rays' direction, which is angled out from the palm for
            // grabbing. Read from the hand visuals, which give the
            // controller's rotation as placed this frame.
            playerHandVisuals.GetLeftHandPose(out _, out Quaternion leftRotation);
            playerHandVisuals.GetRightHandPose(out _, out Quaternion rightRotation);

            TickHand(_left, playerInput.LeftTrigger, leftRotation * Vector3.forward, canAim);
            TickHand(_right, playerInput.RightTrigger, rightRotation * Vector3.forward, canAim);
        }

        /// <summary>
        /// One hand: finish a launch under way, or else aim while the
        /// trigger is held with a prop in hand, and throw (or cancel) when
        /// it's let go. direction is where the controller points.
        /// </summary>
        private void TickHand(HandThrow hand, float trigger, Vector3 direction, bool canAim)
        {
            if (hand.isLaunching) {
                TickLaunch(hand);
                return;
            }

            // No prop fully in hand (or not allowed just now): not aiming.
            // This is also where letting go of grip while aiming ends up -
            // PlayerHandHolding has already dropped the prop.
            if (!canAim || !playerHandHolding.TryGetCarriedCentre(hand.isLeftHand, out Vector3 centre)) {
                StopAiming(hand);
                return;
            }

            if (!hand.isAiming) {
                if (trigger < aimStartTrigger) {
                    return;
                }

                hand.isAiming = true;
            }

            // Worked out every frame of aiming, including the one the
            // trigger is let go on - the throw uses this frame's aim, not
            // last frame's.
            bool isValid = ComputeArc(hand, centre, direction, out Vector3 landingPoint, out Vector3 landingNormal);

            if (trigger <= aimEndTrigger) {
                StopAiming(hand);

                if (isValid) {
                    BeginLaunch(hand, direction);
                }

                return;
            }

            hand.arc.Show(hand.points, hand.pointCount, arcStyle, isValid, landingPoint, landingNormal, playerTracking.HeadPosition);
        }

        /// <summary>
        /// Stops this hand aiming and hides its arc. Nothing is thrown.
        /// </summary>
        private void StopAiming(HandThrow hand)
        {
            hand.isAiming = false;
            hand.arc.Hide();
        }

        /// <summary>
        /// Works out the arc a throw from centre in direction would follow,
        /// into hand.points, stopping at the first thing it hits. True if
        /// it's a throw that can be made: the hand isn't tilted past the
        /// cancel angles, there's room for the launch move, and the arc
        /// lands on something in time.
        /// </summary>
        private bool ComputeArc(HandThrow hand, Vector3 centre, Vector3 direction, out Vector3 landingPoint, out Vector3 landingNormal)
        {
            landingPoint = default;
            landingNormal = Vector3.up;

            // The prop leaves the hand at the end of the launch move, a
            // little way along the throw - so that's where the arc starts.
            Vector3 start = centre + direction * launchReach;
            Vector3[] points = hand.points;

            // The launch move itself must be clear, or the hand would push
            // the prop into (or through) whatever is right in front of it.
            // Drawn as a short stub, so there's still something to see.
            if (Physics.Linecast(centre, start, arcLayers, QueryTriggerInteraction.Ignore)) {
                points[0] = centre;
                points[1] = start;
                hand.pointCount = 2;
                return false;
            }

            // How far the hand is tilted above level. direction has length
            // 1, so its y is the sine of that angle; Asin turns it back
            // into the angle.
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            bool isPitchValid = pitch <= maxAimPitch && pitch >= minAimPitch;

            Vector3 velocity = direction * launchSpeed;
            Vector3 gravity = Physics.gravity;
            float fixedStep = Time.fixedDeltaTime;

            // Two jobs, at two levels of detail.
            //
            // 1. Finding where it lands: follow the arc in a FEW long
            // straight pieces (arcCastTimeStep of flight each), one
            // physics ray per piece. A straight piece cuts the corner of
            // the curve, but by very little - gravity x step squared / 8,
            // about 1cm for a 0.1s piece - so long pieces find the landing
            // nearly as exactly as short ones, with far fewer rays.
            float castStep = Mathf.Max(arcCastTimeStep, 0.02f);
            float flightTime = arcMaxTime;
            bool hasLanded = false;
            Vector3 previous = start;

            for (float previousTime = 0f; previousTime < arcMaxTime;) {
                float t = Mathf.Min(previousTime + castStep, arcMaxTime);
                Vector3 point = ArcPoint(start, velocity, gravity, t, fixedStep);

                if (Physics.Linecast(previous, point, out RaycastHit hit, arcLayers, QueryTriggerInteraction.Ignore)) {
                    // When it lands: the hit's share of the way along this
                    // piece, as the same share of the piece's time.
                    float pieceLength = (point - previous).magnitude;
                    float share = pieceLength > 0f ? hit.distance / pieceLength : 0f;

                    flightTime = previousTime + (t - previousTime) * share;
                    landingPoint = hit.point;
                    landingNormal = hit.normal;
                    hasLanded = true;
                    break;
                }

                previous = point;
                previousTime = t;
            }

            // 2. Drawing it: MANY short pieces, straight from the formula
            // up to the moment it lands - no rays, just arithmetic, so the
            // line can be as smooth as it likes.
            int last = Mathf.Min(Mathf.CeilToInt(flightTime / _arcTimeStep), points.Length - 1);

            for (int i = 0; i < last; i++) {
                points[i] = ArcPoint(start, velocity, gravity, i * _arcTimeStep, fixedStep);
            }

            // The line ends exactly on what it hit (or, with no landing,
            // wherever the arc had got to when it was given up on).
            points[last] = hasLanded ? landingPoint : ArcPoint(start, velocity, gravity, flightTime, fixedStep);
            hand.pointCount = last + 1;

            return hasLanded && isPitchValid;
        }

        /// <summary>
        /// Where a thrown body is t seconds after leaving start at
        /// velocity. The textbook answer is start + velocity x t + half
        /// gravity x t squared - but Unity's physics moves in steps
        /// (fixedStep seconds each), adding gravity to the speed BEFORE
        /// each move, so a body really falls a little further: t x (t +
        /// fixedStep) in place of t squared. Using the same sum puts the
        /// line where the prop will actually go (about 10cm of difference
        /// a second into the flight).
        /// </summary>
        private static Vector3 ArcPoint(Vector3 start, Vector3 velocity, Vector3 gravity, float t, float fixedStep)
        {
            return start + velocity * t + gravity * (0.5f * t * (t + fixedStep));
        }

        /// <summary>
        /// This hand's snap, owned by PlayerHandVisuals.
        /// </summary>
        private HandVisualSnap SnapFor(HandThrow hand)
        {
            return hand.isLeftHand ? playerHandVisuals.LeftVisualSnap : playerHandVisuals.RightVisualSnap;
        }

        /// <summary>
        /// Starts the throw: the hand visual is sent launchReach forward
        /// along direction, taking the prop (its child) with it, and the
        /// prop is told to stay in the hand until it gets there.
        /// </summary>
        private void BeginLaunch(HandThrow hand, Vector3 direction)
        {
            HandVisualSnap snap = SnapFor(hand);
            hand.visual.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);

            // The same rotation and finger pose the hand has now (the snap
            // still remembers the pose it picked the prop up with) - only
            // moved forward.
            HandSnapPose launchPose = new(position + direction * launchReach, rotation, snap.SnapPose.Pose);
            snap.Snap(launchPose, launchDuration);

            playerHandHolding.BeginLaunch(hand.isLeftHand);

            hand.isLaunching = true;
            hand.launchVelocity = direction * launchSpeed;

            // Tumbling end over end: spin about the level axis square to
            // the throw (the cross product of two directions is a third at
            // right angles to both).
            hand.launchSpin = Vector3.Cross(Vector3.up, direction).normalized * launchSpin;
        }

        /// <summary>
        /// Watches a launch move, and lets the prop go when the hand has
        /// reached the end of it.
        /// </summary>
        private void TickLaunch(HandThrow hand)
        {
            HandVisualSnap snap = SnapFor(hand);

            // The prop left the hand some other way (it was destroyed, or
            // carrying was switched off): just bring the hand back.
            if (!playerHandHolding.IsLaunching(hand.isLeftHand)) {
                hand.isLaunching = false;
                snap.Release(launchReturnDuration);
                return;
            }

            if (snap.Weight < 1f) {
                return;
            }

            FinishLaunchNow(hand);
        }
    }
}
