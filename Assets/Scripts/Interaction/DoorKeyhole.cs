using System.Collections.Generic;
using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A keyhole the player can look through. At rest it's a small, black
    /// hole in the door; as the head comes near, it grows (rising by
    /// openOffset) into an opening big enough to see the room behind with
    /// both eyes, the black fading out as it does, and shrinks and darkens
    /// again as the head moves away. Only while the door is shut: an open
    /// door's keyhole stays small and black. While its lock is being picked
    /// (BeginPicking()) nothing is cut at all: the keyhole is a black shape
    /// on the lock's face, hiding the picks' tips inside it.
    ///
    /// The shape is a circle with a wedge (slot) running down from it,
    /// widening towards its bottom. The circle's radius sizes all of it:
    /// the wedge's two widths and its length are in radii, so the shape
    /// keeps its proportions as it grows. The wedge starts half a radius
    /// below the circle's centre, inside the circle. The defaults are the
    /// Lock Plate prefab's keyhole: a radius wide at the top, 1.46 at the
    /// bottom, 2.9 long (its bottom 3.4 radii below the centre).
    ///
    /// The opening is really cut out of the door when it's drawn (the
    /// TeaLeaf/DoorLeaf shader, which the leaf's material must use), so
    /// the player sees through it with their own eyes: real depth, moving
    /// the head looks round the room, and nothing is drawn twice. Only the
    /// picture changes - the leaf's collider is untouched, so hands, props,
    /// light and sound are stopped by the door as before. The shader's cut
    /// has no depth, so this component also makes the opening's walls: a
    /// short tube through the door's thickness (see BuildWalls()), and the
    /// black cap across it (see BuildCap()).
    ///
    /// This object marks where the keyhole is: its position is the
    /// circle's centre (which stays put as the opening grows, so the cut
    /// grows evenly round a lock plate's own keyhole), its X runs across
    /// the door's face and its Y up it. A child of the door, so it swings
    /// with it.
    ///
    /// No Update(): Player.PlayerKeyholes finds the keyhole the head is
    /// near and calls Tick() on that one only, so a keyhole nobody is at
    /// costs nothing.
    /// </summary>
    public class DoorKeyhole : MonoBehaviour, IDebugDrawable
    {
        [Tooltip("The door the keyhole is in: the opening only grows while the door is latched shut, e.g. the Door on the parent leaf (filled in when the component is added). Optional: left empty, it opens whenever the head is near.")]
        [SerializeField] private Door door;

        [Tooltip("The door leaf's renderer, whose material must use the TeaLeaf/DoorLeaf shader: that is what cuts the opening, e.g. the door's LOD0 MeshRenderer (filled with the door's first renderer when the component is added). Required: left empty, nothing is cut and a warning is logged.")]
        [SerializeField] private MeshRenderer leafRenderer;

        [Tooltip("Anything else on the door the opening has to go through as well, such as a lock plate standing on each face. One entry = one renderer to cut, e.g. the lock.fbx on each face. Each must use a TeaLeaf/DoorLeaf material of its own. Optional: left empty, only the leaf is cut.")]
        [SerializeField] private MeshRenderer[] plateRenderers;

        [Tooltip("The lock picked through this keyhole, so the big lock's picks can find it (DoorKeyhole.ForLock()) and the keyhole goes black while it is picked, e.g. the Pickable Lock on the same object. Optional: left empty, a PickableLock on this object is used, if there is one.")]
        [SerializeField] private PickableLock pickableLock;

        [Header("Size")]

        // The circle's radius in metres: at rest, and with the head right
        // up to it. The wedge below is sized in these.
        [Tooltip("The circle's radius at rest, with the head away from the door, in metres. The wedge is sized in radii, so it scales with this. E.g. 0.008 is the Lock Plate prefab's own keyhole (1.6 by 3.5cm). Higher = a bigger keyhole at rest; it should match the lock model's keyhole, which the big lock's picks fit.")]
        [SerializeField] private float restRadius = 0.008f;
        [Tooltip("The circle's radius with the head right up to the keyhole, in metres: big enough to see the room behind with both eyes. The wedge is sized in radii, so it scales with this. E.g. 0.05 = a 10cm-wide circle. Higher = more of the room behind is seen; lower = a narrower peep.")]
        [SerializeField] private float openRadius = 0.05f;
        [Tooltip("How far the circle's centre rises (along this object's Y) when fully open, in metres; partly open, that share of it. E.g. 0.02 = the open circle sits 2cm higher than at rest. Positive raises it, negative lowers it; 0 grows the opening evenly round the lock's keyhole.")]
        [SerializeField] private float openOffset;
        [Tooltip("How much further the opening's walls reach out of each face of the door when fully open, in metres; partly open, that share of it. E.g. 0.01 = fully open, the walls reach 1cm further out of each face. Positive lengthens them, negative shortens them; 0 keeps them at Thickness.")]
        [SerializeField] private float openDepth;

        [Header("Wedge (in circle radii)")]

        // The slot below the circle, in radii, so it grows with the
        // circle. It starts half a radius below the circle's centre.
        // Defaults: the Lock Plate prefab's keyhole.
        [Tooltip("The wedge's gap width at its top, half a radius below the circle's centre (inside the circle), in circle radii. E.g. 1 (the Lock Plate's) = as wide as the circle's radius. Higher = a wider slot top, up to 1.7 (wider would start outside the circle); lower = a narrower one.")]
        [Range(0.05f, 1.7f)]
        [SerializeField] private float wedgeTopWidth = 1f;
        [Tooltip("The wedge's gap width at its bottom, in circle radii. E.g. 1.46 (the Lock Plate's) = a slot widening downwards from 1 at its top. Higher = a wider flare at the bottom; equal to Wedge Top Width = straight sides.")]
        [Range(0.05f, 4f)]
        [SerializeField] private float wedgeBottomWidth = 1.46f;
        [Tooltip("How long the wedge is, from its top (half a radius below the circle's centre) to its bottom, in circle radii. E.g. 2.9 (the Lock Plate's) = bottom 3.4 radii below the centre, 2.7cm at an 8mm radius. Higher = a longer slot; lower = a shorter one.")]
        [Range(0.6f, 8f)]
        [SerializeField] private float wedgeLength = 2.9f;

        [Header("Depth")]

        [Tooltip("How thick the door is at the keyhole, in metres: how deep the opening's walls run. 0 measures it from the leaf renderer's bounds, which is right for a plain slab; with lock plates, set it to the plates' whole depth so the walls reach their faces, e.g. 0.062 for a 5cm door with a 6mm plate on each face. Higher = longer walls; too low leaves a see-through gap at the faces.")]
        [SerializeField] private float thickness;

        [Tooltip("The colour of the opening's walls, inside the door, lit like the door itself. E.g. black (the default) stays black in any light; a dark brown shows as bare wood where a lamp shines on it.")]
        [SerializeField] private Color wallColor = Color.black;

        [Header("Opening")]

        // How far the head is from the keyhole (metres) when it starts to
        // open, and when it's fully open.
        [Tooltip("How far the head is from the keyhole when it starts to open, in metres. Further away than this the keyhole stays at rest, and nearer it grows towards Open Full Distance. E.g. 0.6 = it starts opening as the player leans within 60cm. Higher = opens from further off; lower = only when right up close.")]
        [SerializeField] private float openStartDistance = 0.6f;
        [Tooltip("How far the head is from the keyhole when it is fully open, in metres. Must be less than Open Start Distance. E.g. 0.25 = fully open with the eyes 25cm away. Higher = fully open sooner as the head nears; lower = the head must come closer.")]
        [SerializeField] private float openFullDistance = 0.25f;

        [Tooltip("The fastest the opening grows or shrinks, as a share of the whole a second: 6 is fully open in a sixth of a second. Only noticed when the door is opened with the head at the keyhole; otherwise the head's own movement is slower. Higher = snaps open and shut faster; lower = eases more slowly.")]
        [SerializeField] private float openSpeed = 6f;

        // How far the gizmo's outlines stand out from each face of the
        // door (metres), so they aren't buried in it.
        private const float GizmoStandOff = 0.002f;

        // The door's thickness the gizmo uses when there is neither a
        // thickness set nor a leaf renderer to measure.
        private const float GizmoFallbackThickness = 0.05f;

        // How far in front of the lock's face the cap stands while the
        // lock is picked (metres), so it doesn't flicker against the face.
        private const float PickingCapStandOff = 0.0005f;

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
        private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int _smoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int _bumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int _colorId = Shader.PropertyToID("_Color");

        private const string CapShaderName = "TeaLeaf/KeyholeCap";

        private static readonly int _keyholeSlotId = Shader.PropertyToID("_KeyholeSlot");

        // The material that draws the cap black. The same for every
        // keyhole, so made once (see BuildCap()).
        private static Material _capMaterial;

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

        // Where the wedge's top is, in radii from the circle's centre
        // (inside the circle), for every keyhole. Must match
        // DoorLeaf.shader's value of the same name.
        public const float SlotTop = -0.5f;

        // The Lock Plate prefab's wedge bottom, in radii from the circle's
        // centre: the default length. The big lock's picks use it for a
        // lock with no keyhole.
        public const float DefaultSlotBottom = -3.4f;

        // The outline for a circle of radius 1, and which way out of the
        // opening each point faces. The same for every size (a bigger
        // keyhole is just these times its radius), so worked out once in
        // Awake() - see BuildOutline() - and in the editor on each gizmo
        // draw, so wedge edits show at once.
        private Vector2[] _outlinePoints;
        private Vector2[] _outlineNormals;

        // The cap's mesh: the outline at radius 1, filled (see BuildCap()).
        private Mesh _capMesh;

        // This leaf's own values for the shader, laid over its material's
        // (so the material itself is shared and never changed).
        private MaterialPropertyBlock _block;

        // The same for each plate: the keyhole's place is different in
        // each object's own space, so each needs its own.
        private MaterialPropertyBlock[] _plateBlocks;

        private bool _hasDoor;
        private bool _hasRenderer;

        // While the lock is picked, and for a moment after (the picks
        // burning away): nothing cut, the cap black on the lock's face on
        // the picker's side, _capFaceZ along the walls' Z. Ends on the
        // first Tick() after _pickedUntil (Time.time).
        private bool _isPicked;
        private bool _isPicking;
        private float _pickedUntil;
        private float _capFaceZ;

        // The cap's renderer and its colour (alpha = how dark), set only
        // when the opening changes.
        private MeshRenderer _capRenderer;
        private MaterialPropertyBlock _capBlock;

        // The opening's walls: a short tube through the door, made once
        // in Awake(). Its points are moved when the opening changes size
        // (the same array each time, so nothing is allocated).
        private Mesh _wallMesh;
        private Vector3[] _wallVertices;
        private float _halfThickness;
        private Transform _walls;

        // How far open it is, 0 (rest) to 1, and the value the shader was
        // last given - it's only told when that changes.
        private float _amount;
        private float _appliedAmount = -1f;

        /// The circle's radius at rest, in metres: the lock's own keyhole,
        /// which the big lock's picks are fitted to.
        public float RestRadius => restRadius;

        /// How far below the circle's centre the wedge's middle is at rest,
        /// in metres (negative): where the big lock's left pick goes.
        public float RestSlotMiddle => (SlotTop + SlotBottom) * 0.5f * restRadius;

        // The wedge, from the circle's centre, in radii: its bottom, and
        // how far either side of the middle line its top and bottom reach.
        private float SlotBottom => SlotTop - wedgeLength;
        private float SlotTopHalfWidth => wedgeTopWidth * 0.5f;
        private float SlotBottomHalfWidth => wedgeBottomWidth * 0.5f;

        /// <summary>
        /// The enabled keyhole the lock is picked through, or null. A
        /// search of the few keyholes, once per lock picked.
        /// </summary>
        public static DoorKeyhole ForLock(PickableLock target)
        {
            for (int i = 0; i < _all.Count; i++) {
                if (_all[i].pickableLock == target) {
                    return _all[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Editor-only: runs when the component is added. Finds the door
        /// above it, the door's first renderer and the door's pickable lock.
        /// </summary>
        private void Reset()
        {
            door = GetComponentInParent<Door>();

            if (door != null) {
                leafRenderer = door.GetComponentInChildren<MeshRenderer>();
                pickableLock = door.GetComponentInChildren<PickableLock>();
            }
        }

        private void Awake()
        {
            _hasDoor = door != null;
            _hasRenderer = leafRenderer != null;

            if (pickableLock == null) {
                pickableLock = GetComponent<PickableLock>();
            }

            if (!_hasRenderer) {
                Debug.LogWarning($"DoorKeyhole '{name}': no leaf renderer assigned, so there is nothing to cut an opening in.", this);
                return;
            }

            BuildOutline();

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

            _halfThickness = GetThickness() * 0.5f;
            BuildWalls();
            BuildCap();

            Apply();
        }

        /// <summary>
        /// The keyhole's place as the shader needs it for one object the
        /// opening is cut in (the leaf, or a lock plate): in that object's
        /// own space, since that's what a pixel knows about itself. Also
        /// the wedge's shape, which never changes in play.
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

            // In radii: top half width, bottom half width, bottom.
            block.SetVector(_keyholeSlotId, new Vector4(SlotTopHalfWidth, SlotBottomHalfWidth, SlotBottom, 0f));
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

            if (_capMesh != null) {
                Destroy(_capMesh);
            }
        }

        /// <summary>
        /// How deep the opening's walls run, in metres: thickness if set,
        /// else measured from the leaf renderer. Used by Awake() and the
        /// gizmo, so both agree.
        /// </summary>
        private float GetThickness()
        {
            if (thickness > 0f) {
                return thickness;
            }

            return leafRenderer != null ? MeasureThickness() : GizmoFallbackThickness;
        }

        /// <summary>
        /// How thick the leaf is along the way through the keyhole, from
        /// the leaf mesh's bounding box: the box's half size along each of
        /// its own axes (in metres), each counted by how much that axis
        /// points through the door. For a slab square to the keyhole
        /// that's simply its thickness.
        /// </summary>
        private float MeasureThickness()
        {
            Transform leaf = leafRenderer.transform;
            Vector3 through = Quaternion.Inverse(leaf.rotation) * transform.forward;
            Vector3 extents = Vector3.Scale(leafRenderer.localBounds.extents, leaf.lossyScale);

            return 2f * (Mathf.Abs(through.x * extents.x) + Mathf.Abs(through.y * extents.y) + Mathf.Abs(through.z * extents.z));
        }

        /// <summary>
        /// Works out this keyhole's outline for a circle of radius 1 - the
        /// same shape the shader cuts (see KeyholeDistance() in
        /// DoorLeaf.shader). Measured from the circle's centre; the slot
        /// as SlotTop, SlotBottom and its half widths say. Fills the same
        /// arrays each time, so redrawing the gizmo allocates nothing.
        ///
        /// The points go round anticlockwise as seen from the front. The
        /// arc starts where the slot's right side leaves the circle (a
        /// little under 60 degrees below level for the Lock Plate's
        /// wedge), and runs over the top to where the left side leaves it
        /// (the same place mirrored); then down the slot's left side,
        /// along its bottom and up its right side.
        /// </summary>
        private void BuildOutline()
        {
            if (_outlinePoints == null) {
                _outlinePoints = new Vector2[OutlinePoints];
                _outlineNormals = new Vector2[OutlinePoints];
            }

            Vector2 bottomRight = new(SlotBottomHalfWidth, SlotBottom);
            Vector2 bottomLeft = new(-SlotBottomHalfWidth, SlotBottom);
            Vector2 rightMeeting = CircleExit(new Vector2(SlotTopHalfWidth, SlotTop), bottomRight);
            Vector2 leftMeeting = new(-rightMeeting.x, rightMeeting.y);

            // The arc runs from the right meeting point's angle round to
            // its mirror, which is 180 degrees less it.
            float startAngle = Mathf.Atan2(rightMeeting.y, rightMeeting.x);
            float arcAngle = Mathf.PI - 2f * startAngle;

            for (int i = 0; i < ArcPoints; i++) {
                float angle = startAngle + arcAngle * i / ArcSegments;
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));

                // On a circle, a point faces straight out from the centre.
                _outlinePoints[i] = direction;
                _outlineNormals[i] = direction;
            }

            // The last arc point is worked out by angle: put it exactly on
            // the left meeting point so the walls join without a crack.
            _outlinePoints[ArcSegments] = leftMeeting;

            int index = ArcPoints;
            AddRun(ref index, leftMeeting, bottomLeft);
            AddRun(ref index, bottomLeft, bottomRight);
            AddRun(ref index, bottomRight, rightMeeting);
        }

        /// <summary>
        /// Where a straight line from a point inside the radius-1 circle
        /// to a point outside it crosses the circle. The point at a share
        /// t of the way along is inside + t × (outside - inside); it's on
        /// the circle when its length is 1, which is a quadratic in t
        /// (a t² + 2b t + c = 0) with one answer between 0 and 1.
        /// </summary>
        private static Vector2 CircleExit(Vector2 inside, Vector2 outside)
        {
            Vector2 along = outside - inside;
            float a = Vector2.Dot(along, along);
            float b = Vector2.Dot(inside, along);
            float c = Vector2.Dot(inside, inside) - 1f;

            float t = (-b + Mathf.Sqrt(b * b - a * c)) / a;
            return inside + along * t;
        }

        /// <summary>
        /// Adds one straight side of the slot to the outline: its two ends,
        /// both facing out of the opening. The runs go anticlockwise, so
        /// "out" is the run's direction turned a quarter clockwise.
        /// </summary>
        private void AddRun(ref int index, Vector2 from, Vector2 to)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 normal = new(direction.y, -direction.x);

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
        /// keyhole values of its own) in wallColor. The child undoes any
        /// scale from above, so the mesh is in metres.
        /// </summary>
        private void BuildWalls()
        {
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
            // needs working out again as they move: as wide as the circle
            // or the wedge's bottom, whichever is wider, from the circle's
            // top to the wedge's bottom, raised by as much as the opening
            // ever rises (or lowered).
            float largest = Mathf.Max(restRadius, openRadius);
            float halfWidth = Mathf.Max(1f, SlotBottomHalfWidth) * largest;
            float halfDepth = _halfThickness + Mathf.Max(0f, openDepth);
            Vector3 centre = new(0f, (1f + SlotBottom) * 0.5f * largest + openOffset * 0.5f, 0f);
            _wallMesh.bounds = new Bounds(centre, new Vector3(halfWidth * 2f, (1f - SlotBottom) * largest + Mathf.Abs(openOffset), halfDepth * 2f));

            GameObject walls = new("Keyhole Walls") { layer = gameObject.layer };
            walls.transform.SetParent(transform, false);

            // A door model may be scaled (ShabbyDoor's root is). The walls
            // undo whatever scale this object has from above, so their
            // mesh stays in metres like the shader's cut and the gizmo.
            Vector3 scale = transform.lossyScale;
            walls.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            walls.AddComponent<MeshFilter>().sharedMesh = _wallMesh;

            MeshRenderer wallRenderer = walls.AddComponent<MeshRenderer>();
            wallRenderer.sharedMaterial = leafRenderer.sharedMaterial;
            wallRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wallRenderer.receiveShadows = false;

            // The walls have no texture coordinates, so the leaf's texture
            // is swapped for plain white: they're just wallColor. Matt, and
            // the normal map flattened (its strength 0).
            MaterialPropertyBlock wallBlock = new();
            wallBlock.SetColor(_baseColorId, wallColor);
            wallBlock.SetTexture(_baseMapId, Texture2D.whiteTexture);
            wallBlock.SetFloat(_smoothnessId, 0f);
            wallBlock.SetFloat(_bumpScaleId, 0f);
            wallRenderer.SetPropertyBlock(wallBlock);

            _walls = walls.transform;
        }

        /// <summary>
        /// Makes the cap: the opening's shape, filled, standing across it
        /// halfway through the door and drawn black (TeaLeaf/KeyholeCap).
        /// It hides the room behind at rest and fades out as the keyhole
        /// opens (Apply()), so the player sees through only when looking
        /// into it. A child of the walls, so it has their undone scale; it
        /// is sized by its own scale, never by moving its points.
        ///
        /// The mesh is the outline at radius 1: a fan of triangles from
        /// the circle's centre to each pair of neighbouring outline
        /// points. That fills the shape because every point of its outline
        /// can be seen from the centre in a straight line (the wedge
        /// starts inside the circle).
        /// </summary>
        private void BuildCap()
        {
            Vector3[] vertices = new Vector3[OutlinePoints + 1];
            int[] triangles = new int[OutlinePoints * 3];

            for (int i = 0; i < OutlinePoints; i++) {
                vertices[i + 1] = _outlinePoints[i];

                // The last piece closes the fan back to the first point.
                int next = (i + 1) % OutlinePoints;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = next + 1;
            }

            _capMesh = new Mesh { name = "Keyhole Cap", vertices = vertices, triangles = triangles };

            if (_capMaterial == null) {
                Shader shader = Shader.Find(CapShaderName);

                if (shader == null) {
                    Debug.LogError($"DoorKeyhole: shader '{CapShaderName}' not found - keyholes have no dark cap.");
                    return;
                }

                _capMaterial = new Material(shader) { name = "Keyhole Cap" };
            }

            GameObject cap = new("Keyhole Cap") { layer = gameObject.layer };
            cap.transform.SetParent(_walls, false);
            cap.AddComponent<MeshFilter>().sharedMesh = _capMesh;

            _capRenderer = cap.AddComponent<MeshRenderer>();
            _capRenderer.sharedMaterial = _capMaterial;
            _capRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _capRenderer.receiveShadows = false;
            _capBlock = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Moves the walls' points onto the opening's outline at its
        /// current size: the radius-1 outline times the radius, raised by
        /// rise, once on each face of the door, reaching extra further out
        /// of each.
        /// </summary>
        private void UpdateWalls(float radius, float rise, float extra)
        {
            float halfDepth = _halfThickness + extra;

            for (int i = 0; i < OutlinePoints; i++) {
                Vector2 point = _outlinePoints[i] * radius;
                point.y += rise;

                _wallVertices[i] = new Vector3(point.x, point.y, halfDepth);
                _wallVertices[OutlinePoints + i] = new Vector3(point.x, point.y, -halfDepth);
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
            // While its lock is picked the keyhole stays shut and black on
            // the face (BeginPicking()). Once picking has ended it is kept
            // ticking until the picks have burnt away, then opens as usual.
            if (_isPicked) {
                if (_isPicking || Time.time < _pickedUntil) {
                    return !_isPicking;
                }

                _isPicked = false;
                _appliedAmount = -1f;
            }

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
        /// The lock is being picked from the face at facePoint: shuts the
        /// keyhole at once and cuts nothing, the cap drawn black just in
        /// front of that face. Depth tested, so it hides the parts of the
        /// picks inside the keyhole and none of what sticks out.
        /// </summary>
        public void BeginPicking(Vector3 facePoint)
        {
            if (!_hasRenderer) {
                return;
            }

            _isPicked = true;
            _isPicking = true;
            _amount = 0f;

            // The face in the walls' space (metres, Z through the door),
            // nudged a little further out on the same side.
            float faceZ = _walls.InverseTransformPoint(facePoint).z;
            _capFaceZ = faceZ + Mathf.Sign(faceZ) * PickingCapStandOff;

            _appliedAmount = -1f;
            Apply();
        }

        /// <summary>
        /// The picking has ended. The keyhole stays shut on the face for
        /// holdTime seconds more (the picks burning away in it), then goes
        /// back to opening as the head comes near, on the next Tick().
        /// </summary>
        public void EndPicking(float holdTime)
        {
            _isPicking = false;
            _pickedUntil = Time.time + holdTime;
        }

        /// <summary>
        /// Gives the shader the opening's size and rise, moves the walls
        /// and the cap to match and fades the cap, if the opening has
        /// changed.
        /// </summary>
        private void Apply()
        {
            if (!_hasRenderer || _amount == _appliedAmount) {
                return;
            }

            _appliedAmount = _amount;

            float radius = Mathf.Lerp(restRadius, openRadius, _amount);
            float rise = openOffset * _amount;

            // Picked: radius 0, which the shader takes as no opening.
            Vector4 size = new(_isPicked ? 0f : radius, rise, 0f, 0f);

            _block.SetVector(_keyholeSizeId, size);
            leafRenderer.SetPropertyBlock(_block);

            for (int i = 0; i < _plateBlocks.Length; i++) {
                _plateBlocks[i].SetVector(_keyholeSizeId, size);
                plateRenderers[i].SetPropertyBlock(_plateBlocks[i]);
            }

            UpdateWalls(radius, rise, openDepth * _amount);

            if (_capRenderer != null) {
                // Black at rest, clear when fully open (and then not drawn
                // at all). Halfway through the door, or on the lock's face
                // while picked.
                Transform cap = _capRenderer.transform;
                cap.localPosition = new Vector3(0f, rise, _isPicked ? _capFaceZ : 0f);
                cap.localScale = new Vector3(radius, radius, 1f);

                _capBlock.SetColor(_colorId, new Color(0f, 0f, 0f, 1f - _amount));
                _capRenderer.SetPropertyBlock(_capBlock);
                _capRenderer.enabled = _amount < 1f;
            }
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
        /// on), draws the opening's outline on both faces of the door
        /// (thickness apart, where the walls end): at rest (yellow) and
        /// fully open (green, raised by openOffset and openDepth further
        /// out of each face).
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!detailed) {
                return;
            }

            // Out of play, worked out each draw so wedge edits show at
            // once. In play, the shape Awake() gave the cut and walls,
            // which don't change (if Awake() made one).
            if (!Application.isPlaying || _outlinePoints == null) {
                BuildOutline();
            }

            // Worked out each draw, so editing thickness shows at once.
            float offset = GetThickness() * 0.5f + GizmoStandOff;

            for (int side = -1; side <= 1; side += 2) {
                Vector3 centre = transform.position + transform.forward * (side * offset);

                lines.Color = _restColor;
                DrawOutline(lines, centre, restRadius);

                // Fully open: raised, and out where the walls then end.
                lines.Color = _openColor;
                DrawOutline(lines, centre + transform.up * openOffset + transform.forward * (side * openDepth), openRadius);
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
