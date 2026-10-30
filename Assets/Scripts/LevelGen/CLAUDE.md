# Level generator (`Assets/Scripts/LevelGen/`)

Editor tool: builds a level's streets and buildings from `Assets/Prefabs` variants per a
layout spec (a brief turned into JSON). Real prefab instances under its own root; rerunnable
without touching hand-placed things. Set dressing only on its own buildings/streets: facade
dressing (pipes, vents, walkways, lamp posts = gameplay lights), roof chimneys/dormers/ridge
railings, chimney smoke, vent steam, street grass and nettles, moon shafts; also lays sound
rooms/portals and bakes the guards' NavMesh over its streets. Separate operations after a
generation: rooms behind hand-placed open windows (Open window interiors), then the building
proxies. No other lighting, dressing or gameplay.

**State:** stages 1-7 built; open window interiors built, not yet tried in a scene. Catalogue: 188 `Environment` prefabs (`Containers`,
`Interactive` not scanned). Untested: segment widths, storey counts, decking.

**Fitting rule, every placed piece** (`FitWidths`, `fillStretchShare`): anything covering a
length end to end (run modules, side walls, corner fillers, foundations, beams) lays whole
pieces; the leftover widens the last piece if < 30% of a piece, else adds one piece scaled
down to it. Never a sliver nor an overrun.

**Pack walls are hollow: a visible end face shows black.** Every wall end is covered (next
wall, side wall, filler) or hidden in/behind another wall; only a wall's trimmed (left) end,
a closed post, may show, as a corner post.

## Rules

- **Read-only towards assets**: nothing in `Assets/ASTROFISH_GAMES` / `Assets/Prefabs`
  modified, moved, renamed or saved; prefabs only loaded to measure and instantiate.
- **Real prefab instances only** (`PrefabUtility.InstantiatePrefab`, keeps variant link); no
  substitute geometry. Generated meshes only where derived: street ground, sheared walls,
  building proxies (merged from the placed pieces' own meshes).
- **Only verified pieces placed** (front, pivot, footprint, points confirmed by the maintainer,
  stage 2). Names never trusted for direction.
- **Repeatable:** spec holds the seed; all randomness via one `System.Random` from it, fixed
  order. Same spec + catalogue = same level.
- **Buildings from parts** (wall, corner, window, doorway, foundation, floor, roof), **never
  the pack's ready-made buildings/storeys**: `Building_Exteriors`, `Building_Layers` left out
  of the catalogue; an `assembly` prefab only if marked so by hand.
- **Lights in placed prefabs disabled on the instance**, except gameplay lights
  (`Core.LightSource` on it or a parent: lamp posts, kept to the shadowmask limit) and Baked
  lights (lit window spots, lamp ambience spots: free at runtime). Other level lighting by hand
  (`Assets/Scenes/CLAUDE.md`).
- **Gameplay doors never generated** (`Assets/Prefabs/Interactive`, outside the catalogue).
- **Every tunable lives in `GeneratorSettings`**, never a constant. (Scanner detection
  thresholds stay constants in `WallOrientation` / `PieceShape`: changing them = rescan.)
- **After every generator change Claude runs it in the open Editor** (Unity CLI): wait for
  `recompile_status`; call `LevelGen.LevelGenerator.Generate(<spec path>)` by reflection via
  `unity command eval` in the scene the maintainer has named (none named: ask); save it; read the Console. Street test must pass; street
  clearance should refuse only stray corner foundations; **report lists no free wall end** (any
  listed is looked at - top-down or street-level render from a temporary camera, `Roofs`
  hidden for top-down - before calling it done). Check the scene (gaps along street edges,
  ground meeting steps). Rolled segment widths: `LayoutSpecValidator.ValidateFile` the same
  way (spec widths are only the base). One log line per finishing pass: `Ground paint`, `Foundation
  colliders`, `Walkway colliders`, `Wall colliders`, `Roof colliders`, `Nettles`, `Gaps`,
  `Visibility cull`, `Roof edge`, `Roof features`, `Roof railings`, `Pipes`, `Vents`, `Lamp
  posts`, `Building colours`, `Open windows`, `Backing`, `Moon shafts`, `Sound rooms`,
  `NavMesh`, `Sheared meshes`. Then Build Open Window Interiors (if the sector has open windows) and Rebuild
  Building Proxies, read their Console entries, save.
- **Looking at the scene** (temporary camera rendering to PNG via `eval_file`): turn the camera's
  occlusion culling off (the scene's bake predates a regeneration) and render each view twice,
  keeping the second (the first frame after a change can come out wrong).
- **Stale assembly:** an eval straight after an edit can run the old assembly, and a refresh
  can race several edits and compile only the first: `unity command recompile`, poll
  `recompile_status`, confirm the new code is in (new field/type via reflection, assembly write
  time after the last edit) before trusting a generation.
- Editor code in `LevelGen/Editor`, namespace `LevelGen`; only runtime components are
  `GeneratedRoot` (`LevelGen/GeneratedRoot.cs`) and `OpenWindowInterior` (data only).

## Brief and spec

- Claude writes the spec (`Assets/Data/LevelGen/Layouts/*.json`) in conversation, not via an
  API call from the editor: seed, streets, frontages of facades, lots. Validated against the
  catalogue before building.
- **The maintainer edits no data file by hand**; piece facts and styles are set in Review
  Pieces. **A spec names styles, never pieces** (style = tag on annotations; several per
  piece), so a newly verified tagged piece joins its style automatically.
- **Streets lined without gaps** (gap only at a branch). **Most buildings are facades**
  (street-visible sides only); **a lot inside a loop of streets is built whole**. Generator
  fills the wedge outside a bend and a frontage's leftover. **A street end meeting no street is
  a dead end, closed by a facade across it.** Irregular facade lengths wanted; streets not
  fitted to modules. **Rooftop-reachable levels are built by hand.**
- **No world grid:** streets are polylines at any angle (curve = short segments); lots have
  own origin and yaw; pieces grid only within one lot/segment frame. Where frames meet at an
  angle the generator fills; what it can't, and overlaps, go in the report.

## Data (`Assets/Data/LevelGen/`)

- `catalogue.json` — scanner output per variant (path, GUID, folder, LOD0 render and
  collider bounds relative to root, pivot offset, components, nested prefabs, lights, LODs).
  Never hand-edited.
- `annotations.json` — maintainer's per-prefab facts (survive rescans), written by Review
  Pieces. No entry = not looked at; only `verified`, not `excluded` are placed.
- `Sheared/<sector>/*.asset` — sheared wall meshes for leaning facades and roof panels ending
  on a hip (`ShearedMeshes`), a folder per sector. Reused within the sector; each generation ends by deleting the ones in
  its sector's folder the new level doesn't use (`ClearUnused`); other sectors' folders
  untouched. Deletable (remade at the next generation).
- `Ground/*.asset` — street ground meshes (`GroundMesh`), one per region chunk, `<sector> -
  <area> <region> <x>_<z>`, the area's old ones deleted and rewritten each generation.
  Deletable; ground shows nothing until regenerated.
- `Proxies/*.asset` — buildings' merged meshes (`BuildingProxies`), `<sector> - <owner> Building
  <i>` (proxy), with the sector's atlases and atlas material (`<sector> - Proxy Atlas*`,
  `ProxyAtlas`); the sector's old ones deleted and rewritten each proxy build. The proxies' `ProxyLightmapParameters.asset` lives here too and is never deleted by a build (not a sector's file). **Git-ignored** (tens of MB, rewritten every run): a fresh clone
  or a deleted folder draws buildings beyond full-detail range as nothing until the proxies
  are rebuilt.
- `GeneratorSettings.asset` — tunables (Settings).
- `Layouts/*.json` — specs, one per area:
  - `example-street` (sector `Example Street`, origin): high street bending at a T with a
    lane; facades all sides (high street left one frontage past the bend, right two, split at
    the lane); no lots; cobbles, plaster, wall-tall, plaster-masonry, masonry-woodlarge.
  - `example-block` (`Example Block`, 100m E): hexagonal ring of two streets (turned 15°,
    three segments each, end to end), facades outside, lot `merchant-house` with no outline or
    modules (takes the block), facing S.
  - `example-quarter` (`Example Quarter`, 200m E, frame 22°): gently bending main street, two
    lanes leaving it square, a back street (two segments in line) closing a five-sided block
    round lot `guild-hall` (block from streets, facing main street); facades on every outer
    side; two alleys leaving main and back streets' middles square, away from the lot. Has
    street corners, an inside L (back street meets west lane), a lane running past a
    junction, dead ends.
  - `example-two-lots` (`Example Two Lots`, frame 30°, 110m N): ring round `counting-house` at
    0 and ring round `warden-hall` 1.62m up (each ring two streets end to end), joined by
    exactly two links (low ring's E side mid-edge to high ring's W side mid-edge, square both
    ends, one flight on their second segment); lane and alley off the links' bend points, an
    alley off the low ring's W, a spur off the high ring's E, all dead ends; lot origins at
    street level + `groundStoreyHeight`.
  - `example-three-levels` (`Example Three Levels`, frame 37°, centred (40, 300)): lots in
    rings `counting-house` 0, `guild-house` 4.86m, `warden-hall` 9.72m (three flights per
    level), each ring chained to both others by square-cornered streets climbing one segment
    per level change; seven dead ends, one the one sharp corner; five sloping lanes (west lane
    -0.8m off low ring; tanners' lane +0.9m off middle; hairpin +0.7m on its first segment,
    staying up round its bend; west close -0.8m and north alley +0.6m off upper). **Street test
    fails**: the facade beside a three-flight climb stands at the low level, its upper storey
    and roof in the raised street (maintainer to choose: modules beside a flight stand at the
    flight's surface there, or specs split climbs into one-flight segments). Generated with
    `facadeLean` 5.
  - `example-slope` (`Example Slope`, frame 25°, 150m W 60m S): hill street (level; +1.5m over
    30m, 1 in 20; level after a 20° bend), lane off its first bend square, -1m over 25m;
    two- and three-module facades all sides; three dead ends.
  - `three-streets` (sector `Three Streets`, on origin, main streets 20° and 26° off N; a
    level, no lots): W and E streets roughly N-S, two segments each split where the middle
    street leaves, all sloped (W +1.8 then +1.4m going N, E +1.5 then +1.8m); middle street
    leaves W at 80°, bends 6°, meets E at 80°: +4.86m by three one-flight 10m segments, +1.6m
    slope over 34m, +4.86m by three more. Two-/three-module facades all sides, four dead ends.
  - **Examples' sectors stand apart (all six fit one scene); place new examples clear.
    `three-streets` is a level over `example-street`'s spot - never one scene together.**
  - **Specs Claude writes:** streets irregular, at least ~15° off world axes. Every branch
    within `branchSquareTolerance` of square (validator error), so sharp corners only from
    bends, `sharpCornerShare` of them where possible (validator warns beyond one corner off).
    A side street off a block's street mid-run shares a point added there (facade split at
    it), the segments either side kept in line (two near-parallel edges of different widths
    cut a short side into the lot's block). End-to-end streets meet square or within
    `branchAngle` of straight (more counts as a branch for sizing). Never a 30-45° bend inside a
    street nor a near-straight end-to-end join (bare wall ends both sides). `width` 0 (takes
    `streetWidth`); only alleys narrower (4m in examples). Frontages sized to the settings'
    widths. Back-to-back street edges further apart than NavMesh `EdgeReach`.

## Settings (`GeneratorSettings`)

ScriptableObject in the Inspector; **TeaLeaf > Level Gen > Generator Settings** selects it
(made with defaults if missing); generation and validation read only this asset. Defaults
unless "asset". Meanings are in the sections that use them.

- **Walls:** `wallOverlap` 4cm; `fullSizeShare` 0.75 (share of style's biggest wall/tile =
  full size); `sizeTolerance` 5cm (off module/tile and used as is); `wallScaleLimit` 0.12
  (share of module off and still scaled to fit; side walls vs storey height).
- **Streets:** `streetWidth` 5m; `streetWidthVariance` ±1m/segment; `minStreetWidth` 2m;
  `edgeWallProbe` 1.5m (walls looked for this far from an edge: sound rooms).
- **Storeys:** `minStoreys` 1, `maxStoreys` 3; `maxBuildingHeight` 13m (storeys stop where the
  next top passes this above the street - tames wall-tall's 4.5m; `StoreyLevels`);
  `storeyBias` -1..1, 0; `lotNeighbourDistance` 15m; `lotStoreyBias` -2..2, 0.
- **Facades:** `groundStoreyHeight` 0 (-1..1.2; ground walls'/doors' base above street);
  `wallThickness` 0.36 (share of their own depth every wall, beam and gable is scaled to,
  open window interiors' walls too); `foundationScale` 0.5 (foundations' height and depth);
  `foundationHeight` 0.3m; `foundationFrontOffset` 0.1m; `foundationBury` 0.1m;
  `foundationColliderTolerance` 0.1m (walkways too); `wallColliderTolerance` 0.15m
  (window-base sills stand ~5cm proud at 0.36 thickness); `roofColliderTolerance` 0.1m;
  `fillStretchShare` 0.3; `groundDoorShare` 0.1; `groundWindowShare` 0.3; `upperWindowShare`
  0.4; `storeyBeamOverlap` 4cm; `maxStoreyWidthMismatch` 0.3m.
- **Corners:** `cornerAngleTolerance` 20° (counts as square: walls meet as right angle, roofs
  along hip/valley); `cornerMaxReach` 8m; `cornerOverrun` 0.5.
- **Jetties:** `jettyShare` 0.3; `jettyInset` 5cm; `jettyStoreyDrop` 10cm. **Lean:**
  `facadeLean` 0-5°, 0.
- **Columns:** `columnWidthMin`/`Max` 1-2 modules; `columnShare` 0.15; `columnMinRunModules`
  6; `columnDepthMin`/`Max` 0.7-1.41m (`Max` also the NavMesh band's depth); `columnOutShare` 0.5; `columnHeightShare` 0.3;
  `columnStoreyVariation` 1.
- **Doors:** `doorInsertMargin` 5cm; `doorSideMargin` 0.13m; `doorInsertDepth` 0. **Window
  inserts:** `windowInsertShare` 1; `windowInsertMaxMargin` 0.45m; `windowInsertDepth` 0;
  `litWindowSpacing` 10m.
- **Side walls:** `sideWallMultiplier` 0-5, 1; `sideWallTuck` 0.12m (for walls of the pack's depth, x `wallThickness`; above ~0.16 a lower neighbour's gable on the join shows through). **Gaps:** `gapBridgeMax`
  3m; `gapStoreyTolerance` 0.3m. **Backing:** `backingMaterial` (asset:
  `Assets/Art/Materials/Environment/Backing.mat`; none = no boxes); `backingDepth` 3m;
  `backingBelow` 1m; `backingClearance` 2cm.
- **Dressing:** `pipeBuildingShare` 0.4; `pipeRunsMax` 2; `pipeExtraRunShare` 0.35;
  `pipeMinRunShare` 0.5; `pipeLargeShare` 0.1 (inert: only the thin kit is verified);
  `pipePanelInset` 0.6m; `pipeWallGap` 5cm; `pipeOpeningMargin` 0.25m; `pipeEndSpread` 0.5;
  `valveShare` 0.3; `valveHeight` 1.3m; `ventShare` 0.4; `ventsMax` 2; `ventLargeShare` 0.3;
  `ventFlatTolerance` 3cm; `walkwayShare` 0.3; `placeLamps` on; `lampSpacing` 14m (**asset 20m**);
  `lampEdgeInset` 0.6m; `lampSink` 0.2m.
- **Smoke/steam:** `chimneySmokePrefab`, `chimneySmokeShare` 0.5; `ventSteamPrefab`,
  `ventSteamShare` 0.5.
- **Foliage:** `grassCover` 0.05 (**asset 0: no grass**); `grassPatch` 3m;
  `grassClusterMin`/`Max` 2-4; `grassClusterRadius` 0.5m; `grassEdgeMargin` 1m;
  `nettlesPerStreetMin`/`Max` 2-4; `nettleOut` 0.2m; `nettleSpacing` 2m; `nettleDoorClear`
  0.6m; `foliageScaleMin`/`Max` 0.8-1.2; `foliageSink` 3cm.
- **Layout checks:** `overlapTolerance` 5cm; `minSegmentLength` 0.1m; `junctionDistance`
  0.1m; `branchAngle` 17.5°; `branchSquareTolerance` 20° (a street ending at a junction where
  at least two other ways leave - a street through it, or more streets - meets the street it
  branches off, the line through the two most nearly opposite ways, within this of square;
  two streets end to end are a bend, not a branch).
- **Street clearance:** `streetHeadroom` 2.2m; `streetBallRadius` 0.5m; `streetBallHeight`
  1m. **Sharp corners (specs):** `sharpCornerAngle` 60°; `sharpCornerShare` 0.25 (generator
  clears any angle).
- **Ground:** `groundCell` 0.75m; `groundEdgeMargin` 1.5m; `groundGridMinAngle` 15°;
  `lotGroundMargin` 1m; `groundThickness` 0.25m; `groundOverlap` 3cm; `groundStepsOverlap`
  0.3m. **Paint:** `groundDirtBorder` 0.75m; `groundPaintWobble` 0.15m; `groundPaintScale` 4m;
  `groundPaintFade` 0.68m; `groundPuddleCover` 0.2; `groundPuddleScale` 5m. **Steps/slopes:**
  `streetSlopeMaxGrade` 0.083 (1 in 12); `slopeDropMax` 0.6m.
- **Roofs:** `roofPitch` 60°; `roofRows` 2; `roofEaveOverhang` 0.4m; `roofEaveDrop` 1m;
  `roofVergeOverhang` 0.2m; `roofRowOverlap` 0.25m; `roofPanelOverlap` 0.45m; `roofGableSink`
  5cm; `roofHipOut` 0.75m; `roofHipRaise` 0.2m; `roofHipStep` 2.5m; `roofHipLean` 10°;
  `roofHipAngleMin`/`Max` 50-135°; `roofFeatureMinPanels` 2; `roofFeatureSpread` 0.5;
  `roofDormerUp` 2m; `roofDormerSink` 0.1m; `roofChimneyUp`/`UpMax` 4-8m; `roofChimneySink`
  0.15m; `roofChimneyRise`/`RiseMax` 0.8-2.5m; `roofRailingShare` 0.5; `roofRailingSink` 0.15m;
  `roofRailingLean` 12°; `roofRailingInset` 0.4m; `roofRailingGap` 1m.
- **Visibility cull:** `cullEyeHeight` 1.6m; `cullHeight` 9m; `cullStep` 2m; `cullRange` 80m;
  `roofEdgeGap` 1.5m; `roofEdgeReach` 20m.
- **Building proxies:** `buildingProxies` on; `lowerDetailShare` 0.2 (building's share of
  view height below which the proxy draws, ~25m for a 12m building); `proxyCullShare` 0.04
  (below which nothing draws, ~120m; kept below `lowerDetailShare`); `proxyLightmapScale` 0.6
  (Scale In Lightmap of the proxy: ~18 texels per lightmap chart, what a clean bake needs);
  `proxyBakePushoff` 0.03m (pushoff of the proxies' Lightmap Parameters); `proxyAtlasSize`
  4096, `proxyAtlasTile` 512, `proxyAtlasPadding` 16px (proxy atlas).
- **Open window interiors:** `openWindowFloorTile` (asset: `TL_Platforms_Planks_Ground`);
  `openWindowTileOverlap` 3cm (floor and ceiling tiles, both ways); `openWindowFloorOffset` 0
  (floor surface above the window's base); `openWindowCeilingOffset` 0 (ceiling underside
  above the walls' top); walls thinned by `wallThickness` (Facades);
  `openWindowPortalSize` 1.2 x 1.3m; light `openWindowLightColour` FFD88A,
  `openWindowLightIntensity` 1, `openWindowLightIndirect` 2, `openWindowLightBelowCeiling`
  0.3m, `openWindowLightRangeShare` 1.674.
- **Moon shafts:** `moonShaftStreetMaxWidth` 4.5m (**asset 6m**: every 5m street); `moonShaftSampleSpacing`
  1m; `moonShaftInset` 0.3m; `moonShaftTopClearance` 1m.
- **Sound rooms:** `soundAlleyMaxWidth` 4.5m; `soundStreetMuffle` 0.15; `soundSkyMuffle` 0.3;
  `soundRoomMinHeight` 3m; `soundStreetAmbience` (**asset `AmbienceStreets`**, `ambience_loop.ogg`).
- **Decking:** `deckingOverlap` 3cm; `deckingQuarterTurn` off; `deckingSlideMin`/`Max` 0-0.

## Stages

1. **Catalogue scanner** -> `catalogue.json`.
2. **Annotations/verification** (Review Pieces) -> `annotations.json`.
3. **Layout spec**: format, validation.
4. **Streets and ground**: ground meshes (one per level and per slope, painted), plank decking.
5. **Buildings**, under `Environment/Sectors/<Sector>/Geometry/Generated` (`GeneratedRoot`,
   replaced whole on regeneration; warns if hand-edited). Hierarchy **streets > buildings >
   columns > storeys**. Foundations, then per storey wall modules (plain/window/door modules
   share a width, interchangeable), corners made by the walls themselves (no corner pieces),
   roofs from the roof kit; no floors; interiors only behind open windows (Open window
   interiors, a separate operation); fronts face the street. Window walls
   on any module of any storey. **Where several pieces fit, random, never the module before's
   piece** unless the only fit. **5a** straight runs; **5b** bends, ends, street corners, side
   returns; **5c** side walls by taller columns; **5d** lots; **5e** roofs.
6. **Report**: free wall ends seen from a street, pieces refused for a street, side walls/walls
   left out; Console + markers.
7. **Finishing passes**: order in Generation.

## Facades (5a)

- **`FacadeBuilder.Build`**, after the streets: frontages in spec order under
  `Generated/Facades/<street> <side> <from>-<to>/Building <i>/` (`Foundations`, `Storey 0`,
  ...). Order: lay out every frontage (`LayFrontage`: modules, end extension, columns), then
  the lots (`LayLot`), find corners (`FindJoints`; match/trim/widen corner ends), build
  frontages (lots included), corners (`BuildJoint`), dead ends, roofs (features, railings),
  dressing (walkways, pipes, vents), lamps, colours.
- **Runs:** one per segment along its edge (middle line out by half the width). **At a bend
  both runs end at one point** (`BendEnd`): street's inside of the bend (facade's outer
  corner) where the edges cross, short of the point, a corner joint carrying on; street's
  outside (`InsideBend`) where the walls' back lines cross, past the point (walls overlap
  behind fronts, corner closes itself). If that point is further than `cornerMaxReach` from
  the point's foot, end on the bisector (`Bisector`).
- **Each run cut to its longest stretch clear of other streets** (`StreetClearance.Clear`):
  band from the foundations' front (`foundationFrontOffset` into its street) back to the
  deeper of walls' setback and foundation depth (`FoundationBack`), against every segment's
  rectangle but its own and neighbours', kept clear by `Overhang` (trim past back plane,
  foundation excess width) + `overlapTolerance`. Run with no clear stretch: left out;
  frontage with none: not built (logged).
- **Laying:** modules from the first point at the ground storey's `ModuleSpacing`; one passing
  the run's end (beyond `sizeTolerance`) starts the next run after the leftover is closed
  (`FillRunEnd`: under `fillStretchShare` widen the last module - `ModuleSpot.Width`, read via
  `Spacing()` - else add a narrower plain wall). One passing the last run's end is dropped
  with the rest (logged; validator reports too long). **After the buildings**
  (`ExtendLastBuilding`): the last building continues with whole modules, rest by the rule;
  ending at a dead end (`StoreyPlan.DeadEnds`) or inside L adds one module overrunning behind
  that facade, so it always meets it; starting at one gets the same before its first.
- Seen from the street a left frontage runs L to R, a right one R to L, so `doorModule`
  (counted from the left over the spec's `widthModules`; added modules never move it) is
  mirrored on the right.
- **Depth:** walls line up by back planes on a back line set back from the edge by the
  deepest usable wall's reach in front of its `left` point (render front less back plane), so
  that wall's trim just reaches the edge.
- **Beside a flight** (`Run.Stepped`, `StepAt`; `StepFlights`): **first module wholly past
  the flight's top on the high level, all before on the low** (never a module over steps);
  **where no module is wholly past it (the run ends just beyond the top), the module across the
  top stands high**, its foundations stacked down to the steps, so the run meets the street
  beyond on its level. **Columns always split there** (`SplitColumns`); side walls between
  compare tops above each one's own street level, and **depths against the other column's
  storeys standing beside each storey at its real height** (`FillSide`, `OtherDepthBeside`: the
  lower column's jettied first floor beside the higher one's unjettied ground storey is a step,
  closed). **A low module with the street rising in front**
  (`RisesInFront`: `Flight`, `FlightOffset`, `StreetSteps.Flight.SurfaceAt`) **stacks extra
  foundations on its own** (`StackBesideSteps`; random, no repeat; top `foundationHeight`
  above the highest street in front; each overlapping the one above by `wallOverlap`): a stone
  cheek. Jetty supports continue beside the steps.
- **Beside a slope** (`Run.Slope`, `Run.Levels`; `LevelBuildings`): surface climbs evenly
  (`Surface()`: flight's/slope's `SurfaceAt`, else level), but **each building stands level at
  the highest the street comes under any of its modules on that run** (never above a wall
  base or burying a door; buildings step down a hill; one spanning a bend has a level per
  run). Every street run holds one `RunLevel` per building (beside steps at its side's height,
  one per side for a building across `StepAt`); `Height()` (storeys, beams, jetties, side
  walls, columns, corners, roofs, dressing) reads it; lamp posts use `Surface()`.
  **Buildings either side of every joint take the higher level** (`EqualCornerLevels`, right
  after `EqualCornerHeights`, repeated until stable), beside steps too (only the corner
  building rises, so its walls and roof meet the other side's and the roofs can hip); lots,
  dead ends keep their own (a dead end across a sloping end stands at the end point's height). **Street below a
  module's level: foundations stacked down** (`StackFoundations`, also stacks beside steps;
  random, no repeat, each top overlapping the one above by `wallOverlap`) until the lowest's
  bottom is `foundationBury` below the lowest street in front (`LowestInFront`), centred and
  scaled to the module (height and depth `foundationScale`, courses measured scaled). A
  foundation is ~0.55m tall at 0.5 standing 0.3m up, so an extra course from ~0.15m drop; validator warns above `slopeDropMax` (specs keep slope buildings to 2-3 modules).
  Doors at a building's low end stand up to that drop above the street.
- **Walls** (`BuildingStyle.Modules`; `Fits`: within `sizeTolerance`, or within
  `wallScaleLimit` and **scaled in width and height to fit exactly** - `FitScale`,
  `PiecePlacer.PlaceAt` with scale, in the piece's own axes (roots unrotated, unit scale),
  **depth scaled to `wallThickness`** about the pivot, every wall depth measured scaled with it
  - `Reach`, `FrontPlane`, `DepthMiddle`, side wall thickness, via `_wallThickness` set at the
  start of `Build` - so fronts stay on their lines and back lines come forward; beams thinned
  alike; doors and window inserts unscaled, at the thin wall's mid depth (their depth
  settings in metres); `Modules(category, storey)` drops `groundStoreyOnly` above ground).
  Placed by `left` point at the module's left end on the back line, front to street. **Two
  draws per storey and module, always** (sequence independent of styles): ground storey's spec
  door module, or door draw < `groundDoorShare`, -> `door-wall`; else window draw <
  `groundWindowShare` / `upperWindowShare` and style has one -> `window-wall`; else `wall`;
  `PiecePlacer.Pick`, no repeat along the storey.
  - **Overrun modules** (`ModuleSpot.Overrun`) **and modules narrower than the ground module**
    are always plain (no door/window cut off or squeezed).
  - **`fillsStorey`** plain wall (`PlasterWalls_B_MasonaryWall`): where a storey's plain walls
    hold one, it fills every module but the spec door; no other plain, random door or window
    walls there (draws still made).
  - **`onePerStorey`** walls never mix on a storey (`WallTall_WindowBase`,
    `_StoneWindowBase`): each building storey draws one (`StoreyLevel.OnePerStorey`), used for
    modules, corner fillers, side walls, jetty ends via `PlainWalls` (`SideWallChoices`);
    doors/window walls drawn as usual.
  - Storeys stack at their styles' `StoreySpacing` (or on a beam band); ground base
    `groundStoreyHeight` above street; all storeys at the ground module spacing, **each wall
    width-scaled to fill its module exactly** (width + overlap), so other module widths (up to
    `maxStoreyWidthMismatch`, e.g. plaster on cellar) and widened/narrowed ends join cleanly.
  - Ground style with no module: frontage stops (logged); module with no fitting wall: left,
    reported, storey continues; storey style with none ends the building.
- **Foundations:** one per module from the ground style's `foundation` pieces (random, no
  repeat), centred and width-scaled, height and depth scaled to `foundationScale` (side and
  corner-filler foundations too; depth used scaled by `FoundationBack`, `FoundationCarry`),
  top `foundationHeight` above street, front
  `foundationFrontOffset` past the edge (walls' front line); a plinth above the ground base.
  No usable foundation: reported, walls built without. Steps up to raised doors: not planned.
- **Merged colliders** (`MergedColliders`): pieces lose their upright box colliders, sorted
  into frames (axes matched either way within 0.05°, same layer and `SurfaceTag`) and merged
  pairwise until none can (one holds the other, or two axes match and the third meets) within
  a tolerance; boxes get the pieces' layer, static flags, `SurfaceTag` (copied, else
  fallback). `Run` takes fallback and `uprightOnly` (true foundations/walkways/walls: a
  tilted box stays on its piece; false roofs); `PiecesOfCategories`, `PiecesUnderGroups` pick
  pieces.
  - `RunFoundations`, right after `FacadeBuilder.Build`, before the street test (it checks the
    boxes): `foundation` / `foundation-corner`, `foundationColliderTolerance` (buried bottoms
    differ ~9cm at full size, ~5cm at `foundationScale` 0.5; less stops merging), `Foundation <n>` under `Generated/Foundation Colliders`,
    Stone. Examples: 65-298 pieces to 9-96 boxes.
  - `RunWalls`, after cull and roof edge (no box where a piece was removed): pieces under
    `Storey <n>` / `Corner` groups, `wallColliderTolerance`, `Wall <n>` under `Generated/Wall
    Colliders`, Stone (doors keep Wood). Three-levels: 1713 colliders/1848 pieces -> 1015
    boxes (rest mostly door surrounds, leaves, proud windows).
  - Walkways and roofs below.
- **Jetties:** each facade building draws `jettyShare` (always); jettied if it may have an
  upper storey and a usable `jetty` piece (`Platforms_ShortBoardPlanks_Supports`, 3.02 x
  1.58m, front -Z) fits its ground module (not woodlarge's 4.95m). `SetJetty`: supports'
  backs `jettyInset` behind the ground walls' shallowest main front surface (`FrontPlane`:
  plaster/stone between proud timbers; `Building.SupportBack`), fronts the deepest piece's
  depth out. **All upper storeys move forward** so the first upper storey's median wall reach
  meets the supports' front, plus column depth, one plane (`StoreyJetty`; walls, beams, side
  walls). **First upper storey cut into the deck**: deck top `storeyBeamOverlap` below ground
  walls' top; beam band (both styles beamed, else walls) `jettyStoreyDrop` into it
  (`StoreyLevels`; `JettyDeckTop` = `DeckTop` + drop, for supports and pipes). **Supports**
  (`BuildJetty`, `Building <i>/Jetty/`): per module whose column rises above ground, random no
  repeat, centred, width-scaled, back at `SupportBack`, top at the deck. **Side walls per
  storey depth** (column + jetty): jettied beside unjettied = a step closed from the jettied
  side (a module, or depth + setback if longer); only ground-storey steps get a side
  foundation. **An unjoined run end closes itself** (`CloseRunEnd`, `Building <i>/End <n>/`):
  side walls facing out, against the front wall's end, from behind its face back past the
  column line, beam band below. **Where the neighbouring run goes on nearly in line at the same
  level** (an inside bend between segments of different widths: never a joint, edges offset)
  `StepsInLine` compares the two end modules' fronts storey by storey; each storey standing
  further into the street (> `sizeTolerance`) is closed (module, or step + setback), a ground
  step on a side foundation; the other side closes its own. Elsewhere (no neighbour, a storey
  above its top, or no step either way) a jettied building's upper storeys are closed (module,
  or jetty + setback). Dead ends never jettied.
- **Facade lean** (`facadeLean`, preview of the storybook lean): **on a leaning run** upper
  storeys' street walls, beam bands between and corner fillers carrying them lie on one plane
  tilted over the street, hinged at the first upper storey's base: each wall turned about its
  base (`Lean`), forward by height above hinge x tan (`LeanForward`, added like the jetty;
  `LeanOffset` same for any run); doors and inserts take the wall's rotation.
  - **Only one side of a corner leans** (`Run.Upright`, `AssignLeans`, after corner ends
    widen; both would open a wedge at the top): spec order, a run leans unless one joined to it
    at a corner already does (corners alternate; a four-sided lot leans on two opposite
    sides); runs nearly in line (fronts within branch angle) don't constrain; inside bends and
    dead ends (no joint) both lean. Log `Facade lean: n run(s) lean, m stand upright ...`
    (three-levels 41/45).
  - **Upright side's end follows the leaning face** (`FollowLeans`,
    `ModuleSpot.FollowLeanStart` / `End`): per upper storey the corner-end piece (end module's
    wall if no fillers, else last filler) widened by `LeanOffset` of the storey base (scale,
    plus shift at run start) and sheared toward the corner (`PlaceWall` `shear`); such module
    walls always plain (sheared hole takes no insert); beam below widened, not sheared. Not yet
    looked at: sharp-corner notch cover with one side leaning; top slot at inside bends/dead
    ends.
  - **Leaning run's side walls follow** (`LaySideStorey`: column steps, jetty ends, corner
    steps; upright runs' stay upright): start `LeanForward` nearer the street (side beams at
    their height), mesh sheared (`PlaceSideWall` `lean`): top street corner out by height x
    tan, base level, back edge plumb.
  - **`ShearedMeshes`** (side and corner end walls): mesh copy (every LOD) shifted along width
    by height above the connection point's base x tan, normals/tangents carried; mesh angle =
    world angle x height scale / width scale, rounded 0.25°; one copy per shear per sector
    (`Begin(sector)` at the start of a generation), saved in `Sheared/<sector>/` as
    `<mesh>_<fbx guid>_<axis><+/-><angle>.asset` (three-levels ~106), set on the instance
    MeshFilters as override (transform, colliders, prefab link unchanged). After the NavMesh,
    `ClearUnused` deletes the sector folder's copies no MeshFilter under the root uses (not
    undoable, like the ground meshes). Colliders stay unsheared (up to 0.26m off at a 3m
    storey's top). Also roof panels' hip ends (`ShearEnds`, Roofs 5e): factor per end blended
    across the width, `<mesh>_<fbx guid>_ends<min><max>.asset`, factors rounded 0.01.
  - **Roofs follow the lean** (`RoofFace`: face at the top storey's walls' top, `LeanForward`
    included), so the eave stands in front of the leaning walls as it would upright walls;
    the roof itself stays unleaned.
  - **Everything else upright** (ground storey, supports, dressing).
- **Storey beams:** where both storeys' styles have `beam`s (`PlasterWalls_A_MidBeam`, the
  common-height plaster styles; wall-tall, woodlarge, cellar have none), **a band between the
  walls**, as tall as the lower style's tallest beam, overlapping each side by
  `storeyBeamOverlap` (storeys above rise by band - 2 x overlap; `StoreyLevels`,
  `StoreyLevel.BeamBottom` / `BeamHeight`). `BuildBeams` (after storeys, `Building <i>/Beams/`):
  per module whose column rises above the band, lower style's, random no repeat, centred in
  depth on the wall above (`DepthMiddle`; else back on the back line), width-scaled
  (`FitScale`), height-scaled to the band. **Side walls get beams** (`LaySideBeams`): under each
  banded side-walled storey, same lengths, way round, start, centred in depth (median middle).
- **Doors** (`PlaceDoor`): each `door` opening of each wall gets a usable `door` (random, no
  repeat within the wall, any style, never with a gameplay script), **sized to the doorway seen
  through the wall** (`ThroughOpening`: rays through LOD0, so a narrower stone frame counts;
  wall scale included) + `doorSideMargin` each side + `doorInsertMargin` at top (edges tuck
  behind the frame; an arch hides a square door's corners); bottom middle on the sill under the
  see-through middle, at mid wall depth + `doorInsertDepth`; under the storey group. None
  usable: doorways open.
- **Window inserts** (`PlaceInserts`): every facade wall (dead ends too, not steps' side walls),
  per `window` opening (`PlasterWalls_A_WindowBase_Hole` 1 of 1.2 x 1.3m;
  `Masonary_WoodLarge_WallWindows` 2 of ~0.5 x 1m), at `windowInsertShare`, a usable `window`
  (one front, any style; inserts aren't style-tagged) at least the hole's size and at most
  `windowInsertMaxMargin` bigger each side, **always unlit**; random, no repeat. Two draws
  (fill, pick) per hole always. Render box middle on hole middle at mid depth (back plane to render front) +
  `windowInsertDepth`. Pack inserts (`Window_A`-`D`, `C_Open`, `D_Open`) 1.6-2.0m with frames:
  fit plaster holes; woodlarge holes stay open.
  - **Blind window face** (`faceWindow`): a wall with no opening but a `window*` point
    (`WallTall_WindowBase`, set by hand where the pack's demos put an insert) gets any unlit
    insert centred on the point + `windowInsertDepth`, only when the module's window
    draw came up (`windowDrawn && !overrun`).
  - **Lighting windows** (`LightWindows`, after dead ends, before roofs): **once every window is
    placed**, the inserts above a building's ground storey (**never storey 0**) with a usable lit
    twin (prefab named `<own name>_Lit`) are shuffled and each swapped for its twin (same parent,
    sibling slot, transform) **unless a lit window is already closer than `litWindowSpacing`**
    (3D, between insert roots): lit windows spread evenly at that spacing or a little more. One
    log line (lit of lightable).
  - **Lit windows:** `TL_Window_<x>_Lit` (`Assets/Prefabs/Environment/Windows`) = `TL_Window_<x>`
    + material `WindowLit` (`Assets/Art/Materials/Environment/Windows`; copy of the pack's
    unused `Medieval_Windows_Light_2k`, URP Lit, emission via the pack's pane-only emissive map,
    halved to (3.1, 1.49, 0.83); not judged in headset). **Its GI must stay Baked: with None URP
    strips `_EMISSION` on import** and panes go dark. **Each carries a Baked spot** (`Window
    Light`, child at window middle 0.25m out on +Z, pointing out and 25° down: (1, 0.48, 0.27),
    intensity 1.5, range 4m, 110° cone, 60° inner, soft baked shadows) for the street spill (not
    a mixed light). Tune emission (panes) and spot (spill) separately. No `LightSource`
    (gameplay ignores it); nothing at runtime. Only inserts can be lit (blind-window panes come
    from atlases without emission). Lit twins of the four verified inserts annotated alike
    (`C_Open`, `D_Open` unreviewed both forms).
  - **TeaLeaf > Level Gen > Swap Window Light** (`WindowLightSwap.cs`): each selected
    `TL_Window_*` instance (or anything inside one) replaced by its twin, `_Lit` <-> plain, from
    the same folder: same parent, sibling slot, local transform; instance overrides not
    carried; one Undo step. Outermost instances only (a window nested in a prefab, as in the
    Open Window Wall, is skipped, logged). Under `Generated` a hand edit (regeneration asks,
    then redraws); rebuild proxies and rebake after.
- **Columns** (`SplitColumns`, after laying, before building; side walls `FillSides` after):
  **every building cut end to end into columns** of `columnWidthMin`-`Max` modules, never across
  a building end or bend (last takes the rest). Rolls, in order, always all: width, off the line
  (`columnShare`), forward (`columnOutShare`), depth (`columnDepthMin`-`Max`), height change
  (`columnHeightShare`), taller/lower (even), amount (1..`columnStoreyVariation`).
  - **Depth** only on straight runs of at least `columnMinRunModules`, never a column holding
    the run's first/last module; forward into the street only where the step stays out of other
    streets (`ColumnClear`; a lot side lines the segment whose edge it runs along -
    `StreetClearance.LiningSegment`, both ends within 0.1m of the edge line, facing in - so it
    steps only into that street, never a cross street at a block corner; lining none = stays
    out). Capped at half the frontage's narrowest module of any storey (neighbours differ by at
    most a module; one side wall fills a step). Walls and foundation move with the column.
  - **Height:** `StoreyPlan` count ± change, within `minStoreys`-`maxStoreys`; storeys built up
    to count + `columnStoreyVariation` (within max); a storey above a module's column is left
    out. Lots and dead ends use the building count.
- **Side walls** (`FillSides`, between neighbouring columns of a straight run): plain `wall`s of
  **any width** whose height fits the storey (`Fits`; height-scaled beyond `sizeTolerance`),
  **not `showsOpening`** while another will do (`BuildingStyle.SideWalls`; ground-only ones only
  on the ground), laid by the fitting rule (`FitWidths` on the module width; `PickForWidth`
  random near each width, scaled; band beams below on the same joints), side-on facing out of
  its column toward the other.
  - **Source:** storey's style, else neighbour column's at that storey, else the building's
    storeys above, **else the storey's blind-window walls** (wall-tall has no plain wall
    without an opening and others are far shorter than 4.5m). None: left without, logged once
    per frontage.
  - **Any side-wall run going back (here, corners, jetty ends) is cut where it would enter a
    street** (`StreetClearance.Reach`: ray from either face vs every street shape except one it
    starts in, every corner's outside counted as street), rest covered by the rule; a storey
    `streetHeadroom` up is not cut. Unplaceable side wall, or no room: reported.
  - **Taller column:** each storey above the other's top (> `sizeTolerance`, in metres) gets
    `sideWallMultiplier` x column width in modules (min 1; 0 = none).
  - **Different depths:** forward column's other storeys get one module each; the step one
    `foundation`, front `foundationFrontOffset` past the ground side wall.
  - **Under or beside a gable: back to its back foot** (`GableReach`: 2 x ridge back - where the
    slope's underside meets the walls' top - eave overhang, behind the roof's face; ~7.1m at
    the defaults; facades only, a lot's roofs have none). `FillSide`: the top storey's and every
    storey above the other column (its own gable), and every storey above the other column
    below the other's ridge (`RoofRise`; the other's gable), from the nearer roof face of the
    two. `CloseRunEnd`: every storey it closes. Shorter, the gable's back half stands on
    nothing and a gap shows under it.
  - **Column's right end (from street), plain front-wall end: side wall stands in the other
    column's space, backed onto its own column's end**: back plane on the join (`FaceClearance`
    1cm in), thickness in the other column, starting halfway between the main surface and the
    furthest reach of its front wall beside the join (`SideWallFace`, `FrontPlane`,
    `ModuleSpot.Reach`, per storey) so its street end hides among timbers; covers the hollow
    end, its face never on the end's; continues through the other column's front wall (hidden)
    and above its top through the roof like a party wall.
  - **Column's left end, trimmed front-wall end: side walls tuck behind** (`SideWallStart`,
    column steps and jetty ends, not corners): join moved `sideWallTuck` x `wallThickness` into the column (so the thin side wall still stands out past the end it covers, and its face in front of a lower neighbour's gable centred on the join), street
    end on `FrontPlane`, trim covers them.
  - **Placed by render box end**, not connection point (some trims stand past it).
  - Under `Building <i>/Side <n>/` (`n` = join's module index). Corner-closed run ends get none;
    other jettied run ends close themselves. Dead ends are one column.

## Corners (5b)

- **Inside a bend** (`InsideBend`: next run heads into the street the run faces): runs carried
  past the point to where the walls' back lines cross (`Runs`, `BendEnd`, max
  `cornerMaxReach`); fronts meet first, so it closes; modules fill to it by the fitting rule.
  (On the street's inside, the outer corner, runs stop where the edges cross.)
- **Inside L** (a street ending where another goes on, turning into the street the facade
  faces; outer facades round a block): both runs to where the edges cross (`InsideCorner` in
  `Runs`) and **one more module always overruns behind the other street's facade** (start: laid
  back past it in `LayFrontage`; end: `ExtendLastBuilding`). **The other street** = of streets
  leaving the point, the first met turning from the run's way out toward the facade's side; an
  L only if it starts/ends there (through = branch) and turns into the faced street (not a
  near-straight run on, nor a branch on the facade's side). Block corner wider than a half turn
  (ring bending at a junction): edges cross behind the point, run carried back past it (max
  `cornerMaxReach`). No overrun where clearance cut the run short.
- **Outside a bend and street corners** (two frontage ends of different streets at a junction -
  a branch, each stopping where it clears the other, or two streets end to end at an L - on the
  outside only, `Outside`: one end's way out points the way the other's facade faces; paired
  where each carry <= `cornerMaxReach`, shortest first): **each side carried storey by storey**
  (line at column depth + jetty, against the other side's same-index storey or its top) and
  **the walls make the corner, no corner piece** (`WallTarget`).
  - **Each side runs `cornerOverrun` of the way from the other's back line to its face** (face =
    back line + shallowest wall of its style, `FaceLine`, `Reach`, less `wallOverlap`; the
    frontage line stands out by its styles' deepest wall), the end carried by whichever end-face
    corner (back or face) reaches the target first (`EndReach`): both ends hide inside the other
    wall, end posts meet; the strip between posts is by design (report excuses met ends).
    **Runs nearly in line** (fronts within `branchAngle`: a slight bend between segments of
    different widths, whose edges cross metres short of the point) have no such target: both
    sides' walls are carried to the corner itself and meet end to end.
  - **Covering side** (`FirstCovers`, `TrimmedEnd`: seen from the street a wall's left end is
    the trimmed post, the right hollow; first of the pair if both or neither) places a sharp
    corner's cover and an in-line joint's column.
  - **Side already past its stop** (lines meet behind its end: shallow outside bend + column
    stepped forward): end module cut back before building (`TrimCornerEnds`, on the storey that
    must stop soonest, never below `fillStretchShare` of a module; other storeys carried on); a
    side carried nowhere is reported.
  - **Carry <= `fillStretchShare` of a module widens the end module's wall** (`WidenCornerEnds`,
    pre-build: `ModuleSpot.WidenStart` / `WidenEnd`, read by `BuildStorey`, `BuildBeams`,
    `BuildJetty`). Longer: a **filler** of plain storey-height walls (borrowed as for side
    walls) by the fitting rule (`CornerWalls`, `PlaceFiller`); under each carried storey the
    band's beams (only as long as the fillers: no sliver beams), jetty supports under a jettied
    first storey and ground foundations (`PlaceFiller`, `PlaceInRun`, limited by
    `FoundationCarry`) run on to where the lines meet. **Fillers brought forward to the other
    side's end-wall reach** (`CornerFaceReach`, `faceReach`; beams too), flush.
  - **Columns either side of every convex corner get the same storey count**
    (`EqualCornerHeights`, after corners found, before building: the lower, repeated until
    stable). **Corner-joined buildings share storey styles** (`MatchCornerStoreys`, just
    before; union-find `CornerLeaders`): each set takes the storey styles and jetty of its first
    in spec order, keeping own module spacing, foundations, door; one whose ground module is >
    `maxStoreyWidthMismatch` off keeps its own (logged `Corner storeys: ...`).
  - **Known:** at a ~20° inside bend with a jettied building a storey's corner filler can be
    missing between the trimmed end module and the next; the report excuses the hole (filler
    foundations cover the lowest samples) - `example-slope` hill street, left side, at the bend
    (also with level streets).
- **Up to `cornerAngleTolerance` sharper than right**: met as square; the covering post stands
  out past the other face by depth x cos(angle) (~8cm at 82°). **Foundations stop where their
  back meets the other side's foundation front** (`FoundationCarry`), out of the other street.
- **Sharper:** **each side stops where its back line meets the other's front line**
  (`CarryToBack`), and the covering side puts a **`column`** across the notch (`PlaceCover`:
  own storey style's, else the other's, storey height, scaled; fronts' middle facing out
  between the facades, render box's furthest reach that way on the point). None: logged, bare.
- A storey above the other side's top: **closed by side walls** facing out (`LaySideStorey`).
- **Nearly in line** (lines meet > `cornerMaxReach` from either end or never: slight bend
  between different widths, or **a street running on nearly straight into another** - ends of
  different streets, ways out opposite, facades the same way within the branch angle, <=
  `cornerMaxReach` apart, `InLine`): each side to its run end (bisector) and halfway across any
  gap (`InLineCarry`) by filler. Per storey the side further into the street (>
  `sizeTolerance`) closes the step with side walls facing out (module, or step + setback);
  with no step the first side puts a column over the joint (as `PlaceCover`).
- **Side return:** an unpaired frontage end at a junction gets a one-module run along the
  branching street's near edge (widest street leaving towards the frontage's side), from where
  the frontage line meets it, facing into it, joined as a corner (`AddSideReturn`); far end open.
- Joined ends marked (`ModuleSpot.JoinedStart` / `JoinedEnd`) so nothing else closes them.
  Pieces under `Building <i>/Corner/`.

## Lots (5d)

- `LayLot`, `Generated/Lots/Lot <id>/Building 0/`: a closed frontage of one building (lot's
  styles, `StoreyPlan` count), **a run per footprint side** from the front, facing out (from
  the corners' winding), each given the segment it lines (`LiningSegment`), which its columns
  may step into (log `n run(s) (m lining a street)`).
- **Footprint** (`LotSpec.Corners`, where wall fronts reach): `outline` (any convex shape), or
  **the enclosed block** with neither outline nor modules, else `widthModules` x
  `depthModules` + wall overlap centred on `origin`, front to `yaw`. Each side gets whole
  modules from its start; corners fill the rest. Door counted along the front from the left
  seen from in front. Ground base at `origin.y`.
- **Everything a facade gets applies per side** (columns, jetties on all sides at once, beams,
  doors, windows, foundations, side walls); **corners are joints** (last side joined to the
  first). **No floors/interiors: always by hand.** Ground laid round it (`StreetGround`).
  Validator: outline convex, every side fits a module, door within the front's modules;
  overlaps use its bounding rectangle.

## Roofs (5e)

- `FacadeRoofs.cs` (in `FacadeBuilder`, after corners and dead ends). **Own tree
  `Generated/Roofs/<frontage, lot or dead end>/Building <i>/`** (`RoofGroup`) to hide all
  roofs at once. **Pack's way: ridge along the street, slope each side, gables at the ends.**
- **Pieces** (any style): `roof-end` (`RoofTiles_MiddleSide`), `roof-ridge-end`
  (`RoofTiles_TopCorner`, carries the ridge cap), `roof-middle`, `roof-end-half` /
  `roof-ridge-end-half` (one-panel roofs), `roof-gable` (`RoofWall_LargeA`/`B`, `_MedA`/`B`;
  half gables `RoofWall_CornerA`/`B` unverified, unused), `roof-chimney` (`TL_Chimney_full`,
  maintainer's variant in `Chimneys`), `roof-dormer` (`TL_Extension_Small_window` in
  `Extensions`: box, window on +Z, small gabled roof falling back, pivot back bottom),
  `roof-railing` (`IronFixtures_ArowRailing_Long` 2.42m, `_Short` 1.2m), `roof-hip-base`
  (`Roof_PeakedBase`: lowest piece of a spire corner stack, outer corner between its two
  `fronts`; points `eave` at the outer bottom corner, `next` where the piece above stands by its
  pivot; the only hip piece used; `roof-hip-middle`/`-top` annotated, unused). **Only closed
  (manifold) pieces**: `RoofTiles_Middle` and both `_Half` panels show gaps, excluded; no
  middle -> end pieces fill between (`Middles`); no half -> one-panel roof = one end piece. No
  end + ridge-end: no roofs (logged).
- **One roof per run stretch** whose column tops (m) and top-storey faces (depth + jetty) line
  up, continuing at a run end as far as a corner carried the top storey
  (`ModuleSpot.CarriedStart`/`End`). **Roof end at the outer face of the top storey's end
  wall** (`ModuleSpot.SideFaceStart`/`End`, written by `BuildCornerSide`, `FillSide`,
  `CloseRunEnd` as reach + thickness; `RoofStretches` via `EndOut`), verge `roofVergeOverhang`
  past.
- **Slope:** `roofRows` rows at `roofPitch`, eave `roofEaveOverhang` before the top storey's
  face and `roofEaveDrop` below its walls' top, rows overlapping `roofRowOverlap`; panels =
  round(length + 2 x `roofVergeOverhang`), width-scaled, overlapping `roofPanelOverlap`: verge
  pieces at both ends (verge at own +X; other end mirrored by negative scale), the same pieces
  between - so a verge faces the next panel at every joint, its top tiles set back ~0.3m, which
  `roofPanelOverlap` must cover; one-panel roof = two mirrored halves. Top row ends = ridge
  pieces, except a one-panel row (`LaySlope`).
- **Back slope mirrors the front. Never shorten a facade roof for a street behind** (a
  separately scaled stretch stands at another height and opens a gap; at roof height a slope
  over a street is clear).
- **Gable where a corner can't close an end** (`SetGable`): no `RoofCorners` corner - lot vs
  facade, different heights, convex corner wide of right, folded/parallel eaves, or eaves
  crossing > `cornerMaxReach` from either end - both roofs end alone, gabled, never open.
- **Order:** all worked out first (`RoofStretch`: `RoofStretches`, `RoofShape`), corners
  found and ends set (`RoofCorners` from `RoofJoints`: every joint + every inside bend, which
  has no joint), laid (`BuildRoof`), hip strips last (`HipStrips`, `PlaceStrip`).
- **Corners:** two roofs ending at one joint or inside bend with walls' tops within
  `sizeTolerance` **meet along the hip/valley line** (equidistant from both eaves), no gables,
  every row ending on it. A row b back from its eave ends `slant` x b past the front eaves'
  crossing, `slant` = (other roof's back dir . this end's way out) / (1 + back dirs' dot):
  negative outside (hip), positive inside (rows run under the other roof: valley). Back-slope
  rows (eave 2 x ridgeBack behind) end `slant` x (2 ridgeBack - b) past it: convex = hip front,
  valley back; concave = valley front, hip back. Into a valley a row runs until its top edge
  reaches the line, + `roofPanelOverlap`; at a hip it stops where its middle meets the line
  and **its end panel is sheared so its end edge lies along the hip line** all the way up
  (`ShearHipEnds`, `ShearedMeshes.ShearEnds`: the hip end slants by slant x cos(pitch) per
  metre up the slope, the other end stays square for the overlap; a square end's upper corner
  would stand past the line and stick up through the strip, worst at corners sharper than
  right, where the strip is narrower). The end panel keeps its verge at the corner, hidden
  (turned inward, two set-back ends leave a slit). Convex corners taken when the eaves' angle is
  in `roofHipAngleMin`-`Max` (`RoofCorner.Angle`, `Hipped`), else gable rules (a near-straight
  bend's hip piece would be far wider than tall); concave of any angle taken, back hip stripped
  within range. Two facade roofs take the lower row scale (ridges level); lot sides keep theirs.
  Elsewhere roofs just overlap (no hip/valley tiles in the pack).
- **Hip strips**: **`roof-hip-base` copies stacked up the hip** from the eaves' crossing to the
  ridges' (as far in as the shorter slope reaches, top of its cap, `RidgeCap`), outer corner
  out, **leaned back `roofHipLean`** so the steeper faces lie flatter. Lowest `eave` corner
  `roofHipOut` along each slope's front and `roofHipRaise` above eaves; top `next` at the
  ridges' crossing; even steps of max `roofHipStep` (three on two rows), overlapping like
  scales. **Fitted to the angle**: scaled across by tan(angle / 2) and in height by across x
  sqrt(2 / (1 + across²)) (61° -> 0.59 x 0.72, 128° -> 2.03 x 1.27, wider tiles accepted), in
  the hip's frame: each strip under a `Hip` empty (under the building's roof group) holding
  rotation (lean included) and scale, piece turned 45° inside. `RoofEdgeColliders` finds a
  piece's building via `BuildingGroup` (two below `Roofs`), not its parent. Always in front of
  a convex corner; behind a concave one when both have back slopes. No hip piece: none. No
  collision (`TL_Roof_PeakedBase`: the pack MeshCollider has no mesh).
- **Gables** (`RoofGable`): one whole `roof-gable` per end, **pivot plane on the roof end** (the
  `RoofWall_*` faces are within 0.1m of the pivot plane, beam ends 0.45-0.6m in front, so
  placing by render front sinks the face ~0.5m), upright under the ridge, triangle 2 x
  (ridgeBack - under) wide, depth `wallThickness` about the pivot plane (beam ends come in to
  ~0.2m), pivot at base middle. **Its tie beam's underside, not its box bottom, sits
  `roofGableSink` into the walls' top** (`GableTie`: lowest vertex in the middle half of the
  width, read from the prefab once per build; the Med pieces' carved corner blocks hang 3.5%
  of their height below the beam, else a ~16cm slot shows); height ridge - that base, so the
  apex stays under the ridge;
  `GablePiece` = nearest height-to-width, random within `GableShapeTolerance` 0.1 (Med gables on
  60° two-row roofs), scaled. Not at corners, nor where a wider-than-`roofHipAngleMax` corner's
  other roof runs over (`ModuleSpot.GableStart`/`End`, by `BuildCornerSide`).
- **Lots:** each side slopes up toward the footprint middle, no gables; where the middle is
  nearer than a facade slope's reach rows are scaled to end there (hipped, strips at right
  angles); bigger lots get both slopes round a courtyard, scaled so the inner eave never passes
  the middle.
- **Chimneys/dormers** (`BuildRoofFeatures`, after strips): one per building whose first roof
  is >= `roofFeatureMinPanels` panels (span / end-piece width, rounded), even odds if both
  usable, within `roofFeatureSpread` of the span about its middle, half a piece in from ends.
  - **Chimney straddles the ridge** (needs a back slope): front face `roofChimneyUp`-`UpMax` up
    the slope, capped so its back stays below the ridge, base `roofChimneySink` under the tiles,
    stretched up (never shrunk) so its top is `roofChimneyRise`-`RiseMax` above the tiles at its
    back.
  - **Dormer on the front slope**: front wall bottom (`DormerFront`: mesh's lowest vertices'
    furthest-forward z, once a build) `roofDormerUp` up, `roofDormerSink` under; refused
    (chimney instead) if it would top the ridge or its back pass it. At 52° pitch the back top
    corner shows (fine at 60°). Known: open back shows through a narrow roof's back slope.
  - **Smoke** at `chimneySmokeShare`: `Assets/Prefabs/Effects/Chimney Smoke` child at the top
    middle of the `IronFixtures_Chimney` pot inside (renderer bounds; else piece top), pointing
    up, so the cull takes it with a hidden chimney (`FacadeBuilder.PlaceEffect`); small pot cold.
    Log `Roof features: ...`.
- **Ridge railings** (`BuildRoofRailings`, `PlaceRailings`, after features):
  `roofRailingShare` of buildings, along their first roof's ridge: longest pieces first, shorter
  in the remainder, `roofRailingGap` apart, centred, `roofRailingInset` from the ridge ends (at a
  corner from where ridges meet: |slant| x ridgeBack in from the eaves' crossing), each leaning
  up to `roofRailingLean` along and a third as much across. **Feet seated by ray**
  (`RoofTopAt`: down from 2m above the computed ridge onto the highest `TL_Roof*` Environment
  collider within 4m, after `Physics.SyncTransforms()`), `roofRailingSink` under the cap top.
  Skipped if buried (3m upward Environment ray hits) or the roof under an end differs from under
  the middle by > `RailingLevel` 0.3m (hip, corner, past a gable); the cull removes a railing
  with no `TL_Roof*` within `RailingDrop` 0.5m below its root. Matched to the maintainer's
  hand-placed set (round `example`: feet 0.05-0.29 under the cap, gaps
  0.6-1.4m, leans to 13°). Log `Roof railings: ...`.
- **Roof edge colliders** (`RoofEdgeColliders.cs`, right after the cull): **a roof piece
  facing the void beyond the level loses all colliders** (override; renders stay). Facing: slope
  panel (forward >= 0.3 up) along its level forward, else (gables, hips) away from the
  building's middle (`Roofs/.../Building <i>`). `Physics.CheckBox` (Environment, no triggers)
  piece-wide, from `roofEdgeGap` past the building's furthest point to `roofEdgeReach` further,
  from the piece's top down 100m: empty = void. Log `Roof edge: ...`; kept panels nearly all
  face a street.
- **Roof colliders** (`MergedColliders.RunRoofs`, after roof edge): slope panels
  (`roof-middle`, `roof-end`, `roof-end-half`, `roof-ridge-end`, `roof-ridge-end-half`) lose
  their boxes at any tilt, merged in tilted frames within `roofColliderTolerance` into `Roof
  <n>` (`Generated/Roof Colliders`, Tile). Rows' tops differ 7cm, undersides 2cm: rows merge
  where ends line up (gables), else a box per row. Three-levels: 1215 colliders/1343 pieces ->
  527.
- **Building colours** (`FacadeBuilder.ColourBuildings` in `FacadeRoofs.cs`, last in `Build`
  after lamps; `ColourVariants.cs`): **one colour per corner-joined set** (`CornerLeaders`), on
  members' facade and roof groups. **Colour = material variant by name**: materials in
  `Assets/Art/Materials/Environment/{Roofs,Walls,Extensions}` only (`ColourVariants._roots`)
  listed by file name, `<material>_<suffix>` (e.g. `Medieval_Roofs_4k_brown`) is the
  `<suffix>` variant; choices = suffixes present + `default` (the same-named unsuffixed copy in
  those folders - maintainer's editable copy - else pack material). **Never widen the roots**:
  any `<material>_<x>` copy made for another use there becomes a colour (the doors'
  `Medieval_Doors_4k_Keyhole` in `Doors` would put the keyhole material on generated doors);
  a new coloured family gets its folder added. **Walls and roofs apart** (`Colour()`): two draws per `Roofs/<owner>/Building <i>`,
  always (stable as materials are added), walls then roof, each from its groups' own
  materials' suffixes; `ColourVariants.Apply` swaps every LOD's materials where a variant
  exists (override), noting materials lacking the suffix. Log `Building colours: walls brown
  x36, default x17; roofs brown x16, ...[; n building(s) with no variant][; no variant for some
  colours: ...]` (`ColourCounts()`). Variants (with unsuffixed copies) in
  `Assets/Art/Materials/Environment/{Roofs,Walls,Extensions}`: walls `brown`
  (`Medieval_PlasterWalls_A_4k`, `_B_4k`, `Medieval_WallsTall_A_4k`); roofs `brown`, `grey`
  (slate), `orange` (terracotta) of `Medieval_Roofs_4k`, `Medieval_RoofPeaked_4k` and the
  dormer's `Medieval_Extension_Small_4k` (dormer matches roof; grey/orange albedos = brown with
  tile sheets recoloured outside the editor; a same-named hand-painted `.tif` replaces one).
  Gables keep their material until a variant exists.

## Street clearance, street test, dead ends

- **`StreetClearance`** (one per generation, in `FacadeBuilder`): **no wall may cut across a
  street.** Each segment = convex shape: rectangle at rolled width, **mitred at shared points**
  (`Mitre`: next segment at a bend, other streets at a junction) - each side cut along the line
  from the point through where its edge meets the nearest leaving segment's edge on that side,
  so no square end pokes onto ground the facades need. **Two shapes, differing outside a
  corner**: `Corners` cut through the point; `Filled` extends to where the edges meet first, so
  the outside corner counts as street. Dead ends and near-straight continuations (within the
  branch angle) stay square.
  - Guards: runs cut clear (`Clear`, on `Corners` - `Filled` would cut facades short of an
    outside corner and open a gap); side walls stop short (`Reach`, on `Filled`); **every
    facade/corner/lot/dead-end piece checked as placed** (`Allows`, via
    `PiecePlacer.Clearance`, off for roofs and ground): placed render box (scaled) vs each
    `Filled` shape by SAT.
  - A piece >= `streetHeadroom` above the street beside it (steps included; for its faced
    street beside a flight, the low level it stands on, so jetties run over steps) may overhang.
    Lower: into its faced street (`Facing`; every street for a lot) up to `columnDepthMax` +
    `foundationFrontOffset` + `overlapTolerance`, any other only `foundationFrontOffset` +
    `overlapTolerance`. Beyond: not placed, listed (`Street clearance: ...`); should be only
    the odd corner filler's foundation.
- **`StreetTest`**: **valid only if a ball travels every street without touching a wall**: ball
  `streetBallRadius`, centre `streetBallHeight` above the surface (flights as walked), along
  each middle line end to end, a radius short of a dead end's facade; anything under the root
  but `Streets` hit (`Physics.OverlapSphereNonAlloc` every half radius) blocks. One entry:
  valid, or error per blocked street with up to five blockers and distance. (`StreetTest.Walk`
  reused by the cull.)
- **Dead ends**: street end with no other point within `junctionDistance` -> facade across it
  (`StoreyPlan.DeadEnds`, after the frontages' rolls, before lots; built after frontages,
  `Facades/<street> dead end <point>/Building 0/`). Styles of the frontage building at that end
  (left, else right; none: unclosed, logged), no door, enough ground modules to span the end
  segment, rolled storeys. One straight run through the end point, square to the segment,
  facing back up the street, centred; overhang runs behind the side facades.

## Visibility cull (`VisibilityCull.cs`)

- After the street test: **every generated piece nobody in the streets can see is removed**
  (tiles, back slopes, walls, beams, pipes, walkway parts, foliage). Never: lamp posts (`Lamps`
  group); ground meshes aren't pieces (stay, occlude).
- Looks: walk every street (`StreetTest.Walk`, stopping a radius + `overlapTolerance` short of
  dead ends), every `cullStep`, at `cullEyeHeight` over the middle and each edge (a radius in
  from buildings) and `cullHeight` over the middle (roofs). Looks inside something built (edge
  looks at bends) dropped (rays see through from inside).
- Per piece a grid of points in its LOD0 render box (shrunk 0.8, own space); seen when an
  Environment ray to one hits nothing, its own collider, or a point on its render box within
  5cm (merged boxes standing in for its colliders). Beyond `cullRange` not tried. Nearest eight
  looks one ray at a time until seen; the rest from all in-range looks as parallel
  `RaycastCommand` jobs.
- **Side walls a seen gable stands on or against stay** (`KeepGableWalls`: under a `Side
  `/`End ` group, middle within 0.5m of the gable's plane, across part of its width, top no
  lower than 0.5m under its foot): seen only through the slot under a gable, their sample
  points can all fail while the gap their removal opens shows. Logged `; n side wall(s) under
  a gable kept`.
- Removed with Undo, emptied groups too (groups holding merged colliders stay), then railings
  on removed ridges. Log `Visibility cull: n of m piece(s) removed ... (foliage, facades, lots,
  roofs; looks; time)`. Three-streets: 320 of 3453 in ~5s (197 side walls under gables kept).

## Report (stage 6)

- **`GeneratedReport`**: after everything (`LevelGenerator.Generate`, before the fingerprint)
  and on demand (**TeaLeaf > Level Gen > Report Generated**, selection or every root; spec from
  `GeneratedRoot.SpecPath`). Console summary + worst forty (warning if any); a **marker** each
  on the root (`GeneratedRoot.Marker`: kind, position, box, note), drawn in Scene and headset
  (`IDebugDrawable`/`DebugLines`: red free wall end, orange open roof end, magenta refused
  piece, yellow other; selected, a 3m line up). Only finds and lists; `GapFiller` closes ends
  earlier via `GeneratedReport.FindFreeEnds` (`FreeEnd`: wall, end-face middle, outward and
  front dirs, bottom, top, thickness).
- **Free wall ends** (`FreeEnds`): front walls (storeys, corner fillers, dead ends); side walls
  only cover (their front end is a real end at its step, back end open by design: no back
  walls). Both vertical end faces (across the longer horizontal side of the LOD0 render box,
  own space) sampled 2cm out, 3 depths x 6 heights; a wall wearing a sheared mesh
  (`ShearedMeshes.IsSheared`) is sampled along its slanting ends (`ShearedEnds`: its vertices'
  reach each way within 5cm of its bottom and of its top), never its box's end (the furthest
  either reaches), whose lower samples would hang in the air in front of a leaning storey. **Bare** = no covering box holds it (all
  but doors, inserts, roofs; grown 8cm) **and seen from a street**: line to one of up to six
  standing spots within 14m, >= 30° apart, at 1.6m, crossing no covering box (the wall's own
  included: end faces are seen only from in front). Spots: decking tiles or ground vertices
  under `Streets` (one per 3m world cell, `GroundSpots`), inside a segment rectangle at
  resolved width, >= 0.75m from the footprint of every wall standing below eye height there
  (a jettied storey overhead doesn't count: the street under it is walked).
  - Find = > 1/3 samples bare (bare share, seen from). **An end another wall meets** (any
    sample covered) is a corner, not a hole (corner posts; the by-design strip, open at the
    front, closed at the back) **unless its back stands open over its top or bottom**: the rows
    counted in from that edge whose back sample (behind the wall's front) is uncovered, at least
    `PartRows` 2, judged alone by their bare share (a column on a higher level beside a lower
    one at a flight of steps, only its front touched by a jettied storey or supports). Known
    non-holes: corner post proud of its module (`PlasterWalls_B_PlanksCorner`), wall-tall
    against stone, a post met at an angle beyond the samples (100% bare but closed).
- **Build problems** (`GeneratedReport.Add`): piece refused by `StreetClearance.Allows`, side
  wall with no room or unplaceable, corner side carrying nothing, module with no fitting wall,
  style without foundation.
- Not checked: overlaps/z-fighting, roof coverage, floating or sunken pieces.

## Gaps (`GapFiller`)

- `GapFiller.Run` after nettles, before street test and cull (fillers tested and culled like
  walls): `GeneratedReport.FindFreeEnds` ends (walls stopping short at a bend, lot corners the
  joints miss, a wall above/past its neighbour at a step) are paired and filled; final report
  should list none.
- **Pairs** (`Pairs`): ends on different walls <= `gapBridgeMax` apart, bottoms/tops within
  `gapStoreyTolerance`, facing each other (chord runs out of each), walls the same way round
  (within a quarter turn); nearest first, one pair per end.
- **Bridge** (`Bridge`): one plain wall across the chord between end-face middles, facing the walls' way,
  render middle line through both faces, points `wallOverlap` inside each end (width = chord +
  2 overlaps, height = first end's wall), base at the first end's bottom, under the first wall's
  `Storey n`/`Corner` group (merged by `RunWalls`, ends checked).
- **Return** (unpaired; `Return` via `FacadeBuilder.PlaceSideWall`): plain wall side-on facing
  out of the end, face on the end's plane, thickness inside, near end at the wall front, running
  back at its own width, under the building's `Side gap` group (a `Side ` group: open back end
  ignored).
- **Filler piece** (`Filler`): the end's own wall if plain with no opening; else from its
  first annotated style a side wall (`BuildingStyle.SideWalls`) of its height (`Fits`), else
  blind-window plain walls - never ground-only above `Storey 0` (`Corner` counts as above
  unless the wall is ground-only) nor a different one-per-storey wall - else the wall itself if
  plain. `PiecePlacer.Pick` from its own `System.Random` (seed x 37 + 11): nothing else shifts.
- **Wears its wall's materials** (`WearMaterialsOf`: each swapped for the wall's same-named
  material or name + colour suffix, shortest), so it takes the building colour. Clearance-checked
  (`FacadeBuilder.Clearance`, all streets alike); refused or no fitting wall -> report. Log
  `Gaps: n free wall end(s): a pair(s) bridged, b closed by a return wall, c left for the
  report`. Three-levels: 26 ends, 7 bridged, 12 returns, 0 left.

## Backing (`Backing.cs`, `FacadeBacking.cs`)

- **Black boxes behind every facade**: the buildings have nothing behind their walls, so a gap
  anywhere shows the void and a light behind a facade (a lamp in the next street) reaches its
  pieces from behind; a box closes both. Lots (built whole) get none; dead ends do.
- **Worked out in `FacadeBuilder.Build`** (`CollectBackings`, last, after colours): one per
  stretch of a frontage at one run, building, column, depth, storey count and level
  (`SameBacking`; a column splits at a flight of steps): along the stretch, front on the walls'
  back line (setback - column depth; a dead end's setback from its styles), `backingDepth` back
  - less where a street is nearer behind (`StreetClearance.Reach` every metre, stopping the
  walls' setback short of it) - ends cut short of streets beside it by the same margin
  (`StreetClearance.Clear`); **top at the eaves** (`RoofTop` - `roofEaveDrop`), bottom
  `backingBelow` under the lowest street in front.
- **Placed by `Backing.Build`** after `RunWalls`/`RunRoofs` (gap fillers in, culled pieces
  out): **cut clear of every placed piece** (`TL_*` under the root but `Streets`, LOD0 render
  box in its own space, its reach in the box's frame grown `backingClearance`): per piece
  touched, whichever single cut keeps the most volume - front back past it, back in front of
  it, either end past it, top below it, bottom above it - repeated until clear (overlaps under
  1mm ignored; a box with nothing left is dropped). So it never pokes through a wall, a side
  wall running back, a foundation or a roof.
- `Generated/Backing/Backing <n>`: Unity's built-in cube scaled to the box, `Environment`
  layer, **no collider** (nobody reaches it; no physics query, NavMesh, street test or sound
  probe sees it), static Contribute GI + occluder + occludee (blocks the bake, culls), **lit
  from probes, never batching static**; casts shadows, receives none, no reflection probes.
  Material `Backing.mat` (URP Simple Lit, black, no specular, no reflections): black whatever
  lights it. The report ignores it (not `TL_`). Log `Backing: n box(es) behind the facades (a
  cut back clear of pieces, b left out with nothing clear)`. Three-streets: 94, none left out.

## Building proxies (`BuildingProxies.cs`)

- **Not part of a generation: built by Rebuild Building Proxies** (below) after it and after
  any open window interiors, so they fold in the open windows and leave out the walls those
  stand in place of.
- **Beyond full-detail range each building is one renderer**, not hundreds. Building =
  `Facades|Lots/<owner>/Building <i>` + its `Roofs/<owner>/Building <i>` + **each open window
  standing in it and its room** (`Extras`: the building holding the generated wall nearest the
  window, `OpenWindowInteriors.BuildingOf`; outside the group, held by its `LODGroup`)
  (dressing is under the building; lamp posts, backing, foliage, colliders are not).
- **One `LODGroup` per building on its facade group, switching the whole building at once**:
  level 0 every piece's full detail (and renderers in no `LODGroup`), piece by piece, down to
  `lowerDetailShare` of view height (~25m for a 12m building); level 1 the `Proxy` child down
  to `proxyCullShare`, then culled. Fade copied from the pack's first group. Cull share from
  the pack's (1% of a piece's size) for a building ~4 pieces across; **the switch is further
  off than the pack's own (7%, would be 0.3, ~17m)** so fine baked shadows the proxy's
  coarser shadowmask can't hold (a lamp cage's on a wall: lamps are Mixed, Shadowmask, so a
  static caster's lamp shadow lives only in the receiver's texels) are too small to see go.
  **Pieces' own `LODGroup`s removed, every renderer of theirs but the full detail switched
  off** (a renderer belongs to one `LODGroup`; instance overrides, prefabs untouched). Off or
  shadow-only renderers left as they are, never merged. Particles (smoke, steam) untouched.
- **The pack's lowest level is never drawn**: it has only ~10% fewer triangles than the
  second, not worth a second merged mesh and lightmap.
- **Full detail is never merged**: up close per-piece culling, the pack's own lightmap UVs and
  shared (instanced) meshes matter most, and only a few buildings are in range.
- **Merged mesh**: `Proxy` = every piece's second level (full detail where a piece has one
  level; renderers in no `LODGroup` too), merged in the building group's space
  (`CombineMeshes`, mirrored pieces turned right), one sub-mesh per material, first-met
  order, then **moved onto the sector's atlas** (below): one sub-mesh for everything atlased
  (first), one per material kept apart; exact silhouette, no new shader.
  Pieces' lightmap UVs dropped; **own lightmap UVs unwrapped** (`Unwrapping.
  GenerateSecondaryUVSet`), chart margin 1.5 texels at the mesh's own resolution (UV margin =
  1.5 / (root of surface area x scene's lightmap resolution x scale), uncapped).
- **Unwrapped on a copy welded by position** (vertices within 1mm one): the pack's meshes are
  split at every texture seam, so unwrapped as they are nearly every face is its own chart.
  The copy's triangles come back in the same order; each corner's lightmap UV is carried
  back, a vertex copied where a chart edge runs through it. Still ~1500 charts a building
  (box-like pieces: hard angle and error settings don't lower it). **Lightmap texels per
  chart decide the bake**: below ~15 texels per chart texels go invalid (backface hits) and
  charts share texels (Texel Validity and UV Overlap views red, building dark); hence
  `proxyLightmapScale` 0.6 (~18 per chart, ~30000 texels a building).
- Proxy renderer: `Environment`, static Contribute GI + occludee + reflection probes (never
  occluder, not batching static: one renderer already), casts, receives, **lightmapped**
  (`proxyLightmapScale`; probes off) so far streets show the baked shadows the full detail
  casts in the bake (a lower level receives, doesn't cast into it). No collider (no query,
  NavMesh or sound probe sees it). **Each proxy build needs a rebake.**
- **Own Lightmap Parameters** (`Assets/Data/LevelGen/Proxies/ProxyLightmapParameters.asset`, git-ignored with the proxies, made by
  the build when missing, defaults but pushoff = `proxyBakePushoff`; set on the renderer's
  serialized `m_LightmapParameters`): the bake keeps the full detail as solid geometry a few
  mm from the proxy (only the most detailed level is scene geometry), and its relief blocks
  some of the proxy's bounce and occlusion rays - with Unity's default pushoff the proxy
  baked ~15% darker than its full detail (area-weighted lightmap mean, every building).
  Rays starting beyond that relief let the proxy see what the full detail sees.
- **Proxy atlas** (`ProxyAtlas.cs`; fewer draw calls, shadow pass too): every building split
  first, then from the materials the proxies use (first-met order) **one atlas per sector**,
  three `proxyAtlasSize` PNGs (`<sector> - Proxy Atlas Albedo` sRGB, `Normal` normal map,
  `MRAO` linear: R metallic, G occlusion, A smoothness), the same `proxyAtlasTile` cell per
  material in all three (colour variants get their own), and `<sector> - Proxy Atlas.mat`
  (URP Lit, white, strengths 1, `_NORMALMAP`/`_METALLICSPECGLOSSMAP`/`_OCCLUSIONMAP` on).
  Built before `StartAssetEditing` (imported before the material takes them).
  - **Atlased**: URP Lit, metallic, opaque, no alpha clip, no emission, back culled,
    smoothness from metallic alpha, highlights and reflections on, no detail/height map, base
    map tiling 1 offset 0 (all the pack's opaque materials). Else **kept apart** on its own
    material (foliage, `WindowLit`, `TeaLeaf/*`), as are materials past the atlas's cells.
  - **Tile**: each map shrunk on the GPU to the tile less `proxyAtlasPadding` each side
    (`Graphics.Blit` to a float linear target: sRGB decoded, mip chosen by the GPU), read back;
    **each material's settings baked into the pixels** as URP Lit applies them (colour x
    `_BaseColor`; normal unpacked RG or AG, x `_BumpScale`, packed RGB; metallic map R, its A x
    `_Smoothness`, else the scalars; occlusion G eased to white by `_OcclusionStrength`); edge
    pixels repeated over the padding (holds to mip 4 at 16px). Missing map: white / flat /
    scalars.
  - **A triangle whose texture coordinates leave 0-1** (beyond 0.005, which is clamped)
    stays on its own material: a tile can't repeat (`RoofWall_LargeB` ~7%,
    `PlatformsFoundationsPorch` a few). A vertex serving both kinds is copied.
  - 4096 atlases of 512 tiles: 64 cells, about 50 MB compressed per sector (normal BC5, MRAO
    with alpha).
- Log `Building proxies: n of m building(s), g piece LODGroup(s) folded in; triangles,
  materials per proxy, atlas (materials in it, kept apart, triangles off it), lightmap scale,
  texels, texels per chart, bake pushoff`.
- **TeaLeaf > Level Gen > Rebuild Building Proxies** (selected `Generated` root, else every
  one in the open scene; sector from its `Sectors/<sector>/Geometry` parent): after every
  generation, after open window interiors are built, and after facades are changed by hand.
  Each building put back as placed first (extras too): its `LODGroup` and `Proxy` removed,
  pieces' removed `LODGroup`s reverted from their prefabs, renderer on/off overrides on those
  groups' renderers reverted (only the proxy pass switches piece renderers off); pieces added
  by hand keep their own groups. Then the build, and the root's fingerprint taken again (the
  proxies are no hand edit). Not undoable (old meshes deleted); rebake lighting and occlusion
  after.

## Open window interiors (`OpenWindowInteriors.cs`, `OpenWindowInterior.cs`)

- **Open Window Wall** (`Assets/Prefabs/Environment/Walls/OpenWindowWall.prefab`: two
  `PlasterWalls_A_WindowBase_Hole` back to back, thin, `Window_D_Open` inserts both sides; root
  at the window's middle, +Z to the street) **placed by hand in place of one generated wall
  module, upright facades only (no lean)**, its outer wall's front flush with the generated
  walls' fronts beside it (their depth follows `wallThickness`: a window placed against walls
  of another thickness is moved to match, else it stands behind the facade and the cut misses
  the module's window insert), under the sector's `Geometry` but **never inside
  `Generated`** (a regeneration replaces that whole). Its **`OpenWindowInterior`** holds
  `widthModules` (centred on the window), `depthModules`, and `built` (the room last built,
  replaced when built again).
- **A generation takes out the pieces each window stands in place of** (`CutWalls`, after the
  cull, before `RunWalls`, so no merged collider closes the window): every `wall`,
  `window-wall`, `door-wall`, `window`, `door` under `Facades`/`Lots` whose render middle is
  inside the window's LOD0 box grown 0.3m. **The storey group (`Storey n` / `Corner`) a cut
  piece stood in keeps its pieces' own colliders**: `RunWalls` leaves it out, so no merged box
  spans the window. Log `Open windows: n piece(s) taken out for m open window(s), s storey(s)
  left with their own colliders[; standing in place of nothing: ...]`. The report counts the window's pieces as
  cover (neighbours' ends), never as walls of their own.
- **TeaLeaf > Level Gen > Build Open Window Interiors** (selected windows, else every one in the
  open scene; one Undo step): per window, under `Geometry/Open Window Interiors/<window>
  Interior` (never touched by a regeneration):
  - **Walls**: verified plain `wall`s marked `openWindowInterior` (Review Pieces), one front, a
    `left` point; **those sharing a style with the generated wall nearest the window** (same
    storey, beside it), else those tagged **`default`**, else nothing built (logged). Only the
    commonest height (within `sizeTolerance`); room height = that, walls never height-scaled.
    Laid by the fitting rule (`FitWidths`, normal = median full-size width; per width a wall
    within `wallScaleLimit`, else the nearest), random no repeat, seeded from the window's
    position (rebuilt the same until it moves). **Scaled to `wallThickness` of their
    depth** and to their width.
  - **Box**: module = window box width - `wallOverlap`; room `widthModules` x module across,
    `depthModules` x module deep, from the window's inside face back, floor on the window's
    base. Back plane outside, face inside; back and front walls run on past the side walls'
    backs, side walls past theirs: corners closed, no corner pieces. **Own thin front wall**
    either side of the window, each end `wallOverlap` into it (none at one module wide).
  - **Floor and ceiling**: `openWindowFloorTile` under/over the walls' whole outline, fewest
    whole tiles overlapping by `openWindowTileOverlap`, scaled alike to cover it exactly; floor
    walk surface `openWindowFloorOffset` above the base, ceiling turned over (top facing down),
    underside `openWindowCeilingOffset` above the walls' top.
  - **Colliders: one box per face** (`MergeFace`, `<room>/Colliders/<face>`): back, left,
    right, floor, ceiling; **the front (with the window) keeps its pieces' own colliders**,
    so no merged box stands beside the window. Each merged face's
    pieces lose their box colliders; one box round them all in the room's frame takes their
    place, first piece's layer and static flags, its `SurfaceTag` copied (else Stone walls, Wood
    floor and ceiling).
  - **Sound**: `Sound Room` (`SoundRoom` over the room, priority 2, reverb Room, no ambience)
    and `Portal Window` in it (`openWindowPortalSize`, probes past both window faces). **No
    NavMesh** (guards never go in). Probe light from the level's Adaptive Probe Volume with the
    rest of the level.
  - **Light** (`AddLight`): `Room Light`, a **Baked** point light over the room's middle,
    `openWindowLightBelowCeiling` under the ceiling's underside, `openWindowLightColour`,
    `openWindowLightIntensity`, Indirect Multiplier `openWindowLightIndirect`, soft baked shadows
    (the walls keep it in the room); **range = `openWindowLightRangeShare` x distance to the
    furthest floor corner**, so it just lights the corners (a 9 x 6m room 3m high: 10m). Nothing
    at runtime; no `LightSource` (gameplay ignores it).
  - **Backing** (`RemoveBacking`): every `Generated/Backing` box reaching into the room's outline
    (walls' backs, floor to walls' top) is taken out (whole). If the `Generated` root had no hand
    edits before, its fingerprint is taken again (a regeneration doesn't count the removal as
    one); a regeneration puts the boxes back, the rebuild takes them out again.
  - Log per window: size, height, walls and style, tiles, light range, backing boxes taken
    out; warns if a generated wall still
    stands in the window (regenerate first).
- **Order**: place windows -> generate -> Build Open Window Interiors -> Rebuild Building
  Proxies -> rebake.

## Facade dressing (being built)

- **`FacadeDressing.cs`** (in `FacadeBuilder`, after roofs): walkways, pipes (valves, caps),
  vents (steam), lamp posts; categories `pipe-straight`, `pipe-corner`, `pipe-tee`,
  `pipe-valve`, `pipe-end`, `pipe-vent`, `lamp-post` (`pipe-fitting` catalogued, unplaced).
  Modules record their wall per storey (`ModuleSpot.Walls`) to keep clear of openings; heights
  given to `CrossesOpening` are above the street there.
- **`WallProbe`**: drawn surface along a ray - nearby pieces found by colliders, the ray tested
  against their LOD0 triangles; dressing pieces looked through. (Plaster timbers stand 5-13cm
  proud; colliders don't show it.)
- **Pipes** (`BuildPipes`) - **thin kit only**: `_thin` variants in
  `Assets/Prefabs/Environment/Pipes` (`TL_Pipes_Small_A_thin` root scale 0.5, the 1.5m
  straight; `_Corner_Small_Sml_thin`, `_TJunction_Small_thin`, `_Capped_End_Small_thin` 0.3;
  `_Valve_Small_thin` 0.5; 0.33m across; ends copied from the Small pieces, same sources).
  Small/Large unverified, so `pipeLargeShare` inert; `FacadeDressing.SmallPipe` (0.67m) is the
  diameter compared against. **Straights (`TL_Pipes_Large_A`, `_Small_A`, `_Small_A_thin`)
  collide as one `CapsuleCollider`** along the axis, the radius of the pipe body (not the
  flanges: 0.3 Large, 0.15 Small, before root scale), its round ends standing one radius past
  each end face so it keeps that diameter the whole length; stretched along the axis only, so
  the ends stay round.
  - `pipeBuildingShare` of buildings (rolled per building first) get 1..`pipeRunsMax` runs
    totalling at least `pipeMinRunShare` of width, extra runs at `pipeExtraRunShare`. Runs
    vertical, horizontal or L, level/plumb, never over a door/window (`pipeOpeningMargin`),
    each end into a wall or the ground, following insets/outsets/jetties, never along the
    street floor; lengths by `FitWidths`; a level stretch ending in a wall elbow ends within
    `pipeEndSpread` of module width about its middle (`PipeEndInModule`), never at a wall
    joint; `pipeWallGap` off the face. Ground and dead-end pipes out of the street test lane
    (`ClearOfLane`).
  - **Placement** (from the maintainer's hand-adjusted lot): plumb runs stand off the
    frontmost drawn surface along their height (`PlumbFace`: `WallProbe` every 0.25m from 0.5m
    above foot to straight's top, at axis and both sides, hits within `FaceSlack` 0.25m of the
    storey's wall front; level runs `LevelFace`), clearing posts/beams by 0.15-0.25m;
    `pipePanelInset` from the module's street-left end (on plaster past the end post), else
    over the post at the joint where that would cross an opening (`PlumbPlaces`); top elbow
    under the eaves (`Ceiling`: upward probe less elbow reach and `pipeWallGap`); a wall elbow
    whose leg falls short of the plaster gets a hidden uncounted straight behind it
    (`PlaceWallElbow`, no caps); an L may end in its module at the storey's middle, top or base
    (`CornerRun`).
  - **Placed by ends**: points `a`, `b`, `c`... at each open flange's outer face middle,
    pointing out along the pipe (marked in Review Pieces). Straight `a`->`b`, stretched; elbow
    needs `a`/`b` at right angles, bend where legs' lines cross (`PipeEnds.Bend`), turned by
    `FacadeBuilder.Turn`; tee by `a`, `b` along and `c` branch; valve by `a` into the branch;
    cap by `b`, the flange the pipe meets (`a` = cap face); vent by `b` into the wall. No ends:
    left out, logged. Window shows a help line, **Find pipe ends** (`PipeEnds.Find`: flanges
    where LOD0 meets a render box face, a round vertex ring; right for straights/elbows/tees,
    a guess for caps/valves/vents) and the result (length, legs, bend in yellow). Ends verified.
  - **Valves** (`valveShare` per run): **only on a tee's branch, facing straight out into the
    street**: tee in a straight stretch, branch out, valve on it (scaled with the tee's root,
    being made for the large tee); branch at `valveHeight` on upright stretches, middle on
    level; slides until the pipe either side is none (flange to flange) or at least 0.4m
    (`MinValveStraight`). Never into the lane on the ground storey. Log: placed, runs given
    one, runs with room.
  - **Caps only where a straight enters a wall** (`PierceCap`): level stretches probed from
    both ends; at a drawn surface the matching cap goes round the pipe, **flange `b` flat on
    the wall, cap facing out along the pipe**; a far-end hit within two caps' depth (thin beam)
    gets none. Wall elbows never get caps; no run ends with a straight into a wall, so caps
    only where a stretch crosses a timber or jetty beam.
  - **At least two straights per run**: each run its own group; fewer -> removed whole (valves,
    caps, space). Log counts drops.
  - **Not working yet:** dead-end facade runs stand ~1m off the wall; a corner filler's jetty
    support can reach a street (refused); a thin elbow on a dead-end facade can sit in the
    lane (`ClearOfLane` ignores elbow reach); some buildings get "none fitted".
- **Vents** (`BuildVents`): independent of pipes; up to `ventsMax` per building at `ventShare`
  each, up to 40 random tries: one module's wall within one storey, `pipeOpeningMargin` from
  openings, out of the lane, apart from pipes/vents (colliders), **on smooth wall** (drawn
  surface on a 0.1m grid across the vent within `ventFlatTolerance` depth: never a timber),
  seated on the frontmost point. Most find no place (log: placed of wanted; large rarely fits).
  **Steam** at `ventSteamShare`: `Assets/Prefabs/Effects/Vent Steam` child at the face,
  pointing out (`PlaceEffect`).
- **Smoke/steam prefabs** (`Vent Steam`, `Chimney Smoke`, from Thief VR prologue's smoke):
  motion in a flipbook, not particles. Shuriken, max 5 particles, one 6 x 6 sheet of 36 looping
  frames (`Assets/Art/Textures/SmokeFlipbook.png`, 2048, project's own, `Tools/TextureGen`, painted from looping
  noise to the prologue sheet's coverage and softness; **wrap V Repeat**:
  blending the last frame reaches one row below the sheet, which Repeat maps back onto frame 0,
  where Clamp drew a near-empty smear, a blink once a loop) at 8 fps, URP
  Particles/Unlit, alpha blend, flipbook blending (UV2 + AnimBlend streams), no soft
  particles, camera fading (materials in `Assets/Art/Materials`: `Vent Steam` 0.5->2m,
  `Chimney Smoke` 3->7m). Life 10s, speed 0, dark blue tint (alpha vent 0.11, chimney 0.21),
  grows, fades in over the first fifth, holds to the middle, fades out; **vertical billboard**
  (never tilts seen from above). World space, prewarmed, pause-and-catch-up culling. Vent: 1-2
  quads 1-1.5m, 0.15/s, no shape/forces. Chimney: ~3 quads 5 x 8m, 0.3/s, 0.4m hemisphere above
  the pot, **renderer pivot y 0.4** (the sheet's smoke sits low in each cell: a centred quad hung
  its base inside the chimney; 0.4 puts it at the pot top), gravity -0.25, world wind (2, 0, 1), drag 0.4 by size. Nothing else draws smoke.
- **Walkways** (`BuildWalkways`, `walkwayShare`): the jetty piece along a whole facade at the
  base of one non-jettied upper storey, max one per building, supports' backs at
  `SupportBack` in front of the storey below's back line, `Walkway` group per building.
  Working. **Deck colliders merged** (`MergedColliders.RunWalkways`, with foundations, before
  the street test): flat deck boxes -> one per straight run (`Walkway <n>`, `Generated/Walkway
  Colliders`, foundation tolerance, Wood); tilted brace boxes fail upright and stay.
- **Lamp posts** (`BuildLamps`): at the street side before plain ground walls (never a
  door/window), `lampEdgeInset`, facing the street, sunk `lampSink`. **Gameplay lights**:
  `TL_Lampost` (`Assets/Prefabs/Environment/Iron`) = **Mixed** point (`Point Light`: 10,
  Indirect Multiplier 2, hard shadows, range 5m) + **Baked** spot (`Spot Light`: 0.81, 125°
  down, no shadows: ambience) + `Core.LightSource` (range 5) + `Core.FlameFlicker`, `Flame
  Visual` quad in `TeaLeaf/Flame` (pack `Fire_Effect` particles off); lantern meshes cast no
  shadows. **No `Core.Flame`: can't be put out** (water bolts leave it). `PiecePlacer` keeps
  both lights. **Never two lamps on one spot nor facing across a narrow street**
  (`LampAllowed`): at least `lampSpacing` level distance from every post over every street and
  lights' centres further apart than ranges summed, so the four-mixed-light limit is never
  near. Log `Lamp posts: n (m left out within Xm of another post, or where its light would
  overlap another's)`. **`placeLamps` off: posts worked out as usual (same random draws, so
  building colours don't change) but none placed**; log `Lamp posts: none (Place Lamps is off;
  n would have been placed)`. Two Lots 7; Three Levels 17-19 (least post gap 21.4m, light gap 11.5m).
- Report treats `pipe-*` (vents too), `lamp-post`, `grass`, `nettle` as not covering wall ends.

## Street foliage (`StreetFoliage.cs`)

- Verified `grass` (`Foliage_Grass_A` / `_B`; cluster prefabs uncategorised) and `nettle`
  (`Foliage_Nettle_A` / `_B`). **Own `System.Random`** (`RandomFor`: seed x 31 + 7). Random yaw,
  scale `foliageScaleMin`-`Max`, sunk `foliageSink`; under `Generated/Foliage/Grass - <street>`
  / `Nettles - <street>`.
- **Grass** (`StreetGround.LayArea`): area dealt into `grassPatch` squares; each whose middle is
  inside gets, at `grassCover`, `grassClusterMin`-`Max` tufts within `grassClusterRadius` of a
  random point in open street (`InOpenStreet`: `grassEdgeMargin` inside the outline, outside
  lots, off flights; slopes fine; ~1/3 pass); none on decking. Asset `grassCover` 0.
- **Nettles** (`BuildNettles`, after merged colliders - ground found by rays): each foundation
  / foundation-corner gives a front (render face at top edge, per annotated front), assigned
  to the street it looks onto (`Faces`: within half its width + `columnDepthMax` +
  `foundationFrontOffset` + `lotGroundMargin` + `overlapTolerance`, facing its middle line, or
  across a dead end); per street `nettlesPerStreetMin`-`Max` spots by length, `nettleOut` out,
  `nettleSpacing` apart, `nettleDoorClear` from door openings; ground by a downward ray
  ignoring the foundation and merged boxes, accepted up to 0.15m above foundation top -
  `foundationHeight`. Log `Nettles: n on m street(s), from k foundation front(s)`.

## Moon shafts (`MoonShafts.cs`)

- **One `Core.GodRayBox` per building casting a moon shadow onto a street**, beside that shadow:
  bottom four corners on the street round the moonlit strip, top four straight up the
  moonlight from each to the building's top + `moonShaftTopClearance` (sides run the way the
  light comes; the side nearest the building grazes the edge casting the shadow). `Build` after
  the merged wall/roof colliders, `Generated/Moon Shafts`, `Moon Shaft - <street> <from>-<to> -
  <owner> Building <i>`; same component as a hand-placed shaft (corners adjustable by hand,
  replaced by a regeneration); `Environment`, Occludee static only; only component (eight
  local corners) and transform saved (fingerprinted), mesh built on enable. Object on the
  street in the strip's middle, +Z across the street the way the light travels.
- **Shadow found by physics rays** towards the moon (Environment + Interactable, as gameplay's
  light rays; after `Physics.SyncTransforms()`; start `Clearance` 0.1m above `StreetHeights.
  SurfaceAt`). Streets of base width <= `moonShaftStreetMaxWidth`; each segment less half the
  widest street at each end (`JunctionWidth`: junctions belong to no segment) cut into even
  samples about `moonShaftSampleSpacing` long; per sample a line across, `moonShaftInset` in
  from both edges, from the edge on the moon's side (`Measure`): that edge lit = no shadow
  (nothing); eight points across, then six halvings, find where the moon-side shadow ends
  (strip start) and where a far-side shadow begins (strip end, else the far edge); none lit =
  wholly in shadow (nothing). **Caster** = the building whose box (its `Facades`/`Lots`
  group's renderers plus its `Roofs` group's, particles left out; grown 0.2m; smallest where
  several) holds the hit of the ray from just inside the shadow's edge (`BuildingAt`); none =
  skipped.
- **Consecutive samples with one caster = one shaft** (`PlaceShaft`): run start = first
  sample's strip at the start of its share, run end = last sample's at the end of its share
  (four corners: start near, end near, end far, start far). The shadow's edge wanders along a
  building (gables, jetties, roof ends), so a corner can sit a little inside the shadow.
  Moon less than `MinThrough` 0.2 above level: none. Log `Moon shafts: n beside m building(s)
  casting a moon shadow onto s street segment(s); of k sample(s) across, a lit at the moon's
  side (no shadow), b wholly in shadow, c shaded by something not a building`. Three-streets
  (moon ~75° up): 45 shafts beside 35 buildings, 11 segments, 227 samples (3 lit, 39 dark).

## Sound rooms (`SoundRooms.cs`)

- `Build` after moon shafts, before NavMesh: `Core.SoundRoom`s / `Core.SoundPortal`s under
  `Generated/Sound Rooms` so sound goes round corners, never through buildings.
- **Street room per straight run** (segments within the branch angle of the run's first,
  `InLine`: height changes and slight bends never self-muffle). Box `<street> <from>-<to>`
  (points from 1) along the run's end-to-end line, width = widest segment + points' offsets each
  side + `EdgeMargin` 0.5m into walls; from `Below` 1m under the lowest surface to the eaves
  (lowest wall top along either edge - `MoonShafts.WallTop`, 2m samples, within
  `edgeWallProbe` - minus `roofEaveDrop`, at least `soundRoomMinHeight` above the highest
  surface). Runs `DeadEndReach` 1m past a dead end into its facade.
- **Junction room** where 2+ runs meet (`Junction <street> <point> / ...`), **priority 1**
  (`SoundRoom.priority`, beats street rooms): box turned to its widest street, holding the
  point and, between each pair of neighbouring ways out, where their facing edges cross
  (`Corners`; near-parallel edges give both edge points; a far sharp crossing drawn in to
  `CornerReachLimit` = 3 x widest half width), + `EdgeMargin`; from `Below` under the point to
  the lowest of its streets' room tops, at least `soundRoomMinHeight` above the point.
- **Portal per way out of a junction box** (`Portal <run>`): on the face its middle line
  crosses, centred there, the street's width along the face (max the face), full height, front
  into the street, muffle `soundStreetMuffle`. A street room ending at a junction stops where
  its street leaves the box (near corner on the face, slanted), never reaching round the
  corner; a run through a junction keeps its box.
- **Sky portals** (`Sky <n>`, under the street room) over each stretch no junction box covers
  (min `MinSkyLength` 1m), front up to the rooftops (outside every room), `soundSkyMuffle`:
  rooftops hear streets; two streets hear each other over the roofs, muffled twice. Junctions
  get none.
- **Reverb** `Alley` if spec width (own or `streetWidth`) <= `soundAlleyMaxWidth`, else
  `Outdoors`; a junction is Alley only if all its streets are. **Ambience**
  `soundStreetAmbience` in every room, volume not overridden (the Sound Player's Ambience
  Volume sets every street's).
- **Self-check**: each portal's probes (`ProbeRooms`) must find the two rooms meant; every
  street point (1m grid, 0.3m in from edges, at 0.2m and 1.6m up) must be in a room. Log
  `Sound rooms: n street room(s), m junction room(s), p portal(s) (s to the sky); a portal(s)
  not joining the rooms meant, b of c street point(s) in no room.` + up to 8 problems per kind.
  Three-streets: 3 street, 2 junction, 11 portals (5 sky), none wrong, all 3802 points in.
- **Known:** room tops are level, so a climbing run's room is set by its top and towers over
  the foot's eaves (three-streets' middle street: one 15m room over an 11m climb). Building
  doors/windows' sound placed by hand with the doors.

## NavMesh (`StreetNavMesh.cs`)

- `Build` after sound rooms, before the report. A `NavMeshSurface` (AI Navigation) on the
  `Generated` root collects `Environment` physics colliders **under the root only**
  (`CollectObjects.Children`: ground chunks, top-step boxes, decking boxes, ramps,
  foundations, walls, roofs; nothing hand-placed), default agent (guard: radius 0.3, height
  1.8, step 0.4, slope 45°), all Walkable; **Not Walkable `NavMeshModifierVolume`s carve away
  all non-street** (`Generated/NavMesh Volumes`, on `Environment` - **a volume off the
  surface's layer mask is ignored**). **Must be this way round**: Unity turns Not Walkable
  geometry into nothing no Walkable volume restores (Not Walkable default + Walkable volumes
  bakes empty).
- **Foundations are Not Walkable**: a `NavMeshModifier` (override area Not Walkable) on
  `Generated/Foundation Colliders`, the group put on `Environment` (a modifier off the mask is
  ignored, like a volume). A foundation's top is lower than the agent's step, so as floor the
  bake would run the NavMesh onto any foundation standing out past the edge boxes (a column
  brought forward, its side foundation) up to the wall; as an obstacle the street ends at every
  foundation's front, the agent radius in from it (0.25-0.35m measured).
- Per segment:
  - **Edge boxes** (`<street> <from>-<to> left` / `right`) from the **band's back**
    (`columnDepthMax` past the edge, as deep as a column is set back) out to `EdgeReach` 6m
    (beyond `groundEdgeMargin` + a cell, a lot ring's `lotGroundMargin` more), 1m under the
    lowest surface to 30m over the highest. **Ends** (`CornerReach`, per box's inset and depth)
    where its edge meets the next street's edge round the point that side (`OthersAt`):
    **short of the point inside a corner**, the box behind the band at the other street's
    band back (the band is that street's to fit), at the nearer of the crossings on the box's
    near and far faces (the box is square to its street: at a corner off 90° the near crossing
    alone would put its far corner up to ~1m into the other street; the wedge left lies in the
    other street's own boxes); **past it outside**, by the reach (covers the corner building's
    floor); **at the point** where the next street runs straight on within the branch angle
    (`BranchDot`; a width step leaves a sliver the agent radius eats); **by the reach at a
    dead end**. At a T all corners are inside (boxes stop at the branch's mouths).
    **Back-to-back street edges nearer than the reach carve each other's floor**: specs keep
    them apart.
  - **Band** (`LayBand`, `... band <n>`), edge to band back, fitted to the foundations every
    0.25m (`BandStart`: from the band's back, 0.15m over the street surface, a ray in towards
    the street, `columnDepthMax` past the edge at most, never past the middle): the band's back
    **inside a foundation** (a column set back) - recess left open; **a foundation's back
    hit** - carved from there out (floor behind an ordinary front, and the pocket behind a
    column brought forward, carved from that foundation's back in the street); **none** (a gap
    between buildings) - carved from the edge. Neighbouring stretches starting within 2cm
    merge. **The open part in front** gets a box from `streetHeadroom` above the stretch's
    lowest surface up (`... band <n> over`): walkway decks, jetties and ledges over a recess
    carve as over the street.
  - **Dead-end box** (`... start` / `end`): band back to reach past the end, width + reach each
    side; its band (`... start band <n>`) across the end likewise.
  - **Over boxes** (`... over`): `streetHeadroom` above the surface to 30m, full width, past a
    shared point by the widest half width, so overhanging walkway/jetty decks are no islands.
    Height-changing segments get a chain of level boxes (2m stretches, surface every 0.25m via
    `StreetHeights.SurfaceAt`, equal neighbours merged), each from headroom above its lowest
    surface, never reaching a flight/slope.
- Low clutter in the street (pipe top, lamp) erodes away under the agent radius; baked edge
  ~0.3m in from foundation fronts (set back or not) and facades; a recess's side foundations,
  drainpipes and side walls keep it the radius off too; no inset of its own.
- **Voxel 0.05m** (`VoxelSize`; default 0.1m bakes slivers up to 0.6m off a flight's treads,
  walked by guards). **`buildHeightMesh` on**; together the surface keeps within a few cm.
- After `Physics.SyncTransforms()` bakes; saved as `Assets/Scenes/<scene>/NavMesh -
  <sector>.asset` (folder made; unsaved scene: logged, data unsaved). The surface's Bake button
  gives the same (re-bake after hand dressing under `Generated`); regeneration replaces
  surface, volumes, asset. Log `NavMesh: n volume(s) carving round m segment(s); baked X x Zm,
  saved to ...`. No doors; a hand-built sector uses its own scene-root surface
  (`Assets/Scripts/AI/CLAUDE.md`). Three-streets: 1314m², one region, all streets and recesses walkable.
- **Checking a bake** (`unity command eval_file`): `NavMesh.CalculateTriangulation()` grouped
  by shared vertices = islands; `NavMesh.SamplePosition` at every street point and
  `NavMesh.CalculatePath` between neighbouring middle-line samples find breaks.

## Catalogue scanner (stage 1)

- **`PrefabCatalogueScanner`** (**TeaLeaf > Level Gen > Scan Prefab Catalogue**): every prefab
  under `Assets/Prefabs/Environment` (`ScanRoot`; gameplay prefabs never) except
  `Building_Exteriors`, `Building_Layers` (`_excludedFolders`), by path, via
  `PrefabUtility.LoadPrefabContents` (preview scene), unloaded unsaved; file rewritten only if
  text changed. No date, sizes rounded to mm (rescans stable); `JsonUtility` prints floats in
  full, so numbers are cut to 3 decimals (`ShortenNumbers`). One Console entry: counts per
  flag, orientation, folder.
- **`PrefabCatalogue`**: `PrefabCatalogue`, `CatalogueEntry`, `NamedCount`, `LightInfo`;
  `JsonUtility` (no dictionaries: lists). `format` bumped on layout change.
- **Entry** (root space, pivot origin): name, path, GUID, folder, source prefab; **render
  box** (enabled LOD0 renderers on active objects + renderers in no LODGroup), size,
  `pivotInBounds` (0-1/axis); **collider box** (enabled non-trigger colliders from their own
  shapes - `Collider.bounds` isn't updated in a preview scene); root rotation/scale; object
  count, depth, top-level children, outermost nested prefabs; component counts, gameplay
  scripts; LODs, LOD0 renderers, triangles, materials; collider kinds, triggers; every Light
  (path, type, bake mode, shadows, range).
- **Flags** (reasons to look): `not-a-variant`, `no-render-bounds`, `no-colliders`,
  `has-lights`, `rotated-pieces` (renderer > ~2.5° off root axes), `uneven-scale`, `mirrored`,
  `pivot-not-on-base` (box bottom > 5cm off pivot), `pivot-outside-footprint`,
  `off-environment-layer`, `not-static`, `missing-mesh`, `missing-script`, `shares-source`,
  `duplicate-name` (" 1" suffix), `has-gameplay-scripts` (any MonoBehaviour but `SurfaceTag`,
  `RampCollider`, `BarrelCollider`), `assembly` (nested prefabs), `front-unsure`,
  `no-mesh-data`, `front-not-plus-z`.
- **Front from geometry, never the name** (**`WallOrientation`**), confirmed in stage 2. Pack
  walls: detailed front, one flat back plane. LOD0 triangles to root space (normal from
  winding, flipped under mirroring), filed by facing (+X, -X, +Z, -Z within ~45°) with area and
  depth. A side's **plane** = depth with most area (1cm bins + two neighbours); **planarity** =
  that share.
  - **wall** — slab (thin level side <= 0.6 of the other and <= half the larger of that and the
    height) whose sparser big side (back) is >= 70% one plane, >= 0.2 flatter than the front,
    confidence >= 0.3; confidence = clamp01((front tris / back tris - 1) / 9) x back planarity;
    busier side = front (`suggestedFronts`). Open back: no confidence.
  - **corner** — not a slab, >= 1/4 as tall as its larger level side, on both level axes front
    >= 1.5x back's triangles with back >= 40% one plane: two fronts, lower confidence.
  - **unsure**, **not-a-slab**, **no-mesh-data**.
  - Walls: **back plane box** (`backPlaneMin`/`Max`/`Size`, without trim/beams) = true module
    size and gives points `left`, `right` (base of ends seen from front; right = up x -front),
    `top`, `bottom` (edge middles), each with way out. `sides` keeps per-side counts, area,
    planarity, depth for tuning `SlabRatio`, `SlabDepthRatio`, `FacingDot`, `PlaneTolerance`,
    `FlatBack`, `FlatterBy`, `CornerRatio`, `CornerFlatBack`, `CornerMinHeight`,
    `FullConfidenceRatio`, `MinConfidence` by replay, no rescan.
  - `Mesh.AcquireReadOnlyMeshData` (`WallOrientation.ReadTriangles`, rewinds under mirroring);
    Read/Write import settings untouched. `Suggest` returns the four sides.
- **Walking surface from colliders** (**`WalkSurface`**): floors laid so collider tops form one
  surface. Box faces and mesh collider triangles (not a `RampCollider`'s own) filed by facing;
  upward: modal height (1cm) = `walkHeight`, share `walkShare`, box `walkMin`/`walkMax`. Each
  `RampCollider` -> `ramps` entry (via `SerializedObject`): `foot`, `top`, level `direction`,
  `width`.
- **`PieceShape`** (after colliders): +Z is the front where geometry gives none
  (`frontAssumed`). First matching `suggestedCategory`:
  - `steps` — has a `RampCollider`; points `foot`, `top`.
  - `roof` — in `Roofs` / `RoofWalls` (+Z, no points).
  - `detail` — in `Windows`, `Doors`, `Iron` (never a module).
  - `column` — <= 0.8m across and >= twice that tall (tested before corners).
  - `door-wall` / `window-wall` / `wall` by **openings**: back plane triangles on a 5cm grid;
    connected empty runs touching sides/top = outline, only the base = `door` (>= 0.6 x 1.5m),
    inside = `window` (>= 0.3 x 0.3m); `openings` = flat boxes; `door` / `window` point
    (numbered after the first) at centre, mid-depth, facing out.
  - `outer-corner` / `inner-corner` — inner if both backs on the box's edge (within 10%).
    Points `end` + arm front (e.g. `end+Z`) at each arm's free end base on its back plane,
    along the arm; `top`, `bottom` where backs meet.
  - `floor` / `ground` (Ground folder) — <= 0.35 as tall as shorter level side, and (floor)
    walking surface >= 90% of upward collider area; points `+X`, `-X`, `+Z`, `-Z` at edge
    middles. No front.
  - else `beam` (long side >= 4x height and short side) or none; +Z assumed, points on the -Z
    plane (as back plane) if >= 60% one plane, else render box -Z face.
  - **`front-not-plus-z`**: measured fronts lack +Z; or (none, not floor/steps) slab along Z (X
    <= 0.6 Z); or -Z has 1.5x +Z's triangles with +Z >= 0.2 flatter.

## Annotations and verification (stage 2)

- **`PieceAnnotations`** (`PieceAnnotations`, `PieceAnnotation`; points reuse
  `ConnectionPoint`): `JsonUtility` + `ShortenNumbers`, by path, rewritten only if changed.
  Format 2 (other formats left alone; format 1 read and rewritten as 2).
- **Fields** (root space): `guid` (key; `name`, `path` kept in step for reading); `category`:
  `wall`, `door-wall`, `window-wall`, `outer-corner`, `inner-corner`, `foundation`,
  `foundation-corner`, `floor`, `ground`, `steps`, `column`, `beam`, `roof` (scanner
  suggestion, unused), `detail`, `window` (insert; suggested for `Windows` details), `jetty`,
  `door` (`Door_RectangleShort`, `_RectangleTall`, to review), `roof-end`, `roof-end-half`,
  `roof-ridge-end`, `roof-ridge-end-half`, `roof-middle`, `roof-gable`, `roof-hip-base`,
  `roof-hip-middle`, `roof-hip-top`, `roof-chimney`, `roof-dormer`, `roof-railing`, `pipe-*`,
  `lamp-post`, `grass`, `nettle` (empty = unset); `fronts` (1 wall, 2 corner, 0 floor/steps);
  `width` (along right seen from front), `height`, `depth` (X/Z with no front); `connections`
  (name, position, way out; hand-set `window*` = blind-window insert spot); `styles`
  (normalised: trimmed, lower case, hyphens); toggles `groundStoreyOnly` (e.g.
  `WallTall_StoneWindowBase`), `showsOpening` (plain wall showing a window/door - blind window
  walls, `DoorOrnate_Half*`, `WallDoorArch_Left` / `_Rright` - never a side wall),
  `fillsStorey`, `onePerStorey`, `lit` (`TL_Window_*_Lit`), `openWindowInterior` (plain wall
  the open window interiors build from; still needs `verified`, and any facade style it carries
  puts it on facades too); `excluded`, `verified`, `note`.
- **Suggestion** (`PieceAnnotation.Suggest`): catalogue category, fronts, points; size from
  back plane (wall), walk surface (floor/ground), ramp width/rise/run (steps), else render box;
  `excluded` pre-ticked for `has-gameplay-scripts` / `assembly`. **Re-measure for this front**
  (`SuggestSize`, `SuggestConnections`) redoes size and left/right/top/bottom for a hand-set
  front.
- **`PieceReviewWindow`** (**TeaLeaf > Level Gen > Review Pieces**): list filtered by folder,
  orientation, flag, style, status, name (✓ verified, ✗ excluded, · looked at). Selection puts
  a plain `Instantiate` copy at (0, 300, 0), `DontSave | NotEditable`, lights off, framed;
  destroyed on reselect, close, reload or play (never saved). Handles (editor window, so not
  `IDebugDrawable`): pivot axes, render box white, collider box green, back plane cyan, walk
  surface pale green, openings orange, ramps outline, fronts yellow arrows, points magenta
  (named, way-out line); chosen point gets a move handle. Right: measurements, fields, a
  toggle per style in use plus an add-style field (Enter). Suggestions stored only once
  changed; every change saved at once (dragged point on release). **Enter** accepts
  (verified, not excluded; needs a category) and advances; **Exclude and next**; **up/down**
  move; **Reset to suggestion**.

## Layout spec, styles, validation (stage 3)

- **`LayoutSpec`** (`StreetSpec`, `FrontageSpec`, `BuildingSpec`, `LotSpec`), `JsonUtility`,
  format 2, world metres, yaw = degrees clockwise from +Z (N). `seed`, `sector`;
  - `streets`: `id`, `style`, `width` (0 = `streetWidth`; varied per segment), `points` (y =
    ground height; streets meet only at shared points, always level there; **a height change
    within a segment climbs by slope if gentle enough, else steps**), `frontages`;
  - frontage: `side` (`left`/`right` from the first point), `from`, `to` (point indices; runs
    past bends, ends only where a street branches on its side or ends), `buildings` from
    `from`;
  - building: `widthModules`, `storeys` (styles ground up, **last repeats**, extras past the
    `StoreyPlan` count dropped), `doorModule` (from the left seen from the street, 0-based; -1
    none). Length = modules x the styles' spacing.
  - `lots`: `id`, `origin` (footprint middle at ground floor = street level +
    `groundStoreyHeight`; ground ring laid at `origin.y` minus that), `yaw` (front),
    `widthModules`, `depthModules`, `storeys`, `doorModule`, optional `outline` (world x, z
    corners, 3+, in order, convex; first side = front; replaces modules and yaw;
    `LotSpec.Corners`, `Bounds`).
  - **No outline nor modules: the lot takes the block its streets enclose** round `origin`
    (`LayoutSpec.ResolveLots`, run by the validator right after widths): `BlockOutline` casts
    720 rays, each taking the nearest segment's near edge it crosses (streets bending away or
    side streets off the far side never count); convex region on the origin's side of those
    edges (half-planes clipped from a big square), collinear sides merged, sides < 1m dropped.
    One face per segment (straight runs = one face); front = side facing nearest `yaw`. Error
    if origin on a street or streets don't close round it. Skips the street overlap test.
- **Widths per segment** (`LayoutSpec.ResolveWidths`, the first random draws, made by the
  validator so it checks what is built; generator continues the same `System.Random`): street
  width + random up to ±`streetWidthVariance`, min `minStreetWidth`, one draw per segment in
  spec order. Read via `StreetSpec.SegmentWidth(i)` (point i to i+1) and `WidthAt(point)` (the
  wider there).
- **A storey uses one wall family** (by folder and name; families' corner pieces tagged but
  unused). **`StyleRules`** suggests styles by name (first prefix match after `TL_`), stored on
  accept:
  - `plaster`: `PlasterWalls_A_Cross`, `_A_WindowBase*`, `_A_Zbar`, `_B_WindiwRoof`,
    `PlasterWall_B_LowCurves`; `plaster-planks`: `PlasterWalls_A_Planks*`, `_B_PlanksCorner`;
    `plaster-masonry`: `PlasterWalls_B_MasonaryWall*`, `Masonary_WallDoor*`, `_DoorOrnate*`
    (no separate masonry door style); `PlasterWalls_B_Corner` in all three plaster styles;
  - `wall-tall`: `WallTall_*`, `WallsTall_CornerNarrow`; `masonry-cellar`: `Masonary_Cellar*`,
    `_ShortSideWall*` (~1.74m); `Masonary_WallCorner*` in cellar and plaster-masonry;
    `masonry-woodlarge`: `Masonary_WoodLarge*`; `Masonary_Foundations_*`: every building style;
  - `cobbles`: `StoneFloor_*` (A-E, Tile) - names the style only (ground is the generated
    mesh); `wood-floor` (decking for wooden streets, walkways, bridges): `Platforms_Planks*`,
    `Platforms_LoosePlank*`, `Platforms_BoardPlanks`, `Platforms_ShortBoardPlanks`,
    `Fixtures_WoodenPlatform*`; `_Supports` pieces no style.
  - **`masonry-cellar`, `plaster-masonry`, `plaster-planks` are ground storey only**
    (`StyleRules.GroundStoreyOnly`); woodlarge any storey. `PlasterWalls_A_MidBeam` = the
    plaster styles' `beam`. Other pieces outside `Walls`/`Masonry`: no style.
    **`PlasterWalls_A_MidColumn` is loose trim for covering gaps by hand**: never generated.
- **`BuildingStyle`**: per style, usable pieces by category (verified, not excluded, still
  catalogued, category set) and tagged-but-unusable ones. **Module** = median width and height
  (lower middle if even) of full-size (>= `fullSizeShare` of widest) usable `wall`,
  `window-wall`, `door-wall`. Style with `floor` pieces = **decked**, else **ground** (generated
  mesh) (`TileCategory`); **tile size** = median of full-size floors (both sides >=
  `fullSizeShare` of the biggest); only tiles within `sizeTolerance` laid.
- **`LayoutSpecValidator`** (**TeaLeaf > Level Gen > Validate Layout Spec**, selected specs):
  one Console entry per spec (error if unbuildable, warning if warnings); `ValidateFile` /
  `Validate` return the `Report`.
  - **Errors:** unreadable/other format; no sector; missing/repeated id; negative width, < 2
    points, segment < 0.1m; frontage side not left/right, stretch outside the points,
    overlapping another, no buildings, or a street branching on its side part-way; branch
    > `branchSquareTolerance` off square (`CheckBranchAngles`); building/lot < 1 module, no
    storeys, door off its front; street/storey naming no style or one no piece carries; storey
    style without a fitting `wall`; ground-only style above ground, or listed last when
    `maxStoreys` exceeds the list; ground style without `foundation`/`foundation-corner`, or
    without a fitting `door-wall` when there is a door; storey modules > `maxStoreyWidthMismatch`
    off the ground's; buildings > 5cm longer than the stretch; a rise (> `sizeTolerance`) on a
    decked street, or too steep to slope and not a whole number of flights of a style `steps`
    piece, or too short for them at its ramp run (`CheckRises`); a lot within
    `lotGroundMargin` + `groundEdgeMargin` of a sloping segment (`CheckLotsBesideSlopes`: the
    lot ring is one level; steps fine).
  - **Warnings:** > a module left at a frontage's end; a slope building whose street falls >
    `slopeDropMax` below it (`CheckSlopeDrops`, rough positions); a lot when `maxStoreys` > 1
    and `wood-floor` has no usable `floor`; per style: tagged pieces unusable, walls whose
    width misses the module (not modules; not warned for a no-opening plain wall whose height
    fits - a side wall) or height misses (unused), per `BuildingStyle.Fits`, tiles off size;
    lots overlapping each other or a segment > 5cm (oriented rectangles, SAT); sharp corners
    more than one off `sharpCornerShare` (`CheckSharpCorners`: every wedge between neighbouring
    streets at a point, bend insides included, within branch angle of straight = none, sharp <
    `sharpCornerAngle`).
  - **Stretch** (rough): middle line between its points less `BranchCut` at each end where a
    street branches on its side (sharing the point within 10cm, leaving >= ~17° toward it):
    where the edge and the walls' back line cross the branch's near edge (half the branch width
    at right angles, more when sharp), wall reach = deepest usable wall of its styles
    (`WallDepth`). True edge length is stage 5's; the report checks what was built.

## Generation and the streets' ground (stage 4)

- **`LevelGenerator`** (**TeaLeaf > Level Gen > Generate Level**, selected spec): validates,
  logs, builds into the open scene under `Environment/Sectors/<sector>/Geometry/Generated`
  (made if missing). **`GeneratedRoot`** records spec path and fingerprint (hash of each
  object's path, active state, local transform to mm / 0.01°, prefab). Regeneration replaces
  `Generated` whole in its sibling slot; asks first if the fingerprint changed (hand edits) or
  the spec differs; refuses a `Generated` without `GeneratedRoot`. One `System.Random` (from
  validation, past widths); one Undo step.
- **Heights found once** (`StreetHeights.Find`: flights `StreetSteps`, slopes `StreetSlopes`)
  and shared by ground, facades (and clearance), street test, cull: `SurfaceAt` (as walked,
  up flights/slopes), `GroundAt` (mesh height, level either side of a flight).
- Unlayable street (decked style without tiles, too few points): skipped, logged. One Console
  entry lists what was built. `Generated` holds `Streets`, `Facades`, `Lots`, `Roofs`,
  `Foliage`, `Foundation Colliders`, `Walkway Colliders`, `Wall Colliders`, `Roof Colliders`,
  `Backing`, `Moon Shafts`, `Sound Rooms`, `NavMesh Volumes`, and the `NavMeshSurface`; once
  the proxies are built each building group holds its `LODGroup` and `Proxy`.
- **Pipeline:** validate (widths, `ResolveLots`) -> `StoreyPlan` -> `StreetGround` ->
  `FacadeBuilder.Build` -> `StreetGround.Paint` -> `MergedColliders` foundations, walkways -> nettles -> `GapFiller` ->
  `StreetTest` (own entry, error if invalid; checks merged boxes) -> `VisibilityCull` ->
  `OpenWindowInteriors.CutWalls` -> `RoofEdgeColliders` -> `RunWalls`, `RunRoofs` (after
  pieces/colliders are removed) -> `Backing` (cut clear of the pieces left; no colliders) ->
  `MoonShafts` (rays need every collider; the cull never sees them) -> `SoundRooms` (tops from
  walls left) -> `StreetNavMesh` (bakes from colliders) -> `ShearedMeshes.ClearUnused` (level
  complete) -> `GeneratedReport` -> fingerprint.
  Then by hand: Build Open Window Interiors, Rebuild Building Proxies.
- **`StoreyPlan`** (rolls next after widths; logged): **facade** = min + range x rand^(4^-
  `storeyBias`) (+1 high, -1 low). **Lot matches neighbours**: facades with an edge point
  (sampled per metre, length from ground modules) within `lotNeighbourDistance` of the
  footprint give an average height in metres (storey spacings + top overlap), converted into
  the lot's own storeys (fractional) + `lotStoreyBias`, rounded, clamped; none near or unknown
  -> rolled like a facade. `StyleOf` = a storey's style (last repeats). Also makes
  `DeadEnds`.
- **`PiecePlacer`**: prefab instance, `Light`s disabled on the instance (one with a
  `LightSource` beside it kept), scaled, then (while `Clearance` set) checked against streets
  and destroyed if in one. `PlaceAt`: yaw + put a point of its own space on a world point;
  `Pick`: random, never the previous piece. Decking tiles placed by **walk surface** (walk box
  middle at `walkHeight`, else render top) so collider tops meet.
- **`StreetGround`** (spec order, `Generated/Streets`):
  - **Ground (style without `floor` - cobbles): generated meshes, one grid per area** (streets
    of the style joined at shared points), `Ground - <first street>`. **Grid never squared to
    world axes**: from the first point, randomly turned (always drawn) >= `groundGridMinAngle`
    off X and Z, cells `groundCell`. Outline = segment rectangles (own width) + discs (half the
    wider segment) at bends and ends shared within the area + **each lot's ring** (lot whose
    footprint middle is nearest one of the area's segments): footprint (`LotSpec.Corners`) out
    to `lotGroundMargin` and 0.5m in under its walls (`LotGround`, `GroundLot`), **never under
    the building**. Cell kept if a corner or its middle is inside the outline grown by
    `groundEdgeMargin` (`Inside`): ground runs under facades, shows at branch gaps and dead
    ends. **Vertices at the street surface** (`HeightAt`: lot's level in its ring, else
    `StreetHeights.GroundAt` of the nearest segment).
  - **One mesh per level and per slope** (`GroundMesh`, `RegionOf`, `CutsLevel`,
    `PlaceVertex`): cells sorted by nearest segment; sloping segments' cells = own region;
    level cells joined per connected level (`GroundMesh.JoinLevels`, 4-connected), never across
    a flight (neighbours on opposite sides of the flight's middle kept apart). **No cell wholly
    under a flight** (dropped); vertices moved along the segment to `groundStepsOverlap` inside
    the flight's front (low side) or top (high side): lower mesh runs on under the lowest step,
    upper starts under the top step's tread sunk `groundThickness` (`HeightAt`, under the steps
    only), so no slit shows under the steps' rounded edges. Levels < 6 cells join a neighbour. Regions `Ground slope <street>
    <from>-<to>` / `Ground level <height>m` hold `Chunk <x>_<z>` (32 cells, 24m): mesh asset in
    `Ground/`, `MeshFilter`/`MeshRenderer` on `Environment`, static (GI, occluder, occludee,
    batching, reflection probes), casts no shadow, receives, lightmapped once baked (probe-lit
    before), `Assets/Art/Materials/Environment/Ground/StreetGround.mat` (`TeaLeaf/GroundBlend`,
    `Assets/Scripts/Core/CLAUDE.md`). Normals from the whole grid (per axis the gentler
    one-sided difference, so a flight doesn't tilt neighbours); lightmap UVs planar per chunk:
    seamless. Three-streets: 13 regions (5 slopes, 8 levels), 38 chunks, 5348 verts, 8722 tris.
  - **Paint** (`Paint`; shader height-blends): red = cobbles, blue = wet dirt, none = dirt.
    **Painted after `FacadeBuilder.Build`**, never before: `Build` lays every vertex plain dirt
    and returns the meshes and outlines (`Painting`); `Paint` colours and saves them. **Dirt
    border of even width out from the foundations actually placed**: `groundDirtBorder` from
    the nearest foundation footprint (`MergedColliders.Foundations`; per mesh the rectangle
    round its bounds along its own right/forward, `Footprint`), so it follows columns stepped
    forward or back, side-wall step foundations and lots; cobbles beyond. Where no foundation
    stands (gap, open street end) the street edge stands in, set back `columnDepthMax` past
    the foundation front line (outline `Depth` - `foundationFrontOffset` + `columnDepthMax`; the
    nearer of the two wins, so a recess never paints all dirt). Border's inner edge wobbled by
    up to `groundPaintWobble` with value noise of `groundPaintScale` (`GroundMesh.Noise`, 2
    octaves, integer-hashed from the seed: machine-independent), fading over `groundPaintFade`.
    Only footprints within border + wobble + fade of a chunk are tested for its vertices.
    Log `Ground paint: n vertices in m chunk(s), dirt border from k foundation footprint(s)`.
    Puddles where a second noise of `groundPuddleScale` is in its top `groundPuddleCover`,
    anywhere.
  - **Colliders: each chunk a `MeshCollider` of its own mesh** + Stone `SurfaceTag`. **Don't
    go back to level and tilted boxes**: a level box where a slope meets it leaves a lip up to
    0.2m the body and NavMesh climb. Only box left (`LayColliders`, `Ground -
    <street>/Colliders`, `Ground <segment> top step`): under each flight's top step,
    `groundStepsOverlap` back from the top step's back edge under its tread (the ramp collider
    stops 0.28m short), `groundThickness` deep, top at the upper level, across the street +
    `groundEdgeMargin` each side, `Environment`, static, Stone. Steps keep `RampCollider`s.
    Three-streets: 38 chunks, 6 boxes.
  - **Slopes** (`StreetSlopes`, shared with validator/facades): **a segment rising >
    `sizeTolerance` slopes if grade <= `streetSlopeMaxGrade`, else steps**; ground streets only;
    whole segment foot to top; mesh follows `SurfaceAt`.
  - **Steps** (`StreetSteps`, shared; `StreetGround.LayFlight` after the mesh): a too-steep
    segment gets a flight across the whole street, ground level each side: whole number of
    flights of the style's first usable `steps` piece matching the rise within
    `sizeTolerance` (`Steps Full Width`, 1.62m, `cobbles`), stacked from where the render box
    below ends, height-scaled to the exact rise, side by side as many as round to the width,
    overlapping `groundOverlap`, width-scaled to fill, ramp feet at their levels, centred.
    Under `Ground - <street>/Steps - <street> <from>-<to>`.
  - **Decking (style with `floor` tiles - wood-floor): each segment in its own frame.** Rows
    across, centred; at a bend each segment runs on by half the wider width x tan(half turn)
    (max that width) to meet at the outside edge (inside overlaps). `deckingQuarterTurn` turns
    tiles 90°. Flat tiles follow the segment's height tile by tile, overlapping
    `deckingOverlap`, each a random half turn, **each row slid by a random share of a tile**
    (slide min-max, within a pitch, first unslid); random tile, no repeat unless only one.
  - **Floor colliders** (`FloorColliders`, decking): **tiles' own colliders removed** on the
    instance. **Each run of tiles along a row with collider tops within 1mm and matching
    `SurfaceTag` gets one `BoxCollider`** in the row frame (`BeginRow`: X along, Y up, Z
    across), exactly the tiles' collider extent. A skipped tile or height change ends a run.
    `Floor <n>` under the decking group's `Colliders`, tiles' layer and static flags, tag
    copied else Stone.

## Known about the prefabs

Scanner measurements (to confirm by eye):

- **Wall-like pieces face +Z** (flat back -Z); outer corners +X and +Z; inner corner
  `Foundations_CornerIn_01` -X and +Z. Exceptions: `Platforms_SideWall` flat side +Z,
  `Platforms_SideWall_Half_02` runs along Z, `PortchStraight_Extension` +X and -Z.
- Floors/platforms: one box collider (stone floors, ground pieces: mesh collider), top = walk
  surface.
- **Only cobble tile `StoneFloor_Tile`** (3.04 x 3.04m; variant adds a 3.04 x 0.4 x 3.04 box,
  pack mesh collider disabled). `StoneFloor_A`-`E` irregular 0.7-1.7m patches, never tiles.
  Planks ~3.0 x 3.0m (`Platforms_Planks`, `_Planks_Ground`), half 3.0 x 1.9, nailed 2.9 x 2.3,
  board 3.0 x 1.5-2.0; board direction unknown.
- `RampCollider` on `Platforms_Steps_Wide`, `Steps Full Width`, `Steps Wood Narrow`; other
  steps boxes only. **`Steps Full Width`**: solid stone, six steps climbing along -X, ramp foot
  0.28m before the first step, 1.62m rise over a 2.98m render box, 2.7m wide, plain back:
  `cobbles`' steps. The wooden two are open frames, unusable for streets, no style.
- **Wall modules** (back planes): plaster (`PlasterWalls_A_*`, `_B_PlanksCorner`) **3.0 x
  3.0m**; `WallTall_*` **3.05 x 4.5m**; `Masonary_WoodLarge_*` **4.95 x 4.55m** (half 2.6, low
  1.95, lower 1.0); half plaster ~1.6m wide; cellar 3.2 x 1.74m. Depth ~0.45-0.75m, pivot
  mid-depth.
- **Walls overlap by 3-5cm**: a full wall has trim down its left side and along its top; the
  next wall's left trim stands in for this one's right edge, the storey above sits on its top
  trim. Hence `wallOverlap` 4cm: `ModuleSpacing` = width - it, `StoreySpacing` = height - it,
  lot footprint = `modules x ModuleSpacing + WallOverlap`. `PlasterWalls_B_MasonaryWall`,
  `_Cropped`, `_Half` have `left` 4cm in from the render end to match. **Trimmed end = closed
  post, plain end = hollow.**
- **Wall pivot at the base's left end** (seen from front; back plane x 0 to -width). Doors,
  inserts (`Window_*`), cellar wall pivot at centre (inserts and doors centred in height too).
- **Blind window walls** (`PlasterWalls_A_WindowBase`, `_A_WindowBase_Half`, `_A_PlanksWindow`,
  `WallTall_WindowBase`, `WallTall_StoneWindowBase`, `Masonary_CellarWindow*`): no hole, so no
  `openings`, scanned `wall`; with `PlasterWalls_B_WindiwRoof` marked `showsOpening`.
  `WallTall_WindowBase` has hand-set `window` point (-1.75, 1.95, 0.38). Real openings only in
  `PlasterWalls_A_WindowBase_Hole`, `Masonary_WoodLarge_WallWindows`.
- Doors (both sides detailed) come out `unsure`, correctly; columns, beams, supports,
  foundations mostly `unsure`/`not-a-slab`: fronts set by eye.
- Corner confidences low by nature (0.03-0.16). Doubtful: `PortchStraight_Extension`,
  `Portch_BeamSmall`, `PlasterWalls_A_MidColumn` (pivot mid-height).
- **`pivot-not-on-base`** (93): `RoofTiles_*` flat panels upright in XY, tiles +Z, pivot at
  centre (~2.1m to edges); `RoofWall_*` reach ~0.16m below pivot (overlap the wall below);
  stone floors pivot on the walk surface; inserts/doors centred; rocks, iron, platforms vary.
- No colliders: four `Roof_Peaked*`, `PlanedWood_BeamLong`,
  `Platforms_BoardPlanks_SupportsCorner`.
- Left as they are: `Platforms_Steps_Wide` off `Environment`, not batching-static; `Steps Full
  Width`, `Steps Wood Narrow`, `Curved Top Door` not batching-static.
- `TL_TowerBridge_Entrance` filed under `Chimneys` (an assembly with a mirrored, turned,
  unevenly scaled part).
- Only catalogue lights: `IronFixtures_LargeTorch`, `Lampost`.

From the variant files:

- 265 variants, all to pack prefabs; 191 without `Building_Exteriors` (23), `Building_Layers`
  (51). Assemblies: one chimney (47 parts), three iron fixtures, one platform.
- Most have an LODGroup and box colliders; ground pieces mesh colliders.
- **Roof kit**: panels upright, tiles +Z, pivot centre - `RoofTiles_Middle` 4.13 x 4.16,
  `_MiddleSide` 4.30 x 4.16, `_TopCorner` 4.52 x 4.70 (ridge cap on top); the last two have a
  verge at +X (mirrored for the other end); `_Half` = +X halves (x -0.25 to 2.15).
  **`RoofTiles_Middle` (35 open edges) and both `_Half` (56-59) aren't closed**: excluded;
  `MiddleSide`, `TopCorner`, `RoofWall_Corner` closed. Pack houses: 52-68° pitch, 2-3 rows,
  scaled 0.6-1.4, overlapped joins; no hip/valley pieces. `RoofWall_CornerA`/`B` half gables
  (upright edge at x 0 sloping to +X, ~2.96 x 4.4m; unverified); `RoofWall_MedA`/`B`,
  `LargeA`/`B` whole gables (verified). `Building_Layers` roofs only for ~7, 10, 12m
  footprints. `Roof_Peaked*` = tower spire: four corner stacks of `PeakedBase` (3.03 x 3.46m),
  `PeakedMiddle`, `PeakedTop` (quarter turn from the others), 0.85 height scale, ~3m wide at
  the eave, ~60-75° bell flare (`PeakedBase` faces ~64°); generator uses `PeakedBase` alone.
  `RoofTiles_Supports` = truss under a verge.
- Binary FBX and scaled/turned children: measure sizes and pivots only in the editor.
- Duplicates to resolve: `TL_PlasterWalls_A_MidColumn 1`; `Steps Wood Narrow` /
  `TL_Platforms_Steps_Narrow` (one source). Pack misspellings kept (`Wallkway`, `Rright`,
  `WindiwRoof`).
