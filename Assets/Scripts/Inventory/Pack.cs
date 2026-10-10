using Interaction;
using TMPro;
using UnityEngine;

namespace Inventory
{
    /// What happened to a piece of loot offered to the pack.
    public enum PackResult
    {
        // Put into the grid space it was held over.
        Stored,
        // Small loot: added to the gold, and gone.
        Gold,
        // The level's objective: put in its own space.
        Objective,
        // The space it was held over is taken (or it wasn't over the
        // pack at all): it wasn't put in.
        NoRoom
    }

    /// <summary>
    /// The player's pack: a flat bag with spaces in it that loot is
    /// physically put into. It only holds so much, so the player keeps
    /// what's worth most and leaves the rest.
    ///
    /// Laid out as a board facing the player. Along the top, side by side,
    /// a space for the level's objective and one for the keyring (keys
    /// aren't built yet: the space is there, empty). Under them a grid of
    /// spaces (columns by rows, 3 by 3 to start with). The grid's top left
    /// space is the gold space: small loot (coins, rings) put anywhere in
    /// the pack goes there, adds to the gold and is gone. Everything else
    /// takes exactly one space. The player chooses which by holding the
    /// loot over it: a free space lights up, one with something in it
    /// doesn't, and loot let go of over a lit space snaps into it and
    /// shrinks to fit. It can be taken out again: it grows back to full
    /// size first, then a hand takes it.
    ///
    /// The pack is the physical thing and its contents. What the player
    /// is worth altogether is kept by PlayerInventory, which the pack
    /// tells as things go in and out. Summoning it, and the hands putting
    /// things in and taking them out, are Player.PlayerPack's.
    ///
    /// This object's own axes: X to the player's right, Y up the board, Z
    /// away from the player. Its origin is the middle of the board.
    /// Unscaled. It collides with nothing: the only colliders are trigger
    /// boxes over spaces that have something in them, for the hand rays.
    ///
    /// One in the scene, built once at load. No Update(): PlayerPack calls
    /// Tick() while it's out.
    /// </summary>
    public class Pack : MonoBehaviour
    {
        /// One thing in the pack. A class, one made per space at load, so
        /// nothing is created while playing.
        private class Stored
        {
            // The loot, or null while this space's record is unused.
            public Loot loot;

            // Its parent and local scale out in the world, put back when
            // it's taken out.
            public Transform originalParent;
            public Vector3 originalScale;

            // Its scale at full size as a child of the pack, and how much
            // of that it's shown at when fully shrunk to fit its space.
            public Vector3 fullScale;
            public float fitFactor;

            // Where the middle of its space is, in the pack's own space.
            public Vector3 spaceCentre;

            // 1 = shrunk into its space, 0 = full size. Moves towards 1
            // while it's being put away and towards 0 while it's being
            // taken out.
            public float shrink;
            public bool isLeaving;
        }

        [Header("Grid")]

        // How many spaces across and down. The top left one is the gold
        // space.
        [SerializeField, Min(1)] private int columns = 3;
        [SerializeField, Min(1)] private int rows = 3;

        // Each space's size, the gap between spaces, and how thick the
        // board is, in metres.
        [SerializeField] private float cellSize = 0.09f;
        [SerializeField] private float cellGap = 0.012f;
        [SerializeField] private float depth = 0.05f;

        // How much of its space a stored item fills, 0-1.
        [SerializeField] private float fit = 0.8f;

        // Seconds an item takes to shrink into its space, or to grow back
        // to full size when it's taken out.
        [SerializeField] private float shrinkDuration = 0.12f;

        [Header("Putting Things In")]

        // Loot let go of within this many metres of the pack's middle
        // goes into it.
        [SerializeField] private float storeRadius = 0.24f;

        [Header("Gold")]

        // The gold space shows a pile that grows with the gold in it:
        // this much gold is a full pile.
        [SerializeField] private int goldForFullPile = 20;

        // The number of gold pieces, written over the gold space: how
        // tall its figures are in metres, and its colour.
        [SerializeField] private float goldNumberHeight = 0.03f;
        [SerializeField] private Color goldNumberColor = Color.white;

        [Header("Colours")]
        [SerializeField] private Color boardColor = new(0.3f, 0.21f, 0.13f, 1f);
        [SerializeField] private Color spaceColor = new(0.17f, 0.12f, 0.08f, 1f);
        [SerializeField] private Color goldSpaceColor = new(0.45f, 0.36f, 0.12f, 1f);
        [SerializeField] private Color objectiveSpaceColor = new(0.15f, 0.25f, 0.4f, 1f);
        [SerializeField] private Color keyringSpaceColor = new(0.3f, 0.3f, 0.32f, 1f);
        [SerializeField] private Color goldColor = new(0.9f, 0.7f, 0.15f, 1f);
        [SerializeField] private Color highlightColor = new(0.95f, 0.9f, 0.6f, 1f);

        // The least light the pack is ever shown in, 0-1: it has to be
        // seen in a dark room.
        [SerializeField] private float minLight = 0.4f;

        // The layer the spaces' trigger boxes go on, so hand rays hit
        // them.
        private const string InteractableLayerName = "Interactable";

        // How far out of the board's face the spaces' panels stand, in
        // metres - enough not to flicker against it.
        private const float Proud = 0.002f;

        // How far the spaces' trigger boxes reach out of the board
        // towards the player, in metres.
        private const float SlotReach = 0.1f;

        // The gold space's index in the grid: its top left space.
        private const int GoldCell = 0;

        /// "No space", where a space's number is asked for: 0 and up are
        /// the grid's spaces, ObjectiveSpace the objective's and
        /// KeyringSpace the keyring's.
        public const int NoSpace = -2;
        public const int ObjectiveSpace = -1;
        public const int KeyringSpace = -3;

        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private int _cellCount;

        // Where each grid space's middle is, in the pack's own space, on
        // the board's face.
        private Vector3[] _cellCentres;

        // One record per grid space, used while an item sits there, and
        // one for the objective's space.
        private Stored[] _stored;
        private Stored _objective;

        private PackSlot[] _slots;

        // The block that lights up the space carried loot is held over,
        // the space it's on now (NoSpace while hidden), and how wide the
        // objective's space is.
        private MeshRenderer _highlight;

        // The gold number. Null if the project has no TextMeshPro
        // resources to draw text with - see BuildGoldNumber().
        private TextMeshPro _goldNumber;
        private int _highlightedSpace = NoSpace;
        private float _wideWidth;

        // The keyring's space: where its middle is, and its trigger box.
        private Vector3 _keyringCentre;
        private PackSlot _keyringSlot;

        private Transform _goldPile;
        private MeshRenderer _goldPileRenderer;

        private Mesh _boardMesh;
        private Mesh _blockMesh;
        private Material _material;

        // The loot that has just finished growing back to full size and
        // is ready for a hand - see TryPopTaken().
        private Loot _taken;

        /// True while the pack is out.
        public bool IsOpen { get; private set; }

        /// The gold in the gold space.
        public int Gold { get; private set; }

        /// The keyring. Made with the pack; it starts in its space here.
        public Keyring Keyring { get; private set; }

        /// True while the keyring is in its space in the pack, rather
        /// than in a hand or a lock.
        public bool IsKeyringHome { get; private set; }

        /// True once the level's objective is in its space.
        public bool HasObjective => _objective.loot is not null;

        private void Awake()
        {
            _cellCount = columns * rows;
            _cellCentres = new Vector3[_cellCount];
            _stored = new Stored[_cellCount];
            _slots = new PackSlot[_cellCount];
            _objective = new Stored();

            for (int i = 0; i < _cellCount; i++) {
                _stored[i] = new Stored();
            }

            _material = LockMeshBuilder.CreateMaterial(false, minLight);

            // Moved about by the hand it's on. A Rigidbody tells physics
            // the trigger boxes move, which it handles far more cheaply
            // than colliders it believes are fixed; kinematic, and with
            // only triggers, it pushes nothing.
            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            Build();
            UpdateGoldPile();

            IsOpen = gameObject.activeSelf;
        }

        private void OnDestroy()
        {
            // Meshes and a material made from code aren't cleaned up with
            // their objects.
            Destroy(_boardMesh);
            Destroy(_blockMesh);
            Destroy(_material);
        }

        /// <summary>
        /// Makes the pack: the board, a panel for each space, a trigger
        /// box over each grid space (but the gold one), the gold pile and
        /// the highlight. Works out where every space is as it goes.
        ///
        /// The board is as wide as the grid. From the top: one row of two
        /// wide spaces (objective on the left, keyring on the right), then
        /// the grid's rows.
        /// </summary>
        private void Build()
        {
            float step = cellSize + cellGap;
            float width = columns * cellSize + (columns + 1) * cellGap;
            float height = (rows + 1) * cellSize + (rows + 2) * cellGap;
            float faceZ = -depth * 0.5f;
            Vector3 panelSize = new(cellSize, cellSize, Proud * 2f);

            LockMeshBuilder builder = new();
            builder.Box(Vector3.zero, new Vector3(width, height, depth), Quaternion.identity, boardColor);

            // The top row: two spaces sharing the board's width.
            float topY = height * 0.5f - cellGap - cellSize * 0.5f;
            float wideWidth = (width - cellGap * 3f) * 0.5f;
            float wideX = (wideWidth + cellGap) * 0.5f;
            Vector3 wideSize = new(wideWidth, cellSize, Proud * 2f);

            _objective.spaceCentre = new Vector3(-wideX, topY, faceZ);
            _keyringCentre = new Vector3(wideX, topY, faceZ);
            builder.Box(_objective.spaceCentre, wideSize, Quaternion.identity, objectiveSpaceColor);
            builder.Box(_keyringCentre, wideSize, Quaternion.identity, keyringSpaceColor);

            // The grid, row by row from the top, left to right.
            float firstX = -width * 0.5f + cellGap + cellSize * 0.5f;
            float firstY = topY - step;

            for (int i = 0; i < _cellCount; i++) {
                int column = i % columns;
                int row = i / columns;

                _cellCentres[i] = new Vector3(firstX + column * step, firstY - row * step, faceZ);
                _stored[i].spaceCentre = _cellCentres[i];

                builder.Box(_cellCentres[i], panelSize, Quaternion.identity, i == GoldCell ? goldSpaceColor : spaceColor);
            }

            _boardMesh = builder.ToMesh("Pack Board");
            AddPart("Board", _boardMesh, Vector3.zero);

            // One small block mesh, shared by the gold pile and the
            // highlight: a cube one metre across, scaled to size by each.
            LockMeshBuilder blockBuilder = new();
            blockBuilder.Box(Vector3.zero, Vector3.one, Quaternion.identity, Color.white);
            _blockMesh = blockBuilder.ToMesh("Pack Block");

            _goldPileRenderer = AddPart("Gold Pile", _blockMesh, _cellCentres[GoldCell]);
            _goldPile = _goldPileRenderer.transform;
            Tint(_goldPileRenderer, goldColor);

            BuildGoldNumber();

            // The highlight: one flat block, moved onto whichever space
            // is lit - see ShowHighlight().
            _highlight = AddPart("Highlight", _blockMesh, Vector3.zero);
            _highlight.enabled = false;
            Tint(_highlight, highlightColor);
            _wideWidth = wideWidth;

            // The keyring, in its space. It shares the pack's material
            // and block mesh.
            GameObject keyringObject = new("Keyring") { layer = gameObject.layer };
            Keyring = keyringObject.AddComponent<Keyring>();
            Keyring.Build(_material, _blockMesh);

            int layer = LayerMask.NameToLayer(InteractableLayerName);

            // The keyring's trigger box, like a grid space's but as wide
            // as its space - switched on while the keyring is here with a
            // key on it.
            _keyringSlot = AddSlot("Slot Keyring", KeyringSpace, _keyringCentre, wideWidth, layer);

            for (int i = 0; i < _cellCount; i++) {
                if (i == GoldCell) {
                    continue;
                }

                _slots[i] = AddSlot($"Slot {i}", i, _cellCentres[i], cellSize, layer);
            }

            // Now its trigger box exists, the keyring can go in its space.
            ReturnKeyring();
        }

        /// <summary>
        /// A space's trigger box: over the space, reaching out of the
        /// board towards the player, where what's stored there is. It
        /// starts switched off (PackSlot.SetUp()).
        /// </summary>
        private PackSlot AddSlot(string slotName, int index, Vector3 centre, float width, int layer)
        {
            GameObject slotObject = new(slotName) { layer = layer >= 0 ? layer : gameObject.layer };
            slotObject.transform.SetParent(transform, false);
            slotObject.transform.localPosition = centre;

            BoxCollider box = slotObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0f, -SlotReach * 0.5f);
            box.size = new Vector3(width, cellSize, SlotReach);

            PackSlot slot = slotObject.AddComponent<PackSlot>();
            slot.SetUp(this, index, box);
            return slot;
        }

        /// <summary>
        /// Puts the keyring in its space in the pack - where it starts,
        /// and where it comes back to from a hand or a lock. It can be
        /// taken out again if there's a key on it.
        /// </summary>
        public void ReturnKeyring()
        {
            Transform ring = Keyring.transform;
            ring.SetParent(transform, false);

            // Standing a little out of the board, and a little above the
            // middle of its space: its keys hang down from the ring.
            ring.SetLocalPositionAndRotation(_keyringCentre + new Vector3(0f, cellSize * 0.2f, -0.008f), Quaternion.identity);
            ring.localScale = Vector3.one;

            IsKeyringHome = true;
            _keyringSlot.SetTargetable(Keyring.Count > 0);
        }

        /// <summary>
        /// The keyring is being taken out of the pack: whoever takes it
        /// makes it their own child. False if it isn't here, or has no
        /// keys on it.
        /// </summary>
        public bool TakeKeyring()
        {
            if (!IsOpen || !IsKeyringHome || Keyring.Count == 0) {
                return false;
            }

            IsKeyringHome = false;
            _keyringSlot.SetTargetable(false);
            return true;
        }

        /// <summary>
        /// Lights the keyring's space for a key held near the pack, as
        /// Hover() does a grid space for loot: a key goes onto the
        /// keyring wherever over the pack it's let go of. Returns
        /// KeyringSpace, or NoSpace if it's out of reach.
        /// </summary>
        public int HoverKey(Vector3 worldPoint)
        {
            int space = IsInReach(worldPoint) ? KeyringSpace : NoSpace;
            ShowHighlight(space);
            return space;
        }

        /// <summary>
        /// Puts a key onto the keyring, if it's let go of within the
        /// pack's reach (and the ring isn't full): the key prop is gone,
        /// and its coloured bar shows on the ring - wherever the ring is
        /// just now. False if it wasn't taken.
        /// </summary>
        public bool TryStoreKey(Key key, Vector3 worldPoint)
        {
            if (!IsInReach(worldPoint) || !Keyring.Add(key.KeyId, key.Color)) {
                return false;
            }

            ShowHighlight(NoSpace);
            key.Collect();

            // Now there's something on it to take out.
            if (IsKeyringHome) {
                _keyringSlot.SetTargetable(true);
            }

            return true;
        }

        /// <summary>
        /// A child object drawing mesh at a place in the pack's own space,
        /// with no shadows.
        /// </summary>
        /// <summary>
        /// Makes the gold number: a line of text (TextMeshPro, Unity's
        /// text drawing) in the top of the gold space, standing just
        /// proud of the gold pile and facing the player. TextMeshPro needs
        /// its font and shader, which are imported into the project once
        /// from the editor's menu (Window > TextMeshPro > Import TMP
        /// Essential Resources); without them there is no number, and a
        /// warning says why.
        /// </summary>
        private void BuildGoldNumber()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") == null) {
                Debug.LogWarning("Pack: no TextMeshPro resources in the project, so the gold has no number. Import them from Window > TextMeshPro > Import TMP Essential Resources.", this);
                return;
            }

            GameObject number = new("Gold Number") { layer = gameObject.layer };
            number.transform.SetParent(transform, false);

            // In front of the pile (the player looks along the pack's +Z,
            // so nearer the player is -Z), in the top third of the space.
            number.transform.localPosition = _cellCentres[GoldCell] + new Vector3(0f, cellSize * 0.3f, -0.035f);

            _goldNumber = number.AddComponent<TextMeshPro>();
            _goldNumber.rectTransform.sizeDelta = new Vector2(cellSize, cellSize * 0.4f);
            _goldNumber.alignment = TextAlignmentOptions.Center;
            _goldNumber.textWrappingMode = TextWrappingModes.NoWrap;
            _goldNumber.color = goldNumberColor;

            // A font size of 1 draws figures about a tenth of a metre
            // tall, so the size is ten times the height wanted.
            _goldNumber.fontSize = goldNumberHeight * 10f;

            MeshRenderer numberRenderer = _goldNumber.renderer as MeshRenderer;

            if (numberRenderer != null) {
                numberRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                numberRenderer.receiveShadows = false;
            }
        }

        private MeshRenderer AddPart(string partName, Mesh mesh, Vector3 localPosition)
        {
            GameObject part = new(partName) { layer = gameObject.layer };
            part.transform.SetParent(transform, false);
            part.transform.localPosition = localPosition;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterial = _material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            return partRenderer;
        }

        /// <summary>
        /// Gives one renderer its own colour, laid over the shared
        /// material's (which is left alone). Done once, at load.
        /// </summary>
        private static void Tint(MeshRenderer target, Color color)
        {
            MaterialPropertyBlock block = new();
            block.SetColor(_baseColorId, color);
            target.SetPropertyBlock(block);
        }

        /// <summary>
        /// Brings the pack out.
        /// </summary>
        public void Open()
        {
            IsOpen = true;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Puts the pack away, with everything in it. An item part way
        /// out goes back into its space.
        /// </summary>
        public void Close()
        {
            for (int i = 0; i < _cellCount; i++) {
                Stored stored = _stored[i];

                if (stored.loot is not null && (stored.isLeaving || stored.shrink < 1f)) {
                    stored.isLeaving = false;
                    stored.shrink = 1f;
                    ApplyShrink(stored);
                    _slots[i].SetTargetable(true);
                }
            }

            if (_objective.loot is not null && _objective.shrink < 1f) {
                _objective.shrink = 1f;
                ApplyShrink(_objective);
            }

            ShowHighlight(NoSpace);
            IsOpen = false;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Whether a point in the world is close enough to the pack for
        /// loot let go of there to go in. Compared as squared distances -
        /// no square root needed.
        /// </summary>
        public bool IsInReach(Vector3 worldPoint)
        {
            return IsOpen && (worldPoint - transform.position).sqrMagnitude <= storeRadius * storeRadius;
        }

        /// <summary>
        /// The space loot would go into if it were let go of at
        /// worldPoint: the gold space for gold, the objective's space for
        /// the objective (if that's empty), and for anything else the
        /// grid space it's over - the one whose middle is nearest across
        /// the board - if that space is free. NoSpace if it's out of the
        /// pack's reach, or the space it's over is taken: loot never goes
        /// into a space other than the one it's held over.
        /// </summary>
        private int TargetSpace(Loot loot, Vector3 worldPoint)
        {
            if (!IsInReach(worldPoint)) {
                return NoSpace;
            }

            if (loot.IsGold) {
                return GoldCell;
            }

            if (loot.IsObjective) {
                return _objective.loot is null ? ObjectiveSpace : NoSpace;
            }

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            int nearest = NoSpace;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < _cellCount; i++) {
                if (i == GoldCell) {
                    continue;
                }

                // Across the board only (x and y): how far out in front
                // of the board the loot is held doesn't matter.
                float x = _cellCentres[i].x - local.x;
                float y = _cellCentres[i].y - local.y;
                float sqr = x * x + y * y;

                if (sqr < nearestSqr) {
                    nearest = i;
                    nearestSqr = sqr;
                }
            }

            if (nearest == NoSpace || _stored[nearest].loot is not null) {
                return NoSpace;
            }

            return nearest;
        }

        /// <summary>
        /// Called every frame a hand carries loot near the pack: lights up
        /// the space the loot would go into if let go of now, so the
        /// player can choose the space by holding the loot over it. A
        /// space with something in it doesn't light up. Returns the space
        /// (NoSpace for none), so the caller can tell when it changes.
        /// </summary>
        public int Hover(Loot loot, Vector3 worldPoint)
        {
            int space = TargetSpace(loot, worldPoint);
            ShowHighlight(space);
            return space;
        }

        /// <summary>
        /// Nothing is being held over the pack: no space is lit.
        /// </summary>
        public void ClearHover()
        {
            ShowHighlight(NoSpace);
        }

        /// <summary>
        /// Moves the highlight onto a space, or hides it for NoSpace.
        /// Only touches anything when the space changes.
        /// </summary>
        private void ShowHighlight(int space)
        {
            if (space == _highlightedSpace) {
                return;
            }

            _highlightedSpace = space;
            _highlight.enabled = space != NoSpace;

            if (space == NoSpace) {
                return;
            }

            // The two top spaces are wide; the grid's are square.
            bool isWide = space < 0;
            float width = isWide ? _wideWidth : cellSize;
            Vector3 centre;

            if (space == ObjectiveSpace) {
                centre = _objective.spaceCentre;
            } else if (space == KeyringSpace) {
                centre = _keyringCentre;
            } else {
                centre = _cellCentres[space];
            }

            // Standing a little further out of the board than the
            // spaces' own panels, so it covers them.
            Transform highlight = _highlight.transform;
            highlight.localPosition = centre + Vector3.back * (Proud * 2f);
            highlight.localScale = new Vector3(width, cellSize, Proud * 2f);
        }

        /// <summary>
        /// Puts loot into the pack: let go of at worldPoint, by a hand -
        /// the loot must not be in a hand any more. It goes into the
        /// space it was held over (see TargetSpace()), or isn't taken.
        /// Says what became of it.
        /// </summary>
        public PackResult TryStore(Loot loot, Vector3 worldPoint)
        {
            int space = TargetSpace(loot, worldPoint);

            if (space == NoSpace) {
                return PackResult.NoRoom;
            }

            ShowHighlight(NoSpace);

            // Looked up when needed: there's one per scene, and asking
            // for it is cheap.
            PlayerInventory inventory = PlayerInventory.Instance;

            if (loot.IsGold) {
                Gold += loot.Value;
                loot.Collect();
                UpdateGoldPile();

                if (inventory != null) {
                    inventory.AddLoot(loot.Value);
                }

                return PackResult.Gold;
            }

            if (loot.IsObjective) {
                // Its space is wide: fit it to the height.
                PutIn(_objective, loot);

                if (inventory != null) {
                    inventory.TakeObjective();
                }

                return PackResult.Objective;
            }

            PutIn(_stored[space], loot);
            _slots[space].SetTargetable(true);

            if (inventory != null) {
                inventory.AddLoot(loot.Value);
            }

            return PackResult.Stored;
        }

        /// <summary>
        /// Makes loot a child of the pack in a space, upright, at full
        /// size to begin with: Tick() then shrinks it to fit. Physics
        /// forgets it (Grabbable.Stow()).
        /// </summary>
        private void PutIn(Stored stored, Loot loot)
        {
            Transform prop = loot.transform;
            Grabbable grabbable = loot.Grabbable;

            stored.loot = loot;
            stored.originalParent = prop.parent;
            stored.originalScale = prop.localScale;
            stored.isLeaving = false;
            stored.shrink = 0f;

            // Its real size now, which as a child of the (unscaled) pack
            // is simply its scale.
            stored.fullScale = prop.lossyScale;

            // The ball round the prop (its hold radius) shrunk to fill
            // "fit" of the space - never made bigger than it is.
            float size = grabbable.HoldRadius * 2f;
            stored.fitFactor = size > 0f ? Mathf.Min(1f, cellSize * fit / size) : 1f;

            grabbable.Stow();
            prop.SetParent(transform, false);
            prop.localRotation = Quaternion.identity;
            ApplyShrink(stored);
        }

        /// <summary>
        /// Sizes and places a stored item for how far shrunk it is. Its
        /// middle (not its origin) is kept on the middle of its space,
        /// standing just out of the board towards the player.
        /// </summary>
        private void ApplyShrink(Stored stored)
        {
            float factor = Mathf.Lerp(1f, stored.fitFactor, Mathf.SmoothStep(0f, 1f, stored.shrink));
            Grabbable grabbable = stored.loot.Grabbable;
            Transform prop = stored.loot.transform;

            // Out of the board by the item's own (shrunk) radius, so it
            // sits on the face rather than half inside it.
            Vector3 centre = stored.spaceCentre + Vector3.back * (grabbable.HoldRadius * factor);

            prop.localScale = stored.fullScale * factor;
            prop.localPosition = centre - grabbable.LocalCentre * factor;
        }

        /// <summary>
        /// Whether grid space cell holds an item that can be taken out
        /// now.
        /// </summary>
        public bool CanTakeFrom(int cell)
        {
            // Not a grid space (the keyring's, which PlayerKeys handles).
            if (cell < 0) {
                return false;
            }

            Stored stored = _stored[cell];
            return IsOpen && stored.loot is not null && !stored.isLeaving && _taken is null;
        }

        /// <summary>
        /// Starts taking the item in grid space cell out: it grows back
        /// to full size (Tick()), then is handed over by TryPopTaken().
        /// False if there's nothing there to take.
        /// </summary>
        public bool BeginTake(int cell)
        {
            if (!CanTakeFrom(cell)) {
                return false;
            }

            _stored[cell].isLeaving = true;
            _slots[cell].SetTargetable(false);
            return true;
        }

        /// <summary>
        /// One frame of the pack while it's out: items shrinking into
        /// their spaces or growing back out of them.
        /// </summary>
        public void Tick(float deltaTime)
        {
            float step = shrinkDuration > 0f ? deltaTime / shrinkDuration : 1f;

            for (int i = 0; i < _cellCount; i++) {
                TickStored(_stored[i], i, step);
            }

            TickStored(_objective, -1, step);
        }

        /// <summary>
        /// One stored item: moves its shrink towards where it's going,
        /// and lets it go once it's back at full size on its way out.
        /// </summary>
        private void TickStored(Stored stored, int cell, float step)
        {
            if (stored.loot is null) {
                return;
            }

            float target = stored.isLeaving ? 0f : 1f;

            if (stored.shrink == target) {
                return;
            }

            stored.shrink = Mathf.MoveTowards(stored.shrink, target, step);
            ApplyShrink(stored);

            if (stored.isLeaving && stored.shrink <= 0f) {
                TakeOut(stored, cell);
            }
        }

        /// <summary>
        /// An item has grown back to full size: it leaves the pack. Back
        /// under its old parent at its old scale, where it is now; its
        /// spaces are freed and its worth comes off the total. It's then
        /// waiting for TryPopTaken().
        ///
        /// It stays stowed (colliders off) until whoever takes it from
        /// TryPopTaken() unstows it. Unstowed here, it would sit in front
        /// of the hand for a frame as an ordinary prop, and the hand's
        /// own pick-up (PlayerHandHolding, hand ray on a prop with grip
        /// held) would take it before the pack had handed it over - the
        /// pack would then think the hand-over had failed and put a prop
        /// that was in a hand back into a space. That was a bug: found in
        /// the headset 2026-10-09.
        /// </summary>
        private void TakeOut(Stored stored, int cell)
        {
            Loot loot = stored.loot;
            Transform prop = loot.transform;

            prop.SetParent(stored.originalParent, true);
            prop.localScale = stored.originalScale;

            stored.loot = null;
            stored.originalParent = null;
            stored.isLeaving = false;

            PlayerInventory inventory = PlayerInventory.Instance;

            if (inventory != null) {
                inventory.RemoveLoot(loot.Value);
            }

            _taken = loot;
        }

        /// <summary>
        /// Hands over the loot that has just come out of the pack at full
        /// size, if there is one: it's in the world again, held still and
        /// still stowed. The caller must unstow it (Grabbable.Unstow())
        /// and give it to a hand or let it drop - or put it back with
        /// TryStore().
        /// </summary>
        public bool TryPopTaken(out Loot loot)
        {
            loot = _taken;
            _taken = null;
            return loot is not null;
        }

        /// <summary>
        /// Shows the gold pile at the size the gold calls for: nothing
        /// with no gold, then a block that gets taller up to a full pile,
        /// with the number of gold pieces written over it.
        /// </summary>
        private void UpdateGoldPile()
        {
            _goldPileRenderer.enabled = Gold > 0;

            // The number: only written when the gold changes (this is
            // the one place that happens). SetText() with the number as
            // an argument builds the figures without making a string.
            if (_goldNumber != null) {
                _goldNumber.enabled = Gold > 0;
                _goldNumber.SetText("{0}", Gold);
            }

            if (Gold <= 0) {
                return;
            }

            float full = Mathf.Clamp01(Gold / (float)Mathf.Max(1, goldForFullPile));
            float side = cellSize * 0.7f;
            float tall = Mathf.Lerp(cellSize * 0.1f, cellSize * 0.7f, full);

            // Standing on the bottom of the space, growing upwards.
            _goldPile.localScale = new Vector3(side, tall, 0.03f);
            _goldPile.localPosition = _cellCentres[GoldCell] + new Vector3(0f, (tall - side) * 0.5f, -0.015f);
        }
    }
}
