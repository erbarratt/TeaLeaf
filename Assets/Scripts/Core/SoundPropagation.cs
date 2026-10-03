using UnityEngine;

namespace Core
{
    /// <summary>
    /// Works out how sound gets from one place to another through the
    /// level's SoundRooms and SoundPortals: in the same room it goes
    /// straight; between rooms it must go through portals, by the shortest
    /// route, and arrives from the last portal on that route. With no route
    /// (or one longer than the sound carries) it isn't heard at all. Used
    /// for what guards hear (NoiseSystem) and, later, what the player hears.
    ///
    /// The expensive part is done ahead of time: a table of the shortest
    /// route between every pair of portals, rebuilt only when a room or
    /// portal is added, removed, opened or closed. Asking for a path then
    /// only tries each portal of the source's room against each portal of
    /// the listener's room - a few additions, no physics, no allocation.
    /// </summary>
    public static class SoundPropagation
    {
        // Set whenever the rooms or portals change, or a portal opens or
        // closes; the tables are rebuilt the next time a path is asked for,
        // so several changes in one frame (a scene loading) cost one
        // rebuild.
        private static bool _dirty = true;

        // Set only when rooms or portals are added or removed. Then the
        // rebuild also has every portal look up its two rooms again; a door
        // opening or closing doesn't move anything, so it skips that.
        private static bool _layoutDirty = true;

        // How many portals the tables were built for.
        private static int _count;

        // Three tables, each one entry per pair of portals, stored as flat
        // arrays (row * _count + column). For the shortest route from portal
        // "row" to portal "column", measured between their centres and not
        // counting the two end portals themselves:
        // its length in metres (infinity = no route),
        private static float[] _distance;

        // the muffle picked up from the portals along it,
        private static float[] _muffle;

        // and the portal just before "column" on it (to know which way the
        // sound arrives at the last portal).
        private static int[] _previous;

        /// <summary>
        /// Call when a portal is opened or closed: the routes change, but
        /// which rooms each portal joins doesn't.
        /// </summary>
        public static void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>
        /// Call when a room or portal is enabled or disabled: every portal
        /// looks up its rooms again before the routes are rebuilt. Rooms
        /// and portals aren't expected to move while playing - one that
        /// does must call this too.
        /// </summary>
        public static void MarkLayoutDirty()
        {
            _dirty = true;
            _layoutDirty = true;
        }

        /// <summary>
        /// Finds the path sound takes from one world-space point to another.
        /// False if there is no route or it's longer than maxDistance.
        /// </summary>
        public static bool TryGetPath(Vector3 from, Vector3 to, float maxDistance, out SoundPath path)
        {
            return TryGetPath(from, SoundRoom.Find(from), to, SoundRoom.Find(to), maxDistance, out path);
        }

        /// <summary>
        /// The same, for a caller that already knows which room each point
        /// is in (null = outside) - NoiseSystem looks the source's room up
        /// once for all its listeners.
        /// </summary>
        public static bool TryGetPath(Vector3 from, SoundRoom fromRoom, Vector3 to, SoundRoom toRoom, float maxDistance, out SoundPath path)
        {
            if (_dirty) {
                Rebuild();
            }

            // Same room (or both outside): a straight line.
            if (fromRoom == toRoom) {
                float direct = Vector3.Distance(from, to);
                path = new SoundPath(direct, 0f, from, true);
                return direct <= maxDistance;
            }

            // Different rooms: the sound leaves the source's room through
            // one of its portals (first) and reaches the listener's room
            // through one of its portals (last). Try every pair and keep the
            // shortest. Starting the best at maxDistance means anything
            // longer is never taken.
            float best = maxDistance;
            float bestMuffle = 0f;
            int bestFirst = -1;
            int bestLast = -1;

            for (int first = 0; first < _count; first++) {
                SoundPortal firstPortal = SoundPortal.Get(first);

                if (!firstPortal.Borders(fromRoom)) {
                    continue;
                }

                // One portal joining the two rooms directly: measure through
                // the exact point the sound crosses it, so a source seen
                // through an open doorway is no further than it looks.
                if (firstPortal.Borders(toRoom)) {
                    Vector3 crossing = firstPortal.CrossingPoint(from, to, out bool isStraightThrough);
                    float length = Vector3.Distance(from, crossing) + firstPortal.Penalty + Vector3.Distance(crossing, to);

                    if (length <= best) {
                        best = length;

                        // A source in plain view through an open doorway
                        // isn't muffled at all: nothing is in the way.
                        bestMuffle = firstPortal.IsOpen && isStraightThrough ? 0f : firstPortal.Muffle;
                        bestFirst = first;
                        bestLast = first;
                    }
                }

                float toFirst = Vector3.Distance(from, firstPortal.Centre) + firstPortal.Penalty;

                if (toFirst >= best) {
                    continue;
                }

                for (int last = 0; last < _count; last++) {
                    if (last == first) {
                        continue;
                    }

                    float between = _distance[first * _count + last];

                    // Infinity = no route between these two portals. Checked
                    // before Borders() because it's the cheaper test.
                    if (toFirst + between >= best) {
                        continue;
                    }

                    SoundPortal lastPortal = SoundPortal.Get(last);

                    if (!lastPortal.Borders(toRoom)) {
                        continue;
                    }

                    float length = toFirst + between + lastPortal.Penalty + Vector3.Distance(lastPortal.Centre, to);

                    if (length <= best) {
                        best = length;
                        bestMuffle = firstPortal.Muffle + _muffle[first * _count + last] + lastPortal.Muffle;
                        bestFirst = first;
                        bestLast = last;
                    }
                }
            }

            if (bestFirst < 0) {
                path = default;
                return false;
            }

            // Where the listener hears it from: the point of the last portal
            // the sound comes through, arriving from the source itself if
            // that was the only portal, otherwise from the portal before it.
            Vector3 cameFrom = bestFirst == bestLast
                ? from
                : SoundPortal.Get(_previous[bestFirst * _count + bestLast]).Centre;

            Vector3 heardFrom = SoundPortal.Get(bestLast).CrossingPoint(cameFrom, to);
            path = new SoundPath(best, Mathf.Clamp01(bestMuffle), heardFrom, false);
            return true;
        }

        /// <summary>
        /// Rebuilds the portal-to-portal tables.
        /// </summary>
        private static void Rebuild()
        {
            _dirty = false;
            _count = SoundPortal.Count;
            int cells = _count * _count;

            // Only allocates when there are more portals than ever before -
            // in practice once, as the level loads.
            if (_distance == null || _distance.Length < cells) {
                _distance = new float[cells];
                _muffle = new float[cells];
                _previous = new int[cells];
            }

            if (_layoutDirty) {
                _layoutDirty = false;

                for (int i = 0; i < _count; i++) {
                    SoundPortal.Get(i).FindRooms();
                }
            }

            // Start with only the routes that need no portal in between:
            // two portals of the same room are joined by the straight line
            // between their centres.
            for (int i = 0; i < _count; i++) {
                SoundPortal a = SoundPortal.Get(i);

                for (int j = 0; j < _count; j++) {
                    SoundPortal b = SoundPortal.Get(j);
                    int cell = i * _count + j;
                    _muffle[cell] = 0f;
                    _previous[cell] = i;

                    if (i == j) {
                        _distance[cell] = 0f;
                    } else if (a.Borders(b.FrontRoom) || a.Borders(b.BackRoom)) {
                        _distance[cell] = Vector3.Distance(a.Centre, b.Centre);
                    } else {
                        _distance[cell] = float.PositiveInfinity;
                    }
                }
            }

            // Then let every route try going by way of each other portal in
            // turn, keeping it whenever that's shorter (the Floyd-Warshall
            // algorithm). Going through a portal adds its penalty and
            // muffle. Three nested loops over the portals: thousands of
            // steps for a level, and only when something changed.
            for (int via = 0; via < _count; via++) {
                SoundPortal viaPortal = SoundPortal.Get(via);
                float penalty = viaPortal.Penalty;
                float muffle = viaPortal.Muffle;

                for (int i = 0; i < _count; i++) {
                    float toVia = _distance[i * _count + via];

                    if (float.IsPositiveInfinity(toVia)) {
                        continue;
                    }

                    for (int j = 0; j < _count; j++) {
                        float length = toVia + penalty + _distance[via * _count + j];
                        int cell = i * _count + j;

                        if (length < _distance[cell]) {
                            _distance[cell] = length;
                            _muffle[cell] = _muffle[i * _count + via] + muffle + _muffle[via * _count + j];
                            _previous[cell] = _previous[via * _count + j];
                        }
                    }
                }
            }
        }
    }
}
