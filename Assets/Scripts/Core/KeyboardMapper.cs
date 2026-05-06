using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class KeyLane
{
    public int    midiNote;       // MIDI number (28 = E1, 103 = G7)
    public string noteName;       // e.g. "C4"
    public bool   isBlackKey;
    public Vector3 worldPosition; // Set during calibration or test mode
    public int    laneIndex;      // 0–75
}

public class KeyboardMapper : MonoBehaviour
{
    public static KeyboardMapper Instance { get; private set; }

    [Header("Keyboard Dimensions (metres)")]
    public float keyboardWidth = 1.22f;  // Standard 76-key width ~122 cm
    public float keyboardDepth = 0.15f;
    public float laneHeight    = 0.02f;  // Height above key surface

    // ─────────────────────────────────────────────────────────────────────────
    // flipXLayout
    //
    // When FALSE (default):  E1 is generated at local –X, G7 at local +X.
    //   • Correct when KeyboardMapper's Y rotation = 0 (keyboard faces +Z).
    //   • Use this for the TestMode virtual keyboard.
    //
    // When TRUE:             X positions are negated before rotation is applied.
    //   • Correct when KeyboardMapper's Y rotation = 180° (keyboard faces –Z /
    //     faces the player), which is the typical AR overlay orientation.
    //   • Set this on the CalibrationOverlayKeyboard's KeyboardMapper (or any
    //     mapper whose parent transform has been turned to face the camera).
    //
    // Quick diagnostic: stand in front of the keyboard and check whether the
    // lowest note (E1) appears on YOUR left. If it is on your right, enable
    // flipXLayout on that mapper.
    // ─────────────────────────────────────────────────────────────────────────
    [Header("Layout")]
    [Tooltip("Enable when the keyboard's transform Y rotation is ~180° (facing the camera). " +
             "Reverses the X direction so E1 appears on the player's left.")]
    public bool flipXLayout = false;

    [Header("Runtime Data")]
    public KeyLane[] keyLanes = new KeyLane[76];

    // MIDI 28 = E1 (first key of a 76-key keyboard)
    // MIDI 103 = G7 (last key)
    private const int FIRST_MIDI = 28;
    private const int LAST_MIDI  = 103;

    // Which semitones within an octave are black keys
    // Index = semitone (0 = C, 1 = C#, … 11 = B)
    private static readonly bool[] IsBlack =
    {
        false, true,  false, true,  false,  // C  C#  D  D#  E
        false, true,  false, true,  false, true, false  // F  F#  G  G#  A  A#  B
    };

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GenerateDefaultLanes();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds all 76 lane world-positions from the current transform and
    /// flipXLayout setting.  Call after calibration or any transform change.
    /// </summary>
    public void GenerateDefaultLanes()
    {
        // ── Count white keys so we can derive per-key width ───────────────────
        int whiteKeyCount = 0;
        for (int midi = FIRST_MIDI; midi <= LAST_MIDI; midi++)
            if (!IsBlack[midi % 12]) whiteKeyCount++;

        float whiteKeyWidth  = keyboardWidth / whiteKeyCount;

        // ── Starting X in LOCAL space ─────────────────────────────────────────
        // Without flip: E1 starts at the negative (left) end → moves right (+X).
        // With    flip: E1 starts at the positive (right) end → moves left (–X).
        // After transform.rotation is applied the world-space result is correct
        // for both a forward-facing (0°) and a camera-facing (180°) keyboard.
        float sign           = flipXLayout ? -1f : 1f;
        float currentWhiteX  = sign * (-(keyboardWidth / 2f) + (whiteKeyWidth / 2f));
        float whiteStep      = sign * whiteKeyWidth;

        int laneIdx = 0;

        for (int midi = FIRST_MIDI; midi <= LAST_MIDI; midi++)
        {
            int  semitone = midi % 12;
            bool black    = IsBlack[semitone];
            string name   = GetNoteName(midi);

            Vector3 localPos;

            if (!black)
            {
                // White key: advance the cursor one white-key width
                localPos        = new Vector3(currentWhiteX, laneHeight, 0f);
                currentWhiteX  += whiteStep;
            }
            else
            {
                // Black key: sits halfway between the previous and next white key.
                // currentWhiteX already points to the NEXT white key's centre,
                // so stepping back by half a white key gives the correct gap.
                float blackX = currentWhiteX - (whiteStep * 0.5f);
                localPos     = new Vector3(blackX, laneHeight + 0.005f, -keyboardDepth * 0.3f);
            }

            keyLanes[laneIdx] = new KeyLane
            {
                midiNote      = midi,
                noteName      = name,
                isBlackKey    = black,
                worldPosition = transform.position + transform.rotation * localPos,
                laneIndex     = laneIdx
            };

            laneIdx++;
        }
    }

    /// <summary>Call after calibration to rebuild lane positions from the new transform.</summary>
    public void RebuildLanePositions() => GenerateDefaultLanes();

    /// <summary>Returns the lane for a given MIDI note, or null if out of range.</summary>
    public KeyLane GetLane(int midiNote)
    {
        int idx = midiNote - FIRST_MIDI;
        if (idx < 0 || idx >= 76) return null;
        return keyLanes[idx];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private string GetNoteName(int midi)
    {
        string[] names = { "C","C#","D","D#","E","F","F#","G","G#","A","A#","B" };
        int octave = (midi / 12) - 1;
        return names[midi % 12] + octave;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (keyLanes == null) return;
        foreach (var lane in keyLanes)
        {
            if (lane == null) continue;
            Gizmos.color = lane.isBlackKey ? Color.black : Color.white;
            Gizmos.DrawWireCube(lane.worldPosition, new Vector3(0.018f, 0.002f, 0.12f));
        }
    }
#endif
}