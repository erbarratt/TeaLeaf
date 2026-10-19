using System.Collections.Generic;
using Core;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Shafts of moonlight into the narrow streets: the strip of sky over
    /// an alley or lane, between the eaves of the buildings either side, is
    /// the opening all the moonlight that reaches it comes through, and a
    /// <see cref="MoonShaft"/> is laid across it. Each shaft works out its
    /// own beam from the moon's direction - cutting the strip down to the
    /// part the moon reaches past the roof on the moon's side, following
    /// the light from there down to the far wall and the street - so this
    /// pass only has to find the streets and size the openings.
    ///
    /// Every segment of a street no wider than the settings' street width
    /// with a building on both sides gets one: a level rectangle at the
    /// height of the lower side's eaves (the wall tops found from the placed
    /// pieces along each edge, less the roofs' eave drop), running the
    /// segment's length (shortened at a junction by half the wider street,
    /// so it stops at the corner buildings), across the street from one
    /// edge to the other, each side held in by the settings' inset so its
    /// rays never start inside a wall. The opening's local Y runs across
    /// the street away from the moon, so the shaft's shadow fit starts on
    /// the moon's side. Runs after every piece and collider stands (the
    /// beams end on them) and after the visibility cull (which never sees
    /// these), under Generated/Moon Shafts.
    /// </summary>
    internal static class MoonShafts
    {
        // How far apart along a street's edge the walls beside it are sampled
        // for their tops.
        private const float WallSampleSpacing = 2f;

        /// <summary>
        /// Lays a shaft over every narrow street segment. One log line.
        /// </summary>
        internal static void Build(LayoutSpec spec, GeneratorSettings settings, Transform root, List<string> log)
        {
            if (settings.MoonShaftStreetMaxWidth <= 0f) {
                log.Add("Moon shafts: none (street width 0).");
                return;
            }

            Moonlight moon = Object.FindAnyObjectByType<Moonlight>();

            if (moon == null) {
                log.Add("Moon shafts: none (the scene has no Moonlight).");
                return;
            }

            // The light's level direction: the opening's across axis points
            // the way the light travels, so its near edge is on the moon's side.
            Vector3 lightLevel = -moon.DirectionToMoon;
            lightLevel.y = 0f;
            Vector2 lightWay = lightLevel.sqrMagnitude > 1e-6f ? new Vector2(lightLevel.x, lightLevel.z).normalized : Vector2.up;

            // The walls' tops are read from the pieces standing along each
            // edge: the facades and the lots, not the roofs (the eaves lie
            // below the wall tops by the eave drop) nor the ground.
            List<Bounds> walls = new();
            GatherBounds(root.Find("Facades"), walls);
            GatherBounds(root.Find("Lots"), walls);

            Transform group = new GameObject("Moon Shafts").transform;
            group.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Generate");
            Physics.SyncTransforms();

            int segments = 0;
            int placed = 0;
            int shadowed = 0;
            int oneSided = 0;
            int tooLow = 0;
            int tooShort = 0;

            foreach (StreetSpec street in spec.streets) {
                // A narrow street is one the spec makes so (an alley's own
                // width), judged on its base width rather than the rolled
                // segment widths, which vary either way round it.
                float baseWidth = street.width > 0f ? street.width : settings.StreetWidth;

                if (baseWidth > settings.MoonShaftStreetMaxWidth) {
                    continue;
                }

                for (int segment = 0; segment < street.points.Count - 1; segment++) {
                    float streetWidth = street.SegmentWidth(segment);

                    Vector3 a = street.points[segment];
                    Vector3 b = street.points[segment + 1];
                    Vector2 groundA = Ground(a);
                    Vector2 groundB = Ground(b);
                    Vector2 along = groundB - groundA;
                    float length = along.magnitude;

                    if (length < settings.MinSegmentLength) {
                        continue;
                    }

                    along /= length;
                    segments++;

                    // Stop short of the corner buildings at a junction.
                    float startCut = JunctionWidth(street, groundA, spec.streets, settings) * 0.5f;
                    float endCut = JunctionWidth(street, groundB, spec.streets, settings) * 0.5f;
                    float openingLength = length - startCut - endCut;
                    float openingSpan = streetWidth - 2f * settings.MoonShaftInset;

                    if (openingLength < 1f || openingSpan < 0.5f) {
                        tooShort++;
                        continue;
                    }

                    // The two edges, and the top of the walls along each.
                    Vector2 side = new(along.y, -along.x);
                    Vector2 from = groundA + along * startCut;
                    float streetLevel = Mathf.Min(a.y, b.y);
                    float leftTop = EdgeWallTop(from - side * (streetWidth * 0.5f), along, openingLength, streetLevel, walls, settings);
                    float rightTop = EdgeWallTop(from + side * (streetWidth * 0.5f), along, openingLength, streetLevel, walls, settings);

                    if (float.IsNaN(leftTop) || float.IsNaN(rightTop)) {
                        oneSided++;
                        continue;
                    }

                    float eaves = Mathf.Min(leftTop, rightTop) - settings.RoofEaveDrop;

                    if (eaves - Mathf.Max(a.y, b.y) < settings.MoonShaftMinEaves) {
                        tooLow++;
                        continue;
                    }

                    // Across the street the way the light travels, so the
                    // opening's foot edge is on the moon's side.
                    Vector2 across = Vector2.Dot(side, lightWay) >= 0f ? side : -side;
                    Vector2 middle = from + along * (openingLength * 0.5f);
                    Vector2 foot = middle - across * (openingSpan * 0.5f);
                    Vector3 alongWorld = new(along.x, 0f, along.y);
                    Vector3 acrossWorld = new(across.x, 0f, across.y);

                    var shaftObject = new GameObject($"Moon Shaft - {street.id} {segment}-{segment + 1}");
                    Undo.RegisterCreatedObjectUndo(shaftObject, "Generate");
                    shaftObject.transform.SetParent(group, false);
                    // Local X along the street, local Y across it away from
                    // the moon, local Z straight up or down: a level opening.
                    shaftObject.transform.SetPositionAndRotation(
                        new Vector3(foot.x, eaves, foot.y),
                        Quaternion.LookRotation(Vector3.Cross(alongWorld, acrossWorld), acrossWorld));
                    MoonShaft shaft = shaftObject.AddComponent<MoonShaft>();
                    shaft.Configure(openingLength, openingSpan, settings.MoonShaftMaxLength);

                    if (shaft.IsLit) {
                        placed++;
                    } else {
                        // The moon reaches none of the opening: nothing to
                        // draw, so nothing to keep.
                        shadowed++;
                        Undo.DestroyObjectImmediate(shaftObject);
                    }
                }
            }

            log.Add($"Moon shafts: {placed} over {segments} narrow street segment(s) ({shadowed} in the moon's shadow, {oneSided} without a building on both sides, {tooLow} with eaves under {settings.MoonShaftMinEaves}m, {tooShort} too short).");
        }

        /// <summary>
        /// The width of the widest other street leaving a point of this
        /// street, or 0 where none does (a dead end, or a point along the
        /// street no other shares).
        /// </summary>
        private static float JunctionWidth(StreetSpec street, Vector2 point, List<StreetSpec> streets, GeneratorSettings settings)
        {
            float width = 0f;

            foreach (StreetSpec other in streets) {
                if (other == street) {
                    continue;
                }

                for (int j = 0; j < other.points.Count; j++) {
                    if ((Ground(other.points[j]) - point).magnitude > settings.JunctionDistance) {
                        continue;
                    }

                    for (int n = j - 1; n <= j + 1; n += 2) {
                        if (n >= 0 && n < other.points.Count && (Ground(other.points[n]) - point).magnitude >= settings.MinSegmentLength) {
                            width = Mathf.Max(width, other.SegmentWidth(Mathf.Min(j, n)));
                        }
                    }
                }
            }

            return width;
        }

        /// <summary>
        /// The top of the highest wall along a street edge: the edge is
        /// sampled every couple of metres and each sample looks for walls
        /// within the settings' probe distance. NaN where none stands along
        /// it.
        /// </summary>
        private static float EdgeWallTop(Vector2 start, Vector2 along, float length, float streetLevel, List<Bounds> walls, GeneratorSettings settings)
        {
            float top = float.NaN;
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / WallSampleSpacing) + 1);

            for (int i = 0; i < samples; i++) {
                float wallTop = WallTop(start + along * (length * i / (samples - 1)), streetLevel, walls, settings);

                if (!float.IsNaN(wallTop) && (float.IsNaN(top) || wallTop > top)) {
                    top = wallTop;
                }
            }

            return top;
        }

        /// <summary>
        /// The top of the highest wall standing within the settings' probe
        /// distance of a point on the ground (level distance to the piece's
        /// box), ignoring anything far above or wholly below the street;
        /// NaN where nothing stands there.
        /// </summary>
        private static float WallTop(Vector2 point, float streetLevel, List<Bounds> walls, GeneratorSettings settings)
        {
            float top = float.NaN;
            float probe = settings.MoonShaftWallProbe;

            foreach (Bounds bounds in walls) {
                if (bounds.min.y > streetLevel + 20f || bounds.max.y < streetLevel) {
                    continue;
                }

                float dx = Mathf.Max(bounds.min.x - point.x, 0f, point.x - bounds.max.x);
                float dz = Mathf.Max(bounds.min.z - point.y, 0f, point.y - bounds.max.z);

                if (dx * dx + dz * dz > probe * probe) {
                    continue;
                }

                if (float.IsNaN(top) || bounds.max.y > top) {
                    top = bounds.max.y;
                }
            }

            return top;
        }

        /// <summary>
        /// The world boxes of every renderer under a group.
        /// </summary>
        private static void GatherBounds(Transform group, List<Bounds> into)
        {
            if (group == null) {
                return;
            }

            foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>()) {
                into.Add(renderer.bounds);
            }
        }

        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }
    }
}
