using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core
{
    /// <summary>
    /// Debug-only: on a key press, writes one Console entry describing how
    /// every SoundEmitterDebug in the scene reaches the player's ears right
    /// now - its room, the straight-line distance, the route the sound
    /// takes, how muffled it arrives and whether it's within the cue's
    /// audible range. One entry with no stack trace, so it can be read back
    /// from the editor log. The Game view needs keyboard focus (click it).
    /// Put it on the Debug object.
    /// </summary>
    public class SoundDebug : MonoBehaviour
    {
        [SerializeField] private Key reportKey = Key.L;

        // Reused for each report's text.
        private readonly StringBuilder _report = new();

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null) {
                return;
            }

            if (keyboard[reportKey].wasPressedThisFrame) {
                LogReport();
            }
        }

        /// <summary>
        /// Builds and logs the report. Builds a string, so it allocates -
        /// debug only.
        /// </summary>
        private void LogReport()
        {
            SoundPlayer player = SoundPlayer.Instance;

            if (player == null) {
                Debug.LogWarning("SoundDebug: the scene has no SoundPlayer.", this);
                return;
            }

            Vector3 ears = player.ListenerPosition;
            _report.Clear();
            _report.AppendLine($"SOUND REPORT: listener at {ears:0.00}, room {RoomName(SoundRoom.Find(ears))}, {player.PlayingVoiceCount} voices playing, {SoundPortal.Count} portals");

            for (int i = 0; i < SoundEmitterDebug.Count; i++) {
                SoundEmitterDebug emitter = SoundEmitterDebug.Get(i);
                Vector3 position = emitter.transform.position;
                float straight = Vector3.Distance(position, ears);
                float range = emitter.Cue != null ? emitter.Cue.AudibleRange : 0f;

                _report.Append($"- {emitter.name}: room {RoomName(SoundRoom.Find(position))}, range {range:0.0}m, straight {straight:0.00}m, ");

                // No distance limit, so a route that exists but is too long
                // is still shown.
                if (!SoundPropagation.TryGetPath(position, ears, float.PositiveInfinity, out SoundPath path)) {
                    _report.AppendLine("NO ROUTE -> not played");
                    continue;
                }

                _report.Append($"path {path.Distance:0.00}m ({(path.IsDirect ? "direct" : "via portal")}), muffle {path.Muffle:0.00}, from {path.HeardFrom:0.00} -> ");
                _report.AppendLine(path.Distance <= range ? "PLAYED" : "too far, not played");
            }

            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "{0}", _report.ToString());
        }

        /// <summary>
        /// A room's name for the report, "outside" for no room.
        /// </summary>
        private static string RoomName(SoundRoom room)
        {
            return room != null ? room.name : "outside";
        }
    }
}
