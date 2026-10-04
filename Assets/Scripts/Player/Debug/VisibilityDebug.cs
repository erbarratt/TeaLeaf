using Core;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Debug-only stand-in for the wrist visibility gem: a small gauge that
    /// floats in front of the head, a little below the centre of the view.
    /// The outline is the full range; the bar inside grows from left to
    /// right with PlayerVisibility.Visibility and goes from dim blue
    /// (hidden) to yellow (in plain sight). A short tick under the bar
    /// marks the raw light level, before stance and easing.
    ///
    /// Drawn through DebugLines so it shows in the headset (InHeadsetGizmos
    /// must be on) as well as the Scene view. Also logs to the Console each
    /// time the visibility crosses into a different tenth.
    /// </summary>
    public class VisibilityDebug : MonoBehaviour, IDebugDrawable
    {
        [SerializeField] private PlayerVisibility playerVisibility;

        // The Main Camera: the gauge is placed relative to it.
        [SerializeField] private Transform head;

        // Where the gauge sits in the head's own space, and how big it is,
        // in metres.
        [SerializeField] private Vector3 offset = new(0f, -0.12f, 0.6f);
        [SerializeField] private float width = 0.16f;
        [SerializeField] private float height = 0.015f;

        [SerializeField] private bool logChanges = true;

        private static readonly Color _frameColor = new(1f, 1f, 1f, 0.5f);
        private static readonly Color _hiddenColor = new(0.3f, 0.4f, 1f, 1f);
        private static readonly Color _seenColor = new(1f, 0.9f, 0.2f, 1f);

        // The tenth (0-10) last logged.
        private int _lastLoggedTenth = -1;

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
            if (!logChanges) {
                return;
            }

            int tenth = Mathf.RoundToInt(playerVisibility.Visibility * 10f);

            if (tenth == _lastLoggedTenth) {
                return;
            }

            _lastLoggedTenth = tenth;
            Debug.Log($"[Visibility] {playerVisibility.Visibility:F1} (light {playerVisibility.LightLevel:F1})");
        }

        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// The gauge, in the head's local space so it stays in view.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (playerVisibility == null || head == null) {
                return;
            }

            lines.Matrix = head.localToWorldMatrix;

            float left = offset.x - width * 0.5f;
            float visibility = playerVisibility.Visibility;

            lines.Color = _frameColor;
            lines.WireCube(offset, new Vector3(width, height, 0f));

            // The bar: a few lines side by side, so it reads as solid.
            lines.Color = Color.Lerp(_hiddenColor, _seenColor, visibility);

            const int rows = 4;

            for (int i = 0; i < rows; i++) {
                float y = offset.y - height * 0.5f + height * (i + 0.5f) / rows;
                lines.Line(new Vector3(left, y, offset.z), new Vector3(left + width * visibility, y, offset.z));
            }

            // The raw light level, as a tick below the frame.
            float lightX = left + width * playerVisibility.LightLevel;
            float bottom = offset.y - height * 0.5f;
            lines.Color = _frameColor;
            lines.Line(new Vector3(lightX, bottom, offset.z), new Vector3(lightX, bottom - height, offset.z));
        }
    }
}
