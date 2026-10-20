using Core;
using Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Management;

/// <summary>
/// Development switch: walk the level in Play Mode from the desk, with the
/// mouse and keyboard, without putting the headset on. Movement only -
/// nothing is grabbed, climbed or thrown.
///
/// Lives on the Player root and does nothing unless the Level Manager's
/// "PC Play" box is ticked (LevelManager.IsPcPlay): it disables itself on
/// the first frame otherwise. Ticked, at start it:
/// - stops XR (the loader and its subsystems), so the camera renders to
///   the Game view as an ordinary camera instead of to the headset;
/// - turns off the three Tracked Pose Drivers (head and both hands), so
///   nothing writes tracked poses over the ones placed here;
/// - disables PlayerTracking before its Start() runs, so it neither sets
///   a tracking mode nor holds the level-start fade waiting for a headset.
///   PlayerController still calls its Tick() each frame; with nothing
///   calibrated that only smooths the hands' poses, which is harmless;
/// - locks the cursor to the Game view (Escape frees it, a click locks it
///   again).
///
/// Each frame, before PlayerController's tick (DefaultExecutionOrder): the
/// mouse's sideways movement turns the Player root - the body - so walking
/// forward follows the look, as the locomotion already moves relative to
/// the root; its vertical movement pitches the camera, which sits at the
/// origin of Camera Offset (so it is at standing eye height and the capsule
/// is centred under it); and the two controller objects are put just under
/// and in front of the camera, facing where it looks, so the hand visuals
/// are in view and nothing that reads them is left with a stale pose.
///
/// The keyboard needs no code of its own: the Player map's Move (WASD),
/// Sprint (Left Shift), Jump (Space) and Crouch (C) actions already carry
/// keyboard bindings, read by PlayerInputXR as usual.
/// </summary>
[DefaultExecutionOrder(-100)]
public class PcPlay : MonoBehaviour
{
    // The tracking, for the head and controller transforms it owns, and
    // to disable it. Found on this object in Reset().
    [SerializeField] private PlayerTracking playerTracking;

    [Header("Mouse Look")]

    // Degrees turned per pixel the mouse moves. The Input System's mouse
    // delta is in pixels for the frame, not per second, so no deltaTime.
    [SerializeField] private float mouseSensitivity = 0.1f;

    // How far the view can tilt up or down, in degrees.
    [SerializeField, Range(0f, 89f)] private float maxPitch = 85f;

    [Header("Hands")]

    // Where the right controller is placed, relative to the camera (x
    // right, y up, z forward, in metres); the left mirrors x.
    [SerializeField] private Vector3 handOffset = new(0.2f, -0.25f, 0.35f);

    // The camera's current tilt, degrees above (negative) or below the
    // horizon - kept here, since the camera's rotation is rewritten from it
    // every frame.
    private float _pitch;

    /// <summary>
    /// Editor-only: fills in the tracking reference when the component is
    /// added.
    /// </summary>
    private void Reset()
    {
        playerTracking = GetComponent<PlayerTracking>();
    }

    /// <summary>
    /// Reads the switch and sets PC play up, or disables itself. Start()
    /// rather than Awake(): LevelManager sets Instance in its Awake(), and
    /// every Awake() runs before the first Start(). The execution order
    /// puts this Start() before PlayerTracking's.
    /// </summary>
    private void Start()
    {
        LevelManager levelManager = LevelManager.Instance;

        if (levelManager == null || !levelManager.IsPcPlay) {
            enabled = false;
            return;
        }

        StopXR();

        Transform head = playerTracking.Head;
        Transform leftHand = playerTracking.LeftHand;
        Transform rightHand = playerTracking.RightHand;

        DisableTrackedPoseDriver(head);
        DisableTrackedPoseDriver(leftHand);
        DisableTrackedPoseDriver(rightHand);

        playerTracking.enabled = false;

        // The camera at the tracking origin, looking level - standing eye
        // height, since Camera Offset is saved that far up.
        head.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        PlaceHands(head, leftHand, rightHand);

        Cursor.lockState = CursorLockMode.Locked;

        Debug.Log("PcPlay: on - WASD moves, the mouse looks, Space jumps, C crouches, Left Shift toggles sprint. Escape frees the cursor.", this);
    }

    /// <summary>
    /// Shuts XR down if it was started, so the camera draws to the Game
    /// view. Nothing to do when no headset runtime came up (no loader).
    /// </summary>
    private static void StopXR()
    {
        XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;

        if (manager == null || manager.activeLoader == null) {
            return;
        }

        manager.StopSubsystems();
        manager.DeinitializeLoader();
    }

    /// <summary>
    /// Turns off the Tracked Pose Driver on a tracked object, if it has one.
    /// </summary>
    private static void DisableTrackedPoseDriver(Transform tracked)
    {
        TrackedPoseDriver driver = tracked.GetComponent<TrackedPoseDriver>();

        if (driver != null) {
            driver.enabled = false;
        }
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null) {
            return;
        }

        // The cursor comes free when Escape is pressed in the editor; a
        // click in the Game view takes it back.
        if (Cursor.lockState != CursorLockMode.Locked) {
            if (mouse.leftButton.wasPressedThisFrame) {
                Cursor.lockState = CursorLockMode.Locked;
            }

            return;
        }

        Vector2 delta = mouse.delta.ReadValue();

        // Sideways turns the body, as the thumbstick does, so forward
        // follows the look.
        transform.Rotate(Vector3.up, delta.x * mouseSensitivity, Space.World);

        // Up and down tilts the camera only. Mouse up is a positive y and
        // should look up, which is a negative pitch about x.
        _pitch = Mathf.Clamp(_pitch - delta.y * mouseSensitivity, -maxPitch, maxPitch);

        Transform head = playerTracking.Head;
        head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

        PlaceHands(head, playerTracking.LeftHand, playerTracking.RightHand);
    }

    /// <summary>
    /// Puts both controller objects just under and in front of the camera,
    /// facing the way it looks. The controllers are under Hands, which has
    /// an identity transform under Camera Offset, and the camera is under
    /// Camera Offset directly - so the camera's local pose is in the same
    /// space as theirs.
    /// </summary>
    private void PlaceHands(Transform head, Transform leftHand, Transform rightHand)
    {
        head.GetLocalPositionAndRotation(out Vector3 headPosition, out Quaternion headRotation);

        Vector3 leftOffset = new(-handOffset.x, handOffset.y, handOffset.z);

        leftHand.SetLocalPositionAndRotation(headPosition + headRotation * leftOffset, headRotation);
        rightHand.SetLocalPositionAndRotation(headPosition + headRotation * handOffset, headRotation);
    }
}
