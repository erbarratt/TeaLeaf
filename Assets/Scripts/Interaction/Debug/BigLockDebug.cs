using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Debug-only view of the lock being picked, drawn on the big lock
    /// itself: where each pick is, where this stage's pin is hidden, the
    /// stop the right pick can turn to, how many pins are set and how long
    /// the left pick has been held on the pin.
    ///
    /// Drawn through DebugLines so it shows in the headset (InHeadsetGizmos
    /// must be on) as well as the Scene view. Also logs to the Console
    /// each time the stage changes. On the Big Lock object.
    /// </summary>
    public class BigLockDebug : MonoBehaviour, IDebugDrawable
    {
        [SerializeField] private BigLock bigLock;

        // How far out from the lock's middle the lines reach, and how far
        // in front of its middle they're drawn (towards the player), in
        // metres - clear of the face, so they aren't buried in it.
        [SerializeField] private float radius = 0.16f;
        [SerializeField] private float standOff = 0.04f;

        [SerializeField] private bool logChanges = true;

        private static readonly Color _pickColor = new(1f, 1f, 1f, 1f);
        private static readonly Color _pinColor = new(1f, 0.3f, 0.3f, 1f);
        private static readonly Color _stopColor = new(1f, 0.85f, 0.2f, 1f);
        private static readonly Color _setColor = new(0.3f, 1f, 0.4f, 1f);
        private static readonly Color _emptyColor = new(1f, 1f, 1f, 0.3f);

        // What was last logged.
        private int _loggedPins = -1;
        private bool _loggedSearching;
        private bool _loggedActive;

        /// <summary>
        /// Editor-only: runs when the component is added.
        /// </summary>
        private void Reset()
        {
            bigLock = GetComponent<BigLock>();
        }

        private void OnEnable()
        {
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            DebugDrawRegistry.Unregister(this);
        }

        private void Update()
        {
            if (!logChanges || bigLock == null) {
                return;
            }

            bool isActive = bigLock.IsActive;
            bool isSearching = bigLock.IsSearching;
            int pins = bigLock.PinsSet;

            if (isActive == _loggedActive && isSearching == _loggedSearching && pins == _loggedPins) {
                return;
            }

            _loggedActive = isActive;
            _loggedSearching = isSearching;
            _loggedPins = pins;

            if (!isActive) {
                Debug.Log("[Lockpick] big lock hidden");
            } else if (isSearching) {
                Debug.Log($"[Lockpick] stage {pins + 1} of {BigLock.PinCount}: searching, pin at {bigLock.PinAngle:F0} degrees (left pick at {bigLock.LeftAngle:F0})");
            } else {
                Debug.Log($"[Lockpick] pins set {pins} of {BigLock.PinCount}, right pick at {bigLock.RightAngle:F0} of {bigLock.CurrentStop:F0} degrees");
            }
        }

        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// The readout, in the big lock's own space (x right, y up, the
        /// player at -z), only while the lock is in view.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (bigLock == null || !Application.isPlaying || !bigLock.IsActive) {
                return;
            }

            lines.Matrix = bigLock.transform.localToWorldMatrix;

            // Each pick's angle, as a line from the middle.
            lines.Color = _pickColor;
            Spoke(lines, bigLock.LeftAngle, 0f, radius * 0.8f);
            Spoke(lines, bigLock.RightAngle, 0f, radius * 0.8f);

            // The stop the right pick can turn to now.
            lines.Color = _stopColor;
            Spoke(lines, bigLock.CurrentStop, radius * 0.7f, radius);

            // This stage's pin - where the left pick has to go.
            if (bigLock.PinsSet < BigLock.PinCount) {
                lines.Color = _pinColor;
                Spoke(lines, bigLock.PinAngle, radius * 0.5f, radius * 1.1f);
            }

            // One small square per pin above the lock, green once set.
            float size = radius * 0.12f;

            for (int i = 0; i < BigLock.PinCount; i++) {
                lines.Color = i < bigLock.PinsSet ? _setColor : _emptyColor;
                Vector3 centre = new((i - (BigLock.PinCount - 1) * 0.5f) * size * 2f, radius * 1.25f, -standOff);
                lines.WireCube(centre, new Vector3(size, size, 0f));
            }

            // The hold timer: a bar under the lock that fills from left to
            // right while the left pick is held on the pin.
            float width = radius * 1.2f;
            float y = -radius * 1.25f;
            float filled = bigLock.PinHoldTime > 0f ? Mathf.Clamp01(bigLock.HoldTimer / bigLock.PinHoldTime) : 0f;

            lines.Color = _emptyColor;
            lines.WireCube(new Vector3(0f, y, -standOff), new Vector3(width, size, 0f));

            if (filled > 0f) {
                lines.Color = _setColor;
                lines.Line(new Vector3(-width * 0.5f, y, -standOff), new Vector3(-width * 0.5f + width * filled, y, -standOff));
            }
        }

        /// <summary>
        /// A line along a clock angle (degrees clockwise from 12 o'clock,
        /// seen by the player), from one distance out from the middle to
        /// another.
        /// </summary>
        private void Spoke(DebugLines lines, float clockAngle, float from, float to)
        {
            float radians = clockAngle * Mathf.Deg2Rad;
            Vector3 direction = new(Mathf.Sin(radians), Mathf.Cos(radians), 0f);
            Vector3 offset = new(0f, 0f, -standOff);

            lines.Line(direction * from + offset, direction * to + offset);
        }
    }
}
