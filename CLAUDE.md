# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

TeaLeaf is a Thief-style VR stealth game (Unity 6000.3.25f1, Universal Render Pipeline) built
on OpenXR and the new Input System, with no XR Interaction Toolkit: the project started from
XRI's XR Origin sample rig, and every XRI part has since been replaced by the project's own
code or removed (the package itself on 2026-09-30). Keep it lean - add a package or third-party
component only when it's clearly needed. **The target is PCVR
on a rig most Steam players have or better** (the maintainer's decision, 2026-10-10, replacing
"PCVR first, then Quest 3 standalone as the real target"): **90 fps with 25% headroom at about
2000 x 2000 pixels per eye on an RTX 3060 12 GB, a 6-core processor of Ryzen 5 5600X class and
16 GB of RAM** - a frame budget of 8.3 ms for the CPU and for the GPU, of 90 fps's 11.1 ms.
That rig is the floor, "3060 upwards": the game must hit the target there, and faster machines
get the spare. For scale, Valve's September 2026 Steam Hardware Survey has the RTX 5070 as the
single most common card (5.86% of systems), then the RTX 5060 (4.20%), 5060 Ti (3.87%), 4060
(3.72%) and 3060 (3.54%); 32 GB is the most common memory (42.2%), 16 GB next (37.8%), 8 GB
6.5%; 8 cores 30.1%. The most common rig (5070, 8 cores, 32 GB) was briefly the target the
same day and dropped: weaker cards are together more common than the 5070. Dev and test are on a Quest 3 streamed through
Virtual Desktop (originally an HP Reverb G2 v2 with Oasis drivers through SteamVR). **Quest 3
standalone is an option for the future, not a target**: nothing is built for it now, but
nothing should make a later port impossible. Performance is still an ongoing priority - the
budget is measured against that rig, not the dev machine.

Unity's built-in locomotion system is deliberately not used — the XR Origin prefab was unpacked
and its `Locomotion` object, with all its providers (Move, Turn, Snap Turn, Continuous Turn,
Teleportation, Climb, Grab Move, Jump), deleted. Movement, turning, gravity, crouching,
climbing, ladders and ropes are all hand-built instead, for full control, easier Quest 3
optimization, easier debugging, consistent behavior across headsets, and a better
understanding of how everything works. **Do not suggest bringing back XRI (or its locomotion,
teleportation, climb or interaction components).** Teleport locomotion will never be used.
Quest 3 input and tracking have been verified working.

## Where the detail lives

This root file holds the rules and the cross-cutting architecture. Per-system detail lives in
`CLAUDE.md` files next to the code, which load when working in that folder:

- **`Assets/Scripts/Player/CLAUDE.md`** — every player system (input + the input action table,
  locomotion, `MovementState`, climbing, mantling, hand visuals/snapping/physical hands, hand
  rays and reticles, hand animation), hand art/animation, Debug scripts, and the locomotion and
  hands design decisions.
- **`Assets/Scripts/Interaction/CLAUDE.md`** — hand targets (`IHandTarget`, registry),
  grabbable props, doors (`Door`, `DoorHandle`, `DoorLock`, `DoorBolt`, `KeyLock`), lockpicking (`PickableLock`,
  `BigLock`), climbables (`IClimbable`,
  `ClimbableEdge`, `Ladder`, `ClimbableRope`) and hand snap poses.
- **`Assets/Scripts/Core/CLAUDE.md`** — game state and level restart (`GameState`,
  `LevelManager`, `ExitZone`), the screen fade (`ScreenFade`), noise events and sound
  propagation (`NoiseSystem`, `SoundCue`, `SoundRoom`, `SoundPortal`, `SoundPropagation`, `SoundPlayer`,
  `SoundLoop`, `ReverbSettings`, `SpatialVoice`; loops, per-room reverb, room ambience and
  the project's own spatialiser written 2026-10-10), Quest foveated rendering (`FoveatedRendering`), surfaces (`SurfaceType`, `SurfaceTag`,
  `SurfaceSounds`), gameplay light (`SceneLight`, `Moonlight`, `LightSource`), the procedural
  night sky (`ProceduralSky`, `MoonSurfaceBuilder`), shared debug
  drawing (`DebugLines`, `IDebugDrawable`, `InHeadsetGizmos` - gizmos that also show in the
  headset).

- **`Assets/Scripts/Inventory/CLAUDE.md`** — what the player owns and carries: the
  pack (`Pack`, `PackSlot`), loot (`Loot`), keys (`Key`, `Keyring`) and the inventory data (`PlayerInventory`,
  `ToolType`, `BoltType`), and the maintainer's design for the pack and keys. Started
  2026-10-09.

New system folders (`AI`, `UI`) get their own `CLAUDE.md` when their
first system lands. Read the relevant one before changing a system you haven't read this
session, even when working from another folder.

## Project docs — keep these up to date

- **`CLAUDE.md` files** (this one and the per-folder ones) — update as part of any change,
  without being asked, whenever architecture, conventions, systems, file locations, or design
  decisions change, so they never describe the project inaccurately. Put detail in the
  per-folder file for the system; only cross-cutting rules and architecture go here.
- **`Assets/DEVLOG.txt`** — dated entries, newest at the top, describing what was done, bugs hit
  and how they were fixed, and what was learned. Keep its plain-text style.
- **`Assets/DEVROADMAP.txt`** — the phased plan from the current state to a greybox vertical
  slice of one level (docks warehouse). Tick items `[x]` as they're completed, and edit it when
  scope, order, or design decisions change (also note roadmap changes in the devlog).

**The devlog and roadmap are written once, at the end of a session, only when the maintainer
says so** (decided 2026-09-27) — not after each change. Until then, keep track of what the
session did (bugs, fixes, decisions, lessons, items completed or changed) so the wrap-up entry
is complete. If a session's work was never written up, remind the maintainer at the start of
the next one. Both files are large: read only the top of the devlog and the relevant roadmap
phase rather than the whole file.

## Coding standards

These are explicit project conventions — follow them for new code, and flag (rather than
silently reformat) existing code that doesn't yet match:

- **Namespaces:** top-level only. `namespace Player { }`, never `namespace Player.Locomotion { }`.
- **Serialized fields:** no leading underscore — `[SerializeField] private float moveSpeed;`
- **Private non-serialized fields:** leading underscore — `private bool _snapTurnQueued;`
  (static readonly caches too, e.g. `_gripCurlParam`).
- **Public properties:** no leading underscore; prefer auto-properties with a private setter
  (`public Vector2 MoveAxis { get; private set; }`) over a passthrough expression body where
  the value should be cached/set elsewhere rather than recomputed on every read. Cheap derived
  values (e.g. `IsLeftGrabbing => LeftGrip > 0.5f`) are fine as expression bodies.
- **Comments:** XML doc comments (`/// <summary>`) on methods; detailed inline comments are
  encouraged where they aid learning (this project doubles as a teaching exercise for the
  maintainer — prefer building new systems step-by-step with explanation over handing over a
  finished file all at once). Never insert empty comment lines.
- **Braces:** hybrid style, consistent across the whole codebase. Control-flow braces
  (`if`/`for`/`while`/`switch`) go on the same line, e.g. `if (condition) {`. Class/method/
  property declaration braces go on their own line (Allman) instead, e.g. `public class Foo`
  followed by `{` on the next line. Don't unify these to one style - this is the established
  convention, not a deviation from it.
- **UI draws after everything else:** in-world UI (hand reticles, the mantle arrow, later the
  wrist menu and any markers) renders last and on top, never hidden by world geometry or the
  hands. Build its materials with `OverlayMaterial.Create()` (`Scripts/Core`; the project's
  `TeaLeaf/Overlay` shader - Overlay queue, `ZTest Always`, stereo-safe) rather than a
  depth-tested shader. Built-in shaders can't do this (see `Scripts/Player/CLAUDE.md`).
- **Debug scripts:** always in a system-specific `Debug` subfolder (`Scripts/Player/Debug`,
  `Scripts/Inventory/Debug`, `Scripts/AI/Debug`, ...), never alongside runtime gameplay code.
- **Gizmos must also show in the headset:** a component with gizmos implements `IDebugDrawable`
  and draws through `DebugLines` (never `Gizmos` directly), so the same code draws the Scene
  view gizmos and the in-headset view (`InHeadsetGizmos`). Plain Gizmos only show in one eye
  in VR. See `Scripts/Core/CLAUDE.md`.
- **Performance habits:** no per-frame allocations; cache `Animator.StringToHash`/
  `Shader.PropertyToID` results; use `MaterialPropertyBlock` rather than `.material` writes;
  only touch renderers/materials on state transitions, not every frame; prefer self-registering
  static lists/registries over scene searches or interface `GetComponent` calls.

### Performance rules — apply to everything built from 2026-10-07

The maintainer's rule: new systems, shaders, content and editor builders are designed against
these from the start, not fixed up in a late performance pass. Say so when a request would
break one, and offer the cheaper route. **Rewritten 2026-10-10 for the PCVR target** (above):
they were first written for Quest 3 standalone, and the ones that only existed for mobile
hardware are loosened, while the ones that are cheap to follow now and costly to retrofit
are kept, which also keeps a Quest port possible. The matching tasks are in
`Assets/DEVROADMAP.txt` (the "Performance rules" block, and Phases 6, 8 and 10;
reworded for this target the same day, with the Quest-only tasks kept at the end of Phase 10
as a possible later port).

- **The budget is 8.3 ms a frame, CPU and GPU each, on the target rig** (RTX 3060,
  5600X class, 16 GB, 2000 x 2000 per eye). Nothing has been measured on such a machine yet: until it has, a
  cost is a judgement, and should be called one.
- **Pixels are still the biggest cost** (8 million a frame, 90 times a second). The game
  must not *depend* on a full-screen or post-process effect, or on the camera depth or
  opaque texture: an optional one that can be switched off is allowed once measured. No
  parallax occlusion on surfaces that fill the view. The procedural surface shaders
  (cobblestone, brick) are acceptable on this target, subject to measuring. Keep
  transparent overdraw small; UI and markers stay simple unlit shapes. 4x MSAA is
  affordable and wanted (thin edges shimmer in a headset without it).
- **Shaders:** the cheapest lit model that does the job; SRP Batcher compatible
  (properties in `CBUFFER_START(UnityPerMaterial)`); single-pass-instanced stereo macros;
  work per vertex rather than per pixel where it looks the same.
- **Lighting:** baked where it can be, with light probes for things that move. Realtime
  lights are few and short-ranged. **Torches may cast realtime shadows from moving things**
  (guards, props, hands) with the level's shadows baked - Mixed lights in Shadowmask mode,
  Indirect Multiplier 0 so a torch put out leaves no baked glow (the plan agreed
  2026-10-10, untested) - but as a quality setting that can be turned off, and never the
  whole level drawn into a torch's shadow map. At most four mixed lights overlap at any one
  spot (the shadowmask's limit; the moon counts as one if it is Mixed).
- **Static level geometry** is marked Static, on `Environment`, with a small number of shared
  materials, so it batches, bakes and occlusion-culls. Editor builders that make level or
  test geometry should set this. Runtime-made renderers turn shadow casting and receiving off
  unless they need them.
- **Depth is modelled, detail is textured:** shapes the player gets close to are real
  geometry (cheap, and correct in stereo); normal maps are for fine surface detail only.
- **Layout culls:** break long sightlines with level shape and doors, backed by an occlusion
  bake. Distance haze, nearer cull distances for small props, impostors and application
  spacewarp are reserves, tried only when profiling asks.
- **CPU work is sliced:** anything costly (AI senses, pathfinding, light sampling, sound
  paths) runs a few times a second on a staggered timer, never per frame, and less often
  when far away or unseen. Physics queries are non-allocating, with their own `LayerMask`.
- **Nothing is created or destroyed during play:** pool bolts, effects and audio voices;
  build runtime meshes and materials once, at load.

## Working with this codebase

This is a Unity project, not a CLI/npm/dotnet-cli project — there is no command-line build,
lint, or test workflow set up. Builds, Play Mode testing, and the Test Runner
(com.unity.test-framework is installed but no test assemblies exist yet) are all driven through
the Unity Editor (open the project with the exact editor version in
`ProjectSettings/ProjectVersion.txt`). `.sln`/`.csproj` files at the repo root are
Unity-generated for IDE tooling (Rider/Visual Studio) and should not be hand-edited.

Version control: git, remote `origin` on GitHub, branch `master`. **Commit once per session**
(decided 2026-09-28): a single commit at the end, with the session's devlog/roadmap wrap-up,
rather than one per change - unless the maintainer asks for a commit sooner. A Unity `.gitignore` excludes
generated folders, `.csproj`/`.sln`, and `.idea/`. `.meta` files must always be committed with
their assets. `*.blend` source files (`Blends/`) are ignored because they exceed GitHub's
100 MB limit — they need Git LFS before they can be tracked.

All gameplay scripts currently compile into the default `Assembly-CSharp` assembly — no
`.asmdef` files exist yet, so there is only one compilation unit for `Assets/Scripts`.

## Architecture

### Frame ordering: explicit `Tick()` pattern

`PlayerController` (on the Player root) is the only player script with an `Update()`. It calls
every other player system's `Tick()`-style methods explicitly, so frame order is deterministic
rather than depending on Unity's unspecified order between components. It holds sequencing
and wiring only, with no gameplay logic: decisions belong in the system that owns the
behaviour. The current order is:

1. `playerInput.Tick()` — cache this frame's input (must be first); then
   `playerTracking.Tick()` — smooth the hands' tracked poses (below), then, if the headset
   was recentred since last frame, put the view
   upright and at standing height again, before anything reads the head or hands.
   **Once the level has ended** (`LevelManager.StateChanged` → not `Playing`; caught or won,
   during the end fade) **the frame stops here**: the body is frozen - only
   `playerHandInteraction.Tick()`, `playerHandDoors.TickHeld()`, `playerHandVisuals.Tick()`, `playerHandHolding.TickHeld()`,
   `playerHandThrowing.Tick(false)`,
   `TickReticles()` and `playerHandAnimation.Tick()` run, so the hands (and anything they
   carry) still follow the controllers.
2. `playerLocomotion.TickBody()` — re-centre the capsule under the headset and apply crouch
   height (before the hand systems, since crouch moves the tracked hierarchy).
3. `playerHandInteraction.Tick()` — hand rays + targets (reticles are placed at 8b).
   3b. `playerLockpicking.Tick()` — take the lockpicks, put them in a lock, take hold of a
   pick on the big lock (skipped while mantling, and if the rig has no `PlayerLockpicking`).
   Before the other grab systems: picks are taken by reaching, not by the hand rays, so a
   hand that takes one must already be busy when climbing, carrying and doors run. Then
   `playerKeys.Tick()` — take the keyring from the pack, put it in a lock, take hold of
   the key (skipped likewise, and if the rig has no `PlayerKeys`).
4. `playerClimbing.Tick()` — grab/release, zip line slide, climb movement (skipped while
   mantling); then
   4a. `playerHandHolding.Tick()` — pick up / drop props (also skipped while mantling; after
   climbing, since a hand either grips a climbable or carries a prop); then
   `playerPack.Tick()` — the pack button, loot let go of at the pack this frame going into
   it, and items being taken out (skipped if the rig has no `PlayerPack`); then
   `playerHandDoors.Tick()` — take / let go of door handles (also skipped while mantling;
   last of the three, a hand does one of them; skipped if the rig has no `PlayerHandDoors`);
   then `playerMantling.Tick()` — detects a possible mantle (arrow), starts one on a stick push, or
   advances the one in progress. **While `IsMantling`, the frame stops here**: the mantle has
   already positioned the rig directly (CharacterController disabled), so only `TickState()`,
   `playerVisibility.Tick()`, `playerKeyholes.Tick()`, `playerHandDoors.TickHeld()`, `playerHandVisuals.Tick()`, `playerHandHolding.TickHeld()`,
   `playerHandThrowing.Tick(false)`,
   `TickReticles()` and `playerHandAnimation.Tick()` run - no locomotion, turning or `Move()`.
5. `_frameMovement` = `playerLocomotion.TickMovement(isClimbing)` (thumbstick + gravity;
   zero while climbing) + `playerClimbing.FrameMovement` while climbing; then
   `playerLocomotion.TickTurning()`.
6. One `characterController.Move(_frameMovement)` (during which Unity calls
   `PlayerBodyPushing.OnControllerColliderHit()`, so the body pushes open doors here), then
   `playerClimbing.ReportAppliedMovement()` while climbing.
7. `playerLocomotion.TickState(isClimbing, appliedMovement, collisionFlags)` — after `Move()`,
   because it needs the real applied movement, the `CollisionFlags` `Move()` returned (to stop
   momentum pushing into walls/ceilings), and the `isGrounded` that `Move()` just updated.
   7b. `playerFootsteps.Tick(appliedMovement)` — after `TickState()`, since a step depends on
   this frame's `MovementState` and real movement (skipped if the Player has no
   `PlayerFootsteps`; not run during a mantle or once the level has ended).
   7c. `playerVisibility.Tick()` — after `TickState()` too: it samples the light where the
   body ended up and applies this frame's `MovementState` (skipped if the Player has no
   `PlayerVisibility`; also run during a mantle, not once the level has ended).
   7d. `playerKeyholes.Tick()` — after `Move()`: the keyhole the head is near opens by
   where the head ended up (skipped if the Player has no `PlayerKeyholes`; also run during
   a mantle, not once the level has ended).
8. `playerHandDoors.TickHeld()` — a hand on a door handle turns the lever and swings the
   door, after `Move()` (the door follows where the hand ended up) and straight before the
   visuals (the hand is then snapped onto the handle where the door now is). Then
   `playerLockpicking.TickHeld()` — a hand on a pick turns it, the big lock runs (pins,
   fade), and picking is given up if the player has moved away (wherever
   `playerHandDoors.TickHeld()` runs, on every path). Then `playerKeys.TickHeld()` — a
   hand on a key in a lock turns it with the wrist; the keyring goes back to the pack if
   the player has moved away (every path too). Then
   `playerHandVisuals.Tick()` — after turning/`Move()`: hand visuals are children of the rig,
   so a world-space snap pose placed earlier would be dragged off by them, and the physical
   hand sweep needs the controller's final position. (`TickHandVisuals()` runs the pair.)
   8a. `playerHandHolding.TickHeld()` — once a hand visual has reached the prop it's picking
   up, makes the prop a child of it; so straight after the visuals are placed (skipped if the
   rig has no `PlayerHandHolding`). A carried prop is a child of the hand visual, so nothing
   places it per frame. Then `playerHandThrowing.Tick(canAim)` — aimed throwing: the arc
   starts from where the prop now is (skipped if the rig has no `PlayerHandThrowing`;
   `canAim` is false during a mantle and once the level has ended, when nothing new is aimed
   but a launch already under way still finishes).
   8b. `playerHandInteraction.TickReticles()` — after grabs and hand visuals, so a hand that
   grabbed this frame already hides its reticle.
9. `playerHandAnimation.Tick()` — last, so it reads this frame's snap weight from step 8
   (Animators evaluate after all `Update()` calls anyway).

New per-frame player systems get a `Tick()` (or a `Tick…()` that returns a movement
contribution) and a slot in `PlayerController.Update()`. `PlayerController.Reset()`
auto-fills its references when the component is added (root components via `GetComponent`,
hand components via `GetComponentInChildren`).

**One `Move()` per frame (`_frameMovement` accumulator)** — `PlayerController.Update()` zeroes
`_frameMovement`, adds each system's movement contribution, then calls
`characterController.Move(_frameMovement)` exactly once. Any new movement-contributing system
(ladders, knockback, ...) should return or expose its contribution for `PlayerController` to
add, rather than calling `Move()` directly. The one exception is a mantle, which positions the
rig directly. Everything runs in **`Update()`, not `FixedUpdate()`** — CharacterController
movement and VR tracking both want per-frame updates, for lower latency.

### Player hierarchy (`Main.unity`, originally unpacked from XRI's XR Origin prefab)

```
Player                 [Player layer] CharacterController, PlayerTracking, PlayerInputXR,
                       PlayerHaptics, PlayerLocomotion, PlayerClimbing, PlayerMantling,
                       PlayerFootsteps, PlayerVisibility, PlayerKeyholes, PlayerBodyPushing,
                       PlayerInventory (Inventory namespace), PlayerController
  Camera Offset        (saved at y 1.6m = standing eye height; crouch shifts it)
    Main Camera        Tracked Pose Driver (Tracking/Head* actions)
      Mantle Indicator (MantleIndicator - head-locked, placed a little below centre ~0.5m ahead)
      Screen Fade      (Core.ScreenFade - at the camera's local origin; fade to/from black)
    Hands              PlayerHandInteraction, PlayerHandHolding, PlayerHandThrowing,
                       PlayerPack, PlayerKeys, PlayerHandDoors, PlayerLockpicking, PlayerHandVisuals,
                       PlayerHandState (added at runtime if the scene has none),
                       PlayerHandAnimation (identity transform)
      Left/Right Throw Arc   (made at runtime by PlayerHandThrowing)
      Left Hand        [PlayerHands] tracked controller - Tracked Pose Driver (Tracking/LeftHand*)
        Left Hand Visual   (hand.fbx instance, Animator)
          Lockpicks        (made at runtime by PlayerLockpicking; moves to the right hand
                           visual or into a lock while in use)
          Pack             (Inventory.Pack - a scene-root object moved here at runtime by
                           PlayerPack; active only while summoned)
        Left Hand Reticle
      Right Hand       [PlayerHands] same, visual mirrored (scale.x -1)
```

**A hand does one thing at a time** (climbing, carrying, a door, the lockpicks, the keys):
before a hand system takes a hand it asks `PlayerHandState.IsBusyExcept()` whether another
has it, rather than checking the other systems itself (2026-10-09). A new hand system is
added there, once - see `Assets/Scripts/Player/CLAUDE.md`.

Body/movement systems sit on the root, hand systems on `Hands`. `Hands` must stay under
`Camera Offset` with an identity transform: the tracked hands' poses are relative to it, and
crouch lowers the tracked hierarchy by moving `Camera Offset`.

**The head moves again before rendering; the hands don't** (2026-10-04). The Main Camera's
Tracked Pose Driver uses "Update And Before Render"; the two hands' use **"Update"** only.
The hand models are skinned meshes, which were drawn at their `Update()` pose anyway, while
a plain mesh under the hand (a carried prop) got the later before-render pose - so a carried
prop lagged the hand until the hands were switched to "Update" (found in the headset; the
skinning explanation is the working theory, the fix is confirmed). Don't switch them back.
World-fixed things (a snapped hand, a hand held off a wall) are still detached from the
controller: it was needed when the hands moved before rendering, and is harmless now.

**The controller objects hold a smoothed pose, not the raw tracking** (2026-10-07, tested).
`PlayerTracking.Tick()` overwrites the pose the hands' Tracked Pose Drivers wrote with a
smoothed one (a One Euro filter, tuned per device: PCVR and Quest standalone), so everything
under or reading `Left Hand`/`Right Hand` is steadied. It relies on the hands' drivers being
"Update" only. Climbing uses the smoothed pose as well; nothing reads the raw tracking.
Detail in `Assets/Scripts/Player/CLAUDE.md`.

**Tracking setup** (replaced XROrigin, InputActionManager and XRI's actions, 2026-09-30):
`PlayerTracking.Start()` puts XR tracking in **Device** mode (head measured from where the
headset started or was last recentred, not the real floor), and `Camera Offset`'s saved 1.6m
lifts that to standing eye height - the same for every player, while real crouching still
lowers the head. It then calibrates the view itself (`calibrateView`), after waiting for a
tracked head pose, while `ScreenFade` holds the level-start black: if the head is upside down
it turns `Camera Offset` half a turn, and it shifts `Camera Offset` so the head is at standing
eye height; `PlayerTracking.Tick()` repeats this whenever the headset is recentred
(2026-10-03: through Virtual Desktop/SteamVR the tracking space often started upside down
with its origin on the floor - so `Camera Offset` isn't always at its saved pose). The three Tracked Pose Drivers (Input System's, not XRI's) read the project's
own `Tracking` action map, auto-enabled with the rest of `InputSystem_Actions`. Controllers
bind the **pointer** (aim) pose, not the grip pose, as XRI's actions did - hand visual
placement, ray angles and snap offsets are all tuned against it. An untracked controller stays
active and holds its last pose (nothing deactivates it since XRI's Input Modality Manager went);
the few guards for an inactive controller (`HandRayReticle.Tick()`, the physical follow) are
kept in case untracked hands are hidden later.

### Physics layers

User layers 6-13: `Environment` (static world geometry), `Player` (the CharacterController
object, the Player root), `PlayerHands` (Left/Right Hand objects), `Interactable` (grabbables,
doors, loot, locks), `Climbable` (`ClimbableEdge`s, ladders, ropes), `Guard` (body capsule),
`GuardHead` (blackjack target), `Projectile` (bolts). Unassigned objects stay on `Default`,
which still collides with everything. Collision matrix pairs that are ON among these:
Environment with Environment/Player/PlayerHands/Interactable/Guard/Projectile;
Player-Interactable; Player-Guard; PlayerHands-Interactable; Interactable with
Interactable/Guard/Projectile; Guard-Projectile. Everything else is off (notably
Player-PlayerHands, and Climbable with everything - it's a grab volume, not geometry). **A
prop being carried is moved onto `PlayerHands`** (and back when dropped; it's also a child of
the hand visual while carried), so the body doesn't
collide with it and hand rays, the hand sweep and light rays ignore it. **A door** is on
`Interactable` (its leaf and its handle's trigger grab volume), on a kinematic Rigidbody
turned from code; it checks the `Player` layer itself so it never swings into the body, and
the body walking into an open door pushes it (`PlayerBodyPushing`, 2026-10-09). The
matrix doesn't affect raycasts/overlaps: queries must pass their own `LayerMask` (hand rays use
Environment + Interactable + Climbable, so walls block them; physical hand sweeps use
Environment + Interactable; light rays - `SceneLight` - use Environment + Interactable, so both block light).

### Vertical slice design (see `Assets/DEVROADMAP.txt` for the full plan)

Target: one greybox docks/warehouse level — steal an objective item and escape; being caught
fails and restarts. Agreed mechanics:

- **Visibility:** worked out from the level's geometry, nothing placed by hand but the lights
  (decided 2026-10-04, replacing designer-placed shadow volumes). Levels are at night: the
  base light is **moonlight**, and a point is in full shadow when geometry stands between it
  and the moon - one physics ray towards the moon, so gameplay shadows fall where the rendered
  ones do. **Light sources** (torches, lamps) add light within their range, blocked by
  geometry the same way; extinguishing one just disables it. The brightest light wins.
  Crouch/sprint modify it: standing in moonlight can be seen, crouching in it only from
  close up. The rendered shadow map is not read back (too slow); rays stand in for it.
- **Noise:** footsteps by surface type (scaled by stance), thrown/dropped physics objects,
  doors/interactions — all via a shared noise event system that guards listen to.
- **Sound (decided 2026-10-03):** a sound must tell the player truthfully where it is on
  stereo headphones. Thief-style rooms and portals: designer-placed sound rooms joined by
  portals (doorways, stairwells); sound travels room to room only through portals, never
  through walls, and is heard from the last portal at the distance of the whole path - so a
  guard on the floor above is heard from the stairwell or not at all. Closed doors muffle
  (high end rolled off). Guards' hearing follows the same paths. No ray-cast muffling inside
  a room. **Reverb belongs to the room the listener is in, not the sound's** (the
  maintainer's rule, 2026-10-10): one shared reverb, tuned per `SoundRoom`. Looping sounds
  (`SoundLoop`) and a per-room ambience follow the same rooms. Direction by ear comes from
  the project's own simple spatialiser (`SpatialVoice`: time difference between the ears,
  head shadow, duller from behind; no up/down cue), hand-built like the locomotion - the
  Meta XR Audio SDK was tried and removed 2026-10-10 (not platform-agnostic), and Steam
  Audio is the fallback if up/down turns out to matter. Detail in `Assets/Scripts/Core/CLAUDE.md`.
- **Guard AI:** patrol → suspicion → search → chase → catch (no combat). Knockout state;
  guards react to finding bodies. Placeholder humanoid + Mixamo animations, NavMesh.
  **Voice lines don't repeat** (decided 2026-10-07, not built): guards draw barks and idle
  patrol mutterings from very large pools, and a lightweight shared record of the lines used
  this play session (kept across level restarts) stops one repeating until its whole pool
  has been heard. Several guard voices, each with its own pools. Other guards don't react to
  idle chatter (sound only, no noise event); two-guard conversations may come later.
- **Tools:** blackjack (from-behind takedown on unaware guards), hand crossbow usable in either
  hand, physically cocked, with water / noisemaker / rope bolts; two-handed
  lockpicking with haptics (decided 2026-10-09, replacing the wrist-roll plan; written the
  same day in `PlayerLockpicking`, `Interaction.BigLock` and `PickableLock`, tried and
  working):
  two picks worn on the back of the left hand snap into a simple lock's keyhole, a large
  copy of the lock fades in front of the door, the right hand turns one pick clockwise
  through three stops and a last turn while the left sweeps the other to find each stop's
  random pin by haptics; a hand turns a pick by moving round the lock, not by twisting the
  wrist. Full spec in `Assets/DEVROADMAP.txt`, Phase 5; detail in
  `Assets/Scripts/Interaction/CLAUDE.md` and `Assets/Scripts/Player/CLAUDE.md`.
- **Inventory (the maintainer's design, 2026-10-09, replacing "loot pocketed at the hip
  for a running total"):** a backpack that only holds so much, so the player keeps the most
  valuable loot and leaves the rest. Summoned with X on the left controller, it rides on
  the left hand and loot is put into it by hand: a grid of spaces (3 x 3 for now), one
  per piece of loot, chosen by holding it over a free space; a gold space for small loot, and separate spaces for the objective
  and the keyring. Carried loot shows one to three coins for its worth. Keys are coloured
  and live on the keyring, which is taken from the pack and held to a lock. The pack, loot
  and coins were written and tested 2026-10-09; keys, the keyring and turning a key in a
  keyed door's lock were written and tested the same day. How tools and bolt types
  are chosen (a wrist radial menu was the earlier plan) is still to be settled against the
  pack. Detail in `Assets/Scripts/Inventory/CLAUDE.md`.
- **Throwing is both aimed and physical** (physical built and tested 2026-10-07; aimed
  built the same day in `PlayerHandThrowing`, tested - its launch is a short forward move
  of the hand visual, not an animation clip; the arc can be drawn as a line or as dots).
  Grip picks an object up. Aimed, for accuracy: holding that hand's trigger shows a
  trajectory arc (distance from hand pitch, teleport-arc style), and releasing the trigger
  plays a short hand launch animation and throws along the arc; aiming at nothing (arc
  red/faded) and releasing cancels. Physical, for a quick natural throw: moving the hand and
  letting go of grip sends the object off with the hand's movement; a still hand just drops
  it. Detail in `Assets/Scripts/Player/CLAUDE.md`.
- **Traversal:** sprint, jump, mantling, ladders, rope climbing, drag/hide KO'd bodies.
- **Physical hands:** the visual hand collides with the world, stops at surfaces, and
  elastic-bands back to the controller once clear (Alyx / Thief VR style). A core concept, part
  of Phase 1; kinematic sweep, no Rigidbody (detail in `Assets/Scripts/Player/CLAUDE.md`).
- **Sky (decided 2026-10-07):** no skybox - the camera clears to black and
  `Core.ProceduralSky` draws the stars (camera-locked quads built at level load, none below
  the horizon, twinkling in the shader) and the moon, a disc where the Directional Light
  shines from, its cratered face painted into a small texture at level load. A picture only: it lights nothing. Nebula clouds may be added later.
  Detail in `Assets/Scripts/Core/CLAUDE.md`.
- **Art:** greybox only (ProBuilder/primitives); out of scope: settings/main menu, save/load,
  final art, combat, fall damage (fall damage/health come in a later damage phase, after the
  slice).

### Planned systems

`Assets/Scripts/{AI,UI}` are still empty placeholder folders, `Inventory` holds only the
inventory data and loot so far (started 2026-10-09), and `Core` only holds
the game state/level manager, the screen fade, the noise and sound system, surfaces, gameplay light, the
procedural night sky and the shared debug drawing so far — future systems land there following the same one-class,
one-responsibility pattern (AI: guards; Inventory: items/loot; UI: wrist radial/display). Ending the level
always goes through `LevelManager` (`Caught()`, `SetObjectiveCarried()`): no other system
fades out or reloads the scene itself.

### Scenes

`Assets/Scenes/Main.unity` is the sole scene currently in the project, and the only (first)
scene in the build list. Its test areas are built from the **TeaLeaf** menu (editor scripts
in each system's `Debug/Editor`) and can be rebuilt at any time. As of 2026-10-07 the scene
held only the Sound Test House and the Grabbable Test Props (a Door Test Area can be added
from **TeaLeaf > Build Door Test Area**, written 2026-10-08 - since 2026-10-09 it also sets
up lockpicking, including a `Big Lock` object at the scene root - and a textured, climbable
blacksmith's house from **TeaLeaf > Build Smithy**, written 2026-10-09 - see
`Assets/Scripts/Interaction/CLAUDE.md`): the maintainer removed the
Locomotion Test Course, Town Test Area, Sound Test Area and Noise Test Listeners that day
(the Physical Hands Test Area was already gone), so don't assume those are there.
