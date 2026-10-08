# Interaction systems (`Assets/Scripts/Interaction/`, namespace `Interaction`)

Detail for hand targets, grabbable props, doors, climbables and hand snap poses. The root `CLAUDE.md` holds the project
rules, the tick order and the physics layers; this file is loaded when working in this folder.
Keep it up to date with every change to these systems, like the root file.

## Hand targets

- **`IHandTarget`** — interface for anything a hand ray can target (the reticle shows on it;
  other systems decide what targeting means). One member, `CanBeTargetedFrom(rayOrigin)`, a
  default interface method returning true - override it to refuse rays from some places
  (`Ladder` does, for its back). A refused ray still stops at the target but finds nothing: no
  reticle, no grab. `HasLongReach` (default interface property, false; added 2026-10-04):
  true lets the hand ray target it out to `PlayerHandInteraction`'s `pickUpRayLength`
  instead of `rayLength` - `Grabbable` returns true, climbables keep the short reach.
  **No highlighting** - removed 2026-09-27, the reticle is enough feedback.
  (Was `IHighlightable`.)
- **`HandTargetRegistry`** — static `Collider → IHandTarget` dictionary. Targets register in
  `OnEnable`/unregister in `OnDisable`, so hand raycasts do a dictionary lookup instead of an
  interface `GetComponent`.

## Grabbable props (added 2026-10-04)

- **`Grabbable`** — a physics prop a hand picks up, carries and drops (`IHandTarget` +
  `IHandSnapTarget`; `[RequireComponent(Rigidbody)]`; collider(s) on the Interactable layer,
  all registered with `HandTargetRegistry`). The carrying itself is `Player.PlayerHandHolding`
  (see `Scripts/Player/CLAUDE.md`). **Held at an authored pose** (maintainer's decision
  2026-10-04, over "stays as grabbed"): `gripPoint` (a child Transform, the grip frame; empty
  = the prop's own origin) and a shared `snapProfile` (`HandSnapProfile`; empty = the hand
  sits exactly on the grip frame). `GetSnapPose()` returns where the hand visual would have
  to be to hold the prop as it lies; the holder snaps the hand visual out to that pose, then
  carries the prop at the same pose relative to the hand as the hand comes back.
  **`gripShape`** (2026-10-04): `Point` (default) always uses the grip point, so the prop is
  held one way (the grab point is ignored) - for things with a handle. `Cylinder` (maintainer's
  request: a fixed grip point on the far side of a bottle made the hand or bottle flip round)
  grips like a rope: the grip point (or the prop's origin) is the middle of the grippable
  part with Y along the axis; `GetCylinderGripFrame()` puts the frame on the surface
  (`cylinderRadius`, 0.04m) level with the grab point (clamped to `cylinderGripLength`, 0.1m,
  centred on the grip point), facing straight in at the axis, up along the axis - so the
  prop always ends the same way up in the hand. **The side is the player's, from the head**
  (maintainer's request, later the same day; first built from the ray hit's side): towards
  `headPosition`, turned round the axis by `shoulderAngle` (20°) per hand, exactly as a
  hanging `ClimbableRope` does it; the ray hit's side, then the grip point's back, are only
  fallbacks. Selected (and in the headset), a cylinder draws its grippable part through
  `DebugLines` (`IDebugDrawable`). `holdRadius` (guessed from
  the colliders by `Reset()`) and `LocalCentre` (middle of the solid colliders, rotation-only
  local, measured in `Awake()`) describe the ball the hand's collision grows to while it's
  carried. `BeginHold(heldLayer)`: Rigidbody kinematic, every collider's object moved to the
  PlayerHands layer (original layers remembered). `EndHold(velocity, angularVelocity)`: non-kinematic,
  starting with the velocity and spin given - both zero is a drop from rest, anything else a
  throw (2026-10-07), which also switches the Rigidbody to `ContinuousDynamic` collision
  detection so a fast prop can't pass through a thin wall between physics steps; its own
  mode is put back by the next `BeginHold()` (a kinematic body doesn't support it).
  **Interpolation** (2026-10-07): `EndHold()` sets the Rigidbody to `Interpolate`,
  `BeginHold()` back to `None`. Physics steps 50 times a second (Fixed Timestep 0.02) and
  the headset draws 72-120, so an un-interpolated prop in flight looked like a low frame
  rate; while held it's placed through its parent, which interpolation would fight. A prop
  that has never been picked up keeps whatever the scene gave it: `Interpolate` on the test
  props (set by `GrabbableTestProps` and in the scene, 2026-10-07), so one knocked over
  before it's ever held moves smoothly too. Set it on any new prop.
  **A held prop pushes nothing** (maintainer's request 2026-10-07): `BeginCarry()` - called
  by the holder when the hand has reached the prop (`FinishReach()`), not at the start of
  the reach, so a stack on the prop falls as it's lifted rather than 0.12s early
  (maintainer's request) - sets `Rigidbody.detectCollisions` false, `EndHold()` true. During
  the reach the prop is kinematic but still solid, and doesn't move. A kinematic body otherwise shoves
  every physics body it's moved into (PlayerHands collides with Interactable), so a carried
  prop swept others off a table. It behaves like the hand visual, which has no collider: it
  stops at things only through the hand sweep's held shape. Just before that,
  `WakeNeighbours()` wakes every Rigidbody within 5cm of the prop's bounds (one
  `OverlapBoxNonAlloc`, shared 16-slot buffer): switching collisions off doesn't wake
  sleeping bodies as removing a collider would, so props stacked on the one taken hung in
  the air (found in the headset 2026-10-07).
  Either
  way it is **still on the PlayerHands layer** until
  `RestoreLayers()`, which the holder calls once the prop is clear of the hand (2026-10-04:
  restored at once, the hand's own collision pushed the hand visual off the prop it was
  still wrapped round, flashing the ghost hand). `IsHeld`, `WorldCentre`. **Finger pose:** the profile's `pose`, as for
  climbables - `BottleHold` (added 2026-10-04: enum value, `Hand_L_BottleHold` clip, Snap
  Pose layer state and the `BottleHold` profile) for cylinders; a prop with no profile
  shows `LedgeGrip` (value 0). Other prop shapes (cube, crate) have no hold pose yet.

- **`ImpactNoise`** (built and tested in the headset 2026-10-07) — a prop's sound
  when it hits something, on its Rigidbody object (`[RequireComponent(Rigidbody)]`); separate
  from `Grabbable`, so anything physical can have it. Nothing per frame: all in
  `OnCollisionEnter()`. **How hard** = `collision.impulse.magnitude / mass`, the speed the
  collision took off the prop in m/s (not `relativeVelocity`: a prop sliding onto the next
  floor piece starts a collision but loses no speed). Below `minSpeed` (1) silent; from there
  to `fullSpeed` (8) the volume goes `minVolume` (0.25) to 1 and the noise radius scale
  `minNoiseScale` (0.3) to 1, of the `cue`'s. **What it hit** =
  `SurfaceSounds.GetLoudness(SurfaceTag.Of(collision.collider))`, multiplying both (and
  `Play()`'s `muffle` = 1 - loudness, so a soft surface is duller too); `surfaceSounds` is
  optional (empty = everything is stone). One `SoundPlayer.Instance.Play()` does audio and
  noise, at the first contact point (`GetContact(0)`, no array), with the prop as the source;
  with no sound player in the scene, just `cue.EmitNoise()`. **Kept from repeating:**
  `minInterval` (0.15s) between sounds from one prop (bouncing, rattling to rest), and two
  props hitting each other play once - a static "last impact" step and position, skipped if
  the same physics step and within 0.3m. A kinematic prop (a hand reaching for it) is
  silent, and a carried one is out of physics, so it makes no impacts. The test props get
  one from `GrabbableTestProps` with the placeholder impact cue and surface sounds.

## Doors (started 2026-10-08; written, not yet compiled or tried in the headset)

The maintainer's design: three lock kinds (none, simple = pickable, keyed); any unlocked
door opens by its handle; the handle is ray-targeted and grabbed at the ledge reach, and the
hand snaps onto it; turning the hand 60° frees the door; it swings either way; let go
within a few degrees of closed, it shuts again; an open door is moved by the hand visuals
touching it (written 2026-10-08, second step, untested). **Still to build:** the keyhole view on a simple-lock door (head near the keyhole
shows a larger view through it); an optional sliding bolt, worked only from its own side
whatever the lock; keys and lockpicking (Phases 4-5).

- **`DoorLock`** (enum) — `None`, `Simple` (can be picked), `Keyed` (its own key only).
  Stored as its number: add values at the end.
- **`Door`** — on the hinge: the door turns about its local Y, X runs along it to the handle
  edge, Z is the way through. `[RequireComponent(Rigidbody)]`, made kinematic in `Awake()`.
  **The angle is set from code - no hinge joint, no forces** (decided 2026-10-08): it's
  predictable, cheap, and exact under a snapped hand; the Rigidbody is there so physics
  moves the collider cheaply and the door shoves loose props aside. **Two states, kept
  apart:** `IsLocked` (only ever true with a lock; `startsLocked`) and `IsLatched` (shut
  and caught; `IsOpen` is its opposite, whatever the angle). `Angle` is degrees from closed
  (positive towards local -Z), limited to `maxOpenAngle` (110) each way. `BeginHold()`/
  `EndHold()` (`IsHeld`: one hand at a time), `Unlatch(soundPosition)` (nothing if locked),
  `RattleLocked()`, `SetAngle()` (ignored while latched), `Lock()` (needs a lock and a shut
  door)/`Unlock()` - also right-click "Test Lock"/"Test Unlock" in Play Mode.
  `TryGetBearing(worldPoint, out degrees)` = which way round the hinge a point is, measured
  like `Angle`; false within 0.1m of the hinge line. **Moving with no hand on the handle**
  is the component's `Update()`, and **the component is disabled whenever the door is at
  rest or held**, so a door at rest costs nothing (and so it registers with
  `DebugDrawRegistry` and `HandPushRegistry` in `Awake()`/`OnDestroy()`, not
  `OnEnable()`/`OnDisable()`). `Update()`, in order: pushed this frame or last = nothing;
  still has speed = `TickSwing()`; stopped within `closeAngle` (10°) of closed = turns
  itself shut at `closeSpeed` (60°/s) and latches; stopped further open = stays, and
  disables itself. **Speed** (`_angularVelocity`, degrees a second): measured while a hand
  moves it (`TrackVelocity()`, half old and half new each frame, in `SetAngle()` and
  `Push()`), so it swings on when the hand lets go or stops pushing; `TickSwing()` turns it
  by its speed, which falls by `Exp(-swingDrag x time)` (`swingDrag` 3) and counts as
  stopped under `minSwingSpeed` (5°/s); it stops dead at a limit or against the player.
  **A door swinging by itself that reaches closed latches** - it only goes through its
  frame while a hand takes it through. **Pushed by hands** (`IHandPushable.Push(point,
  displacement)`, from the hand sweep): an open, unheld door turns by the change in bearing
  between the pressed point and that point moved by the push - so nearer the hinge turns it
  further, and a push along the door does nothing - at most `maxPushSpeed` (360°/s), once a
  frame. **`Physics.SyncTransforms()` after every move** (`MoveTo()`): Auto Sync Transforms
  is off in the project, so physics would otherwise see the leaf where it was at the last
  physics step and a hand would keep pushing a door that had already moved. **Never swings into the
  player**: before each move, one `Physics.CheckBox` of the leaf at the new angle against
  `blockingLayers` (Player) - worked out from the leaf's box measured in `Awake()`, nothing
  is moved to find out; blocked only if it isn't already overlapping, so it can always
  swing clear. A door shutting itself that meets the body stops and stays open. Optional
  `soundPortal` (closed while latched, open otherwise, set in `Start()` after the portal's
  own `Awake()`), `latchCue` (freeing and catching), `lockedCue`, `creakCue` (one-shot every
  `creakInterval` 25° of travel - no looping sounds yet); all through `SoundPlayer`, so
  guards hear them. `keyId` is stored but unused. Gizmo (selected / detailed): the swing arc
  on the floor with the limits and the close angle, red locked, yellow latched, green open.
- **`DoorHandle`** — `IHandTarget` + `IHandSnapTarget`; a trigger `BoxCollider` on
  Interactable, a child of the door on the spindle, mid-thickness, with the door's axes.
  **One handle serves both sides**: the grab volume goes through the door and out of both
  faces (further than anything solid), and the hand takes the lever on the side the
  **head** is on (`IsInFront()`), as a two-sided edge does. Grip frame: on the lever's
  surface (`standOff` 0.075 out, `gripAlong` -0.07 along X, `gripRadius`), facing into the
  door, up along the lever towards that hand's thumb (across the player's body, so opposite
  ways for the two hands and the two sides) - a hand held flat to the door then closed
  round the bar. `GetSnapPose(isLeftHand, isFront, leverAngle)` turns the whole pose about
  the spindle by the lever's angle and is called every frame the handle is held (the
  interface version uses the head's side and the current angle). `SetLeverAngle()` turns
  the optional `lever` transform (both levers under one object) about local Z, only on
  change; `GetGripPoint(isFront)`. The levers the player sees have no colliders. Setup
  check: trigger, Interactable layer. Short reach (`HasLongReach` false), like a ledge.
- **Hand pose:** the handle's profile is `Assets/Data/DoorHandle.asset`, made by the test
  builder as a **copy of `BottleHold`** (same finger pose) - a stand-in; tune its offsets in
  the headset, and give it a pose of its own if the bottle's looks wrong.
- **`DoorTestArea`** (`Interaction/Debug/Editor`, menu **TeaLeaf > Build Door Test Area**) —
  a 7m wall (Static, Environment) 2.5m ahead of the main camera with three 0.9 x 2.1m
  doorways: no lock, simple lock (starts locked), keyed lock (starts locked). Each door is
  an unscaled root on the hinge with a kinematic Rigidbody, a leaf, and a handle 1m up with
  a lever each side. Placeholder impact cue for the latch and rattle, no creak. Adds
  `PlayerHandDoors` to the Hands object if missing. No sound rooms or portal.

- **`IHandPushable`** / **`HandPushRegistry`** (2026-10-08) — something solid a hand moves
  by pressing on it: `bool Push(point, displacement)`, displacement being the part of the
  hand's blocked movement that went straight into the surface; true = it moved, and the
  hand then retries its move the same frame instead of stopping (the stutter fix). The registry is a static
  `Collider → IHandPushable` dictionary, like `HandTargetRegistry`; `Door` registers its
  leaf. `Player.HandPhysicalFollow` looks up every collider its sweep is stopped by (only
  on frames it hits something).

The player's half is `Player.PlayerHandDoors` (see `Scripts/Player/CLAUDE.md`).

## Climbables

- **Climbables have no mesh** — `ClimbableEdge`/`Ladder` are just a `BoxCollider` and
  `ClimbableRope` a `CapsuleCollider` (invisible in game), shown in the Scene view by always-on
  gizmos. A renderer is only added to an individual item that should be visible, as a separate
  component the scripts don't touch.
- **Grab volumes are bigger than the thing, and a hand inside one can grab** (2026-09-28) — a
  climbable's collider is a trigger grab volume, deliberately larger than any solid geometry
  it belongs to (Environment, so the physical hands stop against it), so it's easy to aim at.
  `PlayerHandInteraction` targets a hand target whose volume contains the hand before casting
  the ray, since a ray starting inside a collider never detects it - so a hand stopped
  against a solid rope, inside its grab volume, can still grab. Solid geometry must not reach
  as far as the grab volume's surface, or rays tie between the two and targeting flickers.
- **Gizmos show in the headset too** — all three are `IDebugDrawable`s: their gizmos are
  drawn by `DrawDebug(lines, detailed)` through `DebugLines`, called with `detailed` false from
  `OnDrawGizmos` and true from `OnDrawGizmosSelected`, and they register with
  `DebugDrawRegistry` in `OnEnable`/`OnDisable`. The same shapes then show in the headset
  while `InHeadsetGizmos` is on (see `Scripts/Core/CLAUDE.md`). `DrawDebug` uses the cached
  collider when `Awake()` has run, `GetComponent` otherwise (editor).
- **`IClimbable`** — extends `IHandSnapTarget`; marks a grab target as climbable (grabbing it
  starts a climb, unlike future handles/tools/props). Adds no members yet: every climbable
  moves the player the same way. `PlayerClimbing` finds it on a hand's ray target with a type
  check. Implemented by `ClimbableEdge`, `Ladder` and `ClimbableRope`.
- **`IMantleable`** (added 2026-10-03) — a climbable the player can mantle off the top of:
  `MantleEndsCrouched`, `CanMantleFrom(gripPoint, headPosition, headBelowTopAllowance)` (each
  target's own rule, called every frame a hand grips it) and `GetMantleLanding(feetPosition)`.
  `PlayerMantling` asks whatever each hand grips, so it never knows what kind of thing is
  being mantled. Implemented by `ClimbableEdge` (head pulled up to within the allowance of the
  ledge top) and `Ladder` (a hand on the top rung).
- **`IZipLine`** (added 2026-10-03) — a climbable that carries a gripping hand along itself:
  `IsZipLine`, `ZipSpeed`, `ZipAcceleration`, `Length`, `GetPoint(t)`, `GetClosestT(point)`,
  `GetZipDirection(headForward)` (t = 0-1 along the line). The line only describes itself;
  the sliding is `PlayerClimbing`'s. Implemented by `ClimbableRope`.
- **`ClimbableEdge`** — designer-placed `BoxCollider` that is the hand-ray target
  (`IHandTarget`, registered with `HandTargetRegistry`). Gizmos always draw its box faintly and
  its lip (top-front line) brightly. Implements `IClimbable` (so `IHandSnapTarget`): the snap
  point is the ray hit moved onto the box's top-front line (clamped to its length), facing into
  the wall - via `ClosestLipPoint()`, which mantling also uses for the ledge-top height.
  Mantling data (Mantling header): `isMantleable` (tick only on level edges),
  `mantleEndsCrouched`, and `mantlePoint` - where the feet always land, relative to the edge's
  origin in real metres along its axes (`MantlePointWorld` = position + rotation × point,
  **not** `TransformPoint()`, since ledges are stretched cubes). Defaulted from the box (top
  face, 0.4m in from the lip) by `Reset()` / the "Reset Mantle Point" context menu; a gizmo
  shows it while the edge is selected (the detailed view, which also redraws the box and lip,
  so a selected edge looks slightly brighter). **`moveHorizontallyToPoint`** ("Move
  Horizontally To Point", added 2026-09-30, default on = the original behaviour): on, a mantle
  always lands exactly on the mantle point; off, it lands straight ahead of the player - the
  mantle point's height and distance back from the lip, but the player's own position along
  the edge (feet measured square-on, clamped to the lip's ends). `GetMantleLanding(feet)`
  returns either, and is what `PlayerMantling` uses; `MantlePointWorld` is the fixed point.
  Off suits long edges (roof parapets, walls) where being carried sideways to one spot feels
  wrong; the gizmo then draws the landing line along the edge instead of one point.
  **`grabbableFromBothSides`** ("Grabbable From Both Sides", added 2026-10-03, default off =
  the original behaviour): for the top of something free-standing gripped from either side
  (wall, parapet, railing, window sill). On, `GetSnapPose()` snaps the hand onto the lip on
  the player's side - the top-front line from in front, the top-back line from behind, with
  the grip frame facing in from that side. The side is where the **head** is relative to the
  middle of the box front to back (`IsBehind()`), not the hand or the ray hit: a hand reaches
  over a wall top and a ray lands anywhere across it. The box must span the thing's whole
  thickness, overhanging both faces alike, so the back lip is a real one - which is why it's
  off by default and off for ledges set into a wall. One two-sided edge replaces two
  back-to-back edges: their volumes overlapped, a grab often got the far side's edge, and the
  hand snapped onto the far face, reversed. The gizmo draws both lips bright. Mantling is
  unchanged (same landing from either side, so use a top-centre mantle point).
  **Orientation convention:** local X runs along the edge, +Y is up, +Z points out from the
  wall towards the player.
  **Setup check:** `OnValidate()` warns (clickable) if the `BoxCollider` isn't a trigger or the
  object isn't on the Climbable layer. Added 2026-09-27 after three test cubes carried a
  `ClimbableEdge` on their own solid collider on the Climbable layer, so hands and the player's
  body passed through them. The right setup: solid ledge geometry on Environment, plus a
  separate trigger edge volume slightly larger, so hand rays hit it first rather than tying
  with the solid face.
- **`Ladder`** — `IHandTarget` + `IClimbable`. One `BoxCollider` over the whole ladder is the
  ray target; no per-rung colliders. Rungs are designer data (`firstRungHeight`,
  `rungSpacing`, real metres in the unscaled local frame - position + rotation, box size ×
  `lossyScale`), and the rung count is however many fit. `GetSnapPose()` snaps to the rung
  nearest the ray hit, on the box's centre line front to back, using a ledge-style grip frame
  and the `LadderRung` `HandSnapProfile`. Same orientation convention as `ClimbableEdge`.
  **Front only by default** (2026-09-28 - it was grabbable from both sides, and a back grab
  snapped the hand facing the wrong way): `CanBeTargetedFrom()` refuses rays whose origin is
  behind the rung line (-Z), unless `climbableFromBack` is ticked; then a grab from behind
  (hit point behind the rung line) flips the grip frame to face +Z.
  Gizmos: box outline, rails and rungs always drawn faintly; when selected, bright with a
  sphere per rung and an arrow out of each climbing side. **Top exit = a mantle**: either
  the ladder's own (`IMantleable`, added 2026-10-03 - `isMantleable`, **on by default**, so every
  ladder is mantleable unless unticked; a ladder saved before the option existed also comes
  out mantleable, with the field's fallback `mantlePoint` - run "Reset Mantle Point" on it:
  with a hand
  on the top rung the arrow shows and the mantle lands on `mantlePoint`, unscaled local like
  an edge's, crouched if `mantleEndsCrouched`; `Reset()` / "Reset Mantle Point" put it level
  with the box top, 0.4m behind its back face; shown as a gizmo when selected) or a mantleable
  `ClimbableEdge` on the lip above. The top rung is found from the hand's grab point
  (`PlayerClimbing.LeftGrabPoint`/`RightGrabPoint`) with the same `NearestRung()` the snap
  uses.
- **`ClimbableRope`** — `IHandTarget` + `IClimbable`. A static line (no swinging) running
  from its transform's position along local -Y for `length` metres: unrotated it hangs
  straight down, turned on its side it's a rope strung between buildings (2026-10-03). The
  grip frame comes from the rope's own direction, so a strung rope gets the hanging rope's
  pose turned to match with no extra setup. **`endPoint`** (optional Transform, 2026-10-03):
  set, the rope runs from its position straight to that transform's, and its own rotation and
  `length` are ignored (`Length` returns the distance) - place two ends instead of aiming the
  object; an empty child or an object on the other building both work. Read at `Awake()`: the
  rope doesn't follow an end point that moves later. **`sag`** (default 0 = straight, e.g. zip lines):
  how far the middle droops below the straight line between the ends, straight down in the
  world, as a parabola (`GetPoint(t)`, public - builders lay the visible rope along it);
  `length` is then the straight distance between the ends. A sagging rope is `sagSegments`
  (8) straight pieces, a straight one 1. A rope that sags or has an end point
  (`UsesSegments`) can't use its own capsule (it can't bend, and only points along local
  -Y): `Awake()` makes one trigger capsule per piece on a child object (`BuildSegments()`,
  all registered to the rope in `HandTargetRegistry`) and disables the object's own capsule;
  snapping, gizmos and `SegmentCount` use the same pieces. Set `sag`/`endPoint` before Play,
  keep such a rope at scale 1, and leave `sag` 0 on hanging ropes. The child-collider
  setup check is skipped for a sagging rope (it measures from a straight axis). Sizes its own
  `CapsuleCollider` (Awake, OnValidate, `SetLength()`), dividing real metres by scale, and
  locks it (`HideFlags.NotEditable`) and always makes it a trigger - `length`/`grabRadius` on
  the rope are the only size controls, and the top (the object's position) stays put when
  `length` changes. `grabRadius` (0.08m) is far thicker than a rope so rays can hit it. A
  visible, solid rope is a child object (e.g. a thin Environment cylinder) inside the grab
  volume. **Setup check** (`WarnAboutSetup()` from `OnValidate()`, clickable): not on the
  Climbable layer, or a non-trigger child collider reaching `grabRadius` or further from the
  axis (measured by the corners of the collider's own local box, so it errs towards warning;
  not `Collider.bounds`, whose world-aligned box round a long angled rope is enormous - until
  2026-10-03 that falsely warned about the angled zip line). Added 2026-09-28: the
  test rope's grab volume and solid cylinder were both 0.04m (and the grab volume not a
  trigger), so hand rays tied between them and the reticle flickered. `SetLength()` is for Phase 5's rope
  bolt. `GetSnapPose()`: nearest point on the rope's pieces (clamped to the ends), grip frame
  facing into the rope with up along the rope towards its top, `RopeGrip` `HandSnapProfile`.
  **The side the hand grips from is fixed, not where the hand was** (2026-10-03, so the grip
  looks the same every time): on a hanging rope, the player's side (from `headPosition`)
  turned round the rope by `shoulderAngle` (20°) so the wrist points at that hand's shoulder -
  anticlockwise from above for the right hand, clockwise for the left; on a strung stretch,
  straight below (wrist hanging down). The grab side is only a fallback. **On a strung stretch** (direction within
  about 45° of level, `StrungMaxUpY`) the frame's up is instead whichever way along the rope
  points back towards the player - against the way their head faces (`headForward`; up along
  the facing direction was tried first and put the thumb away from the player both ways), so the thumb points the same way relative to the
  player from either direction (2026-10-03: it was back to front when facing away from the
  rope's top). Decided per grab; hanging ropes are unaffected.
  **Zip line** (`isZipLine`, `IZipLine`, added 2026-10-03): any grab slides the hand - and
  the player - along the rope at up to `zipSpeed` (6 m/s, reached at `zipAcceleration` 8
  m/s²) until they let go or reach the end, then they drop. Direction
  (`GetZipDirection()`): downhill if the ends differ in height by more than 5cm, otherwise
  the way the player faces along it. A zip line can't be climbed hand over hand.
  Gizmos: the line (or curve) always, faint; when selected, bright with each piece's capsule.
  Exits as ladders.

## Hand snap poses

The general "hand snaps onto a grab target" mechanism, used by ledges, ladders and ropes now
and handles, tools and props later. `HandPose` (enum of finger poses; `PlayerHandAnimation`
owns how each maps to the Animator), `HandSnapPose` (readonly struct: world position, rotation,
pose), `IHandSnapTarget.GetSnapPose(isLeftHand, grabPoint, headPosition, headForward)` (the head
values are for targets that snap differently by where the player is or faces: a two-sided
`ClimbableEdge` uses the position, a strung `ClimbableRope` the forward). The target computes a
model-agnostic grip frame; a shared `HandSnapProfile` ScriptableObject (Create > TeaLeaf > Hand
Snap Profile) applies per-hand position/rotation offsets in that frame, so all targets of one
kind are tuned in one asset. Profiles live in `Assets/Data/` (`LedgeGrip`, `LadderRung`,
`RopeGrip`, `BottleHold`), each with its own finger-pose clip, all tuned in the headset
(rung and rope on 2026-09-30; `BottleHold` made 2026-10-04, confirmed 2026-10-07). The
profile's `pose` field picks the finger pose: `LedgeGrip`, `RungGrip` (ladder rungs),
`RopeGrip` (ropes) or `BottleHold` (cylinder props: bottles, mugs); a target
with no profile falls back to its own pose. **Append new `HandPose` values at the end, never
with explicit numbers** - profiles serialize the pose as its number.
