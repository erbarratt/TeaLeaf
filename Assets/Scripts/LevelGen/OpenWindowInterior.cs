using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Sits on an Open Window Wall placed by hand in place of one of the
    /// generated facade's wall modules, and holds the settings of the room
    /// built behind it (menu TeaLeaf > Level Gen > Build Open Window
    /// Interiors, OpenWindowInteriors): how many of the building's modules
    /// wide (centred on the window) and deep it is, and the room last built
    /// for it, replaced when it is built again. A generation leaves out the
    /// wall pieces the window stands in place of. Data only; nothing runs.
    /// </summary>
    public class OpenWindowInterior : MonoBehaviour
    {
        [Tooltip("The room's width in wall modules (one module = the window's own width less Wall Overlap), centred on the window, e.g. 3 = the window's module plus one either side. Wider = a bigger room seen through the window; 1 = just as wide as the window, with no front wall either side.")]
        [SerializeField, Min(1)] private int widthModules = 1;
        [Tooltip("The room's depth in wall modules, back from the window's inside face, e.g. 2 = two window widths deep. Deeper = a bigger room behind the window.")]
        [SerializeField, Min(1)] private int depthModules = 1;
        [Tooltip("The room last built for this window by TeaLeaf > Level Gen > Build Open Window Interiors (under the sector's Geometry/Open Window Interiors), filled in by that tool, e.g. 'Open Window Wall Interior'. Destroyed and replaced when the room is built again; empty until it has been built once.")]
        [SerializeField] private GameObject built;

        public int WidthModules => widthModules;
        public int DepthModules => depthModules;
        public GameObject Built => built;
    }
}
