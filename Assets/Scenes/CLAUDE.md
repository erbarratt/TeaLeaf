# Level building guidelines (`Assets/Scenes/`)

How a TeaLeaf level is laid out in the hierarchy, how its collision, layers, lighting and
culling are authored, and what to check when reviewing a scene. The root `CLAUDE.md` holds
the performance rules, the physics layers and the art rules; this file is the working
detail for building and reviewing levels.

**The reference is the prologue of Thief VR: Legacy of Shadow** (Maze Theory; Unity 6, URP),
a shipped PCVR and Quest stealth level of the kind TeaLeaf is building. Its numbers below
were measured from its scene files. What a thing is *for* is inferred from its name, layer
and collision matrix, and is marked "inferred" where that is a guess. A reference, not a
rulebook: where TeaLeaf does something differently on purpose, the TeaLeaf rule wins and
the difference is listed at the end.

## The reference level in numbers

- **Size:** about 72 m x 42 m on plan and 15 m top to bottom (sewers at -4 m, rooftops at
  +11 m). Three street sectors, each roughly 25-35 m across, and two sewer runs.
- **Objects:** 24,800 in the environment scene, 10,200 in the gameplay scene.
- **Drawn things:** 3,345 mesh renderers at full detail (410 more are lower LODs), merged
  by static batching into about 50 combined meshes. **165 materials**, and four of them
  cover 57% of all material slots (structure trim, daub walls, roof wood, roof slate).
  65% of renderers have one material, 26% two.
- **Collision:** 10,718 solid box colliders and **7 mesh colliders**. No convex mesh
  colliders, no capsules or spheres for static geometry.
- **Lights:** 46 Unity lights, **one of them on at runtime** (a mixed point light); the
  rest are baked. 310 baked point lights from the lightmapper (208 general fill, 99 on the
  lamps the player sees).
- **Gameplay scene:** 11 guards, 11 patrol routes, 37 patrol points, 21 lookout points,
  90 loot items, 17 doors, 21 drawers, 44 climbables, 10 checkpoints, 56 sound rooms and
  21 sound portals.

## Hierarchy

The reference splits a level into additive scenes (`_EnvironmentScene`, `_GameplayScene`,
`_AudioScene`, `_DebugScene`). TeaLeaf has one scene, so the same split is made with root
objects: everything static and visual under one root, everything that plays under another.

```
Environment
  Sectors
    Global            things that belong to no one sector
      Set Dressing    props, grouped by named place (Market, Pantry, Exit Alley, ...)
      Decals
      Lighting
        Baked         fill lights that only exist for the bake
        Gameplay Lights   the lamps and torches the player sees and can put out
        Light Probes      the level's one light probe group (TeaLeaf > Place Light Probes)
      VFX
    <Sector name>     one per part of the level that can be culled as a whole
      Geometry        buildings and structure, one group per building
        Generated     what the level generator owns (LevelGen.GeneratedRoot); replaced
                      on every regeneration, so nothing hand-placed goes inside it
  Player Blockers     (see below)
  Occlusion           hand-placed culling areas
Gameplay
  Loot                grouped by place, then mission loot apart from generic loot
  Guards              one group per guard: the guard and its own patrol points
  Triggers            checkpoints, objective and dialogue triggers
  Doors, Keys, Throwables, Breakables, Documents ...
  Big Lock            the one big lock shown while picking (Interaction.BigLock)
Sound
  Rooms and Portals   grouped by place, outdoor apart from indoor
```

- **A sector is a part of the level that can be hidden as a whole** (a street, a sewer
  run), about 25-35 m across. Structure goes in its sector; small props go in `Global/Set
  Dressing` under the name of the place they dress (the reference has 21 such places).
- **A guard's patrol points live with the guard**, not in a shared pool.
- **Group by place, then by kind.** A reviewer should be able to find everything in the
  market by opening one group per root.
- **No loose objects at a group's top level** and no Unity default names (`Cube`,
  `GameObject (3)`): the reference's few are leftovers.

## Naming

- `P_` a placed prefab, `M_` a model inside it, `Mat_` a material, `T_` a texture. Lower
  LODs end `_LOD1`, `_LOD2`.
- A kit piece carries its size in metres in its name: `Wall_3X3M`, `OverhangFloor_3X1M`,
  `Wall_05X3M`.
- A prefab made by merging several props into one mesh ends `_Merged`; one whose textures
  share an atlas ends `_Atlased`.
- TeaLeaf's variants keep the pack's own names (see the root file); these prefixes are for
  what the project makes itself.

## How a placed piece is built

```
P_Pr_Barrel_01a          empty root: the only object that is moved, turned or scaled
  Model                  LODGroup, with one child per LOD holding the mesh and renderer
  Collision              empty; its children are the colliders, one per object
```

- **The model and the collision are separate children**, so either can be changed without
  touching the other.
- **One collider per object**, named `Collider`, never on the renderer's own object (18
  of 10,700 in the reference break this).
- **Collider objects may be scaled unevenly to size a box; renderers are not** (94 of
  3,755 renderers are, and 2 are mirrored). Uneven scale on a renderer breaks batching and
  bends its lighting.
- In TeaLeaf the root is the prefab variant in `Assets/Prefabs`, and the collision child
  is what the variant adds.

## The modular kit

- **Everything structural sits on a 1 m grid with 3 m storeys.** Module sizes in the
  reference, most used first: 1x1, 1x3, 3x1, 3x3, 2x3, 0.5x3, 1.5x3. Bigger pieces (6x3,
  6x4) are rare.
- **Walls are built from wall, window-wall, stone-bottom-wall and door modules of the same
  size**, so any one can be swapped for another.
- **Roofs are their own kit** (angle pieces, edges, corners, trim), as are overhanging
  upper floors with their brackets and beams: the silhouette is built from parts, not
  modelled per building.
- A whole building is then saved as one prefab (`P_PL_Building10_Merged`), with its
  collision as one `Collision` child holding all its boxes (100-300 for a building, 678
  for the largest).

## Collision

- **Collision is authored, never taken from the render mesh.** No level mesh in the
  reference is its own collider.
- **Primitives fitted by hand**: the reference uses boxes for everything, including
  curves (a barrel is 56 boxes in rings; see "Differences" for what TeaLeaf does instead).
  Half its pieces have 3 boxes or fewer.
- **Boxes are thin and small**: the thinnest side is 7 cm at the median, and 95% of boxes
  are 3 m or less on their longest side - a box per wall module, not one slab per wall.
  Small boxes keep each one tight to the mesh; thin plates are safe because the player is
  a slow capsule and bolts are swept.
- **Collide with what the player can reach or see past, and nothing else.** Roof trim,
  high beams and background buildings have no collision in the reference (its one
  background building has none at all).
- **Steps are ramps** (`RampCollider` in TeaLeaf).
- **Every solid collider says what it is made of.** About half the reference's colliders
  carry a sound profile directly (16 kinds). In TeaLeaf that is a `SurfaceTag`, on the
  collider or any parent - so one tag on a building's `Collision` child covers all of it,
  with a nearer tag where a part differs.

## Player blockers

Invisible boxes that only the player's body collides with. The reference has 86.

- **Their own layer, which collides with the player's body and nothing else**: hands,
  held props, thrown objects, bolts and guards all pass through, and no ray hits it.
- **Kept together in one root group** (`Player Blockers`, 69 of them), named
  `InvisibleWall_Col`. A few live inside the prefab they belong to (an A-board carries its
  own).
- **What they are for** (inferred from what each one encloses):
  - **Wrapping clutter.** Most enclose a cluster of detailed prop collision - a market
    stall, a rack of shelves, a stack of crates, a log pile: 13 colliders at the median,
    96 at most. The body then slides along one flat face instead of catching on dozens of
    edges, and cannot wedge itself into a gap between props, while hands still reach in
    to take things off the shelf.
  - **Closing gaps** the body should not squeeze through: plates 15-30 cm thick across a
    fence gate or between two buildings.
  - **Stopping the body leaving the level**: tall ones (7-9 m) along walkway edges and
    behind pipe platforms.
- **Shape:** always upright (turned about the vertical axis only), a plain box. Typically
  1.2 m x 2.5 m x 1.0 m: full body height even round waist-high clutter, so the player
  cannot jump or mantle onto it.
- **In TeaLeaf:** an empty object with a `BoxCollider` (not a trigger) on the
  **`PlayerBlocker`** layer, under the `Player Blockers` root group. No script, no
  renderer, not Static. The layer collides with `Player` only and is in no query mask, so
  a blocker stops `CharacterController.Move()` and nothing else: it casts no gameplay
  shadow, hides nothing from a guard, and is left out of the NavMesh bake (guards walk
  through one, so never use one to keep a guard out).
- **A blocker does not stop a mantle or a climb**: a mantle moves the rig straight to its
  landing point with the CharacterController off, and climbing moves the body by the
  hands. Keep mantle landing points and climbable edges out of blockers, or the player
  arrives inside one.
- **Never the only thing in the way.** A blocker goes round real collision or across a
  gap that looks closed; the player should not meet an invisible wall in open space.

## Layers

The reference splits static collision by what it means to the game, not by what it looks
like:

| Reference layer | On | TeaLeaf |
|---|---|---|
| `Default` | buildings, floors, structure | `Environment` |
| `Cover` | props smaller than a wall: barrels, chairs, stalls, racks, crates (5,500 boxes). Inferred: what guards treat as partial cover | `Environment` |
| `PlayerBlocking` | player blockers | `PlayerBlocker` |
| `Door` | door leaves | `Interactable` |
| `Grabbable` | loot and props that are picked up | `Interactable` |
| `InteractionTriggers` | checkpoints, objective and dialogue triggers | maths boxes, no collider (`ExitZone`) |

- **A static piece is on `Environment`, every object in it**, or it stays on `Default`,
  which collides with everything and is outlined in magenta by `DefaultLayerGizmos`.
- **A collider is never left on a trigger-less object on the wrong layer to "fix" one
  case.** The reference does this for rugs (`Collider-LayerOverrides`); in TeaLeaf a rug
  simply has no collider and a `SurfaceTag` on a thin floor covering.

## Static, batching and materials

- **Every static renderer has every static flag** (3,355 of 3,755 in the reference; the
  rest are LOD parents). In TeaLeaf, TeaLeaf > Make Environment Variants sets this.
- **Few materials, shared widely.** The reference draws a whole district with four
  structural materials plus atlases for props (`BarrelsCratesBaskets_Atlas`,
  `MarketStalls_Atlas`). A new material for one prop is the thing to question.
- **Clusters of props that never move are merged into one mesh** with one material where
  they share an atlas (`ExitAlley_Crates_Merged`).
- **Shadows:** 97% of static renderers cast them and all receive, because the shadows are
  baked and cost nothing at runtime.

## LODs

- 350 LOD groups: about a third have one level (the group only culls the piece when it is
  small), a third two, a third three.
- **The usual set-up is full detail down to 25% of screen height, then gone below 1%.**
  Three-level pieces switch near 17% and 10%.
- Small props are the ones with LOD groups; kit walls and roofs mostly have none, since
  they are large on screen whenever they are visible.

## Lighting

- **Baked, with one light on at runtime.** Every lamp the player sees is a baked light
  with a lamp model; the level's mood comes from 208 extra baked fill lights that have no
  model at all.
- **Fill lights and gameplay lights are separate groups**, so the lamps that matter to
  stealth can be found and counted (61 in the reference).
- **Lamp ranges are short**: 6 m to 8 m typically, 29 m at most.
- **Reflection probes are few**: five, one per sector.
- **Fog is on**: exponential, density 0.015 (a haze that takes the far end off a long
  street). In TeaLeaf distance haze is a reserve, tried only when profiling asks.
- In TeaLeaf a lamp or torch is a `LightSource` with a Unity `Light` beside it, and a
  `Flame` if it can be put out; it goes under `Lighting/Gameplay Lights`.

## Culling

- **Layout first**: the reference's three streets are joined by narrow alleys and
  doorways, so no view takes in more than one sector and part of the next.
- **Baked occlusion culling**, with the static flags above.
- **Hand-placed culling areas on top** (nine): a trigger volume made of two to four boxes
  that, while the player is inside, hides a hand-picked list of renderers and groups,
  switching 50 a frame so the change never costs a spike. They cover what the bake cannot
  know - the inside of a building seen through a window that never opens.
- Animated things (fans) carry their own culler so their animators stop when unseen.

## Sound

- **Rooms and portals, as in TeaLeaf**: 56 rooms and 21 portals for this one level, so
  expect a room for every street segment and every interior room, not one per building.
- **Outdoors is split into rooms too** (the reference has more outdoor rooms than indoor
  ones). In TeaLeaf two points that are both in no room always hear each other directly,
  so streets that should not hear each other need rooms.
- **Loops sit on the thing that makes the sound** (74 in the reference: fans, fires,
  drips), not in a separate audio group.

## Gameplay content

- **Loot is placed by area, with mission loot apart from generic loot**, so the total
  worth of an area can be read off the hierarchy.
- **Lookout points** (21) are separate from patrol points: places a guard stops and looks
  from.
- **Crouch spaces are marked**, not detected: a thin box on the underside of a table
  (1.4 m x 0.1 m x 1.2 m) flags a place the player can crouch under.
- **Blockers that belong to a scripted moment** (a table across a doorway until an
  objective is met) live with the gameplay content, not with the environment's.

## Reviewing a scene

Check, in this order:

1. **Roots and grouping**: the root groups above exist; sectors are of a size that can be
   culled; nothing loose, no default names.
2. **Layers**: nothing with a collider left on `Default`; static pieces on `Environment`
   all the way down; doors and props on `Interactable`; climbables on `Climbable`.
3. **Static flags**: every renderer that never moves has them all; nothing that moves
   (doors, swaying foliage) has any.
4. **Collision**: no render mesh used as its own collider; colliders under a `Collision`
   child, one per object; nothing reachable without collision, nothing unreachable with
   it; steps are ramps; no gap a 0.5 m wide body could wedge into.
   **Player blockers**: upright boxes on `PlayerBlocker`, in the `Player Blockers` group,
   round clutter clusters and across gaps; none in open space, none holding a mantle
   landing point or a climbable edge.
5. **Gameplay shadows**: every mesh that casts a rendered shadow has collision on
   `Environment` or `Interactable` covering its silhouette, with no holes in walls or
   roofs - `SceneLight` reads shadows from colliders, so a roof with no collider is a roof
   the player is lit through.
6. **Surfaces**: every floor that is not stone has a `SurfaceTag`; anything a bolt should
   stick in is tagged, and rope-bolt surfaces are marked.
7. **Scale**: no uneven or negative scale on renderers; structure on the 1 m grid.
8. **Materials and batching**: count the materials in a sector; question any used once.
9. **Lights**: gameplay lights apart from fill lights; each a `LightSource` with a short
   range; no more than four mixed lights overlapping anywhere.
10. **Sightlines**: stand at each end of every street; if more than one sector is in
    view, the layout needs a bend, an arch or a door.
11. **Sound**: every interior room and street segment has a `SoundRoom`; every opening
    between two rooms has a `SoundPortal`, none drawn yellow.
12. **Navigation**: the NavMesh has been baked again since the geometry changed.

## Where TeaLeaf differs from the reference on purpose

- **Gameplay light is worked out, not placed.** The reference gives every lamp a trigger
  volume (a sphere or cone, radius 2-5.5 m) that says "the player is lit here". TeaLeaf
  uses rays against colliders (`SceneLight`), with nothing placed by hand but the lights.
  This is why rule 5 above exists and has no counterpart in the reference.
- **Curved props use one shared convex mesh, not rings of boxes** (`BarrelCollider`): one
  shape per barrel instead of 56.
- **Level-end and trigger volumes are maths boxes, not trigger colliders.**
- **One scene**, not four additive ones.
- **Realtime torch shadows are allowed** as a quality setting; the reference bakes
  everything.
