using UnityEngine;

namespace Player
{
    /// What a hand is busy with. A hand does one thing at a time.
    public enum HandUse
    {
        // Nothing: the hand is free.
        None,
        // Gripping a ledge, ladder or rope (PlayerClimbing).
        Climbing,
        // Carrying a prop (PlayerHandHolding).
        Carrying,
        // On a door handle or a sliding bolt (PlayerHandDoors).
        Door,
        // Carrying the lockpicks, or on a pick (PlayerLockpicking).
        Lockpicks,
        // Carrying the keyring, or on a key in a lock (PlayerKeys).
        Keys,
        // Holding the pack out (PlayerPack).
        Pack,
        // The crossbow active on it, or on its wheel (PlayerCrossbow).
        Crossbow,
        // Holding the blackjack (PlayerBlackjack).
        Blackjack,
        // Holding the compass out (PlayerCompass).
        Compass
    }

    /// <summary>
    /// The one place that knows what each hand is busy with. A hand does
    /// one thing at a time - a hand gripping a ledge can't pick up a prop,
    /// a hand on a door handle can't take a lockpick - so before a hand
    /// system takes a hand it asks here whether any OTHER system has it.
    ///
    /// The hand systems don't hold references to each other. A new one is
    /// added here, once: a HandUse value, and a line each in GetUse() and
    /// IsBusyExcept().
    ///
    /// It keeps no state of its own. Each system still owns what its
    /// hands are doing and says so through its own flags
    /// (IsLeftHandGripping, IsLeftHolding, IsLeftOnDoor, IsLeftBusy, ...);
    /// this class only reads them, at the moment it's asked. So an answer
    /// is as fresh as the systems that have ticked so far this frame - the
    /// same as when they asked each other directly.
    ///
    /// Lives on the Hands object. Every system it reads is optional: a
    /// rig without one simply never has a hand busy with it. No Update().
    /// </summary>
    public class PlayerHandState : MonoBehaviour
    {
        // All optional, and found in Awake() if not wired: climbing is on
        // the Player root above this object, the rest are on this object.
        [SerializeField] private PlayerClimbing playerClimbing;
        [SerializeField] private PlayerHandHolding playerHandHolding;
        [SerializeField] private PlayerHandDoors playerHandDoors;
        [SerializeField] private PlayerLockpicking playerLockpicking;
        [SerializeField] private PlayerKeys playerKeys;
        [SerializeField] private PlayerPack playerPack;
        [SerializeField] private PlayerCrossbow playerCrossbow;
        [SerializeField] private PlayerBlackjack playerBlackjack;
        [SerializeField] private PlayerCompass playerCompass;

        // Looked up once, so the per-frame code tests plain bools.
        private bool _hasClimbing;
        private bool _hasHolding;
        private bool _hasDoors;
        private bool _hasLockpicking;
        private bool _hasKeys;
        private bool _hasPack;
        private bool _hasCrossbow;
        private bool _hasBlackjack;
        private bool _hasCompass;

        /// What the left hand is busy with right now.
        public HandUse LeftUse => GetUse(true);

        /// What the right hand is busy with right now.
        public HandUse RightUse => GetUse(false);

        /// <summary>
        /// The hand state on the Hands object (the object hands is on),
        /// added there if it has none - so a scene set up before this
        /// class existed needs no change. Called by each hand system in
        /// its Awake(); only the first call ever adds anything.
        /// </summary>
        public static PlayerHandState GetOrAdd(PlayerHandVisuals hands)
        {
            PlayerHandState state = hands.GetComponent<PlayerHandState>();

            if (state == null) {
                state = hands.gameObject.AddComponent<PlayerHandState>();
            }

            return state;
        }

        /// <summary>
        /// Editor-only: fills in the references when the component is
        /// added by hand.
        /// </summary>
        private void Reset()
        {
            FindSystems();
        }

        private void Awake()
        {
            FindSystems();

            _hasClimbing = playerClimbing != null;
            _hasHolding = playerHandHolding != null;
            _hasDoors = playerHandDoors != null;
            _hasLockpicking = playerLockpicking != null;
            _hasKeys = playerKeys != null;
            _hasPack = playerPack != null;
            _hasCrossbow = playerCrossbow != null;
            _hasBlackjack = playerBlackjack != null;
            _hasCompass = playerCompass != null;
        }

        /// <summary>
        /// Finds whichever hand systems the rig has. Finding a component
        /// doesn't need its Awake() to have run, so it doesn't matter
        /// which of them starts first.
        /// </summary>
        private void FindSystems()
        {
            if (playerClimbing == null) {
                playerClimbing = GetComponentInParent<PlayerClimbing>();
            }

            if (playerHandHolding == null) {
                playerHandHolding = GetComponent<PlayerHandHolding>();
            }

            if (playerHandDoors == null) {
                playerHandDoors = GetComponent<PlayerHandDoors>();
            }

            if (playerLockpicking == null) {
                playerLockpicking = GetComponent<PlayerLockpicking>();
            }

            if (playerKeys == null) {
                playerKeys = GetComponent<PlayerKeys>();
            }

            if (playerPack == null) {
                playerPack = GetComponent<PlayerPack>();
            }

            if (playerCrossbow == null) {
                playerCrossbow = GetComponent<PlayerCrossbow>();
            }

            if (playerBlackjack == null) {
                playerBlackjack = GetComponent<PlayerBlackjack>();
            }

            if (playerCompass == null) {
                playerCompass = GetComponent<PlayerCompass>();
            }
        }

        /// <summary>
        /// What a hand is busy with, or HandUse.None if it's free. (Two
        /// systems should never both have a hand; if they somehow did,
        /// this names the first in the order below.)
        /// </summary>
        public HandUse GetUse(bool isLeftHand)
        {
            if (IsClimbing(isLeftHand)) {
                return HandUse.Climbing;
            }

            if (IsCarrying(isLeftHand)) {
                return HandUse.Carrying;
            }

            if (IsOnDoor(isLeftHand)) {
                return HandUse.Door;
            }

            if (IsOnLockpicks(isLeftHand)) {
                return HandUse.Lockpicks;
            }

            if (IsOnKeys(isLeftHand)) {
                return HandUse.Keys;
            }

            if (IsOnPack(isLeftHand)) {
                return HandUse.Pack;
            }

            if (IsOnCrossbow(isLeftHand)) {
                return HandUse.Crossbow;
            }

            if (IsOnBlackjack(isLeftHand)) {
                return HandUse.Blackjack;
            }

            if (IsOnCompass(isLeftHand)) {
                return HandUse.Compass;
            }

            return HandUse.None;
        }

        /// <summary>
        /// Whether any system OTHER than asker has this hand - what each
        /// hand system asks before it takes a hand. A system leaves
        /// itself out because it knows its own state already, and often
        /// asks while it's part way through changing it.
        /// </summary>
        public bool IsBusyExcept(bool isLeftHand, HandUse asker)
        {
            return (asker != HandUse.Climbing && IsClimbing(isLeftHand))
                || (asker != HandUse.Carrying && IsCarrying(isLeftHand))
                || (asker != HandUse.Door && IsOnDoor(isLeftHand))
                || (asker != HandUse.Lockpicks && IsOnLockpicks(isLeftHand))
                || (asker != HandUse.Keys && IsOnKeys(isLeftHand))
                || (asker != HandUse.Pack && IsOnPack(isLeftHand))
                || (asker != HandUse.Crossbow && IsOnCrossbow(isLeftHand))
                || (asker != HandUse.Blackjack && IsOnBlackjack(isLeftHand))
                || (asker != HandUse.Compass && IsOnCompass(isLeftHand));
        }

        private bool IsClimbing(bool isLeftHand)
        {
            return _hasClimbing && (isLeftHand ? playerClimbing.IsLeftHandGripping : playerClimbing.IsRightHandGripping);
        }

        private bool IsCarrying(bool isLeftHand)
        {
            return _hasHolding && (isLeftHand ? playerHandHolding.IsLeftHolding : playerHandHolding.IsRightHolding);
        }

        private bool IsOnDoor(bool isLeftHand)
        {
            return _hasDoors && (isLeftHand ? playerHandDoors.IsLeftOnDoor : playerHandDoors.IsRightOnDoor);
        }

        private bool IsOnLockpicks(bool isLeftHand)
        {
            return _hasLockpicking && (isLeftHand ? playerLockpicking.IsLeftBusy : playerLockpicking.IsRightBusy);
        }

        private bool IsOnKeys(bool isLeftHand)
        {
            return _hasKeys && (isLeftHand ? playerKeys.IsLeftBusy : playerKeys.IsRightBusy);
        }

        private bool IsOnPack(bool isLeftHand)
        {
            return _hasPack && (isLeftHand ? playerPack.IsLeftBusy : playerPack.IsRightBusy);
        }

        private bool IsOnCrossbow(bool isLeftHand)
        {
            return _hasCrossbow && (isLeftHand ? playerCrossbow.IsLeftBusy : playerCrossbow.IsRightBusy);
        }

        private bool IsOnBlackjack(bool isLeftHand)
        {
            return _hasBlackjack && (isLeftHand ? playerBlackjack.IsLeftBusy : playerBlackjack.IsRightBusy);
        }

        private bool IsOnCompass(bool isLeftHand)
        {
            return _hasCompass && (isLeftHand ? playerCompass.IsLeftBusy : playerCompass.IsRightBusy);
        }
    }
}
