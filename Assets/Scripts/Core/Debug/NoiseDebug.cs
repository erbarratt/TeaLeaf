using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core
{
    /// <summary>
    /// Debug-only: shows every noise as a wire sphere of its radius that
    /// fades out, in the Scene view and the headset (InHeadsetGizmos), and
    /// makes a test noise from the keyboard until real emitters exist. The
    /// Game view needs keyboard focus (click it). Put it on the Debug
    /// object.
    /// </summary>
    public class NoiseDebug : MonoBehaviour, IDebugDrawable
    {
        // How long a noise stays drawn, in seconds, fading as it goes.
        [SerializeField] private float showDuration = 2f;

        // Also write each noise to the Console.
        [SerializeField] private bool logNoises = true;

        [Header("Test noise")]
        [SerializeField] private Key emitKey = Key.N;

        // Where the test noise is made (e.g. the Main Camera, to make it at
        // the player). This object's own position if empty.
        [SerializeField] private Transform emitFrom;

        [SerializeField] private float testRadius = 8f;
        [SerializeField] private NoiseType testType = NoiseType.Impact;

        // After the test noise, write one Console entry listing what every
        // NoiseListenerDebug made of it.
        [SerializeField] private bool logReport = true;

        // Reused for each report's text.
        private readonly StringBuilder _report = new();

        // The most recent noises and when each was made. A fixed-size ring:
        // a new noise overwrites the oldest, so nothing is allocated however
        // many are made.
        private const int Capacity = 16;
        private readonly Noise[] _noises = new Noise[Capacity];
        private readonly float[] _times = new float[Capacity];
        private int _next;
        private int _count;

        private static readonly Color _noiseColor = new(1f, 0.6f, 0.1f, 1f);

        private void OnEnable()
        {
            NoiseSystem.Emitted += OnNoiseEmitted;
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            NoiseSystem.Emitted -= OnNoiseEmitted;
            DebugDrawRegistry.Unregister(this);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null) {
                return;
            }

            if (keyboard[emitKey].wasPressedThisFrame) {
                Transform from = emitFrom != null ? emitFrom : transform;
                NoiseSystem.Emit(from.position, testRadius, testType, from);

                if (logReport) {
                    LogReport(from.position, testRadius);
                }
            }
        }

        /// <summary>
        /// Writes one Console entry describing the test noise and what every
        /// NoiseListenerDebug in the scene made of it: its room, the
        /// straight-line distance, the route sound actually takes, and
        /// whether that's within the radius. One entry, so it can be copied
        /// out whole. Builds a string, so it allocates - debug only.
        /// </summary>
        private void LogReport(Vector3 position, float radius)
        {
            SoundRoom room = SoundRoom.Find(position);
            _report.Clear();
            _report.AppendLine($"NOISE REPORT: radius {radius:0.0}m at {position:0.00}, room {RoomName(room)}, {SoundPortal.Count} portals");

            for (int i = 0; i < NoiseListenerDebug.Count; i++) {
                NoiseListenerDebug listener = NoiseListenerDebug.Get(i);
                Vector3 ear = listener.EarPosition;
                float straight = Vector3.Distance(position, ear);

                _report.Append($"- {listener.name}: room {RoomName(SoundRoom.Find(ear))}, straight {straight:0.00}m, ");

                // No distance limit, so a route that exists but is too long
                // is still shown.
                if (!SoundPropagation.TryGetPath(position, ear, float.PositiveInfinity, out SoundPath path)) {
                    _report.AppendLine("NO ROUTE -> silent");
                    continue;
                }

                bool heard = path.Distance <= radius;
                _report.Append($"path {path.Distance:0.00}m ({(path.IsDirect ? "direct" : "via portal")}), muffle {path.Muffle:0.00}, from {path.HeardFrom:0.00} -> ");
                _report.AppendLine(heard ? $"HEARD, loudness {1f - path.Distance / radius:0.00}" : "too far, silent");
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

        /// <summary>
        /// Stores each noise in the ring as it's made.
        /// </summary>
        private void OnNoiseEmitted(Noise noise)
        {
            _noises[_next] = noise;
            _times[_next] = Time.time;
            _next = (_next + 1) % Capacity;
            _count = Mathf.Min(_count + 1, Capacity);

            if (logNoises) {
                string source = noise.Source != null ? noise.Source.name : "nobody";
                Debug.Log($"NoiseDebug: {noise.Type} from {source}, radius {noise.Radius:0.0}m at {noise.Position}", this);
            }
        }

        /// <summary>
        /// Draws the recent noises in the Scene view - see DrawDebug().
        /// Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// One wire sphere per recent noise, at its position and radius,
        /// fading out over showDuration. Shared by the Scene view gizmos
        /// and the in-headset view (InHeadsetGizmos).
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            for (int i = 0; i < _count; i++) {
                float age = Time.time - _times[i];

                if (age > showDuration) {
                    continue;
                }

                Color color = _noiseColor;
                color.a = 1f - age / showDuration;
                lines.Color = color;
                lines.WireSphere(_noises[i].Position, _noises[i].Radius);
            }
        }
    }
}
