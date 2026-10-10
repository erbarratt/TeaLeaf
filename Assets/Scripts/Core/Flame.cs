using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Something burning that can be put out: a torch, a brazier, a fire.
    /// A fire in the level is several separate things - the light guards
    /// see by (LightSource), the light the player sees (a Unity Light), the
    /// flame itself and its crackle (SoundLoop) - and this is the one
    /// place that switches them all off, or on again, together.
    ///
    /// Put it on the fire's object. It finds the parts on that object by
    /// itself; any of them may be missing. No Update(): it only does
    /// anything when asked.
    /// </summary>
    public class Flame : MonoBehaviour
    {
        // The light guards and the player's visibility go by. Found on
        // this object if left empty.
        [SerializeField] private LightSource lightSource;

        // The light the player sees. Found on this object if left empty.
        [SerializeField] private Light visibleLight;

        // The fire's sound. Found on this object if left empty.
        [SerializeField] private SoundLoop soundLoop;

        // The flame the player sees (a mesh, particles), switched off with
        // the rest. Optional.
        [SerializeField] private GameObject flameVisual;

        // Off = it starts the level already out.
        [SerializeField] private bool startsLit = true;

        // The hiss of it going out. Optional.
        [SerializeField] private SoundCue extinguishCue;

        // Every enabled flame in the scene. They add and remove
        // themselves, so nothing ever searches the scene for them.
        private static readonly List<Flame> _all = new();

        /// Whether it's burning.
        public bool IsLit { get; private set; } = true;

        /// <summary>
        /// Editor-only: fills in the parts when the component is added.
        /// </summary>
        private void Reset()
        {
            FindParts();
        }

        private void Awake()
        {
            FindParts();
        }

        private void Start()
        {
            if (!startsLit) {
                Apply(false);
            }
        }

        private void OnEnable()
        {
            _all.Add(this);
        }

        private void OnDisable()
        {
            _all.Remove(this);
        }

        /// <summary>
        /// Finds whichever parts weren't wired, on this object.
        /// </summary>
        private void FindParts()
        {
            if (lightSource == null) {
                lightSource = GetComponent<LightSource>();
            }

            if (visibleLight == null) {
                visibleLight = GetComponent<Light>();
            }

            if (soundLoop == null) {
                soundLoop = GetComponent<SoundLoop>();
            }
        }

        /// <summary>
        /// Puts every lit flame within radius metres of a point out (a
        /// water bolt landing). Distance checks only: no physics. Returns
        /// how many went out.
        /// </summary>
        public static int ExtinguishNear(Vector3 point, float radius)
        {
            int count = 0;
            float radiusSqr = radius * radius;

            for (int i = 0; i < _all.Count; i++) {
                Flame flame = _all[i];

                if (flame.IsLit && (flame.transform.position - point).sqrMagnitude <= radiusSqr) {
                    flame.Extinguish();
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Puts it out: no light for guards to see by, no light for the
        /// player, no flame and no crackle. Does nothing if it's out
        /// already.
        /// </summary>
        public void Extinguish()
        {
            if (!IsLit) {
                return;
            }

            Apply(false);

            if (extinguishCue == null) {
                return;
            }

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(extinguishCue, transform.position, transform);
            } else {
                extinguishCue.EmitNoise(transform.position, transform);
            }
        }

        /// <summary>
        /// Lights it again. Does nothing if it's burning already.
        /// </summary>
        public void Relight()
        {
            if (!IsLit) {
                Apply(true);
            }
        }

        /// <summary>
        /// Switches every part on or off. The sound loop fades out by
        /// itself when it's disabled.
        /// </summary>
        private void Apply(bool lit)
        {
            IsLit = lit;

            if (lightSource != null) {
                lightSource.enabled = lit;
            }

            if (visibleLight != null) {
                visibleLight.enabled = lit;
            }

            if (soundLoop != null) {
                soundLoop.enabled = lit;
            }

            if (flameVisual != null) {
                flameVisual.SetActive(lit);
            }
        }
    }
}
