using System.Collections.Generic;
using Interaction;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// Makes a prop a key: something found in the level that opens the
    /// keyed lock with the same key id (Interaction.Door's Key Id). Keys
    /// are told apart by colour. It's picked up and carried like any prop
    /// (its Grabbable does that); put into the pack, it goes onto the
    /// keyring there and is gone from the level - from then on it's the
    /// keyring that's carried to locks (Keyring, Player.PlayerKeys).
    ///
    /// No Update(): it does nothing by itself. Self-registers so the
    /// player can ask "is this prop a key?" without a GetComponent call.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class Key : MonoBehaviour
    {
        // Which locks it opens: the same text as the door's Key Id.
        [SerializeField] private string keyId;

        // Its colour, which is also how it's shown on the keyring.
        [SerializeField] private Color color = Color.red;

        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        // Every enabled key, by its Grabbable.
        private static readonly Dictionary<Grabbable, Key> _byGrabbable = new();

        /// Which locks it opens.
        public string KeyId => keyId;

        /// Its colour.
        public Color Color => color;

        /// The prop this key is.
        public Grabbable Grabbable { get; private set; }

        private void Awake()
        {
            Grabbable = GetComponent<Grabbable>();

            // The prop is drawn in the key's colour: laid over its
            // material's own (which is shared, and left alone). Once, at
            // load.
            MaterialPropertyBlock block = new();
            block.SetColor(_baseColorId, color);

            foreach (MeshRenderer keyRenderer in GetComponentsInChildren<MeshRenderer>()) {
                keyRenderer.SetPropertyBlock(block);
            }
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
        /// The key a prop is, or null if it isn't one. A dictionary
        /// lookup.
        /// </summary>
        public static Key Find(Grabbable grabbable)
        {
            return _byGrabbable.TryGetValue(grabbable, out Key key) ? key : null;
        }

        /// <summary>
        /// The key has gone onto the keyring: it's gone from the level.
        /// Switched off rather than destroyed - nothing is destroyed
        /// during play - and a level restart brings it back with the rest
        /// of the scene.
        /// </summary>
        public void Collect()
        {
            gameObject.SetActive(false);
        }
    }
}
