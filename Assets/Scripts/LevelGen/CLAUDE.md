# Level generator (`Assets/Scripts/LevelGen/`)

An editor tool that builds a level's streets and buildings from the game's own prefab
variants in `Assets/Prefabs`, from a layout spec that a written brief is turned into. It
places real prefab instances under a root of its own and can be run again without
touching anything placed by hand. It generates no lighting, set dressing or gameplay.

**State:** being built in stages (below). Stage 1 (the catalogue scanner) is built and
run: `catalogue.json` holds the `Environment` prefabs (188 once `Containers` and
`Interactive` are no longer scanned). Stage 2 (the
Review Pieces window) is built; one piece is verified so far and none has a style.
Stage 3 (the layout spec, building styles and the validator) is built. Stage 4 (the
streets' ground, the generate command and the `Generated` root) is built; the cobbles
generate in the editor, the segment widths, storey counts and decking are untested.
Stage 5 is built in steps: 5a (the facades' straight runs) is built, untested; 5b-5d and
stage 6 are not built.

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
- **Every tunable value lives in the settings asset** (`GeneratorSettings`, below), not in
  constants: a new tolerance, overlap or rule value the generator or validator needs is
  added there. (The scanner's own detection thresholds stay constants in
  `WallOrientation` / `PieceShape`: changing them means a rescan.)
- Editor-only code in `LevelGen/Editor`, namespace `LevelGen`; the one runtime component is
  the `GeneratedRoot` marker (`LevelGen/GeneratedRoot.cs`).

## The brief and the layout spec

A written brief is turned into a **layout spec** (a JSON file in
`Assets/Data/LevelGen/Layouts`) by Claude in the conversation, not by an API call from
the editor. The spec is what the generator reads. It holds the seed, the streets, the
frontages of facades lining them, and lots for whole buildings. The tool checks a spec
against the catalogue before building and reports what it can't satisfy.

**The maintainer edits no data file by hand.** Claude writes the specs; everything
about pieces, styles included, is set in the Review Pieces window. **A spec never names
a piece, only styles**: a style is a tag on piece annotations (a piece can carry
several), so a newly verified, tagged piece joins its style with no other change.

**Streets are lined without gaps:** along a street the buildings stand side by side,
with a gap only where another street branches off. **Most street buildings are
facades** - only the side(s) the player can see from the street are built. **Where
streets close a loop round a lot, the building on it is built whole**, since it is
seen from every side. **The generator fills the gaps**: the wedge a frontage opens on
the outside of a bend, and whatever length is left at a frontage's end. **A street
end that meets no other street is a dead end, closed by a facade across it**. Facades of
irregular lengths are wanted (the streets feel more organic), so streets are not
fitted to the modules. **Levels where the player can reach the rooftops are built by
hand**: facades are only for streets seen from street level.

**Layouts need not follow a world grid.** A street is a polyline of points at any angle
(a curve is several short segments); a lot has its own origin and yaw. Pieces fit
together on a grid only *within* one lot or one street segment's frame, so a building is
square in itself however it is turned. Where two frames meet at an angle (a bend, a
junction, a lot set askew to its street), the generator fills the gap; anything it
can't fill, and any overlap, is shown in the report rather than hidden.

## Data

- `Assets/Data/LevelGen/catalogue.json` — written by the scanner, one entry per variant:
  asset path and GUID, folder, measured bounds (LOD0 renderers and colliders, relative to
  the root), pivot offset, components, nested prefabs, lights, LODs. Rescanned at any time;
  never edited by hand.
- `Assets/Data/LevelGen/annotations.json` — the maintainer's per-prefab facts, kept apart
  so a rescan never overwrites them, written by the Review Pieces window (stage 2,
  below). A prefab with no entry hasn't been looked at; only pieces `verified` and not
  `excluded` are placed.
- `Assets/Data/LevelGen/Layouts/*.json` — layout specs, one per generated area, written
  by Claude. `example-street.json` is a sample (sector `Example Street`, at the world
  origin): a high street bending at a T-junction
  with a lane, facades on every side (the high street's left side one frontage past
  the bend, its right side two, split at the lane), no lots; cobbles, the plaster,
  wall-tall, plaster-masonry and masonry-woodlarge styles.
- `Assets/Data/LevelGen/GeneratorSettings.asset` — the tunable values (`GeneratorSettings`,
  a ScriptableObject edited in the Inspector; menu **TeaLeaf > Level Gen > Generator
  Settings** selects it, making it with the defaults if missing). Generation and
  validation always read this one asset. **Walls:** `wallOverlap` (4cm),
  `fullSizeShare` (0.75: a wall or tile at least this share of its style's biggest is
  full size), `sizeTolerance` (5cm: how far a wall or tile may be off its style's
  module or tile size and be used as it is), `wallScaleLimit` (0.12: how far, as a share
  of the module, a wall may be off its module and still be used, scaled to fit; side
  walls the same against the storey's height). **Streets:** `streetWidth` (5m, when a spec gives 0),
  `streetWidthVariance` (1m either way, per segment), `minStreetWidth` (2m).
  **Storeys:** `minStoreys` (1) and `maxStoreys` (3) for a
  facade, `storeyBias` (-1 to +1, 0), `lotNeighbourDistance` (15m), `lotStoreyBias`
  (-2 to +2 storeys, 0). **Facades:** `groundStoreyHeight` (0m, -1 to 1.2: how far
  the base of the ground storey's walls, and so its doors, stands above the street),
  `foundationHeight` (0.3m: how far the foundations' tops stand above the street, set
  apart from the walls),
  `foundationFrontOffset` (0.1m: how far their fronts stand out past the walls' front
  line), `groundDoorShare` (0.1), `groundWindowShare` (0.3), `upperWindowShare` (0.4), `storeyBeamOverlap` (4cm), `jettyShare` (0.3: buildings whose upper storeys jut out), `cornerAngleTolerance` (20°), `cornerMaxReach` (8m), `maxStoreyWidthMismatch` (0.3m: how far a storey's module width may differ from the ground storey's). **Columns:** `columnWidthMin` / `Max` (1-2 modules), `columnShare` (0.15: off the line), `columnMinRunModules` (6), `columnDepthMin` / `Max` (0.3-0.8m), `columnOutShare` (0.5), `columnHeightShare` (0.3: taller or lower than the building), `columnStoreyVariation` (1 storey). **Window inserts:** `windowInsertShare` (1), `windowInsertMaxMargin` (0.45m), `windowInsertDepth` (0m). **Side walls:** `sideWallMultiplier` (0-5, 1). **Layout checks:** `overlapTolerance` (5cm),
  `minSegmentLength` (0.1m), `junctionDistance` (0.1m), `branchAngle` (17.5°).
  **Ground tiles:** `groundOverlap` (3cm), `groundTileTurns` (on), `groundSlideMin` /
  `Max` (0.2-0.8 of a tile), `minTileCoverage` (0.1m). **Decking:** `deckingOverlap`
  (3cm), `deckingQuarterTurn` (off), `deckingSlideMin` / `Max` (0-0).

## Stages

1. **Catalogue scanner** — measures every variant and writes `catalogue.json`.
2. **Annotations and verification** — a Scene view tool that shows a piece's pivot,
   measured bounds and assumed front for the maintainer to confirm into
   `annotations.json`.
3. **Layout spec** — the format and its validation.
4. **Streets and ground** — ground tiles along each street segment and at junctions.
5. **Buildings and hierarchy** — each frontage's facades laid side by side along the
   street's edge, each in its own frame, with an outer corner and a short side return
   where a frontage ends at a branching street; the wedge at each bend and the length
   left at a frontage's end filled; and each lot's whole building within the lot's
   frame. Both from parts: foundations, then per storey wall modules (plain, window and
   door modules being the same width, so any can stand in for another), corner pieces
   where sides meet, `wood-floor` floors in a whole building, and a roof from the roof
   kit; the front faces the street. **Window walls can stand at any module of any
   storey**, the ground storey included, mixed freely with plain walls. **Where several pieces fit a module, one is chosen
   at random (from the seed), never the same piece as the module before it** along
   the run, unless it is the only one that fits. **The hierarchy is streets >
   buildings > columns > storeys:** every facade building is cut into columns of one
   or more modules, each with its own depth and storey count. **Side walls where a
   column stands taller than its neighbour** (in its building or the next): each
   storey of the taller column above the lower one's top gets a side wall at that
   end, facing that way and running back from behind its front walls, so from the
   street it seems to go on behind. Its length in modules is `sideWallMultiplier`
   times the column's width in modules, at least one (0 = none). Separate from roofs
   and roof walls. Placed under
   `Environment/Sectors/<Sector>/Geometry/Generated` (`GeneratedRoot`), which a regeneration
   replaces whole. A regeneration warns first if anything inside it was edited by hand.
   Built in steps, each testable alone:
   - **5a** the straight runs: foundations and storeys of wall modules along each
     frontage, broken at bends (built; `FacadeBuilder`, below).
   - **5b** bends and ends (built; Corners, below): **a facade breaks at a bend** and
     goes on along the next segment's edge in that segment's frame; on the inside the
     walls overlap behind their fronts, on the outside each side is carried to where
     the lines meet and **the joint is covered** - an outer corner piece near a right
     angle, else a column. **Two frontages of different streets meet at a street
     corner** the same way; a frontage end at a junction that meets none turns a
     one-module side return into the branching street. **The length left at a
     frontage's end extends its last building** with whole modules; at a dead end one
     more closes it.
   - **5c** the side walls where a taller column stands beside a lower one, along a
     straight run (built, with the columns; at a bend or a frontage's end they
     wait for 5b).
   - **5d** the lots' whole buildings: four sides, corners, `wood-floor` floors.
   - Roofs come after these.
6. **Report** — missing or unverified assets, footprint mismatches, overlaps, gaps along
   streets and between storeys, unconnected connection points, anything not placed.

## Facades (stage 5a)

- **`FacadeBuilder`** builds every frontage, in the spec's order, after the streets,
  under `Generated/Facades/<street> <side> <from>-<to>/Building <i>/` (`Foundations`,
  `Storey 0`, `Storey 1`, ...).
- **Runs:** one straight run per segment along the frontage's street edge (the middle
  line pushed out by half the segment's width). On the inside of a bend a run stops
  where it meets the neighbouring segment's edge (line intersection); on the outside
  the runs leave a gap (closed by a corner); at a frontage's ends a run stops half a
  branching street's width short (`LayoutSpecValidator.BranchWidth`, shared). The
  buildings are laid module after module from the frontage's first point at the
  ground storey's `ModuleSpacing`; a module that would pass its run's end (beyond
  `sizeTolerance`) starts the next run. **The length left after the buildings**
  (`ExtendLastBuilding`): the last building goes on with whole modules while they fit,
  moving to the next run as above; where the frontage ends at a dead end (one
  `StoreyPlan.DeadEnds` closes), one more module is added if any length is left, its
  overrun behind the dead end's facade, so the frontage always meets it. Seen from the
  street a left frontage runs left to right along the street, a right frontage right
  to left, so `doorModule` (counted from the left over the spec's `widthModules`, so the
  added modules never move it) is mirrored on a right frontage.
- **Depth:** all of a frontage's walls line up by their back planes, on a back line set
  back from the street edge by the deepest any of its usable walls reaches in front
  of its `left` connection point (render box front less the back plane), so that
  wall's trim just reaches the edge.
- **Walls** (`BuildingStyle.Modules`: usable pieces of a wall category whose width
  and height each fit the module - `BuildingStyle.Fits`: within `sizeTolerance`, or
  within `wallScaleLimit` of it and **scaled along the wall's width and height to fit
  it exactly** (`FitScale`, `PiecePlacer.PlaceAt` with a scale; the walls' prefab roots
  are all unrotated at unit scale, so the scale is in the piece's own axes; depth is
  never scaled); `Modules(category, storey)` leaves out pieces marked
  `groundStoreyOnly` above the ground storey, for the facade walls and the validator's
  checks) are placed by their verified
  `left` connection point at the module's left end (seen from the street) on the back
  line, turned so their front faces the street. Per storey and module two draws are
  made (always, so the random sequence doesn't depend on which styles have door or
  window walls): on the ground storey the spec's door module, or a module whose door
  draw falls under `groundDoorShare`, takes a `door-wall`; otherwise a `window-wall`
  when the window draw falls under `groundWindowShare` (ground storey) or
  `upperWindowShare` (above it) and the style has one; else a `wall`; chosen with
  `PiecePlacer.Pick`, never the piece before it along that storey. **A plain wall marked `fillsStorey`** (`PlasterWalls_B_MasonaryWall`), where the storey's plain walls hold one, stands at every module but the spec's door: no other plain walls, random doors or window walls on that storey (the draws are still made). Storeys stand on
  each other at their styles' `StoreySpacing` (or on a beam band, below), the ground storey's base
  `groundStoreyHeight` above the street; every storey is laid at the ground storey's module
  spacing, so they stand in line; a storey whose style has another module width (up to `maxStoreyWidthMismatch` off, e.g. plaster on cellar walls) has each wall centred in its place, overlapping or leaving a small gap shared between its sides. A building whose ground style has no module yet
  stops the frontage there (logged); a storey whose style has none ends the building.
- **Foundations:** one per module from the ground style's `foundation` pieces (random,
  no repeat), centred on the module along the street, the top of its render box
  `foundationHeight` above the street and its front face `foundationFrontOffset` out
  past the street edge (the walls' front line); above the ground storey's base it
  shows as a plinth in front of the walls. Doors stand `groundStoreyHeight` above the
  street; steps up to them aren't planned yet.
- **Jetties** (medieval overhangs): each facade building draws at `jettyShare` (always
  drawn); it is jettied where it may have an upper storey and a usable `jetty` piece
  (any style; `Platforms_ShortBoardPlanks_Supports`, 3.02 x 1.58m, its front -Z) fits
  its ground storey's module (`Fits`; not masonry-woodlarge's 4.95m). **All its upper
  storeys stand forward by the jetty** - the deepest fitting piece's depth, about
  1.58m - on top of each column's own depth, in one plane (`StoreyJetty`; walls, beams
  and side walls use it). **The first upper storey stands on the supports' deck, which
  replaces the beam band at that joint**: its base (the deck's top) lies
  `storeyBeamOverlap` below the top of the ground storey's walls (`StoreyLevels`);
  higher joints get beam bands as usual. **Supports** (`BuildJetty`, under
  `Building <i>/Jetty/`): one per module whose column stands above the ground storey,
  at random, never the one before it, facing the street, centred on the module and
  scaled along its width to it, the back of its render box against the face of the
  ground storey's wall there (`ModuleSpot.Reach`), the top of its render box at the
  deck's height, so the brackets hang in front of the ground walls' top and the deck's
  front comes about level with the jettied walls' faces. **Side walls work storey by
  storey** on each storey's depth (column depth plus jetty), so a jettied storey beside
  one that isn't is a step, closed by walls from the jettied side; a step's walls are a
  module long, or longer when the step is deeper (its depth plus the walls' setback).
  Only a ground-storey step (a column's) gets a side foundation. **A jettied storey at a run's end** (a frontage's end or a bend, where no column of the run stands beside it) **is closed by itself** (`CloseJetty`, under `Building <i>/End <n>/`): side walls facing out of the end from its back line back past its column's own line, a module or the jetty plus the setback if longer, with the beam band below as at a join. Dead ends are never
  jettied.
- **Storey beams:** where two storeys' styles both have usable `beam` pieces
  (`PlasterWalls_A_MidBeam`, tagged with the common-height plaster styles; wall-tall,
  masonry-woodlarge and the cellar walls have none, so their joints get no beam), **a
  band of beams lies in its own space between the walls below and above**: the band is
  as tall as the lower style's tallest beam, its bottom overlapping the lower walls'
  top and the upper walls' base overlapping its top by `storeyBeamOverlap` (4cm) each,
  so the storeys above stand higher by the band less twice the overlap
  (`StoreyLevels`; `StoreyLevel.BeamBottom` / `BeamHeight`). Front beams
  (`BuildBeams`, after a building's storeys, under `Building <i>/Beams/`): one per
  module whose column stands above the band, from the lower storey's style at random,
  never the one before it, its back on the walls' back line, centred on the module
  along the street, scaled along its width to the module (`FitScale`, as the walls)
  and in height to the band. **Side walls get beams too** (`LaySideBeams`): under each
  storey that gets side walls and has a band below it, beams laid end to end the same
  length, the same way round and from the same start as those walls, so the band
  never leaves a gap between side walls.
- **Window inserts:** every facade wall placed (dead ends too, not the steps' side
  walls) has its window holes filled: per `window` opening in the catalogue (holes in
  the back plane: `PlasterWalls_A_WindowBase_Hole` has one of 1.2 x 1.3m,
  `Masonary_WoodLarge_WallWindows` two of about 0.5 x 1m), at `windowInsertShare`, one
  of the usable `window` pieces (verified, not excluded, one front; any style - inserts
  aren't style-tagged) whose annotated width and height are at least the hole's and
  no more than `windowInsertMaxMargin` bigger on each side, at random, never the one
  before it in the wall. The insert faces the wall's way, its render box's middle on
  the hole's middle at the middle of the wall's depth (back plane to render front),
  moved forward by `windowInsertDepth`; under the wall's storey group. Both draws are
  made for every hole. The pack's inserts (`Window_A`-`D`, `C_Open`, `D_Open`) are
  1.6-2.0m with their frames, so they fit the plaster hole; the woodlarge holes stay
  open.
- **Columns:** a frontage's modules are all laid out first (where each stands), then
  split into columns (`SplitColumns`), then the buildings built, then the side walls
  placed (`FillSides`). **Every building is cut end to end into columns** of
  `columnWidthMin`-`Max` modules, never across a building's end or a bend (the last
  column of each takes what is left). Each column rolls, in this order and always
  all of them: its width, off the line (`columnShare`), forward (`columnOutShare`),
  its depth (`columnDepthMin`-`Max`), a height change (`columnHeightShare`), taller
  or lower (even odds) and by how much (1 to `columnStoreyVariation`). **Depth** applies
  only on a straight run of at least `columnMinRunModules` modules and never to a
  column holding the run's first or last module; forward is into the street. It is
  capped at half the narrowest module of any storey of the frontage, so two
  neighbouring columns differ by at most a module and one side wall fills any step.
  A module's walls and foundation move with its column's depth. **Height:** the
  building's storey count (`StoreyPlan`) plus or minus the change, kept between
  `minStoreys` and `maxStoreys`; the building's storeys are made up to its count
  plus `columnStoreyVariation` (within `maxStoreys`), and a storey whose module's
  column doesn't reach it is left out. Lots and dead ends still read the building's
  count.
- **Side walls** (`FillSides`), between every two neighbouring columns of a straight
  run, in one building or two, not corner pieces (the pack's corners sit at the ends
  of walls, and one over a wall's face would flicker). Each is a plain `wall` of
  **any width** whose height fits the storey's module height (`Fits`; scaled in height
  to it when beyond `sizeTolerance`) - **never one
  marked `showsOpening`** (`BuildingStyle.SideWalls`; ground-storey-only ones only on
  the ground storey) - laid end to end (each `wallOverlap` short of its width further
  back) until the length is covered, turned side-on to face out of its column's end
  towards the other column. **Its walls come from the storey's own style, else the
  neighbouring column's style at the same storey, else the styles of the building's
  storeys above** (wall-tall has no plain wall of its own that
  shows no opening, so it borrows); a style with none anywhere is left without them
  and logged once per frontage.
  - **A column's storey whose top stands above the other column's top** (by more than
    `sizeTolerance`, in metres, since styles' storey heights differ) gets
    `sideWallMultiplier` times the column's width in modules (at least one) of
    length, from its own back line, its thickness in its own column.
  - **Where the two stand at different depths**, the forward column's other storeys
    get one module's length each, and the step one `foundation`, its front
    `foundationFrontOffset` out past the ground storey's side wall. Where they go
    depends on the other column:
    - **The other column is inset** (stands back from the line): **in the recess**,
      backed against the forward column's end - the side wall's back plane on the
      join, its thickness in the inset column - starting `FaceClearance` (1cm) behind
      the face of the forward column's front wall beside the join (`ModuleSpot.Reach`,
      recorded per storey as the walls are placed), so it covers that wall's end, and
      running back **through the inset column's front walls**, which hide the rest.
    - **Otherwise** (the forward column is an outset): in the outset column, its front
      flush with the join, starting at the column's back line behind its front walls.
      The part past the step's depth is hidden behind the recessed wall.
  - **A side wall is placed by the end of its render box**, not its connection point
    (which some pieces' trim stands past). From the frontage's front line instead of
    behind the front walls it would stand out past every front wall shallower than the
    deepest.
  - Under `Building <i>/Side <n>/` of the column's building, `n` the join's module
    index. Run ends a corner closes get none; a jettied storey at any other run end is closed (Jetties, above). Dead ends
    are one column.
- **Corners** (stage 5b): `FacadeBuilder.Build` lays every frontage out first
  (`LayFrontage`: modules, the end's extension, columns), then finds the corners
  (`FindJoints`), then builds the frontages, then the corners (`BuildJoint`).
  - **Inside a bend** (the next run heads into the street the run faces,
    `InsideBend`) the runs stop where the edges meet, as before, and the building
    being laid gets an added module where length is left: its overrun stands behind
    the next run's walls.
  - **Outside a bend**, and at **a street corner** - two frontage ends of different
    streets at a junction (each stops half the other street's width short), paired
    where each needs carrying no more than `cornerMaxReach`, shortest pairs first -
    **each side is carried storey by storey to where the two lines meet** (each
    storey's line at its column's depth plus its building's jetty, meeting the other
    side's storey of the same index, or its top storey): a **filler** of a plain wall
    of the storey's height (borrowed as for side walls) nearest the length in width,
    scaled to it, with a foundation under the ground storey, the band's beam below a
    banded storey and a jetty support under a jettied first storey, all scaled to the
    length (`PlaceFiller`, `PlaceInRun`).
  - **The joint is covered** (`PlaceCover`, from the first side's storey style, else
    the other's, of the storey's height and scaled to it): within
    `cornerAngleTolerance` of a right angle an **outer corner piece**, its outer edge
    on the meeting point (`FaceClearance` out) and turned to sit between the two
    facades; otherwise a **`column`** piece over the seam (the plaster styles'
    `PlasterWalls_A_MidColumn`, to be reviewed in Review Pieces as a `column` tagged
    with the plaster styles). A style with neither is logged and the seam left bare.
  - A storey standing above the other side's top is **closed by side walls** facing
    out of the corner instead (`LaySideStorey`).
  - **Side return:** a frontage end at a junction that pairs with none gets a new
    run of one added module along the branching street's near edge (the widest
    street leaving the junction towards the frontage's side), from where the
    frontage's line meets it, facing into that street; the end and the return are
    joined as a corner (`AddSideReturn`). The return's far end stays open.
  - Joined run ends are marked (`ModuleSpot.JoinedStart` / `JoinedEnd`) so nothing
    else closes them. Pieces under `Building <i>/Corner/`.
- **Dead ends:** a street end with no other street's point within `junctionDistance`
  is closed by a facade across it (`StoreyPlan.DeadEnds`, made after the frontages'
  rolls and before the lots; built after the frontages, under
  `Facades/<street> dead end <point>/Building 0/`). Its building is made by the
  plan: the styles of the frontage building at that end (the left side's, else the
  right's; not closed, logged, if neither side has one), no door, enough modules of
  its ground style to span the end segment's width, and a rolled storey count. It is
  one straight run through the end point, square to the end segment, facing back up
  the street and centred on the middle line, so its overhang runs on behind the side
  facades (where they meet, the walls overlap behind their fronts).

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
  `roof`, `detail`, `window` (a window insert, put into wall holes; suggested for a `detail` in the `Windows` folder), `jetty` (jetty supports under a jettied upper storey, any style: `Platforms_ShortBoardPlanks_Supports`) (empty = not chosen); `fronts` (level axes: one for a wall, two for
  a corner, none for a floor or steps);
  the module's `width` (along the piece's right as seen from in front), `height` and
  `depth` (along its front; X and Z for a piece with no front), in metres;
  `connections` (name, position, way out); `styles` (building style tags, stored
  normalised: trimmed, lower case, spaces as hyphens); `groundStoreyOnly` (a wall the generator places only on a building's ground storey, whatever its style allows, e.g. `WallTall_StoneWindowBase`; a toggle in the window), `showsOpening` (a plain `wall` that shows a window or door - the blind window walls below and the masonry door halves (`DoorOrnate_Half*`, `WallDoorArch_Left` / `_Rright`) - and so is never a side wall; a toggle in the window), `fillsStorey` (a plain wall that, on a storey whose style offers it, stands at every module but the spec's door; a toggle in the window), `excluded`, `verified`, `note`.
  Roof-kit specifics wait for stage 5. Format 2; a format 1 file (no styles) is read
  and written back as format 2.
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
  catalogue pieces on the left, filtered by folder, suggested orientation, flag, style,
  status (not verified / verified / excluded) and name; ✓ verified, ✗ excluded, · looked at.
  Choosing one puts a plain `Instantiate` copy of it (not a prefab instance) in the open
  scene at (0, 300, 0), above the level, marked `DontSave | NotEditable` with its lights
  off, and frames it in the Scene view; the copy is destroyed when another piece is
  chosen, the window closes, scripts reload or play starts, so it is never saved.
  Scene view Handles on the copy: the pivot's axes, the render box (white), collider
  box (green), back plane (cyan), walking surface (pale green), openings (orange),
  ramps (outline), each front (yellow arrow) and each connection point
  (magenta, named, a line out along its way out); the point chosen in the window gets a
  move handle. The right side shows what was measured and the annotation's fields
  (under Category, a toggle for every style in use and a field that adds a new style
  on Enter); a
  suggestion is only stored once something in it changes, and every change is saved
  straight away (a dragged point when it is let go). **Enter** (or the button) accepts -
  verified, not excluded - and moves to the next piece, which needs a category;
  **Exclude and next**; the **up/down arrows** move through the list; **Reset to
  suggestion** forgets the piece's annotation. Handles rather than `IDebugDrawable`,
  since this is an editor window, not a scene component.

## Layout spec, styles and validation (stage 3)

- **`LayoutSpec`** (`LevelGen/Editor`; `LayoutSpec`, `StreetSpec`, `FrontageSpec`,
  `BuildingSpec`, `LotSpec`), read with `JsonUtility`, format 2. World space, metres;
  a yaw is degrees clockwise from +Z (north) seen from above. `seed`; `sector` (the
  level sector it is built in);
  - `streets`: `id`, `style` (its ground or plank decking), `width` edge to edge (0 =
    the settings' `streetWidth`; **every segment is varied from it**, see
    `ResolveWidths` below),
    `points` along its middle (y = ground height; a curve is short segments; streets
    meet where they share a point), `frontages`;
  - a **frontage** is one side of a street lined with facades: `side` (`left` /
    `right`, looking from the street's first point), `from` and `to` (point indices;
    it runs past bends and ends only where a street branches off on its side, or the
    street ends), `buildings` in order from `from`;
  - a frontage **building**: `widthModules` along the street, `storeys` (styles from
    the ground up: **the last repeats for every storey above the list**, and storeys
    past the count are dropped - the count isn't in the spec, see `StoreyPlan`),
    `doorModule` (from the left seen from the street, from 0; -1 none);
  - `lots` (whole buildings, inside loops): `id`, `origin` (middle of the footprint,
    at the ground storey's floor), `yaw` (the way the front faces), `widthModules`
    along the front and `depthModules` back from it, `storeys`, `doorModule`.
  A building's length follows from its modules and its styles' module spacing (below).
- **Street widths vary by segment** (`LayoutSpec.ResolveWidths`, the first random
  choices from the seed, made by the validator so it checks the widths the generator
  builds; the generator goes on with the same `System.Random`): each segment is its
  street's width (the spec's, or `streetWidth`) plus a random amount up to
  `streetWidthVariance` either way, at least `minStreetWidth`. One draw per segment,
  in the spec's order. So a street steps wider or narrower at its points, and the
  facades lining it step in or out there (stage 5). `StreetSpec.SegmentWidth(i)`
  (segment i runs from point i to i + 1) and `WidthAt(point)` (the wider segment
  meeting there) are what every later step reads.
- **A storey is built from one wall family only**, grouped by folder and name; the
  family's corners serve every storey of it. **`StyleRules`** (`LevelGen/Editor`)
  suggests a piece's styles from its name (first matching prefix after `TL_`), ticked
  in the window for a piece not yet annotated and stored on accepting it:
  - `plaster` - `PlasterWalls_A_Cross`, `_A_WindowBase*`, `_A_Zbar`,
    `_B_WindiwRoof` and `PlasterWall_B_LowCurves`;
  - `plaster-planks` - `PlasterWalls_A_Planks*`, `_B_PlanksCorner`;
  - `plaster-masonry` - `PlasterWalls_B_MasonaryWall*` and the masonry door walls `Masonary_WallDoor*`, `_DoorOrnate*` (there is no separate masonry door style);
  - `PlasterWalls_B_Corner` is in all three plaster styles;
  - `wall-tall` - `WallTall_*` and `WallsTall_CornerNarrow`;
  - `masonry-cellar` - `Masonary_Cellar*` and `_ShortSideWall*`: the low cellar walls
    (about 1.74m), a storey type of their own;
  - `Masonary_WallCorner*` is in `masonry-cellar` and `plaster-masonry`;
  - `masonry-woodlarge` - `Masonary_WoodLarge*` (walls, corners, supports);
  - `Masonary_Foundations_*` - every building style;
  - `cobbles` (street ground) - `StoneFloor_*` (A-E and Tile);
  - `wood-floor` (plank decking for wooden streets, walkways and bridges, and **every
    building's upper floors**, whatever its walls - `StyleRules.BuildingFloors`) -
    `Platforms_Planks*`, `Platforms_LoosePlank*`, `Platforms_BoardPlanks`,
    `Platforms_ShortBoardPlanks`, `Fixtures_WoodenPlatform*`; the `_Supports` pieces (`Platforms_ShortBoardPlanks_Supports` is the `jetty` piece)
    under the decking get no style.
  **`masonry-cellar`, `plaster-masonry` and `plaster-planks` are only ever a ground
  storey** (`StyleRules.GroundStoreyOnly`), of a facade or a whole building;
  `masonry-woodlarge` can be any storey.
  **`PlasterWalls_A_MidBeam` is the storey beam** (category `beam`) of the three
  plaster styles. Every other piece outside `Walls` and `Masonry` gets no style.
  **`PlasterWalls_A_MidColumn` is a loose trim piece for covering gaps by hand**: no
  style, never placed by the generator.
- **`BuildingStyle`** (`LevelGen/Editor`) gathers each style from the annotations: its
  usable pieces by category (verified, not excluded, still catalogued, category set)
  and the tagged pieces that aren't usable yet. Its **module** is the median width and
  height (the lower middle value for an even count) of its full-size usable `wall`,
  `window-wall` and `door-wall` pieces - those at least `fullSizeShare` of its widest
  wall - so the half pieces never set it. Its **tiles** (what a street of the style is
  laid with) are its `ground` pieces, or its `floor` pieces if it has no ground ones
  (`TileCategory`); the **tile size** is the median width and depth of the full-size
  ones (both sides at least `fullSizeShare` of the biggest tile's), and only tiles
  within `sizeTolerance` of it are laid.
- **`LayoutSpecValidator`** (menu **TeaLeaf > Level Gen > Validate Layout Spec**, on the
  spec(s) selected in `Layouts`) writes one Console entry per spec - an error entry if
  it can't be built, a warning entry if it has warnings. **Errors:** unreadable or other
  format, no sector, a missing or repeated id, a street with a negative width, with fewer than two points
  or a segment under 0.1m; a frontage with a side other than left / right, a stretch
  outside the street's points, overlapping another on its side, with no buildings, or
  with a street branching off on its side part-way along it; a building or lot under
  one module, with no storeys or a door module off its front; a street or storey
  naming no style or one no piece carries; a street style without `ground` or `floor`
  pieces (a wooden street, walkway or bridge is decked with floors); a storey style
  without a `wall` that fits its module, or without `outer-corner`; a ground-storey-only style above the ground storey,
  or listed last when `maxStoreys` is more than the list (it would repeat);
  the ground storey's without `foundation` or
  `foundation-corner`, or without a `door-wall` that fits its module when there is a door; storeys whose
  modules differ from the ground storey's by more than `maxStoreyWidthMismatch`; a frontage's buildings more than 5cm longer than
  its stretch. **Warnings:** more than a module's length left at a frontage's end (the
  generator fills it, but another building may suit); a lot, when `maxStoreys` is more
  than 1 and `wood-floor` has no usable `floor` (facades have no floors); per style used,
  tagged pieces not usable yet, walls whose width doesn't fit its module (not used as
  a module; not warned for a plain wall showing no opening whose height fits, since it
  is a side wall) or whose height doesn't (not used), fitting meaning within
  `sizeTolerance` or `wallScaleLimit` (`BuildingStyle.Fits`), and tile-category pieces
  off its tile size (not laid); lots overlapping each other or a street segment by more than
  5cm (oriented rectangles, separating axis test).
- **A frontage's stretch** is measured roughly: the street's middle line between its
  two points, less half the width of any street branching off on its side at either
  end (another street sharing the point, within 10cm, and leaving it at least about
  17 degrees towards that side). The true length along the edge (longer outside a
  bend, shorter inside) is stage 5's. Roofs and the bend wedges are not checked yet
  (stages 5-6). `ValidateFile` / `Validate` return the `Report` for the later stages.
  The 5cm and other tolerances above are the settings asset's defaults.

## Generation and the streets' ground (stage 4)

- **`LevelGenerator`** (menu **TeaLeaf > Level Gen > Generate Level**, on the spec
  selected in `Layouts`) validates the spec and logs the report, then builds into the
  open scene under `Environment/Sectors/<sector>/Geometry/Generated` (each object made
  if missing). `Generated` carries a **`GeneratedRoot`**, which records the spec path
  and a fingerprint (a hash of every object's path, active state, local transform to
  the millimetre / hundredth of a degree, and prefab) of what was built. A
  regeneration replaces `Generated` whole, in the same place among its siblings; it
  asks first if the fingerprint no longer matches (edited by hand) or it was built
  from another spec, and refuses an object named `Generated` with no `GeneratedRoot`.
  One `System.Random` from the seed (handed on from the validation, past the street
  widths); one Undo step for the whole generation. A street
  that can't be laid (no usable tiles, too few points) is skipped and logged rather than
  stopping the run, so the ground can be tried before every building style is
  reviewed. One Console entry lists what was built.
- **`StoreyPlan`** decides every building's storey count before anything is built
  (logged; stage 5 builds to it), its rolls next in the random sequence after the
  street widths. **A facade
  is rolled** between `minStoreys` and `maxStoreys`: a random share of the range
  raised to the power 4^-`storeyBias` (+1 leans high, -1 low, 0 even). **A lot
  matches its neighbours:** the facades with a point of their street edge (sampled
  every metre, their length from the ground storey's modules) within
  `lotNeighbourDistance` of the lot's footprint give an average height in metres
  (storeys at their styles' spacing plus the top overlap), climbed in the lot's own
  storeys to a fractional count, plus `lotStoreyBias`, rounded and kept between min
  and max; rolled like a facade if none is that near or a height isn't known.
  `StyleOf` gives a storey's style (the last listed repeating).
- **`PiecePlacer`** puts a piece in as a real prefab instance
  (`PrefabUtility.InstantiatePrefab`) with any `Light` in it disabled on the instance.
  `PlaceAt` turns a piece about Y and puts a point of its own space (a connection
  point, a face's middle) on a world point; `Pick` is the shared random choice that
  never repeats the piece before it. It places a tile by its **walking surface**: the middle of the catalogue's walk box
  at `walkHeight` (the render box's top without one) goes where the caller asks, so
  tiles meet by their collider tops.
- **`StreetGround`** lays every street, in the spec's order, under `Generated/Streets`:
  - **Ground (a style with `ground` tiles - cobbles): one grid per area**, an area being
    the streets of that style joined at shared points. The grid is squared to the
    area's first segment, from the first street's first point. The outline is each
    segment's rectangle (at its own width) plus a disc (half the wider of the
    segments meeting there) at every bend and at an end shared with another street of
    the area, filling the wedges and width steps. A tile is laid where
    it reaches at least `minTileCoverage` into the outline, so there are no gaps and no
    doubled tiles at bends or junctions; what stands past the street's edge is under
    the buildings lining it (and shows at a branch gap or a dead end). Each tile's
    walking surface is at the height of the nearest point on the area's middle lines.
  - **Decking (a style with only `floor` tiles - wood-floor): each segment in its own
    frame**, so the boards follow the walkway. Rows across the segment's width,
    centred on the middle line; at a bend each segment runs on by half the wider
    segment's width x tan(half the turn) (at most that width) to meet the next at the outside edge, so the inside of a
    bend overlaps. `deckingQuarterTurn` lays the tiles a quarter turn from the street.
    Height follows the segment, tile by tile (flat tiles, not tilted).
  - **Rows:** tiles are laid in rows, neighbours overlapping by `groundOverlap` /
    `deckingOverlap` (the pitch is the tile less it). **Each square ground tile is
    turned a quarter, half or three-quarter turn from the tile before it along its
    row** (at random, so no two neighbours lie the same way; a row's first tile any
    turn); a non-square tile, and every decking tile, takes a random half turn so it
    still fits. **Each row slides along from the one
    before by a random share of a tile** between the slide min and max (kept within
    one pitch; the first row unslid), so the joints don't line up. Along a row the
    tile is chosen at random among the style's tiles, never the one before it unless
    it is the only one.

## Known about the prefabs

Measured by the scanner (still to be confirmed by eye in stage 2):

- **Every wall-like piece faces +Z** (its flat back faces -Z); outer corners face +X
  and +Z, the inner corner `Foundations_CornerIn_01` -X and +Z: the pack is consistent.
  Against it: `Platforms_SideWall` has its flat side on +Z, `Platforms_SideWall_Half_02`
  runs along Z, `PortchStraight_Extension` measures +X and -Z.
- Floors and platforms each have one box collider (the stone floors and ground pieces
  a mesh collider) whose top is the walking surface.
- **The only cobble tile is `StoneFloor_Tile`** (3.04 x 3.04m, flat collider top).
  `StoneFloor_A`-`E` are irregular stone patches (0.7-1.7m across, uneven colliders):
  smaller than `fullSizeShare` of the tile, so never laid as tiles. The plank floors
  are about 3.0 x 3.0m (`Platforms_Planks`, `_Planks_Ground`), with half (3.0 x 1.9m),
  nailed (2.9 x 2.3m) and board (3.0 x 1.5-2.0m) sizes; which way their boards run is
  not yet known. `Platforms_Steps_Wide`,
  `Steps Full Width` and `Steps Wood Narrow` have a `RampCollider`; the other steps
  are box colliders only.
- **Wall modules**, from the back planes: the plaster walls (`PlasterWalls_A_*`,
  `_B_PlanksCorner`) are **3.0m wide and 3.0m tall**; `WallTall_*` **3.05m x 4.5m**;
  the `Masonary_WoodLarge_*` walls **4.95m x 4.55m** (half 2.6m, low 1.95m, lower
  1.0m); half plaster pieces about 1.6m wide; the cellar wall 3.2m x 1.74m. Wall
  depth is about 0.45-0.75m, pivot in the middle of it.
- **Walls overlap their neighbours by 3-5cm** (the maintainer's account of the pack):
  seen from in front, a full-size wall has a trim down its left side and along its
  top. Side by side, the next wall's left trim stands in for this wall's right edge,
  and the storey above sits on this wall's top trim, so neighbours overlap slightly,
  along a side and between storeys. The settings' `wallOverlap` (4cm) is that
  overlap: modules are laid at the module width less it (`ModuleSpacing`) and storeys
  at the module height less it (`StoreySpacing`), and a lot's footprint is
  `modules x ModuleSpacing + WallOverlap`.
- **Wall pivots sit at one end of the base**: the back plane runs from x = 0 to x =
  -width, so seen from in front the pivot is at the wall's left end and the wall runs
  to the right. Doors, window inserts (`Window_*`) and the cellar wall have their
  pivot at the centre instead; window inserts and doors are centred in height too.
- **Blind window walls:** `PlasterWalls_A_WindowBase`, `_A_WindowBase_Half`,
  `_A_PlanksWindow`, `WallTall_WindowBase`, `WallTall_StoneWindowBase` and the
  `Masonary_CellarWindow*` walls frame a window that can't be seen through: no hole,
  so no `openings`, and the scan calls them `wall`; they (and `PlasterWalls_B_WindiwRoof`)
  are marked `showsOpening`. Only `PlasterWalls_A_WindowBase_Hole`
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
