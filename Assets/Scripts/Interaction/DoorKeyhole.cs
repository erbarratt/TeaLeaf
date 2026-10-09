using System.Collections.Generic;
using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A keyhole the player can look through. At rest it's a small hole in
    /// the door; as the head comes near, it grows into an opening big
    /// enough to see the room behind with both eyes, and shrinks again as
    /// the head moves away. Only while the door is shut: an open door's
    /// keyhole stays small.
    ///
    /// The shape is the classic keyhole: a circle with a slot running down
    /// from it. One number sizes all of it - the circle's radius. The slot
    /// is as wide as the radius and reaches a diameter below the circle,
    /// so the whole shape is two radii wide and four tall.
    ///
    /// The opening is really cut out of the door when it's drawn (the
    /// TeaLeaf/DoorLeaf shader, which the leaf's material must use), so
    /// the player sees through it with their own eyes: real depth, moving
    /// the head looks round the room, and nothing is drawn twice. Only the
    /// picture changes - the leaf's collider is untouched, so hands, props,
    /// light and sound are stopped by the door as before. The shader's cut
    /// has no depth, so this component also makes the opening's walls: a
    /// short tube through the door's thickness (see BuildWalls()).
    ///
    /// This object marks where the keyhole is: its position is the middle
    /// of the whole shape (which is the bottom of the circle), its X runs
    /// across the door's face and its Y up it. A child of the door, so it
    /// swings with it.
    ///
    /// No Update(): Player.PlayerKeyholes finds the keyhole the head is
    /// near and calls Tick() on that one only, so a keyhole nobody is at
    /// costs nothing.
    /// </summary>
    public class DoorKeyhole : MonoBehaviour, IDebugDrawable
    {
        // The door it's in. Optional: with none, it opens whenever the
        // head is near.
        [SerializeField] private Door door;

        // The door leaf's renderer, whose material uses TeaLeaf/DoorLeaf.
        [SerializeField] private MeshRenderer leafRenderer;

        // Anything else on the door the opening has to go through as
        // well - a lock plate standing on each face. Each must use a
        // TeaLeaf/DoorLeaf material too (its own, in its own colour).
        // Optional.
        [SerializeField] private MeshRenderer[] plateRenderers;

        [Header("Size")]

        // The circle's radius in metres: at rest, and with the head right
        // up to it. The whole keyhole is two of these wide and four tall.
        [SerializeField] private float restRadius = 0.005f;
        [SerializeField] private float openRadius = 0.05f;

        [Header("Depth")]

        // How thick the door is at the keyhole, in metres: how deep the
        // opening's walls run. 0 = measured from the leaf's renderer,
        // which is right for a plain slab.
        [SerializeField] private float thickness;

        // The colour of the opening's walls - the cut wood inside the
        // door. Lit like the door itself.
        [SerializeField] private Color wallColor = new(0.2f, 0.13f, 0.08f, 1f);

        [Header("Opening")]

        // How far the head is from the keyhole (metres) when it starts to
        // open, and when it's fully open.
        [SerializeField] private float openStartDistance = 0.6f;
        [SerializeField] private float openFullDistance = 0.25f;

        // The fastest it opens or shuts: 6 = fully open in a sixth of a
        // second. Only noticed when the door is opened with the head at
        // the keyhole; otherwise the head's own movement is slower.
        [SerializeField] private float openSpeed = 6f;

        // How far out from the keyhole's middle the gizmo's outlines are
        // drawn, each way, so they stand just proud of a door up to 5cm
        // thick instead of being buried inside it.
        private const float GizmoStandOff = 0.03f;

        private static readonly Color _restColor = new(1f, 0.85f, 0.2f, 1f);
        private static readonly Color _openColor = new(0.3f, 1f, 0.4f, 1f);

        // Every enabled keyhole, so the player can find the nearest
        // without searching the scene.
        private static readonly List<DoorKeyhole> _all = new();

        private static readonly int _keyholeCentreId = Shader.PropertyToID("_KeyholeCentre");
        private static readonly int _keyholeRightId = Shader.PropertyToID("_KeyholeRight");
        private static readonly int _keyholeUpId = Shader.PropertyToID("_KeyholeUp");
        private static readonly int _keyholeSizeId = Shader.PropertyToID("_KeyholeSize");
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        // The outline, as the walls' mesh and the gizmo draw it, is four
        // runs of points: the circle's arc (ArcSegments straight pieces),
        // then the slot's left side, bottom and right side (two points
        // each). The runs don't share points where they meet, because a
        // point can only face one way and the walls turn sharply there.
        private const int ArcSegments = 20;
        private const int ArcPoints = ArcSegments + 1;
        private const int OutlinePoints = ArcPoints + 6;

        // One wall piece between each point and the next within a run.
        private const int OutlinePieces = ArcSegments + 3;

        // The outline for a circle of radius 1, and which way out of the
        // opening each point faces. The same for every keyhole and every
        // size (a bigger keyhole is just these times its radius), so
        // worked out once - see BuildOutline().
        private static Vector2[] _outlinePoints;
        private static Vector2[] _outlineNormals;

        // This leaf's own values for the shader, laid over its material's
        // (so the material itself is shared and never changed).
        private MaterialPropertyBlock _block;

        // The same for each plate: the keyhole's place is different in
        // each object's own space, so each needs its own.
        private MaterialPropertyBlock[] _plateBlocks;

        private bool _hasDoor;
        private bool _hasRenderer;

        // The opening's walls: a short tube through the door, made once
        // in Awake(). Its points are moved when the opening changes size
        // (the same array each time, so nothing is allocated).
        private Mesh _wallMesh;
        private Vector3[] _wallVertices;
        private float _halfThickness;

        // How far open it is, 0 (rest) to 1, and the value the shader was
        // last given - it's only told when that changes.
        private float _amount;
        private float _appliedAmount = -1f;

        /// <summary>
        /// Editor-only: runs when the component is added. Finds the door
        /// above it and the door's first renderer.
        /// </summary>
        private void Reset()
        {
            door = GetComponentInParent<Door>();

            if (door != null) {
                leafRenderer = door.GetComponentInChildren<MeshRenderer>();
            }
        }

        private void Awake()
        {
            _hasDoor = door != null;
            _hasRenderer = leafRenderer != null;

            if (!_hasRenderer) {
                Debug.LogWarning($"DoorKeyhole '{name}': no leaf renderer assigned, so there is nothing to cut an opening in.", this);
                return;
            }

            // Where the keyhole is, for the leaf and for each lock plate.
            // The keyhole moves with all of them, so this is worked out
            // once.
            Transform leaf = leafRenderer.transform;
            _block = BuildBlock(leaf);

            int plateCount = plateRenderers != null ? plateRenderers.Length : 0;
            _plateBlocks = new MaterialPropertyBlock[plateCount];

            for (int i = 0; i < plateCount; i++) {
                _plateBlocks[i] = BuildBlock(plateRenderers[i].transform);
            }

            _halfThickness = (thickness > 0f ? thickness : MeasureThickness(Quaternion.Inverse(leaf.rotation), leaf.lossyScale)) * 0.5f;
            BuildWalls();

            Apply();
        }

        /// <summary>
        /// The keyhole's place as the shader needs it for one object the
        /// opening is cut in (the leaf, or a lock plate): in that object's
        /// own space, since that's what a pixel knows about itself.
        /// </summary>
        private MaterialPropertyBlock BuildBlock(Transform target)
        {
            Quaternion toTarget = Quaternion.Inverse(target.rotation);
            Vector3 scale = target.lossyScale;

            MaterialPropertyBlock block = new();
            block.SetVector(_keyholeCentreId, target.InverseTransformPoint(transform.position));

            // The two directions across the door's face. Turned into the
            // object's space, then multiplied by its scale: the leaf is a
            // stretched cube, so one unit of its own space is "scale"
            // metres, and this makes the shader's distances come out in
            // metres.
            block.SetVector(_keyholeRightId, Vector3.Scale(scale, toTarget * transform.right));
            block.SetVector(_keyholeUpId, Vector3.Scale(scale, toTarget * transform.up));
            return block;
        }

        private void OnEnable()
        {
            _all.Add(this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            _all.Remove(this);
            DebugDrawRegistry.Unregister(this);
        }

        private void OnDestroy()
        {
            // A mesh made from code isn't cleaned up with its object.
            if (_wallMesh != null) {
                Destroy(_wallMesh);
            }
        }

        /// <summary>
        /// How thick the leaf is along the way through the keyhole, from
        /// the leaf mesh's bounding box: the box's half size along each of
        /// its own axes (in metres), each counted by how much that axis
        /// points through the door. For a slab square to the keyhole
        /// that's simply its thickness.
        /// </summary>
        private float MeasureThickness(Quaternion toLeaf, Vector3 scale)
        {
            Vector3 through = toLeaf * transform.forward;
            Vector3 extents = Vector3.Scale(leafRenderer.localBounds.extents, scale);

            return 2f * (Mathf.Abs(through.x * extents.x) + Mathf.Abs(through.y * extents.y) + Mathf.Abs(through.z * extents.z));
        }

        /// <summary>
        /// Works out, once for all keyholes, the outline for a circle of
        /// radius 1 - the same shape the shader cuts (see KeyholeDistance()
        /// in DoorLeaf.shader). Measured from the keyhole's middle: the
        /// circle's centre is 1 up, the slot is half a unit either side and
        /// reaches 2 down.
        ///
        /// The points go round anticlockwise as seen from the front. The
        /// slot's sides meet the circle 60 degrees either side of straight
        /// down from its centre (the slot is half a radius out, and the
        /// sine of 30 degrees is a half). So the arc starts at the right
        /// hand meeting point, 60 degrees below level, and runs 300
        /// degrees over the top to the left hand one; then down the
        /// slot's left side, along its bottom and up its right side.
        /// </summary>
        private static void BuildOutline()
        {
            if (_outlinePoints != null) {
                return;
            }

            _outlinePoints = new Vector2[OutlinePoints];
            _outlineNormals = new Vector2[OutlinePoints];

            Vector2 circleCentre = new(0f, 1f);

            for (int i = 0; i < ArcPoints; i++) {
                float angle = Mathf.Deg2Rad * (-60f + 300f * i / ArcSegments);
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));

                // On a circle, a point faces straight out from the centre.
                _outlinePoints[i] = circleCentre + direction;
                _outlineNormals[i] = direction;
            }

            Vector2 leftMeeting = _outlinePoints[ArcSegments];
            Vector2 rightMeeting = _outlinePoints[0];
            Vector2 bottomLeft = new(-0.5f, -2f);
            Vector2 bottomRight = new(0.5f, -2f);

            int index = ArcPoints;
            AddRun(ref index, leftMeeting, bottomLeft, Vector2.left);
            AddRun(ref index, bottomLeft, bottomRight, Vector2.down);
            AddRun(ref index, bottomRight, rightMeeting, Vector2.right);
        }

        /// <summary>
        /// Adds one straight side of the slot to the outline: its two ends,
        /// both facing the same way.
        /// </summary>
        private static void AddRun(ref int index, Vector2 from, Vector2 to, Vector2 normal)
        {
            _outlinePoints[index] = from;
            _outlineNormals[index] = normal;
            _outlinePoints[index + 1] = to;
            _outlineNormals[index + 1] = normal;
            index += 2;
        }

        /// <summary>
        /// True if outline point i is the last of its run: the next point
        /// starts a new run, and no piece joins the two.
        /// </summary>
        private static bool IsRunEnd(int i)
        {
            return i == ArcSegments || i == ArcSegments + 2 || i == ArcSegments + 4 || i == ArcSegments + 6;
        }

        /// <summary>
        /// Makes the opening's walls: a tube with the opening's outline,
        /// running from one face of the door to the other, seen from the
        /// inside. Without it the door would look paper-thin at the
        /// opening. Real geometry, so it's right in both eyes.
        ///
        /// The mesh is two rings of points - the outline on the front face
        /// and again on the back - joined by a strip of four-sided pieces.
        /// Here only what never changes is set: which points make which
        /// triangles, and which way each point faces. UpdateWalls() puts
        /// the points in place.
        ///
        /// On a child object of its own, drawn with the leaf's material
        /// (the same shader; no opening is cut in it, since it has no
        /// keyhole values of its own) in wallColor. This object and
        /// everything above it are unscaled, so the mesh is in metres.
        /// </summary>
        private void BuildWalls()
        {
            BuildOutline();

            _wallVertices = new Vector3[OutlinePoints * 2];
            Vector3[] normals = new Vector3[OutlinePoints * 2];
            int[] triangles = new int[OutlinePieces * 6];
            int t = 0;

            for (int i = 0; i < OutlinePoints; i++) {
                // A wall faces into the opening: the opposite way from
                // the way its outline point faces out of it.
                Vector3 inward = -(Vector3)_outlineNormals[i];
                normals[i] = inward;
                normals[OutlinePoints + i] = inward;

                if (IsRunEnd(i)) {
                    continue;
                }

                // The piece between this point and the next: two
                // triangles. Front ring points are numbered from 0, back
                // ring points from OutlinePoints. The order of each three
                // decides which side is drawn - this order shows the
                // inside.
                int next = i + 1;

                triangles[t] = i;
                triangles[t + 1] = next;
                triangles[t + 2] = OutlinePoints + i;

                triangles[t + 3] = next;
                triangles[t + 4] = OutlinePoints + next;
                triangles[t + 5] = OutlinePoints + i;
                t += 6;
            }

            _wallMesh = new Mesh { name = "Keyhole Walls" };

            // The points move often: tells Unity to keep them where
            // they're cheap to rewrite.
            _wallMesh.MarkDynamic();
            _wallMesh.vertices = _wallVertices;
            _wallMesh.normals = normals;
            _wallMesh.triangles = triangles;

            // The space the walls can ever fill, given once, so it never
            // needs working out again as they move: two radii wide, four
            // tall, centred on this object.
            float largest = Mathf.Max(restRadius, openRadius);
            _wallMesh.bounds = new Bounds(Vector3.zero, new Vector3(largest * 2f, largest * 4f, _halfThickness * 2f));

            GameObject walls = new("Keyhole Walls") { layer = gameObject.layer };
            walls.transform.SetParent(transform, false);
            walls.AddComponent<MeshFilter>().sharedMesh = _wallMesh;

            MeshRenderer wallRenderer = walls.AddComponent<MeshRenderer>();
            wallRenderer.sharedMaterial = leafRenderer.sharedMaterial;
            wallRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wallRenderer.receiveShadows = false;

            MaterialPropertyBlock wallBlock = new();
            wallBlock.SetColor(_baseColorId, wallColor);
            wallRenderer.SetPropertyBlock(wallBlock);
        }

        /// <summary>
        /// Moves the walls' points onto the opening's outline at its
        /// current size: the radius-1 outline times the radius, once on
        /// each face of the door.
        /// </summary>
        private void UpdateWalls(float radius)
        {
            for (int i = 0; i < OutlinePoints; i++) {
                Vector2 point = _outlinePoints[i] * radius;

                _wallVertices[i] = new Vector3(point.x, point.y, _halfThickness);
                _wallVertices[OutlinePoints + i] = new Vector3(point.x, point.y, -_halfThickness);
            }

            // The bounds were set once in BuildWalls(): no need to measure
            // them again every time.
            _wallMesh.SetVertices(_wallVertices, 0, _wallVertices.Length, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
        }

        /// <summary>
        /// The keyhole the head is nearest to, among those it's close
        /// enough to for them to start opening. Null if there is none. A
        /// distance check per keyhole and no physics, but still called
        /// only a few times a second.
        /// </summary>
        public static DoorKeyhole FindInRange(Vector3 headPosition)
        {
            DoorKeyhole nearest = null;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < _all.Count; i++) {
                DoorKeyhole keyhole = _all[i];

                // Squared distances: comparing them gives the same answer
                // as comparing the distances, without a square root each.
                float sqr = (keyhole.transform.position - headPosition).sqrMagnitude;

                if (sqr < keyhole.openStartDistance * keyhole.openStartDistance && sqr < nearestSqr) {
                    nearest = keyhole;
                    nearestSqr = sqr;
                }
            }

            return nearest;
        }

        /// <summary>
        /// One frame of the keyhole the head is near: opens it by how
        /// close the head is, or shuts it if the door isn't latched. False
        /// once it's back at rest with the head out of range - the caller
        /// can then stop calling.
        /// </summary>
        public bool Tick(Vector3 headPosition, float deltaTime)
        {
            float target = 0f;

            if (!_hasDoor || door.IsLatched) {
                float distance = Vector3.Distance(headPosition, transform.position);

                // 0 at the start distance, 1 at the full distance, eased
                // at both ends so it doesn't start or stop with a jerk.
                target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(openStartDistance, openFullDistance, distance));
            }

            _amount = Mathf.MoveTowards(_amount, target, openSpeed * deltaTime);
            Apply();

            return _amount > 0f || target > 0f;
        }

        /// <summary>
        /// Back to rest at once: the head has gone to another keyhole.
        /// </summary>
        public void Close()
        {
            _amount = 0f;
            Apply();
        }

        /// <summary>
        /// Gives the shader the opening's size and moves the walls to
        /// match, if the size has changed.
        /// </summary>
        private void Apply()
        {
            if (!_hasRenderer || _amount == _appliedAmount) {
                return;
            }

            _appliedAmount = _amount;

            float radius = Mathf.Lerp(restRadius, openRadius, _amount);

            Vector4 size = new(radius, 0f, 0f, 0f);

            _block.SetVector(_keyholeSizeId, size);
            leafRenderer.SetPropertyBlock(_block);

            for (int i = 0; i < _plateBlocks.Length; i++) {
                _plateBlocks[i].SetVector(_keyholeSizeId, size);
                plateRenderers[i].SetPropertyBlock(_plateBlocks[i]);
            }

            UpdateWalls(radius);
        }

        /// <summary>
        /// While selected, draws the keyhole's two sizes - see DrawDebug().
        /// Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// In the detailed view (selected, or in the headset with detail
        /// on), draws the opening's outline on both faces of the door: at
        /// rest (yellow) and fully open (green).
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!detailed) {
                return;
            }

            // Awake() hasn't run in the editor.
            BuildOutline();

            for (int side = -1; side <= 1; side += 2) {
                Vector3 centre = transform.position + transform.forward * (side * GizmoStandOff);

                lines.Color = _restColor;
                DrawOutline(lines, centre, restRadius);

                lines.Color = _openColor;
                DrawOutline(lines, centre, openRadius);
            }
        }

        /// <summary>
        /// The keyhole's outline for a circle of radius, round centre,
        /// across the door's face.
        /// </summary>
        private void DrawOutline(DebugLines lines, Vector3 centre, float radius)
        {
            Vector3 right = transform.right * radius;
            Vector3 up = transform.up * radius;

            for (int i = 0; i < OutlinePoints; i++) {
                if (IsRunEnd(i)) {
                    continue;
                }

                Vector2 from = _outlinePoints[i];
                Vector2 to = _outlinePoints[i + 1];

                lines.Line(centre + right * from.x + up * from.y, centre + right * to.x + up * to.y);
            }
        }
    }
}
