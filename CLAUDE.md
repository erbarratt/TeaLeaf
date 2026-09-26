# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

TeaLeaf is a Thief-style VR stealth game (Unity 6000.3.23f1, Universal Render Pipeline) built
around OpenXR + XR Interaction Toolkit (3.3.2) and the new Input System. Target order: PCVR
first (dev/test via Quest 3 streamed through Virtual Desktop; originally built against an HP
Reverb G2 v2 with Oasis drivers through SteamVR), then Quest 3 standalone. Performance is a
major, ongoing priority because the real target is Quest 3 standalone hardware.

Unity's built-in locomotion system is deliberately disabled (the `PlayerRig > Locomotion`
object and all its providers — Move, Turn, Snap Turn, Continuous Turn, Teleportation, Climb,
Grab Move, Jump). Movement, turning, gravity, crouching, climbing, and future systems like
ladders are all hand-built instead, for full control, easier Quest 3 optimization, easier
debugging, consistent behavior across headsets, and a better understanding of how everything
works. **Do not suggest re-enabling or using XRI's built-in locomotion, teleportation, or climb
providers.** Teleport locomotion will never be used.

Known headset quirk: on the HP Reverb G2 + Oasis, `XRI Right Locomotion/Turn` doesn't produce
values — `XRI Right Locomotion/Snap Turn` must be used instead. Quest 3 input and tracking have
been verified working.

## Project docs — keep these up to date

Three docs track the project. Update them as part of any change, without being asked:

- **`CLAUDE.md`** (this file) — update whenever architecture, conventions, systems, file
  locations, or design decisions change, so it never describes the project inaccurately.
- **`Assets/DEVLOG.txt`** — dated entries, newest at the top, describing what was done, bugs hit
  and how they were fixed, and what was learned. Add to today's entry (or create one) whenever
  work is completed. Keep its plain-text style.
- **`Assets/DEVROADMAP.txt`** — the phased plan from the current state to a greybox vertical
  slice of one level (docks warehouse). Tick items `[x]` as they're completed, and edit it when
  scope, order, or design decisions change (also note roadmap changes in the devlog).

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
- **Debug scripts:** always in a system-specific `Debug` subfolder (`Scripts/Player/Debug`,
  `Scripts/Inventory/Debug`, `Scripts/AI/Debug`, ...), never alongside runtime gameplay code.
- **Performance habits (Quest 3):** no per-frame allocations; cache `Animator.StringToHash`/
  `Shader.PropertyToID` results; use `MaterialPropertyBlock` rather than `.material` writes;
  only touch renderers/materials on state transitions, not every frame; prefer self-registering
  static lists/registries over scene searches or interface `GetComponent` calls.

Note: `PlayerLocomotion.cs` predates the standards and doesn't fully match yet — its class
comment lacks `<summary>`, `HandleMovement()` uses an Allman control-flow brace, and several
`if (...){` lines are missing the space before `{`.

## Working with this codebase

This is a Unity project, not a CLI/npm/dotnet-cli project — there is no command-line build,
lint, or test workflow set up. Builds, Play Mode testing, and the Test Runner (com.unity.test-framework
is installed but no test assemblies exist yet) are all driven through the Unity Editor
(open the project with the exact editor version in `ProjectSettings/ProjectVersion.txt`).
`.sln`/`.csproj` files at the repo root are Unity-generated for IDE tooling (Rider/Visual Studio)
and should not be hand-edited.

Version control: git, remote `origin` on GitHub, branch `master`. A Unity `.gitignore` excludes
generated folders, `.csproj`/`.sln`, and `.idea/`. `.meta` files must always be committed with
their assets. `*.blend` source files (`Blends/`) are ignored because they exceed GitHub's
100 MB limit — they need Git LFS before they can be tracked.

All gameplay scripts currently compile into the default `Assembly-CSharp` assembly — no
`.asmdef` files exist yet, so there is only one compilation unit for `Assets/Scripts`.

## Architecture

### Frame ordering: explicit `Tick()` pattern

Player subsystems don't run their own `Update()`. `PlayerLocomotion.Update()` calls each one's
`Tick()` explicitly, so frame order is deterministic rather than depending on Unity's
unspecified order between components. The current order is:

1. `playerInput.Tick()` — cache this frame's input (must be first).
2. `UpdateCharacterControllerCentre()`, `HandleCrouch()`.
3. `playerHandInteraction.Tick()` — hand rays + highlighting.
4. `playerClimbing.Tick()` — grab/release, climb movement.
5. `playerHandAnimation.Tick()` — after climbing, so it sees this frame's grab state.
6. Either climb movement or `HandleMovement()` + `HandleGravity()`, then `HandleTurning()`.
7. One `characterController.Move(_frameMovement)`, then
   `playerClimbing.ReportAppliedMovement()` while climbing.

New per-frame player systems should follow this pattern. The roadmap suggests eventually
moving this sequencing into a dedicated `PlayerController` as more systems are added.

### Player systems (`Assets/Scripts/Player/`, namespace `Player`)

- **`PlayerInputXR`** — the single source of truth for controller input. Wraps Input System
  `InputActionReference`s (grip/trigger per hand, move/turn thumbsticks, crouch) and caches them
  once per frame in `Tick()` as typed properties (`MoveAxis`, `TurnAxis`, `LeftGrip`,
  `IsLeftGrabbing`, `CrouchPressed`, etc.). Also calls `Tick()` from its own `Update()` as a
  fallback so Debug scripts work without a full rig. Gameplay code should always read input
  through this class rather than referencing Input Actions directly.
- **`PlayerTracking`** — the single source of truth for tracked XR transforms (head, left
  hand, right hand), exposing position/rotation accessors. Other systems should query this
  class instead of walking the XR Rig hierarchy.
- **`PlayerLocomotion`** — drives a `CharacterController` for movement, turning, gravity, and
  crouch, and sequences the other subsystems (see above). Movement is relative to the rig root
  (`playerTransform`), not the headset. Supports snap turn and smooth turn (`useSmoothTurn`);
  turning works while climbing. The controller's horizontal center is re-centered under the
  headset every frame.
- **`PlayerClimbing`** — grab-and-pull ledge climbing. Each hand grabs any `ClimbableEdge` its
  `SphereCollider` overlaps; the most recent grab becomes the primary hand, which drives
  movement (hand-off to the other hand on release). Hand deltas are measured in
  `playerTransform` local space to avoid a feedback loop, and any movement the
  CharacterController didn't apply is retried via `ReportAppliedMovement()` so the grab point
  never drifts. Hand visuals (not tracked transforms) are pinned to the grab point. Sets
  `characterController.minMoveDistance = 0` so slow hand movement isn't swallowed. Exposes
  `IsClimbing`, `IsLeftHandGripping`/`IsRightHandGripping`, `FrameMovement`.
- **`PlayerHandInteraction`** — casts one ray per hand (configurable length, layer mask, and
  per-hand angle offset) and highlights whatever `IHighlightable` it hits, handling both hands
  targeting the same object. Exposes `LeftTarget`/`RightTarget` for future interact actions.
- **`HandRayReticle`** — runtime-built billboard disc shown where a hand ray hits a
  highlightable; ticked by `PlayerHandInteraction`.
- **`PlayerHandAnimation`** — per-hand Animator finger curl. A private `HandState` enum
  (`Idle`, `Climbing`) decides who owns each hand's pose: `Idle` sets `TriggerCurl` (index)
  and `GripCurl` (middle/ring/pinky) from input; other states leave the pose to their own
  system. New hand states (holding items, tools) are added to `HandState`/`GetState()`.

`Assets/Scripts/Player/Debug/` holds standalone debug/diagnostic MonoBehaviours (e.g.
`VRDebugInput`, `InputTest`, `TrackingTest`, `TurnInputTest`, `TurnActionTest`,
`LocomotionInputTest`, `HandRayDebug`) used for manually verifying systems in Play Mode — not
part of the runtime gameplay path.

### Interaction systems (`Assets/Scripts/Interaction/`, namespace `Interaction`)

- **`IHighlightable`** — interface for anything a hand ray can highlight (`SetHighlighted`).
- **`HighlightableRegistry`** — static `Collider → IHighlightable` dictionary. Highlightables
  register in `OnEnable`/unregister in `OnDisable`, so hand raycasts do a dictionary lookup
  instead of an interface `GetComponent`.
- **`ClimbableEdge`** — designer-placed `BoxCollider` that is both the grab volume and the
  highlight visual (opacity fade via `MaterialPropertyBlock`). Self-registers in a static
  `Active` list that `PlayerClimbing` iterates (AABB reject, then oriented overlap check).

### Hand art/animation

Real hand models (`Assets/Art/Models/hand.fbx`, Generic rig, bones like
`J_Left_Hand_Index1-4`). Each hand has its own Animator Controller with one masked layer per
finger group, each a 1D blend tree between single-keyframe Open/Closed pose clips
(`Assets/Art/Animations/Hands`). The index layer is done; the middle+ring+pinky layer and its
Avatar Masks are in progress. The thumb isn't animated yet.

### Locomotion design decisions

- **No jumping** — decided against; gravity still applies (falling off ledges works).
- **Crouch is a button-driven toggle, not physical** — `HandleCrouch()` smoothly moves the
  CharacterController height between the standing height (captured in `Awake`) and
  `minimumHeight`, keeps `center.y` in sync, and shifts `cameraOffsetTransform` by the
  *relative* height delta (never an absolute value, which would discard the XR Origin's Camera
  Y Offset). `UpdateCharacterControllerCentre()` only touches X/Z.
- **CharacterController settings** — Slope Limit 45°, Step Offset 0.3m (validated in a test
  scene: 0.2m step and 30° ramp climbable, 60° ramp not). XR Origin uses Device tracking mode
  with a 1.6m Camera Y Offset.
- **`_frameMovement` accumulator pattern** — `PlayerLocomotion.Update()` zeroes a single
  `Vector3 _frameMovement` field, lets each system contribute to it, then calls
  `characterController.Move(_frameMovement)` exactly once per frame. Any new
  movement-contributing system (sprint, ladders, mantling, knockback, ...) should add to
  `_frameMovement` rather than calling `characterController.Move()` directly.
- **`Update()`, not `FixedUpdate()`** — CharacterController-based movement plus VR tracking
  both want per-frame (not physics-step) updates, for lower latency.
- **Climbing is custom** — no XRI climb provider. Ladders and ropes should reuse the
  grab-and-pull-delta approach from `PlayerClimbing`.
- **Settings will eventually move out of serialized fields** — Smooth Turn/Snap Turn, turn
  speed, snap angle, and movement speed are expected to become user-configurable options
  (out of scope for the vertical slice).

### Vertical slice design (see `Assets/DEVROADMAP.txt` for the full plan)

Target: one greybox docks/warehouse level — steal an objective item and escape; being caught
fails and restarts. Agreed mechanics:

- **Visibility:** designer-placed shadow volumes (not real light sampling); torches are linked
  to the volumes they light, so extinguishing one darkens its area. Crouch/sprint modify it.
- **Noise:** footsteps by surface type (scaled by stance), thrown/dropped physics objects,
  doors/interactions — all via a shared noise event system that guards listen to.
- **Guard AI:** patrol → suspicion → search → chase → catch (no combat). Knockout state;
  guards react to finding bodies. Placeholder humanoid + Mixamo animations, NavMesh.
- **Tools:** blackjack (from-behind takedown on unaware guards), hand crossbow usable in either
  hand, physically cocked, with water / noisemaker / rope bolts; rotate-wrist sweet-spot
  lockpicking with haptics.
- **Inventory:** wrist radial menu to pick tools/bolt types (equip into the other hand); loot
  pocketed at the hip for a running total.
- **Traversal:** sprint, mantling, ladders, rope climbing, drag/hide KO'd bodies.
- **Art:** greybox only (ProBuilder/primitives); out of scope: settings/main menu, save/load,
  final art, combat, fall damage.

### Planned systems

`Assets/Scripts/{AI,Core,Inventory,UI}` are still empty placeholder folders — future systems
land there following the same one-class, one-responsibility pattern (Core: game state, noise,
visibility; AI: guards; Inventory: items/loot; UI: wrist radial/display).

### Scenes

`Assets/Scenes/Main.unity` is the sole scene currently in the project.
