using Core;
using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The compass: a disc worn on the back of the hand, always there to
    /// be looked at, like a watch. It is on the hand without the
    /// lockpicks (the right for a right-handed player), in the same place
    /// on that hand as the picks are on theirs. It takes no input and
    /// never makes its hand busy.
    ///
    /// The model is a prefab (Compass, CompassGem), made once at load.
    /// It shows three things:
    /// - North. The housing stays put on the hand; the dial inside it
    ///   stays turned to the world, the red point of its star at north.
    /// - How well lit the player is: the gem in the middle, dark in
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
    ///
    /// The compass is not a child of the hand visual: it is placed on it
    /// each frame, where its worn profile says (posed with Pose with
    /// Hand). The right hand visual is the left one mirrored, and a child
    /// of it would be drawn mirrored too - east and west swapped.
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// Tick() after the hand visuals are placed.
    /// </summary>
    public class PlayerCompass : MonoBehaviour
    {
        [Tooltip("The PlayerInputXR on the Player root: its Left Handed setting picks the hand the compass is worn on (off = right hand), and its object is the Player root whose own sounds are never shown. Empty finds it on a parent at load.")]
        [SerializeField] private PlayerInputXR playerInput;
        [Tooltip("The PlayerHandVisuals on this Hands object: the hand visual the compass is placed on each frame. Empty finds it on this object at load.")]
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // How well lit the player is, for the gem. Optional: without it
        // the gem stays dark.
        [Tooltip("The PlayerVisibility on the Player root: how well lit the player is, shown by the light gem. Empty finds it on a parent at load; none = the gem stays dark.")]
        [SerializeField] private PlayerVisibility playerVisibility;

        [Header("Where It Sits")]

        // The hand's offset from the compass's middle, per hand, as for a
        // held thing (HandSnapProfile.GripFrameInHand()): posed on the
        // gauntlet's back plate with TeaLeaf > Player > Pose with Hand.
        // Only the offsets are used; the hand keeps its input curl.
        [Tooltip("Where the compass sits on the back of the wearing hand: the hand's offset from the compass's middle, per hand (posed with TeaLeaf > Player > Pose with Hand, Compass (on the back of the hand)); its finger pose isn't played. e.g. CompassWorn. Empty = the compass sits on the hand visual's origin.")]
        [SerializeField] private HandSnapProfile wornProfile;

        [Header("Model")]

        // Made once at load, under this object. Its north, size and gem
        // colours are set on the prefab.
        [Tooltip("The compass prefab worn on the hand (a Compass component on its root: housing, turning dial, light gem), made once at load. e.g. Assets/Prefabs/Core/Compass.prefab. Empty = no compass (an error is logged).")]
        [SerializeField] private Compass compassPrefab;

        [Header("Sound Arrows")]

        // Off: no arrows are made, no sounds listened for, nothing placed
        // per frame. Read at load.
        [Tooltip("Show arrows round the compass for the footsteps and voices just heard. On = an arrow per recent sound, pointing where it came from; off = none are made and no sounds are listened for. Read at load.")]
        [SerializeField] private bool showSoundArrows;

        // How many sounds can show at once. With all in use, a new sound
        // takes the faintest one's place if it's louder.
        [Tooltip("How many sound arrows can show at once, e.g. 4. With all in use, a new sound takes the faintest one's place if it's louder. Made once at load.")]
        [SerializeField] private int arrowCount = 4;

        // Seconds an arrow shows at full strength, then how long it
        // takes to fade away.
        [Tooltip("Seconds an arrow shows at full strength after its sound, e.g. 0.5, before it starts to fade.")]
        [SerializeField] private float arrowHoldTime = 0.5f;
        [Tooltip("Seconds an arrow takes to fade away after Arrow Hold Time, e.g. 2. Longer keeps a sound's direction on show for longer.")]
        [SerializeField] private float arrowFadeTime = 2f;

        // An arrow's size (middle to corner, metres) for the faintest
        // sound and for the loudest.
        [Tooltip("An arrow's size (middle to corner) for the faintest sound shown, in metres, e.g. 0.008 = 8mm; it grows towards Arrow Max Size with loudness.")]
        [SerializeField] private float arrowMinSize = 0.008f;
        [Tooltip("An arrow's size (middle to corner) for a sound at full loudness, in metres, e.g. 0.024 = 2.4cm.")]
        [SerializeField] private float arrowMaxSize = 0.024f;

        // Sounds quieter than this at the ears (0-1) aren't shown.
        [Tooltip("Footsteps and voices quieter than this at the ears (0-1) get no arrow, e.g. 0.02. Raise it to show only nearer, louder sounds; 0 = every one.")]
        [SerializeField] private float minLoudness = 0.02f;

        // The gap between the rim and an arrow, in metres.
        [Tooltip("The gap between the rim and an arrow, in metres, e.g. 0.006 = 6mm.")]
        [SerializeField] private float arrowGap = 0.006f;

        // A sound within this many metres of one already showing is
        // taken as the same thing sounding again (a guard's next
        // footstep): its arrow is moved and renewed, rather than a
        // second one shown.
        [Tooltip("A sound heard within this many metres of one already showing counts as the same thing again (e.g. a guard's next footstep), so its arrow is moved and renewed instead of a second shown, e.g. 1.5. 0 = every sound gets its own arrow.")]
        [SerializeField] private float sameSourceDistance = 1.5f;

        [Tooltip("How thick the sound arrows are (how far they stand off the hand), in metres, e.g. 0.004 = 4mm.")]
        [SerializeField] private float arrowDepth = 0.004f;

        [Tooltip("Colour of the sound arrows, e.g. white; they fade out through their own transparency. Set at load.")]
        [SerializeField] private Color arrowColor = new(1f, 1f, 1f, 1f);

        private static readonly int _alphaId = Shader.PropertyToID("_Alpha");

        private bool _hasVisibility;
        private bool _hasCompass;
        private bool _hasGem;

        // True once the arrows are made (showSoundArrows on, with a
        // compass to put them round).
        private bool _hasArrows;

        // The hand that wears it: the one the lockpicks aren't on.
        private bool _isOnLeftHand;

        // The player's root: a sound made by anything under it is the
        // player's own.
        private Transform _playerRoot;

        // The sound player listened to, kept so the same one is
        // unsubscribed from.
        private SoundPlayer _soundPlayer;

        // The compass made from the prefab, and its transform (the
        // housing's: it rides the hand).
        private Compass _model;
        private Transform _compass;
        private Mesh _arrowMesh;

        // The arrows. For each: its object, its renderer and its own
        // material (so each fades by itself); where its sound came from,
        // how loud it was and when it was heard (-1 = not in use).
        private Transform[] _arrows;
        private MeshRenderer[] _arrowRenderers;
        private Material[] _arrowMaterials;
        private Vector3[] _arrowPoints;
        private float[] _arrowLoudness;
        private float[] _arrowTimes;

        /// <summary>
        /// Editor-only: fills in the references when the component is
        /// added (on the Hands object; the body systems are on the Player
        /// root above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerVisibility = GetComponentInParent<PlayerVisibility>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
        }

        private void Awake()
        {
            if (playerInput == null) {
                playerInput = GetComponentInParent<PlayerInputXR>();
            }

            if (playerHandVisuals == null) {
                playerHandVisuals = GetComponent<PlayerHandVisuals>();
            }

            if (playerVisibility == null) {
                playerVisibility = GetComponentInParent<PlayerVisibility>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasVisibility = playerVisibility != null;
            _playerRoot = playerInput.transform;

            // A right-handed player wears the lockpicks on the left hand,
            // and so the compass on the right; a left-handed one the
            // other way round.
            _isOnLeftHand = playerInput.IsLeftHanded;

            if (compassPrefab == null) {
                Debug.LogError($"PlayerCompass '{name}': no Compass Prefab assigned, so no compass is worn.", this);
                return;
            }

            MakeCompass();

            if (showSoundArrows) {
                BuildArrows();
                _hasArrows = true;
            }
        }

        /// <summary>
        /// Starts listening for sounds. Start() rather than Awake():
        /// SoundPlayer sets Instance in its own Awake(), and every Awake()
        /// has run before the first Start(). A scene with no sound player
        /// simply shows no arrows; with the arrows off, nothing listens.
        /// </summary>
        private void Start()
        {
            if (!_hasArrows) {
                return;
            }

            _soundPlayer = SoundPlayer.Instance;

            if (_soundPlayer != null) {
                _soundPlayer.Heard += OnSoundHeard;
            }
        }

        private void OnEnable()
        {
            // Awake() has always run by now, so the compass exists unless
            // there was no prefab.
            if (_hasCompass) {
                _compass.gameObject.SetActive(true);
            }
        }

        private void OnDisable()
        {
            // Null if Awake() hasn't run (or there was no prefab). And if
            // the scene is being unloaded (a level restart), everything
            // here is on its way out and mustn't be touched.
            if (_compass == null || !gameObject.scene.isLoaded) {
                return;
            }

            // Nothing places it while this is off, so it isn't shown.
            _compass.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_soundPlayer != null) {
                _soundPlayer.Heard -= OnSoundHeard;
            }

            // Meshes and materials made from code aren't cleaned up with
            // their objects. (The compass's own are the prefab's assets.)
            DestroyAsset(_arrowMesh);

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
        /// Makes the compass from its prefab, once, under this object
        /// (load time: nothing is made during play). Its axes are the
        /// Compass's: Z into the hand, Y up the face. No colliders: it's
        /// only to look at.
        /// </summary>
        private void MakeCompass()
        {
            _model = Instantiate(compassPrefab, transform, false);
            _model.name = "Compass";
            _compass = _model.transform;
            _hasCompass = true;
            _hasGem = _model.Gem != null;
        }

        /// <summary>
        /// Makes the pool of sound arrows, once: each a small triangular
        /// prism pointing along its own +Y, a metre from middle to corner
        /// and a metre deep (scaled to size when shown), with a
        /// see-through material of its own so each fades by itself.
        /// Children of the compass, all hidden until a sound is heard.
        /// </summary>
        private void BuildArrows()
        {
            int count = Mathf.Max(arrowCount, 1);

            LockMeshBuilder builder = new();
            builder.Cylinder(1f, 1f, 3, arrowColor, arrowColor, Vector3.zero, 90f);
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
        /// from - the portal, for one from another room. Nothing here
        /// touches anything drawn: Tick() places the arrows.
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
        /// Places the compass on the back of the hand, turns the dial to
        /// north, colours the gem and places the sound arrows. Called by
        /// PlayerController after the hand visuals are placed, on every
        /// path through Update().
        /// </summary>
        public void Tick()
        {
            if (!_hasCompass) {
                return;
            }

            // The housing rides the hand where the worn profile puts it:
            // the profile holds the hand's offset from the compass, so
            // GripFrameInHand() works back from the hand to the compass.
            // It uses the visual's position and rotation, not its scale,
            // so the compass is never drawn mirrored on the right hand -
            // each hand has its own offsets.
            Transform visual = _isOnLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;
            HandSnapProfile.GripFrameInHand(wornProfile, _isOnLeftHand, visual.position, visual.rotation, out Vector3 position, out Quaternion rotation);
            _compass.SetPositionAndRotation(position, rotation);

            // The way into the hand (the compass's own Z), for someone
            // looking at its face.
            Vector3 palm = rotation * Vector3.forward;

            // The dial stays turned to the world, its top at north.
            _model.TurnToNorth(palm);

            // The gem shows how well lit the player is (it writes its
            // colour only when the reading has changed enough to see).
            if (_hasGem) {
                _model.Gem.Show(_hasVisibility ? playerVisibility.Visibility : 0f);
            }

            if (_hasArrows) {
                TickArrows(position, palm);
            }
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

                arrow.SetPositionAndRotation(position + direction * (_model.Radius + arrowGap + size), Quaternion.LookRotation(palm, direction));
                arrow.localScale = new Vector3(size, size, arrowDepth);
                _arrowMaterials[i].SetFloat(_alphaId, strength);
            }
        }
    }
}
