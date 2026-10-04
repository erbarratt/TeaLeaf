using Core;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// How easy the player is to see, as one number from 0 (hidden) to 1
    /// (in plain sight): the value guards' vision and the wrist gem read.
    ///
    /// It's the light falling on the player, changed by what they're doing.
    /// The light comes from Core.SceneLight - moonlight unless something
    /// shadows it, plus any LightSource in reach - sampled at the head and
    /// at the middle of the body and averaged, so leaning half out of a
    /// shadow is half as visible. Crouching then lowers it and sprinting
    /// raises it: standing in moonlight can be seen, crouching in it only
    /// from close up (how far a given Visibility can be seen from is the
    /// guards' side).
    ///
    /// The light is sampled a few times a second, not every frame (each
    /// sample is a few physics rays), and the result is eased rather than
    /// jumping - so stepping out of a shadow fades the value across a
    /// fraction of a second, like eyes adjusting, and the gem won't flicker
    /// on a shadow's edge.
    ///
    /// No Update(): PlayerController calls Tick() after the body has moved.
    /// </summary>
    public class PlayerVisibility : MonoBehaviour
    {
        [SerializeField] private PlayerLocomotion playerLocomotion;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Transform playerTransform;

        // The Main Camera: where the head is.
        [SerializeField] private Transform head;

        [Header("Light")]
        // Seconds between light samples.
        [SerializeField] private float sampleInterval = 0.1f;

        [Header("Stance")]
        // Multiplies the light level while crouched (still or moving).
        [SerializeField, Range(0f, 1f)] private float crouchScale = 0.6f;

        // Multiplies the light level while sprinting.
        [SerializeField, Range(1f, 2f)] private float sprintScale = 1.3f;

        [Header("Easing")]
        // How fast Visibility moves towards its new value, per second:
        // at 4, going from hidden to plain sight takes a quarter second.
        [SerializeField] private float changeSpeed = 4f;

        // Counts down to the next light sample. Starts at zero, so the
        // first Tick() samples.
        private float _sampleTimer;

        /// <summary>
        /// 0 = hidden, 1 = in plain sight.
        /// </summary>
        public float Visibility { get; private set; }

        /// <summary>
        /// The light level on the player at the last sample, before stance:
        /// 0 = full shadow, 1 = fully lit. For debug readouts.
        /// </summary>
        public float LightLevel { get; private set; }

        /// <summary>
        /// Editor-only: fills in the references when the component is added
        /// (it sits on the Player root with the others).
        /// </summary>
        private void Reset()
        {
            playerLocomotion = GetComponent<PlayerLocomotion>();
            characterController = GetComponent<CharacterController>();
            playerTransform = transform;

            Camera headCamera = GetComponentInChildren<Camera>();

            if (headCamera != null) {
                head = headCamera.transform;
            }
        }

        /// <summary>
        /// Works out this frame's Visibility. Called by PlayerController
        /// after Move() and TickState(), so it uses where the player ended
        /// up and this frame's MovementState.
        /// </summary>
        public void Tick()
        {
            _sampleTimer -= Time.deltaTime;

            if (_sampleTimer <= 0f) {
                _sampleTimer = sampleInterval;

                // The middle of the capsule, in world space.
                Vector3 body = playerTransform.TransformPoint(characterController.center);

                LightLevel = (SceneLight.LevelAt(head.position) + SceneLight.LevelAt(body)) * 0.5f;
            }

            // Stance is applied every frame, so crouching shows at once
            // rather than at the next sample.
            float stanceScale = 1f;

            switch (playerLocomotion.MovementState) {
                case MovementState.CrouchStill:
                case MovementState.CrouchWalking:
                    stanceScale = crouchScale;
                    break;
                case MovementState.Sprinting:
                    stanceScale = sprintScale;
                    break;
            }

            float target = Mathf.Clamp01(LightLevel * stanceScale);
            Visibility = Mathf.MoveTowards(Visibility, target, changeSpeed * Time.deltaTime);
        }
    }
}
