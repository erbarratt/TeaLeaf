using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Every value the level generator and the layout spec checks can be
    /// tuned by, kept in one asset (Assets/Data/LevelGen/GeneratorSettings.asset)
    /// and edited in the Inspector: menu TeaLeaf > Level Gen > Generator
    /// Settings selects it, making it with the default values if there is
    /// none. Generation and validation always read this one asset.
    /// </summary>
    public class GeneratorSettings : ScriptableObject
    {
        internal const string SettingsPath = PrefabCatalogueScanner.DataFolder + "/GeneratorSettings.asset";

        [Header("Walls")]
        [Tooltip("How far neighbouring walls overlap, side by side and storey on storey (the pack's walls are made to overlap by 3 to 5cm). Modules are laid at the module size less this.")]
        [SerializeField, Range(0f, 0.15f)] private float wallOverlap = 0.04f;
        [Tooltip("A wall at least this share of its style's widest wall is full size and sets the module; the half pieces are about half. Tiles are judged the same way against the style's biggest tile.")]
        [SerializeField, Range(0.5f, 1f)] private float fullSizeShare = 0.75f;
        [Tooltip("How far a wall or tile may be from its style's module or tile size and still be used as one (the plaster walls vary by about 5cm among themselves).")]
        [SerializeField, Range(0f, 0.25f)] private float sizeTolerance = 0.05f;
        [Tooltip("A wall further from its style's module than the size tolerance, but by no more than this share of it (width and height each), is still used: scaled along its width and height to fit the module exactly. A side wall is scaled the same way to its storey's height.")]
        [SerializeField, Range(0f, 0.25f)] private float wallScaleLimit = 0.12f;
        [Tooltip("Wherever pieces are laid end to end to cover a length (a run's modules, side walls, corner fillers, foundations), the length left after the whole pieces is closed by this rule: less than this share of a piece's normal width, the last piece is stretched to take it; otherwise one more piece is added, scaled down to it. So no piece is ever narrower than this share of its normal width, nor wider than one plus it.")]
        [SerializeField, Range(0.1f, 0.5f)] private float fillStretchShare = 0.3f;

        [Header("Streets")]
        [Tooltip("A street's width edge to edge when its spec gives none (width 0); a width in the spec overrides it.")]
        [SerializeField, Range(1f, 20f)] private float streetWidth = 5f;
        [Tooltip("Each segment of a street (point to point) is made up to this much wider or narrower than its street's width, at random, so streets step in and out at their bends. 0 keeps every segment at the street's width.")]
        [SerializeField, Range(0f, 5f)] private float streetWidthVariance = 1f;
        [Tooltip("No segment is made narrower than this, whatever the variance.")]
        [SerializeField, Range(0.5f, 10f)] private float minStreetWidth = 2f;

        [Header("Storeys")]
        [Tooltip("The fewest storeys a facade is given. A building's styles are listed from the ground up and the last one repeats for any storey above the list.")]
        [SerializeField, Range(1, 8)] private int minStoreys = 1;
        [Tooltip("The most storeys a facade is given.")]
        [SerializeField, Range(1, 8)] private int maxStoreys = 3;
        [Tooltip("The tallest a building (or any column of it) may stand, in metres above its street: its storeys stop where the next one's top would pass this, whatever its count, so tall storey styles never run away above their neighbours.")]
        [SerializeField, Range(3f, 40f)] private float maxBuildingHeight = 13f;
        [Tooltip("Which storey counts a facade's roll favours: 0 = every count between min and max equally likely, +1 = strongly towards max, -1 = strongly towards min.")]
        [SerializeField, Range(-1f, 1f)] private float storeyBias;
        [Tooltip("A lot's whole building takes the average height (in metres) of the facades within this distance of its footprint, as storeys of its own styles. With none that near, it is rolled like a facade.")]
        [SerializeField, Range(1f, 50f)] private float lotNeighbourDistance = 15f;
        [Tooltip("Storeys added to (or, below 0, taken from) a lot's building after matching its neighbours' height; the result is kept between min and max.")]
        [SerializeField, Range(-2f, 2f)] private float lotStoreyBias;

        [Header("Facades")]
        [Tooltip("How far the base of the ground storey's walls stands above the street (below 0, sunk into it). Its doors are this high too.")]
        [SerializeField, Range(-1f, 1.2f)] private float groundStoreyHeight;
        [Tooltip("How far the foundations' tops stand above the street, apart from the walls: above the ground storey's base they show as a plinth in front of the walls.")]
        [SerializeField, Range(0f, 1.2f)] private float foundationHeight = 0.3f;
        [Tooltip("How far the foundations' fronts stand out past the walls' front line, towards the street (below 0, back behind it).")]
        [SerializeField, Range(-0.5f, 0.5f)] private float foundationFrontOffset = 0.1f;
        [Tooltip("Once everything is built, the foundations' colliders, and the walkways' flat deck colliders, are replaced by as few boxes as possible: two boxes become one where the box round both stands out past them by no more than this anywhere. The foundation pieces differ by about 9cm at their buried bottoms, so it must be more than that for neighbours to merge.")]
        [SerializeField, Range(0f, 0.3f)] private float foundationColliderTolerance = 0.1f;
        [Tooltip("The chance that a ground storey module is a door wall, where its style has one; a building's door module in the spec is always a door.")]
        [SerializeField, Range(0f, 1f)] private float groundDoorShare = 0.1f;
        [Tooltip("The chance that a ground storey module that isn't a door is a window wall rather than a plain wall, where its style has window walls.")]
        [SerializeField, Range(0f, 1f)] private float groundWindowShare = 0.3f;
        [Tooltip("The chance that a module above the ground storey is a window wall rather than a plain wall, where its style has window walls.")]
        [SerializeField, Range(0f, 1f)] private float upperWindowShare = 0.4f;
        [Tooltip("Where the storeys below and above a joint both have beam pieces, a band of beams lies between their walls; this is how far the beams overlap the walls below and above them (each). The storeys above stand higher by the beam's height less twice this.")]
        [SerializeField, Range(0f, 0.2f)] private float storeyBeamOverlap = 0.04f;

        [Header("Corners")]
        [Tooltip("A corner within this many degrees of a right angle counts as square: its roofs meet along a hip or valley, and a facade corner no wider than a right angle plus this gets gables.")]
        [SerializeField, Range(0f, 45f)] private float cornerAngleTolerance = 20f;
        [Tooltip("Two frontages' ends at a junction are joined at a street corner only if each needs carrying no further than this to meet the other; otherwise each turns a side return into the branching street.")]
        [SerializeField, Range(1f, 20f)] private float cornerMaxReach = 8f;
        [Tooltip("At a corner the side whose walls end in their trim runs on to the other side's front; the other side's walls, ending plain, run this far past the covering walls' back line, into their depth, so their end is hidden inside them.")]
        [SerializeField, Range(0f, 0.4f)] private float cornerTuck = 0.15f;

        [Header("Jetties")]
        [Tooltip("The chance that a facade building's upper storeys jut out over the street (by the depth of the jetty piece, about 1.5m), carried on jetty supports - where it has an upper storey and a jetty piece fits its ground storey's module. Higher for more jettied buildings, lower for fewer.")]
        [SerializeField, Range(0f, 1f)] private float jettyShare = 0.3f;
        [Tooltip("How far the backs of the jetty supports sink into the ground storey's walls, behind the walls' main front surface (the plaster or stone between the timbers), so no gap shows behind them. The jettied storeys above stand back by the same amount, so their faces stay level with the supports' front.")]
        [SerializeField, Range(0f, 0.3f)] private float jettyInset = 0.05f;

        [Header("Doors")]
        [Tooltip("Every doorway in a placed wall gets a plain door (a verified piece of the door category; never a gameplay door), at random, scaled to the doorway: this much taller than it at the top, so its top tucks into the frame.")]
        [SerializeField, Range(0f, 0.3f)] private float doorInsertMargin = 0.05f;
        [Tooltip("How much wider a door is on each side than the doorway as seen through the wall (measured through its mesh, so a stone frame narrower than the hole in the back plane counts), so its edges tuck in behind the frame.")]
        [SerializeField, Range(0f, 0.4f)] private float doorSideMargin = 0.13f;
        [Tooltip("How far a doorway's door stands forward of the middle of the wall's depth (back, below 0).")]
        [SerializeField, Range(-0.5f, 0.5f)] private float doorInsertDepth;

        [Header("Window inserts")]
        [Tooltip("The chance that a hole for a window in a placed wall gets a window insert (a verified piece of the window category) rather than staying open.")]
        [SerializeField, Range(0f, 1f)] private float windowInsertShare = 1f;
        [Tooltip("How much bigger than the hole an insert may be on each side (its frame overlapping the wall round the hole); an insert smaller than the hole never fits.")]
        [SerializeField, Range(0f, 1f)] private float windowInsertMaxMargin = 0.45f;
        [Tooltip("How far the insert's middle stands forward of the middle of the wall's depth (back, below 0).")]
        [SerializeField, Range(-0.5f, 0.5f)] private float windowInsertDepth;
        [Tooltip("The share of the window holes filled whose insert is a lit one (a window piece marked Lit: its panes glow as if lit from behind); the rest get unlit inserts.")]
        [SerializeField, Range(0f, 1f)] private float litWindowShare = 0.3f;

        [Header("Columns")]
        [Tooltip("Every facade building is cut end to end into columns this many modules wide, at random between these (the last column of a building, or of a straight run, takes what is left). Each column has its own depth and storey count.")]
        [SerializeField, Range(1, 6)] private int columnWidthMin = 1;
        [SerializeField, Range(1, 6)] private int columnWidthMax = 2;
        [Tooltip("The chance that a column stands forward of, or back from, the facade's line for its full height, with side walls filling the steps.")]
        [SerializeField, Range(0f, 1f)] private float columnShare = 0.15f;
        [Tooltip("A straight run of facade (between bends) needs at least this many modules to have columns off the line. A column holding its first or last module always stays on the line.")]
        [SerializeField, Range(3, 20)] private int columnMinRunModules = 6;
        [Tooltip("How far a column stands forward or back from the line, at random between these (kept to half a module, so one side wall fills any step).")]
        [SerializeField, Range(0.05f, 3f)] private float columnDepthMin = 0.3f;
        [SerializeField, Range(0.05f, 3f)] private float columnDepthMax = 0.8f;
        [Tooltip("The share of columns off the line that stand forward of it (into the street); the rest stand back from it.")]
        [SerializeField, Range(0f, 1f)] private float columnOutShare = 0.5f;
        [Tooltip("The chance that a column stands taller or lower than its building's storey count (taller or lower equally likely), kept between min and max storeys.")]
        [SerializeField, Range(0f, 1f)] private float columnHeightShare = 0.3f;
        [Tooltip("How many storeys at most a column stands taller or lower than its building, at random from 1 up to this.")]
        [SerializeField, Range(1, 4)] private int columnStoreyVariation = 1;

        [Header("Dressing: pipes, valves, vents, walkways, lamp posts")]
        [Tooltip("Every building gets one run of pipe; each further run, up to this many in all, comes at pipeExtraRunShare.")]
        [SerializeField, Range(1, 5)] private int pipeRunsMax = 2;
        [Tooltip("The chance of each run of pipe beyond a building's first.")]
        [SerializeField, Range(0f, 1f)] private float pipeExtraRunShare = 0.35f;
        [Tooltip("A building's runs of pipe together are at least this share of its width along the street (more runs are added until they are, as far as they can be placed).")]
        [SerializeField, Range(0f, 1f)] private float pipeMinRunShare = 0.5f;
        [Tooltip("The share of runs in the large pipe (1.1m across) rather than the small (0.67m).")]
        [SerializeField, Range(0f, 1f)] private float pipeLargeShare = 0.1f;
        [Tooltip("Clear space between a wall's face and the pipe beside it.")]
        [SerializeField, Range(0f, 0.3f)] private float pipeWallGap = 0.05f;
        [Tooltip("Clear space kept between a pipe and any door or window.")]
        [SerializeField, Range(0f, 1f)] private float pipeOpeningMargin = 0.25f;
        [Tooltip("Where a level stretch of pipe ends in an elbow turning into the wall, the end lands at random within this share of its module's width about the module's middle (0 = always the middle, 1 = anywhere along it), never at a joint between walls.")]
        [SerializeField, Range(0f, 1f)] private float pipeEndSpread = 0.5f;
        [Tooltip("The chance that a run of pipe gets a valve: a T-junction in one of its straight stretches, its branch pointing out from the facade into the street, the valve on the branch.")]
        [SerializeField, Range(0f, 1f)] private float valveShare = 0.3f;
        [Tooltip("How high above the street a valve on an upright stretch of pipe stands (the T-junction's middle).")]
        [SerializeField, Range(0.5f, 3f)] private float valveHeight = 1.3f;
        [Tooltip("The chance of each vent a building gets, up to ventsMax. A vent stands on a smooth piece of wall anywhere on the facade, apart from the pipes, never over a door or window.")]
        [SerializeField, Range(0f, 1f)] private float ventShare = 0.4f;
        [Tooltip("The most vents one building gets.")]
        [SerializeField, Range(0, 4)] private int ventsMax = 2;
        [Tooltip("The share of vents that are the large vent rather than the small.")]
        [SerializeField, Range(0f, 1f)] private float ventLargeShare = 0.3f;
        [Tooltip("How far in depth the wall's drawn surface may vary under a vent (measured at the vent's middle and corners) for the wall to count as smooth there.")]
        [SerializeField, Range(0.005f, 0.2f)] private float ventFlatTolerance = 0.03f;
        [Tooltip("The share of buildings with an upper storey that get a walkway along their facade: a row of the jetty piece at the base of one upper storey (never the base of a jettied one).")]
        [SerializeField, Range(0f, 1f)] private float walkwayShare = 0.3f;
        [Tooltip("Metres of street frontage per lamp post, on average (0 = no lamp posts). Posts stand at the side of the street before plain ground-storey walls, never before a door or window, facing into the street.")]
        [SerializeField, Range(0f, 60f)] private float lampSpacing = 14f;
        [Tooltip("How far into the street from its edge a lamp post stands.")]
        [SerializeField, Range(0.3f, 2f)] private float lampEdgeInset = 0.6f;
        [Tooltip("The most lamp posts whose lights may reach any one spot, over every street. A lamp's light is Mixed, and the shadowmask holds four Mixed lights per spot, the moon being one; a post that would break this is not placed.")]
        [SerializeField, Range(1, 4)] private int lampMaxOverlap = 3;

        [Header("Side walls")]
        [Tooltip("Where a column stands taller than the column beside it (in its building or the next), each of its storeys above the lower one gets a side wall facing that way and running back from its front walls, so from the street it seems to go on behind. The wall's length in modules is this times the column's width in modules, at least one (0 = no side walls). Separate from roofs and roof walls.")]
        [SerializeField, Range(0f, 5f)] private float sideWallMultiplier = 1f;
        [Tooltip("At a column's left end (seen from the street), where its front walls' end has its trim, the side walls are tucked in behind that end as the walls go together: their back plane this far inside the column from the join, their street end on the front wall's main surface (the plaster or stone between the timbers). At the right end they stand outside the column, covering the front walls' plain end.")]
        [SerializeField, Range(0f, 1f)] private float sideWallTuck = 0.26f;

        [Tooltip("How far a storey's module width may differ from the ground storey's below it. Every storey is laid at the ground storey's spacing, each wall centred in its place, so a wider wall overlaps its neighbours and a narrower one leaves a small gap.")]
        [SerializeField, Range(0f, 1f)] private float maxStoreyWidthMismatch = 0.3f;

        [Header("Layout checks")]
        [Tooltip("How far two footprints may overlap, or a frontage overhang its stretch of street, before it is reported.")]
        [SerializeField, Range(0f, 0.5f)] private float overlapTolerance = 0.05f;
        [Tooltip("A street segment shorter than this along the ground is an error.")]
        [SerializeField, Range(0.01f, 1f)] private float minSegmentLength = 0.1f;
        [Tooltip("Two streets' points closer than this along the ground are one junction.")]
        [SerializeField, Range(0.01f, 1f)] private float junctionDistance = 0.1f;
        [Tooltip("A street leaving a junction at least this many degrees off another street, towards one of its sides, branches off on that side.")]
        [SerializeField, Range(1f, 89f)] private float branchAngle = 17.5f;
        [Tooltip("A street ending at a junction where another street runs on (a branch) must meet it within this many degrees of square: 20 allows 70 to 110 degrees. A bend where just two streets meet end to end is not a branch.")]
        [SerializeField, Range(0f, 89f)] private float branchSquareTolerance = 20f;

        [Header("Street clearance")]
        [Tooltip("A piece standing at least this high above the street its facade faces (a jetty, a beam, an upper storey stepping out) may reach out over that street. Lower down a piece may come into it only as far as a column steps forward; into any other street, only as far as a foundation's front.")]
        [SerializeField, Range(1f, 5f)] private float streetHeadroom = 2.2f;
        [Tooltip("The street test after generating: a ball of this radius travels down the middle of every street, its centre this high above the ground, up and down the steps. A layout is valid only if it never touches anything the generator built for the buildings.")]
        [SerializeField, Range(0.1f, 1.5f)] private float streetBallRadius = 0.5f;
        [SerializeField, Range(0.3f, 2f)] private float streetBallHeight = 1f;

        [Header("Sharp corners (for layout specs)")]
        [Tooltip("A corner between two streets (the wedge between two of them leaving a junction, or the inside of a bend) narrower than this many degrees is sharp. The generator clears any angle; this only sets what counts as sharp.")]
        [SerializeField, Range(10f, 89f)] private float sharpCornerAngle = 60f;
        [Tooltip("The share of a layout's street corners that are sharp. Claude writes layout specs to it, and the validator reports how far a spec is from it.")]
        [SerializeField, Range(0f, 1f)] private float sharpCornerShare = 0.25f;

        [Header("Ground tiles (cobbles)")]
        [Tooltip("How far neighbouring ground tiles overlap, along a row and between rows.")]
        [SerializeField, Range(0f, 0.2f)] private float groundOverlap = 0.03f;
        [Tooltip("Each tile is turned a quarter, half or three-quarter turn from the tile before it along its row (square tiles; other tiles take a random half turn).")]
        [SerializeField] private bool groundTileTurns = true;
        [Tooltip("Each row slides along from the row before it by a random share of a tile between these two, so the joints never line up.")]
        [SerializeField, Range(0f, 1f)] private float groundSlideMin = 0.2f;
        [SerializeField, Range(0f, 1f)] private float groundSlideMax = 0.8f;
        [Tooltip("Each street area's ground grid is turned at random so its rows lie at least this many degrees off the world's X and Z axes: never squared to them. Tiles are laid wherever they overlap the area, so the turn leaves no gaps.")]
        [SerializeField, Range(0f, 45f)] private float groundGridMinAngle = 15f;
        [Tooltip("The ground is laid under each lot too, as part of the nearest street area's grid: a ring from the lot's footprint out to this far round it, so it reaches under its walls and foundations (and any yard a lot with its own outline or modules leaves). Never under the building itself.")]
        [SerializeField, Range(0f, 20f)] private float lotGroundMargin = 1f;
        [Tooltip("A tile is laid only where it overlaps the street by at least this much; the rest of it may stand out under the buildings lining the street.")]
        [SerializeField, Range(0f, 1.5f)] private float minTileCoverage = 0.1f;

        [Header("Steps (streets changing height)")]
        [Tooltip("The landing of ground tiles laid square to a flight of steps at its top, in the steps' own frame, stands this far below the street's level, so where the street's own grid of tiles overlaps it the grid's tiles lie on top.")]
        [SerializeField, Range(0f, 0.1f)] private float landingDrop = 0.02f;

        [Header("Roofs")]
        [Tooltip("The roofs' pitch, in degrees from level. The pack's own roofs lie between about 52 and 68.")]
        [SerializeField, Range(30f, 75f)] private float roofPitch = 60f;
        [Tooltip("Rows of tile panels up a facade's roof, from the eave to the ridge (the top row carries the ridge cap). A lot's roof takes as many as reach its middle.")]
        [SerializeField, Range(1, 4)] private int roofRows = 2;
        [Tooltip("How far a roof's eave stands out in front of the face of the top storey's walls.")]
        [SerializeField, Range(0f, 2f)] private float roofEaveOverhang = 0.4f;
        [Tooltip("How far a roof's eave lies below the top of the walls, so the walls' top is hidden under the roof.")]
        [SerializeField, Range(0f, 2f)] private float roofEaveDrop = 1f;
        [Tooltip("How far a roof's tiles run on past the gable at each of its ends.")]
        [SerializeField, Range(0f, 1.5f)] private float roofVergeOverhang = 0.2f;
        [Tooltip("How far one row of tile panels overlaps the row below it, up the slope.")]
        [SerializeField, Range(0f, 1f)] private float roofRowOverlap = 0.25f;
        [Tooltip("How far neighbouring tile panels overlap along a row: enough to cover a panel's verge end, whose top tiles are set back about 0.3m, where it faces the next panel.")]
        [SerializeField, Range(0f, 0.6f)] private float roofPanelOverlap = 0.45f;
        [Tooltip("How far a gable's base sinks below the top of the walls it stands on.")]
        [SerializeField, Range(0f, 0.3f)] private float roofGableSink = 0.05f;
        [Tooltip("How far the lowest piece of a hip strip (Roof_PeakedBase pieces stacked up a hipped corner) stands out of the corner, along each slope's front, so the walls' corner stays inside it (the piece's own corner is cut back).")]
        [SerializeField, Range(0f, 1.5f)] private float roofHipOut = 0.75f;
        [Tooltip("How far the lowest piece of a hip strip stands above the eaves.")]
        [SerializeField, Range(0f, 1.5f)] private float roofHipRaise = 0.2f;
        [Tooltip("The most one piece of a hip strip rises above the one below it; the strip takes as many pieces as reach from the eaves to the ridge in such steps, overlapping like scales.")]
        [SerializeField, Range(0.5f, 4f)] private float roofHipStep = 2.5f;
        [Tooltip("How far back, in degrees, each piece of a hip strip leans (its outer corner up), so its faces, steeper than the roofs' pitch, lie flatter on the slopes.")]
        [SerializeField, Range(0f, 25f)] private float roofHipLean = 10f;

        [Header("Roof cull (last of all)")]
        [Tooltip("The roof cull, the last thing a generation does: a probe walks the middle of every street this high above the ground tiles (up and down the steps), and every roof piece it can't see from anywhere along the way is removed.")]
        [SerializeField, Range(2f, 30f)] private float roofCullHeight = 9f;
        [Tooltip("How far the roof cull's probe moves between looks.")]
        [SerializeField, Range(0.5f, 10f)] private float roofCullStep = 2f;
        [Tooltip("The farthest the roof cull's probe looks: a roof piece further than this from every look is removed.")]
        [SerializeField, Range(10f, 300f)] private float roofCullRange = 80f;
        [Tooltip("Roof pieces facing the void beyond the level lose their colliders: how far past the furthest point of a piece's building the empty space must start.")]
        [SerializeField, Range(0f, 10f)] private float roofEdgeGap = 1.5f;
        [Tooltip("How far out the space beyond a roof piece's building must be empty (no Environment collider, from the piece's top down) for the piece to lose its colliders.")]
        [SerializeField, Range(2f, 100f)] private float roofEdgeReach = 20f;

        [Header("Decking (wooden streets, walkways, bridges)")]
        [Tooltip("How far neighbouring decking tiles overlap, along a row and between rows.")]
        [SerializeField, Range(0f, 0.2f)] private float deckingOverlap = 0.03f;
        [Tooltip("Lay the decking tiles a quarter turn from the street (turn this on if the boards run along the walkway rather than across it).")]
        [SerializeField] private bool deckingQuarterTurn;
        [Tooltip("Each row slides along from the row before it by a random share of a tile between these two (0 and 0 keeps the rows' ends in line).")]
        [SerializeField, Range(0f, 1f)] private float deckingSlideMin;
        [SerializeField, Range(0f, 1f)] private float deckingSlideMax;

        internal float WallOverlap => wallOverlap;
        internal float FullSizeShare => fullSizeShare;
        internal float SizeTolerance => sizeTolerance;
        internal float WallScaleLimit => wallScaleLimit;
        internal float FillStretchShare => fillStretchShare;
        internal float StreetWidth => streetWidth;
        internal float StreetWidthVariance => streetWidthVariance;
        internal float MinStreetWidth => minStreetWidth;
        internal int MinStoreys => minStoreys;
        internal int MaxStoreys => maxStoreys;
        internal float MaxBuildingHeight => maxBuildingHeight;
        internal float StoreyBias => storeyBias;
        internal float LotNeighbourDistance => lotNeighbourDistance;
        internal float LotStoreyBias => lotStoreyBias;
        internal float GroundStoreyHeight => groundStoreyHeight;
        internal float FoundationHeight => foundationHeight;
        internal float FoundationFrontOffset => foundationFrontOffset;
        internal float FoundationColliderTolerance => foundationColliderTolerance;
        internal float GroundDoorShare => groundDoorShare;
        internal float GroundWindowShare => groundWindowShare;
        internal float UpperWindowShare => upperWindowShare;
        internal float StoreyBeamOverlap => storeyBeamOverlap;
        internal float JettyShare => jettyShare;
        internal float JettyInset => jettyInset;
        internal float CornerAngleTolerance => cornerAngleTolerance;
        internal float CornerMaxReach => cornerMaxReach;
        internal float CornerTuck => cornerTuck;
        internal float DoorInsertMargin => doorInsertMargin;
        internal float DoorSideMargin => doorSideMargin;
        internal float DoorInsertDepth => doorInsertDepth;
        internal float WindowInsertShare => windowInsertShare;
        internal float WindowInsertMaxMargin => windowInsertMaxMargin;
        internal float WindowInsertDepth => windowInsertDepth;
        internal float LitWindowShare => litWindowShare;
        internal float ColumnShare => columnShare;
        internal int ColumnMinRunModules => columnMinRunModules;
        internal int ColumnWidthMin => columnWidthMin;
        internal int ColumnWidthMax => columnWidthMax;
        internal float ColumnDepthMin => columnDepthMin;
        internal float ColumnDepthMax => columnDepthMax;
        internal float ColumnOutShare => columnOutShare;
        internal float ColumnHeightShare => columnHeightShare;
        internal int ColumnStoreyVariation => columnStoreyVariation;
        internal float MaxStoreyWidthMismatch => maxStoreyWidthMismatch;
        internal float SideWallMultiplier => sideWallMultiplier;
        internal float SideWallTuck => sideWallTuck;
        internal int PipeRunsMax => pipeRunsMax;
        internal float PipeExtraRunShare => pipeExtraRunShare;
        internal float PipeMinRunShare => pipeMinRunShare;
        internal float PipeLargeShare => pipeLargeShare;
        internal float PipeWallGap => pipeWallGap;
        internal float PipeOpeningMargin => pipeOpeningMargin;
        internal float PipeEndSpread => pipeEndSpread;
        internal float ValveShare => valveShare;
        internal float ValveHeight => valveHeight;
        internal float VentShare => ventShare;
        internal int VentsMax => ventsMax;
        internal float VentLargeShare => ventLargeShare;
        internal float VentFlatTolerance => ventFlatTolerance;
        internal float WalkwayShare => walkwayShare;
        internal float LampSpacing => lampSpacing;
        internal float LampEdgeInset => lampEdgeInset;
        internal int LampMaxOverlap => lampMaxOverlap;
        internal float OverlapTolerance => overlapTolerance;
        internal float MinSegmentLength => minSegmentLength;
        internal float JunctionDistance => junctionDistance;
        // The branch test compares the leaving direction with the side's
        // normal, so the angle off the street is its sine.
        internal float BranchDot => Mathf.Sin(branchAngle * Mathf.Deg2Rad);
        internal float BranchSquareTolerance => branchSquareTolerance;
        internal float StreetHeadroom => streetHeadroom;
        internal float StreetBallRadius => streetBallRadius;
        internal float StreetBallHeight => streetBallHeight;
        internal float SharpCornerAngle => sharpCornerAngle;
        internal float SharpCornerShare => sharpCornerShare;
        internal float GroundOverlap => groundOverlap;
        internal bool GroundTileTurns => groundTileTurns;
        internal float GroundSlideMin => groundSlideMin;
        internal float GroundSlideMax => groundSlideMax;
        internal float MinTileCoverage => minTileCoverage;
        internal float GroundGridMinAngle => groundGridMinAngle;
        internal float LotGroundMargin => lotGroundMargin;
        internal float LandingDrop => landingDrop;
        internal float RoofPitch => roofPitch;
        internal int RoofRows => roofRows;
        internal float RoofEaveOverhang => roofEaveOverhang;
        internal float RoofEaveDrop => roofEaveDrop;
        internal float RoofVergeOverhang => roofVergeOverhang;
        internal float RoofRowOverlap => roofRowOverlap;
        internal float RoofPanelOverlap => roofPanelOverlap;
        internal float RoofGableSink => roofGableSink;
        internal float RoofHipOut => roofHipOut;
        internal float RoofHipRaise => roofHipRaise;
        internal float RoofHipStep => roofHipStep;
        internal float RoofHipLean => roofHipLean;
        internal float RoofCullHeight => roofCullHeight;
        internal float RoofCullStep => roofCullStep;
        internal float RoofCullRange => roofCullRange;
        internal float RoofEdgeGap => roofEdgeGap;
        internal float RoofEdgeReach => roofEdgeReach;
        internal float DeckingOverlap => deckingOverlap;
        internal bool DeckingQuarterTurn => deckingQuarterTurn;
        internal float DeckingSlideMin => deckingSlideMin;
        internal float DeckingSlideMax => deckingSlideMax;

        /// <summary>
        /// Keeps each range in order (min no more than max).
        /// </summary>
        private void OnValidate()
        {
            maxStoreys = Mathf.Max(minStoreys, maxStoreys);
            columnWidthMax = Mathf.Max(columnWidthMin, columnWidthMax);
            columnDepthMax = Mathf.Max(columnDepthMin, columnDepthMax);
            groundSlideMax = Mathf.Max(groundSlideMin, groundSlideMax);
            deckingSlideMax = Mathf.Max(deckingSlideMin, deckingSlideMax);
        }

        /// <summary>
        /// The settings asset, made with the default values if there is none yet.
        /// </summary>
        internal static GeneratorSettings Load()
        {
            GeneratorSettings settings = AssetDatabase.LoadAssetAtPath<GeneratorSettings>(SettingsPath);

            if (settings != null) {
                return settings;
            }

            settings = CreateInstance<GeneratorSettings>();
            PrefabCatalogueScanner.EnsureFolder(PrefabCatalogueScanner.DataFolder);
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>
        /// Selects the settings asset for editing in the Inspector.
        /// </summary>
        [MenuItem("TeaLeaf/Level Gen/Generator Settings")]
        private static void SelectSettings()
        {
            GeneratorSettings settings = Load();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
