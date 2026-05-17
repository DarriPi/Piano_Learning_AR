using System;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// A single musical note in a song.
    /// Time is in seconds, measured from the start of the song.
    /// MIDI number follows the standard: middle C (C4) = 60, A4 = 69, A0 = 21.
    /// For a 76-key keyboard, the range is E1 (28) to G7 (103).
    /// </summary>
    [Serializable]
    public struct PianoNote
    {
        /// <summary>MIDI note number (0-127). E.g. 60 = middle C.</summary>
        public int midiNumber;

        /// <summary>When the note starts, in seconds from song start.</summary>
        public float startTime;

        /// <summary>How long the note is held, in seconds. Must be &gt; 0.</summary>
        public float duration;

        /// <summary>How loud the note is, 0..1. From MIDI velocity / 127.</summary>
        public float velocity;

        public PianoNote(int midiNumber, float startTime, float duration, float velocity = 0.7f)
        {
            this.midiNumber = midiNumber;
            this.startTime = startTime;
            this.duration = Mathf.Max(0.001f, duration);
            this.velocity = Mathf.Clamp01(velocity);
        }

        /// <summary>The time (seconds) at which the note ends.</summary>
        public float EndTime => startTime + duration;

        public override string ToString()
        {
            return $"{NoteUtils.GetNoteName(midiNumber)} (MIDI {midiNumber}) @ {startTime:F2}s for {duration:F2}s";
        }
    }
}
