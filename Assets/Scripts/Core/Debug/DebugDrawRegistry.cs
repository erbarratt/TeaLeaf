using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// Static list of every enabled IDebugDrawable, so InHeadsetGizmos can
    /// draw them all without searching the scene. Drawables add themselves
    /// in OnEnable and remove themselves in OnDisable (the project's
    /// self-registering registry pattern, like HandTargetRegistry).
    ///
    /// Registering costs a list add once per object - nothing per frame, and
    /// nothing is drawn unless InHeadsetGizmos is enabled.
    /// </summary>
    public static class DebugDrawRegistry
    {
        private static readonly List<IDebugDrawable> _drawables = new();

        /// How many drawables are registered.
        public static int Count => _drawables.Count;

        /// <summary>
        /// The drawable at index - read with Count in a for loop (no
        /// enumerator, so no allocation).
        /// </summary>
        public static IDebugDrawable Get(int index)
        {
            return _drawables[index];
        }

        /// <summary>
        /// Adds a drawable. Call from OnEnable.
        /// </summary>
        public static void Register(IDebugDrawable drawable)
        {
            if (!_drawables.Contains(drawable)) {
                _drawables.Add(drawable);
            }
        }

        /// <summary>
        /// Removes a drawable. Call from OnDisable.
        /// </summary>
        public static void Unregister(IDebugDrawable drawable)
        {
            _drawables.Remove(drawable);
        }
    }
}
