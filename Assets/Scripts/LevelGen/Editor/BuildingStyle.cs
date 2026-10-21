using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// One style's pieces, gathered from the annotations tagged with it:
    /// what a layout spec's street or storey can be built from. Only pieces
    /// that are verified, not excluded and still in the catalogue are usable;
    /// the rest tagged with the style are kept apart, so a report can say
    /// what is waiting to be reviewed.
    ///
    /// The style's module (the size of one wall module) is the median size of
    /// its full-size usable walls - plain, window and door walls at least
    /// GeneratorSettings.FullSizeShare of the widest - so the half pieces
    /// never set it. Neighbouring walls overlap by a few centimetres, side by
    /// side and storey on storey, so modules are laid at the module size less
    /// that overlap (ModuleSpacing, StoreySpacing).
    ///
    /// A street of the style is decked with its floor pieces (tiles) where
    /// it has any, else it is ground - the streets' own mesh, which needs no
    /// pieces (TileCategory). The tile size is the median of the full-size
    /// floor pieces, and only pieces within the size tolerance of it are
    /// laid.
    /// </summary>
    internal class BuildingStyle
    {
        // The categories whose width is the module.
        internal static readonly string[] WallCategories = { "wall", "window-wall", "door-wall" };

        internal string Name { get; private set; }
        // Usable pieces, by category.
        internal Dictionary<string, List<PieceAnnotation>> Usable { get; } = new();
        // Pieces tagged with the style that can't be placed yet (not verified,
        // excluded, or no longer in the catalogue).
        internal List<PieceAnnotation> NotUsable { get; } = new();
        // 0 when the style has no usable walls.
        internal float ModuleWidth { get; private set; }
        internal float ModuleHeight { get; private set; }
        // How far apart neighbouring modules are laid, along a side and up a
        // building: the module less the walls' overlap (0 without usable walls).
        internal float ModuleSpacing { get; private set; }
        internal float StoreySpacing { get; private set; }
        // The usable pieces of each wall category that fit the module (width
        // and height each within the size tolerance, or within the wall scale
        // limit and scaled to it - Fits): what a storey's modules are built
        // from.
        private readonly Dictionary<string, List<PieceAnnotation>> _modules = new();
        // The same, less the pieces marked ground storey only: what a storey
        // above the ground is built from.
        private readonly Dictionary<string, List<PieceAnnotation>> _upperModules = new();
        // The usable plain walls of any size that show no window or door:
        // what side walls are built from.
        private List<PieceAnnotation> _sideWalls = new();
        // GeneratorSettings.SizeTolerance and WallScaleLimit, kept for Fits.
        private float _sizeTolerance;
        private float _scaleLimit;
        // "ground" (cobbles, laid on one grid per street area) or "floor"
        // (decking, laid along each street segment); empty when the style
        // has neither.
        internal string TileCategory { get; private set; } = "";
        // The tile size: width along the piece's X, depth along its Z (0
        // without tiles).
        internal float TileWidth { get; private set; }
        internal float TileDepth { get; private set; }
        // The usable tiles of TileCategory within the size tolerance of the
        // tile size: the pieces a street of this style is laid with.
        internal List<PieceAnnotation> Tiles { get; } = new();

        /// <summary>
        /// The usable pieces of one category (empty if there are none).
        /// </summary>
        internal IReadOnlyList<PieceAnnotation> Pieces(string category)
        {
            if (Usable.TryGetValue(category, out List<PieceAnnotation> pieces)) {
                return pieces;
            }

            return Array.Empty<PieceAnnotation>();
        }

        /// <summary>
        /// The usable pieces of a wall category that fit the module (empty if
        /// there are none).
        /// </summary>
        internal IReadOnlyList<PieceAnnotation> Modules(string category)
        {
            if (_modules.TryGetValue(category, out List<PieceAnnotation> pieces)) {
                return pieces;
            }

            return Array.Empty<PieceAnnotation>();
        }

        /// <summary>
        /// The usable pieces of a wall category that fit the module and may
        /// stand on a storey (0 = the ground): above the ground, without the
        /// pieces marked ground storey only.
        /// </summary>
        internal IReadOnlyList<PieceAnnotation> Modules(string category, int storey)
        {
            if (storey == 0) {
                return Modules(category);
            }

            if (_upperModules.TryGetValue(category, out List<PieceAnnotation> pieces)) {
                return pieces;
            }

            return Array.Empty<PieceAnnotation>();
        }

        /// <summary>
        /// The usable plain walls of any width and height that show no window
        /// or door: what side walls are built from (the caller picks those of
        /// the storey's height, and leaves out the ground-storey-only ones
        /// above the ground).
        /// </summary>
        internal IReadOnlyList<PieceAnnotation> SideWalls()
        {
            return _sideWalls;
        }

        /// <summary>
        /// Whether a wall's size (its width or height) can be used for a
        /// module size: within the size tolerance as it is, or within the
        /// wall scale limit (a share of the module) once scaled to it.
        /// </summary>
        internal bool Fits(float size, float module)
        {
            return Mathf.Abs(size - module) <= Mathf.Max(_sizeTolerance, module * _scaleLimit);
        }

        /// <summary>
        /// The scale that makes a fitting wall's size the module size: 1
        /// within the size tolerance (used as it is), else module / size.
        /// </summary>
        internal float FitScale(float size, float module)
        {
            return Mathf.Abs(size - module) <= _sizeTolerance || size <= 0f ? 1f : module / size;
        }

        /// <summary>
        /// Gathers every style the annotations use, by name.
        /// </summary>
        internal static Dictionary<string, BuildingStyle> Gather(PieceAnnotations annotations, PrefabCatalogue catalogue, GeneratorSettings settings)
        {
            HashSet<string> inCatalogue = new();

            foreach (CatalogueEntry entry in catalogue.prefabs) {
                inCatalogue.Add(entry.guid);
            }

            Dictionary<string, BuildingStyle> styles = new(StringComparer.Ordinal);

            foreach (PieceAnnotation piece in annotations.pieces) {
                bool usable = piece.verified && !piece.excluded && inCatalogue.Contains(piece.guid) && piece.category != "";

                foreach (string name in piece.styles) {
                    if (!styles.TryGetValue(name, out BuildingStyle style)) {
                        style = new BuildingStyle { Name = name };
                        styles.Add(name, style);
                    }

                    if (!usable) {
                        style.NotUsable.Add(piece);
                        continue;
                    }

                    if (!style.Usable.TryGetValue(piece.category, out List<PieceAnnotation> list)) {
                        list = new List<PieceAnnotation>();
                        style.Usable.Add(piece.category, list);
                    }

                    list.Add(piece);
                }
            }

            foreach (BuildingStyle style in styles.Values) {
                style.MeasureModule(settings);
                style.MeasureTiles(settings);
            }

            return styles;
        }

        /// <summary>
        /// Sets the module from the median width and height of the full-size
        /// usable walls, and the spacing they are laid at.
        /// </summary>
        private void MeasureModule(GeneratorSettings settings)
        {
            float widest = 0f;

            foreach (string category in WallCategories) {
                foreach (PieceAnnotation piece in Pieces(category)) {
                    widest = Mathf.Max(widest, piece.width);
                }
            }

            List<float> widths = new();
            List<float> heights = new();

            foreach (string category in WallCategories) {
                foreach (PieceAnnotation piece in Pieces(category)) {
                    if (piece.width >= widest * settings.FullSizeShare) {
                        widths.Add(piece.width);
                        heights.Add(piece.height);
                    }
                }
            }

            ModuleWidth = Median(widths);
            ModuleHeight = Median(heights);
            ModuleSpacing = ModuleWidth > 0f ? ModuleWidth - settings.WallOverlap : 0f;
            StoreySpacing = ModuleHeight > 0f ? ModuleHeight - settings.WallOverlap : 0f;

            _sizeTolerance = settings.SizeTolerance;
            _scaleLimit = settings.WallScaleLimit;

            foreach (string category in WallCategories) {
                List<PieceAnnotation> fitting = new();

                foreach (PieceAnnotation piece in Pieces(category)) {
                    if (Fits(piece.width, ModuleWidth) && Fits(piece.height, ModuleHeight)) {
                        fitting.Add(piece);
                    }
                }

                _modules[category] = fitting;
                _upperModules[category] = fitting.FindAll(piece => !piece.groundStoreyOnly);
            }

            _sideWalls = new List<PieceAnnotation>(Pieces("wall")).FindAll(piece => !piece.showsOpening);
        }

        /// <summary>
        /// Sets the tile category: "floor" for a style with usable floor
        /// pieces (plank decking), else "ground" (the streets' own mesh,
        /// which needs no pieces). For decking, the tile size and the tiles
        /// that fit it: full size means both sides at least FullSizeShare of
        /// the biggest tile's (so a narrow strip never counts, however long).
        /// </summary>
        private void MeasureTiles(GeneratorSettings settings)
        {
            TileCategory = Pieces("floor").Count > 0 ? "floor" : "ground";

            if (TileCategory == "ground") {
                return;
            }

            IReadOnlyList<PieceAnnotation> pieces = Pieces(TileCategory);
            PieceAnnotation biggest = pieces[0];

            foreach (PieceAnnotation piece in pieces) {
                if (piece.width * piece.depth > biggest.width * biggest.depth) {
                    biggest = piece;
                }
            }

            List<float> widths = new();
            List<float> depths = new();

            foreach (PieceAnnotation piece in pieces) {
                if (piece.width >= biggest.width * settings.FullSizeShare && piece.depth >= biggest.depth * settings.FullSizeShare) {
                    widths.Add(piece.width);
                    depths.Add(piece.depth);
                }
            }

            TileWidth = Median(widths);
            TileDepth = Median(depths);

            foreach (PieceAnnotation piece in pieces) {
                if (Mathf.Abs(piece.width - TileWidth) <= settings.SizeTolerance && Mathf.Abs(piece.depth - TileDepth) <= settings.SizeTolerance) {
                    Tiles.Add(piece);
                }
            }
        }

        /// <summary>
        /// The middle value of a list (the lower of the two middle ones for an
        /// even count, so it is always a real piece's size), or 0 if empty.
        /// </summary>
        private static float Median(List<float> values)
        {
            if (values.Count == 0) {
                return 0f;
            }

            values.Sort();
            return values[(values.Count - 1) / 2];
        }
    }
}
