using Core;
using Interaction;
using Inventory;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// The player's side of the pack (Inventory.Pack): bringing it out and
    /// putting it away, and the hands putting loot in and taking it out.
    /// What the pack holds, and where things go in it, is the pack's own
    /// business.
    ///
    /// - A hand at the chest, palm towards it (PlayerBodyZones), gripping
    ///   brings the pack out, with either hand. It rides on
    ///   that hand's visual like something carried, at a set place
    ///   relative to the hand, for as long as the grip is held; letting
    ///   go puts it away. It collides with nothing.
    /// - Loot (Inventory.Loot) held over one of the pack's spaces lights
    ///   that space up if it's free, and let go of there goes into it;
    ///   anything else, or anywhere else, just drops. What's tested is
    ///   where the loot is, not where the hand is.
    /// - The other hand takes an item out the usual way - hand ray on it,
    ///   grip. The item first grows back to full size in the pack, then
    ///   the hand reaches for it as for any prop. (The hand holding the
    ///   pack doesn't take from it.)
    /// - While a hand carries loot, coins hover over it showing roughly
    ///   what it's worth (LootWorthMarker).
    ///
    /// It doesn't change how props are carried: it watches what each hand
    /// holds (PlayerHandHolding), and when a hand that held loot last
    /// frame holds nothing now, that loot has just been let go of.
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls
    /// TickSummon() (bringing the pack out and putting it away) before
    /// the grab systems, Tick() straight after PlayerHandHolding.Tick(),
    /// and TickHeld() (the pack's own movement, and the coins) after the
    /// hand visuals are placed.
    /// </summary>
    public class PlayerPack : MonoBehaviour
    {
        /// One hand's state. A class, made once per hand in Awake(), so
        /// the methods below can change it without ref parameters.
        private class PackHand
        {
            public bool isLeftHand;

            // The prop this hand was carrying last frame (or null), and
            // the loot it is - null for a prop that isn't loot.
            public Grabbable grabbable;
            public Loot loot;

            // The key it is, if it's a key instead (never both).
            public Key key;

            // The pack space that loot was lighting up last frame
            // (Pack.NoSpace for none).
            public int hoverSpace = Pack.NoSpace;

            // The coins shown over loot this hand carries.
            public LootWorthMarker marker;

            // Whether grip was held last frame, and whether the hand was
            // at the chest and free: the pack comes out on the frame grip
            // is pressed there, and a tap is felt once on reaching it.
            public bool wasGrabbing;
            public bool wasAtChest;
        }

        [Tooltip("The input system on the Player root: a grip press at the chest brings the pack out, holding grip keeps it out, e.g. drag the Player root's PlayerInputXR here (filled in when the component is added).")]
        [SerializeField] private PlayerInputXR playerInput;
        [Tooltip("The tracking on the Player root: the head, which the loot-worth coins face, e.g. drag the Player root's PlayerTracking here (filled in when the component is added).")]
        [SerializeField] private PlayerTracking playerTracking;
        [Tooltip("The prop carrying on Hands: loot let go of at the pack goes in, and loot taken out is put in the hand through it, e.g. drag the Hands object's PlayerHandHolding here (filled in when the component is added).")]
        [SerializeField] private PlayerHandHolding playerHandHolding;
        [Tooltip("The hand rays on Hands: a hand whose ray is on a pack space takes out what is in it, e.g. drag the Hands object's PlayerHandInteraction here (filled in when the component is added).")]
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [Tooltip("The hand visuals on Hands: the pack rides on the visual of the hand that brought it out, e.g. drag the Hands object's PlayerHandVisuals here (filled in when the component is added).")]
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        // What each hand is busy with: a hand climbing, carrying or on a
        // door can't bring the pack out. Found (or made) in Awake() if not
        // wired (it's on this same object).
        [Tooltip("What each hand is busy with, asked before a hand brings the pack out, e.g. a hand carrying a prop can't. Found (or added) on this object if empty.")]
        [SerializeField] private PlayerHandState playerHandState;

        // Where the chest is, which the pack is reached for. Found (or
        // made) in Awake() if not wired (it's on this same object).
        [Tooltip("Where the chest is, which a hand reaches for (palm towards it) to bring the pack out, e.g. the Hands object's PlayerBodyZones. Found (or added) on this object if empty.")]
        [SerializeField] private PlayerBodyZones bodyZones;

        // Optional: a rig with no haptics still uses the pack.
        [Tooltip("The haptics on the Player root, for the taps and pulses below, e.g. the tap when a free hand reaches the chest. Found on the Player root if empty; with none, the pack works without vibration.")]
        [SerializeField] private PlayerHaptics playerHaptics;

        // The pack in the scene. Optional: found in Awake() if not wired,
        // and made with its default settings if there is none.
        [Tooltip("The pack (Inventory.Pack) the player stores loot and keys in, e.g. the Pack saved as a child of the Player root. Empty, the first in the scene is used, or one is made with default settings if there is none.")]
        [SerializeField] private Pack pack;

        [Header("Where The Pack Rides")]

        // How a hand holds the pack by the chest's handle: each hand's
        // offset from the pack's own origin and axes (the middle of the
        // spaces, Z away from the player), and the fingers.
        [Tooltip("How a hand holds the pack by its handle (Assets/Data/Hand Poses/PackHold.asset): each hand's offset from the pack's origin and axes (middle of the spaces, X right, Y up, Z away from the player), and the finger pose. Posed with TeaLeaf > Player > Pose with Hand. e.g. with PackHold, the hand grips the chest's end handle with the spaces facing the player. Empty puts the pack's origin on the hand visual's.")]
        [SerializeField] private HandSnapProfile packProfile;

        [Header("Loot Worth Coins")]

        // Each coin's radius, the distance between coins' middles, and
        // how far above the loot (beyond its own size) they hover, in
        // metres.
        [Tooltip("Radius of each worth coin shown over carried loot, in metres, e.g. 0.012 = a 2.4cm coin. Higher makes the coins bigger, lower smaller.")]
        [SerializeField] private float coinRadius = 0.012f;
        [Tooltip("Distance between neighbouring worth coins' middles, in metres, e.g. 0.03 = three coins span about 8cm. Higher spreads them out, lower bunches them (below twice Coin Radius they overlap).")]
        [SerializeField] private float coinSpacing = 0.03f;
        [Tooltip("How far above carried loot (beyond its own hold radius) the worth coins hover, in metres, e.g. 0.05 = 5cm over the top of the loot. Higher floats them further up, lower closer.")]
        [SerializeField] private float coinHeight = 0.05f;
        [Tooltip("Colour of the worth coins (drawn on top of everything), e.g. gold. Its alpha sets how see-through they are.")]
        [SerializeField] private Color coinColor = new(1f, 0.8f, 0.2f, 1f);

        [Header("Feedback")]

        // The sound of something going into the pack. Optional. Quiet,
        // and it barely carries to guards.
        [Tooltip("The sound of loot or a key going into the pack, e.g. a soft rustle cue. Empty, storing is silent and makes no noise.")]
        [SerializeField] private SoundCue storeCue;
        [Tooltip("Multiplies the store cue's volume (what the player hears), e.g. 0.5 = half the cue's own volume. Higher is louder, lower quieter; 0 silent.")]
        [SerializeField] private float storeVolume = 0.5f;
        [Tooltip("Multiplies the store cue's noise radius (what guards hear), e.g. 0.2 = a fifth of the cue's radius, so it barely carries. Higher lets guards hear it from further, 0 makes it no noise.")]
        [SerializeField] private float storeNoiseScale = 0.2f;

        // A free hand reaching the chest, and the pack coming out.
        [Tooltip("Strength (0-1) of the tap a free hand feels on reaching the chest, where the pack can be brought out, e.g. 0.15 = a light tick. Higher is stronger; 0 = no tap.")]
        [SerializeField] private float reachAmplitude = 0.15f;
        [Tooltip("Length of the tap on reaching the chest, in seconds, e.g. 0.03 = a brief tick. Higher is longer; 0 = no tap.")]
        [SerializeField] private float reachDuration = 0.03f;
        [Tooltip("Strength (0-1) of the pulse as the pack comes out into the hand, e.g. 0.5 = a firm bump. Higher is stronger; 0 = none.")]
        [SerializeField] private float summonAmplitude = 0.5f;
        [Tooltip("Length of the pulse as the pack comes out, in seconds, e.g. 0.06 = a short bump. Higher is longer; 0 = none.")]
        [SerializeField] private float summonDuration = 0.06f;

        // Carried loot lighting up a space in the pack.
        [Tooltip("Strength (0-1) of the tap as carried loot lights up a new space in the pack, e.g. 0.15 = a light tick per space. Higher is stronger; 0 = no tap.")]
        [SerializeField] private float hoverAmplitude = 0.15f;
        [Tooltip("Length of the tap as carried loot lights up a new space, in seconds, e.g. 0.03 = a brief tick. Higher is longer; 0 = no tap.")]
        [SerializeField] private float hoverDuration = 0.03f;

        // Loot going into the pack.
        [Tooltip("Strength (0-1) of the pulse as loot or a key goes into the pack, e.g. 0.6 = a clear thud. Higher is stronger; 0 = none.")]
        [SerializeField] private float storeAmplitude = 0.6f;
        [Tooltip("Length of the pulse as loot or a key goes into the pack, in seconds, e.g. 0.08 = a short thud. Higher is longer; 0 = none.")]
        [SerializeField] private float storeDuration = 0.08f;

        // Loot the pack had no room for: longer and rougher.
        [Tooltip("Strength (0-1) of the buzz when the pack has no room for the loot let go of (it drops), e.g. 0.9 = a strong refusal. Higher is stronger; 0 = none.")]
        [SerializeField] private float refuseAmplitude = 0.9f;
        [Tooltip("Length of the no-room buzz, in seconds, e.g. 0.25 = a long rough buzz, unlike the short store thud. Higher is longer; 0 = none.")]
        [SerializeField] private float refuseDuration = 0.25f;

        // How many straight pieces a coin's round edge is made of.
        private const int CoinSegments = 16;

        private PackHand _left;
        private PackHand _right;

        private bool _hasHaptics;

        // Whether the pack is out, and which hand is holding it.
        private bool _isOut;
        private bool _isPackLeft;

        // True from the free hand asking for an item until the pack
        // hands it over (it's growing back to full size).
        private bool _isTaking;

        // True once a hand has lit a pack space up this frame.
        private bool _isHovering;

        private Mesh _coinMesh;
        private Material _coinMaterial;

        /// The pack - for PlayerKeys, which takes the keyring out of it.
        /// Set by the end of Awake().
        public Pack Pack => pack;

        /// True while that hand is holding the pack out. The other hand
        /// systems leave it alone meanwhile (PlayerHandState).
        public bool IsLeftBusy => _isOut && _isPackLeft;
        public bool IsRightBusy => _isOut && !_isPackLeft;

        /// <summary>
        /// Whose finger pose a hand plays: the pack profile's in the hand
        /// holding the pack, else null. Read by PlayerHandAnimation each
        /// frame.
        /// </summary>
        public HandSnapProfile GetHeldProfile(bool isLeftHand)
        {
            return _isOut && _isPackLeft == isLeftHand ? packProfile : null;
        }

        /// <summary>
        /// Editor-only: fills in the references when the component is
        /// added (on the Hands object; the body systems are on the Player
        /// root above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerTracking = GetComponentInParent<PlayerTracking>();
            playerHaptics = GetComponentInParent<PlayerHaptics>();
            playerHandHolding = GetComponent<PlayerHandHolding>();
            playerHandInteraction = GetComponent<PlayerHandInteraction>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
        }

        private void Awake()
        {
            if (playerHandState == null) {
                playerHandState = PlayerHandState.GetOrAdd(playerHandVisuals);
            }

            if (bodyZones == null) {
                bodyZones = PlayerBodyZones.GetOrAdd(playerHandVisuals);
            }

            if (playerHaptics == null) {
                playerHaptics = GetComponentInParent<PlayerHaptics>();
            }

            // Looked up once, so the per-frame code tests a plain bool.
            _hasHaptics = playerHaptics != null;

            // One pack, made at load. A search of the scene, but only
            // once, at startup.
            if (pack == null) {
                pack = FindFirstObjectByType<Pack>();
            }

            if (pack == null) {
                pack = new GameObject("Pack").AddComponent<Pack>();
            }

            // One coin mesh and one material, shared by both hands'
            // markers.
            _coinMesh = BuildCoinMesh(coinRadius);
            _coinMaterial = OverlayMaterial.Create(coinColor);

            _left = new PackHand {
                isLeftHand = true,
                marker = new LootWorthMarker("Left Loot Worth", transform, _coinMesh, _coinMaterial, coinSpacing)
            };

            _right = new PackHand {
                isLeftHand = false,
                marker = new LootWorthMarker("Right Loot Worth", transform, _coinMesh, _coinMaterial, coinSpacing)
            };
        }

        /// <summary>
        /// Puts the pack on a hand and puts it away. Start() rather than
        /// Awake(): PlayerHandVisuals copies each hand visual in its own
        /// Awake() to make the ghost hands, and a pack already on the
        /// hand by then would be copied onto the ghost too. Every Awake()
        /// has run before the first Start().
        /// </summary>
        private void Start()
        {
            PlaceOnHand(true);
            pack.Close();
        }

        private void OnDestroy()
        {
            // A mesh and a material made from code aren't cleaned up with
            // their objects.
            if (_coinMesh != null) {
                Destroy(_coinMesh);
            }

            if (_coinMaterial != null) {
                Destroy(_coinMaterial);
            }
        }

        /// <summary>
        /// A coin: a flat disc in its own XY plane, facing +Z, as a fan
        /// of triangles round its middle (the same shape as a hand
        /// reticle's).
        /// </summary>
        private static Mesh BuildCoinMesh(float radius)
        {
            Vector3[] vertices = new Vector3[CoinSegments + 1];
            int[] triangles = new int[CoinSegments * 3];

            for (int i = 0; i < CoinSegments; i++) {
                float angle = i / (float)CoinSegments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;

                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % CoinSegments + 1;
            }

            Mesh mesh = new() { name = "Loot Worth Coin" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Makes the pack a child of one hand's visual, where the pack
        /// profile puts it, with the chest's handle on that hand's side.
        /// The pack itself (its spaces) is never mirrored, so it shows the
        /// right way round and up in either hand.
        /// </summary>
        private void PlaceOnHand(bool isLeftHand)
        {
            Transform visual = isLeftHand ? playerHandVisuals.LeftHandVisual : playerHandVisuals.RightHandVisual;

            pack.SetHandleOnLeft(isLeftHand);
            HandSnapProfile.PlaceInHand(packProfile, isLeftHand, pack.transform, visual, transform);
        }

        /// <summary>
        /// Bringing the pack out and putting it away. Called by
        /// PlayerController before the grab systems (climbing, carrying,
        /// doors): the pack is taken by reaching, not by the hand rays, so
        /// a hand that takes it must already be busy when those look at
        /// what its ray is on.
        /// </summary>
        public void TickSummon()
        {
            bool isLeftGrabbing = playerInput.IsLeftGrabbing;
            bool isRightGrabbing = playerInput.IsRightGrabbing;

            if (_isOut) {
                // Out for as long as the hand holding it keeps its grip.
                bool isHolding = _isPackLeft ? isLeftGrabbing : isRightGrabbing;

                if (!isHolding) {
                    ClosePack();
                }
            } else {
                TickReach(_left, isLeftGrabbing);

                if (!_isOut) {
                    TickReach(_right, isRightGrabbing);
                }
            }

            _left.wasGrabbing = isLeftGrabbing;
            _right.wasGrabbing = isRightGrabbing;
        }

        /// <summary>
        /// One hand, while the pack is away: a tap as the hand, free,
        /// comes to the chest (PlayerBodyZones: in the chest box, palm
        /// towards it), and the pack out on that hand on the frame its
        /// grip is pressed there. (Pressed, not held: a hand that arrives
        /// already gripping doesn't bring it out.)
        /// </summary>
        private void TickReach(PackHand hand, bool isGrabbing)
        {
            bool isAtChest = !playerHandState.IsBusyExcept(hand.isLeftHand, HandUse.Pack)
                && bodyZones.IsAtChest(hand.isLeftHand);

            if (isAtChest && !hand.wasAtChest) {
                Pulse(hand.isLeftHand, reachAmplitude, reachDuration);
            }

            hand.wasAtChest = isAtChest;

            if (!isAtChest || !isGrabbing || hand.wasGrabbing) {
                return;
            }

            _isOut = true;
            _isPackLeft = hand.isLeftHand;
            _left.wasAtChest = false;
            _right.wasAtChest = false;

            PlaceOnHand(hand.isLeftHand);
            pack.Open();
            Pulse(hand.isLeftHand, summonAmplitude, summonDuration);
        }

        /// <summary>
        /// Loot being let go of, and items being taken out. Called by
        /// PlayerController straight after PlayerHandHolding.Tick(), so a
        /// prop let go of this frame is seen this frame, before it has
        /// started to fall.
        /// </summary>
        public void Tick()
        {
            // Set by whichever hand lights a space up this frame - see
            // TickHover().
            _isHovering = false;

            TickHand(_right, playerHandHolding.RightHeld, playerHandHolding.IsRightHolding);
            TickHand(_left, playerHandHolding.LeftHeld, playerHandHolding.IsLeftHolding);

            if (pack.IsOpen) {
                // No hand is holding loot over a free space: nothing lit.
                if (!_isHovering) {
                    pack.ClearHover();
                }

                TickTaking();
            }
        }

        /// <summary>
        /// Puts the pack away. An item that had just come out of it and
        /// not yet reached a hand goes back in first.
        /// </summary>
        private void ClosePack()
        {
            if (pack.TryPopTaken(out Loot loot)) {
                PutBack(loot);
            }

            _isOut = false;
            _isTaking = false;
            _left.hoverSpace = Pack.NoSpace;
            _right.hoverSpace = Pack.NoSpace;
            pack.Close();
        }

        /// <summary>
        /// One hand. held is what it carries now. isStillBusy is
        /// PlayerHandHolding's "holding" flag, which stays true for a
        /// moment after an aimed throw (until grip is let go): a hand
        /// that's empty but still busy has just thrown what it had, and
        /// thrown loot never goes into the pack.
        /// </summary>
        private void TickHand(PackHand hand, Grabbable held, bool isStillBusy)
        {
            // Let go of since last frame: into the pack, if it was loot
            // or a key and is close enough to it.
            bool wasPackable = hand.loot is not null || hand.key is not null;

            if (wasPackable && hand.grabbable != held) {
                bool wasThrown = held is null && isStillBusy;

                // Unity's == null is true for a prop destroyed while held.
                if (!wasThrown && hand.grabbable != null) {
                    TryStore(hand);
                }

                hand.loot = null;
                hand.key = null;
                hand.hoverSpace = Pack.NoSpace;
            }

            if (held is null) {
                hand.grabbable = null;
                return;
            }

            // Newly picked up: is it loot, or a key? A dictionary lookup
            // or two, on the frame of the pick-up only.
            if (hand.grabbable != held) {
                hand.grabbable = held;
                hand.loot = Loot.Find(held);
                hand.key = hand.loot is null ? Key.Find(held) : null;
                hand.hoverSpace = Pack.NoSpace;
            }

            if (hand.loot is not null || hand.key is not null) {
                TickHover(hand);
            }
        }

        /// <summary>
        /// A hand carrying loot: the pack lights up the space the loot is
        /// held over, if that space is free - which is how the player
        /// chooses where it goes. A light tap on the controller each time
        /// a different space lights up. Only one hand lights a space in a
        /// frame: once one has, the other is skipped.
        /// </summary>
        private void TickHover(PackHand hand)
        {
            if (!pack.IsOpen || _isHovering) {
                hand.hoverSpace = Pack.NoSpace;
                return;
            }

            // A key lights the keyring's space, wherever over the pack
            // it's held.
            Vector3 centre = CarriedCentre(hand);
            int space = hand.key is not null ? pack.HoverKey(centre) : pack.Hover(hand.loot, centre);

            if (space != Pack.NoSpace) {
                _isHovering = true;

                if (space != hand.hoverSpace) {
                    Pulse(hand.isLeftHand, hoverAmplitude, hoverDuration);
                }
            }

            hand.hoverSpace = space;
        }

        /// <summary>
        /// The loot this hand has just let go of goes into the pack, into
        /// the space it was held over, if it's close enough to the pack
        /// and that space is free. Let go of over a space that's taken,
        /// it's refused (a rough buzz) and drops like any prop; away from
        /// the pack nothing happens here at all.
        /// </summary>
        private void TryStore(PackHand hand)
        {
            Vector3 position = hand.grabbable.WorldCentre;

            if (!pack.IsOpen || !pack.IsInReach(position)) {
                return;
            }

            // A key goes onto the keyring; loot into the space it's over.
            bool wasTaken = hand.key is not null
                ? pack.TryStoreKey(hand.key, position)
                : pack.TryStore(hand.loot, position) != PackResult.NoRoom;

            if (!wasTaken) {
                Pulse(hand.isLeftHand, refuseAmplitude, refuseDuration);
                return;
            }

            Pulse(hand.isLeftHand, storeAmplitude, storeDuration);

            if (storeCue == null) {
                return;
            }

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(storeCue, position, transform, storeNoiseScale, storeVolume);
            } else {
                storeCue.EmitNoise(position, transform, storeNoiseScale);
            }
        }

        /// <summary>
        /// Taking an item out with the hand that isn't holding the pack.
        /// Asking: the hand is empty, grip is held and its ray is on one
        /// of the pack's spaces - the item there starts to grow.
        /// Receiving: once it's full size the pack hands it over, and the
        /// hand picks it up as it would any prop. If the hand has let go
        /// of grip by then, or can't take it, it goes back in.
        /// </summary>
        private void TickTaking()
        {
            bool isTakerLeft = !_isPackLeft;
            bool isGrabbing = isTakerLeft ? playerInput.IsLeftGrabbing : playerInput.IsRightGrabbing;

            if (pack.TryPopTaken(out Loot loot)) {
                _isTaking = false;

                Grabbable grabbable = loot.Grabbable;

                // Grip let go while it grew: straight back in, still
                // stowed.
                if (!isGrabbing) {
                    PutBack(loot);
                    return;
                }

                // Its colliders come back on only now, in the same breath
                // as the hand taking it. This runs after
                // PlayerHandHolding.Tick() each frame, so the hand's own
                // pick-up never sees it lying loose - see Pack.TakeOut().
                grabbable.Unstow();

                if (!playerHandHolding.TryPickUp(isTakerLeft, grabbable, grabbable.WorldCentre)) {
                    PutBack(loot);
                }

                return;
            }

            bool isTakerHolding = isTakerLeft ? playerHandHolding.IsLeftHolding : playerHandHolding.IsRightHolding;

            if (_isTaking || !isGrabbing || isTakerHolding) {
                return;
            }

            // The ray target is only an IHandTarget; a type pattern
            // checks whether it's one of the pack's spaces.
            IHandTarget target = isTakerLeft ? playerHandInteraction.LeftTarget : playerHandInteraction.RightTarget;

            if (target is PackSlot slot && slot.Pack == pack) {
                _isTaking = pack.BeginTake(slot.Index);
            }
        }

        /// <summary>
        /// Loot that came out of the pack and couldn't be given to a
        /// hand goes back in. If even that fails it's let go of where it
        /// is, to fall like any dropped prop - it must never be left
        /// hanging in the air.
        /// </summary>
        private void PutBack(Loot loot)
        {
            Grabbable grabbable = loot.Grabbable;

            // Never a prop that's in a hand: the pack would shrink it and
            // switch its colliders off while the hand carried it.
            if (grabbable.IsHeld) {
                return;
            }

            if (pack.TryStore(loot, grabbable.WorldCentre) == PackResult.NoRoom) {
                grabbable.Unstow();
                grabbable.EndHold(Vector3.zero, Vector3.zero);
            }
        }

        /// <summary>
        /// The pack's own movement (items shrinking and growing) and the
        /// coins over carried loot. Called by PlayerController straight
        /// after the hand visuals are placed, on every path through
        /// Update(), so the coins sit over the loot where it's drawn.
        /// </summary>
        public void TickHeld()
        {
            if (pack.IsOpen) {
                pack.Tick(Time.deltaTime);
            }

            Vector3 headPosition = playerTracking.HeadPosition;
            TickMarker(_left, headPosition);
            TickMarker(_right, headPosition);
        }

        /// <summary>
        /// Shows the coins over the loot this hand carries, or hides them.
        /// </summary>
        private void TickMarker(PackHand hand, Vector3 headPosition)
        {
            // != null (Unity's) rather than "is not null": also false for
            // loot destroyed while carried.
            if (hand.loot == null || !hand.grabbable.IsHeld) {
                hand.marker.Tick(0, default, headPosition);
                return;
            }

            Vector3 above = CarriedCentre(hand) + Vector3.up * (hand.grabbable.HoldRadius + coinHeight);
            hand.marker.Tick(hand.loot.CoinLevel, above, headPosition);
        }

        /// <summary>
        /// Where the middle of the prop this hand carries is. Asked of
        /// PlayerHandHolding once the prop is in the hand: under the
        /// right hand, which is mirrored, the prop's own rotation can't
        /// be trusted to work it out from. Before then (the hand is
        /// still reaching for it) the prop lies in the world as normal.
        /// </summary>
        private Vector3 CarriedCentre(PackHand hand)
        {
            if (playerHandHolding.TryGetCarriedCentre(hand.isLeftHand, out Vector3 centre)) {
                return centre;
            }

            return hand.grabbable.WorldCentre;
        }

        /// <summary>
        /// Buzzes one controller, if the rig has haptics.
        /// </summary>
        private void Pulse(bool isLeftHand, float amplitude, float duration)
        {
            if (_hasHaptics) {
                playerHaptics.Pulse(isLeftHand, amplitude, duration);
            }
        }
    }
}
