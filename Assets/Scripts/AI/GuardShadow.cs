using Core;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// Soft shadows on the ground under a guard, so it stands on the floor
    /// rather than floating over it. The level's lighting is all baked -
    /// the moon included - so nothing moving casts a real shadow except
    /// near a Mixed lamp; these stand in everywhere.
    ///
    /// Three flat quads drawn with TeaLeaf/BlobShadow (a dark patch
    /// multiplied onto the ground, no texture): one under the body and one
    /// under each foot. A foot's patch is long and narrow along the foot,
    /// darkest while the foot is planted and fading as it lifts, so each
    /// step is seen to touch the ground. The body's patch sits under the
    /// hips; while the guard stands in moonlight it's stretched away from
    /// the moon (longer the lower the moon) and thins towards its far end,
    /// the way the baked shadows of the buildings round it fall. Out of
    /// the moon - indoors, in a building's shadow - it eases back to round.
    ///
    /// Whether the moon reaches the guard is the same question SceneLight
    /// asks: one ray towards the moon, from chest height. With it, one ray
    /// down finds the real floor under the guard (the NavMesh the root
    /// stands on can sit a few centimetres off it), and four more down at
    /// the corners of the body patch fit a plane to the ground, which the
    /// patches are tilted to lie on (eased, so a kerb doesn't snap them).
    /// All a few times a second, staggered between guards, never every
    /// frame; between checks the floor is kept as a plane through a height
    /// relative to the root, so it follows the guard up a slope.
    ///
    /// Runs in LateUpdate after the Animator, GuardFootLock and the
    /// ragdoll's stand-up have placed the bones (DefaultExecutionOrder),
    /// only while the model is drawn. Hidden while the guard lies limp: a
    /// ragdoll on the floor touches it for real.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Guard))]
    public class GuardShadow : MonoBehaviour
    {
        // The bones the patches follow, found under the guard by name if
        // not assigned.
        [Tooltip("Hips bone: the body's patch is centred under it. Found by name (Hips) if not assigned.")]
        [SerializeField] private Transform hips;
        [Tooltip("Left foot bone (the ankle). Found by name (Left_Foot) if not assigned.")]
        [SerializeField] private Transform leftFoot;
        [Tooltip("Tip of the left toes. Found by name (Left_ToesEnd) if not assigned.")]
        [SerializeField] private Transform leftToesEnd;
        [Tooltip("Right foot bone (the ankle). Found by name (Right_Foot) if not assigned.")]
        [SerializeField] private Transform rightFoot;
        [Tooltip("Tip of the right toes. Found by name (Right_ToesEnd) if not assigned.")]
        [SerializeField] private Transform rightToesEnd;

        // The model's skinned mesh: patches are moved only while it is
        // drawn. Found in the children if not assigned.
        [Tooltip("The model's skinned mesh: shadows are updated only while it is drawn. Found in the children if not assigned.")]
        [SerializeField] private Renderer bodyRenderer;

        [Header("Body")]
        // Width of the body's patch, in metres (and its length when round).
        [Tooltip("Width of the body's shadow, in metres; its length too when round.")]
        [SerializeField] private float bodyWidth = 0.8f;

        // How dark the middle of the body's patch is, 0-1.
        [Tooltip("How dark the middle of the body's shadow is, 0-1.")]
        [Range(0f, 1f)]
        [SerializeField] private float bodyStrength = 0.5f;

        [Header("Moon")]
        // How tall a thing the stretched shadow is cast by, in metres. Not
        // the guard's full height: a blurred patch as long as a real moon
        // shadow reads as a stain, so only the part near the feet is drawn.
        [Tooltip("Height of what casts the moon-stretched shadow, in metres. Less than the guard's height: only the part of the shadow near the feet is drawn.")]
        [SerializeField] private float castHeight = 0.8f;

        // The longest the stretched patch gets, however low the moon.
        [Tooltip("Longest the moon-stretched shadow gets, in metres, however low the moon.")]
        [SerializeField] private float maxLength = 2.2f;

        // How much thinner the far end of a stretched patch is, 0-1.
        [Tooltip("How much the far end of the moon-stretched shadow fades, 0-1.")]
        [Range(0f, 1f)]
        [SerializeField] private float stretchTaper = 0.7f;

        // Seconds between looks at the moon and the floor.
        [Tooltip("Seconds between checks for moonlight and the floor height (one ray each).")]
        [SerializeField] private float checkInterval = 0.25f;

        // Seconds to ease between round and stretched on entering or
        // leaving the moon's shadow.
        [Tooltip("Seconds to ease between round and stretched when the guard walks in or out of moonlight.")]
        [SerializeField] private float moonEaseTime = 0.4f;

        [Header("Feet")]
        // Width of a foot's patch, in metres.
        [Tooltip("Width of a foot's shadow, in metres.")]
        [SerializeField] private float footWidth = 0.16f;

        // A foot's patch length as a multiple of the ankle-to-toe length.
        [Tooltip("Length of a foot's shadow as a multiple of ankle-to-toe length.")]
        [SerializeField] private float footLengthScale = 1.6f;

        // How dark the middle of a planted foot's patch is, 0-1.
        [Tooltip("How dark the middle of a planted foot's shadow is, 0-1.")]
        [Range(0f, 1f)]
        [SerializeField] private float footStrength = 0.6f;

        // Ankle heights above the floor between which a foot's patch
        // fades out. The clips' planted ankles sit at 0.11-0.13m.
        [Tooltip("Ankle height above the floor (m) up to which a foot's shadow is full strength. The clips' planted ankles sit at 0.11-0.13m.")]
        [SerializeField] private float plantedHeight = 0.14f;
        [Tooltip("Ankle height above the floor (m) at which a foot's shadow has faded out.")]
        [SerializeField] private float liftedHeight = 0.35f;

        [Header("Floor")]
        // How far above the floor the patches lie, in metres.
        [Tooltip("How far above the floor the shadows lie, in metres.")]
        [SerializeField] private float floorClearance = 0.015f;

        // Furthest above or below the root to look for the floor.
        [Tooltip("Furthest above or below the guard's root the floor is looked for, in metres.")]
        [SerializeField] private float floorSearch = 0.4f;

        // Shared by every guard: one quad, one material, made once.
        private static Mesh _quad;
        private static Material _material;
        private static MaterialPropertyBlock _block;
        private static readonly int _strengthId = Shader.PropertyToID("_Strength");
        private static readonly int _taperId = Shader.PropertyToID("_Taper");

        // What blocks the moon and what's floor, as SceneLight.
        private static int _occluders;

        private Guard _guard;
        private GuardRagdoll _ragdoll;
        private bool _isValid;

        private Patch _body;
        private Patch _leftFootPatch;
        private Patch _rightFootPatch;

        // Seconds to the next check of the moon and the floor.
        private float _checkTimer;
        // Whether the last check found the moon, and how far the body's
        // patch is eased towards stretched (0 round, 1 stretched).
        private bool _isMoonlit;
        private float _moonlit;
        // The floor's height relative to the root, from the last check.
        private float _floorOffset;
        // The floor's slope: the normal of the plane best fitting the
        // ground under the guard at the last check, and the one the
        // patches are tilted to, eased towards it so a kerb or a change of
        // slope doesn't snap them over.
        private Vector3 _targetNormal = Vector3.up;
        private Vector3 _floorNormal = Vector3.up;
        // Where round the root the four slope rays go down (x, z), made
        // once from the body's width.
        private Vector3[] _slopeOffsets;
        private bool _isShown = true;

        // Worked out once from the moon: which way a shadow falls (flat,
        // away from the moon) and how long the stretched patch is.
        private Vector3 _awayFromMoon = Vector3.forward;
        private float _stretchedLength;

        /// <summary>
        /// One quad and the last values its property block was given, so
        /// the block is set only when they change.
        /// </summary>
        private class Patch
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public float Strength = -1f;
            public float Taper = -1f;
        }

        private void Awake()
        {
            _guard = GetComponent<Guard>();
            _ragdoll = GetComponent<GuardRagdoll>();

            if (hips == null) hips = FindBone("Hips");
            if (leftFoot == null) leftFoot = FindBone("Left_Foot");
            if (leftToesEnd == null) leftToesEnd = FindBone("Left_ToesEnd");
            if (rightFoot == null) rightFoot = FindBone("Right_Foot");
            if (rightToesEnd == null) rightToesEnd = FindBone("Right_ToesEnd");

            if (bodyRenderer == null) {
                bodyRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            }

            _isValid = hips != null && leftFoot != null && leftToesEnd != null
                && rightFoot != null && rightToesEnd != null && bodyRenderer != null;

            if (!_isValid) {
                Debug.LogError($"GuardShadow '{name}': bones or body renderer not found.", this);
                enabled = false;
                return;
            }

            if (_occluders == 0) {
                _occluders = LayerMask.GetMask("Environment", "Interactable");
            }

            CreateShared();

            _body = CreatePatch("Shadow Body");
            _leftFootPatch = CreatePatch("Shadow Left Foot");
            _rightFootPatch = CreatePatch("Shadow Right Foot");

            // The slope rays sit at the corners of the round body patch, so
            // the plane is fitted to the ground the patches lie on.
            float spread = bodyWidth * 0.5f;
            _slopeOffsets = new[] {
                new Vector3(-spread, 0f, -spread),
                new Vector3(spread, 0f, -spread),
                new Vector3(spread, 0f, spread),
                new Vector3(-spread, 0f, spread)
            };

            // Spread the guards' checks out, so they don't all fall on one
            // frame.
            _checkTimer = Random.Range(0f, checkInterval);
        }

        private void Start()
        {
            // The moon doesn't move (it's baked), so where shadows fall is
            // worked out once. Start, not Awake: the Moonlight registers
            // itself in its OnEnable.
            Moonlight moon = Moonlight.Instance;

            if (moon == null) {
                return;
            }

            Vector3 toMoon = moon.DirectionToMoon;
            Vector3 flat = new Vector3(-toMoon.x, 0f, -toMoon.z);

            // A moon nearly overhead casts no stretch to speak of; one at
            // or below the horizon casts none at all.
            if (toMoon.y <= 0.01f || flat.sqrMagnitude < 0.0001f) {
                _stretchedLength = bodyWidth;
                return;
            }

            // A thing castHeight tall throws a shadow castHeight / tan(the
            // moon's height in the sky) long; flat length / height of the
            // direction to the moon is 1 / tan of that.
            float shadowLength = castHeight * flat.magnitude / toMoon.y;
            _awayFromMoon = flat.normalized;
            _stretchedLength = Mathf.Min(bodyWidth + shadowLength, Mathf.Max(maxLength, bodyWidth));
        }

        private void LateUpdate()
        {
            // Limp (or knocked flat without a ragdoll): the body lies on
            // the floor for real.
            bool down = _ragdoll != null ? _ragdoll.IsLimp : _guard.IsKnockedOut;
            SetShown(!down);

            if (down || !bodyRenderer.isVisible) {
                return;
            }

            _checkTimer -= Time.deltaTime;

            if (_checkTimer <= 0f) {
                _checkTimer += checkInterval;
                Check();
            }

            // Ease between round and stretched.
            float target = _isMoonlit ? 1f : 0f;
            _moonlit = Mathf.MoveTowards(_moonlit, target, Time.deltaTime / Mathf.Max(moonEaseTime, 0.01f));

            // Ease the tilt over about one check, so each new fit is
            // blended in by the time the next arrives.
            float ease = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(checkInterval, 0.01f));
            _floorNormal = Vector3.Lerp(_floorNormal, _targetNormal, ease).normalized;

            PlaceBody();
            PlaceFoot(_leftFootPatch, leftFoot, leftToesEnd);
            PlaceFoot(_rightFootPatch, rightFoot, rightToesEnd);
        }

        /// <summary>
        /// Height of the fitted floor plane straight under a point, plus the
        /// clearance the patches lie above it. The plane passes through the
        /// floor height at the root and is tilted by the floor normal.
        /// </summary>
        private float FloorHeightAt(Vector3 point)
        {
            Vector3 root = transform.position;
            float rise = -(_floorNormal.x * (point.x - root.x) + _floorNormal.z * (point.z - root.z)) / _floorNormal.y;
            return root.y + _floorOffset + rise + floorClearance;
        }

        /// <summary>
        /// The few-times-a-second look: is the guard in moonlight, and
        /// where is the floor under it.
        /// </summary>
        private void Check()
        {
            Vector3 root = transform.position;

            Moonlight moon = Moonlight.Instance;
            _isMoonlit = moon != null
                && !Physics.Raycast(root + Vector3.up * 1.2f, moon.DirectionToMoon, moon.MaxDistance, _occluders, QueryTriggerInteraction.Ignore);

            // The floor: straight down from a little above the root. Not
            // found (over a gap, off the level) keeps the last height.
            Vector3 from = root + Vector3.up * floorSearch;

            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, floorSearch * 2f, _occluders, QueryTriggerInteraction.Ignore)) {
                return;
            }

            _floorOffset = hit.point.y - root.y;
            _targetNormal = FitFloorNormal(root, hit.normal);
        }

        /// <summary>
        /// The normal of the plane best fitting the ground round the root:
        /// four rays down at the corners of the body patch, the normal
        /// across the two diagonals between their hits (for four points,
        /// the plane closest to all of them). On a step or kerb that tilts
        /// the patches across the edge rather than following one face.
        /// A corner over a gap, or a fit steeper than the agent could walk,
        /// falls back to the face under the root.
        /// </summary>
        private Vector3 FitFloorNormal(Vector3 root, Vector3 fallback)
        {
            // Started higher than the centre ray by the spread, so the
            // uphill corners of a 45° slope still begin above the ground.
            float spread = bodyWidth * 0.5f;
            float above = floorSearch + spread;
            Vector3 c0 = Vector3.zero, c1 = Vector3.zero, c2 = Vector3.zero, c3 = Vector3.zero;

            for (int i = 0; i < 4; i++) {
                Vector3 from = root + _slopeOffsets[i] + Vector3.up * above;

                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, above * 2f, _occluders, QueryTriggerInteraction.Ignore)) {
                    return fallback.y > 0.7f ? fallback : Vector3.up;
                }

                switch (i) {
                    case 0: c0 = hit.point; break;
                    case 1: c1 = hit.point; break;
                    case 2: c2 = hit.point; break;
                    default: c3 = hit.point; break;
                }
            }

            // Corners go round anticlockwise seen from above, so diagonal
            // 1->3 crossed with 0->2 points up.
            Vector3 normal = Vector3.Cross(c3 - c1, c2 - c0).normalized;

            // 0.7 = about 45°, the agent's steepest slope.
            if (normal.y < 0.7f) {
                return fallback.y > 0.7f ? fallback : Vector3.up;
            }

            return normal;
        }

        /// <summary>
        /// The body's patch: under the hips, round, or stretched away from
        /// the moon with its near end kept under the feet.
        /// </summary>
        private void PlaceBody()
        {
            float length = Mathf.Lerp(bodyWidth, _stretchedLength, _moonlit);

            // Centre moved away from the moon by the extra length's half,
            // so the near end stays where the round patch's was.
            Vector3 centre = hips.position + _awayFromMoon * ((length - bodyWidth) * 0.5f);
            centre.y = FloorHeightAt(centre);

            _body.Transform.SetPositionAndRotation(centre, LieOnFloor(_awayFromMoon));
            _body.Transform.localScale = new Vector3(bodyWidth, 1f, length);
            SetValues(_body, bodyStrength, stretchTaper * _moonlit);
        }

        /// <summary>
        /// A foot's patch: from the ankle to the toes, laid along the
        /// foot, fading out as the foot lifts.
        /// </summary>
        private void PlaceFoot(Patch patch, Transform ankle, Transform toesEnd)
        {
            Vector3 heel = ankle.position;
            Vector3 toe = toesEnd.position;
            Vector3 along = new Vector3(toe.x - heel.x, 0f, toe.z - heel.z);
            float footLength = along.magnitude;

            if (footLength < 0.001f) {
                along = transform.forward;
                footLength = 0.2f;
            }

            Vector3 centre = (heel + toe) * 0.5f;
            centre.y = FloorHeightAt(centre);

            // Lift measured to the floor straight under the ankle, so a
            // foot planted up a slope still counts as planted.
            float lift = heel.y - FloorHeightAt(heel);
            float strength = footStrength * (1f - Mathf.InverseLerp(plantedHeight, liftedHeight, lift));

            patch.Transform.SetPositionAndRotation(centre, LieOnFloor(along));
            patch.Transform.localScale = new Vector3(footWidth, 1f, footLength * footLengthScale);
            SetValues(patch, strength, 0f);
        }

        /// <summary>
        /// A patch's rotation lying on the fitted floor plane, its length
        /// (+Z) pointing the given flat direction as seen from above.
        /// </summary>
        private Quaternion LieOnFloor(Vector3 flatDirection)
        {
            Vector3 along = Vector3.ProjectOnPlane(flatDirection, _floorNormal);
            return Quaternion.LookRotation(along, _floorNormal);
        }

        /// <summary>
        /// Gives a patch's renderer its darkness and taper, only when
        /// they've changed enough to see.
        /// </summary>
        private static void SetValues(Patch patch, float strength, float taper)
        {
            if (Mathf.Abs(patch.Strength - strength) < 0.005f && Mathf.Abs(patch.Taper - taper) < 0.005f) {
                return;
            }

            patch.Strength = strength;
            patch.Taper = taper;
            _block.SetFloat(_strengthId, strength);
            _block.SetFloat(_taperId, taper);
            patch.Renderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// Shows or hides all three patches, on change only.
        /// </summary>
        private void SetShown(bool shown)
        {
            if (shown == _isShown) {
                return;
            }

            _isShown = shown;
            _body.Renderer.enabled = shown;
            _leftFootPatch.Renderer.enabled = shown;
            _rightFootPatch.Renderer.enabled = shown;
        }

        /// <summary>
        /// A quad under the guard, drawn with the shared mesh and material:
        /// no shadows, light probes or motion vectors.
        /// </summary>
        private Patch CreatePatch(string patchName)
        {
            GameObject quad = new GameObject(patchName);
            quad.layer = gameObject.layer;
            quad.transform.SetParent(transform, false);
            quad.AddComponent<MeshFilter>().sharedMesh = _quad;

            MeshRenderer quadRenderer = quad.AddComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = _material;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            quadRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            quadRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            quadRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            quadRenderer.allowOcclusionWhenDynamic = true;

            return new Patch { Transform = quad.transform, Renderer = quadRenderer };
        }

        /// <summary>
        /// The quad and material every guard's patches share, made by the
        /// first guard to wake. The quad lies flat, 1m square, centred, its
        /// UV v running along +Z (0 at the near end, 1 at the far end).
        /// </summary>
        private static void CreateShared()
        {
            if (_block == null) {
                _block = new MaterialPropertyBlock();
            }

            if (_material == null) {
                _material = new Material(Shader.Find("TeaLeaf/BlobShadow")) {
                    name = "Guard Shadow",
                    hideFlags = HideFlags.DontSave
                };
            }

            if (_quad != null) {
                return;
            }

            _quad = new Mesh {
                name = "Guard Shadow Quad",
                hideFlags = HideFlags.DontSave,
                vertices = new[] {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f)
                },
                uv = new[] {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f)
                },
                triangles = new[] { 0, 2, 1, 2, 3, 1 }
            };
            _quad.RecalculateBounds();
            _quad.UploadMeshData(true);
        }

        /// <summary>
        /// Finds a bone under the guard by name.
        /// </summary>
        private Transform FindBone(string boneName)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);

            foreach (Transform child in children) {
                if (child.name == boneName) {
                    return child;
                }
            }

            return null;
        }
    }
}
