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
        [Tooltip("The chance that a ground storey module is a door wall, where its style has one; a building's door module in the spec is always a door.")]
        [SerializeField, Range(0f, 1f)] private float groundDoorShare = 0.1f;
        [Tooltip("The chance that a ground storey module that isn't a door is a window wall rather than a plain wall, where its style has window walls.")]
        [SerializeField, Range(0f, 1f)] private float groundWindowShare = 0.3f;
        [Tooltip("The chance that a module above the ground storey is a window wall rather than a plain wall, where its style has window walls.")]
        [SerializeField, Range(0f, 1f)] private float upperWindowShare = 0.4f;
        [Tooltip("Where the storeys below and above a joint both have beam pieces, a band of beams lies between their walls; this is how far the beams overlap the walls below and above them (each). The storeys above stand higher by the beam's height less twice this.")]
        [SerializeField, Range(0f, 0.2f)] private float storeyBeamOverlap = 0.04f;

        [Header("Corners")]
        [Tooltip("Where two facades meet on the outside of a bend or at a street corner, a joint within this many degrees of a right angle is covered by the style's outer corner piece (turned to sit between the two); any other joint by a column piece over the seam.")]
        [SerializeField, Range(0f, 45f)] private float cornerAngleTolerance = 20f;
        [Tooltip("Two frontages' ends at a junction are joined at a street corner only if each needs carrying no further than this to meet the other; otherwise each turns a side return into the branching street.")]
        [SerializeField, Range(1f, 20f)] private float cornerMaxReach = 8f;

        [Header("Jetties")]
        [Tooltip("The chance that a facade building's upper storeys jut out over the street (by the depth of the jetty piece, about 1.5m), carried on jetty supports - where it has an upper storey and a jetty piece fits its ground storey's module. Higher for more jettied buildings, lower for fewer.")]
        [SerializeField, Range(0f, 1f)] private float jettyShare = 0.3f;

        [Header("Window inserts")]
        [Tooltip("The chance that a hole for a window in a placed wall gets a window insert (a verified piece of the window category) rather than staying open.")]
        [SerializeField, Range(0f, 1f)] private float windowInsertShare = 1f;
        [Tooltip("How much bigger than the hole an insert may be on each side (its frame overlapping the wall round the hole); an insert smaller than the hole never fits.")]
        [SerializeField, Range(0f, 1f)] private float windowInsertMaxMargin = 0.45f;
        [Tooltip("How far the insert's middle stands forward of the middle of the wall's depth (back, below 0).")]
        [SerializeField, Range(-0.5f, 0.5f)] private float windowInsertDepth;

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

        [Header("Side walls")]
        [Tooltip("Where a column stands taller than the column beside it (in its building or the next), each of its storeys above the lower one gets a side wall facing that way and running back from its front walls, so from the street it seems to go on behind. The wall's length in modules is this times the column's width in modules, at least one (0 = no side walls). Separate from roofs and roof walls.")]
        [SerializeField, Range(0f, 5f)] private float sideWallMultiplier = 1f;

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

        [Header("Ground tiles (cobbles)")]
        [Tooltip("How far neighbouring ground tiles overlap, along a row and between rows.")]
        [SerializeField, Range(0f, 0.2f)] private float groundOverlap = 0.03f;
        [Tooltip("Each tile is turned a quarter, half or three-quarter turn from the tile before it along its row (square tiles; other tiles take a random half turn).")]
        [SerializeField] private bool groundTileTurns = true;
        [Tooltip("Each row slides along from the row before it by a random share of a tile between these two, so the joints never line up.")]
        [SerializeField, Range(0f, 1f)] private float groundSlideMin = 0.2f;
        [SerializeField, Range(0f, 1f)] private float groundSlideMax = 0.8f;
        [Tooltip("A tile is laid only where it overlaps the street by at least this much; the rest of it may stand out under the buildings lining the street.")]
        [SerializeField, Range(0f, 1.5f)] private float minTileCoverage = 0.1f;

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
        internal float StreetWidth => streetWidth;
        internal float StreetWidthVariance => streetWidthVariance;
        internal float MinStreetWidth => minStreetWidth;
        internal int MinStoreys => minStoreys;
        internal int MaxStoreys => maxStoreys;
        internal float StoreyBias => storeyBias;
        internal float LotNeighbourDistance => lotNeighbourDistance;
        internal float LotStoreyBias => lotStoreyBias;
        internal float GroundStoreyHeight => groundStoreyHeight;
        internal float FoundationHeight => foundationHeight;
        internal float FoundationFrontOffset => foundationFrontOffset;
        internal float GroundDoorShare => groundDoorShare;
        internal float GroundWindowShare => groundWindowShare;
        internal float UpperWindowShare => upperWindowShare;
        internal float StoreyBeamOverlap => storeyBeamOverlap;
        internal float JettyShare => jettyShare;
        internal float CornerAngleTolerance => cornerAngleTolerance;
        internal float CornerMaxReach => cornerMaxReach;
        internal float WindowInsertShare => windowInsertShare;
        internal float WindowInsertMaxMargin => windowInsertMaxMargin;
        internal float WindowInsertDepth => windowInsertDepth;
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
        internal float OverlapTolerance => overlapTolerance;
        internal float MinSegmentLength => minSegmentLength;
        internal float JunctionDistance => junctionDistance;
        // The branch test compares the leaving direction with the side's
        // normal, so the angle off the street is its sine.
        internal float BranchDot => Mathf.Sin(branchAngle * Mathf.Deg2Rad);
        internal float GroundOverlap => groundOverlap;
        internal bool GroundTileTurns => groundTileTurns;
        internal float GroundSlideMin => groundSlideMin;
        internal float GroundSlideMax => groundSlideMax;
        internal float MinTileCoverage => minTileCoverage;
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
