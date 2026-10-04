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

        public SurfaceType Surface => surface;

        /// <summary>
        /// The surface of a collider: the nearest SurfaceTag on its object
        /// or above it, or Stone if there is none. Walks up the hierarchy
        /// one parent at a time - a handful of lookups, a couple of times a
        /// second, and TryGetComponent creates nothing when it finds none.
        /// </summary>
        public static SurfaceType Of(Collider collider)
        {
            Transform current = collider.transform;

            while (current != null) {
                if (current.TryGetComponent(out SurfaceTag tag)) {
                    return tag.surface;
                }

                current = current.parent;
            }

            return SurfaceType.Stone;
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
