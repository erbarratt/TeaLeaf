# Inventory systems (`Assets/Scripts/Inventory/`, namespace `Inventory`)

Detail for what the player owns and carries: the pack, loot and the inventory data. The root
`CLAUDE.md` holds the project rules, the tick order and the physics layers; this file is
loaded when working in this folder. Keep it up to date with every change to these systems,
like the root file.

Phase 4 of `Assets/DEVROADMAP.txt`. **Built and working in the headset:** the pack, loot, the
worth coins, the inventory data, keys and the keyring. **Not built:** equipping tools, bolt
selection, a number for the gold.

## The design

**A backpack that only holds so much.** The player searches the level and keeps the most
valuable things, discarding the rest. Each piece of loot takes exactly one of the pack's
spaces, chosen by holding it over that space (a free space lights up); small things (coins,
rings) just add to a gold amount. The pack is a physical bag the player summons (X on the
left controller, again to dismiss) and puts things into by hand; it rides on the left hand
visual and collides with nothing. Its layout: along the top, side by side, a space for the
**objective** and one for the **keyring** - both apart from the loot - and under them a
tunable grid, 3 x 3, whose top left space is the **gold** space. An item in the pack snaps to
a space and shrinks to fit it; taken out, it grows back first and then the hand takes it as
normal. Carried loot shows **one to three coins** hovering near it for its worth (the value
behind each level is not set yet). No hip pocket and no multi-space items.

**Keys:** coloured; collected keys show on the keyring in the pack. To unlock, the keyring is
taken from the pack and held to a door's lock: if the right key is on it, it snaps into the
lock, and the player grips it and turns it 90 degrees - anticlockwise if the lock is on the
right of the door leaf from the player's side, clockwise if on the left.

## The pack

- **`Pack`** — the physical pack and what's in it. One in the scene, built in `Awake()` from
  its own fields; `Player.PlayerPack` finds it (or makes one with defaults) and puts it on
  the left hand. Own axes: X to the player's right, Y up the board, Z away from the player;
  origin = the middle of the board; unscaled. **Layout** (`Build()`): a board `columns` x
  `rows` spaces wide/tall (`cellSize` 0.09, `cellGap` 0.012, `depth` 0.05) plus a top row
  of two wide spaces (objective left, keyring right). Grid spaces are numbered row by row
  from the top left; **space 0 is the gold space** (`GoldCell`). One vertex-coloured mesh
  from `Interaction.LockMeshBuilder` with the `TeaLeaf/LockFade` solid material (a lit
  colour that's never darker than `minLight`); a gold pile and the highlight share one block
  mesh, tinted by property block once at load. **No solid colliders.** Each grid space but
  the gold one has a trigger box (`PackSlot`, Interactable layer, reaching 0.1m out towards
  the player) that is **only enabled while an item sits there**; the pack has a kinematic
  Rigidbody so physics moves the triggers cheaply. `Open()` / `Close()` are `SetActive` (an
  item part way out goes back in on closing); `IsOpen`. No `Update()`.
  **Choosing a space** (every item takes exactly one space, chosen by holding the loot over
  it). `TargetSpace(loot, worldPoint)`: `NoSpace` out of reach (`IsInReach()`: within
  `storeRadius` 0.24m of the board's middle); the gold space for gold; `ObjectiveSpace` for
  the objective if its space is empty; otherwise the grid space whose middle is nearest
  **across the board** (x and y only - how far out in front doesn't matter), and only if
  it's free - loot never goes into a space other than the one it's over. `Hover(loot,
  worldPoint)` (called every frame a hand carries loot, by `PlayerPack`) moves one
  highlight block onto that space (**a free space lights up, a taken one doesn't**) and
  returns it; `ClearHover()`; the block is only touched when the space changes.
  **Storing:** `TryStore(loot, worldPoint)` uses the same `TargetSpace()` → `PackResult`:
  **`Gold`** (value added to `Gold`, `Loot.Collect()`, the pile grows - a full pile at
  `goldForFullPile` 200), **`Objective`** (into its own space;
  `PlayerInventory.TakeObjective()`; **can't be taken out again**), **`Stored`**, or
  **`NoRoom`** (the space it was over is taken, or it wasn't over the pack). `PutIn()`:
  `Grabbable.Stow()`, child of the pack, upright, its original parent and scale remembered.
  **Shrinking** (`Tick()`, `ApplyShrink()`): `shrink` goes 0 → 1 over `shrinkDuration`
  (0.12s), scaling the item from full size to `fitFactor` = `cellSize x fit (0.8) / (2 x
  HoldRadius)` (never above 1), with its middle (`Grabbable.LocalCentre`, not its origin)
  kept on the space's middle, one shrunk radius out of the board.
  **Taking out:** `CanTakeFrom(cell)`, `BeginTake(cell)` → the item grows back; at full size
  `TakeOut()` puts it back under its old parent at its old scale, frees its space and holds
  it for `TryPopTaken(out loot)` - the caller must `Unstow()` it and hand it to a hand, let
  it drop, or put it back. **The item stays stowed until that hand-over**: unstowed a frame
  early it is an ordinary prop in front of the hand, the hand's own pick-up takes it in
  `PlayerHandHolding.Tick()` before `PlayerPack.Tick()` runs, and the pack then "puts back"
  a prop that is in the hand. `PlayerPack` unstows it in the same call as the pick-up, after
  the holding tick. One item is taken at a time. The pack tells `PlayerInventory`
  (`AddLoot` / `RemoveLoot`) as things go in and out.
- **`PackSlot`** — one grid space as an `IHandTarget` (made by the pack; `Pack`, `Index`,
  `SetTargetable()` enables its trigger). Registered with `HandTargetRegistry`. The stored
  item's own colliders are off; this stands in for them, so the reticle shows on what can
  be taken out and an empty space never blocks a hand ray.

## Keys and the keyring

- **`Key`** — makes a prop a key pickup: `[RequireComponent(typeof(Grabbable))]`, `keyId`
  (the same text as the door's Key Id) and `color`. `Awake()` tints the prop's renderers
  in its colour by property block. A static `Grabbable → Key` dictionary; `Key.Find()`.
  `Collect()` switches it off. **Collected by putting it into the pack like loot**: held
  near the pack it lights the keyring's space wherever it is (`Pack.HoverKey()`), and let
  go of there `Pack.TryStoreKey()` adds it to the keyring and it's gone. No coins show.
- **`Keyring`** — the ring and what's on it: up to `MaxKeys` (6) ids and colours in fixed
  arrays (`Add()`, `Has(keyId)`, `TryGetColor()`, `Count`), drawn as a square ring (one
  mesh) with a coloured bar per key hanging below it, all made hidden at load and shown as
  keys are added; `ShowKeyInLock(isShown, color)` shows a bar from the ring's middle
  along its Z - the key going into the door. Made by the `Pack` in `Build()`, sharing its
  material and block mesh; real size (it fits its space unscaled). It doesn't move itself:
  whoever has it makes it their child. Own axes: X viewer's right, Y up, Z away.
- **In the `Pack`:** the keyring's space is the top right one (`KeyringSpace` = -3, with
  `ObjectiveSpace` -1 and `NoSpace` -2), with its own `PackSlot` trigger, on only while the
  keyring is home **with at least one key on it**. `Keyring`, `IsKeyringHome`,
  `TakeKeyring()` (false if the pack is shut, the ring away or empty), `ReturnKeyring()`.
  `CanTakeFrom()` is false for it, so `PlayerPack`'s taking ignores that slot.
- Carrying the ring to a lock and turning the key is `Player.PlayerKeys`
  (`Scripts/Player/CLAUDE.md`); the lock is `Interaction.KeyLock`
  (`Scripts/Interaction/CLAUDE.md`).

## Loot

- **`Loot`** — makes a prop loot: `[RequireComponent(typeof(Grabbable))]`. `value`;
  `coinLevel` (1-3, the coins shown over it while carried); `isGold` (small loot: adds to
  the pack's gold and is gone); `isObjective` (the level's objective: its own space in the
  pack). Picked up, carried and thrown by its `Grabbable` like any prop. Self-registers in a
  static `Grabbable → Loot` dictionary (`OnEnable`/`OnDisable`); `Loot.Find(grabbable)`
  answers "is this prop loot?" without a `GetComponent` (not while it's in a closed pack:
  it's inactive then). `Collect()` switches the object off rather than destroying it.

## Inventory data

- **`PlayerInventory`** — on the Player root; `Instance` (static), like `LevelManager`.
  **Data only**. Made again with the scene on a level restart. Holds: `LootTotal`,
  `LootCount` (`AddLoot(value)` / `RemoveLoot(value)` - the worth of what's in the pack,
  gold included); `HasObjective` (`TakeObjective()`, which also calls
  `LevelManager.SetObjectiveCarried(true)` - ending the level stays the level manager's
  business); tools owned (`Owns(tool)`, `GiveTool(tool)`; starting set from `ownsBlackjack`
  / `ownsCrossbow`); bolt counts (`GetBoltCount(bolt)`, `AddBolts(bolt, count)`,
  `TryUseBolt(bolt)`; starting counts from three fields). **`Changed`** (a plain C#
  instance event) is raised after any change. No `Update()`.
- **`ToolType`** (`Blackjack`, `Crossbow`) and **`BoltType`** (`Water`, `Noisemaker`,
  `Rope`) — enums. Add values at the end, no explicit numbers (`BoltType` is an array
  index). The lockpicks aren't a `ToolType`: they're always on the back of the left hand.

## Debug

- **`InventoryDebug`** (`Inventory/Debug`, on the Player root) — logs the whole inventory
  to the Console on each `Changed`. The only place the gold's exact amount shows.
- **`LootTestProps`** (`Inventory/Debug/Editor`, menu **TeaLeaf > Add Loot Test Props**) —
  a table 1.2m ahead of and 1.7m to the right of the main camera with more loot than the
  pack holds: four coins (gold, 5 each), four purses (20, one coin), three goblets (50, two
  coins), two candlesticks (80, two coins), a crown (150, three coins) and
  an idol (100, the objective), in `Assets/Art/Materials/LootGold.mat`. Props are made by
  `Player.GrabbableTestProps.MakeGrabbable()` plus a `Loot`. Adds `PlayerInventory` and
  `InventoryDebug` to the Player root and `PlayerPack` to the Hands object, and makes a
  **`Pack`** object at the scene root (outside the test props, so a rebuild keeps its
  tuning), each only if missing.
