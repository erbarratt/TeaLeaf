# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

TeaLeaf is a Thief-style VR stealth game (Unity 6000.3.23f1, Universal Render Pipeline) built
around OpenXR + XR Interaction Toolkit (3.3.2) and the new Input System. Target order: PCVR
first (dev/test via Quest 3 streamed through Virtual Desktop; originally built against an HP
Reverb G2 v2 with Oasis drivers through SteamVR), then Quest 3 standalone. Performance is a
major, ongoing priority because the real target is Quest 3 standalone hardware.

Unity's built-in locomotion system is deliberately not used — the XR Origin prefab was unpacked
and its `Locomotion` object, with all its providers (Move, Turn, Snap Turn, Continuous Turn,
Teleportation, Climb, Grab Move, Jump), deleted. Movement, turning, gravity, crouching, climbing, and future systems like
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

Note: `PlayerLocomotion.cs` predates the standards and doesn't fully match yet — several
`if (...){` lines in `HandleTurning()` are missing the space before `{`, many field comments
use `///` without `<summary>`, and it has an unused `using System;`.

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

`PlayerController` (on the Player root) is the only player script with an `Update()`. It calls
every other player system's `Tick()`-style methods explicitly, so frame order is deterministic
rather than depending on Unity's unspecified order between components. It holds sequencing
and wiring only, with no gameplay logic: decisions belong in the system that owns the
behaviour. The current order is:

1. `playerInput.Tick()` — cache this frame's input (must be first).
2. `playerLocomotion.TickBody()` — re-centre the capsule under the headset and apply crouch
   height (before the hand systems, since crouch moves the tracked hierarchy).
3. `playerHandInteraction.Tick()` — hand rays + highlighting.
4. `playerClimbing.Tick()` — grab/release, climb movement.
5. `playerHandAnimation.Tick()` — after climbing, so it sees this frame's grab state.
6. `_frameMovement` = `playerLocomotion.TickMovement(isClimbing)` (thumbstick + gravity;
   zero while climbing) + `playerClimbing.FrameMovement` while climbing; then
   `playerLocomotion.TickTurning()`.
7. One `characterController.Move(_frameMovement)`, then
   `playerClimbing.ReportAppliedMovement()` while climbing.
8. `playerLocomotion.TickState(isClimbing, appliedMovement)` — after `Move()`, because it needs
   the real applied movement and the `isGrounded` that `Move()` just updated.
9. `playerClimbing.TickHandVisuals()` — last: hand visuals are children of the rig, so a
   world-space snap pose placed before turning/`Move()` would be dragged off by them.

New per-frame player systems get a `Tick()` (or a `Tick…()` that returns a movement
contribution) and a slot in `PlayerController.Update()`. `PlayerController.Reset()`
auto-fills its references when the component is added (root components via `GetComponent`,
hand components via `GetComponentInChildren`).

### Player hierarchy (`Main.unity`, unpacked from XRI's XR Origin prefab)

```
Player                 [Player layer] XROrigin, InputActionManager, XRInputModalityManager,
                       XRGazeAssistance, CharacterController, PlayerTracking, PlayerInputXR,
                       PlayerLocomotion, PlayerClimbing, PlayerController
  Camera Offset        (crouch shifts this; Camera Y Offset 1.6m)
    Main Camera
    Hands              PlayerHandInteraction, PlayerHandAnimation (identity transform)
      Left Hand        [PlayerHands] tracked controller
        Left Hand Visual   (hand.fbx instance, Animator)
        Left Hand Reticle
      Right Hand       [PlayerHands] same, visual mirrored (scale.x -1)
```

Body/movement systems sit on the root, hand systems on `Hands`. `Hands` must stay under
`Camera Offset` with an identity transform: the tracked hands' poses are relative to it, and
crouch lowers the tracked hierarchy by moving `Camera Offset`.

### Player systems (`Assets/Scripts/Player/`, namespace `Player`)

- **`PlayerInputXR`** — the single source of truth for controller input. Wraps Input System
  `InputActionReference`s (grip/trigger per hand, move/turn thumbsticks, crouch, sprint) and
  caches them once per frame in `Tick()` as typed properties (`MoveAxis`, `TurnAxis`, `LeftGrip`,
  `IsLeftGrabbing`, `CrouchPressed`, `SprintPressed`, etc.). Enables every action it reads in
  `OnEnable()`, since they come from two assets (see below). Also calls `Tick()` from its own
  `Update()` as a fallback so Debug scripts work without a full rig. Gameplay code should always
  read input through this class rather than referencing Input Actions directly.

  **Input assets:** grip/trigger/move/turn still reference XRI's sample
  `XRI Default Input Actions` (enabled by the Input Action Manager on Player). New gameplay
  actions go in the project-owned, project-wide `Assets/InputSystem_Actions.inputactions`
  (`Player` map, auto-enabled by Unity) with an `XR`-group binding, not in the XRI sample asset,
  which a package update could overwrite. Current XR bindings there: `Crouch` = right A
  (`{RightHand}/{PrimaryButton}`), `Sprint` = left stick click
  (`{LeftHand}/{Primary2DAxisClick}`). Its `Jump` action is free for the planned jump.
- **`PlayerTracking`** — the single source of truth for tracked XR transforms (head, left
  hand, right hand), exposing position/rotation accessors. Other systems should query this
  class instead of walking the XR Rig hierarchy.
- **`PlayerController`** — the tick orchestrator and sole owner of `characterController.Move()`
  (see above).
- **`PlayerLocomotion`** — thumbstick movement, turning, gravity, and crouch for the
  `CharacterController`, exposed as `TickBody()`/`TickMovement(isClimbing)`/`TickTurning()`.
  It returns its movement rather than calling `Move()` itself. Movement is relative to the rig root
  (`playerTransform`), not the headset. Supports snap turn and smooth turn (`useSmoothTurn`);
  turning works while climbing. The controller's horizontal center is re-centered under the
  headset every frame. Exposes `IsSprinting`, `IsCrouching`, and `MovementState`.
- **`MovementState`** (enum) — `Still`, `Walking`, `Sprinting`, `CrouchStill`, `CrouchWalking`,
  `Climbing`, `Airborne`: the single value noise, visibility, AI and the wrist gem should read,
  rather than combining flags themselves. It is set by `PlayerLocomotion.TickState()` in priority
  order (Climbing > Airborne > crouch > still > sprint/walk). "Moving" means the real horizontal
  applied movement exceeds `movingSpeedThreshold`, so pushing into a wall counts as still.
  `Airborne` only kicks in after `airborneGraceTime` off the ground, since `isGrounded`
  flickers on steps and slopes. Physical roomscale walking doesn't count as moving yet.
- **`PlayerClimbing`** — grab-and-pull ledge climbing. A hand grabs the `ClimbableEdge` its
  hand ray is on (`PlayerHandInteraction.LeftTarget`/`RightTarget`) while grip is held, so
  what's highlighted is what gets grabbed and the ray length is the grab reach; the most recent grab becomes the primary hand, which drives
  movement (hand-off to the other hand on release). Hand deltas are measured in
  `playerTransform` local space to avoid a feedback loop, and any movement the
  CharacterController didn't apply is retried via `ReportAppliedMovement()` so the grab point
  never drifts. On grab it asks the edge for a `HandSnapPose` and hands it to that hand's
  `HandVisualSnap` (exposed as `LeftVisualSnap`/`RightVisualSnap`). Sets
  `characterController.minMoveDistance = 0` so slow hand movement isn't swallowed. Exposes
  `IsClimbing`, `IsLeftHandGripping`/`IsRightHandGripping`, `FrameMovement`.
- **`HandVisualSnap`** — plain C# class (one per hand, owned by `PlayerClimbing` for now;
  should move to a hand-level owner when grabbing/tools need it). Blends a hand *visual*
  (never the tracked controller) between its rest local pose and a world-space `HandSnapPose`
  over `snapBlendDuration` (SmoothStep), both ways, and doesn't touch the transform at rest.
  While snapped the visual is **detached to the scene root** and re-attached when the release
  blend ends: the hands' Tracked Pose Drivers use "Update And Before Render", so they move the
  controller again after all `Update()` code, and a child visual would wobble. Anything that
  must stay world-fixed can't be a child of a tracked transform. (Not parented to the ledge -
  ledges are non-uniformly scaled, which would shear a rotated child.)
- **`PlayerHandInteraction`** — casts one ray per hand (configurable length, layer mask, and
  per-hand angle offset) and highlights whatever `IHighlightable` it hits, handling both hands
  targeting the same object. Exposes `LeftTarget`/`RightTarget` and the hit points
  `LeftTargetPoint`/`RightTargetPoint`; `PlayerClimbing` grabs from these. Ticked before
  climbing, so they're always this frame's.
- **`HandRayReticle`** — runtime-built billboard disc shown where a hand ray hits a
  highlightable; ticked by `PlayerHandInteraction`.
- **`PlayerHandAnimation`** — per-hand Animator finger curl. A private `HandState` enum
  (`Idle`, `Climbing`) decides who owns each hand's pose: `Idle` sets `TriggerCurl` (index)
  and `GripCurl` (middle/ring/pinky) from input; other states leave the pose to their own
  system. New hand states (holding items, tools) are added to `HandState`/`GetState()`.

`Assets/Scripts/Player/Debug/` holds standalone debug/diagnostic MonoBehaviours (e.g.
`VRDebugInput`, `InputTest`, `TrackingTest`, `TurnInputTest`, `TurnActionTest`,
`LocomotionInputTest`, `HandRayDebug`, `MovementStateDebug`) used for manually verifying systems in Play Mode — not
part of the runtime gameplay path.

### Interaction systems (`Assets/Scripts/Interaction/`, namespace `Interaction`)

- **`IHighlightable`** — interface for anything a hand ray can highlight (`SetHighlighted`).
- **`HighlightableRegistry`** — static `Collider → IHighlightable` dictionary. Highlightables
  register in `OnEnable`/unregister in `OnDisable`, so hand raycasts do a dictionary lookup
  instead of an interface `GetComponent`.
- **`ClimbableEdge`** — designer-placed `BoxCollider` that is both the hand-ray target and the
  highlight visual (opacity fade via `MaterialPropertyBlock`). Registers with
  `HighlightableRegistry`; `PlayerClimbing` identifies it from a hand's ray target with a type
  check. Implements `IHandSnapTarget`: the snap point is the ray hit moved onto the box's
  top-front line (clamped to its length), facing into the wall. **Orientation convention:**
  local X runs along the edge, +Y is up, +Z points out from the wall towards the player.
- **Hand snap poses** — the general "hand snaps onto a grab target" mechanism, reused by
  ledges now and ladders, ropes, handles, tools and props later. `HandPose` (enum of finger
  poses; `PlayerHandAnimation` owns how each maps to the Animator), `HandSnapPose` (readonly
  struct: world position, rotation, pose), `IHandSnapTarget.GetSnapPose(isLeftHand, grabPoint)`.
  The target computes a model-agnostic grip frame; a shared `HandSnapProfile` ScriptableObject
  (Create > TeaLeaf > Hand Snap Profile) applies per-hand position/rotation offsets in that
  frame, so all targets of one kind are tuned in one asset.

### Physics layers

User layers 6-13: `Environment` (static world geometry), `Player` (the CharacterController
object, the Player root), `PlayerHands` (Left/Right Hand objects - physical hand colliders
later), `Interactable` (grabbables, doors, loot, locks), `Climbable`
(`ClimbableEdge`s, later ladders/ropes), `Guard` (body capsule), `GuardHead` (blackjack target),
`Projectile` (bolts). Unassigned objects stay on `Default`, which still collides with
everything. Collision matrix pairs that are ON among these: Environment with Environment/Player/
PlayerHands/Interactable/Guard/Projectile; Player-Interactable; Player-Guard;
PlayerHands-Interactable; Interactable with Interactable/Guard/Projectile; Guard-Projectile.
Everything else is off (notably Player-PlayerHands, and Climbable with everything - it's a grab
volume, not geometry). The matrix doesn't affect raycasts/overlaps: queries must pass their own
`LayerMask` (hand rays use Environment + Interactable + Climbable, so walls block them). Shadow
volumes are planned as physics-free point-in-box checks, so they have no layer.

XRI's `XR Input Modality Manager` on Player deactivates the Left/Right Hand (controller) objects
while that controller is untracked, including at scene start before their children's `Awake()`
has run. Anything under those objects that is ticked externally must tolerate being inactive
(see `HandRayReticle.Tick()`).

### Hand art/animation

Real hand model (`Assets/Art/Models/hand.fbx`, Generic rig, bones like
`J_Left_HandIndex1-3`). It is a left hand; the right hand is the same model mirrored with
`LocalScale.x = -1`, so both hands share one Animator Controller
(`Assets/Art/Animations/Hands/Left Hand Visual.controller`). It has one layer per finger
group, each a 1D blend tree between single-keyframe Open/Closed pose clips: Base Layer
(`TriggerCurl`, index) and Grip (`GripCurl`, middle+ring+pinky). No Avatar Masks are used —
each clip only keys its own finger bones, and a Generic-rig layer only writes the properties
its clips animate. New pose clips should likewise key only the bones they need. The thumb
isn't animated yet. Keep the mirrored right hand in mind for colliders and anything
handedness-dependent.

### Locomotion design decisions

- **Jumping is in** (reversed from an earlier "no jumping" decision) — a custom jump button
  in Phase 1, not XRI's jump provider. `_verticalVelocity` is set from a tunable jump *height*.
  A horizontal air momentum carries the takeoff movement velocity through the jump; it
  clears on landing and cancels against walls. The same air momentum is reused for climb
  release: letting go of a climb keeps its momentum (a few-frame average of the applied climb
  velocity), so pushing off a ledge launches the player. Landing will emit a noise event scaled
  by fall speed.
- **Sprint is click-to-toggle** (left stick click) rather than hold, because holding a stick
  click while pushing the stick is tiring in VR. `sprintSpeed` replaces `moveSpeed`. The sprint
  ends when the stick returns to centre, on a second click, on crouch or on climb, and can't
  start while crouched. `PlayerLocomotion.IsSprinting` is the value other systems read.
- **Crouch is a button-driven toggle, not physical** — `HandleCrouch()` smoothly moves the
  CharacterController height between the standing height (captured in `Awake`) and
  `minimumHeight`, keeps `center.y` in sync, and shifts `cameraOffsetTransform` by the
  *relative* height delta (never an absolute value, which would discard the XR Origin's Camera
  Y Offset). `UpdateCharacterControllerCentre()` only touches X/Z.
- **CharacterController settings** — Slope Limit 45°, Step Offset 0.3m (validated in a test
  scene: 0.2m step and 30° ramp climbable, 60° ramp not). XR Origin uses Device tracking mode
  with a 1.6m Camera Y Offset.
- **`_frameMovement` accumulator pattern** — `PlayerController.Update()` zeroes a single
  `Vector3 _frameMovement` field, adds each system's movement contribution to it, then calls
  `characterController.Move(_frameMovement)` exactly once per frame. Any new
  movement-contributing system (sprint, jump, ladders, mantling, knockback, ...) should return
  or expose its contribution for `PlayerController` to add, rather than calling
  `characterController.Move()` directly.
- **`Update()`, not `FixedUpdate()`** — CharacterController-based movement plus VR tracking
  both want per-frame (not physics-step) updates, for lower latency.
- **Climbing is custom** — no XRI climb provider. Ladders and ropes should reuse the
  grab-and-pull-delta approach from `PlayerClimbing`.
- **Ray-targeted grabs + hand snap poses** (Phase 1, in progress) — climbing starts when grip
  is held while the hand ray/reticle is on a climbable (built; replaced the old SphereCollider
  overlap). On grab the visual hand snaps (with a short blend)
  to a target-defined position and rotation (built); the target's finger pose (e.g. fingers
  curled over a ledge) is carried but not played yet. This is a general mechanism: any grab
  target (ledge, ladder rung, rope, door handle, tool, prop) supplies its own per-hand snap
  pose, rather than hand code special-casing each one.
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
- **Traversal:** sprint, jump, climb push-off momentum, mantling, ladders, rope climbing,
  drag/hide KO'd bodies.
- **Physical hands:** the visual hand is separate from the tracked controller, collides with
  the world via dedicated primitive colliders, stops at surfaces, and elastic-bands back to
  the controller once clear (Alyx / Thief VR style). Planned for Phase 3, before grabbing; the
  maintainer wants to be guided through choosing the most performant approach when it's built.
- **Art:** greybox only (ProBuilder/primitives); out of scope: settings/main menu, save/load,
  final art, combat, fall damage.

### Planned systems

`Assets/Scripts/{AI,Core,Inventory,UI}` are still empty placeholder folders — future systems
land there following the same one-class, one-responsibility pattern (Core: game state, noise,
visibility; AI: guards; Inventory: items/loot; UI: wrist radial/display).

### Scenes

`Assets/Scenes/Main.unity` is the sole scene currently in the project.
