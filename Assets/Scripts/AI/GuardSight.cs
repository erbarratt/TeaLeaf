using System;
using Core;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// How guards see, as one asset shared by every guard: Thief VR's
    /// vision zones and the fill times that go with them. Create one with
    /// Assets > Create > TeaLeaf > Guard Sight (it starts with Thief VR's
    /// numbers, from Reset()).
    ///
    /// The guard has one view frustum in front of its eye - the widest
    /// zone, the corner of the eye - and rules in code split it into
    /// zones (very close, close, straight ahead, the corner of the eye).
    /// Each zone is a few numbers: how far ahead it reaches, an angle
    /// either side of straight ahead and how wide it can get, an angle
    /// up and down and how high and low it can get, and where it starts
    /// narrowing towards its far end. A point is in a zone when it's
    /// within those limits at its depth - a few multiplies, no physics.
    ///
    /// Each zone's reach is cut into distance bands. A band says, for a
    /// lit player and for a dark one, how many seconds the guard takes
    /// to fill each meter (suspicion and detection), and how standing
    /// still, crouching and moving fast change that. A fill time below
    /// zero means never: the dark side of the far bands is never, so
    /// in the dark a guard sees only what is near it. Times are blended
    /// across a band towards the next band's.
    ///
    /// Where zones overlap, the fastest fill wins. The asset only does
    /// the sums; the guard does the rays (Guard.Look()).
    /// </summary>
    [CreateAssetMenu(fileName = "GuardSight", menuName = "TeaLeaf/Guard Sight")]
    public class GuardSight : ScriptableObject
    {
        /// <summary>
        /// Fill times for one band in one light: seconds to fill each
        /// meter from empty, and what multiplies them. A multiplier above
        /// 1 is slower, below faster; a time below zero or a multiplier
        /// of 0 means never.
        /// </summary>
        [Serializable]
        public struct Fill
        {
            [Tooltip("Seconds to fill the suspicion meter from empty. Below zero: never.")]
            public float suspicionTime;
            [Tooltip("Multiplies Suspicion Time while the player stands still. 0: never.")]
            public float suspicionStillMultiplier;
            [Tooltip("Multiplies Suspicion Time while the player is crouched. 0: never.")]
            public float suspicionCrouchMultiplier;
            [Tooltip("Seconds to fill the detection meter from empty (full = chase). Below zero: never.")]
            public float detectionTime;
            [Tooltip("Multiplies Detection Time while the player stands still. 0: never.")]
            public float detectionStillMultiplier;
            [Tooltip("Multiplies Detection Time while the player is crouched. 0: never.")]
            public float detectionCrouchMultiplier;
            [Tooltip("Faster than this, in metres a second, the player counts as moving fast.")]
            public float fastSpeed;
            [Tooltip("Multiplies both times while the player moves, up to Fast Speed.")]
            public float movingMultiplier;
            [Tooltip("Multiplies both times while the player moves faster than Fast Speed.")]
            public float fastMultiplier;

            public Fill(float suspicionTime, float suspicionStill, float suspicionCrouch,
                float detectionTime, float detectionStill, float detectionCrouch,
                float fastSpeed, float moving, float fast)
            {
                this.suspicionTime = suspicionTime;
                suspicionStillMultiplier = suspicionStill;
                suspicionCrouchMultiplier = suspicionCrouch;
                this.detectionTime = detectionTime;
                detectionStillMultiplier = detectionStill;
                detectionCrouchMultiplier = detectionCrouch;
                this.fastSpeed = fastSpeed;
                movingMultiplier = moving;
                fastMultiplier = fast;
            }

            /// A dark side that never sees: both times never.
            public static Fill Never(float fastSpeed, float fast) => new(-1f, 1f, 1f, -1f, 1f, 1f, fastSpeed, 1f, fast);
        }

        /// <summary>
        /// One distance band of a zone: how long it is (bands follow one
        /// another out from the eye) and its fill times lit and dark.
        /// </summary>
        [Serializable]
        public class Band
        {
            public string name;
            [Tooltip("How long the band is, in metres, starting where the one before ends.")]
            public float length;
            [Tooltip("Fill times for a player in light (Visibility at least Lit Threshold).")]
            public Fill lit;
            [Tooltip("Fill times for a player in the dark.")]
            public Fill dark;
        }

        /// <summary>
        /// One vision zone: the rules for its part of the view, and its
        /// distance bands. Across the view it opens at an angle from the
        /// eye until it reaches its widest, and likewise up and down;
        /// from Narrow From to Range each limit eases to its end value.
        /// </summary>
        [Serializable]
        public class Zone
        {
            public string name;
            [Tooltip("How far ahead of the eye the zone reaches, in metres.")]
            public float range;
            [Tooltip("Degrees either side of straight ahead the zone opens at.")]
            [Range(0f, 89f)] public float halfAngle;
            [Tooltip("The widest the zone gets either side of straight ahead, in metres.")]
            public float maxHalfWidth;
            [Tooltip("Degrees above straight ahead the zone opens at.")]
            [Range(0f, 89f)] public float upAngle;
            [Tooltip("The highest the zone gets above the eye, in metres.")]
            public float maxAbove;
            [Tooltip("Degrees below straight ahead the zone opens at.")]
            [Range(0f, 89f)] public float downAngle;
            [Tooltip("The lowest the zone gets below the eye, in metres.")]
            public float maxBelow;
            [Tooltip("From this far ahead, in metres, the limits ease to the end values below by Range. At Range or more: no narrowing.")]
            public float narrowFrom;
            [Tooltip("Half width at Range, when narrowing.")]
            public float endHalfWidth;
            [Tooltip("Height above the eye at Range, when narrowing.")]
            public float endAbove;
            [Tooltip("Depth below the eye at Range, when narrowing.")]
            public float endBelow;
            [Tooltip("Distance bands out from the eye, nearest first. Beyond the last, the last one's times hold.")]
            public Band[] bands;
        }

        [Tooltip("The vision zones, the widest (the view frustum) last. Where they overlap, the fastest fill wins.")]
        [SerializeField] private Zone[] zones;

        // The player counts as lit at this Visibility or more, dark below
        // it: which side of each band's table applies. Visibility already
        // includes crouching (PlayerVisibility), so a player crouched in
        // moonlight drops into the dark side.
        [Tooltip("The player counts as lit at this Visibility (0-1) or more, else dark. Visibility includes crouching, so crouching in moonlight (0.4 x 0.6) counts as dark.")]
        [Range(0f, 1f)]
        [SerializeField] private float litThreshold = 0.3f;

        // Slower than this the player counts as standing still. Room-scale
        // sway moves the body a little even standing.
        [Tooltip("Slower than this, in metres a second, the player counts as standing still (room-scale sway moves the body a little even standing).")]
        [SerializeField] private float stillSpeed = 0.25f;

        // The player's test points that show (PlayerVisibility weights)
        // must add up to this to be seen: the body, or the head seen from
        // below plus a little more.
        [Tooltip("The player's test points that show (weights on PlayerVisibility) must add up to at least this to be seen.")]
        [SerializeField] private float seenWeight = 0.5f;

        public float LitThreshold => litThreshold;
        public float SeenWeight => seenWeight;

        /// <summary>
        /// The furthest any zone reaches from the eye, in metres: beyond
        /// it nothing can be seen and a guard needn't look closely.
        /// </summary>
        public float Reach
        {
            get
            {
                float reach = 0f;

                if (zones == null) {
                    return reach;
                }

                // Each zone's far corner, its furthest point from the eye
                // (no zone is wider or taller anywhere nearer).
                foreach (Zone zone in zones) {
                    GetLimits(zone, zone.range, out float halfWidth, out float above, out float below);
                    reach = Mathf.Max(reach, new Vector3(halfWidth, Mathf.Max(above, below), zone.range).magnitude);
                }

                return reach;
            }
        }

        /// <summary>
        /// Whether a point (relative to the eye: x right, y up, z ahead)
        /// is inside any zone. For things seen whatever the light (bodies,
        /// which bring their own light test).
        /// </summary>
        public bool Contains(Vector3 local)
        {
            if (zones == null) {
                return false;
            }

            foreach (Zone zone in zones) {
                if (IsInside(zone, local)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The fill times for the player at a point (relative to the eye),
        /// lit or dark, moving at a speed, crouched or not: the fastest of
        /// every zone the point is in, each meter on its own. A time below
        /// zero is never. Returns whether either meter can fill at all -
        /// false outside every zone, or where every zone it is in can't
        /// see it (so the guard needn't cast a ray).
        /// </summary>
        public bool TryGetFillTimes(Vector3 local, bool isLit, float speed, bool isCrouched, out float suspicionTime, out float detectionTime)
        {
            suspicionTime = -1f;
            detectionTime = -1f;

            if (zones == null) {
                return false;
            }

            float distance = local.magnitude;

            foreach (Zone zone in zones) {
                if (zone.bands == null || zone.bands.Length == 0 || !IsInside(zone, local)) {
                    continue;
                }

                GetBandTimes(zone.bands, distance, isLit, speed, isCrouched, out float suspicion, out float detection);
                suspicionTime = Fastest(suspicionTime, suspicion);
                detectionTime = Fastest(detectionTime, detection);
            }

            return suspicionTime >= 0f || detectionTime >= 0f;
        }

        /// <summary>
        /// The fill times in a zone at a distance: the band the distance
        /// falls in, its times blended towards the next band's across it
        /// (unless either is never), then multiplied for how the player
        /// moves (that band's multipliers).
        /// </summary>
        private void GetBandTimes(Band[] bands, float distance, bool isLit, float speed, bool isCrouched, out float suspicionTime, out float detectionTime)
        {
            // Finds the band: each starts where the one before ends.
            // Beyond the last, the last one holds.
            int index = bands.Length - 1;
            float start = 0f;

            for (int i = 0; i < bands.Length; i++) {
                if (distance < start + bands[i].length) {
                    index = i;
                    break;
                }

                if (i < bands.Length - 1) {
                    start += bands[i].length;
                }
            }

            Band band = bands[index];
            Fill fill = isLit ? band.lit : band.dark;
            suspicionTime = fill.suspicionTime;
            detectionTime = fill.detectionTime;

            // Blended across the band towards the next band's times, so
            // the step from one band to the next is a slope, not a cliff.
            if (index < bands.Length - 1 && band.length > 0f) {
                Fill next = isLit ? bands[index + 1].lit : bands[index + 1].dark;
                float t = Mathf.Clamp01((distance - start) / band.length);
                suspicionTime = BlendTime(suspicionTime, next.suspicionTime, t);
                detectionTime = BlendTime(detectionTime, next.detectionTime, t);
            }

            // How the player moves: standing still, or moving (fast or
            // not); and crouched or not.
            bool isStill = speed < stillSpeed;
            float moving = speed > fill.fastSpeed ? fill.fastMultiplier : fill.movingMultiplier;

            suspicionTime = Multiply(suspicionTime, isStill ? fill.suspicionStillMultiplier : moving);
            detectionTime = Multiply(detectionTime, isStill ? fill.detectionStillMultiplier : moving);

            if (isCrouched) {
                suspicionTime = Multiply(suspicionTime, fill.suspicionCrouchMultiplier);
                detectionTime = Multiply(detectionTime, fill.detectionCrouchMultiplier);
            }
        }

        /// <summary>
        /// A fill time blended towards another; either one never (below
        /// zero) and it stays as it is.
        /// </summary>
        private static float BlendTime(float from, float to, float t)
        {
            return from < 0f || to < 0f ? from : Mathf.Lerp(from, to, t);
        }

        /// <summary>
        /// A fill time multiplied: never stays never, and a multiplier of
        /// 0 (or less) makes it never.
        /// </summary>
        private static float Multiply(float time, float multiplier)
        {
            return time < 0f || multiplier <= 0f ? -1f : time * multiplier;
        }

        /// <summary>
        /// The faster of two fill times, where below zero is never.
        /// </summary>
        private static float Fastest(float a, float b)
        {
            if (a < 0f) {
                return b;
            }

            return b < 0f ? a : Mathf.Min(a, b);
        }

        /// <summary>
        /// Whether a point (relative to the eye) is inside a zone: ahead
        /// of the eye within its range, and within its limits at the
        /// point's depth.
        /// </summary>
        private static bool IsInside(Zone zone, Vector3 local)
        {
            float depth = local.z;

            if (depth < 0f || depth > zone.range) {
                return false;
            }

            GetLimits(zone, depth, out float halfWidth, out float above, out float below);

            return Mathf.Abs(local.x) <= halfWidth && local.y <= above && local.y >= -below;
        }

        /// <summary>
        /// A zone's limits at a depth ahead of the eye: half its width,
        /// how far above the eye and how far below it (both positive).
        /// Each opens at its angle from the eye until it reaches its
        /// most, then past Narrow From eases to its end value at Range.
        /// </summary>
        private static void GetLimits(Zone zone, float depth, out float halfWidth, out float above, out float below)
        {
            // Past Narrow From the limits are blended from where they
            // were there; before it, they are worked out at the depth.
            bool isNarrowing = zone.narrowFrom < zone.range && depth > zone.narrowFrom;
            float openDepth = isNarrowing ? zone.narrowFrom : depth;

            halfWidth = Mathf.Min(openDepth * Mathf.Tan(zone.halfAngle * Mathf.Deg2Rad), zone.maxHalfWidth);
            above = Mathf.Min(openDepth * Mathf.Tan(zone.upAngle * Mathf.Deg2Rad), zone.maxAbove);
            below = Mathf.Min(openDepth * Mathf.Tan(zone.downAngle * Mathf.Deg2Rad), zone.maxBelow);

            if (!isNarrowing) {
                return;
            }

            float t = (depth - zone.narrowFrom) / (zone.range - zone.narrowFrom);
            halfWidth = Mathf.Lerp(halfWidth, zone.endHalfWidth, t);
            above = Mathf.Lerp(above, zone.endAbove, t);
            below = Mathf.Lerp(below, zone.endBelow, t);
        }

        // The depths a zone's outline is drawn at (DrawZones()), reused
        // so drawing allocates nothing.
        private static readonly float[] _drawDepths = new float[6];

        /// <summary>
        /// Draws every zone from an eye: a rectangle wherever the zone's
        /// outline bends (where a limit stops opening, where narrowing
        /// starts, its far end) and the four edges joining them.
        /// </summary>
        public void DrawZones(DebugLines lines, Vector3 eyePosition, Quaternion eyeRotation)
        {
            if (zones == null) {
                return;
            }

            foreach (Zone zone in zones) {
                // Where each limit reaches its most, where narrowing
                // starts, the eye and the far end, in order.
                _drawDepths[0] = 0f;
                _drawDepths[1] = OpenedDepth(zone.maxHalfWidth, zone.halfAngle);
                _drawDepths[2] = OpenedDepth(zone.maxAbove, zone.upAngle);
                _drawDepths[3] = OpenedDepth(zone.maxBelow, zone.downAngle);
                _drawDepths[4] = zone.narrowFrom;
                _drawDepths[5] = zone.range;

                for (int i = 0; i < _drawDepths.Length; i++) {
                    _drawDepths[i] = Mathf.Clamp(_drawDepths[i], 0f, zone.range);
                }

                Array.Sort(_drawDepths);

                for (int i = 1; i < _drawDepths.Length; i++) {
                    float near = _drawDepths[i - 1];
                    float far = _drawDepths[i];

                    GetLimits(zone, near, out float nearHalfWidth, out float nearAbove, out float nearBelow);
                    GetLimits(zone, far, out float halfWidth, out float above, out float below);

                    Vector3 a = Corner(eyePosition, eyeRotation, -halfWidth, -below, far);
                    Vector3 b = Corner(eyePosition, eyeRotation, halfWidth, -below, far);
                    Vector3 c = Corner(eyePosition, eyeRotation, halfWidth, above, far);
                    Vector3 d = Corner(eyePosition, eyeRotation, -halfWidth, above, far);

                    lines.Line(a, b);
                    lines.Line(b, c);
                    lines.Line(c, d);
                    lines.Line(d, a);

                    lines.Line(Corner(eyePosition, eyeRotation, -nearHalfWidth, -nearBelow, near), a);
                    lines.Line(Corner(eyePosition, eyeRotation, nearHalfWidth, -nearBelow, near), b);
                    lines.Line(Corner(eyePosition, eyeRotation, nearHalfWidth, nearAbove, near), c);
                    lines.Line(Corner(eyePosition, eyeRotation, -nearHalfWidth, nearAbove, near), d);
                }
            }
        }

        /// <summary>
        /// How far ahead a limit opening at an angle reaches its most.
        /// </summary>
        private static float OpenedDepth(float most, float angle)
        {
            float tan = Mathf.Tan(angle * Mathf.Deg2Rad);

            return tan > 0f ? most / tan : 0f;
        }

        private static Vector3 Corner(Vector3 eyePosition, Quaternion eyeRotation, float x, float y, float depth)
        {
            return eyePosition + eyeRotation * new Vector3(x, y, depth);
        }

        /// <summary>
        /// Editor-only: a new asset (or Reset in its menu) starts with
        /// Thief VR's numbers - its guard data asset and its four vision
        /// meshes (VeryClose, Close, Direct LOS, Peripheral), read from
        /// the extracted game and fitted with angles and limits. Their
        /// meshes sit 1.8m up on the body, facing the body's way; ours
        /// sit on the eye.
        /// </summary>
        private void Reset()
        {
            litThreshold = 0.3f;
            stillSpeed = 0.25f;
            seenWeight = 0.5f;

            // Lit at arm's length: seen at once.
            Fill veryCloseLit = new(0.01f, 1f, 1f, 0.01f, 1f, 1f, 1.415f, 1f, 1f);
            Fill closeLit = new(0.5f, 1f, 1f, 1.5f, 1f, 1f, 3f, 1f, 0.5f);

            zones = new[] {
                new Zone {
                    name = "Very Close",
                    range = 3f,
                    halfAngle = 68f, maxHalfWidth = 1.5f,
                    upAngle = 50f, maxAbove = 0.95f,
                    downAngle = 74f, maxBelow = 2.12f,
                    narrowFrom = 1.8f, endHalfWidth = 0.5f, endAbove = 0.95f, endBelow = 1.8f,
                    bands = new[] {
                        new Band {
                            name = "0-1.5m", length = 1.5f, lit = veryCloseLit,
                            dark = new Fill(0.375f, 1f, 1f, 0.2f, 1f, 1f, 1.5f, 1f, 0.02f)
                        },
                        new Band {
                            name = "1.5-3m", length = 1.5f, lit = veryCloseLit,
                            dark = new Fill(1f, 5f, 1f, 0.5f, 2.5f, 1f, 3f, 2.5f, 0.02f)
                        }
                    }
                },
                new Zone {
                    name = "Close",
                    range = 6f,
                    halfAngle = 71.5f, maxHalfWidth = 4.75f,
                    upAngle = 45f, maxAbove = 1.5f,
                    downAngle = 68.5f, maxBelow = 3.8f,
                    narrowFrom = 3.7f, endHalfWidth = 2.1f, endAbove = 1.25f, endBelow = 1.25f,
                    bands = new[] {
                        new Band {
                            name = "0-3.75m", length = 3.75f, lit = closeLit,
                            dark = new Fill(3f, 1.5f, 2f, 2f, 2.5f, 1f, 3f, 1f, 0.05f)
                        },
                        // Dark and still or crouched, no suspicion at all.
                        new Band {
                            name = "3.75-6m", length = 2.25f, lit = closeLit,
                            dark = new Fill(6f, 0f, 0f, 2f, 2.5f, 1f, 3f, 1f, 0.05f)
                        }
                    }
                },
                new Zone {
                    // About 58 degrees across, then a 10m-wide corridor.
                    name = "Direct Line Of Sight",
                    range = 30f,
                    halfAngle = 29f, maxHalfWidth = 5f,
                    upAngle = 12.5f, maxAbove = 2f,
                    downAngle = 24f, maxBelow = 4f,
                    narrowFrom = 30f, endHalfWidth = 5f, endAbove = 2f, endBelow = 4f,
                    bands = new[] {
                        new Band {
                            name = "0-9m", length = 9f,
                            lit = new Fill(0.4f, 1f, 1f, 1.5f, 1f, 1f, 3f, 1f, 0.5f),
                            dark = Fill.Never(1.415f, 0.5f)
                        },
                        new Band {
                            name = "9-20m (lit only)", length = 11f,
                            lit = new Fill(1f, 1f, 1f, 2f, 1f, 1f, 3f, 1f, 0.5f),
                            dark = Fill.Never(1.3f, 1f)
                        },
                        new Band {
                            name = "20-30m (lit only)", length = 10f,
                            lit = new Fill(1f, 1f, 1f, 2.5f, 1f, 1f, 3f, 1f, 0.5f),
                            dark = Fill.Never(1.3f, 1f)
                        }
                    }
                },
                new Zone {
                    // The view frustum: every other zone sits inside it.
                    name = "Peripheral",
                    range = 30f,
                    halfAngle = 77f, maxHalfWidth = 12.5f,
                    upAngle = 49.5f, maxAbove = 2f,
                    downAngle = 76f, maxBelow = 6.8f,
                    narrowFrom = 30f, endHalfWidth = 12.5f, endAbove = 2f, endBelow = 6.8f,
                    bands = new[] {
                        new Band {
                            name = "0-20m (lit only)", length = 20f,
                            lit = new Fill(3f, 1.5f, 1f, 3f, 1.5f, 1f, 3f, 1f, 0.25f),
                            dark = Fill.Never(1.315f, 1f)
                        },
                        new Band {
                            name = "20-30m (lit only)", length = 10f,
                            lit = new Fill(4f, 1.5f, 1f, 3.75f, 1.5f, 1f, 3f, 1f, 0.33f),
                            dark = Fill.Never(1.315f, 1f)
                        }
                    }
                }
            };
        }
    }
}
