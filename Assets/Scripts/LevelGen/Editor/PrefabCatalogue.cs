using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// The measured facts about every prefab under Assets/Prefabs, as
    /// written to Assets/Data/LevelGen/catalogue.json by
    /// PrefabCatalogueScanner. Only what can be read or measured from the
    /// prefab itself is here; what has to be judged by eye (category,
    /// front, connection points, verified) lives in the annotations file,
    /// so a rescan never overwrites it.
    ///
    /// Plain serializable classes so JsonUtility can write them. JsonUtility
    /// has no dictionaries, so counts are lists of NamedCount.
    /// </summary>
    [Serializable]
    public class PrefabCatalogue
    {
        // Bumped when the format changes, so the later stages can refuse an
        // old file rather than misread it.
        public const int CurrentFormat = 2;

        [Tooltip("The catalogue file's format number, e.g. 2 (the current format); later stages refuse a file in another format rather than misread it. Rescan to update.")]
        public int format = CurrentFormat;
        [Tooltip("The Unity version the catalogue was scanned with, e.g. \"6000.3.25f1\". For reading only.")]
        public string unityVersion;
        [Tooltip("How many prefabs the scan catalogued, e.g. 188. For reading only.")]
        public int prefabCount;
        [Tooltip("The scanned prefabs: one entry is one prefab's measurements (boxes, pivot, components, LODs, lights, suggested front and category), by path.")]
        public List<CatalogueEntry> prefabs = new();
    }

    /// <summary>
    /// One prefab's measurements. All positions and sizes are in metres in
    /// the prefab root's own space (the pivot is the origin), rounded to
    /// the millimetre so a rescan of unchanged prefabs writes the same file.
    /// </summary>
    [Serializable]
    public class CatalogueEntry
    {
        // Identity.
        [Tooltip("The prefab's name, e.g. \"TL_PlasterWalls_A_Cross\". Never trusted for direction.")]
        public string name;
        [Tooltip("The prefab's asset path, which the catalogue is keyed and sorted by, e.g. Assets/Prefabs/Environment/Walls/TL_PlasterWalls_A_Cross.prefab.")]
        public string path;
        [Tooltip("The prefab's GUID, which its annotation is keyed by, e.g. the 32-hex-digit id from its .meta file.")]
        public string guid;
        [Tooltip("The folder the prefab is in, under Assets/Prefabs, e.g. \"Environment/Walls\". Review Pieces filters by it.")]
        public string folder;
        [Tooltip("The asset path of the pack prefab this one is a variant of, e.g. a prefab in Assets/ASTROFISH_GAMES. Empty if not a variant.")]
        public string sourcePrefab;
        [Tooltip("True if the prefab is a variant of another (as every game prefab should be, of a pack prefab); false is flagged not-a-variant.")]
        public bool isVariant;

        // What the player sees: the box round every LOD0 renderer (renderers
        // not in an LODGroup count too; inactive or disabled ones don't).
        [Tooltip("True if the prefab has any enabled LOD0 renderer to measure a render box from; false is flagged no-render-bounds and the render box is unused.")]
        public bool hasRenderBounds;
        [Tooltip("The render box's lowest corner in the root's space, in metres, e.g. (-3, 0, -0.3) for a wall with its pivot at its base's left end.")]
        public Vector3 renderMin;
        [Tooltip("The render box's highest corner in the root's space, in metres, e.g. (0, 3.1, 0.4).")]
        public Vector3 renderMax;
        [Tooltip("The render box's size in metres (max - min), e.g. (3.05, 3.1, 0.7) for a plaster wall with trim.")]
        public Vector3 renderSize;

        // Where the pivot sits inside the render box, per axis: 0 = at the
        // box's min side, 0.5 = in the middle, 1 = at the max side. A floor
        // piece with its pivot on its underside has y near 0.
        [Tooltip("Where the pivot sits inside the render box, per axis, 0-1: 0 = at the min side, 0.5 = the middle, 1 = the max side, e.g. y 0 = pivot on the base. Off the base is flagged pivot-not-on-base.")]
        public Vector3 pivotInBounds;

        // What the body collides with: the box round every enabled
        // non-trigger collider.
        [Tooltip("True if the prefab has any enabled non-trigger collider; false is flagged no-colliders and the collider box is unused.")]
        public bool hasColliderBounds;
        [Tooltip("The collider box's lowest corner in the root's space, in metres (round every enabled non-trigger collider), e.g. (-3, 0, -0.25).")]
        public Vector3 colliderMin;
        [Tooltip("The collider box's highest corner in the root's space, in metres, e.g. (0, 3, 0.25).")]
        public Vector3 colliderMax;
        [Tooltip("The collider box's size in metres, e.g. (3, 3, 0.5): what the body bumps into, often slimmer than the render box.")]
        public Vector3 colliderSize;

        // The root's own stored transform (normally identity).
        [Tooltip("The root's own stored rotation in degrees, normally (0, 0, 0); anything else means the piece is turned inside its prefab.")]
        public Vector3 rootEuler;
        [Tooltip("The root's own stored scale, normally (1, 1, 1), e.g. 0.5 for the thin pipe kit's roots.")]
        public Vector3 rootScale;

        // Hierarchy.
        [Tooltip("How many objects the prefab holds, root included, e.g. 4 for a wall with LOD children. Higher = a heavier piece to place.")]
        public int objectCount;
        [Tooltip("How deep the prefab's hierarchy goes below the root, e.g. 1 = children only, no grandchildren.")]
        public int maxDepth;
        [Tooltip("The names of the root's direct children: one entry per child, e.g. \"LOD0\", \"LOD1\".")]
        public List<string> topChildren = new();
        [Tooltip("The outermost nested prefabs inside it: one entry is one prefab's asset path and how many times it occurs; any entry marks the piece an assembly.")]
        public List<NamedCount> nestedPrefabs = new();

        // Cost and set-up.
        [Tooltip("The components on the prefab: one entry is one component type and how many there are, e.g. MeshRenderer x3.")]
        public List<NamedCount> components = new();
        // MonoBehaviours other than a static piece's set-up (SurfaceTag,
        // RampCollider, BarrelCollider): Door, Ladder, ... The generator
        // never places a prefab that has any.
        [Tooltip("The gameplay scripts on the prefab (any MonoBehaviour but SurfaceTag, RampCollider, BarrelCollider): one entry per script type, e.g. \"Door\". The generator never places a prefab with any.")]
        public List<string> gameplayScripts = new();
        [Tooltip("How many LODGroups the prefab has, e.g. 1 for most pack pieces.")]
        public int lodGroupCount;
        [Tooltip("How many levels of detail its LODGroup has, e.g. 3; 0 with no LODGroup.")]
        public int lodLevels;
        [Tooltip("How many renderers draw its full detail (LOD0), e.g. 1. Higher = more draw calls per piece.")]
        public int renderersLod0;
        [Tooltip("How many triangles its full detail (LOD0) has, e.g. 2400. Higher = a costlier piece.")]
        public int trianglesLod0;
        [Tooltip("How many materials its full detail (LOD0) uses, e.g. 2. Higher = more draw calls per piece.")]
        public int materialsLod0;
        [Tooltip("The colliders on the prefab: one entry is one collider kind and how many, e.g. BoxCollider x2.")]
        public List<NamedCount> colliders = new();
        [Tooltip("How many of its colliders are triggers, e.g. 0 for a static wall.")]
        public int triggerColliders;
        [Tooltip("The Lights inside the prefab: one entry is one light (path, type, bake mode, shadows, range), e.g. a lamp post's point light. The generator disables them on placed instances, except gameplay and Baked lights.")]
        public List<LightInfo> lights = new();

        // Which way the piece faces, suggested from its LOD0 meshes by
        // WallOrientation (never from its name). A wall's back is one flat
        // plane and its front is detailed, so the busier of its two big sides
        // is the front. Only a suggestion: confirmed by eye in stage 2.
        // orientation is one of WallOrientation's Kind* values.
        [Tooltip("What the scanner judged the piece from its meshes: \"wall\", \"corner\", \"unsure\", \"not-a-slab\" or \"no-mesh-data\", e.g. \"wall\" for a slab with one flat back. Only a suggestion, confirmed in Review Pieces.")]
        public string orientation;
        [Tooltip("The front(s) suggested from the meshes, each a level axis of the root: one entry per front, e.g. one (0, 0, 1) for a wall facing +Z, two for a corner.")]
        public List<Vector3> suggestedFronts = new();
        [Tooltip("How sure the front suggestion is, 0-1, e.g. 0.8 for a wall with a busy front and a flat back; corners are low by nature (0.03-0.16). Higher = more sure.")]
        public float frontConfidence;
        // The flat back plane's box: the wall's structural slab, without the
        // trim and beams that stand out from it, so its width and height are
        // the module's true size. Only for a wall.
        [Tooltip("True if a flat back plane was found (walls only); its box is then the module's true size, without trim and beams.")]
        public bool hasBackPlane;
        [Tooltip("The back plane box's lowest corner in the root's space, in metres, e.g. (-3, 0, -0.3).")]
        public Vector3 backPlaneMin;
        [Tooltip("The back plane box's highest corner in the root's space, in metres, e.g. (0, 3, -0.3).")]
        public Vector3 backPlaneMax;
        [Tooltip("The back plane box's size in metres: the wall's true module, e.g. (3, 3, 0) for a plaster wall.")]
        public Vector3 backPlaneSize;
        // Where the wall would join its neighbours, on the back plane: left and
        // right (as seen from in front of it) at its base, top and bottom at
        // its middle. Only for a wall.
        [Tooltip("The suggested points where a wall joins its neighbours, on its back plane: one entry is one point, e.g. \"left\" and \"right\" at its base ends, \"top\" and \"bottom\" at its edge middles, plus door/window points. Walls and corners only.")]
        public List<ConnectionPoint> suggestedConnections = new();
        // What the suggestion was made from, for tuning: the triangles facing
        // each way along the two level axes.
        [Tooltip("What the front suggestion was made from, for tuning: one entry per level axis (+X, -X, +Z, -Z) with its triangles, area and planarity.")]
        public List<SideStats> sides = new();

        // The pack faces every piece +Z. Where the geometry gives no front of
        // its own (anything not a wall or corner, and not a floor, ground or
        // steps), +Z is taken as the front, with left/right/top/bottom
        // points on its -Z face, and this is true. The flag
        // front-not-plus-z marks a piece whose geometry says otherwise.
        [Tooltip("True where the geometry gives no front of its own (not a wall, corner, floor, ground or steps) and +Z is taken as the front, as the pack faces its pieces, e.g. a beam. Check it by eye.")]
        public bool frontAssumed;
        // The category PieceShape suggests from the measurements (one of
        // PieceAnnotation.Categories, or empty), for the review to confirm.
        [Tooltip("The category suggested from the measurements, for Review Pieces to confirm, e.g. \"window-wall\" for a wall with a hole in its back plane. Empty = no suggestion.")]
        public string suggestedCategory = "";
        // Gaps in a wall's back plane: a door reaches its base, a window
        // doesn't. Only for a wall.
        [Tooltip("The gaps found in a wall's back plane: one entry is one opening (door if it reaches the base, else window) with its box, e.g. a 1.2 x 1.3m window. Walls only.")]
        public List<Opening> openings = new();
        // The walking surface, from the colliders (not the meshes, so pieces
        // laid edge to edge make one even surface): the height holding most
        // of the level, upward-facing collider area, the share of that area
        // it holds, and the box round it (min and max at that height).
        [Tooltip("True if any upward-facing collider area was found to walk on; the walk fields are then set.")]
        public bool hasWalkSurface;
        [Tooltip("The walking surface's height above the pivot, in metres: the height holding most upward-facing collider area, e.g. 0.4 for a floor tile whose box is 40cm thick. Decking tiles are placed by it.")]
        public float walkHeight;
        [Tooltip("The share, 0-1, of the upward-facing collider area at Walk Height, e.g. 1 = all of it, a flat floor. Lower = an uneven top.")]
        public float walkShare;
        [Tooltip("The walking surface box's lowest corner at Walk Height, in the root's space, in metres, e.g. (-1.52, 0.4, -1.52).")]
        public Vector3 walkMin;
        [Tooltip("The walking surface box's highest corner at Walk Height, in the root's space, in metres, e.g. (1.52, 0.4, 1.52).")]
        public Vector3 walkMax;
        // Every RampCollider in the piece (a flight of steps' walking slope).
        [Tooltip("Every RampCollider in the piece: one entry is one walking slope (foot, top, way up, width), e.g. one for Steps Full Width.")]
        public List<RampInfo> ramps = new();

        // Things that make the measured box less trustworthy, or the piece
        // unsuitable as it is. Each also appears as a flag.
        [Tooltip("How many renderers are turned more than about 2.5 degrees off the root's axes (flagged rotated-pieces), e.g. 0. Above 0 the boxes are less trustworthy.")]
        public int rotatedRenderers;
        [Tooltip("How many renderers are scaled unevenly (flagged uneven-scale), e.g. 0. Above 0 the boxes are less trustworthy.")]
        public int unevenScaleRenderers;
        [Tooltip("How many renderers are mirrored by a negative scale (flagged mirrored), e.g. 0. Above 0 triangle facing is rewound.")]
        public int mirroredRenderers;
        [Tooltip("How many objects are off the Environment layer (flagged off-environment-layer), e.g. 0. Above 0 they may not block hand rays, light or the cull.")]
        public int offLayerObjects;
        [Tooltip("How many objects are not marked Static (flagged not-static), e.g. 0. Above 0 they won't batch, bake or occlude.")]
        public int nonStaticObjects;
        [Tooltip("How many renderers have no mesh (flagged missing-mesh), e.g. 0. Above 0 the piece draws incompletely.")]
        public int missingMeshes;
        [Tooltip("How many components are missing scripts (flagged missing-script), e.g. 0. Above 0 the prefab is broken.")]
        public int missingScripts;

        // Short kebab-case reasons this prefab needs a look by eye before the
        // generator trusts it (see PrefabCatalogueScanner.Flag* constants).
        [Tooltip("Short reasons this prefab needs a look by eye before the generator trusts it: one entry per reason, e.g. \"pivot-not-on-base\", \"no-colliders\", \"has-gameplay-scripts\". Empty = nothing odd found.")]
        public List<string> flags = new();
    }

    /// <summary>
    /// A name and how many times it occurs (a component type, a nested
    /// prefab's asset path, a collider kind).
    /// </summary>
    [Serializable]
    public class NamedCount
    {
        [Tooltip("What is counted: a component type, a nested prefab's asset path or a collider kind, e.g. \"BoxCollider\".")]
        public string name;
        [Tooltip("How many times it occurs in the prefab, e.g. 2 = two box colliders.")]
        public int count;
    }

    /// <summary>
    /// A point where a piece joins another: its position in the prefab
    /// root's space and the way out of the piece there.
    /// </summary>
    [Serializable]
    public class ConnectionPoint
    {
        [Tooltip("The point's name, which the generator looks it up by, e.g. \"left\" (a wall's base left end), \"top\", \"a\"/\"b\" (a pipe's flanges), \"eave\", \"window\" (blind-window insert spot).")]
        public string name;
        [Tooltip("Where the point is, in the prefab root's space, in metres, e.g. (0, 0, -0.3) at a wall's base left end on its back plane.")]
        public Vector3 position;
        [Tooltip("The way out of the piece at the point, a direction in the root's space, e.g. (1, 0, 0) for a wall's left end seen from in front, or along the pipe for a flange.")]
        public Vector3 direction;
    }

    /// <summary>
    /// A gap in a wall's back plane: "door" (reaches the base) or "window",
    /// and its box in the prefab root's space (flat, on the back plane).
    /// </summary>
    [Serializable]
    public class Opening
    {
        [Tooltip("What the gap is: \"door\" if it reaches the wall's base, else \"window\", e.g. \"window\" for PlasterWalls_A_WindowBase_Hole's hole.")]
        public string kind;
        [Tooltip("The opening box's lowest corner in the root's space, in metres (flat, on the back plane), e.g. (-2.1, 1, -0.3).")]
        public Vector3 min;
        [Tooltip("The opening box's highest corner in the root's space, in metres, e.g. (-0.9, 2.3, -0.3) for a 1.2 x 1.3m window.")]
        public Vector3 max;
    }

    /// <summary>
    /// A RampCollider's slope in the prefab root's space: the foot (on the
    /// floor, in the middle of its width), the top of the slope, the level
    /// way up the flight, and the width across it.
    /// </summary>
    [Serializable]
    public class RampInfo
    {
        [Tooltip("The ramp's foot, on the floor in the middle of its width, in the root's space, in metres, e.g. 0.28m before the first step of Steps Full Width.")]
        public Vector3 foot;
        [Tooltip("The top of the ramp's slope, in the root's space, in metres, e.g. 1.62m above the foot for Steps Full Width.")]
        public Vector3 top;
        [Tooltip("The level way up the flight, a direction in the root's space, e.g. (-1, 0, 0) for steps climbing along -X.")]
        public Vector3 direction;
        [Tooltip("The ramp's width across the flight, in metres, e.g. 2.7 for Steps Full Width.")]
        public float width;
    }

    /// <summary>
    /// The LOD0 triangles facing one way along a level axis (+X, -X, +Z,
    /// -Z): how many, their area, and how much of that area lies in one
    /// plane (planarity 1 = all of it), at planeDepth along the axis.
    /// </summary>
    [Serializable]
    public class SideStats
    {
        [Tooltip("Which level axis the triangles face: \"+X\", \"-X\", \"+Z\" or \"-Z\", e.g. \"-Z\" for a wall's flat back.")]
        public string side;
        [Tooltip("How many LOD0 triangles face that way, e.g. 40 for a flat back, 900 for a detailed front. A wall's front is the busier side.")]
        public int triangles;
        [Tooltip("The total area of those triangles, in square metres, e.g. 9 for a 3 x 3m wall's back.")]
        public float area;
        [Tooltip("The share, 0-1, of that area lying in one plane, e.g. 0.95 for a wall's flat back. Higher = flatter; a wall's back must be at least 0.7.")]
        public float planarity;
        [Tooltip("Where along the axis that plane lies, in metres from the pivot, e.g. -0.3 for a back plane 30cm behind it.")]
        public float planeDepth;
    }

    /// <summary>
    /// One Light inside a prefab: where it is, what kind, and how it is
    /// baked. The generator disables these on each placed instance.
    /// </summary>
    [Serializable]
    public class LightInfo
    {
        [Tooltip("The light's object path inside the prefab, from the root, e.g. \"Lamp/Point Light\".")]
        public string objectPath;
        [Tooltip("The light's type, e.g. \"Point\" or \"Spot\".")]
        public string type;
        [Tooltip("The light's bake mode, e.g. \"Mixed\" (a gameplay lamp's light) or \"Baked\" (a lit window's spill spot).")]
        public string mode;
        [Tooltip("The light's shadow type, e.g. \"Hard\", \"Soft\" or \"None\".")]
        public string shadows;
        [Tooltip("The light's range in metres, e.g. 5 for a lamp post's point light.")]
        public float range;
        [Tooltip("True if the light is enabled in the prefab; the generator disables lights on placed instances anyway, except gameplay and Baked lights.")]
        public bool enabled;
    }
}
