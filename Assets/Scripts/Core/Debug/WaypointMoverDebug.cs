using UnityEngine;

namespace Core
{
    /// <summary>
    /// Debug-only: moves this object along a list of points at walking
    /// speed, there and back again, forever. With a SoundEmitterDebug on
    /// the same object it's a stand-in for a patrolling guard's footsteps,
    /// for hearing a sound move from room to room. It moves in straight
    /// lines between the points with no collision, so place the points
    /// where a guard could really walk.
    /// </summary>
    public class WaypointMoverDebug : MonoBehaviour
    {
        // The points to visit, in order.
        [SerializeField] private Transform[] waypoints;

        // Metres per second.
        [SerializeField] private float speed = 1.4f;

        // The point being walked towards, and whether the route is being
        // walked forwards (1) or back (-1).
        private int _target;
        private int _direction = 1;

        private void Start()
        {
            if (waypoints == null || waypoints.Length < 2) {
                enabled = false;
                return;
            }

            transform.position = waypoints[0].position;
            _target = 1;
        }

        private void Update()
        {
            Vector3 goal = waypoints[_target].position;
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);

            if (transform.position != goal) {
                return;
            }

            // Reached a point: turn round at either end of the route.
            if (_target == waypoints.Length - 1) {
                _direction = -1;
            } else if (_target == 0) {
                _direction = 1;
            }

            _target += _direction;
        }
    }
}
