using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

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
        [Tooltip("How far neighbouring walls overlap, side by side and storey on storey, in metres (the pack's walls are made to overlap by 3 to 5cm); modules are laid at the module size less this, e.g. 0.04 lays 3m plaster walls every 2.96m. Higher = walls pushed further into each other; too low and seams open between them.")]
        [SerializeField, Range(0f, 0.15f)] private float wallOverlap = 0.04f;
        [Tooltip("A wall at least this share of its style's widest wall is full size and sets the module; tiles are judged the same way against the style's biggest tile, e.g. 0.75 counts a 3m plaster wall as full size but not a 1.6m half piece. Higher = only the very widest pieces set the module; lower = narrower pieces count too.")]
        [SerializeField, Range(0.5f, 1f)] private float fullSizeShare = 0.75f;
        [Tooltip("How far, in metres, a wall or tile may be from its style's module or tile size and still be used as it is, unscaled (the plaster walls vary by about 5cm among themselves), e.g. 0.05 uses a 3.04m wall in a 3m module as it stands. Higher = more pieces used unscaled; lower = more of them scaled to fit.")]
        [SerializeField, Range(0f, 0.25f)] private float sizeTolerance = 0.05f;
        [Tooltip("A wall further from its style's module than Size Tolerance, but by no more than this share of it (width and height each), is still used, scaled in width and height to fit the module exactly; a side wall is scaled the same way to its storey's height, e.g. 0.12 lets a 2.7m wall stretch into a 3m module. Higher = more pieces usable but visibly stretched; 0 = only pieces within Size Tolerance.")]
        [SerializeField, Range(0f, 0.25f)] private float wallScaleLimit = 0.12f;
        [Tooltip("Wherever pieces are laid end to end to cover a length (modules, side walls, corner fillers, foundations), the length left after the whole pieces is closed this way: less than this share of a piece's width and the last piece is stretched to take it, otherwise one more piece is added, scaled down to fit, e.g. 0.3 stretches a 3m wall to 3.6m but adds a 1.2m piece for 1.2m left. Higher = more stretching, fewer narrow pieces; lower = more narrow pieces, less stretching.")]
        [SerializeField, Range(0.1f, 0.5f)] private float fillStretchShare = 0.3f;

        [Header("Streets")]
        [Tooltip("A street's width edge to edge, in metres, when its spec gives none (width 0); a width in the spec overrides it, e.g. 5 = a 5m wide street before Street Width Variance. Higher = wider default streets.")]
        [SerializeField, Range(1f, 20f)] private float streetWidth = 5f;
        [Tooltip("Each segment of a street (point to point) is made up to this many metres wider or narrower than its street's width, at random, so streets step in and out at their bends, e.g. 1 makes a 5m street's segments 4 to 6m wide. Higher = more uneven streets; 0 keeps every segment at the street's width.")]
        [SerializeField, Range(0f, 5f)] private float streetWidthVariance = 1f;
        [Tooltip("No segment is made narrower than this, in metres, whatever the variance, e.g. 2 = a 2.5m alley with 1m variance never drops below 2m. Higher = no tight squeezes; lower = allows very narrow segments.")]
        [SerializeField, Range(0.5f, 10f)] private float minStreetWidth = 2f;
        [Tooltip("How far from a street's edge, in metres, the walls along it are looked for; their tops set how high the moon shafts' openings and the sound rooms reach, e.g. 1.5 finds the facade walls standing up to 1.5m back from the edge. Higher = finds walls set further back but may catch ones behind; lower = may miss deep-set walls.")]
        [SerializeField, Range(0.2f, 5f), FormerlySerializedAs("moonShaftWallProbe")] private float edgeWallProbe = 1.5f;

        [Header("Storeys")]
        [Tooltip("The fewest storeys a facade is given; a building's styles are listed from the ground up and the last one repeats for any storey above the list, e.g. 1 allows single-storey cottages. Higher = no low buildings.")]
        [SerializeField, Range(1, 8)] private int minStoreys = 1;
        [Tooltip("The most storeys a facade is given (Max Building Height can stop it sooner), e.g. 3 = at most a ground floor and two upper storeys. Higher = taller streets.")]
        [SerializeField, Range(1, 8)] private int maxStoreys = 3;
        [Tooltip("The tallest a building (or any column of it) may stand, in metres above its street: storeys stop where the next one's top would pass this, whatever the count, so tall storey styles never tower over their neighbours, e.g. 13 stops two 4.5m wall-tall storeys from getting a third. Higher = taller buildings allowed; lower = cuts storeys off sooner.")]
        [SerializeField, Range(3f, 40f)] private float maxBuildingHeight = 13f;
        [Tooltip("Which storey counts a facade's roll favours: 0 = every count between Min and Max Storeys equally likely, +1 = strongly towards Max, -1 = strongly towards Min, e.g. 0.5 makes most buildings tall with a few low ones.")]
        [SerializeField, Range(-1f, 1f)] private float storeyBias;
        [Tooltip("A lot's whole building takes the average height of the facades within this many metres of its footprint, as storeys of its own styles; with none that near it is rolled like a facade, e.g. 15 matches a guild hall to the houses across its streets. Higher = matches further-off buildings; lower = more lots rolled on their own.")]
        [SerializeField, Range(1f, 50f)] private float lotNeighbourDistance = 15f;
        [Tooltip("Storeys added to (or, below 0, taken from) a lot's building after matching its neighbours' height, kept between Min and Max Storeys, e.g. 1 makes a lot one storey taller than the houses round it. 0 = the same height as its neighbours.")]
        [SerializeField, Range(-2f, 2f)] private float lotStoreyBias;

        [Header("Facades")]
        [Tooltip("How far the base of the ground storey's walls (and its doors) stands above the street, in metres; below 0 it is sunk into it, e.g. 0.3 raises the ground floor 30cm over the cobbles. Higher = buildings stand up off the street on their foundations.")]
        [SerializeField, Range(-1f, 1.2f)] private float groundStoreyHeight;
        [Tooltip("The share of their own depth (front to back) every generated wall is scaled to - facade walls, side walls, corner fillers, gap fillers, roof gables and open window interiors' walls - e.g. 0.36 turns a 0.5m wall into an 18cm one, its front kept on the street line. Higher = thicker walls; 1 = the pack's own depth.")]
        [SerializeField, Range(0.05f, 1f), FormerlySerializedAs("openWindowWallThickness")] private float wallThickness = 0.36f;
        [Tooltip("The share of their own height and depth every foundation is scaled to (their width still fills the module), e.g. 0.5 = half as tall and half as deep. Their top stays foundationHeight above the street and their front foundationFrontOffset past the walls; below a sloping street more courses are stacked to reach the ground. Higher = bigger stones; 1 = the pack's own size.")]
        [SerializeField, Range(0.1f, 1f)] private float foundationScale = 0.5f;
        [Tooltip("How far the foundations' tops stand above the street, in metres, apart from the walls: above the ground storey's base they show as a plinth in front of the walls, e.g. 0.3 = a 30cm stone course along the foot of each facade. Higher = a taller plinth; 0 = flush with the street.")]
        [SerializeField, Range(0f, 1.2f)] private float foundationHeight = 0.3f;
        [Tooltip("How far the foundations' fronts stand out past the walls' front line towards the street, in metres; below 0 they sit back behind it, e.g. 0.1 = the stone course stands 10cm proud of the walls. Higher = a deeper ledge into the street.")]
        [SerializeField, Range(-0.5f, 0.5f)] private float foundationFrontOffset = 0.1f;
        [Tooltip("Where the street falls below a building's level (beside a slope, or where a corner raised it), more foundations are stacked under it until the lowest one's bottom is this many metres below the lowest the street comes in front, so the stone always meets the ground, e.g. 0.1 buries the bottom course 10cm. Higher = deeper buried, safer on uneven ground; 0 = just touching.")]
        [SerializeField, Range(0f, 1f)] private float foundationBury = 0.1f;
        [Tooltip("The foundations' colliders and the walkways' flat deck colliders are merged into as few boxes as possible: two boxes become one where the box round both stands out past them by no more than this, in metres, e.g. 0.1 merges neighbouring foundations whose buried bottoms differ by 9cm at full size (about 5cm at foundationScale 0.5). Higher = fewer, rougher boxes; below the bottoms' difference little merges.")]
        [SerializeField, Range(0f, 0.3f)] private float foundationColliderTolerance = 0.1f;
        [Tooltip("The same for the walls' colliders (every piece under a building's Storey and Corner groups), merged after the visibility cull along a storey and storey onto storey, in metres. A window-base wall's collider stands 12-14cm proud of a plain wall's at full depth (its sill; about 5cm at wallThickness 0.36), e.g. 0.15 makes a storey one box whose front lies at the sills. Higher = fewer boxes, less exact; below the sills' reach the two kinds stay apart and little merges.")]
        [SerializeField, Range(0f, 0.3f)] private float wallColliderTolerance = 0.15f;
        [Tooltip("The same for the roof slope panels' colliders, merged after the roof edge pass panel onto panel along a row, and row onto row where their ends line up, in metres. The two rows' collider tops differ by 7cm, e.g. 0.1 lets rows merge at gables. Higher = fewer boxes; below 0.07 each row stays its own box.")]
        [SerializeField, Range(0f, 0.3f)] private float roofColliderTolerance = 0.1f;
        [Tooltip("The chance, 0-1, that a ground storey module is a door wall, where its style has one; a building's door module in the spec is always a door, e.g. 0.1 = about one ground module in ten has a door. Higher = more doors along the street; 0 = only the spec's doors.")]
        [SerializeField, Range(0f, 1f)] private float groundDoorShare = 0.1f;
        [Tooltip("The chance, 0-1, that a ground storey module that isn't a door is a window wall rather than a plain wall, where its style has window walls, e.g. 0.3 = about a third of ground modules have windows. Higher = more windows; 0 = blank ground floors.")]
        [SerializeField, Range(0f, 1f)] private float groundWindowShare = 0.3f;
        [Tooltip("The chance, 0-1, that a module above the ground storey is a window wall rather than a plain wall, where its style has window walls, e.g. 0.4 = about two upper modules in five have windows. Higher = more windows; 0 = blank upper storeys.")]
        [SerializeField, Range(0f, 1f)] private float upperWindowShare = 0.4f;
        [Tooltip("Where the storeys below and above a joint both have beam pieces, a band of beams lies between them; this is how far, in metres, the beams overlap the walls below and above (each), and the storeys above rise by the beam's height less twice this, e.g. 0.04 = 4cm tucked in each side. Higher = beams sink further into the walls; too low and seams show.")]
        [SerializeField, Range(0f, 0.2f)] private float storeyBeamOverlap = 0.04f;

        [Header("Corners")]
        [Tooltip("A corner within this many degrees of a right angle counts as square: its walls meet as a right angle and its roofs meet along a hip or valley, and a facade corner no wider than a right angle plus this gets gables, e.g. 20 treats an 80 degree corner as square. Higher = more corners treated as square; lower = more get the sharp-corner treatment.")]
        [SerializeField, Range(0f, 45f)] private float cornerAngleTolerance = 20f;
        [Tooltip("Two frontages' ends at a junction are joined at a street corner only if each needs carrying no further than this many metres to meet the other; otherwise each turns a side return into the branching street, e.g. 8 joins corners up to 8m apart. Higher = more corners joined; lower = more side returns.")]
        [SerializeField, Range(1f, 20f)] private float cornerMaxReach = 8f;
        [Tooltip("At a corner each side's walls run this share of the way from the other side's back line to its face, so both ends hide inside the other wall and the end posts meet, e.g. 0.5 = halfway into the other wall. 0 = stop at the other's back line, 1 = run to its face.")]
        [SerializeField, Range(0f, 1f)] private float cornerOverrun = 0.5f;

        [Header("Jetties")]
        [Tooltip("The chance, 0-1, that a facade building's upper storeys jut out over the street (by the jetty piece's depth, about 1.5m) on jetty supports, where it has an upper storey and a jetty piece fits its ground module, e.g. 0.3 = about a third of buildings jettied. Higher = more jettied buildings; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float jettyShare = 0.3f;
        [Tooltip("How far, in metres, the backs of the jetty supports sink into the ground storey's walls, behind their main front surface (the plaster or stone between the timbers), so no gap shows behind them; the storeys above stand back by the same, e.g. 0.05 = 5cm. Higher = supports sunk deeper; 0 = touching, a gap may show.")]
        [SerializeField, Range(0f, 0.3f)] private float jettyInset = 0.05f;
        [Tooltip("How far, in metres, a jettied building's first upper storey (its beam band and walls) stands down into the jetty supports' deck, so it cuts into the planks instead of resting on them with a seam, e.g. 0.1 = 10cm into the deck. Higher = sunk deeper; 0 = resting on the deck.")]
        [SerializeField, Range(0f, 0.3f)] private float jettyStoreyDrop = 0.1f;

        [Header("Lean")]
        [Tooltip("How far, in degrees, a facade's street walls lean out over the street from the first upper storey up: upper walls and beams tip forward as one plane, side walls moved and sheared with them; at a corner only one side leans, the other's end wall sheared to follow, e.g. 5 puts a three-storey top about 0.5m out. Roofs and dressing stay upright (a preview of the look). Higher = more crooked; 0 = upright.")]
        [SerializeField, Range(0f, 5f)] private float facadeLean;

        [Header("Doors")]
        [Tooltip("Every doorway in a placed wall gets a plain door (a verified door piece, never a gameplay door), scaled to the doorway and this many metres taller at the top, so its top tucks into the frame, e.g. 0.05 = 5cm hidden behind the lintel. Higher = more of the door hidden; 0 = exactly the doorway's height, a gap may show.")]
        [SerializeField, Range(0f, 0.3f)] private float doorInsertMargin = 0.05f;
        [Tooltip("How much wider a door is, in metres each side, than the doorway as seen through the wall (measured through its mesh, so a narrower stone frame counts), so its edges tuck behind the frame, e.g. 0.13 = 13cm hidden each side. Higher = edges hidden deeper; 0 = gaps may show at the sides.")]
        [SerializeField, Range(0f, 0.4f)] private float doorSideMargin = 0.13f;
        [Tooltip("How far, in metres, a doorway's door stands forward of the middle of the wall's depth; below 0, back from it, e.g. -0.1 sets doors 10cm deeper into the wall. 0 = in the middle.")]
        [SerializeField, Range(-0.5f, 0.5f)] private float doorInsertDepth;

        [Header("Window inserts")]
        [Tooltip("The chance, 0-1, that a window hole in a placed wall gets a window insert (a verified window piece) rather than staying open, e.g. 1 = every hole glazed, 0.5 = about half left open. 0 = all open.")]
        [SerializeField, Range(0f, 1f)] private float windowInsertShare = 1f;
        [Tooltip("How much bigger than the hole, in metres each side, an insert may be (its frame overlapping the wall round the hole); an insert smaller than the hole never fits, e.g. 0.45 lets a 1.9m-wide insert fill a 1.2m hole. Higher = more inserts fit, with bigger frames; lower = only close fits, more holes left open.")]
        [SerializeField, Range(0f, 1f)] private float windowInsertMaxMargin = 0.45f;
        [Tooltip("How far, in metres, an insert's middle stands forward of the middle of the wall's depth; below 0, back from it, e.g. 0.05 brings windows 5cm forward. 0 = in the middle.")]
        [SerializeField, Range(-0.5f, 0.5f)] private float windowInsertDepth;
        [Tooltip("The least distance, in metres, between two lit windows (inserts swapped for their Lit twin: panes glow, a baked spot spills light into the street), spread evenly over every window above the ground storey once all are placed, e.g. 10 = lit windows about 10-15m apart. Higher = fewer, darker streets; lower = more lit windows.")]
        [SerializeField, Range(2f, 50f)] private float litWindowSpacing = 10f;

        [Header("Columns")]
        [Tooltip("The fewest modules wide a column is: every facade building is cut end to end into columns of Column Width Min to Max modules at random (the last takes what is left), each with its own depth and storey count, e.g. 1 allows one-module columns. Lower = busier, more broken-up facades.")]
        [SerializeField, Range(1, 6)] private int columnWidthMin = 1;
        [Tooltip("The most modules wide a column is (see Column Width Min), e.g. 2 = columns of one or two modules. Higher = longer stretches of facade without a step; kept no less than Column Width Min.")]
        [SerializeField, Range(1, 6)] private int columnWidthMax = 2;
        [Tooltip("The chance, 0-1, that a column stands forward of, or back from, the facade's line for its full height, side walls filling the steps, e.g. 0.15 = about one column in seven stepped. Higher = a more uneven street front; 0 = every facade in line.")]
        [SerializeField, Range(0f, 1f)] private float columnShare = 0.15f;
        [Tooltip("A straight run of facade (between bends) needs at least this many modules to have columns off the line; a column holding its first or last module always stays on the line, e.g. 6 = only runs of six modules or more get steps. Higher = steps only on long runs; lower = steps on short runs too.")]
        [SerializeField, Range(3, 20)] private int columnMinRunModules = 6;
        [Tooltip("The least distance, in metres, a stepped column stands forward or back from the line: drawn at random between Column Depth Min and Max, kept to half a module so one side wall fills any step, e.g. 0.3 = steps of at least 30cm. Higher = no shallow steps.")]
        [SerializeField, Range(0.05f, 3f)] private float columnDepthMin = 0.3f;
        [Tooltip("The most distance, in metres, a stepped column stands forward or back from the line (see Column Depth Min); also how far a facade's pieces may reach into the street they face, e.g. 0.8 = steps of up to 80cm. Higher = deeper steps; kept no less than Column Depth Min.")]
        [SerializeField, Range(0.05f, 3f)] private float columnDepthMax = 0.8f;
        [Tooltip("The share, 0-1, of stepped columns that stand forward of the line (into the street); the rest stand back from it, e.g. 0.5 = half forward, half back. 1 = all forward, 0 = all back.")]
        [SerializeField, Range(0f, 1f)] private float columnOutShare = 0.5f;
        [Tooltip("The chance, 0-1, that a column stands taller or lower than its building's storey count (either equally likely), kept between Min and Max Storeys, e.g. 0.3 = about a third of columns differ in height. Higher = a more ragged roofline; 0 = every column of a building the same height.")]
        [SerializeField, Range(0f, 1f)] private float columnHeightShare = 0.3f;
        [Tooltip("How many storeys at most a column stands taller or lower than its building, drawn from 1 up to this, e.g. 2 lets a column rise or drop by one or two storeys. Higher = bigger height differences.")]
        [SerializeField, Range(1, 4)] private int columnStoreyVariation = 1;

        [Header("Dressing: pipes, valves, vents, walkways, lamp posts")]
        [Tooltip("The share, 0-1, of buildings that get pipes at all; the rest get none, e.g. 0.4 = about two buildings in five piped. Higher = more pipes about; 0 = no pipes.")]
        [SerializeField, Range(0f, 1f)] private float pipeBuildingShare = 0.4f;
        [Tooltip("The most runs of pipe one building gets: a piped building gets one run, and each further run up to this many comes at Pipe Extra Run Share, e.g. 2 = one or two runs. Higher = busier facades.")]
        [SerializeField, Range(1, 5)] private int pipeRunsMax = 2;
        [Tooltip("The chance, 0-1, of each run of pipe beyond a building's first (up to Pipe Runs Max), e.g. 0.35 = about a third of piped buildings get a second run. Higher = more runs; 0 = one run each.")]
        [SerializeField, Range(0f, 1f)] private float pipeExtraRunShare = 0.35f;
        [Tooltip("A building's runs of pipe together cover at least this share, 0-1, of its width along the street (runs are added until they do, as far as they can be placed), e.g. 0.5 = pipes along at least half a 9m facade. Higher = longer pipework; 0 = no minimum.")]
        [SerializeField, Range(0f, 1f)] private float pipeMinRunShare = 0.5f;
        [Tooltip("The share, 0-1, of runs laid in the large pipe kit rather than the small, e.g. 0.1 = one run in ten large. Has no effect while only the thin kit is verified.")]
        [SerializeField, Range(0f, 1f)] private float pipeLargeShare = 0.1f;
        [Tooltip("Clear space, in metres, between a wall's face and the pipe beside it, e.g. 0.05 = pipes stand 5cm off the plaster. Higher = pipes stand further out; 0 = touching.")]
        [SerializeField, Range(0f, 0.3f)] private float pipeWallGap = 0.05f;
        [Tooltip("How far, in metres, from a module's left end (seen from the street) a plumb run of pipe stands, on the plaster past the wall's end post; where that would cross a door or window it goes over the post at the joint instead, e.g. 0.6 = 60cm in from the post. Higher = further into the module.")]
        [SerializeField, Range(0.2f, 1.5f)] private float pipePanelInset = 0.6f;
        [Tooltip("Clear space, in metres, kept between a pipe (or vent) and any door or window, e.g. 0.25 = never within 25cm of an opening. Higher = pipes kept further off, fewer places for them.")]
        [SerializeField, Range(0f, 1f)] private float pipeOpeningMargin = 0.25f;
        [Tooltip("Where a level stretch of pipe ends in an elbow into the wall, the end lands at random within this share of its module's width about the module's middle, never at a joint between walls, e.g. 0.5 = in the middle half. 0 = always the middle, 1 = anywhere along it.")]
        [SerializeField, Range(0f, 1f)] private float pipeEndSpread = 0.5f;
        [Tooltip("The chance, 0-1, that a run of pipe gets a valve: a T-junction in a straight stretch, its branch pointing out into the street with the valve on it, e.g. 0.3 = about a third of runs. Higher = more valves; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float valveShare = 0.3f;
        [Tooltip("How high, in metres, above the street a valve on an upright stretch of pipe stands (the T-junction's middle), e.g. 1.3 = about chest height. Higher = valves further up the wall.")]
        [SerializeField, Range(0.5f, 3f)] private float valveHeight = 1.3f;
        [Tooltip("The chance, 0-1, of each vent a building gets, up to Vents Max; a vent stands on smooth wall anywhere on the facade, apart from the pipes, never over a door or window, e.g. 0.4 = each of a building's two tries comes up two times in five. Higher = more vents; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float ventShare = 0.4f;
        [Tooltip("The most vents one building gets, e.g. 2 = none, one or two. 0 = no vents.")]
        [SerializeField, Range(0, 4)] private int ventsMax = 2;
        [Tooltip("The share, 0-1, of vents that are the large vent rather than the small, e.g. 0.3 = about a third large (large ones rarely find room). 0 = all small.")]
        [SerializeField, Range(0f, 1f)] private float ventLargeShare = 0.3f;
        [Tooltip("How far in depth, in metres, the wall's drawn surface may vary under a vent for the wall to count as smooth there, e.g. 0.03 = within 3cm, so never across a timber. Higher = vents also sit over uneven wall; lower = only on very flat plaster.")]
        [SerializeField, Range(0.005f, 0.2f)] private float ventFlatTolerance = 0.03f;
        [Tooltip("The share, 0-1, of buildings with an upper storey that get a walkway along their facade: a row of the jetty piece at the base of one upper storey (never a jettied one), e.g. 0.3 = about a third. Higher = more walkways; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float walkwayShare = 0.3f;
        [Tooltip("Whether lamp posts are placed along the streets at all, e.g. off for a level lit only by hand-placed lights. On = posts placed per Lamp Spacing; off = no lamp posts, everything else (building colours included) generated as with them.")]
        [SerializeField] private bool placeLamps = true;
        [Tooltip("Metres of street frontage per lamp post on average, and the least distance between any two posts over every street; posts stand before plain ground walls facing the street, and two posts' lights never overlap, e.g. 20 = about one post per 20m, never closer. Higher = fewer, darker streets; 0 = no lamp posts.")]
        [SerializeField, Range(0f, 60f)] private float lampSpacing = 14f;
        [Tooltip("How far, in metres, into the street from its edge a lamp post stands, e.g. 0.6 = 60cm out from the facade line. Higher = posts further out into the street.")]
        [SerializeField, Range(0.3f, 2f)] private float lampEdgeInset = 0.6f;
        [Tooltip("How far, in metres, a lamp post is lowered into the ground, hiding its base in the cobbles, e.g. 0.2 = 20cm sunk. Higher = a shorter post; 0 = standing on the surface.")]
        [SerializeField, Range(0f, 0.5f)] private float lampSink = 0.2f;

        [Header("Smoke and steam (particle systems)")]
        [Tooltip("The smoke prefab placed at the top of a smoking chimney's pot, pointing up, e.g. Assets/Prefabs/Effects/Chimney Smoke. Left empty, no chimney smokes.")]
        [SerializeField] private GameObject chimneySmokePrefab;
        [Tooltip("The share, 0-1, of chimneys that smoke, e.g. 0.5 = half of them. Higher = more smoke (more particles drawn); 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float chimneySmokeShare = 0.5f;
        [Tooltip("The steam prefab placed at the face of a steaming vent, pointing out of the wall, e.g. Assets/Prefabs/Effects/Vent Steam. Left empty, no vent steams.")]
        [SerializeField] private GameObject ventSteamPrefab;
        [Tooltip("The share, 0-1, of vents that steam, e.g. 0.5 = half of them. Higher = more steam; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float ventSteamShare = 0.5f;

        [Header("Side walls")]
        [Tooltip("Where a column stands taller than the one beside it, each storey above the lower one gets a side wall running back from its front walls, so from the street it seems to go on behind; its length in modules is this times the column's width in modules, at least one, e.g. 1 = as long as the column is wide. Higher = deeper-looking buildings; 0 = no such side walls.")]
        [SerializeField, Range(0f, 5f)] private float sideWallMultiplier = 1f;
        [Tooltip("At a column's left end (seen from the street), where its front wall's end has its trim, the side walls tuck in behind it: their back plane this many metres inside the column from the join, their street end on the front wall's main surface, e.g. 0.12 = 12cm in for walls of the pack's depth, scaled with wallThickness (0.36: about 4cm), so the trim covers them and their face stands in front of a lower neighbour's gable on the join. Higher = tucked further in (a gable shows through above about 0.16 at 0.36). At the right end they stand outside, covering the plain end.")]
        [SerializeField, Range(0f, 1f)] private float sideWallTuck = 0.12f;

        [Header("Gaps")]
        [Tooltip("Free wall ends (a front wall's end nothing stands against, in view of a street) are closed before the street test and cull: two ends facing each other at one storey height no more than this many metres apart are bridged by one plain wall; an end with no partner gets a return wall running back, e.g. 3 bridges gaps up to 3m. Higher = wider gaps bridged; lower = more return walls.")]
        [SerializeField, Range(0.5f, 10f)] private float gapBridgeMax = 3f;
        [Tooltip("How far, in metres, two free ends' bottoms (and tops) may differ in height and still count as one storey, to be bridged, e.g. 0.3 bridges ends up to 30cm out of level. Higher = more pairs bridged, possibly crookedly; lower = more return walls.")]
        [SerializeField, Range(0f, 1f)] private float gapStoreyTolerance = 0.3f;

        [Header("Backing (black boxes behind the facades)")]
        [Tooltip("The material of the black boxes behind every facade column, eaves down, so a gap in the walls shows black instead of the void and lights behind a facade never reach its pieces from behind, e.g. Assets/Art/Materials/Environment/Backing.mat. Left empty, no boxes are built.")]
        [SerializeField] private Material backingMaterial;
        [Tooltip("How far back, in metres, from a column's walls its backing box reaches (less where a street is nearer behind), e.g. 3 = boxes 3m deep. Higher = covers deeper gaps; lower = shallower boxes.")]
        [SerializeField, Range(0.5f, 10f)] private float backingDepth = 3f;
        [Tooltip("How far, in metres, below the lowest street in front of a column its backing box reaches, hidden under the ground, e.g. 1 = 1m down. Higher = safer on sloping streets; 0 = stops at the street.")]
        [SerializeField, Range(0f, 3f)] private float backingBelow = 1f;
        [Tooltip("How far, in metres, a backing box keeps from every placed piece: it is cut back (front, back, ends, top or bottom, whichever loses least) until it touches none, e.g. 0.02 = 2cm clear. Higher = boxes kept further off, smaller; 0 = may touch pieces.")]
        [SerializeField, Range(0f, 0.2f)] private float backingClearance = 0.02f;

        [Tooltip("How far, in metres, a storey style's module width may differ from the ground storey's below it; every storey is laid at the ground storey's spacing, each wall width-scaled to fill its module, e.g. 0.3 lets 3m plaster stand on a 3.2m cellar. Beyond it the spec is an error (and corner-joined buildings keep their own storeys). Higher = more style mixes allowed, more stretching.")]
        [SerializeField, Range(0f, 1f)] private float maxStoreyWidthMismatch = 0.3f;

        [Header("Layout checks")]
        [Tooltip("How far, in metres, two footprints may overlap, or a frontage overhang its stretch of street, before it is reported; also the slack added to street clearance checks, e.g. 0.05 lets pieces touch by up to 5cm unreported. Higher = more forgiving checks; lower = stricter.")]
        [SerializeField, Range(0f, 0.5f)] private float overlapTolerance = 0.05f;
        [Tooltip("A street segment shorter than this many metres along the ground is a spec error, e.g. 0.1 rejects two points of a street 5cm apart. Higher = catches more near-duplicate points.")]
        [SerializeField, Range(0.01f, 1f)] private float minSegmentLength = 0.1f;
        [Tooltip("Two streets' points closer than this many metres along the ground are one junction; a street end with none within it is a dead end, e.g. 0.1 joins points within 10cm. Higher = sloppier specs still join; lower = points must match exactly.")]
        [SerializeField, Range(0.01f, 1f)] private float junctionDistance = 0.1f;
        [Tooltip("A street leaving a junction at least this many degrees off another street, towards one of its sides, branches off on that side; two streets end to end within it count as running straight on, e.g. 17.5 = a 10 degree kink is a bend, a 30 degree turn a branch. Higher = more joins count as bends; lower = more count as branches.")]
        [SerializeField, Range(1f, 89f)] private float branchAngle = 17.5f;
        [Tooltip("A street ending at a junction where another street runs on (a branch) must meet it within this many degrees of square, or the spec is an error, e.g. 20 allows 70 to 110 degrees. A bend where just two streets meet end to end is not a branch. Higher = more slanted branches allowed.")]
        [SerializeField, Range(0f, 89f)] private float branchSquareTolerance = 20f;

        [Header("Street clearance")]
        [Tooltip("A piece standing at least this many metres above the street its facade faces (a jetty, a beam, an upper storey stepping out) may reach out over it; lower down a piece may come into it only as far as a column steps forward, and into any other street only as far as a foundation's front, e.g. 2.2 lets jetties overhang above head height. Higher = more headroom kept, fewer overhangs.")]
        [SerializeField, Range(1f, 5f)] private float streetHeadroom = 2.2f;
        [Tooltip("The street test's ball radius, in metres: after generating, a ball travels down the middle of every street (up and down the steps) and the layout is valid only if it never touches anything built for the buildings, e.g. 0.5 = a 1m-wide ball. Higher = a stricter test, wider clear lanes needed; lower = more clutter passes.")]
        [SerializeField, Range(0.1f, 1.5f)] private float streetBallRadius = 0.5f;
        [Tooltip("How high, in metres, above the street surface the street test's ball centre travels, e.g. 1 = the ball spans from 0.5m to 1.5m up with a 0.5m radius. Higher = tests for low overhangs more than for clutter at the foot of the walls.")]
        [SerializeField, Range(0.3f, 2f)] private float streetBallHeight = 1f;

        [Header("Sharp corners (for layout specs)")]
        [Tooltip("A corner between two streets (the wedge between two leaving a junction, or the inside of a bend) narrower than this many degrees counts as sharp; the generator clears any angle, this only sets what counts as sharp, e.g. 60 = a 45 degree wedge is sharp, a 75 degree one isn't. Higher = more corners count as sharp.")]
        [SerializeField, Range(10f, 89f)] private float sharpCornerAngle = 60f;
        [Tooltip("The share, 0-1, of a layout's street corners meant to be sharp: Claude writes layout specs to it and the validator warns how far a spec is from it, e.g. 0.25 = about one corner in four sharp. Higher = more sharp corners wanted; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float sharpCornerShare = 0.25f;

        [Header("Ground (the streets' mesh)")]
        [Tooltip("The side of one square cell of the ground mesh's grid, in metres, e.g. 0.75 = a vertex every 75cm. Smaller = follows edges, slopes and paint more closely but costs more vertices; larger = cheaper, coarser.")]
        [SerializeField, Range(0.25f, 3f)] private float groundCell = 0.75f;
        [Tooltip("How far, in metres, the ground runs on past the streets' edges, under the buildings lining them, and past the ring round a lot; the boxes under flights' top steps reach as far past the edges, e.g. 1.5 = ground runs 1.5m under the facades. Higher = no gaps at branch mouths and dead ends, more hidden mesh.")]
        [SerializeField, Range(0f, 5f)] private float groundEdgeMargin = 1.5f;
        [Tooltip("Each street area's ground grid is turned at random so its cells lie at least this many degrees off the world's X and Z axes, never squared to them, e.g. 15 = the grid is turned 15 to 75 degrees. Higher = the grid kept further off the axes; 0 = may line up with them.")]
        [SerializeField, Range(0f, 45f)] private float groundGridMinAngle = 15f;
        [Tooltip("The ground is laid under each lot as a ring from its footprint out this many metres round it (as part of the nearest street area's mesh), reaching under its walls and foundations, never under the building itself, e.g. 1 = a 1m ring round a guild hall. Higher = a wider yard of ground round lots.")]
        [SerializeField, Range(0f, 20f)] private float lotGroundMargin = 1f;
        [Tooltip("How thick, in metres, the collider box under a flight's top step is (its top on the upper level); the upper ground's edge, run on under the top step's tread, sinks this far there, out of sight, e.g. 0.25 = 25cm. Higher = a thicker box, ground sunk deeper under the step.")]
        [SerializeField, Range(0.05f, 1f)] private float groundThickness = 0.25f;
        [Tooltip("How far, in metres, a flight of steps' pieces overlap side by side across the street, so no crack shows between them, e.g. 0.03 = 3cm. Higher = more overlap; 0 = edge to edge, cracks may show.")]
        [SerializeField, Range(0f, 0.2f)] private float groundOverlap = 0.03f;
        [Tooltip("How far, in metres, the upper level's collider box runs back over a flight's top step, under its tread, so the steps' ramp meets the ground with no gap for the NavMesh or the player's feet; the ground meshes run as far in under the lowest and top steps, so no slit shows, e.g. 0.3 = 30cm. Higher = more overlap; 0 = a gap may open.")]
        [SerializeField, Range(0f, 0.6f)] private float groundStepsOverlap = 0.3f;

        [Header("Ground paint (the vertex colours the shader blends)")]
        [Tooltip("How wide, in metres, the dirt border along every street edge is, measured from the foundations' front into the street (to the middle of the fade to cobbles), cobbles beyond, e.g. 0.75 = 75cm of dirt before the cobbles. Higher = wider dirt borders, less cobble; 0 = cobbles up to the foundations.")]
        [SerializeField, Range(0f, 3f)] private float groundDirtBorder = 0.75f;
        [Tooltip("How far, in metres, the noise moves the dirt border's inner edge in and out, e.g. 0.15 = the edge wanders up to 15cm. Higher = a more ragged, less even border; 0 = a straight edge.")]
        [SerializeField, Range(0f, 3f)] private float groundPaintWobble = 0.15f;
        [Tooltip("The size of the noise that wobbles the cobbles' edge, in metres per blob, e.g. 4 = the edge bulges every few metres. Higher = long, slow wobbles; lower = tight, busy ones.")]
        [SerializeField, Range(0.5f, 20f)] private float groundPaintScale = 4f;
        [Tooltip("How far, in metres, the paint fades from all cobbles to all dirt at the edge (the shader's height blend sharpens it at the pixels), e.g. 1 = over a metre. Higher = a softer, wider blend; lower = a crisp edge.")]
        [SerializeField, Range(0.1f, 3f)] private float groundPaintFade = 1f;
        [Tooltip("Roughly the share, 0-1, of the ground given to puddles of wet dirt (wherever a second noise rises into its top this share), anywhere on the street, e.g. 0.2 = about a fifth of the ground wet. Higher = more puddles; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float groundPuddleCover = 0.2f;
        [Tooltip("The size of the puddle noise, in metres per blob, e.g. 5 = puddles a few metres across. Higher = fewer, bigger puddles; lower = many small ones.")]
        [SerializeField, Range(0.5f, 20f)] private float groundPuddleScale = 5f;

        [Header("Foliage (grass, nettles)")]
        [Tooltip("The chance, 0-1, that each Grass Patch square of street ground gets a cluster of grass (verified grass pieces), drawn per patch, e.g. 0.05 = about one patch in twenty. Higher = grassier streets; 0 lays none.")]
        [SerializeField, Range(0f, 1f)] private float grassCover = 0.05f;
        [Tooltip("The side, in metres, of the square patches the ground is dealt out in for grass, each getting at most one cluster, e.g. 3 = one chance per 3 x 3m. Lower = more chances, so more grass at the same Grass Cover.")]
        [SerializeField, Range(1f, 10f)] private float grassPatch = 3f;
        [Tooltip("The fewest tufts of grass in a cluster, drawn between Grass Cluster Min and Max, e.g. 2. Higher = fuller clusters.")]
        [SerializeField, Range(1, 12)] private int grassClusterMin = 2;
        [Tooltip("The most tufts of grass in a cluster (see Grass Cluster Min), e.g. 4 = two to four tufts. Higher = fuller clusters; kept no less than Grass Cluster Min.")]
        [SerializeField, Range(1, 12)] private int grassClusterMax = 4;
        [Tooltip("How far, in metres, from a cluster's middle its tufts are scattered, e.g. 0.5 = within half a metre. Higher = looser clusters; 0 = all on one spot.")]
        [SerializeField, Range(0f, 2f)] private float grassClusterRadius = 0.5f;
        [Tooltip("How far, in metres, a grass cluster's middle keeps in from the street's edges and every lot's footprint (clear of foundations and stepped columns), and from flights of steps, e.g. 1 = never within 1m of the walls. Higher = grass only in the open middle; lower = grass up to the walls.")]
        [SerializeField, Range(0f, 3f)] private float grassEdgeMargin = 1f;
        [Tooltip("The fewest nettles a street gets (by its length), drawn between Nettles Per Street Min and Max; each stands at the foot of a foundation facing the street (verified nettle pieces), e.g. 2. Higher = more nettles.")]
        [SerializeField, Range(0, 20)] private int nettlesPerStreetMin = 2;
        [Tooltip("The most nettles a street gets (see Nettles Per Street Min), e.g. 4 = two to four per street. Higher = more nettles; kept no less than Nettles Per Street Min.")]
        [SerializeField, Range(0, 20)] private int nettlesPerStreetMax = 4;
        [Tooltip("How far, in metres, a nettle's middle stands out from the foundation's face: less than half the plant's width, so its back grows out of the joint, e.g. 0.2 = 20cm out. Higher = further into the street.")]
        [SerializeField, Range(0f, 0.5f)] private float nettleOut = 0.2f;
        [Tooltip("No two nettles stand closer than this many metres, e.g. 2 = at least 2m apart. Higher = more spread out, fewer fit; 0 = may bunch up.")]
        [SerializeField, Range(0f, 10f)] private float nettleSpacing = 2f;
        [Tooltip("No nettle stands within this many metres of a doorway's sides, e.g. 0.6 keeps doorsteps clear. Higher = wider clear space round doors.")]
        [SerializeField, Range(0f, 2f)] private float nettleDoorClear = 0.6f;
        [Tooltip("The smallest a tuft of grass or a nettle is scaled, drawn between Foliage Scale Min and Max so no two look alike, e.g. 0.8 = down to 80% size. Lower = more size variety.")]
        [SerializeField, Range(0.5f, 1.5f)] private float foliageScaleMin = 0.8f;
        [Tooltip("The largest a tuft of grass or a nettle is scaled (see Foliage Scale Min), e.g. 1.2 = up to 120% size. Higher = bigger plants; kept no less than Foliage Scale Min.")]
        [SerializeField, Range(0.5f, 1.5f)] private float foliageScaleMax = 1.2f;
        [Tooltip("How far, in metres, each piece of foliage is sunk below the ground's surface, so its roots hide in the stones rather than float over them, e.g. 0.03 = 3cm. Higher = sunk deeper; 0 = sitting on the surface.")]
        [SerializeField, Range(0f, 0.1f)] private float foliageSink = 0.03f;

        [Header("Steps and slopes (streets changing height)")]
        [Tooltip("A segment rising no more steeply than this (rise over length) is climbed by a slope in the ground mesh; a steeper one needs whole flights of steps, e.g. 0.083 = 1 in 12, about 4.8 degrees. Higher = steeper slopes allowed, fewer steps; lower = more rises become steps.")]
        [SerializeField, Range(0.01f, 0.25f)] private float streetSlopeMaxGrade = 0.083f;
        [Tooltip("A building beside a slope stands level at the highest the street comes under it, foundations stacked down; the validator warns of a building whose street falls more than this many metres below its level (one foundation course is about a metre), e.g. 0.6 warns of a long building on a steep hill. Higher = fewer warnings, taller stone cheeks allowed.")]
        [SerializeField, Range(0.1f, 2f)] private float slopeDropMax = 0.6f;

        [Header("Roofs")]
        [Tooltip("The roofs' pitch, in degrees from level, e.g. 60 = steep storybook roofs (the pack's own lie between about 52 and 68). Higher = steeper, taller roofs; lower = flatter ones (below about 55 a dormer's back corner shows).")]
        [SerializeField, Range(30f, 75f)] private float roofPitch = 60f;
        [Tooltip("Rows of tile panels up a facade's roof from eave to ridge (the top row carries the ridge cap); a lot's roof takes as many as reach its middle, e.g. 2 = an eave row and a ridge row. Higher = taller, deeper roofs.")]
        [SerializeField, Range(1, 4)] private int roofRows = 2;
        [Tooltip("How far, in metres, a roof's eave stands out in front of the face of the top storey's walls, e.g. 0.4 = 40cm overhang. Higher = deeper eaves shading the street; 0 = flush with the wall.")]
        [SerializeField, Range(0f, 2f)] private float roofEaveOverhang = 0.4f;
        [Tooltip("How far, in metres, a roof's eave lies below the top of the walls, so the walls' top is hidden under the roof; the backing boxes and sound rooms also stop this far under the walls' top, e.g. 1 = the eave 1m below the wall top. Higher = roofs sit lower on the walls.")]
        [SerializeField, Range(0f, 2f)] private float roofEaveDrop = 1f;
        [Tooltip("How far, in metres, a roof's tiles run on past its end (the gable) at each end, e.g. 0.2 = 20cm verge overhang. Higher = longer overhangs; 0 = flush with the end wall.")]
        [SerializeField, Range(0f, 1.5f)] private float roofVergeOverhang = 0.2f;
        [Tooltip("How far, in metres, one row of tile panels overlaps the row below it, up the slope, e.g. 0.25 = 25cm. Higher = more overlap, a shorter roof; too low and gaps show between rows.")]
        [SerializeField, Range(0f, 1f)] private float roofRowOverlap = 0.25f;
        [Tooltip("How far, in metres, neighbouring tile panels overlap along a row: enough to cover a panel's verge end, whose top tiles are set back about 0.3m, where it faces the next panel, e.g. 0.45 = 45cm. Lower than about 0.3 and gaps show at the joints.")]
        [SerializeField, Range(0f, 0.6f)] private float roofPanelOverlap = 0.45f;
        [Tooltip("How far, in metres, a gable's base sinks below the top of the walls it stands on, e.g. 0.05 = 5cm, so no seam shows. Higher = sunk deeper; 0 = resting on the wall top.")]
        [SerializeField, Range(0f, 0.3f)] private float roofGableSink = 0.05f;
        [Tooltip("How far, in metres, the lowest piece of a hip strip (Roof_PeakedBase pieces stacked up a hipped corner) stands out of the corner along each slope's front, so the walls' corner stays inside it, e.g. 0.75 = 75cm out. Higher = the strip's foot stands further out; too low and the wall corner pokes through.")]
        [SerializeField, Range(0f, 1.5f)] private float roofHipOut = 0.75f;
        [Tooltip("How far, in metres, the lowest piece of a hip strip stands above the eaves, e.g. 0.2 = 20cm up. Higher = the strip sits higher on the corner.")]
        [SerializeField, Range(0f, 1.5f)] private float roofHipRaise = 0.2f;
        [Tooltip("The most, in metres, one piece of a hip strip rises above the one below it; the strip takes as many pieces as reach eaves to ridge in such steps, overlapping like scales, e.g. 2.5 = three pieces on a two-row roof. Lower = more, more overlapped pieces; higher = fewer.")]
        [SerializeField, Range(0.5f, 4f)] private float roofHipStep = 2.5f;
        [Tooltip("How far back, in degrees, each piece of a hip strip leans (its outer corner up), so its faces, steeper than the roofs' pitch, lie flatter on the slopes, e.g. 10. Higher = flatter on the roof; 0 = the piece as modelled.")]
        [SerializeField, Range(0f, 25f)] private float roofHipLean = 10f;
        [Tooltip("The sharpest corner (angle between its two eaves, in degrees) whose roofs meet along a hip under a hip strip; a sharper one's roofs end in gables and overlap, e.g. 50 = a 45 degree corner gets gables. Lower = hips on sharper corners (the strip squeezed narrower).")]
        [SerializeField, Range(30f, 90f)] private float roofHipAngleMin = 50f;
        [Tooltip("The widest corner (angle between its two eaves, in degrees) whose roofs meet along a hip under a hip strip; a wider one's roofs end in gables and overlap (a near-straight bend would need a piece many times wider than tall), e.g. 135 = a 150 degree bend gets gables. Higher = hips on gentler bends, the strip stretched wider.")]
        [SerializeField, Range(90f, 170f)] private float roofHipAngleMax = 135f;
        [Tooltip("How much of a building's roof, about its middle, a chimney or dormer may stand along, e.g. 0.5 = anywhere in the middle half. 0 = always at the middle, 1 = anywhere along the roof.")]
        [SerializeField, Range(0f, 1f)] private float roofFeatureSpread = 0.5f;
        [Tooltip("A building's roof gets a chimney or dormer only where it is at least this many tile panels long (as the top row counts them), e.g. 2 = a one-panel roof gets none. Higher = only long roofs get features.")]
        [SerializeField, Range(1, 6)] private int roofFeatureMinPanels = 2;
        [Tooltip("How far, in metres along the slope, the bottom of a dormer's front wall sits up the front slope from the eave; its back is sunk into the slope, e.g. 2 = 2m up. Higher = dormers further up (refused, and a chimney placed instead, if it would top the ridge).")]
        [SerializeField, Range(0f, 6f)] private float roofDormerUp = 2f;
        [Tooltip("How far, in metres, below the tiles' surface the bottom of a dormer's front wall sits, so no gap shows under it, e.g. 0.1 = 10cm. Higher = sunk deeper; 0 = on the tiles, a gap may show.")]
        [SerializeField, Range(0f, 0.5f)] private float roofDormerSink = 0.1f;
        [Tooltip("The least distance, in metres along the slope from the eave, the bottom of a chimney's front face sits up the front slope (drawn between Roof Chimney Up and Up Max; its back sunk into the slope, never past the ridge), e.g. 4 = at least 4m up. Higher = chimneys nearer the ridge.")]
        [SerializeField, Range(0f, 8f)] private float roofChimneyUp = 4f;
        [Tooltip("The most distance, in metres along the slope, a chimney's front face sits up from the eave (see Roof Chimney Up); capped so its back never passes the ridge, e.g. 8 = between 4 and 8m up. Higher = more spread in chimney positions.")]
        [SerializeField, Range(0f, 12f)] private float roofChimneyUpMax = 8f;
        [Tooltip("How far, in metres, below the tiles' surface the bottom of a chimney's front face sits, so no gap shows under it, e.g. 0.15 = 15cm. Higher = sunk deeper; 0 = on the tiles.")]
        [SerializeField, Range(0f, 0.5f)] private float roofChimneySink = 0.15f;
        [Tooltip("The least, in metres, a chimney's top stands above the tiles at its back face (drawn between Roof Chimney Rise and Rise Max); it is stretched up, never shrunk, to reach it, e.g. 0.8 = at least 80cm clear. Higher = taller chimneys.")]
        [SerializeField, Range(0f, 3f)] private float roofChimneyRise = 0.8f;
        [Tooltip("The most, in metres, a chimney's top stands above the tiles at its back face (see Roof Chimney Rise), so some stand clear above the ridge, e.g. 2.5 = from 0.8 to 2.5m. Higher = some much taller chimneys.")]
        [SerializeField, Range(0f, 5f)] private float roofChimneyRiseMax = 2.5f;
        [Tooltip("The share, 0-1, of buildings whose first roof gets iron arrow railings along its ridge, one draw per building, e.g. 0.5 = about half the roofs. Higher = more railings; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float roofRailingShare = 0.5f;
        [Tooltip("How far, in metres, a ridge railing's feet sink below the top of the roof under them (the ridge cap, found by a ray), so they stand in the tiles rather than float on the cap, e.g. 0.15 = 15cm. Higher = shorter-looking railings; 0 = standing on the cap.")]
        [SerializeField, Range(0f, 0.6f)] private float roofRailingSink = 0.15f;
        [Tooltip("The most, in degrees, a ridge railing leans along the ridge (and a third as much across it), drawn at random for each: storybook-crooked, e.g. 12 = up to 12 degrees. Higher = more crooked; 0 = all upright.")]
        [SerializeField, Range(0f, 25f)] private float roofRailingLean = 12f;
        [Tooltip("How far, in metres, in from each end of the ridge the railings keep (at a corner, from where the ridges meet), e.g. 0.4 = 40cm clear at each end. Higher = shorter railed stretches.")]
        [SerializeField, Range(0f, 2f)] private float roofRailingInset = 0.4f;
        [Tooltip("The gap, in metres, between one ridge railing and the next, e.g. 1 = a metre between railings. Higher = fewer, sparser railings; 0 = end to end.")]
        [SerializeField, Range(0f, 2f)] private float roofRailingGap = 1f;

        [Header("Visibility cull (last of all)")]
        [Tooltip("The visibility cull walks every street (up and down the steps) looking from this many metres above the ground, along the middle and both edges, and removes every generated piece (walls, beams, pipes, walkways, roofs, foliage; never lamp posts or the ground mesh) it can't see from anywhere, e.g. 1.6 = standing eye height. Higher = sees over more, removes less.")]
        [SerializeField, Range(1f, 3f)] private float cullEyeHeight = 1.6f;
        [Tooltip("The cull also looks from this many metres above the middle of the street, for the roofs, e.g. 9 = about rooftop height of a three-storey street. Higher = more roof slopes seen and kept; lower = more back slopes removed.")]
        [FormerlySerializedAs("roofCullHeight")]
        [SerializeField, Range(2f, 30f)] private float cullHeight = 9f;
        [Tooltip("How far, in metres, the cull moves along a street between looks, e.g. 2 = a look every 2m. Lower = more thorough (fewer wrongly removed pieces) but slower; higher = faster, may remove pieces seen only from between looks.")]
        [FormerlySerializedAs("roofCullStep")]
        [SerializeField, Range(0.5f, 10f)] private float cullStep = 2f;
        [Tooltip("The farthest, in metres, the cull looks: a piece further than this from every look is removed, e.g. 80 = pieces only visible from over 80m away go. Higher = keeps more far-seen pieces, slower; lower = removes more.")]
        [FormerlySerializedAs("roofCullRange")]
        [SerializeField, Range(10f, 300f)] private float cullRange = 80f;
        [Tooltip("Roof pieces facing the void beyond the level lose their colliders: how far, in metres, past the furthest point of a piece's building the empty space must start, e.g. 1.5. Higher = only roofs well clear of others count as facing the void.")]
        [SerializeField, Range(0f, 10f)] private float roofEdgeGap = 1.5f;
        [Tooltip("How far out, in metres, the space beyond a roof piece's building must be empty (no Environment collider, from the piece's top down) for the piece to lose its colliders, e.g. 20 = nothing within 20m. Higher = fewer roofs lose colliders; lower = more do.")]
        [SerializeField, Range(2f, 100f)] private float roofEdgeReach = 20f;

        [Header("Building proxies")]
        [Tooltip("On: Rebuild Building Proxies draws each building from further off as one renderer (every piece's second level of detail merged, atlased, lightmapped) through one LODGroup per building. Off: no proxies, every building drawn piece by piece at every distance.")]
        [SerializeField] private bool buildingProxies = true;
        [Tooltip("The share of the view's height a building fills below which its proxy is drawn instead of its pieces' full detail, e.g. 0.2 = about 25m off for a 12m building (the pack's own would be 0.3, about 17m). Higher = switches nearer (cheaper, baked shadow detail may visibly pop); lower = switches further off. Kept above Proxy Cull Share.")]
        [SerializeField, Range(0.02f, 1f)] private float lowerDetailShare = 0.2f;
        [Tooltip("The share of the view's height a building fills below which nothing of it is drawn, e.g. 0.04 = gone beyond about 120m for a 12m building (a seventh of Lower Detail Share, as the pack culls). Higher = culled nearer, cheaper; 0 = never culled. Kept below Lower Detail Share.")]
        [SerializeField, Range(0f, 0.5f)] private float proxyCullShare = 0.04f;
        [Tooltip("Scale In Lightmap of each building's proxy, its share of the level's lightmap resolution; a proxy's ~1500-2000 charts each need about 15-20 texels to bake clean, e.g. 0.6 = about 30000 texels a building at 10 texels/m. Higher = cleaner bake, bigger lightmaps; lower = invalid texels and dark buildings.")]
        [SerializeField, Range(0.05f, 2f)] private float proxyLightmapScale = 0.6f;
        [Tooltip("Pushoff, in metres, of the proxies' own Lightmap Parameters: how far off the surface the bake starts each ray from a proxy's texels, so the full detail's relief (still in the bake, millimetres away) doesn't block them and bake the proxy darker, e.g. 0.03 = 3cm. Higher = brighter, but light leaks into corners; lower = proxies bake darker than their full detail.")]
        [SerializeField, Range(0f, 0.2f)] private float proxyBakePushoff = 0.03f;
        [Tooltip("Width and height, in pixels, of each of the sector's three proxy atlases (colour, normal, metallic/occlusion/smoothness); each plain opaque material gets one tile, holding (size / tile)² materials, the rest kept apart, e.g. 4096 with 512 tiles = 64 materials, about 50 MB a sector. Higher = more materials atlased, more memory.")]
        [SerializeField, Range(512, 8192)] private int proxyAtlasSize = 4096;
        [Tooltip("Pixels across each material's tile in the proxy atlases, padding included; a proxy is seen from about 25m on, where some 40 pixels cover a metre, e.g. 512 is plenty for a texture covering a few metres. Higher = sharper proxies, fewer materials fit; lower = blurrier, more fit.")]
        [SerializeField, Range(64, 2048)] private int proxyAtlasTile = 512;
        [Tooltip("Pixels round each tile's picture filled with its edge pixels, so smaller mip levels don't blend one tile into its neighbour; each mip level halves it, e.g. 16 holds to the fourth mip. Higher = safe to smaller mips, less picture per tile; 0 = tiles bleed at a distance.")]
        [SerializeField, Range(0, 64)] private int proxyAtlasPadding = 16;

        [Header("Open window interiors")]
        [Tooltip("The floor tile laid, scaled to fit, as each open window interior's floor and (turned over) ceiling; it needs a verified floor annotation for its walking surface, e.g. TL_Platforms_Planks_Ground. Left empty (or not catalogued and annotated with a size), Build Open Window Interiors logs an error and builds nothing.")]
        [SerializeField] private GameObject openWindowFloorTile;
        [Tooltip("How far, in metres, neighbouring floor and ceiling tiles overlap each other, both ways, e.g. 0.03 = 3cm. Higher = more overlap; 0 = edge to edge, cracks may show.")]
        [SerializeField, Range(0f, 0.5f)] private float openWindowTileOverlap = 0.03f;
        [Tooltip("How far, in metres, an open window interior's floor surface stands above the window's base; negative = below, e.g. -0.1 lowers the floor 10cm under the sill line. 0 = level with the window's base.")]
        [SerializeField, Range(-1f, 1f)] private float openWindowFloorOffset;
        [Tooltip("How far, in metres, an open window interior's ceiling underside stands above its walls' top; negative = below, e.g. -0.05 hides the walls' top edge behind the ceiling. 0 = level with the walls' top.")]
        [SerializeField, Range(-1f, 1f)] private float openWindowCeilingOffset;
        [Tooltip("Width and height, in metres, of the sound portal laid in each open window, joining the interior's sound room to the street's, e.g. (1.2, 1.3) = the window hole's size. Bigger = sound passes over a wider opening.")]
        [SerializeField] private Vector2 openWindowPortalSize = new(1.2f, 1.3f);
        [Tooltip("Colour of the Baked point light hung in each open window interior, e.g. FFD88A = warm candle yellow. Warmer or cooler to taste; it lights only the bake.")]
        [SerializeField] private Color openWindowLightColour = new(1f, 0.847f, 0.541f);
        [Tooltip("Intensity of each open window interior's Baked point light, e.g. 1. Higher = a brighter room in the bake; 0 = dark.")]
        [SerializeField, Range(0f, 10f)] private float openWindowLightIntensity = 1f;
        [Tooltip("Indirect Multiplier of each open window interior's Baked point light: how much its light bounces round the room in the bake, e.g. 2 = twice the physical bounce. Higher = brighter, softer corners; 0 = direct light only.")]
        [SerializeField, Range(0f, 10f)] private float openWindowLightIndirect = 2f;
        [Tooltip("How far, in metres, each open window interior's light hangs below its ceiling, over the room's middle, e.g. 0.3 = 30cm down. Higher = lower in the room; 0 = on the ceiling.")]
        [SerializeField, Range(0f, 2f)] private float openWindowLightBelowCeiling = 0.3f;
        [Tooltip("The light's range as a share of the distance from it to the room's furthest floor corner, so the light just reaches the corners, e.g. 1.674 = range 10m in a 9 x 6m room 3m high. Higher = brighter corners; below about 1.3 the corners fall dark.")]
        [SerializeField, Range(1f, 3f)] private float openWindowLightRangeShare = 1.674f;

        [Header("Moon shafts")]
        [Tooltip("Streets whose base width is no more than this many metres get moon shafts: one per building casting a moon shadow onto the street, rising from the lit strip of street beside that shadow to above the building, e.g. 6 = every street of the default 5m width. Lower = only narrower streets; 0 places none.")]
        [SerializeField, Range(0f, 10f)] private float moonShaftStreetMaxWidth = 4.5f;
        [Tooltip("How far apart, in metres, along a street the moon's shadow is measured across it, e.g. 1 = every metre. Lower = shafts follow the shadow's ends more closely (more rays at generation); higher = coarser ends.")]
        [SerializeField, Range(0.25f, 5f)] private float moonShaftSampleSpacing = 1f;
        [Tooltip("How far, in metres, in from each edge of the street the shadow is measured, so its rays never start inside the walls, e.g. 0.3 = 30cm. Higher = shafts stop further short of the walls; 0 = rays may start in the walls.")]
        [SerializeField, Range(0f, 1.5f)] private float moonShaftInset = 0.3f;
        [Tooltip("How far, in metres, a shaft reaches into the sky above the roof line (the top of the building casting its shadow), e.g. 100 = the beam runs on 100m above the roofs. Higher = longer beams into the sky; lower = beams stopping just over the roofs.")]
        [SerializeField, Range(0f, 500f)] private float moonShaftTopClearance = 100f;

        [Header("Sound rooms")]
        [Tooltip("A street whose spec width (its own, or Street Width when 0) is no more than this many metres echoes as an alley; a wider one, and its junctions, as outdoors, e.g. 4.5 = a 4m lane has alley reverb, a 5m street open-air. Higher = more streets echo as alleys.")]
        [SerializeField, Range(0f, 10f)] private float soundAlleyMaxWidth = 4.5f;
        [Tooltip("How much sound is muffled, 0-1, passing through a junction's mouth into a street (the open muffle of the portals between them); mild, as nothing stands in the way, and a sound heard straight through the mouth isn't muffled at all, e.g. 0.15 = a slight dulling. Higher = more muffled round corners; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float soundStreetMuffle = 0.15f;
        [Tooltip("How much sound is muffled, 0-1, passing up out of a street to the rooftops or down into one (the open muffle of the portal over each street room), e.g. 0.3 = two streets hear each other over the roofs, muffled twice. Higher = more muffled; 0 = none.")]
        [SerializeField, Range(0f, 1f)] private float soundSkyMuffle = 0.3f;
        [Tooltip("A street room reaches at least this many metres above the highest its street comes, whatever the eaves along it, e.g. 3 = at least 3m tall. Higher = taller rooms.")]
        [SerializeField, Range(2f, 10f)] private float soundRoomMinHeight = 3f;
        // Street rooms play it at the Sound Player's Ambience Volume (no
        // override), so one number sets every street's loudness.
        [Tooltip("The background loop heard in every generated street room (the cue's first clip), played at the Sound Player's Ambience Volume, e.g. the AmbienceStreets cue (ambience_loop.ogg). Left empty, the streets are silent.")]
        [SerializeField] private Core.SoundCue soundStreetAmbience;

        [Header("Decking (wooden streets, walkways, bridges)")]
        [Tooltip("How far, in metres, neighbouring decking tiles overlap, along a row and between rows, e.g. 0.03 = 3cm. Higher = more overlap; 0 = edge to edge, cracks may show.")]
        [SerializeField, Range(0f, 0.2f)] private float deckingOverlap = 0.03f;
        [Tooltip("On: decking tiles are laid a quarter turn from the street (use it if the boards run along the walkway rather than across it). Off: tiles laid as modelled, rows across the street.")]
        [SerializeField] private bool deckingQuarterTurn;
        [Tooltip("The least share of a tile each decking row slides along from the row before (drawn between Decking Slide Min and Max; the first row unslid), e.g. 0.2 = at least a fifth of a tile. 0 and 0 keeps the rows' ends in line.")]
        [SerializeField, Range(0f, 1f)] private float deckingSlideMin;
        [Tooltip("The most share of a tile each decking row slides along from the row before (see Decking Slide Min), e.g. 0.5 = up to half a tile, so ends stagger like real boards. Higher = more staggered; kept no less than Decking Slide Min.")]
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
        internal float WallThickness => wallThickness;
        internal float FoundationScale => foundationScale;
        internal float FoundationHeight => foundationHeight;
        internal float FoundationFrontOffset => foundationFrontOffset;
        internal float FoundationBury => foundationBury;
        internal float FoundationColliderTolerance => foundationColliderTolerance;
        internal float WallColliderTolerance => wallColliderTolerance;
        internal float RoofColliderTolerance => roofColliderTolerance;
        internal float GroundDoorShare => groundDoorShare;
        internal float GroundWindowShare => groundWindowShare;
        internal float UpperWindowShare => upperWindowShare;
        internal float StoreyBeamOverlap => storeyBeamOverlap;
        internal float JettyShare => jettyShare;
        internal float JettyInset => jettyInset;
        internal float JettyStoreyDrop => jettyStoreyDrop;
        internal float FacadeLean => facadeLean;
        internal float CornerAngleTolerance => cornerAngleTolerance;
        internal float CornerMaxReach => cornerMaxReach;
        internal float CornerOverrun => cornerOverrun;
        internal float DoorInsertMargin => doorInsertMargin;
        internal float DoorSideMargin => doorSideMargin;
        internal float DoorInsertDepth => doorInsertDepth;
        internal float WindowInsertShare => windowInsertShare;
        internal float WindowInsertMaxMargin => windowInsertMaxMargin;
        internal float WindowInsertDepth => windowInsertDepth;
        internal float LitWindowSpacing => litWindowSpacing;
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
        internal float GapBridgeMax => gapBridgeMax;
        internal float GapStoreyTolerance => gapStoreyTolerance;
        internal Material BackingMaterial => backingMaterial;
        internal float BackingDepth => backingDepth;
        internal float BackingBelow => backingBelow;
        internal float BackingClearance => backingClearance;
        internal float PipeBuildingShare => pipeBuildingShare;
        internal int PipeRunsMax => pipeRunsMax;
        internal float PipeExtraRunShare => pipeExtraRunShare;
        internal float PipeMinRunShare => pipeMinRunShare;
        internal float PipeLargeShare => pipeLargeShare;
        internal float PipeWallGap => pipeWallGap;
        internal float PipePanelInset => pipePanelInset;
        internal float PipeOpeningMargin => pipeOpeningMargin;
        internal float PipeEndSpread => pipeEndSpread;
        internal float ValveShare => valveShare;
        internal float ValveHeight => valveHeight;
        internal float VentShare => ventShare;
        internal int VentsMax => ventsMax;
        internal float VentLargeShare => ventLargeShare;
        internal float VentFlatTolerance => ventFlatTolerance;
        internal GameObject ChimneySmokePrefab => chimneySmokePrefab;
        internal float ChimneySmokeShare => chimneySmokeShare;
        internal GameObject VentSteamPrefab => ventSteamPrefab;
        internal float VentSteamShare => ventSteamShare;
        internal float WalkwayShare => walkwayShare;
        internal bool PlaceLamps => placeLamps;
        internal float LampSpacing => lampSpacing;
        internal float LampEdgeInset => lampEdgeInset;
        internal float LampSink => lampSink;
        internal float OverlapTolerance => overlapTolerance;
        internal float MinSegmentLength => minSegmentLength;
        internal float JunctionDistance => junctionDistance;
        // The branch test compares the leaving direction with the side's
        // normal, so the angle off the street is its sine.
        internal float BranchDot => Mathf.Sin(branchAngle * Mathf.Deg2Rad);
        internal float BranchAngle => branchAngle;
        internal float BranchSquareTolerance => branchSquareTolerance;
        internal float StreetHeadroom => streetHeadroom;
        internal float StreetBallRadius => streetBallRadius;
        internal float StreetBallHeight => streetBallHeight;
        internal float SharpCornerAngle => sharpCornerAngle;
        internal float SharpCornerShare => sharpCornerShare;
        internal float GroundCell => groundCell;
        internal float GroundEdgeMargin => groundEdgeMargin;
        internal float GroundGridMinAngle => groundGridMinAngle;
        internal float LotGroundMargin => lotGroundMargin;
        internal float GroundThickness => groundThickness;
        internal float GroundOverlap => groundOverlap;
        internal float GroundStepsOverlap => groundStepsOverlap;
        internal float GroundDirtBorder => groundDirtBorder;
        internal float GroundPaintWobble => groundPaintWobble;
        internal float GroundPaintScale => groundPaintScale;
        internal float GroundPaintFade => groundPaintFade;
        internal float GroundPuddleCover => groundPuddleCover;
        internal float GroundPuddleScale => groundPuddleScale;
        internal float GrassCover => grassCover;
        internal float GrassPatch => grassPatch;
        internal int GrassClusterMin => grassClusterMin;
        internal int GrassClusterMax => grassClusterMax;
        internal float GrassClusterRadius => grassClusterRadius;
        internal float GrassEdgeMargin => grassEdgeMargin;
        internal int NettlesPerStreetMin => nettlesPerStreetMin;
        internal int NettlesPerStreetMax => nettlesPerStreetMax;
        internal float NettleOut => nettleOut;
        internal float NettleSpacing => nettleSpacing;
        internal float NettleDoorClear => nettleDoorClear;
        internal float FoliageScaleMin => foliageScaleMin;
        internal float FoliageScaleMax => foliageScaleMax;
        internal float FoliageSink => foliageSink;
        internal float StreetSlopeMaxGrade => streetSlopeMaxGrade;
        internal float SlopeDropMax => slopeDropMax;
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
        internal float RoofHipAngleMin => roofHipAngleMin;
        internal float RoofHipAngleMax => roofHipAngleMax;
        internal float RoofFeatureSpread => roofFeatureSpread;
        internal int RoofFeatureMinPanels => roofFeatureMinPanels;
        internal float RoofDormerUp => roofDormerUp;
        internal float RoofDormerSink => roofDormerSink;
        internal float RoofChimneyUp => roofChimneyUp;
        internal float RoofChimneyUpMax => roofChimneyUpMax;
        internal float RoofChimneySink => roofChimneySink;
        internal float RoofChimneyRise => roofChimneyRise;
        internal float RoofChimneyRiseMax => roofChimneyRiseMax;
        internal float RoofRailingShare => roofRailingShare;
        internal float RoofRailingSink => roofRailingSink;
        internal float RoofRailingLean => roofRailingLean;
        internal float RoofRailingInset => roofRailingInset;
        internal float RoofRailingGap => roofRailingGap;
        internal float CullEyeHeight => cullEyeHeight;
        internal float CullHeight => cullHeight;
        internal float CullStep => cullStep;
        internal float CullRange => cullRange;
        internal float RoofEdgeGap => roofEdgeGap;
        internal float RoofEdgeReach => roofEdgeReach;
        internal bool BuildingProxies => buildingProxies;
        internal float LowerDetailShare => lowerDetailShare;
        internal float ProxyLightmapScale => proxyLightmapScale;
        internal float ProxyBakePushoff => proxyBakePushoff;
        internal float ProxyCullShare => proxyCullShare;
        internal int ProxyAtlasSize => proxyAtlasSize;
        internal int ProxyAtlasTile => proxyAtlasTile;
        internal int ProxyAtlasPadding => proxyAtlasPadding;
        internal GameObject OpenWindowFloorTile => openWindowFloorTile;
        internal float OpenWindowTileOverlap => openWindowTileOverlap;
        internal float OpenWindowFloorOffset => openWindowFloorOffset;
        internal float OpenWindowCeilingOffset => openWindowCeilingOffset;
        internal Vector2 OpenWindowPortalSize => openWindowPortalSize;
        internal Color OpenWindowLightColour => openWindowLightColour;
        internal float OpenWindowLightIntensity => openWindowLightIntensity;
        internal float OpenWindowLightIndirect => openWindowLightIndirect;
        internal float OpenWindowLightBelowCeiling => openWindowLightBelowCeiling;
        internal float OpenWindowLightRangeShare => openWindowLightRangeShare;
        internal float MoonShaftStreetMaxWidth => moonShaftStreetMaxWidth;
        internal float MoonShaftSampleSpacing => moonShaftSampleSpacing;
        internal float EdgeWallProbe => edgeWallProbe;
        internal float MoonShaftInset => moonShaftInset;
        internal float MoonShaftTopClearance => moonShaftTopClearance;
        internal float SoundAlleyMaxWidth => soundAlleyMaxWidth;
        internal float SoundStreetMuffle => soundStreetMuffle;
        internal float SoundSkyMuffle => soundSkyMuffle;
        internal float SoundRoomMinHeight => soundRoomMinHeight;
        internal Core.SoundCue SoundStreetAmbience => soundStreetAmbience;
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
            grassClusterMax = Mathf.Max(grassClusterMin, grassClusterMax);
            nettlesPerStreetMax = Mathf.Max(nettlesPerStreetMin, nettlesPerStreetMax);
            foliageScaleMax = Mathf.Max(foliageScaleMin, foliageScaleMax);
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
