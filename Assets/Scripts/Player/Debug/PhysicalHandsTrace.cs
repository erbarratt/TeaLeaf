using Player;
using UnityEngine;

/// <summary>
/// While enabled, turns on HandPhysicalFollow's sweep trace: every frame a
/// hand's sweep hits a collider whose name contains colliderNameFilter, one
/// Console/Editor.log line for that hand with its contact state, the
/// push-out, and each sweep's hit (collider, distance, normal) - for
/// diagnosing hand collision bugs, e.g. jitter, from the log.
///
/// Enable it before entering Play Mode (toggling it in the headset is
/// awkward): the filter keeps the log to the case being investigated, so
/// touching anything else logs nothing. Allocates strings every frame while
/// on, so switch it off again afterwards.
/// </summary>
public class PhysicalHandsTrace : MonoBehaviour
{
    // Only hits on colliders whose name contains this are logged - e.g.
    // "Gap" for the test area's Gap Block Left/Right. Empty logs every hit.
    [SerializeField] private string colliderNameFilter = "Gap";

    private void OnEnable()
    {
        HandPhysicalFollow.TraceColliderFilter = colliderNameFilter;
        HandPhysicalFollow.TraceEnabled = true;
        Debug.Log($"[HandTrace] on, filter '{colliderNameFilter}'");
    }

    private void OnDisable()
    {
        HandPhysicalFollow.TraceEnabled = false;
        Debug.Log("[HandTrace] off");
    }
}
