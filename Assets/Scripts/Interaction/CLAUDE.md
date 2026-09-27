# Interaction systems (`Assets/Scripts/Interaction/`, namespace `Interaction`)

Detail for hand targets, climbables and hand snap poses. The root `CLAUDE.md` holds the project
rules, the tick order and the physics layers; this file is loaded when working in this folder.
Keep it up to date with every change to these systems, like the root file.

## Hand targets

- **`IHandTarget`** — empty marker interface for anything a hand ray can target (the reticle
  shows on it; other systems decide what targeting means). **No highlighting** - removed
  2026-09-27, the reticle is enough feedback. (Was `IHighlightable`.)
- **`HandTargetRegistry`** — static `Collider → IHandTarget` dictionary. Targets register in
  `OnEnable`/unregister in `OnDisable`, so hand raycasts do a dictionary lookup instead of an
  interface `GetComponent`.

## Climbables

- **Climbables have no mesh** — `ClimbableEdge`/`Ladder` are just a `BoxCollider` and
  `ClimbableRope` a `CapsuleCollider` (invisible in game), shown in the Scene view by always-on
  gizmos. A renderer is only added to an individual item that should be visible, as a separate
  component the scripts don't touch.
- **`IClimbable`** — extends `IHandSnapTarget`; marks a grab target as climbable (grabbing it
  starts a climb, unlike future handles/tools/props). Adds no members yet: every climbable
  moves the player the same way. `PlayerClimbing` finds it on a hand's ray target with a type
  check. Implemented by `ClimbableEdge`, `Ladder` and `ClimbableRope`.
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
  shows it while the edge is selected.
  **Orientation convention:** local X runs along the edge, +Y is up, +Z points out from the
  wall towards the player.
- **`Ladder`** — `IHandTarget` + `IClimbable`. One `BoxCollider` over the whole ladder is the
  ray target; no per-rung colliders. Rungs are designer data (`firstRungHeight`,
  `rungSpacing`, real metres in the unscaled local frame - position + rotation, box size ×
  `lossyScale`), and the rung count is however many fit. `GetSnapPose()` snaps to the rung
  nearest the ray hit, on the box's centre line front to back, using a ledge-style grip frame
  and the `LadderRung` `HandSnapProfile`. Same orientation convention as `ClimbableEdge`.
  Gizmos: box outline, rails and rungs always drawn faintly; when selected, bright with a
  sphere per rung and an arrow out of the climbing side. Top exit = a mantleable
  `ClimbableEdge` on the lip above.
- **`ClimbableRope`** — `IHandTarget` + `IClimbable`. A static straight line (no swinging)
  hanging from its transform's position down local -Y for `length` metres. Sizes its own
  `CapsuleCollider` (Awake, OnValidate, `SetLength()`), dividing real metres by scale, and
  locks it (`HideFlags.NotEditable`) - `length`/`grabRadius` on the rope are the only size
  controls, and the top (the object's position) stays put when `length` changes. `grabRadius`
  (0.08m) is far thicker than a rope so rays can hit it. `SetLength()` is for Phase 5's rope
  bolt. `GetSnapPose()`: nearest point on the axis (clamped to the ends), grip frame facing
  into the rope from the side the ray hit (a rope has no front), `RopeGrip` `HandSnapProfile`.
  Gizmos: the line always, faint; when selected, bright with end rings and the capsule's sides.
  Exits as ladders.

## Hand snap poses

The general "hand snaps onto a grab target" mechanism, used by ledges, ladders and ropes now
and handles, tools and props later. `HandPose` (enum of finger poses; `PlayerHandAnimation`
owns how each maps to the Animator), `HandSnapPose` (readonly struct: world position, rotation,
pose), `IHandSnapTarget.GetSnapPose(isLeftHand, grabPoint)`. The target computes a
model-agnostic grip frame; a shared `HandSnapProfile` ScriptableObject (Create > TeaLeaf > Hand
Snap Profile) applies per-hand position/rotation offsets in that frame, so all targets of one
kind are tuned in one asset. Profiles live in `Assets/Data/` (`LedgeGrip`, `LadderRung`,
`RopeGrip` - the latter two still start from the ledge values).
