using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Closes the free wall ends left once every facade, lot and dead end
    /// stands (GeneratedReport.FindFreeEnds: a front wall's end nothing
    /// stands against, in view of a street - the pack's walls are hollow, so
    /// such an end shows black). Run by LevelGenerator before the street
    /// test and the visibility cull, so the fillers are tested and culled
    /// like everything else.
    ///
    /// Two ends facing each other across a gap at the same storey height
    /// (walls either side of a bend in the street that stop short of each
    /// other, a lot's corner whose walls meet at an angle the corners don't
    /// close) are bridged by one plain wall laid across the chord between
    /// them, overlapping each end by wallOverlap, so the facade runs on
    /// unbroken; an end with no partner (a wall standing above or past its
    /// neighbour at a step) gets a return: a plain wall turned side-on, its
    /// face flush with the end, running back from the front, as a side wall
    /// does. The filler is a plain wall of the end's wall's style and height
    /// (the wall itself where it is plain, else one of the style's side
    /// walls, else one of its blind-window walls) wearing the wall's
    /// materials, so it takes the building's colour. A bridge goes in the
    /// wall's own storey group (so its colliders merge with the walls'), a
    /// return under the building's "Side gap" group (its back end open by
    /// design, which the report leaves alone as it does a side wall's).
    /// Whatever can't be closed (no plain wall fits, a filler refused for
    /// standing in a street) is left for the report to list.
    /// </summary>
    internal static class GapFiller
    {
        // The group a building's returns go under: "Side " so the report
        // treats them as side walls.
        private const string ReturnGroup = "Side gap";
        private const string StoreyGroupPrefix = "Storey ";

        /// <summary>
        /// Finds the free wall ends under a Generated root and closes them.
        /// Adds one line to log.
        /// </summary>
        internal static void Run(Transform root, PieceAnnotations annotations, Dictionary<string, BuildingStyle> styles, LayoutSpec spec,
            GeneratorSettings settings, PiecePlacer placer, List<string> log)
        {
            List<GeneratedReport.FreeEnd> ends = GeneratedReport.FindFreeEnds(root, annotations, spec);

            if (ends.Count == 0) {
                log.Add("Gaps: no free wall end to close.");
                return;
            }

            Dictionary<string, PieceAnnotation> byName = new();

            foreach (PieceAnnotation piece in annotations.pieces) {
                byName[piece.name] = piece;
            }

            // A sequence of its own from the seed, so the fillers change
            // nothing else that is built.
            System.Random random = new(unchecked(spec.seed * 37 + 11));
            // Every pair of ends facing each other, nearest first; an end
            // joins one pair at most.
            List<(float distance, int a, int b)> pairs = new();

            for (int i = 0; i < ends.Count; i++) {
                for (int j = i + 1; j < ends.Count; j++) {
                    if (Pairs(ends[i], ends[j], settings)) {
                        pairs.Add((Flat(ends[j].Centre - ends[i].Centre).magnitude, i, j));
                    }
                }
            }

            pairs.Sort((x, y) => x.distance.CompareTo(y.distance));
            bool[] taken = new bool[ends.Count];
            int bridged = 0;
            int returned = 0;
            int left = 0;
            // The fillers are checked against the streets as every wall is,
            // against every street alike.
            StreetClearance clearance = FacadeBuilder.Clearance;

            if (clearance != null) {
                clearance.Facing = null;
            }

            placer.Clearance = clearance;

            foreach ((float _, int a, int b) in pairs) {
                if (taken[a] || taken[b]) {
                    continue;
                }

                taken[a] = taken[b] = true;

                if (Bridge(ends[a], ends[b], byName, styles, settings, random, placer)) {
                    bridged++;
                } else {
                    left += 2;
                }
            }

            for (int i = 0; i < ends.Count; i++) {
                if (taken[i]) {
                    continue;
                }

                if (Return(ends[i], byName, styles, random, placer)) {
                    returned++;
                } else {
                    left++;
                }
            }

            placer.Clearance = null;
            log.Add($"Gaps: {ends.Count} free wall end(s): {bridged} pair(s) bridged, {returned} closed by a return wall, {left} left for the report.");
        }

        /// <summary>
        /// Whether two ends are the two sides of one gap: on different walls,
        /// no further apart than gapBridgeMax, at one storey height (bottoms
        /// and tops within gapStoreyTolerance), each facing the other, their
        /// walls facing the same way round (within a quarter turn).
        /// </summary>
        private static bool Pairs(GeneratedReport.FreeEnd a, GeneratedReport.FreeEnd b, GeneratorSettings settings)
        {
            if (a.Wall == b.Wall) {
                return false;
            }

            Vector3 chord = Flat(b.Centre - a.Centre);
            float distance = chord.magnitude;

            if (distance < 0.001f || distance > settings.GapBridgeMax) {
                return false;
            }

            if (Mathf.Abs(a.Bottom - b.Bottom) > settings.GapStoreyTolerance || Mathf.Abs(a.Top - b.Top) > settings.GapStoreyTolerance) {
                return false;
            }

            return Vector3.Dot(a.Outward, chord) > 0f && Vector3.Dot(b.Outward, chord) < 0f && Vector3.Dot(a.Front, b.Front) > 0f;
        }

        /// <summary>
        /// One plain wall across the chord between two ends, facing the way
        /// their walls face, its render box's middle line through the two
        /// end faces' middles, its base at the first end's bottom and its
        /// height scaled to the first end's wall, its connection points
        /// wallOverlap inside each end, under the first end's wall's group.
        /// False if no wall fits or it couldn't be placed.
        /// </summary>
        private static bool Bridge(GeneratedReport.FreeEnd a, GeneratedReport.FreeEnd b, Dictionary<string, PieceAnnotation> byName,
            Dictionary<string, BuildingStyle> styles, GeneratorSettings settings, System.Random random, PiecePlacer placer)
        {
            PieceAnnotation piece = Filler(a, byName, styles, random);
            CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;

            if (entry == null || piece.fronts.Count != 1 || piece.width <= 0f || !FacadeBuilder.Connection(piece, "left", out Vector3 left)) {
                return false;
            }

            Vector3 chord = Flat(b.Centre - a.Centre);
            float length = chord.magnitude;
            Vector3 right = chord / length;

            // Seen from in front a wall runs to the viewer's right from its
            // left point: the bridge's right runs along the chord the way
            // round that has it face the way the two walls face.
            if (Vector3.Dot(Vector3.Cross(Vector3.up, right), a.Front + b.Front) < 0f) {
                right = -right;
            }

            Vector3 front = Vector3.Cross(Vector3.up, right);
            GeneratedReport.FreeEnd leftEnd = Vector3.Dot(right, chord) > 0f ? a : b;
            float overlap = settings.WallOverlap;
            float widthScale = (length + 2f * overlap) / piece.width;
            float heightScale = HeightScale(a, entry);
            // The box's middle in depth stands this far in front of the left
            // point; the point goes behind the end faces' middle line by it.
            float depthMiddle = Mathf.Max(0f, FacadeBuilder.DepthMiddle(piece, placer));
            Vector3 at = new Vector3(leftEnd.Centre.x, leftEnd.Bottom + (left.y - entry.renderMin.y) * heightScale, leftEnd.Centre.z)
                - front * depthMiddle - right * overlap;
            GameObject placed = placer.PlaceAt(piece, left, at, FacadeBuilder.Yaw(front) - FacadeBuilder.Yaw(piece.fronts[0]),
                FacadeBuilder.PieceScale(piece, widthScale, heightScale), a.Wall.parent);

            if (placed == null) {
                return false;
            }

            WearMaterialsOf(a.Wall, placed.transform);
            return true;
        }

        /// <summary>
        /// A plain wall turned side-on at an end: facing out of the end, its
        /// face on the end's plane with its thickness inside the wall, its
        /// near end at the wall's front and the rest running back, its base
        /// at the end's bottom and its height scaled to the wall, at its own
        /// width; under the building's "Side gap" group. False if no wall
        /// fits or it couldn't be placed.
        /// </summary>
        private static bool Return(GeneratedReport.FreeEnd a, Dictionary<string, PieceAnnotation> byName, Dictionary<string, BuildingStyle> styles,
            System.Random random, PiecePlacer placer)
        {
            PieceAnnotation piece = Filler(a, byName, styles, random);
            CatalogueEntry entry = piece != null ? placer.Entry(piece) : null;
            Transform building = a.Wall.parent != null ? a.Wall.parent.parent : null;

            if (entry == null || piece.fronts.Count != 1 || building == null || !FacadeBuilder.Connection(piece, "left", out Vector3 left)) {
                return false;
            }

            Transform group = building.Find(ReturnGroup);

            if (group == null) {
                group = new GameObject(ReturnGroup).transform;
                group.SetParent(building, false);
            }

            int before = group.childCount;
            float heightScale = HeightScale(a, entry);
            Vector3 corner = a.Centre + a.Front * (a.Thickness * 0.5f);
            float height = a.Bottom + (left.y - entry.renderMin.y) * heightScale;
            FacadeBuilder.PlaceSideWall(piece, new Vector2(corner.x, corner.z), height, 1f, heightScale, new Vector2(a.Outward.x, a.Outward.z),
                new Vector2(-a.Front.x, -a.Front.z), false, placer, group);

            if (group.childCount == before) {
                return false;
            }

            WearMaterialsOf(a.Wall, group.GetChild(group.childCount - 1));
            return true;
        }

        /// <summary>
        /// The plain wall to close an end with: the end's own wall where it
        /// is plain and shows no opening; else one of its style's side walls
        /// (plain, no opening) of its height, else one of the style's plain
        /// walls that show a blind window (wall-tall has no other), never a
        /// ground-storey-only wall above the ground nor another
        /// one-per-storey wall than the wall's own; else the wall itself if
        /// it is a plain wall at all. Null if nothing fits.
        /// </summary>
        private static PieceAnnotation Filler(GeneratedReport.FreeEnd end, Dictionary<string, PieceAnnotation> byName, Dictionary<string, BuildingStyle> styles,
            System.Random random)
        {
            if (!byName.TryGetValue(end.Wall.name, out PieceAnnotation wall)) {
                return null;
            }

            if (wall.category == "wall" && !wall.showsOpening) {
                return wall;
            }

            BuildingStyle style = null;

            foreach (string name in wall.styles) {
                if (styles.TryGetValue(name, out style)) {
                    break;
                }
            }

            if (style != null) {
                // The storey from the wall's group: unknown (a corner's
                // fillers) counts as above the ground unless the wall itself
                // is a ground-storey wall.
                int storey = Storey(end.Wall.parent);
                bool ground = storey == 0 || wall.groundStoreyOnly;
                List<PieceAnnotation> choices = Fitting(style.SideWalls(), style, wall, ground);

                if (choices.Count == 0) {
                    choices = Fitting(style.Pieces("wall"), style, wall, ground);
                }

                if (choices.Count > 0) {
                    return PiecePlacer.Pick(choices, null, random);
                }
            }

            return wall.category == "wall" ? wall : null;
        }

        /// <summary>
        /// The plain walls among some pieces that may stand for a wall: of
        /// its height (BuildingStyle.Fits), allowed on its storey, and no
        /// other one-per-storey wall than itself.
        /// </summary>
        private static List<PieceAnnotation> Fitting(IReadOnlyList<PieceAnnotation> pieces, BuildingStyle style, PieceAnnotation wall, bool ground)
        {
            return new List<PieceAnnotation>(pieces).FindAll(piece =>
                (ground || !piece.groundStoreyOnly) && style.Fits(piece.height, wall.height) && (!piece.onePerStorey || !wall.onePerStorey || piece == wall));
        }

        /// <summary>
        /// The storey index of a "Storey n" group, or -1 for any other.
        /// </summary>
        private static int Storey(Transform group)
        {
            if (group != null && group.name.StartsWith(StoreyGroupPrefix) && int.TryParse(group.name.Substring(StoreyGroupPrefix.Length), out int storey)) {
                return storey;
            }

            return -1;
        }

        /// <summary>
        /// The scale that makes a piece's render height the end's wall's
        /// height (the wall is scaled to its storey; the filler follows).
        /// </summary>
        private static float HeightScale(GeneratedReport.FreeEnd end, CatalogueEntry entry)
        {
            float pieceHeight = entry.renderMax.y - entry.renderMin.y;
            return pieceHeight > 0.01f ? (end.Top - end.Bottom) / pieceHeight : 1f;
        }

        /// <summary>
        /// Gives a filler the materials its end's wall wears, so it takes the
        /// building's colour (ColourVariants: a colour is a material named
        /// after the piece's own with a suffix, or a same-named copy): each
        /// of the filler's materials is swapped for the wall's material with
        /// that name or that name plus a suffix, the shortest such name
        /// where there are several.
        /// </summary>
        private static void WearMaterialsOf(Transform wall, Transform filler)
        {
            List<Material> worn = new();

            foreach (Renderer renderer in wall.GetComponentsInChildren<Renderer>(true)) {
                foreach (Material material in renderer.sharedMaterials) {
                    if (material != null && !worn.Contains(material)) {
                        worn.Add(material);
                    }
                }
            }

            foreach (Renderer renderer in filler.GetComponentsInChildren<Renderer>(true)) {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++) {
                    Material own = materials[i];

                    if (own == null || worn.Contains(own)) {
                        continue;
                    }

                    Material best = null;

                    foreach (Material material in worn) {
                        if ((material.name == own.name || material.name.StartsWith(own.name + "_")) && (best == null || material.name.Length < best.name.Length)) {
                            best = material;
                        }
                    }

                    if (best != null) {
                        materials[i] = best;
                        changed = true;
                    }
                }

                if (changed) {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        /// <summary>
        /// A vector flattened to the ground.
        /// </summary>
        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }
    }
}
