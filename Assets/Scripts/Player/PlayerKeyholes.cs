using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Looking through keyholes: opens the keyhole the player's head is
    /// near (an Interaction.DoorKeyhole) and lets it shut again as the head
    /// moves away. The player's half only - how a keyhole opens, and how
    /// far, is the keyhole's own business.
    ///
    /// Finding which keyhole the head is near is done a few times a
    /// second, not every frame; only the one keyhole found is then updated
    /// every frame, so its opening follows the head smoothly. With no
    /// keyhole in range this does nothing but count down a timer.
    ///
    /// No Update(): PlayerController calls Tick() after the body has moved.
    /// </summary>
    public class PlayerKeyholes : MonoBehaviour
    {
        [SerializeField] private PlayerTracking playerTracking;

        // Seconds between searches for a keyhole near the head.
        [SerializeField] private float searchInterval = 0.2f;

        // Counts down to the next search. Starts at zero, so the first
        // Tick() searches.
        private float _searchTimer;

        // The keyhole being opened, and whether there is one - a plain
        // bool, so Tick() doesn't ask Unity whether the object still
        // exists every frame.
        private DoorKeyhole _current;
        private bool _hasCurrent;

        /// <summary>
        /// Editor-only: fills in the reference when the component is added
        /// (it sits on the Player root with PlayerTracking).
        /// </summary>
        private void Reset()
        {
            playerTracking = GetComponent<PlayerTracking>();
        }

        /// <summary>
        /// Called by PlayerController once the body has moved, so the head
        /// is where it will be drawn from.
        /// </summary>
        public void Tick()
        {
            Vector3 headPosition = playerTracking.HeadPosition;
            float deltaTime = Time.deltaTime;

            _searchTimer -= deltaTime;

            if (_searchTimer <= 0f) {
                _searchTimer = searchInterval;

                DoorKeyhole nearest = DoorKeyhole.FindInRange(headPosition);

                // A different keyhole is now the nearest: the old one
                // shuts at once (two keyholes within reach of one head is
                // rare enough not to need anything smoother).
                if (nearest != null && nearest != _current) {
                    if (_hasCurrent) {
                        _current.Close();
                    }

                    _current = nearest;
                    _hasCurrent = true;
                }
            }

            // Tick() says false once the keyhole is back at rest with the
            // head out of range: nothing more to do until the next search
            // finds one.
            if (_hasCurrent && !_current.Tick(headPosition, deltaTime)) {
                _current = null;
                _hasCurrent = false;
            }
        }
    }
}
