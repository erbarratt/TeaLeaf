using UnityEngine;

namespace Player
{
    /// <summary>
    /// The compass model the player wears on the back of the hand
    /// (Assets/Prefabs/Core/Compass.prefab): a fixed housing, a dial
    /// inside it that stays turned to north, and the light gem in the
    /// middle (CompassGem). PlayerCompass makes one at load, places it on
    /// the hand each frame and asks it to turn its dial.
    ///
    /// Its own axes, which the model is built to: Z points away from
    /// whoever is looking at its face (into the hand), Y is up the face
    /// and X to the right - so on the dial, +Y is north (the red point)
    /// and +X is east. The origin is the middle of the face, a few
    /// millimetres in front of the housing's back.
    /// </summary>
    public class Compass : MonoBehaviour
    {
        [Tooltip("The dial: the face with the star and the dots, turned each frame so its red point (its +Y) is north. Its origin must be the compass's middle. e.g. drag the prefab's Dial child here. Found by name (Dial) when the component is added; empty = the dial never turns.")]
        [SerializeField] private Transform dial;

        [Tooltip("The light gem in the middle (CompassGem), coloured by how well lit the player is. Found among the children when the component is added; empty = no gem reading.")]
        [SerializeField] private CompassGem gem;

        [Tooltip("The housing's outer radius, in metres: the sound arrows are placed just outside it. e.g. 0.05 = a compass 10cm across. Raise it if the arrows overlap the rim; lower it to bring them in.")]
        [SerializeField] private float radius = 0.05f;

        // Which way north is, in degrees round the world's vertical from
        // its +Z axis (0 = +Z is north and +X is east, the way a level
        // is seen from above in the editor).
        [Tooltip("Which way north is, in degrees round the world's vertical from +Z, e.g. 0 = world +Z is north and +X east; 90 = north turned a quarter round. Changes only where the dial points.")]
        [SerializeField] private float northAngle;

        private bool _hasDial;

        /// The light gem, or null if the model has none.
        public CompassGem Gem => gem;

        /// The housing's outer radius, in metres.
        public float Radius => radius;

        /// <summary>
        /// Editor-only: finds the dial and the gem when the component is
        /// added.
        /// </summary>
        private void Reset()
        {
            dial = transform.Find("Dial");
            gem = GetComponentInChildren<CompassGem>();
        }

        private void Awake()
        {
            // Looked up once, so the per-frame code tests a plain bool.
            _hasDial = dial != null;
        }

        /// <summary>
        /// Turns the dial so its top is north, laid flat onto the
        /// compass's face. palm is the way into the hand (the compass's
        /// own Z) in the world. With the face edge-on to north there's no
        /// such direction, and the dial is left as it was.
        /// </summary>
        public void TurnToNorth(Vector3 palm)
        {
            if (!_hasDial) {
                return;
            }

            Vector3 north = Vector3.ProjectOnPlane(Quaternion.Euler(0f, northAngle, 0f) * Vector3.forward, palm);

            if (north.sqrMagnitude > 0.0001f) {
                dial.rotation = Quaternion.LookRotation(palm, north);
            }
        }
    }
}
