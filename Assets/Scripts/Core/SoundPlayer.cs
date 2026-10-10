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
    /// processing, which is the real cost on Quest, so the pool size is the
    /// budget: when every voice is busy, the one that started longest ago
    /// is cut off and reused.
    ///
    /// Three things run all the time, from Update():
    /// - Loops (SoundLoop: torches, fires). Each has its route worked out
    ///   again a few times a second, and the ones that reach the player
    ///   play on a second, smaller pool of voices, gliding to wherever the
    ///   route now says they're heard from.
    /// - Reverb. One reverb for everything, set from the room the player
    ///   is standing in (SoundRoom's reverb settings) and faded to the
    ///   next room's on the way through a doorway.
    /// - Ambience. The room's background loop, with no position, faded
    ///   over to the next room's.
    ///
    /// With a spatialiser plugin chosen in the project's audio settings,
    /// every voice is passed through it, so a sound is heard in front,
    /// behind, above or below rather than only left or right.
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

        // A loop only takes a voice from a quieter one if it's this many
        // times louder - otherwise two loops of about the same loudness
        // would keep taking it from each other.
        private const float StealMargin = 1.25f;

        // The longest step the easing takes in one frame, in seconds, so a
        // loading hitch can't jump it.
        private const float MaxStep = 0.05f;

        // The pool. One AudioSource and one low-pass filter per voice, and
        // the time each was last started (to find the oldest).
        private AudioSource[] _sources;
        private AudioLowPassFilter[] _filters;
        private float[] _startTimes;

        // The loops' own pool, so a burst of one-off sounds can never cut
        // a torch off.
        private LoopVoice[] _loopVoices;

        // Whether the voices go through the project's own spatialiser, or
        // through a spatialiser plugin (never both).
        private bool _builtIn;
        private bool _spatialised;

        // For the project's own spatialiser: one per one-off voice, with
        // where that voice's sound is in the world and how far it carries,
        // so its direction from the head can be kept up as the head turns.
        private SpatialVoice[] _spatials;
        private Vector3[] _positions;
        private float[] _ranges;

        // The turn from world directions to the head's own (x right, y
        // up, z ahead), worked out once a frame.
        private Quaternion _toHead = Quaternion.identity;
        private int _sampleRate;

        // The room the player's ears were in at the last look (null =
        // outside), and when to look next.
        private SoundRoom _listenerRoom;
        private float _nextListenerCheck;

        // The one reverb: a Unity reverb filter on the same object as the
        // AudioListener, where it works on everything the player hears
        // after it has been mixed together - one effect however many
        // sounds are playing.
        // A change of room blends from where the settings had got to
        // (_reverbFrom) to the new room's (_reverbTarget); _reverbBlend
        // runs 0 to 1 and stays at 1 once there, when nothing is touched.
        private AudioReverbFilter _reverbFilter;
        private float _appliedReverbAmount;
        private ReverbSettings _reverbFrom;
        private ReverbSettings _reverbCurrent;
        private ReverbSettings _reverbTarget;
        private float _reverbBlend = 1f;
        private bool _reverbStarted;

        // The ambience: two plain (not 3D) looping sources, so one room's
        // can fade out on one while the next room's fades in on the
        // other. For each: the cue it's playing, the cue's volume times
        // the room's, how far it has faded in (0-1) and where that fade is
        // heading. _ambienceActive is the one playing the current room's.
        private readonly AudioSource[] _ambienceSources = new AudioSource[2];
        private readonly SoundCue[] _ambienceCues = new SoundCue[2];
        private readonly float[] _ambienceVolumes = new float[2];
        private readonly float[] _ambienceGains = new float[2];
        private readonly float[] _ambienceTargets = new float[2];
        private int _ambienceActive;
        private bool _ambienceFading;

        /// <summary>
        /// One voice of the loop pool and what it's doing.
        /// </summary>
        private class LoopVoice
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public SpatialVoice Spatial;

            // How far the loop it's playing carries, in metres.
            public float Range;

            // The loop it's playing. InUse is kept separately because a
            // loop destroyed with its object reads as null while its voice
            // is still fading out.
            public SoundLoop Loop;
            public bool InUse;

            // Where the sound is heard from, relative to the ears.
            public Vector3 Offset;

            // Whether the route was a straight line at the last look, and
            // how much longer to ease after it changed between straight
            // and through a portal.
            public bool WasDirect;
            public float SettleTime;

            // The fade in/out (0-1), the eased muffle (0-1) and the loop's
            // own volume, and the volume and muffle last sent to the
            // AudioSource and filter - they're only written when changed.
            public float Gain;
            public float Muffle;
            public float BaseVolume;
            public float AppliedVolume;
            public float AppliedMuffle;
        }

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

        /// <summary>
        /// How many loops have a voice right now. For debug readouts.
        /// </summary>
        public int PlayingLoopCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _loopVoices.Length; i++) {
                    if (_loopVoices[i].InUse) {
                        count++;
                    }
                }

                return count;
            }
        }

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

            AnimationCurve falloff = BuildFalloffCurve();

            for (int i = 0; i < voiceCount; i++) {
                CreateVoice($"Voice {i}", falloff, out _sources[i], out _filters[i], out _spatials[i]);
                _startTimes[i] = float.NegativeInfinity;
            }

            _loopVoices = new LoopVoice[loopVoiceCount];

            for (int i = 0; i < loopVoiceCount; i++) {
                LoopVoice voice = new();
                CreateVoice($"Loop Voice {i}", falloff, out voice.Source, out voice.Filter, out voice.Spatial);
                voice.Source.loop = true;
                _loopVoices[i] = voice;
            }

            for (int i = 0; i < _ambienceSources.Length; i++) {
                GameObject bed = new($"Ambience {i}");
                bed.transform.SetParent(transform, false);

                AudioSource source = bed.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;

                // Not 3D: it comes from nowhere in particular.
                source.spatialBlend = 0f;
                _ambienceSources[i] = source;
            }

            if (reverb && listener != null) {
                // A filter only works on the listener's output if it's on
                // the very object the AudioListener is on.
                if (listener.TryGetComponent(out AudioListener _)) {
                    _reverbFilter = listener.gameObject.AddComponent<AudioReverbFilter>();

                    // "User" is the preset that takes its numbers from code.
                    _reverbFilter.reverbPreset = AudioReverbPreset.User;
                } else {
                    Debug.LogWarning("SoundPlayer: the Listener object has no AudioListener, so there is no reverb.", this);
                }
            }
        }

        /// <summary>
        /// Makes one 3D voice as a child object: an AudioSource and the
        /// low-pass filter that muffles it, and with the project's own
        /// spatialiser a SpatialVoice (null otherwise). Shared by the
        /// one-off pool and the loop pool.
        /// </summary>
        private void CreateVoice(string voiceName, AnimationCurve falloff, out AudioSource source, out AudioLowPassFilter filter, out SpatialVoice spatial)
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

            Vector3 offset = HeardOffset(path, position, ears);
            audioSource.transform.position = ears + offset;

            if (_builtIn) {
                // Remembered, so the direction can follow the head as it
                // turns while the sound plays (TickSpatialVoices()).
                _positions[voice] = ears + offset;
                _ranges[voice] = range;
                _spatials[voice].Begin(Quaternion.Inverse(listener.rotation) * offset, range, falloffSharpness);
            }
            audioSource.maxDistance = range;
            audioSource.clip = cue.GetClip(Random.Range(0, cue.ClipCount));
            audioSource.pitch = 1f + Random.Range(-cue.PitchVariation, cue.PitchVariation);
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
        private static Vector3 HeardOffset(in SoundPath path, Vector3 position, Vector3 ears)
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
        private void SetMuffle(AudioLowPassFilter filter, float muffle)
        {
            bool muffled = muffle > 0f;
            filter.enabled = muffled;

            if (muffled) {
                filter.cutoffFrequency = OpenCutoff * Mathf.Pow(muffledCutoff / OpenCutoff, muffle);
            }
        }

        /// <summary>
        /// The loops, the reverb and the ambience. Its own Update() rather
        /// than a Tick() from PlayerController: it isn't a player system
        /// and nothing depends on when in the frame it runs. Unscaled time,
        /// so sound carries on if the game is ever paused by time scale.
        /// </summary>
        private void Update()
        {
            float now = Time.unscaledTime;
            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            Vector3 ears = listener.position;

            if (now >= _nextListenerCheck) {
                _nextListenerCheck = now + listenerCheckInterval;
                UpdateListenerRoom(ears);
            }

            if (_builtIn) {
                spatialiser.Prepare(_sampleRate);
                _toHead = Quaternion.Inverse(listener.rotation);
                TickSpatialVoices(ears);
            }

            TickReverb(deltaTime);
            TickAmbience(deltaTime);
            TickLoopChecks(now, ears);
            TickLoopVoices(deltaTime, ears);
        }

        /// <summary>
        /// The project's own spatialiser, for the one-off voices: tells
        /// each one that is playing where its sound now is relative to the
        /// head. The sound stays put in the world, but the head moves and
        /// turns under it - and a direction that follows the head's turn
        /// is most of how a listener tells in front from behind. (The loop
        /// voices are told in TickLoopVoices().)
        /// </summary>
        private void TickSpatialVoices(Vector3 ears)
        {
            for (int i = 0; i < _sources.Length; i++) {
                if (_sources[i].isPlaying) {
                    _spatials[i].SetTarget(_toHead * (_positions[i] - ears), _ranges[i], falloffSharpness);
                }
            }
        }

        /// <summary>
        /// Looks up which room the player is in and points the reverb and
        /// the ambience at that room's settings. A few times a second, not
        /// per frame. Comparing the settings rather than the room means a
        /// room tuned in the Inspector while playing is heard straight
        /// away.
        /// </summary>
        private void UpdateListenerRoom(Vector3 ears)
        {
            _listenerRoom = SoundRoom.Find(ears);
            bool inRoom = _listenerRoom != null;

            if (_reverbFilter != null) {
                ReverbSettings wanted = inRoom ? _listenerRoom.Reverb : outsideReverb;

                if (!_reverbStarted) {
                    // The first look, as the level starts: no blend.
                    _reverbStarted = true;
                    _reverbCurrent = wanted;
                    _reverbTarget = wanted;
                    _reverbBlend = 1f;
                    _appliedReverbAmount = reverbAmount;
                    wanted.ApplyTo(_reverbFilter, reverbAmount);
                } else if (!wanted.Matches(_reverbTarget) || reverbAmount != _appliedReverbAmount) {
                    // Blend from wherever the reverb has got to, so a
                    // change part-way through another is still smooth.
                    _reverbFrom = _reverbCurrent;
                    _reverbTarget = wanted;
                    _reverbBlend = 0f;
                }
            }

            if (inRoom) {
                SetAmbience(_listenerRoom.Ambience, _listenerRoom.AmbienceVolume);
            } else {
                SetAmbience(outsideAmbience, outsideAmbienceVolume);
            }
        }

        /// <summary>
        /// Moves the reverb on towards the current room's settings. Does
        /// nothing once it's there, so the reverb is only touched for the
        /// moment after a change of room.
        /// </summary>
        private void TickReverb(float deltaTime)
        {
            if (_reverbBlend >= 1f) {
                return;
            }

            _reverbBlend = Mathf.Min(1f, _reverbBlend + deltaTime / Mathf.Max(reverbBlendTime, 0.01f));

            // SmoothStep eases in and out of the change.
            _reverbCurrent = ReverbSettings.Lerp(_reverbFrom, _reverbTarget, Mathf.SmoothStep(0f, 1f, _reverbBlend));
            _appliedReverbAmount = reverbAmount;
            _reverbCurrent.ApplyTo(_reverbFilter, reverbAmount);
        }

        /// <summary>
        /// Says what the ambience should be now: cue (null = silence) at
        /// roomVolume times the cue's own. If that isn't what's playing,
        /// the current one starts fading out and the new one fading in on
        /// the other source.
        /// </summary>
        private void SetAmbience(SoundCue cue, float roomVolume)
        {
            if (cue != null && (cue.ClipCount == 0 || cue.GetClip(0) == null)) {
                cue = null;
            }

            int active = _ambienceActive;
            float volume = cue != null ? cue.Volume * roomVolume : 0f;

            // Already playing it (the same room, or the next room shares
            // the cue): carry on, at this room's volume.
            if (_ambienceCues[active] == cue) {
                if (cue != null && !Mathf.Approximately(_ambienceVolumes[active], volume)) {
                    _ambienceVolumes[active] = volume;
                    _ambienceFading = true;
                }

                return;
            }

            int other = 1 - active;
            AudioSource source = _ambienceSources[other];
            _ambienceTargets[active] = 0f;
            _ambienceActive = other;
            _ambienceFading = true;

            if (cue == null) {
                // Into silence: nothing to start.
                source.Stop();
                _ambienceCues[other] = null;
                _ambienceGains[other] = 0f;
                _ambienceTargets[other] = 0f;
                return;
            }

            // Stepping back into the room just left, its ambience is still
            // fading out on this source: turn the fade round rather than
            // starting the clip again.
            if (_ambienceCues[other] != cue || !source.isPlaying) {
                // An ambience uses its cue's first clip, so the same cue
                // is always the same sound.
                source.clip = cue.GetClip(0);
                source.volume = 0f;
                source.Play();
                _ambienceGains[other] = 0f;
            }

            _ambienceCues[other] = cue;
            _ambienceVolumes[other] = volume;
            _ambienceTargets[other] = 1f;
        }

        /// <summary>
        /// Moves the two ambience sources' fades on, and stops one that
        /// has faded out. Does nothing once both have arrived.
        /// </summary>
        private void TickAmbience(float deltaTime)
        {
            if (!_ambienceFading) {
                return;
            }

            float step = deltaTime / Mathf.Max(ambienceFadeTime, 0.01f);
            bool stillFading = false;

            for (int i = 0; i < _ambienceSources.Length; i++) {
                if (_ambienceCues[i] == null) {
                    continue;
                }

                _ambienceGains[i] = Mathf.MoveTowards(_ambienceGains[i], _ambienceTargets[i], step);
                _ambienceSources[i].volume = _ambienceVolumes[i] * _ambienceGains[i];

                if (_ambienceGains[i] != _ambienceTargets[i]) {
                    stillFading = true;
                } else if (_ambienceTargets[i] <= 0f) {
                    _ambienceSources[i].Stop();
                    _ambienceCues[i] = null;
                }
            }

            _ambienceFading = stillFading;
        }

        /// <summary>
        /// Works out the route again for every loop whose turn it is.
        /// Each loop keeps its own time for this, so the work is spread
        /// over frames: a few route look-ups a frame at most, however many
        /// loops the level has.
        /// </summary>
        private void TickLoopChecks(float now, Vector3 ears)
        {
            bool roomIsFresh = false;

            for (int i = 0; i < SoundLoop.Count; i++) {
                SoundLoop loop = SoundLoop.Get(i);

                if (now < loop.NextCheckTime) {
                    continue;
                }

                // The route needs the room the ears are in right now, not
                // as of the last reverb look: once for the frame's loops.
                if (!roomIsFresh) {
                    roomIsFresh = true;
                    _listenerRoom = SoundRoom.Find(ears);
                }

                CheckLoop(loop, now, ears);
            }
        }

        /// <summary>
        /// Works out whether one loop reaches the player and by what
        /// route, and gives it a voice if it does and has none.
        /// </summary>
        private void CheckLoop(SoundLoop loop, float now, Vector3 ears)
        {
            SoundCue cue = loop.Cue;
            bool audible = false;
            float interval = loopFarCheckInterval;

            if (cue != null && cue.ClipCount > 0) {
                Vector3 position = loop.Position;
                float range = cue.AudibleRange;

                // Cheapest test first, as for a one-off sound: too far in
                // a straight line is too far by any route. Those loops are
                // looked at less often.
                if ((position - ears).sqrMagnitude <= range * range) {
                    interval = loopCheckInterval;

                    if (SoundPropagation.TryGetPath(position, SoundRoom.Find(position), ears, _listenerRoom, range, out SoundPath path)) {
                        audible = true;
                        loop.Path = path;

                        // The same sum the voice's volume comes to: the
                        // cue's volume, the fade with distance, the muffle.
                        loop.Loudness = cue.Volume * loop.VolumeScale
                            * Mathf.Pow(Mathf.Clamp01(1f - path.Distance / range), falloffSharpness)
                            * Mathf.Lerp(1f, muffledVolume, path.Muffle);
                    }
                }
            }

            loop.IsAudible = audible;

            // A little random each time, so loops enabled together (a
            // level loading) drift apart rather than all landing on the
            // same frame for ever.
            loop.NextCheckTime = now + interval * Random.Range(0.9f, 1.1f);

            if (audible && loop.VoiceIndex < 0) {
                TryGiveVoice(loop, ears);
            }
        }

        /// <summary>
        /// Finds a loop voice for a loop that has just come into earshot:
        /// a free one, or failing that the quietest one playing, if this
        /// loop is clearly louder.
        /// </summary>
        private void TryGiveVoice(SoundLoop loop, Vector3 ears)
        {
            int chosen = -1;
            float quietest = float.PositiveInfinity;

            for (int i = 0; i < _loopVoices.Length; i++) {
                LoopVoice voice = _loopVoices[i];

                if (!voice.InUse) {
                    chosen = i;
                    quietest = -1f;
                    break;
                }

                // A voice that is fading out counts as silent.
                float loudness = IsHeard(voice, i) ? voice.Loop.Loudness : 0f;

                if (loudness < quietest) {
                    quietest = loudness;
                    chosen = i;
                }
            }

            if (chosen < 0 || quietest * StealMargin >= loop.Loudness) {
                return;
            }

            LoopVoice target = _loopVoices[chosen];

            if (target.InUse) {
                ReleaseLoopVoice(target, chosen);
            }

            SoundCue cue = loop.Cue;
            AudioClip clip = cue.GetClip(Random.Range(0, cue.ClipCount));

            if (clip == null) {
                return;
            }

            SoundPath path = loop.Path;
            target.Loop = loop;
            target.InUse = true;
            target.Offset = HeardOffset(path, loop.Position, ears);
            target.WasDirect = path.IsDirect;
            target.SettleTime = 0f;
            target.Gain = 0f;
            target.Muffle = path.Muffle;
            target.BaseVolume = cue.Volume * loop.VolumeScale;
            target.Range = cue.AudibleRange;
            target.AppliedVolume = 0f;
            target.AppliedMuffle = path.Muffle;
            loop.VoiceIndex = chosen;

            AudioSource source = target.Source;
            source.transform.position = ears + target.Offset;
            source.maxDistance = cue.AudibleRange;
            source.clip = clip;
            source.pitch = 1f + Random.Range(-cue.PitchVariation, cue.PitchVariation);
            source.volume = 0f;
            SetMuffle(target.Filter, path.Muffle);

            if (_builtIn) {
                target.Spatial.Begin(Quaternion.Inverse(listener.rotation) * target.Offset, target.Range, falloffSharpness);
            }

            source.Play();

            // Start somewhere at random in the clip, so two torches with
            // the same clip don't crackle in step.
            source.timeSamples = Random.Range(0, clip.samples);
        }

        /// <summary>
        /// Whether a loop voice's loop is still there, still enabled,
        /// still this voice's, and still reaching the player.
        /// </summary>
        private static bool IsHeard(LoopVoice voice, int index)
        {
            SoundLoop loop = voice.Loop;
            return loop != null && loop.isActiveAndEnabled && loop.VoiceIndex == index && loop.IsAudible;
        }

        /// <summary>
        /// Stops a loop voice and frees it for another loop.
        /// </summary>
        private static void ReleaseLoopVoice(LoopVoice voice, int index)
        {
            voice.Source.Stop();
            voice.InUse = false;

            if (voice.Loop != null && voice.Loop.VoiceIndex == index) {
                voice.Loop.VoiceIndex = -1;
            }

            voice.Loop = null;
        }

        /// <summary>
        /// Every frame, for the few loops that have a voice: put the voice
        /// where the loop is now heard from, and move its fade and muffle
        /// on. The route itself comes from the loop's last check; this
        /// only glides between one answer and the next.
        /// </summary>
        private void TickLoopVoices(float deltaTime, Vector3 ears)
        {
            float easeTime = Mathf.Max(loopEaseTime, 0.01f);

            for (int i = 0; i < _loopVoices.Length; i++) {
                LoopVoice voice = _loopVoices[i];

                if (!voice.InUse) {
                    continue;
                }

                bool heard = IsHeard(voice, i);

                if (heard) {
                    SoundLoop loop = voice.Loop;
                    SoundPath path = loop.Path;
                    Vector3 target = HeardOffset(path, loop.Position, ears);

                    // Changing between "in my room" and "through a
                    // doorway" moves where it's heard from a long way at
                    // once: ease for a while afterwards.
                    if (path.IsDirect != voice.WasDirect) {
                        voice.WasDirect = path.IsDirect;
                        voice.SettleTime = easeTime * 4f;
                    }

                    if (!path.IsDirect || voice.SettleTime > 0f) {
                        // Through a portal the route's length is a
                        // fraction of a second old and arrives in steps,
                        // so always ease. Slerp turns the direction and
                        // slides the distance separately: a straight line
                        // from one side of the head to the other would
                        // pass through it, and be loud on the way.
                        voice.SettleTime -= deltaTime;
                        voice.Offset = Vector3.Slerp(voice.Offset, target, 1f - Mathf.Exp(-deltaTime / easeTime));
                    } else {
                        // In the same room and settled: exactly where it
                        // is, with no lag as the player moves.
                        voice.Offset = target;
                    }

                    voice.Muffle = Mathf.MoveTowards(voice.Muffle, path.Muffle, deltaTime / easeTime);
                    voice.BaseVolume = loop.Cue.Volume * loop.VolumeScale;
                }

                voice.Gain = Mathf.MoveTowards(voice.Gain, heard ? 1f : 0f, deltaTime / Mathf.Max(loopFadeTime, 0.01f));

                if (!heard && voice.Gain <= 0f) {
                    ReleaseLoopVoice(voice, i);
                    continue;
                }

                voice.Source.transform.position = ears + voice.Offset;

                if (_builtIn) {
                    voice.Spatial.SetTarget(_toHead * voice.Offset, voice.Range, falloffSharpness);
                }

                float volume = voice.BaseVolume * Mathf.Lerp(1f, muffledVolume, voice.Muffle) * voice.Gain;

                if (volume != voice.AppliedVolume) {
                    voice.AppliedVolume = volume;
                    voice.Source.volume = volume;
                }

                if (voice.Muffle != voice.AppliedMuffle) {
                    voice.AppliedMuffle = voice.Muffle;
                    SetMuffle(voice.Filter, voice.Muffle);
                }
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
