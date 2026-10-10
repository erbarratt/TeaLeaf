using System;
using System.Collections.Generic;
using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A lock the player can pick: the simple lock on a door (and, later,
    /// on a chest). It only says where the lock is, whether it's locked,
    /// and what unlocking it does. The picking itself is worked on the big
    /// copy of the lock (BigLock), by Player.PlayerLockpicking.
    ///
    /// This object marks the lock: its position is the middle of the
    /// keyway (in the middle of the door's thickness), its X runs across
    /// the door's face, its Y up it, its Z through the door. The same
    /// object as the door's DoorKeyhole, normally. A lock in a door can be
    /// picked from either side.
    ///
    /// No Update(): self-registers in a static list, which the player
    /// searches while carrying the picks.
    /// </summary>
    public class PickableLock : MonoBehaviour, IDebugDrawable
    {
        // The door it locks. Optional: with none (a chest, later) it keeps
        // its own locked state and raises Unlocked for whatever it holds
        // shut.
        [SerializeField] private Door door;

        // The chest it locks, for a lock on a chest rather than a door.
        // Found on a parent if both are left empty.
        [SerializeField] private Chest chest;

        // How far the lock's face is from this object, through the door,
        // in metres - half the door's thickness plus however far the lock
        // plate stands out. The picks sit here once they're in.
        [SerializeField] private float faceOffset = 0.031f;

        // The radius the gizmo draws the lock's face at, in metres.
        private const float GizmoRadius = 0.035f;

        private static readonly Color _lockedColor = new(1f, 0.3f, 0.3f, 1f);
        private static readonly Color _unlockedColor = new(0.3f, 1f, 0.4f, 1f);

        // Every enabled lock, so the player can find the nearest without
        // searching the scene.
        private static readonly List<PickableLock> _all = new();

        private bool _hasDoor;
        private bool _hasChest;

        // False if the door's lock isn't the kind that can be picked.
        private bool _isPickable = true;

        // The locked state of a lock with no door.
        private bool _isLockedAlone = true;

        /// True while the lock is locked.
        public bool IsLocked => _hasDoor ? door.IsLocked : _hasChest ? chest.IsLocked : _isLockedAlone;

        /// True while the picks are in this lock.
        public bool IsBeingPicked { get; private set; }

        /// True if the picks can be put in: it's locked, it's the kind of
        /// lock that can be picked, and the picks aren't already in it.
        public bool CanBePicked => _isPickable && !IsBeingPicked && IsLocked;

        /// Raised when the lock is picked open - for a lock with no door
        /// (a chest opens its lid).
        public event Action Unlocked;

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

            if (_hasChest && chest.LockType != DoorLock.Simple) {
                Debug.LogWarning($"PickableLock '{name}': its chest's lock isn't a Simple one, so it can't be picked.", this);
                _isPickable = false;
            }

            if (_hasDoor && door.LockType != DoorLock.Simple) {
                Debug.LogWarning($"PickableLock '{name}': its door's lock isn't a Simple one, so it can't be picked.", this);
                _isPickable = false;
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
        /// The nearest lock that can be picked whose middle is within
        /// range metres of point, or null. A distance check per lock and
        /// no physics; there are only ever a few locks in a level.
        /// </summary>
        public static PickableLock FindInRange(Vector3 point, float range)
        {
            PickableLock nearest = null;
            float nearestSqr = range * range;

            for (int i = 0; i < _all.Count; i++) {
                PickableLock candidate = _all[i];

                // Squared distances: comparing them gives the same answer
                // as comparing the distances, without a square root each.
                float sqr = (candidate.transform.position - point).sqrMagnitude;

                if (sqr < nearestSqr && candidate.CanBePicked) {
                    nearest = candidate;
                    nearestSqr = sqr;
                }
            }

            return nearest;
        }

        /// <summary>
        /// The lock's face on the side viewerPosition is on: the middle of
        /// the keyway on that face, and the direction straight out of it,
        /// towards that side.
        /// </summary>
        public void GetFace(Vector3 viewerPosition, out Vector3 facePoint, out Vector3 outward)
        {
            Vector3 forward = transform.forward;
            bool isInFront = Vector3.Dot(viewerPosition - transform.position, forward) >= 0f;

            outward = isInFront ? forward : -forward;
            facePoint = transform.position + outward * faceOffset;
        }

        /// <summary>
        /// The picks have gone into the lock: nothing else can pick it
        /// until EndPicking().
        /// </summary>
        public void BeginPicking()
        {
            IsBeingPicked = true;
        }

        /// <summary>
        /// The picks have come out again, whether or not it was opened.
        /// </summary>
        public void EndPicking()
        {
            IsBeingPicked = false;
        }

        /// <summary>
        /// Opens the lock: called by the big lock when the last turn is
        /// made. A door is unlocked (it stays shut - its handle still has
        /// to be turned) until the level restarts.
        /// </summary>
        public void Unlock()
        {
            if (_hasDoor) {
                door.Unlock();
            } else if (_hasChest) {
                chest.Unlock();
            }

            _isLockedAlone = false;
            Unlocked?.Invoke();
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
        /// on), draws a ring on each face of the lock - where the picks go
        /// in - red while it's locked, green once it's open.
        /// </summary>
        public void DrawDebug(DebugLines lines, bool detailed)
        {
            if (!detailed) {
                return;
            }

            // Outside Play Mode Awake() hasn't run and nothing has been
            // unlocked yet: shown as locked.
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
