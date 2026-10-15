using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The second pass over a piece, after WallOrientation and WalkSurface:
    /// works out a suggested category and more connection points from the
    /// measurements, taking +Z as the front wherever the geometry gives none
    /// of its own (the pack faces every piece +Z).
    ///
    /// - Steps (a RampCollider): "foot" and "top" points at the slope's ends.
    /// - Walls: gaps in the back plane, found on a grid. A gap reaching the
    ///   base is a door, one inside the plane a window, so the category is
    ///   door-wall, window-wall or wall.
    /// - Corners: outer or inner (an outer corner's flat backs are inside its
    ///   box, an inner one's on its edge), a point at each arm's free end
    ///   where a wall joins it, and top and bottom where the backs meet.
    /// - Floors and ground (flat, with a walking surface): a point at the
    ///   middle of each edge of the collider-top surface, at its height.
    /// - Anything else: +Z is taken as its front, with left/right/top/bottom
    ///   points on its -Z face; a column or a beam is told by its proportions.
    ///
    /// Every result is a suggestion, confirmed by eye in the review window.
    /// </summary>
    internal static class PieceShape
    {
        internal const string FlagFrontNotPlusZ = "front-not-plus-z";

        // A floor is at most FlatRatio as tall as its shorter level side, and
        // (outside the Ground folder) its walking surface holds at least
        // FloorShare of its upward collider area.
        private const float FlatRatio = 0.35f;
        private const float FloorShare = 0.9f;
        // A column is at most ColumnMaxWidth across and at least ColumnRatio
        // times as tall as that; a beam's longer level side is at least
        // BeamRatio times its height and its shorter level side.
        private const float ColumnMaxWidth = 0.8f;
        private const float ColumnRatio = 2f;
        private const float BeamRatio = 4f;
        // A piece is turned away from +Z if it is thin along X (a slab running
        // along Z), or its -Z side has at least BusierRatio times the +Z
        // side's triangles and +Z is at least FlatterBy flatter.
        private const float ThinRatio = 0.6f;
        private const float BusierRatio = 1.5f;
        private const float FlatterBy = 0.2f;
        // An assumed front's -Z side counts as a back plane when at least this
        // much of its area is one plane; otherwise the render box's -Z face
        // stands in.
        private const float AssumedFlatBack = 0.6f;
        // A corner's back plane is on its box's edge (an inner corner) when
        // within this share of the box's size of it.
        private const float EdgeShare = 0.1f;
        // Openings are found on a grid of OpeningCell metres; smaller than a
        // door's or a window's minimum size, they're left out.
        private const float OpeningCell = 0.05f;
        private const float DoorMinWidth = 0.6f;
        private const float DoorMinHeight = 1.5f;
        private const float WindowMinSize = 0.3f;

        // Folders whose pieces are suggested by folder: the roof kit (left
        // for stage 5) and inserts and fixtures that are never a module.
        private static readonly string[] _roofFolders = { "Environment/Roofs", "Environment/RoofWalls" };
        private static readonly string[] _detailFolders = { "Environment/Windows", "Environment/Doors", "Environment/Iron" };

        /// <summary>
        /// Fills in the entry's suggested category, the +Z front where it is
        /// assumed, openings, and the connection points for its shape.
        /// sides are WallOrientation's (+X, -X, +Z, -Z), or null if the
        /// meshes couldn't be read.
        /// </summary>
        internal static void Measure(CatalogueEntry entry, WallOrientation.Side[] sides)
        {
            Vector3 size = entry.renderSize;
            float longLevel = Mathf.Max(size.x, size.z);
            float shortLevel = Mathf.Min(size.x, size.z);
            bool ground = entry.folder == "Environment/Ground";
            // Ground pieces' uneven stones never put most of their area at one
            // height; a floor's collider top is nearly all of it.
            bool flat = entry.hasWalkSurface && size.y <= shortLevel * FlatRatio && (ground || entry.walkShare >= FloorShare);
            bool column = longLevel <= ColumnMaxWidth && size.y >= longLevel * ColumnRatio;

            if (sides != null && FrontNotPlusZ(entry, sides, flat)) {
                entry.flags.Add(FlagFrontNotPlusZ);
            }

            if (entry.ramps.Count > 0) {
                entry.suggestedCategory = "steps";
                AddRampConnections(entry);
            } else if (System.Array.IndexOf(_roofFolders, entry.folder) >= 0) {
                entry.suggestedCategory = "roof";
                AssumeFront(entry, sides, false);
            } else if (System.Array.IndexOf(_detailFolders, entry.folder) >= 0) {
                // Window and door inserts and iron fixtures look like walls to
                // the measurements, but are never a wall module.
                entry.suggestedCategory = "detail";
                AssumeFront(entry, sides, true);
            } else if (column) {
                entry.suggestedCategory = "column";
                AssumeFront(entry, sides, true);
            } else if (entry.orientation == WallOrientation.KindWall) {
                FindOpenings(entry, sides);
                entry.suggestedCategory = HasOpening(entry, "door") ? "door-wall" : HasOpening(entry, "window") ? "window-wall" : "wall";
            } else if (entry.orientation == WallOrientation.KindCorner) {
                entry.suggestedCategory = AddCornerConnections(entry, sides) ? "inner-corner" : "outer-corner";
            } else if (flat) {
                entry.suggestedCategory = ground ? "ground" : "floor";
                AddFloorConnections(entry);
            } else {
                if (longLevel >= Mathf.Max(size.y, shortLevel) * BeamRatio) {
                    entry.suggestedCategory = "beam";
                }

                AssumeFront(entry, sides, true);
            }
        }

        /// <summary>
        /// Whether the geometry says the piece faces some way other than +Z:
        /// a measured front list without +Z, or (with none measured) a slab
        /// running along Z, or a -Z side that is busier and less flat than +Z
        /// (the pattern of a front). Floors and steps have no front to check.
        /// </summary>
        private static bool FrontNotPlusZ(CatalogueEntry entry, WallOrientation.Side[] sides, bool flat)
        {
            if (entry.suggestedFronts.Count > 0) {
                return !entry.suggestedFronts.Contains(Vector3.forward);
            }

            if (flat || entry.ramps.Count > 0) {
                return false;
            }

            if (entry.renderSize.x <= entry.renderSize.z * ThinRatio) {
                return true;
            }

            WallOrientation.Side plusZ = sides[2];
            WallOrientation.Side minusZ = sides[3];
            return minusZ.Triangles.Count >= plusZ.Triangles.Count * BusierRatio &&
                   plusZ.Planarity >= minusZ.Planarity + FlatterBy;
        }

        /// <summary>
        /// Takes +Z as the front of a piece the geometry gave none, and (if
        /// asked) adds left/right/top/bottom points on its back: the -Z
        /// side's plane when that is flat enough (also kept as its back
        /// plane), otherwise the render box's -Z face.
        /// </summary>
        private static void AssumeFront(CatalogueEntry entry, WallOrientation.Side[] sides, bool withConnections)
        {
            if (entry.suggestedFronts.Count > 0) {
                return;
            }

            entry.suggestedFronts.Add(Vector3.forward);
            entry.frontAssumed = true;

            if (!withConnections) {
                return;
            }

            if (sides != null && sides[3].Planarity >= AssumedFlatBack) {
                WallOrientation.AddBackPlane(entry, sides[3], Vector3.forward);
            }

            if (!entry.hasBackPlane) {
                Bounds box = new((entry.renderMin + entry.renderMax) * 0.5f, entry.renderSize);
                WallOrientation.AddEdgeConnections(entry, box, 2, entry.renderMin.z, Vector3.forward);
            }
        }

        /// <summary>
        /// A "foot" and a "top" point for each ramp (numbered after the first),
        /// facing down and up the flight.
        /// </summary>
        private static void AddRampConnections(CatalogueEntry entry)
        {
            for (int i = 0; i < entry.ramps.Count; i++) {
                RampInfo ramp = entry.ramps[i];
                string suffix = i == 0 ? "" : (i + 1).ToString();
                WallOrientation.AddConnection(entry, "foot" + suffix, ramp.foot, -ramp.direction);
                WallOrientation.AddConnection(entry, "top" + suffix, ramp.top, ramp.direction);
            }
        }

        /// <summary>
        /// A point at the middle of each edge of the walking surface, at its
        /// height, named by the way out of it.
        /// </summary>
        private static void AddFloorConnections(CatalogueEntry entry)
        {
            Vector3 min = entry.walkMin;
            Vector3 max = entry.walkMax;
            Vector3 centre = (min + max) * 0.5f;

            WallOrientation.AddConnection(entry, "+X", new Vector3(max.x, centre.y, centre.z), Vector3.right);
            WallOrientation.AddConnection(entry, "-X", new Vector3(min.x, centre.y, centre.z), Vector3.left);
            WallOrientation.AddConnection(entry, "+Z", new Vector3(centre.x, centre.y, max.z), Vector3.forward);
            WallOrientation.AddConnection(entry, "-Z", new Vector3(centre.x, centre.y, min.z), Vector3.back);
        }

        /// <summary>
        /// A corner's points: one at the free end of each arm, on that arm's
        /// back plane at its base (named "end" and the arm's front, e.g.
        /// "end+Z"), pointing along the arm; top and bottom where the two
        /// back planes meet. Returns whether it is an inner corner.
        /// </summary>
        private static bool AddCornerConnections(CatalogueEntry entry, WallOrientation.Side[] sides)
        {
            Vector3 frontX = entry.suggestedFronts.Find(front => front.x != 0f);
            Vector3 frontZ = entry.suggestedFronts.Find(front => front.z != 0f);
            WallOrientation.Side backX = SideFacing(sides, -frontX);
            WallOrientation.Side backZ = SideFacing(sides, -frontZ);

            if (backX == null || backZ == null ||
                !WallOrientation.TryGetPlaneBox(backX, out Bounds planeX) ||
                !WallOrientation.TryGetPlaneBox(backZ, out Bounds planeZ)) {
                return false;
            }

            // An inner corner's backs are the outside of its box; an outer
            // corner's are the inner faces of its arms, inside the box.
            float edgeX = frontX.x > 0f ? entry.renderMin.x : entry.renderMax.x;
            float edgeZ = frontZ.z > 0f ? entry.renderMin.z : entry.renderMax.z;
            bool inner = Mathf.Abs(backX.PlaneDepth - edgeX) <= entry.renderSize.x * EdgeShare &&
                         Mathf.Abs(backZ.PlaneDepth - edgeZ) <= entry.renderSize.z * EdgeShare;

            // The arm with the Z front runs along X: its free end is the end of
            // its back plane further from the other arm's back plane.
            float freeX = Farther(planeZ.min.x, planeZ.max.x, backX.PlaneDepth);
            float freeZ = Farther(planeX.min.z, planeX.max.z, backZ.PlaneDepth);
            float baseY = Mathf.Min(planeX.min.y, planeZ.min.y);
            float topY = Mathf.Max(planeX.max.y, planeZ.max.y);

            WallOrientation.AddConnection(entry, "end" + AxisName(frontZ), new Vector3(freeX, planeZ.min.y, backZ.PlaneDepth),
                new Vector3(Mathf.Sign(freeX - backX.PlaneDepth), 0f, 0f));
            WallOrientation.AddConnection(entry, "end" + AxisName(frontX), new Vector3(backX.PlaneDepth, planeX.min.y, freeZ),
                new Vector3(0f, 0f, Mathf.Sign(freeZ - backZ.PlaneDepth)));
            WallOrientation.AddConnection(entry, "top", new Vector3(backX.PlaneDepth, topY, backZ.PlaneDepth), Vector3.up);
            WallOrientation.AddConnection(entry, "bottom", new Vector3(backX.PlaneDepth, baseY, backZ.PlaneDepth), Vector3.down);
            return inner;
        }

        /// <summary>
        /// Finds the gaps in a wall's back plane. Its in-plane triangles are
        /// drawn onto a grid across the plane's box (a cell is covered when
        /// its centre is inside one); each connected run of empty cells is a
        /// gap. One touching the plane's sides or top is part of its outline
        /// and is left out; one touching only the base is a door, one inside
        /// the plane a window, if big enough. A point is added at each
        /// opening's centre, at the middle of the wall's depth, facing out of
        /// the front.
        /// </summary>
        private static void FindOpenings(CatalogueEntry entry, WallOrientation.Side[] sides)
        {
            Vector3 front = entry.suggestedFronts[0];
            WallOrientation.Side back = SideFacing(sides, -front);

            if (back == null || !WallOrientation.TryGetPlaneBox(back, out Bounds plane)) {
                return;
            }

            // u runs across the plane (X or Z), v up it.
            int uAxis = back.Axis == 0 ? 2 : 0;
            float uMin = plane.min[uAxis];
            float vMin = plane.min.y;
            int columns = Mathf.Max(1, Mathf.CeilToInt(plane.size[uAxis] / OpeningCell));
            int rows = Mathf.Max(1, Mathf.CeilToInt(plane.size.y / OpeningCell));
            bool[,] covered = new bool[columns, rows];

            foreach (WallOrientation.Triangle triangle in WallOrientation.InPlane(back)) {
                Vector2 a = new(triangle.A[uAxis], triangle.A.y);
                Vector2 b = new(triangle.B[uAxis], triangle.B.y);
                Vector2 c = new(triangle.C[uAxis], triangle.C.y);
                int fromU = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - uMin) / OpeningCell));
                int toU = Mathf.Min(columns - 1, Mathf.FloorToInt((Mathf.Max(a.x, b.x, c.x) - uMin) / OpeningCell));
                int fromV = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y, c.y) - vMin) / OpeningCell));
                int toV = Mathf.Min(rows - 1, Mathf.FloorToInt((Mathf.Max(a.y, b.y, c.y) - vMin) / OpeningCell));

                for (int u = fromU; u <= toU; u++) {
                    for (int v = fromV; v <= toV; v++) {
                        if (!covered[u, v] && Inside(new Vector2(uMin + (u + 0.5f) * OpeningCell, vMin + (v + 0.5f) * OpeningCell), a, b, c)) {
                            covered[u, v] = true;
                        }
                    }
                }
            }

            bool[,] seen = new bool[columns, rows];
            Stack<Vector2Int> stack = new();
            float middle = (entry.renderMin[back.Axis] + entry.renderMax[back.Axis]) * 0.5f;

            for (int startU = 0; startU < columns; startU++) {
                for (int startV = 0; startV < rows; startV++) {
                    if (covered[startU, startV] || seen[startU, startV]) {
                        continue;
                    }

                    // Flood the empty region, keeping its cell box and which edges it touches.
                    int lowU = startU, highU = startU, lowV = startV, highV = startV;
                    bool touchesSide = false, touchesTop = false, touchesBase = false;
                    seen[startU, startV] = true;
                    stack.Push(new Vector2Int(startU, startV));

                    while (stack.Count > 0) {
                        Vector2Int cell = stack.Pop();
                        lowU = Mathf.Min(lowU, cell.x);
                        highU = Mathf.Max(highU, cell.x);
                        lowV = Mathf.Min(lowV, cell.y);
                        highV = Mathf.Max(highV, cell.y);
                        touchesSide |= cell.x == 0 || cell.x == columns - 1;
                        touchesTop |= cell.y == rows - 1;
                        touchesBase |= cell.y == 0;

                        Visit(cell.x - 1, cell.y);
                        Visit(cell.x + 1, cell.y);
                        Visit(cell.x, cell.y - 1);
                        Visit(cell.x, cell.y + 1);
                    }

                    if (touchesSide || touchesTop) {
                        continue;
                    }

                    float width = (highU - lowU + 1) * OpeningCell;
                    float height = (highV - lowV + 1) * OpeningCell;
                    string kind = touchesBase ? "door" : "window";

                    if (touchesBase ? width < DoorMinWidth || height < DoorMinHeight : width < WindowMinSize || height < WindowMinSize) {
                        continue;
                    }

                    Vector3 min = Vector3.zero;
                    Vector3 max = Vector3.zero;
                    min[uAxis] = uMin + lowU * OpeningCell;
                    max[uAxis] = uMin + (highU + 1) * OpeningCell;
                    min.y = vMin + lowV * OpeningCell;
                    max.y = vMin + (highV + 1) * OpeningCell;
                    min[back.Axis] = back.PlaneDepth;
                    max[back.Axis] = back.PlaneDepth;
                    entry.openings.Add(new Opening { kind = kind, min = WallOrientation.Round(min), max = WallOrientation.Round(max) });

                    Vector3 centre = (min + max) * 0.5f;
                    centre[back.Axis] = middle;
                    int number = entry.openings.FindAll(opening => opening.kind == kind).Count;
                    WallOrientation.AddConnection(entry, number == 1 ? kind : kind + number, centre, front);
                }
            }

            void Visit(int u, int v)
            {
                if (u < 0 || v < 0 || u >= columns || v >= rows || covered[u, v] || seen[u, v]) {
                    return;
                }

                seen[u, v] = true;
                stack.Push(new Vector2Int(u, v));
            }
        }

        /// <summary>
        /// Whether the entry has an opening of a kind.
        /// </summary>
        private static bool HasOpening(CatalogueEntry entry, string kind)
        {
            return entry.openings.Exists(opening => opening.kind == kind);
        }

        /// <summary>
        /// Whether a point is inside a 2D triangle (either winding), edges
        /// included.
        /// </summary>
        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross(b - a, p - a);
            float bc = Cross(c - b, p - b);
            float ca = Cross(a - c, p - c);
            return (ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f);
        }

        /// <summary>
        /// The 2D cross product (z of the 3D one).
        /// </summary>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>
        /// The side facing a level axis direction, or null.
        /// </summary>
        private static WallOrientation.Side SideFacing(WallOrientation.Side[] sides, Vector3 direction)
        {
            foreach (WallOrientation.Side side in sides) {
                if (Vector3.Dot(side.Direction, direction) > 0.9f) {
                    return side;
                }
            }

            return null;
        }

        /// <summary>
        /// Of two values, the one further from a reference.
        /// </summary>
        private static float Farther(float a, float b, float from)
        {
            return Mathf.Abs(a - from) >= Mathf.Abs(b - from) ? a : b;
        }

        /// <summary>
        /// A level axis direction's name, e.g. "+Z".
        /// </summary>
        private static string AxisName(Vector3 direction)
        {
            if (direction.x > 0.5f) {
                return "+X";
            }

            if (direction.x < -0.5f) {
                return "-X";
            }

            return direction.z >= 0f ? "+Z" : "-Z";
        }
    }
}
