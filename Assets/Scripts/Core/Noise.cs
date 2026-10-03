using UnityEngine;

namespace Core
{
    /// <summary>
    /// One noise event: where it happened, how far it carries, what made it
    /// and who made it. This is the gameplay side of a sound - what guards
    /// can hear - not the audio the player hears.
    ///
    /// A struct, not a class, so emitting a noise creates nothing for the
    /// garbage collector to clean up: it lives on the stack and is passed to
    /// listeners by reference ("in").
    /// </summary>
    public readonly struct Noise
    {
        // Where the noise was made, in world space.
        public readonly Vector3 Position;

        // How far it carries, in metres. A listener further away than this
        // doesn't hear it.
        public readonly float Radius;

        // What kind of noise it is.
        public readonly NoiseType Type;

        // Who made it (the player's root, a guard, a thrown object), so a
        // listener can ignore its own noises. May be null.
        public readonly Transform Source;

        public Noise(Vector3 position, float radius, NoiseType type, Transform source)
        {
            Position = position;
            Radius = radius;
            Type = type;
            Source = source;
        }
    }
}
