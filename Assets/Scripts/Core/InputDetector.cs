using System;
using UnityEngine;

public class InputDetector : MonoBehaviour
{
    public static InputDetector Instance { get; private set; }

    [Header("References")]
    [SerializeField] private TestModeKeyboard testKeyboard; // Test Keybaord

    // Subscribe to receive (midiNote, isDown) events
    public event Action<int, bool> OnNoteInput;

    // PC keyboard mapping: maps KeyCode → MIDI note for quick testing
    // Maps A-Z + number row to 26 piano notes starting at C4 (MIDI 60)
    private readonly (KeyCode key, int midi)[] PCKeyMap = {
        (KeyCode.A, 60), (KeyCode.S, 62), (KeyCode.D, 64), (KeyCode.F, 65),
        (KeyCode.G, 67), (KeyCode.H, 69), (KeyCode.J, 71), (KeyCode.K, 72),
        (KeyCode.L, 74), (KeyCode.Semicolon, 76), (KeyCode.Quote, 77),
        (KeyCode.W, 61), (KeyCode.E, 63), (KeyCode.T, 66), (KeyCode.Y, 68),
        (KeyCode.U, 70), (KeyCode.O, 73), (KeyCode.P, 75),
    };

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (GameModeManager.Instance?.currentMode != AppMode.TestMode) return;
        HandlePCKeyboardInput();
    }

    private void HandlePCKeyboardInput()
    {
        foreach (var (key, midi) in PCKeyMap)
        {
            if (Input.GetKeyDown(key)) FireNote(midi, true);
            if (Input.GetKeyUp(key)) FireNote(midi, false);
        }
    }

    // Call this from audio detector or MIDI in AR Mode
    public void ReportNoteDetected(int midiNote, bool isDown)
    {
        FireNote(midiNote, isDown);
    }

    private void FireNote(int midiNote, bool isDown)
    {
        testKeyboard?.SimulateKeyPress(midiNote, isDown);
        OnNoteInput?.Invoke(midiNote, isDown);
        Debug.Log($"[Input] MIDI {midiNote} ({(isDown ? "DOWN" : "UP")})");

        // Add this:
        if (isDown)
            NoteSpawner.Instance?.CheckHit(midiNote);
    }
}
