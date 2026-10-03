namespace Core
{
    /// <summary>
    /// What kind of thing made a noise. Guards use it to decide how to
    /// react (a footstep is a person, a thrown bottle is a distraction), and
    /// to ignore noises that mean nothing to them (another guard's steps).
    /// </summary>
    public enum NoiseType
    {
        // Someone walking or running.
        Footstep,

        // Someone landing from a jump or a fall.
        Landing,

        // An object hitting something: thrown, dropped or knocked over.
        Impact,

        // A door, chest or lock being worked.
        Mechanism,

        // A voice: a guard's bark.
        Voice,

        // Made on purpose to draw attention: the noisemaker bolt.
        Distraction
    }
}
