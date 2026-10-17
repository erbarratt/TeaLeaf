using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The street test, run after a generation: a ball of StreetBallRadius
    /// travels down the middle of every street from its first point to its
    /// last, its centre StreetBallHeight above the street's surface (up and
    /// down the flights of steps as walked), stopping its radius short of a
    /// dead end's facade. The layout is valid only if the ball never touches
    /// anything the generator built for the buildings (any collider under the
    /// generated root but the streets' own ground and steps). Each street that
    /// is blocked is reported with what blocks it and where.
    /// </summary>
    internal static class StreetTest
    {
        // How far the ball moves between checks, as a share of its radius.
        private const float StepShare = 0.5f;
        // The most blockers reported per street.
        private const int MostReported = 5;

        private static readonly Collider[] _hits = new Collider[32];

        /// <summary>
        /// Runs the test over a generated root whose streets' ground is under
        /// streetsRoot. Returns whether every street is clear; adds a line per
        /// blocked street to report.
        /// </summary>
        internal static bool Run(LayoutSpec spec, List<StreetSteps.Flight> flights, StoreyPlan plan, Transform root, Transform streetsRoot,
            GeneratorSettings settings, List<string> report)
        {
            Physics.SyncTransforms();
            float radius = settings.StreetBallRadius;
            float step = radius * StepShare;
            int blocked = 0;

            foreach (StreetSpec street in spec.streets) {
                List<string> blockers = new();

                Walk(street, flights, plan, settings, step, radius + settings.OverlapTolerance, (surface, segment, travelled) => {
                    Vector3 centre = surface + Vector3.up * settings.StreetBallHeight;
                    int count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, ~0, QueryTriggerInteraction.Ignore);

                    for (int h = 0; h < count; h++) {
                        Transform hit = _hits[h].transform;

                        if (!hit.IsChildOf(root) || hit.IsChildOf(streetsRoot)) {
                            continue;
                        }

                        string name = Describe(hit, root);

                        if (blockers.Count < MostReported && !blockers.Exists(line => line.StartsWith(name))) {
                            blockers.Add($"{name}, {travelled:0.0}m along (segment {segment})");
                        }
                    }
                });

                if (blockers.Count > 0) {
                    blocked++;
                    report.Add($"Street '{street.id}' is blocked: {string.Join("; ", blockers)}.");
                }
            }

            report.Insert(0, blocked == 0
                ? $"Street test: a ball of radius {radius}m at {settings.StreetBallHeight}m travels every street end to end untouched: the layout is valid."
                : $"Street test FAILED: {blocked} of {spec.streets.Count} street(s) blocked by the buildings.");
            return blocked == 0;
        }

        /// <summary>
        /// Walks a street's middle line from its first point to its last,
        /// every step metres, calling visit with the point on the street's
        /// surface there (up and down the flights of steps as walked), the
        /// segment and the distance along the street. Stops endGap short of
        /// a dead end's facade, which stands across the end point.
        /// </summary>
        internal static void Walk(StreetSpec street, List<StreetSteps.Flight> flights, StoreyPlan plan, GeneratorSettings settings, float step,
            float endGap, System.Action<Vector3, int, float> visit)
        {
            float travelled = 0f;

            for (int i = 0; i + 1 < street.points.Count; i++) {
                Vector2 a = Ground(street.points[i]);
                Vector2 b = Ground(street.points[i + 1]);
                float length = (b - a).magnitude;

                if (length < settings.MinSegmentLength) {
                    continue;
                }

                Vector2 axis = (b - a) / length;
                StreetSteps.Flight flight = StreetSteps.Of(flights, street, i);
                float from = i == 0 && IsDeadEnd(plan, street, 0) ? endGap : 0f;
                float to = i + 2 == street.points.Count && IsDeadEnd(plan, street, i + 1) ? length - endGap : length;

                for (float along = from; along <= to; along += step) {
                    float surface = flight != null ? flight.SurfaceAt(along) : Mathf.Lerp(street.points[i].y, street.points[i + 1].y, along / length);
                    Vector2 at = a + axis * along;
                    visit(new Vector3(at.x, surface, at.y), i, travelled + along);
                }

                travelled += length;
            }
        }

        /// <summary>
        /// Whether a street's end point is a dead end closed by a facade.
        /// </summary>
        private static bool IsDeadEnd(StoreyPlan plan, StreetSpec street, int point)
        {
            return plan.DeadEnds.Exists(end => end.Street == street && end.Point == point);
        }

        /// <summary>
        /// A collider's piece, named by its path from the generated root down
        /// to the placed prefab instance.
        /// </summary>
        private static string Describe(Transform hit, Transform root)
        {
            GameObject piece = UnityEditor.PrefabUtility.GetOutermostPrefabInstanceRoot(hit.gameObject);
            Transform named = piece != null ? piece.transform : hit;
            string path = named.name;

            for (Transform parent = named.parent; parent != null && parent != root; parent = parent.parent) {
                path = parent.name + "/" + path;
            }

            return path;
        }

        /// <summary>
        /// A point on the ground: its x and z.
        /// </summary>
        private static Vector2 Ground(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }
    }
}
