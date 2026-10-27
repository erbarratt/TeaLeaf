using Core;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// How easy the player is to see, as one number from 0 (hidden) to 1
    /// (in plain sight): the value guards' vision and the compass read.
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
        [Tooltip("The locomotion on the Player root: its Movement State sets the stance scale, e.g. Crouch Walking applies Crouch Scale. Drag the Player root's PlayerLocomotion here.")]
        [SerializeField] private PlayerLocomotion playerLocomotion;
        [Tooltip("The CharacterController on the Player root: the body light sample and the waist sight point are taken at its centre, e.g. crouching lowers that centre with the capsule. Drag the Player root's CharacterController here.")]
        [SerializeField] private CharacterController characterController;
        [Tooltip("The Player root. The feet are at its position, e.g. guards search where its position was when they saw the player. Drag the Player root here.")]
        [SerializeField] private Transform playerTransform;

        [Tooltip("The Main Camera: where the head is, one of the two points the light is sampled at and the centre of the head sight points, e.g. leaning the head out of a shadow into a lit doorway raises Visibility. Drag the Main Camera here.")]
        [SerializeField] private Transform head;

        [Header("Light")]
        [Tooltip("Seconds between light samples. Each sample is a few physics rays, so this is not every frame. e.g. 0.1 = ten samples a second. Higher costs less but reacts later to stepping into light, lower reacts sooner but casts more rays.")]
        [SerializeField] private float sampleInterval = 0.1f;

        [Header("Stance")]
        [Tooltip("Multiplies the light level while crouched, still or moving (0-1), e.g. 0.6 = crouched in 0.4 moonlight is 0.24, dark enough to be seen only from close. Lower makes crouching hide more, 1 makes it hide nothing.")]
        [SerializeField, Range(0f, 1f)] private float crouchScale = 0.6f;

        [Tooltip("Multiplies the light level while sprinting (1-2), e.g. 1.3 = sprinting in 0.4 moonlight is 0.52. Higher makes sprinting more visible, 1 makes it no worse than walking; full shadow stays 0 whatever this is.")]
        [SerializeField, Range(1f, 2f)] private float sprintScale = 1.3f;

        [Header("Easing")]
        [Tooltip("How fast Visibility moves towards its new value, per second: e.g. at 4, hidden to plain sight takes a quarter of a second. Higher follows the light more sharply, lower smooths it out more slowly.")]
        [SerializeField] private float changeSpeed = 4f;

        // The points guards test for a clear line to the player (Thief
        // VR's five): either side of the head, its top, just below it,
        // and the waist. Each counts for a weight; the guard needs enough
        // of them showing (AI.GuardSight) - so a head peeking round a
        // corner isn't the player seen, the body is.
        [Header("Sight Points (what guards look for)")]
        [Tooltip("How far either side of the head's middle the two side sight points are, in metres, e.g. 0.1 = a guard can see an ear poking past a corner the head's middle is behind. Higher makes the head a wider target, lower a narrower one.")]
        [SerializeField] private float headSideOffset = 0.1f;
        [Tooltip("How far above the head's middle the top sight point is, in metres, e.g. 0.12 = the top of the head can show over a crate the eyes are below. Higher makes the head a taller target, lower a shorter one.")]
        [SerializeField] private float headTopOffset = 0.12f;
        [Tooltip("How far below the head's middle the sight point under the head (about the neck) is, in metres, e.g. 0.15 = 15cm below the eyes. Higher moves it down towards the chest, lower up towards the face.")]
        [SerializeField] private float underHeadOffset = 0.15f;
        [Tooltip("How far the waist sight point (the capsule's middle) is raised while crouched, in metres, e.g. 0.15 = crouched, it sits 15cm above the short capsule's middle. Higher puts it nearer the shoulders, 0 leaves it at the middle.")]
        [SerializeField] private float crouchWaistRaise = 0.15f;
        [Tooltip("How much each head side point and the top point count when a guard can see them; the guard sees the player once the weights it can see add up to its sight's Seen Weight. e.g. 0.17 with Seen Weight 0.5 = all three head points must show. Higher makes a peeking head easier to spot, lower harder.")]
        [SerializeField] private float headPointWeight = 0.17f;
        [Tooltip("How much the point under the head counts when a guard can see it (added to the other points seen, against the sight's Seen Weight). e.g. 0.5 with Seen Weight 0.5 = that point alone is enough to be seen. Higher makes it count for more, lower less.")]
        [SerializeField] private float underHeadWeight = 0.5f;
        [Tooltip("How much the waist point counts when a guard can see it (added to the other points seen, against the sight's Seen Weight). e.g. 0.8 = a visible body is seen even with the head hidden. Higher makes it count for more, lower less.")]
        [SerializeField] private float waistWeight = 0.8f;

        /// How many sight points there are (GetSightPoint()).
        public const int SightPointCount = 5;

        // Counts down to the next light sample. Starts at zero, so the
        // first Tick() samples.
        private float _sampleTimer;

        // Speed: where the feet were at the last measure, and seconds
        // since it.
        private Vector3 _lastFeetPosition;
        private float _speedTimer;

        /// <summary>
        /// 0 = hidden, 1 = in plain sight.
        /// </summary>
        public float Visibility { get; private set; }

        /// <summary>
        /// The light level on the player at the last sample, before stance:
        /// 0 = full shadow, 1 = fully lit. For debug readouts.
        /// </summary>
        public float LightLevel { get; private set; }

        /// The scene's player, as guards see them: reachable without a
        /// scene search. Null if the scene has none.
        public static PlayerVisibility Instance { get; private set; }

        /// Where the head is, in world space.
        public Vector3 HeadPosition => head.position;

        /// The middle of the body (the capsule), in world space.
        public Vector3 BodyPosition => playerTransform.TransformPoint(characterController.center);

        /// Where the feet are: the Player root, in world space.
        public Vector3 FeetPosition => playerTransform.position;

        /// <summary>
        /// How fast the body is moving, in metres a second: stick and
        /// room-scale walking alike, measured each light sample. Guards
        /// read it for moving fast; whether the player is still is
        /// IsStill.
        /// </summary>
        public float Speed { get; private set; }

        /// <summary>
        /// Whether the player is standing still, as guards see it: the
        /// MovementState is Still or Crouch Still - the stick isn't moving
        /// the body (room-scale steps and sway don't count). Sliding down
        /// steep ground and falling are Airborne, climbing and mantling
        /// their own states: all moving.
        /// </summary>
        public bool IsStill => playerLocomotion.MovementState is MovementState.Still or MovementState.CrouchStill;

        /// Whether the player is crouched.
        public bool IsCrouched => playerLocomotion.IsCrouching;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// One of the points guards look for (index 0 to SightPointCount
        /// - 1), in world space: head left, head right, head top, under
        /// the head, waist.
        /// </summary>
        public Vector3 GetSightPoint(int index)
        {
            Vector3 headPosition = head.position;

            switch (index) {
                case 0:
                    return headPosition - head.right * headSideOffset;
                case 1:
                    return headPosition + head.right * headSideOffset;
                case 2:
                    return headPosition + Vector3.up * headTopOffset;
                case 3:
                    return headPosition - Vector3.up * underHeadOffset;
                default:
                    return BodyPosition + (IsCrouched ? Vector3.up * crouchWaistRaise : Vector3.zero);
            }
        }

        /// <summary>
        /// How much a sight point counts for when a guard can see it.
        /// </summary>
        public float GetSightWeight(int index)
        {
            return index switch {
                3 => underHeadWeight,
                4 => waistWeight,
                _ => headPointWeight
            };
        }

        private void OnDestroy()
        {
            if (Instance == this) {
                Instance = null;
            }
        }

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
            // Speed over the last sample interval: steadier than one
            // frame's movement.
            _speedTimer += Time.deltaTime;

            if (_speedTimer >= sampleInterval) {
                Vector3 feet = playerTransform.position;
                Speed = Vector3.Distance(feet, _lastFeetPosition) / _speedTimer;
                _lastFeetPosition = feet;
                _speedTimer = 0f;
            }

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
