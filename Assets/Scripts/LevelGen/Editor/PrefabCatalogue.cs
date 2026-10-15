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

        public int format = CurrentFormat;
        public string unityVersion;
        public int prefabCount;
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
        public string name;
        public string path;
        public string guid;
        public string folder;
        public string sourcePrefab;
        public bool isVariant;

        // What the player sees: the box round every LOD0 renderer (renderers
        // not in an LODGroup count too; inactive or disabled ones don't).
        public bool hasRenderBounds;
        public Vector3 renderMin;
        public Vector3 renderMax;
        public Vector3 renderSize;

        // Where the pivot sits inside the render box, per axis: 0 = at the
        // box's min side, 0.5 = in the middle, 1 = at the max side. A floor
        // piece with its pivot on its underside has y near 0.
        public Vector3 pivotInBounds;

        // What the body collides with: the box round every enabled
        // non-trigger collider.
        public bool hasColliderBounds;
        public Vector3 colliderMin;
        public Vector3 colliderMax;
        public Vector3 colliderSize;

        // The root's own stored transform (normally identity).
        public Vector3 rootEuler;
        public Vector3 rootScale;

        // Hierarchy.
        public int objectCount;
        public int maxDepth;
        public List<string> topChildren = new();
        public List<NamedCount> nestedPrefabs = new();

        // Cost and set-up.
        public List<NamedCount> components = new();
        // MonoBehaviours other than a static piece's set-up (SurfaceTag,
        // RampCollider, BarrelCollider): Door, Ladder, ... The generator
        // never places a prefab that has any.
        public List<string> gameplayScripts = new();
        public int lodGroupCount;
        public int lodLevels;
        public int renderersLod0;
        public int trianglesLod0;
        public int materialsLod0;
        public List<NamedCount> colliders = new();
        public int triggerColliders;
        public List<LightInfo> lights = new();

        // Which way the piece faces, suggested from its LOD0 meshes by
        // WallOrientation (never from its name). A wall's back is one flat
        // plane and its front is detailed, so the busier of its two big sides
        // is the front. Only a suggestion: confirmed by eye in stage 2.
        // orientation is one of WallOrientation's Kind* values.
        public string orientation;
        public List<Vector3> suggestedFronts = new();
        public float frontConfidence;
        // The flat back plane's box: the wall's structural slab, without the
        // trim and beams that stand out from it, so its width and height are
        // the module's true size. Only for a wall.
        public bool hasBackPlane;
        public Vector3 backPlaneMin;
        public Vector3 backPlaneMax;
        public Vector3 backPlaneSize;
        // Where the wall would join its neighbours, on the back plane: left and
        // right (as seen from in front of it) at its base, top and bottom at
        // its middle. Only for a wall.
        public List<ConnectionPoint> suggestedConnections = new();
        // What the suggestion was made from, for tuning: the triangles facing
        // each way along the two level axes.
        public List<SideStats> sides = new();

        // The pack faces every piece +Z. Where the geometry gives no front of
        // its own (anything not a wall or corner, and not a floor, ground or
        // steps), +Z is taken as the front, with left/right/top/bottom
        // points on its -Z face, and this is true. The flag
        // front-not-plus-z marks a piece whose geometry says otherwise.
        public bool frontAssumed;
        // The category PieceShape suggests from the measurements (one of
        // PieceAnnotation.Categories, or empty), for the review to confirm.
        public string suggestedCategory = "";
        // Gaps in a wall's back plane: a door reaches its base, a window
        // doesn't. Only for a wall.
        public List<Opening> openings = new();
        // The walking surface, from the colliders (not the meshes, so pieces
        // laid edge to edge make one even surface): the height holding most
        // of the level, upward-facing collider area, the share of that area
        // it holds, and the box round it (min and max at that height).
        public bool hasWalkSurface;
        public float walkHeight;
        public float walkShare;
        public Vector3 walkMin;
        public Vector3 walkMax;
        // Every RampCollider in the piece (a flight of steps' walking slope).
        public List<RampInfo> ramps = new();

        // Things that make the measured box less trustworthy, or the piece
        // unsuitable as it is. Each also appears as a flag.
        public int rotatedRenderers;
        public int unevenScaleRenderers;
        public int mirroredRenderers;
        public int offLayerObjects;
        public int nonStaticObjects;
        public int missingMeshes;
        public int missingScripts;

        // Short kebab-case reasons this prefab needs a look by eye before the
        // generator trusts it (see PrefabCatalogueScanner.Flag* constants).
        public List<string> flags = new();
    }

    /// <summary>
    /// A name and how many times it occurs (a component type, a nested
    /// prefab's asset path, a collider kind).
    /// </summary>
    [Serializable]
    public class NamedCount
    {
        public string name;
        public int count;
    }

    /// <summary>
    /// A point where a piece joins another: its position in the prefab
    /// root's space and the way out of the piece there.
    /// </summary>
    [Serializable]
    public class ConnectionPoint
    {
        public string name;
        public Vector3 position;
        public Vector3 direction;
    }

    /// <summary>
    /// A gap in a wall's back plane: "door" (reaches the base) or "window",
    /// and its box in the prefab root's space (flat, on the back plane).
    /// </summary>
    [Serializable]
    public class Opening
    {
        public string kind;
        public Vector3 min;
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
        public Vector3 foot;
        public Vector3 top;
        public Vector3 direction;
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
        public string side;
        public int triangles;
        public float area;
        public float planarity;
        public float planeDepth;
    }

    /// <summary>
    /// One Light inside a prefab: where it is, what kind, and how it is
    /// baked. The generator disables these on each placed instance.
    /// </summary>
    [Serializable]
    public class LightInfo
    {
        public string objectPath;
        public string type;
        public string mode;
        public string shadows;
        public float range;
        public bool enabled;
    }
}
