using System;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// How one space echoes: the reverb every sound is heard with while the
    /// player stands in it. Each SoundRoom has one, and the SoundPlayer has
    /// one for outside. Four numbers a level builder can reason about
    /// (how much, how long, how dull, how big) rather than the dozen
    /// technical ones Unity's reverb takes - ApplyTo() turns them into
    /// those.
    ///
    /// A struct, so blending between two rooms' settings allocates nothing.
    /// </summary>
    [Serializable]
    public struct ReverbSettings
    {
        // How much reverb there is, as a volume: 0 = none (a dead space),
        // 1 = a normal amount, up to 2 for a space that rings.
        [SerializeField, Range(0f, 2f)] private float level;

        // How long the tail takes to die away, in seconds: about 0.4 for a
        // small furnished room, 1.5 for a stone hall, 4 or more for a big
        // warehouse or a cave.
        [SerializeField, Range(0.1f, 20f)] private float decayTime;

        // How quickly the high end of the tail dies: 0 = bright and ringing
        // (bare stone, tile, metal), 1 = dull (carpet, hangings, timber
        // and straw).
        [SerializeField, Range(0f, 1f)] private float damping;

        // Roughly how far away the walls are, in metres. It sets the gap
        // before the echo starts, which is how the ear judges a space's
        // size: a bigger number is a bigger-sounding room.
        [SerializeField, Range(0f, 50f)] private float size;

        // Millibels (hundredths of a decibel) are what Unity's reverb is
        // set in. -10000 is its "silent".
        private const float SilentMillibels = -10000f;

        // The speed of sound in metres per second, to turn a distance into
        // a delay.
        private const float SpeedOfSound = 343f;

        public float Level => level;
        public float DecayTime => decayTime;
        public float Damping => damping;
        public float Size => size;

        public ReverbSettings(float level, float decayTime, float damping, float size)
        {
            this.level = level;
            this.decayTime = decayTime;
            this.damping = damping;
            this.size = size;
        }

        // Starting points for the kinds of space a level has. Tune from
        // these on the room itself.
        public static ReverbSettings None => new(0f, 0.3f, 0.5f, 2f);
        public static ReverbSettings SmallRoom => new(0.7f, 0.5f, 0.6f, 2f);
        public static ReverbSettings Room => new(0.9f, 0.9f, 0.4f, 4f);
        public static ReverbSettings StoneRoom => new(1f, 1.4f, 0.15f, 4f);
        public static ReverbSettings StoneHall => new(1.1f, 2.6f, 0.1f, 10f);
        public static ReverbSettings Cellar => new(1.1f, 1.8f, 0.25f, 3f);
        public static ReverbSettings Warehouse => new(1f, 3.5f, 0.35f, 15f);
        public static ReverbSettings Alley => new(0.8f, 1.1f, 0.3f, 5f);
        public static ReverbSettings Outdoors => new(0.35f, 1.2f, 0.6f, 20f);

        /// <summary>
        /// The settings part-way from a to b (t from 0 to 1), for fading
        /// from one room's reverb to the next as the player walks through
        /// a doorway.
        /// </summary>
        public static ReverbSettings Lerp(in ReverbSettings a, in ReverbSettings b, float t)
        {
            return new ReverbSettings(
                Mathf.Lerp(a.level, b.level, t),
                Mathf.Lerp(a.decayTime, b.decayTime, t),
                Mathf.Lerp(a.damping, b.damping, t),
                Mathf.Lerp(a.size, b.size, t));
        }

        /// <summary>
        /// Whether two settings are the same, number for number - how the
        /// SoundPlayer notices a different room, or a room being tuned in
        /// the Inspector while playing.
        /// </summary>
        public bool Matches(in ReverbSettings other)
        {
            return level == other.level
                && decayTime == other.decayTime
                && damping == other.damping
                && size == other.size;
        }

        /// <summary>
        /// Sets a Unity reverb filter to these settings, with the amount of
        /// reverb multiplied by amount (1 = as set). The filter must be on
        /// its User preset (any other preset ignores what is set here).
        /// </summary>
        public void ApplyTo(AudioReverbFilter filter, float amount = 1f)
        {
            // The sound itself passes through untouched; the reverb is
            // added on top.
            filter.dryLevel = 0f;

            // The whole effect's volume. Unity's own presets sit at -1000,
            // so a level of 1 is as loud as those.
            filter.room = Mathf.Clamp(Millibels(level * amount) - 1000f, SilentMillibels, 0f);

            // Damping takes the high end off twice over: the reverb as a
            // whole is duller, and its highs die away sooner than its lows.
            filter.roomHF = Mathf.Lerp(-100f, -6000f, damping);
            filter.decayHFRatio = Mathf.Lerp(1f, 0.1f, damping);
            filter.roomLF = 0f;

            filter.decayTime = Mathf.Clamp(decayTime, 0.1f, 20f);

            // The first echoes arrive after the sound has gone to a wall
            // and back; the tail builds a little after that. Unity caps
            // the two delays at 0.3 and 0.1 seconds.
            float wallAndBack = 2f * size / SpeedOfSound;
            filter.reflectionsDelay = Mathf.Clamp(wallAndBack, 0f, 0.3f);
            filter.reverbDelay = Mathf.Clamp(wallAndBack * 0.5f, 0f, 0.1f);

            // The first echoes are quieter in a big space (they've
            // travelled further), the tail about the same.
            filter.reflectionsLevel = Mathf.Lerp(-1200f, -2600f, Mathf.InverseLerp(2f, 20f, size));
            filter.reverbLevel = 200f;

            // As smooth and as dense as the effect goes: a lower setting
            // sounds grainy and metallic.
            filter.diffusion = 100f;
            filter.density = 100f;
        }

        /// <summary>
        /// A volume multiplier as millibels: 1 = 0, a half is about -600,
        /// nothing is "silent".
        /// </summary>
        private static float Millibels(float gain)
        {
            return gain > 0.0001f ? 2000f * Mathf.Log10(gain) : SilentMillibels;
        }
    }
}
