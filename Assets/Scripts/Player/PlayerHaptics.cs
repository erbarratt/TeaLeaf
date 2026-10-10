using UnityEngine;
using UnityEngine.XR;

namespace Player
{
    /// <summary>
    /// The single way to buzz a controller. Other systems (physical hands
    /// now; landing, the blackjack and lockpicking later) call Pulse() or
    /// raise an event this class listens to, and never talk to the haptics
    /// hardware themselves, so strengths and durations are tuned in one
    /// place and the output route can change without touching them.
    ///
    /// Haptics go out through Unity's XR device for each hand (the left
    /// and right controllers, as the XR runtime reports them), which is
    /// found once and kept: sending a pulse then creates nothing, so it's
    /// safe to call many times a second.
    ///
    /// No Update() and no Tick(): it only does anything when asked.
    /// </summary>
    public class PlayerHaptics : MonoBehaviour
    {
        // Global vibration switch, a player setting like PlayerLocomotion's
        // useSmoothTurn: off, Pulse() does nothing, so every haptic in the
        // game stops. A future settings dialogue sets it through
        // HapticsEnabled.
        [SerializeField] private bool hapticsEnabled = true;

        // Raises HandContactStarted when a physical hand first touches a
        // surface. Filled in by Reset().
        [SerializeField] private PlayerHandVisuals playerHandVisuals;

        [Header("Hand Contact")]
        // Strength (0-1) and length (seconds) of the pulse when a hand first
        // touches something - a light tap, not a rumble.
        [SerializeField] private float contactAmplitude = 0.15f;
        [SerializeField] private float contactDuration = 0.03f;

        // Seconds after a contact pulse before the same hand can pulse
        // again, so a hand sliding over a bumpy surface (brief losses of
        // contact) taps once instead of buzzing continuously.
        [SerializeField] private float contactCooldown = 0.25f;

        [Header("Test Pulse")]
        // Strength (0-1) and length (seconds) of the right-click "Test ...
        // Pulse" menu items, for checking the bindings in Play Mode.
        [SerializeField] private float testAmplitude = 0.5f;
        [SerializeField] private float testDuration = 0.1f;

        // The two controllers as XR devices. Looked up the first time a
        // pulse is sent and again whenever one stops being valid (a
        // controller switched off and on comes back as a new device).
        private InputDevice _leftDevice;
        private InputDevice _rightDevice;

        // Time.time before which each hand's contact pulse is still cooling
        // down - see contactCooldown.
        private float _leftContactReadyTime;
        private float _rightContactReadyTime;

        /// Whether controllers vibrate at all - the global on/off for a
        /// settings dialogue. Backed by the serialized hapticsEnabled, so the
        /// Inspector and code change the same value.
        public bool HapticsEnabled
        {
            get => hapticsEnabled;
            set => hapticsEnabled = value;
        }

        /// <summary>
        /// Editor-only: runs when the component is first added. Finds
        /// PlayerHandVisuals on the Hands object under this one.
        /// </summary>
        private void Reset()
        {
            playerHandVisuals = GetComponentInChildren<PlayerHandVisuals>();
        }

        private void OnEnable()
        {
            playerHandVisuals.HandContactStarted += OnHandContactStarted;
        }

        private void OnDisable()
        {
            playerHandVisuals.HandContactStarted -= OnHandContactStarted;
        }

        /// <summary>
        /// Buzzes one controller. amplitude is 0-1, duration in seconds.
        /// Does nothing if that controller isn't connected, or if haptics
        /// are switched off (HapticsEnabled) - every vibration goes through
        /// here, so that one check covers them all.
        /// </summary>
        public void Pulse(bool isLeftHand, float amplitude, float duration)
        {
            if (!hapticsEnabled) {
                return;
            }

            if (isLeftHand) {
                Send(ref _leftDevice, XRNode.LeftHand, amplitude, duration);
            } else {
                Send(ref _rightDevice, XRNode.RightHand, amplitude, duration);
            }
        }

        /// <summary>
        /// Sends the pulse to a hand's device, finding the device first if
        /// the one kept isn't valid (never found yet, or disconnected).
        /// The device is a struct passed by reference, so the one found is
        /// kept for next time.
        /// </summary>
        private static void Send(ref InputDevice device, XRNode hand, float amplitude, float duration)
        {
            if (!device.isValid) {
                device = InputDevices.GetDeviceAtXRNode(hand);

                if (!device.isValid) {
                    return;
                }
            }

            // Channel 0: controllers have the one motor.
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
        }

        /// <summary>
        /// A physical hand has just started touching a surface: a light tap
        /// on that controller, unless it tapped less than contactCooldown
        /// ago.
        /// </summary>
        private void OnHandContactStarted(bool isLeftHand)
        {
            float readyTime = isLeftHand ? _leftContactReadyTime : _rightContactReadyTime;

            if (Time.time < readyTime) {
                return;
            }

            if (isLeftHand) {
                _leftContactReadyTime = Time.time + contactCooldown;
            } else {
                _rightContactReadyTime = Time.time + contactCooldown;
            }

            Pulse(isLeftHand, contactAmplitude, contactDuration);
        }

        /// <summary>
        /// Right-click menu, Play Mode only: buzzes the left controller with
        /// the test amplitude and duration.
        /// </summary>
        [ContextMenu("Test Left Pulse")]
        private void TestLeftPulse()
        {
            Pulse(true, testAmplitude, testDuration);
        }

        /// <summary>
        /// Right-click menu, Play Mode only: buzzes the right controller.
        /// </summary>
        [ContextMenu("Test Right Pulse")]
        private void TestRightPulse()
        {
            Pulse(false, testAmplitude, testDuration);
        }
    }
}
