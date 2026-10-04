using UnityEngine;

namespace Core
{
    /// <summary>
    /// How much light falls on a point in the level, for gameplay: the one
    /// place visibility asks (the player now; guards spotting bodies
    /// later). 0 = full shadow, 1 = fully lit.
    ///
    /// Nothing is placed by hand except the lights themselves. A point is
    /// lit by the moon if nothing stands between it and the moon, and by a
    /// LightSource if it's within range and nothing stands between them.
    /// "Nothing stands between" is one physics ray each, against the
    /// level's solid geometry - so shadows fall where the level's shape
    /// puts them, and opening a door or knocking out a torch changes them
    /// with no extra work. The brightest light wins; they don't add up.
    ///
    /// A ray costs little, but not nothing: callers sample a few times a
    /// second, not every frame (see PlayerVisibility).
    /// </summary>
    public static class SceneLight
    {
        // What blocks light: static world geometry, and props and doors.
        // The layers' names are fixed for the project (see the root
        // CLAUDE.md), so the mask is worked out once, on first use.
        private static int _occluders;

        /// <summary>
        /// The light level at a world-space point.
        /// </summary>
        public static float LevelAt(Vector3 point)
        {
            if (_occluders == 0) {
                _occluders = LayerMask.GetMask("Environment", "Interactable");
            }

            float light = 0f;

            // The moon: lit unless something is in the way towards it.
            Moonlight moon = Moonlight.Instance;

            if (moon != null && !Physics.Raycast(point, moon.DirectionToMoon, moon.MaxDistance, _occluders, QueryTriggerInteraction.Ignore)) {
                light = moon.Level;
            }

            // Each light source. Cheapest tests first: out of range, or no
            // brighter here than what's already been found - only then the
            // ray from the light to the point.
            for (int i = 0; i < LightSource.Count; i++) {
                LightSource source = LightSource.Get(i);
                Vector3 sourcePosition = source.Position;
                float range = source.Range;
                float sqrDistance = (point - sourcePosition).sqrMagnitude;

                if (sqrDistance >= range * range) {
                    continue;
                }

                float level = source.LevelAtDistance(Mathf.Sqrt(sqrDistance));

                if (level <= light) {
                    continue;
                }

                if (!Physics.Linecast(sourcePosition, point, _occluders, QueryTriggerInteraction.Ignore)) {
                    light = level;
                }
            }

            return light;
        }
    }
}
