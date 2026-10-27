using UnityEngine;

namespace Core
{
    /// <summary>
    /// A wedge-shaped collider for a flight of steps: one convex MeshCollider
    /// with six corners, so the player walks up a smooth ramp instead of
    /// bumping up each step. It is a solid wedge (slope, both sides, back and
    /// bottom), so it also works for steps that stand free.
    ///
    /// Goes on an empty child of the steps, placed at the foot of the first
    /// step in the middle of its width. Orientation convention - local X runs
    /// across the steps, +Y is up, and +Z points up the flight: the slope
    /// starts on the floor at the object's position and rises to `height` at
    /// `length` along Z. Turn the object half a turn about Y for steps that
    /// climb the other way.
    ///
    /// The three sizes are in the object's own units, so keep the object (and
    /// its parents) at scale 1 for them to be metres.
    ///
    /// The mesh is never saved: it is built from the three sizes when the
    /// level loads (Awake) and whenever the scene loads or a size changes in
    /// the editor (OnValidate), so the collider is also there for editor
    /// bakes such as the NavMesh.
    /// </summary>
    [RequireComponent(typeof(MeshCollider))]
    public class RampCollider : MonoBehaviour
    {
        // How far the ramp runs along local Z, from the foot of the first
        // step to the back of the top one.
        [Tooltip("How far the ramp runs along local Z, from the foot of the first step to the back of the top one, in metres (keep scale 1), e.g. 1.8 = six 0.3m treads. Longer = a gentler slope for the same Height.")]
        [SerializeField] private float length = 1.8f;

        // How high the back of the ramp is: the height of the top step.
        [Tooltip("How high the back of the ramp is, i.e. the top step's height, in metres, e.g. 1.08 = six 0.18m risers. Higher = a steeper slope for the same Length (over 45 degrees the player slides).")]
        [SerializeField] private float height = 1.08f;

        // How wide the ramp is along local X, centred on the object.
        [Tooltip("How wide the ramp is along local X, centred on this object, in metres, e.g. 2 = the steps' full width. Match it to the steps so the player can't walk off the side onto nothing.")]
        [SerializeField] private float width = 2f;

        // Smallest allowed size - a wedge with no length, height or width has
        // no volume, and a convex collider can't be made from it.
        private const float MinSize = 0.01f;

        // The two triangles of each four-sided face, then the two ends.
        // Corners are numbered as in Rebuild(). Clockwise seen from outside.
        private static readonly int[] _triangles = {
            0, 4, 5, 0, 5, 1, // slope
            0, 1, 3, 0, 3, 2, // bottom
            2, 3, 5, 2, 5, 4, // back
            0, 2, 4,          // left side
            1, 5, 3           // right side
        };

        private readonly Vector3[] _corners = new Vector3[6];
        private Mesh _mesh;

        private void Awake()
        {
            Rebuild();
        }

        /// <summary>
        /// Editor-only: runs whenever a value is changed in the Inspector,
        /// and when the scene loads. Keeps the sizes sane and the collider
        /// matching them.
        /// </summary>
        private void OnValidate()
        {
            length = Mathf.Max(length, MinSize);
            height = Mathf.Max(height, MinSize);
            width = Mathf.Max(width, MinSize);

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_mesh != null) {
                Destroy(_mesh);
            }
        }

        /// <summary>
        /// Builds the wedge from the three sizes and gives it to the
        /// MeshCollider. The same Mesh object is reused, so changing a size
        /// in the Inspector doesn't leave old meshes behind.
        /// </summary>
        private void Rebuild()
        {
            MeshCollider meshCollider = GetComponent<MeshCollider>();

            if (meshCollider == null) {
                return;
            }

            if (_mesh == null) {
                // Not saved into the scene or prefab: it is rebuilt from the
                // sizes instead. (Left out of these flags on purpose is
                // "don't unload", so one orphaned in the editor is cleaned up.)
                _mesh = new Mesh {
                    name = "Ramp Collider",
                    hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
                };
            }

            float halfWidth = width * 0.5f;

            // The foot of the slope, on the floor.
            _corners[0] = new Vector3(-halfWidth, 0f, 0f);
            _corners[1] = new Vector3(halfWidth, 0f, 0f);
            // The bottom of the back face.
            _corners[2] = new Vector3(-halfWidth, 0f, length);
            _corners[3] = new Vector3(halfWidth, 0f, length);
            // The top of the back face, where the slope ends.
            _corners[4] = new Vector3(-halfWidth, height, length);
            _corners[5] = new Vector3(halfWidth, height, length);

            _mesh.Clear();
            _mesh.vertices = _corners;
            _mesh.triangles = _triangles;
            _mesh.RecalculateBounds();

            // Clearing the collider's mesh first makes it take the new shape:
            // given the same Mesh object again, it would keep the old one.
            meshCollider.sharedMesh = null;
            meshCollider.convex = true;
            meshCollider.sharedMesh = _mesh;
        }
    }
}
