using System;
using System.Collections.Generic;
using Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Rooms behind the open windows: an Open Window Wall (with an
    /// OpenWindowInterior on it) is placed by hand in place of one of a
    /// generated facade's wall modules, anywhere under the sector's Geometry
    /// but outside Generated, so a regeneration keeps it.
    ///
    /// A generation takes out the wall pieces each window stands in place of
    /// (CutWalls, after the visibility cull, before the walls' colliders are
    /// merged, so no collider box closes the window). Menu TeaLeaf > Level
    /// Gen > Build Open Window Interiors then builds the room behind each
    /// window (Build), under Geometry/Open Window Interiors, where a
    /// regeneration never reaches: a box of thin walls (plain walls marked
    /// Open Window Interior in Review Pieces, of the building's style, else
    /// of the "default" style, scaled to WallThickness of their
    /// depth) the window's module wide times WidthModules, centred on it,
    /// and times DepthModules deep; its own front wall either side of the
    /// window; the settings' floor tile scaled to cover floor and ceiling
    /// (the ceiling's turned over, its top facing down into the room); a
    /// SoundRoom over it with a portal in the window. No NavMesh: guards
    /// never go in. The building proxies (BuildingProxies) fold the window
    /// and its room into the building they stand in.
    /// </summary>
    internal static class OpenWindowInteriors
    {
        internal const string GroupName = "Open Window Interiors";

        // A generated piece whose middle lies within this of a window's box
        // is one the window stands in place of.
        private const float CutMargin = 0.3f;

        // The pieces of a wall module a window stands in place of: the wall
        // and what was put into its holes.
        private static readonly string[] _cutCategories = { "wall", "window-wall", "door-wall", "window", "door" };
        private static readonly string[] _wallCategories = { "wall", "window-wall", "door-wall" };

        // Beats the street rooms (0) and junctions (1) where they overlap.
        private const int RoomPriority = 2;

        // How far past each face of the window the portal looks for its rooms.
        private const float PortalProbeOut = 0.3f;

        private const string DefaultStyle = "default";

        // Every object a room is built of, groups included.
        private static int EnvironmentLayer => LayerMask.NameToLayer("Environment");

        /// <summary>
        /// Every open window in a sector: those under its Geometry but not
        /// inside its Generated root.
        /// </summary>
        internal static List<OpenWindowInterior> InSector(Transform geometry)
        {
            List<OpenWindowInterior> windows = new();

            if (geometry == null) {
                return windows;
            }

            foreach (OpenWindowInterior window in geometry.GetComponentsInChildren<OpenWindowInterior>(true)) {
                if (window.GetComponentInParent<GeneratedRoot>(true) == null) {
                    windows.Add(window);
                }
            }

            return windows;
        }

        /// <summary>
        /// Takes out every generated wall piece (and the door or window
        /// insert in it) an open window of the sector stands in place of: a
        /// piece under Facades or Lots whose middle lies inside the window's
        /// box. Gives back the storey groups ("Storey n", "Corner") an open
        /// window stands in, whose colliders are then left unmerged
        /// (MergedColliders.RunWalls). Adds one line to log.
        /// </summary>
        internal static HashSet<Transform> CutWalls(Transform root, PieceAnnotations annotations, List<string> log)
        {
            List<OpenWindowInterior> windows = InSector(root.parent);
            HashSet<Transform> opened = new();

            if (windows.Count == 0) {
                log.Add("Open windows: none.");
                return opened;
            }

            Dictionary<string, PieceAnnotation> byGuid = ByGuid(annotations);
            List<GameObject> cut = new();
            List<string> missed = new();

            foreach (OpenWindowInterior window in windows) {
                Bounds box = LocalBox(window.transform);
                int before = cut.Count;

                foreach ((GameObject piece, PieceAnnotation annotation) in Pieces(root, byGuid)) {
                    if (Array.IndexOf(_cutCategories, annotation.category) >= 0 && !cut.Contains(piece) && Inside(window.transform, box, Middle(piece), CutMargin)) {
                        cut.Add(piece);
                    }
                }

                if (cut.Count == before) {
                    missed.Add(window.name);
                }
            }

            foreach (GameObject piece in cut) {
                for (Transform group = piece.transform.parent; group != null && group != root; group = group.parent) {
                    if (MergedColliders.IsWallGroup(group.name)) {
                        opened.Add(group);
                        break;
                    }
                }

                Undo.DestroyObjectImmediate(piece);
            }

            log.Add($"Open windows: {cut.Count} piece(s) taken out for {windows.Count} open window(s), {opened.Count} storey(s) left with their own colliders"
                + (missed.Count > 0 ? $"; standing in place of nothing: {string.Join(", ", missed)}." : "."));
            return opened;
        }

        /// <summary>
        /// The building group (Facades or Lots/[owner]/Building i) a window
        /// stands in: the one holding the generated wall nearest it. Null if
        /// none is found.
        /// </summary>
        internal static Transform BuildingOf(OpenWindowInterior window, Transform root, PieceAnnotations annotations)
        {
            GameObject nearest = NearestWall(window, root, ByGuid(annotations), out _);

            if (nearest == null) {
                return null;
            }

            for (Transform t = nearest.transform; t.parent != null && t.parent.parent != null; t = t.parent) {
                if (t.parent.parent.parent == root && (t.parent.parent.name == "Facades" || t.parent.parent.name == "Lots")) {
                    return t;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the rooms behind the selected open windows, else behind
        /// every one in the open scene.
        /// </summary>
        [MenuItem("TeaLeaf/Level Gen/Build Open Window Interiors")]
        private static void BuildSelected()
        {
            List<OpenWindowInterior> windows = new();

            foreach (GameObject selected in Selection.gameObjects) {
                windows.AddRange(selected.GetComponentsInChildren<OpenWindowInterior>(true));
            }

            if (windows.Count == 0) {
                windows.AddRange(UnityEngine.Object.FindObjectsByType<OpenWindowInterior>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            }

            if (windows.Count == 0) {
                Debug.LogWarning("OpenWindowInteriors: no Open Window Wall (with an OpenWindowInterior) selected or in the scene.");
                return;
            }

            Build(windows);
        }

        /// <summary>
        /// Builds (again) the room behind each window, one Undo step for all,
        /// and logs what was built.
        /// </summary>
        internal static void Build(List<OpenWindowInterior> windows)
        {
            if (!LayoutSpecValidator.LoadInputs(out PrefabCatalogue catalogue, out PieceAnnotations annotations)) {
                return;
            }

            GeneratorSettings settings = GeneratorSettings.Load();
            Dictionary<string, PieceAnnotation> byGuid = ByGuid(annotations);
            PiecePlacer placer = new(catalogue);
            PieceAnnotation tile = null;

            if (settings.OpenWindowFloorTile != null) {
                byGuid.TryGetValue(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settings.OpenWindowFloorTile)), out tile);
            }

            if (tile == null || placer.Entry(tile) == null || tile.width <= 0f || tile.depth <= 0f) {
                Debug.LogError("OpenWindowInteriors: the settings' Open Window Floor Tile is missing, or not catalogued and annotated with a size.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Open Window Interiors");
            List<string> log = new();

            foreach (OpenWindowInterior window in windows) {
                log.Add(BuildOne(window, annotations, byGuid, placer, tile, settings));
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(windows[0].gameObject.scene);
            Debug.Log($"OpenWindowInteriors: {windows.Count} window(s).\n{string.Join("\n", log)}\nRebuild Building Proxies to fold them into their buildings, then rebake.");
        }

        /// <summary>
        /// Builds the room behind one window, replacing the one built before.
        /// Gives back its log line.
        /// </summary>
        private static string BuildOne(OpenWindowInterior window, PieceAnnotations annotations, Dictionary<string, PieceAnnotation> byGuid, PiecePlacer placer,
            PieceAnnotation tile, GeneratorSettings settings)
        {
            if (window.GetComponentInParent<GeneratedRoot>(true) != null) {
                return $"    {window.name}: inside Generated, which a regeneration replaces whole; move it under the sector's Geometry first. Not built.";
            }

            Transform geometry = Geometry(window.transform);

            if (geometry == null) {
                return $"    {window.name}: not under Environment/Sectors/<sector>/Geometry. Not built.";
            }

            Bounds box = LocalBox(window.transform);

            if (box.size == Vector3.zero) {
                return $"    {window.name}: draws nothing to measure. Not built.";
            }

            Transform root = geometry.Find("Generated");
            List<string> styles = new();
            bool stillWalled = false;

            if (root != null) {
                GameObject nearest = NearestWall(window, root, byGuid, out stillWalled);

                if (nearest != null && byGuid.TryGetValue(GuidOf(nearest), out PieceAnnotation beside)) {
                    styles.AddRange(beside.styles);
                }
            }

            // The walls: building's style, else the default style; of one
            // height (the commonest), so the ceiling meets every one.
            List<PieceAnnotation> walls = Walls(annotations, placer, styles, out string style);

            if (walls.Count == 0) {
                return $"    {window.name}: no verified plain wall marked Open Window Interior in style(s) {(styles.Count > 0 ? string.Join(", ", styles) : "(none found beside it)")} or \"{DefaultStyle}\". Not built.";
            }

            float height = Median(walls, p => p.height);
            walls.RemoveAll(p => Mathf.Abs(p.height - height) > settings.SizeTolerance);
            float widest = 0f;

            foreach (PieceAnnotation wall in walls) {
                widest = Mathf.Max(widest, wall.width);
            }

            List<PieceAnnotation> fullSize = walls.FindAll(p => p.width >= widest * settings.FullSizeShare);
            float normal = Median(fullSize, p => p.width);
            float thickness = 0f;

            foreach (PieceAnnotation wall in walls) {
                thickness = Mathf.Max(thickness, Reach(wall, placer.Entry(wall)) * settings.WallThickness);
            }

            if (window.Built != null) {
                Undo.DestroyObjectImmediate(window.Built);
            }

            Transform group = Child(geometry, GroupName);
            GameObject room = new($"{window.name} Interior") {
                layer = EnvironmentLayer
            };
            Undo.RegisterCreatedObjectUndo(room, "Build Open Window Interiors");
            room.transform.SetParent(group, false);
            SerializedObject serialized = new(window);
            serialized.FindProperty("built").objectReferenceValue = room;
            serialized.ApplyModifiedProperties();

            // The room's frame: origin at the window module's middle, on the
            // storey's base, on the window's inside face; X along the facade,
            // Z out through the window to the street. The room lies behind it.
            float overlap = settings.WallOverlap;
            float module = box.size.x - overlap;
            float width = window.WidthModules * module;
            float depth = window.DepthModules * module;
            Quaternion turn = Quaternion.Euler(0f, window.transform.eulerAngles.y, 0f);
            Vector3 origin = window.transform.TransformPoint(new Vector3(box.center.x, box.min.y, box.min.z));
            Frame frame = new() { Origin = origin, Turn = turn };
            System.Random random = new(Seed(window.transform.position));

            // Each wall's back plane on the outside of the room, its face
            // inside, ends running on past the neighbours' backs so every
            // corner closes without a corner piece.
            Transform wallGroup = Child(room.transform, "Walls");
            float halfWidth = width * 0.5f;
            float t = thickness;
            // Each face's pieces, whose colliders become one box.
            List<(string name, List<GameObject> pieces)> faces = new() {
                ("Back", LayRun(frame, new Vector3(halfWidth + t, 0f, -depth - t), Vector3.forward, width + 2f * t, walls, normal, placer, settings, random, wallGroup)),
                ("Left", LayRun(frame, new Vector3(-halfWidth - t, 0f, -depth - t), Vector3.right, depth + 2f * t, walls, normal, placer, settings, random, wallGroup)),
                ("Right", LayRun(frame, new Vector3(halfWidth + t, 0f, t), Vector3.left, depth + 2f * t, walls, normal, placer, settings, random, wallGroup))
            };

            // The front wall either side of the window, each end tucked
            // into the window's own inside wall. Its pieces keep their own
            // colliders: no merged box beside the window.
            float side = (width - module) * 0.5f;
            int placed = 0;

            if (side > settings.SizeTolerance) {
                float length = side + t + overlap;
                placed += LayRun(frame, new Vector3(-halfWidth - t, 0f, t), Vector3.back, length, walls, normal, placer, settings, random, wallGroup).Count;
                placed += LayRun(frame, new Vector3(module * 0.5f - overlap, 0f, t), Vector3.back, length, walls, normal, placer, settings, random, wallGroup).Count;
            }


            foreach ((string name, List<GameObject> pieces) face in faces) {
                placed += face.pieces.Count;
            }

            // Floor and ceiling under and over the walls' whole outline.
            List<GameObject> floor = LayTiles(frame, tile, width + 2f * t, depth + 2f * t, -depth * 0.5f, settings.OpenWindowFloorOffset, false, settings, placer,
                Child(room.transform, "Floor"));
            List<GameObject> ceiling = LayTiles(frame, tile, width + 2f * t, depth + 2f * t, -depth * 0.5f, height + settings.OpenWindowCeilingOffset, true, settings,
                placer, Child(room.transform, "Ceiling"));

            // One box per face in place of its pieces' colliders (not the
            // front, which holds the window).
            Transform colliders = Child(room.transform, "Colliders");
            int removed = 0;

            foreach ((string name, List<GameObject> pieces) in faces) {
                removed += MergeFace(frame, pieces, name, SurfaceType.Stone, colliders);
            }

            removed += MergeFace(frame, floor, "Floor", SurfaceType.Wood, colliders);
            removed += MergeFace(frame, ceiling, "Ceiling", SurfaceType.Wood, colliders);

            AddSound(window.transform, box, frame, width, depth, height, settings, room.transform);
            float range = AddLight(frame, width, depth, height, settings, room.transform);
            int backings = root != null ? RemoveBacking(root, frame, width + 2f * t, depth + 2f * t, height) : 0;

            return $"    {window.name}: {width:0.##} x {depth:0.##}m, {height:0.##}m high, {placed} wall(s) of style \"{style}\" at {settings.WallThickness:0.##} depth, "
                + $"{floor.Count + ceiling.Count} tile(s), {removed} collider(s) replaced by {colliders.childCount} box(es), sound room and portal, light of range {range:0.##}m, "
                + $"{backings} backing box(es) taken out"
                + (stillWalled ? "; a generated wall still stands in the window: regenerate the level to open it." : ".");
        }

        /// <summary>
        /// A room's frame: its origin and turn (yaw only) in the world.
        /// </summary>
        private struct Frame
        {
            public Vector3 Origin;
            public Quaternion Turn;

            public Vector3 Point(Vector3 local)
            {
                return Origin + Turn * local;
            }
        }

        /// <summary>
        /// Lays plain walls end to end from start (the first one's left point,
        /// on its back plane, in the room's frame) for a length, facing
        /// inward (in the room's frame), running along their right as seen
        /// from in front; whole walls by the fitting rule (FitWidths), each
        /// scaled to its width and to the settings' thickness share of its
        /// depth. Gives back those placed.
        /// </summary>
        private static List<GameObject> LayRun(Frame frame, Vector3 start, Vector3 inward, float length, List<PieceAnnotation> walls, float normal, PiecePlacer placer,
            GeneratorSettings settings, System.Random random, Transform parent)
        {
            float overlap = settings.WallOverlap;
            Vector3 right = Vector3.Cross(Vector3.up, -inward);
            float along = 0f;
            List<GameObject> placed = new();
            PieceAnnotation previous = null;

            foreach (float width in FacadeBuilder.FitWidths(length + overlap, normal, overlap, settings)) {
                PieceAnnotation piece = PiecePlacer.Pick(ForWidth(walls, width, settings), previous, random);
                Vector3 front = piece.fronts[0];
                Vector3 pieceRight = Vector3.Cross(Vector3.up, -front);
                Vector3 scale = Vector3.one + (settings.WallThickness - 1f) * Abs(front) + (width / piece.width - 1f) * Abs(pieceRight);
                Quaternion rotation = frame.Turn * Quaternion.LookRotation(inward) * Quaternion.Inverse(Quaternion.LookRotation(front));
                Vector3 left = piece.connections.Find(c => c.name == "left").position;

                GameObject made = placer.PlaceAt(piece, left, frame.Point(start + right * along), rotation, scale, parent);

                if (made != null) {
                    placed.Add(made);
                }

                along += width - overlap;
                previous = piece;
            }

            return placed;
        }

        /// <summary>
        /// Covers a rectangle of the room (across x along X, deep along Z,
        /// centred on X at 0 and on Z at middleZ) with the fewest whole tiles,
        /// neighbours overlapping by the settings' tile overlap, all scaled
        /// alike to cover it exactly, their walking surfaces at height
        /// (turned over for a ceiling: the surface facing down). Gives back
        /// those placed.
        /// </summary>
        private static List<GameObject> LayTiles(Frame frame, PieceAnnotation tile, float across, float deep, float middleZ, float height, bool overhead,
            GeneratorSettings settings, PiecePlacer placer, Transform parent)
        {
            float overlap = settings.OpenWindowTileOverlap;
            TileCount(across, tile.width, overlap, out int countX, out float sizeX);
            TileCount(deep, tile.depth, overlap, out int countZ, out float sizeZ);
            Vector3 scale = new(sizeX / tile.width, 1f, sizeZ / tile.depth);
            Quaternion rotation = overhead ? frame.Turn * Quaternion.Euler(180f, 0f, 0f) : frame.Turn;
            List<GameObject> placed = new();

            for (int x = 0; x < countX; x++) {
                for (int z = 0; z < countZ; z++) {
                    Vector3 local = new(-across * 0.5f + sizeX * 0.5f + x * (sizeX - overlap), height, middleZ - deep * 0.5f + sizeZ * 0.5f + z * (sizeZ - overlap));
                    GameObject made = placer.PlaceTile(tile, frame.Point(local), rotation, scale, parent);

                    if (made != null) {
                        placed.Add(made);
                    }
                }
            }

            return placed;
        }

        /// <summary>
        /// The fewest tiles of a size, each overlapping the next, that cover a
        /// length, and the size each is scaled to so they cover it exactly.
        /// </summary>
        private static void TileCount(float length, float tileSize, float overlap, out int count, out float size)
        {
            float step = Mathf.Max(0.01f, tileSize - overlap);
            count = Mathf.Max(1, Mathf.CeilToInt((length - overlap) / step - 0.001f));
            size = (length + (count - 1) * overlap) / count;
        }

        /// <summary>
        /// Replaces the box colliders of one face's pieces with one box
        /// round them all, in the room's frame, under parent: on the first
        /// piece's layer with its static flags, with its SurfaceTag copied
        /// (else one set to fallback). Gives back how many were removed.
        /// </summary>
        private static int MergeFace(Frame frame, List<GameObject> pieces, string name, SurfaceType fallback, Transform parent)
        {
            Quaternion inverse = Quaternion.Inverse(frame.Turn);
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            // The first collider's object, not the collider: a destroyed
            // component reads as null.
            GameObject first = null;
            SurfaceTag sourceTag = null;
            int removed = 0;

            foreach (GameObject piece in pieces) {
                foreach (BoxCollider collider in piece.GetComponentsInChildren<BoxCollider>(true)) {
                    if (!collider.enabled || collider.isTrigger) {
                        continue;
                    }

                    Vector3 half = collider.size * 0.5f;

                    for (int i = 0; i < 8; i++) {
                        Vector3 sign = new((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                        Vector3 corner = inverse * (collider.transform.TransformPoint(collider.center + Vector3.Scale(half, sign)) - frame.Origin);
                        min = Vector3.Min(min, corner);
                        max = Vector3.Max(max, corner);
                    }

                    if (first == null) {
                        first = collider.gameObject;
                        sourceTag = SurfaceTag.Find(collider);
                    }

                    UnityEngine.Object.DestroyImmediate(collider);
                    removed++;
                }
            }

            if (first == null) {
                return 0;
            }

            GameObject made = new(name) {
                layer = first.layer
            };
            GameObjectUtility.SetStaticEditorFlags(made, GameObjectUtility.GetStaticEditorFlags(first));
            made.transform.SetParent(parent, false);
            made.transform.SetPositionAndRotation(frame.Point((min + max) * 0.5f), frame.Turn);
            made.AddComponent<BoxCollider>().size = max - min;
            SurfaceTag tag = made.AddComponent<SurfaceTag>();

            if (sourceTag != null) {
                EditorUtility.CopySerialized(sourceTag, tag);
            } else {
                SerializedObject serialized = new(tag);
                serialized.FindProperty("surface").enumValueIndex = (int)fallback;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return removed;
        }

        /// <summary>
        /// A SoundRoom filling the room (it beats the streets' rooms where
        /// they overlap) and an open portal in the window joining it to the
        /// street's room outside.
        /// </summary>
        private static void AddSound(Transform window, Bounds box, Frame frame, float width, float depth, float height, GeneratorSettings settings, Transform parent)
        {
            GameObject roomObject = new("Sound Room") {
                layer = EnvironmentLayer
            };
            Undo.RegisterCreatedObjectUndo(roomObject, "Build Open Window Interiors");
            roomObject.transform.SetParent(parent, false);
            roomObject.transform.SetPositionAndRotation(frame.Point(new Vector3(0f, height * 0.5f, -depth * 0.5f)), frame.Turn);
            SoundRoom room = roomObject.AddComponent<SoundRoom>();
            SerializedObject serializedRoom = new(room);
            serializedRoom.FindProperty("size").vector3Value = new Vector3(width, height, depth);
            serializedRoom.FindProperty("priority").intValue = RoomPriority;
            serializedRoom.ApplyModifiedPropertiesWithoutUndo();

            GameObject portalObject = new("Portal Window") {
                layer = EnvironmentLayer
            };
            Undo.RegisterCreatedObjectUndo(portalObject, "Build Open Window Interiors");
            portalObject.transform.SetParent(roomObject.transform, false);
            portalObject.transform.SetPositionAndRotation(window.TransformPoint(box.center), frame.Turn);
            SoundPortal portal = portalObject.AddComponent<SoundPortal>();
            SerializedObject serializedPortal = new(portal);
            serializedPortal.FindProperty("size").vector2Value = settings.OpenWindowPortalSize;
            serializedPortal.FindProperty("probeDistance").floatValue = box.extents.z + PortalProbeOut;
            serializedPortal.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A Baked point light over the room's middle, the settings' distance
        /// below its ceiling, its range the settings' share of the distance to
        /// the furthest floor corner so it just lights the corners. Soft baked
        /// shadows, so the walls keep it in the room. Baked only: nothing at
        /// runtime, and gameplay light (Core.LightSource) never sees it.
        /// Gives back its range.
        /// </summary>
        private static float AddLight(Frame frame, float width, float depth, float height, GeneratorSettings settings, Transform parent)
        {
            float lightHeight = height + settings.OpenWindowCeilingOffset - settings.OpenWindowLightBelowCeiling;
            float corner = new Vector3(width * 0.5f, lightHeight - settings.OpenWindowFloorOffset, depth * 0.5f).magnitude;
            float range = corner * settings.OpenWindowLightRangeShare;

            GameObject lightObject = new("Room Light") {
                layer = EnvironmentLayer
            };
            Undo.RegisterCreatedObjectUndo(lightObject, "Build Open Window Interiors");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.SetPositionAndRotation(frame.Point(new Vector3(0f, lightHeight, -depth * 0.5f)), frame.Turn);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = settings.OpenWindowLightColour;
            light.intensity = settings.OpenWindowLightIntensity;
            light.bounceIntensity = settings.OpenWindowLightIndirect;
            light.range = range;
            light.shadows = LightShadows.Soft;
            return range;
        }

        /// <summary>
        /// Takes out every backing box (Generated/Backing) reaching into the
        /// room's outline (walls' backs, floor to walls' top): the boxes stood
        /// behind the facade where the room now is. If the Generated root had
        /// no hand edits before, its fingerprint is taken again, so a
        /// regeneration doesn't take the removal for one. Gives back how many
        /// were taken out.
        /// </summary>
        private static int RemoveBacking(Transform root, Frame frame, float across, float deep, float height)
        {
            Transform group = root.Find(Backing.GroupName);

            if (group == null) {
                return 0;
            }

            GeneratedRoot generated = root.GetComponent<GeneratedRoot>();
            bool unedited = generated != null && generated.Fingerprint == LevelGenerator.Fingerprint(root);
            // Boxes only touching the room's outline stay.
            const float margin = 0.01f;
            Vector3 roomMin = new(-across * 0.5f + margin, margin, -deep + margin);
            Vector3 roomMax = new(across * 0.5f - margin, height - margin, -margin);
            Quaternion inverse = Quaternion.Inverse(frame.Turn);
            List<GameObject> inside = new();

            foreach (MeshFilter filter in group.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh == null) {
                    continue;
                }

                // The box's corners in the room's frame, the window's inside
                // face at z 0 (the room's front walls stand just in front).
                Bounds mesh = filter.sharedMesh.bounds;
                Vector3 min = Vector3.positiveInfinity;
                Vector3 max = Vector3.negativeInfinity;

                for (int c = 0; c < 8; c++) {
                    Vector3 local = mesh.center + Vector3.Scale(mesh.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                    Vector3 inRoom = inverse * (filter.transform.TransformPoint(local) - frame.Origin);
                    min = Vector3.Min(min, inRoom);
                    max = Vector3.Max(max, inRoom);
                }

                if (min.x < roomMax.x && max.x > roomMin.x && min.y < roomMax.y && max.y > roomMin.y && min.z < roomMax.z && max.z > roomMin.z) {
                    inside.Add(filter.gameObject);
                }
            }

            foreach (GameObject box in inside) {
                Undo.DestroyObjectImmediate(box);
            }

            if (inside.Count > 0 && unedited) {
                Undo.RecordObject(generated, "Build Open Window Interiors");
                generated.Record(generated.SpecPath, LevelGenerator.Fingerprint(root));
            }

            return inside.Count;
        }

        /// <summary>
        /// The usable interior walls: verified, not excluded, marked Open
        /// Window Interior, plain, catalogued, one front and a left point;
        /// those of any of the styles, else those of the default style.
        /// Gives back which style they came from.
        /// </summary>
        private static List<PieceAnnotation> Walls(PieceAnnotations annotations, PiecePlacer placer, List<string> styles, out string style)
        {
            List<PieceAnnotation> usable = annotations.pieces.FindAll(p => p.verified && !p.excluded && p.openWindowInterior && p.category == "wall"
                && p.fronts.Count == 1 && p.width > 0f && placer.Entry(p) != null && p.connections.Exists(c => c.name == "left"));
            List<PieceAnnotation> matched = usable.FindAll(p => p.styles.Exists(styles.Contains));

            if (matched.Count > 0) {
                style = string.Join(", ", styles.FindAll(s => matched.Exists(p => p.styles.Contains(s))));
                return matched;
            }

            style = DefaultStyle;
            return usable.FindAll(p => p.styles.Contains(DefaultStyle));
        }

        /// <summary>
        /// The walls that scale to a width within the settings' limit, else
        /// the one nearest it in width.
        /// </summary>
        private static List<PieceAnnotation> ForWidth(List<PieceAnnotation> walls, float width, GeneratorSettings settings)
        {
            List<PieceAnnotation> fitting = walls.FindAll(p => Mathf.Abs(width / p.width - 1f) <= settings.WallScaleLimit);

            if (fitting.Count > 0) {
                return fitting;
            }

            PieceAnnotation nearest = walls[0];

            foreach (PieceAnnotation wall in walls) {
                if (Mathf.Abs(wall.width - width) < Mathf.Abs(nearest.width - width)) {
                    nearest = wall;
                }
            }

            return new List<PieceAnnotation> { nearest };
        }

        /// <summary>
        /// How far a wall stands out in front of its back plane (its left
        /// point) to the front of its render box.
        /// </summary>
        private static float Reach(PieceAnnotation wall, CatalogueEntry entry)
        {
            Vector3 front = wall.fronts[0];
            Vector3 left = wall.connections.Find(c => c.name == "left").position;
            Vector3 furthest = front.x + front.y + front.z > 0f ? entry.renderMax : entry.renderMin;
            return Vector3.Dot(front, furthest) - Vector3.Dot(front, left);
        }

        /// <summary>
        /// The generated wall (wall, window or door wall under Facades or
        /// Lots) nearest the window's middle, beside it rather than in its
        /// place, on the same storey; and whether some generated wall still
        /// stands in its place.
        /// </summary>
        private static GameObject NearestWall(OpenWindowInterior window, Transform root, Dictionary<string, PieceAnnotation> byGuid, out bool stillWalled)
        {
            Bounds box = LocalBox(window.transform);
            Vector3 middle = window.transform.TransformPoint(box.center);
            GameObject nearest = null;
            float best = float.MaxValue;
            stillWalled = false;

            foreach ((GameObject piece, PieceAnnotation annotation) in Pieces(root, byGuid)) {
                if (Array.IndexOf(_wallCategories, annotation.category) < 0) {
                    continue;
                }

                Vector3 at = Middle(piece);

                if (Inside(window.transform, box, at, CutMargin)) {
                    stillWalled = true;
                    continue;
                }

                if (Mathf.Abs(at.y - middle.y) > box.extents.y) {
                    continue;
                }

                float distance = (at - middle).sqrMagnitude;

                if (distance < best) {
                    best = distance;
                    nearest = piece;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Every placed piece under the root's Facades and Lots (outermost
        /// prefab instances) with its annotation.
        /// </summary>
        private static IEnumerable<(GameObject, PieceAnnotation)> Pieces(Transform root, Dictionary<string, PieceAnnotation> byGuid)
        {
            foreach (string treeName in new[] { "Facades", "Lots" }) {
                Transform tree = root.Find(treeName);

                if (tree == null) {
                    continue;
                }

                foreach (Transform t in tree.GetComponentsInChildren<Transform>(true)) {
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) && byGuid.TryGetValue(GuidOf(t.gameObject), out PieceAnnotation annotation)) {
                        yield return (t.gameObject, annotation);
                    }
                }
            }
        }

        /// <summary>
        /// A box in an object's own space holding its drawn meshes (each
        /// piece's full detail only where it has levels of detail).
        /// </summary>
        internal static Bounds LocalBox(Transform t)
        {
            Bounds box = default;
            bool any = false;
            MeshRenderer[] renderers = t.GetComponentsInChildren<MeshRenderer>(true);
            bool lods = Array.Exists(renderers, r => r.name.EndsWith("_LOD0"));

            foreach (MeshRenderer renderer in renderers) {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();

                if ((lods && renderer.name.Contains("_LOD") && !renderer.name.EndsWith("_LOD0")) || filter == null || filter.sharedMesh == null) {
                    continue;
                }

                Bounds mesh = filter.sharedMesh.bounds;

                for (int c = 0; c < 8; c++) {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                    Vector3 local = t.InverseTransformPoint(renderer.transform.TransformPoint(corner));

                    if (!any) {
                        box = new Bounds(local, Vector3.zero);
                        any = true;
                    } else {
                        box.Encapsulate(local);
                    }
                }
            }

            return box;
        }

        /// <summary>
        /// The middle of a piece's drawn bounds in the world.
        /// </summary>
        private static Vector3 Middle(GameObject piece)
        {
            Bounds bounds = default;
            bool any = false;

            foreach (MeshRenderer renderer in piece.GetComponentsInChildren<MeshRenderer>(true)) {
                if (!any) {
                    bounds = renderer.bounds;
                    any = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any ? bounds.center : piece.transform.position;
        }

        /// <summary>
        /// Whether a world point lies inside a box in an object's own space,
        /// grown by a margin.
        /// </summary>
        private static bool Inside(Transform t, Bounds box, Vector3 world, float margin)
        {
            Vector3 local = t.InverseTransformPoint(world) - box.center;
            return Mathf.Abs(local.x) <= box.extents.x + margin && Mathf.Abs(local.y) <= box.extents.y + margin && Mathf.Abs(local.z) <= box.extents.z + margin;
        }

        /// <summary>
        /// The sector's Geometry object an object stands under
        /// (Environment/Sectors/[sector]/Geometry), or null.
        /// </summary>
        private static Transform Geometry(Transform t)
        {
            for (Transform p = t.parent; p != null; p = p.parent) {
                if (p.name == "Geometry" && p.parent != null && p.parent.parent != null && p.parent.parent.name == "Sectors") {
                    return p;
                }
            }

            return null;
        }

        /// <summary>
        /// A child by name, made (with Undo) if missing.
        /// </summary>
        private static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);

            if (child != null) {
                return child;
            }

            GameObject made = new(name) {
                layer = EnvironmentLayer
            };
            Undo.RegisterCreatedObjectUndo(made, "Build Open Window Interiors");
            made.transform.SetParent(parent, false);
            return made.transform;
        }

        private static Dictionary<string, PieceAnnotation> ByGuid(PieceAnnotations annotations)
        {
            Dictionary<string, PieceAnnotation> byGuid = new();

            foreach (PieceAnnotation piece in annotations.pieces) {
                byGuid[piece.guid] = piece;
            }

            return byGuid;
        }

        private static string GuidOf(GameObject instance)
        {
            return AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance));
        }

        private static float Median(List<PieceAnnotation> pieces, Func<PieceAnnotation, float> value)
        {
            List<float> values = pieces.ConvertAll(p => value(p));
            values.Sort();
            return values[(values.Count - 1) / 2];
        }

        private static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        /// <summary>
        /// A seed from a position (to the centimetre), so a window's room is
        /// built the same each time until the window moves.
        /// </summary>
        private static int Seed(Vector3 position)
        {
            unchecked {
                return Mathf.RoundToInt(position.x * 100f) * 73856093 ^ Mathf.RoundToInt(position.y * 100f) * 19349663 ^ Mathf.RoundToInt(position.z * 100f) * 83492791;
            }
        }
    }
}
