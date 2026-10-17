using System;
using System.Collections.Generic;

namespace LevelGen
{
    /// <summary>
    /// The building styles a piece is suggested to belong to, from its name:
    /// the pack's walls come in families (by folder and name) and a storey is
    /// built from one family only. Each family's corners serve every storey
    /// of that family; the foundations serve every building style. Street
    /// ground pieces have styles of their own.
    ///
    /// Only a suggestion, ticked in the Review Pieces window for a piece with
    /// no annotation yet and stored when the piece is accepted, like the rest
    /// of the suggestion. Names are trusted for the family, never for
    /// direction.
    /// </summary>
    internal static class StyleRules
    {
        internal const string Plaster = "plaster";
        internal const string PlasterPlanks = "plaster-planks";
        internal const string PlasterMasonry = "plaster-masonry";
        internal const string WallTall = "wall-tall";
        internal const string MasonryCellar = "masonry-cellar";
        internal const string MasonryWoodLarge = "masonry-woodlarge";
        internal const string Cobbles = "cobbles";
        internal const string WoodFloor = "wood-floor";

        // The styles buildings are made of (the foundations serve all of them).
        internal static readonly string[] Buildings = { Plaster, PlasterPlanks, PlasterMasonry, WallTall, MasonryCellar, MasonryWoodLarge };
        // Styles that are only ever a building's (or facade's) ground storey.
        internal static readonly string[] GroundStoreyOnly = { MasonryCellar, PlasterMasonry, PlasterPlanks };
        // Every style a rule gives, offered in the window from the start.
        internal static readonly string[] All = { Plaster, PlasterPlanks, PlasterMasonry, WallTall, MasonryCellar, MasonryWoodLarge, Cobbles, WoodFloor };

        // Name prefixes (after "TL_"), most specific first; the first match wins.
        private static readonly (string prefix, string[] styles)[] _rules = {
            // Plaster walls: three families, sharing the plaster corner.
            ("PlasterWalls_A_MidBeam", new[] { Plaster, PlasterPlanks, PlasterMasonry }),
            ("PlasterWalls_A_Planks", new[] { PlasterPlanks }),
            ("PlasterWalls_B_PlanksCorner", new[] { PlasterPlanks }),
            ("PlasterWalls_B_MasonaryWall", new[] { PlasterMasonry }),
            ("PlasterWalls_A_Cross", new[] { Plaster }),
            ("PlasterWalls_A_WindowBase", new[] { Plaster }),
            ("PlasterWalls_A_Zbar", new[] { Plaster }),
            ("PlasterWall_B_LowCurves", new[] { Plaster }),
            ("PlasterWalls_B_WindiwRoof", new[] { Plaster }),
            ("PlasterWalls_B_Corner", new[] { Plaster, PlasterPlanks, PlasterMasonry }),
            // The tall walls, with their own corner.
            ("WallTall_", new[] { WallTall }),
            ("WallsTall_", new[] { WallTall }),
            // Masonry: the low cellar walls (a storey of their own), the door
            // walls (part of plaster-masonry's masonry ground storey), sharing
            // the masonry corners, and the large wood-framed walls.
            ("Masonary_Cellar", new[] { MasonryCellar }),
            ("Masonary_ShortSideWall", new[] { MasonryCellar }),
            ("Masonary_WallDoor", new[] { PlasterMasonry }),
            ("Masonary_DoorOrnate", new[] { PlasterMasonry }),
            ("Masonary_WallCorner", new[] { MasonryCellar, PlasterMasonry }),
            ("Masonary_WoodLarge", new[] { MasonryWoodLarge }),
            // The only foundations in the pack: under every style.
            ("Masonary_Foundations", Buildings),
            // Street ground: cobbles, and plank decking for wooden streets,
            // walkways and bridges (the supports under it have no style).
            ("StoneFloor_", new[] { Cobbles }),
            ("Platforms_Planks", new[] { WoodFloor }),
            ("Platforms_LoosePlank", new[] { WoodFloor }),
            ("Fixtures_WoodenPlatform", new[] { WoodFloor }),
            ("Platforms_BoardPlanks_Supports", Array.Empty<string>()),
            ("Platforms_BoardPlanks", new[] { WoodFloor }),
            ("Platforms_ShortBoardPlanks_Supports", Array.Empty<string>()),
            ("Platforms_ShortBoardPlanks", new[] { WoodFloor })
        };

        /// <summary>
        /// The styles suggested for a piece by its name (empty if none).
        /// </summary>
        internal static List<string> Suggest(string pieceName)
        {
            string name = pieceName.StartsWith("TL_", StringComparison.Ordinal) ? pieceName.Substring(3) : pieceName;

            foreach ((string prefix, string[] styles) in _rules) {
                if (name.StartsWith(prefix, StringComparison.Ordinal)) {
                    List<string> suggested = new(styles);
                    suggested.Sort(StringComparer.Ordinal);
                    return suggested;
                }
            }

            return new List<string>();
        }
    }
}
