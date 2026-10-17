using System.Collections.Generic;
using Core;
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
    /// anything inside was edited by hand, and the generator's report on the
    /// result: a marker at every problem it found (a wall end nothing stands
    /// against, a roof end left open, a piece refused for standing in a
    /// street), drawn as wire boxes in the Scene view and in the headset
    /// (IDebugDrawable through DebugLines), so each one can be walked to.
    /// Nothing runs per frame.
    /// </summary>
    public class GeneratedRoot : MonoBehaviour, IDebugDrawable
    {
        /// <summary>
        /// What kind of problem a marker stands at; each kind has its colour.
        /// </summary>
        public enum MarkerKind
        {
            FreeEnd,
            OpenRoof,
            Refused,
            Other
        }

        /// <summary>
        /// One problem the report found: where, how big a box to draw there,
        /// and a line saying what it is.
        /// </summary>
        [System.Serializable]
        public struct Marker
        {
            public MarkerKind kind;
            public Vector3 position;
            public Vector3 size;
            public string note;
        }

        private static readonly Color[] _colours = { Color.red, new(1f, 0.5f, 0f), Color.magenta, Color.yellow };

        [SerializeField] private string specPath;
        [SerializeField] private string fingerprint;
        [SerializeField] private List<Marker> markers = new();

        public string SpecPath => specPath;
        public string Fingerprint => fingerprint;
        public IReadOnlyList<Marker> Markers => markers;

        /// <summary>
        /// Records the spec built from and the fingerprint of the result.
        /// </summary>
        public void Record(string builtFrom, string builtFingerprint)
        {
            specPath = builtFrom;
            fingerprint = builtFingerprint;
        }

        /// <summary>
        /// Replaces the report's markers.
        /// </summary>
        public void SetMarkers(List<Marker> found)
        {
            markers = found ?? new List<Marker>();
        }

        private void OnEnable()
        {
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            DebugDrawRegistry.Unregister(this);
        }

        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// A wire box at every marker in its kind's colour; selected, a line
        /// up from each as well, so one can be found from far off.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            foreach (Marker marker in markers) {
                lines.Color = _colours[Mathf.Clamp((int)marker.kind, 0, _colours.Length - 1)];
                lines.WireCube(marker.position, marker.size);

                if (detailed) {
                    lines.Line(marker.position, marker.position + Vector3.up * 3f);
                }
            }
        }
    }
}
