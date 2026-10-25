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
        [Tooltip("The room's width in modules (the window's own width), centred on the window.")]
        [SerializeField, Min(1)] private int widthModules = 1;
        [Tooltip("The room's depth in modules, back from the window.")]
        [SerializeField, Min(1)] private int depthModules = 1;
        [Tooltip("The room last built for this window (under the sector's Geometry/Open Window Interiors); replaced when built again.")]
        [SerializeField] private GameObject built;

        public int WidthModules => widthModules;
        public int DepthModules => depthModules;
        public GameObject Built => built;
    }
}
