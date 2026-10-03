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
- **`PlayerHaptics`** (on the Player root, added 2026-09-30) — the single way to buzz a
  controller: `Pulse(isLeftHand, amplitude, duration)`. Sends through
  `OpenXRInput.SendHapticImpulse` on the `LeftHaptic`/`RightHaptic` actions (PassThrough, bound
  to each controller's `haptic` output - the name every OpenXR controller profile uses), which
  it enables itself. No `Update()`/`Tick()`; for events, not per frame (OpenXR looks the
  controller up by name on each call, a small allocation). Right-click "Test Left/Right Pulse"
  (Play Mode) checks the bindings. Also owns the feedback tuning: it subscribes to
  `PlayerHandVisuals.HandContactStarted` and taps that controller (`contactAmplitude` 0.15,
  `contactDuration` 0.03s), at most once per `contactCooldown` (0.25s) per hand so sliding over
  a bumpy surface doesn't buzz continuously. Verified working through Virtual Desktop + SteamVR
  (both this route and the older `XR.InputDevice.SendHapticImpulse`).
  Global on/off: `HapticsEnabled` (see the settings decision below).

**Input assets:** all gameplay input reads the project-owned, project-wide
`Assets/InputSystem_Actions.inputactions` (auto-enabled by Unity, every map). (XRI and its
sample `XRI Default Input Actions` were removed 2026-09-30; before that, gameplay already
avoided them, since a package update could overwrite them and their bindings carried XRI
interactions.) The `Tracking` map holds the head and controller poses the three Tracked Pose
Drivers read (`Head`/`LeftHand`/`RightHand` + `Position`/`Rotation`/`TrackingState`: head
`<XRHMD>/centerEye*`, controllers `<XRController>{Hand}/pointerPosition`/`pointerRotation` -
the aim pose everything is tuned against - and `/trackingState`). The `Player` map has an `XR`-group action for every Quest controller input,
named by function where gameplay uses it and by button as a placeholder where it doesn't yet:

| Action | XR binding | Used by |
|---|---|---|
| `Move` | `{LeftHand}/{Primary2DAxis}` | movement |
| `Turn` | `{RightHand}/{Primary2DAxis}` | turning, mantle (stick up) |
| `LeftGrip` / `RightGrip` | `{LeftHand}`/`{RightHand}/{Grip}` | grabbing, finger curl |
| `LeftTrigger` / `RightTrigger` | `{LeftHand}`/`{RightHand}/{Trigger}` | index curl (planned: aim/throw a held object) |
| `Sprint` | `{LeftHand}/{Primary2DAxisClick}` | sprint toggle |
| `Crouch` | `{RightHand}/{PrimaryButton}` (A) | crouch toggle |
| `Jump` | `{RightHand}/{SecondaryButton}` (B) | jump |
| `ButtonX` | `{LeftHand}/{PrimaryButton}` | unassigned placeholder |
| `ButtonY` | `{LeftHand}/{SecondaryButton}` | unassigned placeholder |
| `Menu` | `{LeftHand}/{MenuButton}` | unassigned placeholder |
| `RightStickClick` | `{RightHand}/{Primary2DAxisClick}` | unassigned placeholder |
| `LeftHaptic` / `RightHaptic` | `{LeftHand}`/`{RightHand}/haptic` | output, not input: `PlayerHaptics` (PassThrough) |

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
  instead of walking the XR Rig hierarchy. Its `Start()` also sets XR tracking to Device mode
  (what XROrigin did; see "Tracking setup" in the root `CLAUDE.md`), then - with
  `calibrateView` (default on) and XR running - starts the one-off `CalibrateWhenTracked()`
  coroutine: `ScreenFade.Instance.Hold()`, wait until the head reports a tracked position and
  rotation (`InputDevices`/`CommonUsages.trackingState`) or `trackingTimeout` (3s),
  `Calibrate()`, one more frame, `Release()`. **`Calibrate()`** puts the view right by moving
  Camera Offset (`head.parent`), never the tracked transforms: (1) if the head's up points
  below the horizon (`IsHeadUpsideDown()`), `FlipTrackingSpace()` turns Camera Offset half a
  turn about the head's level facing direction, through the head's position - same spot, same
  facing, upright - and logs the rotations it found; (2) it shifts Camera Offset vertically so
  the head is at standing eye height (Camera Offset's saved 1.6m, captured in `Awake()`) less
  the game crouch's drop (standing capsule height - current). **`Tick()`** (PlayerController
  step 1b) recalibrates whenever the head's raw local pose jumps faster than a head can move
  (8 m/s or 1500°/s, scaled by frame time) - i.e. the headset was recentred. Added 2026-10-03:
  through Virtual Desktop/SteamVR the level often started with the tracking space rolled 180°
  (head local rotation ~(358, 1, 178)) and its origin on the floor until the Quest was
  recentred; `XRInputSubsystem.TryRecenter()` didn't cure it (tried and removed), a one-off
  flip left the height wrong, and a later Quest recentre then turned the view upside down
  again - hence height in the calibration and the per-frame jump check. **Camera Offset can
  therefore be rotated and moved** - don't assume its saved pose. Predates the coding
  standards: odd indentation on the accessors and empty `//` comment lines.
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
  `LeftGrabbed`/`RightGrabbed` (the held `IClimbable` or null) with
  `LeftGrabPoint`/`RightGrabPoint` (the ray hit at the moment of the grab) - used by mantling.
  **Zip lines** (2026-10-03): grabbing an `IZipLine` with `IsZipLine` starts a ride
  (`BeginZipGrip()`: from rest, direction from the line; a second hand joins at the ride's
  speed). `TickZip()` (after the grabs, before `UpdateFrameMovement()`) speeds up towards
  `ZipSpeed` and slides each hand on the line - `HandVisualSnap.MoveSnapPose()` moves the
  snapped pose, and the primary hand's slide (`_zipMovement`) is added to `FrameMovement`, so
  ordinary climbing still works on top and the single `Move()` rule holds. A hand reaching
  the end is `ForceRelease()`d (needs a regrip, so it can't re-grab the rope next frame); with
  none left the player drops, from rest (no release momentum, as for all climbing). The ride
  also ends if the primary hand grabs something else (the hand left on the line is let go), or
  if the body falls more than 0.5m behind the grip (`ZipBlockedDistance`: blocked by a wall
  or the ground - checked in `ReportAppliedMovement()`).
- **`PlayerMantling`** — decides when a mantle is possible (`CanMantle`, `MantleTarget`) and
  shows the `MantleIndicator` to match: a hand grips an `IMantleable` whose own rule
  (`CanMantleFrom()`) says so - a mantleable edge with the head at least `ledge top -
  headBelowTopAllowance`, or a mantleable ladder with a hand on its top rung (2026-10-03). No
  physics queries - where the mantle lands and whether it ends crouched are per-target
  designer data (`ClimbableEdge`, `Ladder`). Either stick pushed up
  (`stickUpThreshold`) starts the mantle: `PlayerClimbing.ReleaseAll()`,
  `PlayerLocomotion.BeginMantle(endsCrouched)`, CharacterController disabled, then the rig is
  positioned directly along an eased up-and-over arc (`duration`, `riseEndsAt`,
  `forwardStartsAt`) so the capsule bottom lands on the mantle point (+`landingLift`).
  Uncancellable; `IsMantling` makes `PlayerController` skip everything else. At the end the
  controller is re-enabled and `EndMantle()` hands back control. **The one exception to the
  single-`Move()` rule** - chosen because the landing is designer-placed and collision could
  only stop it landing there.
- **`MantleIndicator`** — runtime-built white arrow on a child of Main Camera (so head-locked
  with no code). Uses an `OverlayMaterial` so it draws through walls (the face is against the
  wall while climbing). `SetVisible()` only touches the renderer on change.
- **`OverlayMaterial`** (in `Scripts/Core`, namespace `Core`, since 2026-10-03 - the screen
  fade uses it too; `using Core;`) — static factory for in-world UI marker materials (reticles,
  mantle arrow, later UI) using the project's own `TeaLeaf/Overlay` shader
  (`Assets/Art/Shaders/Resources/Overlay.shader`: flat `_Color`, `ZTest Always`, `ZWrite Off`,
  `Cull Off`, alpha blend, Overlay queue 4000, `SRPDefaultUnlit` pass, single-pass-instanced
  stereo macros; in `Resources` so `Shader.Find()` works in builds). Replaced `UI/Default` +
  a material override of `unity_GUIZTestMode` (2026-09-27): that property isn't declared in
  UI/Default's Properties, so the override did nothing and walls and hands still hid both
  markers; URP's Unlit has no depth-test property at all. **Every UI marker must use it** - the
  maintainer's rule: UI draws after everything else.

## Hands

- **`PlayerHandVisuals`** (on `Hands`) — the single owner of where each hand *visual* is placed,
  so no two systems fight over a visual transform. Holds the `leftHandVisual`/`rightHandVisual`
  references and `snapBlendDuration`, creates and ticks both `HandVisualSnap`s
  (`LeftVisualSnap`/`RightVisualSnap`), which `PlayerClimbing` snaps/releases and
  `PlayerHandAnimation` reads, plus one `HandPhysicalFollow` per hand. Per hand, per tick: the
  snap first; only if it isn't using the visual (not snapped and fully blended back) does the
  physical follow run - snapping always wins. Physical Hands settings: `collisionLayers` (must
  be Environment + Interactable; never Player/Climbable; empty = collision off), `handRadius`
  (0.035m), `skinWidth` (0.005m), `catchUpDuration` (0.1s), `maxSeparation` (0.4m), and the wrist/fingertip bones per hand. `Reset()` / the "Find
  Hand Bones" context menu find the visuals and bones by name (`J_Left_Hand`,
  `J_Left_HandMiddle4` - the mirrored right hand shares the Left names). Exposes
  `IsLeftHandInContact`/`IsRightHandInContact`, and raises `HandContactStarted(isLeftHand)` on the tick a
  hand goes from free to in contact (not on snapping; for `PlayerHaptics`). Debug capsules (`IDebugDrawable` - gizmos when
  selected, and in the headset while `InHeadsetGizmos` is on, see `Scripts/Core/CLAUDE.md`): in
  Play Mode the target capsule (faint) and the visual's (green, red in contact); in Edit Mode
  the bone capsule, for checking `handRadius`.
- **`HandPhysicalFollow`** — plain C# class, one per hand, the physical hands collision response
  (kinematic sweep, no Rigidbody). A capsule from wrist to middle fingertip (measured once from
  the bones at Awake, in controller space so the mirrored hand needs nothing special; ends inset
  by the radius) is `CapsuleCast` from last frame's visual position towards the controller's;
  hits stop it `skinWidth` short and the way *to the goal* from there is projected onto the
  surface and re-swept (collide-and-slide, max 3 sweeps, no allocations). **Re-aimed at the
  goal each sweep, never the leftover of the last move** - moving along the goal's projection
  can't take the hand further from the goal. Carrying the leftover (until 2026-09-28) slid the
  hand round a block's edge and out, as far sideways as the controller was deep (the 5cm gap
  jitter, diagnosed with `PhysicalHandsTrace`). **Creases:** if a slide would
  push back into the surface the previous sweep slid along (a V, e.g. the mouth of a gap
  narrower than the hand), the move is projected onto the line where the two surfaces meet
  instead (their normals' cross product); parallel surfaces stop it. Triggers are ignored. **Before**
  the sweep, `Depenetrate()` pushes the capsule - at last frame's position, with this frame's
  rotation - out of anything it overlaps (`OverlapCapsuleNonAlloc` into a shared static buffer,
  then `ComputePenetration` per overlap, moved by distance + `skinWidth`, max 3 passes). It runs
  before rather than after, because a rotation-induced overlap is shallow at the start, while
  after a sweep the hand could be past the middle of a thin wall and get pushed out of the far
  side. `Depenetrate()` reports `isClear`; a sweep hit at distance 0 lets the hand move freely
  **only** on the first sweep when the start couldn't be cleared (genuinely stuck inside, so it
  isn't pinned) - any other distance-0 hit is "touching" and stops the sweep (2026-09-28;
  every distance-0 hit used to let the hand through). `ComputePenetration` needs an enabled collider for the hand but takes the pose
  as arguments, so each hand builds a `Penetration Collider` (trigger `CapsuleCollider` on Z,
  `PlayerHands` layer, parked at y -1000 at the scene root, never moved, height = wrist to
  fingertip, radius synced from `handRadius` on change, destroyed from
  `PlayerHandVisuals.OnDestroy()`). Blocked →
  visual detached and placed by code (`IsPlacedByCode`). Attached state is read from the actual
  parent, since `HandVisualSnap` detaches the same transform. **Position and rotation are held
  and released separately** (`_isPositionHeld`/`_isRotationHeld`): a blocked sweep holds both;
  the position is free as soon as the sweep reaches its goal, but the rotation (held at the
  controller's rotation from the first blocked frame) only once a `CheckCapsule` at the hand's
  current position, turned to the controller's rotation, is clear (otherwise it would turn with
  its fingers in the wall and flicker). `IsInContact` = either is held. Split 2026-09-28: when
  one flag covered both, a hand free to move but not to turn stayed "in contact", and contact
  movement isn't eased, so its position jumped onto the controller while only the rotation
  eased. **Elastic band:** each of position and rotation, from the moment it comes free, eases
  its offset from the controller (stored in the controller's space so the hand keeps following
  the real hand's motion) to zero over `catchUpDuration` (0.1s, SmoothStep via
  `CatchUpWeight()`, on `PlayerHandVisuals`), still swept each frame, so it can land back in
  contact; re-attached when both are fully back. Rejected: an exponential chase of the controller (lags further the faster the
  hand moves, may never settle). Tracked by `_handRotation` (last frame's rotation, as a
  controller rotation). **Snap-back:** in contact, if the visual is more than `maxSeparation`
  (0.4m, 0 = never) from the controller, contact is dropped and the hand enters "passing
  through" (`TickPassingThrough()`): collision off, it eases back onto the controller -
  position and rotation, over `catchUpDuration`, straight through the wall - then stays on it
  (attached) until a `CheckCapsule` at the controller is clear (one query per frame, only once
  the ease is done). Was an instant jump until 2026-09-28; now every return to the controller
  is eased. Without the wait, the next push-out would move the hand to the nearest face of
  whatever the controller is inside; the near face would be too far again and it would snap
  back every frame. The eases share `AdvanceEases()`/`StartPositionReturn()`/
  `StartRotationReturn()`, and placement is `PlaceVisual()`. Controllers are assumed unscaled (verified: Player → controllers all
  scale 1). `Suspend()` while snapped/untracked.
- **`HandGhost`** (added 2026-09-30) — plain C# class, one per hand, owned and ticked by
  `PlayerHandVisuals` (after the physical follow; told it can't show while snapped). A faint
  copy of the hand at the real controller while a surface holds the visual away (Alyx style).
  Made in `Awake()` by `Instantiate`-ing the visual under the controller at its rest pose (so
  it follows tracking, including the before-render update, with no code moving it) with its
  Animator destroyed; while shown, its bones copy the visual's `localRotation`s (one frame
  behind, invisible on a ghost). Shown when the visual is more than `ghostShowDistance`
  (0.03m) from it, hidden again within half that; the renderer is only switched on changes,
  bones only copied while shown. Settings on `PlayerHandVisuals` (Ghost Hands):
  `showGhostHands` (a likely player setting later), `ghostShowDistance`, `ghostColor` (white,
  alpha 0.2, read once in `Awake()`). Drawn with **`TeaLeaf/Ghost`**
  (`Art/Shaders/Resources/Ghost.shader`): Overlay's no-depth-test flat colour (the real hand
  is usually inside the wall, so a depth-tested ghost would be hidden), queue `Overlay-1` so
  UI markers stay on top, `Cull Back`, plus a stencil test on bit 128 so each pixel is drawn
  once - without it the see-through hand darkens wherever its own triangles overlap.
- **`HandVisualSnap`** — plain C# class (one per hand, owned and ticked by `PlayerHandVisuals`;
  other systems only call `Snap()`/`Release()`). Blends a hand *visual* (never the tracked
  controller) between its rest local pose and a world-space `HandSnapPose` over
  `snapBlendDuration` (SmoothStep), both ways, and doesn't touch the transform at rest. A grab
  from rest blends **from where the visual actually is** (captured in `Snap()` in the
  controller's space, so it still follows the real hand), not from the rest pose - a surface
  may be holding it off the controller (fixed 2026-09-30: it used to jump back to the
  controller on the first frame, the "pop"). `Release()` resets that to the rest pose, so a
  release always blends back onto the controller. Exposes
  the eased blend as `Weight` (0 = following the controller, 1 = snapped), which
  `PlayerHandAnimation` uses as the finger pose layer weight so the two stay in step. While
  snapped the visual is **detached to the scene root** and re-attached when the release blend
  ends: the hands' Tracked Pose Drivers use "Update And Before Render", so they move the
  controller again after all `Update()` code, and a child visual would wobble. Anything that
  must stay world-fixed can't be a child of a tracked transform. (Not parented to the ledge -
  ledges are non-uniformly scaled, which would shear a rotated child.)
- **`PlayerHandInteraction`** — casts one ray per hand **from the hand visual, not the
  controller** (`PlayerHandVisuals.GetLeft/RightHandPose()` → `HandPhysicalFollow.GetHandPose()`:
  the controller pose shifted to where a surface holds the visual, so a controller pushed
  through a wall can't target/grab behind it and the angle offsets stay valid; just the
  controller while the visual is on it; last frame's placement, since rays run before the
  visuals tick) (configurable length, layer mask, and
  per-hand angle offset, pre-rotated into a cached local ray direction in `Awake()`/
  `OnValidate()`) and records whatever `IHandTarget` it hits, if the target accepts a ray from
  that hand (`CanBeTargetedFrom(origin)`, e.g. not from behind a ladder). **Before the ray**,
  `FindTargetContaining()` checks whether the ray origin is already *inside* a target's
  collider (a 1mm `OverlapSphereNonAlloc` into a shared buffer, triggers included, registry
  lookup): if so that target wins, targeted at the hand itself, and the ray is skipped - a ray
  never detects a collider it starts inside, and grab volumes are bigger than what they belong
  to (added 2026-09-28 so a hand against a solid rope can grab it). One extra query per hand
  per frame (nothing is highlighted -
  the reticle alone shows what can be interacted with). Exposes `LeftTarget`/`RightTarget` and
  the hit points `LeftTargetPoint`/`RightTargetPoint`; `PlayerClimbing` grabs from these.
  Ticked before climbing, so they're always this frame's. `TickReticles()` places the reticles
  later in the frame (step 8b).
- **`HandRayReticle`** — runtime-built billboard disc shown where a hand ray hits a hand target,
  hidden while that hand is holding something (its visual is snapped); ticked by
  `PlayerHandInteraction.TickReticles()`. Only toggles its renderer when visibility changes
  (same pattern as `MantleIndicator.SetVisible()`). Uses an `OverlayMaterial` (was
  `Sprites/Default` at queue 3100, depth-tested, so the hand model or surface could hide it).
- **`PlayerHandAnimation`** — per-hand Animator. The base layers always follow input
  (`TriggerCurl` index, `GripCurl` middle/ring/pinky). The `Snap Pose` override layer plays the
  snap target's `HandPose` with its weight set from that hand's `HandVisualSnap.Weight`, so any
  snapped hand (ledges, rungs, ropes now; props, tools later) gets its target's finger pose and
  fades back to input curl on release. `SetFloat()`/`Play()`/`SetLayerWeight()` only run when
  the curl, pose or weight changes; both Animators set `keepAnimatorStateOnDisable` in `Awake()`
  so those caches stay valid if a controller object is ever deactivated (XRI's Input Modality
  Manager used to, when untracked; nothing does now). The layer index is looked up by name; state hashes are built once from
  `HandPose`'s enum names. (Replaced an earlier `HandState` enum — every non-input pose is a
  snap pose, so the snap weight already says who owns the fingers.) New poses: add a `HandPose`
  value plus a same-named state and clip on the layer, no new code; `Awake()` warns once for
  any `HandPose` with no matching state.

### Hand art/animation

Real hand model (`Assets/Art/Models/hand.fbx`, Generic rig, bones like `J_Left_HandIndex1-3`).
It is a left hand; the right hand is the same model mirrored with `LocalScale.x = -1`, so both
hands share one Animator Controller (`Assets/Art/Animations/Hands/Left Hand Visual.controller`).
It has one layer per finger group, each a 1D blend tree between single-keyframe Open/Closed
pose clips: Base Layer (`TriggerCurl`, index) and Grip (`GripCurl`, middle+ring+pinky). No
Avatar Masks are used — each clip only keys its own finger bones, and a Generic-rig layer only
writes the properties its clips animate. New pose clips should likewise key only the bones they
need. On top sits the `Snap Pose` layer (Override, default weight 0, driven from code): one
state per `HandPose` value, **named exactly like the enum value** (`LedgeGrip`, `RungGrip`,
`RopeGrip`), each
holding a single-keyframe clip. A snap clip must key **every joint (1, 2 and 3) of every finger
it poses**: like the other layers it only writes the bones it keys (Write Defaults doesn't reset
unkeyed bones here), so any unkeyed joint keeps the input layers' curl - and grip is held while
climbing. The thumb (`J_Left_HandThumb1-4`) is only posed by the `RungGrip` and `RopeGrip` snap
clips (2026-09-30) - the input layers and `LedgeGrip` leave it at rest. Keep the mirrored right hand in mind for colliders and
anything handedness-dependent.

## Debug scripts

`Assets/Scripts/Player/Debug/` holds standalone debug/diagnostic MonoBehaviours (e.g.
`VRDebugInput`, `InputTest`, `TrackingTest`, `TurnInputTest`, `TurnActionTest`,
`LocomotionInputTest`, `HandRayDebug`, `MovementStateDebug`) used for manually verifying
systems in Play Mode — not part of the runtime gameplay path. `Debug/Editor/` holds editor-only
tools: **`PhysicalHandsTestArea`** (menu **TeaLeaf > Build Physical Hands Test Area**) builds
a greybox row at (4.5, 0, -5.5), facing the spawn, under one root (rebuilding asks to replace
it; undoable). Pieces: an inside corner (wall + return wall), a 2cm thin panel (tunnelling,
far-side push-out, maxSeparation snap-back), a table (top, edges, underneath) with a static
0.3m crate on Interactable, a pillar (outside corners), a 5cm gap between two blocks (narrower
than the 7cm hand), a 45° slope and a round post. **`LocomotionTestCourse`** (menu **TeaLeaf >
Build Locomotion Test Course**, added 2026-09-30) builds the same way a row 10m in front of the
spawn (root at (12, 0, 10), turned 180° so the pieces face it; locally a row along X, fronts at
z = 0 facing +Z): a 1.3m chest ledge, a 2.3m above-head ledge, a 1.1m low shelf under a
ceiling 1.2m above it (edge ends the mantle crouched), a 4m tower with a ladder (visible
Environment rails/rungs inside the `Ladder` grab box, rungs from the same
`firstRungHeight`/`rungSpacing`) and a 3.7m rope (thin Environment cylinder inside the
`ClimbableRope`) sharing one mantleable top edge, a 20° ramp to three 1m platforms with a 1.5m
(walking) and a 2.5m (sprint) jump gap, 0.45m (jumpable) and 0.6m (control) crates, a
ceiling slab at 2m to jump under, and (2026-10-03) two 5m ropes strung between posts 2.2m up
past the end of the row - one sagging 0.3m (placed by an `endPoint` child), one straight
(a `ClimbableRope` turned on its side) that is a level zip line; an angled zip line
(`BuildAngledZipLine()`) from 2.2m above the back of the tower top down 9m behind the row to
2.6m above the ground, both ends hung from an arm off a post to one side; the tower's ladder
mantles from its top rung by itself, and the tower's mantleable edge now only covers the rope
half of the lip so the two exits can be told apart - each rope (`AddVisibleRope()`) with a visible cylinder per piece of its curve. Sizes come from the player settings (see the class
comment). Every climbable is built as its setup checks expect - trigger volumes on Climbable,
larger than the solid part, colliders set up before the component is added - with its
`LedgeGrip`/`LadderRung`/`RopeGrip` profile, mantle points where "Reset Mantle Point" puts
them. **`TownTestArea`** (menu **TeaLeaf > Build Town Test Area**, added 2026-09-30) builds a
larger greybox town square (root at (0, 0, -35), behind the spawn): six flat-roofed mud-brick
style houses modelled on a reference photo of a two-storey Gulf house, big shapes only, each
from a `BuildingSpec` (size, optional tower over one end or all of it, terrace, balcony, door
position) in its own frame (front outer face at z = 0 facing +Z, turned to face the square),
plus four free-standing 2.4m compound walls and a few crates. A 3.6m ground floor (door,
auto-spaced windows) under a slab that is both lower roof and tower first floor, a 0.6m roof
parapet; the tower adds a 3.2m storey (door onto the lower roof, windows, balcony door) and
its own parapet. Doors and windows are empty openings (walls are built as the pieces around
them); steps are smooth ramps - the terrace's 20° front ramp and a ramp up the tower's outer
side wall inside, under a hole in the first floor from where headroom runs out. Ledges where a
building has them, about a metre apart so every face climbs hand over hand to the roof:
window sills (two-sided, mantled into crouched - a 1.2m window is too low to stand in),
window hoods and the band below each roofline (grab only), and parapets, balcony balustrades,
compound walls (all two-sided) and pergola roofs (mantleable; these long ones with "Move
Horizontally To Point" off, so a mantle lands straight ahead). Two-sided = one edge across
the whole top with "Grabbable From Both Sides" on (2026-10-03, replacing back-to-back pairs
whose overlapping volumes snapped the hand onto the far face). Also two-sided and mantleable
(added 2026-10-03): the balcony's two side balustrades and the terrace's low walls (front
pieces and sides; wall-top stretches under 0.5m get none). **Town edges follow the solid
geometry almost exactly** (maintainer's request, 2026-10-03): every town edge uses
`TestGeometry.TightOverhang` (1cm past the solid top and faces, not the default 5cm) and is
no taller than a thin piece (hood, band, pergola roof). Not zero: a volume flush with the
solid ties with it for hand rays and the reticle flickers. **Every mantle lands on top of
what it climbs** (mantling moves the player with collision off, so a landing beyond a wall
would drag the feet through it); the player steps or drops down after. All three builders make
their pieces through **`TestGeometry`**: `Box()`/`Primitive()` give every test piece the brown
`Assets/Art/Materials/TestGeometry.mat` (URP Lit, created by the first build if missing; edit
it in the Inspector) so test areas stand out from the grey floor; `Edge()` makes a
`ClimbableEdge` on a solid lip facing any yaw - an unscaled trigger strip on Climbable (0.2m
tall, 0.3m deep, 5cm past the solid top and face), set up before the component is added, with
the landing `landingInset` in from the lip (`TopCentreInset(thickness)` = the middle of a wall
top); `bothSidesThickness` above 0 makes it two-sided (volume across the whole top, 5cm past
both faces, "Grabbable From Both Sides" on); `overhang` (default 5cm, `TightOverhang` 1cm)
and `height` (default 0.2m) size the volume against the solid. New test-area builders should use it too.
**`PhysicalHandsTrace`** (on the Debug object, enable *before* Play - toggling it in the
headset is awkward) turns on `HandPhysicalFollow.TraceEnabled`: one log line per hand per
frame whose sweep hits a collider whose name contains `colliderNameFilter` (default "Gap") -
contact state, push-out, and each sweep's hit (collider, distance, normal), no stack trace.
Read it from `%LOCALAPPDATA%/Unity/Editor/Editor.log`. Allocates while on. The hands' cost is
profiled under the
**`PhysicalHands.Follow`** `ProfilerMarker` (one sample per hand per frame, around
`HandPhysicalFollow.Tick()` in `PlayerHandVisuals.TickHand()`). The in-headset gizmo view
(`InHeadsetGizmos`) is shared by every system, so it lives in `Scripts/Core/Debug`.

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
  *relative* height delta (never an absolute value, which would discard `Camera Offset`'s saved
  1.6m standing eye height). `UpdateCharacterControllerCentre()` only touches X/Z.
- **CharacterController settings** — Slope Limit 45°, Step Offset 0.3m (validated in a test
  scene: 0.2m step and 30° ramp climbable, 60° ramp not). Device tracking mode (set by
  `PlayerTracking`) with `Camera Offset` at 1.6m. The CharacterController is kept rather than
  replaced with a custom one (decided 2026-09-30): it runs natively inside PhysX with nothing
  unused costing anything, and a C# replacement would be slower on Quest and a large source of
  bugs.
- **Mantling** — a quick, committed move onto a *mantleable* (always horizontal)
  `ClimbableEdge`. When a hand grips one and the head has been pulled up near the top, a small
  white head-locked arrow appears; pushing up on either thumbstick then starts the mantle. It
  overrides all other locomotion (movement, turning, gravity, jump, crouch, climbing) and can't
  be cancelled, and lands the feet at the edge's designer-set mantle point (the same spot
  wherever the mantle started), crouched if the edge says so - or, for an edge with "Move
  Horizontally To Point" off, straight ahead of the player at that point's height and distance
  from the lip (`ClimbableEdge.GetMantleLanding()`, decided 2026-09-30 for long ledges). The
  mantle's rise-then-forward motion is the same either way.
- **Throwing is aimed, not physical** (decided 2026-09-29, not built yet - Phase 3) — the
  player doesn't release a held object with a throwing motion. Grip picks an object up; while
  holding it, that hand's trigger held shows a trajectory arc, and releasing the trigger plays
  a short hand visual launch animation and throws the object along the arc. Each hand throws
  what it holds with its own trigger, so either hand works and there's no clash with the
  sticks. Throw distance comes from the hand's pitch, like standard VR teleport arcs: a fixed
  launch speed, with the angle taken from where the hand points (range peaks around 45°).
  This is for accuracy (thrown noisemakers/distractions need to land where intended) and to
  avoid how awkward physical throwing feels in VR (no weight, release timing, hand-velocity
  noise). Releasing grip without aiming just drops the object. **Cancel is aim-at-nothing**,
  as with teleport arcs: pointing the hand steeply up or down, or anywhere the arc has no
  valid landing, turns the arc red/faded, and releasing the trigger then cancels instead of
  throwing. Chosen over a fast-release-throws / slow-release-cancels rule, which silently
  drops throws when a tense player eases off the trigger, gives no feedback before release, and
  needs a per-player speed threshold (could be a later secondary cancel if playtests ask).
  Letting go of grip while aiming, and face-button or stick-click cancels, were also rejected:
  they clash with grip-release-drops, or differ between hands.
- **Climbing is custom** — no XRI climb provider. Ladders and ropes reuse the
  grab-and-pull-delta approach from `PlayerClimbing` by implementing `IClimbable` (an
  interface, not a shared base class). Ladder and rope movement is unconstrained like ledges
  (ropes are static - no swinging); their top exit is a mantleable `ClimbableEdge` placed on
  the lip, or for a ladder its own mantle from the top rung (2026-10-03; ropes have none), and
  their bottom exit is letting go. A rope marked as a zip line slides the grip instead of
  being climbed (2026-10-03). Letting go of
  every grip mid-climb falls normally; no fall damage in the slice.
- **Ray-targeted grabs + hand snap poses** — climbing starts when grip is held while the hand
  ray/reticle is on a climbable (replaced the old SphereCollider overlap). On grab the visual
  hand snaps (with a short blend) to a target-defined position and rotation, and plays the
  target's finger pose (e.g. fingers curled over a ledge) on the hand Animator's `Snap Pose`
  layer. This is a general mechanism: any grab target (ledge, ladder rung, rope, door handle,
  tool, prop) supplies its own per-hand snap pose, rather than hand code special-casing each
  one.
- **Physical hands: kinematic sweep, no Rigidbody** (decided 2026-09-27) — in `Update()`/the
  tick order, a hand capsule takes its rotation at the visual's last position, is depenetrated
  there with `ComputePenetration`, then sweeps towards the controller and collide-and-slides.
  Push-out before the sweep, not after - found in headset testing 2026-09-27: a twisting wrist
  overlapped the wall at the sweep's start, so the sweep saw nothing and the hand passed
  through. Rotation follows the controller while free but is **held at the contact rotation
  while in contact** (2026-09-27, replacing "always follows the controller"): with rotation
  always following, pushing deeper tilted the wrist, the fingers swung into the wall and the
  push-out backed the whole hand away from it. Rejected: freezing the hand via the snap system
  (no sliding or collision while snapped, switches the finger pose, needs a `HandSnapPose`), and
  pivoting the rotation about the contact point (complex, awkward with several contacts). The
  visual stays a child of the controller while free and is detached while in contact (Tracked
  Pose Driver before-render constraint). Rejected: a velocity-driven Rigidbody hand (physics-rate, costlier on Quest,
  jitter-prone, needs teleports for snap turn/mantle). Being built step by step (steps in
  `DEVROADMAP.txt` Phase 1).
- **Settings will eventually move out of serialized fields** — Smooth Turn/Snap Turn, turn
  speed, snap angle, movement speed and vibration on/off are expected to become
  user-configurable options (out of scope for the vertical slice). Vibration already has its
  public switch: `PlayerHaptics.HapticsEnabled` (serialized `hapticsEnabled`, default on),
  checked once in `Pulse()` so it silences every haptic.
