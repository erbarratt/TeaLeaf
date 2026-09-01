using UnityEngine;
using Player;

/// <summary>
/// Simple test script.
///
/// Prints tracking information once per second
/// so we can verify the tracking system is working.
///
/// This script is temporary and will eventually
/// be removed.
/// </summary>
public class TrackingTest : MonoBehaviour
{
    // Reference to our tracking system.
    [SerializeField] private PlayerTracking tracking;

    // Used to count elapsed time.
    private float _timer;

    private void Update()
    {
        // Add frame time to our timer.
        _timer += Time.deltaTime;

        // Only log once every second.
        if (_timer < 1f)
        {
            return;
        }

        // Reset timer.
        _timer = 0f;

        // Print current positions.
        Debug.Log(
            $"Head Position: {tracking.HeadPosition}\n" +
            $"Left Hand Position: {tracking.LeftHandPosition}\n" +
            $"Right Hand Position: {tracking.RightHandPosition}");
    }
}