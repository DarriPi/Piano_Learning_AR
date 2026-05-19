using System;
using System.Collections.Generic;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Compares notes detected by <see cref="PianoAudioDetector"/> against the notes
    /// currently expected by <see cref="FallingNotesController"/>, and fires events for
    /// correct hits, wrong notes, and missed notes.
    ///
    /// Also drives the visual feedback colours on the falling note blocks.
    ///
    /// Scoring (per note):
    ///   Correct  : +100 × timing multiplier (1.0 at perfect, 0.5 at edge of window)
    ///   Incorrect: +0   (wrong note played while another was expected)
    ///   Missed   : +0   (note passed the hit window without any detection)
    ///
    /// Attach this to any GameObject. Wire up the two references in the Inspector,
    /// then call ResetScore() before each song.
    /// </summary>
    [DisallowMultipleComponent]
    public class NoteEvaluator : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // Inspector
        // ----------------------------------------------------------------

        [Header("References")]
        [Tooltip("The PianoAudioDetector that supplies detected MIDI notes each frame.")]
        public PianoAudioDetector audioDetector;

        [Tooltip("The FallingNotesController driving playback.")]
        public FallingNotesController fallingNotesController;

        [Header("Timing")]
        [Tooltip("Seconds before/after a note's start time in which a detection counts as correct. " +
                 "±150 ms is a comfortable window that still rewards accurate playing.")]
        public float hitWindowSeconds = 0.15f;

        [Tooltip("Extra seconds after the hit window closes before a note is officially marked missed. " +
                 "Gives a small grace period for slow analysisInterval ticks.")]
        public float missGraceSeconds = 0.06f;

        [Header("Visual Feedback")]
        public Color correctColor   = new Color(0.15f, 0.90f, 0.30f, 1f); // green
        public Color incorrectColor = new Color(0.95f, 0.20f, 0.20f, 1f); // red
        public Color missedColor    = new Color(0.50f, 0.50f, 0.50f, 1f); // grey

        [Tooltip("How long (seconds) the feedback colour stays on a note block.")]
        public float feedbackDuration = 0.30f;

        // ----------------------------------------------------------------
        // Events
        // ----------------------------------------------------------------

        /// <summary>A detected note matched the expected note within the hit window.</summary>
        public event Action<int> OnNoteCorrect;

        /// <summary>A note was detected but it didn't match anything expected right now.</summary>
        public event Action<int> OnNoteIncorrect;

        /// <summary>An expected note passed its hit window without being detected.</summary>
        public event Action<int> OnNoteMissed;

        /// <summary>Fired after every song note has been judged (last note missed or correct).</summary>
        public event Action<int, int, int, int> OnSongComplete; // score, correct, incorrect, missed

        // ----------------------------------------------------------------
        // Public read-only scoring
        // ----------------------------------------------------------------

        public int Score          { get; private set; }
        public int NotesCorrect   { get; private set; }
        public int NotesIncorrect { get; private set; }
        public int NotesMissed    { get; private set; }

        /// <summary>Total notes in the song (set when a song is loaded).</summary>
        public int TotalNotes     { get; private set; }

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        // Indices (into Song.notes) that have already been judged (hit or missed).
        private readonly HashSet<int> _judgedIndices = new HashSet<int>();
        private bool _songCompleteFired;

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void OnEnable()
        {
            if (audioDetector != null)
                audioDetector.OnNotesDetected += HandleDetection;
        }

        private void OnDisable()
        {
            if (audioDetector != null)
                audioDetector.OnNotesDetected -= HandleDetection;
        }

        private void Update()
        {
            CheckMissedNotes();
            CheckSongComplete();
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>Reset all scores and judgement state. Call this before starting a new song.</summary>
        public void ResetScore()
        {
            Score = 0;
            NotesCorrect  = 0;
            NotesIncorrect = 0;
            NotesMissed   = 0;
            _judgedIndices.Clear();
            _songCompleteFired = false;

            TotalNotes = (fallingNotesController?.Song != null)
                ? fallingNotesController.Song.notes.Count
                : 0;
        }

        // ----------------------------------------------------------------
        // Detection handler (called by PianoAudioDetector.OnNotesDetected)
        // ----------------------------------------------------------------

        private void HandleDetection(int[] detectedMidi)
        {
            if (!IsControllerReady()) return;

            float now    = fallingNotesController.CurrentTime;
            var   notes  = fallingNotesController.Song.notes;

            foreach (int detected in detectedMidi)
            {
                bool matchedExpected = false;

                for (int i = 0; i < notes.Count; i++)
                {
                    if (_judgedIndices.Contains(i)) continue;

                    var   note     = notes[i];
                    float timeDiff = now - note.startTime; // positive = late, negative = early

                    if (timeDiff < -hitWindowSeconds || timeDiff > hitWindowSeconds) continue;

                    if (detected == note.midiNumber)
                    {
                        // --- Correct hit ---
                        _judgedIndices.Add(i);

                        // Timing multiplier: 1.0 at perfect centre, 0.5 at window edge.
                        float t      = 1f - Mathf.Abs(timeDiff) / hitWindowSeconds;
                        int   points = Mathf.RoundToInt(100f * Mathf.Lerp(0.5f, 1.0f, t));

                        Score += points;
                        NotesCorrect++;

                        FlashNote(note.midiNumber, correctColor);
                        OnNoteCorrect?.Invoke(detected);
                        matchedExpected = true;
                        break; // one detection can only satisfy one expected note
                    }
                }

                if (!matchedExpected)
                {
                    // --- Wrong note (no matching expected note in window) ---
                    // Note: we do NOT count spurious detections when no note is expected
                    // near the current time, to avoid penalising harmonics / room noise
                    // (a known limitation of audio detection per the article).
                    bool anyNoteExpectedNow = AnyNoteExpectedNow(notes, now);
                    if (anyNoteExpectedNow)
                    {
                        NotesIncorrect++;
                        OnNoteIncorrect?.Invoke(detected);
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // Missed-note detection (runs every Update frame)
        // ----------------------------------------------------------------

        private void CheckMissedNotes()
        {
            if (!IsControllerReady()) return;

            float now   = fallingNotesController.CurrentTime;
            var   notes = fallingNotesController.Song.notes;

            for (int i = 0; i < notes.Count; i++)
            {
                if (_judgedIndices.Contains(i)) continue;

                var   note     = notes[i];
                float deadline = note.startTime + hitWindowSeconds + missGraceSeconds;

                if (now > deadline)
                {
                    _judgedIndices.Add(i);
                    NotesMissed++;
                    FlashNote(note.midiNumber, missedColor);
                    OnNoteMissed?.Invoke(note.midiNumber);
                }
            }
        }

        // ----------------------------------------------------------------
        // Song-complete detection
        // ----------------------------------------------------------------

        private void CheckSongComplete()
        {
            if (_songCompleteFired) return;
            if (!IsControllerReady()) return;

            var notes = fallingNotesController.Song.notes;
            if (_judgedIndices.Count < notes.Count) return;
            if (!fallingNotesController.IsFinished && fallingNotesController.IsPlaying) return;

            _songCompleteFired = true;
            OnSongComplete?.Invoke(Score, NotesCorrect, NotesIncorrect, NotesMissed);
        }

        // ----------------------------------------------------------------
        // Visual feedback
        // ----------------------------------------------------------------

        private void FlashNote(int midiNumber, Color color)
        {
            if (fallingNotesController == null) return;
            FallingNote fn = fallingNotesController.FindActiveNote(midiNumber);
            fn?.SetFeedbackColor(color, feedbackDuration);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private bool IsControllerReady()
        {
            return fallingNotesController != null
                && fallingNotesController.Song != null
                && fallingNotesController.IsPlaying;
        }

        private bool AnyNoteExpectedNow(System.Collections.Generic.List<PianoNote> notes, float now)
        {
            foreach (var note in notes)
            {
                float diff = now - note.startTime;
                if (diff >= -hitWindowSeconds && diff <= hitWindowSeconds) return true;
            }
            return false;
        }
    }
}
