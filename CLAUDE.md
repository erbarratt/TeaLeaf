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

XRI's sample `Turn` and `Snap Turn` actions have Sector interactions on their bindings, so they
read (0, 0) unless the stick is pushed straight from centre into the left/right sector (this
was probably also the old "Turn doesn't produce values on the HP Reverb G2" quirk). Turning
therefore reads the project's own interaction-free `Player/Turn` action instead. Quest 3 input
and tracking have been verified working.

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
`if (...){` lines in `HandleTurning()` are missing the space before `{`, and many older field
comments use `///` without `<summary>` (newer fields use `//`).

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
3. `playerHandInteraction.Tick()` — hand rays + targets (reticles are placed at 8b).
4. `playerClimbing.Tick()` — grab/release, climb movement (skipped while mantling); then
   `playerMantling.Tick()` — detects a possible mantle (arrow), starts one on a stick push, or
   advances the one in progress. **While `IsMantling`, the frame stops here**: the mantle has
   already positioned the rig directly (CharacterController disabled), so only
   `TickState()`, `playerHandVisuals.Tick()`, `TickReticles()` and `playerHandAnimation.Tick()` run - no locomotion,
   turning or `Move()`.
5. `_frameMovement` = `playerLocomotion.TickMovement(isClimbing)` (thumbstick + gravity;
   zero while climbing) + `playerClimbing.FrameMovement` while climbing; then
   `playerLocomotion.TickTurning()`.
6. One `characterController.Move(_frameMovement)`, then
   `playerClimbing.ReportAppliedMovement()` while climbing.
7. `playerLocomotion.TickState(isClimbing, appliedMovement, collisionFlags)` — after `Move()`,
   because it needs the real applied movement, the `CollisionFlags` `Move()` returned (to stop
   momentum pushing into walls/ceilings), and the `isGrounded` that `Move()` just updated.
8. `playerHandVisuals.Tick()` — after turning/`Move()`: hand visuals are children of
   the rig, so a world-space snap pose placed earlier would be dragged off by them.
   8b. `playerHandInteraction.TickReticles()` — after grabs and hand visuals, so a hand that
   grabbed this frame already hides its reticle.
9. `playerHandAnimation.Tick()` — last, so it reads this frame's snap weight from step 8
   (Animators evaluate after all `Update()` calls anyway).

New per-frame player systems get a `Tick()` (or a `Tick…()` that returns a movement
contribution) and a slot in `PlayerController.Update()`. `PlayerController.Reset()`
auto-fills its references when the component is added (root components via `GetComponent`,
hand components via `GetComponentInChildren`).

### Player hierarchy (`Main.unity`, unpacked from XRI's XR Origin prefab)

```
Player                 [Player layer] XROrigin, InputActionManager, XRInputModalityManager,
                       XRGazeAssistance, CharacterController, PlayerTracking, PlayerInputXR,
                       PlayerLocomotion, PlayerClimbing, PlayerMantling, PlayerController
  Camera Offset        (crouch shifts this; Camera Y Offset 1.6m)
    Main Camera
      Mantle Indicator (MantleIndicator - head-locked, placed a little below centre ~0.5m ahead)
    Hands              PlayerHandInteraction, PlayerHandVisuals, PlayerHandAnimation (identity transform)
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
  `IsLeftGrabbing`, `CrouchPressed`, `SprintPressed`, `JumpPressed`, etc.). Enables every action it
  reads in `OnEnable()` rather than relying on the asset being enabled. Also calls `Tick()` from
  its own `Update()` as a fallback so Debug scripts work without a full rig. Gameplay code should
  always read input through this class rather than referencing Input Actions directly.

  **Input assets:** all gameplay input reads the project-owned, project-wide
  `Assets/InputSystem_Actions.inputactions` (`Player` map, auto-enabled by Unity), never XRI's
  sample `XRI Default Input Actions`, which a package update could overwrite and whose bindings
  carry XRI interactions. (The XRI asset is still enabled by the Input Action Manager on Player
  for XRI's own use.) The `Player` map has an `XR`-group action for every Quest controller input,
  named by function where gameplay uses it and by button as a placeholder where it doesn't yet:

  | Action | XR binding | Used by |
  |---|---|---|
  | `Move` | `{LeftHand}/{Primary2DAxis}` | movement |
  | `Turn` | `{RightHand}/{Primary2DAxis}` | turning, mantle (stick up) |
  | `LeftGrip` / `RightGrip` | `{LeftHand}`/`{RightHand}/{Grip}` | grabbing, finger curl |
  | `LeftTrigger` / `RightTrigger` | `{LeftHand}`/`{RightHand}/{Trigger}` | index curl |
  | `Sprint` | `{LeftHand}/{Primary2DAxisClick}` | sprint toggle |
  | `Crouch` | `{RightHand}/{PrimaryButton}` (A) | crouch toggle |
  | `Jump` | `{RightHand}/{SecondaryButton}` (B) | jump |
  | `ButtonX` | `{LeftHand}/{PrimaryButton}` | unassigned placeholder |
  | `ButtonY` | `{LeftHand}/{SecondaryButton}` | unassigned placeholder |
  | `Menu` | `{LeftHand}/{MenuButton}` | unassigned placeholder |
  | `RightStickClick` | `{RightHand}/{Primary2DAxisClick}` | unassigned placeholder |

  Rename a placeholder to its function when it gets a job (e.g. the wrist radial menu), and
  add it to `PlayerInputXR` then - not before. Bind XR actions to a specific hand
  (`{LeftHand}`/`{RightHand}`) - a bare `<XRController>` binding fires from either controller -
  and never add interactions unless they're wanted. Unity's template actions (`Look`,
  `Attack`, `Interact`, `Previous`, `Next`) are still in the map but unused; `Attack` has a bare
  either-trigger XR binding.
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
  headset every frame. Horizontal movement is one persistent `_horizontalVelocity`: set from
  the stick while grounded, kept as momentum while airborne (light air control/drag), and
  reduced to the actually-applied velocity when an airborne `Move()` hits a side. Jump
  (`HandleJump()`, before gravity) has coyote time, a jump buffer and a `_hasJumped` guard.
  Exposes `IsSprinting`, `IsCrouching`, `MovementState`, and a `Landed` event (fall speed).
- **`MovementState`** (enum) — `Still`, `Walking`, `Sprinting`, `CrouchStill`, `CrouchWalking`,
  `Climbing`, `Airborne`, `Mantling` (set whenever a mantle is running): the single value noise, visibility, AI and the wrist gem should read,
  rather than combining flags themselves. It is set by `PlayerLocomotion.TickState()` in priority
  order (Climbing > Airborne > crouch > still > sprint/walk). "Moving" means the real horizontal
  applied movement exceeds `movingSpeedThreshold`, so pushing into a wall counts as still.
  `Airborne` only kicks in after `airborneGraceTime` off the ground, since `isGrounded`
  flickers on steps and slopes - except after a jump, which is `Airborne` immediately. Physical roomscale walking doesn't count as moving yet.
- **`PlayerClimbing`** — grab-and-pull climbing. A hand grabs the `IClimbable` (ledge, ladder
  or rope) its hand ray is on (`PlayerHandInteraction.LeftTarget`/`RightTarget`) while
  grip is held, so
  what the reticle is on is what gets grabbed and the ray length is the grab reach; the most recent grab becomes the primary hand, which drives
  movement (hand-off to the other hand on release). Hand deltas are measured in
  `playerTransform` local space to avoid a feedback loop, and any movement the
  CharacterController didn't apply is retried via `ReportAppliedMovement()` so the grab point
  never drifts. On grab it asks the edge for a `HandSnapPose` and hands it to that hand's
  `HandVisualSnap` (`PlayerHandVisuals.LeftVisualSnap`/`RightVisualSnap`). `ReleaseAll()` force-releases
  both hands (used by mantling); a force-released hand can't grab again until its grip is let
  go, since "held" grabbing would otherwise instantly re-grab. Sets
  `characterController.minMoveDistance = 0` so slow hand movement isn't swallowed. Exposes
  `IsClimbing`, `IsLeftHandGripping`/`IsRightHandGripping`, `FrameMovement`, and
  `LeftGrabbedEdge`/`RightGrabbedEdge` (the held `ClimbableEdge`, or null while holding
  nothing or a non-ledge - used by mantling).
- **`PlayerMantling`** — decides when a mantle is possible (`CanMantle`, `MantleEdge`) and shows
  the `MantleIndicator` to match: a hand grips a mantleable edge and the head is at least
  `ledge top - headBelowTopAllowance`. No physics queries - where the mantle lands and whether
  it ends crouched are per-edge designer data on `ClimbableEdge`. Either stick pushed up
  (`stickUpThreshold`) starts the mantle: `PlayerClimbing.ReleaseAll()`,
  `PlayerLocomotion.BeginMantle(endsCrouched)`, CharacterController disabled, then the rig is
  positioned directly along an eased up-and-over arc (`duration`, `riseEndsAt`,
  `forwardStartsAt`) so the capsule bottom lands on the mantle point (+`landingLift`).
  Uncancellable; `IsMantling` makes `PlayerController` skip everything else. At the end the
  controller is re-enabled and `EndMantle()` hands back control. **The one exception to the
  single-`Move()` rule** - chosen because the landing is designer-placed and collision could
  only stop it landing there.
- **`MantleIndicator`** — runtime-built white arrow on a child of Main Camera (so head-locked
  with no code). Uses `UI/Default` with `unity_GUIZTestMode` = Always so it draws through walls
  (the face is against the wall while climbing) while staying single-pass-stereo safe.
  `SetVisible()` only touches the renderer on change.
- **`PlayerHandVisuals`** (on `Hands`) — the single owner of where each hand *visual* is
  placed, so no two systems fight over a visual transform. Holds the `leftHandVisual`/
  `rightHandVisual` references and `snapBlendDuration`, creates and ticks both
  `HandVisualSnap`s (`LeftVisualSnap`/`RightVisualSnap`), which `PlayerClimbing` snaps/releases
  and `PlayerHandAnimation` reads. Physical hands (step 2 on) are being added here, with
  snapping taking priority. `Reset()` finds the visuals by name.
- **`HandVisualSnap`** — plain C# class (one per hand, owned and ticked by `PlayerHandVisuals`;
  other systems only call `Snap()`/`Release()`). Blends a hand *visual*
  (never the tracked controller) between its rest local pose and a world-space `HandSnapPose`
  over `snapBlendDuration` (SmoothStep), both ways, and doesn't touch the transform at rest.
  Exposes the eased blend as `Weight` (0 = following the controller, 1 = snapped), which
  `PlayerHandAnimation` uses as the finger pose layer weight so the two stay in step.
  While snapped the visual is **detached to the scene root** and re-attached when the release
  blend ends: the hands' Tracked Pose Drivers use "Update And Before Render", so they move the
  controller again after all `Update()` code, and a child visual would wobble. Anything that
  must stay world-fixed can't be a child of a tracked transform. (Not parented to the ledge -
  ledges are non-uniformly scaled, which would shear a rotated child.)
- **`PlayerHandInteraction`** — casts one ray per hand (configurable length, layer mask, and
  per-hand angle offset) and records whatever `IHandTarget` it hits (nothing is highlighted -
  the reticle alone shows what can be interacted with). Exposes `LeftTarget`/`RightTarget` and the hit points
  `LeftTargetPoint`/`RightTargetPoint`; `PlayerClimbing` grabs from these. Ticked before
  climbing, so they're always this frame's.
- **`HandRayReticle`** — runtime-built billboard disc shown where a hand ray hits a
  hand target, hidden while that hand is holding something (its visual is snapped); ticked
  by `PlayerHandInteraction.TickReticles()`.
- **`PlayerHandAnimation`** — per-hand Animator. The base layers always follow input
  (`TriggerCurl` index, `GripCurl` middle/ring/pinky). The `Snap Pose` override layer plays the
  snap target's `HandPose` with its weight set from that hand's `HandVisualSnap.Weight`, so
  any snapped hand (ledge now; rungs, ropes, props, tools later) gets its target's finger pose
  and fades back to input curl on release. `Play()`/`SetLayerWeight()` only run when the pose
  or weight changes. The layer index is looked up by name; state hashes are built once from
  `HandPose`'s enum names. (Replaced an earlier `HandState` enum — every non-input pose is a
  snap pose, so the snap weight already says who owns the fingers.) New poses: add a
  `HandPose` value plus a same-named state and clip on the layer, no new code.

`Assets/Scripts/Player/Debug/` holds standalone debug/diagnostic MonoBehaviours (e.g.
`VRDebugInput`, `InputTest`, `TrackingTest`, `TurnInputTest`, `TurnActionTest`,
`LocomotionInputTest`, `HandRayDebug`, `MovementStateDebug`) used for manually verifying systems in Play Mode — not
part of the runtime gameplay path.

### Interaction systems (`Assets/Scripts/Interaction/`, namespace `Interaction`)

- **`IHandTarget`** — empty marker interface for anything a hand ray can target (the reticle
  shows on it; other systems decide what targeting means). **No highlighting** - removed
  2026-09-27, the reticle is enough feedback. (Was `IHighlightable`.)
- **`HandTargetRegistry`** — static `Collider → IHandTarget` dictionary. Targets register in
  `OnEnable`/unregister in `OnDisable`, so hand raycasts do a dictionary lookup instead of an
  interface `GetComponent`.
- **Climbables have no mesh** — `ClimbableEdge`/`Ladder` are just a `BoxCollider` and
  `ClimbableRope` a `CapsuleCollider` (invisible in game), shown in the Scene view by always-on gizmos. A renderer is only added to an individual
  item that should be visible, as a separate component the scripts don't touch.
- **`IClimbable`** — extends `IHandSnapTarget`; marks a grab target as climbable (grabbing it
  starts a climb, unlike future handles/tools/props). Adds no members yet: every climbable
  moves the player the same way. `PlayerClimbing` finds it on a hand's ray target with a type
  check. Implemented by `ClimbableEdge`, `Ladder` and `ClimbableRope`.
- **`ClimbableRope`** — `IHandTarget` + `IClimbable`. A static straight line (no swinging)
  hanging from its transform's position down local -Y for `length` metres. Sizes its own
  `CapsuleCollider` (Awake, OnValidate, `SetLength()`), dividing real metres by scale, and
  locks it (`HideFlags.NotEditable`) - `length`/`grabRadius` on the rope are the only size
  controls, and the top (the object's position) stays put when `length` changes. `grabRadius` (0.08m) is far thicker than a rope so rays can hit
  it. `SetLength()` is for Phase 5's rope bolt. `GetSnapPose()`: nearest point on the axis
  (clamped to the ends), grip frame facing into the rope from the side the ray hit (a rope has
  no front), `RopeGrip` `HandSnapProfile`. Gizmos: the line always, faint; when selected, bright
  with end rings and the capsule's sides. Exits as ladders.
- **`Ladder`** — `IHandTarget` + `IClimbable`. One `BoxCollider` over the whole ladder is
  the ray target; no per-rung colliders. Rungs are designer data (`firstRungHeight`, `rungSpacing`, real metres in the
  unscaled local frame - position + rotation, box size × `lossyScale`), and the rung count is
  however many fit. `GetSnapPose()` snaps to the rung nearest the ray hit, on the box's
  centre line front to back, using a ledge-style grip frame and the `LadderRung`
  `HandSnapProfile`. Same orientation convention as `ClimbableEdge`. Gizmos: box outline, rails
  and rungs always drawn faintly; when selected, bright with a sphere per rung and an arrow out of the
  climbing side. Top exit = a mantleable `ClimbableEdge` on the lip above.
- **`ClimbableEdge`** — designer-placed `BoxCollider` that is the hand-ray target
  (`IHandTarget`, registered with `HandTargetRegistry`). Gizmos always draw its box faintly and
  its lip (top-front line) brightly. Implements `IClimbable` (so `IHandSnapTarget`): the snap point is the ray hit moved onto the box's
  top-front line (clamped to its length), facing into the wall - via `ClosestLipPoint()`, which
  mantling also uses for the ledge-top height. Mantling data (Mantling header): `isMantleable`
  (tick only on level edges), `mantleEndsCrouched`, and `mantlePoint` - where the feet always
  land, relative to the edge's origin in real metres along its axes (`MantlePointWorld` =
  position + rotation × point, **not** `TransformPoint()`, since ledges are stretched cubes).
  Defaulted from the box (top face, 0.4m in from the lip) by `Reset()` / the "Reset Mantle
  Point" context menu; a gizmo shows it while the edge is selected.
  **Orientation convention:**
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
its clips animate. New pose clips should likewise key only the bones they need. On top sits
the `Snap Pose` layer (Override, default weight 0, driven from code): one state per
`HandPose` value, **named exactly like the enum value** (e.g. `LedgeGrip`), each holding a
single-keyframe clip. A snap clip must key **every joint (1, 2 and 3) of every finger it
poses**: like the other layers it only writes the bones it keys (Write Defaults doesn't reset
unkeyed bones here), so any unkeyed joint keeps the input layers' curl - and grip is held
while climbing. The thumb isn't animated yet. Keep the mirrored right hand in mind for
colliders and anything handedness-dependent.

### Locomotion design decisions

- **Jumping is in** (reversed from an earlier "no jumping" decision) — a custom jump on right
  B, not XRI's jump provider. `_verticalVelocity` is set from a tunable jump *height*
  (`sqrt(2h·-g)`). Horizontal momentum (`_horizontalVelocity`) carries the takeoff velocity
  through the jump with **light air control**; walls redirect it and ceilings stop the rise
  (via `Move()`'s `CollisionFlags`, instead of a pre-jump clearance check). **Jump while
  crouched only stands up** (stealth-safe); a jump waits for full standing height. `Landed`
  (fall speed) will drive the landing noise event.
- **No climb release momentum** (tried and removed, 2026-09-27) — letting go of a climb drops
  the player from rest. Pushing off a ledge to launch felt bad in headset either way it was
  tuned: the body is locked 1:1 to the hand while gripping and players stop their arm before
  letting go, so an averaged launch is a brake-then-kick stutter, and launching at the last
  frame's velocity (seamless) barely launches at all. Don't re-add it without a new idea for
  that problem.
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
- **Mantling** — a quick, committed move onto a *mantleable*
  (always horizontal) `ClimbableEdge`. When a hand grips one and the head has been pulled up
  near the top, a small white head-locked arrow appears; pushing up on either thumbstick then starts the mantle. It overrides all other
  locomotion (movement, turning, gravity, jump, crouch, climbing) and can't be cancelled, and
  always lands the feet at the edge's designer-set mantle point (the same spot wherever the
  mantle started), crouched if the edge says so. Full plan in `DEVROADMAP.txt` Phase 1.
- **Climbing is custom** — no XRI climb provider. Ladders and ropes reuse the
  grab-and-pull-delta approach from `PlayerClimbing` by implementing `IClimbable` (an
  interface, not a shared base class). Ladder and rope movement is unconstrained like ledges
  (ropes are static - no swinging); their top exit is a mantleable `ClimbableEdge` placed on
  the lip (no ladder/rope-specific mantle), and their bottom exit is letting go.
- **Ray-targeted grabs + hand snap poses** — climbing starts when grip is held while the hand
  ray/reticle is on a climbable (replaced the old SphereCollider overlap). On grab the visual hand snaps (with a short blend)
  to a target-defined position and rotation, and plays the target's finger pose (e.g. fingers
  curled over a ledge) on the hand Animator's `Snap Pose` layer. This is a general mechanism: any grab
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
- **Traversal:** sprint, jump, mantling, ladders, rope climbing,
  drag/hide KO'd bodies.
- **Physical hands:** the visual hand is separate from the tracked controller, collides with
  the world via dedicated primitive colliders, stops at surfaces, and elastic-bands back to
  the controller once clear (Alyx / Thief VR style). A core concept: part of Phase 1 (moved
  from Phase 3 on 2026-09-27), before grabbing. **Approach decided: kinematic sweep, no
  Rigidbody** - in `Update()`/the tick order, a hand capsule sweeps from the visual's last pose
  towards the controller, collide-and-slides, then depenetrates (`ComputePenetration`);
  rotation always follows the controller and is resolved by the push-out. The visual stays a
  child of the controller while free and is detached while in contact (Tracked Pose Driver
  before-render constraint). Rejected: a velocity-driven Rigidbody hand (physics-rate, costlier
  on Quest, jitter-prone, needs teleports for snap turn/mantle). Being built step by step.
- **Art:** greybox only (ProBuilder/primitives); out of scope: settings/main menu, save/load,
  final art, combat, fall damage (fall damage/health come in a later damage phase, after the
  slice; falling itself already works - letting go of every grip mid-climb falls normally).

### Planned systems

`Assets/Scripts/{AI,Core,Inventory,UI}` are still empty placeholder folders — future systems
land there following the same one-class, one-responsibility pattern (Core: game state, noise,
visibility; AI: guards; Inventory: items/loot; UI: wrist radial/display).

### Scenes

`Assets/Scenes/Main.unity` is the sole scene currently in the project.
