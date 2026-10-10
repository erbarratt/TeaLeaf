using Core;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// A guard's patrol: the places it walks between, in order. Each
    /// child object of this one is a point on the route, in the order
    /// they're listed in the Hierarchy - so a route is laid out by
    /// dragging its points about in the Scene view.
    ///
    /// At the last point the guard either goes straight back to the first
    /// (a loop) or turns round and walks the route backwards (there and
    /// back).
    ///
    /// Data only: the walking is the Guard's. No Update().
    /// </summary>
    public class PatrolRoute : MonoBehaviour, IDebugDrawable
    {
        // Seconds the guard stands at each point before walking on.
        [SerializeField] private float waitTime = 2f;

        // On = from the last point straight to the first. Off = there
        // and back along the same points.
        [SerializeField] private bool loops;

        private static readonly Color _routeColor = new(0.3f, 0.9f, 1f, 1f);

        // The points' positions, read once when the game starts: a route
        // doesn't move while playing.
        private Vector3[] _points;

        public float WaitTime => waitTime;
        public bool Loops => loops;

        /// How many points the route has.
        public int Count => _points != null ? _points.Length : 0;

        private void Awake()
        {
            _points = new Vector3[transform.childCount];

            for (int i = 0; i < _points.Length; i++) {
                _points[i] = transform.GetChild(i).position;
            }
        }

        private void OnEnable()
        {
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// Point number index, in world space.
        /// </summary>
        public Vector3 GetPoint(int index)
        {
            return _points[index];
        }

        /// <summary>
        /// The number of the point nearest a position - where a guard
        /// coming back from a search takes its route up again.
        /// </summary>
        public int NearestIndex(Vector3 position)
        {
            int nearest = 0;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < _points.Length; i++) {
                // Squared distances: the same answer as comparing the
                // distances, without a square root each.
                float sqr = (_points[i] - position).sqrMagnitude;

                if (sqr < nearestSqr) {
                    nearestSqr = sqr;
                    nearest = i;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Editor-only: the route, always.
        /// </summary>
        private void OnDrawGizmos()
        {
            DebugLines.ForGizmos.Draw(this, false);
        }

        /// <summary>
        /// Draws the route: a small ball at each point and a line from
        /// each to the next (and from the last back to the first, for a
        /// loop). Read from the child objects, so it also shows while
        /// editing, as the points are dragged.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            int count = transform.childCount;
            lines.Color = _routeColor;

            for (int i = 0; i < count; i++) {
                Vector3 point = transform.GetChild(i).position;
                lines.WireSphere(point, 0.1f);

                if (i + 1 < count) {
                    lines.Line(point, transform.GetChild(i + 1).position);
                } else if (loops && count > 2) {
                    lines.Line(point, transform.GetChild(0).position);
                }
            }
        }
    }
}
