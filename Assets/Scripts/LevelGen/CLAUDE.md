# Level generator (`Assets/Scripts/LevelGen/`)

An editor tool that builds a level's streets and buildings from the game's own prefab
variants in `Assets/Prefabs`, from a layout spec that a written brief is turned into. It
places real prefab instances under a root of its own and can be run again without
touching anything placed by hand. Of set dressing it places only what sits on its own
buildings and streets: the facade dressing (pipes, vents, walkways, lamp posts; below),
the lamp posts being gameplay lights, the roofs' chimneys, dormers and ridge railings,
chimney smoke and vent steam, and grass and nettles in the streets; it generates no
other lighting, dressing or gameplay.

**State:** being built in stages (below). Stage 1 (the catalogue scanner) is built and
run: `catalogue.json` holds the `Environment` prefabs (188 once `Containers` and
`Interactive` are no longer scanned). Stage 2 (the
Review Pieces window) is built; the pieces the generator places are verified in it.
Stage 3 (the layout spec, building styles and the validator) is built. Stage 4 (the
streets' ground, the generate command and the `Generated` root) is built; the cobbles
generate in the editor, the segment widths, storey counts and decking are untested.
Stage 5 (the facades, corners, side walls, lots and roofs) is built. Stage 6 (the
report on what was built: free wall ends, open roof ends, refused pieces, with a marker
at each) is built. **Every placed piece follows one fitting rule** (`FitWidths`,
`fillStretchShare`): whatever covers a length end to end - a run's modules, side
walls, a corner's fillers, foundations, beams - lays whole pieces, and the length left
under a piece is closed by widening the last piece when it is under 30% of a piece's
width, else by one more piece scaled down to it; nothing is ever a sliver nor an
overrun. **The pack's walls are hollow: an end face left in view shows black**, so every
wall end must be covered (by the next wall, a side wall or a filler) or hidden behind
or inside another wall; only a wall's trimmed (left) end, a closed post, may show, as
the post of a corner.

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
  placed object, the prefab untouched) - **except a light with a `Core.LightSource`
  beside it, a gameplay light, which stays on**: the generator's lamp posts are such
  lights (Facade dressing, Lamp posts, below), kept to the shadowmask's limit. All
  other level lighting is placed by hand, to the rules in `Assets/Scenes/CLAUDE.md`.
- **Gameplay doors are never generated** (`Assets/Prefabs/Interactive`, outside the
  catalogue).
- **Every tunable value lives in the settings asset** (`GeneratorSettings`, below), not in
  constants: a new tolerance, overlap or rule value the generator or validator needs is
  added there. (The scanner's own detection thresholds stay constants in
  `WallOrientation` / `PieceShape`: changing them means a rescan.)
- **After every change to the generator, Claude runs it in the open Editor** (Unity CLI,
  root `CLAUDE.md`): wait for `recompile_status` to finish, call
  `LevelGen.LevelGenerator.Generate(<spec path>)` by reflection through `unity command
  eval`, save the scene, read the Console's validator and generator logs - the street
  test must pass, the street clearance should refuse nothing but stray corner
  foundations, and **the report (stage 6) should list no free wall end**; one it lists is
  looked at (a top-down or street-level render from a temporary camera, with the
  `Roofs` group hidden for the top-down, is how) before the work is called done - then
  check the scene (gaps along street edges, hidden ground tiles). The rolled
  segment widths come from `LayoutSpecValidator.ValidateFile` the same way, since the
  spec's widths are only the base. The finishing passes each log one line (`Foundation
  colliders`, `Walkway colliders`, `Wall colliders`, `Roof colliders`, `Nettles`,
  `Visibility cull`, `Roof edge`, `Roof
  features`, `Roof railings`, `Pipes`, `Vents`, `Lamp posts`, `Building colours`), read
  with the rest. An eval sent straight after a script edit can run
  the old assembly: check the new code is in (a new field or type seen by reflection)
  before trusting a generation.
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
  wall-tall, plaster-masonry and masonry-woodlarge styles. `example-block.json` (sector
  `Example Block`, 100m east of the street): a hexagonal ring of two streets (turned 15 degrees) (three segments each, meeting end to
  end), facades on its outer side, and a lot (`merchant-house`) inside it with neither
  outline nor modules, so it takes the six-sided block the ring encloses, facing south.
  `example-quarter.json` (sector `Example Quarter`, 200m east of the street, laid out in a frame turned 22
  degrees): lots and side streets together - a gently bending main street with two
  side lanes leaving it square and a back street (its two segments in line) closing a
  five-sided block round a lot (`guild-hall`, its block from the streets, facing the
  main street), facades on every outer side, and two alleys leaving the middle of the
  main street and the back street square, away from the lot, splitting those
  facades: street corners where lanes and alleys branch off, an inside L where the
  back street meets the west lane, a lane running on past a junction, and dead ends.
  `example-two-lots.json` (sector `Example Two Lots`, laid out in a frame turned 30
  degrees, 110m north of the street): streets at
  two levels - a four-sided ring of two streets round a lot (`counting-house`) at
  the street level 0, and another round a lot (`warden-hall`) 1.62m up, the two
  halves of each ring meeting end to end - joined by exactly two link streets leaving
  the low ring's east side mid-edge and meeting the high ring's west side mid-edge,
  square at both ends, each climbing by one flight of steps on its second segment; a
  lane and an alley leaving the links' bend points square, an alley off the low
  ring's west side and a spur off the high ring's east side, all dead ends; facades
  on every outer side, lot origins at their street level plus `groundStoreyHeight`.
  `example-three-levels.json` (sector `Example Three Levels`, laid out in a frame
  turned 37 degrees, centred at world (40, 300)): streets at three levels - three
  lots in rings (`counting-house` at 0, `guild-house` 4.86m up, `warden-hall` 9.72m
  up: three flights of steps between levels), each ring joined to both others by a
  chain of streets meeting at square corners, each chain climbing on one segment per
  level change, and seven dead ends, one of them the layout's one sharp corner.
  **Its street test fails**: the facade beside a three-flight climb stands at the low
  level and its upper storey and roof stand in the raised street (the maintainer is
  to choose between the generator standing each module beside a flight at the
  flight's surface height there, and specs splitting a climb into one-flight
  segments with landings).
  **The examples' sectors stand apart in world space, so all five can be in one scene
  without overlapping; a new example spec is placed clear of them.**
  The other examples are turned off the axes too. **Specs Claude writes keep streets
  at irregular angles, at least about 15 degrees off the world axes; every branch
  meets the street it leaves within `branchSquareTolerance` of square (the
  validator's error), so sharp corners come only from bends - `sharpCornerShare` of
  them where the layout allows (the validator warns when a spec is more than a
  corner off it); a side street branching off a block's street mid-run shares a
  point added there, with the facade on that side split at it, and a street with a
  point mid-run for a branch keeps its segments either side in line (two nearly
  parallel edges of different widths would cut a short side into the block a lot
  takes). Two streets chained end to end meet square or within the branch angle of
  straight on (a join more than `branchAngle` off straight counts as a branch when
  the validator sizes frontages); a bend of 30-45 degrees inside a street, or a
  near-straight end-to-end join, leaves bare wall ends on both sides, so neither is
  written. A street's `width` is left 0, so it takes the settings'
  `streetWidth` (a spec's own width overrides the setting); only an alley gives an
  explicit narrower width (4m in the examples). Frontages are sized to the widths
  the settings give (the validator reports any too long).**
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
  facade, `maxBuildingHeight` (13m: a building's or column's storeys stop where the
  next one's top would pass this above its street, whatever its count - so tall
  storey styles such as wall-tall's 4.5m never run away above their neighbours;
  `StoreyLevels`), `storeyBias` (-1 to +1, 0), `lotNeighbourDistance` (15m), `lotStoreyBias`
  (-2 to +2 storeys, 0). **Facades:** `groundStoreyHeight` (0m, -1 to 1.2: how far
  the base of the ground storey's walls, and so its doors, stands above the street),
  `foundationHeight` (0.3m: how far the foundations' tops stand above the street, set
  apart from the walls),
  `foundationFrontOffset` (0.1m: how far their fronts stand out past the walls' front
  line), `foundationColliderTolerance` (0.1m: merged collider boxes, Foundations below
  - two boxes become one where the box round both stands out past them by no more
  than this; the walkways' boxes use it too), `wallColliderTolerance` (0.15m: the
  facade walls' merged boxes - the window-base walls' collider fronts, their sills,
  stand 12-14cm proud of the plain walls', so less merges little),
  `roofColliderTolerance` (0.1m: the roof slope panels' merged boxes - the two rows'
  collider tops differ by 7cm), `fillStretchShare` (0.3: the fitting rule's share - the length left under a
  piece widens the last piece below it, adds a scaled-down piece above it),
  `groundDoorShare` (0.1), `groundWindowShare` (0.3), `upperWindowShare` (0.4), `storeyBeamOverlap` (4cm), `maxStoreyWidthMismatch` (0.3m: how far a storey's module width may differ from the ground storey's). **Corners:** `cornerAngleTolerance` (20°: a corner this close to a right angle counts as square - its walls meet as at a right angle, its roofs meet along a hip or valley), `cornerMaxReach` (8m), `cornerOverrun` (0.5: the share of the way from the other side's back line to its face that each side's walls run at a corner, both ends hidden inside the other wall). **Jetties:** `jettyShare` (0.3: buildings whose upper storeys jut out), `jettyInset` (5cm: how far the supports' backs sink behind the ground walls' main front surface), `jettyStoreyDrop` (10cm: how far the first upper storey - its beam band, else its walls - stands down into the supports' deck). **Columns:** `columnWidthMin` / `Max` (1-2 modules), `columnShare` (0.15: off the line), `columnMinRunModules` (6), `columnDepthMin` / `Max` (0.3-0.8m), `columnOutShare` (0.5), `columnHeightShare` (0.3: taller or lower than the building), `columnStoreyVariation` (1 storey). **Doors:** `doorInsertMargin` (5cm, at the top), `doorSideMargin` (0.13m, each side), `doorInsertDepth` (0m). **Window inserts:** `windowInsertShare` (1), `windowInsertMaxMargin` (0.45m), `windowInsertDepth` (0m), `litWindowShare` (0.3: the share of the holes filled whose insert is a lit one). **Side walls:** `sideWallMultiplier` (0-5, 1), `sideWallTuck` (0.26m: how far into its column a side wall at the column's left end stands). **Dressing:** `pipeRunsMax` (2), `pipeExtraRunShare` (0.35), `pipeMinRunShare` (0.5), `pipeLargeShare` (0.1; nothing while only the thin pipe kit is verified), `pipePanelInset` (0.6m: how far from a module's left end a plumb run stands, on the plaster past the end post), `pipeWallGap` (5cm), `pipeOpeningMargin` (0.25m), `pipeEndSpread` (0.5), `valveShare` (0.3), `valveHeight` (1.3m), `ventShare` (0.4), `ventsMax` (2), `ventLargeShare` (0.3), `ventFlatTolerance` (3cm), `walkwayShare` (0.3), `lampSpacing` (14m; the asset has 20m: the least distance between any two lamp posts, over every street), `lampEdgeInset` (0.6m), `lampSink` (0.2m: how far a post is lowered into the ground tiles). **Smoke and steam:** `chimneySmokePrefab` / `chimneySmokeShare` (0.5), `ventSteamPrefab` / `ventSteamShare` (0.5). **Foliage:** `grassCover` (0.05 of the ground tiles), `grassClusterMin` / `Max` (2-4 tufts), `grassClusterRadius` (0.5m), `grassEdgeMargin` (1m), `nettlesPerStreetMin` / `Max` (2-4), `nettleOut` (0.2m), `nettleSpacing` (2m), `nettleDoorClear` (0.6m), `foliageScaleMin` / `Max` (0.8-1.2), `foliageSink` (3cm). **Layout checks:** `overlapTolerance` (5cm),
  `minSegmentLength` (0.1m), `junctionDistance` (0.1m), `branchAngle` (17.5°), `branchSquareTolerance` (20°: a
  street ending at a junction where at least two other ways leave the point - another
  street running on through it, or more streets meeting - must meet the street it
  branches off, the line through the two other ways most nearly opposite, within this
  of square; two streets meeting end to end are a bend, not a branch).
  **Street clearance:** `streetHeadroom` (2.2m: a piece this high above a street may
  overhang it), `streetBallRadius` (0.5m) and `streetBallHeight` (1m: the street
  test's ball).
  **Sharp corners (for layout specs):** `sharpCornerAngle` (60°: a corner between two
  streets narrower than this is sharp) and `sharpCornerShare` (0.25: the share of a
  layout's corners Claude makes sharp; the generator clears any angle).
  **Ground tiles:** `groundOverlap` (3cm), `groundTileTurns` (on), `groundSlideMin` /
  `Max` (0.2-0.8 of a tile), `minTileCoverage` (0.1m), `groundGridMinAngle` (15°: the ground grid's least turn off the world axes), `lotGroundMargin` (1m: ground laid round a lot). **Steps:** `landingDrop` (2cm: how far a flight's landing lies below the street, so the grid's tiles overlapping it lie on top). **Roofs:** `roofPitch` (60°), `roofRows` (2), `roofEaveOverhang` (0.4m), `roofEaveDrop` (1m), `roofVergeOverhang` (0.2m), `roofRowOverlap` (0.25m), `roofPanelOverlap` (0.45m: enough to cover a verge end's set-back top tiles where it faces the next panel), `roofGableSink` (5cm), `roofHipOut` (0.75m) and `roofHipRaise` (0.2m: how far out of the corner, along each slope's front, and above the eaves a hip strip's lowest piece stands), `roofHipStep` (2.5m: the most one piece of a hip strip rises above the one below) and `roofHipLean` (10°: how far back each piece leans, its outer corner up), `roofHipAngleMin` / `Max` (50-135°: the corner angles, between the eaves, whose roofs meet along a hip under a strip; outside them, gables), `roofFeatureMinPanels` (2: a roof this many panels long may get a chimney or dormer), `roofFeatureSpread` (0.5: how much of the roof about its middle one may stand along), `roofDormerUp` (2m up the slope) and `roofDormerSink` (0.1m), `roofChimneyUp` / `UpMax` (4-8m up the slope), `roofChimneySink` (0.15m), `roofChimneyRise` / `RiseMax` (0.8-2.5m above the tiles at its back), `roofRailingShare` (0.5 of buildings), `roofRailingSink` (0.15m below the ridge cap), `roofRailingLean` (12°), `roofRailingInset` (0.4m from the ridge's ends), `roofRailingGap` (1m). **Visibility cull** (last of all): `cullEyeHeight` (1.6m: the probe looks from this high above the street, along its middle and both edges), `cullHeight` (9m: and from this high above the middle, for the roofs), `cullStep` (2m: between looks), `cullRange` (80m: the farthest it looks), `roofEdgeGap` (1.5m) and `roofEdgeReach` (20m: a roof piece with nothing but empty space from the gap past its building out to the reach loses its colliders). **Decking:** `deckingOverlap`
  (3cm), `deckingQuarterTurn` (off), `deckingSlideMin` / `Max` (0-0).

## Stages

1. **Catalogue scanner** — measures every variant and writes `catalogue.json`.
2. **Annotations and verification** — a Scene view tool that shows a piece's pivot,
   measured bounds and assumed front for the maintainer to confirm into
   `annotations.json`.
3. **Layout spec** — the format and its validation.
4. **Streets and ground** — ground tiles along each street segment and at junctions.
5. **Buildings and hierarchy** — each frontage's facades laid side by side along the
   street's edge, each in its own frame, with a corner and a short side return
   where a frontage ends at a branching street; the wedge at each bend and the length
   left at a frontage's end filled; and each lot's whole building within the lot's
   frame. Both from parts: foundations, then per storey wall modules (plain, window and
   door modules being the same width, so any can stand in for another), corners
   where the walls themselves meet (no corner pieces), and a roof from the roof kit (no floors or interiors: those are
   made by hand); the front faces the street. **Window walls can stand at any module of any
   storey**, the ground storey included, mixed freely with plain walls. **Where several pieces fit a module, one is chosen
   at random (from the seed), never the same piece as the module before it** along
   the run, unless it is the only one that fits. **The hierarchy is streets >
   buildings > columns > storeys:** every facade building is cut into columns of one
   or more modules, each with its own depth and storey count. **Side walls where a
   column stands taller than its neighbour** (in its building or the next): each
   storey of the taller column above the lower one's top gets a side wall at that
   end, facing that way, standing just outside the end against the front wall's end
   and running back from its face, so the end is covered and from the street the
   building seems to go on behind. Its length in modules is `sideWallMultiplier`
   times the column's width in modules, at least one (0 = none). Separate from roofs
   and roof walls. Placed under
   `Environment/Sectors/<Sector>/Geometry/Generated` (`GeneratedRoot`), which a regeneration
   replaces whole. A regeneration warns first if anything inside it was edited by hand.
   Built in steps, each testable alone:
   - **5a** the straight runs: foundations and storeys of wall modules along each
     frontage, broken at bends (built; `FacadeBuilder`, below).
   - **5b** bends and ends (built; Corners, below): **a facade breaks at a bend** and
     goes on along the next segment's edge in that segment's frame, both runs ending
     at one point; on the inside the walls overlap behind their fronts, on the outside
     each side is carried to the corner and **the walls meet there** - the side
     ending in its walls' trim runs on to the other's face, the other tucks in behind
     it. **Two frontages of different streets meet
     at a street corner** the same way; a frontage end at a junction that meets none
     turns a one-module side return into the branching street. **The length left at a
     frontage's end extends its last building** with whole modules and the fitting
     rule closes the rest; at a dead end one more module overruns behind the dead
     end's facade.
   - **5c** the side walls where a taller column stands beside a lower one, along a
     straight run (built, with the columns).
   - **5d** the lots' whole buildings: walls along every side of the footprint, corners (built;
     Lots, below).
   - **5e** roofs over every facade, dead end and lot (built; Roofs, below).
6. **Report** (built; Report, below) — after everything is placed: every front wall
   end nothing stands against that can be seen from a street, every piece refused for
   standing in a street, every side wall or wall left out, each listed in the Console
   and marked in the scene.
7. **Finishing passes** (built; Generation, below, gives the order): the foundations'
   and walkways' colliders merged into few boxes, the nettles at the foundations, the
   street test, the visibility cull (every piece no one in a street can see removed),
   the roof edge colliders (roofs facing the void lose theirs), then the facade walls'
   and the roof slope panels' colliders merged into few boxes; and, last in the
   facades' build, one colour per set of joined buildings and the lamp posts as
   gameplay lights.

## Facades (stage 5a)

- **`FacadeBuilder`** builds every frontage, in the spec's order, after the streets,
  under `Generated/Facades/<street> <side> <from>-<to>/Building <i>/` (`Foundations`,
  `Storey 0`, `Storey 1`, ...).
- **Runs:** one straight run per segment along the frontage's street edge (the middle
  line pushed out by half the segment's width). **At a bend the two runs end at one
  point** (`BendEnd`), whatever the two segments' widths: on the street's inside of
  the bend (the facade's outer corner) where the two edges cross, short of the point,
  a corner joint carrying the walls on from there; on the street's outside (the
  facade's inside corner, `InsideBend`) where the walls' back lines cross, past the
  point, so the walls overlap behind their fronts and the corner closes by itself.
  Only where that point is further than `cornerMaxReach` from the point's foot
  (nearly straight edges of different widths cross far off) does a run end on the
  bend's bisector through the point instead (`Bisector`). **Then every run is cut to
  the longest stretch where its walls and foundations stand in no other street**
  (`StreetClearance.Clear`, Street clearance below): the band from the foundations'
  front (`foundationFrontOffset` into its own street) back to the deepest of the
  walls' setback and the foundations' depth (`FoundationBack`), against every
  segment's rectangle but its own segment and its neighbours, each kept clear by how
  far a piece of the frontage stands out past its module's end (`Overhang`: a wall's
  trim past its back plane, a foundation's excess width) plus `overlapTolerance`. So
  a run stops clear of a street branching off at its ends (half the branch's width at
  a right angle, far more at a sharp one, where the back line crosses last) and of a
  street passing close behind; a run with no clear stretch is left out, and a
  frontage with none is not built (logged). The
  buildings are laid module after module from the frontage's first point at the
  ground storey's `ModuleSpacing`; a module that would pass its run's end (beyond
  `sizeTolerance`) starts the next run, **the length left on the run first closed by
  the fitting rule** (`FillRunEnd`: under `fillStretchShare` of a module, the run's
  last module is widened to the end - `ModuleSpot.Width`, read everywhere through
  `Spacing()`; otherwise a narrower module is added, always a plain wall), and **one
  that would pass the last run's end is left out with the rest of the frontage's
  modules** (logged; the validator reports such a frontage as too long). **The length
  left after the buildings** (`ExtendLastBuilding`): the last building goes on with
  whole modules while they fit, moving to the next run as above, the rest closed by
  the rule; where the frontage ends at a dead end (one `StoreyPlan.DeadEnds` closes)
  or at an inside L, one more module is added instead, its overrun behind the dead
  end's or the other street's facade, so the frontage always meets it; a frontage that
  starts at a dead end or an inside L gets the same overrun module before its first.
  Seen from the
  street a left frontage runs left to right along the street, a right frontage right
  to left, so `doorModule` (counted from the left over the spec's `widthModules`, so the
  added modules never move it) is mirrored on a right frontage.
- **Beside a flight of steps** (`Run.Stepped`, `StepAt`; `StepFlights`): the street is
  level either side of the flight, so the run stands its modules on the low or high
  level with the change on a module's end - **the first module wholly past the
  flight's top stands on the high level, every module before it on the low level**
  (the street rising against their fronts, never a module over the steps), and
  **columns are always split there** (`SplitColumns`). The side walls between the two
  columns compare their tops in metres above each one's own street level
  (`FillSide`), so the higher column's storeys above the lower one's top are closed
  as for any taller column. **A module on the low level with the street rising in
  front of it** (beside the flight or its landing; `RisesInFront`, from the run's
  `Flight` and `FlightOffset` and `StreetSteps.Flight.SurfaceAt`, the steps' line as
  walked) **gets more masonry foundations stacked on its own** (`StackBesideSteps`):
  its style's foundations, at random, never the one before, the top one's top
  `foundationHeight` above the highest the street comes in front of the module, each
  below overlapping the one above by `wallOverlap`, down to its own foundation,
  placed like it - so a stone cheek closes the gap between the steps or the landing
  and the walls. A jettied building's supports run on beside the steps like the
  storeys they carry.
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
  `PiecePlacer.Pick`, never the piece before it along that storey. **An overrun
  module** (`ModuleSpot.Overrun`: one standing partly behind another facade, at an
  inside L or a closed end) and **a module narrower than the ground module** (added to
  close a run's end) are always plain walls, so no door or window is cut off or
  squeezed. **A plain wall marked `fillsStorey`** (`PlasterWalls_B_MasonaryWall`), where the storey's plain walls hold one, stands at every module but the spec's door: no other plain walls, random doors or window walls on that storey (the draws are still made). **Plain walls marked `onePerStorey` never mix on a storey** (`WallTall_WindowBase` and `_StoneWindowBase`): each building storey draws one of them (`StoreyLevel.OnePerStorey`) and uses no other so marked - its modules, and its corner fillers, side walls and jetty ends through `PlainWalls` (`SideWallChoices`); doors and window walls are drawn as usual. Storeys stand on
  each other at their styles' `StoreySpacing` (or on a beam band, below), the ground storey's base
  `groundStoreyHeight` above the street; every storey is laid at the ground storey's module
  spacing, so they stand in line, and **every wall is scaled along its width to fill its
  module exactly** (the module's width plus the overlap), so a storey whose style has
  another module width (up to `maxStoreyWidthMismatch` off, e.g. plaster on cellar
  walls), and a module widened or narrowed to close a run's end, joins its neighbours
  with neither gap nor overlap. A building whose ground style has no module yet
  stops the frontage there (logged); a module with no wall that fits is left and
  reported, the storey going on; a storey whose style has none ends the building.
- **Foundations:** one per module from the ground style's `foundation` pieces (random,
  no repeat), centred on the module along the street and scaled along its width to it
  as the walls are, the top of its render box `foundationHeight` above the street and
  its front face `foundationFrontOffset` out past the street edge (the walls' front
  line); above the ground storey's base it shows as a plinth in front of the walls. A
  style with no usable foundation is reported and its walls built without. Doors stand
  `groundStoreyHeight` above the street; steps up to them aren't planned yet. **Once
  everything is built, the foundations' colliders are merged**
  (`MergedColliders.RunFoundations`, run by `LevelGenerator` straight after
  `FacadeBuilder.Build`, before the street test, which then checks the boxes): every
  placed `foundation` / `foundation-corner` piece loses its upright box colliders,
  which are sorted into frames (a collider's right, up and forward, matched either
  way round within 0.05°, same layer and `SurfaceTag`) and merged pairwise until none
  can - one box holds the other, or two axes match and the third meets - each within
  `foundationColliderTolerance` (0.1m; the foundation pieces' buried bottoms differ
  by about 9cm, so less stops neighbours merging). The boxes (`Foundation <n>`) go
  under `Generated/Foundation Colliders`, with the pieces' layer, static flags and a
  `SurfaceTag` (the source's copied, else Stone; the walkways' boxes Wood -
  `MergedColliders.Run` takes the fallback, and `uprightOnly`: true for the
  foundations, walkways and walls, so a tilted box stays on its piece, false for the
  roofs; `PiecesOfCategories` and `PiecesUnderGroups` pick the pieces); one log line
  (`Foundation colliders: ...`). The four examples go from 65-298 pieces to 9-96 boxes.
- **Wall colliders** (`MergedColliders.RunWalls`, after the visibility cull and the
  roof edge colliders, so no box stands where a piece was removed): every piece under a
  `Storey <n>` or `Corner` group (facades and lots) loses its upright box colliders,
  merged as the foundations' are within `wallColliderTolerance` (0.15m) into boxes
  (`Wall <n>`) under `Generated/Wall Colliders`, fallback Stone (a door keeps its Wood
  tag); one log line (`Wall colliders: ...`). Three-levels: 2471 colliders on 1940
  pieces become 1456 boxes; what is left separate is mostly door surrounds, door leaves
  and windows standing proud.
- **Jetties** (medieval overhangs): each facade building draws at `jettyShare` (always
  drawn); it is jettied where it may have an upper storey and a usable `jetty` piece
  (any style; `Platforms_ShortBoardPlanks_Supports`, 3.02 x 1.58m, its front -Z) fits
  its ground storey's module (`Fits`; not masonry-woodlarge's 4.95m). **Where the
  supports and storeys stand** (`SetJetty`): the supports' backs `jettyInset` behind
  the shallowest main front surface of the ground storey's walls (`FrontPlane`: the
  plaster or stone between the timbers, which stand proud of it; `Building.SupportBack`),
  so every support sinks a little into the wall behind it, their fronts the deepest
  piece's depth further out; **all its upper storeys stand forward by the jetty** -
  as much as brings the front of their typical wall (the median reach of the first
  upper storey's walls) level with the supports' front - on top of each column's own
  depth, in one plane (`StoreyJetty`; walls, beams and side walls use it). **The
  first upper storey stands on the supports' deck, cut into it**: the deck's top lies
  `storeyBeamOverlap` below the top of the ground storey's walls, and the beam band
  (where both styles have beams; else the walls) stands `jettyStoreyDrop` (10cm) down
  into the deck, as it would on the walls but lower, so the beam and walls meet the
  planks with no seam (`StoreyLevels`; `JettyDeckTop` gives the deck's height back
  from the storey's bottom - `DeckTop` plus the drop - for the supports and the pipes).
  **Supports** (`BuildJetty`, under
  `Building <i>/Jetty/`): one per module whose column stands above the ground storey,
  at random, never the one before it, facing the street, centred on the module and
  scaled along its width to it, the back of its render box at `SupportBack`, the top
  of its render box at the deck's height, so the brackets hang in front of the ground
  walls' top. **Side walls work storey by
  storey** on each storey's depth (column depth plus jetty), so a jettied storey beside
  one that isn't is a step, closed by walls from the jettied side; a step's walls are a
  module long, or longer when the step is deeper (its depth plus the walls' setback).
  Only a ground-storey step (a column's) gets a side foundation. **A jettied storey at a run's end** (a frontage's end or a bend, where no column of the run stands beside it) **is closed by itself** (`CloseJetty`, under `Building <i>/End <n>/`): side walls facing out of the end, standing just outside it against the front wall's end (a storey that high may overhang a street) from behind the wall's face back past its column's own line, a module or the jetty plus the setback if longer, with the beam band below as at a join. Dead ends are never
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
  never the one before it, centred in depth on the wall above it (`DepthMiddle`; its
  back on the walls' back line where that isn't known), centred on the module along
  the street, scaled along its width to the module (`FitScale`, as the walls) and in
  height to the band. **Side walls get beams too** (`LaySideBeams`): under each
  storey that gets side walls and has a band below it, beams laid end to end the same
  length, the same way round and from the same start as those walls, centred in depth
  on them (their median middle), so the band never leaves a gap between side walls.
- **Doors:** every doorway (a `door` opening in the catalogue) of every wall placed gets
  a plain door (`PlaceDoor`): one of the usable `door` pieces at random, never the one
  before in that wall - any style, and never a prefab with a gameplay script (gameplay
  doors and locks are placed by hand). **Sized to the doorway as seen through the
  wall** (`ThroughOpening`: rays through the wall's LOD0 mesh, so a stone frame
  narrower than the hole in the back plane counts; the wall's own scale included):
  `doorSideMargin` wider each side and `doorInsertMargin` taller at the top, so its
  edges tuck in behind the frame (a rectangular door in an arch hides its top corners
  in the stone); its bottom middle on the sill below the see-through doorway's
  middle, at the middle of the wall's depth moved forward by `doorInsertDepth`; facing the wall's way,
  under the wall's storey group. With no usable door piece the doorways stay open.
- **Window inserts:** every facade wall placed (dead ends too, not the steps' side
  walls) has its window holes filled: per `window` opening in the catalogue (holes in
  the back plane: `PlasterWalls_A_WindowBase_Hole` has one of 1.2 x 1.3m,
  `Masonary_WoodLarge_WallWindows` two of about 0.5 x 1m), at `windowInsertShare`, one
  of the usable `window` pieces (verified, not excluded, one front; any style - inserts
  aren't style-tagged) whose annotated width and height are at least the hole's and
  no more than `windowInsertMaxMargin` bigger on each side - **a lit one** (marked
  `lit`, Annotations below) **at `litWindowShare`, else an unlit one**, either kind
  when none of the kind drawn fits, so a hole is never left open for want of one -
  at random, never the one before it in the wall. The insert faces the wall's way,
  its render box's middle on the hole's middle at the middle of the wall's depth (back
  plane to render front), moved forward by `windowInsertDepth`; under the wall's
  storey group. All three draws (fill, lit, pick) are made for every hole. The pack's
  inserts (`Window_A`-`D`, `C_Open`, `D_Open`) are 1.6-2.0m with their frames, so they
  fit the plaster hole; the woodlarge holes stay open. **A blind window wall gets a
  window on its face** (`PlaceInserts` with `faceWindow`): a wall with no `window`
  opening but a connection point named `window*` in its annotation
  (`WallTall_WindowBase`, set by hand where the pack's demo scenes put an insert)
  gets an insert with its render box's middle on the point, moved forward by
  `windowInsertDepth`, facing the wall's way - any usable insert, lit or unlit as for
  holes - but only on a module whose own window draw came up (`windowDrawn && !overrun`),
  so a wall-tall storey, every module a `WindowBase`, shows windows at the window
  shares rather than on every module. **Lit windows** look lit from
  behind: `TL_Window_<x>_Lit` in `Assets/Prefabs/Environment/Windows` are variants of
  the unlit `TL_Window_<x>` with one material override, `WindowLit`
  (`Assets/Art/Materials/Environment/Windows`: a copy of the pack's unused
  `Medieval_Windows_Light_2k` - URP Lit, emission on, the pack's emissive map painting
  the panes only - with its emission halved to (3.1, 1.49, 0.83); not yet judged in
  the headset). **Its Global Illumination is Baked and must stay so: URP turns the
  `_EMISSION` keyword off on import when it is None** and the panes go dark. So a
  lightmap bake adds a faint warm patch round each lit pane, accepted; the gameplay
  side knows nothing of a window's light (no `LightSource`). Only inserts can be lit:
  the blind-window walls draw their panes from wall atlases with no emissive map.
  Nothing runs at runtime for it - an emissive texture, as Thief's lit windows were.
  The lit twins of the four verified inserts are annotated like them (`C_Open` and
  `D_Open` are unreviewed in both forms).
- **Columns:** a frontage's modules are all laid out first (where each stands), then
  split into columns (`SplitColumns`), then the buildings built, then the side walls
  placed (`FillSides`). **Every building is cut end to end into columns** of
  `columnWidthMin`-`Max` modules, never across a building's end or a bend (the last
  column of each takes what is left). Each column rolls, in this order and always
  all of them: its width, off the line (`columnShare`), forward (`columnOutShare`),
  its depth (`columnDepthMin`-`Max`), a height change (`columnHeightShare`), taller
  or lower (even odds) and by how much (1 to `columnStoreyVariation`). **Depth** applies
  only on a straight run of at least `columnMinRunModules` modules and never to a
  column holding the run's first or last module; forward is into the street, and
  only where the step stays out of every other street (`ColumnClear`: the street
  clearance's band from the edge forward by the depth; a lot's side counts as lining
  the street segment whose edge it runs along - `StreetClearance.LiningSegment`, both
  ends within 0.1m of a segment's edge line and facing into it - so its columns step
  forward into that street only, never into a cross street at a block corner, and a
  side lining none stays out of every street). It is
  capped at half the narrowest module of any storey of the frontage, so two
  neighbouring columns differ by at most a module and one side wall fills any step.
  A module's walls and foundation move with its column's depth. **Height:** the
  building's storey count (`StoreyPlan`) plus or minus the change, kept between
  `minStoreys` and `maxStoreys`; the building's storeys are made up to its count
  plus `columnStoreyVariation` (within `maxStoreys`), and a storey whose module's
  column doesn't reach it is left out. Lots and dead ends still read the building's
  count.
- **Side walls** (`FillSides`), between every two neighbouring columns of a straight
  run, in one building or two. Each is a plain `wall` of
  **any width** whose height fits the storey's module height (`Fits`; scaled in height
  to it when beyond `sizeTolerance`) - **not one marked `showsOpening`** while any
  other will do (`BuildingStyle.SideWalls`; ground-storey-only ones only on
  the ground storey) - laid end to end by the fitting rule (`FitWidths` on the
  storey's module width: whole walls, the last widened or one more narrowed, each a
  piece near its width at random - `PickForWidth` - scaled to it; the beam band's
  beams below on the same joints), turned side-on to face out of its column's end
  towards the other column. **Its walls come from the storey's own style, else the
  neighbouring column's style at the same storey, else the styles of the building's
  storeys above**, **else the storey's own plain walls that show a blind window**
  (`showsOpening`; wall-tall has no plain wall showing no opening, and the other
  styles' are far shorter than its 4.5m, so its side walls are its blind-window
  walls); a style with none at all is left without them and logged once per
  frontage. **Any run of side walls going back (here, at
  corners, jetty ends) is cut short where it would reach into a street**
  (`StreetClearance.Reach`: a ray from either face of the walls against every
  street shape, except one it starts in, the outside of every corner counted as
  street) - near a sharp corner the other street lies just behind - the cut length
  then covered by the rule, so the last wall never passes the street nor ends as a
  sliver; a storey standing `streetHeadroom` above the street is not cut (it may
  overhang, as the clearance lets a jetty). A side wall that can't be placed, or a
  run with no room before a street, is reported.
  - **A column's storey whose top stands above the other column's top** (by more than
    `sizeTolerance`, in metres, since styles' storey heights differ) gets
    `sideWallMultiplier` times the column's width in modules (at least one) of
    length.
  - **Where the two stand at different depths**, the forward column's other storeys
    get one module's length each, and the step one `foundation`, its front
    `foundationFrontOffset` out past the ground storey's side wall.
  - **At a column's right end (seen from the street), where its front walls' end is
    plain, a side wall stands in the other column's space, backed against its own
    column's end**: its back plane on the join (`FaceClearance`, 1cm, in from it),
    its thickness in the other column, starting halfway between the main surface and
    the furthest reach of its column's front wall beside the join (`SideWallFace`,
    from `FrontPlane` and `ModuleSpot.Reach`, recorded per storey as the walls are
    placed), so its street end hides among the timbers. So it covers that wall's hollow end - above the other column's top,
    or in front of its face - and its own face never lies on the end's; behind the
    other column's face it runs on through that column's front wall, which hides
    the rest, and above its top it rises through its roof like a party wall.
  - **At a column's left end, where its front walls' end carries its trim, the side
    walls are tucked in behind that end** (`SideWallStart`, for column steps and
    jetty ends, not corners): the join moved `sideWallTuck` back into the column,
    their street end on the front wall's main surface (`FrontPlane`), so the trim
    covers them.
  - **A side wall is placed by the end of its render box**, not its connection point
    (which some pieces' trim stands past).
  - Under `Building <i>/Side <n>/` of the column's building, `n` the join's module
    index. Run ends a corner closes get none; a jettied storey at any other run end is closed (Jetties, above). Dead ends
    are one column.
- **Corners** (stage 5b): `FacadeBuilder.Build` lays every frontage out first
  (`LayFrontage`: modules, the end's extension, columns), then finds the corners
  (`FindJoints`), then builds the frontages, then the corners (`BuildJoint`).
  - **Inside a bend** (the next run heads into the street the run faces,
    `InsideBend`) **the runs are carried past the point to the one point where the
    walls' back lines cross** (`Runs`, `BendEnd`, no further than `cornerMaxReach`):
    the walls' fronts stand back from the edge by differing amounts, and wherever they
    stand they meet before the back lines do, so the corner is closed - each run's
    modules fill it to that point by the fitting rule, and nothing overruns past it.
    (On the street's inside of a bend, the outer corner, both runs stop where the
    edges cross.) **An inside L between two streets** - a
    street ending at a point another street goes on from, turning into the street the
    facade faces (the outer facades round a block meet like this) - is closed the same
    way: both runs are carried to where the two edges cross (`InsideCorner`, in `Runs`),
    and **one module more always overruns behind the other street's facade** at that
    end - at the frontage's start a module laid back past it (`LayFrontage`), at its end
    `ExtendLastBuilding`'s, as at a dead end. **The other street is the one bounding
    the facade's corner**: of every street leaving the point, the first met turning
    from the way the run leaves it towards the facade's side - an L only if it starts
    or ends there (one passing through is a branch) and turns into the street the
    facade faces (not a near-straight run on, nor a street branching off on the
    facade's side). **Where the block's corner is wider than a half turn** (a ring
    bending at a junction) the edges cross behind the point, and the run is carried
    back past it, up to `cornerMaxReach`. No overrun where the street clearance cut
    the run short of the corner.
  - **Outside a bend**, and at **a street corner** - two frontage ends of different
    streets at a junction (another street has a point there: a street branching off,
    each end stopping where it clears the other street (`StreetClearance.Clear`), or two streets meeting end to
    end at an L), on the outside of the corner only (`Outside`: the way out of one end
    points the way the other's facade faces), paired
    where each needs carrying no more than `cornerMaxReach`, shortest pairs first -
    **each side is carried storey by storey** (each storey's line at its column's
    depth plus its building's jetty, against the other side's storey of the same
    index, or its top storey) and **the walls themselves make the corner; no corner
    piece is used** (`WallTarget`). **Each side's walls run `cornerOverrun` (0.5) of
    the way from the other side's back line to its face** (that face is the other
    storey's back line plus the shallowest of its style's walls, `FaceLine`, `Reach` -
    the frontage's line stands out by the deepest wall any of its styles could use -
    less `wallOverlap`), a side's end carried by whichever corner of its end face,
    back line or face, reaches the target line first (`EndReach`), so both ends are
    hidden inside the other wall and the two walls' end posts meet at the corner;
    the strip of each end left between the posts is by design, and the report
    excuses an end another wall meets. The side whose walls end there in their trim
    (`FirstCovers`, `TrimmedEnd`: seen from the street a wall's trim runs down its
    left side, a closed post where the other end is hollow; the first side of the
    pair where both or neither do) is the one that places a sharp corner's cover and
    an in-line joint's column (below). **A side
    already past where it should stop** (the lines meet behind its end: a shallow
    outside bend with a column stepped forward) **has its end module cut back to that
    point before anything is built** (`TrimCornerEnds`, on the storey that must stop
    soonest, never below `fillStretchShare` of a module; the other storeys are carried
    on from it); a side the corner carries nowhere is reported. **A carry no longer
    than `fillStretchShare` of a module widens the end module's own wall** at that
    storey instead of adding a sliver beside it (`WidenCornerEnds`, before the
    frontages are built: `ModuleSpot.WidenStart` / `WidenEnd`, read by `BuildStorey`,
    `BuildBeams` and `BuildJetty`, so the beam band and the supports widen with the
    wall).
    A longer one gets a **filler**: plain walls of the storey's height (borrowed as
    for side walls) by the fitting rule (`CornerWalls`, `PlaceFiller`). Under every
    carried storey the band's beams below a banded storey and the jetty supports under
    a jettied first storey run on to where the two lines meet on the same rule, each
    band only as long as the wall fillers (no sliver beams), and
    foundations under the ground storey too (`PlaceFiller`, `PlaceInRun`), as far as
    `FoundationCarry` lets them. **A corner's fillers are brought forward to the reach
    of the other side's end wall** (`CornerFaceReach`, `PlaceFiller`'s `faceReach`; the
    beams with them), so they stand flush with the wall they meet. **The columns either side of every convex corner stand the same number of
    storeys** (`EqualCornerHeights`, after the corners are found and before anything
    is built: both take the lower count, repeated until no corner changes, since a
    column can end at two). **A corner is the corner of one building, so the
    buildings either side of every joint take the same storey styles**
    (`MatchCornerStoreys`, just before): each set of buildings joined by corners takes
    the storey styles and jetty of its first in the spec's order, so their storeys stand
    at the same heights; each keeps its own module spacing, foundations and door. One
    whose ground storey's module is more than `maxStoreyWidthMismatch` off the first's
    keeps its own (logged, `Corner storeys: ...`).
  - **A corner up to `cornerAngleTolerance` sharper than a right angle** is met as a
    right angle is: the covering side's post stands out past the other side's face by
    its depth times the cosine of the angle (about 8cm at 82°). **Its foundations
    stop where their back meets the other side's foundations' front**
    (`FoundationCarry`), so their back corner stays out of the other street.
  - **A sharper corner**: carried to the tip, a side's walls would cross the other
    side's front into its street, so **each side stops where its back line (its
    setback behind its front) meets the other side's front line** (`CarryToBack`),
    and the covering side puts a **`column`** piece across the notch between the two
    stops (`PlaceCover`, from its storey's style, else the other side's, of the
    storey's height and scaled to it; a column has one front or two, turned so the
    middle of its fronts faces out between the facades, its render box's furthest
    reach that way on the point): a cut-off corner with a post. A style with none is
    logged and the notch left bare.
  - A storey standing above the other side's top is **closed by side walls** facing
    out of the corner instead (`LaySideStorey`).
  - **Nearly in line** (the two lines meet more than `cornerMaxReach` from either
    end, or not at all - a slight bend between segments of different widths, or **one
    street running on nearly straight into another** at a junction: two frontage ends
    of different streets whose ways out are opposite and whose facades face the same
    way, within the branch angle, no more than `cornerMaxReach` apart, paired by
    `InLine`): each side is carried to its own run's end (on a bend's bisector) and
    on halfway across any gap left between the two (`InLineCarry`), by a filler as
    above. Per storey, the side standing further into the street
    (beyond `sizeTolerance`) closes the step with side walls facing out of the
    corner, a module long or the step plus the setback if longer; with no step the
    first side puts a column over the joint between the two ends (as `PlaceCover`).
  - **Side return:** a frontage end at a junction that pairs with none gets a new
    run of one added module along the branching street's near edge (the widest
    street leaving the junction towards the frontage's side), from where the
    frontage's line meets it, facing into that street; the end and the return are
    joined as a corner (`AddSideReturn`). The return's far end stays open.
  - Joined run ends are marked (`ModuleSpot.JoinedStart` / `JoinedEnd`) so nothing
    else closes them. Pieces under `Building <i>/Corner/`.
- **Lots** (stage 5d, `LayLot`, under `Generated/Lots/Lot <id>/Building 0/`): a lot's
  whole building is laid as a closed frontage of one building (the lot's styles and
  `StoreyPlan` storey count) with **a run along each side of its footprint**, in
  order round it from the front side, each facing out (outward found from the
  corners' winding), each run given the street segment whose edge its side runs
  along (`LiningSegment`), which its columns may step into (logged `n run(s) (m
  lining a street)`). **The footprint** (`LotSpec.Corners`) is the line the walls'
  fronts reach: the lot's `outline` if it has one (any convex shape - three, five, six
  sides; **a lot with neither an outline nor modules takes the block its streets
  enclose**, below), else the rectangle of `widthModules` by `depthModules` modules plus the
  walls' overlap, centred on `origin`, its front facing `yaw`. Each side gets as many
  whole modules as fit, laid from its start; the corners fill the rest. The spec's
  door is counted along the front side from the left as seen from in front (whichever
  way that side runs). The ground storey's base stands at `origin.y` (the street level
  under it being `groundStoreyHeight` lower). **Everything a facade gets applies per
  side**: columns stepping in and out and standing taller or lower, jetties (on every
  side at once), beam bands, doors, windows, foundations, side walls. **The corners
  are joints** like a bend's (the last side joined back to the first), the walls
  meeting as Corners (above) says. **No floors or
  interiors: those are always made by hand.** Roofed as Roofs (below) says. **The street ground is
  laid under each lot** (`StreetGround`, below). The validator checks an outline:
  convex, every side long enough for a module, the door within the front side's
  modules; overlaps use its bounding rectangle.
- **Roofs** (stage 5e, `FacadeRoofs.cs`, part of `FacadeBuilder`; built last, after
  the corners and dead ends): **in a tree of their own, `Generated/Roofs/<frontage,
  lot or dead end>/Building <i>/`** (`RoofGroup`), beside `Facades` and `Lots`, so
  every roof can be hidden in one go while working on the level. **The pack's own
  way - a ridge along the street, a slope each side meeting at it, gables at the
  ends.**
  - **Pieces** (any style; categories set in Review Pieces): `roof-end`
    (`RoofTiles_MiddleSide`) and `roof-ridge-end` (`RoofTiles_TopCorner`, carrying the
    ridge cap) for a row's two ends, `roof-middle` between them, `roof-end-half` /
    `roof-ridge-end-half` for a roof of one panel, `roof-gable` (whole gable
    triangles: `RoofWall_LargeA` / `B`, `_MedA` / `B`; the half gables
    `RoofWall_CornerA` / `B` are unverified and unused), `roof-chimney`
    (`TL_Chimney_full`, the maintainer's own variant in `Chimneys`), `roof-dormer`
    (`TL_Extension_Small_window`, in `Extensions`: a box with a window on its +Z front
    and a little gabled roof falling to its back, pivot at its back bottom),
    `roof-railing` (`IronFixtures_ArowRailing_Long` 2.42m and `_Short` 1.2m), and
    `roof-hip-base` (`Roof_PeakedBase`: the lowest
    piece of a tower spire's corner stack, its outer corner between its two `fronts`;
    connection points `eave` on its outer bottom corner and `next` where the piece
    above stands by its pivot, from the pack's tower - the only hip piece used;
    `roof-hip-middle` / `-top` are annotated but unused). **Only closed (manifold) pieces are used**: the pack's
    `RoofTiles_Middle` and the two `_Half` panels have open edges that show as gaps,
    so they are excluded; with no usable middle, the row's end pieces stand between
    its ends too (`Middles`), and with no half, a one-panel roof is one end piece.
    With no usable end and ridge-end piece, no roofs (logged).
  - **One roof per stretch of a run** whose columns' tops (in metres) and top
    storeys' faces (column depth plus jetty) line up; at a run's end it goes on as
    far as a corner carried the top storey (`ModuleSpot.CarriedStart` / `End`). **A
    roof's end stands at the outer face of the top storey's end wall**
    (`ModuleSpot.SideFaceStart` / `End`, written by `BuildCornerSide`, `FillSide` and
    `CloseJetty` as the wall's reach plus its thickness; `RoofStretches` ends there
    through `EndOut`), the verge `roofVergeOverhang` past it.
  - **A slope:** rows of panels (`roofRows`, 2) tilted to `roofPitch` (60°), the
    eave `roofEaveOverhang` in front of the top storey's face and `roofEaveDrop`
    below the top of its walls, rows overlapping by `roofRowOverlap`. Along it, as
    many panels as round to its length plus `roofVergeOverhang` past each end,
    scaled along their width to fill it, overlapping by `roofPanelOverlap`: verge
    pieces at the two ends (they carry their verge at their own +X; the other end's
    is mirrored with a negative scale), plain ones between (the same pieces, so a
    verge faces the next panel at every joint: its top tiles are set back about 0.3m
    behind its render edge, which `roofPanelOverlap` must cover); a roof of one panel
    is two mirrored halves. The top row's ends are the ridge pieces, except on a row
    of one panel, which never takes the ridge piece (`LaySlope`).
  - **The back slope mirrors the front**, so the roof reads whole from behind and
    from the next street; a facade's roof is never shortened for a street behind
    it (a stretch scaled apart from its neighbours stands at another height and
    opens a gap between them; at roof height a slope over a street is clear of it).
  - **A roof end a corner can't close gets a gable** (`SetGable`): where
    `RoofCorners` takes no corner for a joint - a lot against a facade, roofs of
    different heights, a convex corner wide of a right angle, folded or parallel
    eaves, or eaves so nearly parallel (two roofs in line) that they cross more than
    `cornerMaxReach` from either roof's end - both roofs end on their own and each end
    is gabled, never left open.
  - **Roofs are worked out before any is laid** (`RoofStretch`: `RoofStretches`, `RoofShape`),
    then the corners where two roofs meet found and the roofs' ends set to meet there
    (`RoofCorners`, from `RoofJoints`: every joint plus every bend on the inside, which
    has no joint), then all laid (`BuildRoof`), the hip strips last (`HipStrips`,
    `PlaceStrip`).
  - **Corners:** where two roofs (facades' or a lot's sides) end at the same joint or
    inside bend and their walls' tops are within `sizeTolerance` (the corner storeys
    match, Corners below), **they meet along the hip or valley line - the line equally
    far back from both eaves - instead of overlapping gabled roofs**: no gables, and
    each row of each slope ends on that line. A row lying b back from its eave ends
    `slant` x b past the point where the two front eaves cross, towards the corner,
    where `slant` = (the other roof's back direction . the way out of this end) /
    (1 + the two back directions' dot): negative on the outside of a corner (the rows
    stop short, the line is a hip), positive on the inside (the rows run on under the
    other roof, the line is a valley); a back slope's rows, their eave 2 x ridgeBack
    behind the front's, end `slant` x (2 ridgeBack - b) past it, so a convex corner has
    a hip in front and a valley behind, a concave one a valley in front and a hip
    behind. Into a valley a row runs until its top edge reaches the line (closing it all
    the way up, its bottom corner hidden under the other roof); at a hip it stops where
    its middle meets the line, its corners straddling it under the hip strip. Into a
    valley a row runs `roofPanelOverlap` further, so a verge end's set-back top tiles
    still close it. The end panel there keeps its verge at the corner, hidden (turned
    inward it would meet the next panel's verge, two set-back ends leaving a slit). A
    convex corner is taken where the angle between its eaves lies within
    `roofHipAngleMin` to `roofHipAngleMax` (50-135°; `RoofCorner.Angle`, `Hipped`); a
    wider or sharper one keeps the gable rules below (a near-straight bend's hip
    piece would be many times wider than it is tall). A concave corner of any angle
    is taken, its back hip stripped when within the range. Two facade
    roofs meeting take the lower of their row scales, so their ridges meet at one height;
    a lot's sides keep theirs (each reaches the footprint's middle).
  - **Hip strips** cover the hips of hipped corners, where the two slopes' rows cross
    (`HipStrips`, `PlaceStrip`): **copies of the one `roof-hip-base` piece stacked up the
    hip**, from where the two eaves cross to where the ridges cross (as far in from
    both eaves as the shorter slope reaches, at the top of its ridge cap, `RidgeCap`),
    each piece's outer corner facing out between the slopes and **leaned
    back `roofHipLean` (10°)** so its faces, steeper than the roofs' pitch, lie flatter
    on the slopes. The lowest piece's `eave` corner stands `roofHipOut` (0.75m) out of the
    corner along each slope's front (the piece's own corner is cut back) and
    `roofHipRaise` (0.2m) above the eaves; the top piece's `next` point is at the ridges'
    crossing; between them the pieces step evenly, each rising at most `roofHipStep`
    (2.5m) above the one below, so they overlap like scales (three on a two-row roof).
    **The right-angled piece is fitted to the corner's angle**: scaled across the hip
    by tan(angle / 2) and in height by across x sqrt(2 / (1 + across²)), so its faces
    turn to the slopes at the same pitch (a 61° corner 0.59 x 0.72, a 128° one 2.03 x
    1.27, its tiles visibly wider, accepted); the scale is in the hip's frame, so each
    strip's pieces stand under a `Hip` empty (one per strip, under the building's roof
    group) holding the frame's rotation, lean included, and scale, the piece turned
    45° inside it. `RoofEdgeColliders` finds a piece's building group through
    `BuildingGroup` (two below the `Roofs` root), not its parent.
    In front of a convex corner always; behind a concave one when both roofs have a
    back slope. With no usable hip piece, no strips.
  - **Gables:** every roof end gets one whole `roof-gable` triangle (`RoofGable`):
    its pivot plane on the roof's end (the four `RoofWall_*` pieces have their wall
    face within 0.1m of the pivot plane and their beam ends 0.45-0.6m in front of it,
    so placing by the render front would put the face half a metre inside the end),
    upright under the ridge, the triangle 2 x (ridgeBack - under) wide and ridge -
    walls' top + `roofGableSink` tall, pivot at the middle of its base; `GablePiece`
    takes the piece whose height-to-width is nearest the triangle's, at random among
    those within `GableShapeTolerance` (0.1; the Med gables on 60° two-row roofs),
    scaled to it - except at a corner (above), and at a corner wider than
    `roofHipAngleMax`, where the other side's roof runs on over it
    (`ModuleSpot.GableStart` / `End`, recorded by `BuildCornerSide`).
  - **Lots:** each side slopes up towards the footprint's middle (no gables). Where
    the middle is nearer than a facade roof's slope reaches, the rows reach it,
    scaled up the slope to end there, and the sides meet as a hipped roof (at a right-angled
    corner, under a hip strip: Corners, above); a bigger
    lot's sides get both slopes, round a courtyard, scaled down so the inner eave
    never passes the middle.
  - **Chimneys and dormers** (`BuildRoofFeatures`, after the hip strips): one per
    building whose first roof is at least `roofFeatureMinPanels` panels long (the
    span over the roof's end-piece width, rounded), chimney or dormer at even odds
    where both pieces are usable; along the run within `roofFeatureSpread` of the
    building's span about its middle, half the piece's width in from the ends. **A
    chimney straddles the ridge** (it needs a back slope): its front face drawn
    between `roofChimneyUp` and `roofChimneyUpMax` up the slope, capped so its back
    stays below the ridge, its base `roofChimneySink` below the tiles at its faces,
    stretched up (never shrunk) so its top stands between `roofChimneyRise` and
    `roofChimneyRiseMax` above the tiles at its back - so chimneys sit at different
    heights and the tallest stand clear above the ridge. **A dormer stands on the
    front slope**, the bottom of its front wall (`DormerFront`: its mesh's lowest
    vertices' furthest-forward z, found once a build) `roofDormerUp` up the slope and
    `roofDormerSink` below the tiles, refused (a chimney instead) where it would top
    the ridge or its back would pass it; at 52° pitch its back top corner would show
    (fine at 60°). Known: a dormer's open back shows through the back slope of a
    narrow roof. **Chimney smoke** (`chimneySmokePrefab`, `chimneySmokeShare`): a
    chimney that smokes gets the prefab (`Assets/Prefabs/Effects/Chimney Smoke`, a
    Shuriken particle system) as a child, at the middle of the top of the
    `IronFixtures_Chimney` pot inside the piece (its renderer bounds; the piece's own
    top if it has none), pointing up, so the cull takes a hidden chimney's smoke with
    it (`FacadeBuilder.PlaceEffect`); the small pot stays cold. One log line (`Roof
    features: ...`).
  - **Ridge railings** (`BuildRoofRailings`, `PlaceRailings`, after the features):
    `roofRailingShare` of buildings get `roof-railing` pieces laid end to end along
    the ridge of their first roof - longest first, shorter ones in the remainder,
    `roofRailingGap` between, the row centred, `roofRailingInset` in from the ridge's
    ends (at a corner, from where the ridge meets the other roof's: |slant| x
    ridgeBack in from the eaves' crossing) - each leaning up to `roofRailingLean`
    along the ridge and a third as much across. **The feet are seated by a ray**
    (`RoofTopAt`: down from 2m above the worked-out ridge height onto the highest
    `TL_Roof*` Environment collider within 4m, after `Physics.SyncTransforms()`),
    `roofRailingSink` below the ridge cap's top. A piece is skipped where the ridge
    is buried under another roof (an upward Environment ray of 3m from the seat
    hits) or the roof top under either end differs from that under its middle by
    more than `RailingLevel` (0.3m: a hip, a corner, past a gable); the visibility
    cull removes any railing left with no `TL_Roof*` piece within `RailingDrop`
    (0.5m) below its root. Measured against the maintainer's hand-placed railings
    (the `example` object in `Level Gen Test.unity`): feet 0.05-0.29 below the cap,
    gaps 0.6-1.4m, leans up to 13°. One log line (`Roof railings: ...`).
  - Anywhere else roofs meet they overlap (the pack has no hip or valley tile panels).
  - **The visibility cull** (`VisibilityCull.cs`, run by `LevelGenerator` last of
    everything built, after the street test): **every piece the generator placed that
    no one in the streets can see is removed** - ground tiles under and behind the
    buildings, back slopes, walls, beams, pipes, walkway parts and foliage hidden
    behind other pieces; the lamp posts (the `Lamps` group) are never culled, being
    lights. A probe walks every street (`StreetTest.Walk`, up and down the flights,
    stopping a ball's radius plus `overlapTolerance` short of a dead end), looking
    every `cullStep` (2m) from `cullEyeHeight` (1.6m) above the ground at the middle
    of the street and at each edge (a ball's radius in from the buildings) and from
    `cullHeight` (9m) above the middle, for the roofs; a look inside something built
    (an edge look at a bend) is dropped, since from inside a collider a ray sees
    through it. Each piece gets a grid of sample points through its LOD0 render box
    (shrunk to 0.8, in its own space); it is seen from a look when a ray (Environment
    layer) to one of them hits nothing, the piece's own collider, or a point on its
    render box within 5cm (the merged box that stands in for a tile's, a foundation's
    or a walkway's own colliders); a piece further than `cullRange` (80m) from a look
    isn't tried from it. Each piece is tried from its nearest eight looks one ray at
    a time, stopping at the first that sees it, and whatever is still unseen is tried
    from every other look in range as parallel `RaycastCommand` jobs. Unseen pieces
    are removed (Undo), emptied groups with them (a group holding merged colliders
    stays), then railings left on a removed ridge; one log line (`Visibility cull: n
    of m piece(s) removed ... (streets a of b, foliage, facades, lots, roofs; looks;
    time)`). Three-levels loses 824 of 10247 pieces in 19s, with no hole seen from a
    street.
  - **Roof edge colliders** (`RoofEdgeColliders.cs`, straight after the cull): **a roof
    piece facing the void beyond the level loses its colliders** (every collider on the
    instance removed, an override; its renderers stay), so nothing invisible is climbed
    or bumped out there. Each piece faces out of its building (`Roofs/.../Building <i>`):
    a slope panel (its forward at least 0.3 up) along its level forward, anything else
    (gables, hip pieces) away from the building's middle. A `Physics.CheckBox`
    (Environment, triggers ignored) the piece's width, from `roofEdgeGap` (1.5m) past
    the building's furthest point that way out to `roofEdgeReach` (20m) further, from
    the piece's top down 100m: empty means the void. One log line (`Roof edge: ...`);
    the kept panels almost all face a street.
  - **Roof colliders** (`MergedColliders.RunRoofs`, after the roof edge colliders):
    every slope panel (categories `roof-middle`, `roof-end`, `roof-end-half`,
    `roof-ridge-end`, `roof-ridge-end-half`) loses its box colliders, at any tilt,
    merged in their own tilted frames as the foundations' are within
    `roofColliderTolerance` (0.1m) into boxes (`Roof <n>`) under `Generated/Roof
    Colliders`, fallback Tile; one log line (`Roof colliders: ...`). The two rows'
    collider tops differ by 7cm and undersides by 2cm, so a slope's two rows merge
    where their ends line up (gable ends), else one box per row. Three-levels: 1215
    colliders on 1343 pieces become 527 boxes. The hip strips (`TL_Roof_PeakedBase`)
    have no collision: the pack prefab's MeshCollider has no mesh.
  - **Building colours** (`FacadeBuilder.ColourBuildings`, in `FacadeRoofs.cs`, last in
    `FacadeBuilder.Build` after the lamps; `ColourVariants.cs`): **one colour per set
    of corner-joined buildings** (`CornerLeaders`, the union-find `MatchCornerStoreys`
    uses), applied to every member's facade group and roof group. A colour is a
    **material variant by name**: every material under `Assets/Art/Materials` and
    `Assets/Prefabs` is listed by file name, and `<material>_<suffix>` (e.g.
    `Medieval_Roofs_4k_brown`) is the `<suffix>` variant of `<material>`; a set's
    choices are the suffixes any of its renderers' materials have, plus `default`
    (each material's same-named unsuffixed copy under those roots - the maintainer's
    plain, editable copy - if there is one, else the pack material). **The walls and
    the roofs are coloured apart** (`Colour()`): two draws per
    `Roofs/<owner>/Building <i>` (always made, so later draws don't shift when
    materials are added), walls then roof, the first applied to the set's facade
    groups from their own materials' suffixes, the second to its roof groups from
    theirs; `ColourVariants.Apply` swaps each renderer's materials (every
    LOD) for the suffix's variant where one exists (an instance override), noting
    materials that have variants but not this suffix. Log line `Building colours:
    walls brown x36, default x17; roofs brown x16, default x10, grey x9, orange x18[;
    n building(s) with no variant][; no variant for some colours: ...]`
    (`ColourCounts()`). The maintainer's variants so far, with the unsuffixed copies,
    in `Assets/Art/Materials/Environment/{Roofs,Walls,Extensions}`: walls `brown`
    (`Medieval_PlasterWalls_A_4k`, `_B_4k`, `Medieval_WallsTall_A_4k`); roofs
    `brown`, `grey` (slate) and `orange` (terracotta) of `Medieval_Roofs_4k`,
    `Medieval_RoofPeaked_4k` and the dormer's `Medieval_Extension_Small_4k`, so a
    dormer matches its roof (the grey and orange albedos are the brown ones with only
    the tile sheets recoloured, made outside the editor; a hand-painted `.tif` of the
    same name replaces one). Gable walls keep their material until a variant is made.
- **Street clearance** (`StreetClearance`, one per generation, held by
  `FacadeBuilder`): **no wall may cut across a street.** Each street segment is a
  convex shape: its rectangle at the rolled width, **mitred where it meets other
  segments at a point** (`Mitre`: its own street's next segment at a bend, other
  streets at a junction) - on each side, cut along the line from the point through
  the corner where its edge meets the edge of the nearest segment leaving the point
  on that side, so the segments fit together like mitred joints, filling a bend's
  and a junction's corners, and no square end pokes out past a street leaving at a
  sharp angle onto ground the facades between them need. **Each segment has two
  shapes, differing only on the outside of a corner**, where the edges meet past
  the point: `Corners` is cut through the point there, and `Filled` lengthens the
  end to where the edges meet before cutting, so the outside corner is street too.
  A dead end, and a side where the next segment runs on nearly straight (within the
  branch angle; the edges would meet far off), stays square. Three things keep the
  buildings out of them: the runs are cut clear of every other street (`Clear`,
  Runs above, on `Corners` - the facades lining an outside corner are carried to it
  themselves, and on `Filled` a corner between two streets would cut them short of
  it and open a gap); side walls stop short of streets (`Reach`, on `Filled`); and
  **every piece the
  facades, corners, lots and dead ends place is checked as it is placed** (`Allows`,
  through `PiecePlacer.Clearance`, set for everything but the roofs and the streets'
  ground): its render box, as placed (scale included), against each street shape by
  separating axes (`Filled`). A piece standing `streetHeadroom` (2.2m) or more above a street
  (the street's surface beside it, steps included; for the street its facade faces,
  beside a flight of steps, the low level the facade stands on, so a jetty runs on over
  the steps) may overhang it - a jetty, an
  upper storey, a beam; lower down it may come into the street its facade faces
  (`Facing`; every street for a lot) as far as `columnDepthMax` plus
  `foundationFrontOffset` plus `overlapTolerance`, and into any other street only
  `foundationFrontOffset` plus `overlapTolerance`. A piece that would go further is
  not placed, and listed in one log line (`Street clearance: ...`): the run cut should
  leave nothing for it but the odd corner filler's foundation.
- **The street test** (`StreetTest`, run by `LevelGenerator` after building): **a
  layout is valid only if a ball can travel down every street without touching a
  wall.** A ball of `streetBallRadius` (0.5m), its centre `streetBallHeight` (1m)
  above the street's surface (up and down the flights as walked), moves along every
  street's middle line from its first point to its last, a radius short of a dead
  end's facade; anything under the generated root but `Streets` that it touches (by
  `Physics.OverlapSphereNonAlloc`, every half radius) blocks the street. One Console
  entry: valid, or an error naming each blocked street with up to five blockers and
  how far along.
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

## Report (stage 6)

- **`GeneratedReport`** runs after everything is placed (`LevelGenerator.Generate`,
  before the fingerprint is taken) and on demand for a `Generated` root already in the
  scene (**TeaLeaf > Level Gen > Report Generated**, on the selection or every root; it
  reads the spec the root records, `GeneratedRoot.SpecPath`). It finds what a walk
  through the level would find, lists it in the Console (a summary, then the worst
  forty finds; a warning when anything is found) and puts a **marker** at each on the
  `GeneratedRoot` (`GeneratedRoot.Marker`: kind, position, box size, note), which the
  root draws as wire boxes in the Scene view and in the headset (`IDebugDrawable`
  through `DebugLines`: red a free wall end, orange an open roof end, magenta a
  refused piece, yellow anything else; selected, a 3m line up from each). It only
  finds and lists; **nothing is filled in afterwards** - each find is fixed where the
  piece is placed.
- **Free wall ends** (`FreeEnds`): the subjects are the front walls (the storeys',
  a corner's fillers, a dead end's; side walls only cover - a side wall's front end
  is a real wall end at its step, its back end open by design, the buildings having no
  back walls). Each wall's two vertical end faces (across the longer horizontal side
  of its render box, taken from its LOD0 meshes in its own space, so a wall at any
  angle or scale is measured true) are sampled 2cm outside, at three depths by three
  heights. A sample is **bare** when no covering piece's box holds it (everything
  placed but doors, window inserts and roofs; grown 8cm) **and it can be seen from a
  street**: a straight line from it to one of up to six street tiles within 14m,
  spread at least 30° apart round it, at eye height (1.6m) over the tile, that no
  covering piece's box crosses - the wall's own box included, since an end face is
  only seen from in front of it. One can stand only on a tile whose middle lies in a
  street segment's rectangle at its resolved width and at least 0.75m from every
  wall's footprint (the ground tiles overrun the edges under and behind the facades).
  An end with over a third of its samples bare is a find, with its bare share and
  where it is seen from - **except an end that another wall meets** (any sample
  covered): a corner's post, or the strip of wall end the corner overrun leaves
  between two posts by design. Known finds that aren't holes: a corner post standing
  proud of its module (`PlasterWalls_B_PlanksCorner`), a wall-tall wall against a
  stone one, a corner post the other wall meets at an angle beyond the samples'
  reach (100% bare, but closed).
- **Problems met while building** (`GeneratedReport.Add`, from the builders): a piece
  refused for standing in a street (`StreetClearance.Allows`), a side wall with no
  room before a street or one that could not be placed, a corner side that carries
  nothing because the lines meet behind its end, a module with no wall that fits, a
  style with no usable foundation.
- Not checked: overlaps and z-fighting, roof coverage, floating or sunken pieces.

## Facade dressing (being built)

- **`FacadeDressing.cs`** (part of `FacadeBuilder`, after the roofs): walkways, pipes
  (with valves and capped ends), vents (with steam) and lamp posts on the facades, from pieces of
  categories `pipe-straight`, `pipe-corner`, `pipe-tee`, `pipe-valve`, `pipe-end`,
  `pipe-vent` and `lamp-post` (`pipe-fitting` is catalogued, not placed); tunables
  under "Dressing" in `GeneratorSettings`. Each module records its wall piece per
  storey (`ModuleSpot.Walls`), which the dressing reads to keep clear of openings.
  Heights given to `CrossesOpening` are above the street at that point, as the
  storeys' bases are.
- **`WallProbe`** finds the drawn surface along a ray: the pieces near it are found
  by their colliders (mostly plain boxes), then the ray is tested against their LOD0
  meshes, triangle by triangle; the dressing's own pieces are looked through. The
  timbers of the plaster walls stand 5-13cm proud of the plaster, which the colliders
  don't show.
- **Pipes** (`BuildPipes`): **the thin pipe kit only** - the `_thin` variants in
  `Assets/Prefabs/Environment/Pipes` (`TL_Pipes_Small_A_thin`, root scale 0.5, the
  1.5m straight; `_Corner_Small_Sml_thin`, `_TJunction_Small_thin` and
  `_Capped_End_Small_thin` at 0.3; `_Valve_Small_thin` at 0.5: 0.33m across, half the
  pack's small pipe; their ends copied from the Small pieces, the same source
  prefabs). The Small and Large pieces are unverified, so there is one kit and
  `pipeLargeShare` does nothing; `FacadeDressing.SmallPipe` (0.67m) is a diameter
  compared with the kit's. Every building gets at least one run, up to
  `pipeRunsMax`, together at least `pipeMinRunShare` of its width; runs are vertical,
  horizontal or L-shaped, level or plumb, never over a door or window
  (`pipeOpeningMargin`), each end into a wall or the ground, following insets, outsets
  and jetties, never along the street floor; lengths by `FitWidths`; a level stretch
  ending in an elbow into the wall ends within `pipeEndSpread` of its module's width
  about the module's middle (`PipeEndInModule`), never at a joint between walls;
  `pipeWallGap` clear of the wall's face,
  each run past the first at `pipeExtraRunShare`. Ground and dead-end
  pipes are kept out of the street test's lane (`ClearOfLane`). **Where a run
  stands** (rules taken from the maintainer's hand-adjusted copy of a lot): a plumb
  run stands off the frontmost drawn surface along its height (`PlumbFace`: a
  `WallProbe` every 0.25m from 0.5m above its foot to the straight's top, at the axis
  and both sides, hits within `FaceSlack` 0.25m of the storey's wall front counted; a
  level run likewise, `LevelFace`), so it clears the posts and beams by 0.15-0.25m; it
  stands `pipePanelInset` from its module's street-left end, on the plaster past the
  end post, else over the post at the joint where that would cross an opening
  (`PlumbPlaces`); its top elbow keeps under the eaves (`Ceiling`: an upward probe,
  less the elbow's reach and `pipeWallGap`); every elbow into a wall whose leg falls
  short of the plaster gets a hidden straight behind it (`PlaceWallElbow`; not one of
  the run's counted straights, no caps); an L run may end in its own module at the
  storey's middle, top or base (`CornerRun`).
- **Pipe pieces are placed by their ends**, connection points named `a`, `b`, `c`...
  at the middle of each open flange's outer face, pointing out of the piece along the
  pipe, marked in Review Pieces. A straight (`pipe-straight`) runs from `a` to `b`
  and is stretched along that axis to its length; an elbow (`pipe-corner`) needs `a`
  and `b` at right angles, its bend where their legs' lines cross
  (`PipeEnds.Bend`), and is turned so its legs point the ways the run needs
  (`FacadeBuilder.Turn`). A piece without them is left out and logged. In the window
  a pipe piece shows a help line, a **Find pipe ends** button (`PipeEnds.Find`: the
  flanges where the LOD0 mesh meets a face of its render box, told by a round ring
  of vertices; right for the straights, elbows and junctions, a guess for capped
  ends, valves and vents) and what the generator makes of it (a straight's length,
  an elbow's legs, the bend drawn yellow in the Scene view). The maintainer has
  verified the pipe pieces' ends. A T-junction (`pipe-tee`) is placed by `a` and `b`
  along the pipe and `c`, its branch; a valve (`pipe-valve`, `TL_Pipes_Valve_Small_thin`)
  by `a`, pointing into the branch; a capped end (`pipe-end`) by `b`, the flange the
  pipe meets (its `a` is the cap's face); a vent (`pipe-vent`) by `b`, pointing into
  the wall.
- **Valves** (`valveShare`, `valveHeight`): a run is given a valve at `valveShare`.
  **A valve only ever sits on a T-junction's branch** (the end at right angles to the
  other two), **and faces straight out from the facade into the street**: the T
  stands in one of the run's straight stretches, its branch pointing out, the valve
  on it (scaled with the T's root, being made for the large T). On an upright stretch
  the T's branch is at `valveHeight` above the street, in the middle of a level one;
  it slides along the stretch until the pipe either side is none at all (flange to
  flange with the next piece) or at least 0.4m (`MinValveStraight`). Not where the
  valve would reach into the street's lane on the ground storey. The log gives the
  valves placed, the runs given one and those with room.
- **Capped ends only where a straight pipe piece goes into a wall** (`PierceCap`):
  each level straight stretch is probed along its length from both ends
  (`WallProbe`); where it meets a drawn surface, the capped end of its size goes on
  that surface round the pipe, **its flange (`b`) flat on the wall and its cap side
  facing out along the pipe**. A second hit from the far end within two caps' depth of
  the first (a thin beam) gets none. Elbow legs that turn into the wall never get a
  cap, and no run is laid out to end with a straight into a wall, so caps only come
  where a stretch crosses something (a timber, a jetty's beam).
- **Every run has at least two straight pieces**: each run is built in a group of its
  own, and one with fewer than two straight pieces placed is taken out whole (its
  valves, caps and taken space with it). The log counts the runs dropped.
- **Vents** (`BuildVents`, `ventShare`, `ventsMax`, `ventLargeShare`,
  `ventFlatTolerance`): independent of the pipe runs. Each building gets up to
  `ventsMax`, each at `ventShare`, tried at up to 40 random places on its facade: on
  one module's wall within one storey, `pipeOpeningMargin` clear of every door and
  window, out of the street's lane, apart from any pipe or vent (their colliders),
  and **on a smooth piece of wall**: the drawn surface over a grid of points 0.1m
  apart across the vent lies within `ventFlatTolerance` (3cm) in depth, so a vent
  never sits on a timber. It is seated on the frontmost point, facing the street.
  Smooth patches between the timbers are small, so most vents wanted find no place
  (the log gives placed of wanted; the large vent rarely fits). **Vent steam**
  (`ventSteamPrefab`, `ventSteamShare`): a vent that steams gets
  `Assets/Prefabs/Effects/Vent Steam` (a Shuriken particle system) as a child at its
  face, pointing out of the wall (`PlaceEffect`).
- **Not working yet:** runs on a dead-end facade stand about a metre off the wall; a
  corner filler's jetty support can reach into a street (refused there); a thin elbow
  on a dead-end facade can stand in the street test's lane (`ClearOfLane` doesn't
  count an elbow's reach); some buildings get no run ("none fitted").
- **Walkways** (`BuildWalkways`, `walkwayShare`): the jetty piece
  (`ShortBoardPlanks_Supports`) along a whole facade at the base of one upper storey,
  never a jettied one, one per building at most, its supports' backs at
  `SupportBack` in front of the storey below's back line (as a jetty's), under a
  `Walkway` group per building. Working. **Their deck colliders are merged**
  (`MergedColliders.RunWalkways`, with the foundations', before the street test): the
  flat deck boxes of every piece under a `Walkway` group become one box per straight
  run (`Walkway <n>` under `Generated/Walkway Colliders`, same tolerance), while the
  tilted brace box on each piece fails the upright test and stays on the instance.
- **Lamp posts** (`BuildLamps`, `lampSpacing`, `lampEdgeInset`, `lampSink`): at
  the street's side before plain ground-storey walls, never before a door or window,
  facing the street, each post lowered `lampSink` (0.2m) into the ground tiles so its
  base never floats. **They are gameplay lights:** the `TL_Lampost` variant
  (`Assets/Prefabs/Environment/Iron`) carries a
  **Mixed** point light (Indirect Multiplier 0, soft shadows, range 5m) with a
  `Core.LightSource` (range 5) and a `Core.Flame` (its `flameVisual` the `Flame
  Visual` quad drawn by the `TeaLeaf/Flame` shader, `Assets/Scripts/Core/CLAUDE.md`;
  the pack's `Fire_Effect` particles are turned off), and its lantern meshes cast no
  shadows (the light sits inside them);
  `PiecePlacer` leaves the light on. **No two lamps light the same spot, and none
  stand across a narrow street from each other:** a post is placed only if it stands
  at least `lampSpacing` (level distance; 20m in the settings asset) from every other
  post over every street and its light is clear of every other's (the lights' centres
  further apart than their ranges added) (`LampAllowed`); so the shadowmask's limit
  of four mixed lights at a spot is never neared by lamps. One log line (`Lamp posts:
  n (m left out within Xm of another post, or where its light would overlap
  another's)`). Two Lots gets 7 lamps, Three Levels 17-19 (least post distance 21.4m,
  least gap between lights 11.5m).
- The report treats `pipe-*` (vents included), `lamp-post`, `grass` and `nettle`
  pieces as not covering a wall end.

## Street foliage

- **`StreetFoliage.cs`**: grass clusters on the streets' ground and nettles at the
  foot of the foundations, from verified pieces of the categories `grass`
  (`Foliage_Grass_A` / `_B`; the grass cluster prefabs stay uncategorised) and
  `nettle` (`Foliage_Nettle_A` / `_B`); tunables under "Foliage" in
  `GeneratorSettings`. **The foliage draws from a `System.Random` of its own**
  (`RandomFor`: the seed x 31 + 7), so it changes nothing else in the build. Every
  piece gets a random yaw, a scale between `foliageScaleMin` and `Max`, and its pivot
  sunk `foliageSink` below the ground; placed under `Generated/Foliage/Grass -
  <street>` and `Nettles - <street>`.
- **Grass** (`StreetGround.LayArea`, as each grid tile is laid): at `grassCover` a
  cluster of `grassClusterMin`-`Max` tufts within `grassClusterRadius` of a random
  point on the tile, only where that point is in open street (`InOpenStreet`:
  `grassEdgeMargin` inside the street outline, outside every lot's footprint, off any
  flight and its landing); decking and landings get none. The margin passes about a
  third of the draws, so the cover laid is nearer 1.5% of tiles.
- **Nettles** (`BuildNettles`, run by `LevelGenerator` after the merged colliders,
  since their ground is found by rays): every placed foundation / foundation-corner
  piece gives a front (its render box's face at its top edge, per annotated front),
  assigned to the street it looks onto (`Faces`: beside it within half its width plus
  `columnDepthMax`, `foundationFrontOffset`, `lotGroundMargin` and `overlapTolerance`,
  facing its middle line, or across a dead end); per street
  `nettlesPerStreetMin`-`Max` spots drawn along its fronts by length, `nettleOut` in
  front of the face, `nettleSpacing` apart, `nettleDoorClear` from a doorway (a door
  wall's opening); the ground by a downward ray that ignores the foundation and the
  merged foundation boxes, accepted only up to 0.15m above the foundation's top less
  `foundationHeight`. One log line (`Nettles: n on m street(s), from k foundation
  front(s)`).

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
  `roof`, `detail`, `window` (a window insert, put into wall holes; suggested for a `detail` in the `Windows` folder), `jetty` (jetty supports under a jettied upper storey, any style: `Platforms_ShortBoardPlanks_Supports`), `door` (a plain door put into doorways, any style: `Door_RectangleShort` and `_RectangleTall`, to be reviewed), the roof kit's `roof-end`, `roof-end-half`, `roof-ridge-end`, `roof-ridge-end-half`, `roof-middle`, `roof-gable`, `roof-hip-base`, `roof-hip-middle`, `roof-hip-top`, `roof-chimney`, `roof-dormer` and `roof-railing` (Roofs, above; `roof` is the scanner's suggestion for the kit, not used), the dressing's `pipe-*` and `lamp-post` (Facade dressing, above), `grass` and `nettle` (Street foliage, above) (empty = not chosen); `fronts` (level axes: one for a wall, two for
  a corner, none for a floor or steps);
  the module's `width` (along the piece's right as seen from in front), `height` and
  `depth` (along its front; X and Z for a piece with no front), in metres;
  `connections` (name, position, way out; a point named `window*` set by hand on a blind window wall is where a window insert is placed on its face - Window inserts, above); `styles` (building style tags, stored
  normalised: trimmed, lower case, spaces as hyphens); `groundStoreyOnly` (a wall the generator places only on a building's ground storey, whatever its style allows, e.g. `WallTall_StoneWindowBase`; a toggle in the window), `showsOpening` (a plain `wall` that shows a window or door - the blind window walls below and the masonry door halves (`DoorOrnate_Half*`, `WallDoorArch_Left` / `_Rright`) - and so is never a side wall; a toggle in the window), `fillsStorey` (a plain wall that, on a storey whose style offers it, stands at every module but the spec's door; a toggle in the window), `onePerStorey` (a plain wall that never mixes on a storey with another so marked; a toggle in the window, "One Per Storey"), `lit` (a `window` insert whose panes glow as if lit from behind, placed at `litWindowShare`: the `TL_Window_*_Lit` variants; a toggle in the window, "Lit"), `excluded`, `verified`, `note`.
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
    meet where they share a point, so always at one level; **a segment whose two
    points differ in height is climbed by a flight of steps**, Steps below),
    `frontages`;
  - a **frontage** is one side of a street lined with facades: `side` (`left` /
    `right`, looking from the street's first point), `from` and `to` (point indices;
    it runs past bends and ends only where a street branches off on its side, or the
    street ends), `buildings` in order from `from`;
  - a frontage **building**: `widthModules` along the street, `storeys` (styles from
    the ground up: **the last repeats for every storey above the list**, and storeys
    past the count are dropped - the count isn't in the spec, see `StoreyPlan`),
    `doorModule` (from the left seen from the street, from 0; -1 none);
  - `lots` (whole buildings, inside loops): `id`, `origin` (middle of the footprint,
    at the ground storey's floor: its street level plus `groundStoreyHeight`, since
    the ground round it is laid at `origin.y` less that), `yaw` (the way the front faces), `widthModules`
    along the front and `depthModules` back from it, `storeys`, `doorModule`, and
    optionally `outline`: the footprint's corners (world x, z; three or more, in order
    round it, convex), the side from the first to the second being the front; given, it
    replaces `widthModules`, `depthModules` and `yaw` (`LotSpec.Corners`, `Bounds`). With
    neither an outline nor modules, the lot takes **the block its streets enclose**
    round `origin` (`LayoutSpec.ResolveLots`, run by the validator straight after the
    widths, so generation sees it too): the street edges seen from the origin
    (`BlockOutline`: 720 rays cast round it, each taking the nearest segment's near edge
    it crosses, so a street bending away or a side street leaving the far side of a
    block's street never counts), then the convex region on the origin's side of each
    of those edges (half-planes clipped from a large square), sides in line merged and
    any side under 1m (a sliver where a street's junction wedge clips a corner) dropped
    so its neighbours meet. So its faces sit on the inner street edges - one street
    width from the facades opposite, one face per segment (a straight run of segments
    is one face).
    Its front is the side facing nearest `yaw`. None if the origin is on a street or the
    streets don't close round it (an error). Such a lot skips the validator's overlap
    test with the streets.
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
- **A storey is built from one wall family only**, grouped by folder and name (the
  families' corner pieces are tagged but not used: the walls make the corners).
  **`StyleRules`** (`LevelGen/Editor`)
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
  - `wood-floor` (plank decking for wooden streets, walkways and bridges) -
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
  with a street branching off on its side part-way along it; a branch meeting the
  street it leaves more than `branchSquareTolerance` off square (`CheckBranchAngles`);
  a building or lot under
  one module, with no storeys or a door module off its front; a street or storey
  naming no style or one no piece carries; a street style without `ground` or `floor`
  pieces (a wooden street, walkway or bridge is decked with floors); a storey style
  without a `wall` that fits its module; a ground-storey-only style above the ground storey,
  or listed last when `maxStoreys` is more than the list (it would repeat);
  the ground storey's without `foundation` or
  `foundation-corner`, or without a `door-wall` that fits its module when there is a door; storeys whose
  modules differ from the ground storey's by more than `maxStoreyWidthMismatch`; a frontage's buildings more than 5cm longer than
  its stretch; a segment that rises or falls (more than `sizeTolerance`) on a decked
  street, by other than a whole number of flights of one of its style's `steps`
  pieces, or too short for those flights (at the piece's annotated ramp run) and
  their landing (`CheckRises`). **Warnings:** more than a module's length left at a frontage's end (the
  generator fills it, but another building may suit); a lot, when `maxStoreys` is more
  than 1 and `wood-floor` has no usable `floor` (facades have no floors); per style used,
  tagged pieces not usable yet, walls whose width doesn't fit its module (not used as
  a module; not warned for a plain wall showing no opening whose height fits, since it
  is a side wall) or whose height doesn't (not used), fitting meaning within
  `sizeTolerance` or `wallScaleLimit` (`BuildingStyle.Fits`), and tile-category pieces
  off its tile size (not laid); lots overlapping each other or a street segment by more than
  5cm (oriented rectangles, separating axis test).
- **A frontage's stretch** is measured roughly: the street's middle line between its
  two points, less how far short its edge stops of any street branching off on its
  side at either end (another street sharing the point, within 10cm, and leaving it
  at least about 17 degrees towards that side): `BranchCut`, about the stop the
  facades' runs make (where the edge, and the line the walls reach back to, cross
  the branch's near edge - half the branch's width at a right angle, much more at a
  sharp one), the walls' reach taken as the deepest usable wall of the frontage's
  styles (`WallDepth`). **Sharp corners** (`CheckSharpCorners`): every wedge between
  neighbouring streets leaving a point (a bend's inside included; within the branch
  angle of straight on is no corner) is counted, sharp under `sharpCornerAngle`; a
  warning when the spec is more than one corner off `sharpCornerShare`. The true length along the edge (longer outside a
  bend, shorter inside) is stage 5's; what was built is checked by the report (stage
  6, below). `ValidateFile` / `Validate` return the `Report` for the later stages.
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
  reviewed. One Console entry lists what was built. `Generated` holds `Streets`,
  `Facades`, `Lots`, `Roofs`, `Foliage`, `Foundation Colliders`, `Walkway
  Colliders`, `Wall Colliders` and `Roof Colliders`. **After `FacadeBuilder.Build`, in
  this order:** the merged colliders (`MergedColliders`: foundations, then walkways),
  the nettles (`StreetFoliage.BuildNettles`), the street test (Facades, The street
  test; its own entry, an error if the layout isn't valid - it checks the merged
  boxes), the visibility cull (`VisibilityCull`), the roof edge colliders
  (`RoofEdgeColliders`), the walls' and the roofs' merged colliders
  (`MergedColliders.RunWalls`, `RunRoofs`: after the cull and the roof edge, which
  remove pieces and colliders, so no box stands for one), the report
  (`GeneratedReport`, its markers on the root), then the fingerprint.
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
  (`PrefabUtility.InstantiatePrefab`) with any `Light` in it disabled on the instance
  (a light with a `LightSource` beside it, a gameplay light such as the lamp post's,
  is left on),
  scaled, then (while its `Clearance` is set) checked against the streets and
  destroyed again if it would stand in one (Street clearance, above).
  `PlaceAt` turns a piece about Y and puts a point of its own space (a connection
  point, a face's middle) on a world point; `Pick` is the shared random choice that
  never repeats the piece before it. It places a tile by its **walking surface**: the middle of the catalogue's walk box
  at `walkHeight` (the render box's top without one) goes where the caller asks, so
  tiles meet by their collider tops.
- **`StreetGround`** lays every street, in the spec's order, under `Generated/Streets`:
  - **Ground (a style with `ground` tiles - cobbles): one grid per area**, an area being
    the streets of that style joined at shared points. **The grid is never squared to
    the world's axes**: from the first street's first point, turned at random (always
    drawn) so its rows lie at least `groundGridMinAngle` off world X and Z. The outline
    is each segment's rectangle (at its own width) plus a disc (half the wider of the
    segments meeting there) at every bend and at an end shared with another street of
    the area, filling the wedges and width steps, **plus the ground round each lot**
    whose footprint's middle is nearest one of the area's segments: **a ring from its
    footprint** (`LotSpec.Corners`, the line its walls' fronts reach) **out to
    `lotGroundMargin`** (`LotGround`, `GroundLot`, `LotCoverage`: the footprint grown
    by the margin, by separating axes), **never under the building** - a tile with
    every corner more than `minTileCoverage` inside the footprint is not laid by the
    lot (interiors are made by hand). A tile is laid where it reaches at least
    `minTileCoverage` into the outline, so the turn leaves no gaps and no tile is
    doubled at bends or junctions; what stands past the street's edge is under the
    buildings lining it (and shows at a branch gap or a dead end). Each tile's walking
    surface is at a lot's street level within its ring's outer edge, else at the
    height of the nearest point on the area's middle lines - on a segment with a
    flight of steps, the level of the flight's side it is on.
  - **Steps** (`StreetSteps`: the choice and geometry, shared with the validator and
    the facades; `StreetGround.LayFlight` lays them, after the area's grid): **streets
    meet level at their points, and a segment whose points differ in height (by more
    than `sizeTolerance`) climbs by a flight of steps across the whole street**, the
    ground flat at each point's height either side of it. Only a street of `ground`
    tiles can change height. The flight is a whole number of flights of the street
    style's first usable `steps` piece that makes the rise within `sizeTolerance`
    (the stone `Steps Full Width`, 1.62m, tagged `cobbles`), stacked up the segment
    each from where the render box of the one below ends, scaled in height to make
    the rise exactly, as many side by side as round to the segment's width,
    overlapping by `groundOverlap` (no crack between them) and scaled along their
    width so together they fill it, each one's ramp foot at its level. The
    facades either side close the gap to the steps with stacked foundations (Facades,
    Beside a flight of steps). **At the top, a
    landing** of `LandingRows` rows of ground tiles in the segment's own frame (each
    row slid and each tile turned as on the grid) from the top step's edge, a tile's
    diagonal deep (two rows of the 3m tile), `landingDrop` below the street: the
    turned grid can't make the straight edge the top step needs. The flights and the
    landing are centred on the segment. A grid tile is left out (`OverFlight`) where
    its middle is on the high side of halfway up the flight and short of the
    landing's far edge less half a diagonal, within the width plus half a diagonal of
    the middle line, so no grid tile lies over the steps and the landing covers what
    they leave; a grid tile on the low side lies under the solid steps or the
    landing. Under `Ground - <street>/Steps - <street> <from>-<to>`.
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
  - **Floor colliders** (`FloorColliders`): **the tiles keep no colliders of their
    own** (every collider on a placed tile, the tile's disabled mesh collider
    included, is removed from the instance as it is laid - an override, the prefab
    untouched). **Each run of tiles side by side along a row whose collider tops are
    at the same height (1mm) and whose `SurfaceTag` matches gets one `BoxCollider`**
    in the row's own frame, exactly the extent of the run's tile box colliders (their
    top at the walking surface, 0.4m thick for `StoneFloor_Tile`), so no gap opens and
    no invisible floor reaches past the tiles. A tile skipped or refused, or a change
    of height (a lot's ring, a flight's two levels), ends a run. Grid rows, landing
    rows (running across the street, 2cm under the grid tiles they meet) and decking
    rows (a sloped segment's tiles, each at its own height, get a box each) all work
    so. The boxes (`Floor <n>`) go under a `Colliders` child of the area's or street's
    ground group, on the tiles' collider layer (`Environment`) with their static flags
    and a `SurfaceTag` (the tiles' copied - `TL_StoneFloor_Tile` carries one set to
    Stone - and Stone where a tile has none). The steps keep their `RampCollider`s. About one box per row: the four
    examples go from 166-688 tile colliders to 15-62 boxes per area.

## Known about the prefabs

Measured by the scanner (still to be confirmed by eye in stage 2):

- **Every wall-like piece faces +Z** (its flat back faces -Z); outer corners face +X
  and +Z, the inner corner `Foundations_CornerIn_01` -X and +Z: the pack is consistent.
  Against it: `Platforms_SideWall` has its flat side on +Z, `Platforms_SideWall_Half_02`
  runs along Z, `PortchStraight_Extension` measures +X and -Z.
- Floors and platforms each have one box collider (the stone floors and ground pieces
  a mesh collider) whose top is the walking surface.
- **The only cobble tile is `StoneFloor_Tile`** (3.04 x 3.04m, flat collider top: a
  3.04 x 0.4 x 3.04 box collider the variant adds, the pack's mesh collider beside it
  disabled).
  `StoneFloor_A`-`E` are irregular stone patches (0.7-1.7m across, uneven colliders):
  smaller than `fullSizeShare` of the tile, so never laid as tiles. The plank floors
  are about 3.0 x 3.0m (`Platforms_Planks`, `_Planks_Ground`), with half (3.0 x 1.9m),
  nailed (2.9 x 2.3m) and board (3.0 x 1.5-2.0m) sizes; which way their boards run is
  not yet known. `Platforms_Steps_Wide`,
  `Steps Full Width` and `Steps Wood Narrow` have a `RampCollider`; the other steps
  are box colliders only. **`Steps Full Width`** (seen in the editor) is a solid
  stone flight of six steps climbing along its -X, its ramp's foot 0.28m in front of
  its first step, rising 1.62m over a 2.98m render box, 2.7m wide, with a plain back
  face: the street steps of `cobbles`. The two wooden ones are mostly open support
  frames, not stairs a street can use; neither has a style.
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
  `modules x ModuleSpacing + WallOverlap`. The `PlasterWalls_B_MasonaryWall`,
  `_Cropped` and `_Half` annotations' `left` point sits 4cm in from their render
  box's end, so they overlap like the rest. **A wall's trimmed end is a closed post,
  its plain end hollow**: corners and side walls are built round that.
- **Wall pivots sit at one end of the base**: the back plane runs from x = 0 to x =
  -width, so seen from in front the pivot is at the wall's left end and the wall runs
  to the right. Doors, window inserts (`Window_*`) and the cellar wall have their
  pivot at the centre instead; window inserts and doors are centred in height too.
- **Blind window walls:** `PlasterWalls_A_WindowBase`, `_A_WindowBase_Half`,
  `_A_PlanksWindow`, `WallTall_WindowBase`, `WallTall_StoneWindowBase` and the
  `Masonary_CellarWindow*` walls frame a window that can't be seen through: no hole,
  so no `openings`, and the scan calls them `wall`; they (and `PlasterWalls_B_WindiwRoof`)
  are marked `showsOpening`; `WallTall_WindowBase` carries a hand-set `window` point
  at (-1.75, 1.95, 0.38) on which an insert is placed (Window inserts, above). Only
  `PlasterWalls_A_WindowBase_Hole` and `Masonary_WoodLarge_WallWindows` have real
  openings.
- Doors (static and gameplay) are detailed on both sides, so they come out `unsure`,
  correctly. Columns, beams, supports and foundations mostly come out `unsure` or
  `not-a-slab` and need their front set by eye.
- **Corner confidences are low by nature** (0.03-0.16): the formula rewards a flat
  back, and an L's back isn't one. The doubtful corners are `PortchStraight_Extension`,
  `Portch_BeamSmall` and `PlasterWalls_A_MidColumn` (a column pivoted at mid-height).
- **Pivots that aren't on the base** (93, flag `pivot-not-on-base`), by kind: roof
  tiles (`RoofTiles_*`) are flat panels modelled upright in their XY plane, tiles
  facing +Z, pivoted at the panel's centre (about 2.1m to either edge), tilted to the
  slope by the house that places them;
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
- **The roof kit** (measured in the editor): tile panels modelled upright, tiles facing
  +Z, pivoted at their centre - `RoofTiles_Middle` 4.13 x 4.16m, `_MiddleSide` 4.30 x
  4.16m and `_TopCorner` 4.52 x 4.70m (the ridge cap along its top), the last two with
  a verge at their +X end (the pack mirrors them for the other end), and `_Half`
  versions that are their +X halves (x -0.25 to 2.15). **`RoofTiles_Middle` (35 open
  edges) and both `_Half` panels (56-59) are not closed meshes** - one-sided where
  cut, so gaps show - and are excluded; `MiddleSide`, `TopCorner` and the
  `RoofWall_Corner` half gables are closed. The pack's houses tilt them to
  52-68°, two or three rows a slope, scaled 0.6-1.4 by hand, joins overlapped; there
  are no hip or valley pieces. `RoofWall_CornerA` / `B` are half gables (upright edge
  at x 0, sloping down to +X, about 2.96 x 4.4m; unverified), `RoofWall_MedA` / `B`
  and `LargeA` / `B` whole gables (verified; their wall face within 0.1m of the pivot
  plane, the beam ends 0.45-0.6m in front of it). The pack's roof layers (`Building_Layers`) come only for footprints of about
  7, 10 and 12m. `Roof_Peaked*` make a tower's spire: four corner stacks of
  `PeakedBase` (3.03 x 3.46m), `PeakedMiddle` and `PeakedTop` (a quarter turn from the
  other two), scaled 0.85 in height, each stack a hip strip about 3m wide at the eave,
  its slope about 60-75° (a bell flare; `PeakedBase`'s faces about 64°) - the generator's hip strips stack copies of `PeakedBase` alone; `RoofTiles_Supports` is a truss
  under a verge.
- The meshes are binary FBX, and many pieces have scaled or turned children, so sizes
  and pivots can only be measured in the editor.
- Duplicates to choose between: `TL_PlasterWalls_A_MidColumn 1`, and
  `Steps Wood Narrow` / `TL_Platforms_Steps_Narrow`
  (one source). The pack's misspellings (`Wallkway`, `Rright`, `WindiwRoof`) are kept.
