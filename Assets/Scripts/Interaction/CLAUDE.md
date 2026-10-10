# Interaction systems (`Assets/Scripts/Interaction/`, namespace `Interaction`)

Detail for hand targets, grabbable props, doors, lockpicking, climbables and hand snap poses.
The root `CLAUDE.md` holds the project rules, the tick order and the physics layers; this
file is loaded when working in this folder. Keep it up to date with every change to these
systems, like the root file.

## Hand targets

- **`IHandTarget`** — interface for anything a hand ray can target (the reticle shows on it;
  other systems decide what targeting means). `CanBeTargetedFrom(rayOrigin)`, a default
  interface method returning true - override it to refuse rays from some places (`Ladder`
  does, for its back). A refused ray still stops at the target but finds nothing: no
  reticle, no grab. `HasLongReach` (default interface property, false): true lets the hand
  ray target it out to `PlayerHandInteraction`'s `pickUpRayLength` instead of `rayLength` -
  `Grabbable` returns true, climbables keep the short reach. **No highlighting**: the
  reticle is the feedback.
- **`HandTargetRegistry`** — static `Collider → IHandTarget` dictionary. Targets register in
  `OnEnable`/unregister in `OnDisable`, so hand raycasts do a dictionary lookup instead of an
  interface `GetComponent`.
- **`IHandPushable`** / **`HandPushRegistry`** — something solid the player moves by
  pressing on it: `bool Push(point, displacement)`, displacement being the part of the
  blocked movement that went straight into the surface; true = it moved, and a hand then
  retries its move the same frame instead of stopping. The registry is a static `Collider →
  IHandPushable` dictionary; `Door` registers its leaf. `Player.HandPhysicalFollow` looks up
  every collider its sweep is stopped by (only on frames it hits something);
  `Player.PlayerBodyPushing` uses the same registry for the body.

## Grabbable props

- **`Grabbable`** — a physics prop a hand picks up, carries and drops (`IHandTarget` +
  `IHandSnapTarget`; `[RequireComponent(Rigidbody)]`; collider(s) on the Interactable layer,
  all registered with `HandTargetRegistry`). The carrying itself is `Player.PlayerHandHolding`
  (see `Scripts/Player/CLAUDE.md`).
  - **Held at an authored pose** (not "stays as grabbed"): `gripPoint` (a child Transform,
    the grip frame; empty = the prop's own origin) and a shared `snapProfile`
    (`HandSnapProfile`; empty = the hand sits exactly on the grip frame). `GetSnapPose()`
    returns where the hand visual would have to be to hold the prop as it lies; the holder
    snaps the hand visual out to that pose, then carries the prop at the same pose relative
    to the hand as the hand comes back.
  - **`gripShape`**: `Point` (default) always uses the grip point, so the prop is held one
    way - for things with a handle. `Cylinder` grips like a rope: the grip point (or the
    prop's origin) is the middle of the grippable part with Y along the axis;
    `GetCylinderGripFrame()` puts the frame on the surface (`cylinderRadius`, 0.04m) level
    with the grab point (clamped to `cylinderGripLength`, 0.1m, centred on the grip point),
    facing straight in at the axis, up along the axis - so the prop always ends the same way
    up in the hand. **The side is the player's, from the head**: towards `headPosition`,
    turned round the axis by `shoulderAngle` (20°) per hand, exactly as a hanging
    `ClimbableRope` does it; the ray hit's side, then the grip point's back, are only
    fallbacks. Selected (and in the headset), a cylinder draws its grippable part
    (`IDebugDrawable`).
  - `holdRadius` (guessed from the colliders by `Reset()`) and `LocalCentre` (middle of the
    solid colliders, rotation-only local, measured in `Awake()`) describe the ball the
    hand's collision grows to while it's carried. `IsHeld`, `WorldCentre`.
  - `BeginHold(heldLayer)`: Rigidbody kinematic, every collider's object moved to the
    PlayerHands layer (original layers remembered), interpolation `None`.
    `EndHold(velocity, angularVelocity)`: non-kinematic, starting with the velocity and spin
    given - both zero is a drop from rest, anything else a throw, which also switches the
    Rigidbody to `ContinuousDynamic` collision detection (its own mode is put back by the
    next `BeginHold()`) - and interpolation `Interpolate` (physics steps at 50Hz, the
    headset draws faster; while held it's placed through its parent, which interpolation
    would fight). **Set `Interpolate` on any new prop** so one knocked over before it's ever
    held moves smoothly too.
  - **A held prop pushes nothing**: `BeginCarry()` - called by the holder when the hand has
    reached the prop (`FinishReach()`), not at the start of the reach - sets
    `Rigidbody.detectCollisions` false, `EndHold()` true. During the reach the prop is
    kinematic but still solid, and doesn't move. Just before, `WakeNeighbours()` wakes every
    Rigidbody within 5cm of the prop's bounds (one `OverlapBoxNonAlloc`, shared 16-slot
    buffer): switching collisions off doesn't wake sleeping bodies, so props stacked on the
    one taken would hang in the air.
  - After `EndHold()` it is **still on the PlayerHands layer** until `RestoreLayers()`,
    which the holder calls once the prop is clear of the hand (restored at once, the hand's
    own collision pushes the hand visual off the prop it's still wrapped round).
  - **Finger pose:** the profile's `pose`, as for climbables - `BottleHold` for cylinders; a
    prop with no profile shows `LedgeGrip` (value 0). Other prop shapes (cube, crate) have
    no hold pose yet.
  - **`Stow()` / `Unstow()`** (for the player's pack - `Scripts/Inventory`): `Stow()` takes
    a prop no hand holds out of the world - kinematic, no interpolation, every collider
    disabled - so whatever stowed it can move and scale it; `Unstow()` enables the colliders
    again, leaving it kinematic for the caller to hand to a hand (`BeginHold()`) or drop
    (`EndHold()`). `IsStowed`.
- **`ImpactNoise`** — a prop's sound when it hits something, on its Rigidbody object
  (`[RequireComponent(Rigidbody)]`); separate from `Grabbable`, so anything physical can
  have it. Nothing per frame: all in `OnCollisionEnter()`. **How hard** =
  `collision.impulse.magnitude / mass`, the speed the collision took off the prop in m/s
  (not `relativeVelocity`: a prop sliding onto the next floor piece starts a collision but
  loses no speed). Below `minSpeed` (1) silent; from there to `fullSpeed` (8) the volume
  goes `minVolume` (0.25) to 1 and the noise radius scale `minNoiseScale` (0.3) to 1, of the
  `cue`'s. **What it hit** = `SurfaceSounds.GetLoudness(SurfaceTag.Of(collision.collider))`,
  multiplying both (and `Play()`'s `muffle` = 1 - loudness, so a soft surface is duller
  too); `surfaceSounds` is optional (empty = everything is stone). One
  `SoundPlayer.Instance.Play()` does audio and noise, at the first contact point
  (`GetContact(0)`, no array), with the prop as the source; with no sound player in the
  scene, just `cue.EmitNoise()`. **Kept from repeating:** `minInterval` (0.15s) between
  sounds from one prop, and two props hitting each other play once - a static "last impact"
  step and position, skipped if the same physics step and within 0.3m. A kinematic prop is
  silent, and a carried one is out of physics. The test props get one from
  `GrabbableTestProps` with the placeholder impact cue and surface sounds.

## Doors

Three lock kinds (none, simple = pickable, keyed); any unlocked door opens by its handle; the
handle is ray-targeted and grabbed at the ledge reach, and the hand snaps onto it; turning
the hand 60° frees the door; it swings either way; let go within a few degrees of closed, it
shuts again; an open door is moved by the hand visuals and the body touching it; a
simple-lock door has a keyhole to look through; an optional sliding bolt is worked only from
its own side whatever the lock. The player's half is `Player.PlayerHandDoors` (see
`Scripts/Player/CLAUDE.md`).

- **`DoorLock`** (enum) — `None`, `Simple` (can be picked), `Keyed` (its own key only).
  Stored as its number: add values at the end.
- **`Door`** — on the hinge: the door turns about its local Y, X runs along it to the handle
  edge, Z is the way through. `[RequireComponent(Rigidbody)]`, made kinematic in `Awake()`.
  **The angle is set from code - no hinge joint, no forces**: predictable, cheap, and exact
  under a snapped hand; the Rigidbody is there so physics moves the collider cheaply and the
  door shoves loose props aside.
  - **Two states, kept apart:** `IsLocked` (only ever true with a lock; `startsLocked`) and
    `IsLatched` (shut and caught; `IsOpen` is its opposite, whatever the angle). `Angle` is
    degrees from closed (positive towards local -Z), limited to `maxOpenAngle` (110) each
    way. `IsBolted` is set by a `DoorBolt` through `SetBolted()`; `IsHeldShut` = locked or
    bolted, which is what `Unlatch()` refuses and what the player's handle code reads for
    the short stop and the rattle. `keyId` is the id its key must have.
  - `BeginHold()`/`EndHold()` (`IsHeld`: one hand at a time), `Unlatch(soundPosition)`,
    `RattleLocked()`, `SetAngle()` (ignored while latched), `Lock()` (needs a lock and a
    shut door)/`Unlock()` - also right-click "Test Lock"/"Test Unlock" in Play Mode.
    `TryGetBearing(worldPoint, out degrees)` = which way round the hinge a point is,
    measured like `Angle`; false within 0.1m of the hinge line. `LeafCentre`.
  - **Moving with no hand on the handle** is the component's `Update()`, and **the component
    is disabled whenever the door is at rest or held**, so a door at rest costs nothing (so
    it registers with `DebugDrawRegistry` and `HandPushRegistry` in `Awake()`/`OnDestroy()`,
    not `OnEnable()`/`OnDisable()`). `Update()`, in order: pushed this frame or last =
    nothing; still has speed = `TickSwing()`; stopped within `closeAngle` (10°) of closed =
    turns itself shut at `closeSpeed` (60°/s) and latches; stopped further open = stays, and
    disables itself.
  - **Speed** (`_angularVelocity`, degrees a second): measured while a hand moves it
    (`TrackVelocity()`, half old and half new each frame, in `SetAngle()` and `Push()`), so
    it swings on when the hand lets go or stops pushing; `TickSwing()` turns it by its
    speed, which falls by `Exp(-swingDrag x time)` (`swingDrag` 3) and counts as stopped
    under `minSwingSpeed` (5°/s); it stops dead at a limit or against the player. **A door
    swinging by itself that reaches closed latches** - it only goes through its frame while
    a hand takes it through.
  - **Pushed** (`IHandPushable.Push(point, displacement)`, from the hand sweep and from
    `Player.PlayerBodyPushing` when the body walks into the leaf): an open, unheld door
    turns by the change in bearing between the pressed point and that point moved by the
    push - so nearer the hinge turns it further, and a push along the door does nothing - at
    most `maxPushSpeed` (360°/s), once a frame.
  - **`Physics.SyncTransforms()` after every move** (`MoveTo()`): Auto Sync Transforms is
    off in the project, so physics would otherwise see the leaf where it was at the last
    physics step and a hand would keep pushing a door that had already moved.
  - **Never swings into the player**: before each move, one `Physics.CheckBox` of the leaf
    at the new angle against `blockingLayers` (Player) - worked out from the leaf's box
    measured in `Awake()`; blocked only if it isn't already overlapping, so it can always
    swing clear. A door shutting itself, or pulled towards the body by its handle, that
    meets the body stops; a door pushed by the body moves away from it, so the check doesn't
    stop that.
  - Optional `soundPortal` (closed while latched, open otherwise, set in `Start()` after the
    portal's own `Awake()`), `latchCue` (freeing and catching), `lockedCue`, `creakCue`
    (one-shot every `creakInterval` 25° of travel); all through `SoundPlayer`, so guards
    hear them.
  - Gizmo (selected / detailed): the swing arc on the floor with the limits and the close
    angle, red locked, yellow latched, green open.
- **`DoorHandle`** — `IHandTarget` + `IHandSnapTarget`; a trigger `BoxCollider` on
  Interactable, a child of the door on the spindle, mid-thickness, with the door's axes.
  **One handle serves both sides**: the grab volume goes through the door and out of both
  faces (further than anything solid), and the hand takes the lever on the side the
  **head** is on (`IsInFront()`), as a two-sided edge does. Grip frame: on the lever's
  surface (`standOff` 0.075 out, `gripAlong` -0.07 along X, `gripRadius`), facing into the
  door, up along the lever towards that hand's thumb (across the player's body, so opposite
  ways for the two hands and the two sides). `GetSnapPose(isLeftHand, isFront, leverAngle)`
  turns the whole pose about the spindle by the lever's angle and is called every frame the
  handle is held (the interface version uses the head's side and the current angle).
  `SetLeverAngle()` turns the optional `lever` transform (both levers under one object)
  about local Z, only on change; `GetGripPoint(isFront)`. The levers the player sees have no
  colliders. Setup check: trigger, Interactable layer. Short reach, like a ledge.
- **`DoorBolt`** — a sliding bolt on one face of a door. `IHandTarget` + `IHandSnapTarget`;
  a trigger `BoxCollider` on Interactable covering the bolt on its own side, a child of the
  door. The object is on the door's face where the knob is when drawn back, **with the
  door's axes** (X towards the free edge = the way it shoots, Y up, Z out of the front);
  `onFront` says which face. **Worked only from its own side**: `CanBeTargetedFrom()`
  refuses a ray starting on the other side. `Slide` 0 (drawn) to 1 (shot) over `travel`
  (0.05m); `SlideAt(worldPoint)` = how far along the travel a point is (not limited);
  `SetSlide()` (clamped; **held at 0 while the door is open**; true on the frame it reaches
  an end) moves the optional `bar` transform; `Release()` settles a bolt left part way at
  the nearer end. `IsShot` is passed to `Door.SetBolted()`; `startsShot` (applied in
  `Start()`). `GetGripPoint()`, `GetSnapPose(isLeftHand)` (on the knob, `standOff` out,
  facing into the door, thumb up; through `snapProfile` - the test bolt reuses
  `DoorHandle.asset`). `slideCue` at each end. Gizmo: the grab volume; selected, the travel
  (green drawn, red shot) and the knob. The player's half is in `PlayerHandDoors`.
- **`KeyLock`** — marks a keyed door's lock, as `PickableLock` does a simple one: the middle
  of the keyway, mid-thickness, X across the face, Y up, Z through. `door`, `faceOffset`,
  `unlockCue`. `KeyId` (the door's), `IsLocked`, `HasKeyIn` (`BeginKey()` / `EndKey()`).
  `FindInRange(point, range)` = the nearest locked one with no key in it (a static list,
  distance checks only). `GetFace(viewerPosition, out facePoint, out facing)`: the face on
  the viewer's side and **`facing`** - how something looking at the lock from there is
  turned (forward into the door, up the lock's up, so its right is the viewer's right).
  **`TurnsAnticlockwise(facing)`**: the key turns anticlockwise if the lock is on the right
  of the door leaf from the viewer's side, clockwise if on the left - worked out from where
  the lock is relative to `Door.LeafCentre` along the viewer's right. `Unlock()` calls
  `Door.Unlock()` and plays the cue. Workable from either side. Gizmo as `PickableLock`'s.
  The keys themselves are `Inventory.Key` / `Keyring` (`Scripts/Inventory/CLAUDE.md`); the
  player's half is `Player.PlayerKeys`.
- **`DoorKeyhole`** — the keyhole view. **An opening in the door, not a second camera**: as
  the head nears, a keyhole-shaped opening grows in the leaf and the player looks through
  with their own eyes - true stereo depth, the head looks round, nothing drawn twice, not
  magnified. The shape is **a circle with a slot down from it**: the slot as wide as the
  circle's radius and reaching a diameter below it, so the whole is 2 radii wide and 4 tall,
  sized by the radius alone. An empty child of the door marking the keyhole's middle (the
  bottom of the circle; mid-thickness; X across the face, Y up).
  - The cut is made by the leaf's shader, **`TeaLeaf/DoorLeaf`** (see
    `Scripts/Core/CLAUDE.md`), which the leaf's material must use; **picture only** - the
    leaf's collider is untouched, so hands, props, light rays and sound are stopped as
    before. `Awake()` gives the shader the keyhole's place in the leaf's own space through a
    `MaterialPropertyBlock` (centre, and the face's two directions times the leaf's scale so
    distances are metres), once; `Apply()` sends the size only when the amount changes.
  - The radius goes from `restRadius` (5mm: a real 1 x 2cm hole at rest) to `openRadius`
    (5cm: 10 x 20cm) as the head goes from `openStartDistance` (0.6m) to `openFullDistance`
    (0.25m) from the keyhole, smoothstepped, then limited to `openSpeed` (6 a second).
    **Only while the door is latched** - an open door's keyhole goes back to rest.
  - **The opening has real depth**: `BuildWalls()` makes, once in `Awake()`, a `Keyhole
    Walls` child with a small dynamic mesh - the outline on each face joined into a tube
    seen from inside, 46 triangles, normals inward, shadows off - drawn with the leaf's
    material and a `wallColor` property block (no keyhole values, so nothing is cut in it).
    The outline (`BuildOutline()`, static, for radius 1, shared by every keyhole and by the
    gizmo; **it must match `KeyholeDistance()` in the shader**) is four runs of points that
    don't share points where they meet (hard edges): a 300° arc in 20 pieces from the slot's
    right meeting point over the top to its left one, then the slot's left side, bottom and
    right side. `UpdateWalls()` moves its points whenever the size changes (same array,
    fixed bounds, no allocation). Depth = `thickness`, or 0 (default) = measured from the
    leaf renderer's bounds along the keyhole's forward. Needs the keyhole and its parents
    unscaled.
  - No `Update()`: self-registers in a static list; `FindInRange(headPosition)` returns the
    nearest one in range, and `Tick(headPosition, deltaTime)` (false once at rest and out of
    range) / `Close()` are called by `Player.PlayerKeyholes` on that one only.
  - **Lock plates** (`plateRenderers`): other renderers the opening must go through as well
    - the round lock plate standing on each face. Each uses a `TeaLeaf/DoorLeaf` material of
    its own and gets its own property block (`BuildBlock(target)`), sized with the leaf's in
    `Apply()`. Set `thickness` to the plate's whole depth so the walls reach its faces. The
    keyway on the door is keyhole-shaped; the big lock's is a rectangle.
  - Gizmo (selected / detailed): the rest (yellow) and open (green) outlines 3cm either side
    of the middle.
- **Hand pose:** the handle's profile is `Assets/Data/DoorHandle.asset`, a **copy of
  `BottleHold`** (same finger pose) - a stand-in; tune its offsets in the headset, and give
  it a pose of its own if the bottle's looks wrong.
- **`DoorTestArea`** (`Interaction/Debug/Editor`, menu **TeaLeaf > Build Door Test Area**) —
  a 7m wall (Static, Environment) 2.5m ahead of the main camera with three 0.9 x 2.1m
  doorways: no lock, simple lock (starts locked), keyed lock (starts locked). Each door is
  an unscaled root on the hinge with a kinematic Rigidbody, a leaf, and a handle 1m up with
  a lever each side. Placeholder impact cue for the latch and rattle and the placeholder
  creak (`PlaceholderSounds.Create()` is run first).
  - **Sound:** a `SoundPortal` in each doorway (a child of the area, not the door; wired to
    the door's `soundPortal`), a 7 x 3 x 6m `SoundRoom` each side of the wall, and a `Sound
    Emitter Behind Doors` cube 2.5m beyond the wall knocking every 3s - disable it to test
    in silence.
  - **No-lock door:** a sliding bolt, 1.35m up on the side the area faces (the door's back).
  - **Simple-lock door:** a `Keyhole` child (`DoorKeyhole` + `PickableLock`), 0.18m below
    the handle (0.82m up: crouch or bend to look), with a `Lock Plate` child (a primitive
    cylinder through the door, 3.5cm radius, 6mm proud of each face, no collider,
    `Assets/Art/Materials/LockPlate.mat` = DoorLeaf shader in dark iron); its leaf uses
    `Assets/Art/Materials/DoorLeaf.mat`.
  - **Keyed door:** a `Key Lock` child (`KeyLock`, with a lock plate) and the key id `red`;
    a `Key Stand` on the camera's side of the wall between the simple-lock and keyed doors
    holds a red key (opens the keyed door) and a blue one (opens nothing).
  - **Added if missing:** `PlayerHandDoors`, `PlayerLockpicking` and `PlayerKeys` on the
    Hands object; `PlayerBodyPushing` and `PlayerKeyholes` on the Player root; the pack and
    inventory (through `Inventory.LootTestProps.EnsurePlayerComponents()`); and a **`Big
    Lock`** object (`BigLock` + `BigLockDebug`) at the scene root - outside the test area,
    so a rebuild keeps its tuning - with `Assets/Data/LockpickHold.asset` (a copy of
    `RopeGrip`, a stand-in) and the placeholder impact cue for picking and unlocking.
    `PlayerKeys`' hand pose on the key is `DoorHandle.asset`, a stand-in.

## Lockpicking

The two-pick design (spec: `Assets/DEVROADMAP.txt`, Phase 5). The player's half is
`Player.PlayerLockpicking` (see `Scripts/Player/CLAUDE.md`). **Angles are clock positions on
the lock's face seen from the player's side, in degrees clockwise from 12.**

- **`PickableLock`** — marks a lock that can be picked: the middle of the keyway,
  mid-thickness, X across the face, Y up, Z through (normally the `DoorKeyhole`'s object).
  `door` (optional: with none - a chest, later - it keeps its own locked state and raises
  `Unlocked`), `faceOffset` (from the object out to the lock's face, where the picks sit).
  `IsLocked`, `IsBeingPicked` (`BeginPicking()`/`EndPicking()`), `CanBePicked`. Only a
  `DoorLock.Simple` door's can be picked (warning otherwise). `GetFace(viewerPosition, out
  facePoint, out outward)` gives the face on the viewer's side - **pickable from either
  side**. `Unlock()` calls `Door.Unlock()` (the door stays shut; unlocked until the level
  restarts). No `Update()`: a static list, `FindInRange(point, range)` = the nearest that
  can be picked (distance checks only). Gizmo (selected / detailed): a ring on each face,
  red locked, green open.
- **`BigLock`** — the large copy of the lock that floats in front of the real one, **and
  the puzzle itself**. One in the scene, built in `Awake()` from its own fields and reused
  for every lock; `PlayerLockpicking` finds it (or makes one with defaults if the scene has
  none). Own axes: X to the player's right, Y up, Z into the door; origin = the middle of
  the body; unscaled. No `Update()`.
  - **Looks:** a round body (`faceRadius` 0.11, `bodyDepth` 0.05) with a dark rectangular
    keyway, a mark on the rim at each of the right pick's four stops and each end of the
    left pick's sweep, and two picks (`pickLength` 0.2) on child objects that turn about the
    keyway, leaning `pickLift` (35°) out of the face. Meshes from `LockMeshBuilder`,
    vertex-coloured, `TeaLeaf/LockFade` (see `Scripts/Core/CLAUDE.md`). **No colliders**:
    hands reach into it.
  - **Fade:** `Show(target, position, rotation)` / `Hide()`; `Tick()` moves `_fade` over
    `fadeDuration` (0.25s); the three renderers share two materials it made - see-through
    while fading (its `_Alpha` written directly: the class owns it), solid once in - and are
    disabled when out; swapped only on a change (`SetLook()`).
  - **The puzzle:** `PinCount` 3. Right pick: `rightStartAngle` (30 = 1 o'clock),
    `CurrentStop` = start + `stopSpacing` (28°) x (`PinsSet` + 1) - four evenly spaced
    stops, the last one unlocking. Left pick: `leftStartAngle` (-30 = 11 o'clock), swept
    `LeftSweep` 0-`leftArc` (120°, to 7 o'clock) anticlockwise. `TurnPick(isLeftPick,
    handPosition)`: the pick's angle is the hand's clock angle round the lock's middle
    (`Atan2(x, y)` in the lock's space), measured from the middle of the pick's range with
    `Mathf.DeltaAngle` and clamped to it - stateless, no wrap problems; a hand within
    `deadRadius` (3cm) of the axis changes nothing. The right pick reaching its stop
    (`stopTolerance` 1.5°, re-armed once turned 3x that back) raises `StopReached`, or with
    every pin set unlocks: `Target.Unlock()`, `unlockCue`, `Unlocked`. `IsSearching` = right
    pick held at its stop with a pin to find; then (`TickSearch()`) with the left pick in
    hand, `PinNearness` = 0 at `pinFeelAngle` (10°) from the pin rising to 1 at
    `pinSetAngle` (5°), and `HoldTimer` counts while within the set angle (back to 0 on
    leaving); at `pinHoldTime` (1s) the pin is set (`PinSet`), and the next stage's pin
    chosen. `ChoosePin()`: a random sweep, never within `minPinGap` (20°) of where the left
    pick is (chosen from the sweep with that stretch cut out - no retry loop).
  - **Reset:** `ReleasePick(right)` before the unlock puts `PinsSet` back to 0 with a new
    pin (`WasReset` if anything was lost), and `Tick()` swings the pick back at
    `returnSpeed` (240°/s); the left pick stays where it's left.
  - `HoldPick()`, `GetGripPoint()`, `GetSnapPose(isLeftPick)` (a rope-style grip frame: up
    along the pick, facing away from the player; through `snapProfile`, or
    `HandPose.RopeGrip` with none), `CanBeWorked` (faded at least half in), `IsActive`.
  - **Sound:** `pickCue` (quiet: `pickVolume` 0.4, `pickNoiseScale` 0.3) at each stop, pin,
    reset and every `scrapeAngle` (25°) the left pick sweeps; played **from the real lock**,
    not the floating copy, through `SoundPlayer` (so guards hear it).
- **`LockMeshBuilder`** — collects boxes (`Box(centre, size, rotation, color)`) and a
  cylinder along Z (`Cylinder(radius, depth, segments, faceColor, sideColor)`) into one
  vertex-coloured mesh (`ToMesh()`); `CreateMaterial(seeThrough, minLight)` makes the two
  kinds of `TeaLeaf/LockFade` material. Load time only. Also used by the pack and keyring.
- **`BigLockDebug`** (`Interaction/Debug`, on the Big Lock object) — `IDebugDrawable`, in
  the big lock's own space while it's in view: both picks' angles (white), the current
  stop (yellow), the pin (red), a square per pin (green once set) and the hold timer as a
  bar; logs stage changes to the Console.
- **Not built / stand-ins:** the "lockpick hold" finger pose (`LockpickHold` is a copy of
  `RopeGrip`); chests; a left-handed setup; real sounds.

## Smithy building

Menu **TeaLeaf > Build Smithy**: a half-timbered blacksmith's house, 10m ahead of the main
camera, front towards it. A building made as a game asset rather than greybox cubes. Three
files in `Interaction/Debug/Editor`:

- **`BuildingMesh`** — collects flat faces into one mesh per material. `Polygon(normal,
  uAxis, vAxis, points)` is the base: UV = the point's distance along the two axes in
  metres / `tileSize` (so textures are the same size on every piece and run on unbroken),
  tangent = uAxis, and the triangle order is checked against the normal, so callers can't
  get winding wrong. On top: `OrientedBox`, `Box(min, max)`, `Beam(from, to, width, depth,
  depthAxis)` (grain along its length), `Prism` (a convex outline pushed through a
  thickness), `Cylinder` (flat-sided, with an outline of heights and radii). `Matrix`
  places everything added next (UVs are taken before it). Flat-shaded. `WriteTo(mesh)`.
- **`SmithyTextures`** — paints five tiling 512 textures in code (wrapping value noise) and
  saves them as PNGs in `Assets/Art/Textures/Smithy`: plaster (blotches, stains, hairline
  cracks), timber (grain along u), stone (six courses of uneven blocks, moss), roof tiles
  (8 rows of 10; **v runs down the roof**), planks (six across u). Stone, tiles and planks
  also get a normal map from a painted height; plaster and timber don't. Materials in
  `Assets/Art/Materials/Smithy`: URP **Simple Lit**, highlights off; iron is a colour with a
  dull shine. **Assets are only made if missing** - delete one to remake it; hand edits are
  kept. `…TileSize` constants are the metres one repeat covers.
- **`SmithyBuilding`** — the building. **Six materials, one mesh each** (saved in
  `Assets/Art/Models/Smithy`, overwritten by a rebuild): six draw calls. Each part object
  is Static, on Environment, with a non-convex `MeshCollider` of the same mesh and a
  `SurfaceTag` (wood, tile, metal). Stone ground floor (6 x 5m, 0.4m walls, forge arch
  with hearth, open doorway, barred window, buttress); upper floor jettied 0.3m at front
  and sides, timber-framed plaster with a window in each wall; 48° tiled roof, ridge along
  X, with a cross gable over the front left; ridge caps; chimney; a 12° lean-to awning over
  the forge; barrels, anvil, workbench, tool rail. **Hollow**: ground room, an inside ladder
  through a floor hatch, upper room with open windows; the attic is closed. Where roof
  slopes cross, nothing is trimmed - the hidden parts overlap inside. Beams of different
  kinds stand different distances proud so overlapping ones never share a face.
  **Climbables** (under `Climbables`; edges through `TestGeometry.Edge()` with
  `TightOverhang`): per upper wall the jetty beam, sill rail (split round the window) and
  head rail (grab only) and the window's sill (two-sided, mantled into crouched); the
  awning's front edge (mantled; its landing is lifted to the slope); each gable's rails;
  the main ridge (two-sided along it, and end-on at each gable) and the front ridge's end
  (all mantled - the caps are 0.34m wide to stand on); the eaves and the barred window's
  sill (grab only); the outside ladder (leaning, not mantleable, ends under the side
  window) and the inside ladder (mantles onto the floor beside the hatch). Roof slopes are
  too steep to stand on. Props are part of the meshes: none can be picked up.

## Climbables

- **Climbables have no mesh** — `ClimbableEdge`/`Ladder` are just a `BoxCollider` and
  `ClimbableRope` a `CapsuleCollider` (invisible in game), shown in the Scene view by always-on
  gizmos. A renderer is only added to an individual item that should be visible, as a separate
  component the scripts don't touch.
- **Grab volumes are bigger than the thing, and a hand inside one can grab** — a climbable's
  collider is a trigger grab volume, deliberately larger than any solid geometry it belongs
  to (Environment, so the physical hands stop against it), so it's easy to aim at.
  `PlayerHandInteraction` targets a hand target whose volume contains the hand before casting
  the ray, since a ray starting inside a collider never detects it - so a hand stopped
  against a solid rope, inside its grab volume, can still grab. Solid geometry must not reach
  as far as the grab volume's surface, or rays tie between the two and targeting flickers.
- **Gizmos show in the headset too** — all three are `IDebugDrawable`s: their gizmos are
  drawn by `DrawDebug(lines, detailed)` through `DebugLines`, called with `detailed` false from
  `OnDrawGizmos` and true from `OnDrawGizmosSelected`, and they register with
  `DebugDrawRegistry` in `OnEnable`/`OnDisable` (see `Scripts/Core/CLAUDE.md`). `DrawDebug`
  uses the cached collider when `Awake()` has run, `GetComponent` otherwise (editor).
- **`IClimbable`** — extends `IHandSnapTarget`; marks a grab target as climbable (grabbing it
  starts a climb). Adds no members: every climbable moves the player the same way.
  `PlayerClimbing` finds it on a hand's ray target with a type check. Implemented by
  `ClimbableEdge`, `Ladder` and `ClimbableRope`.
- **`IMantleable`** — a climbable the player can mantle off the top of:
  `MantleEndsCrouched`, `CanMantleFrom(gripPoint, headPosition, headBelowTopAllowance)` (each
  target's own rule, called every frame a hand grips it) and `GetMantleLanding(feetPosition)`.
  `PlayerMantling` asks whatever each hand grips, so it never knows what kind of thing is
  being mantled. Implemented by `ClimbableEdge` (head pulled up to within the allowance of the
  ledge top) and `Ladder` (a hand on the top rung).
- **`IZipLine`** — a climbable that carries a gripping hand along itself: `IsZipLine`,
  `ZipSpeed`, `ZipAcceleration`, `Length`, `GetPoint(t)`, `GetClosestT(point)`,
  `GetZipDirection(headForward)` (t = 0-1 along the line). The line only describes itself;
  the sliding is `PlayerClimbing`'s. Implemented by `ClimbableRope`.
- **`ClimbableEdge`** — designer-placed `BoxCollider` that is the hand-ray target
  (`IHandTarget`, registered with `HandTargetRegistry`). Implements `IClimbable`: the snap
  point is the ray hit moved onto the box's top-front line (clamped to its length), facing
  into the wall - via `ClosestLipPoint()`, which mantling also uses for the ledge-top height.
  **Orientation convention:** local X runs along the edge, +Y is up, +Z points out from the
  wall towards the player.
  - **Mantling:** `isMantleable` (tick only on level edges), `mantleEndsCrouched`, and
    `mantlePoint` - where the feet land, relative to the edge's origin in real metres along
    its axes (`MantlePointWorld` = position + rotation × point, **not** `TransformPoint()`,
    since ledges are stretched cubes). Defaulted from the box (top face, 0.4m in from the
    lip) by `Reset()` / the "Reset Mantle Point" context menu.
    **`moveHorizontallyToPoint`** (default on): on, a mantle always lands exactly on the
    mantle point; off, it lands straight ahead of the player - the mantle point's height and
    distance back from the lip, but the player's own position along the edge (feet measured
    square-on, clamped to the lip's ends). Off suits long edges (roof parapets, walls).
    `GetMantleLanding(feet)` returns either, and is what `PlayerMantling` uses.
  - **`grabbableFromBothSides`** (default off): for the top of something free-standing
    gripped from either side (wall, parapet, railing, window sill). On, `GetSnapPose()`
    snaps the hand onto the lip on the player's side - the top-front line from in front, the
    top-back line from behind, with the grip frame facing in from that side. The side is
    where the **head** is relative to the middle of the box front to back (`IsBehind()`),
    not the hand or the ray hit. The box must span the thing's whole thickness, overhanging
    both faces alike - so leave it off for ledges set into a wall. Use one two-sided edge,
    never two back-to-back edges (their volumes overlap and a grab gets the far side's).
    Mantling is the same from either side, so use a top-centre mantle point.
  - **Setup:** solid ledge geometry on Environment, plus a separate trigger edge volume
    slightly larger on the Climbable layer, so hand rays hit it first rather than tying with
    the solid face. `OnValidate()` warns (clickable) if the `BoxCollider` isn't a trigger or
    the object isn't on the Climbable layer.
  - Gizmos: the box faintly and its lip (both lips if two-sided) brightly; selected, also
    the mantle point, or the landing line when `moveHorizontallyToPoint` is off.
- **`Ladder`** — `IHandTarget` + `IClimbable`. One `BoxCollider` over the whole ladder is the
  ray target; no per-rung colliders. Rungs are designer data (`firstRungHeight`,
  `rungSpacing`, real metres in the unscaled local frame - position + rotation, box size ×
  `lossyScale`), and the rung count is however many fit. `GetSnapPose()` snaps to the rung
  nearest the ray hit, on the box's centre line front to back, using a ledge-style grip frame
  and the `LadderRung` `HandSnapProfile`. Same orientation convention as `ClimbableEdge`.
  **Front only by default**: `CanBeTargetedFrom()` refuses rays whose origin is behind the
  rung line (-Z), unless `climbableFromBack` is ticked; then a grab from behind flips the
  grip frame to face +Z. **Top exit = a mantle**: either the ladder's own (`IMantleable`;
  `isMantleable`, on by default: with a hand on the top rung the arrow shows and the mantle
  lands on `mantlePoint`, unscaled local like an edge's, crouched if `mantleEndsCrouched`;
  `Reset()` / "Reset Mantle Point" put it level with the box top, 0.4m behind its back face)
  or a mantleable `ClimbableEdge` on the lip above. The top rung is found from the hand's
  grab point (`PlayerClimbing.LeftGrabPoint`/`RightGrabPoint`) with the same `NearestRung()`
  the snap uses. Gizmos: box outline, rails and rungs faintly; selected, bright with a
  sphere per rung, an arrow out of each climbing side and the mantle point.
- **`ClimbableRope`** — `IHandTarget` + `IClimbable`. A static line (no swinging) running
  from its transform's position along local -Y for `length` metres: unrotated it hangs
  straight down, turned on its side it's a rope strung between buildings. The grip frame
  comes from the rope's own direction.
  - **`endPoint`** (optional Transform): set, the rope runs from its position straight to
    that transform's, and its own rotation and `length` are ignored (`Length` returns the
    distance). Read at `Awake()`: the rope doesn't follow an end point that moves later.
  - **`sag`** (default 0 = straight, e.g. zip lines): how far the middle droops below the
    straight line between the ends, straight down in the world, as a parabola
    (`GetPoint(t)`, public - builders lay the visible rope along it). A sagging rope is
    `sagSegments` (8) straight pieces, a straight one 1. A rope that sags or has an end
    point (`UsesSegments`) can't use its own capsule: `Awake()` makes one trigger capsule
    per piece on a child object (`BuildSegments()`, all registered to the rope in
    `HandTargetRegistry`) and disables the object's own capsule. Set `sag`/`endPoint` before
    Play, keep such a rope at scale 1, and leave `sag` 0 on hanging ropes.
  - **Size:** it sizes its own `CapsuleCollider` (Awake, OnValidate, `SetLength()`),
    dividing real metres by scale, locks it (`HideFlags.NotEditable`) and always makes it a
    trigger - `length`/`grabRadius` on the rope are the only size controls, and the top (the
    object's position) stays put when `length` changes. `grabRadius` (0.08m) is far thicker
    than a rope so rays can hit it. A visible, solid rope is a child object (e.g. a thin
    Environment cylinder) inside the grab volume. `SetLength()` is for the rope bolt.
  - **Setup check** (`WarnAboutSetup()` from `OnValidate()`, clickable): not on the
    Climbable layer, or a non-trigger child collider reaching `grabRadius` or further from
    the axis (measured by the corners of the collider's own local box, not `Collider.bounds`,
    whose world-aligned box round a long angled rope is enormous). Skipped for a sagging
    rope.
  - **`GetSnapPose()`**: nearest point on the rope's pieces (clamped to the ends), grip
    frame facing into the rope with up along the rope towards its top, `RopeGrip`
    `HandSnapProfile`. **The side the hand grips from is fixed, not where the hand was**: on
    a hanging rope, the player's side (from `headPosition`) turned round the rope by
    `shoulderAngle` (20°) so the wrist points at that hand's shoulder - anticlockwise from
    above for the right hand, clockwise for the left; on a strung stretch, straight below
    (wrist hanging down). The grab side is only a fallback. **On a strung stretch**
    (direction within about 45° of level, `StrungMaxUpY`) the frame's up is whichever way
    along the rope points back towards the player - against the way their head faces
    (`headForward`), so the thumb points the same way relative to the player from either
    direction. Decided per grab.
  - **Zip line** (`isZipLine`, `IZipLine`): any grab slides the hand - and the player -
    along the rope at up to `zipSpeed` (6 m/s, reached at `zipAcceleration` 8 m/s²) until
    they let go or reach the end, then they drop. Direction (`GetZipDirection()`): downhill
    if the ends differ in height by more than 5cm, otherwise the way the player faces along
    it. A zip line can't be climbed hand over hand.
  - Gizmos: the line (or curve) always, faint; when selected, bright with each piece's
    capsule. Exits as ladders.

## Hand snap poses

The general "hand snaps onto a grab target" mechanism, used by ledges, ladders, ropes, props,
door handles, bolts, picks and keys. `HandPose` (enum of finger poses; `PlayerHandAnimation`
owns how each maps to the Animator), `HandSnapPose` (readonly struct: world position, rotation,
pose), `IHandSnapTarget.GetSnapPose(isLeftHand, grabPoint, headPosition, headForward)` (the head
values are for targets that snap differently by where the player is or faces: a two-sided
`ClimbableEdge` uses the position, a strung `ClimbableRope` the forward). The target computes a
model-agnostic grip frame; a shared `HandSnapProfile` ScriptableObject (Create > TeaLeaf > Hand
Snap Profile) applies per-hand position/rotation offsets in that frame, so all targets of one
kind are tuned in one asset. Profiles live in `Assets/Data/` (`LedgeGrip`, `LadderRung`,
`RopeGrip`, `BottleHold`, each with its own finger-pose clip and tuned in the headset; plus
the stand-in copies `DoorHandle` and `LockpickHold`). The profile's `pose` field picks the
finger pose: `LedgeGrip`, `RungGrip` (ladder rungs), `RopeGrip` (ropes) or `BottleHold`
(cylinder props: bottles, mugs); a target with no profile falls back to its own pose.
**Append new `HandPose` values at the end, never with explicit numbers** - profiles serialize
the pose as its number.
