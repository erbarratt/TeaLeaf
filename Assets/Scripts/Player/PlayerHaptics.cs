using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR.Input;

namespace Player
{
    /// <summary>
    /// The single way to buzz a controller. Other systems (physical hands
    /// now; landing, the blackjack and lockpicking later) call Pulse() or
    /// raise an event this class listens to, and never talk to the haptics
    /// hardware themselves, so strengths and durations are tuned in one
    /// place and the output route can change without touching them.
    ///
    /// Haptics go out through the OpenXR runtime, via a PassThrough action
    /// bound to each controller's "haptic" output (Player/LeftHaptic and
    /// Player/RightHaptic in the project's InputSystem_Actions). The same
    /// binding works on every OpenXR controller profile, so on PCVR and
    /// Quest standalone alike.
    ///
    /// No Update() and no Tick(): it only does anything when asked.
    /// </summary>
    public class PlayerHaptics : MonoBehaviour
    {
        // Player/LeftHaptic and Player/RightHaptic - output-only actions
        // bound to <XRController>{LeftHand}/haptic and {RightHand}/haptic.
        [SerializeField] private InputActionReference leftHapticAction;
        [SerializeField] private InputActionReference rightHapticAction;

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

        // Resolved once in OnEnable(), like PlayerInputXR: .action does a
        // lookup on every access.
        private InputAction _leftHaptic;
        private InputAction _rightHaptic;

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
            _leftHaptic = leftHapticAction.action;
            _rightHaptic = rightHapticAction.action;

            // An action only has bound controls - which is how OpenXR finds
            // the controller to buzz - while it's enabled.
            _leftHaptic.Enable();
            _rightHaptic.Enable();

            playerHandVisuals.HandContactStarted += OnHandContactStarted;
        }

        private void OnDisable()
        {
            playerHandVisuals.HandContactStarted -= OnHandContactStarted;
        }

        /// <summary>
        /// Buzzes one controller. amplitude is 0-1 (clamped by OpenXR),
        /// duration in seconds. Does nothing if that controller isn't
        /// connected, or if haptics are switched off (HapticsEnabled) -
        /// every vibration goes through here, so that one check covers them
        /// all. Meant for events (a contact, a hit), not every frame:
        /// OpenXR looks the controller up by name on each call, which
        /// allocates a little.
        /// </summary>
        public void Pulse(bool isLeftHand, float amplitude, float duration)
        {
            if (!hapticsEnabled) {
                return;
            }

            OpenXRInput.SendHapticImpulse(isLeftHand ? _leftHaptic : _rightHaptic, amplitude, duration);
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
