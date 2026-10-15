using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelGen
{
    /// <summary>
    /// Puts pieces in the scene for the generator: each one a real prefab
    /// instance (PrefabUtility.InstantiatePrefab, so it keeps its link to its
    /// variant), with any Light inside it disabled on the instance - the
    /// prefab itself is never touched. Where a piece goes is worked out from
    /// its catalogue measurements, so the caller says where its walking
    /// surface (or, later, its connection points) should be, not where its
    /// pivot is.
    /// </summary>
    internal class PiecePlacer
    {
        private readonly Dictionary<string, CatalogueEntry> _entries = new();
        private readonly Dictionary<string, GameObject> _prefabs = new();

        // How many pieces have been placed.
        internal int Placed { get; private set; }

        /// <summary>
        /// A placer for the pieces in one catalogue.
        /// </summary>
        internal PiecePlacer(PrefabCatalogue catalogue)
        {
            foreach (CatalogueEntry entry in catalogue.prefabs) {
                _entries[entry.guid] = entry;
            }
        }

        /// <summary>
        /// A piece's catalogue entry, or null if it is no longer catalogued.
        /// </summary>
        internal CatalogueEntry Entry(PieceAnnotation piece)
        {
            return _entries.TryGetValue(piece.guid, out CatalogueEntry entry) ? entry : null;
        }

        /// <summary>
        /// Places a piece turned yaw degrees about Y so that a point in its
        /// own space (a connection point, a face's middle) lands on a world
        /// point. Returns null if its prefab can't be found.
        /// </summary>
        internal GameObject PlaceAt(PieceAnnotation piece, Vector3 localPoint, Vector3 worldPoint, float yaw, Transform parent)
        {
            return PlaceAt(piece, localPoint, worldPoint, yaw, Vector3.one, parent);
        }

        /// <summary>
        /// The same, the piece scaled along its own axes first (a wall
        /// stretched or squeezed to fit its module; the walls' prefab roots
        /// are unrotated, so their own axes are the root's).
        /// </summary>
        internal GameObject PlaceAt(PieceAnnotation piece, Vector3 localPoint, Vector3 worldPoint, float yaw, Vector3 scale, Transform parent)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            GameObject instance = Place(piece, worldPoint - rotation * Vector3.Scale(scale, localPoint), rotation, parent);

            if (instance != null && scale != Vector3.one) {
                instance.transform.localScale = Vector3.Scale(instance.transform.localScale, scale);
            }

            return instance;
        }

        /// <summary>
        /// One of the pieces at random, never the one before it unless it is
        /// the only one (null for an empty list).
        /// </summary>
        internal static PieceAnnotation Pick(IReadOnlyList<PieceAnnotation> pieces, PieceAnnotation previous, System.Random random)
        {
            if (pieces.Count == 0) {
                return null;
            }

            if (pieces.Count == 1) {
                return pieces[0];
            }

            int previousIndex = -1;

            for (int i = 0; i < pieces.Count; i++) {
                if (pieces[i] == previous) {
                    previousIndex = i;
                }
            }

            if (previousIndex < 0) {
                return pieces[random.Next(pieces.Count)];
            }

            // Choose among the others: skip over the previous one's index.
            int index = random.Next(pieces.Count - 1);
            return pieces[index >= previousIndex ? index + 1 : index];
        }

        /// <summary>
        /// Places a floor or ground tile so the middle of its walking surface
        /// (its collider tops) is at surfaceCentre, turned yaw degrees about Y.
        /// Returns null if its prefab can't be found.
        /// </summary>
        internal GameObject PlaceTile(PieceAnnotation piece, Vector3 surfaceCentre, float yaw, Transform parent)
        {
            if (!_entries.TryGetValue(piece.guid, out CatalogueEntry entry)) {
                return null;
            }

            // The walking surface's middle in the prefab's own space: from the
            // collider tops if it has a walking surface, else the top of the
            // render box.
            Vector3 surface = entry.hasWalkSurface
                ? new Vector3((entry.walkMin.x + entry.walkMax.x) * 0.5f, entry.walkHeight, (entry.walkMin.z + entry.walkMax.z) * 0.5f)
                : new Vector3((entry.renderMin.x + entry.renderMax.x) * 0.5f, entry.renderMax.y, (entry.renderMin.z + entry.renderMax.z) * 0.5f);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            return Place(piece, surfaceCentre - rotation * surface, rotation, parent);
        }

        /// <summary>
        /// Instantiates a piece's prefab under a parent with its pivot at a
        /// world position and rotation, its lights disabled.
        /// </summary>
        private GameObject Place(PieceAnnotation piece, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (!_prefabs.TryGetValue(piece.guid, out GameObject prefab)) {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(piece.guid));
                _prefabs.Add(piece.guid, prefab);
            }

            if (prefab == null) {
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.SetPositionAndRotation(position, rotation);

            // Level lighting is placed by hand: a light in a piece is switched
            // off on this instance (an override), the prefab left as it is.
            foreach (Light light in instance.GetComponentsInChildren<Light>(true)) {
                light.enabled = false;
            }

            Placed++;
            return instance;
        }
    }
}
