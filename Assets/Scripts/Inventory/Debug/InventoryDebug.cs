using System;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// Debug-only stand-in for the wrist display: writes the inventory to
    /// the Console each time it changes - the loot total, how many pieces,
    /// whether the objective is held, the tools owned and the bolt counts.
    ///
    /// Listens to PlayerInventory.Changed, so it does nothing between
    /// changes. On the Player root, with the inventory.
    /// </summary>
    public class InventoryDebug : MonoBehaviour
    {
        [SerializeField] private PlayerInventory playerInventory;

        /// <summary>
        /// Editor-only: runs when the component is added.
        /// </summary>
        private void Reset()
        {
            playerInventory = GetComponent<PlayerInventory>();
        }

        private void OnEnable()
        {
            if (playerInventory != null) {
                playerInventory.Changed += OnChanged;
            }
        }

        private void OnDisable()
        {
            if (playerInventory != null) {
                playerInventory.Changed -= OnChanged;
            }
        }

        /// <summary>
        /// Builds one line from everything in the inventory. It makes a
        /// few strings each time, which is fine here: only on a change,
        /// and only in a debug script.
        /// </summary>
        private void OnChanged()
        {
            string tools = "";

            foreach (ToolType tool in Enum.GetValues(typeof(ToolType))) {
                if (playerInventory.Owns(tool)) {
                    tools += $" {tool}";
                }
            }

            string bolts = "";

            foreach (BoltType bolt in Enum.GetValues(typeof(BoltType))) {
                bolts += $" {bolt} {playerInventory.GetBoltCount(bolt)}";
            }

            Debug.Log(
                $"[Inventory] loot {playerInventory.LootTotal} ({playerInventory.LootCount} pieces)"
                + $", objective {(playerInventory.HasObjective ? "yes" : "no")}"
                + $", tools:{(tools.Length > 0 ? tools : " none")}, bolts:{bolts}");
        }
    }
}
