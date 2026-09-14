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
Grab Move, Jump). Movement, turning, gravity, and future systems like ladder climbing are all
hand-built instead, for full control, easier Quest 3 optimization, easier debugging,
consistent behavior across headsets, and a better understanding of how everything works.
**Do not suggest re-enabling or using XRI's built-in locomotion, teleportation, or climb
providers.** Teleport locomotion will never be used.

Known headset quirk: on the HP Reverb G2 + Oasis, `XRI Right Locomotion/Turn` doesn't produce
values — `XRI Right Locomotion/Snap Turn` must be used instead. Whether Quest 3 has the same
issue is unverified.

## Coding standards

These are explicit project conventions — follow them for new code, and flag (rather than
silently reformat) existing code that doesn't yet match:

- **Namespaces:** top-level only. `namespace Player { }`, never `namespace Player.Locomotion { }`.
- **Serialized fields:** no leading underscore — `[SerializeField] private float moveSpeed;`
- **Private non-serialized fields:** leading underscore — `private bool _snapTurnQueued;`
- **Public properties:** no leading underscore; prefer auto-properties with a private setter
  (`public Vector2 MoveAxis { get; private set; }`) over a passthrough expression body where
  the value should be cached/set elsewhere rather than recomputed on every read.
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

Note: the current `PlayerLocomotion.cs`/`PlayerInputXR.cs` don't fully match yet (mixed
Allman/K&R braces, expression-bodied properties instead of `{ get; private set; }`) — these
predate the standard being formalized.

## Working with this codebase

This is a Unity project, not a CLI/npm/dotnet-cli project — there is no command-line build,
lint, or test workflow set up. Builds, Play Mode testing, and the Test Runner (com.unity.test-framework
is installed but no test assemblies exist yet) are all driven through the Unity Editor
(open the project with the exact editor version in `ProjectSettings/ProjectVersion.txt`).
`.sln`/`.csproj` files at the repo root are Unity-generated for IDE tooling (Rider/Visual Studio)
and should not be hand-edited.

All gameplay scripts currently compile into the default `Assembly-CSharp` assembly — no
`.asmdef` files exist yet, so there is only one compilation unit for `Assets/Scripts`.

## Architecture

### Player systems (`Assets/Scripts/Player/`, namespace `Player`)

The player rig is split into three cooperating MonoBehaviours, each with a single
responsibility:

- **`PlayerInputXR`** — the single source of truth for controller input. Wraps Input System
  `InputActionReference`s (grip/trigger per hand, move/turn thumbsticks) and exposes them as
  typed properties (`MoveAxis`, `TurnAxis`, `IsLeftGrabbing`, etc.). Gameplay code should
  always read input through this class rather than referencing Input Actions directly.
- **`PlayerTracking`** — the single source of truth for tracked XR transforms (head, left
  hand, right hand), exposing position/rotation accessors. Other systems should query this
  class instead of walking the XR Rig hierarchy, so hierarchy changes only need updating in
  one place.
- **`PlayerLocomotion`** — consumes `PlayerInputXR` and drives a `CharacterController` for
  movement, turning, and gravity. Movement direction is relative to the rig root transform
  (`playerTransform`), not the headset, so looking around doesn't change "forward". Turning
  supports both snap turn and smooth turn (toggle via `useSmoothTurn`); snap turn is the
  current default because Turn action data is unreliable on the Reverb G2/Oasis driver combo.
  The `CharacterController`'s horizontal center is re-centered under the headset every frame
  while height is left to other systems (e.g. future crouching).

`Assets/Scripts/Player/Debug/` holds standalone debug/diagnostic MonoBehaviours (e.g.
`VRDebugInput`, `TrackingTest`, `TurnInputTest`, `LocomotionInputTest`) used for manually
verifying input/tracking/locomotion in Play Mode — not part of the runtime gameplay path.

### Locomotion design decisions

- **No jumping** — decided against; gravity still applies (falling off ledges works).
- **Crouch will be button-driven, not physical** — a fixed crouch height and fixed standing
  height with a smooth transition, not room-scale crouch detection. CharacterController
  height/`center.y` are intentionally left untouched by `UpdateCharacterControllerCentre()`
  for this reason.
- **`_frameMovement` accumulator pattern** — `PlayerLocomotion.Update()` zeroes a single
  `Vector3 _frameMovement` field, lets `HandleMovement()`/`HandleTurning()`/`HandleGravity()`
  each contribute to it, then calls `characterController.Move(_frameMovement)` exactly once
  per frame. Any new movement-contributing system (sprint, ladders, knockback, ...) should add
  to `_frameMovement` rather than calling `characterController.Move()` directly.
- **`Update()`, not `FixedUpdate()`** — CharacterController-based movement plus VR tracking
  both want per-frame (not physics-step) updates, for lower latency.
- **Future ladder climbing will be custom** — no XRI climb provider; concept is: grip the
  ladder, measure controller movement delta, move the player the opposite direction.
- **Settings will eventually move out of serialized fields** — Smooth Turn/Snap Turn, turn
  speed, snap angle, and movement speed are expected to become user-configurable options
  rather than inspector-only fields.

### Planned systems

`Assets/Scripts/{AI,Core,Interaction,Inventory,UI}` exist only as empty placeholder folders
(no scripts yet) — future systems are expected to land there following the same one-class,
one-responsibility pattern used by the Player scripts.

### Scenes

`Assets/Scenes/Main.unity` is the sole scene currently in the project.
