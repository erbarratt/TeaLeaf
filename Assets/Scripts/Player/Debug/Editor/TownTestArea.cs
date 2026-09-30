using System.Collections.Generic;
using Interaction;
using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Build Town Test Area) that builds a
    /// greybox town square of six flat-roofed, mud-brick style houses - big
    /// shapes only, modelled on a two-storey Gulf house: a single-storey
    /// block with a roof parapet, an optional two-storey tower over part or
    /// all of it, an optional raised front terrace and an optional balcony
    /// with a pergola roof on the tower front. Doors and windows are empty
    /// openings, and steps are smooth ramps. Plus four free-standing
    /// compound walls and a few crates, for a larger space to test climbing,
    /// mantling and (later) sneaking in. Built from code so it can be
    /// rebuilt identically after changes, and undone with Ctrl+Z.
    ///
    /// Ledges sit where a real building has them, about a metre apart so
    /// every face can be climbed hand over hand from the ground to the roof:
    /// window sills (both sides, mantled into crouched), the hoods over
    /// windows and the band below each roofline (grab only), roof parapets
    /// and balcony balustrades (both sides, mantleable) and pergola roofs.
    /// Every mantle lands the feet on top of the thing it climbs (mantling
    /// moves the player with collision off, so landing beyond a wall or
    /// railing would drag the feet through it) - the player then steps or
    /// drops down on the far side.
    ///
    /// Each building is built in its own frame: its front wall's outer face
    /// is at z = 0 facing +Z (the climbables' "out towards the player"
    /// direction), the building runs back to z = -depth and spans x = +-width
    /// / 2, and the building object is turned to face the square. Solid
    /// pieces are TestGeometry boxes on Environment; edges are
    /// TestGeometry.Edge() grab volumes on Climbable.
    ///
    /// In an Editor folder, so it's compiled into the editor-only assembly
    /// and never ends up in a build.
    /// </summary>
    public static class TownTestArea
    {
        private const string RootName = "Town Test Area";
        private const string EnvironmentLayerName = "Environment";
        private const string ClimbableLayerName = "Climbable";
        private const string LedgeProfilePath = "Assets/Data/LedgeGrip.asset";

        // Behind the spawn, clear of the physical hands test area (z -5.5)
        // and the locomotion course (z 10). The square is centred on the
        // root; the houses reach about 23m out from it.
        private static readonly Vector3 _rootPosition = new(0f, 0f, -35f);

        // Storeys: the ground floor is LowerHeight to the top of the slab
        // that is both its ceiling and the lower roof / tower's first floor;
        // the tower's upper storey adds UpperHeight to the top of its roof.
        private const float LowerHeight = 3.6f;
        private const float UpperHeight = 3.2f;
        private const float SlabThickness = 0.3f;
        private const float WallThickness = 0.4f;
        private const float ParapetHeight = 0.6f;
        private const float ParapetThickness = 0.3f;

        // Openings, measured from the storey's floor. Window sills at 1m are
        // an easy grab from the ground; a 1.2m tall window is too low to
        // stand in but fits the 1m crouch, so sill mantles end crouched.
        private const float DoorWidth = 1.2f;
        private const float DoorHeight = 2.3f;
        private const float WindowWidth = 1f;
        private const float WindowSill = 1f;
        private const float WindowTop = 2.2f;

        // Automatic window placement along a wall: no closer than
        // WindowEndMargin to either end (clear of the corners), roughly
        // WindowSpacing apart, and WindowClearance clear of any door.
        private const float WindowEndMargin = 1.4f;
        private const float WindowSpacing = 2.6f;
        private const float WindowClearance = 0.4f;

        // Projecting ledges: the band just below each roofline (its top
        // BandDrop below the storey top), and the hood over each window (its
        // top HoodGap above the opening). Both stick out from the wall.
        private const float BandProjection = 0.12f;
        private const float BandHeight = 0.2f;
        private const float BandDrop = 0.2f;
        private const float HoodProjection = 0.12f;
        private const float HoodHeight = 0.15f;
        private const float HoodGap = 0.25f;

        // Interior ramp (the stairs) up to the tower's first floor, against
        // the tower's outer side wall. The floor above it is cut open from
        // where the ramp gets high enough that a standing head (plus margin)
        // would hit the slab: RampHeadroomHeight up the ramp.
        private const float RampWidth = 1.1f;
        private const float RampThickness = 0.1f;
        private const float RampHeadroomHeight = 1.3f;

        // Balcony on the tower front (floor level with the tower's first
        // floor), with a solid balustrade and a pergola roof on two posts.
        private const float BalconyDepth = 1.4f;
        private const float BalconyInset = 0.3f;
        private const float BalustradeHeight = 0.9f;
        private const float BalustradeThickness = 0.2f;
        private const float PergolaHeight = 2.6f;
        private const float PergolaDepth = 1.8f;
        private const float PergolaThickness = 0.1f;
        private const float BalconyDoorWidth = 1.1f;

        // Front terrace: a raised platform (the image's stone porch) with a
        // 20 degree ramp where its steps would be, and a low solid wall
        // where its railing would be.
        private const float TerraceHeight = 0.6f;
        private const float TerraceDepth = 2.4f;
        private const float TerraceRampWidth = 1.4f;
        private const float TerraceRampAngle = 20f;
        private const float TerraceWallHeight = 0.8f;
        private const float TerraceWallThickness = 0.15f;

        // Free-standing compound walls around the square.
        private const float CompoundWallHeight = 2.4f;
        private const float CompoundWallThickness = 0.5f;

        // Set at the start of Build() - editor-only, so shared static state
        // keeps every helper's argument list short.
        private static int _environment;
        private static int _climbable;
        private static HandSnapProfile _ledgeProfile;

        /// <summary>
        /// One building's layout. TowerSide: +1 = tower over the right (+X)
        /// end, -1 = the left end, 0 = no tower; a TowerWidth equal to Width
        /// makes the whole house two storeys. DoorX is the front door's centre
        /// and TerraceX0/X1 the terrace's ends, in the building's own x.
        /// </summary>
        private sealed class BuildingSpec
        {
            public string Name;
            public Vector3 Position;
            public float Yaw;
            public float Width;
            public float Depth;
            public int TowerSide;
            public float TowerWidth;
            public float DoorX;
            public bool HasTerrace;
            public float TerraceX0;
            public float TerraceX1;
            public bool HasBalcony;
        }

        /// <summary>
        /// A door or window in a wall: Centre along the wall from its start,
        /// and Bottom/Top above the wall's bottom.
        /// </summary>
        private readonly struct Opening
        {
            public readonly float Centre;
            public readonly float Width;
            public readonly float Bottom;
            public readonly float Top;
            public readonly bool IsWindow;

            public Opening(float centre, float width, float bottom, float top, bool isWindow)
            {
                Centre = centre;
                Width = width;
                Bottom = bottom;
                Top = top;
                IsWindow = isWindow;
            }
        }

        /// <summary>
        /// One outer face of a rectangular footprint, in the building's
        /// frame: Start is its left end seen from outside, on the outer face;
        /// Along runs to its right end, Length long; Inward points into the
        /// building; Yaw turns an edge to face out from it.
        /// </summary>
        private readonly struct Face
        {
            public readonly Vector3 Start;
            public readonly Vector3 Along;
            public readonly Vector3 Inward;
            public readonly float Length;
            public readonly float Yaw;

            public Face(Vector3 start, Vector3 along, Vector3 inward, float length, float yaw)
            {
                Start = start;
                Along = along;
                Inward = inward;
                Length = length;
                Yaw = yaw;
            }

            /// The point on the outer face, a along it, at height y.
            public Vector3 Point(float a, float y)
            {
                Vector3 point = Start + Along * a;
                point.y = y;
                return point;
            }
        }

        // Face order from Faces(): front (+Z), right (+X), back, left.
        private const int Front = 0;
        private const int Right = 1;
        private const int Back = 2;
        private const int Left = 3;

        /// <summary>
        /// Builds the town, replacing an existing one (after asking).
        /// </summary>
        [MenuItem("TeaLeaf/Build Town Test Area")]
        private static void Build()
        {
            _environment = LayerMask.NameToLayer(EnvironmentLayerName);
            _climbable = LayerMask.NameToLayer(ClimbableLayerName);

            if (_environment < 0 || _climbable < 0) {
                Debug.LogError($"TownTestArea: the '{EnvironmentLayerName}' and '{ClimbableLayerName}' layers must exist.");
                return;
            }

            _ledgeProfile = AssetDatabase.LoadAssetAtPath<HandSnapProfile>(LedgeProfilePath);

            if (_ledgeProfile == null) {
                Debug.LogError($"TownTestArea: couldn't load the hand snap profile {LedgeProfilePath}.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);

            if (existing != null) {
                if (!EditorUtility.DisplayDialog(
                        "Town Test Area",
                        "A town test area already exists in the scene. Replace it?",
                        "Replace",
                        "Cancel")) {
                    return;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            GameObject root = new(RootName);
            root.transform.position = _rootPosition;
            Undo.RegisterCreatedObjectUndo(root, "Build Town Test Area");
            Transform parent = root.transform;

            // Six houses round a square about 20m across, each turned to face
            // it. House 1 is the reference image: tower on one end, terrace
            // under a balcony in front of it.
            BuildingSpec[] buildings = {
                new() { Name = "House 1", Position = new Vector3(-6f, 0f, 14f), Yaw = 180f, Width = 10f, Depth = 8f,
                        TowerSide = 1, TowerWidth = 5f, DoorX = -2.5f,
                        HasTerrace = true, TerraceX0 = 0.2f, TerraceX1 = 4.6f, HasBalcony = true },
                new() { Name = "House 2", Position = new Vector3(7f, 0f, 13f), Yaw = 180f, Width = 8f, Depth = 7f,
                        TowerSide = -1, TowerWidth = 4f, DoorX = 2f, HasBalcony = true },
                new() { Name = "House 3", Position = new Vector3(-15f, 0f, 0f), Yaw = 90f, Width = 9f, Depth = 8f,
                        TowerSide = 0, DoorX = -2f, HasTerrace = true, TerraceX0 = 0.5f, TerraceX1 = 3.5f },
                new() { Name = "House 4", Position = new Vector3(15f, 0f, 1f), Yaw = -90f, Width = 7f, Depth = 7f,
                        TowerSide = 1, TowerWidth = 7f, DoorX = -1.5f, HasBalcony = true },
                new() { Name = "House 5", Position = new Vector3(-6f, 0f, -14f), Yaw = 0f, Width = 10f, Depth = 8f,
                        TowerSide = 1, TowerWidth = 5f, DoorX = -2.5f,
                        HasTerrace = true, TerraceX0 = 0.2f, TerraceX1 = 4.6f },
                new() { Name = "House 6", Position = new Vector3(7f, 0f, -14f), Yaw = 0f, Width = 8f, Depth = 8f,
                        TowerSide = -1, TowerWidth = 4f, DoorX = 2f,
                        HasTerrace = true, TerraceX0 = -3.7f, TerraceX1 = -0.3f, HasBalcony = true },
            };

            foreach (BuildingSpec spec in buildings) {
                BuildBuilding(parent, spec);
            }

            // Compound walls closing off the square's corners - cover to
            // sneak behind, and mantleable from either side.
            CompoundWall(parent, "Compound Wall NW", new Vector3(-14f, 0f, 10f), 6f);
            CompoundWall(parent, "Compound Wall NE", new Vector3(14f, 0f, 10f), 6f);
            CompoundWall(parent, "Compound Wall SW", new Vector3(-14f, 0f, -10f), 6f);
            CompoundWall(parent, "Compound Wall SE", new Vector3(14f, 0f, -10f), 6f);

            // Crates against a few walls: a leg-up to the first ledges, and
            // one two-high stack.
            TestGeometry.Box("Crate", parent, new Vector3(-14.3f, 0.5f, 3f), Vector3.one, _environment);
            TestGeometry.Box("Crate", parent, new Vector3(11.6f, 0.5f, 16.5f), Vector3.one, _environment);
            TestGeometry.Box("Crate", parent, new Vector3(11.6f, 1.35f, 16.5f), Vector3.one * 0.7f, _environment);
            TestGeometry.Box("Crate", parent, new Vector3(9.5f, 0.4f, -12.8f), Vector3.one * 0.8f, _environment);
            TestGeometry.Box("Crate", parent, new Vector3(-11.6f, 0.5f, -17f), Vector3.one, _environment);

            Selection.activeGameObject = root;
        }

        /// <summary>
        /// Builds one house under parent, in its own frame (see the class
        /// comment): ground floor, first floor / lower roof slab, roof
        /// parapet, then the tower, balcony, terrace and interior ramp as the
        /// spec asks.
        /// </summary>
        private static void BuildBuilding(Transform parent, BuildingSpec spec)
        {
            GameObject building = new(spec.Name);
            building.transform.SetParent(parent, false);
            building.transform.SetLocalPositionAndRotation(spec.Position, Quaternion.Euler(0f, spec.Yaw, 0f));
            Transform b = building.transform;

            float x0 = -spec.Width * 0.5f;
            float x1 = spec.Width * 0.5f;
            float z0 = -spec.Depth;
            const float z1 = 0f;

            bool hasTower = spec.TowerSide != 0;
            bool isFullTower = hasTower && spec.TowerWidth >= spec.Width - 0.01f;
            float towerX0 = spec.TowerSide > 0 ? x1 - spec.TowerWidth : x0;
            float towerX1 = spec.TowerSide > 0 ? x1 : x0 + spec.TowerWidth;

            // The ramp runs up the tower's outer side wall (the right wall
            // for a full-width tower), front to back.
            bool rampOnRight = spec.TowerSide >= 0;
            float rampX0 = rampOnRight ? x1 - WallThickness - RampWidth : x0 + WallThickness;
            float rampX1 = rampX0 + RampWidth;

            // Ground floor walls, up to the slab. The wall the ramp runs
            // along gets no windows.
            Face[] lower = Faces(x0, x1, z0, z1);
            float lowerWallHeight = LowerHeight - SlabThickness;

            for (int i = 0; i < lower.Length; i++) {
                List<Opening> openings = new();

                if (i == Front) {
                    openings.Add(new Opening(spec.DoorX - x0, DoorWidth, 0f, DoorHeight, false));
                }

                bool isRampWall = hasTower && i == (rampOnRight ? Right : Left);

                if (!isRampWall) {
                    AddWindows(openings, lower[i].Length);
                }

                BuildWall(b, "Wall", lower[i], 0f, lowerWallHeight, WallThickness, openings);
                Band(b, lower[i], LowerHeight - BandDrop);
            }

            // First floor / lower roof slab, open over the top of the ramp.
            float rampStartZ = z1 - WallThickness - 0.05f;
            float rampEndZ = z0 + WallThickness;
            float rampRun = rampStartZ - rampEndZ;
            float holeNearZ = rampStartZ - rampRun * (RampHeadroomHeight / LowerHeight);

            if (hasTower) {
                SlabWithHole(b, "Floor Slab", x0, x1, z0, z1, LowerHeight - SlabThickness, SlabThickness,
                    rampX0, rampX1, rampEndZ, holeNearZ);
                Ramp(b, "Stair Ramp", (rampX0 + rampX1) * 0.5f, RampWidth, rampStartZ, rampEndZ, 0f, LowerHeight);
            } else {
                Slab(b, "Roof Slab", x0, x1, z0, z1, LowerHeight - SlabThickness, SlabThickness);
            }

            // Parapet round whatever of the lower roof the tower doesn't
            // cover. For each face, the stretch of it (from, to) that's roof.
            if (!isFullTower) {
                for (int i = 0; i < lower.Length; i++) {
                    GetLowerRoofSpan(i, spec, x0, x1, towerX0, towerX1, out float from, out float to);

                    if (to - from > 0.01f) {
                        Parapet(b, lower[i], from, to, LowerHeight);
                    }
                }
            }

            if (hasTower) {
                BuildTower(b, spec, towerX0, towerX1, z0, z1, isFullTower);
            }

            if (spec.HasTerrace) {
                Terrace(b, spec.TerraceX0, spec.TerraceX1);
            }
        }

        /// <summary>
        /// The tower's upper storey on the first floor: walls with windows, a
        /// door onto the lower roof in the wall facing it, the balcony door
        /// (and balcony) on the front, then the band, roof slab and roof
        /// parapet.
        /// </summary>
        private static void BuildTower(Transform b, BuildingSpec spec, float towerX0, float towerX1, float z0, float z1, bool isFullTower)
        {
            Face[] upper = Faces(towerX0, towerX1, z0, z1);
            float upperWallHeight = UpperHeight - SlabThickness;
            int roofSide = spec.TowerSide > 0 ? Left : Right;

            for (int i = 0; i < upper.Length; i++) {
                List<Opening> openings = new();
                float middle = upper[i].Length * 0.5f;

                if (i == Front && spec.HasBalcony) {
                    openings.Add(new Opening(middle, BalconyDoorWidth, 0f, DoorHeight, false));
                }

                if (i == roofSide && !isFullTower) {
                    openings.Add(new Opening(middle, DoorWidth, 0f, DoorHeight, false));
                } else {
                    AddWindows(openings, upper[i].Length);
                }

                BuildWall(b, "Tower Wall", upper[i], LowerHeight, upperWallHeight, WallThickness, openings);
                Band(b, upper[i], LowerHeight + UpperHeight - BandDrop);
                Parapet(b, upper[i], 0f, upper[i].Length, LowerHeight + UpperHeight);
            }

            Slab(b, "Tower Roof Slab", towerX0, towerX1, z0, z1, LowerHeight + UpperHeight - SlabThickness, SlabThickness);

            if (spec.HasBalcony) {
                Balcony(b, towerX0, towerX1);
            }
        }

        /// <summary>
        /// The four outer faces of the footprint x0..x1 by z0..z1 (back to
        /// front), in the order front, right, back, left - each running left
        /// to right as seen from outside.
        /// </summary>
        private static Face[] Faces(float x0, float x1, float z0, float z1)
        {
            return new[] {
                new Face(new Vector3(x0, 0f, z1), Vector3.right, Vector3.back, x1 - x0, 0f),
                new Face(new Vector3(x1, 0f, z1), Vector3.back, Vector3.left, z1 - z0, 90f),
                new Face(new Vector3(x1, 0f, z0), Vector3.left, Vector3.forward, x1 - x0, 180f),
                new Face(new Vector3(x0, 0f, z0), Vector3.forward, Vector3.right, z1 - z0, -90f),
            };
        }

        /// <summary>
        /// Which stretch (from, to, along the face) of a ground floor face
        /// has lower roof above it rather than the tower - where the roof
        /// parapet goes. The tower side has none; front and back have the
        /// part beside the tower; the far side has all of it.
        /// </summary>
        private static void GetLowerRoofSpan(int face, BuildingSpec spec, float x0, float x1, float towerX0, float towerX1, out float from, out float to)
        {
            float width = x1 - x0;
            from = 0f;
            to = width;

            if (spec.TowerSide == 0) {
                if (face == Right || face == Left) {
                    to = spec.Depth;
                }

                return;
            }

            bool towerOnRight = spec.TowerSide > 0;

            switch (face) {
                case Front:
                    // Runs left to right (+X) from x0.
                    from = towerOnRight ? 0f : towerX1 - x0;
                    to = towerOnRight ? towerX0 - x0 : width;
                    break;
                case Back:
                    // Runs right to left (-X) from x1.
                    from = towerOnRight ? x1 - towerX0 : 0f;
                    to = towerOnRight ? width : x1 - towerX1;
                    break;
                case Right:
                    to = towerOnRight ? 0f : spec.Depth;
                    break;
                case Left:
                    to = towerOnRight ? spec.Depth : 0f;
                    break;
            }
        }

        /// <summary>
        /// Adds windows along a wall of the given length: evenly spread about
        /// WindowSpacing apart, clear of both ends, leaving out any that would
        /// run into an opening already in the list (the door).
        /// </summary>
        private static void AddWindows(List<Opening> openings, float length)
        {
            float span = length - WindowEndMargin * 2f;

            if (span < 0f) {
                return;
            }

            int count = Mathf.FloorToInt(span / WindowSpacing) + 1;
            int existing = openings.Count;

            for (int i = 0; i < count; i++) {
                float centre = count == 1 ? length * 0.5f : WindowEndMargin + span * i / (count - 1);
                bool isClear = true;

                for (int j = 0; j < existing; j++) {
                    float gap = Mathf.Abs(centre - openings[j].Centre) - (WindowWidth + openings[j].Width) * 0.5f;

                    if (gap < WindowClearance) {
                        isClear = false;
                        break;
                    }
                }

                if (isClear) {
                    openings.Add(new Opening(centre, WindowWidth, WindowSill, WindowTop, true));
                }
            }
        }

        /// <summary>
        /// A wall along a face, thickness deep on its inward side, from bottom
        /// up height, with its openings left empty: full-height pieces
        /// between openings, and a piece below (a window's sill) and above
        /// (the lintel) each one. Each window also gets its ledges: a sill
        /// edge on both sides (mantled into crouched - a window is too low to
        /// stand in) and a hood with a grab edge above it outside.
        /// </summary>
        private static void BuildWall(Transform parent, string name, Face face, float bottom, float height, float thickness, List<Opening> openings)
        {
            openings.Sort((a, b) => a.Centre.CompareTo(b.Centre));
            float top = bottom + height;
            float cursor = 0f;

            foreach (Opening opening in openings) {
                float left = opening.Centre - opening.Width * 0.5f;
                float right = opening.Centre + opening.Width * 0.5f;
                WallPiece(parent, name, face, cursor, left, bottom, top, thickness);
                WallPiece(parent, name, face, left, right, bottom, bottom + opening.Bottom, thickness);
                WallPiece(parent, name, face, left, right, bottom + opening.Top, top, thickness);
                cursor = right;

                if (opening.IsWindow) {
                    WindowLedges(parent, face, opening, bottom, thickness);
                }
            }

            WallPiece(parent, name, face, cursor, face.Length, bottom, top, thickness);
        }

        /// <summary>
        /// One solid box of a wall: from a0 to a1 along the face, y0 to y1
        /// up, thickness deep inwards from the outer face. Skips empty pieces
        /// (a door has nothing below it).
        /// </summary>
        private static void WallPiece(Transform parent, string name, Face face, float a0, float a1, float y0, float y1, float thickness)
        {
            if (a1 - a0 < 0.001f || y1 - y0 < 0.001f) {
                return;
            }

            Vector3 centre = face.Point((a0 + a1) * 0.5f, (y0 + y1) * 0.5f) + face.Inward * (thickness * 0.5f);
            TestGeometry.Box(name, parent, centre, Size(face, a1 - a0, y1 - y0, thickness), _environment);
        }

        /// <summary>
        /// The box size of something length long along a face, height tall
        /// and depth deep across it. Faces are axis-aligned, so this just
        /// puts each number on the right axis.
        /// </summary>
        private static Vector3 Size(Face face, float length, float height, float depth)
        {
            Vector3 along = new(Mathf.Abs(face.Along.x), 0f, Mathf.Abs(face.Along.z));
            Vector3 across = new(Mathf.Abs(face.Inward.x), 0f, Mathf.Abs(face.Inward.z));
            return along * length + across * depth + Vector3.up * height;
        }

        /// <summary>
        /// A window's ledges: a mantleable sill edge on each side (landing
        /// on the sill, crouched) and, outside, a projecting hood over the
        /// opening with a grab-only edge on it.
        /// </summary>
        private static void WindowLedges(Transform parent, Face face, Opening window, float bottom, float thickness)
        {
            float sillY = bottom + window.Bottom;
            float sillInset = TestGeometry.TopCentreInset(thickness);
            Vector3 outerSill = face.Point(window.Centre, sillY);
            TestGeometry.Edge("Sill Edge", parent, outerSill, face.Yaw, window.Width, true, true, sillInset, _ledgeProfile, _climbable);
            TestGeometry.Edge("Sill Edge", parent, outerSill + face.Inward * thickness, face.Yaw + 180f, window.Width, true, true, sillInset, _ledgeProfile, _climbable);

            float hoodTop = bottom + window.Top + HoodGap;
            float hoodLength = window.Width + 0.3f;
            Vector3 hoodCentre = face.Point(window.Centre, hoodTop - HoodHeight * 0.5f) - face.Inward * (HoodProjection * 0.5f);
            TestGeometry.Box("Window Hood", parent, hoodCentre, Size(face, hoodLength, HoodHeight, HoodProjection), _environment);
            Vector3 hoodLip = face.Point(window.Centre, hoodTop) - face.Inward * HoodProjection;
            TestGeometry.Edge("Hood Edge", parent, hoodLip, face.Yaw, hoodLength, false, false, 0f, _ledgeProfile, _climbable);
        }

        /// <summary>
        /// The projecting band along a whole face with its top at topY, and a
        /// grab-only edge on it. It runs BandProjection past both ends, so
        /// the bands of neighbouring faces meet at the corners.
        /// </summary>
        private static void Band(Transform parent, Face face, float topY)
        {
            float middle = face.Length * 0.5f;
            Vector3 centre = face.Point(middle, topY - BandHeight * 0.5f) - face.Inward * (BandProjection * 0.5f);
            TestGeometry.Box("Band", parent, centre, Size(face, face.Length + BandProjection * 2f, BandHeight, BandProjection), _environment);
            Vector3 lip = face.Point(middle, topY) - face.Inward * BandProjection;
            TestGeometry.Edge("Band Edge", parent, lip, face.Yaw, face.Length, false, false, 0f, _ledgeProfile, _climbable);
        }

        /// <summary>
        /// A roof parapet on the stretch from..to of a face, standing on the
        /// roof at roofY, with a mantleable edge on each side of its top: up
        /// from outside, or back over from the roof. Both land on the top.
        /// </summary>
        private static void Parapet(Transform parent, Face face, float from, float to, float roofY)
        {
            float top = roofY + ParapetHeight;
            WallPiece(parent, "Parapet", face, from, to, roofY, top, ParapetThickness);
            EdgePair(parent, face, (from + to) * 0.5f, top, to - from, ParapetThickness);
        }

        /// <summary>
        /// Mantleable edges on both sides of a wall top thickness deep, at
        /// the middle a of a face, length long, both landing on the top -
        /// straight ahead of the player, since these run for metres (see
        /// ClimbableEdge's moveHorizontallyToPoint).
        /// </summary>
        private static void EdgePair(Transform parent, Face face, float a, float top, float length, float thickness)
        {
            float inset = TestGeometry.TopCentreInset(thickness);
            Vector3 outer = face.Point(a, top);
            TestGeometry.Edge("Top Edge", parent, outer, face.Yaw, length, true, false, inset, _ledgeProfile, _climbable, false);
            TestGeometry.Edge("Top Edge", parent, outer + face.Inward * thickness, face.Yaw + 180f, length, true, false, inset, _ledgeProfile, _climbable, false);
        }

        /// <summary>
        /// A horizontal slab over x0..x1 by z0..z1, from bottom up thickness.
        /// </summary>
        private static void Slab(Transform parent, string name, float x0, float x1, float z0, float z1, float bottom, float thickness)
        {
            if (x1 - x0 < 0.001f || z1 - z0 < 0.001f) {
                return;
            }

            TestGeometry.Box(name, parent,
                new Vector3((x0 + x1) * 0.5f, bottom + thickness * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, thickness, z1 - z0), _environment);
        }

        /// <summary>
        /// A slab like Slab() with a rectangular hole (hx0..hx1 by hz0..hz1)
        /// cut through it, built as the four pieces around the hole.
        /// </summary>
        private static void SlabWithHole(Transform parent, string name, float x0, float x1, float z0, float z1, float bottom, float thickness,
            float hx0, float hx1, float hz0, float hz1)
        {
            Slab(parent, name, x0, hx0, z0, z1, bottom, thickness);
            Slab(parent, name, hx1, x1, z0, z1, bottom, thickness);
            Slab(parent, name, hx0, hx1, z0, hz0, bottom, thickness);
            Slab(parent, name, hx0, hx1, hz1, z1, bottom, thickness);
        }

        /// <summary>
        /// A smooth ramp (in place of steps), width wide, centred on x, whose
        /// top surface runs from lowY at z = lowZ to highY at z = highZ.
        /// Built as a box tipped about X, pushed down half its thickness along
        /// its own up so its top surface (not its middle) is on that line,
        /// and 5cm longer at each end so it tucks into the floor it meets.
        /// </summary>
        private static void Ramp(Transform parent, string name, float x, float width, float lowZ, float highZ, float lowY, float highY)
        {
            float run = highZ - lowZ;
            float rise = highY - lowY;

            // Tipping a box by a positive angle about X lowers its +Z end, so
            // the angle's sign follows which way (in Z) the ramp climbs.
            float angle = Mathf.Atan2(rise, Mathf.Abs(run)) * Mathf.Rad2Deg * (run < 0f ? 1f : -1f);
            Quaternion rotation = Quaternion.Euler(angle, 0f, 0f);
            Vector3 topMiddle = new(x, (lowY + highY) * 0.5f, (lowZ + highZ) * 0.5f);
            float length = Mathf.Sqrt(run * run + rise * rise) + 0.1f;

            GameObject ramp = TestGeometry.Box(name, parent, topMiddle - rotation * Vector3.up * (RampThickness * 0.5f),
                new Vector3(width, RampThickness, length), _environment);
            ramp.transform.localRotation = rotation;
        }

        /// <summary>
        /// The balcony on the tower front at first floor level: a slab, a
        /// solid balustrade (mantleable from outside and in, landing on its
        /// top) and a pergola roof on two posts, itself mantleable.
        /// </summary>
        private static void Balcony(Transform parent, float towerX0, float towerX1)
        {
            float bx0 = towerX0 + BalconyInset;
            float bx1 = towerX1 - BalconyInset;
            float middle = (bx0 + bx1) * 0.5f;
            float floor = LowerHeight;
            float railTop = floor + BalustradeHeight;

            Slab(parent, "Balcony", bx0, bx1, 0f, BalconyDepth, floor - 0.25f, 0.25f);
            Slab(parent, "Balustrade", bx0, bx1, BalconyDepth - BalustradeThickness, BalconyDepth, floor, BalustradeHeight);
            Slab(parent, "Balustrade", bx0, bx0 + BalustradeThickness, 0f, BalconyDepth - BalustradeThickness, floor, BalustradeHeight);
            Slab(parent, "Balustrade", bx1 - BalustradeThickness, bx1, 0f, BalconyDepth - BalustradeThickness, floor, BalustradeHeight);

            // The front balustrade faces +Z, like the building front.
            Face balustrade = new(new Vector3(bx0, 0f, BalconyDepth), Vector3.right, Vector3.back, bx1 - bx0, 0f);
            EdgePair(parent, balustrade, middle - bx0, railTop, bx1 - bx0, BalustradeThickness);

            float pergolaTop = floor + PergolaHeight;
            Slab(parent, "Pergola", towerX0 + 0.1f, towerX1 - 0.1f, 0f, PergolaDepth, pergolaTop - PergolaThickness, PergolaThickness);

            for (int side = 0; side < 2; side++) {
                float postX = side == 0 ? bx0 + 0.1f : bx1 - 0.1f;
                Slab(parent, "Pergola Post", postX - 0.06f, postX + 0.06f, BalconyDepth - 0.16f, BalconyDepth - 0.04f,
                    floor, PergolaHeight - PergolaThickness);
            }

            TestGeometry.Edge("Pergola Edge", parent, new Vector3(middle, pergolaTop, PergolaDepth), 0f,
                towerX1 - towerX0 - 0.2f, true, false, 0.45f, _ledgeProfile, _climbable, false);
        }

        /// <summary>
        /// The raised front terrace from x0 to x1: a platform, a ramp down
        /// from its front at the x1 end (where the image's steps are), and a
        /// low wall round its open sides with a gap for the ramp.
        /// </summary>
        private static void Terrace(Transform parent, float x0, float x1)
        {
            Slab(parent, "Terrace", x0, x1, 0f, TerraceDepth, 0f, TerraceHeight);

            float rampX1 = x1 - 0.2f;
            float rampX0 = rampX1 - TerraceRampWidth;
            float rampRun = TerraceHeight / Mathf.Tan(TerraceRampAngle * Mathf.Deg2Rad);
            Ramp(parent, "Terrace Ramp", (rampX0 + rampX1) * 0.5f, TerraceRampWidth, TerraceDepth + rampRun, TerraceDepth, 0f, TerraceHeight);

            float wallTop = TerraceHeight + TerraceWallHeight;
            float frontZ0 = TerraceDepth - TerraceWallThickness;
            Slab(parent, "Terrace Wall", x0, rampX0, frontZ0, TerraceDepth, TerraceHeight, TerraceWallHeight);
            Slab(parent, "Terrace Wall", rampX1, x1, frontZ0, TerraceDepth, TerraceHeight, TerraceWallHeight);
            Slab(parent, "Terrace Wall", x0, x0 + TerraceWallThickness, 0f, frontZ0, TerraceHeight, TerraceWallHeight);
            Slab(parent, "Terrace Wall", x1 - TerraceWallThickness, x1, 0f, frontZ0, TerraceHeight, TerraceWallHeight);
        }

        /// <summary>
        /// A free-standing wall centred on centre, length long along X, with
        /// mantleable edges on both sides of its top.
        /// </summary>
        private static void CompoundWall(Transform parent, string name, Vector3 centre, float length)
        {
            GameObject wall = new(name);
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = centre;
            Transform w = wall.transform;

            TestGeometry.Box("Wall", w, new Vector3(0f, CompoundWallHeight * 0.5f, 0f),
                new Vector3(length, CompoundWallHeight, CompoundWallThickness), _environment);

            // Seen from +Z, as a face: its outer face is the +Z side.
            float half = CompoundWallThickness * 0.5f;
            Face face = new(new Vector3(-length * 0.5f, 0f, half), Vector3.right, Vector3.back, length, 0f);
            EdgePair(w, face, length * 0.5f, CompoundWallHeight, length, CompoundWallThickness);
        }
    }
}
