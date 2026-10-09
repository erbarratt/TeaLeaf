using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A sliding bolt on one face of a door. Shot, it holds the door shut
    /// whatever its lock is doing: the handle turns a little and stops, as
    /// on a locked door. It can only be worked from the side it's on - a
    /// hand on the other side can't target it.
    ///
    /// A hand takes it like a door handle (hand ray on it, grip) and slides
    /// it by moving along the door: towards the door's free edge shoots it,
    /// back towards the hinge draws it. Let go part way, it settles at
    /// whichever end is nearer. It can only be shot while the door is shut.
    ///
    /// This object sits on the door's face where the bolt's knob is when
    /// drawn back, with the door's own axes: X along the door towards its
    /// free edge (the way the bolt shoots), Y up, Z out of the door's
    /// front. onFront says which face it's on.
    ///
    /// The bolt only describes itself and tells the Door when it's shot.
    /// Reading the hand's movement is Player.PlayerHandDoors' job.
    ///
    /// Set up: a trigger BoxCollider on the Interactable layer covering
    /// the bolt on its own side of the door, a child of the Door, unscaled.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class DoorBolt : MonoBehaviour, IHandTarget, IHandSnapTarget, IDebugDrawable
    {
        // The door this bolt holds shut. Left empty, found on a parent.
        [SerializeField] private Door door;

        // Which face of the door the bolt is on: its front (+Z) or back.
        [SerializeField] private bool onFront = true;

        // The part that slides: the bar and its knob. Optional.
        [SerializeField] private Transform bar;

        // How far the bolt slides between drawn and shot, in metres.
        [SerializeField] private float travel = 0.05f;

        [SerializeField] private bool startsShot;

        [Header("Grip")]

        // Hand offsets and finger pose for a hand on the knob.
        [SerializeField] private HandSnapProfile snapProfile;

        // How far out from the door's face the hand grips the knob, in
        // metres.
        [SerializeField] private float standOff = 0.03f;

        [Header("Sound")]

        // The bolt reaching either end. Optional (empty = silent).
        [SerializeField] private SoundCue slideCue;

        // The layer the bolt must be on - see OnValidate().
        private const string InteractableLayerName = "Interactable";

        private static readonly Color _gizmoBoxColor = new(0.3f, 0.8f, 1f, 0.35f);
        private static readonly Color _drawnColor = new(0.3f, 1f, 0.4f, 1f);
        private static readonly Color _shotColor = new(1f, 0.3f, 0.3f, 1f);

        private BoxCollider _boxCollider;
        private bool _hasDoor;

        // The bar's local position with the bolt drawn back, which the
        // slide is added to.
        private Vector3 _barRestPosition;
        private bool _hasBar;

        /// The door this bolt holds shut.
        public Door Door => door;

        /// How far the bolt has been slid: 0 drawn back, 1 fully shot.
        public float Slide { get; private set; }

        /// True while the bolt is holding the door: slid at least half way.
        public bool IsShot { get; private set; }

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();

            if (door == null) {
                door = GetComponentInParent<Door>();
            }

            _hasDoor = door != null;
            _hasBar = bar != null;

            if (_hasBar) {
                _barRestPosition = bar.localPosition;
            }
        }

        /// <summary>
        /// Start() rather than Awake() to tell the door: the door sets
        /// itself up in its own Awake(), which may run after this one.
        /// </summary>
        private void Start()
        {
            if (startsShot) {
                ApplySlide(1f);
                SetShot(true);
            }
        }

        private void OnEnable()
        {
            HandTargetRegistry.Register(_boxCollider, this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            HandTargetRegistry.Unregister(_boxCollider);
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// Editor-only: warns about a bolt set up as something solid, or on
        /// a layer hand rays may not hit. Clicking the warning selects it.
        /// </summary>
        private void OnValidate()
        {
            BoxCollider box = GetComponent<BoxCollider>();

            if (box != null && !box.isTrigger) {
                Debug.LogWarning(
                    $"DoorBolt '{name}': its BoxCollider isn't a trigger. It's a grab volume, not " +
                    "geometry - tick Is Trigger; the bolt the player sees needs no collider.",
                    this);
            }

            int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);

            if (interactableLayer >= 0 && gameObject.layer != interactableLayer) {
                Debug.LogWarning($"DoorBolt '{name}': not on the {InteractableLayerName} layer, so hand rays may miss it.", this);
            }
        }

        /// <summary>
        /// The direction straight out of the face the bolt is on.
        /// </summary>
        private Vector3 Outward => onFront ? transform.forward : -transform.forward;

        /// <summary>
        /// Only a hand on the bolt's own side of the door can target it.
        /// </summary>
        public bool CanBeTargetedFrom(Vector3 rayOrigin)
        {
            return Vector3.Dot(rayOrigin - transform.position, Outward) > 0f;
        }

        /// <summary>
        /// How far along the bolt's travel a world point is: 0 level with
        /// the drawn position, 1 level with the shot one, and beyond
        /// either way (not limited). For turning a hand's position into a
        /// slide.
        /// </summary>
        public float SlideAt(Vector3 worldPoint)
        {
            return Vector3.Dot(worldPoint - transform.position, transform.right) / travel;
        }

        /// <summary>
        /// Slides the bolt to slide (limited to 0-1) while a hand holds
        /// it. It can't leave its drawn position while the door is open:
        /// there's nothing for it to shoot into. True on the frame it
        /// reaches either end - for a click in the hand.
        /// </summary>
        public bool SetSlide(float slide)
        {
            slide = Mathf.Clamp01(slide);

            if (_hasDoor && door.IsOpen) {
                slide = 0f;
            }

            if (slide == Slide) {
                return false;
            }

            ApplySlide(slide);

            bool isAtEnd = slide <= 0f || slide >= 1f;

            if (isAtEnd) {
                SetShot(slide >= 1f);
                PlaySlide();
            }

            return isAtEnd;
        }

        /// <summary>
        /// The hand has let go: a bolt left part way settles at the nearer
        /// end.
        /// </summary>
        public void Release()
        {
            if (Slide <= 0f || Slide >= 1f) {
                return;
            }

            bool shot = Slide >= 0.5f;
            ApplySlide(shot ? 1f : 0f);
            SetShot(shot);
            PlaySlide();
        }

        /// <summary>
        /// Records the slide and moves the bar to match.
        /// </summary>
        private void ApplySlide(float slide)
        {
            Slide = slide;

            if (_hasBar) {
                bar.localPosition = _barRestPosition + Vector3.right * (travel * slide);
            }
        }

        /// <summary>
        /// Shot or drawn: tells the door when it changes.
        /// </summary>
        private void SetShot(bool shot)
        {
            if (shot == IsShot) {
                return;
            }

            IsShot = shot;

            if (_hasDoor) {
                door.SetBolted(shot);
            }
        }

        /// <summary>
        /// Where the hand grips the knob now, in the world.
        /// </summary>
        public Vector3 GetGripPoint()
        {
            return transform.position + transform.right * (travel * Slide) + Outward * standOff;
        }

        /// <summary>
        /// The hand snap pose for a hand on the knob as it is now: facing
        /// into the door, thumb up. Called every frame the bolt is held,
        /// since it slides and moves with the door. The other values
        /// aren't needed.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand, Vector3 grabPoint, Vector3 headPosition, Vector3 headForward)
        {
            return GetSnapPose(isLeftHand);
        }

        /// <summary>
        /// The same, with only what's needed.
        /// </summary>
        public HandSnapPose GetSnapPose(bool isLeftHand)
        {
            Vector3 gripPosition = GetGripPoint();
            Quaternion gripRotation = Quaternion.LookRotation(-Outward, transform.up);

            // Without a profile, still snap to the bare grip frame so a
            // missing reference is obvious (hand badly offset), not a crash.
            return snapProfile == null
                ? new HandSnapPose(gripPosition, gripRotation, default)
                : snapProfile.Apply(isLeftHand, gripPosition, gripRotation);
        }

        /// <summary>
        /// The bolt's sound, from the bolt: the player hears it, and
        /// guards hear it as a noise. Nothing for an empty cue.
        /// </summary>
        private void PlaySlide()
        {
            if (slideCue == null) {
                return;
            }

            Vector3 position = GetGripPoint();
            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(slideCue, position, transform);
            } else {
                slideCue.EmitNoise(position, transform);
            }
        }

        /// <summary>
        /// Always draws the grab volume in the Scene view - see
        /// DrawDebug(). Editor-only.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// The grab volume's outline, faintly; in the detailed view also
        /// the bolt's travel, standing out of the face it's on - green
        /// while drawn, red while shot - with a mark where the knob is.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            // _boxCollider is only cached by Awake(), which hasn't run in
            // the editor.
            BoxCollider box = _boxCollider != null ? _boxCollider : GetComponent<BoxCollider>();

            if (box == null) {
                return;
            }

            lines.Matrix = transform.localToWorldMatrix;
            lines.Color = _gizmoBoxColor;
            lines.WireCube(box.center, box.size);
            lines.Matrix = Matrix4x4.identity;

            if (!detailed) {
                return;
            }

            // In the editor nothing has run yet: go by how it will start.
            bool shot = Application.isPlaying ? IsShot : startsShot;
            float slide = Application.isPlaying ? Slide : (startsShot ? 1f : 0f);

            Vector3 from = transform.position + Outward * standOff;
            Vector3 to = from + transform.right * travel;

            lines.Color = shot ? _shotColor : _drawnColor;
            lines.Line(from, to);
            lines.WireSphere(Vector3.Lerp(from, to, slide), 0.01f);
        }
    }
}
