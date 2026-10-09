using Interaction;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// The keyring: one ring with every key the player has collected
    /// hanging from it, each a bar in its key's colour. It lives in the
    /// pack's keyring space, where it shows what's been collected; to open
    /// a lock it's taken out, carried to the lock and, if the right key
    /// is on it, put in and turned (Player.PlayerKeys).
    ///
    /// It holds the keys' ids and colours and draws itself. It doesn't
    /// move itself about: whoever has it (the pack, a hand, a lock) makes
    /// it their child.
    ///
    /// Its own axes: X to the viewer's right, Y up, Z away from the
    /// viewer. Its origin is the middle of the ring. Small enough to fit
    /// the pack's space at its real size, so it's never scaled.
    ///
    /// Made by the Pack at load, never placed by hand. No Update().
    /// </summary>
    public class Keyring : MonoBehaviour
    {
        // The most keys it can show. More than a level should need.
        public const int MaxKeys = 6;

        // The ring: a square outline this many metres across, made of
        // bars this thick.
        private const float RingSize = 0.032f;
        private const float RingThickness = 0.005f;

        // Each key: a bar this wide, long and thick, hanging below the
        // ring, with this far between neighbours' middles.
        private const float KeyWidth = 0.007f;
        private const float KeyLength = 0.036f;
        private const float KeyThickness = 0.004f;
        private const float KeySpacing = 0.011f;

        // The key in a lock: a bar from the ring's middle into the door.
        private const float StemLength = 0.035f;

        private static readonly Color32 _ringColor = new(150, 150, 155, 255);
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        // The ids and colours of the keys collected, in the order they
        // were found. Fixed arrays, so collecting one creates nothing.
        private readonly string[] _ids = new string[MaxKeys];
        private readonly Color[] _colors = new Color[MaxKeys];
        private readonly MeshRenderer[] _keyRenderers = new MeshRenderer[MaxKeys];

        private MeshRenderer _stemRenderer;
        private Mesh _ringMesh;

        // One block reused for every colour change (a renderer copies
        // what it's given).
        private MaterialPropertyBlock _block;

        /// How many keys are on the ring.
        public int Count { get; private set; }

        /// <summary>
        /// Makes the keyring's parts. Called once by the pack, with the
        /// material it draws everything in and its unit block mesh (a
        /// cube one metre across, scaled to size by each part).
        /// </summary>
        public void Build(Material material, Mesh blockMesh)
        {
            _block = new MaterialPropertyBlock();

            // The ring: four bars round a square, in one mesh.
            LockMeshBuilder builder = new();
            float half = RingSize * 0.5f;
            Vector3 across = new(RingSize + RingThickness, RingThickness, RingThickness);
            Vector3 upright = new(RingThickness, RingSize + RingThickness, RingThickness);

            builder.Box(new Vector3(0f, half, 0f), across, Quaternion.identity, _ringColor);
            builder.Box(new Vector3(0f, -half, 0f), across, Quaternion.identity, _ringColor);
            builder.Box(new Vector3(-half, 0f, 0f), upright, Quaternion.identity, _ringColor);
            builder.Box(new Vector3(half, 0f, 0f), upright, Quaternion.identity, _ringColor);

            _ringMesh = builder.ToMesh("Keyring Ring");
            AddPart("Ring", _ringMesh, material, Vector3.zero, Vector3.one);

            // The keys: all made now and hidden, shown as they're
            // collected. In a row below the ring, centred under it.
            float keyY = -half - KeyLength * 0.5f;

            for (int i = 0; i < MaxKeys; i++) {
                float x = (i - (MaxKeys - 1) * 0.5f) * KeySpacing;

                _keyRenderers[i] = AddPart($"Key {i + 1}", blockMesh, material, new Vector3(x, keyY, 0f), new Vector3(KeyWidth, KeyLength, KeyThickness));
                _keyRenderers[i].enabled = false;
            }

            _stemRenderer = AddPart("Key In Lock", blockMesh, material, new Vector3(0f, 0f, StemLength * 0.5f), new Vector3(KeyWidth, KeyThickness, StemLength));
            _stemRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            // A mesh made from code isn't cleaned up with its object.
            if (_ringMesh != null) {
                Destroy(_ringMesh);
            }
        }

        /// <summary>
        /// A child object drawing mesh at a place and size in the
        /// keyring's own space, with no shadows.
        /// </summary>
        private MeshRenderer AddPart(string partName, Mesh mesh, Material material, Vector3 localPosition, Vector3 localScale)
        {
            GameObject part = new(partName) { layer = gameObject.layer };
            part.transform.SetParent(transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterial = material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return partRenderer;
        }

        /// <summary>
        /// Whether the key with this id is on the ring. A handful of
        /// text comparisons at most, and only asked at a lock.
        /// </summary>
        public bool Has(string keyId)
        {
            return IndexOf(keyId) >= 0;
        }

        /// <summary>
        /// The colour of the key with this id. False if it isn't on the
        /// ring.
        /// </summary>
        public bool TryGetColor(string keyId, out Color color)
        {
            int index = IndexOf(keyId);
            color = index >= 0 ? _colors[index] : default;
            return index >= 0;
        }

        private int IndexOf(string keyId)
        {
            if (string.IsNullOrEmpty(keyId)) {
                return -1;
            }

            for (int i = 0; i < Count; i++) {
                if (_ids[i] == keyId) {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Puts a key on the ring: its bar shows, in its colour. False if
        /// the ring is full. A key already on the ring (the same id found
        /// twice) is simply counted as added.
        /// </summary>
        public bool Add(string keyId, Color color)
        {
            if (Has(keyId)) {
                return true;
            }

            if (Count >= MaxKeys) {
                return false;
            }

            _ids[Count] = keyId;
            _colors[Count] = color;

            SetColor(_keyRenderers[Count], color);
            _keyRenderers[Count].enabled = true;
            Count++;
            return true;
        }

        /// <summary>
        /// Shows or hides the key in the lock: the bar that runs from the
        /// ring into the door, in the colour of the key being used.
        /// </summary>
        public void ShowKeyInLock(bool isShown, Color color)
        {
            if (isShown) {
                SetColor(_stemRenderer, color);
            }

            _stemRenderer.enabled = isShown;
        }

        /// <summary>
        /// Gives one renderer its own colour, laid over the shared
        /// material's (which is left alone).
        /// </summary>
        private void SetColor(MeshRenderer target, Color color)
        {
            _block.SetColor(_baseColorId, color);
            target.SetPropertyBlock(_block);
        }
    }
}
