using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// A sound that goes on and on from one place: a torch's crackle, a
    /// fire, water lapping at a quay, a ticking clock. Put it on the object
    /// that makes the sound and give it a cue; the SoundPlayer does the
    /// rest.
    ///
    /// It travels through rooms and portals like every other sound, but
    /// because it never stops, its route is worked out again a few times a
    /// second as the player moves (by the SoundPlayer, each loop at its own
    /// moment so they never all land on one frame), and where it's heard
    /// from and how muffled it is glide to the new answer.
    ///
    /// Only the loops the player can hear take up a voice. Disabling the
    /// component fades the sound out - a torch put out goes quiet by
    /// itself.
    ///
    /// The cue's noise half is ignored: guards don't react to a torch
    /// burning.
    /// </summary>
    public class SoundLoop : MonoBehaviour
    {
        // What it plays. One of the cue's clips is picked when the loop
        // starts to be heard, and repeats. Use clips made to loop (the end
        // runs into the start with no click).
        [SerializeField] private SoundCue cue;

        // Multiplies the cue's volume for this one loop (a small candle
        // and a big brazier can share a cue).
        [SerializeField, Range(0f, 2f)] private float volumeScale = 1f;

        // Every enabled loop in the scene. Loops add and remove themselves,
        // so nothing ever searches the scene for them.
        private static readonly List<SoundLoop> _loops = new();

        /// How many loops are enabled (heard or not).
        public static int Count => _loops.Count;

        public SoundCue Cue => cue;
        public float VolumeScale => volumeScale;
        public Vector3 Position => transform.position;

        // The rest is the SoundPlayer's bookkeeping for this loop, kept
        // here so it needs no lookup table: when to work the route out
        // next, what the last answer was, and which of its voices (if any)
        // is playing this loop.

        /// When the route is next worked out (unscaled time).
        public float NextCheckTime { get; set; }

        /// Whether the sound reached the player at the last check.
        public bool IsAudible { get; set; }

        /// The route it took at the last check (only meaningful while
        /// IsAudible).
        public SoundPath Path { get; set; }

        /// About how loud it arrives, 0-1, for choosing which loops get a
        /// voice when there are more than voices.
        public float Loudness { get; set; }

        /// The SoundPlayer's loop voice playing it, or -1.
        public int VoiceIndex { get; set; } = -1;

        /// <summary>
        /// The enabled loop at index - read with Count in a for loop.
        /// </summary>
        public static SoundLoop Get(int index)
        {
            return _loops[index];
        }

        private void OnEnable()
        {
            _loops.Add(this);

            // Checked on the SoundPlayer's next frame.
            NextCheckTime = 0f;
        }

        private void OnDisable()
        {
            _loops.Remove(this);

            // The SoundPlayer sees this and fades the voice out.
            IsAudible = false;
        }
    }
}
