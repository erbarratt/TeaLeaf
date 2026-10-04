# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

TeaLeaf is a Thief-style VR stealth game (Unity 6000.3.23f1, Universal Render Pipeline) built
on OpenXR and the new Input System, with no XR Interaction Toolkit: the project started from
XRI's XR Origin sample rig, and every XRI part has since been replaced by the project's own
code or removed (the package itself on 2026-09-30). Keep it lean - add a package or third-party
component only when it's clearly needed. Target order: PCVR
first (dev/test via Quest 3 streamed through Virtual Desktop; originally built against an HP
Reverb G2 v2 with Oasis drivers through SteamVR), then Quest 3 standalone. Performance is a
major, ongoing priority because the real target is Quest 3 standalone hardware.

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
  climbables (`IClimbable`, `ClimbableEdge`, `Ladder`, `ClimbableRope`) and hand snap poses.
- **`Assets/Scripts/Core/CLAUDE.md`** — game state and level restart (`GameState`,
  `LevelManager`, `ExitZone`), the screen fade (`ScreenFade`), noise events and sound
  propagation (`NoiseSystem`, `SoundCue`, `SoundRoom`, `SoundPortal`, `SoundPropagation`, `SoundPlayer`;
  loops, reverb and a spatialiser planned), surfaces (`SurfaceType`, `SurfaceTag`,
  `SurfaceSounds`), gameplay light (`SceneLight`, `Moonlight`, `LightSource`), shared debug
  drawing (`DebugLines`, `IDebugDrawable`, `InHeadsetGizmos` - gizmos that also show in the
  headset).

New system folders (`AI`, `Inventory`, `UI`) get their own `CLAUDE.md` when their
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
- **Performance habits (Quest 3):** no per-frame allocations; cache `Animator.StringToHash`/
  `Shader.PropertyToID` results; use `MaterialPropertyBlock` rather than `.material` writes;
  only touch renderers/materials on state transitions, not every frame; prefer self-registering
  static lists/registries over scene searches or interface `GetComponent` calls.

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
   `playerTracking.Tick()` — if the headset was recentred since last frame, put the view
   upright and at standing height again, before anything reads the head or hands.
   **Once the level has ended** (`LevelManager.StateChanged` → not `Playing`; caught or won,
   during the end fade) **the frame stops here**: the body is frozen - only
   `playerHandInteraction.Tick()`, `playerHandVisuals.Tick()`, `playerHandHolding.TickHeld()`,
   `TickReticles()` and `playerHandAnimation.Tick()` run, so the hands (and anything they
   carry) still follow the controllers.
2. `playerLocomotion.TickBody()` — re-centre the capsule under the headset and apply crouch
   height (before the hand systems, since crouch moves the tracked hierarchy).
3. `playerHandInteraction.Tick()` — hand rays + targets (reticles are placed at 8b).
4. `playerClimbing.Tick()` — grab/release, zip line slide, climb movement (skipped while
   mantling); then
   4a. `playerHandHolding.Tick()` — pick up / drop props (also skipped while mantling; after
   climbing, since a hand either grips a climbable or carries a prop); then
   `playerMantling.Tick()` — detects a possible mantle (arrow), starts one on a stick push, or
   advances the one in progress. **While `IsMantling`, the frame stops here**: the mantle has
   already positioned the rig directly (CharacterController disabled), so only `TickState()`,
   `playerVisibility.Tick()`, `playerHandVisuals.Tick()`, `playerHandHolding.TickHeld()`,
   `TickReticles()` and `playerHandAnimation.Tick()` run - no locomotion, turning or `Move()`.
5. `_frameMovement` = `playerLocomotion.TickMovement(isClimbing)` (thumbstick + gravity;
   zero while climbing) + `playerClimbing.FrameMovement` while climbing; then
   `playerLocomotion.TickTurning()`.
6. One `characterController.Move(_frameMovement)`, then
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
8. `playerHandVisuals.Tick()` — after turning/`Move()`: hand visuals are children of the rig,
   so a world-space snap pose placed earlier would be dragged off by them, and the physical
   hand sweep needs the controller's final position.
   8a. `playerHandHolding.TickHeld()` — places carried props at the hand visuals, so straight
   after them (skipped if the rig has no `PlayerHandHolding`; props are placed once more just
   before rendering, by the component itself).
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
                       PlayerFootsteps, PlayerVisibility, PlayerController
  Camera Offset        (saved at y 1.6m = standing eye height; crouch shifts it)
    Main Camera        Tracked Pose Driver (Tracking/Head* actions)
      Mantle Indicator (MantleIndicator - head-locked, placed a little below centre ~0.5m ahead)
      Screen Fade      (Core.ScreenFade - at the camera's local origin; fade to/from black)
    Hands              PlayerHandInteraction, PlayerHandHolding, PlayerHandVisuals,
                       PlayerHandAnimation (identity transform)
      Left Hand        [PlayerHands] tracked controller - Tracked Pose Driver (Tracking/LeftHand*)
        Left Hand Visual   (hand.fbx instance, Animator)
        Left Hand Reticle
      Right Hand       [PlayerHands] same, visual mirrored (scale.x -1)
```

Body/movement systems sit on the root, hand systems on `Hands`. `Hands` must stay under
`Camera Offset` with an identity transform: the tracked hands' poses are relative to it, and
crouch lowers the tracked hierarchy by moving `Camera Offset`.

**Tracked transforms move again before rendering.** The hands' Tracked Pose Drivers use
"Update And Before Render", so the controllers move once more after all `Update()` code.
Anything that must stay world-fixed (a snapped hand, a hand held off a wall) can't be a child
of a tracked transform - it's detached while that applies.

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
prop being carried is moved onto `PlayerHands`** (and back when dropped), so the body doesn't
collide with it and hand rays, the hand sweep and light rays ignore it. The
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
  a room; per-room reverb is planned. Detail in `Assets/Scripts/Core/CLAUDE.md`.
- **Guard AI:** patrol → suspicion → search → chase → catch (no combat). Knockout state;
  guards react to finding bodies. Placeholder humanoid + Mixamo animations, NavMesh.
- **Tools:** blackjack (from-behind takedown on unaware guards), hand crossbow usable in either
  hand, physically cocked, with water / noisemaker / rope bolts; rotate-wrist sweet-spot
  lockpicking with haptics.
- **Inventory:** wrist radial menu to pick tools/bolt types (equip into the other hand); loot
  pocketed at the hip for a running total.
- **Throwing is aimed, not physical:** grip picks an object up; holding that hand's trigger
  shows a trajectory arc (distance from hand pitch, teleport-arc style), and releasing the
  trigger plays a short hand launch animation and throws along the arc; aiming at nothing
  (arc red/faded) and releasing cancels. It's for accuracy, and
  to avoid how awkward physical throwing feels in VR. Detail in `Assets/Scripts/Player/CLAUDE.md`.
- **Traversal:** sprint, jump, mantling, ladders, rope climbing, drag/hide KO'd bodies.
- **Physical hands:** the visual hand collides with the world, stops at surfaces, and
  elastic-bands back to the controller once clear (Alyx / Thief VR style). A core concept, part
  of Phase 1; kinematic sweep, no Rigidbody (detail in `Assets/Scripts/Player/CLAUDE.md`).
- **Art:** greybox only (ProBuilder/primitives); out of scope: settings/main menu, save/load,
  final art, combat, fall damage (fall damage/health come in a later damage phase, after the
  slice).

### Planned systems

`Assets/Scripts/{AI,Inventory,UI}` are still empty placeholder folders, and `Core` only holds
the game state/level manager, the screen fade, the noise and sound system, surfaces, gameplay light and the
shared debug drawing so far — future systems land there following the same one-class,
one-responsibility pattern (AI: guards; Inventory: items/loot; UI: wrist radial/display). Ending the level
always goes through `LevelManager` (`Caught()`, `SetObjectiveCarried()`): no other system
fades out or reloads the scene itself.

### Scenes

`Assets/Scenes/Main.unity` is the sole scene currently in the project, and the only (first)
scene in the build list.
