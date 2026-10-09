using System.Collections.Generic;
using Interaction;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// Makes a prop loot: something worth stealing. It's picked up,
    /// carried and thrown like any other prop (its Grabbable does that),
    /// and can be put into the player's pack (Pack), where it takes up
    /// one of the pack's spaces. The pack is small, so the player
    /// keeps what's worth most and leaves the rest.
    ///
    /// Three kinds, by the two tick boxes:
    /// - Ordinary loot takes one space in the pack and can be taken out
    ///   again.
    /// - Gold (coins, rings - small things) takes no space of its own:
    ///   put anywhere in the pack it goes to the gold space, its value is
    ///   added to the gold there and it's gone.
    /// - The level's objective has a space of its own, apart from the
    ///   rest: putting it in the pack is what lets the player leave.
    ///
    /// No Update(): it does nothing by itself. Self-registers so the
    /// player can ask "is this prop loot?" without a GetComponent call.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class Loot : MonoBehaviour
    {
        // What it's worth.
        [SerializeField] private int value = 10;

        // How many coins are shown over it while it's carried, 1 to 3: a
        // rough guide to its worth, so the player can choose what to keep.
        [SerializeField, Range(1, 3)] private int coinLevel = 1;

        // Small loot that just adds to the gold in the pack.
        [SerializeField] private bool isGold;

        // The item the level is about: packing it lets the player leave.
        [SerializeField] private bool isObjective;

        // Every enabled piece of loot, by its Grabbable.
        private static readonly Dictionary<Grabbable, Loot> _byGrabbable = new();

        /// What it's worth.
        public int Value => value;

        /// How many coins show its worth, 1 to 3.
        public int CoinLevel => coinLevel;

        /// True for small loot that just adds to the pack's gold.
        public bool IsGold => isGold;

        /// True for the item the level is about.
        public bool IsObjective => isObjective;

        /// The prop this loot is.
        public Grabbable Grabbable { get; private set; }

        private void Awake()
        {
            Grabbable = GetComponent<Grabbable>();
        }

        private void OnEnable()
        {
            _byGrabbable[Grabbable] = this;
        }

        private void OnDisable()
        {
            _byGrabbable.Remove(Grabbable);
        }

        /// <summary>
        /// The loot a prop is, or null if it's an ordinary prop. A
        /// dictionary lookup.
        /// </summary>
        public static Loot Find(Grabbable grabbable)
        {
            return _byGrabbable.TryGetValue(grabbable, out Loot loot) ? loot : null;
        }

        /// <summary>
        /// The loot has been turned into gold in the pack: it's gone from
        /// the level. Switched off rather than destroyed - nothing is
        /// destroyed during play - and a level restart brings it back with
        /// the rest of the scene.
        /// </summary>
        public void Collect()
        {
            gameObject.SetActive(false);
        }
    }
}
