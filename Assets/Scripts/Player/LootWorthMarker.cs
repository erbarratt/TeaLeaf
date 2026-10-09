using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// The coins that hover over a piece of loot while a hand carries it:
    /// one, two or three in a row, a rough guide to what it's worth, so
    /// the player can choose what to keep.
    ///
    /// In-world UI: flat discs that always face the player and are drawn
    /// on top of everything (an overlay material), like the hand reticles.
    ///
    /// A plain C# class rather than a MonoBehaviour: it has no Update() of
    /// its own. PlayerPack makes one per hand at load and ticks it. Its
    /// three discs exist from the start and are only shown or hidden -
    /// nothing is created while playing.
    /// </summary>
    public class LootWorthMarker
    {
        // The most coins ever shown.
        public const int MaxCoins = 3;

        private readonly Transform _root;
        private readonly Transform[] _coins = new Transform[MaxCoins];
        private readonly MeshRenderer[] _renderers = new MeshRenderer[MaxCoins];
        private readonly float _spacing;

        // How many coins are showing now (0 = hidden), so the renderers
        // are only touched when the number changes.
        private int _shown;

        /// <summary>
        /// Builds the marker's objects under parent. mesh is one coin (a
        /// disc facing its own +Z) and material its overlay material -
        /// both shared by every marker, and owned by the caller. spacing
        /// is the distance between the middles of neighbouring coins.
        /// </summary>
        public LootWorthMarker(string name, Transform parent, Mesh mesh, Material material, float spacing)
        {
            _spacing = spacing;

            GameObject root = new(name);
            _root = root.transform;
            _root.SetParent(parent, false);

            for (int i = 0; i < MaxCoins; i++) {
                GameObject coin = new($"Coin {i + 1}");
                _coins[i] = coin.transform;
                _coins[i].SetParent(_root, false);
                coin.AddComponent<MeshFilter>().sharedMesh = mesh;

                MeshRenderer coinRenderer = coin.AddComponent<MeshRenderer>();
                coinRenderer.sharedMaterial = material;
                coinRenderer.shadowCastingMode = ShadowCastingMode.Off;
                coinRenderer.receiveShadows = false;
                coinRenderer.enabled = false;
                _renderers[i] = coinRenderer;
            }
        }

        /// <summary>
        /// Shows coins coins (1 to 3) at worldPoint, facing viewerPosition
        /// (the player's head) - or hides the marker for 0. Called every
        /// frame for each hand.
        /// </summary>
        public void Tick(int coins, Vector3 worldPoint, Vector3 viewerPosition)
        {
            SetShown(Mathf.Clamp(coins, 0, MaxCoins));

            if (_shown == 0) {
                return;
            }

            // The discs' fronts (local +Z) towards the viewer. Position
            // and rotation together: one transform update, not two.
            _root.SetPositionAndRotation(worldPoint, Quaternion.LookRotation(viewerPosition - worldPoint));
        }

        /// <summary>
        /// Shows the first count coins, in a row centred on the marker,
        /// and hides the rest. Only when the number changes.
        /// </summary>
        private void SetShown(int count)
        {
            if (count == _shown) {
                return;
            }

            _shown = count;

            for (int i = 0; i < MaxCoins; i++) {
                _renderers[i].enabled = i < count;

                // Spread either side of the middle: for three coins, one
                // spacing left, the middle, one spacing right.
                _coins[i].localPosition = new Vector3((i - (count - 1) * 0.5f) * _spacing, 0f, 0f);
            }
        }
    }
}
