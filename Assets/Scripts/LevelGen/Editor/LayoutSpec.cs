using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// A layout spec: one generated area's streets, the buildings lining
    /// them and the whole buildings on lots inside loops, as kept in a JSON
    /// file in Assets/Data/LevelGen/Layouts. Written from a brief by Claude
    /// in the conversation, and read by the generator.
    ///
    /// Pieces are never named here: a street or a storey names a style, and
    /// the pieces come from the annotations tagged with it (BuildingStyle).
    /// All positions are in world space, in metres; a yaw is in degrees
    /// clockwise from +Z (north) seen from above, as Unity turns about Y.
    /// </summary>
    [Serializable]
    public class LayoutSpec
    {
        // Bumped when the format changes; a spec in another format is refused.
        public const int CurrentFormat = 2;

        internal const string LayoutsFolder = PrefabCatalogueScanner.DataFolder + "/Layouts";

        public int format;
        // Every random choice the generator makes comes from one
        // System.Random made from this, in a fixed order.
        public int seed;
        // The sector the area is built in: its pieces go under
        // Environment/Sectors/<sector>/Geometry/Generated, which a
        // regeneration replaces whole.
        public string sector;
        public List<StreetSpec> streets = new();
        public List<LotSpec> lots = new();

        /// <summary>
        /// Gives every street segment its width, the first random choices
        /// made from the seed (so the validator, which makes only these, sees
        /// the same widths as the generator): the street's width - its
        /// spec's, or GeneratorSettings.StreetWidth if that is 0 - made up to
        /// StreetWidthVariance wider or narrower, and at least MinStreetWidth.
        /// One draw per segment, in the spec's order, whatever the segment.
        /// </summary>
        internal void ResolveWidths(GeneratorSettings settings, System.Random random)
        {
            foreach (StreetSpec street in streets) {
                float baseWidth = street.width > 0f ? street.width : settings.StreetWidth;
                street.SegmentWidths = new List<float>();

                for (int i = 1; i < street.points.Count; i++) {
                    float variance = Mathf.Lerp(-settings.StreetWidthVariance, settings.StreetWidthVariance, (float)random.NextDouble());
                    street.SegmentWidths.Add(Mathf.Max(settings.MinStreetWidth, baseWidth + variance));
                }
            }
        }
    }

    /// <summary>
    /// A street: a polyline along its middle, at any angle (a curve is several
    /// short segments). Two streets meet where they share a point. Its sides
    /// are lined by frontages.
    /// </summary>
    [Serializable]
    public class StreetSpec
    {
        public string id;
        // The style its ground (or plank decking) comes from.
        public string style;
        // Width from edge to edge, in metres; 0 uses GeneratorSettings.StreetWidth.
        // Each segment is varied from it (LayoutSpec.ResolveWidths).
        public float width;
        // The middle line, each point with its ground height in y.
        public List<Vector3> points = new();
        public List<FrontageSpec> frontages = new();

        // Each segment's width once resolved (segment i runs from point i to
        // point i + 1). Not in the file.
        internal List<float> SegmentWidths { get; set; }

        /// <summary>
        /// A segment's resolved width (the spec's width before it is resolved).
        /// </summary>
        internal float SegmentWidth(int segment)
        {
            return SegmentWidths != null && segment >= 0 && segment < SegmentWidths.Count ? SegmentWidths[segment] : width;
        }

        /// <summary>
        /// The widest of the segments meeting at a point (one at an end).
        /// </summary>
        internal float WidthAt(int point)
        {
            return Mathf.Max(SegmentWidth(point - 1 >= 0 ? point - 1 : point), SegmentWidth(point < points.Count - 1 ? point : point - 1));
        }
    }

    /// <summary>
    /// One side of a street, from one of its points to a later one, lined
    /// with buildings standing side by side along the street's edge, in
    /// order from the first point. A frontage runs past bends; it ends only
    /// where another street branches off on its side (or the street ends),
    /// since a street has no gaps between buildings but there. The buildings
    /// are facades: only what is seen from the street is built. Gaps at
    /// bends and whatever length is left at the end are filled by the
    /// generator, so a frontage needn't fit its stretch exactly.
    /// </summary>
    [Serializable]
    public class FrontageSpec
    {
        // "left" or "right", looking along the street from its first point.
        public string side;
        // The point indices the frontage runs between (from < to).
        public int from;
        public int to;
        public List<BuildingSpec> buildings = new();
    }

    /// <summary>
    /// One building in a frontage: its facade along the street.
    /// </summary>
    [Serializable]
    public class BuildingSpec
    {
        // Modules along the street.
        public int widthModules;
        // Styles from the ground storey up; the last repeats for every storey
        // above the list, and storeys past the count rolled are dropped
        // (the count comes from GeneratorSettings, StoreyPlan).
        public List<string> storeys = new();
        // Which module along the front holds the door, counted from the left
        // as seen from the street, from 0; -1 for none.
        public int doorModule = -1;
    }

    /// <summary>
    /// A lot: a whole building, seen from every side, where streets close a
    /// loop round it. Square in itself, at its own position and yaw; its
    /// footprint is a whole number of wall modules along its front and along
    /// its depth.
    /// </summary>
    [Serializable]
    public class LotSpec
    {
        public string id;
        // The middle of the footprint, at the height of the ground storey's floor.
        public Vector3 origin;
        // The way the front faces, in degrees clockwise from +Z.
        public float yaw;
        // Modules along the front, and from the front to the back.
        public int widthModules;
        public int depthModules;
        // Styles from the ground storey up; the last repeats for every storey
        // above the list, and storeys past the count rolled are dropped
        // (the count comes from GeneratorSettings, StoreyPlan).
        public List<string> storeys = new();
        // Which module along the front holds the door, counted from the left
        // as seen from in front, from 0; -1 for none.
        public int doorModule = -1;

        /// <summary>
        /// The direction the front faces, level.
        /// </summary>
        internal Vector3 Front => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    }
}
