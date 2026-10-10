using UnityEngine;
using UnityEngine.Serialization;

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
    /// processing, so the pool size is the budget: when every voice is
    /// busy, the one that started longest ago is cut off and reused.
    ///
    /// This class does the one-off sounds itself. It also holds every
    /// sound setting, makes the voices, and each frame works out where the
    /// player's ears are and which room they're in - and hands that to
    /// three helpers that each do one job:
    /// - SoundLoopPlayer: the looping sounds (SoundLoop: torches, fires).
    /// - ListenerReverb: one reverb for everything, set from the room the
    ///   player is standing in.
    /// - RoomAmbience: that room's background loop.
    ///
    /// Each voice is given its direction by a SpatialVoice (the project's
    /// own spatialiser), unless the spatialiser mode says otherwise.
    /// </summary>
    public class SoundPlayer : MonoBehaviour
    {
        // The player's ears: the Main Camera (it has the AudioListener).
        [SerializeField] private Transform listener;

        // How many one-off sounds can play at once.
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

        [Header("Spatialiser (direction by ear)")]
        // How voices are given a direction: Unity's left/right panning,
        // the project's own spatialiser, or a plugin. Read once in
        // Awake(), so change it before pressing Play to compare them.
        [SerializeField] private SpatialiserMode spatialiserMode = SpatialiserMode.BuiltIn;

        // The project's own spatialiser's numbers. Read every frame: tune
        // them by ear while playing.
        [SerializeField] private SpatialiserSettings spatialiser = new();

        // Plugin only: run it after a voice's muffling filter rather than
        // before it. Read once in Awake().
        [SerializeField] private bool spatialiseAfterMuffle;

        [Header("Loops (SoundLoop: torches, fires)")]
        // How many loops can be heard at once. With more in earshot than
        // this, the loudest get the voices.
        [SerializeField] private int loopVoiceCount = 8;

        // Seconds between working a loop's route out again while it's
        // within earshot, and while it's too far to hear at all.
        [SerializeField] private float loopCheckInterval = 0.2f;
        [SerializeField] private float loopFarCheckInterval = 1f;

        // About how long a loop takes to glide to a new place or a new
        // muffle when its route changes, in seconds.
        [SerializeField] private float loopEaseTime = 0.25f;

        // How long a loop takes to fade in when it comes into earshot, and
        // out when it's switched off, in seconds.
        [SerializeField] private float loopFadeTime = 0.4f;

        [Header("Reverb (set by the room the player is in)")]
        // Off = no reverb at all. Read once in Awake().
        [SerializeField] private bool reverb = true;

        // The reverb while the player is in no SoundRoom.
        [SerializeField] private ReverbSettings outsideReverb = ReverbSettings.Outdoors;

        // How long the reverb takes to change from one room's to the
        // next's, in seconds.
        [SerializeField] private float reverbBlendTime = 0.6f;

        // Multiplies every room's reverb level: one knob for "all the
        // reverb is too much" without retuning each room.
        [SerializeField, FormerlySerializedAs("reverbSend"), Range(0f, 2f)] private float reverbAmount = 1f;

        [Header("Ambience (set by the room the player is in)")]
        // The background loop while the player is in no SoundRoom (wind,
        // the harbour at night). Empty = silence.
        [SerializeField] private SoundCue outsideAmbience;
        [SerializeField, Range(0f, 1f)] private float outsideAmbienceVolume = 1f;

        // How long one room's ambience takes to fade over to the next's,
        // in seconds.
        [SerializeField] private float ambienceFadeTime = 1.5f;

        // Seconds between looking up which room the player is in, for the
        // reverb and the ambience.
        [SerializeField] private float listenerCheckInterval = 0.2f;

        // The top of human hearing, in Hz: a cutoff this high removes
        // nothing.
        private const float OpenCutoff = 22000f;

        // The longest step the easing takes in one frame, in seconds, so a
        // loading hitch can't jump it.
        private const float MaxStep = 0.05f;

        // Added to a voice's worked-out end time, in seconds, so its
        // direction is kept up until the sound has certainly finished.
        private const float EndMargin = 0.1f;

        // The pool. One AudioSource and one low-pass filter per voice, and
        // the time each was last started (to find the oldest).
        private AudioSource[] _sources;
        private AudioLowPassFilter[] _filters;
        private float[] _startTimes;

        // Whether the voices go through the project's own spatialiser, or
        // through a spatialiser plugin (never both).
        private bool _builtIn;
        private bool _spatialised;

        // For the project's own spatialiser: one per one-off voice, with
        // where that voice's sound is in the world, how far it carries and
        // when it will have finished (unscaled time), so its direction
        // from the head can be kept up as the head turns - without asking
        // every voice every frame whether it's still playing.
        private SpatialVoice[] _spatials;
        private Vector3[] _positions;
        private float[] _ranges;
        private float[] _endTimes;
        private int _sampleRate;

        // The room the player's ears were in at the last look (null =
        // outside), and when to look next.
        private SoundRoom _listenerRoom;
        private float _nextListenerCheck;

        // The three helpers. The reverb is null when reverb is off.
        private SoundLoopPlayer _loops;
        private ListenerReverb _reverb;
        private RoomAmbience _ambience;

        /// The scene's sound player. Set in Awake(), so use it from Start()
        /// or later.
        public static SoundPlayer Instance { get; private set; }

        /// Where the player's ears are, in world space.
        public Vector3 ListenerPosition => listener.position;

        /// The room the player's ears are in (null = outside), as of the
        /// last look a fraction of a second ago.
        public SoundRoom ListenerRoom => _listenerRoom;

        /// How the voices are given a direction. A Plugin choice with no
        /// plugin installed falls back to Unity's panning - see
        /// IsSpatialised.
        public SpatialiserMode Spatialiser => spatialiserMode;

        /// Whether the voices go through a spatialiser (ours or a plugin)
        /// rather than Unity's left/right panning.
        public bool IsSpatialised => _builtIn || _spatialised;

        /// How many loops have a voice right now. For debug readouts.
        public int PlayingLoopCount => _loops.PlayingCount;

        // What the loop player reads of the settings, each frame, so they
        // can be tuned while playing.
        public float FalloffSharpness => falloffSharpness;
        public float MuffledVolume => muffledVolume;
        public float LoopCheckInterval => loopCheckInterval;
        public float LoopFarCheckInterval => loopFarCheckInterval;
        public float LoopEaseTime => loopEaseTime;
        public float LoopFadeTime => loopFadeTime;

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

            _builtIn = spatialiserMode == SpatialiserMode.BuiltIn;
            _sampleRate = AudioSettings.outputSampleRate;
            spatialiser.Prepare(_sampleRate);

            // A plugin is a project setting (one for the whole game); here
            // we only ask whether one is chosen.
            bool wantsPlugin = spatialiserMode == SpatialiserMode.Plugin;
            _spatialised = wantsPlugin && !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName());

            if (wantsPlugin && !_spatialised) {
                Debug.Log("SoundPlayer: no spatialiser plugin is chosen (Project Settings > Audio > Spatializer Plugin), so sounds are only panned left and right.", this);
            }

            _sources = new AudioSource[voiceCount];
            _filters = new AudioLowPassFilter[voiceCount];
            _startTimes = new float[voiceCount];
            _spatials = new SpatialVoice[voiceCount];
            _positions = new Vector3[voiceCount];
            _ranges = new float[voiceCount];
            _endTimes = new float[voiceCount];

            AnimationCurve falloff = BuildFalloffCurve();

            for (int i = 0; i < voiceCount; i++) {
                CreateVoice($"Voice {i}", falloff, out _sources[i], out _filters[i], out _spatials[i]);
                _startTimes[i] = float.NegativeInfinity;
                _endTimes[i] = float.NegativeInfinity;
            }

            _loops = new SoundLoopPlayer(this, loopVoiceCount, falloff);
            _ambience = new RoomAmbience(transform);

            if (reverb && listener != null) {
                if (listener.TryGetComponent(out AudioListener _)) {
                    _reverb = new ListenerReverb(listener.gameObject);
                } else {
                    Debug.LogWarning("SoundPlayer: the Listener object has no AudioListener, so there is no reverb.", this);
                }
            }
        }

        /// <summary>
        /// Makes one 3D voice as a child object: an AudioSource and the
        /// low-pass filter that muffles it, and with the project's own
        /// spatialiser a SpatialVoice (null otherwise). Used for this
        /// class's one-off pool and by the loop player for its own.
        /// </summary>
        public void CreateVoice(string voiceName, AnimationCurve falloff, out AudioSource source, out AudioLowPassFilter filter, out SpatialVoice spatial)
        {
            GameObject voice = new(voiceName);
            voice.transform.SetParent(transform, false);

            source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;

            // Fully 3D: panned and faded by Unity by where it is. With the
            // project's own spatialiser, plain 2D instead: the SpatialVoice
            // added below does the direction and the fade itself, and
            // Unity must not add its own on top.
            source.spatialBlend = _builtIn ? 0f : 1f;

            // Through the spatialiser plugin, if that is the choice.
            source.spatialize = _spatialised;
            source.spatializePostEffects = spatialiseAfterMuffle;

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
            filter = voice.AddComponent<AudioLowPassFilter>();
            filter.enabled = false;

            // Added after the filter, so it comes after it in the voice's
            // chain and works on the muffled sound.
            spatial = null;

            if (_builtIn) {
                spatial = voice.AddComponent<SpatialVoice>();
                spatial.Initialise(spatialiser);
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
            AudioClip clip = cue.GetClip(Random.Range(0, cue.ClipCount));
            float pitch = 1f + Random.Range(-cue.PitchVariation, cue.PitchVariation);

            Vector3 offset = HeardOffset(path, position, ears);
            audioSource.transform.position = ears + offset;

            if (_builtIn) {
                // Remembered, so the direction can follow the head as it
                // turns while the sound plays (TickSpatialVoices()), until
                // the clip has run its length at this pitch.
                _positions[voice] = ears + offset;
                _ranges[voice] = range;
                _endTimes[voice] = clip != null
                    ? Time.unscaledTime + delay + clip.length / Mathf.Max(pitch, 0.01f) + EndMargin
                    : float.NegativeInfinity;
                _spatials[voice].Begin(Quaternion.Inverse(listener.rotation) * offset, range, falloffSharpness);
            }

            audioSource.maxDistance = range;
            audioSource.clip = clip;
            audioSource.pitch = pitch;
            audioSource.volume = cue.Volume * volumeScale * Mathf.Lerp(1f, muffledVolume, path.Muffle);

            // The route's muffle plus the caller's own, capped at fully
            // muffled.
            SetMuffle(filter, Mathf.Clamp01(path.Muffle + muffle));

            if (delay > 0f) {
                audioSource.PlayDelayed(delay);
            } else {
                audioSource.Play();
            }

            _startTimes[voice] = Time.time + delay;
            return true;
        }

        /// <summary>
        /// Where a sound is heard from, relative to the ears: its real
        /// position in the same room. Otherwise in the direction of the
        /// portal it came through, but as far away as the whole route - so
        /// the 3D fade and panning match the route, not the straight line
        /// through the wall.
        /// </summary>
        public static Vector3 HeardOffset(in SoundPath path, Vector3 position, Vector3 ears)
        {
            if (path.IsDirect) {
                return position - ears;
            }

            Vector3 toPortal = path.HeardFrom - ears;
            float portalDistance = toPortal.magnitude;

            return portalDistance > 0.01f
                ? toPortal * (path.Distance / portalDistance)
                : toPortal;
        }

        /// <summary>
        /// Sets a voice's low-pass filter for a muffle of 0-1, switching
        /// it off when there is nothing to remove. Pitch is heard in
        /// ratios, not Hz (each octave doubles), so the cutoff slides
        /// between open and muffled by ratio too: halfway muffled is
        /// halfway in octaves.
        /// </summary>
        public void SetMuffle(AudioLowPassFilter filter, float muffle)
        {
            bool muffled = muffle > 0f;
            filter.enabled = muffled;

            if (muffled) {
                filter.cutoffFrequency = OpenCutoff * Mathf.Pow(muffledCutoff / OpenCutoff, muffle);
            }
        }

        /// <summary>
        /// Looks up which room the ears are in right now, remembers it and
        /// returns it. For the loop player, whose routes need the room as
        /// of this frame rather than as of the last reverb look.
        /// </summary>
        public SoundRoom RefreshListenerRoom(Vector3 ears)
        {
            _listenerRoom = SoundRoom.Find(ears);
            return _listenerRoom;
        }

        /// <summary>
        /// Every frame: where the ears are and how the head is turned,
        /// then each job in turn. Its own Update() rather than a Tick()
        /// from PlayerController: it isn't a player system and nothing
        /// depends on when in the frame it runs. Unscaled time, so sound
        /// carries on if the game is ever paused by time scale.
        /// </summary>
        private void Update()
        {
            float now = Time.unscaledTime;
            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            Vector3 ears = listener.position;

            // The turn from world directions to the head's own (x right,
            // y up, z ahead), for the spatialiser.
            Quaternion toHead = Quaternion.identity;

            if (_builtIn) {
                spatialiser.Prepare(_sampleRate);
                toHead = Quaternion.Inverse(listener.rotation);
                TickSpatialVoices(now, ears, toHead);
            }

            if (now >= _nextListenerCheck) {
                _nextListenerCheck = now + listenerCheckInterval;
                UpdateListenerRoom(ears);
            }

            if (_reverb != null) {
                _reverb.Tick(deltaTime, reverbBlendTime, reverbAmount);
            }

            _ambience.Tick(deltaTime, ambienceFadeTime);
            _loops.Tick(now, deltaTime, ears, toHead);
        }

        /// <summary>
        /// The project's own spatialiser, for the one-off voices: tells
        /// each one that hasn't finished where its sound now is relative
        /// to the head. The sound stays put in the world, but the head
        /// moves and turns under it - and a direction that follows the
        /// head's turn is most of how a listener tells in front from
        /// behind. (The loop player does the same for its voices.)
        /// </summary>
        private void TickSpatialVoices(float now, Vector3 ears, Quaternion toHead)
        {
            for (int i = 0; i < _spatials.Length; i++) {
                if (now < _endTimes[i]) {
                    _spatials[i].SetTarget(toHead * (_positions[i] - ears), _ranges[i], falloffSharpness);
                }
            }
        }

        /// <summary>
        /// Looks up which room the player is in and points the reverb and
        /// the ambience at that room's settings. A few times a second, not
        /// per frame.
        /// </summary>
        private void UpdateListenerRoom(Vector3 ears)
        {
            SoundRoom room = RefreshListenerRoom(ears);
            bool inRoom = room != null;

            if (_reverb != null) {
                _reverb.SetWanted(inRoom ? room.Reverb : outsideReverb, reverbAmount);
            }

            if (inRoom) {
                _ambience.Set(room.Ambience, room.AmbienceVolume);
            } else {
                _ambience.Set(outsideAmbience, outsideAmbienceVolume);
            }
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
