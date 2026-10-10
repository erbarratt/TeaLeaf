namespace Core
{
    /// <summary>
    /// How the SoundPlayer's voices are given a direction.
    /// </summary>
    public enum SpatialiserMode
    {
        // Unity's own 3D sound: panned left and right, nothing more.
        UnityPanning,

        // The project's own spatialiser (SpatialVoice): time difference
        // between the ears, head shadow, and a duller sound from behind.
        BuiltIn,

        // The spatialiser plugin chosen in Project Settings > Audio >
        // Spatializer Plugin (none is installed; Steam Audio would go
        // here).
        Plugin
    }
}
