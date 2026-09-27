using UnityEngine;
using Player;

/// <summary>
/// Logs PlayerLocomotion.MovementState to the Console whenever it changes,
/// so the state machine can be checked in Play Mode (walk, sprint, crouch,
/// climb, fall off a ledge) without a world-space readout.
///
/// Logs on transitions only, not every frame, so the Console stays readable
/// and there's no per-frame string allocation.
/// </summary>
public class MovementStateDebug : MonoBehaviour
{
    [SerializeField] private PlayerLocomotion playerLocomotion;

    private MovementState _lastState;

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
