using UnityEngine;

namespace Core
{
    /// <summary>
    /// Plays the scene's SoundLoops (torches, fires): sounds that never
    /// stop, from one place.
    ///
    /// Each loop has its route through rooms and portals worked out again
    /// a few times a second, on the loop's own timer so the work is spread
    /// over frames. The ones that reach the player play on this class's
    /// own small pool of voices - separate from the SoundPlayer's, so a
    /// burst of one-off sounds can never cut a torch off - gliding to
    /// wherever the route now says they're heard from. With more loops in
    /// earshot than voices, the loudest get them.
    ///
    /// A plain class, made and ticked by the SoundPlayer, which owns the
    /// settings and makes the voices.
    /// </summary>
    public class SoundLoopPlayer
    {
        // A loop only takes a voice from a quieter one if it's this many
        // times louder - otherwise two loops of about the same loudness
        // would keep taking it from each other.
        private const float StealMargin = 1.25f;

        private readonly SoundPlayer _player;
        private readonly Voice[] _voices;

        /// <summary>
        /// One voice of the pool and what it's doing.
        /// </summary>
        private class Voice
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

        /// <summary>
        /// How many loops have a voice right now. For debug readouts.
        /// </summary>
        public int PlayingCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _voices.Length; i++) {
                    if (_voices[i].InUse) {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Makes the pool: voiceCount voices from the SoundPlayer, each
        /// set to repeat its clip.
        /// </summary>
        public SoundLoopPlayer(SoundPlayer player, int voiceCount, AnimationCurve falloff)
        {
            _player = player;
            _voices = new Voice[voiceCount];

            for (int i = 0; i < voiceCount; i++) {
                Voice voice = new();
                player.CreateVoice($"Loop Voice {i}", falloff, out voice.Source, out voice.Filter, out voice.Spatial);
                voice.Source.loop = true;
                _voices[i] = voice;
            }
        }

        /// <summary>
        /// Once a frame: the route checks that are due, then the voices.
        /// toHead turns a world direction into the head's own axes, for
        /// the spatialiser.
        /// </summary>
        public void Tick(float now, float deltaTime, Vector3 ears, Quaternion toHead)
        {
            TickChecks(now, ears, toHead);
            TickVoices(deltaTime, ears, toHead);
        }

        /// <summary>
        /// Works out the route again for every loop whose turn it is.
        /// Each loop keeps its own time for this, so the work is spread
        /// over frames: a few route look-ups a frame at most, however many
        /// loops the level has.
        /// </summary>
        private void TickChecks(float now, Vector3 ears, Quaternion toHead)
        {
            SoundRoom listenerRoom = null;
            bool hasRoom = false;

            for (int i = 0; i < SoundLoop.Count; i++) {
                SoundLoop loop = SoundLoop.Get(i);

                if (now < loop.NextCheckTime) {
                    continue;
                }

                // The route needs the room the ears are in right now:
                // looked up once for the frame's loops.
                if (!hasRoom) {
                    hasRoom = true;
                    listenerRoom = _player.RefreshListenerRoom(ears);
                }

                CheckLoop(loop, now, ears, listenerRoom, toHead);
            }
        }

        /// <summary>
        /// Works out whether one loop reaches the player and by what
        /// route, and gives it a voice if it does and has none.
        /// </summary>
        private void CheckLoop(SoundLoop loop, float now, Vector3 ears, SoundRoom listenerRoom, Quaternion toHead)
        {
            SoundCue cue = loop.Cue;
            bool audible = false;
            float interval = _player.LoopFarCheckInterval;

            if (cue != null && cue.ClipCount > 0) {
                Vector3 position = loop.Position;
                float range = cue.AudibleRange;

                // Cheapest test first, as for a one-off sound: too far in
                // a straight line is too far by any route. Those loops are
                // looked at less often.
                if ((position - ears).sqrMagnitude <= range * range) {
                    interval = _player.LoopCheckInterval;

                    if (SoundPropagation.TryGetPath(position, SoundRoom.Find(position), ears, listenerRoom, range, out SoundPath path)) {
                        audible = true;
                        loop.Path = path;

                        // The same sum the voice's volume comes to: the
                        // cue's volume, the fade with distance, the muffle.
                        loop.Loudness = cue.Volume * loop.VolumeScale
                            * Mathf.Pow(Mathf.Clamp01(1f - path.Distance / range), _player.FalloffSharpness)
                            * Mathf.Lerp(1f, _player.MuffledVolume, path.Muffle);
                    }
                }
            }

            loop.IsAudible = audible;

            // A little random each time, so loops enabled together (a
            // level loading) drift apart rather than all landing on the
            // same frame for ever.
            loop.NextCheckTime = now + interval * Random.Range(0.9f, 1.1f);

            if (audible && loop.VoiceIndex < 0) {
                TryGiveVoice(loop, ears, toHead);
            }
        }

        /// <summary>
        /// Finds a voice for a loop that has just come into earshot: a
        /// free one, or failing that the quietest one playing, if this
        /// loop is clearly louder.
        /// </summary>
        private void TryGiveVoice(SoundLoop loop, Vector3 ears, Quaternion toHead)
        {
            int chosen = -1;
            float quietest = float.PositiveInfinity;

            for (int i = 0; i < _voices.Length; i++) {
                Voice voice = _voices[i];

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

            SoundCue cue = loop.Cue;
            AudioClip clip = cue.GetClip(Random.Range(0, cue.ClipCount));

            if (clip == null) {
                return;
            }

            Voice target = _voices[chosen];

            if (target.InUse) {
                Release(target, chosen);
            }

            SoundPath path = loop.Path;
            target.Loop = loop;
            target.InUse = true;
            target.Offset = SoundPlayer.HeardOffset(path, loop.Position, ears);
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
            _player.SetMuffle(target.Filter, path.Muffle);

            if (target.Spatial != null) {
                target.Spatial.Begin(toHead * target.Offset, target.Range, _player.FalloffSharpness);
            }

            source.Play();

            // Start somewhere at random in the clip, so two torches with
            // the same clip don't crackle in step.
            source.timeSamples = Random.Range(0, clip.samples);
        }

        /// <summary>
        /// Whether a voice's loop is still there, still enabled, still
        /// this voice's, and still reaching the player.
        /// </summary>
        private static bool IsHeard(Voice voice, int index)
        {
            SoundLoop loop = voice.Loop;
            return loop != null && loop.isActiveAndEnabled && loop.VoiceIndex == index && loop.IsAudible;
        }

        /// <summary>
        /// Stops a voice and frees it for another loop.
        /// </summary>
        private static void Release(Voice voice, int index)
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
        private void TickVoices(float deltaTime, Vector3 ears, Quaternion toHead)
        {
            float easeTime = Mathf.Max(_player.LoopEaseTime, 0.01f);
            float fadeTime = Mathf.Max(_player.LoopFadeTime, 0.01f);

            for (int i = 0; i < _voices.Length; i++) {
                Voice voice = _voices[i];

                if (!voice.InUse) {
                    continue;
                }

                bool heard = IsHeard(voice, i);

                if (heard) {
                    SoundLoop loop = voice.Loop;
                    SoundPath path = loop.Path;
                    Vector3 target = SoundPlayer.HeardOffset(path, loop.Position, ears);

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

                voice.Gain = Mathf.MoveTowards(voice.Gain, heard ? 1f : 0f, deltaTime / fadeTime);

                if (!heard && voice.Gain <= 0f) {
                    Release(voice, i);
                    continue;
                }

                voice.Source.transform.position = ears + voice.Offset;

                if (voice.Spatial != null) {
                    voice.Spatial.SetTarget(toHead * voice.Offset, voice.Range, _player.FalloffSharpness);
                }

                float volume = voice.BaseVolume * Mathf.Lerp(1f, _player.MuffledVolume, voice.Muffle) * voice.Gain;

                if (volume != voice.AppliedVolume) {
                    voice.AppliedVolume = volume;
                    voice.Source.volume = volume;
                }

                if (voice.Muffle != voice.AppliedMuffle) {
                    voice.AppliedMuffle = voice.Muffle;
                    _player.SetMuffle(voice.Filter, voice.Muffle);
                }
            }
        }
    }
}
