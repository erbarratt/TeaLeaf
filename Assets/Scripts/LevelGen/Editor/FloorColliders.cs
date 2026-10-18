using Core;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Replaces the colliders of the floor tiles StreetGround lays with one
    /// box per run of tiles: the tiles side by side along one row whose
    /// collider tops are at the same height (and whose surface is the same).
    /// The box is exactly what the run's tile colliders covered, in the row's
    /// own frame, so no gap opens and no invisible floor appears; the tiles'
    /// own colliders are removed from the placed instances (an override, the
    /// prefabs untouched). A street area's hundreds of tile colliders become
    /// about one box per row.
    ///
    /// The caller lays its tiles row by row: BeginRow() with the row's
    /// direction, Add() for each tile placed along it, Break() where the row
    /// is broken (a tile skipped or refused). The boxes go under a
    /// "Colliders" child of the group the tiles are in, on the tiles'
    /// collider layer with their static flags, carrying a copy of the tiles'
    /// SurfaceTag if they have one.
    /// </summary>
    internal class FloorColliders
    {
        // Collider tops this close are at the same height, and a tile
        // starting this far past the end of the run before it still joins
        // it: only rounding, since the tiles of a row overlap.
        private const float SameHeight = 0.001f;

        private readonly Transform _group;
        private Transform _parent;
        private Vector2 _axisX;
        private Vector2 _axisZ;
        // The run being gathered: its extent along and across the row, its
        // bottom and top, and what its box copies from its tiles.
        private bool _open;
        private float _alongMin;
        private float _alongMax;
        private float _acrossMin;
        private float _acrossMax;
        private float _bottom;
        private float _top;
        private SurfaceTag _tag;
        private GameObject _source;

        // How many boxes have been made, and how many tile colliders removed.
        internal int Boxes { get; private set; }
        internal int Removed { get; private set; }

        /// <summary>
        /// Boxes for the tiles laid under a group.
        /// </summary>
        internal FloorColliders(Transform group)
        {
            _group = group;
        }

        /// <summary>
        /// Starts a new row running along a direction on the ground (x, z),
        /// ending the run before it.
        /// </summary>
        internal void BeginRow(Vector2 direction)
        {
            Break();
            _axisX = direction.normalized;
            _axisZ = new Vector2(-_axisX.y, _axisX.x);
        }

        /// <summary>
        /// Adds the next tile along the row: it joins the run if it meets it
        /// at the same height and surface, else the run is boxed and a new
        /// one starts with it. Its colliders are then removed. A null tile
        /// (not placed) breaks the row.
        /// </summary>
        internal void Add(GameObject tile)
        {
            if (tile == null) {
                Break();
                return;
            }

            Collider[] colliders = tile.GetComponentsInChildren<Collider>(true);
            bool found = false;
            float alongMin = float.MaxValue, alongMax = float.MinValue;
            float acrossMin = float.MaxValue, acrossMax = float.MinValue;
            float bottom = float.MaxValue, top = float.MinValue;
            Collider first = null;

            foreach (Collider collider in colliders) {
                if (!collider.enabled || collider.isTrigger) {
                    continue;
                }

                if (!Corners(collider, out Vector3[] corners)) {
                    continue;
                }

                foreach (Vector3 corner in corners) {
                    Vector2 ground = new(corner.x, corner.z);
                    float along = Vector2.Dot(ground, _axisX);
                    float across = Vector2.Dot(ground, _axisZ);
                    alongMin = Mathf.Min(alongMin, along);
                    alongMax = Mathf.Max(alongMax, along);
                    acrossMin = Mathf.Min(acrossMin, across);
                    acrossMax = Mathf.Max(acrossMax, across);
                    bottom = Mathf.Min(bottom, corner.y);
                    top = Mathf.Max(top, corner.y);
                }

                first ??= collider;
                found = true;
            }

            if (!found) {
                Break();
                return;
            }

            SurfaceTag tag = SurfaceTag.Find(first);
            bool joins = _open && Mathf.Abs(top - _top) <= SameHeight && alongMin <= _alongMax + SameHeight && SameSurface(tag, _tag);

            if (!joins) {
                Break();
                _open = true;
                _alongMin = alongMin;
                _alongMax = alongMax;
                _acrossMin = acrossMin;
                _acrossMax = acrossMax;
                _bottom = bottom;
                _top = top;
                _tag = tag;
                _source = first.gameObject;
            } else {
                _alongMin = Mathf.Min(_alongMin, alongMin);
                _alongMax = Mathf.Max(_alongMax, alongMax);
                _acrossMin = Mathf.Min(_acrossMin, acrossMin);
                _acrossMax = Mathf.Max(_acrossMax, acrossMax);
                _bottom = Mathf.Min(_bottom, bottom);
            }

            foreach (Collider collider in colliders) {
                Object.DestroyImmediate(collider);
                Removed++;
            }
        }

        /// <summary>
        /// Ends the run being gathered, if any, making its box.
        /// </summary>
        internal void Break()
        {
            if (!_open) {
                return;
            }

            _open = false;

            if (_parent == null) {
                _parent = new GameObject("Colliders").transform;
                _parent.SetParent(_group, false);
            }

            // The run's middle, back in the world, and its box turned to the
            // row: local X along it, Z across.
            float along = (_alongMin + _alongMax) * 0.5f;
            float across = (_acrossMin + _acrossMax) * 0.5f;
            Vector2 middle = _axisX * along + _axisZ * across;
            GameObject box = new($"Floor {Boxes}") {
                layer = _source.layer
            };
            GameObjectUtility.SetStaticEditorFlags(box, GameObjectUtility.GetStaticEditorFlags(_source));
            box.transform.SetParent(_parent, false);
            box.transform.SetPositionAndRotation(new Vector3(middle.x, (_bottom + _top) * 0.5f, middle.y),
                Quaternion.LookRotation(new Vector3(_axisZ.x, 0f, _axisZ.y), Vector3.up));
            box.AddComponent<BoxCollider>().size = new Vector3(_alongMax - _alongMin, _top - _bottom, _acrossMax - _acrossMin);

            // What it is made of: the tiles' tag copied, else Stone (what an
            // untagged floor counts as anyway) made explicit.
            SurfaceTag tag = box.AddComponent<SurfaceTag>();

            if (_tag != null) {
                EditorUtility.CopySerialized(_tag, tag);
            } else {
                SerializedObject serialized = new(tag);
                serialized.FindProperty("surface").enumValueIndex = (int)SurfaceType.Stone;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            Boxes++;
        }

        /// <summary>
        /// A collider's eight corners in the world: a box's own, or the box
        /// round a mesh collider's mesh (a flat tile's, near enough). False
        /// for any other kind.
        /// </summary>
        private static bool Corners(Collider collider, out Vector3[] corners)
        {
            Vector3 centre;
            Vector3 half;

            switch (collider) {
                case BoxCollider boxCollider:
                    centre = boxCollider.center;
                    half = boxCollider.size * 0.5f;
                    break;
                case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                    centre = meshCollider.sharedMesh.bounds.center;
                    half = meshCollider.sharedMesh.bounds.extents;
                    break;
                default:
                    corners = null;
                    return false;
            }

            corners = new Vector3[8];

            for (int i = 0; i < 8; i++) {
                Vector3 sign = new((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                corners[i] = collider.transform.TransformPoint(centre + Vector3.Scale(half, sign));
            }

            return true;
        }

        /// <summary>
        /// Whether two tiles' tags make the same surface (no tag on either is
        /// the same).
        /// </summary>
        private static bool SameSurface(SurfaceTag a, SurfaceTag b)
        {
            if (a == null || b == null) {
                return a == b;
            }

            return a.Surface == b.Surface && a.SupportsRopeBolt == b.SupportsRopeBolt;
        }
    }
}
