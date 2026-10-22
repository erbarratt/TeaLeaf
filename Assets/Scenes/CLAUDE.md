# Level building guidelines (`Assets/Scenes/`)

Reference ("ref"): Thief VR: Legacy of Shadow prologue (Maze Theory; Unity 6, URP; shipped PCVR+Quest), numbers measured from its scene files; purposes inferred from names/layers, "inferred" where guessed. Deliberate TeaLeaf differences win (end of file).

## Ref in numbers

- ~72x42 m plan, 15 m tall (sewers -4, roofs +11); 3 street sectors ~25-35 m, 2 sewer runs. 24,800 env objects, 10,200 gameplay.
- 3,345 full-detail renderers (+410 LODs) static-batched into ~50 meshes. 165 materials; 4 (structure trim, daub walls, roof wood, roof slate) = 57% of slots; 65% renderers 1 material, 26% 2.
- 10,718 solid boxes, 7 mesh colliders; no convex mesh, capsules or spheres on static geometry.
- 46 Unity lights, 1 on at runtime (mixed point); 310 baked point lights (208 fill, 99 on visible lamps).
- 11 guards, 11 routes, 37 patrol points, 21 lookout points, 90 loot, 17 doors, 21 drawers, 44 climbables, 10 checkpoints, 56 sound rooms, 21 portals.

## Hierarchy

One scene; root objects stand in for ref's additive scenes.

```
Environment
  Sectors
    Global
      Set Dressing    props by named place (Market, Pantry, Exit Alley...)
      Decals
      Lighting
        Baked         bake-only fill lights
        Gameplay Lights   visible lamps/torches
        Light Probes      the one probe group (TeaLeaf > Lighting > Place Light Probes)
      VFX
    <Sector>          cullable as a whole
      Geometry        one group per building
        Generated     LevelGen.GeneratedRoot; replaced on regen - nothing hand-placed inside
  Player Blockers
  Occlusion           hand-placed culling areas
Gameplay
  Loot                by place, mission apart from generic
  Guards              per guard: guard + its own patrol points
  Triggers            checkpoints, objective, dialogue
  Doors, Keys, Throwables, Breakables, Documents ...
  Big Lock            the one Interaction.BigLock
Sound
  Rooms and Portals   by place, outdoor apart from indoor
```

- Sector = street/sewer run, ~25-35 m. Structure in its sector; small props in `Global/Set Dressing` by place (ref: 21).
- Group by place, then kind. No loose objects at a group's top level, no default names (`Cube`, `GameObject (3)`).

## Naming (project-made assets; variants keep pack names)

`P_` prefab, `M_` model, `Mat_` material, `T_` texture, `_LOD1`/`_LOD2`. Kit size in metres: `Wall_3X3M`, `OverhangFloor_3X1M`, `Wall_05X3M`. `_Merged` = props merged to one mesh; `_Atlased` = shared atlas.

## Placed piece

```
P_Pr_Barrel_01a   empty root: only object moved/turned/scaled (TeaLeaf: the Assets/Prefabs variant)
  Model           LODGroup, child per LOD
  Collision       empty; one child per collider (what the variant adds)
```

- One collider per object, named `Collider`, never on the renderer's object.
- Colliders may scale unevenly; renderers never (breaks batching, bends lighting; ref: 94 of 3,755 uneven, 2 mirrored).

## Modular kit

- 1 m grid, 3 m storeys. Ref module sizes by use: 1x1, 1x3, 3x1, 3x3, 2x3, 0.5x3, 1.5x3; 6x3, 6x4 rare.
- Wall, window-wall, stone-bottom-wall, door modules share sizes (swappable). Roofs (angles, edges, corners, trim) and overhanging floors (brackets, beams) are own kits.
- Building = one prefab (`P_PL_Building10_Merged`), one `Collision` child (100-300 boxes, max 678).

## Collision

- Authored, never the render mesh. Hand-fitted primitives (ref: boxes even for curves, barrel = 56; half of pieces ≤3 boxes).
- Thin, small boxes: median thinnest side 7 cm, 95% ≤3 m long - one per wall module, not a slab per wall (safe: slow capsule, swept bolts).
- Only what the player can reach or see past: none on roof trim, high beams, background buildings.
- Steps are ramps (`RampCollider`).
- Every solid collider has a `SurfaceTag` on it or a parent (one on a building's `Collision` covers it; nearer tags where parts differ). Ref: sound profile (16 kinds) on ~half its colliders.

## Player blockers

Ref: 86; 69 in one root group, named `InvisibleWall_Col`; a few in their prefab (A-board).

- **TeaLeaf:** empty object, `BoxCollider` (not trigger), layer `PlayerBlocker`, under `Player Blockers`; no script/renderer, not Static. Stops only `CharacterController.Move()`: no gameplay shadow, hides nothing from guards, not in NavMesh bake (guards walk through; never use to keep a guard out).
- Uses (inferred): **wrap clutter** (most; stall, shelves, crates, log pile; median 13 colliders, max 96) so the body slides on one face and can't wedge, hands still reach in; **close gaps** (15-30 cm plates across gates, between buildings); **keep body in level** (7-9 m tall along walkway edges, behind pipe platforms).
- Plain upright box (yaw only), typically 1.2x2.5x1.0 m: full body height so it can't be jumped/mantled onto.
- Doesn't stop mantles (rig moved with CharacterController off) or climbing: keep mantle landing points and climbable edges out of them.
- Never the only thing in the way: round real collision or across a gap that looks closed; no invisible walls in open space.

## Layers

| Ref layer | On | TeaLeaf |
|---|---|---|
| `Default` | buildings, floors, structure | `Environment` |
| `Cover` | sub-wall props: barrels, chairs, stalls, racks, crates (5,500 boxes); inferred: guards' partial cover | `Environment` |
| `PlayerBlocking` | blockers | `PlayerBlocker` |
| `Door` | door leaves | `Interactable` |
| `Grabbable` | loot, props | `Interactable` |
| `InteractionTriggers` | checkpoint/objective/dialogue triggers | maths boxes, no collider (`ExitZone`) |

- Every object of a static piece on `Environment`, else `Default` (collides with all; magenta via `DefaultLayerGizmos`).
- No wrong-layer collider to "fix" one case (ref's rug `Collider-LayerOverrides`); a TeaLeaf rug has no collider, `SurfaceTag` on a thin floor covering.

## Static, batching, materials

- Every static renderer has every static flag (ref 3,355 of 3,755; rest LOD parents); Make Environment Variants sets this, minus Batching Static on probe-lit renderers.
- Few shared materials (ref: district from 4 structural + prop atlases `BarrelsCratesBaskets_Atlas`, `MarketStalls_Atlas`); question a material for one prop.
- Never-moving prop clusters merged, one material where atlased (`ExitAlley_Crates_Merged`).
- Ref shadows: 97% static renderers cast, all receive (baked).

## LODs

Ref: 350 groups, ~1/3 each one/two/three levels (one = cull only). Usual: full to 25% screen height, culled <1%; three-level switch ~17%, 10%. Small props get them; kit walls/roofs mostly not.

## Lighting

- Ref: baked, 1 runtime light; every visible lamp a baked light + model; mood from 208 model-less fills. Lamps 6-8 m range typical, 29 m max. 5 reflection probes (one per sector). Exponential fog 0.015 (TeaLeaf: haze is a reserve, only if profiling asks).
- Fill and gameplay lights in separate groups so stealth lamps can be counted (ref 61).
- TeaLeaf lamp/torch: `LightSource` + `Light`, + `Flame` only if an open flame (torch, fire pit; lamp posts burn on); under `Lighting/Gameplay Lights`. Generator lamp posts (`TL_Lampost`): Mixed, `lampSpacing` apart, none overlapping, under the generated root (`Assets/Scripts/LevelGen/CLAUDE.md`).

## Culling

- Layout first: narrow alleys/doorways so no view shows more than one sector plus part of the next. Then baked occlusion.
- Hand-placed culling areas (ref 9): 2-4-box trigger hiding a hand-picked renderer/group list while inside, 50 switched per frame (no spike); for what the bake can't know (interior behind a never-opening window).
- Animated things (fans) have their own culler to stop animators unseen.

## Sound

- Room per street segment and per interior room, not per building (ref 56 rooms, 21 portals; more outdoor than indoor).
- Two points both in no room hear each other directly, so streets that shouldn't need rooms. Generator gives its streets rooms (per straight run, junctions, portals between and to sky; `Assets/Scripts/LevelGen/CLAUDE.md`, Sound rooms); its buildings' rooms and door/window portals are hand-placed.
- Loops sit on the source (ref 74: fans, fires, drips), not an audio group.

## Gameplay content

- Loot by area, mission apart from generic (area worth readable).
- Lookout points (ref 21) separate from patrol points.
- Crouch spaces marked, not detected: thin box under a table (1.4x0.1x1.2 m).
- Scripted-moment blockers (table across a doorway until an objective) live in gameplay content.

## Reviewing a scene (in order)

1. **Roots**: groups above exist; sectors cullable size; nothing loose, no default names.
2. **Layers**: no collider on `Default`; static pieces `Environment` all the way down; doors/props `Interactable`; climbables `Climbable`.
3. **Static flags**: all on never-moving renderers, except probe-lit (Receive GI: Light Probes, or not Contribute GI) not Batching Static (`Assets/Scripts/Core/CLAUDE.md`); none on moving things (doors, swaying foliage).
4. **Collision**: no render mesh as collider; under `Collision`, one per object; nothing reachable without, nothing unreachable with; steps ramps; no gap a 0.5 m body could wedge into. **Blockers**: upright, `PlayerBlocker`, in `Player Blockers`, round clutter/across gaps; none in open space or holding a mantle landing point or climbable edge.
5. **Gameplay shadows**: every rendered shadow caster has `Environment`/`Interactable` collision covering its silhouette, no holes in walls/roofs (`SceneLight` reads colliders; a collider-less roof lights the player).
6. **Surfaces**: non-stone floors have a `SurfaceTag`; bolt-holding surfaces tagged; rope-bolt surfaces marked.
7. **Scale**: no uneven/negative renderer scale; structure on 1 m grid.
8. **Materials**: count per sector; question any used once.
9. **Lights**: gameplay apart from fill; each a short-range `LightSource`; ≤4 mixed overlapping anywhere.
10. **Sightlines**: from each end of every street, >1 sector in view needs a bend, arch or door.
11. **Sound**: `SoundRoom` per interior room and street segment; `SoundPortal` per opening between rooms, none drawn yellow.
12. **Navigation**: NavMesh rebaked since geometry changed.

## Deliberate differences from ref

- Gameplay light by rays against colliders (`SceneLight`), not ref's per-lamp trigger volumes (sphere/cone, 2-5.5 m) - hence item 5.
- Curved props: one shared convex mesh (`BarrelCollider`), not 56 boxes.
- Level-end/trigger volumes are maths boxes, not trigger colliders.
- One scene, not four additive.
- Realtime torch shadows allowed as a quality setting; ref bakes all.
