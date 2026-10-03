using UnityEngine;

namespace Core
{
    /// <summary>
    /// The route a sound takes from where it's made to a listener, as
    /// worked out by SoundPropagation. A struct, so asking for one
    /// allocates nothing.
    /// </summary>
    public readonly struct SoundPath
    {
        // How far the sound travelled, in metres: the straight line in the
        // same room, otherwise the length of the route through the portals
        // (plus the extra distance of any closed ones).
        public readonly float Distance;

        // How muffled it arrives, from the portals it passed (a little for
        // each open one, more for each closed one): 0 = clear, 1 = fully
        // muffled.
        public readonly float Muffle;

        // Where the listener hears it coming from, in world space: the
        // sound's own position in the same room, otherwise the point of the
        // last portal it came through.
        public readonly Vector3 HeardFrom;

        // Whether it arrived without passing through any portal.
        public readonly bool IsDirect;

        public SoundPath(float distance, float muffle, Vector3 heardFrom, bool isDirect)
        {
            Distance = distance;
            Muffle = muffle;
            HeardFrom = heardFrom;
            IsDirect = isDirect;
        }
    }
}
