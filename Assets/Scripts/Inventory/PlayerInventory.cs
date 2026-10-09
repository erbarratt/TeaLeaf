using System;
using Core;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// What the player has: the worth of the loot in their pack, whether they have
    /// the level's objective, which tools they own and how many bolts of
    /// each kind. Data only - it doesn't show anything, play anything or
    /// decide how things are picked up. Other systems change it through
    /// the methods here and listen to Changed.
    ///
    /// On the Player root. One per scene, reachable through Instance, and
    /// made again with the scene on a level restart - so a restart empties
    /// the pack, like everything else.
    ///
    /// No Update(): it only does anything when asked.
    /// </summary>
    public class PlayerInventory : MonoBehaviour
    {
        [Header("Tools Owned At The Start")]
        [SerializeField] private bool ownsBlackjack;
        [SerializeField] private bool ownsCrossbow;

        [Header("Bolts At The Start")]
        [SerializeField] private int waterBolts;
        [SerializeField] private int noisemakerBolts;
        [SerializeField] private int ropeBolts;

        // How many kinds of tool and bolt there are, counted once from the
        // enums rather than written as numbers that could fall behind.
        private static readonly int _toolTypeCount = Enum.GetValues(typeof(ToolType)).Length;
        private static readonly int _boltTypeCount = Enum.GetValues(typeof(BoltType)).Length;

        // Indexed by (int)ToolType and (int)BoltType.
        private bool[] _ownedTools;
        private int[] _boltCounts;

        /// The scene's inventory, so other systems can reach it without a
        /// scene search. Null if the scene has none.
        public static PlayerInventory Instance { get; private set; }

        /// The value of all the loot in the pack, gold included.
        public int LootTotal { get; private set; }

        /// How many pieces of loot have gone into the pack and not come out.
        public int LootCount { get; private set; }

        /// True once the level's objective is in the pack.
        public bool HasObjective { get; private set; }

        /// Raised after anything in the inventory changes - for the wrist
        /// display and the debug readout, so neither has to check every
        /// frame. An instance event: the inventory is destroyed with the
        /// scene on a restart, and its subscribers go with it.
        public event Action Changed;

        private void Awake()
        {
            Instance = this;

            _ownedTools = new bool[_toolTypeCount];
            _ownedTools[(int)ToolType.Blackjack] = ownsBlackjack;
            _ownedTools[(int)ToolType.Crossbow] = ownsCrossbow;

            _boltCounts = new int[_boltTypeCount];
            _boltCounts[(int)BoltType.Water] = waterBolts;
            _boltCounts[(int)BoltType.Noisemaker] = noisemakerBolts;
            _boltCounts[(int)BoltType.Rope] = ropeBolts;
        }

        private void OnDestroy()
        {
            if (Instance == this) {
                Instance = null;
            }
        }

        /// <summary>
        /// Adds one piece of loot worth value to the total: it has gone
        /// into the pack.
        /// </summary>
        public void AddLoot(int value)
        {
            LootTotal += value;
            LootCount++;
            Changed?.Invoke();
        }

        /// <summary>
        /// Takes one piece of loot worth value off the total: it has been
        /// taken out of the pack again.
        /// </summary>
        public void RemoveLoot(int value)
        {
            LootTotal -= value;
            LootCount--;
            Changed?.Invoke();
        }

        /// <summary>
        /// The level's objective is in the pack. Tells the level, which
        /// then watches the exit zones: ending the level is the level
        /// manager's business, not the inventory's.
        /// </summary>
        public void TakeObjective()
        {
            HasObjective = true;

            LevelManager levelManager = LevelManager.Instance;

            if (levelManager != null) {
                levelManager.SetObjectiveCarried(true);
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Whether the player owns a tool.
        /// </summary>
        public bool Owns(ToolType tool)
        {
            return _ownedTools[(int)tool];
        }

        /// <summary>
        /// Gives the player a tool (a pickup in the level, later).
        /// </summary>
        public void GiveTool(ToolType tool)
        {
            if (_ownedTools[(int)tool]) {
                return;
            }

            _ownedTools[(int)tool] = true;
            Changed?.Invoke();
        }

        /// <summary>
        /// How many bolts of one kind the player has.
        /// </summary>
        public int GetBoltCount(BoltType bolt)
        {
            return _boltCounts[(int)bolt];
        }

        /// <summary>
        /// Adds bolts of one kind (a pickup in the level, later).
        /// </summary>
        public void AddBolts(BoltType bolt, int count)
        {
            _boltCounts[(int)bolt] += count;
            Changed?.Invoke();
        }

        /// <summary>
        /// Takes one bolt of a kind to load the crossbow with. False, and
        /// nothing taken, if there are none left.
        /// </summary>
        public bool TryUseBolt(BoltType bolt)
        {
            if (_boltCounts[(int)bolt] <= 0) {
                return false;
            }

            _boltCounts[(int)bolt]--;
            Changed?.Invoke();
            return true;
        }
    }
}
