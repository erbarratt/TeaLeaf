using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core
{
    /// <summary>
    /// Fades the whole view to a flat colour (black) and back: to black when
    /// the player is caught or the level restarts, and from black to clear
    /// when a level starts, so the level is revealed rather than popping in.
    ///
    /// Lives on a child of the Main Camera, so it follows the headset with
    /// no code (like MantleIndicator). It builds a small cube around the
    /// head at runtime and draws it with the overlay shader: no depth test,
    /// both sides of every face, in both eyes. From inside a cube every
    /// direction you look passes through exactly one face, so the whole view
    /// is covered once, however the head turns - a flat quad in front of the
    /// face would need to be huge to cover both eyes' full field of view.
    ///
    /// Costs nothing while the view is clear: the renderer is off, and the
    /// component disables itself so its Update() stops being called. Only a
    /// fade in progress runs Update().
    /// </summary>
    public class ScreenFade : MonoBehaviour
    {
        [SerializeField] private Color fadeColor = Color.black;

        [Header("Level Start")]

        // Start the level black and fade to clear. Off, the level starts
        // clear and the fade only happens when something asks for it.
        [SerializeField] private bool fadeInOnStart = true;

        // How long the view stays fully black before the level-start fade
        // begins, in seconds - covers the first frames after loading, where
        // tracking settles and frame times are uneven.
        [SerializeField] private float startHoldDuration = 0.3f;

        // How long the level-start fade from black to clear takes, in seconds.
        [SerializeField] private float startFadeDuration = 1.5f;

        // Half the cube's width in metres: its faces are this far from the
        // head. Well outside the camera's near clip plane and the distance
        // between the eyes, and the exact value doesn't matter beyond that -
        // with no depth test it covers everything at any size.
        private const float HalfSize = 0.5f;

        // The longest step one frame may advance a fade by, in seconds. A
        // frame that takes far longer than normal (a loading hitch) would
        // otherwise jump the fade most of the way in one go.
        private const float MaxTimeStep = 0.05f;

        // Drawn after the other overlay materials (reticles, mantle arrow),
        // so a fade covers those too.
        private const int RenderQueueOffset = 100;

        private static readonly int _colorId = Shader.PropertyToID("_Color");

        private MeshRenderer _meshRenderer;
        private Material _material;

        // The fade in progress: from one opacity to another over _duration,
        // after waiting _delay. _elapsed counts up through the delay first.
        private float _fromAlpha;
        private float _toAlpha;
        private float _duration;
        private float _delay;
        private float _elapsed;

        // Called once when the fade in progress finishes, if set.
        private Action _onComplete;

        // How many Hold() calls are waiting for their Release() - see Hold().
        private int _holds;

        /// The scene's screen fade, so other systems (game state, later the
        /// level manager) can reach it without a scene search. Null if the
        /// scene has none.
        public static ScreenFade Instance { get; private set; }

        /// How opaque the fade is right now: 0 = clear, 1 = fully covered.
        public float Alpha { get; private set; }

        /// True while a fade is running (including a level-start hold).
        public bool IsFading { get; private set; }

        private void Awake()
        {
            Instance = this;

            MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = BuildCubeMesh(HalfSize);

            // The overlay material draws on top of everything with no depth
            // test - see OverlayMaterial. One step later in the queue than
            // the UI markers, so the fade hides them as well.
            _material = OverlayMaterial.Create(fadeColor);
            _material.renderQueue = (int)RenderQueue.Overlay + RenderQueueOffset;

            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Set up in Awake() rather than Start(), so the very first frame
            // rendered is already black - no flash of the level before the
            // fade-in begins.
            if (fadeInOnStart) {
                ApplyAlpha(1f, true);
                StartFade(0f, startFadeDuration, startHoldDuration, null);
            } else {
                ApplyAlpha(0f, true);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) {
                Instance = null;
            }

            // Made in code, so nothing else will free it.
            Destroy(_material);
        }

        /// <summary>
        /// Fades from however covered the view is now to fully covered
        /// (black) over duration seconds, then calls onComplete if given -
        /// e.g. to reload the level once nothing can be seen. A duration of
        /// 0 covers the view at once.
        /// </summary>
        public void FadeOut(float duration, Action onComplete = null)
        {
            StartFade(1f, duration, 0f, onComplete);
        }

        /// <summary>
        /// Fades from however covered the view is now to fully clear over
        /// duration seconds, revealing the level, then calls onComplete if
        /// given.
        /// </summary>
        public void FadeIn(float duration, Action onComplete = null)
        {
            StartFade(0f, duration, 0f, onComplete);
        }

        /// <summary>
        /// Freezes any fade - running now or started later - until Release()
        /// is called, leaving the view as covered as it is. For work that
        /// must finish while the screen is still black: at level start the
        /// fade begins black, so a system that calls this from its Start()
        /// (PlayerTracking, recentring the headset) keeps the view black
        /// until it's done. Calls are counted, so several systems can hold
        /// at once and the fade waits for all of them; each Hold() needs
        /// exactly one Release().
        /// </summary>
        public void Hold()
        {
            _holds++;
        }

        /// <summary>
        /// Ends one Hold(). The fade carries on once every hold is released.
        /// </summary>
        public void Release()
        {
            _holds = Mathf.Max(_holds - 1, 0);
        }

        /// <summary>
        /// Starts a fade from the current opacity to toAlpha, replacing any
        /// fade already running (its onComplete is dropped, not called).
        /// Starting from the current opacity, rather than always from fully
        /// clear or covered, means a fade interrupted halfway carries on
        /// smoothly from where it was.
        /// </summary>
        private void StartFade(float toAlpha, float duration, float delay, Action onComplete)
        {
            _fromAlpha = Alpha;
            _toAlpha = toAlpha;
            _duration = duration;
            _delay = delay;
            _elapsed = 0f;
            _onComplete = onComplete;
            IsFading = true;

            // Switches Update() on - see the class comment.
            enabled = true;
        }

        /// <summary>
        /// Advances the fade in progress. Only runs while one is: the
        /// component disables itself when it finishes. Uses unscaled time,
        /// so a fade still plays if the game is ever paused by setting the
        /// time scale to 0.
        /// </summary>
        private void Update()
        {
            // Frozen while anything is holding - see Hold().
            if (_holds > 0) {
                return;
            }

            _elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxTimeStep);

            // Still holding before the fade starts.
            if (_elapsed < _delay) {
                return;
            }

            float t = _duration > 0f ? Mathf.Clamp01((_elapsed - _delay) / _duration) : 1f;

            // SmoothStep eases in and out, so the fade doesn't start or stop
            // abruptly.
            ApplyAlpha(Mathf.Lerp(_fromAlpha, _toAlpha, Mathf.SmoothStep(0f, 1f, t)), false);

            if (t < 1f) {
                return;
            }

            IsFading = false;
            enabled = false;

            // Cleared before it's called, so the callback can start another
            // fade (with its own callback) without this one wiping it.
            Action onComplete = _onComplete;
            _onComplete = null;
            onComplete?.Invoke();
        }

        /// <summary>
        /// Sets how covered the view is. The colour is written straight to
        /// the material this component made and owns - not through
        /// renderer.material, which would make a hidden copy. The renderer
        /// is only switched when the view goes between clear and not clear
        /// (or when forced, for the first call), so a clear view draws
        /// nothing at all.
        /// </summary>
        private void ApplyAlpha(float alpha, bool force)
        {
            bool wasVisible = Alpha > 0f;
            bool isVisible = alpha > 0f;
            Alpha = alpha;

            if (isVisible) {
                Color color = fadeColor;
                color.a = alpha;
                _material.SetColor(_colorId, color);
            }

            if (force || isVisible != wasVisible) {
                _meshRenderer.enabled = isVisible;
            }
        }

        /// <summary>
        /// Play Mode test from the component's right-click menu: fade to
        /// black over a second.
        /// </summary>
        [ContextMenu("Test Fade Out")]
        private void TestFadeOut()
        {
            FadeOut(1f);
        }

        /// <summary>
        /// Play Mode test from the component's right-click menu: fade back
        /// to clear over a second.
        /// </summary>
        [ContextMenu("Test Fade In")]
        private void TestFadeIn()
        {
            FadeIn(1f);
        }

        /// <summary>
        /// Builds a cube centred on the origin, halfSize from the centre to
        /// each face: 8 corners shared by 12 triangles. The overlay shader
        /// draws both sides of every triangle, so which way each one faces
        /// doesn't matter.
        /// </summary>
        private static Mesh BuildCubeMesh(float halfSize)
        {
            float h = halfSize;

            var vertices = new[] {
                new Vector3(-h, -h, -h),
                new Vector3(h, -h, -h),
                new Vector3(h, h, -h),
                new Vector3(-h, h, -h),
                new Vector3(-h, -h, h),
                new Vector3(h, -h, h),
                new Vector3(h, h, h),
                new Vector3(-h, h, h)
            };

            var triangles = new[] {
                // Back (-Z) and front (+Z).
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,

                // Left (-X) and right (+X).
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,

                // Bottom (-Y) and top (+Y).
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2
            };

            var mesh = new Mesh { name = "ScreenFadeCube" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
