using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Lets the player's body push things out of its way by walking into
    /// them - an open door swings off as the player walks through it.
    /// Whatever a hand can push by pressing on it (an
    /// Interaction.IHandPushable) the body can push too.
    ///
    /// On the Player root, with the CharacterController: Unity calls
    /// OnControllerColliderHit() on that object, from inside
    /// characterController.Move(), for everything the body runs into. So
    /// this has no Tick(): it happens during PlayerController's one Move()
    /// each frame.
    ///
    /// The body is stopped by the door on the frame it walks into it (the
    /// door has moved by then, but Move() has already been blocked), and
    /// walks on into the space the next frame - too short a stop to see.
    /// A latched door, or one a hand holds by its handle, doesn't move.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerBodyPushing : MonoBehaviour
    {
        // How fast the body pushes something away, in metres a second,
        // when walking straight into it. About walking pace: the door
        // then stays just ahead of a walking player, and swings on a
        // little by itself when they stop.
        [SerializeField] private float pushSpeed = 1.5f;

        // A surface the body is walking this squarely into or more (0-1:
        // 1 = head on) is pushed. Lower, and brushing past a door's edge
        // would move it.
        [SerializeField] private float minSquareness = 0.2f;

        /// <summary>
        /// Called by Unity for each thing the body ran into during this
        /// frame's Move(). Floors and walls are the usual case: one
        /// dictionary lookup each, which finds nothing.
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Standing on something isn't pushing it.
            if (hit.normal.y > 0.5f) {
                return;
            }

            IHandPushable pushable = HandPushRegistry.Find(hit.collider);

            if (pushable is null) {
                return;
            }

            // How squarely the body is moving into the surface: the part
            // of its direction that goes against the surface's normal.
            float squareness = -Vector3.Dot(hit.moveDirection, hit.normal);

            if (squareness < minSquareness) {
                return;
            }

            // Pushed straight into the surface, level (a body doesn't
            // push a door up or down), by how far the body would have
            // walked into it this frame.
            Vector3 into = -hit.normal;
            into.y = 0f;

            pushable.Push(hit.point, into * (squareness * pushSpeed * Time.deltaTime));
        }
    }
}
