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
            // Metres travelled per step.
            public float stride;

            // Multiplies the surface cue's noise radius (what guards hear).
            public float noiseScale;

            // Multiplies the surface cue's volume (what the player hears).
            [Range(0f, 1f)] public float volumeScale;
        }

        [SerializeField] private PlayerLocomotion playerLocomotion;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Transform playerTransform;

        // The footstep cue for each surface.
        [SerializeField] private SurfaceSounds surfaceSounds;

        // What counts as floor for the ray that finds the surface.
        [SerializeField] private LayerMask groundLayers;

        [Header("Steps")]
        [SerializeField] private Gait walking = new() { stride = 1.4f, noiseScale = 1f, volumeScale = 1f };
        [SerializeField] private Gait sprinting = new() { stride = 1.6f, noiseScale = 1.6f, volumeScale = 1f };
        [SerializeField] private Gait crouching = new() { stride = 1.2f, noiseScale = 0.4f, volumeScale = 0.5f };

        // How loud a step is at the slowest creep, as a fraction of the
        // same step at full stick: both the noise and the volume slide
        // between this and 1 with the player's speed. Multiplies with the
        // gait's own scales, so a slow crouched creep is the quietest way
        // to move.
        [SerializeField, Range(0f, 1f)] private float slowestStepScale = 0.4f;

        [Header("Landing")]

        // The fall speed, in metres per second, of a standard landing:
        // about what a jump on the flat lands at. A longer fall is louder,
        // a short drop quieter, in proportion.
        [SerializeField] private float referenceLandingSpeed = 3f;

        // The least and most that proportion can be, however gentle or
        // hard the landing.
        [SerializeField] private float minLandingScale = 0.5f;
        [SerializeField] private float maxLandingScale = 2f;

        // A standard landing compared with a walking step on the same
        // surface: how many times as far its noise carries to guards, and
        // how many times the volume (Unity caps a voice at full volume, so
        // above that the loudness comes from the two feet overlapping).
        [SerializeField] private float landingNoiseScale = 1.5f;
        [SerializeField] private float landingVolumeScale = 1.25f;

        // Seconds between the first foot's sound and the second's.
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

        // Which steps get the toe sound on a heel-and-toe surface - see
        // HeelAndToeCadence.
        private HeelAndToeCadence _heelAndToe;

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

            // The speed this gait moves at with the stick fully pushed.
            // Crouching has no speed of its own: it moves at walking speed.
            float fullSpeed = playerLocomotion.MoveSpeed;

            switch (playerLocomotion.MovementState) {
                case MovementState.Walking:
                    gait = walking;
                    break;
                case MovementState.Sprinting:
                    gait = sprinting;
                    fullSpeed = playerLocomotion.SprintSpeed;
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

            // PlayStep() plays the surface's audio, emits its noise, and
            // adds the toe sound on a heel-and-toe surface.
            if (surfaceSounds != null) {
                Vector3 position = FindFloor();
                surfaceSounds.PlayStep(_lastSurface, position, playerTransform, gait.noiseScale * speedScale, gait.volumeScale * speedScale, ref _heelAndToe);
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

            if (cue == null) {
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
    }
}
