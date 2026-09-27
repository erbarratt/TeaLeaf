# Player systems (`Assets/Scripts/Player/`, namespace `Player`)

Detail for the player systems. The root `CLAUDE.md` holds the project rules, the tick order,
the Player hierarchy and the physics layers; this file is loaded when working in this folder.
Keep it up to date with every change to these systems, like the root file.

Note: `PlayerLocomotion.cs` predates the coding standards and doesn't fully match yet —
several `if (...){` lines in `HandleTurning()` are missing the space before `{`, and many older
field comments use `///` without `<summary>` (newer fields use `//`).

## Input

- **`PlayerInputXR`** — the single source of truth for controller input. Wraps Input System
  `InputActionReference`s (grip/trigger per hand, move/turn thumbsticks, crouch, sprint) and
  caches them once per frame in `Tick()` as typed properties (`MoveAxis`, `TurnAxis`,
  `LeftGrip`, `IsLeftGrabbing`, `CrouchPressed`, `SprintPressed`, `JumpPressed`, etc.). Enables
  every action it reads in `OnEnable()` rather than relying on the asset being enabled, and
  caches the resolved `InputAction`s there (no `.action` lookups per frame). Also calls
  `Tick()` from its own `Update()` as a fallback so Debug scripts work without a full rig;
  `Tick()` is guarded by `Time.frameCount`, so only the first call each frame reads input.
  Gameplay code should always read input through this class rather than referencing Input
  Actions directly.

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

Rename a placeholder to its function when it gets a job (e.g. the wrist radial menu), and add
it to `PlayerInputXR` then - not before. Bind XR actions to a specific hand
(`{LeftHand}`/`{RightHand}`) - a bare `<XRController>` binding fires from either controller -
and never add interactions unless they're wanted. Unity's template actions (`Look`, `Attack`,
`Interact`, `Previous`, `Next`) are still in the map but unused; `Attack` has a bare
either-trigger XR binding.

XRI's sample `Turn` and `Snap Turn` actions have Sector interactions on their bindings, so they
read (0, 0) unless the stick is pushed straight from centre into the left/right sector (this was
probably also the old "Turn doesn't produce values on the HP Reverb G2" quirk). Turning
therefore reads the project's own interaction-free `Player/Turn` action.

## Body and movement

- **`PlayerTracking`** — the single source of truth for tracked XR transforms (head, left hand,
  right hand), exposing position/rotation accessors. Other systems should query this class
  instead of walking the XR Rig hierarchy.
- **`PlayerController`** — the tick orchestrator and sole owner of `characterController.Move()`
  (see the tick order in the root `CLAUDE.md`).
- **`PlayerLocomotion`** — thumbstick movement, turning, gravity, and crouch for the
  `CharacterController`, exposed as `TickBody()`/`TickMovement(isClimbing)`/`TickTurning()`. It
  returns its movement rather than calling `Move()` itself. Movement is relative to the rig root
  (`playerTransform`), not the headset. Supports snap turn and smooth turn (`useSmoothTurn`);
  turning works while climbing. The controller's horizontal center is re-centered under the
  headset whenever the head has drifted more than `recentreThreshold` (2mm) from it, so
  tracking jitter doesn't rewrite the physics shape every frame. Horizontal movement is one persistent `_horizontalVelocity`: set from the
  stick while grounded, kept as momentum while airborne (light air control/drag), and reduced to
  the actually-applied velocity when an airborne `Move()` hits a side. Jump (`HandleJump()`,
  before gravity) has coyote time, a jump buffer and a `_hasJumped` guard. Exposes
  `IsSprinting`, `IsCrouching`, `MovementState`, and a `Landed` event (fall speed).
- **`MovementState`** (enum) — `Still`, `Walking`, `Sprinting`, `CrouchStill`, `CrouchWalking`,
  `Climbing`, `Airborne`, `Mantling` (set whenever a mantle is running): the single value
  noise, visibility, AI and the wrist gem should read, rather than combining flags themselves.
  It is set by `PlayerLocomotion.TickState()` in priority order (Climbing > Airborne > crouch >
  still > sprint/walk). "Moving" means the real horizontal applied movement exceeds
  `movingSpeedThreshold`, so pushing into a wall counts as still. `Airborne` only kicks in after
  `airborneGraceTime` off the ground, since `isGrounded` flickers on steps and slopes - except
  after a jump, which is `Airborne` immediately. Physical roomscale walking doesn't count as
  moving yet.

## Climbing and mantling

- **`PlayerClimbing`** — grab-and-pull climbing. A hand grabs the `IClimbable` (ledge, ladder or
  rope) its hand ray is on (`PlayerHandInteraction.LeftTarget`/`RightTarget`) while grip is
  held, so what the reticle is on is what gets grabbed and the ray length is the grab reach;
  the most recent grab becomes the primary hand, which drives movement (hand-off to the other
  hand on release). Hand deltas are measured in `playerTransform` local space to avoid a
  feedback loop, and any movement the CharacterController didn't apply is retried via
  `ReportAppliedMovement()` so the grab point never drifts. On grab it asks the target for a
  `HandSnapPose` and hands it to that hand's `HandVisualSnap`
  (`PlayerHandVisuals.LeftVisualSnap`/`RightVisualSnap`). `ReleaseAll()` force-releases both
  hands (used by mantling); a force-released hand can't grab again until its grip is let go,
  since "held" grabbing would otherwise instantly re-grab. Sets
  `characterController.minMoveDistance = 0` so slow hand movement isn't swallowed. Exposes
  `IsClimbing`, `IsLeftHandGripping`/`IsRightHandGripping`, `FrameMovement`, and
  `LeftGrabbedEdge`/`RightGrabbedEdge` (the held `ClimbableEdge`, or null while holding nothing
  or a non-ledge - used by mantling).
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

## Hands

- **`PlayerHandVisuals`** (on `Hands`) — the single owner of where each hand *visual* is placed,
  so no two systems fight over a visual transform. Holds the `leftHandVisual`/`rightHandVisual`
  references and `snapBlendDuration`, creates and ticks both `HandVisualSnap`s
  (`LeftVisualSnap`/`RightVisualSnap`), which `PlayerClimbing` snaps/releases and
  `PlayerHandAnimation` reads, plus one `HandPhysicalFollow` per hand. Per hand, per tick: the
  snap first; only if it isn't using the visual (not snapped and fully blended back) does the
  physical follow run - snapping always wins. Physical Hands settings: `collisionLayers` (must
  be Environment + Interactable; never Player/Climbable; empty = collision off), `handRadius`
  (0.035m), `skinWidth` (0.005m), and the wrist/fingertip bones per hand. `Reset()` / the "Find
  Hand Bones" context menu find the visuals and bones by name (`J_Left_Hand`,
  `J_Left_HandMiddle4` - the mirrored right hand shares the Left names). Exposes
  `IsLeftHandInContact`/`IsRightHandInContact`. Gizmos (selected): in Play Mode the target
  capsule (faint) and the visual's (green, red in contact); in Edit Mode the bone capsule, for
  checking `handRadius`.
- **`HandPhysicalFollow`** — plain C# class, one per hand, the physical hands collision response
  (kinematic sweep, no Rigidbody). A capsule from wrist to middle fingertip (measured once from
  the bones at Awake, in controller space so the mirrored hand needs nothing special; ends inset
  by the radius) is `CapsuleCast` from last frame's visual position towards the controller's;
  hits stop it `skinWidth` short and the rest of the move is projected onto the surface and
  re-swept (collide-and-slide, max 3 sweeps, no allocations). Triggers are ignored. A sweep that
  starts inside geometry (distance 0) doesn't block - depenetration is a later step. Blocked →
  visual detached and placed by code; clear → re-attached to the controller at its rest pose
  (no smooth catch-up yet). Attached state is read from the actual parent, since
  `HandVisualSnap` detaches the same transform. Rotation always follows the controller.
  `Suspend()` while snapped/untracked.
- **`HandVisualSnap`** — plain C# class (one per hand, owned and ticked by `PlayerHandVisuals`;
  other systems only call `Snap()`/`Release()`). Blends a hand *visual* (never the tracked
  controller) between its rest local pose and a world-space `HandSnapPose` over
  `snapBlendDuration` (SmoothStep), both ways, and doesn't touch the transform at rest. Exposes
  the eased blend as `Weight` (0 = following the controller, 1 = snapped), which
  `PlayerHandAnimation` uses as the finger pose layer weight so the two stay in step. While
  snapped the visual is **detached to the scene root** and re-attached when the release blend
  ends: the hands' Tracked Pose Drivers use "Update And Before Render", so they move the
  controller again after all `Update()` code, and a child visual would wobble. Anything that
  must stay world-fixed can't be a child of a tracked transform. (Not parented to the ledge -
  ledges are non-uniformly scaled, which would shear a rotated child.)
- **`PlayerHandInteraction`** — casts one ray per hand (configurable length, layer mask, and
  per-hand angle offset, pre-rotated into a cached local ray direction in `Awake()`/
  `OnValidate()`) and records whatever `IHandTarget` it hits (nothing is highlighted -
  the reticle alone shows what can be interacted with). Exposes `LeftTarget`/`RightTarget` and
  the hit points `LeftTargetPoint`/`RightTargetPoint`; `PlayerClimbing` grabs from these.
  Ticked before climbing, so they're always this frame's. `TickReticles()` places the reticles
  later in the frame (step 8b).
- **`HandRayReticle`** — runtime-built billboard disc shown where a hand ray hits a hand target,
  hidden while that hand is holding something (its visual is snapped); ticked by
  `PlayerHandInteraction.TickReticles()`. Only toggles its renderer when visibility changes
  (same pattern as `MantleIndicator.SetVisible()`).
- **`PlayerHandAnimation`** — per-hand Animator. The base layers always follow input
  (`TriggerCurl` index, `GripCurl` middle/ring/pinky). The `Snap Pose` override layer plays the
  snap target's `HandPose` with its weight set from that hand's `HandVisualSnap.Weight`, so any
  snapped hand (ledges, rungs, ropes now; props, tools later) gets its target's finger pose and
  fades back to input curl on release. `SetFloat()`/`Play()`/`SetLayerWeight()` only run when
  the curl, pose or weight changes; both Animators set `keepAnimatorStateOnDisable` in `Awake()`
  so those caches stay valid when XRI deactivates an untracked controller. The layer index is looked up by name; state hashes are built once from
  `HandPose`'s enum names. (Replaced an earlier `HandState` enum — every non-input pose is a
  snap pose, so the snap weight already says who owns the fingers.) New poses: add a `HandPose`
  value plus a same-named state and clip on the layer, no new code.

### Hand art/animation

Real hand model (`Assets/Art/Models/hand.fbx`, Generic rig, bones like `J_Left_HandIndex1-3`).
It is a left hand; the right hand is the same model mirrored with `LocalScale.x = -1`, so both
hands share one Animator Controller (`Assets/Art/Animations/Hands/Left Hand Visual.controller`).
It has one layer per finger group, each a 1D blend tree between single-keyframe Open/Closed
pose clips: Base Layer (`TriggerCurl`, index) and Grip (`GripCurl`, middle+ring+pinky). No
Avatar Masks are used — each clip only keys its own finger bones, and a Generic-rig layer only
writes the properties its clips animate. New pose clips should likewise key only the bones they
need. On top sits the `Snap Pose` layer (Override, default weight 0, driven from code): one
state per `HandPose` value, **named exactly like the enum value** (e.g. `LedgeGrip`), each
holding a single-keyframe clip. A snap clip must key **every joint (1, 2 and 3) of every finger
it poses**: like the other layers it only writes the bones it keys (Write Defaults doesn't reset
unkeyed bones here), so any unkeyed joint keeps the input layers' curl - and grip is held while
climbing. The thumb isn't animated yet. Keep the mirrored right hand in mind for colliders and
anything handedness-dependent.

## Debug scripts

`Assets/Scripts/Player/Debug/` holds standalone debug/diagnostic MonoBehaviours (e.g.
`VRDebugInput`, `InputTest`, `TrackingTest`, `TurnInputTest`, `TurnActionTest`,
`LocomotionInputTest`, `HandRayDebug`, `MovementStateDebug`) used for manually verifying
systems in Play Mode — not part of the runtime gameplay path.

## Locomotion and hands design decisions

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
- **Mantling** — a quick, committed move onto a *mantleable* (always horizontal)
  `ClimbableEdge`. When a hand grips one and the head has been pulled up near the top, a small
  white head-locked arrow appears; pushing up on either thumbstick then starts the mantle. It
  overrides all other locomotion (movement, turning, gravity, jump, crouch, climbing) and can't
  be cancelled, and always lands the feet at the edge's designer-set mantle point (the same
  spot wherever the mantle started), crouched if the edge says so.
- **Climbing is custom** — no XRI climb provider. Ladders and ropes reuse the
  grab-and-pull-delta approach from `PlayerClimbing` by implementing `IClimbable` (an
  interface, not a shared base class). Ladder and rope movement is unconstrained like ledges
  (ropes are static - no swinging); their top exit is a mantleable `ClimbableEdge` placed on
  the lip (no ladder/rope-specific mantle), and their bottom exit is letting go. Letting go of
  every grip mid-climb falls normally; no fall damage in the slice.
- **Ray-targeted grabs + hand snap poses** — climbing starts when grip is held while the hand
  ray/reticle is on a climbable (replaced the old SphereCollider overlap). On grab the visual
  hand snaps (with a short blend) to a target-defined position and rotation, and plays the
  target's finger pose (e.g. fingers curled over a ledge) on the hand Animator's `Snap Pose`
  layer. This is a general mechanism: any grab target (ledge, ladder rung, rope, door handle,
  tool, prop) supplies its own per-hand snap pose, rather than hand code special-casing each
  one.
- **Physical hands: kinematic sweep, no Rigidbody** (decided 2026-09-27) — in `Update()`/the
  tick order, a hand capsule sweeps from the visual's last pose towards the controller,
  collide-and-slides, then (later step) depenetrates with `ComputePenetration`; rotation always
  follows the controller and is resolved by the push-out. The visual stays a child of the
  controller while free and is detached while in contact (Tracked Pose Driver before-render
  constraint). Rejected: a velocity-driven Rigidbody hand (physics-rate, costlier on Quest,
  jitter-prone, needs teleports for snap turn/mantle). Being built step by step (steps in
  `DEVROADMAP.txt` Phase 1).
- **Settings will eventually move out of serialized fields** — Smooth Turn/Snap Turn, turn
  speed, snap angle, and movement speed are expected to become user-configurable options (out
  of scope for the vertical slice).
