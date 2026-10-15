# Level generator (`Assets/Scripts/LevelGen/`)

An editor tool that builds a level's streets and buildings from the game's own prefab
variants in `Assets/Prefabs`, from a layout spec that a written brief is turned into. It
places real prefab instances under a root of its own and can be run again without
touching anything placed by hand. It generates no lighting, set dressing or gameplay.

**State:** being built in stages (below). Stage 1 (the catalogue scanner) is built and
run: `catalogue.json` holds the `Environment` prefabs (188 once `Containers` and
`Interactive` are no longer scanned). Stage 2 (the
Review Pieces window) is built; no piece has been reviewed yet. Stages 3-6 are not
built.

## Rules

- **Read-only towards assets.** Nothing in `Assets/ASTROFISH_GAMES` or `Assets/Prefabs` is
  modified, moved, renamed or saved by the generator. Prefabs are only loaded to be
  measured and instantiated.
- **Real prefab instances only**, through `PrefabUtility.InstantiatePrefab`, so every
  placed piece keeps its link to its variant. No substitute geometry.
- **Only verified pieces are placed.** A piece's front, pivot, footprint and connection
  points are confirmed by the maintainer (stage 2) before the generator will use it.
  Names are never trusted for direction.
- **Repeatable:** the layout spec holds the seed; all randomness goes through one
  `System.Random` made from it, in a fixed order. The same spec and catalogue give the
  same level.
- **Buildings are built from component parts** - wall, corner, window, doorway,
  foundation, floor and roof pieces - **never from the pack's ready-made buildings or
  building storeys**: `Building_Exteriors` and `Building_Layers` are left out of the
  catalogue, and a prefab made of other prefabs (flagged `assembly`) is only used if
  it is marked so by hand.
- **Lights inside placed prefabs are disabled on the instance** (an override on the
  placed object, the prefab untouched). Level lighting is placed by hand, to the rules
  in `Assets/Scenes/CLAUDE.md`.
- **Gameplay doors are never generated** (`Assets/Prefabs/Interactive`, outside the
  catalogue).
- Editor-only code in `LevelGen/Editor`, namespace `LevelGen`; the one runtime component is
  the `GeneratedRoot` marker.

## The brief and the layout spec

A written brief is turned into a **layout spec** (a JSON file in `Assets/Data/LevelGen`)
by Claude in the conversation, not by an API call from the editor. The spec is what the
generator reads, and can be read and edited by hand. It holds the seed, the streets, the
lots along them and, per lot, the building's footprint, storeys and style tags. The tool
checks a spec against the catalogue before building and reports what it can't satisfy.

**Layouts need not follow a world grid.** A street is a polyline of points at any angle
(a curve is several short segments); a lot has its own origin and yaw. Pieces fit
together on a grid only *within* one lot or one street segment's frame, so a building is
square in itself however it is turned. Where two frames meet at an angle (a bend, a
junction, a lot set askew to its street), the gap or overlap left is measured and shown
in the report rather than hidden.

## Data

- `Assets/Data/LevelGen/catalogue.json` — written by the scanner, one entry per variant:
  asset path and GUID, folder, measured bounds (LOD0 renderers and colliders, relative to
  the root), pivot offset, components, nested prefabs, lights, LODs. Rescanned at any time;
  never edited by hand.
- `Assets/Data/LevelGen/annotations.json` — the maintainer's per-prefab facts, kept apart
  so a rescan never overwrites them, written by the Review Pieces window (stage 2,
  below). A prefab with no entry hasn't been looked at; only pieces `verified` and not
  `excluded` are placed.
- Layout specs, one file per generated area.

## Stages

1. **Catalogue scanner** — measures every variant and writes `catalogue.json`.
2. **Annotations and verification** — a Scene view tool that shows a piece's pivot,
   measured bounds and assumed front for the maintainer to confirm into
   `annotations.json`.
3. **Layout spec** — the format and its validation.
4. **Streets and ground** — ground tiles along each street segment and at junctions.
5. **Buildings and hierarchy** — each lot's building assembled from parts within the
   lot's frame: foundations round the footprint, then per storey wall modules along each
   side (plain, window and door modules being the same width, so any can stand in for
   another), corner pieces where sides meet, floors, and a roof from the roof kit;
   the front faces the street. Placed under
   `Environment/Sectors/<Sector>/Geometry/Generated` (`GeneratedRoot`), which a regeneration
   replaces whole. A regeneration warns first if anything inside it was edited by hand.
6. **Report** — missing or unverified assets, footprint mismatches, overlaps, gaps along
   streets and between storeys, unconnected connection points, anything not placed.

## Catalogue scanner (stage 1)

- **`PrefabCatalogueScanner`** (`LevelGen/Editor`, menu **TeaLeaf > Level Gen > Scan
  Prefab Catalogue**) — every prefab under `Assets/Prefabs/Environment` (`ScanRoot`:
  level art only, so gameplay prefabs such as `Interactive` and `Core` are never
  catalogued) except `Building_Exteriors` and `Building_Layers` (`_excludedFolders`),
  sorted by path, is opened
  with `PrefabUtility.LoadPrefabContents` (a preview scene) and unloaded unsaved, and
  `catalogue.json` is rewritten only if its text changed. No date in the file, and every
  size is rounded to the millimetre, so rescanning unchanged prefabs leaves it as it was.
  `JsonUtility` writes floats in full (0.3 as 0.30000001192092898), so every number
  outside a string is written back with at most three decimals (`ShortenNumbers`).
  One Console entry: counts per flag, per suggested orientation and per folder.
- **`PrefabCatalogue`** (`LevelGen/Editor`) — the file's classes (`PrefabCatalogue`,
  `CatalogueEntry`, `NamedCount`, `LightInfo`), written with `JsonUtility` (no
  dictionaries: counts are lists). `format` is bumped when the layout changes.
- **What an entry holds**, all in the prefab root's own space with the pivot at the
  origin: name, path, GUID, folder under `Assets/Prefabs`, source prefab; the **render
  box** (enabled LOD0 renderers on active objects, plus renderers no LODGroup lists),
  its size and `pivotInBounds` (where the pivot sits in the box, 0-1 per axis); the
  **collider box** (enabled non-trigger colliders, from each collider's own shape -
  `Collider.bounds` isn't updated in a preview scene); the root's stored rotation and
  scale; object count, depth, top-level children, the outermost nested prefabs (a nested
  house counts once, not once per wall in it); component counts and gameplay scripts;
  LOD groups and levels, LOD0 renderers, triangles and materials (the draw cost); collider
  kinds and triggers; every Light (path, type, bake mode, shadows, range).
- **Flags** (a reason to look by eye, not a fault in itself): `not-a-variant`,
  `no-render-bounds`, `no-colliders`, `has-lights`, `rotated-pieces` (a renderer more
  than about 2.5° off the root's axes, so the box is loose), `uneven-scale`, `mirrored`,
  `pivot-not-on-base` (the box's bottom more than 5cm from the pivot),
  `pivot-outside-footprint`, `off-environment-layer`, `not-batching-static`,
  `missing-mesh`, `missing-script`, `shares-source` (another variant of the same pack
  prefab), `duplicate-name` (a Unity " 1" copy suffix), `has-gameplay-scripts` (any
  MonoBehaviour but `SurfaceTag`, `RampCollider`, `BarrelCollider`), `assembly` (made of
  nested prefabs rather than being one part),
  `front-unsure` (a slab whose two big sides couldn't be told apart), `no-mesh-data`
  (its meshes couldn't be read), `front-not-plus-z` (see below).
- **The front is suggested from the geometry, never from the name**
  (**`WallOrientation`**, `LevelGen/Editor`), and confirmed by eye in stage 2. The
  pack's walls have a detailed front and a back that is one flat plane. Every LOD0
  triangle is taken into root space (its normal from the winding, turned round under a
  mirrored matrix) and filed under the level side it faces (+X, -X, +Z, -Z; within
  about 45°), with its area and its depth along that axis. Each side's **plane** is
  the depth holding most of its area (1cm bins, a bin counted with its two
  neighbours); its **planarity** is that share of its area. Then:
  - **wall** — the piece is a slab (its thin level side at most 0.6 of the other level
    side and at most half the larger of that side and its height, so a narrow tall
    half-wall counts) and, of its two big sides, the one with fewer triangles (the
    back) is at least 70% one plane, at least 0.2 flatter than the front, with
    confidence at least 0.3. Confidence = (front triangles / back triangles - 1) / 9,
    clamped to 0-1, times the back's planarity. The busier side is the front
    (`suggestedFronts`, one axis vector in root space). An open back (no triangles)
    gives no confidence.
  - **corner** — not a slab, at least a quarter as tall as its larger level side (so
    never a floor tile), and along both level axes the front has at least 1.5 times
    the back's triangles with the back at least 40% one plane (an L's back is two
    arms' inner faces plus their end caps, so less flat than a wall's): two fronts,
    the lower confidence.
  - **unsure** (a slab that fails the test), **not-a-slab**, **no-mesh-data**.
  - For a wall, the **back plane's box** (`backPlaneMin`/`Max`/`Size`: its in-plane
    triangles, so without the trim and beams standing out from it) is taken as the
    module's true size, and gives the **suggested connection points**: `left` and
    `right` at the base of its two ends as seen from in front (right = up x -front),
    `top` and `bottom` at the middle of its upper and lower edges, each with the way
    out of the piece. `sides` keeps each side's triangle count, area, planarity and
    plane depth, for tuning the thresholds (`SlabRatio`, `SlabDepthRatio`, `FacingDot`,
    `PlaneTolerance`, `FlatBack`, `FlatterBy`, `CornerRatio`, `CornerFlatBack`,
    `CornerMinHeight`, `FullConfidenceRatio`, `MinConfidence`): a candidate rule can be
    tried by replaying it over a scan's `sides`, with no rescan.
  - Mesh data is read with `Mesh.AcquireReadOnlyMeshData` (`WallOrientation.ReadTriangles`,
    which also rewinds triangles under a mirrored matrix), leaving import settings
    (Read/Write) alone. `Suggest` returns the four sides for the next pass.
- **The walking surface comes from the colliders, not the meshes**
  (**`WalkSurface`**, `LevelGen/Editor`): floors are laid so their collider tops meet in
  one even surface, however uneven the painted boards are. Every enabled non-trigger box
  collider's faces and mesh collider's triangles (a `RampCollider`'s own collider left
  out) are filed by facing as above; of the upward ones, the height holding most area
  (1cm bins) is `walkHeight`, its share of that area `walkShare`, and the box round it
  `walkMin`/`walkMax`. Each `RampCollider` gives a `ramps` entry from its own transform
  and its three private sizes (read through `SerializedObject`): `foot`, `top`, the
  level `direction` up the flight and `width`.
- **Shape, category and more points** (**`PieceShape`**, `LevelGen/Editor`, after the
  colliders): the pack faces every piece +Z, so +Z is the front wherever the geometry
  gives none (`frontAssumed`). By the first rule that fits, `suggestedCategory` is:
  - `steps` — has a `RampCollider`: points `foot` and `top` (down and up the flight).
  - `roof` — in `Roofs` or `RoofWalls` (+Z assumed, no points; the roof kit waits for
    stage 5).
  - `detail` — in `Windows`, `Doors` or `Iron`: window and door inserts and iron
    fixtures measure like walls but are never a wall module.
  - `column` — at most 0.8m across and twice that tall (before the corner test: thin
    posts measure like corners).
  - `door-wall` / `window-wall` / `wall` — a wall, by its **openings**: its back
    plane's in-plane triangles are drawn on a 5cm grid and each connected run of empty
    cells is a gap; one touching the plane's sides or top is its outline, one touching
    only the base is a `door` (at least 0.6 x 1.5m), one inside it a `window` (at
    least 0.3 x 0.3m). `openings` holds each one's flat box on the back plane, and a
    `door` / `window` point (numbered after the first) sits at its centre, at the
    middle of the wall's depth, facing out of the front.
  - `outer-corner` / `inner-corner` — a corner: inner when both flat backs are on the
    box's edge (within 10% of its size), outer when inside it. Points: `end` plus the
    arm's front (e.g. `end+Z`) at the base of each arm's free end on its back plane
    (the end further from the other arm's back), pointing along the arm; `top` and
    `bottom` where the two backs meet.
  - `floor` / `ground` (`ground` in the Ground folder) — a walking surface, at most
    0.35 as tall as its shorter level side, and (a floor) the surface holding at least
    90% of the upward collider area (a ground piece's uneven stones never do): points
    `+X`, `-X`, `+Z`, `-Z` at the middle of each edge of the walking surface, at its
    height. No front.
  - otherwise `beam` (longer level side at least 4 times its height and its shorter
    side) or none; +Z assumed, with
    left/right/top/bottom points on the -Z side's plane (kept as the back plane) when
    at least 60% of it is one plane, else on the render box's -Z face.
  - **`front-not-plus-z`** flags a piece whose geometry disagrees: measured fronts
    without +Z, or (none measured, not a floor or steps) a slab running along Z (X at
    most 0.6 of Z), or a -Z side with 1.5 times the +Z side's triangles and +Z at least
    0.2 flatter.

## Annotations and verification (stage 2)

- **`PieceAnnotations`** (`LevelGen/Editor`) — the classes of `annotations.json`
  (`PieceAnnotations`, `PieceAnnotation`; connection points reuse the catalogue's
  `ConnectionPoint`), written with `JsonUtility` through the scanner's
  `ShortenNumbers`, sorted by path and rewritten only if the text changed. `format` is
  bumped when the layout changes; a file in another format is left alone.
- **What an annotation holds**, in the prefab root's space: `guid` (the key; `name` and
  `path` are copied in for reading by hand and kept in step with the catalogue);
  `category` - `wall`, `door-wall`, `window-wall`, `outer-corner`, `inner-corner`,
  `foundation`, `foundation-corner`, `floor`, `ground`, `steps`, `column`, `beam`,
  `roof`, `detail` (empty = not chosen); `fronts` (level axes: one for a wall, two for
  a corner, none for a floor or steps);
  the module's `width` (along the piece's right as seen from in front), `height` and
  `depth` (along its front; X and Z for a piece with no front), in metres;
  `connections` (name, position, way out); `excluded`, `verified`, `note`. Roof-kit
  specifics wait for stage 5.
- **The suggestion an annotation starts from** (`PieceAnnotation.Suggest`): the
  catalogue's `suggestedCategory`, fronts (measured or assumed) and connection points;
  the size from the back plane for a wall, the walking surface's width and depth for a
  floor or ground piece, the ramp (width, rise, run) for steps, the render box
  otherwise; `excluded` already ticked for `has-gameplay-scripts` and `assembly` pieces
  (an assembly is used only once taken in by hand). **Re-measure for this front**
  (`SuggestSize`, `SuggestConnections`) redoes the size and the left / right / top /
  bottom points for a front set by hand, on the back face of the back plane or render
  box - the same points the scanner gives a wall.
- **`PieceReviewWindow`** (menu **TeaLeaf > Level Gen > Review Pieces**) — the list of
  catalogue pieces on the left, filtered by folder, suggested orientation, flag, status
  (not verified / verified / excluded) and name; ✓ verified, ✗ excluded, · looked at.
  Choosing one puts a plain `Instantiate` copy of it (not a prefab instance) in the open
  scene at (0, 300, 0), above the level, marked `DontSave | NotEditable` with its lights
  off, and frames it in the Scene view; the copy is destroyed when another piece is
  chosen, the window closes, scripts reload or play starts, so it is never saved.
  Scene view Handles on the copy: the pivot's axes, the render box (white), collider
  box (green), back plane (cyan), walking surface (pale green), openings (orange),
  ramps (outline), each front (yellow arrow) and each connection point
  (magenta, named, a line out along its way out); the point chosen in the window gets a
  move handle. The right side shows what was measured and the annotation's fields; a
  suggestion is only stored once something in it changes, and every change is saved
  straight away (a dragged point when it is let go). **Enter** (or the button) accepts -
  verified, not excluded - and moves to the next piece, which needs a category;
  **Exclude and next**; the **up/down arrows** move through the list; **Reset to
  suggestion** forgets the piece's annotation. Handles rather than `IDebugDrawable`,
  since this is an editor window, not a scene component.

## Known about the prefabs

Measured by the scanner (still to be confirmed by eye in stage 2):

- **Every wall-like piece faces +Z** (its flat back faces -Z); outer corners face +X
  and +Z, the inner corner `Foundations_CornerIn_01` -X and +Z: the pack is consistent.
  Against it: `Platforms_SideWall` has its flat side on +Z, `Platforms_SideWall_Half_02`
  runs along Z, `PortchStraight_Extension` measures +X and -Z.
- Floors and platforms each have one box collider (the stone floors and ground pieces
  a mesh collider) whose top is the walking surface. `Platforms_Steps_Wide`,
  `Steps Full Width` and `Steps Wood Narrow` have a `RampCollider`; the other steps
  are box colliders only.
- **Wall modules**, from the back planes: the plaster walls (`PlasterWalls_A_*`,
  `_B_PlanksCorner`) are **3.0m wide and 3.0m tall**; `WallTall_*` **3.05m x 4.5m**;
  the `Masonary_WoodLarge_*` walls **4.95m x 4.55m** (half 2.6m, low 1.95m, lower
  1.0m); half plaster pieces about 1.6m wide; the cellar wall 3.2m x 1.74m. Wall
  depth is about 0.45-0.75m, pivot in the middle of it.
- **Wall pivots sit at one end of the base**: the back plane runs from x = 0 to x =
  -width, so seen from in front the pivot is at the wall's left end and the wall runs
  to the right. Doors, window inserts (`Window_*`) and the cellar wall have their
  pivot at the centre instead; window inserts and doors are centred in height too.
- **Blind window walls:** `PlasterWalls_A_WindowBase`, `_A_WindowBase_Half`,
  `_A_PlanksWindow`, `WallTall_WindowBase`, `WallTall_StoneWindowBase` and the
  `Masonary_CellarWindow*` walls frame a window that can't be seen through: no hole,
  so no `openings`, and the scan calls them `wall`. Only `PlasterWalls_A_WindowBase_Hole`
  and `Masonary_WoodLarge_WallWindows` have real openings.
- Doors (static and gameplay) are detailed on both sides, so they come out `unsure`,
  correctly. Columns, beams, supports and foundations mostly come out `unsure` or
  `not-a-slab` and need their front set by eye.
- **Corner confidences are low by nature** (0.03-0.16): the formula rewards a flat
  back, and an L's back isn't one. The doubtful corners are `PortchStraight_Extension`,
  `Portch_BeamSmall` and `PlasterWalls_A_MidColumn` (a column pivoted at mid-height).
- **Pivots that aren't on the base** (93, flag `pivot-not-on-base`), by kind: roof
  tiles (`RoofTiles_*`) hang about 2.1m below their pivot (pivot at the top edge);
  gable walls (`RoofWall_*`) reach about 0.16m below theirs, overlapping the wall
  under them; stone floors (`StoneFloor_*`) have the pivot on the walking surface,
  the slab below it; window inserts and doors are pivoted at the centre; the cliff
  rocks, iron fixtures and most platform and support pieces vary.
- The four `Roof_Peaked*` pieces, `PlanedWood_BeamLong` and
  `Platforms_BoardPlanks_SupportsCorner` have no colliders.
- Not set up like the rest (left as they are): `Platforms_Steps_Wide` is off the
  `Environment` layer and not batching-static; `Steps Full Width`, `Steps Wood Narrow`
  and `Curved Top Door` are not batching-static.
- `TL_TowerBridge_Entrance` is filed under `Chimneys` but is a tower bridge entrance
  (an assembly, with a mirrored, turned, unevenly scaled part).
- The lamp and torch fixtures (`IronFixtures_LargeTorch`, `Lampost`) hold the only
  lights left in the catalogue.

From reading the variant files:

- 265 variants, all resolving to pack prefabs; 191 once `Building_Exteriors` (23) and
  `Building_Layers` (51) are left out. Of the rest, a few are assemblies: one chimney
  (47 nested parts), three iron fixtures and one platform piece.
- Most pieces have an LODGroup and box colliders; the ground pieces have mesh colliders.
- The roof kit (`RoofTiles_*`, `Roof_Peaked*`, `RoofWall_*`) has middle, side, top-corner
  and support pieces; whether they cover any footprint, or only some widths, is not yet
  known.
- The meshes are binary FBX, and many pieces have scaled or turned children, so sizes
  and pivots can only be measured in the editor.
- Duplicates to choose between: `TL_PlasterWalls_A_MidColumn 1`, and
  `Steps Wood Narrow` / `TL_Platforms_Steps_Narrow`
  (one source). The pack's misspellings (`Wallkway`, `Rright`, `WindiwRoof`) are kept.
