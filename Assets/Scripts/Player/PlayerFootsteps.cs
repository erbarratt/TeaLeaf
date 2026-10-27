using System;
using Core;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The player's footsteps and landings: the sound the player hears and
    /// the noise guards hear.
    ///
    /// A step is a distance, not a time: the horizontal distance actually
    /// moved is added up, and each time it reaches a stride a step sounds.
    /// So creeping gives slow steps and running fast ones with no timers,
    /// and pushing into a wall (no real movement) is silent.
    ///
    /// How loud a step is to guards = the surface x the stance x the speed.
    /// The surface part is the noise radius of that surface's SoundCue
    /// (SurfaceSounds); the stance part is the scale in the Gait below; and
    /// a stick pushed only part way gives quieter steps (slowestStepScale).
    ///
    /// A landing has no sound of its own: it's the surface's step sound
    /// played loud, twice, a moment apart - one foot, then the other.
    ///
    /// On a moss patch (laid by a moss arrow, PlayerArrows.IsOnMoss())
    /// steps and landings are silent: no sound, no noise.
    ///
    /// Like every player system it has no Update(): PlayerController calls
    /// Tick() straight after PlayerLocomotion.TickState(), which is what
    /// sets this frame's MovementState.
    /// </summary>
    public class PlayerFootsteps : MonoBehaviour
    {
        /// How one way of moving sounds.
        [Serializable]
        private struct Gait
        {
            [Tooltip("Metres travelled per step, e.g. 1.4 = one step sound each 1.4m walked. Longer = fewer, further-apart steps.")]
            public float stride;

            [Tooltip("Multiplies the surface cue's noise radius, what guards hear, e.g. 1.6 = heard 60% further than a walking step on the same floor; 0 = guards hear nothing.")]
            public float noiseScale;

            [Tooltip("Multiplies the surface cue's volume (0-1), what the player hears, e.g. 0.5 = half as loud. 0 = silent to the player (guards may still hear it).")]
            [Range(0f, 1f)] public float volumeScale;
        }

        [Tooltip("The PlayerLocomotion on the Player root: its Movement State picks the gait, its speeds scale the steps and its Landed event makes a landing. Required; filled in when this component is added.")]
        [SerializeField] private PlayerLocomotion playerLocomotion;
        [Tooltip("The CharacterController on the Player root: the feet are the bottom of its capsule, e.g. where each step's sound plays. Required; filled in when this component is added.")]
        [SerializeField] private CharacterController characterController;
        [Tooltip("The Player root. The feet are measured in its space, and it is the source of every footstep noise (so guards and the compass know it is the player's). Required; filled in when this component is added.")]
        [SerializeField] private Transform playerTransform;

        [Tooltip("The footstep cue for each surface type, e.g. Assets/Data/Sound/Shared/Footsteps/SurfaceFootstepSounds.asset. Empty = the player's steps and landings are silent, to the player and to guards.")]
        [SerializeField] private SurfaceSounds surfaceSounds;

        [Tooltip("What counts as floor for the ray down from the feet that finds the surface, e.g. Environment and Interactable (stone floor, a crate's wood top). A miss keeps the last surface found.")]
        [SerializeField] private LayerMask groundLayers;

        [Tooltip("The PlayerArrows on the Hands object, asked whether the feet are on a moss patch (silent steps). Empty finds it in children at load; none = no moss.")]
        [SerializeField] private PlayerArrows playerArrows;

        [Header("Steps")]
        [Tooltip("The step taken while Walking: its stride (m), and how its noise and volume scale, e.g. stride 1.4, noise 1, volume 1 = the surface cue as it is, every 1.4m.")]
        [SerializeField] private Gait walking = new() { stride = 1.4f, noiseScale = 1f, volumeScale = 1f };
        [Tooltip("The step taken while Sprinting, e.g. stride 1.6, noise 1.6 = longer strides heard 60% further than walking.")]
        [SerializeField] private Gait sprinting = new() { stride = 1.6f, noiseScale = 1.6f, volumeScale = 1f };
        [Tooltip("The step taken while Crouch Walking (crouched movement is at walking speed), e.g. stride 1.2, noise 0.4, volume 0.5 = shorter, much quieter steps.")]
        [SerializeField] private Gait crouching = new() { stride = 1.2f, noiseScale = 0.4f, volumeScale = 0.5f };

        // How loud a step is at the slowest creep, as a fraction of the
        // same step at full stick: both the noise and the volume slide
        // between this and 1 with the player's speed. Multiplies with the
        // gait's own scales, so a slow crouched creep is the quietest way
        // to move.
        [Tooltip("How loud a step at the slowest creep is, as a fraction (0-1) of the same step at full stick: noise and volume both slide between this and 1 with the player's speed, e.g. 0.4 = a creeping step is 40% as loud. 1 = speed makes no difference.")]
        [SerializeField, Range(0f, 1f)] private float slowestStepScale = 0.4f;

        [Tooltip("Crouched, a step taken slower than this fraction (0-1) of full walking speed, averaged over the stride, makes no sound and no noise, e.g. 0.3 = a crouched creep under 30% speed is silent. 0 means every crouched step sounds.")]
        [SerializeField, Range(0f, 1f)] private float silentCrouchSpeed = 0.3f;

        [Header("Landing")]

        [Tooltip("The fall speed of a standard landing, in metres per second, e.g. 3 = about what a jump on the flat lands at. A faster fall is louder and a slower drop quieter, in proportion; raise it to make every landing quieter.")]
        [SerializeField] private float referenceLandingSpeed = 3f;

        // The least and most that proportion can be, however gentle or
        // hard the landing.
        [Tooltip("The least a landing's loudness can be, as a multiple of a standard landing, however gentle the drop, e.g. 0.5 = a tiny hop still lands at half a standard landing.")]
        [SerializeField] private float minLandingScale = 0.5f;
        [Tooltip("The most a landing's loudness can be, as a multiple of a standard landing, however hard the fall, e.g. 2 = never more than twice a standard landing.")]
        [SerializeField] private float maxLandingScale = 2f;

        // A standard landing compared with a walking step on the same
        // surface: how many times as far its noise carries to guards, and
        // how many times the volume (Unity caps a voice at full volume, so
        // above that the loudness comes from the two feet overlapping).
        [Tooltip("How many times further than a walking step's noise a standard landing's noise carries to guards, e.g. 1.5 = half as far again. 0 = guards never hear landings.")]
        [SerializeField] private float landingNoiseScale = 1.5f;
        [Tooltip("How many times a walking step's volume a standard landing plays at, e.g. 1.25. Each foot's voice is capped at full volume, so above that the extra loudness comes from the two feet overlapping.")]
        [SerializeField] private float landingVolumeScale = 1.25f;

        [Tooltip("Seconds between the first foot's landing sound and the second's, e.g. 0.1. 0 = both feet at once.")]
        [SerializeField] private float secondFootDelay = 0.1f;

        // The surface ray starts this far above the feet (so it starts
        // inside the capsule, never under a floor the feet have sunk a hair
        // into) and reaches this far below them.
        private const float ProbeAbove = 0.3f;
        private const float ProbeBelow = 0.5f;

        // Sounds are made this far above the feet, so they're inside the
        // sound room the player is standing in rather than exactly on its
        // bottom face.
        private const float SoundHeight = 0.1f;

        // Horizontal metres moved since the last step.
        private float _distanceSinceStep;

        // Seconds spent moving since the last step (time standing still
        // isn't counted). With the distance, it gives the average speed
        // over the stride - steadier than one frame's speed.
        private float _timeSinceStep;

        // The last surface the ray found - kept for a step where it finds
        // nothing (the lip of a stair, a gap between two floor pieces).
        private SurfaceType _lastSurface = SurfaceType.Stone;

        // Whether there are arrows to ask about moss, looked up once.
        private bool _hasArrows;

        /// <summary>
        /// Editor-only: fills in the references when the component is added
        /// (it sits on the Player root with the others).
        /// </summary>
        private void Reset()
        {
            playerLocomotion = GetComponent<PlayerLocomotion>();
            characterController = GetComponent<CharacterController>();
            playerTransform = transform;
            groundLayers = LayerMask.GetMask("Environment", "Interactable");
            playerArrows = GetComponentInChildren<PlayerArrows>(true);
        }

        private void Awake()
        {
            if (playerArrows == null) {
                playerArrows = GetComponentInChildren<PlayerArrows>(true);
            }

            _hasArrows = playerArrows != null;
        }

        private void OnEnable()
        {
            playerLocomotion.Landed += OnLanded;
        }

        private void OnDisable()
        {
            playerLocomotion.Landed -= OnLanded;
        }

        /// <summary>
        /// Adds this frame's real movement to the distance walked, and
        /// makes a step when that reaches the current stride. Called by
        /// PlayerController after Move() and TickState(): appliedMovement is
        /// how far the player actually moved this frame.
        /// </summary>
        public void Tick(Vector3 appliedMovement)
        {
            Gait gait;

            // The speed the player's gait (walk, sprint, crouch or crouch
            // sprint) moves at with the stick fully pushed forward.
            float fullSpeed = playerLocomotion.GaitSpeed;

            switch (playerLocomotion.MovementState) {
                case MovementState.Walking:
                    gait = walking;
                    break;
                case MovementState.Sprinting:
                    gait = sprinting;
                    break;
                case MovementState.CrouchWalking:
                    gait = crouching;
                    break;
                case MovementState.Still:
                case MovementState.CrouchStill:
                    // Standing still keeps the distance so far. If it were
                    // dropped, moving in short bursts would never reach a
                    // stride, and the player could cross a room in silence.
                    return;
                default:
                    // Off the ground (in the air, climbing): no steps, and
                    // start counting afresh when back on it.
                    _distanceSinceStep = 0f;
                    _timeSinceStep = 0f;
                    return;
            }

            // Horizontal only: going down a slope or a step shouldn't count
            // the drop as distance walked.
            appliedMovement.y = 0f;
            _distanceSinceStep += appliedMovement.magnitude;
            _timeSinceStep += Time.deltaTime;

            if (_distanceSinceStep < gait.stride) {
                return;
            }

            // Creeping is quieter: the average speed over this stride, as a
            // fraction of the gait's full speed, slides the step between
            // slowestStepScale and 1. Speed = distance / time.
            float speedFraction = _timeSinceStep > 0f
                ? Mathf.Clamp01(_distanceSinceStep / _timeSinceStep / fullSpeed)
                : 1f;
            float speedScale = Mathf.Lerp(slowestStepScale, 1f, speedFraction);

            // Keep what's left over rather than zeroing, so the rhythm
            // stays even whatever the frame rate.
            _distanceSinceStep -= gait.stride;
            _timeSinceStep = 0f;

            // A slow enough crouched step is silent, to the player and to
            // guards. The stride is still used up above, so speeding up
            // doesn't sound a step straight away.
            if (playerLocomotion.MovementState == MovementState.CrouchWalking && speedFraction < silentCrouchSpeed) {
                return;
            }

            // PlayStep() plays the surface's audio and emits its noise.
            // Moss swallows the step whole.
            if (surfaceSounds != null) {
                Vector3 position = FindFloor();

                if (IsOnMoss(position)) {
                    return;
                }

                surfaceSounds.PlayStep(_lastSurface, position, playerTransform, gait.noiseScale * speedScale, gait.volumeScale * speedScale);
            }
        }

        /// <summary>
        /// Raised by PlayerLocomotion on the frame the player lands, with
        /// the downward speed at impact. Plays the surface's step sound
        /// loud, twice: the first foot now and the second a moment later
        /// (SoundPlayer delays it, so there's no timer here). The faster
        /// the fall, the louder - both to guards and in the player's ears.
        /// </summary>
        private void OnLanded(float fallSpeed)
        {
            // The landing is this stride's sound; the next step comes a
            // full stride later.
            _distanceSinceStep = 0f;
            _timeSinceStep = 0f;

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer == null || surfaceSounds == null) {
                return;
            }

            Vector3 position = FindFloor();
            SoundCue cue = surfaceSounds.GetFootstep(_lastSurface);

            if (cue == null || IsOnMoss(position)) {
                return;
            }

            float strength = Mathf.Clamp(fallSpeed / referenceLandingSpeed, minLandingScale, maxLandingScale);

            // One noise for the whole landing, emitted here rather than by
            // Play() (noise scale 0 below): guards should hear one event,
            // not one per foot, and as a Landing rather than a Footstep.
            NoiseSystem.Emit(position, cue.NoiseRadius * landingNoiseScale * strength, NoiseType.Landing, playerTransform);

            float volume = landingVolumeScale * strength;
            soundPlayer.Play(cue, position, playerTransform, 0f, volume);
            soundPlayer.Play(cue, position, playerTransform, 0f, volume, secondFootDelay);
        }

        /// <summary>
        /// Looks at the floor under the feet: updates _lastSurface (kept as
        /// it was if the ray finds nothing) and returns where a sound made
        /// by the feet should play.
        /// </summary>
        private Vector3 FindFloor()
        {
            // The bottom of the capsule, in world space: its centre is kept
            // under the headset, so this follows the player walking about
            // their real room too.
            Vector3 feet = characterController.center;
            feet.y -= characterController.height * 0.5f;
            feet = playerTransform.TransformPoint(feet);

            if (SurfaceTag.TryFindBelow(feet + Vector3.up * ProbeAbove, ProbeAbove + ProbeBelow, groundLayers, out SurfaceType surface)) {
                _lastSurface = surface;
            }

            return feet + Vector3.up * SoundHeight;
        }

        /// <summary>
        /// Whether a sound made at position (from FindFloor(), just above
        /// the feet) would be made on a moss patch.
        /// </summary>
        private bool IsOnMoss(Vector3 position)
        {
            return _hasArrows && playerArrows.IsOnMoss(position - Vector3.up * SoundHeight);
        }
    }
}
