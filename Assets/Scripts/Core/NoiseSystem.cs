using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// The shared noise event system. Anything that makes a noise calls
    /// Emit(); every registered listener close enough to hear it is told.
    /// The emitter doesn't know who is listening and listeners don't know
    /// who can make noise - they only share this class.
    ///
    /// Noise travels the way all sound does here (SoundPropagation):
    /// straight within a room, through portals between rooms, never through
    /// walls. A listener hears it only if the route to it is within the
    /// noise's radius.
    ///
    /// Static, with a self-registering listener list: nothing searches the
    /// scene, and nothing runs per frame. A noise costs one distance check
    /// per listener, plus a path lookup for those in range, only at the
    /// moment it's made.
    /// </summary>
    public static class NoiseSystem
    {
        // Every listener currently able to hear. Listeners add and remove
        // themselves (OnEnable/OnDisable), so a scene reload empties the
        // list by itself as the old scene's objects are disabled.
        private static readonly List<INoiseListener> _listeners = new();

        /// <summary>
        /// Raised for every noise, whether or not anyone heard it. For debug
        /// drawing and UI (a sound direction indicator), not for guards -
        /// they are listeners. Subscribers must unsubscribe in OnDisable:
        /// the event is static, so it outlives a scene reload.
        /// </summary>
        public static event Action<Noise> Emitted;

        /// <summary>
        /// Adds a listener. Call from OnEnable.
        /// </summary>
        public static void Register(INoiseListener listener)
        {
            if (!_listeners.Contains(listener)) {
                _listeners.Add(listener);
            }
        }

        /// <summary>
        /// Removes a listener. Call from OnDisable.
        /// </summary>
        public static void Unregister(INoiseListener listener)
        {
            _listeners.Remove(listener);
        }

        /// <summary>
        /// Makes a noise at position that carries radius metres. source is
        /// who made it (may be null).
        /// </summary>
        public static void Emit(Vector3 position, float radius, NoiseType type, Transform source)
        {
            if (radius <= 0f) {
                return;
            }

            Noise noise = new(position, radius, type, source);
            float sqrRadius = radius * radius;

            // The noise's room is the same for every listener, so it's
            // looked up once.
            SoundRoom room = SoundRoom.Find(position);

            // Backwards, so a listener that unregisters while reacting (a
            // guard disabled by what it heard) doesn't make the loop skip
            // the next one.
            for (int i = _listeners.Count - 1; i >= 0; i--) {
                INoiseListener listener = _listeners[i];
                Vector3 ear = listener.EarPosition;

                // The cheap test first: the route through the portals is
                // never shorter than the straight line, so a listener
                // further than the radius in a straight line can't hear it.
                // Squared distances compare the same way as real ones but
                // skip the square root.
                if ((ear - position).sqrMagnitude > sqrRadius) {
                    continue;
                }

                // Then the real route: straight in the same room, through
                // the portals between rooms, nothing if there's no way
                // through within the radius.
                if (!SoundPropagation.TryGetPath(position, room, ear, SoundRoom.Find(ear), radius, out SoundPath path)) {
                    continue;
                }

                float loudness = 1f - path.Distance / radius;
                listener.OnNoiseHeard(in noise, loudness, path.HeardFrom);
            }

            Emitted?.Invoke(noise);
        }
    }
}
