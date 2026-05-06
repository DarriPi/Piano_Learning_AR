using UnityEngine;
using System.IO;

/// <summary>
/// Handles AR keyboard overlay alignment via VR controller input.
///
/// CONTROL SCHEME
/// ─────────────────────────────────────────────────────────────────
/// DEFAULT MODE (release X button)
///   Left  stick XY  → translate overlay forward/back + strafe (camera-relative)
///   Right stick Y   → translate overlay up / down
///   Right stick X   → rotate overlay around Y axis
///
/// STRETCH MODE (hold X button on left controller)
///   Left  stick X   → stretch keyboard width  (local X scale)
///   Left  stick Y   → stretch keyboard depth  (local Z scale)
///   Right stick Y   → adjust thickness        (local Y scale, clamped 0.2–0.5)
///   Right stick X   → (unused in this mode)
///
/// BOTH MODES
///   A button → confirm and save calibration
///   B button → cancel without saving
/// ─────────────────────────────────────────────────────────────────
/// </summary>
public class CalibrationManager : MonoBehaviour
{
    public static CalibrationManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private KeyboardMapper mapper;          // The KeyboardMapper's transform
    [SerializeField] private GameObject overlayKeyboard;    // Semi-transparent overlay

    [Header("Movement Sensitivity")]
    [SerializeField] private float translateSpeed = 1.0f;   // metres/sec  (was 0.5 — doubled for responsiveness)
    [SerializeField] private float rotateSpeed    = 45f;    // degrees/sec (was 30 — slightly faster)

    [Header("Scale / Stretch Sensitivity")]
    [SerializeField] private float stretchSpeed = 0.4f;     // local-scale units/sec

    [Header("Thickness Limits (Y scale)")]
    [SerializeField] private float minThickness = 0.2f;     // never thinner than this
    [SerializeField] private float maxThickness = 0.5f;     // never thicker than this

    [Header("Stretch Limits")]
    [SerializeField] private float minWidthScale = 0.3f;    // X scale lower bound
    [SerializeField] private float minDepthScale = 0.05f;   // Z scale lower bound

    // ── State ────────────────────────────────────────────────────────────────
    private bool isCalibrating = false;
    private string SavePath => Path.Combine(Application.persistentDataPath, "calibration.json");

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        LoadCalibration();
    }

    void Update()
    {
        if (!isCalibrating || overlayKeyboard == null) return;
        HandleControllerInput();
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public void StartCalibration()
    {
        isCalibrating = true;
        overlayKeyboard?.SetActive(true);
        Debug.Log("[Calibration] Started.\n" +
                  "  Default : Left stick = move XZ | Right stick = move Y + rotate\n" +
                  "  Hold X  : Left stick = stretch W/D | Right stick Y = thickness");
    }

    public void ConfirmCalibration()
    {
        isCalibrating = false;

        mapper.transform.position   = overlayKeyboard.transform.position;
        mapper.transform.rotation   = overlayKeyboard.transform.rotation;
        mapper.transform.localScale = overlayKeyboard.transform.localScale;
        mapper.RebuildLanePositions();

        SaveCalibration();
        overlayKeyboard?.SetActive(false);
        Debug.Log("[Calibration] Saved.");
    }

    // ── Input handling ────────────────────────────────────────────────────────
    private void HandleControllerInput()
    {
        Transform t = overlayKeyboard.transform;

        Vector2 leftStick  = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);

        // X button (left controller, Button.Three) toggles stretch mode while held
        bool stretchMode = OVRInput.Get(OVRInput.Button.Three);

        if (stretchMode)
        {
            HandleStretchInput(t, leftStick, rightStick);
        }
        else
        {
            HandleMoveInput(t, leftStick, rightStick);
        }

        // ── Confirm / Cancel ─────────────────────────────────────────────────
        if (OVRInput.GetDown(OVRInput.Button.One))   // A button
            ConfirmCalibration();

        if (OVRInput.GetDown(OVRInput.Button.Two))   // B button
        {
            isCalibrating = false;
            overlayKeyboard.SetActive(false);
            Debug.Log("[Calibration] Cancelled.");
        }
    }

    /// <summary>
    /// Default mode: translate and rotate the overlay.
    /// Translation is camera-relative so the overlay always moves in the
    /// direction the player is facing, regardless of its own orientation.
    /// </summary>
    private void HandleMoveInput(Transform t, Vector2 leftStick, Vector2 rightStick)
    {
        // ── Camera-relative XZ translation (left stick) ──────────────────────
        //    Project camera forward/right onto the horizontal plane so vertical
        //    head tilt doesn't make the keyboard fly up or dive down.
        Transform cam = Camera.main != null ? Camera.main.transform : transform;

        Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        Vector3 camRight   = Vector3.ProjectOnPlane(cam.right,   Vector3.up).normalized;

        Vector3 xzMove = (camRight * leftStick.x + camForward * leftStick.y)
                         * translateSpeed * Time.deltaTime;
        t.position += xzMove;

        // ── Vertical translation (right stick Y) ─────────────────────────────
        t.position += Vector3.up * rightStick.y * translateSpeed * Time.deltaTime;

        // ── Y-axis rotation (right stick X) ──────────────────────────────────
        t.Rotate(0f, rightStick.x * rotateSpeed * Time.deltaTime, 0f, Space.World);
    }

    /// <summary>
    /// Stretch mode (hold X): independently scale width, depth, and thickness.
    /// Y scale (thickness) is clamped to [minThickness, maxThickness].
    /// </summary>
    private void HandleStretchInput(Transform t, Vector2 leftStick, Vector2 rightStick)
    {
        Vector3 s = t.localScale;

        // ── Width stretch: left stick X → local X scale ───────────────────────
        s.x = Mathf.Max(minWidthScale, s.x + leftStick.x * stretchSpeed * Time.deltaTime);

        // ── Depth stretch: left stick Y → local Z scale ───────────────────────
        s.z = Mathf.Max(minDepthScale, s.z + leftStick.y * stretchSpeed * Time.deltaTime);

        // ── Thickness: right stick Y → local Y scale (clamped) ───────────────
        s.y = Mathf.Clamp(
            s.y + rightStick.y * stretchSpeed * Time.deltaTime,
            minThickness,
            maxThickness
        );

        t.localScale = s;

        // Debug HUD so the user can see live values in the Editor console
        Debug.Log($"[Calibration] Scale  W:{s.x:F3}  H:{s.y:F3}  D:{s.z:F3}");
    }

    // ── Persistence ───────────────────────────────────────────────────────────
    private void SaveCalibration()
    {
        Transform t = mapper.transform;
        var data = new CalibrationData
        {
            posX   = t.position.x,    posY   = t.position.y,    posZ   = t.position.z,
            rotX   = t.eulerAngles.x,  rotY   = t.eulerAngles.y,  rotZ   = t.eulerAngles.z,
            scaleX = t.localScale.x,   scaleY = t.localScale.y,   scaleZ = t.localScale.z
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        Debug.Log($"[Calibration] Saved to {SavePath}");
    }

    private void LoadCalibration()
    {
        if (!File.Exists(SavePath)) return;
        try
        {
            var data = JsonUtility.FromJson<CalibrationData>(File.ReadAllText(SavePath));
            mapper.transform.SetPositionAndRotation(data.Position, data.Rotation);
            mapper.transform.localScale = data.Scale;
            mapper.RebuildLanePositions();
            Debug.Log("[Calibration] Loaded.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Calibration] Load failed: {e.Message}");
        }
    }
}