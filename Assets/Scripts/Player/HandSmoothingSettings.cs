using System;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// How strongly the hands' tracking is smoothed, for one device target
    /// (PlayerTracking has a set for PCVR and a set for Quest standalone -
    /// the two track differently, and streaming adds shake of its own).
    /// Used by HandPoseFilter, which explains what the numbers do.
    ///
    /// Tuning, one pair at a time (position, then rotation):
    /// 1. Hold the hand still. Lower the minimum cutoff until the shake is
    ///    gone. Too low and slow, careful movements start to trail.
    /// 2. Wave the hand about. Raise the speed coefficient until it no
    ///    longer feels behind. Too high and the shake comes back during
    ///    slow movements.
    ///
    /// A class rather than a struct so PlayerTracking can hand the chosen
    /// set to the filter without copying it every frame.
    /// </summary>
    [Serializable]
    public class HandSmoothingSettings
    {
        [Tooltip("Off, this device's hands follow the tracking exactly.")]
        public bool enabled = true;

        [Header("Position")]

        [Tooltip("How much a still hand's position is smoothed, as a frequency in hertz: shake faster than about this many wobbles a second is removed. Lower is steadier but trails more.")]
        public float positionMinCutoff = 3f;

        [Tooltip("How fast the position smoothing is given up as the hand speeds up: hertz added to the cutoff for every metre a second the hand moves. Higher means less lag in movement.")]
        public float positionSpeedCoefficient = 40f;

        [Header("Rotation")]

        // The same two for the hand's rotation. Rotation shake usually
        // shows most: a small wrist tremor swings whatever is held or
        // aimed a long way.
        [Tooltip("How much a still hand's rotation is smoothed, as a frequency in hertz. Lower is steadier but trails more.")]
        public float rotationMinCutoff = 3f;

        [Tooltip("Hertz added to the rotation cutoff for every radian a second (about 57 degrees a second) the hand turns. Higher means less lag.")]
        public float rotationSpeedCoefficient = 15f;
    }
}
