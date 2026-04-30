using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

[System.Serializable]
public class KeyLane
{
    public int midiNote;    // MIDI note number (e.g., 60 for Middle C)
    public string noteName; // Note name (e.g., "C4")
    public bool isBlackKey; // Indicates if the key is a black key
    public Vector3 worldPosition; // Position of the key in world space
    public int laneIndex; // Index of the lane to which the key belongs. 0-75 in this case
}

public class KeyboardMapper : MonoBehaviour
{
    // Array of KeyLane objects representing the 76 keys of the piano
    public static KeyboardMapper Instance { get; private set; }

    [Header("Keyboard Dimensions (meters)")]
    public float keyboardWidth = 1.22f; // Total width of the keyboard in meters
    public float keyboardDepth = 0.15f; // Depth of the keys in meters
    public float laneHeight = 0.02f; // Height of the lane above the keyboard in meters

    [Header("Runtime Data")]
    public KeyLane[] keyLanes = new KeyLane[76]; // Array to hold the KeyLane data for each key

    // First and last MIDI note numbers for the 76 keys E1 to G7
    private const int FIRST_MIDI = 28;
    private const int LAST_MIDI = 103;

    // White key positions: which semitones within an octave are white
    private static readonly bool[] IsBlack = {
        false, true, false, true, false, false, true, false, true, false, true, false
        // C,   C#,   D,     D#,    E,    F,     F#,   G,     G#,    A,    A#,    B
    };

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// </summary>
    void Awake()
    {
        // Ensure that only one instance of KeyboardMapper exists (Singleton pattern)
        // Need to make sure there is only one keyboard mapper in the scene, otherwise
        //  we might have multiple instances trying to manage the same data
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }

        Instance = this;
        // Generate the default lane positions based on the keyboard dimensions and MIDI note range
        GenerateDefaultLanes();
    }

    public void GenerateDefaultLanes()
    {
        // Count white keys to calculate X spacing
        int whiteKeyCount = 0;
        // We can calculate the number of white keys by iterating 
        // through the MIDI notes and checking if they are black or white
        for (int midi = FIRST_MIDI; midi <= LAST_MIDI; midi++)
        {
            int semitone = midi % 12;
            if (!IsBlack[semitone]) whiteKeyCount++;
        }

        float whiteKeyWidth = keyboardWidth / whiteKeyCount; // Width of each white key
        float currentWhiteX = -(keyboardWidth / 2f) + (whiteKeyWidth / 2f); // Start from the left edge
        int laneIdx = 0;

        // Iterate through MIDI notes and create KeyLane objects
        for (int midi = FIRST_MIDI; midi <= LAST_MIDI; midi++)
        {
            int semitone = midi % 12;
            bool black = IsBlack[semitone];
            string name = GetNoteName(midi);

            Vector3 pos;

            if (!black)
            {
                pos = new Vector3(currentWhiteX, laneHeight, 0f);
                currentWhiteX += whiteKeyWidth; // Move to the next white key position
            } // For black keys, we position them between the white keys. The X position is calculated based on the current white key position.
            else 
            {
                // For black keys, we need to position them between the white keys. The X position is calculated based on the current white key position.
                float blackX = currentWhiteX - (whiteKeyWidth / 2f); // Position black key between white keys
                pos = new Vector3(blackX, laneHeight + 0.005f, -keyboardDepth * 0.3f); // Slightly above and in front of white keys
            }

            // Create a new KeyLane object and store it in the array
            keyLanes[laneIdx] = new KeyLane
            {
                midiNote = midi,
                noteName = name,
                isBlackKey = black,
                worldPosition = pos,
                laneIndex = laneIdx
            };
            laneIdx++;
        }
    }

    public void RebuildLanePositions()
    {
        // This method can be called if the keyboard dimensions 
        // change at runtime, to recalculate the positions of the lanes
        GenerateDefaultLanes();
    }

    public KeyLane GetLane(int midiNote)
    {
        int idx = midiNote - FIRST_MIDI;
        if (idx < 0 || idx >= keyLanes.Length) return null; // Out of range
        return keyLanes[idx];
    }

    // /// <summary>
    // /// Start is called on the frame when a script is enabled just before
    // /// any of the Update methods is called the first time.
    // /// </summary>
    // void Start()
    // {
    //     GenerateDefaultLanes();
    // }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (keyLanes == null || keyLanes.Length == 0) return;
        
        foreach (var lane in keyLanes)
        {
            if (lane == null) continue;
            Gizmos.color = lane.isBlackKey ? Color.black : Color.white;
            Gizmos.DrawWireCube(lane.worldPosition, new  Vector3(0.018f, 0.002f, 0.12f)); // Draw a
        }
    }
#endif

    // ********** HELPER METHODS **********
    private string GetNoteName(int midi)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int octave = (midi / 12) - 1; // MIDI note 0 is C-1
        return names[midi % 12] + octave;
    }
}
