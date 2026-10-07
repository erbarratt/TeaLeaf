using Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// What a hand draws while aiming a throw: the arc the prop will fly
    /// along, with a disc where it will land. In-world UI, drawn on top of
    /// everything like the hand reticles (OverlayMaterial). White while the
    /// throw is good, red and faded with no disc while it isn't - letting
    /// go of the trigger then cancels.
    ///
    /// The arc can be drawn two ways (Style): a solid line, or a row of
    /// dots. Both follow the same points.
    ///
    /// Only draws. PlayerHandThrowing works out the arc and owns one of
    /// these per hand, made at startup with Create() - so there's nothing
    /// to place in the scene.
    ///
    /// The line is a LineRenderer: Unity's component that draws a ribbon
    /// through a list of points, turned to face the camera. The dots are
    /// one mesh holding every dot, rebuilt each frame of aiming (moving
    /// vertices in an array made once - no objects per dot, and nothing
    /// allocated). Both are placed in the world, not relative to the rig.
    /// </summary>
    public class ThrowArc : MonoBehaviour
    {
        /// How the arc is drawn.
        public enum Style
        {
            Line,
            Dots
        }

        // Segments in the landing disc's rim.
        private const int MarkerSegments = 24;

        // Each dot is a flat shape with this many corners (8 = an octagon,
        // round enough at this size), built as a fan of triangles round a
        // middle vertex: so DotCorners + 1 vertices a dot.
        private const int DotCorners = 8;
        private const int DotVertices = DotCorners + 1;

        // The most dots one arc can show; any beyond this are left off the
        // far end.
        private const int MaxDots = 256;

        // The corners of a dot of radius 1 in its own flat plane, worked
        // out once for every arc: the sin/cos calls aren't repeated per
        // dot per frame.
        private static readonly Vector2[] _dotCorners = BuildDotCorners();

        private LineRenderer _line;
        private Material _material;
        private Transform _marker;
        private MeshRenderer _markerRenderer;

        // The dots: their own object (see Build()), one mesh, and the
        // array its vertices are written into each frame.
        private GameObject _dotsObject;
        private MeshRenderer _dotsRenderer;
        private Mesh _dotsMesh;
        private Vector3[] _dotVertices;

        // How many dots the mesh showed last frame, so only the ones no
        // longer needed are cleared.
        private int _shownDotCount;

        private float _dotRadius;
        private float _dotSpacing;
        private Color _validColor;
        private Color _invalidColor;

        // What's showing now, so renderers and the material are only
        // touched when something changes - not every frame of aiming.
        private bool _isLineVisible;
        private bool _isDotsVisible;
        private bool _isMarkerVisible;
        private bool _isShownValid;

        /// <summary>
        /// Makes an arc as a child of parent, hidden. Called once per hand,
        /// from PlayerHandThrowing.Awake().
        /// </summary>
        public static ThrowArc Create(Transform parent, string objectName, float lineWidth, float dotRadius, float dotSpacing, float markerRadius, Color validColor, Color invalidColor)
        {
            GameObject arcObject = new(objectName);
            arcObject.transform.SetParent(parent, false);

            ThrowArc arc = arcObject.AddComponent<ThrowArc>();
            arc.Build(lineWidth, dotRadius, dotSpacing, markerRadius, validColor, invalidColor);
            return arc;
        }

        /// <summary>
        /// Builds the line, the dots and the landing disc. All start
        /// hidden.
        /// </summary>
        private void Build(float lineWidth, float dotRadius, float dotSpacing, float markerRadius, Color validColor, Color invalidColor)
        {
            _dotRadius = dotRadius;
            _dotSpacing = Mathf.Max(dotSpacing, 0.01f);
            _validColor = validColor;
            _invalidColor = invalidColor;

            // One material for the line and the dots (only one of them
            // shows at a time). Its colour changes with the throw, so it's
            // this arc's own.
            _material = OverlayMaterial.Create(validColor);
            _isShownValid = true;

            _line = gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial = _material;
            _line.useWorldSpace = true;
            _line.widthMultiplier = lineWidth;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.positionCount = 0;
            _line.enabled = false;

            BuildDots(name + " Dots");

            // The landing disc: a child object, laid flat on whatever the
            // arc hits. Only ever shown for a good throw, so always in the
            // valid colour.
            GameObject markerObject = new("Landing Marker");
            _marker = markerObject.transform;
            _marker.SetParent(transform, false);

            markerObject.AddComponent<MeshFilter>().mesh = BuildDiscMesh(markerRadius);

            _markerRenderer = markerObject.AddComponent<MeshRenderer>();
            _markerRenderer.sharedMaterial = OverlayMaterial.Create(validColor);
            _markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _markerRenderer.receiveShadows = false;
            _markerRenderer.enabled = false;
        }

        /// <summary>
        /// Builds the dots' object and mesh. The object sits at the
        /// scene's origin with no parent and never moves, so a vertex's
        /// position in the mesh IS its position in the world - the dots
        /// can be written straight from the arc's points. (As a child of
        /// the rig, every vertex would need converting into the rig's
        /// space each frame.)
        /// </summary>
        private void BuildDots(string objectName)
        {
            _dotsObject = new GameObject(objectName);
            _dotVertices = new Vector3[MaxDots * DotVertices];

            // Which vertices make which triangles never changes - only
            // where the vertices are - so it's set once. Per dot: vertex 0
            // is the middle, 1 to DotCorners the rim, and each triangle is
            // the middle plus two neighbouring rim vertices.
            var triangles = new int[MaxDots * DotCorners * 3];

            for (int dot = 0; dot < MaxDots; dot++) {
                int firstVertex = dot * DotVertices;
                int firstIndex = dot * DotCorners * 3;

                for (int corner = 0; corner < DotCorners; corner++) {
                    int next = (corner + 1) % DotCorners;
                    triangles[firstIndex + corner * 3 + 0] = firstVertex;
                    triangles[firstIndex + corner * 3 + 1] = firstVertex + 1 + corner;
                    triangles[firstIndex + corner * 3 + 2] = firstVertex + 1 + next;
                }
            }

            _dotsMesh = new Mesh { name = "ThrowArcDots" };

            // Tells Unity the vertices will change often, so it keeps the
            // mesh somewhere cheap to update.
            _dotsMesh.MarkDynamic();
            _dotsMesh.SetVertices(_dotVertices);
            _dotsMesh.SetTriangles(triangles, 0);

            // Unity skips drawing a mesh whose bounds are out of view, and
            // would need the bounds recalculated every frame as the dots
            // move. A box far bigger than any level means "always draw" -
            // fine for something only enabled while aiming.
            _dotsMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

            _dotsObject.AddComponent<MeshFilter>().mesh = _dotsMesh;

            _dotsRenderer = _dotsObject.AddComponent<MeshRenderer>();
            _dotsRenderer.sharedMaterial = _material;
            _dotsRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _dotsRenderer.receiveShadows = false;
            _dotsRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            // The dots' object isn't a child of this one, so it has to be
            // removed by hand; and meshes and materials made in code aren't
            // cleaned up with the objects that used them.
            if (_dotsObject != null) {
                Destroy(_dotsObject);
            }

            Destroy(_dotsMesh);
            Destroy(_material);
            Destroy(_markerRenderer.sharedMaterial);
        }

        /// <summary>
        /// Draws the arc through the first count of points (world space),
        /// as a line or as dots. A valid arc is drawn in the valid colour
        /// with the disc at landingPoint, lying on a surface facing
        /// landingNormal; an invalid one in the invalid colour with no
        /// disc. viewerPosition (the player's head) is what the dots turn
        /// to face.
        /// </summary>
        public void Show(Vector3[] points, int count, Style style, bool isValid, Vector3 landingPoint, Vector3 landingNormal, Vector3 viewerPosition)
        {
            bool isLine = style == Style.Line;

            if (isLine) {
                // positionCount first: SetPositions() copies that many
                // points out of the array, which is longer than the arc
                // usually is.
                _line.positionCount = count;
                _line.SetPositions(points);
            } else {
                PlaceDots(points, count, viewerPosition);
            }

            if (isValid != _isShownValid) {
                _isShownValid = isValid;
                _material.color = isValid ? _validColor : _invalidColor;
            }

            // Written this way round so changing the style while aiming
            // (in the Inspector) swaps one for the other.
            if (isLine != _isLineVisible) {
                _isLineVisible = isLine;
                _line.enabled = isLine;
            }

            if (!isLine != _isDotsVisible) {
                _isDotsVisible = !isLine;
                _dotsRenderer.enabled = !isLine;
            }

            if (isValid) {
                // The disc is built in its own XY plane, so pointing its Z
                // axis along the surface's normal lays it flat on it.
                _marker.SetPositionAndRotation(landingPoint, Quaternion.LookRotation(landingNormal));
            }

            if (isValid != _isMarkerVisible) {
                _isMarkerVisible = isValid;
                _markerRenderer.enabled = isValid;
            }
        }

        /// <summary>
        /// Hides the line, the dots and the disc. Free to call when
        /// already hidden.
        /// </summary>
        public void Hide()
        {
            if (_isLineVisible) {
                _isLineVisible = false;
                _line.enabled = false;
            }

            if (_isDotsVisible) {
                _isDotsVisible = false;
                _dotsRenderer.enabled = false;
            }

            if (_isMarkerVisible) {
                _isMarkerVisible = false;
                _markerRenderer.enabled = false;
            }
        }

        /// <summary>
        /// Puts a dot every _dotSpacing metres along the path through the
        /// points, each turned to face the viewer, by moving the dots
        /// mesh's vertices.
        ///
        /// The points are evenly spaced in TIME, so they spread out as the
        /// prop speeds up; the dots are spaced by DISTANCE instead, by
        /// walking along the path and dropping one each time another
        /// _dotSpacing has gone by.
        /// </summary>
        private void PlaceDots(Vector3[] points, int count, Vector3 viewerPosition)
        {
            int dotCount = 0;

            // How far along the whole path the start of the current piece
            // is, and how far along the next dot is due.
            float travelled = 0f;
            float nextDot = 0f;

            for (int i = 1; i < count && dotCount < MaxDots; i++) {
                Vector3 from = points[i - 1];
                Vector3 piece = points[i] - from;
                float length = piece.magnitude;

                if (length <= 0f) {
                    continue;
                }

                // Every dot due somewhere within this piece.
                while (nextDot <= travelled + length && dotCount < MaxDots) {
                    Vector3 centre = from + piece * ((nextDot - travelled) / length);
                    WriteDot(dotCount, centre, viewerPosition);
                    dotCount++;
                    nextDot += _dotSpacing;
                }

                travelled += length;
            }

            // Dots shown last frame but not needed now: all their vertices
            // go to one point, which makes triangles with no area - nothing
            // is drawn for them.
            for (int i = dotCount * DotVertices; i < _shownDotCount * DotVertices; i++) {
                _dotVertices[i] = Vector3.zero;
            }

            _shownDotCount = dotCount;
            _dotsMesh.SetVertices(_dotVertices);
        }

        /// <summary>
        /// Writes one dot's vertices: flat, centred on centre, facing the
        /// viewer.
        /// </summary>
        private void WriteDot(int dot, Vector3 centre, Vector3 viewerPosition)
        {
            // Two directions lying in the dot's flat face: both square to
            // the line from the dot to the viewer, and to each other. A
            // cross product gives a direction at right angles to the two
            // it's given.
            Vector3 toViewer = (viewerPosition - centre).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, toViewer);

            // Looking straight down (or up) at the dot, "up" and the view
            // line are the same direction and the cross product is nothing
            // - any level direction will do then.
            if (right.sqrMagnitude < 0.0001f) {
                right = Vector3.right;
            }

            right = right.normalized * _dotRadius;
            Vector3 up = Vector3.Cross(toViewer, right);

            int vertex = dot * DotVertices;
            _dotVertices[vertex] = centre;

            for (int corner = 0; corner < DotCorners; corner++) {
                Vector2 offset = _dotCorners[corner];
                _dotVertices[vertex + 1 + corner] = centre + right * offset.x + up * offset.y;
            }
        }

        /// <summary>
        /// The corners of a dot of radius 1, evenly spaced round a circle.
        /// </summary>
        private static Vector2[] BuildDotCorners()
        {
            var corners = new Vector2[DotCorners];

            for (int i = 0; i < DotCorners; i++) {
                float angle = i / (float)DotCorners * Mathf.PI * 2f;
                corners[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            return corners;
        }

        /// <summary>
        /// Builds a flat disc of the given radius in the local XY plane, as
        /// a triangle fan round the origin (as HandRayReticle's is).
        /// </summary>
        private static Mesh BuildDiscMesh(float radius)
        {
            var vertices = new Vector3[MarkerSegments + 1];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < MarkerSegments; i++) {
                float angle = i / (float)MarkerSegments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            }

            var triangles = new int[MarkerSegments * 3];

            for (int i = 0; i < MarkerSegments; i++) {
                int next = (i + 1) % MarkerSegments;
                triangles[i * 3 + 0] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = next + 1;
            }

            var mesh = new Mesh { name = "ThrowArcLandingMarker" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
