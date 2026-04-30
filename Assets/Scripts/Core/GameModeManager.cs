using UnityEngine;
using UnityEngine.Events;

// Enum for different game modes
public enum AppMode { ARMode, TestMode}

public class GameModeManager : MonoBehaviour
{
    public static GameModeManager Instance { get; private set; }

    [Header("Mode")]
    public AppMode currentMode = AppMode.TestMode;

    [SerializeField] private GameObject arModeObjects;  // Objects specific to AR mode
    [SerializeField] private GameObject testModeObjects; // Objects specific to Test mode

    [Header("Events")]
    public UnityEvent<AppMode> onModeChanged;

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// </summary>
    void Awake()
    {
        // Singleton pattern to ensure only one instance of GameModeManager exists 
        // If an instance already exists and it's not this, destroy this game object
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        ApplyMode(currentMode); // Apply the initial mode at the start of the game
    }

    public void SwtichToARMode() => ApplyMode(AppMode.ARMode);
    public void SwithToTestMode() => ApplyMode(AppMode.TestMode);
    public void ToggleMode() => ApplyMode(currentMode == AppMode.ARMode ? AppMode.TestMode : AppMode.ARMode);

    // ********** Helper Methods **********
    private void ApplyMode(AppMode mode)
    {
        currentMode = mode;
        arModeObjects?.SetActive(mode == AppMode.ARMode); // Activate AR mode objects if in AR mode
        testModeObjects?.SetActive(mode == AppMode.TestMode); // Activate Test mode objects
        onModeChanged?.Invoke(mode); // Invoke the mode changed event
        Debug.Log($"[GameModeManager] Switched to {mode}");
    }
}
