namespace Core
{
    /// <summary>
    /// What a floor is made of, for footsteps and landings: each surface has
    /// its own sounds, and its own loudness to guards (carpet is quiet,
    /// metal carries a long way). A collider says which it is with a
    /// SurfaceTag; SurfaceSounds holds the sounds for each.
    ///
    /// Stone is first, so it's the value of anything untagged: most of a
    /// level needs no tags at all, only the floors that differ.
    ///
    /// Add new surfaces at the end: tags and SurfaceSounds entries are
    /// saved as the value's number, so inserting one in the middle would
    /// silently change what every later one means.
    /// </summary>
    public enum SurfaceType
    {
        Stone,
        Wood,
        Carpet,
        Metal,
        Water,
        Tile,
        Grass,
        Gravel
    }
}
