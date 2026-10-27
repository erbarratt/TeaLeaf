using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGen
{
    /// <summary>
    /// Black boxes behind the facades (worked out by FacadeBuilder,
    /// CollectBackings: one per stretch of a column, from its eaves down,
    /// behind its walls). The buildings are facades with nothing behind them:
    /// a gap anywhere in the walls would show the void, and a light behind a
    /// facade (a lamp in the next street) would reach its pieces from behind.
    /// A plain black, shadow-casting box behind every column closes both.
    ///
    /// Run once every piece stands (after the gap fillers and the visibility
    /// cull): each box is cut back until it touches no placed piece - its
    /// front back, an end in, its top down or its bottom up, whichever keeps
    /// the most of it - so it never pokes through a wall, a side wall
    /// running back, a foundation or a roof. Then placed under the Generated
    /// root's Backing group as Unity's cube scaled to it: no collider (nobody
    /// reaches it, and it changes no physics query), static for the lighting
    /// bake and occlusion culling, lit from probes (it is black whatever lights
    /// it), never batching static (a probe-lit renderer is never batched).
    /// </summary>
    internal static class Backing
    {
        internal const string GroupName = "Backing";
        // How often the boxes are cut against every piece before giving up:
        // a cut can bring a box clear of one piece only to leave it in
        // another it was already clear of, never more than a few times.
        private const int CutPasses = 8;
        // Overlaps shallower than this (metres) count as none.
        private const float Touching = 0.001f;

        /// <summary>
        /// One box: a level box from Corner (its bottom, on the walls' back
        /// line, at its start) Length along, Depth back, Height up.
        /// </summary>
        internal struct Box
        {
            public string Name;
            public Vector3 Corner;
            public Vector3 Along;
            public Vector3 Back;
            public float Length;
            public float Depth;
            public float Height;
        }

        /// <summary>
        /// A placed piece's render box: its root and the box's corners in
        /// the root's own space, with its world bounds to pass over it fast.
        /// </summary>
        private struct Piece
        {
            public Transform Root;
            public Vector3 Min;
            public Vector3 Max;
            public Bounds World;
        }

        /// <summary>
        /// Cuts the last build's boxes clear of every piece under the
        /// Generated root and places them. Adds one line to log.
        /// </summary>
        internal static void Build(Transform root, List<Box> boxes, GeneratorSettings settings, List<string> log)
        {
            Material material = settings.BackingMaterial;

            if (material == null) {
                log.Add("Backing: none (no backing material in the settings).");
                return;
            }

            List<Piece> pieces = Pieces(root);
            Transform group = new GameObject(GroupName).transform;
            group.SetParent(root, false);
            GameObjectUtility.SetStaticEditorFlags(group.gameObject, StaticFlags);
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            int layer = LayerMask.NameToLayer("Environment");
            int placed = 0;
            int cut = 0;
            int lost = 0;

            foreach (Box box in boxes) {
                Box clear = box;

                if (!CutClear(ref clear, pieces, settings.BackingClearance)) {
                    lost++;
                    continue;
                }

                if (clear.Length < box.Length - 0.001f || clear.Depth < box.Depth - 0.001f || clear.Height < box.Height - 0.001f) {
                    cut++;
                }

                GameObject placedBox = new($"Backing {placed}", typeof(MeshFilter), typeof(MeshRenderer));
                Transform t = placedBox.transform;
                t.SetParent(group, false);
                t.SetPositionAndRotation(clear.Corner + clear.Along * (clear.Length * 0.5f) + clear.Back * (clear.Depth * 0.5f) + Vector3.up * (clear.Height * 0.5f),
                    Quaternion.LookRotation(clear.Back, Vector3.up));
                // The cube's x across the box's length, z along its depth.
                t.localScale = new Vector3(clear.Length, clear.Height, clear.Depth);
                placedBox.layer = layer < 0 ? 0 : layer;
                GameObjectUtility.SetStaticEditorFlags(placedBox, StaticFlags);
                placedBox.GetComponent<MeshFilter>().sharedMesh = cube;
                MeshRenderer renderer = placedBox.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = false;
                renderer.receiveGI = ReceiveGI.LightProbes;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                placed++;
            }

            log.Add($"Backing: {placed} box(es) behind the facades ({cut} cut back clear of pieces, {lost} left out with nothing clear).");
        }

        // Static for the bake (it blocks light) and occlusion culling; not
        // batching static, since it is lit from probes.
        private const StaticEditorFlags StaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        /// <summary>
        /// Every placed piece under the root (the outermost objects named like
        /// a prefab variant, TL_..., but the streets' own), each with its
        /// render box in its own space from its LOD0 renderers (all of its
        /// renderers where it has no LODs).
        /// </summary>
        private static List<Piece> Pieces(Transform root)
        {
            List<Piece> pieces = new();
            List<MeshRenderer> renderers = new();

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) {
                if (!t.name.StartsWith("TL_") || InsidePiece(t, root) || UnderStreets(t, root)) {
                    continue;
                }

                renderers.Clear();
                t.GetComponentsInChildren(true, renderers);
                bool lods = renderers.Exists(r => r.name.EndsWith("_LOD0"));
                Vector3 min = Vector3.positiveInfinity;
                Vector3 max = Vector3.negativeInfinity;
                Bounds world = default;
                bool any = false;

                foreach (MeshRenderer renderer in renderers) {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();

                    if ((lods && !renderer.name.EndsWith("_LOD0")) || filter == null || filter.sharedMesh == null) {
                        continue;
                    }

                    Bounds mesh = filter.sharedMesh.bounds;

                    for (int c = 0; c < 8; c++) {
                        Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                        Vector3 inWorld = renderer.transform.TransformPoint(corner);
                        Vector3 local = t.InverseTransformPoint(inWorld);
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);

                        if (!any) {
                            world = new Bounds(inWorld, Vector3.zero);
                            any = true;
                        } else {
                            world.Encapsulate(inWorld);
                        }
                    }
                }

                if (any) {
                    pieces.Add(new Piece { Root = t, Min = min, Max = max, World = world });
                }
            }

            return pieces;
        }

        /// <summary>
        /// Whether a piece's object sits inside another piece (a part of it).
        /// </summary>
        private static bool InsidePiece(Transform t, Transform root)
        {
            for (Transform parent = t.parent; parent != null && parent != root; parent = parent.parent) {
                if (parent.name.StartsWith("TL_")) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether an object is under the root's Streets group (the ground,
        /// steps and decking: below and in front of every box).
        /// </summary>
        private static bool UnderStreets(Transform t, Transform root)
        {
            for (Transform parent = t.parent; parent != null && parent != root; parent = parent.parent) {
                if (parent.parent == root && parent.name == "Streets") {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Cuts a box until it stands clearance clear of every piece: for
        /// each piece it touches (its render box's reach in the box's frame,
        /// grown by clearance), whichever single cut keeps the most of the box
        /// - front back past the piece, back in front of it, either end past
        /// it, top below it or bottom above it. False if nothing is left.
        /// </summary>
        private static bool CutClear(ref Box box, List<Piece> pieces, float clearance)
        {
            for (int pass = 0; pass < CutPasses; pass++) {
                bool cutAny = false;

                foreach (Piece piece in pieces) {
                    if (!piece.World.Intersects(WorldBounds(box, clearance))) {
                        continue;
                    }

                    // The piece's reach in the box's frame: x along, y up,
                    // z back, from the box's corner.
                    Vector3 low = Vector3.positiveInfinity;
                    Vector3 high = Vector3.negativeInfinity;

                    for (int c = 0; c < 8; c++) {
                        Vector3 local = new((c & 1) == 0 ? piece.Min.x : piece.Max.x, (c & 2) == 0 ? piece.Min.y : piece.Max.y,
                            (c & 4) == 0 ? piece.Min.z : piece.Max.z);
                        Vector3 offset = piece.Root.TransformPoint(local) - box.Corner;
                        Vector3 inBox = new(Vector3.Dot(offset, box.Along), offset.y, Vector3.Dot(offset, box.Back));
                        low = Vector3.Min(low, inBox);
                        high = Vector3.Max(high, inBox);
                    }

                    low -= Vector3.one * clearance;
                    high += Vector3.one * clearance;

                    // A box just cut to a piece's face touches it, to
                    // rounding: not again.
                    if (high.x <= Touching || low.x >= box.Length - Touching || high.y <= Touching || low.y >= box.Height - Touching || high.z <= Touching
                        || low.z >= box.Depth - Touching) {
                        continue;
                    }

                    // Each cut's box size (length, height, depth) and how it
                    // moves the corner; the one keeping the most volume wins.
                    float best = -1f;
                    Vector3 size = Vector3.zero;
                    Vector3 shift = Vector3.zero;
                    Consider(box.Length - high.x, box.Height, box.Depth, new Vector3(high.x, 0f, 0f), ref best, ref size, ref shift);
                    Consider(low.x, box.Height, box.Depth, Vector3.zero, ref best, ref size, ref shift);
                    Consider(box.Length, box.Height - high.y, box.Depth, new Vector3(0f, high.y, 0f), ref best, ref size, ref shift);
                    Consider(box.Length, low.y, box.Depth, Vector3.zero, ref best, ref size, ref shift);
                    Consider(box.Length, box.Height, box.Depth - high.z, new Vector3(0f, 0f, high.z), ref best, ref size, ref shift);
                    Consider(box.Length, box.Height, low.z, Vector3.zero, ref best, ref size, ref shift);

                    if (best <= 0f) {
                        return false;
                    }

                    box.Corner += box.Along * shift.x + Vector3.up * shift.y + box.Back * shift.z;
                    box.Length = size.x;
                    box.Height = size.y;
                    box.Depth = size.z;
                    cutAny = true;
                }

                if (!cutAny) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One possible cut: kept if it leaves more of the box than the best
        /// so far.
        /// </summary>
        private static void Consider(float length, float height, float depth, Vector3 cornerShift, ref float best, ref Vector3 size, ref Vector3 shift)
        {
            if (length <= 0f || height <= 0f || depth <= 0f) {
                return;
            }

            float volume = length * height * depth;

            if (volume > best) {
                best = volume;
                size = new Vector3(length, height, depth);
                shift = cornerShift;
            }
        }

        /// <summary>
        /// A box's world bounds, grown by a margin.
        /// </summary>
        private static Bounds WorldBounds(Box box, float margin)
        {
            Bounds bounds = new(box.Corner, Vector3.zero);
            bounds.Encapsulate(box.Corner + box.Along * box.Length);
            bounds.Encapsulate(box.Corner + box.Back * box.Depth);
            bounds.Encapsulate(box.Corner + box.Along * box.Length + box.Back * box.Depth);
            bounds.Encapsulate(bounds.max + Vector3.up * box.Height);
            bounds.Expand(margin * 2f);
            return bounds;
        }
    }
}
