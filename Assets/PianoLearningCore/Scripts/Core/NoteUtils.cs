using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Static helpers for converting between MIDI note numbers and human-readable
    /// note names, and for distinguishing white vs black keys.
    /// </summary>
    public static class NoteUtils
    {
        // ----- Reference points -----

        /// <summary>MIDI number of middle C (C4).</summary>
        public const int MIDDLE_C = 60;

        /// <summary>MIDI number of A0 (lowest key on a full 88-key piano).</summary>
        public const int PIANO_LOWEST_88 = 21; // A0

        /// <summary>MIDI number of C8 (highest key on a full 88-key piano).</summary>
        public const int PIANO_HIGHEST_88 = 108; // C8

        /// <summary>MIDI number of E1 (lowest key on a 76-key keyboard).</summary>
        public const int KEY76_LOWEST = 28; // E1

        /// <summary>MIDI number of G7 (highest key on a 76-key keyboard).</summary>
        public const int KEY76_HIGHEST = 103; // G7

        // ----- White / black key detection -----

        // Within an octave (pitch class 0..11), these are the black-key pitch classes:
        //   1=C#, 3=D#, 6=F#, 8=G#, 10=A#
        private static readonly bool[] BlackKeyPattern = new bool[]
        {
            false, true,  false, true,  false, false, true,  false, true,  false, true,  false
            // C    C#    D      D#    E      F      F#     G      G#    A      A#     B
        };

        public static bool IsBlackKey(int midiNumber)
        {
            int pc = ((midiNumber % 12) + 12) % 12; // handle negatives just in case
            return BlackKeyPattern[pc];
        }

        public static bool IsWhiteKey(int midiNumber) => !IsBlackKey(midiNumber);

        // ----- Names -----

        private static readonly string[] NoteNames = new string[]
        {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };

        /// <summary>Get a name like "C4", "F#5", "A0" from a MIDI number.</summary>
        public static string GetNoteName(int midiNumber)
        {
            int pc = ((midiNumber % 12) + 12) % 12;
            int octave = (midiNumber / 12) - 1; // MIDI 60 -> octave 4
            return NoteNames[pc] + octave;
        }

        // ----- White-key index (used to lay out white keys side-by-side) -----

        /// <summary>
        /// How many white keys are at or below the given MIDI number, starting
        /// from rangeStartMidi inclusive. Used to compute the X position of a key
        /// on a horizontally arranged keyboard.
        ///
        /// E.g. on a 76-key keyboard starting at E1, E1 returns 0, F1 returns 1, etc.
        /// Returns -1 if the given midi is outside the range or is a black key.
        /// </summary>
        public static int GetWhiteKeyIndex(int midiNumber, int rangeStartMidi)
        {
            if (midiNumber < rangeStartMidi) return -1;
            if (IsBlackKey(midiNumber)) return -1;

            int count = 0;
            for (int m = rangeStartMidi; m < midiNumber; m++)
            {
                if (IsWhiteKey(m)) count++;
            }
            return count;
        }

        /// <summary>Count white keys in the inclusive range [startMidi, endMidi].</summary>
        public static int CountWhiteKeys(int startMidi, int endMidi)
        {
            int count = 0;
            for (int m = startMidi; m <= endMidi; m++)
            {
                if (IsWhiteKey(m)) count++;
            }
            return count;
        }

        /// <summary>
        /// Returns the MIDI number of the white key immediately to the left of a given key.
        /// If the given key is itself white, returns it. If there is no white key to the left
        /// within the range, returns -1.
        /// </summary>
        public static int GetWhiteKeyToLeft(int midiNumber)
        {
            int m = midiNumber;
            while (m >= 0 && IsBlackKey(m)) m--;
            return m;
        }
    }
}
