# Inventory (`Assets/Scripts/Inventory/`, namespace `Inventory`)

Roadmap Phase 4. Pack design is in root `CLAUDE.md`. Built and working in headset except: gold number untried; bolt pickups in the level and any display of bolt counts not built (crossbow uses counts, returns re-picked bolts: `Scripts/Player/CLAUDE.md`).
Also: each loot piece takes exactly one space (no multi-space items, no hip pocket); stored items shrink to fit, grow back before the hand takes them. Key turns 90 deg: anticlockwise if the lock is right of the leaf from the player's side, clockwise if left.

## Pack

- **`Pack`** — one, saved as an **active** child of the Player root (lookup skips inactive); built in `Awake()` from its fields. `Player.PlayerPack` finds it (or makes a default) and parents it to the hand bringing it out (x scale -1 under the mirrored right hand visual, so never mirrored). Axes X player's right, Y up board, Z away; origin board middle; unscaled. No `Update()`.
- **Layout** (`Build()`): `columns` x `rows` grid (`cellSize` 0.09, `cellGap` 0.012, `depth` 0.05) + top row of two wide spaces (objective left, keyring right). Grid indexed row-wise from top left; **0 = gold** (`GoldCell`). One vertex-coloured mesh (`Interaction.LockMeshBuilder`, `TeaLeaf/LockFade` solid, lit, floor `minLight`); gold pile and highlight share one block mesh, tinted by property block once at load.
- **No solid colliders.** Non-gold grid spaces each have a `PackSlot` trigger (Interactable, reaching 0.1m towards player), **enabled only while occupied**. Kinematic Rigidbody so physics moves triggers cheaply. `Open()`/`Close()` = `SetActive` (an item part way out goes back in); `IsOpen`.
- **`TargetSpace(loot, worldPoint)`**: `NoSpace` if not `IsInReach()` (`storeRadius` 0.24m of board middle); gold → gold space; objective → `ObjectiveSpace` if empty; else nearest grid space by x/y only (depth ignored), and only if free - never another space. `Hover()` (each frame loot is carried, from `PlayerPack`) moves the highlight there (free lights, taken doesn't), touching it only on change; `ClearHover()`.
- **`TryStore(loot, worldPoint)`** → `PackResult`: `Gold` (adds to `Gold`, `Loot.Collect()`, pile grows, full at `goldForFullPile` 20, number rewritten), `Objective` (`PlayerInventory.TakeObjective()`; **never taken out again**), `Stored`, `NoRoom`. `PutIn()`: `Grabbable.Stow()`, parented, upright, old parent/scale remembered.
- **Shrink** (`Tick()`, `ApplyShrink()`): 0→1 over `shrinkDuration` 0.12s to `fitFactor` = `cellSize x fit(0.8) / (2 x HoldRadius)`, max 1; `Grabbable.LocalCentre` (not origin) kept on space middle, one shrunk radius off the board.
- **Take out**: `CanTakeFrom(cell)`, `BeginTake(cell)` grows it; at full size `TakeOut()` restores parent/scale, frees space, holds it for `TryPopTaken(out loot)`; caller `Unstow()`s and hands it over, drops or returns it. One at a time. **Gotcha: stays stowed until hand-over** - unstowed a frame early, `PlayerHandHolding.Tick()` picks it up before `PlayerPack.Tick()` and the pack "puts back" a held prop; so `PlayerPack` unstows in the pick-up call, after the holding tick.
- Calls `PlayerInventory.AddLoot`/`RemoveLoot` on in/out.
- **Gold number** (`BuildGoldNumber()`): 3D `TextMeshPro` (not canvas) at top of gold space, proud of pile, facing player; `goldNumberHeight` 0.03m, `goldNumberColor`, no shadows, depth-tested. Set only in `UpdateGoldPile()` via `SetText("{0}", Gold)` (no alloc); hidden at 0. If TMP Essential Resources are missing (`Resources.Load<TMP_Settings>("TMP Settings")` null) no number, with a warning.
- **`PackSlot`** — grid space as `IHandTarget` (`Pack`, `Index`, `SetTargetable()` toggles trigger), in `HandTargetRegistry`. Stands in for the stored item's disabled colliders, so the reticle shows on takeables and empty spaces never block rays.

## Keys

- **`Key`** — `[RequireComponent(typeof(Grabbable))]`; `keyId` (= door's Key Id), `color` (tints renderers by property block in `Awake()`). Static `Grabbable → Key` dict, `Key.Find()`. Stored like loot: near the pack it lights the keyring space wherever held (`Pack.HoverKey()`); released, `Pack.TryStoreKey()` adds it and `Collect()` switches it off. No coins.
- **`Keyring`** — up to `MaxKeys` 6 ids/colours in fixed arrays (`Add()`, `Has(keyId)`, `TryGetColor()`, `Count`); square ring mesh + hanging bar per key, hidden at load, shown as added. `ShowKeyInLock(isShown, color)`: bar from ring middle along Z. Made by `Pack.Build()` (shares material, block mesh); real size. Never moves itself: holder parents it. Axes X right, Y up, Z away.
- **In `Pack`**: keyring space top right (`KeyringSpace` -3, `ObjectiveSpace` -1, `NoSpace` -2), own `PackSlot`, on only while home **with at least one key**. `Keyring`, `IsKeyringHome`, `TakeKeyring()` (false if shut, away or empty), `ReturnKeyring()`. `CanTakeFrom()` false for it.
- Using it: `Player.PlayerKeys`; the lock: `Interaction.KeyLock`.

## Loot

- **`Loot`** — `[RequireComponent(typeof(Grabbable))]`; `coinLevel` 1-3 (`Value` = coin level), `isGold`, `isObjective`. Static `Grabbable → Loot` dict (`OnEnable`/`OnDisable`); `Loot.Find(grabbable)` instead of `GetComponent` (misses it in a closed pack: inactive). `Collect()` deactivates, never destroys.

## Inventory data

- **`PlayerInventory`** — Player root, static `Instance`. **Data only**, no `Update()`, remade on restart. `LootTotal`/`LootCount` (`AddLoot`/`RemoveLoot(value)`, gold included); `HasObjective` (`TakeObjective()` calls `LevelManager.SetObjectiveCarried(true)` - ending stays LevelManager's job); `Owns`/`GiveTool(tool)`, start from `ownsBlackjack`/`ownsCrossbow`; `GetBoltCount`, `AddBolts(bolt, count)`, `TryUseBolt`, `TryAddBolt` (false if full); start counts `waterBolts`..`sleepBolts` (default 0); **cap `maxBoltsPerKind` 3**, start counts and `AddBolts()` clamped. **`Changed`** (C# instance event) after any change.
- **`ToolType`** (`Blackjack`, `Crossbow`), **`BoltType`** (`Water`, `Noisemaker`, `Rope`, `Stun`, `Sleep`): append only, no explicit numbers (`BoltType` is an array index). Lockpicks aren't a `ToolType`.

## Debug

- **`InventoryDebug`** (`Inventory/Debug`, Player root) — logs inventory on each `Changed`; only place exact gold shows.
- **`LootTestProps`** (`Inventory/Debug/Editor`, **TeaLeaf > Dev Room > Add Loot Test Props**) — table 1.2m ahead, 1.7m right of main camera, more loot than fits: 4 coins (gold, 1), 4 purses (1), 3 goblets (2), 2 candlesticks (2), crown (3), idol (3, objective); `Assets/Art/Materials/LootGold.mat`; via `Player.GrabbableTestProps.MakeGrabbable()` + `Loot`. Adds if missing: `PlayerInventory`, `InventoryDebug` (Player root), `PlayerPack` (Hands), `Pack` child of Player root (outside the props so rebuilds keep its tuning).
