using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AI
{
    /// <summary>
    /// Debug-only: loops one animation clip on a guard model, at a
    /// playback speed and a travel speed set in the Inspector, so the
    /// clip's feet can be matched to the ground it moves over. On the
    /// root of each guard in the Guard Tests area (TeaLeaf > Dev Room >
    /// Guard Tests), which makes one of these for every clip in
    /// Assets/Art/Animations/Guard.
    ///
    /// The clip is played through a Playable graph straight onto the
    /// model's Animator, bypassing Guard.controller, so any clip can be
    /// looked at on its own with nothing blending into it. The graph is
    /// told to loop every clip, the one-shot fall included.
    ///
    /// The guard travels along its own forward at moveSpeed, and once
    /// it has gone lapLength it is put back where it started, so it
    /// walks the same lap over and over. With the clip's feet matched
    /// to moveSpeed, the feet stay planted as the guard passes; sliding
    /// means the two don't agree. Tweak playbackSpeed (how fast the clip
    /// plays) and moveSpeed (how fast the ground goes by) in the
    /// Inspector while playing until the feet hold: the numbers that
    /// work are what the guard's blend tree and its patrol, search and
    /// chase speeds should agree on. The walk clip was made at 1.07 m/s
    /// and the run at 3.58 m/s, so at playback 1 those travel speeds
    /// should already be right. A label over the guard shows the clip
    /// and the two numbers.
    /// </summary>
    public class GuardAnimationTest : MonoBehaviour
    {
        // The clip to loop.
        [SerializeField] private AnimationClip clip;

        // The model's Animator, a child of the root. Found in the
        // children if not assigned.
        [SerializeField] private Animator animator;

        // How fast the clip plays: 1 is as authored, 2 twice as fast.
        [SerializeField, Min(0f)] private float playbackSpeed = 1f;

        // How far the guard travels along its forward each second, in
        // metres. 0 plays the clip on the spot.
        [SerializeField, Min(0f)] private float moveSpeed = 0f;

        // How far it travels before it is put back at its start.
        [SerializeField, Min(0.1f)] private float lapLength = 6f;

        // The label over the guard's head, if it has one.
        [SerializeField] private TextMeshPro label;

        private PlayableGraph _graph;
        private AnimationClipPlayable _playable;
        private Vector3 _startPosition;
        private float _travelled;

        // What the playable and label were last given, so each is only
        // touched on a change.
        private float _appliedPlaybackSpeed = -1f;
        private float _labelledMoveSpeed = -1f;

        private void Awake()
        {
            if (animator == null) {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator == null || clip == null) {
                Debug.LogError($"GuardAnimationTest '{name}': needs a clip and an Animator under it.", this);
                enabled = false;
                return;
            }

            // The model stays under this root: the test moves the root,
            // not the clip.
            animator.applyRootMotion = false;

            _playable = AnimationPlayableUtilities.PlayClip(animator, clip, out _graph);
            _startPosition = transform.position;
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) {
                _graph.Destroy();
            }
        }

        private void Update()
        {
            if (!Mathf.Approximately(_appliedPlaybackSpeed, playbackSpeed)) {
                _appliedPlaybackSpeed = playbackSpeed;
                _playable.SetSpeed(playbackSpeed);
                RefreshLabel();
            }

            if (!Mathf.Approximately(_labelledMoveSpeed, moveSpeed)) {
                RefreshLabel();
            }

            // Every clip loops here, whatever its import settings say:
            // the clip's own time is wound back by one length as it
            // runs past the end. A looping clip wraps on its own as
            // well, and winding its time back makes no difference to it.
            double time = _playable.GetTime();

            if (time >= clip.length) {
                _playable.SetTime(time - clip.length);
            }

            if (moveSpeed <= 0f) {
                return;
            }

            float step = moveSpeed * Time.deltaTime;
            _travelled += step;

            if (_travelled >= lapLength) {
                _travelled = 0f;
                transform.position = _startPosition;
            } else {
                transform.position += transform.forward * step;
            }
        }

        /// <summary>
        /// Writes the clip's name and the two speeds on the label.
        /// </summary>
        private void RefreshLabel()
        {
            _labelledMoveSpeed = moveSpeed;

            if (label != null) {
                label.text = $"{clip.name}\nclip x{playbackSpeed:0.00}   {moveSpeed:0.00} m/s";
            }
        }
    }
}
