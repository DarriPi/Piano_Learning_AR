using UnityEngine;
using UnityEngine.Events;

namespace PianoLearningCore
{
    /// <summary>
    /// Quest-controller-based replacement for KeyboardCalibrator.
    /// Lets the user nudge the virtual keyboard into alignment with a physical one
    /// using the touch controllers' thumbsticks and buttons.
    ///
    /// Default mapping:
    ///   Right thumbstick (X / Y)  : move keyboard along X and Z (horizontal plane)
    ///   Left  thumbstick X        : rotate keyboard around Y (yaw)
    ///   Left  thumbstick Y        : move keyboard up / down
    ///   Right index trigger held  : 10x speed multiplier (fast mode)
    ///   B button (right)          : reset to initial pose
    ///   A button (right)          : fire OnCalibrationConfirmed event
    ///   X / Y buttons (left)      : scale keyboard width down / up
    ///
    /// Attach to the same GameObject as your KeyboardLayout (the keyboard root).
    ///
    /// REQUIRES the Meta XR SDK (already present in this project - uses OVRInput).
    /// If you ever uninstall the Meta XR SDK, delete this file.
    /// </summary>
    [DisallowMultipleComponent]
    public class XRControllerCalibrator : MonoBehaviour
    {
        [Header("Speeds")]
        [Tooltip("Meters per second at full thumbstick deflection.")]
        public float positionSpeed = 0.10f;

        [Tooltip("Degrees per second at full thumbstick deflection.")]
        public float rotationSpeed = 25f;

        [Tooltip("Scale change per second (multiplicative) when X / Y buttons held.")]
        public float scaleSpeed = 0.15f;

        [Tooltip("Multiplier applied while the right index trigger is held.")]
        public float fastMultiplier = 10f;

        [Header("Deadzone (avoids drift on idle thumbsticks)")]
        [Range(0f, 0.5f)]
        public float thumbstickDeadzone = 0.15f;

        [Header("Behaviour")]
        [Tooltip("If false, this component is inert - useful when calibration mode is OFF and the user is playing.")]
        public bool isActive = true;

        [Tooltip("Remember the keyboard's pose in Awake so the reset button (B) can restore it.")]
        public bool rememberInitialPose = true;

        [Tooltip("Clamp world Y so the keyboard can never go below the floor.")]
        public bool clampToFloor = true;

        [Header("Events (optional)")]
        public UnityEvent OnCalibrationConfirmed;
        public UnityEvent OnCalibrationReset;

        private Vector3 _initialPos;
        private Quaternion _initialRot;
        private Vector3 _initialScale;

        private void Awake()
        {
            if (rememberInitialPose)
            {
                _initialPos = transform.position;
                _initialRot = transform.rotation;
                _initialScale = transform.localScale;
            }
        }

        private void Update()
        {
            if (!isActive) return;
            if (!Application.isPlaying) return;

            float dt = Time.deltaTime;
            float speedMul = GetTriggerHeld(true) ? fastMultiplier : 1f;

            Vector2 rightStick = ApplyDeadzone(GetThumbstick(false), thumbstickDeadzone);
            Vector2 leftStick  = ApplyDeadzone(GetThumbstick(true),  thumbstickDeadzone);

            if (rightStick.sqrMagnitude > 0f)
            {
                Vector3 move = new Vector3(rightStick.x, 0f, rightStick.y);
                transform.position += move * positionSpeed * speedMul * dt;
            }

            if (Mathf.Abs(leftStick.x) > 0f)
            {
                transform.Rotate(0f, leftStick.x * rotationSpeed * speedMul * dt, 0f, Space.World);
            }

            if (Mathf.Abs(leftStick.y) > 0f)
            {
                Vector3 p = transform.position;
                p.y += leftStick.y * positionSpeed * speedMul * dt;
                transform.position = p;
            }

            if (clampToFloor && transform.position.y < 0f)
            {
                Vector3 p = transform.position;
                p.y = 0f;
                transform.position = p;
            }

            float scaleInput = 0f;
            if (GetButtonHeld(LeftX)) scaleInput -= 1f;
            if (GetButtonHeld(LeftY)) scaleInput += 1f;
            if (scaleInput != 0f)
            {
                float factor = 1f + scaleInput * scaleSpeed * speedMul * dt;
                Vector3 s = transform.localScale;
                s.x = Mathf.Max(0.05f, s.x * factor);
                transform.localScale = s;
            }

            if (GetButtonDown(RightB) && rememberInitialPose)
            {
                transform.position = _initialPos;
                transform.rotation = _initialRot;
                transform.localScale = _initialScale;
                OnCalibrationReset?.Invoke();
            }

            if (GetButtonDown(RightA))
            {
                OnCalibrationConfirmed?.Invoke();
            }
        }

        public void SetActive(bool active) => isActive = active;

        public void CaptureInitialPose()
        {
            _initialPos = transform.position;
            _initialRot = transform.rotation;
            _initialScale = transform.localScale;
        }

        private static Vector2 ApplyDeadzone(Vector2 v, float dz)
        {
            if (v.magnitude < 0.0001f) return Vector2.zero;
            float mag = v.magnitude;
            if (mag <= dz) return Vector2.zero;
            float scaled = (mag - dz) / (1f - dz);
            return v.normalized * Mathf.Clamp01(scaled);
        }

        private static Vector2 GetThumbstick(bool left)
        {
            return left
                ? OVRInput.Get(OVRInput.RawAxis2D.LThumbstick)
                : OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
        }

        private static bool GetTriggerHeld(bool right)
        {
            float v = right
                ? OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger)
                : OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger);
            return v > 0.5f;
        }

        private struct ButtonRef { public OVRInput.Button button; public OVRInput.Controller controller; }
        private static ButtonRef RightA => new ButtonRef { button = OVRInput.Button.One, controller = OVRInput.Controller.RTouch };
        private static ButtonRef RightB => new ButtonRef { button = OVRInput.Button.Two, controller = OVRInput.Controller.RTouch };
        private static ButtonRef LeftX  => new ButtonRef { button = OVRInput.Button.One, controller = OVRInput.Controller.LTouch };
        private static ButtonRef LeftY  => new ButtonRef { button = OVRInput.Button.Two, controller = OVRInput.Controller.LTouch };

        private static bool GetButtonHeld(ButtonRef b) => OVRInput.Get(b.button, b.controller);
        private static bool GetButtonDown(ButtonRef b) => OVRInput.GetDown(b.button, b.controller);
    }
}
