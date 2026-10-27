using System.Collections.Generic;
using Core;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Shafts of moonlight in the streets: the moonlit air beside each
    /// building's moon shadow. First it finds which buildings cast a moon
    /// shadow onto a street: along every street segment no wider than the
    /// settings' street width, every sample spacing, the street is measured
    /// across from its edge on the moon's side with physics rays towards the
    /// moon (the rays gameplay's moonlight uses), finding where the shadow
    /// there ends and the moonlit strip of street begins (and ends, where a
    /// building on the far side shades it again); the ray blocked at the
    /// shadow's edge names the building casting it. Then each run of samples
    /// shadowed by one building gets one <see cref="GodRayBox"/>: its bottom
    /// four corners on the street round that lit strip, its top four
    /// straight up the moonlight from them to just above the building - so
    /// the shaft's side nearest the building grazes the edge casting the
    /// shadow and its sides all run the way the light comes. A sample whose
    /// street edge on the moon's side is lit (no building shades it) or
    /// whose whole width is in shadow gets nothing.
    ///
    /// Shafts are the same component as hand-placed ones (corners can be
    /// adjusted by hand; a regeneration replaces them). Runs after every
    /// piece and collider stands (the rays need them) and after the
    /// visibility cull (which never sees these), under Generated/Moon Shafts.
    /// </summary>
    internal static class MoonShafts
    {
        // The moon must shine down into the streets: below this height of
        // its direction (about 12 degrees up), none are placed.
        private const float MinThrough = 0.2f;

        // How far above the street surface the shadow rays start, and how
        // far a building's box is grown when looking for the one a ray hit.
        private const float Clearance = 0.1f;

        // How many points across the street are tried before the shadow's
        // edges are found by halving: enough to find a lit strip between
        // shadows from both sides.
        private const int AcrossSamples = 8;

        // How many times the search for a shadow's edge halves its range: 6
        // gives about a sixtieth of the street's width.
        private const int FitSteps = 6;

        // How far towards the moon a ray looks for something in the way.
        private const float MoonReach = 200f;

        // The layers that block moonlight (as gameplay's light rays).
        private static int _lightMask;

        /// <summary>
        /// One building of the level: its name, as its group's path under
        /// the root, and the box round its pieces and roof.
        /// </summary>
        private struct BuildingBox
        {
            public string Name;
            public Bounds Bounds;
        }

        /// <summary>
        /// One sample across a street: the stretch of the segment it stands
        /// for, the building whose shadow ends where the moonlit strip
        /// starts, and that strip as shares of the street's width from its
        /// edge on the moon's side.
        /// </summary>
        private struct Sample
        {
            public float From;
            public float To;
            public int Building;
            public float Near;
            public float Far;
        }

        /// <summary>
        /// Lays a shaft beside each building's moon shadow in the streets.
        /// One log line.
        /// </summary>
        internal static void Build(LayoutSpec spec, StreetHeights heights, GeneratorSettings settings, Transform root, List<string> log)
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

            Vector3 toMoon = moon.DirectionToMoon.normalized;

            if (toMoon.y < MinThrough) {
                log.Add("Moon shafts: none (the moon is too low to shine down into a street).");
                return;
            }

            // The light's level direction, the way it travels across a street.
            Vector2 lightWay = new Vector2(-toMoon.x, -toMoon.z);
            lightWay = lightWay.sqrMagnitude > 1e-6f ? lightWay.normalized : Vector2.up;

            _lightMask = LayerMask.GetMask("Environment", "Interactable");
            List<BuildingBox> buildings = Buildings(root);

            Transform group = new GameObject("Moon Shafts").transform;
            group.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Generate");
            Physics.SyncTransforms();

            int segments = 0;
            int samples = 0;
            int lit = 0;
            int dark = 0;
            int notBuilding = 0;
            int placed = 0;
            var casters = new HashSet<int>();

            foreach (StreetSpec street in spec.streets) {
                float baseWidth = street.width > 0f ? street.width : settings.StreetWidth;

                if (baseWidth > settings.MoonShaftStreetMaxWidth) {
                    continue;
                }

                for (int segment = 0; segment < street.points.Count - 1; segment++) {
                    Vector2 groundA = Ground(street.points[segment]);
                    Vector2 groundB = Ground(street.points[segment + 1]);
                    Vector2 along = groundB - groundA;
                    float length = along.magnitude;

                    if (length < settings.MinSegmentLength) {
                        continue;
                    }

                    along /= length;

                    // Junctions belong to no one segment: stop short of them
                    // by half the widest street leaving there.
                    float startCut = JunctionWidth(street, groundA, spec.streets, settings) * 0.5f;
                    float endCut = JunctionWidth(street, groundB, spec.streets, settings) * 0.5f;
                    float usable = length - startCut - endCut;

                    if (usable < settings.MoonShaftSampleSpacing) {
                        continue;
                    }

                    segments++;

                    // Across the street from its edge on the moon's side to
                    // the far edge, held in from both walls.
                    Vector2 side = new(along.y, -along.x);
                    Vector2 fromMoon = Vector2.Dot(side, lightWay) >= 0f ? side : -side;
                    float halfSpan = street.SegmentWidth(segment) * 0.5f - settings.MoonShaftInset;

                    if (halfSpan <= 0f) {
                        continue;
                    }

                    // Each sample stands for an even share of the segment.
                    int count = Mathf.Max(1, Mathf.RoundToInt(usable / settings.MoonShaftSampleSpacing));
                    float share = usable / count;
                    var run = new List<Sample>();

                    for (int k = 0; k <= count; k++) {
                        Sample sample = default;
                        bool keep = false;

                        if (k < count) {
                            samples++;
                            float middle = startCut + share * (k + 0.5f);
                            Vector2 centre = groundA + along * middle;
                            float height = heights.SurfaceAt(street, segment, middle) + Clearance;
                            Vector3 moonEdge = new(centre.x - fromMoon.x * halfSpan, height, centre.y - fromMoon.y * halfSpan);
                            Vector3 across = new Vector3(fromMoon.x, 0f, fromMoon.y) * (halfSpan * 2f);

                            switch (Measure(moonEdge, across, toMoon, buildings, out sample)) {
                                case Shade.Lit: lit++; break;
                                case Shade.Dark: dark++; break;
                                case Shade.NotBuilding: notBuilding++; break;
                                default: keep = true; break;
                            }

                            sample.From = startCut + share * k;
                            sample.To = sample.From + share;
                        }

                        // A run of samples shaded by one building ends where
                        // the next is shaded by another, or by none.
                        if (run.Count > 0 && (!keep || sample.Building != run[0].Building)) {
                            casters.Add(run[0].Building);
                            PlaceShaft(street, segment, groundA, along, fromMoon, halfSpan, run, buildings[run[0].Building], toMoon, heights, settings, group);
                            placed++;
                            run.Clear();
                        }

                        if (keep) {
                            run.Add(sample);
                        }
                    }
                }
            }

            log.Add($"Moon shafts: {placed} beside {casters.Count} building(s) casting a moon shadow onto {segments} street segment(s); of {samples} sample(s) across, {lit} lit at the moon's side (no shadow), {dark} wholly in shadow, {notBuilding} shaded by something not a building.");
        }

        private enum Shade { Shaft, Lit, Dark, NotBuilding }

        /// <summary>
        /// Measures one line across the street, from its edge on the moon's
        /// side (moonEdge) to its far edge (moonEdge + across): where the
        /// shadow cast from the moon's side ends and the moonlit strip
        /// begins, where that strip ends (the far edge, or a shadow from the
        /// far side), and which building casts the first shadow.
        /// </summary>
        private static Shade Measure(Vector3 moonEdge, Vector3 across, Vector3 toMoon, List<BuildingBox> buildings, out Sample sample)
        {
            sample = default;

            if (Blocker(moonEdge, toMoon) == null) {
                return Shade.Lit;
            }

            // The first lit point across, then the shadow's edge between it
            // and the last shadowed one.
            int firstLit = -1;

            for (int i = 1; i <= AcrossSamples; i++) {
                if (Blocker(moonEdge + across * (i / (float)AcrossSamples), toMoon) == null) {
                    firstLit = i;
                    break;
                }
            }

            if (firstLit < 0) {
                return Shade.Dark;
            }

            float shadowed = (firstLit - 1) / (float)AcrossSamples;
            float litAt = firstLit / (float)AcrossSamples;

            for (int step = 0; step < FitSteps; step++) {
                float middle = (shadowed + litAt) * 0.5f;

                if (Blocker(moonEdge + across * middle, toMoon) == null) {
                    litAt = middle;
                } else {
                    shadowed = middle;
                }
            }

            // The building whose edge casts the shadow: the one the ray from
            // just inside the shadow hits.
            Collider caster = Blocker(moonEdge + across * shadowed, toMoon, out Vector3 hitPoint);
            sample.Building = caster != null ? BuildingAt(hitPoint, buildings) : -1;

            if (sample.Building < 0) {
                return Shade.NotBuilding;
            }

            sample.Near = litAt;

            // The strip ends at the far edge, or where a shadow from the far
            // side begins: the first shadowed point past the strip.
            float stripEnd = 1f;

            for (int i = firstLit + 1; i <= AcrossSamples; i++) {
                if (Blocker(moonEdge + across * (i / (float)AcrossSamples), toMoon) == null) {
                    continue;
                }

                float stillLit = (i - 1) / (float)AcrossSamples;
                float dark = i / (float)AcrossSamples;

                for (int step = 0; step < FitSteps; step++) {
                    float middle = (stillLit + dark) * 0.5f;

                    if (Blocker(moonEdge + across * middle, toMoon) == null) {
                        stillLit = middle;
                    } else {
                        dark = middle;
                    }
                }

                stripEnd = stillLit;
                break;
            }

            sample.Far = Mathf.Max(stripEnd, sample.Near);
            return Shade.Shaft;
        }

        /// <summary>
        /// One shaft for a run of samples shaded by one building: bottom
        /// corners on the street round the moonlit strip (the first sample's
        /// strip at the run's start, the last's at its end), top corners up
        /// the moonlight from each to the building's top plus the settings'
        /// clearance.
        /// </summary>
        private static void PlaceShaft(StreetSpec street, int segment, Vector2 groundA, Vector2 along, Vector2 fromMoon, float halfSpan,
            List<Sample> run, BuildingBox building, Vector3 toMoon, StreetHeights heights, GeneratorSettings settings, Transform group)
        {
            Sample first = run[0];
            Sample last = run[run.Count - 1];
            float top = building.Bounds.max.y + settings.MoonShaftTopClearance;

            // In order round the strip: start near, end near, end far, start
            // far (near = the shadow's edge, far = the strip's other side).
            Vector3[] corners = new Vector3[GodRayBox.Sides * 2];
            corners[GodRayBox.Sides] = OnStreet(first.From, first.Near);
            corners[GodRayBox.Sides + 1] = OnStreet(last.To, last.Near);
            corners[GodRayBox.Sides + 2] = OnStreet(last.To, last.Far);
            corners[GodRayBox.Sides + 3] = OnStreet(first.From, first.Far);

            Vector3 middle = Vector3.zero;

            for (int i = 0; i < GodRayBox.Sides; i++) {
                Vector3 bottom = corners[GodRayBox.Sides + i];
                corners[i] = bottom + toMoon * (Mathf.Max(0f, top - bottom.y) / toMoon.y);
                middle += bottom / GodRayBox.Sides;
            }

            var shaftObject = new GameObject($"Moon Shaft - {street.id} {segment}-{segment + 1} - {building.Name}");
            Undo.RegisterCreatedObjectUndo(shaftObject, "Generate");
            shaftObject.transform.SetParent(group, false);
            // Stands on the street in the strip's middle, +Z across the
            // street the way the light travels.
            shaftObject.transform.SetPositionAndRotation(middle, Quaternion.LookRotation(new Vector3(fromMoon.x, 0f, fromMoon.y), Vector3.up));
            shaftObject.layer = LayerMask.NameToLayer("Environment");
            GameObjectUtility.SetStaticEditorFlags(shaftObject, StaticEditorFlags.OccludeeStatic);
            shaftObject.AddComponent<GodRayBox>().SetCorners(corners);
            return;

            // A point on the street surface: a distance along the segment and
            // a share of the width across from the moon's side.
            Vector3 OnStreet(float distance, float acrossShare)
            {
                Vector2 point = groundA + along * distance + fromMoon * (halfSpan * (acrossShare * 2f - 1f));
                return new Vector3(point.x, heights.SurfaceAt(street, segment, distance), point.y);
            }
        }

        /// <summary>
        /// What stands between a point and the moon on the light layers,
        /// or null where the moon reaches it.
        /// </summary>
        private static Collider Blocker(Vector3 point, Vector3 toMoon)
        {
            return Blocker(point, toMoon, out _);
        }

        private static Collider Blocker(Vector3 point, Vector3 toMoon, out Vector3 hitPoint)
        {
            if (Physics.Raycast(point, toMoon, out RaycastHit hit, MoonReach, _lightMask, QueryTriggerInteraction.Ignore)) {
                hitPoint = hit.point;
                return hit.collider;
            }

            hitPoint = default;
            return null;
        }

        /// <summary>
        /// Every building the generator built (facades, dead ends, lots),
        /// each with the box round its pieces and its roof. Particle
        /// renderers (smoke, steam) left out.
        /// </summary>
        private static List<BuildingBox> Buildings(Transform root)
        {
            var buildings = new List<BuildingBox>();
            Transform roofs = root.Find("Roofs");

            foreach (string groupName in new[] { "Facades", "Lots" }) {
                Transform owners = root.Find(groupName);

                if (owners == null) {
                    continue;
                }

                foreach (Transform owner in owners) {
                    Transform roofOwner = roofs != null ? roofs.Find(owner.name) : null;

                    foreach (Transform building in owner) {
                        bool any = false;
                        Bounds bounds = default;
                        Grow(building, ref bounds, ref any);
                        Grow(roofOwner != null ? roofOwner.Find(building.name) : null, ref bounds, ref any);

                        if (any) {
                            buildings.Add(new BuildingBox { Name = $"{owner.name} {building.name}", Bounds = bounds });
                        }
                    }
                }
            }

            return buildings;

            static void Grow(Transform group, ref Bounds bounds, ref bool any)
            {
                if (group == null) {
                    return;
                }

                foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>()) {
                    if (renderer is ParticleSystemRenderer) {
                        continue;
                    }

                    if (any) {
                        bounds.Encapsulate(renderer.bounds);
                    } else {
                        bounds = renderer.bounds;
                        any = true;
                    }
                }
            }
        }

        /// <summary>
        /// The building whose box holds a point (grown a little), the
        /// smallest where boxes overlap; -1 for none.
        /// </summary>
        private static int BuildingAt(Vector3 point, List<BuildingBox> buildings)
        {
            int best = -1;
            float bestVolume = float.MaxValue;

            for (int i = 0; i < buildings.Count; i++) {
                Bounds bounds = buildings[i].Bounds;
                bounds.Expand(Clearance * 2f);

                if (!bounds.Contains(point)) {
                    continue;
                }

                float volume = bounds.size.x * bounds.size.y * bounds.size.z;

                if (volume < bestVolume) {
                    best = i;
                    bestVolume = volume;
                }
            }

            return best;
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
        /// The top of the highest wall standing within the settings' probe
        /// distance of a point on the ground (level distance to the piece's
        /// box), ignoring anything far above or wholly below the street;
        /// NaN where nothing stands there. Used by the sound rooms.
        /// </summary>
        internal static float WallTop(Vector2 point, float streetLevel, List<Bounds> walls, GeneratorSettings settings)
        {
            float top = float.NaN;
            float probe = settings.EdgeWallProbe;

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
        internal static void GatherBounds(Transform group, List<Bounds> into)
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
