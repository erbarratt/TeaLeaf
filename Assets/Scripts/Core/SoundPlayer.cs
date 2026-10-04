using UnityEngine;

namespace Core
{
    /// <summary>
    /// Plays every sound in the game. One per scene, on its own Sound
    /// Player object. Anything that makes a sound calls
    /// SoundPlayer.Instance.Play(cue, position): the cue's noise goes to the
    /// guards (NoiseSystem) and its audio goes to the player's ears.
    ///
    /// The audio takes the same route through rooms and portals as noise
    /// (SoundPropagation). In the same room it plays where it was made.
    /// From another room it plays from the direction of the doorway it came
    /// through, as far away as the whole route is long, and with its high
    /// end rolled off a little for every doorway on the way and more for
    /// every closed door. With no route, or one longer than the cue's
    /// audible range, it isn't played at all.
    ///
    /// Voices are a fixed pool of AudioSources made once in Awake(), so
    /// playing a sound creates nothing. Each playing voice costs audio
    /// processing, which is the real cost on Quest, so the pool size is the
    /// budget: when every voice is busy, the one that started longest ago
    /// is cut off and reused.
    /// </summary>
    public class SoundPlayer : MonoBehaviour
    {
        // The player's ears: the Main Camera (it has the AudioListener).
        [SerializeField] private Transform listener;

        // How many sounds can play at once.
        [SerializeField] private int voiceCount = 16;

        // The shape of the fade from full volume at the sound to silence at
        // the cue's audible range. 1 = a straight line. Higher = drops
        // quickly close up and slowly far away, closer to real hearing:
        // distant sounds change little as the player moves. Read once in
        // Awake().
        [SerializeField, Range(1f, 4f)] private float falloffSharpness = 2f;

        [Header("Muffling (sound that came through portals)")]
        // Frequencies above this are rolled off when fully muffled, in Hz.
        [SerializeField] private float muffledCutoff = 1200f;

        // Volume multiplier when fully muffled.
        [SerializeField, Range(0f, 1f)] private float muffledVolume = 0.5f;

        // The top of human hearing, in Hz: a cutoff this high removes
        // nothing.
        private const float OpenCutoff = 22000f;

        // The pool. One AudioSource and one low-pass filter per voice, and
        // the time each was last started (to find the oldest).
        private AudioSource[] _sources;
        private AudioLowPassFilter[] _filters;
        private float[] _startTimes;

        /// The scene's sound player. Set in Awake(), so use it from Start()
        /// or later.
        public static SoundPlayer Instance { get; private set; }

        /// Where the player's ears are, in world space.
        public Vector3 ListenerPosition => listener.position;

        /// <summary>
        /// How many voices are playing right now. For debug readouts - it
        /// asks every voice, so don't call it per frame in gameplay code.
        /// </summary>
        public int PlayingVoiceCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _sources.Length; i++) {
                    if (_sources[i].isPlaying) {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Fills in the listener when the component is added.
        /// </summary>
        private void Reset()
        {
            if (Camera.main != null) {
                listener = Camera.main.transform;
            }
        }

        private void Awake()
        {
            Instance = this;

            if (listener == null) {
                Debug.LogError("SoundPlayer: Listener isn't assigned (the Main Camera).", this);
                enabled = false;
            }

            _sources = new AudioSource[voiceCount];
            _filters = new AudioLowPassFilter[voiceCount];
            _startTimes = new float[voiceCount];

            AnimationCurve falloff = BuildFalloffCurve();

            for (int i = 0; i < voiceCount; i++) {
                GameObject voice = new($"Voice {i}");
                voice.transform.SetParent(transform, false);

                AudioSource source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;

                // Fully 3D: panned and faded by where it is.
                source.spatialBlend = 1f;

                // No pitch shift from movement - voices jump between
                // positions, which would read as huge speeds.
                source.dopplerLevel = 0f;

                // Fade with distance along our own curve, which reaches
                // silence at the maximum distance - so "beyond the audible
                // range" really is silent (Unity's default curve never quite
                // reaches zero).
                source.rolloffMode = AudioRolloffMode.Custom;
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, falloff);

                // The filter only runs while a muffled sound needs it.
                AudioLowPassFilter filter = voice.AddComponent<AudioLowPassFilter>();
                filter.enabled = false;

                _sources[i] = source;
                _filters[i] = filter;
                _startTimes[i] = float.NegativeInfinity;
            }
        }

        /// <summary>
        /// The volume-over-distance curve every voice shares. Its horizontal
        /// axis is distance as a fraction of the maximum (0 = at the sound,
        /// 1 = the cue's audible range), so one curve fits every cue. The
        /// volume is (1 - fraction) raised to falloffSharpness: at 2, a
        /// sound halfway to its range plays at a quarter volume, which the
        /// ear hears as roughly half as loud.
        /// </summary>
        private AnimationCurve BuildFalloffCurve()
        {
            const int steps = 16;
            Keyframe[] keys = new Keyframe[steps + 1];

            for (int i = 0; i <= steps; i++) {
                float fraction = i / (float)steps;
                keys[i] = new Keyframe(fraction, Mathf.Pow(1f - fraction, falloffSharpness));
            }

            AnimationCurve curve = new(keys);

            // Round off the corners between the points.
            for (int i = 0; i <= steps; i++) {
                curve.SmoothTangents(i, 0f);
            }

            return curve;
        }

        private void OnDestroy()
        {
            if (Instance == this) {
                Instance = null;
            }
        }

        /// <summary>
        /// Makes a sound at a world-space position: emits the cue's noise
        /// for guards (its radius multiplied by noiseScale - 0 for none) and
        /// plays its audio for the player, if it reaches them. source is who
        /// made it (may be null). volumeScale multiplies the cue's volume,
        /// for one sound made softly or hard (a crouched footstep, a heavy
        /// landing). delay starts the audio that many seconds from now (the
        /// noise is still emitted at once): for a sound in two parts, like
        /// the two feet of a landing, with no timer needed by the caller -
        /// Unity's audio engine does the waiting. muffle (0-1) dulls this
        /// one sound on top of whatever its route through portals adds: it
        /// rolls off the high end only, leaving the volume to volumeScale.
        /// Returns whether the audio was played.
        /// </summary>
        public bool Play(SoundCue cue, Vector3 position, Transform source = null, float noiseScale = 1f, float volumeScale = 1f, float delay = 0f, float muffle = 0f)
        {
            cue.EmitNoise(position, source, noiseScale);

            if (!enabled || cue.ClipCount == 0) {
                return false;
            }

            // Cheapest test first: further than the audible range in a
            // straight line can't be heard by any route.
            Vector3 ears = listener.position;
            float range = cue.AudibleRange;

            if ((position - ears).sqrMagnitude > range * range) {
                return false;
            }

            if (!SoundPropagation.TryGetPath(position, ears, range, out SoundPath path)) {
                return false;
            }

            int voice = FindVoice();
            AudioSource audioSource = _sources[voice];
            AudioLowPassFilter filter = _filters[voice];

            // Where it's heard: the real position in the same room.
            // Otherwise in the direction of the portal it came through, but
            // as far away as the whole route - so the 3D fade and panning
            // match the route, not the straight line through the wall.
            Vector3 heardAt = position;

            if (!path.IsDirect) {
                Vector3 toPortal = path.HeardFrom - ears;
                float portalDistance = toPortal.magnitude;

                heardAt = portalDistance > 0.01f
                    ? ears + toPortal * (path.Distance / portalDistance)
                    : path.HeardFrom;
            }

            audioSource.transform.position = heardAt;
            audioSource.maxDistance = range;
            audioSource.clip = cue.GetClip(Random.Range(0, cue.ClipCount));
            audioSource.pitch = 1f + Random.Range(-cue.PitchVariation, cue.PitchVariation);
            audioSource.volume = cue.Volume * volumeScale * Mathf.Lerp(1f, muffledVolume, path.Muffle);

            // Pitch is heard in ratios, not Hz (each octave doubles), so the
            // cutoff slides between open and muffled by ratio too: halfway
            // muffled is halfway in octaves.
            // The route's muffle plus the caller's own, capped at fully
            // muffled.
            float totalMuffle = Mathf.Clamp01(path.Muffle + muffle);
            bool muffled = totalMuffle > 0f;
            filter.enabled = muffled;

            if (muffled) {
                filter.cutoffFrequency = OpenCutoff * Mathf.Pow(muffledCutoff / OpenCutoff, totalMuffle);
            }

            if (delay > 0f) {
                audioSource.PlayDelayed(delay);
            } else {
                audioSource.Play();
            }

            _startTimes[voice] = Time.time + delay;
            return true;
        }

        /// <summary>
        /// A voice to play on: the first idle one, or if all are busy the
        /// one that started longest ago (most likely nearly finished).
        /// </summary>
        private int FindVoice()
        {
            int oldest = 0;

            for (int i = 0; i < _sources.Length; i++) {
                if (!_sources[i].isPlaying) {
                    return i;
                }

                if (_startTimes[i] < _startTimes[oldest]) {
                    oldest = i;
                }
            }

            return oldest;
        }
    }
}
