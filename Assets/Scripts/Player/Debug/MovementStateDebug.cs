using UnityEngine;
using Player;

/// <summary>
/// Logs PlayerLocomotion.MovementState to the Console whenever it changes,
/// so the state machine can be checked in Play Mode (walk, sprint, crouch,
/// climb, fall off a ledge) without a world-space readout.
///
/// Logs on transitions only, not every frame, so the Console stays readable
/// and there's no per-frame string allocation. Also logs each Landed event
/// with its fall speed, to check jumps and falls report sensible values.
/// </summary>
public class MovementStateDebug : MonoBehaviour
{
    [SerializeField] private PlayerLocomotion playerLocomotion;

    private MovementState _lastState;

    private void OnEnable()
    {
        playerLocomotion.Landed += LogLanded;
    }

    /// <summary>
    /// Always unsubscribe what OnEnable subscribed - otherwise a disabled or
    /// destroyed debug component stays referenced by the event.
    /// </summary>
    private void OnDisable()
    {
        playerLocomotion.Landed -= LogLanded;
    }

    /// <summary>
    /// Landed handler - logs the fall speed at impact.
    /// </summary>
    private static void LogLanded(float fallSpeed)
    {
        Debug.Log($"[MovementState] Landed at {fallSpeed:F2} m/s");
    }

    private void Start()
    {
        _lastState = playerLocomotion.MovementState;
        Debug.Log($"[MovementState] {_lastState}");
    }

    private void Update()
    {
        MovementState state = playerLocomotion.MovementState;

        if (state == _lastState) {
            return;
        }

        Debug.Log($"[MovementState] {_lastState} -> {state}");
        _lastState = state;
    }
}
