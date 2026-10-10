using UnityEditor;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Editor menu command (TeaLeaf > Add Hand Tools To Player) that adds
    /// the crossbow, the blackjack and the compass to the player's Hands
    /// object - each only if it's missing. Each one's Reset() fills in
    /// its own references, and PlayerController and PlayerHandState find
    /// them by themselves when the game starts.
    ///
    /// Undone with Ctrl+Z. In an Editor folder, so it never ends up in a
    /// build.
    /// </summary>
    public static class PlayerHandTools
    {
        [MenuItem("TeaLeaf/Add Hand Tools To Player")]
        private static void Add()
        {
            PlayerHandVisuals hands = Object.FindFirstObjectByType<PlayerHandVisuals>();

            if (hands == null) {
                Debug.LogWarning("PlayerHandTools: no PlayerHandVisuals in the scene - add the components to the Hands object by hand.");
                return;
            }

            int added = 0;
            added += Ensure<PlayerCrossbow>(hands.gameObject);
            added += Ensure<PlayerBlackjack>(hands.gameObject);
            added += Ensure<PlayerCompass>(hands.gameObject);

            Debug.Log($"PlayerHandTools: added {added} component(s) to '{hands.name}'.", hands);
            Selection.activeGameObject = hands.gameObject;
        }

        /// <summary>
        /// Adds a component to target if it has none. Returns how many
        /// were added (0 or 1).
        /// </summary>
        private static int Ensure<T>(GameObject target) where T : Component
        {
            if (target.GetComponent<T>() != null) {
                return 0;
            }

            Undo.AddComponent<T>(target);
            return 1;
        }
    }
}
