# Interaction systems (`Assets/Scripts/Interaction/`, namespace `Interaction`)

Detail for hand targets, climbables and hand snap poses. The root `CLAUDE.md` holds the project
rules, the tick order and the physics layers; this file is loaded when working in this folder.
Keep it up to date with every change to these systems, like the root file.

## Hand targets

- **`IHandTarget`** — interface for anything a hand ray can target (the reticle shows on it;
  other systems decide what targeting means). One member, `CanBeTargetedFrom(rayOrigin)`, a
  default interface method returning true - override it to refuse rays from some places
  (`Ladder` does, for its back). A refused ray still stops at the target but finds nothing: no
  reticle, no grab. **No highlighting** - removed 2026-09-27, the reticle is enough feedback.
  (Was `IHighlightable`.)
- **`HandTargetRegistry`** — static `Collider → IHandTarget` dictionary. Targets register in
  `OnEnable`/unregister in `OnDisable`, so hand raycasts do a dictionary lookup instead of an
  interface `GetComponent`.

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
  axis (measured by its bounds' corners, so it errs towards warning). Added 2026-09-28: the
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
`RopeGrip`), each with its own finger-pose clip, all tuned in the headset (rung and rope on
2026-09-30). The profile's `pose` field
picks the finger pose: `LedgeGrip`, `RungGrip` (ladder rungs) or `RopeGrip` (ropes); a target
with no profile falls back to its own pose. **Append new `HandPose` values at the end, never
with explicit numbers** - profiles serialize the pose as its number.
