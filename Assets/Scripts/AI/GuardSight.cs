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
            [Tooltip("Seconds of being seen to fill the suspicion meter from empty (full = Suspicious). e.g. 1 = a second in view makes the guard stop and look. Higher = slower to suspect; below zero = never.")]
            public float suspicionTime;
            [Tooltip("Multiplies Suspicion Time while the player stands still (in place of the moving multipliers). e.g. 2.5 = two and a half times slower when still; below 1 = faster. 0 = never fills while still.")]
            public float suspicionStillMultiplier;
            [Tooltip("Multiplies Suspicion Time while the player is crouched, on top of the still or moving multiplier. e.g. 1.5 = half as slow again crouched. 0 = never fills while crouched.")]
            public float suspicionCrouchMultiplier;
            [Tooltip("Seconds of being seen to fill the detection meter from empty (full = chase). e.g. 3 = three seconds in view and the guard gives chase. Higher = slower to detect; below zero = never.")]
            public float detectionTime;
            [Tooltip("Multiplies Detection Time while the player stands still (in place of the moving multipliers). e.g. 2.5 = two and a half times slower when still. 0 = never fills while still.")]
            public float detectionStillMultiplier;
            [Tooltip("Multiplies Detection Time while the player is crouched, on top of the still or moving multiplier. e.g. 1.5 = half as slow again crouched. 0 = never fills while crouched.")]
            public float detectionCrouchMultiplier;
            [Tooltip("Faster than this, in metres a second, the player counts as moving fast and Fast Multiplier applies instead of Moving Multiplier. e.g. 3 = sprinting. Lower = walking counts as fast.")]
            public float fastSpeed;
            [Tooltip("Multiplies both times while the player moves (not still) at up to Fast Speed. e.g. 0.5 = fills twice as fast as the base times. Higher = movement is noticed less; 0 = never fills while moving.")]
            public float movingMultiplier;
            [Tooltip("Multiplies both times while the player moves faster than Fast Speed. e.g. 0.02 = fifty times faster, nearly instant. Higher = running is noticed less; 0 = never fills while moving fast.")]
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
            [Tooltip("A label for the band, shown in the list; nothing reads it. e.g. \"0-3m\".")]
            public string name;
            [Tooltip("How long the band is, in metres, starting where the one before ends (the first starts at the eye). e.g. 3 after a 3m band = covers 3-6m. Higher = its fill times hold over more distance.")]
            public float length;
            [Tooltip("Fill times for a player in light (Visibility at least Lit Threshold), e.g. stood in moonlight.")]
            public Fill lit;
            [Tooltip("Fill times for a player in the dark (Visibility below Lit Threshold), e.g. crouched in shadow. Set the times below zero for a band where the dark is never seen.")]
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
            [Tooltip("A label for the zone, shown in the list; nothing reads it. e.g. \"Very Close\" or \"Peripheral\".")]
            public string name;
            [Tooltip("How far ahead of the eye the zone reaches, in metres (depth straight ahead). e.g. 30 = sees across a wide street. Higher = sees further.")]
            public float range;
            [Tooltip("Degrees either side of straight ahead the zone opens at, until it reaches Max Half Width. e.g. 29 = about 58 degrees across. Higher = a wider view.")]
            [Range(0f, 89f)] public float halfAngle;
            [Tooltip("The widest the zone gets either side of straight ahead, in metres, however far out. e.g. 5 = a corridor 10m wide past where the angle reaches it. Higher = wider far off.")]
            public float maxHalfWidth;
            [Tooltip("Degrees above straight ahead the zone opens at, until it reaches Max Above. e.g. 20. Higher = sees more of what is above it (balconies, roofs).")]
            [Range(0f, 89f)] public float upAngle;
            [Tooltip("The highest the zone gets above the eye, in metres. e.g. 2 = a player on a ledge 2m over the guard's head is above the zone. Higher = sees higher up.")]
            public float maxAbove;
            [Tooltip("Degrees below straight ahead the zone opens at, until it reaches Max Below. e.g. 40. Higher = sees more of the ground close in front.")]
            [Range(0f, 89f)] public float downAngle;
            [Tooltip("The lowest the zone gets below the eye, in metres. e.g. 4 = sees down into a sunken yard up to 4m below the eye. Higher = sees further down.")]
            public float maxBelow;
            [Tooltip("From this far ahead, in metres, the width, height and depth limits ease to the End values by Range. e.g. 1.8 in a 3m zone = it tapers over its last 1.2m. At Range or more: no narrowing.")]
            public float narrowFrom;
            [Tooltip("Half width at Range, in metres, when narrowing (from Narrow From the width eases to this). e.g. 0.5 = the zone's far end is 1m across.")]
            public float endHalfWidth;
            [Tooltip("Height above the eye at Range, in metres, when narrowing. e.g. 0.5 = the zone's far end reaches half a metre above the eye.")]
            public float endAbove;
            [Tooltip("Depth below the eye at Range, in metres, when narrowing. e.g. 1.25 = the zone's far end reaches 1.25m below the eye.")]
            public float endBelow;
            [Tooltip("The zone's distance bands, one entry per band, nearest the eye first, each with its own lit and dark fill times. Beyond the last, the last one's times hold.")]
            public Band[] bands;
        }

        [Tooltip("The vision zones, one entry per zone, the widest (the view frustum) last, e.g. Very Close, Close, Direct Line Of Sight, Peripheral. Where they overlap, the fastest fill wins.")]
        [SerializeField] private Zone[] zones;

        // The player counts as lit at this Visibility or more, dark below
        // it: which side of each band's table applies. Visibility already
        // includes crouching (PlayerVisibility), so a player crouched in
        // moonlight drops into the dark side.
        [Tooltip("The player counts as lit at this Visibility (0-1) or more, else dark: picks each band's Lit or Dark fill times. Visibility includes crouching, e.g. at 0.3 crouching in moonlight (0.24) counts as dark. Higher = more of the level counts as dark; lower = more as lit.")]
        [Range(0f, 1f)]
        [SerializeField] private float litThreshold = 0.3f;

        // The player's test points that show (PlayerVisibility weights)
        // must add up to this to be seen: the body, or the head seen from
        // below plus a little more.
        [Tooltip("The weights of the player's test points in view (set on PlayerVisibility: head points 0.17 each, under the head 0.5, waist 0.8) must add up to at least this to be seen. e.g. 0.5 = the body, or the head seen from below and a little more. Higher = more of the player must show.")]
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
        /// lit or dark, still or moving at a speed, crouched or not: the
        /// fastest of
        /// every zone the point is in, each meter on its own. A time below
        /// zero is never. Returns whether either meter can fill at all -
        /// false outside every zone, or where every zone it is in can't
        /// see it (so the guard needn't cast a ray). Fastest Zone is the
        /// index of the zone giving the fastest time of either meter (-1
        /// if none can fill), for the gizmo.
        /// </summary>
        public bool TryGetFillTimes(Vector3 local, bool isLit, bool isStill, float speed, bool isCrouched, out float suspicionTime, out float detectionTime, out int fastestZone)
        {
            suspicionTime = -1f;
            detectionTime = -1f;
            fastestZone = -1;

            if (zones == null) {
                return false;
            }

            float distance = local.magnitude;
            float fastestTime = -1f;

            for (int i = 0; i < zones.Length; i++) {
                Zone zone = zones[i];

                if (zone.bands == null || zone.bands.Length == 0 || !IsInside(zone, local)) {
                    continue;
                }

                GetBandTimes(zone.bands, distance, isLit, isStill, speed, isCrouched, out float suspicion, out float detection);
                suspicionTime = Fastest(suspicionTime, suspicion);
                detectionTime = Fastest(detectionTime, detection);

                // The zone filling either meter soonest so far.
                float zoneTime = Fastest(suspicion, detection);

                if (zoneTime >= 0f && (fastestTime < 0f || zoneTime < fastestTime)) {
                    fastestTime = zoneTime;
                    fastestZone = i;
                }
            }

            return suspicionTime >= 0f || detectionTime >= 0f;
        }

        /// <summary>
        /// The fill times in a zone at a distance: the band the distance
        /// falls in, its times blended towards the next band's across it
        /// (unless either is never), then multiplied for how the player
        /// moves (that band's multipliers).
        /// </summary>
        private void GetBandTimes(Band[] bands, float distance, bool isLit, bool isStill, float speed, bool isCrouched, out float suspicionTime, out float detectionTime)
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

            // How the player moves: standing still (the stick let go,
            // PlayerVisibility.IsStill), or moving (fast or not); and
            // crouched or not.
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
        /// starts, its far end) and the four edges joining them. Zones
        /// are drawn in the lines' colour, but the one at Highlight Zone
        /// (an index; -1 = none) in Highlight Color.
        /// </summary>
        public void DrawZones(DebugLines lines, Vector3 eyePosition, Quaternion eyeRotation, int highlightZone, Color highlightColor)
        {
            if (zones == null) {
                return;
            }

            Color baseColor = lines.Color;

            for (int zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++) {
                Zone zone = zones[zoneIndex];
                lines.Color = zoneIndex == highlightZone ? highlightColor : baseColor;

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

            lines.Color = baseColor;
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
