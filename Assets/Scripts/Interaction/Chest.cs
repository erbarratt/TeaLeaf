using Core;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// A chest: a box with a hinged lid, and a lock like a door's - none,
    /// simple (can be picked) or keyed (its own key only). Locked, the lid
    /// won't move. Unlocked by any means, the lid is opened by taking hold
    /// of its front (ChestLid) and lifting the hand.
    ///
    /// On the chest's body. Its own axes: X along the hinge, Y up, Z
    /// towards the front - the side it's opened from. The lid is a child
    /// object ON the hinge line, with the same axes: it turns about its
    /// own X, and its front is along its +Z.
    ///
    /// The lid's angle is set from code, as a door's is: no joint and no
    /// forces. A lock on it is a PickableLock or a KeyLock with this chest
    /// given to it instead of a door. No Update(): nothing happens unless
    /// a hand is on the lid.
    /// </summary>
    public class Chest : MonoBehaviour
    {
        // What kind of lock it has, and for a keyed one the id its key
        // must have (the same text as the Key's Key Id).
        [SerializeField] private DoorLock lockType = DoorLock.None;
        [SerializeField] private string keyId;

        // Whether it starts the level locked. Only with a lock.
        [SerializeField] private bool startsLocked = true;

        // The lid: a child on the hinge line that turns about its own X.
        [SerializeField] private Transform lid;

        // How far the lid opens, in degrees, and the angle below which a
        // lid that's let go of drops shut.
        [SerializeField] private float maxOpenAngle = 100f;
        [SerializeField] private float closeAngle = 8f;

        // The lid starting to open, dropping shut, and being tried while
        // locked. All optional.
        [SerializeField] private SoundCue openCue;
        [SerializeField] private SoundCue closeCue;
        [SerializeField] private SoundCue lockedCue;

        // How the lid is turned when shut.
        private Quaternion _lidRestRotation;

        public DoorLock LockType => lockType;
        public string KeyId => keyId;

        /// Whether the lock is holding the lid shut.
        public bool IsLocked { get; private set; }

        /// How far open the lid is, in degrees: 0 = shut.
        public float Angle { get; private set; }

        /// Whether the lid is open at all.
        public bool IsOpen => Angle > 0f;

        /// The middle of the chest, for working out which side of it a
        /// lock is on (which way a key turns).
        public Vector3 Centre => transform.position;

        private void Awake()
        {
            IsLocked = lockType != DoorLock.None && startsLocked;

            if (lid == null) {
                Debug.LogError($"Chest '{name}': no Lid assigned.", this);
                enabled = false;
                return;
            }

            _lidRestRotation = lid.localRotation;
        }

        /// <summary>
        /// Unlocks it - picked, or opened with its key. The lid stays shut
        /// until it's lifted.
        /// </summary>
        public void Unlock()
        {
            IsLocked = false;
        }

        /// <summary>
        /// Locks it, if it has a lock and the lid is shut. False if not.
        /// </summary>
        public bool Lock()
        {
            if (lockType == DoorLock.None || IsOpen) {
                return false;
            }

            IsLocked = true;
            return true;
        }

        /// <summary>
        /// The angle the lid would be at for its front to pass through a
        /// world-space point: how far round the hinge the point is, from
        /// level with the front (0) up and over. Not limited - SetAngle()
        /// does that.
        /// </summary>
        public float AngleAt(Vector3 worldPoint)
        {
            // From the hinge to the point, as how far up and how far
            // forward it is. Atan2 turns those two into the angle.
            Vector3 fromHinge = worldPoint - lid.position;
            float up = Vector3.Dot(fromHinge, transform.up);
            float forward = Vector3.Dot(fromHinge, transform.forward);

            return Mathf.Atan2(up, forward) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Asks for the lid to be at an angle (kept within what it can
        /// do). False, and nothing moves, while it's locked.
        /// </summary>
        public bool SetAngle(float angle)
        {
            if (IsLocked || lid == null) {
                return false;
            }

            angle = Mathf.Clamp(angle, 0f, maxOpenAngle);

            if (angle == Angle) {
                return true;
            }

            bool wasShut = Angle <= 0f;
            Angle = angle;

            // A negative turn about X lifts the lid's front (its +Z end).
            lid.localRotation = _lidRestRotation * Quaternion.Euler(-angle, 0f, 0f);

            if (wasShut) {
                Play(openCue);
            } else if (angle <= 0f) {
                Play(closeCue);
            }

            return true;
        }

        /// <summary>
        /// The lid has been let go of: nearly shut, it drops shut;
        /// otherwise it stays where it was left.
        /// </summary>
        public void Release()
        {
            if (Angle > 0f && Angle < closeAngle) {
                SetAngle(0f);
            }
        }

        /// <summary>
        /// The lid was tried while locked: the rattle.
        /// </summary>
        public void RattleLocked()
        {
            Play(lockedCue);
        }

        /// <summary>
        /// Plays a cue from the lid (and emits its noise for guards), if
        /// there is one.
        /// </summary>
        private void Play(SoundCue cue)
        {
            if (cue == null) {
                return;
            }

            SoundPlayer soundPlayer = SoundPlayer.Instance;

            if (soundPlayer != null) {
                soundPlayer.Play(cue, lid.position, transform);
            } else {
                cue.EmitNoise(lid.position, transform);
            }
        }
    }
}
