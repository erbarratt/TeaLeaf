using System.Collections;
using System.Collections.Generic;
using Core;
using UnityEngine;
using UnityEngine.XR;

namespace Player
{
    /// <summary>
    /// Central access point for all VR tracked transforms.
    ///
    /// Other systems should ask THIS class for head and hand positions,
    /// rather than searching through the XR Rig hierarchy themselves.
    ///
    /// This means if Unity changes its XR hierarchy later,
    /// we only need to update one script.
    /// </summary>
    public class PlayerTracking : MonoBehaviour
    {
        [Header("Tracked XR Objects")]

        // Reference to the player's VR headset camera.
        // This transform moves and rotates with the user's head.
        [SerializeField] private Transform head;

        // Reference to the left controller transform.
        // Updated automatically by OpenXR every frame.
        [SerializeField] private Transform leftHand;

        // Reference to the right controller transform.
        // Updated automatically by OpenXR every frame.
        [SerializeField] private Transform rightHand;

        [Header("Calibration")]

        // Check the view when the level starts, behind the level-start fade,
        // and again whenever the headset is recentred: upright, and at
        // standing eye height - see Calibrate(). Off, the view is left
        // exactly as the tracking reports it.
        [SerializeField] private bool calibrateView = true;

        // The longest to wait for the headset to report a tracked pose
        // before calibrating anyway, in seconds - the screen stays black
        // meanwhile (e.g. the headset is off the player's head).
        [SerializeField] private float trackingTimeout = 3f;

        /// Which device's smoothing settings to use.
        public enum SmoothingTarget
        {
            // Quest standalone in an Android build, PCVR otherwise (the
            // editor and Windows builds, including a Quest streamed to
            // the PC).
            Auto,
            Pcvr,
            Quest
        }

        [Header("Hand Smoothing")]

        // Auto picks by what the game is running on. Pcvr and Quest force
        // one set - e.g. to try the Quest's numbers in the editor.
        [SerializeField] private SmoothingTarget smoothingTarget = SmoothingTarget.Auto;

        // Smoothing of the controllers' tracking, per device - see
        // HandSmoothingSettings and SmoothHands(). Read every frame, so
        // they can be tuned while playing. PCVR starts a little stronger:
        // streaming adds shake of its own.
        [SerializeField] private HandSmoothingSettings pcvrSmoothing = new();

        [SerializeField] private HandSmoothingSettings questSmoothing = new() {
            positionMinCutoff = 5f,
            rotationMinCutoff = 5f
        };

        // A head moving faster than this between two frames, in metres or
        // degrees per second, didn't really move: the tracking origin did
        // (the headset was recentred). Far beyond anything a neck can do -
        // a fast head turn is a few hundred degrees per second.
        private const float RecentreHeadSpeed = 8f;
        private const float RecentreHeadTurnSpeed = 1500f;

        // Filled by SubsystemManager.GetSubsystems() in Start() - a reused
        // list, so the lookup doesn't allocate a new one.
        private static readonly List<XRInputSubsystem> _inputSubsystems = new();

        // For reading how far the game's crouch has lowered the view - see
        // Calibrate().
        private CharacterController _characterController;
        private float _standingControllerHeight;

        // Camera Offset's saved height (1.6m): the standing eye height.
        private float _standingEyeHeight;

        // Set once the level-start calibration has run; Tick() does nothing
        // before that.
        private bool _isCalibrated;

        // The head's pose last frame, relative to Camera Offset (raw
        // tracking data) - see Tick().
        private Vector3 _lastHeadLocalPosition;
        private Quaternion _lastHeadLocalRotation;

        // One smoothing filter per hand - see SmoothHands().
        private readonly HandPoseFilter _leftHandFilter = new();
        private readonly HandPoseFilter _rightHandFilter = new();

        // Whether smoothing ran last frame, so the filters are reset once
        // when it's switched off rather than every frame it stays off.
        private bool _wasSmoothing;

        private void Awake()
        {

            _characterController = GetComponent<CharacterController>();
            _standingControllerHeight = _characterController.height;
            _standingEyeHeight = head.parent.localPosition.y;
        }

        /// <summary>
        /// Puts XR tracking in Device mode (replaced XROrigin 2026-09-30,
        /// which did only this for us, plus setting the height below). Device
        /// mode measures the head from where the headset was at startup (or
        /// the last recentre) rather than from the real floor, and Camera
        /// Offset - saved 1.6m up in the scene - lifts that to standing eye
        /// height. So every player stands at the same height whatever their
        /// real height, while real crouching still lowers the head. Once, at
        /// startup: XR is initialised before the first scene loads, so the
        /// subsystem is already running by Start().
        /// </summary>
        private void Start()
        {
            SubsystemManager.GetSubsystems(_inputSubsystems);
            bool isSet = false;

            foreach (XRInputSubsystem subsystem in _inputSubsystems) {
                isSet |= subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
            }

            if (!isSet) {
                Debug.LogWarning("PlayerTracking: couldn't set Device tracking mode - is a headset connected and XR initialised on startup? Head height may be wrong.", this);
            }

            // No XR running at all (e.g. Play in the editor with no headset):
            // nothing to calibrate, and no reason to keep the screen black.
            if (calibrateView && _inputSubsystems.Count > 0) {
                StartCoroutine(CalibrateWhenTracked());
            }
        }

        /// <summary>
        /// Calibrates the view once at level start, while the screen is
        /// still black: waits until the headset reports a tracked pose (or
        /// trackingTimeout runs out), calibrates (Calibrate()), then lets
        /// the level-start fade go ahead. Added 2026-10-03: through Virtual
        /// Desktop/SteamVR the level often started with the view upside down
        /// and at floor level until the headset was recentred by hand.
        /// Waiting for tracking first matters - calibrating on a pose the
        /// runtime hasn't worked out yet would lock in whatever rubbish it
        /// reported.
        ///
        /// A coroutine because it's a one-off wait at startup, not a
        /// per-frame system: it runs for a few frames and ends.
        /// </summary>
        private IEnumerator CalibrateWhenTracked()
        {
            // Keep the level-start fade black until this is done. Kept in a
            // local so the same fade is released that was held.
            ScreenFade fade = ScreenFade.Instance;

            if (fade != null) {
                fade.Hold();
            }

            float waited = 0f;

            while (!IsHeadTracked() && waited < trackingTimeout) {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Calibrate();
            _lastHeadLocalPosition = head.localPosition;
            _lastHeadLocalRotation = head.localRotation;
            _isCalibrated = true;

            // One more frame, so everything that follows the head and hands
            // has caught up with the corrected rig before anything is seen.
            yield return null;

            if (fade != null) {
                fade.Release();
            }
        }

        /// <summary>
        /// Whether the headset is currently reporting a tracked position and
        /// rotation.
        /// </summary>
        private static bool IsHeadTracked()
        {
            const InputTrackingState tracked = InputTrackingState.Position | InputTrackingState.Rotation;
            InputDevice headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);

            return headDevice.isValid
                && headDevice.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state)
                && (state & tracked) == tracked;
        }

        /// <summary>
        /// Takes the small shake out of both controllers' poses, by
        /// replacing each controller object's tracked pose with a smoothed
        /// one (HandPoseFilter) for the rest of the frame.
        ///
        /// Each frame, before any script's Update(), the Tracked Pose
        /// Drivers put the controllers exactly where the tracking says.
        /// This runs first thing in the player's frame and writes the
        /// smoothed pose over that - so everything that follows the
        /// controller objects (the hand models, hand rays, the throw arc, a
        /// carried prop) is steadied at once, with no changes of their own.
        /// The hands' drivers don't move them again before rendering, so
        /// the smoothed pose is also the one that's drawn.
        ///
        /// Smoothed relative to the controllers' parent - the tracking
        /// space - not in the world: walking or turning the body then isn't
        /// hand movement, so it neither weakens the smoothing nor leaves
        /// the hands trailing behind the body.
        ///
        /// Climbing uses the smoothed pose too: it moves the body by the
        /// hand's movement, so a shaking hand on a ledge shook the whole
        /// view.
        /// </summary>
        private void SmoothHands()
        {
            leftHand.GetLocalPositionAndRotation(out Vector3 leftPosition, out Quaternion leftRotation);
            rightHand.GetLocalPositionAndRotation(out Vector3 rightPosition, out Quaternion rightRotation);

            HandSmoothingSettings settings = CurrentSmoothing();

            if (!settings.enabled) {
                if (_wasSmoothing) {
                    _wasSmoothing = false;
                    _leftHandFilter.Reset();
                    _rightHandFilter.Reset();
                }

                return;
            }

            _wasSmoothing = true;

            // Real time, not game time: the hands are the player's own, and
            // must keep up even if the game is ever slowed or paused.
            float deltaTime = Time.unscaledDeltaTime;

            _leftHandFilter.Filter(ref leftPosition, ref leftRotation, settings, deltaTime);
            _rightHandFilter.Filter(ref rightPosition, ref rightRotation, settings, deltaTime);

            leftHand.SetLocalPositionAndRotation(leftPosition, leftRotation);
            rightHand.SetLocalPositionAndRotation(rightPosition, rightRotation);
        }

        /// <summary>
        /// The smoothing settings for the device the game is running on (or
        /// the one smoothingTarget forces).
        /// </summary>
        private HandSmoothingSettings CurrentSmoothing()
        {
            switch (smoothingTarget) {
                case SmoothingTarget.Pcvr:
                    return pcvrSmoothing;
                case SmoothingTarget.Quest:
                    return questSmoothing;
                default:
                    // A Quest standalone build is an Android build; the
                    // editor and Windows builds are PCVR.
                    return Application.platform == RuntimePlatform.Android ? questSmoothing : pcvrSmoothing;
            }
        }

        /// <summary>
        /// Watches for the headset being recentred mid-game (the Quest's own
        /// recentre, or the runtime sorting its tracking out), and calibrates
        /// again when it happens. A recentre shows up as the head's raw
        /// tracked pose jumping between two frames, further or faster than a
        /// real head can move. This matters most after the level-start fix:
        /// if the tracking space started upside down and Calibrate() turned
        /// Camera Offset to cancel it, a recentre that puts the tracking
        /// right leaves that turn making the view upside down instead - so
        /// it has to be undone the moment the jump is seen.
        ///
        /// Called by PlayerController each frame, before the systems that
        /// read the head. Costs a distance and an angle check. Also smooths
        /// the hands' tracking first - see SmoothHands().
        /// </summary>
        public void Tick()
        {
            SmoothHands();

            if (!_isCalibrated) {
                return;
            }

            Vector3 position = head.localPosition;
            Quaternion rotation = head.localRotation;

            // Scaled by the frame's length, so a slow frame (a hitch) with an
            // ordinary head movement across it isn't mistaken for a jump.
            float deltaTime = Time.unscaledDeltaTime;
            bool hasJumped =
                Vector3.Distance(position, _lastHeadLocalPosition) > RecentreHeadSpeed * deltaTime
                || Quaternion.Angle(rotation, _lastHeadLocalRotation) > RecentreHeadTurnSpeed * deltaTime;

            _lastHeadLocalPosition = position;
            _lastHeadLocalRotation = rotation;

            if (hasJumped) {
                Calibrate();
            }
        }

        /// <summary>
        /// Puts the view right for wherever the tracking origin currently
        /// is, by moving Camera Offset (the tracking space: the parent of
        /// the head and, through Hands, both controllers) and never the
        /// tracked transforms themselves:
        /// 1. Upright - if the head is upside down, the tracking space is,
        ///    so turn it half a turn (FlipTrackingSpace()).
        /// 2. Height - lift or lower it so the head is at standing eye
        ///    height right now, less however far the game's crouch has
        ///    lowered the view. This is what Device tracking mode promises
        ///    (the head starts at the origin, Camera Offset's 1.6m up), done
        ///    by hand because the runtime doesn't always deliver it: the
        ///    upside-down space also had its origin on the floor.
        /// Works from any state, so it's safe to run again after a recentre.
        /// </summary>
        private void Calibrate()
        {
            if (IsHeadUpsideDown()) {
                FlipTrackingSpace();
            }

            // The crouch lowers Camera Offset by exactly as much as it
            // shortens the capsule (PlayerLocomotion.HandleCrouch()), so the
            // capsule says how far down the view should currently be.
            float crouchDrop = _standingControllerHeight - _characterController.height;
            float targetHeadY = transform.position.y + _standingEyeHeight - crouchDrop;

            Transform trackingSpace = head.parent;
            trackingSpace.position += Vector3.up * (targetHeadY - head.position.y);
        }

        /// <summary>
        /// Whether the head is more than a quarter turn from upright: its
        /// own up pointing below the horizon. A real head can't get there
        /// at level start or across a recentre, so it means the tracking
        /// space is flipped.
        /// </summary>
        private bool IsHeadUpsideDown()
        {
            return Vector3.Dot(head.up, Vector3.up) < 0f;
        }

        /// <summary>
        /// Turns the whole tracking space (Camera Offset - the parent of the
        /// head and, through Hands, both controllers) half a turn, so an
        /// upside-down view comes out upright. The turn is about the way the
        /// head is facing (flattened to level) and through the head's own
        /// position: the player keeps looking the same way from the same
        /// spot, and only up and down swap. Everything tracked is under
        /// Camera Offset, so the hands are corrected with the head.
        ///
        /// Logs the rotations it found first - which transform was flipped
        /// says where the problem comes from (the tracking data if the
        /// head's local rotation is upside down, our own rig if Camera
        /// Offset or the Player root is).
        /// </summary>
        private void FlipTrackingSpace()
        {
            Transform trackingSpace = head.parent;

            Debug.LogWarning(
                "PlayerTracking: the view was upside down - turned the tracking space half a turn to put it upright. " +
                $"Head local rotation {head.localEulerAngles}, {trackingSpace.name} local rotation {trackingSpace.localEulerAngles}, " +
                $"{name} rotation {transform.eulerAngles}.",
                this);

            // The level direction the head faces. Looking straight up or
            // down it has none, so fall back to the top of the head, then
            // the rig's own forward.
            Vector3 facing = Vector3.ProjectOnPlane(head.forward, Vector3.up);

            if (facing.sqrMagnitude < 0.0001f) {
                facing = Vector3.ProjectOnPlane(head.up, Vector3.up);
            }

            if (facing.sqrMagnitude < 0.0001f) {
                facing = transform.forward;
            }

            trackingSpace.RotateAround(head.position, facing.normalized, 180f);
        }

        // TRANSFORM ACCESS
        //
        // Sometimes another system needs the entire Transform object.
        // For example:
        //
        // sword.transform.position = tracking.RightHand.position;
        //
        // So we expose the transforms directly.

            public Transform Head => head;
            public Transform LeftHand => leftHand;
            public Transform RightHand => rightHand;
        
        // POSITION ACCESS
        // Most gameplay code only needs positions.
        
        // Example:
        //
        // Distance between hand and loot item.
        //
        // float distance =
        //     Vector3.Distance(
        //         tracking.RightHandPosition,
        //         loot.transform.position);

            public Vector3 HeadPosition => head.position;
            public Vector3 LeftHandPosition => leftHand.position;
            public Vector3 RightHandPosition => rightHand.position;
        
        /// <summary>
        /// Position of the Player root in world space.
        ///
        /// Useful for:
        /// - AI perception
        /// - Footstep sounds
        /// - Save/load systems
        /// - Trigger volumes
        /// </summary>
            public Vector3 PlayerPosition => transform.position;

        // ROTATION ACCESS
        //
        // Useful for:
        //
        // - Bow aiming
        // - Flashlight direction
        // - Throwing calculations
        // - Door handle orientation

            public Quaternion HeadRotation => head.rotation;
            public Quaternion LeftHandRotation => leftHand.rotation;
            public Quaternion RightHandRotation => rightHand.rotation;
        
        /// <summary>
        /// Rotation of the Player root in world space.
        /// </summary>
        public Quaternion PlayerRotation => transform.rotation;
        
    }
}