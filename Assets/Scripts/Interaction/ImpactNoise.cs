using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// Makes a physics prop sound when it hits something: a thrown bottle
    /// landing, a dropped crate. One call to the sound player does both
    /// halves - the player hears the impact, and guards hear it as a noise
    /// (through rooms and portals, like every other sound).
    ///
    /// How loud depends on two things:
    /// - how hard it hit. Below minSpeed there's no sound at all (a prop
    ///   settling, or set down gently); from there up to fullSpeed the
    ///   volume and the distance guards hear it from grow.
    /// - what it hit. A soft floor (carpet, grass) is quieter and duller
    ///   than stone, a ringing one (metal) louder - using the same
    ///   per-surface loudness footsteps have (SurfaceSounds), so each
    ///   surface is tuned in one place.
    ///
    /// Nothing runs per frame: Unity calls OnCollisionEnter() on the frame
    /// a collision starts, and that's the only time this does anything.
    ///
    /// Set up: on the prop's Rigidbody object, with a sound cue. A prop
    /// being carried is out of physics (see Grabbable.BeginCarry()), so it
    /// makes no impacts until it's let go.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ImpactNoise : MonoBehaviour
    {
        // The sound this prop makes when it hits something: its audio, and
        // how far it carries to guards at a full-strength impact.
        [SerializeField] private SoundCue cue;

        // For how loud each surface is. Optional: left empty, every
        // surface sounds like stone.
        [SerializeField] private SurfaceSounds surfaceSounds;

        [Header("Strength")]

        // How hard the hit must be to make any sound, and how hard for the
        // full sound, as the speed the collision took off the prop in
        // metres a second. A prop dropped from a metre lands at about 4.4.
        [SerializeField] private float minSpeed = 1f;
        [SerializeField] private float fullSpeed = 8f;

        // The volume and the noise radius of the softest impact that
        // sounds at all, as fractions of the cue's (which a full-strength
        // impact gets).
        [SerializeField, Range(0f, 1f)] private float minVolume = 0.25f;
        [SerializeField, Range(0f, 1f)] private float minNoiseScale = 0.3f;

        // The shortest time between two sounds from this prop, in seconds:
        // a prop bouncing or rattling to rest starts a new collision many
        // times a second.
        [SerializeField] private float minInterval = 0.15f;

        // Two props hitting each other each get told about the collision,
        // in the same physics step. An impact in the same step and within
        // this distance of the last one played is that same collision
        // again, and stays silent.
        private const float SameImpactDistance = 0.3f;

        // When and where the last impact was played, by any prop - see
        // SameImpactDistance.
        private static float _lastImpactStep = -1f;
        private static Vector3 _lastImpactPosition;

        private Rigidbody _rigidbody;

        // When this prop last sounded - see minInterval. Starts far in the
        // past, so the first impact is never held back.
        private float _lastSoundTime = float.NegativeInfinity;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Called by Unity on the physics step this prop starts touching
        /// another collider. Plays the impact if it was hard enough.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            // A kinematic prop isn't being moved by physics (a hand is
            // reaching for it): whatever ran into it makes its own sound.
            if (cue == null || _rigidbody.isKinematic) {
                return;
            }

            if (Time.time - _lastSoundTime < minInterval) {
                return;
            }

            // How hard it hit. The collision's impulse is the push physics
            // gave the prop to stop it going through the other thing; an
            // impulse over a mass is a change of speed. That measures the
            // hit itself - a prop sliding fast across a floor and onto the
            // next floor piece starts a collision too, but loses no speed.
            float speed = collision.impulse.magnitude / _rigidbody.mass;

            if (speed < minSpeed) {
                return;
            }

            // The first of the points where the two touch. GetContact()
            // reads one without making an array, unlike collision.contacts.
            Vector3 point = collision.GetContact(0).point;

            // The other half of this same collision, already played.
            float step = Time.fixedTime;

            if (step == _lastImpactStep && (point - _lastImpactPosition).sqrMagnitude < SameImpactDistance * SameImpactDistance) {
                return;
            }

            _lastImpactStep = step;
            _lastImpactPosition = point;
            _lastSoundTime = Time.time;

            // 0 at minSpeed, 1 at fullSpeed and beyond.
            float hardness = Mathf.InverseLerp(minSpeed, fullSpeed, speed);

            // What it hit: 1 for stone, less for softer surfaces, more for
            // louder ones. Another prop has no surface tag, so counts as
            // stone.
            float surfaceLoudness = surfaceSounds != null ? surfaceSounds.GetLoudness(SurfaceTag.Of(collision.collider)) : 1f;

            float noiseScale = Mathf.Lerp(minNoiseScale, 1f, hardness) * surfaceLoudness;
            float volumeScale = Mathf.Clamp01(Mathf.Lerp(minVolume, 1f, hardness) * surfaceLoudness);

            // A soft surface also dulls the sound, not just quietens it.
            float muffle = Mathf.Clamp01(1f - surfaceLoudness);

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(cue, point, transform, noiseScale, volumeScale, 0f, muffle);
            } else {
                // A scene with no sound player (a test scene): guards
                // still hear it.
                cue.EmitNoise(point, transform, noiseScale);
            }
        }
    }
}
