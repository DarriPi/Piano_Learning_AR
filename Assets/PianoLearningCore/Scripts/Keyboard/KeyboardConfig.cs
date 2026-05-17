using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Physical dimensions of the keyboard you're aligning to.
    /// Defaults are tuned for a standard 76-key keyboard (range E1..G7)
    /// with full-size keys (~23 mm wide white keys).
    ///
    /// All values are in METERS. Unity's default unit is 1 unit = 1 meter.
    /// </summary>
    [CreateAssetMenu(fileName = "KeyboardConfig", menuName = "Piano Learning/Keyboard Config")]
    public class KeyboardConfig : ScriptableObject
    {
        [Header("Note range (MIDI numbers)")]
        [Tooltip("Lowest playable MIDI note. 28 = E1 (lowest of a 76-key keyboard).")]
        public int startMidiNote = NoteUtils.KEY76_LOWEST;   // 28 = E1

        [Tooltip("Highest playable MIDI note. 103 = G7 (highest of a 76-key keyboard).")]
        public int endMidiNote = NoteUtils.KEY76_HIGHEST;    // 103 = G7

        [Header("White key dimensions (meters)")]
        [Tooltip("Width of one white key. Standard full-size pianos are ~23 mm.")]
        public float whiteKeyWidth = 0.0235f;

        [Tooltip("Depth of a white key (front edge to back edge).")]
        public float whiteKeyLength = 0.15f;

        [Header("Black key dimensions (meters)")]
        [Tooltip("Width of one black key. Typically ~11–14 mm.")]
        public float blackKeyWidth = 0.013f;

        [Tooltip("Length (depth) of a black key. About 60% of a white key.")]
        public float blackKeyLength = 0.095f;

        [Tooltip("How far the black keys sit above the white-key top surface.")]
        public float blackKeyHeightAboveWhite = 0.012f;

        [Header("Where notes 'hit' the keyboard")]
        [Tooltip("The Z coordinate where a falling note has just reached the hit line. " +
                 "0 = front edge of the keys (closest to the player).")]
        public float hitLineZ = 0f;

        // ---------------- Computed helpers ----------------

        /// <summary>Total number of keys (white + black) in this configuration.</summary>
        public int TotalKeys => endMidiNote - startMidiNote + 1;

        /// <summary>Number of white keys in this configuration.</summary>
        public int WhiteKeyCount => NoteUtils.CountWhiteKeys(startMidiNote, endMidiNote);

        /// <summary>Total physical width of the keyboard in meters.</summary>
        public float TotalWidth => WhiteKeyCount * whiteKeyWidth;

        /// <summary>True if the given MIDI number is inside this keyboard's range.</summary>
        public bool ContainsNote(int midiNumber)
        {
            return midiNumber >= startMidiNote && midiNumber <= endMidiNote;
        }
    }
}
