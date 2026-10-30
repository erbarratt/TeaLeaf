using System.Collections.Generic;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// A mark over a guard's head showing the player what it is thinking:
    /// a question mark while it's suspicious, an exclamation mark once it
    /// has detected the player. Both are a white mark that fills with
    /// colour from the bottom up as the guard's meter does, and empties
    /// downwards as the meter drains.
    ///
    /// - Suspicion or detection rising: the question mark pops up and fills
    ///   yellow with whichever meter is higher (detection fills only once
    ///   suspicion is full, but holds longer, so it can outlast it). Once
    ///   suspicion is full (the guard stops, Suspicious) the mark is red.
    /// - Searching: the question mark is full and yellow for the whole
    ///   search. When the search ends it drains away at drainRate (the
    ///   guard empties its meters at once on giving up; the mark doesn't).
    /// - Detection filling from sight (suspicion full, player still seen):
    ///   the exclamation mark pops up and fills red with detection; full is
    ///   a chase. It empties as detection drains (the player hid, or after
    ///   the chase). Once detection is empty the question mark takes over
    ///   if suspicion is still up. Detection raised another way (a noise,
    ///   a bump) shows on the question mark until full.
    /// - Nothing left to show, or knocked out: no mark.
    ///
    /// Each time a mark appears (or one mark changes to the other) it pops:
    /// grows from nothing with a stretch and squash bounce over
    /// popDuration. It stands upright and turns to face the player's head.
    ///
    /// Drawn as UI, on top of everything (TeaLeaf/OverlayFill, the
    /// Overlay queue with no depth test), like the hand reticles - so it
    /// shows through walls. Shapes are flat meshes built once, shared by
    /// every guard; each guard's fill and colour go on its renderer by
    /// property block, only when they change.
    /// </summary>
    [RequireComponent(typeof(Guard))]
    public class GuardAlertMarker : MonoBehaviour
    {
        // Which shape is up.
        private enum Mark
        {
            None,
            Question,
            Exclamation
        }

        [Tooltip("Height of the mark's foot above the guard's feet (its root), in metres. e.g. 2.1 = just over the head of a 1.8m guard. Higher = floats further above the head.")]
        [SerializeField] private float height = 2.1f;

        [Tooltip("Height of the mark, in metres (width follows its shape). e.g. 0.3 = about the size of the guard's head. Higher = bigger, easier to read from afar.")]
        [SerializeField] private float size = 0.3f;

        [Tooltip("How fast the question mark empties once the guard stops searching or shrugs a suspicion off, in whole marks per second (it never shows less than the suspicion or detection meter). e.g. 1 = a full mark drains away in a second. Higher = vanishes sooner.")]
        [SerializeField] private float drainRate = 1f;

        [Header("Pop")]
        [Tooltip("Seconds the pop takes each time a mark appears or swaps, from appearing to settled (it grows over the first 30%, then wobbles). e.g. 0.45 = a quick bounce. Higher = slower, longer wobble.")]
        [SerializeField] private float popDuration = 0.45f;

        [Tooltip("How far the first stretch of the pop goes, as a share of the mark's height: e.g. 0.35 = 35% taller (and thinner) than settled; the squash after is about half that. Higher = bouncier; 0 = it only grows in, no wobble.")]
        [Range(0f, 1f)]
        [SerializeField] private float popStretch = 0.35f;

        [Header("Colours")]
        [Tooltip("The mark's colour above its fill, where it isn't filled, e.g. white: a half-full question mark is yellow below, white above.")]
        [SerializeField] private Color baseColor = Color.white;

        [Tooltip("The question mark's fill colour while suspicion rises and all through a search, e.g. yellow.")]
        [SerializeField] private Color suspicionColor = new Color(1f, 0.85f, 0.1f, 1f);

        [Tooltip("The question mark's fill colour once suspicion is full or the guard is Suspicious, and the exclamation mark's fill as detection rises, e.g. red.")]
        [SerializeField] private Color alarmColor = new Color(0.9f, 0.1f, 0.1f, 1f);

        // Shared by every guard: built by the first to wake.
        private static Mesh _questionMesh;
        private static Mesh _exclamationMesh;
        private static Material _material;
        private static MaterialPropertyBlock _block;
        private static readonly int _colorId = Shader.PropertyToID("_Color");
        private static readonly int _fillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int _fillId = Shader.PropertyToID("_Fill");

        // Width of each mark's stroke and dot, as a share of its height
        // (the meshes are built 1 high).
        private const float DotRadius = 0.1f;

        private Guard _guard;
        private Transform _markTransform;
        private MeshFilter _markFilter;
        private MeshRenderer _markRenderer;
        private Transform _viewer;

        private Mark _mark;
        private float _popTime;

        // The exclamation mark: latched from detection filling from sight
        // (or reaching full) until it has drained out.
        private bool _isDetected;

        // The question mark's fill: never below the higher of the two
        // meters, full while searching, otherwise draining at drainRate - so a meter
        // emptied at once still drains away on the mark.
        private float _questionFill;

        // Whether the question mark was last red, so it drains in the
        // colour it had.
        private bool _isQuestionRed;

        // What the property block last held, so it's set only on change.
        private float _shownFill = -1f;
        private Color _shownFillColor;

        private void Awake()
        {
            _guard = GetComponent<Guard>();
            CreateShared();

            GameObject markObject = new GameObject("Alert Mark");
            markObject.layer = gameObject.layer;
            _markTransform = markObject.transform;
            _markTransform.SetParent(transform, false);
            _markTransform.localPosition = new Vector3(0f, height, 0f);

            _markFilter = markObject.AddComponent<MeshFilter>();
            _markRenderer = markObject.AddComponent<MeshRenderer>();
            _markRenderer.sharedMaterial = _material;
            _markRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _markRenderer.receiveShadows = false;
            _markRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _markRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _markRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _markRenderer.enabled = false;
        }

        private void OnValidate()
        {
            // Height tuned in Play Mode moves the mark straight away.
            if (_markTransform != null) {
                _markTransform.localPosition = new Vector3(0f, height, 0f);
            }
        }

        private void LateUpdate()
        {
            GuardState state = _guard.State;
            float suspicion = _guard.Suspicion;
            float detection = _guard.Detection;

            // Down: nothing to show, and nothing left to drain once it's up.
            if (state == GuardState.KnockedOut) {
                _isDetected = false;
                _questionFill = 0f;
            }

            // Latched: set once the guard's eyes start filling detection
            // (or it's full some other way, e.g. a noise), cleared at
            // empty - so the exclamation mark fills as detection rises.
            if (detection > 0f && (detection >= 1f || _guard.IsDetecting)) {
                _isDetected = true;
            } else if (detection <= 0f) {
                _isDetected = false;
            }

            // Full through a search, but not while shrugging it off: the
            // meters are emptied then, and the mark drains away.
            if (state == GuardState.Search && !_guard.IsShrugging) {
                _questionFill = 1f;
                _isQuestionRed = false;
            } else if (state != GuardState.KnockedOut) {
                // Detection counts too: it holds longer than suspicion, or
                // a noise raises it, so it can be the higher, and until
                // it's full (the exclamation mark) this is the only mark
                // that can show it.
                float meter = Mathf.Max(suspicion, detection);
                _questionFill = Mathf.Max(meter, _questionFill - drainRate * Time.deltaTime);

                // Red once suspicion is full: stopped to look (Suspicious),
                // or about to. Yellow while either meter fills; a mark
                // draining after a suspicion was shrugged off stays the
                // colour it was.
                if (state == GuardState.Suspicious || suspicion >= 1f) {
                    _isQuestionRed = true;
                } else if (meter > 0f) {
                    _isQuestionRed = false;
                }
            }

            Mark mark = Mark.None;
            float fill = 0f;
            Color fillColor = alarmColor;

            if (_isDetected) {
                mark = Mark.Exclamation;
                fill = detection;
            } else if (_questionFill > 0f) {
                mark = Mark.Question;
                fill = _questionFill;
                fillColor = _isQuestionRed ? alarmColor : suspicionColor;
            }

            if (mark != _mark) {
                SetMark(mark);
            }

            if (mark == Mark.None) {
                return;
            }

            SetFill(fill, fillColor);
            Place();
        }

        /// <summary>
        /// Switches to another mark (or none), starting its pop.
        /// </summary>
        private void SetMark(Mark mark)
        {
            _mark = mark;
            _markRenderer.enabled = mark != Mark.None;

            if (mark == Mark.None) {
                return;
            }

            _markFilter.sharedMesh = mark == Mark.Question ? _questionMesh : _exclamationMesh;
            _popTime = Time.time;
        }

        /// <summary>
        /// Sets the mark's fill level and colour on its renderer, only when
        /// either has changed.
        /// </summary>
        private void SetFill(float fill, Color fillColor)
        {
            if (fill == _shownFill && fillColor == _shownFillColor) {
                return;
            }

            _shownFill = fill;
            _shownFillColor = fillColor;

            _block.Clear();
            _block.SetColor(_colorId, baseColor);
            _block.SetColor(_fillColorId, fillColor);
            _block.SetFloat(_fillId, fill);
            _markRenderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// Turns the mark to face the viewer, upright, and scales it for
        /// the pop.
        /// </summary>
        private void Place()
        {
            if (_viewer == null) {
                Camera viewerCamera = Camera.main;

                if (viewerCamera == null) {
                    return;
                }

                _viewer = viewerCamera.transform;
            }

            // Forward away from the viewer, so the mark's +X is the
            // viewer's right and the question mark reads the right way
            // round. Upright: the fill line stays level.
            Vector3 away = _markTransform.position - _viewer.position;
            away.y = 0f;

            if (away.sqrMagnitude > 0.0001f) {
                _markTransform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }

            _markTransform.localScale = PopScale((Time.time - _popTime) / Mathf.Max(popDuration, 0.001f)) * size;
        }

        /// <summary>
        /// The pop's scale at a point through it (0-1, settled after): grows
        /// from nothing over the first 30% (fast, easing in to size), while
        /// a fading wobble stretches it tall and thin, squashes it short
        /// and wide, and a little stretch again. Width goes as one over the
        /// square root of height, so it keeps its bulk as it wobbles.
        /// </summary>
        private Vector3 PopScale(float t)
        {
            if (t >= 1f) {
                return Vector3.one;
            }

            float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.3f), 3f);

            // One and a half waves, fading to nothing by the end: stretch
            // at 1/6, squash at 1/2, small stretch at 5/6.
            float stretch = 1f + popStretch * Mathf.Sin(t * 3f * Mathf.PI) * (1f - t);
            float width = 1f / Mathf.Sqrt(stretch);

            return new Vector3(width * grow, stretch * grow, grow);
        }

        /// <summary>
        /// The marks' meshes, material and property block every guard
        /// shares, made by the first guard to wake.
        /// </summary>
        private static void CreateShared()
        {
            if (_block == null) {
                _block = new MaterialPropertyBlock();
            }

            if (_material == null) {
                Shader shader = Shader.Find("TeaLeaf/OverlayFill");

                if (shader == null) {
                    Debug.LogError("GuardAlertMarker: shader 'TeaLeaf/OverlayFill' not found - alert marks will be pink.");
                    shader = Shader.Find("Hidden/InternalErrorShader");
                }

                _material = new Material(shader) {
                    name = "Guard Alert Mark",
                    hideFlags = HideFlags.DontSave,
                    renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay
                };
            }

            if (_questionMesh == null) {
                _questionMesh = BuildQuestionMesh();
            }

            if (_exclamationMesh == null) {
                _exclamationMesh = BuildExclamationMesh();
            }
        }

        /// <summary>
        /// The exclamation mark, 1 high, foot at the origin, flat in XY: the
        /// dot, and above it an elongated teardrop - round at the top,
        /// narrowing to a small round end near the dot. The teardrop is the
        /// outline round two circles (wide one at the top, small one at the
        /// bottom), joined by the lines touching both; convex, so a fan
        /// from its middle fills it.
        /// </summary>
        private static Mesh BuildExclamationMesh()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            AddDisc(vertices, triangles, new Vector2(0f, DotRadius), DotRadius);

            Vector2 bottom = new Vector2(0f, 0.32f);
            Vector2 top = new Vector2(0f, 0.87f);
            const float bottomRadius = 0.04f;
            const float topRadius = 0.13f;

            // Where the side lines touch the circles, as an angle round
            // each: a little below level, as the top circle is the wider.
            float side = -Mathf.Asin((topRadius - bottomRadius) / (top.y - bottom.y));
            const int arcSegments = 24;

            int centre = vertices.Count;
            vertices.Add((bottom + top) * 0.5f);
            int first = vertices.Count;

            // Over the top, right to left.
            for (int i = 0; i <= arcSegments; i++) {
                float angle = Mathf.Lerp(side, Mathf.PI - side, i / (float)arcSegments);
                vertices.Add(top + topRadius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            }

            // Round the bottom, left to right.
            for (int i = 0; i <= arcSegments; i++) {
                float angle = Mathf.Lerp(Mathf.PI - side, 2f * Mathf.PI + side, i / (float)arcSegments);
                vertices.Add(bottom + bottomRadius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            }

            int count = vertices.Count - first;

            for (int i = 0; i < count; i++) {
                triangles.Add(centre);
                triangles.Add(first + i);
                triangles.Add(first + (i + 1) % count);
            }

            return CreateMesh("Guard Exclamation Mark", vertices, triangles);
        }

        /// <summary>
        /// The question mark, 1 high, foot at the origin, flat in XY: the
        /// same dot as the exclamation mark, and above it a stroke with
        /// round ends - a short stem rising from over the dot, bending
        /// right in an S into a round hook that curls up over the top and
        /// down the left.
        /// </summary>
        private static Mesh BuildQuestionMesh()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            AddDisc(vertices, triangles, new Vector2(0f, DotRadius), DotRadius);

            // The stroke's centre line: a Bezier S from the stem up to the
            // hook's right side, then the hook as an arc round its centre.
            var path = new List<Vector2>();
            Vector2 p0 = new Vector2(0f, 0.37f);
            Vector2 p1 = new Vector2(0f, 0.52f);
            Vector2 p2 = new Vector2(0.19f, 0.5f);
            Vector2 p3 = new Vector2(0.19f, 0.71f);
            const int curveSegments = 12;

            for (int i = 0; i <= curveSegments; i++) {
                float t = i / (float)curveSegments;
                float u = 1f - t;
                path.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
            }

            Vector2 hookCentre = new Vector2(0f, 0.71f);
            const float hookRadius = 0.19f;
            const float hookEnd = 200f * Mathf.Deg2Rad;
            const int hookSegments = 24;

            for (int i = 1; i <= hookSegments; i++) {
                float angle = hookEnd * i / hookSegments;
                path.Add(hookCentre + hookRadius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            }

            // Half the stroke's width: the hook's top edge at 0.71 + 0.19 +
            // 0.095, about 1.
            const float halfWidth = 0.095f;
            int start = vertices.Count;

            for (int i = 0; i < path.Count; i++) {
                Vector2 along = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)];
                Vector2 across = new Vector2(-along.y, along.x).normalized * halfWidth;
                vertices.Add(path[i] + across);
                vertices.Add(path[i] - across);
            }

            for (int i = 0; i < path.Count - 1; i++) {
                int a = start + i * 2;
                triangles.Add(a);
                triangles.Add(a + 1);
                triangles.Add(a + 2);
                triangles.Add(a + 1);
                triangles.Add(a + 3);
                triangles.Add(a + 2);
            }

            // Round ends.
            AddDisc(vertices, triangles, path[0], halfWidth);
            AddDisc(vertices, triangles, path[path.Count - 1], halfWidth);

            return CreateMesh("Guard Question Mark", vertices, triangles);
        }

        /// <summary>
        /// Adds a filled circle: a fan from its centre.
        /// </summary>
        private static void AddDisc(List<Vector3> vertices, List<int> triangles, Vector2 centre, float radius)
        {
            const int segments = 24;
            int middle = vertices.Count;
            vertices.Add(centre);

            for (int i = 0; i < segments; i++) {
                float angle = 2f * Mathf.PI * i / segments;
                vertices.Add(centre + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            }

            for (int i = 0; i < segments; i++) {
                triangles.Add(middle);
                triangles.Add(middle + 1 + i);
                triangles.Add(middle + 1 + (i + 1) % segments);
            }
        }

        /// <summary>
        /// Makes a finished, GPU-only mesh from built lists.
        /// </summary>
        private static Mesh CreateMesh(string meshName, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh {
                name = meshName,
                hideFlags = HideFlags.DontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
