using System.Collections.Generic;
using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A lock that takes a key: the keyed lock on a door. It only says
    /// where the lock is, which key fits it (the door's key id), whether
    /// it's locked, which way the key turns, and what unlocking it does.
    /// Bringing the keyring to it and turning the key is
    /// Player.PlayerKeys' business.
    ///
    /// This object marks the lock: its position is the middle of the
    /// keyway (in the middle of the door's thickness), its X runs across
    /// the door's face, its Y up it, its Z through the door. A lock in a
    /// door can be worked from either side.
    ///
    /// No Update(): self-registers in a static list, which the player
    /// searches while carrying the keyring.
    /// </summary>
    public class KeyLock : MonoBehaviour, IDebugDrawable
    {
        // The door it locks, whose key id says which key fits.
        [SerializeField] private Door door;

        // The chest it locks, for a lock on a chest rather than a door.
        // Found on a parent if both are left empty.
        [SerializeField] private Chest chest;

        // How far the lock's face is from this object, through the door,
        // in metres - half the door's thickness plus however far the lock
        // plate stands out. The key sits here once it's in.
        [SerializeField] private float faceOffset = 0.031f;

        // The lock opening. Optional (empty = silent).
        [SerializeField] private SoundCue unlockCue;

        // The radius the gizmo draws the lock's face at, in metres.
        private const float GizmoRadius = 0.035f;

        private static readonly Color _lockedColor = new(1f, 0.3f, 0.3f, 1f);
        private static readonly Color _unlockedColor = new(0.3f, 1f, 0.4f, 1f);

        // Every enabled lock, so the player can find the nearest without
        // searching the scene.
        private static readonly List<KeyLock> _all = new();

        private bool _hasDoor;
        private bool _hasChest;

        /// Which key fits: the door's key id.
        public string KeyId => _hasDoor ? door.KeyId : _hasChest ? chest.KeyId : null;

        /// True while the lock is locked.
        public bool IsLocked => _hasDoor ? door.IsLocked : _hasChest && chest.IsLocked;

        /// True while a key is in this lock.
        public bool HasKeyIn { get; private set; }

        /// <summary>
        /// Editor-only: runs when the component is added. Finds the door
        /// above it.
        /// </summary>
        private void Reset()
        {
            door = GetComponentInParent<Door>();
            chest = GetComponentInParent<Chest>();
        }

        private void Awake()
        {
            _hasDoor = door != null;
            // A lock given neither is on a chest if one of its parents is one.
            if (door == null && chest == null) {
                chest = GetComponentInParent<Chest>();
            }

            _hasChest = !_hasDoor && chest != null;

            if (!_hasDoor && !_hasChest) {
                Debug.LogWarning($"KeyLock '{name}': no door or chest assigned, so it locks nothing.", this);
            } else if ((_hasDoor ? door.LockType : chest.LockType) != DoorLock.Keyed) {
                Debug.LogWarning($"KeyLock '{name}': what it's on doesn't have a Keyed lock.", this);
            }
        }

        private void OnEnable()
        {
            _all.Add(this);
            DebugDrawRegistry.Register(this);
        }

        private void OnDisable()
        {
            _all.Remove(this);
            DebugDrawRegistry.Unregister(this);
        }

        /// <summary>
        /// The nearest locked lock, with no key in it, whose middle is
        /// within range metres of point - or null. A distance check per
        /// lock and no physics; there are only ever a few locks in a
        /// level.
        /// </summary>
        public static KeyLock FindInRange(Vector3 point, float range)
        {
            KeyLock nearest = null;
            float nearestSqr = range * range;

            for (int i = 0; i < _all.Count; i++) {
                KeyLock candidate = _all[i];

                // Squared distances: comparing them gives the same answer
                // as comparing the distances, without a square root each.
                float sqr = (candidate.transform.position - point).sqrMagnitude;

                if (sqr < nearestSqr && candidate.IsLocked && !candidate.HasKeyIn) {
                    nearest = candidate;
                    nearestSqr = sqr;
                }
            }

            return nearest;
        }

        /// <summary>
        /// The lock's face on the side viewerPosition is on: the middle of
        /// the keyway on that face, and how something facing the lock
        /// from that side is turned - its forward into the door, its up
        /// the lock's up, so its right is the viewer's right.
        /// </summary>
        public void GetFace(Vector3 viewerPosition, out Vector3 facePoint, out Quaternion facing)
        {
            Vector3 forward = transform.forward;
            bool isInFront = Vector3.Dot(viewerPosition - transform.position, forward) >= 0f;
            Vector3 outward = isInFront ? forward : -forward;

            facePoint = transform.position + outward * faceOffset;
            facing = Quaternion.LookRotation(-outward, transform.up);
        }

        /// <summary>
        /// Which way the key turns to unlock, as the viewer sees it. The
        /// maintainer's rule: anticlockwise if the lock is on the right
        /// of the door leaf from the viewer's side, clockwise if it's on
        /// the left - so the key always turns away from the door's edge.
        /// facing is GetFace()'s.
        /// </summary>
        public bool TurnsAnticlockwise(Quaternion facing)
        {
            if (!_hasDoor && !_hasChest) {
                return false;
            }

            // How far the lock is from the middle of the leaf (or of the
            // chest), measured along the viewer's right: above zero, it's
            // on the right.
            Vector3 centre = _hasDoor ? door.LeafCentre : chest.Centre;
            Vector3 viewerRight = facing * Vector3.right;
            return Vector3.Dot(transform.position - centre, viewerRight) > 0f;
        }

        /// <summary>
        /// A key has gone into the lock: no other can until EndKey().
        /// </summary>
        public void BeginKey()
        {
            HasKeyIn = true;
        }

        /// <summary>
        /// The key has come out again, whether or not it was turned.
        /// </summary>
        public void EndKey()
        {
            HasKeyIn = false;
        }

        /// <summary>
        /// The key has been turned all the way: the door is unlocked (it
        /// stays shut - its handle still has to be turned) until the
        /// level restarts.
        /// </summary>
        public void Unlock()
        {
            if (_hasDoor) {
                door.Unlock();
            } else if (_hasChest) {
                chest.Unlock();
            } else {
                return;
            }

            if (unlockCue == null) {
                return;
            }

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(unlockCue, transform.position, transform);
            } else {
                unlockCue.EmitNoise(transform.position, transform);
            }
        }

        /// <summary>
        /// While selected, draws the lock's two faces - see DrawDebug().
        /// Editor-only.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            DebugLines.ForGizmos.Draw(this, true);
        }

        /// <summary>
        /// In the detailed view (selected, or in the headset with detail
        /// on), draws a ring on each face of the lock - where the key
        /// goes in - red while it's locked, green once it's open.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!detailed) {
                return;
            }

            // Outside Play Mode nothing has been unlocked yet: shown as
            // locked.
            bool isLocked = !Application.isPlaying || IsLocked;
            lines.Color = isLocked ? _lockedColor : _unlockedColor;

            Vector3 right = transform.right * GizmoRadius;
            Vector3 up = transform.up * GizmoRadius;

            for (int side = -1; side <= 1; side += 2) {
                lines.Circle(transform.position + transform.forward * (side * faceOffset), right, up);
            }
        }
    }
}
