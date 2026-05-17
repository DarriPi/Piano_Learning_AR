using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace PianoLearningCore
{
    /// <summary>
    /// Lets you manually align a virtual keyboard with a physical one.
    /// Attach this to the keyboard root GameObject (the same one that has KeyboardLayout).
    ///
    /// Keyboard controls (works with both old and new Unity Input Systems):
    ///   Arrow keys / WASD : move the keyboard along X / Z
    ///   Q / E             : move down / up (Y)
    ///   Z / C             : rotate around Y (yaw)
    ///   [ / ]             : scale down / up uniformly (only X scale for width fitting)
    ///   R                 : reset transform
    ///
    /// All movements are tiny (millimeters per frame while a key is held) so the user
    /// can do precise alignment. Hold LeftShift to move 10x faster.
    /// </summary>
    [DisallowMultipleComponent]
    public class KeyboardCalibrator : MonoBehaviour
    {
        [Header("Tuning")]
        [Tooltip("Meters per second when nudging position.")]
        public float positionSpeed = 0.05f;

        [Tooltip("Degrees per second when rotating.")]
        public float rotationSpeed = 15f;

        [Tooltip("Scale change per second (multiplicative).")]
        public float scaleSpeed = 0.2f;

        [Tooltip("Hold-Shift multiplier for fast adjustment.")]
        public float fastMultiplier = 10f;

        [Header("Behaviour")]
        [Tooltip("If true, the very first frame the calibrator remembers its current pose " +
                 "so the R key can restore it.")]
        public bool rememberInitialPose = true;

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
            float speedMul = IsHeld(InputKey.Shift) ? fastMultiplier : 1f;
            float dt = Time.deltaTime;

            // ---- Position ----
            Vector3 move = Vector3.zero;
            if (IsHeld(InputKey.Left)  || IsHeld(InputKey.A)) move.x -= 1f;
            if (IsHeld(InputKey.Right) || IsHeld(InputKey.D)) move.x += 1f;
            if (IsHeld(InputKey.Down)  || IsHeld(InputKey.S)) move.z -= 1f;
            if (IsHeld(InputKey.Up)    || IsHeld(InputKey.W)) move.z += 1f;
            if (IsHeld(InputKey.Q)) move.y -= 1f;
            if (IsHeld(InputKey.E)) move.y += 1f;
            if (move.sqrMagnitude > 0f)
            {
                transform.position += move.normalized * positionSpeed * speedMul * dt;
            }

            // ---- Rotation around Y ----
            float yaw = 0f;
            if (IsHeld(InputKey.Z)) yaw -= 1f;
            if (IsHeld(InputKey.C)) yaw += 1f;
            if (yaw != 0f)
            {
                transform.Rotate(0f, yaw * rotationSpeed * speedMul * dt, 0f, Space.World);
            }

            // ---- Uniform-ish scale (only X for width fitting) ----
            float scaleDelta = 0f;
            if (IsHeld(InputKey.LeftBracket))  scaleDelta -= 1f;
            if (IsHeld(InputKey.RightBracket)) scaleDelta += 1f;
            if (scaleDelta != 0f)
            {
                float factor = 1f + scaleDelta * scaleSpeed * speedMul * dt;
                Vector3 s = transform.localScale;
                s.x = Mathf.Max(0.05f, s.x * factor);
                transform.localScale = s;
            }

            // ---- Reset ----
            if (WasPressedThisFrame(InputKey.R) && rememberInitialPose)
            {
                transform.position = _initialPos;
                transform.rotation = _initialRot;
                transform.localScale = _initialScale;
            }
        }

        // ------------------------------------------------------------
        // Input abstraction so this works with BOTH input systems.
        // ------------------------------------------------------------

        private enum InputKey
        {
            Left, Right, Up, Down,
            W, A, S, D, Q, E, Z, C, R,
            Shift, LeftBracket, RightBracket
        }

        private static bool IsHeld(InputKey k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return MapNew(kb, k)?.isPressed ?? false;
#else
            return Input.GetKey(MapLegacy(k));
#endif
        }

        private static bool WasPressedThisFrame(InputKey k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return MapNew(kb, k)?.wasPressedThisFrame ?? false;
#else
            return Input.GetKeyDown(MapLegacy(k));
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static KeyControl MapNew(Keyboard kb, InputKey k)
        {
            switch (k)
            {
                case InputKey.Left:  return kb.leftArrowKey;
                case InputKey.Right: return kb.rightArrowKey;
                case InputKey.Up:    return kb.upArrowKey;
                case InputKey.Down:  return kb.downArrowKey;
                case InputKey.W: return kb.wKey;
                case InputKey.A: return kb.aKey;
                case InputKey.S: return kb.sKey;
                case InputKey.D: return kb.dKey;
                case InputKey.Q: return kb.qKey;
                case InputKey.E: return kb.eKey;
                case InputKey.Z: return kb.zKey;
                case InputKey.C: return kb.cKey;
                case InputKey.R: return kb.rKey;
                case InputKey.Shift: return kb.leftShiftKey;
                case InputKey.LeftBracket:  return kb.leftBracketKey;
                case InputKey.RightBracket: return kb.rightBracketKey;
            }
            return null;
        }
#else
        private static KeyCode MapLegacy(InputKey k)
        {
            switch (k)
            {
                case InputKey.Left:  return KeyCode.LeftArrow;
                case InputKey.Right: return KeyCode.RightArrow;
                case InputKey.Up:    return KeyCode.UpArrow;
                case InputKey.Down:  return KeyCode.DownArrow;
                case InputKey.W: return KeyCode.W;
                case InputKey.A: return KeyCode.A;
                case InputKey.S: return KeyCode.S;
                case InputKey.D: return KeyCode.D;
                case InputKey.Q: return KeyCode.Q;
                case InputKey.E: return KeyCode.E;
                case InputKey.Z: return KeyCode.Z;
                case InputKey.C: return KeyCode.C;
                case InputKey.R: return KeyCode.R;
                case InputKey.Shift: return KeyCode.LeftShift;
                case InputKey.LeftBracket:  return KeyCode.LeftBracket;
                case InputKey.RightBracket: return KeyCode.RightBracket;
            }
            return KeyCode.None;
        }
#endif
    }
}
