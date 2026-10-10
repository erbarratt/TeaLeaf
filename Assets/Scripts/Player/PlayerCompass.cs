using Core;
using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The compass: a disc that appears on an open hand. A hand that is
    /// empty, isn't pointing at anything it could grab, and has its palm
    /// to the sky brings it out by pressing grip, and it stays for as
    /// long as grip is held - like the pack. Either hand; one at a time.
    ///
    /// It shows three things:
    /// - North. The outer rim stays put on the hand; the dial inside it,
    ///   only slightly smaller, stays turned to the world, with a
    ///   triangle at each of north, east, south and west (north's is
    ///   bigger, and red).
    /// - How well lit the player is: a small gem in the middle, dark in
    ///   shadow and bright in the light (PlayerVisibility).
    /// - What the player has just heard: a small arrow outside the rim
    ///   for each of the last few sounds, pointing the way it came from,
    ///   bigger the louder it was, and fading away. Because the arrows
    ///   point at places in the world, they go round the compass as the
    ///   player turns.
    ///
    /// Only other people's footsteps and voices are shown - it is for
    /// following guards - and an arrow points where the sound is heard
    /// from: at the doorway, for one coming through from another room.
    /// Sounds are noted whether or not the compass is out, so bringing
    /// it out shows what was heard a moment ago.
    ///
    /// The compass is not a child of the hand visual: it is placed on it
    /// each frame. The right hand visual is the left one mirrored, and a
    /// child of it would be drawn mirrored too - east and west swapped.
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// Tick() (coming out) after the grab systems, and TickHeld()
    /// (placing it, and putting it away) after the hand visuals.
    /// </summary>
    public class PlayerCompass : MonoBehaviour
    {
        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerTracking playerTracking;
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // What each hand is busy with. Found (or made) in Awake() if not
        // wired (it's on this same object).
        [SerializeField] private PlayerHandState playerHandState;

        // How well lit the player is, for the gem. Optional: without it
        // the gem stays dark.
        [SerializeField] private PlayerVisibility playerVisibility;

        // Optional: a rig with no haptics still shows the compass.
        [SerializeField] private PlayerHaptics playerHaptics;

        [Header("Bringing It Out")]

        // How far the palm may be turned away from straight up, in
        // degrees, and still count as facing the sky when grip is
        // pressed.
        [SerializeField] private float palmUpAngle = 50f;

        [Header("Where It Sits")]

        // The middle of the compass in the hand visual's own space (its
        // fingers point along -Y, the palm faces +X): over the palm.
        [SerializeField] private Vector3 inHandPosition = new(0.07f, -0.09f, 0f);

        [Header("North")]

        // Which way north is, in degrees round the world's vertical from
        // its +Z axis (0 = +Z is north and +X is east, the way a level
        // is seen from above in the editor).
        [SerializeField] private float northAngle;

        [Header("Its Shape (greybox)")]

        // The rim's radius and thickness, in metres. The dial and gem
        // are sized from them.
        [SerializeField] private float radius = 0.05f;
        [SerializeField] private float thickness = 0.01f;

        // The dial's radius as a fraction of the rim's ("only slightly
        // smaller"), and the gem's.
        [SerializeField] private float dialSize = 0.88f;
        [SerializeField] private float gemSize = 0.3f;

        // How big the east, south and west triangles are (the distance
        // from a triangle's middle to its corners, as a fraction of the
        // rim's radius), and how much bigger north's is.
        [SerializeField] private float markSize = 0.14f;
        [SerializeField] private float northMarkScale = 1.6f;

        [SerializeField] private Color rimColor = new(0.45f, 0.36f, 0.2f, 1f);
        [SerializeField] private Color dialColor = new(0.85f, 0.82f, 0.72f, 1f);
        [SerializeField] private Color markColor = new(0.2f, 0.2f, 0.22f, 1f);
        [SerializeField] private Color northColor = new(0.85f, 0.15f, 0.1f, 1f);

        // The least light the compass is ever shown in, 0-1.
        [SerializeField] private float minLight = 0.5f;

        [Header("Light Gem")]

        // The gem's colour when the player is hidden and when they're in
        // plain sight; in between, a mix.
        [SerializeField] private Color gemDarkColor = new(0.05f, 0.07f, 0.12f, 1f);
        [SerializeField] private Color gemLightColor = new(1f, 0.95f, 0.6f, 1f);

        [Header("Sound Arrows")]

        // How many sounds can show at once. With all in use, a new sound
        // takes the faintest one's place if it's louder.
        [SerializeField] private int arrowCount = 4;

        // Seconds an arrow shows at full strength, then how long it
        // takes to fade away.
        [SerializeField] private float arrowHoldTime = 0.5f;
        [SerializeField] private float arrowFadeTime = 2f;

        // An arrow's size (middle to corner, metres) for the faintest
        // sound and for the loudest.
        [SerializeField] private float arrowMinSize = 0.008f;
        [SerializeField] private float arrowMaxSize = 0.024f;

        // Sounds quieter than this at the ears (0-1) aren't shown.
        [SerializeField] private float minLoudness = 0.02f;

        // The gap between the rim and an arrow, in metres.
        [SerializeField] private float arrowGap = 0.006f;

        // A sound within this many metres of one already showing is
        // taken as the same thing sounding again (a guard's next
        // footstep): its arrow is moved and renewed, rather than a
        // second one shown.
        [SerializeField] private float sameSourceDistance = 1.5f;

        [SerializeField] private Color arrowColor = new(1f, 1f, 1f, 1f);

        [Header("Haptics")]

        // The compass coming out.
        [SerializeField] private float showAmplitude = 0.25f;
        [SerializeField] private float showDuration = 0.03f;

        // How far the gem's brightness must change before its colour is
        // written again.
        private const float GemStep = 0.02f;

        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int _alphaId = Shader.PropertyToID("_Alpha");

        private bool _hasHaptics;
        private bool _hasVisibility;

        // The player's root: a sound made by anything under it is the
        // player's own.
        private Transform _playerRoot;

        // The sound player listened to, kept so the same one is
        // unsubscribed from.
        private SoundPlayer _soundPlayer;

        // The compass (the rim is on it), the dial and the gem.
        private Transform _compass;
        private Transform _dial;
        private Mesh _rimMesh;
        private Mesh _dialMesh;
        private Mesh _gemMesh;
        private Mesh _arrowMesh;
        private Material _material;
        private Material _gemMaterial;

        // The gem's brightness as last written (-1 = never).
        private float _gemShown = -1f;

        // Whether it's out, and on which hand.
        private bool _isOut;
        private bool _isOnLeftHand;

        // Grip last frame, per hand: it comes out on the frame grip is
        // pressed, not for a hand that arrives gripping.
        private bool _wasLeftGrabbing;
        private bool _wasRightGrabbing;

        // The arrows. For each: its object, its renderer and its own
        // material (so each fades by itself); where its sound came from,
        // how loud it was and when it was heard (-1 = not in use).
        private Transform[] _arrows;
        private MeshRenderer[] _arrowRenderers;
        private Material[] _arrowMaterials;
        private Vector3[] _arrowPoints;
        private float[] _arrowLoudness;
        private float[] _arrowTimes;

        /// True while that hand holds the compass out. The other hand
        /// systems leave it alone meanwhile (PlayerHandState).
        public bool IsLeftBusy => _isOut && _isOnLeftHand;
        public bool IsRightBusy => _isOut && !_isOnLeftHand;

        /// <summary>
        /// Editor-only: fills in the references when the component is
        /// added (on the Hands object; the body systems are on the Player
        /// root above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerTracking = GetComponentInParent<PlayerTracking>();
            playerVisibility = GetComponentInParent<PlayerVisibility>();
            playerHaptics = GetComponentInParent<PlayerHaptics>();
            playerHandInteraction = GetComponent<PlayerHandInteraction>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
        }

        private void Awake()
        {
            if (playerInput == null) {
                playerInput = GetComponentInParent<PlayerInputXR>();
            }

            if (playerTracking == null) {
                playerTracking = GetComponentInParent<PlayerTracking>();
            }

            if (playerHandInteraction == null) {
                playerHandInteraction = GetComponent<PlayerHandInteraction>();
            }

            if (playerHandVisuals == null) {
                playerHandVisuals = GetComponent<PlayerHandVisuals>();
            }

            if (playerHandState == null) {
                playerHandState = PlayerHandState.GetOrAdd(playerHandVisuals);
            }

            if (playerVisibility == null) {
                playerVisibility = GetComponentInParent<PlayerVisibility>();
            }

            if (playerHaptics == null) {
                playerHaptics = GetComponentInParent<PlayerHaptics>();
            }

            // Looked up once, so the per-frame code tests plain bools.
            _hasHaptics = playerHaptics != null;
            _hasVisibility = playerVisibility != null;
            _playerRoot = playerInput.transform;

            BuildCompass();
            BuildArrows();
        }

        /// <summary>
        /// Starts listening for sounds. Start() rather than Awake():
        /// SoundPlayer sets Instance in its own Awake(), and every Awake()
        /// has run before the first Start(). A scene with no sound player
        /// simply shows no arrows.
        /// </summary>
        private void Start()
        {
            _soundPlayer = SoundPlayer.Instance;

            if (_soundPlayer != null) {
                _soundPlayer.Heard += OnSoundHeard;
            }
        }

        private void OnDisable()
        {
            // Null if Awake() hasn't run. And if the scene is being
            // unloaded (a level restart), everything here is on its way
            // out and mustn't be touched.
            if (_compass == null || !gameObject.scene.isLoaded) {
                return;
            }

            PutAway();
        }

        private void OnDestroy()
        {
            if (_soundPlayer != null) {
                _soundPlayer.Heard -= OnSoundHeard;
            }

            // Meshes and materials made from code aren't cleaned up with
            // their objects.
            DestroyAsset(_rimMesh);
            DestroyAsset(_dialMesh);
            DestroyAsset(_gemMesh);
            DestroyAsset(_arrowMesh);
            DestroyAsset(_material);
            DestroyAsset(_gemMaterial);

            if (_arrowMaterials != null) {
                for (int i = 0; i < _arrowMaterials.Length; i++) {
                    DestroyAsset(_arrowMaterials[i]);
                }
            }
        }

        private static void DestroyAsset(Object asset)
        {
            if (asset != null) {
                Destroy(asset);
            }
        }

        /// <summary>
        /// Makes the compass, once, under this object and switched off.
        /// Its own axes: Z points away from whoever is looking at its
        /// face (into the palm), Y is up the face and X to the right -
        /// so on the dial, +Y is north and +X is east. Three stacked
        /// discs: the rim, the dial on top of it and the gem on top of
        /// that. No colliders: it's only to look at.
        /// </summary>
        private void BuildCompass()
        {
            _material = LockMeshBuilder.CreateMaterial(false, minLight);

            // The gem is never darkened by the light around it - it is
            // the reading - so its least light is full light. Its colour
            // is the material's, written when the reading changes.
            _gemMaterial = LockMeshBuilder.CreateMaterial(false, 1f);

            float dialRadius = radius * dialSize;
            float dialDepth = thickness * 0.4f;
            float markDepth = thickness * 0.3f;
            float gemDepth = thickness * 0.5f;

            // The rim: the largest disc, at the compass's own middle.
            LockMeshBuilder rim = new();
            rim.Cylinder(radius, thickness, 24, rimColor, rimColor);
            _rimMesh = rim.ToMesh("Compass Rim");

            // The dial: on the rim's face (towards the viewer is -Z),
            // with the four triangles standing on it near its edge. A
            // three-sided cylinder is a triangular prism, its first
            // corner along +X until it's turned: each is turned to point
            // outwards.
            float dialZ = -(thickness + dialDepth) * 0.5f;
            float markZ = dialZ - (dialDepth + markDepth) * 0.5f;
            float mark = radius * markSize;
            float northMark = mark * northMarkScale;

            LockMeshBuilder dial = new();
            dial.Cylinder(dialRadius, dialDepth, 24, dialColor, dialColor, new Vector3(0f, 0f, dialZ));
            dial.Cylinder(northMark, markDepth, 3, northColor, northColor, new Vector3(0f, dialRadius - northMark, markZ), 90f);
            dial.Cylinder(mark, markDepth, 3, markColor, markColor, new Vector3(dialRadius - mark, 0f, markZ), 0f);
            dial.Cylinder(mark, markDepth, 3, markColor, markColor, new Vector3(0f, -(dialRadius - mark), markZ), 270f);
            dial.Cylinder(mark, markDepth, 3, markColor, markColor, new Vector3(-(dialRadius - mark), 0f, markZ), 180f);
            _dialMesh = dial.ToMesh("Compass Dial");

            // The gem: a small disc on the middle of the dial.
            LockMeshBuilder gem = new();
            gem.Cylinder(radius * gemSize, gemDepth, 16, Color.white, Color.white, new Vector3(0f, 0f, dialZ - (dialDepth + gemDepth) * 0.5f));
            _gemMesh = gem.ToMesh("Compass Gem");

            _compass = MakePart("Compass", _rimMesh, transform, _material, out _).transform;
            _dial = MakePart("Dial", _dialMesh, _compass, _material, out _).transform;
            MakePart("Light Gem", _gemMesh, _dial, _gemMaterial, out _);

            _compass.gameObject.SetActive(false);
        }

        /// <summary>
        /// Makes the pool of sound arrows, once: each a small triangular
        /// prism pointing along its own +Y, a metre from middle to corner
        /// (scaled to size when shown), with a see-through material of
        /// its own so each fades by itself. Children of the compass, all
        /// hidden until a sound is heard.
        /// </summary>
        private void BuildArrows()
        {
            int count = Mathf.Max(arrowCount, 1);

            LockMeshBuilder builder = new();
            builder.Cylinder(1f, 0.4f, 3, arrowColor, arrowColor, Vector3.zero, 90f);
            _arrowMesh = builder.ToMesh("Compass Sound Arrow");

            _arrows = new Transform[count];
            _arrowRenderers = new MeshRenderer[count];
            _arrowMaterials = new Material[count];
            _arrowPoints = new Vector3[count];
            _arrowLoudness = new float[count];
            _arrowTimes = new float[count];

            for (int i = 0; i < count; i++) {
                _arrowMaterials[i] = LockMeshBuilder.CreateMaterial(true, 1f);

                GameObject arrow = MakePart($"Sound Arrow {i}", _arrowMesh, _compass, _arrowMaterials[i], out _arrowRenderers[i]);
                _arrowRenderers[i].enabled = false;
                _arrows[i] = arrow.transform;
                _arrowTimes[i] = -1f;
            }
        }

        /// <summary>
        /// One drawn piece: an object under parent with a mesh, a
        /// material and no shadows (small things that don't need them).
        /// </summary>
        private static GameObject MakePart(string partName, Mesh mesh, Transform parent, Material material, out MeshRenderer partRenderer)
        {
            GameObject part = new(partName);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterial = material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return part;
        }

        /// <summary>
        /// A sound the player has heard (SoundPlayer.Heard). Only
        /// someone else's footsteps and voices are given an arrow: the
        /// compass is for following guards, so doors, impacts, bolts and
        /// the player's own steps are left out, as is anything too faint
        /// to show. heardFrom is where the ears take the sound to come
        /// from - the portal, for one from another room. Called whenever
        /// a sound plays, whether or not the compass is out; nothing here
        /// touches anything drawn.
        /// </summary>
        private void OnSoundHeard(Vector3 heardFrom, float loudness, NoiseType type, Transform source)
        {
            if (type != NoiseType.Footstep && type != NoiseType.Voice) {
                return;
            }

            if (loudness < minLoudness || (source != null && source.IsChildOf(_playerRoot))) {
                return;
            }

            loudness = Mathf.Clamp01(loudness);

            float now = Time.time;
            float sameSqr = sameSourceDistance * sameSourceDistance;
            int chosen = -1;
            float faintest = float.MaxValue;

            for (int i = 0; i < _arrows.Length; i++) {
                float strength = Strength(i, now);

                // The same thing sounding again: its arrow is renewed.
                if (strength > 0f && (_arrowPoints[i] - heardFrom).sqrMagnitude <= sameSqr) {
                    chosen = i;
                    faintest = -1f;
                    break;
                }

                // Otherwise the faintest arrow (a free one counts as 0).
                float shown = strength * _arrowLoudness[i];

                if (shown < faintest) {
                    faintest = shown;
                    chosen = i;
                }
            }

            // Every arrow is showing something louder: this sound isn't
            // one of the nearest few.
            if (chosen < 0 || faintest >= loudness) {
                return;
            }

            _arrowPoints[chosen] = heardFrom;
            _arrowLoudness[chosen] = loudness;
            _arrowTimes[chosen] = now;
        }

        /// <summary>
        /// How strongly an arrow shows now: 1 while it holds, falling to
        /// 0 over the fade; 0 for one not in use.
        /// </summary>
        private float Strength(int index, float now)
        {
            float heardAt = _arrowTimes[index];

            if (heardAt < 0f) {
                return 0f;
            }

            float fading = now - heardAt - arrowHoldTime;

            if (fading <= 0f) {
                return 1f;
            }

            return Mathf.Clamp01(1f - fading / Mathf.Max(arrowFadeTime, 0.01f));
        }

        /// <summary>
        /// The compass coming out. Called by PlayerController after the
        /// grab systems (climbing, carrying, doors), so a hand that
        /// grabbed something this frame is already busy: a grab always
        /// wins over the compass.
        /// </summary>
        public void Tick()
        {
            bool isLeftGrabbing = playerInput.IsLeftGrabbing;
            bool isRightGrabbing = playerInput.IsRightGrabbing;

            if (!_isOut) {
                if (isLeftGrabbing && !_wasLeftGrabbing && CanShow(true)) {
                    Show(true);
                } else if (isRightGrabbing && !_wasRightGrabbing && CanShow(false)) {
                    Show(false);
                }
            }

            _wasLeftGrabbing = isLeftGrabbing;
            _wasRightGrabbing = isRightGrabbing;
        }

        /// <summary>
        /// Whether a hand may bring the compass out now: it's doing
        /// nothing else, its ray isn't on anything it could grab, and its
        /// palm faces the sky.
        /// </summary>
        private bool CanShow(bool isLeftHand)
        {
            if (playerHandState.IsBusyExcept(isLeftHand, HandUse.Compass)) {
                return false;
            }

            IHandTarget target = isLeftHand ? playerHandInteraction.LeftTarget : playerHandInteraction.RightTarget;

            if (target is not null) {
                return false;
            }

            // The dot product with straight up is the cosine of the angle
            // between the two: 1 when the palm faces the sky.
            return Vector3.Dot(Palm(isLeftHand), Vector3.up) >= Mathf.Cos(palmUpAngle * Mathf.Deg2Rad);
        }

        /// <summary>
        /// Which way a hand's palm faces, in the world: the hand visual's
        /// own +X. TransformVector(), not TransformDirection(): the right
        /// visual is the left one mirrored, and only TransformVector()
        /// takes the mirroring into account.
        /// </summary>
        private Vector3 Palm(bool isLeftHand)
        {
            Transform visual = isLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;
            return visual.TransformVector(Vector3.right).normalized;
        }

        private void Show(bool isLeftHand)
        {
            _isOut = true;
            _isOnLeftHand = isLeftHand;
            _compass.gameObject.SetActive(true);

            if (_hasHaptics) {
                playerHaptics.Pulse(isLeftHand, showAmplitude, showDuration);
            }
        }

        private void PutAway()
        {
            _isOut = false;
            _compass.gameObject.SetActive(false);
        }

        /// <summary>
        /// Puts the compass away when its hand lets go; otherwise places
        /// it on the hand, turns the dial to north, colours the gem and
        /// places the sound arrows. Called by PlayerController after the
        /// hand visuals are placed, on every path through Update().
        /// </summary>
        public void TickHeld()
        {
            if (!_isOut) {
                return;
            }

            if (!(_isOnLeftHand ? playerInput.IsLeftGrabbing : playerInput.IsRightGrabbing)) {
                PutAway();
                return;
            }

            Transform visual = _isOnLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;
            Vector3 position = visual.TransformPoint(inHandPosition);
            Vector3 palm = Palm(_isOnLeftHand);

            // The rim rides the hand: its face out of the palm (the
            // compass's own Z points into the palm), its top towards the
            // fingers (the visual's -Y).
            Vector3 fingers = Vector3.ProjectOnPlane(visual.TransformVector(Vector3.down), palm);

            if (fingers.sqrMagnitude > 0.000001f) {
                _compass.SetPositionAndRotation(position, Quaternion.LookRotation(-palm, fingers));
            } else {
                _compass.position = position;
            }

            // The dial stays turned to the world: its top is north, laid
            // flat onto the compass's face. With the face edge-on to
            // north there's no such direction, and the dial is left as it
            // was.
            Vector3 north = Vector3.ProjectOnPlane(Quaternion.Euler(0f, northAngle, 0f) * Vector3.forward, palm);

            if (north.sqrMagnitude > 0.0001f) {
                _dial.rotation = Quaternion.LookRotation(-palm, north);
            }

            TickGem();
            TickArrows(position, palm);
        }

        /// <summary>
        /// The gem's colour from how visible the player is - written only
        /// when the reading has changed enough to see.
        /// </summary>
        private void TickGem()
        {
            float visibility = _hasVisibility ? playerVisibility.Visibility : 0f;

            if (Mathf.Abs(visibility - _gemShown) < GemStep) {
                return;
            }

            _gemShown = visibility;
            _gemMaterial.SetColor(_baseColorId, Color.Lerp(gemDarkColor, gemLightColor, visibility));
        }

        /// <summary>
        /// Places each arrow in use just outside the rim, on the side its
        /// sound came from, pointing outwards; sized by how loud the
        /// sound was and faded by how long ago it was.
        /// </summary>
        private void TickArrows(Vector3 position, Vector3 palm)
        {
            float now = Time.time;

            for (int i = 0; i < _arrows.Length; i++) {
                float strength = Strength(i, now);

                // The way to the sound, level (how far above or below it
                // is isn't shown), laid flat onto the compass's face.
                Vector3 toSound = _arrowPoints[i] - position;
                toSound.y = 0f;
                Vector3 direction = Vector3.ProjectOnPlane(toSound, palm);

                bool isShown = strength > 0f && direction.sqrMagnitude > 0.0001f;
                MeshRenderer arrowRenderer = _arrowRenderers[i];

                if (arrowRenderer.enabled != isShown) {
                    arrowRenderer.enabled = isShown;
                }

                if (!isShown) {
                    // Faded right out: free for the next sound.
                    if (strength <= 0f) {
                        _arrowTimes[i] = -1f;
                    }

                    continue;
                }

                direction.Normalize();

                float size = Mathf.Lerp(arrowMinSize, arrowMaxSize, _arrowLoudness[i]);
                Transform arrow = _arrows[i];

                arrow.SetPositionAndRotation(position + direction * (radius + arrowGap + size), Quaternion.LookRotation(-palm, direction));
                arrow.localScale = new Vector3(size, size, thickness);
                _arrowMaterials[i].SetFloat(_alphaId, strength);
            }
        }
    }
}
