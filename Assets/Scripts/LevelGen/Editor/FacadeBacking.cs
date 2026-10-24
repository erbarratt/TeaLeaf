using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    internal static partial class FacadeBuilder
    {
        // The boxes behind the facades worked out by the last build, for
        // Backing to cut clear of the pieces and place once everything stands.
        private static readonly List<Backing.Box> _backings = new();
        internal static List<Backing.Box> Backings => _backings;

        /// <summary>
        /// Works out a black box behind every stretch of a facade or dead end
        /// that stands at one depth, one height and one level (a column, or
        /// the part of one on one side of a flight of steps): along the
        /// stretch, from just behind its walls' back line back BackingDepth
        /// (less where a street is nearer behind it: it stops the walls' depth
        /// short of that street, and its ends stop as far short of any street
        /// beside them), from BackingBelow under the lowest street in front up
        /// to the eaves (the top storey's top less RoofEaveDrop). Lots are
        /// built whole and get none. Backing cuts them clear of the pieces.
        /// </summary>
        private static void CollectBackings(List<Frontage> frontages, Dictionary<string, BuildingStyle> styles, GeneratorSettings settings,
            PiecePlacer placer)
        {
            _backings.Clear();

            foreach (Frontage frontage in frontages) {
                if (frontage.Lot != null) {
                    continue;
                }

                // A dead end's frontage carries no setback of its own: its
                // walls stand back from the end as its building's styles say.
                float setback = frontage.Street != null ? frontage.WallSetback : Setback(new[] { frontage.Buildings[0].Spec }, styles, placer);
                List<ModuleSpot> spots = frontage.Spots;
                int first = 0;

                for (int k = 1; k <= spots.Count; k++) {
                    if (k < spots.Count && SameBacking(spots[first], spots[k], frontage)) {
                        continue;
                    }

                    AddBacking(frontage, first, k - 1, setback, settings);
                    first = k;
                }
            }
        }

        /// <summary>
        /// Whether two neighbouring modules stand behind one box: one run, one
        /// building and column, one depth, as many storeys, on one level.
        /// </summary>
        private static bool SameBacking(ModuleSpot a, ModuleSpot b, Frontage frontage)
        {
            return a.Run == b.Run && a.Building == b.Building && a.Column == b.Column && Mathf.Abs(a.Depth - b.Depth) < SameDepth && a.Storeys == b.Storeys
                && Mathf.Abs(SpotGround(a, frontage.Runs, frontage.Buildings) - SpotGround(b, frontage.Runs, frontage.Buildings)) < SameDepth;
        }

        /// <summary>
        /// The box behind modules first to last of a frontage (one stretch,
        /// SameBacking), cut to clear the streets; none if nothing is left.
        /// </summary>
        private static void AddBacking(Frontage frontage, int first, int last, float setback, GeneratorSettings settings)
        {
            ModuleSpot a = frontage.Spots[first];
            ModuleSpot b = frontage.Spots[last];
            Run run = frontage.Runs[a.Run];
            float start = a.Position;
            float end = b.Position + Spacing(b, frontage.Buildings);
            // Behind the street edge, where the walls' back line stands.
            float near = setback - a.Depth;
            // A street behind: the box stops the walls' depth short of it, so
            // it stands behind where that street's facade stands.
            float depth = settings.BackingDepth;
            int samples = Mathf.Max(2, Mathf.CeilToInt(end - start) + 1);

            for (int s = 0; s < samples; s++) {
                Vector2 onLine = run.Start + run.Along * Mathf.Lerp(start, end, (float)s / (samples - 1)) - run.Front * near;
                depth = Mathf.Min(depth, _clearance.Reach(onLine, -run.Front) - setback);
            }

            // Its ends stop as far short of a street beside it (a branch, the
            // street round a corner).
            if (depth < settings.BackingClearance * 2f
                || !_clearance.Clear(run.Street, run.Segment, run.Start, run.Along, -run.Front, -near, near + depth, setback, ref start, ref end)) {
                return;
            }

            float lowest = float.MaxValue;

            for (int k = first; k <= last; k++) {
                lowest = Mathf.Min(lowest, LowestInFront(run, frontage.Spots[k], Spacing(frontage.Spots[k], frontage.Buildings)));
            }

            float top = RoofTop(a, frontage) - settings.RoofEaveDrop;
            float bottom = lowest - settings.BackingBelow;

            if (top - bottom < settings.BackingClearance * 2f) {
                return;
            }

            Vector2 corner = run.Start + run.Along * start - run.Front * near;
            _backings.Add(new Backing.Box {
                Name = $"{frontage.Label}, modules {first}-{last}",
                Corner = new Vector3(corner.x, bottom, corner.y),
                Along = Level(run.Along),
                Back = Level(-run.Front),
                Length = end - start,
                Depth = depth,
                Height = top - bottom
            });
        }
    }
}
