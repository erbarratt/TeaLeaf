# CLAUDE.md

Guidance for Claude Code in this repository.

## Project overview

TeaLeaf: Thief-style VR stealth game, Unity 6000.3.25f1, URP, OpenXR + new Input System, **no XR Interaction Toolkit**. Keep it lean: add a package/third-party component only when clearly needed.

**Target: PCVR, 90 fps with 25% headroom at ~2000x2000 per eye on RTX 3060 12 GB, Ryzen 5 5600X-class 6-core, 16 GB RAM** = 8.3 ms/frame CPU and GPU each. That rig is the floor ("3060 upwards"); budget is measured against it, not the dev machine. Dev/test: Quest 3 via Virtual Desktop. **Quest 3 standalone is a future option, not a target**: nothing built for it, nothing may make a port impossible.

Movement, turning, gravity, crouch, climbing, ladders, ropes are all hand-built (control, debuggability, cross-headset consistency, understanding). **Never suggest XRI (or its locomotion, teleport, climb or interaction components).** No teleport locomotion, ever.

## Choosing a solution

- **Every solution is the simplest, most stable, most performant way.** Fewer moving parts, well-trodden over clever, cheaper at runtime; no unrequested options, layers or generality.
- **Where there are levels/options of complexity, stop and explain before building** (as for a question, no file changes): each option, its cost (build, maintain, runtime), what it gives, comparison, recommendation and why; then wait for the maintainer's choice. Applies when the three aims pull apart, when a request could be met at more than one scale, and when a third-party package is a route. One obvious simplest way: just build it.

## Where the detail lives

Root = rules + cross-cutting architecture. Per-system `CLAUDE.md` next to the code (load in that folder):

- `Assets/Scripts/Player/CLAUDE.md` - all player systems (input + action table, locomotion, `MovementState`, climbing, mantling, hand visuals/snapping/physical hands, hand rays/reticles, hand animation, crossbow, blackjack, compass), hand art/animation, Debug scripts (incl. `InHeadsetGizmos`: per-type gizmo toggles, noise circles).
- `Assets/Scripts/Interaction/CLAUDE.md` - hand targets (`IHandTarget`, registry), grabbables, doors (`Door`, `DoorHandle`, `DoorLock`, `DoorBolt`, `KeyLock`), chests (`Chest`, `ChestLid`), lockpicking (`PickableLock`, `BigLock`), climbables (`IClimbable`, `ClimbableEdge`, `Ladder`, `ClimbableRope`), hand snap poses.
- `Assets/Scripts/Core/CLAUDE.md` - game state/restart (`GameState`, `LevelManager`, `ExitZone`), `ScreenFade`, noise and sound (`NoiseSystem`, `SoundCue`, `SoundRoom`, `SoundPortal`, `SoundPropagation`, `SoundPlayer`, `SoundLoop`, `SoundLoopPlayer`, `ListenerReverb`, `RoomAmbience`, `ReverbSettings`, `SpatialVoice`), render settings, surfaces (`SurfaceType`, `SurfaceTag`, `SurfaceSounds`, `RampCollider`, `BarrelCollider`, `EnvironmentVariants`), gameplay light (`SceneLight`, `Moonlight`, `LightSource`, `Flame`), night sky (`ProceduralSky`, `MoonSurfaceBuilder`), `MoonShaft`, level surface shaders, debug drawing (`DebugLines`, `IDebugDrawable`).
- `Assets/Scripts/Inventory/CLAUDE.md` - pack (`Pack`, `PackSlot`), `Loot`, keys (`Key`, `Keyring`), inventory data (`PlayerInventory`, `ToolType`, `BoltType`).
- `Assets/Scripts/AI/CLAUDE.md` - guards (`Guard`, `GuardState`, `PatrolRoute`), NavMesh baking, test guard set-up.
- `Assets/Scenes/CLAUDE.md` - level building guidelines (hierarchy, collision, layers, static flags, lighting, culling, sound, measured against a shipped VR stealth level) and scene review checklist. Read before building/changing/reviewing level geometry.
- `Assets/Scripts/LevelGen/CLAUDE.md` - level generator (in progress): prefab catalogue + annotations, layout spec from a written brief, street/building placement, guards' NavMesh, regeneration, placement report.

A new system folder (`UI`) gets its own `CLAUDE.md` with its first system. Read the relevant one before changing a system not read this session, even from another folder.

## Project docs - keep up to date

- **`CLAUDE.md` files**: update as part of any change, unasked, whenever architecture, conventions, systems, file locations or design decisions change. Detail in the per-folder file; only cross-cutting rules/architecture here.
- **`Assets/DEVLOG.txt`**: dated entries, newest on top: what was done, bugs and fixes, lessons. Plain-text style.
- **`Assets/DEVROADMAP.txt`**: phased plan to a greybox vertical slice of one level (docks warehouse). Tick `[x]` when done; edit when scope/order/design changes.
- **These files describe what is true now, not history.** Rewrite text to the new state; never note that something changed, was decided on a date, replaced a plan, or was tried and dropped (CLAUDE.md, roadmap, devlog). A rejected approach survives only as a short current rule where it stops a likely wrong suggestion.
- **Devlog and roadmap are written once, at session end, only when the maintainer says so.** Until then track the session's bugs, fixes, lessons, items completed. If a session's work was never written up, remind the maintainer at the start of the next. Both files are large: read only the devlog's top and the relevant roadmap phase.

## Coding standards

Follow for new code; flag (don't silently reformat) existing code that doesn't match.

- **Namespaces:** top-level only (`namespace Player { }`, never `Player.Locomotion`).
- **Serialized fields:** no underscore (`[SerializeField] private float moveSpeed;`). **Private non-serialized:** leading underscore (`_snapTurnQueued`), static readonly caches too (`_gripCurlParam`).
- **Public properties:** no underscore; prefer auto-property with private setter (`public Vector2 MoveAxis { get; private set; }`) over a passthrough expression body when the value should be cached/set elsewhere. Cheap derived values as expression bodies are fine (`IsLeftGrabbing => LeftGrip > 0.5f`).
- **Comments:** `/// <summary>` on methods; detailed inline comments encouraged (project is a teaching exercise: build new systems step-by-step with explanation, not a finished file at once). Never empty comment lines.
- **Braces (hybrid, keep consistent, don't unify):** control flow (`if`/`for`/`while`/`switch`) same line `if (x) {`; class/method/property declarations Allman (own line).
- **UI draws last, on top**, never hidden by world or hands (hand reticles, mantle arrow, markers). Materials via `OverlayMaterial.Create()` (`Scripts/Core`; shader `TeaLeaf/Overlay`: Overlay queue, `ZTest Always`, stereo-safe), not depth-tested shaders; built-ins can't do this (see `Scripts/Player/CLAUDE.md`). Things held/worn on a hand (pack, compass) are objects, depth-tested like the hand.
- **Debug scripts:** always in a system `Debug` subfolder (`Scripts/Player/Debug`, `Scripts/Inventory/Debug`, `Scripts/AI/Debug`...), never beside runtime code.
- **Gizmos must show in the headset:** implement `IDebugDrawable`, draw through `DebugLines` (never `Gizmos` directly) so Scene view and headset (`InHeadsetGizmos`) share code; plain Gizmos show in one eye only. See `Scripts/Core/CLAUDE.md`; a new drawable type also gets a toggle in `InHeadsetGizmos` (`Scripts/Player/CLAUDE.md`).
- **Performance habits:** no per-frame allocations; cache `Animator.StringToHash`/`Shader.PropertyToID`; `MaterialPropertyBlock` not `.material`; touch renderers/materials only on state transitions; self-registering static lists/registries over scene searches or interface `GetComponent`.

### Performance rules

Design new systems, shaders, content and editor builders against these from the start. Say when a request breaks one and offer the cheaper route. Tasks: `Assets/DEVROADMAP.txt` ("Performance rules" block, Phases 6, 8, 10).

- **Budget 8.3 ms/frame CPU and GPU each on the target rig.** Nothing measured on such a machine yet: any cost is a judgement and must be called one.
- **Pixels are the biggest cost** (8M/frame x 90). Never *depend* on a full-screen/post effect or camera depth/opaque texture (an optional, switchable one is allowed once measured). No parallax occlusion on view-filling surfaces. Procedural surface shaders (cobblestone, brick, water) acceptable subject to measuring. Small transparent overdraw; UI/markers simple unlit shapes. 4x MSAA wanted (thin edges shimmer).
- **Shaders:** cheapest lit model that works; SRP Batcher compatible (`CBUFFER_START(UnityPerMaterial)`); single-pass-instanced stereo macros; per-vertex over per-pixel where it looks the same.
- **Lighting:** baked where possible, light probes for movers; few, short-range realtime lights. **Torches may cast realtime shadows from moving things** (guards, props, hands) with level shadows baked - Mixed lights, Shadowmask, Indirect Multiplier 0 so an extinguished torch leaves no baked glow (planned, untested) - as a switchable quality setting, never drawing the whole level into a torch shadow map. **Max four mixed lights overlap at any spot** (shadowmask limit; moon counts if Mixed).
- **Static level geometry**: Static, on `Environment`, few shared materials (batch, bake, occlusion-cull); editor builders making level/test geometry set this. **A probe-lit renderer is never Batching Static** (static batching reads probes at the merged mesh centre); the variants tool keeps this (`Assets/Scripts/Core/CLAUDE.md`). Runtime-made renderers: shadow cast/receive off unless needed.
- **Depth is modelled, detail is textured:** shapes the player gets close to are real geometry; normal maps for fine detail only.
- **Layout culls:** break long sightlines with level shape and doors, plus an occlusion bake. Distance haze, nearer small-prop cull distances, impostors: reserves, only when profiling asks.
- **CPU work is sliced:** costly work (AI senses, pathfinding, light sampling, sound paths) a few times a second on staggered timers, never per frame, less when far/unseen. Physics queries non-allocating with their own `LayerMask`.
- **Nothing created/destroyed during play:** pool bolts, effects, audio voices; build runtime meshes/materials once at load.
- **Quest-port compatible:** all of the above except realtime torch shadows, optional post-processing and procedural shaders (a port drops or bakes those).

## Working with this codebase

- Unity project; no CLI build/lint/test, no automated tests. Builds and Play Mode via the Editor (exact version in `ProjectSettings/ProjectVersion.txt`).
- **Claude drives the open Editor via Unity's CLI** (`unity`, in user's `AppData/Local/Unity/bin`) + experimental editor-only `com.unity.pipeline` (in `Packages/manifest.json`): `unity status` (reachable?), `unity command recompile_status` / `console` / `save_scene`, `unity command eval --code '...'` (C# in the Editor; project `internal` types via reflection).
- **No scene is used, checked or suggested until the maintainer names it.** **Code, comments and docs never name a scene** unless what they describe is unique to it (mechanics work in every scene): code works on the open scene or takes a path, never a hard-coded scene path. After any level generator change, run it on the spec in use in the named scene (none named: ask) and save it (`Assets/Scripts/LevelGen/CLAUDE.md`).
- Root `.sln`/`.csproj` are Unity-generated; never hand-edit.
- All gameplay scripts compile into `Assembly-CSharp`; no `.asmdef` files.

**Git:** remote `origin` (GitHub), branch `master`. **One commit per session**, at the end, with the devlog/roadmap wrap-up, unless the maintainer asks sooner. Unity `.gitignore` excludes generated folders, `.csproj`/`.sln`, `.idea/`. Always commit `.meta` files with their assets. `*.blend` (`Blends/`) ignored (exceed GitHub's 100 MB; need Git LFS to track). `Assets/ASTROFISH_GAMES` (bought art packs, 3.9 GB) ignored: a fresh clone re-imports from the Asset Store, converts materials to URP (Window > Rendering > Render Pipeline Converter, Material Upgrade), caps textures at 2048, turns Read/Write off on every pack model (meshes never read at runtime; readable = second copy in memory). Mesh compression stays off (saves nothing at runtime, can open seams between modular pieces).

## Architecture

### Frame ordering: explicit `Tick()` pattern

`PlayerController` (Player root) is the only player script with `Update()`; it calls every player system's `Tick()`-style method explicitly for deterministic order. Sequencing and wiring only, no gameplay logic (decisions live in the owning system). Order:

1. `playerInput.Tick()` (first: caches input); `playerTracking.Tick()` (smooth hand poses; if headset recentred since last frame, put view upright and at standing height) - before anything reads head/hands. **Once the level has ended** (`LevelManager.StateChanged` -> not `Playing`; caught or won, during end fade) **frame stops here**, body frozen; only `playerHandInteraction.Tick()`, `playerHandDoors.TickHeld()`, `playerHandVisuals.Tick()`, `playerHandHolding.TickHeld()`, `playerHandThrowing.Tick(false)`, `TickReticles()`, `playerHandAnimation.Tick()` run, so hands (and what they carry) follow the controllers.
2. `playerLocomotion.TickBody()` - re-centre capsule under headset, apply crouch height (before hand systems: crouch moves the tracked hierarchy).
3. `playerHandInteraction.Tick()` - hand rays + targets (reticles placed at 8b).
   3b. Reach-taken items, before the other grab systems (a hand taking one must already be busy when climbing/carrying/doors run); each skipped while mantling and if the rig lacks the component: `playerPack.TickSummon()` (bring pack out by reaching over a shoulder + grip, or put away on release); `playerLockpicking.Tick()` (take picks, put in lock, take hold of a pick on the big lock); `playerKeys.Tick()` (take keyring from pack, put in lock, take hold of key); `playerCrossbow.TickWheel()` (other hand taking the wheel - ray on it + grip, like a prop - and either hand picking up a bolt its ray is on); `playerBlackjack.Tick()` (drawing at the chest).
4. `playerClimbing.Tick()` - grab/release, zip line, climb movement (skipped while mantling).
   4a. `playerHandHolding.Tick()` - pick up/drop props (skipped while mantling; after climbing, a hand grips a climbable or carries a prop); `playerPack.Tick()` - loot released at the pack goes in, items taken out (skipped without `PlayerPack`); `playerHandDoors.Tick()` - take/release door handles (skipped while mantling and without `PlayerHandDoors`; last of the three); `playerCrossbow.Tick()` - crossbow becomes active/stops (skipped while mantling and without `PlayerCrossbow`; after every grab system so a grab always wins); `playerMantling.Tick()` - detect (arrow), start on stick push, or advance a mantle. **While `IsMantling` the frame stops here** (mantle positions the rig directly, CharacterController disabled): only `TickState()`, `playerVisibility.Tick()`, `playerKeyholes.Tick()`, `playerHandDoors.TickHeld()`, `playerHandVisuals.Tick()`, `playerHandHolding.TickHeld()`, `playerHandThrowing.Tick(false)`, `TickReticles()`, `playerHandAnimation.Tick()` run - no locomotion, turning or `Move()`.
5. `_frameMovement = playerLocomotion.TickMovement(isClimbing)` (stick + gravity; zero while climbing) + `playerClimbing.FrameMovement` while climbing; then `playerLocomotion.TickTurning()`.
6. One `characterController.Move(_frameMovement)` (Unity calls `PlayerBodyPushing.OnControllerColliderHit()` during it: body pushes open doors), then `playerClimbing.ReportAppliedMovement()` while climbing.
7. `playerLocomotion.TickState(isClimbing, appliedMovement, collisionFlags)` - after `Move()`: needs real applied movement, returned `CollisionFlags` (stop momentum into walls/ceilings), and updated `isGrounded`.
   7b. `playerFootsteps.Tick(appliedMovement)` - after `TickState()` (needs this frame's `MovementState` + real movement); skipped without `PlayerFootsteps`, not run in a mantle or after level end.
   7c. `playerVisibility.Tick()` - after `TickState()`: samples light where the body ended up, applies `MovementState`; skipped without `PlayerVisibility`; runs in a mantle, not after level end.
   7d. `playerKeyholes.Tick()` - after `Move()`: keyhole near the head opens by final head position; skipped without `PlayerKeyholes`; runs in a mantle, not after level end.
8. `playerHandDoors.TickHeld()` - hand on a handle turns the lever and swings the door, after `Move()` and straight before visuals (hand then snapped onto the handle where the door now is). `playerLockpicking.TickHeld()` - hand on a pick turns it, big lock runs (pins, fade), picking given up if the player moved away (runs wherever `playerHandDoors.TickHeld()` runs, every path). `playerKeys.TickHeld()` - key in a lock turned by the wrist; keyring returns to pack if player moved away (every path). `playerHandVisuals.Tick()` - after turning/`Move()`: visuals are rig children, so an earlier world-space snap pose would be dragged off, and the physical hand sweep needs the final controller position. (`TickHandVisuals()` runs the pair.)
   8a. `TickHeldProps()`: `playerHandHolding.TickHeld()` - once a visual reaches the prop being picked up, parent the prop to it (straight after visuals; skipped without `PlayerHandHolding`); a carried prop is a hand-visual child, nothing places it per frame. `playerHandThrowing.Tick(canAim)` - arc starts from the prop's current position (skipped without `PlayerHandThrowing`; `canAim` false in a mantle and after level end: nothing new aimed, a launch under way still finishes). `playerPack.TickHeld()`. `playerCrossbow.TickHeld(canAim)` - wheel, arc, shot, bolts in flight (bolts fly on every path; no aim/shot when `canAim` false). `playerCompass.Tick()` - compass placed on the wearing hand visual's wrist (every path; skipped without `PlayerCompass`).
   8b. `playerHandInteraction.TickReticles()` - after grabs and visuals, so a hand that grabbed this frame hides its reticle.
9. `playerHandAnimation.Tick()` - last; reads this frame's snap weight from step 8 (Animators evaluate after all `Update()`s anyway).

New per-frame player systems get a `Tick()` (or `Tick...()` returning a movement contribution) and a slot in `PlayerController.Update()`. `PlayerController.Reset()` auto-fills references (root via `GetComponent`, hands via `GetComponentInChildren`).

**One `Move()` per frame:** `Update()` zeroes `_frameMovement`, adds each contribution, calls `characterController.Move()` once. New movement sources (ladders, knockback...) return/expose a contribution, never call `Move()`. Only exception: a mantle positions the rig directly. Everything in **`Update()`, not `FixedUpdate()`** (CharacterController and VR tracking want per-frame, lower latency).

### Player hierarchy

```
Player                 [Player layer] CharacterController, PlayerTracking, PlayerInputXR,
                       PlayerHaptics, PlayerLocomotion, PlayerClimbing, PlayerMantling,
                       PlayerFootsteps, PlayerVisibility, PlayerKeyholes, PlayerBodyPushing,
                       PlayerInventory (Inventory ns), PlayerController,
                       PcPlay (Debug; disables itself unless LevelManager's PC Play is on)
  Camera Offset        (saved y 1.6m = standing eye height; crouch shifts it)
    Main Camera        Tracked Pose Driver (Tracking/Head*)
      Mantle Indicator (MantleIndicator - head-locked, a little below centre ~0.5m ahead)
      Screen Fade      (Core.ScreenFade - at camera local origin)
    Hands              PlayerHandInteraction, PlayerHandHolding, PlayerHandThrowing,
                       PlayerPack, PlayerKeys, PlayerHandDoors, PlayerLockpicking, PlayerCrossbow,
                       PlayerBlackjack, PlayerCompass, PlayerHandVisuals,
                       PlayerHandState (added at runtime if none), PlayerHandAnimation
                       (identity transform)
      Left/Right Throw Arc   (runtime, PlayerHandThrowing)
      Crossbow Arc           (runtime, PlayerCrossbow)
      Blackjack              (runtime, PlayerBlackjack; inactive here, moved onto a hand
                             visual while drawn)
      Compass                (runtime, PlayerCompass; placed each frame on the wrist of the
                             lockpicks' hand visual, never its child)
      Left Hand        [PlayerHands] tracked controller - Tracked Pose Driver (Tracking/LeftHand*)
        Left Hand Visual   (hand.fbx instance, Animator)
          Lockpicks        (runtime, PlayerLockpicking; left visual for right-handed; moves to
                           other visual or into a lock in use)
          Crossbow         (runtime, PlayerCrossbow; visual without the lockpicks: right for
                           right-handed)
          Pack             (Inventory.Pack - saved as Player root child, moved by PlayerPack onto
                           the hand visual that brings it out; active only while out)
        Left Hand Reticle
      Right Hand       [PlayerHands] same, visual mirrored (scale.x -1)
```

- **A hand does one thing at a time** (climbing, carrying, door, lockpicks, keys, holding pack, crossbow active or its wheel, blackjack): before taking a hand a system asks `PlayerHandState.IsBusyExcept()`, never checks other systems itself. New hand systems are added there once (`Assets/Scripts/Player/CLAUDE.md`).
- Body/movement systems on root, hand systems on `Hands`. `Hands` stays under `Camera Offset` with identity transform (tracked poses are relative to it; crouch moves `Camera Offset`).
- **Head re-updates before render; hands don't.** Main Camera TPD: "Update And Before Render"; hands' TPDs: **"Update"** only (before-render on hands made carried props lag). Don't switch back. World-fixed things (snapped hand, hand held off a wall) are detached from the controller.
- **Controller objects hold a smoothed pose:** `PlayerTracking.Tick()` overwrites the TPD pose with a One Euro filtered one (relies on hands' TPDs being "Update" only). Climbing uses it too; nothing reads raw tracking. Detail in `Assets/Scripts/Player/CLAUDE.md`.
- **Tracking:** XR tracking in **Device** mode (not floor); `Camera Offset` 1.6m gives every player the same standing eye height, real crouching still lowers it. `PlayerTracking` calibrates the view (`calibrateView`: flips an upside-down head, shifts to standing height) under the start fade and again on recentre (VD/SteamVR often start upside down with origin on the floor, so `Camera Offset` isn't always at its saved pose). TPDs read the project's `Tracking` action map (in `InputSystem_Actions`). Controllers bind the **pointer (aim) pose, not grip** - visuals, rays, snap offsets are tuned to it. Untracked controllers stay active holding last pose. Detail: `Assets/Scripts/Player/CLAUDE.md`.

### Physics layers

User layers 6-15: `Environment` (static world), `Player` (CharacterController, Player root), `PlayerHands` (Left/Right Hand), `Interactable` (grabbables, doors, loot, locks), `Climbable` (`ClimbableEdge`s, ladders, ropes), `Guard` (body capsule), `GuardHead` (blackjack target), `Projectile` (bolts), `PlayerBlocker` (invisible player-only boxes), `Ragdoll` (guard bone colliders: bolt hit boxes, the body when knocked out). Unassigned = `Default` (collides with everything).

Matrix pairs ON: Environment with Environment/Player/PlayerHands/Interactable/Guard/Projectile/Ragdoll; Player-Interactable; Player-Guard; PlayerHands-Interactable; Interactable with Interactable/Guard/Projectile/Ragdoll; Guard-Projectile; Projectile-Ragdoll; Ragdoll-Ragdoll; Player-PlayerBlocker. All else off (notably Player-PlayerHands, Player-Ragdoll (bodies walked through), Guard-Ragdoll, and Climbable with everything: it's a grab volume).

- **`PlayerBlocker` collides only with `Player`; no query mask includes it** (hands, props, bolts, guards, hand rays, light rays, guard sight pass through). New query masks must leave it out. Usage in `Assets/Scenes/CLAUDE.md`.
- **A carried prop moves to `PlayerHands`** (back on drop; also a hand-visual child) so the body doesn't hit it and hand rays, hand sweep and light rays ignore it.
- **Doors**: `Interactable` (leaf + handle trigger grab volume), kinematic Rigidbody turned from code; checks `Player` layer itself so it never swings into the body; body walking into an open door pushes it (`PlayerBodyPushing`).
- Matrix doesn't affect queries; each passes its own `LayerMask`: hand rays Environment + Interactable + Climbable (walls block); physical hand sweeps Environment + Interactable; light rays (`SceneLight`) Environment + Interactable (both block light); crossbow bolts Default + Environment + Interactable + Ragdoll (limbs, never the `Guard` capsule).

### Vertical slice design (full plan: `Assets/DEVROADMAP.txt`)

Target: one docks/warehouse level - steal an objective, escape; caught = fail and restart.

- **Visibility:** from level geometry; only lights are hand-placed. Night levels: base light **moonlight**; a point is in full shadow when geometry blocks one physics ray towards the moon (gameplay shadows match rendered). **Light sources** (torches, lamps) add light in range, blocked likewise; extinguishing = disabling. Brightest light wins. Crouch/sprint modify: standing in moonlight is visible, crouching only from close. Shadow map not read back (too slow); rays stand in. No designer-placed shadow/light volumes.
- **Noise:** footsteps by surface (scaled by stance), thrown/dropped physics objects, doors/interactions - via a shared noise event system guards listen to.
- **Sound:** must tell the player truthfully where it is on stereo headphones. Thief-style designer-placed sound rooms joined by portals (doorways, stairwells); sound passes room to room only via portals, never through walls, heard from the last portal at the whole path's distance (a guard upstairs is heard from the stairwell or not at all). Closed doors muffle (high end rolled off). Guard hearing uses the same paths. No ray-cast muffling inside a room. **Reverb is the listener's room's, not the sound's**: one shared reverb tuned per `SoundRoom`. `SoundLoop`s and per-room ambience follow rooms. Direction from the project's own spatialiser (`SpatialVoice`: interaural time difference, head shadow, duller from behind; no up/down cue). No third-party audio package; Steam Audio is the fallback if up/down matters. Detail: `Assets/Scripts/Core/CLAUDE.md`.
- **Guard AI:** patrol -> suspicion -> search -> chase -> catch (no combat). Knockout state; **a knocked-out guard can always be woken by an awake one** that sees it and walks over. **A guard hit by a bolt that does nothing to it becomes suspicious and searches where it was shot from.** Built (`AI.Guard`, working in headset): one detection meter filled by sight (player `Visibility`, distance, how much shows) and noises; baked NavMesh; looks a few times a second; catches via `LevelManager.Caught()`; model prefab in `Assets/Prefabs/Actors` animated by `GuardAnimation`. Guards have their own `Update()`, outside the player tick order. **Voice lines don't repeat** (not built): large pools per voice, a session-wide record (kept across restarts) blocks repeats until a pool is exhausted; idle chatter is sound only, no noise event. Detail: `Assets/Scripts/AI/CLAUDE.md`.
- **Weapons:** blackjack and hand crossbow; no weapon menu. **Nobody dies (guard or player); every weapon is non-fatal**, and aggression towards guards is dissuaded.
  - **Blackjack** (`PlayerBlackjack`, `AI.Guard.TryKnockOut()`; built, working in headset): drawn from the chest by an empty hand close to the chest, palm towards it, pressing grip; held while grip held; release puts it away (never dropped). Either hand. **Head-to-head swing knocks out if the guard isn't chasing and the player is behind it**; otherwise the guard turns on the player.
  - **Crossbow** (`PlayerCrossbow`; built, greybox, working in headset): **always mounted on the back of one hand**, lockpicks on the other (see Handedness). Active when grip pressed on that hand while empty, its ray on nothing grabbable (**a grab always wins**) and palm facing the floor; stays active until grip released. Shows a ballistic arc like an aimed throw's but much flatter/longer; bolt speed from an editor-set **range** (carry over level ground at best angle), computed once at load. **Trigger shoots on press** (throw is on release). Clockwork: self-winds, ready after a short wait. Kind chosen by gripping the wheel at the back with the other hand and turning it.
  - **Five bolt kinds**, each own model, each shot uses one from `PlayerInventory`, **max three of each carried**: **water** (blue tip) puts out every `Core.Flame` near where it lands; **noisemaker** makes noise where it lands for a while; **rope** (shaft wound with rope) hangs a climbable rope from where it sticks; **stun** (black tip) on a guard's head stuns 10s, then it comes round and searches; **sleep dart** hitting a guard anywhere knocks it out 10s later. Water and noisemaker are used up where they land. **The rest stick only into surfaces tagged as holding a bolt** (wood, plaster, carpet, grass) **and bounce off everything else**, guards included - a bounced bolt is a real physics object until it settles. **A stun bolt, or a sleep dart that hit no guard, is picked up** by pointing + grip: straight into inventory, never carried. Rope bolts never recovered; the rope bears weight only where the `SurfaceTag` has **Supports Rope Bolt** ticked - elsewhere it sticks but pulls out and the rope falls when the player takes hold.
- **Open flames can be put out, lamps can't:** torch/fire pit has a `Core.Flame` (switches gameplay light, visible light, flame and sound off together); lamp posts have none and burn on.
- **Chests** (`Interaction.Chest`, `ChestLid`): same lock kinds as doors (none, simple = pickable, keyed) via `PickableLock` / `KeyLock`. Once unlocked, a hand takes the lid front and lifts it.
- **Compass** (`PlayerCompass`; detail `Assets/Scripts/Player/CLAUDE.md`): **always worn on the back of the wrist of the lockpicks' hand**, further up the arm; read like a watch, no input, never makes its hand busy. World-aligned dial (**north = world +Z**), **light gem** showing `PlayerVisibility`, **arrows for the last few footsteps and voices heard** (never the player's own; no other sounds) pointing where heard from (the portal, for another room), sized by loudness, fading.
- **Text:** TextMeshPro (`com.unity.ugui`); font and shader in `Assets/TextMesh Pro` (TMP Essential Resources).
- **Controls:** left stick moves; right stick turns, up mantles, **down toggles crouch**; grip grabs; trigger aims a throw; **A jumps**; **B is the universal cancel** (aimed throw, for now); crossbow, blackjack and pack gestures as above/below; **left stick click toggles sprint**; X, Y, Menu, right stick click unassigned. **PC Play** (dev switch on `LevelManager`, off for a build): Play Mode at the desk without headset - WASD walk, mouse look, Space jump, C crouch, Left Shift toggles sprint; movement only, hands ride under the camera (`PcPlay`, `Scripts/Player/Debug`).
- **Tools:** lockpicks only; no others planned. Two-handed lockpicking with haptics (built: `PlayerLockpicking`, `Interaction.BigLock`, `PickableLock`): two picks worn on the back of the left hand snap into a simple lock's keyhole; a large copy of the lock fades in front of the door; right hand turns one pick clockwise through three stops and a last turn while the left sweeps the other to find each stop's random pin by haptics; a pick is turned by moving the hand round the lock, not twisting the wrist. Detail: `Assets/Scripts/Interaction/CLAUDE.md`, `Assets/Scripts/Player/CLAUDE.md`.
- **Inventory** (all built; `Assets/Scripts/Inventory/CLAUDE.md`): backpack of limited size, so the player keeps the most valuable loot. **Brought out by reaching over a shoulder with either hand + grip**; rides on that hand while grip held, release puts it away; the other hand puts loot in: 3x3 grid, one loot per space, chosen by holding loot over a free space; a gold space for small loot; separate spaces for objective and keyring. Carried loot shows one to three coins for worth. Coloured keys on the keyring, taken from the pack and held to a lock. **One coin level = one gold piece** (loot worth 1-3 gold); pack gold shown as a number (TextMeshPro). **Handedness** is one setting, `PlayerInputXR.leftHanded` (read as `IsLeftHanded`): off = lockpicks (and compass) left, crossbow right; on = swapped. Pack and keyring work in either hand; the lock puzzle is never mirrored.
- **Throwing, aimed and physical** (both built: `PlayerHandThrowing`, `PlayerHandHolding`; detail `Assets/Scripts/Player/CLAUDE.md`). Grip picks up. Aimed: hold that hand's trigger for a trajectory arc (distance from hand pitch, teleport-arc style); release plays a short hand launch (forward move of the visual, not a clip) and throws along the arc. **B cancels** (prop stays in hand); an arc landing on nothing is red and releasing throws nothing. Physical: move the hand and release grip to throw with the hand's movement; a still hand just drops.
- **Traversal:** sprint, jump, mantling, ladders, rope climbing (built); drag/hide KO'd bodies (with the guards).
- **Physical hands:** visual hand collides with the world, stops at surfaces, elastic-bands back to the controller once clear (Alyx / Thief VR); kinematic sweep, no Rigidbody.
- **Sky:** no skybox; camera clears to black; `Core.ProceduralSky` draws stars and the moon (disc where the Directional Light shines from). Picture only, lights nothing. **Moon shafts** (`Core.MoonShaft`, shader `TeaLeaf/MoonShaft`) are placed geometry built at load, never a screen effect; generator can lay one per alley, others hand-placed. **Off for now** (none in any scene; generator shaft width 0) until the look is right. Detail: `Assets/Scripts/Core/CLAUDE.md`.
- **Art:** slice must look like a finished game, not greybox.
  - **Shapes storybook-crooked**: leaning half-timbered houses storey by storey, swept roofs with flared eaves, bowed timbers, staggered chimneys. **Surfaces painted, Dishonored-style**: broad dabs, grimy washed-out palette, cool shadows, warm lights, light painted onto edges. **What the player stands on, holds or opens stays true** (flat floors, level straight ledges/rails, straight ladders, upright door hinges): crookedness is in the silhouette.
  - **Built from the Astrofish Games packs** in `Assets/ASTROFISH_GAMES`: `STYLZ_MVillage` (village walls, roofs, doors, windows, steps, bridges, fences, foliage, terrain), `DETAILED_MCastle` (castle, cliff rock), `DETAILED_FantasyMarkets` (stalls, goods).
  - **Level art is placed by hand or by the level generator (`Assets/Scripts/LevelGen`), nothing else.** Generator lays streets and buildings from real `Assets/Prefabs` variants under its own root (pipes, vents, walkways, lamp posts on facades; chimneys, dormers, railings on roofs; smoke/steam; grass and nettles; moon shafts over alleys), lays sound rooms/portals, bakes the guards' NavMesh over streets. **Street ground is the one thing it builds**: own meshes, one per level and per slope, vertex-painted for `TeaLeaf/GroundBlend` (dirt, cobbles, puddles blended by height, as Thief VR; `Assets/Scripts/Core/CLAUDE.md`). Other dressing, lighting, gameplay by hand. **Don't propose any other editor builder for level art.**
  - Pack folder git-ignored, its prefabs untouched. **Game prefabs live in `Assets/Prefabs`, each a variant of a pack prefab** adding Static + `Environment` layer, a collider, a `SurfaceTag` where walked on/shot at, scripts (`Door`, `Ladder`...). Levels use the variants, never the pack directly. **TeaLeaf > Modify Prefab > Make Environment Variants** bulk-makes variants for static pieces from selected pack prefabs/folders (`Environment` + Static; `Assets/Scripts/Core/CLAUDE.md`). Pack materials URP Lit, textures max 2048: keep so.
  - **Project copies and colour variants of pack materials** (`<material>_<suffix>`, drawn on by the generator), the lit window material and the street ground material live in `Assets/Art/Materials/Environment` (`Roofs`, `Walls`, `Extensions`, `Windows`, `Ground`); pack materials left as they are. Pack demo scenes, skyboxes, lighting settings, post effects and `SimpleCameraController` unused.
  - Organic models (guard, hands, hero props) from outside. Exceptions built from code by TeaLeaf > Dev Room editor builders: test areas (the Smithy textured - `Assets/Scripts/Interaction/CLAUDE.md` - the rest greybox) and props. Guard: Tripo model (`Assets/TripoModels/guard_1`), plain URP Lit, not yet painted style.
- **Out of scope:** settings/main menu, save/load, combat, fall damage, health.

### Planned systems

`Assets/Scripts/UI` is an empty placeholder (menus). New systems follow one-class, one-responsibility. Level generator (`Assets/Scripts/LevelGen`) built in stages; its `CLAUDE.md` holds design and which stages exist. **Ending the level always goes through `LevelManager`** (`Caught()`, `SetObjectiveCarried()`); no other system fades out or reloads the scene.

### Test areas

- **Test areas** from **TeaLeaf > Dev Room** (editor scripts in each system's `Debug/Editor`; real-level tools sit directly in the **TeaLeaf** menu), buildable any time: Sound Test House, Grabbable Test Props, Door Test Area (also sets up lockpicking incl. a `Big Lock` under `Gameplay`, and a keyed door), Chest Test Area, Smithy (textured, climbable blacksmith's house), locomotion course, town, sound test area, noise listeners, physical hands area, Guard Tests (floor with one guard per animation clip, each looping it, for matching clip feet to guard speeds). **Set Up Test Guard** puts a `Test Guard` on the Sound Test House path and bakes a NavMesh from `Environment` (`NavMesh` at scene root; rerun after geometry changes). Never assume any is present. **Add Hand Tools To Player** adds missing crossbow, blackjack, compass components to `Hands`.