# Player systems (`Assets/Scripts/Player/`, namespace `Player`)

Detail for the player systems. The root `CLAUDE.md` holds the project rules, the tick order,
the Player hierarchy and the physics layers; this file is loaded when working in this folder.
Keep it up to date with every change to these systems, like the root file.

Code that doesn't match the coding standards yet: `PlayerLocomotion.cs` (several `if (...){`
lines in `HandleTurning()` missing the space before `{`; many field comments using `///`
without `<summary>`) and `PlayerTracking.cs` (odd indentation on the accessors, empty `//`
comment lines).

## Input

- **`PlayerInputXR`** — the single source of truth for controller input. Wraps Input System
  `InputActionReference`s (grip/trigger per hand, move/turn thumbsticks, crouch, sprint) and
  caches them once per frame in `Tick()` as typed properties (`MoveAxis`, `TurnAxis`,
  `LeftGrip`, `IsLeftGrabbing`, `CrouchPressed`, `SprintPressed`, `JumpPressed`,
  `CancelPressed`, etc.), and holds the **handedness** setting: `leftHanded`
  (`IsLeftHanded`; off = lockpicks on the left hand and crossbow on the right, on = the
  other way round; read at startup by `PlayerLockpicking` and `PlayerCrossbow`). **`CrouchPressed`
  is the right stick pushed down** (`ReadCrouchStick()`: past `crouchStickThreshold` 0.7
  and further down than to either side, then not again until it is back above
  `crouchStickRearm` 0.4), or the keyboard's crouch key through the `Crouch` action.
  The `Cancel` action
  needs no wiring in the scene: it is found by name in the action map the other actions
  are in (`FindByName()`); a warning, and never pressed, if it is missing. Enables every action it reads in `OnEnable()` rather
  than relying on the asset being enabled, and caches the resolved `InputAction`s there (no
  `.action` lookups per frame). Also calls `Tick()` from its own `Update()` as a fallback so
  Debug scripts work without a full rig; `Tick()` is guarded by `Time.frameCount`, so only
  the first call each frame reads input. Gameplay code always reads input through this class
  rather than referencing Input Actions directly.
- **`PlayerHaptics`** (on the Player root) — the single way to buzz a controller:
  `Pulse(isLeftHand, amplitude, duration)`. Sends through Unity's XR device for that hand
  (`UnityEngine.XR.InputDevice.SendHapticImpulse`, channel 0), each device found with
  `InputDevices.GetDeviceAtXRNode()` and kept, looked up again only when it stops being
  valid - so a pulse creates nothing and may be sent many times a second. Not through
  `OpenXRInput.SendHapticImpulse` on an input action: that builds a control name on every
  call. **Not yet tried in the headset.** No `Update()`/`Tick()`. Right-click "Test
  Left/Right Pulse" (Play Mode) checks it. Also owns the feedback tuning: it
  subscribes to `PlayerHandVisuals.HandContactStarted` and taps that controller
  (`contactAmplitude` 0.15, `contactDuration` 0.03s), at most once per `contactCooldown`
  (0.25s) per hand. Global on/off: `HapticsEnabled` (serialized `hapticsEnabled`, default
  on), checked once in `Pulse()` so it silences every haptic.

**Input assets:** all gameplay input reads the project-owned, project-wide
`Assets/InputSystem_Actions.inputactions` (auto-enabled by Unity, every map). The `Tracking`
map holds the head and controller poses the three Tracked Pose Drivers read
(`Head`/`LeftHand`/`RightHand` + `Position`/`Rotation`/`TrackingState`: head
`<XRHMD>/centerEye*`, controllers `<XRController>{Hand}/pointerPosition`/`pointerRotation` -
the aim pose everything is tuned against - and `/trackingState`). The `Player` map has an
`XR`-group action for every Quest controller input, named by function where gameplay uses it
and by button as a placeholder where it doesn't yet:

| Action | XR binding | Used by |
|---|---|---|
| `Move` | `{LeftHand}/{Primary2DAxis}` | movement |
| `Turn` | `{RightHand}/{Primary2DAxis}` | turning, mantle (stick up), crouch toggle (stick down) |
| `LeftGrip` / `RightGrip` | `{LeftHand}`/`{RightHand}/{Grip}` | grabbing, finger curl |
| `LeftTrigger` / `RightTrigger` | `{LeftHand}`/`{RightHand}/{Trigger}` | index curl; aim/throw a held prop (`PlayerHandThrowing`); shoot the crossbow (`PlayerCrossbow`) |
| `Sprint` | `{LeftHand}/{Primary2DAxisClick}` | sprint toggle |
| `Crouch` | none on a controller | crouch toggle from the keyboard; on a controller it is `Turn`'s stick down |
| `Jump` | `{RightHand}/{PrimaryButton}` (A) | jump |
| `Cancel` | `{RightHand}/{SecondaryButton}` (B) | the universal cancel: an aimed throw (`PlayerHandThrowing`) |
| `RightStickClick` | `{RightHand}/{Primary2DAxisClick}` | unassigned placeholder |
| `ButtonX` | `{LeftHand}/{PrimaryButton}` | unassigned placeholder |
| `ButtonY` | `{LeftHand}/{SecondaryButton}` | unassigned placeholder |
| `Menu` | `{LeftHand}/{MenuButton}` | unassigned placeholder |
| `LeftHaptic` / `RightHaptic` | `{LeftHand}`/`{RightHand}/haptic` | unused (output actions; `PlayerHaptics` sends through the XR devices instead) |

Rename a placeholder to its function when it gets a job, and add it to `PlayerInputXR` then -
not before. Bind XR actions to a specific hand (`{LeftHand}`/`{RightHand}`) - a bare
`<XRController>` binding fires from either controller - and never add interactions unless
they're wanted (a Sector interaction on a stick binding reads (0, 0) unless the stick goes
straight from centre into that sector). The asset also keeps Unity's `UI` map and its
Keyboard&Mouse, Gamepad, Touch and Joystick control schemes, unused by the game: the Input
System reports an error in the editor if the project-wide actions have no `UI` map, and
that map's bindings use those schemes. `Move` and a few other `Player` actions still carry
the template's keyboard and gamepad bindings.

## Body and movement

- **`PlayerTracking`** — the single source of truth for tracked XR transforms (head, left hand,
  right hand), exposing position/rotation accessors. Other systems query this class instead
  of walking the rig hierarchy.
  - **Hand smoothing:** `SmoothHands()`, first thing in `Tick()` (tick step 1, every frame,
    calibrated or not), overwrites each controller object's tracked local pose with a
    smoothed one - so the hand visuals, hand rays, throw arc, carried props and climbing are
    all steadied with no changes of their own. It works because the hands' Tracked Pose
    Drivers write the tracked pose once per frame before `Update()` and not again before
    rendering. **`HandPoseFilter`** (plain class, one per hand) is a One Euro filter: simple
    smoothing whose cutoff frequency rises with the hand's speed, so a still hand is
    steadied and a moving one isn't lagged; position and rotation each have their own pair
    of numbers. Filtered in the controllers' parent space (tracking space), so the body
    walking or turning isn't hand movement, with `Time.unscaledDeltaTime`. **Per device**
    (`HandSmoothingSettings`, a serializable class: `enabled`,
    `positionMinCutoff`/`positionSpeedCoefficient`, `rotationMinCutoff`/
    `rotationSpeedCoefficient`): `pcvrSmoothing` (3 Hz, 40, 3 Hz, 15) and `questSmoothing`
    (5 Hz, 40, 5 Hz, 15; untuned) chosen by `smoothingTarget`: `Auto` (Android build =
    Quest, anything else = PCVR) or forced to either. Read every frame, so tunable in Play
    Mode. Nothing reads the raw tracking.
  - **Tracking mode and calibration:** `Start()` sets XR tracking to Device mode (see
    "Tracking setup" in the root `CLAUDE.md`), then - with `calibrateView` (default on) and
    XR running - starts the one-off `CalibrateWhenTracked()` coroutine:
    `ScreenFade.Instance.Hold()`, wait until the head reports a tracked position and
    rotation (`InputDevices`/`CommonUsages.trackingState`) or `trackingTimeout` (3s),
    `Calibrate()`, one more frame, `Release()`. **`Calibrate()`** puts the view right by
    moving Camera Offset (`head.parent`), never the tracked transforms: (1) if the head's up
    points below the horizon (`IsHeadUpsideDown()`), `FlipTrackingSpace()` turns Camera
    Offset half a turn about the head's level facing direction, through the head's position
    - same spot, same facing, upright - and logs the rotations it found; (2) it shifts
    Camera Offset vertically so the head is at standing eye height (Camera Offset's saved
    1.6m, captured in `Awake()`) less the game crouch's drop (standing capsule height -
    current). **`Tick()`** recalibrates whenever the head's raw local pose jumps faster than
    a head can move (8 m/s or 1500°/s, scaled by frame time) - i.e. the headset was
    recentred. Through Virtual Desktop/SteamVR the level often starts with the tracking
    space rolled 180° and its origin on the floor; `XRInputSubsystem.TryRecenter()` doesn't
    cure it. **Camera Offset can therefore be rotated and moved** - don't assume its saved
    pose.
- **`PlayerController`** — the tick orchestrator and sole owner of `characterController.Move()`
  (see the tick order in the root `CLAUDE.md`). Subscribes to `Core.LevelManager.StateChanged`
  in `Start()`: once the level has ended, the body is frozen for the end fade and only the
  hand systems tick.
- **`PlayerLocomotion`** — thumbstick movement, turning, gravity, and crouch for the
  `CharacterController`, exposed as `TickBody()`/`TickMovement(isClimbing)`/`TickTurning()`. It
  returns its movement rather than calling `Move()` itself. Movement is relative to the rig root
  (`playerTransform`), not the headset. Supports snap turn and smooth turn (`useSmoothTurn`);
  turning works while climbing. The controller's horizontal centre is re-centred under the
  headset whenever the head has drifted more than `recentreThreshold` (2mm) from it, so
  tracking jitter doesn't rewrite the physics shape every frame
  (`UpdateCharacterControllerCentre()` only touches X/Z). Exposes `IsSprinting`,
  `IsCrouching`, `MoveSpeed`/`SprintSpeed` (the full-stick speeds), `MovementState`, and a
  `Landed` event (fall speed).
  - **Momentum:** horizontal movement is one persistent `_horizontalVelocity`: set from the
    stick while grounded, kept as momentum while airborne (light air control/drag), and
    reduced to the actually-applied velocity when an airborne `Move()` hits a side.
  - **Jump** (right A; `HandleJump()`, before gravity): `_verticalVelocity` is set from a
    tunable jump *height* (`sqrt(2h·-g)`); coyote time, a jump buffer and a `_hasJumped`
    guard. Walls redirect the momentum and ceilings stop the rise (via `Move()`'s
    `CollisionFlags`, no pre-jump clearance check). **Jump while crouched only stands up**;
    a jump waits for full standing height.
  - **Sprint is click-to-toggle** (left stick click), not hold: holding a stick click while
    pushing the stick is tiring in VR. `sprintSpeed` replaces `moveSpeed`. The sprint ends
    when the stick returns to centre, on a second click, on crouch or on climb, and can't
    start while crouched.
  - **Crouch is a toggle (right stick down), not physical:** `HandleCrouch()` smoothly moves the
    CharacterController height between the standing height (captured in `Awake`) and
    `minimumHeight`, keeps `center.y` in sync, and shifts `cameraOffsetTransform` by the
    *relative* height delta (never an absolute value, which would discard `Camera Offset`'s
    saved 1.6m standing eye height).
  - **CharacterController settings:** Slope Limit 45°, Step Offset 0.3m. The
    CharacterController is kept rather than replaced with a custom one: it runs natively
    inside PhysX, and a C# replacement would be slower and a large source of bugs.
- **`MovementState`** (enum) — `Still`, `Walking`, `Sprinting`, `CrouchStill`, `CrouchWalking`,
  `Climbing`, `Airborne`, `Mantling` (set whenever a mantle is running): the single value
  noise, visibility, AI and the wrist gem read, rather than combining flags themselves.
  Set by `PlayerLocomotion.TickState()` in priority order (Climbing > Airborne > crouch >
  still > sprint/walk). "Moving" means the real horizontal applied movement exceeds
  `movingSpeedThreshold`, so pushing into a wall counts as still. `Airborne` only kicks in after
  `airborneGraceTime` off the ground, since `isGrounded` flickers on steps and slopes - except
  after a jump, which is `Airborne` immediately. Physical roomscale walking doesn't count as
  moving.
- **`PlayerFootsteps`** (on the Player root) — footsteps and landings. A step is one
  `Core.SurfaceSounds.PlayStep()`, giving the audio, the noise event (the Player root as the
  noise source) and the toe sound on heel-and-toe surfaces (its own `HeelAndToeCadence`; see
  `Scripts/Core/CLAUDE.md`). `Tick(appliedMovement)` (tick step 7b) adds up the real
  horizontal distance moved and steps each time it reaches the current gait's `stride`,
  keeping the remainder. Three serialized `Gait`s (`stride`, `noiseScale`, `volumeScale`):
  walking (1.4m, 1, 1), sprinting (1.6m, 1.6, 1), crouching (1.2m, 0.4, 0.5), picked from
  `MovementState`. `Still`/`CrouchStill` keep the distance so far (dropping it would let
  short bursts of movement cross a room silently); `Airborne`/`Climbing` zero it.
  **Creeping is quieter**: each step's noise and volume are also scaled by
  `Lerp(slowestStepScale (0.4), 1, speed fraction)`, where the speed is the average over the
  stride (distance / time spent moving, `_timeSinceStep`) against the gait's full speed
  (`MoveSpeed`, or `SprintSpeed` when sprinting; crouch moves at walk speed). So loudness to
  guards = the surface cue's noise radius x stance x speed. The surface is found only when a
  sound is made: one ray down from 0.3m above the capsule's bottom (`groundLayers`,
  Environment + Interactable; `SurfaceTag.TryFindBelow()`); a miss reuses the last surface.
  **A landing is the surface's step sound played loud, twice** (no separate landing cue): on
  `PlayerLocomotion.Landed`, strength = fall speed / `referenceLandingSpeed` (3 m/s, about a
  flat jump) clamped to `minLandingScale`-`maxLandingScale` (0.5-2); the first foot plays at
  once and the second `secondFootDelay` (0.1s) later (`Play()`'s `delay`), both at volume x
  `landingVolumeScale` (1.25) x strength, with noise scale 0. The noise is emitted once,
  directly through `NoiseSystem.Emit()`, as `NoiseType.Landing` with radius = the cue's noise
  radius x `landingNoiseScale` (1.5) x strength. Sounds play 0.1m above the feet so they're
  inside the sound room being stood in. Needs `surfaceSounds` assigned
  (`Assets/Data/SurfaceSoundsPlaceholder.asset`); silent without it or without a
  `SoundPlayer`.
- **`PlayerVisibility`** (on the Player root) — how easy the player is to see: `Visibility`
  0 (hidden) to 1 (plain sight), the one value guards' vision and the wrist gem read.
  `Tick()` (tick step 7c, and during a mantle) averages `Core.SceneLight.LevelAt()` at the
  head (Main Camera) and the capsule's centre (`LightLevel`, exposed for debug) - **sampled
  every `sampleInterval` (0.1s), not per frame**, since each sample is a few physics rays -
  then every frame multiplies by stance from `MovementState` (`crouchScale` 0.6 for both
  crouch states, `sprintScale` 1.3), clamps to 0-1 and eases `Visibility` towards it at
  `changeSpeed` (4 per second) so a shadow's edge fades rather than flickers. Stance
  multiplies, so full shadow stays 0 even when sprinting. Moonlight (0.4) gives 0.4 standing
  and 0.24 crouched: how far away each can be seen from is the guards' vision to decide.
  **`VisibilityDebug`** (`Player/Debug`, on the Debug object) - a head-locked gauge drawn
  through `DebugLines` (needs `InHeadsetGizmos` on): a frame with a bar that fills with
  `Visibility` (blue to yellow) and a tick under it at the raw `LightLevel`; logs each time
  the value moves into a different tenth.

## What each hand is busy with

**A hand does one thing at a time**, and seven systems can have a hand: climbing, carrying,
doors (handles and bolts), the lockpicks, the keys, holding the pack and the crossbow.

- **`PlayerHandState`** (on `Hands`) — the one place that knows what each hand is busy
  with. **It keeps no state**: each system owns what its hands are doing and says so
  through its own flags (`PlayerClimbing.IsLeftHandGripping`,
  `PlayerHandHolding.IsLeftHolding`, `PlayerHandDoors.IsLeftOnDoor`,
  `PlayerLockpicking.IsLeftBusy`, `PlayerKeys.IsLeftBusy`, `PlayerPack.IsLeftBusy`,
  `PlayerCrossbow.IsLeftBusy`, and the right-hand ones); this
  class reads them when asked, so an answer is as fresh as the systems that have ticked so
  far this frame. `HandUse` (enum: `None`, `Climbing`, `Carrying`, `Door`, `Lockpicks`,
  `Keys`, `Pack`, `Crossbow`); `GetUse(isLeftHand)` / `LeftUse` / `RightUse`; **`IsBusyExcept(isLeftHand,
  asker)`** - whether any system *other than the asker* has the hand, which is what every
  hand system asks before taking one. Every system it reads is optional, found in `Awake()`.
  Each hand system gets it through `PlayerHandState.GetOrAdd(playerHandVisuals)` in its own
  `Awake()`, which adds the component to the Hands object if the scene has none.
  **Adding a hand system:** a `HandUse` value, a line each in `GetUse()` and
  `IsBusyExcept()`, the system's own busy flags, and one `IsBusyExcept()` call in it -
  nothing in the other systems.
- **`HandStateDebug`** (`Player/Debug`, on `Hands`) — logs each change of either hand's
  `HandUse` to the Console ("Left: None -> Climbing").

## Carrying props

- **`PlayerHandHolding`** (on `Hands`; optional everywhere - `PlayerController`,
  `PlayerClimbing` and `PlayerHandInteraction` find it in `Awake()` and work without it) —
  picks up, carries and drops `Interaction.Grabbable`s. `Tick()` (tick step 4a): a free hand
  whose grip is held picks up the `Grabbable` its hand ray is on ("held" grabbing, like
  climbing); letting go of grip drops it from rest, or throws it if the hand was moving.
  `LeftHeld`/`RightHeld` expose the prop; `IsLeftHolding`/`IsRightHolding` are its busy
  flags; `PlayerHandInteraction.TickReticles()` hides a carrying hand's reticle.
  - **Pick-up: the hand goes to the prop, then both come back.** The prop goes kinematic at
    once and stays where it lies while that hand's `HandVisualSnap` is snapped to the prop's
    `GetSnapPose()` over `reachDuration` (0.12s, `isReaching`). When the snap's `Weight`
    reaches 1, `FinishReach()` (in `TickHeld()`) calls `Grabbable.BeginCarry()` (from here
    the prop is out of physics and pushes nothing; props resting on it are woken and fall),
    parents the prop to the visual and releases the snap over `returnDuration` (0.18s), so
    the prop rides the release blend back to the controller. Letting go mid-reach drops the
    prop where it lies and releases the snap. The return is the snap's blend, so it isn't
    collision-swept (the physical follow, with the held shape, takes over once it ends).
    Picking up passes `PlayerTracking.HeadPosition` to `GetSnapPose()` (a cylinder prop is
    gripped on the player's side).
  - **The prop follows the hand visual, not the controller, as a child of the visual**:
    `SetParent(visual, true)` when the reach ends, back to its original parent and local
    scale on drop. Nothing places it per frame. The right visual is mirrored (x scale -1):
    parenting with the world pose kept gives the prop a mirrored local scale that cancels
    it, so it isn't drawn mirrored.
  - **Finger pose while carrying:** the reach's snap brings the profile's `HandPose` in, and
    `LeftPoseWeight`/`RightPoseWeight` (1 while a prop is in the hand, easing to 0 over
    `poseReleaseDuration` 0.1s after a drop) keep it on - `PlayerHandAnimation` uses the
    larger of that and the snap's `Weight`. A prop with no profile shows `LedgeGrip`.
  - **Physical throw:** `TickThrowSamples()` (in `TickHeld()`) records, per hand, the
    carried prop's middle (`centreInVisual`, a point in the hand visual's space, set in
    `FinishReach()` - so a wrist flick counts, and the mirrored right hand is handled), the
    visual's rotation and the time, **relative to the rig** (a snap turn would otherwise
    read as a huge throw), in a 32-slot ring made once per hand. No samples while reaching
    or while the snap's `Weight` is above 0. On letting go, `TryGetThrow()` takes the newest
    sample and the oldest within `throwSampleWindow` (0.1s): velocity = distance / time,
    spin from the turn between the two rotations. Slower than `throwMinSpeed` (1 m/s,
    relative to the body) is a drop from rest. Otherwise the prop leaves with that velocity
    x `throwStrength` (1) turned into the world, plus `CharacterController.velocity`, capped
    at `throwMaxSpeed` (12 m/s). Measured from the **visual hand, not the controller**: a
    hand held against a wall throws nothing however the controller moves. `Drop(hold,
    canThrow)`: only a grip release can throw - `OnDisable` and a destroyed prop drop from
    rest.
  - **A dropped prop stays on the PlayerHands layer until it's clear of the hand**
    (`released`, `TickReleased()` in `TickHeld()`): its layers go back
    (`Grabbable.RestoreLayers()`) once its centre is `HoldRadius + releaseClearance` (0.12m)
    from the hand visual, after `releaseTimeout` (0.5s), or when that hand picks up
    something else.
  - `TickHeld()` (tick step 8a, on every path through `Update()`) finishes reaches, samples
    throws and restores released props. `TryPickUp(isLeftHand, grabbable, grabPoint)` picks
    a prop up without the hand's ray being on it (for the pack); false if the hand is busy.
    `TryGetCarriedCentre()` gives the carried prop's middle (picked up, snap weight 0, not
    launching). `OnDisable` drops both hands, unless the scene is unloading.
- **Held shape** — so a carried prop stops at surfaces with the hand:
  `PlayerHandVisuals.SetHeldShape(isLeftHand, centreFromVisual, radius)`/`ClearHeldShape()` →
  `HandPhysicalFollow.SetHeldShape()` swaps the swept capsule (wrist to fingertip, hand
  radius, ends inset) for one from the wrist to the middle of the prop with the prop's
  `holdRadius`, ends not inset, resizing the penetration collider (on pick-up and drop only).
  One round shape for hand and prop together is approximate, but the whole collide-and-slide
  runs unchanged. `ShapeRadius(handRadius)` is the radius in use (the debug capsules draw
  it). `PlayerHandVisuals.LeftHandVisual`/`RightHandVisual` expose the visuals, read-only.
- **`PlayerHandThrowing`** (on `Hands`; optional) — aimed throwing, for accuracy (thrown
  distractions need to land where intended, which a physical throw is poor at in VR).
  `Tick(canAim)` runs straight after `PlayerHandHolding.TickHeld()` (tick step 8a) on every
  path through `Update()`; `canAim` is false during a mantle and once the level has ended
  (nothing new is aimed, a launch under way still finishes).
  - **Aiming:** a hand with a prop fully in hand starts aiming when its trigger passes
    `aimStartTrigger` (0.6) and throws or cancels when it falls to `aimEndTrigger` (0.3) -
    two values so a hovering finger can't flicker. Direction = **straight out of the
    controller** (its forward axis, from `PlayerHandVisuals.GetLeft/RightHandPose()`), like
    a menu pointer - not the hand rays' direction, which is angled out from the palm for
    grabbing. Speed fixed (`launchSpeed`, 9 m/s), so distance comes from the hand's pitch.
  - **The arc** (`ComputeArc()`, every frame of aiming): starts `launchReach` (0.15m) along
    the throw from the prop's middle - where the launch move will let go. The path itself
    is the shared **`BallisticArc`** (static; also the crossbow's): `Compute()` finds the
    landing and fills the points, `Point()` is one moment on the path. The landing is
    found with one `Physics.Linecast` per `arcCastTimeStep` (0.1s) of flight - at most 25 -
    up to `arcMaxTime` (2.5s), on `arcLayers` (Default, Environment, Interactable, Guard;
    triggers ignored), stopping at the first hit. The line is then drawn from the formula
    alone, in `arcSegmentLength` (0.1m) pieces up to the landing time, ending exactly on the
    hit point. A throw's points use **Unity's stepped gravity, not the textbook
    formula**: `0.5 * g * t * (t + fixedDeltaTime)` (`BallisticArc`'s `fixedStep`; the
    crossbow passes 0, since its bolts aren't moved by physics). The arc is cast with rays, not the
    prop's shape, so a wide prop can clip something the line clears.
  - **Cancel is the cancel button** (`PlayerInputXR.CancelPressed`, right B): the aim ends,
    the prop stays in the hand, and that hand can't aim again until its trigger is let out
    (`isCancelled`). One button for both hands. No pitch limits. The arc is still invalid
    (red/faded; releasing the trigger throws nothing) when it hits nothing in time or the
    launch move's path is blocked (drawn as a stub). Letting go of grip while aiming is
    just `PlayerHandHolding`'s drop or physical throw.
  - **Launch** (`BeginLaunch()`): a procedural move, not a clip - the hand's
    `HandVisualSnap` is snapped `launchReach` forward along the throw over `launchDuration`
    (0.08s) with its current rotation and finger pose, `PlayerHandHolding.BeginLaunch()`
    makes the hand ignore grip, and when the snap's `Weight` reaches 1
    `ReleaseLaunched(velocity, spin)` lets the prop go with exactly the arc's velocity (no
    body velocity added - it must land where shown) and `launchSpin` (5 rad/s, end over
    end); the snap is released over `launchReturnDuration` (0.2s). **After an aimed throw
    the hand stays busy until grip is let go** (`isWaitingForGripRelease`;
    `IsLeftHolding`/`IsRightHolding` stay true), or the still-held grip would pick up or
    climb whatever the ray was on.
- **`ThrowArc`** — the arc's display, one per hand, made at runtime by
  `PlayerHandThrowing.Awake()` (`ThrowArc.Create()`, children of `Hands`): the arc and a
  landing disc laid on the hit normal, all `OverlayMaterial`. White when valid; red, faded
  and no disc when not. `Show()`/`Hide()` only touch renderers and the material colour when
  something changes. **Two styles** (`ThrowArc.Style`, `PlayerHandThrowing.arcStyle`; passed
  to `Show()` every frame, so it can be switched in the Inspector while aiming): `Line`, a
  world-space `LineRenderer`; `Dots`, one dynamic mesh of up to 256 octagons (`MaxDots`),
  `dotRadius` 0.012m, one every `dotSpacing` 0.12m of distance along the path, each turned
  to face the head. The dots' object is at the scene root at the origin, so mesh vertices
  are world positions; its triangles are set once, its vertex array is made once and
  rewritten each frame of aiming (unused dots collapsed to a point), and its bounds are huge
  so they never need recalculating. `OnDestroy()` removes the root object, the mesh and the
  materials.
- **`GrabbableTestProps`** (`Debug/Editor`, menu **TeaLeaf > Add Grabbable Test Props**) — a
  table 1.2m ahead of the main camera with a cube, a bottle and a crate, each a
  Rigidbody + `Grabbable` + `ImpactNoise` (placeholder impact cue and surface sounds, from
  `Core.PlaceholderSounds`' paths) on Interactable, Rigidbody on `Interpolate`. The cube and
  crate have no grip point or profile (held by the middle). The bottle (`BuildBottle()`) is
  an unscaled root at the middle of the body with a `Body` cylinder (8cm x 18cm, box
  collider so it stands) and a `Neck` cylinder (3cm x 8cm) as children, and has a cylinder
  grip and the `BottleHold` profile (`Assets/Data/`, loaded by path; a warning and no
  profile if it's missing). `MakeGrabbable()` is reused by the loot test props.

## The pack

- **`PlayerPack`** (on `Hands`; optional - `PlayerController` finds it in `Awake()` and
  works without it; needs `PlayerHandHolding`) — the player's half of `Inventory.Pack` (the
  design and the pack itself: `Scripts/Inventory/CLAUDE.md`).
  - **Bringing it out** (`TickSummon()`, tick step 3b, before the grab systems): **a hand
    reaching over a shoulder and gripping**, either hand. The shoulder is a ball of
    `shoulderRadius` (0.2m) at `shoulderOffset` ((0.2, -0.05, -0.12): right, up, ahead)
    from the head, and its mirror image on the left, measured with the head's tilt ignored
    (`HeadYaw()`); either hand may reach either one; the controller's position is what is
    tested. A free hand (`PlayerHandState.IsBusyExcept(..., HandUse.Pack)`) arriving there
    feels a tap, and on the frame its grip is pressed (pressed, not already held) the pack
    comes out on it: `PlaceOnHand()` makes the pack a child of that hand's visual at
    `packPosition` / `packRotation` ((0.14, -0.2, 0.08) and (90, 0, 0): the board in front
    of a hand held thumb up, facing back at the player), with x scale -1 under the mirrored
    right visual so the pack isn't drawn back to front. **It stays out while that grip is
    held**; letting go puts it away (`ClosePack()`). `IsLeftBusy`/`IsRightBusy` are what
    `PlayerHandState` reports as `HandUse.Pack`. The pack is first placed in `Start()`,
    not `Awake()` (the ghost hands copy the hand visuals in `PlayerHandVisuals.Awake()`).
    All of it untuned and not yet tried in the headset. `IDebugDrawable`: the two shoulder
    balls, in the detailed view (yellow, green while the pack is out).
  - **Putting loot in:** it doesn't change how props are carried; `Tick()` (tick step 4a,
    straight after `PlayerHandHolding.Tick()`) compares what each hand holds with last
    frame, and loot a hand has just let go of within the pack's reach (`Pack.IsInReach()` of
    the loot's middle, not the hand) is offered to `Pack.TryStore()`: a pulse and `storeCue`
    if it went in, a longer rougher pulse for `NoRoom` (it then just drops). **Thrown loot
    never goes in**: after an aimed throw `IsLeftHolding` stays true until grip is let go,
    and an empty hand that's still "holding" is taken as a throw.
  - **Choosing the space** (`TickHover()`): each frame a hand carries loot with the pack
    out, `Pack.Hover()` lights the space the loot is over if it's free, with a light tap
    each time a different space lights; one hand a frame, and
    `Pack.ClearHover()` when neither lights one. Hover and store measure the same point, so
    the lit space is the one it goes into.
  - **Taking out** (`TickTaking()`, **the hand not holding the pack**): empty
    hand, grip held, ray on a `PackSlot` → `Pack.BeginTake()`; when the pack hands the
    full-size item over (`TryPopTaken()`), the hand picks it up through
    `PlayerHandHolding.TryPickUp()`; if grip was let go meanwhile or the hand isn't free it
    goes back in (`PutBack()`, which drops it as a last resort). No busy flag for the other
    hand systems: while the item grows the hand's ray target is a pack space, which none of
    them act on.
  - **Keys:** a carried prop that isn't loot is asked of `Inventory.Key.Find()` too; a key
    held near the pack lights the keyring's space (`Pack.HoverKey()`) and let go of there
    goes onto the keyring (`Pack.TryStoreKey()`), with the same pulse and sound as loot.
  - `TickHeld()` (with the carried props, tick step 8a, every path): `Pack.Tick()` while
    open, and the worth coins. The carried loot's middle comes from
    `PlayerHandHolding.TryGetCarriedCentre()` (a prop under the mirrored right hand can't be
    trusted for it).
- **`LootWorthMarker`** — plain class, one per hand, made at load by `PlayerPack`: up to
  three coin discs in a row, shown over loot a hand carries (`Loot.CoinLevel`),
  `coinHeight` (0.05m) above its hold radius, facing the head. One shared disc mesh and one
  `OverlayMaterial`. Renderers and positions are only touched when the number of coins
  changes.

## Keys

- **`PlayerKeys`** (on `Hands`, needs `PlayerPack` there; optional) — the player's half of
  `Inventory.Keyring` and `Interaction.KeyLock`. The pack and keyring are found in
  `Start()` (`PlayerPack.Pack`). The keyring is in one of three places (`RingPlace`):
  **in the pack**; **in a hand** (`RingPlace.InHand`, `_isCarrierLeft`) - taken the usual
  way by either hand that is free, so not the one holding the pack (grip held, ray on the
  pack's keyring `PackSlot`; `Pack.TakeKeyring()`), a child of that hand's visual at
  `inHandPosition` / `inHandRotation` (untuned; the same numbers give the mirror-image
  place in either hand) while grip is held, back to the pack when it's let go; or **in a lock** - within `insertDistance`
  (0.15m) of a `KeyLock.FindInRange()` lock whose `KeyId` the ring `Has()`, it snaps onto
  the lock's face (`Insert()`; a child of the lock, the fitting key's colour shown going
  into the door) and the carrying hand needs a regrip. **A lock the ring has no key for**
  buzzes the carrying hand once (`_refusedBy`, re-armed away from every lock).
  **Turning** (`TickHandAtKey()` in `Tick()`, tick step 3b after the lockpicks'): either
  hand, free and holding grip within `keyReach` (0.1m) of the key's grip point (by
  reaching, as for the lockpicks; a tap on coming into reach; one hand at a time) takes it
  and its visual snaps on. `TickHeld()` (tick step 8, every path): the key's `_turn` is the
  **wrist's twist** about the lock face's Z since it took hold (`TwistAboutZ()`, as a door
  handle; the face's Z points away from the player, so positive is anticlockwise as they
  see it), only in the unlocking direction (`KeyLock.TurnsAnticlockwise()`), clamped to
  `unlockTurn` (90°); the keyring is shown turned by it and the snap pose turns with it
  (`snapProfile`: the door handle's, a stand-in). At the full turn: a pulse,
  `KeyLock.Unlock()`, and `ReturnRing()` - the keyring goes back to the pack. Let go of
  sooner, the key springs back at `returnSpeed`; a controller over `breakDistance` (0.3m)
  from the key lets go by force. The head more than `leaveDistance` (1.5m, level) from the
  lock, or the lock opened another way, also returns the ring. `IsLeftBusy` /
  `IsRightBusy` are what `PlayerHandState` reports as `HandUse.Keys`.

## Lockpicking

- **`PlayerLockpicking`** (on `Hands`; optional) — the player's half of
  `Interaction.BigLock`/`PickableLock` (see `Scripts/Interaction/CLAUDE.md`): the picks and
  the hands; the puzzle is the big lock's. The game doesn't pause.
  - **The picks** are one object (`Lockpicks`: two thin boxes in one mesh, tips at its
    origin, handles along its Z, no collider), made in **`Start()`** - not `Awake()`,
    because `PlayerHandVisuals.Awake()` copies each hand visual for its ghost and would copy
    picks already on it. **Which hand wears them is the handedness setting**
    (`PlayerInputXR.IsLeftHanded`: off, the left wears them and the right takes them; on,
    the other way round; read at startup). `_taker` is the hand that takes and carries. Always in one of three
    places (`PicksPlace`): **on the wearing hand** (child of its visual at
    `onHandPosition`/`onHandRotation` - the same numbers give the back of either hand,
    since the right visual is the left mirrored), **in the taker's hand** (child of its
    visual at `inHandPosition`/`inHandRotation`) or **in a lock** (child of the `PickableLock`, tips `insertDepth` inside the face, handles
    straight out). `PlacePicks()` always sets the local scale to one, since the right hand
    visual is mirrored. **The hand visual's axes**: fingers along -Y, back of the hand -X,
    thumb side +Z, origin near the wrist.
  - **Taken by reaching, not by the hand rays** (they're on or just in front of the player's
    body): no reticle; a light tap (`reachAmplitude`) says a hand has come within reach.
    `Tick()` (tick step 3b, before climbing, so a hand that takes a pick is already busy for
    the systems after it): on the wearing hand, the free taker within `takeDistance`
    (0.12m) takes them **on the frame grip is pressed**; carried, letting go of grip puts
    them back, and within `insertDistance` (0.15m) of a lock that `CanBePicked`
    (`PickableLock.FindInRange()`, each frame while carried) they go in (`Insert()`): the
    taker needs a regrip, and the big lock is shown `bigLockDistance` (0.2m) out from
    the real lock's face on the head's side, `bigLockBelowHead` (0.3m) below the head but
    never lower than the real lock, facing the same way as the door. In a lock, a free hand
    holding grip within `pickReach` (0.1m) of its own pick's grip point takes it (left hand
    the left pick, right the right - **never mirrored**, whichever hand wears the picks)
    and its hand visual snaps onto it.
  - `TickHeld()` (tick step 8, with the doors', on every path through `Update()`): gives up
    if the lock has gone or opened another way, or the head is more than `leaveDistance`
    (1.5m, measured level) from it; else per held pick `BigLock.TurnPick(controller
    position)`, the snap pose moved onto the pick (`SetSnapPose()`), and a hand whose
    controller is over `breakDistance` (0.3m) from the grip point is let go by force (regrip
    needed; the right hand's letting go resets the lock); then `BigLock.Tick()` (also while
    it fades out), and the pin feel. `StopPicking()` (leaving, or the unlock): both hands
    off, `BigLock.Hide()`, picks back on the wearing hand.
  - **Haptics:** right hand on inserting and at each stop; left hand as a run of pulses
    every `pinPulseInterval` (0.05s, each 1.5x that long) from `pinMinAmplitude` to `pinMaxAmplitude` by
    `BigLock.PinNearness`, and a strong one when a pin sets; both on the unlock.
  - `IsLeftBusy`/`IsRightBusy` (true through a needed regrip, and while the taker
    carries the picks) are what `PlayerHandState` reports as `HandUse.Lockpicks`.

## The crossbow (first version; not yet tried in the headset)

- **`PlayerCrossbow`** (on `Hands`; optional - `PlayerController` finds it in `Awake()` and
  works without it; **it must be added to the `Hands` object by hand**) — the hand
  crossbow, always worn on the back of one hand: the right for a right-handed player
  (`PlayerInputXR.IsLeftHanded`), so never the hand the lockpicks are on.
  - **The model** (`BuildCrossbow()`, in `Start()` - after the ghost hands have copied the
    visuals): a stock, a bow across the front and a wheel at the back as boxes in one mesh
    from `Interaction.LockMeshBuilder`, `TeaLeaf/LockFade` solid, no collider; its own Z is
    the way it shoots. A child of the hand visual at `onHandPosition` ((-0.035, -0.05, 0)),
    pointing along the fingers (the visual's -Y) with its top away from the back of the
    hand (-X), then turned by `onHandTilt`. A `Loaded Bolt` child lies on the stock while
    it is wound.
  - **Becoming active** (`Tick()`, tick step 4a, after every grab system): on the frame
    grip is pressed on the crossbow hand, if the hand is free
    (`PlayerHandState.IsBusyExcept(..., HandUse.Crossbow)`), its ray target is null (**a
    grab always wins**) and the palm faces the floor - the visual's +X, turned into the world with `TransformVector()`
    so the mirrored right hand's palm comes out the right way (`TransformDirection()`
    ignores the mirroring), within `palmDownAngle` (50°) of straight down. It stays active until grip is let go, however
    the hand is turned. `IsLeftBusy`/`IsRightBusy` are what `PlayerHandState` reports as
    `HandUse.Crossbow`, so nothing else takes the hand meanwhile.
  - **The arc** (`TickHeld(canAim)`, tick step 8a): from `muzzleDistance` (0.1m) ahead of
    the stock along the crossbow's own forward - what is shown is what is shot - through
    `BallisticArc.Compute()` with no stepped-gravity allowance, drawn by a `ThrowArc`
    (`Crossbow Arc`). **Speed comes from `range`** (40m: how far a bolt carries over level
    ground at 45°): `speed = sqrt(range x gravity)`, worked out once in `Awake()`, with the
    longest flight (1.5 x the time to reach the range) and the points array. Red with no
    landing disc when it would come down on nothing; it can still be shot.
  - **Shooting:** on the trigger's **press** (`fireTrigger` 0.6; it must fall to
    `rearmTrigger` 0.3 before it counts again, and a trigger already held when the
    crossbow comes up doesn't shoot). Clockwork: `reloadTime` (1s) after each shot before
    the next; a pull while it winds is used up and does nothing. Optional `fireCue` and
    `impactCue`, through `SoundPlayer`.
  - **Bolts:** a pool of `boltCount` (8) made at load under a `Crossbow Bolts` object at
    the scene root; the oldest is reused. **Not physics objects**: each is moved along
    `BallisticArc.Point()` from where and how fast it left, so it lands where the arc
    showed, with one `Physics.Linecast` a frame along its move in case something has come
    into its way. It stops where it lands and is put away after `boltLifetime` (30s); one
    that lands on nothing is put away at the end of its flight. Bolts tick on every path
    through `Update()`.
  - **Not built:** bolt types and their effects and models, ammunition (every shot is
    free), choosing the type by turning the wheel with the other hand, any effect of a
    bolt on what it hits.

## Opening doors

- **`PlayerHandDoors`** (on `Hands`; optional) — the player's half of
  `Interaction.Door`/`DoorHandle`/`DoorBolt` (see `Scripts/Interaction/CLAUDE.md`). `Tick()`
  (tick step 4a, after climbing and carrying): a free hand whose grip is held takes the
  `DoorHandle` its ray is on, if no other hand is on that door - the hand visual snaps onto
  the lever on the head's side; letting go of grip lets go. `IsLeftOnDoor`/`IsRightOnDoor`
  (true through a needed regrip; they cover a hand on a bolt too) are what `PlayerHandState`
  reports as `HandUse.Door`. The reticle is hidden by the snap (`IsSnapped`), as for a
  ledge.
  - `TickHeld()` (tick step 8, after `Move()`, straight before `PlayerHandVisuals.Tick()`,
    on every path through `Update()`), per held handle: **twist** = how far the controller
    has turned about the spindle (door-local Z) since it took hold, both rotations taken
    relative to the door so a swinging door isn't a twist (`TwistAboutZ()`: twice the
    arctangent of the quaternion's z over w); the lever shows it up to its stop -
    `Door.UnlatchTwist` (60°), or `LockedTwist` (10°) on a door that is locked or bolted
    (`Door.IsHeldShut`). At the stop an unlocked door is unlatched (haptic click); a held-
    shut one rattles once (haptic knock), re-armed when the lever is turned half way back.
  - **Swing** (once unlatched, or at once on an already open door): each frame the change in
    the controller's bearing round the hinge (`Door.TryGetBearing()`, `Mathf.DeltaAngle`) is
    added to `swingAngle`, which is what the door is asked for - so the door turns as far as
    the hand went round, at any distance from the hinge. The asked-for angle is kept when
    the door can't follow (its limit, or the player's body), up to 45° past the limit: the
    hand has to come back that far before the door moves again. Then the snapped pose is
    moved onto the handle (`HandVisualSnap.SetSnapPose()`).
  - **Break-away:** the real hand more than `breakDistance` (0.4m) from the grip point lets
    go by force, and the hand needs a regrip.
  - **Sliding bolts:** a free hand holding grip with its ray on an `Interaction.DoorBolt`
    takes it the same way (`TakeBolt()`; one hand per bolt) and its visual snaps onto the
    knob. In `TickHeld()` (`TickHeldBolt()`) the bolt's slide is `bolt.SlideAt(controller
    position)` plus the offset noted at the grab - so it moves as far as the hand does along
    the door, from where it was - with the unlatch haptic as it reaches an end; the snap
    pose follows the knob; the same `breakDistance`. Letting go (`LetGoOfBolt()`) lets the
    bolt settle.
- **`PlayerBodyPushing`** (on the Player root, with the `CharacterController`) — the body
  pushes open doors. `OnControllerColliderHit()`, which Unity calls from inside `Move()` (so
  no `Tick()`: it happens at tick step 6): looks the collider up in
  `Interaction.HandPushRegistry` and pushes it straight into the surface, level, by
  `squareness x pushSpeed (1.5 m/s) x deltaTime`, where squareness is how directly the
  body's move goes against the surface's normal (below `minSquareness` 0.2, or standing on
  it: nothing). `ControllerColliderHit.moveLength` is the distance travelled *before* the
  hit, not what was left, which is why a set speed is used. The body is stopped for the one
  frame, then walks into the space. A hand and the body share the door's one push a frame.
- **`PlayerKeyholes`** (on the Player root; optional) — the player's half of
  `Interaction.DoorKeyhole`. `Tick()` (tick step 7d, and during a mantle): every
  `searchInterval` (0.2s) asks `DoorKeyhole.FindInRange()` for the keyhole nearest the head
  (`PlayerTracking.HeadPosition`; distance checks only, no physics); the one found is then
  ticked every frame so its opening follows the head, until it reports it's back at rest.
  Switching to a nearer keyhole shuts the old one at once. With none in range it only
  counts down a timer.

## Climbing and mantling

- **`PlayerClimbing`** — custom grab-and-pull climbing. A hand grabs the `IClimbable`
  (ledge, ladder or rope) its hand ray is on (`PlayerHandInteraction.LeftTarget`/
  `RightTarget`) while grip is held, so what the reticle is on is what gets grabbed and the
  ray length (`rayLength`, the short one) is the grab reach; the most recent grab becomes
  the primary hand, which drives movement (hand-off to the other hand on release). Hand
  deltas are measured in `playerTransform` local space to avoid a feedback loop, and any
  movement the CharacterController didn't apply is retried via `ReportAppliedMovement()` so
  the grab point never drifts. On grab it asks the target for a `HandSnapPose` and hands it
  to that hand's `HandVisualSnap` (`PlayerHandVisuals.LeftVisualSnap`/`RightVisualSnap`).
  `ReleaseAll()` force-releases both hands (used by mantling); a force-released hand can't
  grab again until its grip is let go, since "held" grabbing would otherwise instantly
  re-grab. Sets `characterController.minMoveDistance = 0` so slow hand movement isn't
  swallowed. Exposes `IsClimbing`, `IsLeftHandGripping`/`IsRightHandGripping`,
  `FrameMovement`, and `LeftGrabbed`/`RightGrabbed` (the held `IClimbable` or null) with
  `LeftGrabPoint`/`RightGrabPoint` (the ray hit at the moment of the grab) - used by
  mantling. Ladder and rope movement is unconstrained like ledges; the bottom exit is
  letting go, which falls normally. **No climb release momentum**: letting go drops the
  player from rest. Don't add a launch - averaged over the release it is a brake-then-kick
  stutter, and at the last frame's velocity it barely launches at all.
  - **Zip lines:** grabbing an `IZipLine` with `IsZipLine` starts a ride (`BeginZipGrip()`:
    from rest, direction from the line; a second hand joins at the ride's speed).
    `TickZip()` (after the grabs, before `UpdateFrameMovement()`) speeds up towards
    `ZipSpeed` and slides each hand on the line - `HandVisualSnap.MoveSnapPose()` moves the
    snapped pose, and the primary hand's slide (`_zipMovement`) is added to `FrameMovement`,
    so ordinary climbing still works on top and the single `Move()` rule holds. A hand
    reaching the end is `ForceRelease()`d; with none left the player drops, from rest. The
    ride also ends if the primary hand grabs something else (the hand left on the line is
    let go), or if the body falls more than 0.5m behind the grip (`ZipBlockedDistance`:
    blocked by a wall or the ground - checked in `ReportAppliedMovement()`).
- **`PlayerMantling`** — a quick, committed, uncancellable move onto the top of what is
  gripped. It decides when a mantle is possible (`CanMantle`, `MantleTarget`) and shows the
  `MantleIndicator` to match: a hand grips an `IMantleable` whose own rule
  (`CanMantleFrom()`) says so - a mantleable edge with the head at least `ledge top -
  headBelowTopAllowance`, or a mantleable ladder with a hand on its top rung. No physics
  queries - where the mantle lands and whether it ends crouched are per-target designer data
  (`GetMantleLanding()`). Either stick pushed up (`stickUpThreshold`) starts the mantle:
  `PlayerClimbing.ReleaseAll()`, `PlayerLocomotion.BeginMantle(endsCrouched)`,
  CharacterController disabled, then the rig is positioned directly along an eased
  up-and-over arc (`duration`, `riseEndsAt`, `forwardStartsAt`) so the capsule bottom lands
  on the mantle point (+`landingLift`). `IsMantling` makes `PlayerController` skip
  everything else. At the end the controller is re-enabled and `EndMantle()` hands back
  control. **The one exception to the single-`Move()` rule** - the landing is
  designer-placed and collision could only stop it landing there, so **every mantle must
  land on top of what it climbs**.
- **`MantleIndicator`** — runtime-built white arrow on a child of Main Camera (so head-locked
  with no code). Uses an `OverlayMaterial` so it draws through walls (the face is against the
  wall while climbing). `SetVisible()` only touches the renderer on change.
- **`OverlayMaterial`** (in `Scripts/Core`, namespace `Core`; `using Core;`) — static factory
  for in-world UI marker materials (reticles, mantle arrow, coins, throw arc) using the
  project's own `TeaLeaf/Overlay` shader (`Assets/Art/Shaders/Resources/Overlay.shader`: flat
  `_Color`, `ZTest Always`, `ZWrite Off`, `Cull Off`, alpha blend, Overlay queue 4000,
  `SRPDefaultUnlit` pass, single-pass-instanced stereo macros; in `Resources` so
  `Shader.Find()` works in builds). **Every UI marker must use it.** Built-in shaders can't
  do this: `UI/Default` doesn't declare `unity_GUIZTestMode` as a property, so a material
  override of it does nothing, and URP's Unlit has no depth-test property.

## Hands

- **`PlayerHandVisuals`** (on `Hands`) — the single owner of where each hand *visual* is placed,
  so no two systems fight over a visual transform. Holds the `leftHandVisual`/`rightHandVisual`
  references and `snapBlendDuration`, creates and ticks both `HandVisualSnap`s
  (`LeftVisualSnap`/`RightVisualSnap`), which other systems snap/release and
  `PlayerHandAnimation` reads, plus one `HandPhysicalFollow` and one `HandGhost` per hand.
  Per hand, per tick: the snap first; only if it isn't using the visual (not snapped and
  fully blended back) does the physical follow run - snapping always wins. Physical Hands
  settings: `collisionLayers` (must be Environment + Interactable; never Player/Climbable;
  empty = collision off), `handRadius` (0.035m), `skinWidth` (0.005m), `catchUpDuration`
  (0.1s), `maxSeparation` (0.4m), and the wrist/fingertip bones per hand. `Reset()` / the
  "Find Hand Bones" context menu find the visuals and bones by name (`J_Left_Hand`,
  `J_Left_HandMiddle4` - the mirrored right hand shares the Left names). Exposes
  `IsLeftHandInContact`/`IsRightHandInContact`, and raises `HandContactStarted(isLeftHand)`
  on the tick a hand goes from free to in contact (not on snapping; for `PlayerHaptics`) -
  **and when it starts pushing something**: `HandPhysicalFollow.IsPushing` is true on a
  frame the sweep's push moved something, and a push counts as started when that hand has
  pushed nothing for `pushRestTime` (0.5s), since a pushed door swings ahead of the hand and
  is caught up with again. Debug capsules (`IDebugDrawable`): in Play Mode the target
  capsule (faint) and the visual's (green, red in contact); in Edit Mode the bone capsule,
  for checking `handRadius`. The hands' cost is profiled under the **`PhysicalHands.Follow`**
  `ProfilerMarker` (one sample per hand per frame, in `TickHand()`).
- **`HandPhysicalFollow`** — plain C# class, one per hand, the physical hands collision
  response: **a kinematic sweep, no Rigidbody** (a velocity-driven Rigidbody hand is
  physics-rate, jitter-prone and needs teleports for snap turn and mantle). Controllers are
  assumed unscaled. `Suspend()` while snapped/untracked.
  - **Sweep:** a capsule from wrist to middle fingertip (measured once from the bones at
    Awake, in controller space so the mirrored hand needs nothing special; ends inset by the
    radius) is `CapsuleCast` from last frame's visual position towards the controller's;
    hits stop it `skinWidth` short and the way *to the goal* from there is projected onto
    the surface and re-swept (collide-and-slide, max 3 sweeps, no allocations). **Re-aimed
    at the goal each sweep, never the leftover of the last move** (carrying the leftover
    slides the hand round a block's edge and out). **Creases:** if a slide would push back
    into the surface the previous sweep slid along (a V, e.g. the mouth of a gap narrower
    than the hand), the move is projected onto the line where the two surfaces meet instead
    (their normals' cross product); parallel surfaces stop it. Triggers are ignored.
  - **Pushing:** each collider a sweep is stopped by is looked up in
    `Interaction.HandPushRegistry`; an `IHandPushable` (an open door's leaf) is told the hit
    point and how far the hand was still trying to go straight into the surface, and moves
    itself (`Push()` returns whether it moved). **If it moved, the sweep is tried again in
    the same frame**, straight at the goal, without using up a slide - once per frame - so a
    hand pushing a door stays on its controller and is never "in contact" (stopping the hand
    at the surface instead makes it stutter). A door that can't keep up or can't move still
    holds the hand at its surface. The push asks for `skinWidth` more than the hand needs.
  - **Depenetration before the sweep:** `Depenetrate()` pushes the capsule - at last
    frame's position, with this frame's rotation - out of anything it overlaps
    (`OverlapCapsuleNonAlloc` into a shared static buffer, then `ComputePenetration` per
    overlap, moved by distance + `skinWidth`, max 3 passes). Before rather than after: a
    rotation-induced overlap is shallow at the start, while after a sweep the hand could be
    past the middle of a thin wall and get pushed out of the far side. It reports `isClear`;
    a sweep hit at distance 0 lets the hand move freely **only** on the first sweep when the
    start couldn't be cleared (genuinely stuck inside) - any other distance-0 hit is
    "touching" and stops the sweep. `ComputePenetration` needs an enabled collider for the
    hand but takes the pose as arguments, so each hand builds a `Penetration Collider`
    (trigger `CapsuleCollider` on Z, `PlayerHands` layer, parked at y -1000 at the scene
    root, never moved, height = wrist to fingertip, radius synced from `handRadius` on
    change, destroyed from `PlayerHandVisuals.OnDestroy()`).
  - **Held and released:** blocked → visual detached and placed by code (`IsPlacedByCode`).
    Attached state is read from the actual parent, since `HandVisualSnap` detaches the same
    transform. **Position and rotation are held and released separately**
    (`_isPositionHeld`/`_isRotationHeld`): a blocked sweep holds both; the position is free
    as soon as the sweep reaches its goal, but the rotation (held at the controller's
    rotation from the first blocked frame - with rotation always following, a tilting wrist
    swings the fingers into the wall) only once a `CheckCapsule` at the hand's current
    position, turned to the controller's rotation, is clear. `IsInContact` = either is held.
  - **Elastic band:** each of position and rotation, from the moment it comes free, eases
    its offset from the controller (stored in the controller's space so the hand keeps
    following the real hand's motion) to zero over `catchUpDuration` (0.1s, SmoothStep via
    `CatchUpWeight()`), still swept each frame, so it can land back in contact; re-attached
    when both are fully back. Not an exponential chase of the controller (lags further the
    faster the hand moves, may never settle). The eases share
    `AdvanceEases()`/`StartPositionReturn()`/`StartRotationReturn()`, and placement is
    `PlaceVisual()`.
  - **Snap-back:** in contact, if the visual is more than `maxSeparation` (0.4m, 0 = never)
    from the controller, contact is dropped and the hand enters "passing through"
    (`TickPassingThrough()`): collision off, it eases back onto the controller - position
    and rotation, over `catchUpDuration`, straight through the wall - then stays on it
    (attached) until a `CheckCapsule` at the controller is clear (one query per frame, only
    once the ease is done). Without the wait, the next push-out would move the hand to the
    nearest face of whatever the controller is inside and it would snap back every frame.
- **`HandGhost`** — plain C# class, one per hand, owned and ticked by `PlayerHandVisuals`
  (after the physical follow; told it can't show while snapped). A faint copy of the hand at
  the real controller while a surface holds the visual away (Alyx style). Made in `Awake()`
  by `Instantiate`-ing the visual under the controller at its rest pose (so it follows
  tracking with no code moving it) with its Animator destroyed; while shown, its bones copy
  the visual's `localRotation`s. Shown when the visual is more than `ghostShowDistance`
  (0.03m) from it, hidden again within half that; the renderer is only switched on changes,
  bones only copied while shown. Settings on `PlayerHandVisuals` (Ghost Hands):
  `showGhostHands`, `ghostShowDistance`, `ghostColor` (white, alpha 0.2, read once in
  `Awake()`). Drawn with **`TeaLeaf/Ghost`** (`Art/Shaders/Resources/Ghost.shader`):
  Overlay's no-depth-test flat colour (the real hand is usually inside the wall), queue
  `Overlay-1` so UI markers stay on top, `Cull Back`, plus a stencil test on bit 128 so each
  pixel is drawn once - without it the see-through hand darkens wherever its own triangles
  overlap. **Anything put on a hand visual at load must be added in `Start()`**, after the
  ghosts have copied the visuals in `Awake()`.
- **`HandVisualSnap`** — plain C# class (one per hand, owned and ticked by `PlayerHandVisuals`;
  other systems only call `Snap()`/`Release()`). Blends a hand *visual* (never the tracked
  controller) between its rest local pose and a world-space `HandSnapPose` over
  `snapBlendDuration` (SmoothStep), both ways, and doesn't touch the transform at rest. A grab
  from rest blends **from where the visual actually is** (captured in `Snap()` in the
  controller's space, so it still follows the real hand), not from the rest pose - a surface
  may be holding it off the controller. `Release()` resets that to the rest pose, so a
  release always blends back onto the controller. Exposes the eased blend as `Weight` (0 =
  following the controller, 1 = snapped), which `PlayerHandAnimation` uses as the finger
  pose layer weight so the two stay in step. While snapped the visual is **detached to the
  scene root** and re-attached when the release blend ends: anything that must stay
  world-fixed can't be a child of a tracked transform (and not a child of the ledge either -
  ledges are non-uniformly scaled, which would shear a rotated child).
  `Snap(pose, blendDuration)`/`Release(blendDuration)` overloads give one blend its own
  time (prop pick-up's reach and return); the plain calls use `snapBlendDuration`.
  `MoveSnapPose(movement)` shifts the snapped pose (a zip line grip) and
  `SetSnapPose(position, rotation)` replaces it, keeping the finger pose (a door handle,
  which moves and turns).
- **`PlayerHandInteraction`** — casts one ray per hand **from the hand visual, not the
  controller** (`PlayerHandVisuals.GetLeft/RightHandPose()` → `HandPhysicalFollow.GetHandPose()`:
  the controller pose shifted to where a surface holds the visual, so a controller pushed
  through a wall can't target/grab behind it; just the controller while the visual is on it;
  last frame's placement, since rays run before the visuals tick) - configurable layer mask
  and per-hand angle offset, pre-rotated into a cached local ray direction in `Awake()`/
  `OnValidate()` - and records whatever `IHandTarget` it hits, if the target accepts a ray
  from that hand (`CanBeTargetedFrom(origin)`). **Two reaches**: `rayLength` (0.5m) for
  climbables and `pickUpRayLength` (1m) for targets with `IHandTarget.HasLongReach` (props).
  One ray at the longer length; a hit beyond `rayLength` only counts for a long-reach
  target. If that far hit is a trigger (an out-of-reach grab volume), the ray is cast once
  more ignoring triggers, so a prop standing in or behind a ledge volume can still be picked
  up. **Before the ray**, `FindTargetContaining()` checks whether the ray origin is already
  *inside* a target's collider (a 1mm `OverlapSphereNonAlloc` into a shared buffer, triggers
  included, registry lookup): if so that target wins, targeted at the hand itself, and the
  ray is skipped - a ray never detects a collider it starts inside, and grab volumes are
  bigger than what they belong to. Nothing is highlighted - the reticle alone shows what can
  be interacted with. Exposes `LeftTarget`/`RightTarget` and the hit points
  `LeftTargetPoint`/`RightTargetPoint`. Ticked before the grab systems, so they're always
  this frame's. `TickReticles()` places the reticles later in the frame (step 8b).
- **`HandRayReticle`** — runtime-built billboard disc shown where a hand ray hits a hand target,
  hidden while that hand is holding something (its visual is snapped); ticked by
  `PlayerHandInteraction.TickReticles()`. Only toggles its renderer when visibility changes.
  Uses an `OverlayMaterial`.
- **`PlayerHandAnimation`** — per-hand Animator. The base layers always follow input
  (`TriggerCurl` index, `GripCurl` middle/ring/pinky). The `Snap Pose` override layer plays the
  snap target's `HandPose` with its weight set from that hand's `HandVisualSnap.Weight` (or
  the carrying pose weight, whichever is larger), so any snapped hand gets its target's
  finger pose and fades back to input curl on release. `SetFloat()`/`Play()`/
  `SetLayerWeight()` only run when the curl, pose or weight changes; both Animators set
  `keepAnimatorStateOnDisable` in `Awake()` so those caches stay valid if a controller
  object is ever deactivated. The layer index is looked up by name; state hashes are built
  once from `HandPose`'s enum names. New poses: add a `HandPose` value plus a same-named
  state and clip on the layer, no new code; `Awake()` warns once for any `HandPose` with no
  matching state.

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
`RopeGrip`, `BottleHold` - clip `Hand_L_BottleHold`), each holding a single-keyframe clip. A
snap clip must key **every joint (1, 2 and 3) of every finger it poses**: it only writes the
bones it keys (Write Defaults doesn't reset unkeyed bones here), so any unkeyed joint keeps
the input layers' curl - and grip is held while climbing. The thumb (`J_Left_HandThumb1-4`)
is only posed by the `RungGrip` and `RopeGrip` snap clips - the input layers and `LedgeGrip`
leave it at rest. Keep the mirrored right hand in mind for colliders and anything
handedness-dependent.

## Debug scripts and test area builders

`Assets/Scripts/Player/Debug/` holds standalone debug/diagnostic MonoBehaviours (e.g.
`VRDebugInput`, `InputTest`, `TrackingTest`, `TurnInputTest`, `TurnActionTest`,
`LocomotionInputTest`, `HandRayDebug`, `MovementStateDebug`) used for manually verifying
systems in Play Mode — not part of the runtime gameplay path. The in-headset gizmo view
(`InHeadsetGizmos`) is shared by every system, so it lives in `Scripts/Core/Debug`.

- **`PhysicalHandsTrace`** (on the Debug object, enable *before* Play) turns on
  `HandPhysicalFollow.TraceEnabled`: one log line per hand per frame whose sweep hits a
  collider whose name contains `colliderNameFilter` (default "Gap") - contact state,
  push-out, and each sweep's hit (collider, distance, normal), no stack trace. Read it from
  `%LOCALAPPDATA%/Unity/Editor/Editor.log`. Allocates while on.
- **`TestGeometry`** (`Debug/Editor`) — what every test-area builder makes its pieces
  through. `Box()`/`Primitive()` give every piece the brown
  `Assets/Art/Materials/TestGeometry.mat` (URP Lit, created by the first build if missing).
  `Edge()` makes a `ClimbableEdge` on a solid lip facing any yaw - an unscaled trigger strip
  on Climbable (0.2m tall, 0.3m deep, 5cm past the solid top and face), set up before the
  component is added, with the landing `landingInset` in from the lip
  (`TopCentreInset(thickness)` = the middle of a wall top); `bothSidesThickness` above 0
  makes it two-sided (volume across the whole top, 5cm past both faces, "Grabbable From Both
  Sides" on); `overhang` (default 5cm, `TightOverhang` 1cm - never zero: a volume flush with
  the solid ties with it for hand rays and the reticle flickers) and `height` (default 0.2m)
  size the volume against the solid. New test-area builders use it too, and build every
  climbable as its setup checks expect: trigger volumes on Climbable, larger than the solid
  part, colliders set up before the component is added, with its
  `LedgeGrip`/`LadderRung`/`RopeGrip` profile.
- **`PhysicalHandsTestArea`** (menu **TeaLeaf > Build Physical Hands Test Area**; not in the
  scene) — a greybox row at (4.5, 0, -5.5), facing the spawn: an inside corner, a 2cm thin
  panel (tunnelling, far-side push-out, snap-back), a table with a static 0.3m crate on
  Interactable, a pillar, a 5cm gap between two blocks (narrower than the 7cm hand), a 45°
  slope and a round post.
- **`LocomotionTestCourse`** (menu **TeaLeaf > Build Locomotion Test Course**; not in the
  scene) — a row 10m in front of the spawn (root at (12, 0, 10), turned 180°): a 1.3m chest
  ledge, a 2.3m above-head ledge, a 1.1m low shelf under a ceiling (mantle ends crouched), a
  4m tower with a ladder (mantles from its top rung) and a 3.7m rope sharing a top edge that
  covers only the rope's half of the lip, a 20° ramp to three 1m platforms with 1.5m and
  2.5m jump gaps, 0.45m and 0.6m crates, a ceiling slab at 2m, two 5m ropes strung between
  posts 2.2m up (one sagging 0.3m via an `endPoint` child, one straight level zip line) and
  an angled zip line from above the tower down 9m behind the row. Each rope has a visible
  cylinder per piece of its curve (`AddVisibleRope()`). Sizes come from the player settings.
- **`TownTestArea`** (menu **TeaLeaf > Build Town Test Area**; not in the scene) — a greybox
  town square (root at (0, 0, -35), behind the spawn): six flat-roofed two-storey houses,
  each from a `BuildingSpec` (size, optional tower, terrace, balcony, door position) in its
  own frame (front outer face at z = 0 facing +Z, turned to face the square), four
  free-standing 2.4m compound walls and a few crates. Doors and windows are empty openings;
  steps are smooth ramps. Ledges about a metre apart so every face climbs hand over hand to
  the roof: window sills (two-sided, mantled into crouched), window hoods and the band below
  each roofline (grab only), and parapets, balcony balustrades, compound walls and terrace
  walls (two-sided, mantleable, the long ones with "Move Horizontally To Point" off). Every
  edge uses `TestGeometry.TightOverhang` and is no taller than a thin piece.

## Settings

Smooth Turn/Snap Turn, turn speed, snap angle, movement speed, ghost hands and vibration
on/off are expected to become user-configurable options (out of scope for the vertical
slice); they are serialized fields for now. Vibration already has its public switch:
`PlayerHaptics.HapticsEnabled`.
