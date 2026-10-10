using UnityEngine;

namespace Core
{
    /// <summary>
    /// The project's own spatialiser, for one of the SoundPlayer's voices:
    /// it makes the voice's sound seem to come from a direction by giving
    /// each ear what it would really get from there (see
    /// SpatialiserSettings for the cues). It also does the fade with
    /// distance. The voice's AudioSource is set to plain 2D, so Unity adds
    /// no panning or fade of its own on top.
    ///
    /// It works in two halves, on two threads:
    /// - The game's main thread, once a frame (SetTarget): from where the
    ///   sound is relative to the head, work out what each ear should get
    ///   - a volume, a delay and an amount of dulling. These are the
    ///   "targets".
    /// - Unity's audio thread (OnAudioFilterRead), each time it wants the
    ///   next few hundredths of a second of sound: change the samples
    ///   accordingly, sliding from the values in use to the targets over
    ///   that stretch, so nothing jumps and clicks.
    /// The audio thread must never create objects (the garbage collector
    /// pausing it would be heard as a crackle), so everything it uses is
    /// made up front.
    ///
    /// Added to each voice by the SoundPlayer, after the voice's muffling
    /// filter, so it works on the already-muffled sound.
    /// </summary>
    public class SpatialVoice : MonoBehaviour
    {
        // The delay line: the last DelayLength samples of the sound, so an
        // ear can be given the sound from a moment ago. A power of two, so
        // going round the end is a cheap bit mask. The largest real delay
        // is about 0.7 ms - some 32 samples at 48,000 a second.
        private const int DelayLength = 256;
        private const int DelayMask = DelayLength - 1;
        private const float MaxDelay = DelayLength - 2;

        private const float SpeedOfSound = 343f;

        // Closer to the head than this, in metres, a sound's direction is
        // faded out: right at the head a tiny movement would swing it from
        // ear to ear.
        private const float NearDistance = 0.25f;

        // Below this a filter's memory is treated as silence. Numbers this
        // close to zero are very slow for a processor to work with.
        private const float Silence = 1e-15f;

        private SpatialiserSettings _settings;

        // The targets, written by the main thread: per ear a volume, a
        // delay in samples and how much head shadow to mix in; how much of
        // the "behind" dulling to mix in; and the filters' coefficients.
        private float _targetGainLeft;
        private float _targetGainRight;
        private float _targetDelayLeft;
        private float _targetDelayRight;
        private float _targetShadowLeft;
        private float _targetShadowRight;
        private float _targetRear;
        private float _shadowCoefficient = 1f;
        private float _rearCoefficient = 1f;

        // Set by the main thread when a new sound starts on this voice:
        // the audio thread then jumps straight to the targets rather than
        // sliding there from the last sound's direction. volatile tells
        // the compiler another thread changes it.
        private volatile bool _restart = true;

        // The audio thread's own: the values in use now, the delay line
        // and where it's being written, and the three filters' memories.
        private float _gainLeft;
        private float _gainRight;
        private float _delayLeft;
        private float _delayRight;
        private float _shadowLeft;
        private float _shadowRight;
        private float _rear;
        private readonly float[] _delay = new float[DelayLength];
        private int _writeIndex;
        private float _shadowLeftState;
        private float _shadowRightState;
        private float _rearState;

        /// <summary>
        /// Gives the voice the shared settings. Called once, when the
        /// SoundPlayer makes the voice.
        /// </summary>
        public void Initialise(SpatialiserSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// A new sound is starting on this voice: set where it is, with no
        /// slide from where the voice's last sound was.
        /// </summary>
        public void Begin(Vector3 localOffset, float range, float falloffSharpness)
        {
            SetTarget(localOffset, range, falloffSharpness);
            _restart = true;
        }

        /// <summary>
        /// Says where the sound is now. localOffset is from the head to
        /// the sound in the head's own axes: x to the right, y up, z
        /// ahead, in metres. range is how far the sound carries and
        /// falloffSharpness the shape of its fade (the SoundPlayer's).
        /// Main thread, once a frame while the voice is playing.
        /// </summary>
        public void SetTarget(Vector3 localOffset, float range, float falloffSharpness)
        {
            SpatialiserSettings settings = _settings;
            float distance = localOffset.magnitude;

            // The fade with distance: the same curve Unity's own 3D fade
            // was given - full at the sound, silent at its range.
            float fade = range > 0f ? Mathf.Pow(Mathf.Clamp01(1f - distance / range), falloffSharpness) : 1f;

            Vector3 direction = distance > 0.0001f ? localOffset / distance : Vector3.forward;
            float nearness = Mathf.Clamp01(distance / NearDistance);

            // How far to one side: -1 = fully left, 0 = anywhere on the
            // plane between the ears (ahead, above, behind), 1 = fully
            // right. And how far behind: 0 = level with the ears or
            // ahead, 1 = directly behind.
            float side = direction.x * nearness;
            float amount = Mathf.Abs(side);
            float behind = Mathf.Clamp01(-direction.z) * nearness;

            // The time difference, by Woodworth's formula for a ball-
            // shaped head: the sound reaches the far ear by going straight
            // to the edge of the head (radius x sin of the angle) and then
            // round its curve (radius x the angle).
            float angle = Mathf.Asin(amount);
            float seconds = settings.HeadRadius / SpeedOfSound * (angle + amount) * settings.TimeDifference;
            float delay = Mathf.Min(seconds * settings.SampleRate, MaxDelay);

            float gain = fade * settings.Volume * Mathf.Lerp(1f, settings.RearVolume, behind);
            float nearGain = gain * Mathf.Lerp(1f, settings.NearEarVolume, amount);
            float farGain = gain * Mathf.Lerp(1f, settings.FarEarVolume, amount);
            float shadow = amount * settings.ShadowAmount;

            // A sound on the right: the left ear is the far one.
            if (side >= 0f) {
                _targetGainLeft = farGain;
                _targetGainRight = nearGain;
                _targetDelayLeft = delay;
                _targetDelayRight = 0f;
                _targetShadowLeft = shadow;
                _targetShadowRight = 0f;
            } else {
                _targetGainLeft = nearGain;
                _targetGainRight = farGain;
                _targetDelayLeft = 0f;
                _targetDelayRight = delay;
                _targetShadowLeft = 0f;
                _targetShadowRight = shadow;
            }

            _targetRear = behind * settings.RearAmount;
            _shadowCoefficient = settings.ShadowCoefficient;
            _rearCoefficient = settings.RearCoefficient;
        }

        /// <summary>
        /// Called by Unity on the audio thread with the voice's next
        /// stretch of sound, to be changed in place. data holds the
        /// samples for every output channel in turn (left, right, left,
        /// right, ...); a voice's one-channel clip arrives with the same
        /// sound in both.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            // Stereo output only (headphones): with one channel there is
            // no left and right to shape.
            if (_settings == null || channels < 2) {
                return;
            }

            int frames = data.Length / channels;

            if (frames == 0) {
                return;
            }

            // Take one copy of the targets, so every value slides to the
            // same frame's answer even if the main thread writes new ones
            // part-way through.
            float targetGainLeft = _targetGainLeft;
            float targetGainRight = _targetGainRight;
            float targetDelayLeft = _targetDelayLeft;
            float targetDelayRight = _targetDelayRight;
            float targetShadowLeft = _targetShadowLeft;
            float targetShadowRight = _targetShadowRight;
            float targetRear = _targetRear;
            float shadowCoefficient = _shadowCoefficient;
            float rearCoefficient = _rearCoefficient;

            if (_restart) {
                _restart = false;
                _gainLeft = targetGainLeft;
                _gainRight = targetGainRight;
                _delayLeft = targetDelayLeft;
                _delayRight = targetDelayRight;
                _shadowLeft = targetShadowLeft;
                _shadowRight = targetShadowRight;
                _rear = targetRear;

                // Forget the last sound: no tail of it in the delay line
                // or the filters.
                System.Array.Clear(_delay, 0, DelayLength);
                _shadowLeftState = 0f;
                _shadowRightState = 0f;
                _rearState = 0f;
            }

            // How much each value moves per sample to arrive at its target
            // by the end of this stretch.
            float step = 1f / frames;
            float gainLeftStep = (targetGainLeft - _gainLeft) * step;
            float gainRightStep = (targetGainRight - _gainRight) * step;
            float delayLeftStep = (targetDelayLeft - _delayLeft) * step;
            float delayRightStep = (targetDelayRight - _delayRight) * step;
            float shadowLeftStep = (targetShadowLeft - _shadowLeft) * step;
            float shadowRightStep = (targetShadowRight - _shadowRight) * step;
            float rearStep = (targetRear - _rear) * step;

            for (int frame = 0, i = 0; frame < frames; frame++, i += channels) {
                _gainLeft += gainLeftStep;
                _gainRight += gainRightStep;
                _delayLeft += delayLeftStep;
                _delayRight += delayRightStep;
                _shadowLeft += shadowLeftStep;
                _shadowRight += shadowRightStep;
                _rear += rearStep;

                // One sound in: the two channels averaged (they're the
                // same for a one-channel clip).
                float input = (data[i] + data[i + 1]) * 0.5f;

                // Behind: mix in a dulled copy. The filter's memory moves
                // a fraction of the way to each sample, which is what
                // takes the high end off; the mix says how much of that
                // dulled copy replaces the original.
                _rearState += rearCoefficient * (input - _rearState);
                float sound = input + (_rearState - input) * _rear;

                // Into the delay line, and out again for each ear from as
                // long ago as that ear's delay.
                _delay[_writeIndex] = sound;
                float left = ReadDelay(_delayLeft);
                float right = ReadDelay(_delayRight);

                // Head shadow: the same dulling trick per ear, mixed in
                // only for the far one.
                _shadowLeftState += shadowCoefficient * (left - _shadowLeftState);
                _shadowRightState += shadowCoefficient * (right - _shadowRightState);

                data[i] = (left + (_shadowLeftState - left) * _shadowLeft) * _gainLeft;
                data[i + 1] = (right + (_shadowRightState - right) * _shadowRight) * _gainRight;

                // Any further channels (surround speakers) get nothing.
                for (int channel = 2; channel < channels; channel++) {
                    data[i + channel] = 0f;
                }

                _writeIndex = (_writeIndex + 1) & DelayMask;
            }

            // Land exactly on the targets (the steps leave rounding error).
            _gainLeft = targetGainLeft;
            _gainRight = targetGainRight;
            _delayLeft = targetDelayLeft;
            _delayRight = targetDelayRight;
            _shadowLeft = targetShadowLeft;
            _shadowRight = targetShadowRight;
            _rear = targetRear;

            if (Mathf.Abs(_rearState) < Silence) {
                _rearState = 0f;
            }

            if (Mathf.Abs(_shadowLeftState) < Silence) {
                _shadowLeftState = 0f;
            }

            if (Mathf.Abs(_shadowRightState) < Silence) {
                _shadowRightState = 0f;
            }
        }

        /// <summary>
        /// The sound from delay samples ago. A delay is rarely a whole
        /// number of samples, so this blends the two stored samples either
        /// side of that moment. The mask wraps the index round the end of
        /// the delay line (and round the start, for a negative one).
        /// </summary>
        private float ReadDelay(float delay)
        {
            int whole = (int)delay;
            float fraction = delay - whole;
            float newer = _delay[(_writeIndex - whole) & DelayMask];
            float older = _delay[(_writeIndex - whole - 1) & DelayMask];
            return newer + (older - newer) * fraction;
        }
    }
}
