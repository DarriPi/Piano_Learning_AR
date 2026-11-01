using System;
using System.Collections.Generic;

namespace PianoLearningCore
{
    /// <summary>
    /// Tidies a MIDI file's notes before they become a <see cref="Song"/>. Downloaded files often
    /// carry things nobody can play on one keyboard, and each of them stalls the song:
    ///
    ///   Duplicates — the same key starting twice at (almost) the same moment: a part copied onto a
    ///                second track or channel, both hands on one key, a melody doubled by another
    ///                instrument. The two blocks are drawn exactly on top of each other, but one key
    ///                press judges only one of them, so Practice waits for a second press and
    ///                Assessment marks the other copy missed. (A Für Elise download had every one of
    ///                its 1,051 notes twice.) Merged into one note: the earliest start, the latest end.
    ///   Drums      — MIDI channel 10 is General MIDI percussion; its "notes" are drum sounds that
    ///                would land on random piano keys. Removed.
    ///
    /// A clean file comes through unchanged: chords (different keys together) are never touched, and
    /// no real repeated note is anywhere near <see cref="DuplicateWindowSeconds"/> apart.
    ///
    /// Notes outside the keyboard are dropped later, by <see cref="FallingNotesController.LoadSong"/>,
    /// because only the controller knows which keyboard the song is going onto.
    ///
    /// No Unity types in here, so it can be run and tested outside the editor.
    /// </summary>
    public static class MidiNoteCleanup
    {
        /// <summary>The same key starting again within this many seconds is one note written twice.
        /// Nobody can strike a key twice this fast: a fast piece's quickest repeat is ~100 ms.</summary>
        public const float DuplicateWindowSeconds = 0.03f;

        /// <summary>Zero-based index of MIDI channel 10, General MIDI percussion.</summary>
        public const int DrumChannel = 9;

        /// <summary>What <see cref="Clean"/> removed.</summary>
        public struct Report
        {
            public int duplicatesMerged;
            public int drumNotesRemoved;

            public bool Changed => duplicatesMerged > 0 || drumNotesRemoved > 0;
        }

        /// <summary>
        /// Returns a cleaned copy of <paramref name="notes"/> (the input list is not modified).
        /// The result is NOT in time order; the caller sorts it.
        /// </summary>
        public static List<MidiFileParser.RawNote> Clean(List<MidiFileParser.RawNote> notes, out Report report)
        {
            report = new Report();

            var kept = new List<MidiFileParser.RawNote>(notes.Count);
            foreach (var n in notes)
            {
                if (n.channel == DrumChannel) report.drumNotesRemoved++;
                else kept.Add(n);
            }

            // Key by key, in time order, so copies of one note end up next to each other.
            kept.Sort((a, b) => a.midiNumber != b.midiNumber
                ? a.midiNumber.CompareTo(b.midiNumber)
                : a.startSeconds.CompareTo(b.startSeconds));

            var result = new List<MidiFileParser.RawNote>(kept.Count);
            foreach (var n in kept)
            {
                int last = result.Count - 1;
                // Measured from the FIRST copy's start, so a chain of near-copies can't creep along.
                if (last >= 0 && result[last].midiNumber == n.midiNumber
                    && n.startSeconds - result[last].startSeconds <= DuplicateWindowSeconds)
                {
                    var first = result[last];
                    float end = Math.Max(first.startSeconds + first.durationSeconds,
                                         n.startSeconds + n.durationSeconds);
                    first.durationSeconds = end - first.startSeconds;
                    first.velocity = Math.Max(first.velocity, n.velocity);
                    result[last] = first;
                    report.duplicatesMerged++;
                    continue;
                }
                result.Add(n);
            }
            return result;
        }
    }
}
