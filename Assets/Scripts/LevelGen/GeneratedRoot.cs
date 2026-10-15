using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Marks the object holding what the level generator built for a sector
    /// (Environment/Sectors/&lt;sector&gt;/Geometry/Generated). A regeneration
    /// replaces it whole, so nothing placed by hand belongs inside it.
    ///
    /// It remembers which layout spec it was built from and a fingerprint of
    /// everything under it as built, so a regeneration can warn first if
    /// anything inside was edited by hand. Does nothing at runtime.
    /// </summary>
    public class GeneratedRoot : MonoBehaviour
    {
        [SerializeField] private string specPath;
        [SerializeField] private string fingerprint;

        public string SpecPath => specPath;
        public string Fingerprint => fingerprint;

        /// <summary>
        /// Records the spec built from and the fingerprint of the result.
        /// </summary>
        public void Record(string builtFrom, string builtFingerprint)
        {
            specPath = builtFrom;
            fingerprint = builtFingerprint;
        }
    }
}
