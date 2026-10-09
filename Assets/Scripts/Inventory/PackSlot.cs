using Interaction;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// One space in the pack's grid, as something a hand ray can target:
    /// a trigger box the pack makes over the space. The box is only
    /// switched on while there's an item in the space, so the reticle
    /// shows on what can be taken out and an empty space never gets in a
    /// hand ray's way. The stored item's own colliders are off while it's
    /// in the pack - this stands in for them.
    ///
    /// Made by Pack at load, never placed by hand. What taking an item
    /// out means is Player.PlayerPack's business.
    /// </summary>
    public class PackSlot : MonoBehaviour, IHandTarget
    {
        private Collider _collider;

        /// The pack this space is in.
        public Pack Pack { get; private set; }

        /// Which of the pack's grid spaces this is.
        public int Index { get; private set; }

        /// <summary>
        /// Called by the pack straight after it adds the component. The
        /// space starts empty, so its box starts switched off.
        /// </summary>
        public void SetUp(Pack pack, int index, Collider slotCollider)
        {
            Pack = pack;
            Index = index;
            _collider = slotCollider;
            _collider.enabled = false;

            // OnEnable() has already run (it runs as the component is
            // added), before there was a collider to register.
            HandTargetRegistry.Register(_collider, this);
        }

        private void OnEnable()
        {
            if (_collider != null) {
                HandTargetRegistry.Register(_collider, this);
            }
        }

        private void OnDisable()
        {
            if (_collider != null) {
                HandTargetRegistry.Unregister(_collider);
            }
        }

        /// <summary>
        /// Switches the space's box on (something to take out) or off.
        /// </summary>
        public void SetTargetable(bool isTargetable)
        {
            _collider.enabled = isTargetable;
        }
    }
}
