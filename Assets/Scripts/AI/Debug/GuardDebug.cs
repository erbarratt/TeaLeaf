using UnityEngine;
using UnityEngine.AI;

namespace AI
{
    /// <summary>
    /// Debug-only: writes what a guard is doing to the Console - each
    /// change of state, and each time it sets off or comes to a stop -
    /// so a play session leaves a record of what the guard did and
    /// where. On the guard's root object, next to its Guard.
    /// </summary>
    [RequireComponent(typeof(Guard))]
    public class GuardDebug : MonoBehaviour
    {
        // Also log each time it starts and stops walking (a line or two
        // for every point of a patrol).
        [Tooltip("Also log each time it starts and stops walking (a line or two for every point of a patrol).")]
        [SerializeField] private bool logMovement = true;

        // Faster than this, in metres a second, counts as walking.
        private const float MovingSpeed = 0.1f;

        private Guard _guard;
        private NavMeshAgent _agent;
        private GuardState _lastState;
        private bool _wasMoving;

        private void Awake()
        {
            _guard = GetComponent<Guard>();
            _agent = GetComponent<NavMeshAgent>();
        }

        private void Start()
        {
            _lastState = _guard.State;
            Debug.Log($"[Guard] {name}: starts in {_lastState} at {transform.position:F1}, on the NavMesh: {_agent.isOnNavMesh}.", this);
        }

        /// <summary>
        /// Compares the guard with last frame and logs what changed.
        /// Nothing is built or logged on a frame with no change.
        /// </summary>
        private void Update()
        {
            GuardState state = _guard.State;

            if (state != _lastState) {
                Debug.Log($"[Guard] {name}: {_lastState} -> {state} (meter {_guard.Detection:F2}) at {transform.position:F1}.", this);
                _lastState = state;
            }

            if (!logMovement || !_agent.enabled) {
                return;
            }

            bool isMoving = _agent.velocity.sqrMagnitude > MovingSpeed * MovingSpeed;

            if (isMoving == _wasMoving) {
                return;
            }

            _wasMoving = isMoving;

            if (isMoving) {
                Debug.Log($"[Guard] {name}: sets off from {transform.position:F1} for {_agent.destination:F1} ({state}).", this);
            } else {
                Debug.Log($"[Guard] {name}: stops at {transform.position:F1} ({state}).", this);
            }
        }
    }
}
