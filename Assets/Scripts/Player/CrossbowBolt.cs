using Interaction;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// One of the crossbow's pooled bolts: the parts of it that are real
    /// physics, and the bolt as something a hand ray can target.
    ///
    /// In flight a bolt isn't a physics object - PlayerCrossbow moves it
    /// along its arc. Only one that bounces off something becomes one:
    /// BeginTumble() hands it to physics, it falls, and the first floor
    /// it touches damps it hard so it comes to rest quickly rather than
    /// skittering about. EndTumble() takes it back.
    ///
    /// A bolt lying or stuck where it can be picked up has a trigger
    /// ball round it for the hand rays (SetTargetable()). What picking
    /// it up does is PlayerCrossbow's business.
    ///
    /// Made by PlayerCrossbow at load, never placed by hand.
    /// </summary>
    public class CrossbowBolt : MonoBehaviour, IHandTarget
    {
        // Below these it has stopped: metres a second and radians a
        // second, both squared.
        private const float SettledSqrSpeed = 0.0025f;
        private const float SettledSqrSpin = 0.25f;

        // How upward a surface must face for a bolt touching it to have
        // landed on it: the least Y of its normal (0.5 = up to 60
        // degrees from level).
        private const float FloorNormalY = 0.5f;

        // How little the air slows it while it falls.
        private const float FallingDamping = 0.05f;

        private Rigidbody _body;
        private Collider _solidCollider;
        private Collider _grabCollider;
        private float _landedDamping;
        private float _landedAngularDamping;

        // True from BeginTumble() to EndTumble(), and once it has touched
        // something on the way.
        private bool _isTumbling;
        private bool _hasLanded;

        /// Which bolt of the crossbow's pool this is.
        public int Index { get; private set; }

        /// A bolt on the floor is out of arm's reach: it can be targeted
        /// from as far away as a prop.
        public bool HasLongReach => true;

        /// True once a tumbling bolt has touched something and come to
        /// rest (or physics has put it to sleep).
        public bool HasSettled => _hasLanded && (_body.IsSleeping() || (_body.linearVelocity.sqrMagnitude < SettledSqrSpeed && _body.angularVelocity.sqrMagnitude < SettledSqrSpin));

        /// <summary>
        /// Called by the crossbow straight after it adds the component.
        /// </summary>
        public void SetUp(int index, Rigidbody body, Collider solidCollider, Collider grabCollider, float landedDamping, float landedAngularDamping)
        {
            Index = index;
            _body = body;
            _solidCollider = solidCollider;
            _grabCollider = grabCollider;
            _landedDamping = landedDamping;
            _landedAngularDamping = landedAngularDamping;

            _solidCollider.enabled = false;
            _grabCollider.enabled = false;

            // OnEnable() has already run (it runs as the component is
            // added), before there was a collider to register.
            HandTargetRegistry.Register(_grabCollider, this);
        }

        private void OnEnable()
        {
            if (_grabCollider != null) {
                HandTargetRegistry.Register(_grabCollider, this);
            }
        }

        private void OnDisable()
        {
            if (_grabCollider != null) {
                HandTargetRegistry.Unregister(_grabCollider);
            }
        }

        /// <summary>
        /// Whether hand rays can find the bolt: only its trigger ball is
        /// switched, so a bolt that can't be picked up never stops a ray.
        /// </summary>
        public void SetTargetable(bool isTargetable)
        {
            _grabCollider.enabled = isTargetable;
        }

        /// <summary>
        /// Hands the bolt to physics, moving as given. The order matters:
        /// the careful collision checking that stops a thin fast thing
        /// passing through a floor is only allowed on a body physics is
        /// moving, so it is asked for after the body stops being
        /// kinematic.
        /// </summary>
        public void BeginTumble(Vector3 velocity, Vector3 angularVelocity)
        {
            _isTumbling = true;
            _hasLanded = false;

            _solidCollider.enabled = true;
            _body.isKinematic = false;
            _body.useGravity = true;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Physics steps 50 times a second and the headset draws
            // faster: without this the bolt would only move on some
            // frames, and judder. Interpolate draws it part way between
            // physics steps on the frames in between.
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.linearDamping = FallingDamping;
            _body.angularDamping = FallingDamping;
            _body.linearVelocity = velocity;
            _body.angularVelocity = angularVelocity;
        }

        /// <summary>
        /// Takes the bolt back from physics, where it is: it touches
        /// nothing from here on. Does nothing to a bolt that isn't
        /// tumbling.
        /// </summary>
        public void EndTumble()
        {
            if (!_isTumbling) {
                return;
            }

            _isTumbling = false;

            // Placed by code from here on, which interpolation would
            // fight.
            _body.interpolation = RigidbodyInterpolation.None;
            _body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _body.useGravity = false;
            _body.isKinematic = true;
            _solidCollider.enabled = false;
        }

        /// <summary>
        /// A touch while tumbling. Called by Unity as a touch begins.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            CheckLanded(collision);
        }

        /// <summary>
        /// Called by Unity every physics step a touch goes on. Needed as
        /// well as the one above: a wall and the floor below it can be
        /// one collider, and a touch that began on the wall never
        /// "begins" again when the bolt reaches the floor.
        /// </summary>
        private void OnCollisionStay(Collision collision)
        {
            CheckLanded(collision);
        }

        /// <summary>
        /// The first touch on something under the bolt - a floor, a
        /// table top: from here it is damped hard, so it stops where it
        /// comes down. Only a surface facing upwards counts. The bolt
        /// starts its tumble against the very thing it bounced off, and
        /// counting that touch would damp it from the first moment: it
        /// would sink slowly and not turn.
        /// </summary>
        private void CheckLanded(Collision collision)
        {
            if (!_isTumbling || _hasLanded || collision.contactCount == 0) {
                return;
            }

            // A contact's normal points out of the other surface, towards
            // the bolt: mostly upwards means the bolt is on top of it.
            if (collision.GetContact(0).normal.y < FloorNormalY) {
                return;
            }

            _hasLanded = true;
            _body.linearDamping = _landedDamping;
            _body.angularDamping = _landedAngularDamping;
        }
    }
}
