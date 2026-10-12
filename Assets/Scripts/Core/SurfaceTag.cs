using UnityEngine;

namespace Core
{
    /// <summary>
    /// Marks what a floor is made of. Put it on the object with the
    /// collider, or on any parent: a tag covers everything beneath it in
    /// the hierarchy, so one tag on a "Warehouse Floor" group tags every
    /// piece of it, and a child with its own tag (a rug on that floor)
    /// overrides it. Anything with no tag above it is Stone.
    ///
    /// Holds data only: nothing runs per frame, and it's only looked up at
    /// the moment a foot comes down (see Of()).
    /// </summary>
    public class SurfaceTag : MonoBehaviour
    {
        [SerializeField] private SurfaceType surface = SurfaceType.Wood;

        // Whether a rope bolt stuck in this holds a climber's weight.
        // Only matters on a surface bolts stick into (HoldsBolt()). Off,
        // the bolt still sticks and the rope still hangs, but both come
        // away as soon as the player takes hold of the rope.
        [SerializeField] private bool supportsRopeBolt;

        public SurfaceType Surface => surface;

        public bool SupportsRopeBolt => supportsRopeBolt;

        /// <summary>
        /// Whether a crossbow bolt sticks into a surface of this kind.
        /// It bounces off anything else - including everything untagged,
        /// which is Stone.
        /// </summary>
        public static bool HoldsBolt(SurfaceType surface)
        {
            return surface == SurfaceType.Wood || surface == SurfaceType.Carpet || surface == SurfaceType.Grass || surface == SurfaceType.Plaster;
        }

        /// <summary>
        /// The tag covering a collider: the nearest SurfaceTag on its
        /// object or above it, or null if there is none. Walks up the
        /// hierarchy one parent at a time - a handful of lookups, a couple
        /// of times a second, and TryGetComponent creates nothing when it
        /// finds none.
        /// </summary>
        public static SurfaceTag Find(Collider collider)
        {
            Transform current = collider.transform;

            while (current != null) {
                if (current.TryGetComponent(out SurfaceTag tag)) {
                    return tag;
                }

                current = current.parent;
            }

            return null;
        }

        /// <summary>
        /// The surface of a collider: its tag's (see Find()), or Stone if
        /// it has none.
        /// </summary>
        public static SurfaceType Of(Collider collider)
        {
            SurfaceTag tag = Find(collider);
            return tag != null ? tag.surface : SurfaceType.Stone;
        }

        /// <summary>
        /// The surface of the floor under a point: one ray straight down
        /// from "from", distance metres long, against layers (triggers are
        /// ignored). False if it hits nothing. Shared by anything with
        /// feet, so they all find the floor the same way.
        /// </summary>
        public static bool TryFindBelow(Vector3 from, float distance, LayerMask layers, out SurfaceType surface)
        {
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, distance, layers, QueryTriggerInteraction.Ignore)) {
                surface = Of(hit.collider);
                return true;
            }

            surface = SurfaceType.Stone;
            return false;
        }
    }
}
