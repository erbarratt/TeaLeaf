using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Core
{
    /// <summary>
    /// Turns on fixed foveated rendering for a Quest build: the headset
    /// draws the edges of each eye's view at a lower resolution than the
    /// middle. The lenses blur the edges anyway, so little is lost, and
    /// pixels are the tightest budget on Quest.
    ///
    /// Two things are needed. The OpenXR "Foveated Rendering" feature has
    /// to be enabled for Android, with the Foveated Rendering API set to
    /// SRP Foveation (Project Settings > XR Plug-in Management > OpenXR) -
    /// that only makes it possible. Then a level has to be asked for at
    /// runtime, which is all this class does: the level starts at 0 (off).
    ///
    /// Not a component: it runs by itself once when the game starts, after
    /// the first scene has loaded (XR is running by then), and the level
    /// stays set across level restarts. Nothing per frame. Android builds
    /// only - PCVR is left alone.
    /// </summary>
    public static class FoveatedRendering
    {
        // How strongly the edges are reduced: 0 = off, 1 = the most the
        // headset offers. A first guess, to be tuned on the device - too
        // high and the edges of the view shimmer, most of all on thin
        // bright things like the stars.
        private const float Level = 0.5f;

        // Reused list for the subsystem lookup, so it allocates only once.
        private static readonly List<XRDisplaySubsystem> _displays = new();

        /// <summary>
        /// Asks every running XR display for the foveation level. Called by
        /// Unity once, after the first scene has loaded.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Apply()
        {
            if (Application.platform != RuntimePlatform.Android) {
                return;
            }

            SubsystemManager.GetSubsystems(_displays);

            for (int i = 0; i < _displays.Count; i++) {
                // No flags: fixed foveation, not eye-tracked (the Quest 3
                // has no eye tracking).
                _displays[i].foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;
                _displays[i].foveatedRenderingLevel = Level;
            }

            if (_displays.Count == 0) {
                Debug.LogWarning("FoveatedRendering: no XR display running, so no foveation level was set.");
            }
        }
    }
}
