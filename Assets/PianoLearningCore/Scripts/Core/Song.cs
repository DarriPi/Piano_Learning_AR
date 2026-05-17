using System;
using System.Collections.Generic;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// A song is a title plus a chronologically sorted list of PianoNotes.
    /// The notes are sorted by startTime ascending so the FallingNotesController
    /// can spawn them efficiently in order.
    /// </summary>
    [Serializable]
    public class Song
    {
        public string title = "Untitled";
        public float bpm = 120f;

        /// <summary>All notes in the song, sorted by startTime.</summary>
        public List<PianoNote> notes = new List<PianoNote>();

        /// <summary>Length of the song in seconds (computed from the latest note's end time).</summary>
        public float DurationSeconds
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < notes.Count; i++)
                {
                    if (notes[i].EndTime > max) max = notes[i].EndTime;
                }
                return max;
            }
        }

        /// <summary>Sort notes by startTime. Call this after adding/loading notes.</summary>
        public void SortByTime()
        {
            notes.Sort((a, b) => a.startTime.CompareTo(b.startTime));
        }

        /// <summary>
        /// Build a small built-in test song so you can see falling notes
        /// before you have any .mid files. Plays a C major scale ascending then descending.
        /// </summary>
        public static Song BuildCMajorScaleDemo()
        {
            var song = new Song { title = "C Major Scale Demo", bpm = 120f };

            // C major scale MIDI numbers starting at C4 (middle C):
            //  C4=60, D4=62, E4=64, F4=65, G4=67, A4=69, B4=71, C5=72
            int[] ascending = { 60, 62, 64, 65, 67, 69, 71, 72 };
            int[] descending = { 71, 69, 67, 65, 64, 62, 60 };

            float t = 0.5f;       // start half a second in so the first note isn't already past
            float quarter = 0.5f; // 120 BPM = 2 beats/s, quarter note = 0.5s
            float gap = 0.05f;    // tiny breath between notes

            foreach (int n in ascending)
            {
                song.notes.Add(new PianoNote(n, t, quarter - gap));
                t += quarter;
            }
            foreach (int n in descending)
            {
                song.notes.Add(new PianoNote(n, t, quarter - gap));
                t += quarter;
            }

            // Add a final long chord (C major) so you can see a held note.
            song.notes.Add(new PianoNote(60, t, 2f)); // C4
            song.notes.Add(new PianoNote(64, t, 2f)); // E4
            song.notes.Add(new PianoNote(67, t, 2f)); // G4

            song.SortByTime();
            return song;
        }
    }
}
