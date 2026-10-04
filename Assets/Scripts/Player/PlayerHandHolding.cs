using System.Collections.Generic;
using Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace Player
{
    /// <summary>
    /// Picking up, carrying and dropping props (Interaction.Grabbable). A
    /// hand picks up the prop its hand ray is on while grip is held, the
    /// same way PlayerClimbing grabs a ledge - what the reticle is on is
    /// what gets picked up. Letting go of grip drops it.
    ///
    /// Climbing snaps the hand onto the ledge; this does the reverse, since
    /// a prop can move and a ledge can't: the hand stays where it is and
    /// the prop is brought to it, into the pose the prop's grip point and
    /// snap profile describe, over a short blend so it doesn't pop.
    ///
    /// The prop follows the hand VISUAL, not the controller. The visual
    /// stops at surfaces (the physical hands), and while a prop is carried
    /// the hand's collision shape is enlarged to cover it
    /// (PlayerHandVisuals.SetHeldShape) - so a carried prop stops at a wall
    /// with the hand instead of being pushed through it.
    ///
    /// A hand does one thing at a time: a hand gripping a climbable can't
    /// pick up, and a hand carrying a prop can't climb (PlayerClimbing
    /// checks IsLeftHolding/IsRightHolding).
    ///
    /// Lives on the Hands object. No Update(): PlayerController calls Tick()
    /// (pick up / drop) after climbing, and TickHeld() (placing carried
    /// props) straight after the hand visuals are placed. Carried props are
    /// also placed once more just before rendering - see PlaceHeldForRender().
    /// </summary>
    public class PlayerHandHolding : MonoBehaviour
    {
        /// One hand's carrying state. A class, made once per hand in Awake(),
        /// so the methods below can change it without ref parameters.
        private class HandHold
        {
            // The hand model's transform - what a carried prop follows.
            public Transform visual;
            public bool isLeftHand;

            // What this hand is carrying, or null.
            public Grabbable grabbable;

            // The prop's pose relative to the hand visual while carried
            // (position and rotation only - no scale, so the right hand's
            // mirrored visual needs no special handling).
            public Vector3 localPosition;
            public Quaternion localRotation;

            // Where the prop was when it was picked up, and how far (0-1)
            // it has blended from there into the hand.
            public Vector3 fromPosition;
            public Quaternion fromRotation;
            public float blend;
        }

        [SerializeField] private PlayerInputXR playerInput;
        [SerializeField] private PlayerHandInteraction playerHandInteraction;
        [SerializeField] private PlayerHandVisuals playerHandVisuals;
        [SerializeField] private PlayerClimbing playerClimbing;

        // Seconds for a picked-up prop to move into the hand.
        [SerializeField] private float pickUpBlendDuration = 0.1f;

        // The layer carried props are moved to. The hands' own layer: the
        // player's body doesn't collide with it, and neither hand rays nor
        // the hands' own collision sweep include it.
        private const string HeldLayerName = "PlayerHands";

        private HandHold _left;
        private HandHold _right;
        private int _heldLayer;

        /// True while the left hand is carrying a prop.
        public bool IsLeftHolding => _left.grabbable is not null;

        /// True while the right hand is carrying a prop.
        public bool IsRightHolding => _right.grabbable is not null;

        /// What the left hand is carrying, or null - for throwing, and for
        /// anything that cares what the player has in hand.
        public Grabbable LeftHeld => _left.grabbable;

        /// What the right hand is carrying, or null.
        public Grabbable RightHeld => _right.grabbable;

        /// <summary>
        /// Editor-only: fills in the references when the component is added
        /// (on the Hands object; the body systems are on the Player root
        /// above it).
        /// </summary>
        private void Reset()
        {
            playerInput = GetComponentInParent<PlayerInputXR>();
            playerClimbing = GetComponentInParent<PlayerClimbing>();
            playerHandInteraction = GetComponent<PlayerHandInteraction>();
            playerHandVisuals = GetComponent<PlayerHandVisuals>();
        }

        private void Awake()
        {
            _heldLayer = LayerMask.NameToLayer(HeldLayerName);

            // The visuals are read here, while they're still children of
            // their controllers - PlayerHandVisuals exposes them.
            _left = new HandHold { visual = playerHandVisuals.LeftHandVisual, isLeftHand = true };
            _right = new HandHold { visual = playerHandVisuals.RightHandVisual, isLeftHand = false };
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += PlaceHeldForRender;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= PlaceHeldForRender;

            // Nothing may be left kinematic on the hands' layer with no hand
            // carrying it - e.g. when the scene is being unloaded.
            Drop(_left);
            Drop(_right);
        }

        /// <summary>
        /// Picks up and drops, for both hands. Called by PlayerController
        /// after PlayerClimbing.Tick(), so it sees this frame's climbing
        /// grips.
        /// </summary>
        public void Tick()
        {
            TickHand(
                _left,
                playerInput.IsLeftGrabbing,
                playerClimbing.IsLeftHandGripping,
                playerHandInteraction.LeftTarget,
                playerHandInteraction.LeftTargetPoint);

            TickHand(
                _right,
                playerInput.IsRightGrabbing,
                playerClimbing.IsRightHandGripping,
                playerHandInteraction.RightTarget,
                playerHandInteraction.RightTargetPoint);
        }

        /// <summary>
        /// One hand: drop what it's carrying if grip was let go, otherwise
        /// pick up the prop its ray is on if grip is held and the hand is
        /// free.
        /// </summary>
        private void TickHand(HandHold hold, bool isGrabbing, bool isClimbing, IHandTarget rayTarget, Vector3 rayTargetPoint)
        {
            if (hold.grabbable is not null) {
                if (!isGrabbing) {
                    Drop(hold);
                }

                return;
            }

            if (!isGrabbing || isClimbing) {
                return;
            }

            // The ray target is only an IHandTarget; a type pattern checks
            // whether it's a prop (as PlayerClimbing does for climbables).
            // One already in the other hand can't be targeted at all - it's
            // off the layers hand rays hit - but check anyway.
            if (rayTarget is not Grabbable grabbable || grabbable.IsHeld) {
                return;
            }

            PickUp(hold, grabbable, rayTargetPoint);
        }

        /// <summary>
        /// Starts carrying grabbable in this hand. Works out, once, where
        /// the prop sits relative to the hand: the prop says where the hand
        /// would have to be to hold it as it lies (GetSnapPose), and the
        /// prop's pose measured from that hand pose is the pose to carry it
        /// at.
        /// </summary>
        private void PickUp(HandHold hold, Grabbable grabbable, Vector3 grabPoint)
        {
            HandSnapPose handPose = grabbable.GetSnapPose(hold.isLeftHand, grabPoint, default, default);
            Transform prop = grabbable.transform;
            prop.GetPositionAndRotation(out Vector3 propPosition, out Quaternion propRotation);

            // "Measured from the hand pose": undo the hand's rotation, so
            // the offset is in the hand's own axes and turns with it.
            Quaternion toHand = Quaternion.Inverse(handPose.Rotation);
            hold.localPosition = toHand * (propPosition - handPose.Position);
            hold.localRotation = toHand * propRotation;

            hold.fromPosition = propPosition;
            hold.fromRotation = propRotation;
            hold.blend = 0f;
            hold.grabbable = grabbable;

            grabbable.BeginHold(_heldLayer);

            // The hand's collision now covers the prop too: a ball round
            // the prop's middle, given where that is relative to the hand.
            Vector3 centreFromHand = hold.localPosition + hold.localRotation * grabbable.LocalCentre;
            playerHandVisuals.SetHeldShape(hold.isLeftHand, centreFromHand, grabbable.HoldRadius);
        }

        /// <summary>
        /// Lets go of whatever this hand is carrying (nothing happens if
        /// it's empty): the prop goes back under physics, from rest, and
        /// the hand's collision goes back to just the hand.
        /// </summary>
        private void Drop(HandHold hold)
        {
            if (hold == null || hold.grabbable is null) {
                return;
            }

            // Unity's == null is true for a prop destroyed while carried.
            if (hold.grabbable != null) {
                hold.grabbable.EndHold();
            }

            hold.grabbable = null;
            playerHandVisuals.ClearHeldShape(hold.isLeftHand);
        }

        /// <summary>
        /// Moves the pick-up blends on and places carried props at the hand
        /// visuals. Called by PlayerController straight after
        /// PlayerHandVisuals.Tick(), every frame - also during a mantle and
        /// once the level has ended, so a carried prop never hangs in the
        /// air.
        /// </summary>
        public void TickHeld()
        {
            Advance(_left);
            Advance(_right);
            Place(_left);
            Place(_right);
        }

        private void Advance(HandHold hold)
        {
            if (hold.grabbable is null || hold.blend >= 1f) {
                return;
            }

            float step = pickUpBlendDuration > 0f ? Time.deltaTime / pickUpBlendDuration : 1f;
            hold.blend = Mathf.MoveTowards(hold.blend, 1f, step);
        }

        /// <summary>
        /// Just before the frame is drawn. The hands' Tracked Pose Drivers
        /// move the controllers once more after all Update() code, with
        /// fresher tracking, and the hand visuals go with them - so a prop
        /// placed only in TickHeld() would be drawn slightly behind the
        /// hand, and wobble in it as the hand moves. Placing it again here
        /// keeps it locked to the hand that's drawn. (Not parented to the
        /// hand instead, because the right hand visual is mirrored: a child
        /// would be drawn mirrored too.)
        /// </summary>
        private void PlaceHeldForRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            Place(_left);
            Place(_right);
        }

        /// <summary>
        /// Puts this hand's prop at its carried pose relative to the hand
        /// visual - or, while the pick-up blend is running, part of the way
        /// there from where it was picked up.
        /// </summary>
        private void Place(HandHold hold)
        {
            if (hold.grabbable is null) {
                return;
            }

            // Destroyed while carried (Unity's == null): just let go.
            if (hold.grabbable == null) {
                Drop(hold);
                return;
            }

            hold.visual.GetPositionAndRotation(out Vector3 handPosition, out Quaternion handRotation);
            Vector3 position = handPosition + handRotation * hold.localPosition;
            Quaternion rotation = handRotation * hold.localRotation;

            if (hold.blend < 1f) {
                float t = Mathf.SmoothStep(0f, 1f, hold.blend);
                position = Vector3.Lerp(hold.fromPosition, position, t);
                rotation = Quaternion.Slerp(hold.fromRotation, rotation, t);
            }

            hold.grabbable.transform.SetPositionAndRotation(position, rotation);
        }
    }
}
