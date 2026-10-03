using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Debug-only: a stand-in pair of ears until guards exist. Put it on
    /// any object (a cube where a guard might stand); it registers as a
    /// noise listener and shows what it hears: the object itself turns
    /// yellow (if it has a renderer), a wire marker round it turns from
    /// grey to yellow with a line to the noise, in the Scene view and the
    /// headset (InHeadsetGizmos), plus a Console line.
    /// </summary>
    public class NoiseListenerDebug : MonoBehaviour, INoiseListener, IDebugDrawable
    {
        // How long the object stays lit after hearing a noise, in seconds.
        [SerializeField] private float showDuration = 2f;

        // Also write each noise heard to the Console.
        [SerializeField] private bool logHeard = true;

        // The last noise heard and when. Starts far in the past so the
        // marker begins unlit.
        private Vector3 _lastPosition;

        // Where that noise seemed to come from: the portal it came through,
        // or the noise itself in the same room.
        private Vector3 _lastHeardFrom;
        private float _lastTime = float.NegativeInfinity;

        // Whether the object is currently showing the "heard" colour, so the
        // renderer is only touched when that changes.
        private bool _isLit;

        // This object's renderer, if it has one (the test cube's).
        private Renderer _renderer;

        // Per-renderer colour override. Unlike renderer.material, it doesn't
        // create a copy of the material.
        private MaterialPropertyBlock _propertyBlock;

        // Bigger than the 0.3m test cube, so the marker shows round it.
        private const float MarkerRadius = 0.3f;

        private static readonly Color _idleColor = new(0.6f, 0.6f, 0.6f, 0.6f);
        private static readonly Color _heardColor = new(1f, 0.9f, 0.1f, 1f);

        // The colour property of URP's Lit shader, looked up once.
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        // Every enabled debug listener, so NoiseDebug can list them all in
        // its test report.
        private static readonly List<NoiseListenerDebug> _all = new();

        /// How many debug listeners are enabled.
        public static int Count => _all.Count;

        public Vector3 EarPosition => transform.position;

        /// <summary>
        /// The debug listener at index - read with Count in a for loop.
        /// </summary>
        public static NoiseListenerDebug Get(int index)
        {
            return _all[index];
        }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            _all.Add(this);
            NoiseSystem.Register(this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            _all.Remove(this);
            NoiseSystem.Unregister(this);
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// Turns the object back to its own colour once showDuration has
        /// passed since the last noise.
        /// </summary>
        private void Update()
        {
            if (_isLit && Time.time - _lastTime > showDuration) {
                SetLit(false);
            }
        }

        /// <summary>
        /// Remembers the noise for drawing, lights the object up and logs it.
        /// </summary>
        public void OnNoiseHeard(in Noise noise, float loudness, Vector3 heardFrom)
        {
            _lastPosition = noise.Position;
            _lastHeardFrom = heardFrom;
            _lastTime = Time.time;
            SetLit(true);

            if (logHeard) {
                Debug.Log($"NoiseListenerDebug: {name} heard {noise.Type} at loudness {loudness:0.00}", this);
            }
        }

        /// <summary>
        /// Shows the "heard" colour on the object's renderer, or clears the
        /// override so its material's own colour shows again.
        /// </summary>
        private void SetLit(bool lit)
        {
            if (lit == _isLit) {
                return;
            }

            _isLit = lit;

            if (_renderer == null) {
                return;
            }

            if (lit) {
                _propertyBlock.SetColor(_baseColorId, _heardColor);
                _renderer.SetPropertyBlock(_propertyBlock);
            } else {
                _renderer.SetPropertyBlock(null);
            }
        }

        /// <summary>
        /// Draws the marker in the Scene view - see DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// A wire sphere round the ears: grey while idle, yellow with a
        /// line to the noise for showDuration after hearing one. Shared by
        /// the Scene view gizmos and the in-headset view (InHeadsetGizmos).
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            bool heard = Time.time - _lastTime <= showDuration;

            lines.Color = heard ? _heardColor : _idleColor;
            lines.WireSphere(EarPosition, MarkerRadius);

            if (heard) {
                lines.Line(EarPosition, _lastHeardFrom);
                lines.Line(_lastHeardFrom, _lastPosition);
            }
        }
    }
}
