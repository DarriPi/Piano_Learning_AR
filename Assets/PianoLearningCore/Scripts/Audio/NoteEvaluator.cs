using System;
using System.Collections.Generic;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Compares notes detected by <see cref="PianoAudioDetector"/> against the notes
    /// currently expected by <see cref="FallingNotesController"/>, drives the visual
    /// feedback colours on the falling-note blocks, and tracks the score.
    ///
    /// Listens to <see cref="PianoAudioDetector.OnNoteOn"/>, which fires exactly once
    /// per detected keystroke (debounced and harmonic-suppressed upstream). This means
    /// each press is judged once, not many times per second as the raw detection feed
    /// would do.
    ///
    /// Scoring (per note):
    ///   Correct  : pointsPerNote * timingMultiplier (1.0 at perfect, edgeFraction at window edge)
    ///   Incorrect: 0  (a wrong note was played while another was expected)
    ///   Missed   : 0  (a note passed its hit window without being played)
    ///
    /// Attach this to any GameObject (or the same one as PianoAudioDetector for clarity).
    /// Wire up the two References in the Inspector and call ResetScore() before each song.
    /// </summary>
    [DisallowMultipleComponent]
    public class NoteEvaluator : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // Inspector
        // ----------------------------------------------------------------

        [Header("References")]
        [Tooltip("The PianoAudioDetector that supplies note-on events.")]
        public PianoAudioDetector audioDetector;

        [Tooltip("The FallingNotesController driving song playback.")]
        public FallingNotesController fallingNotesController;

        [Header("Mode")]
        [Tooltip("Practice = guidance on: a wrong press reveals the expected note(s) in red.\n" +
                 "Assessment = graded run: that answer-reveal is withheld (you still see your own " +
                 "correct/green hits, and the score still counts errors).\n" +
                 "Set automatically when a song is chosen from the SongSelectionMenu.")]
        public SessionMode mode = SessionMode.Practice;

        [Header("Timing")]
        [Tooltip("Seconds before/after a note's start time in which a press counts as correct. " +
                 "+/-150 ms is a comfortable window that still rewards accurate playing.")]
        public float hitWindowSeconds = 0.15f;

        [Tooltip("Extra seconds after the hit window closes before a note is marked missed. " +
                 "Gives a small grace period for FFT-detection latency.")]
        public float missGraceSeconds = 0.05f;

        [Header("Scoring")]
        [Tooltip("Maximum points awarded for a perfectly-timed hit.")]
        public int pointsPerNote = 100;

        [Range(0.1f, 1f)]
        [Tooltip("Fraction of pointsPerNote awarded for a hit at the very edge of the window. " +
                 "0.5 = a barely-on-time hit is worth half a perfect hit.")]
        public float edgeWindowScoreFraction = 0.5f;

        [Header("Visual Feedback")]
        public Color correctColor   = new Color(0.15f, 0.90f, 0.30f, 1f); // green
        public Color incorrectColor = new Color(0.95f, 0.20f, 0.20f, 1f); // red
        public Color missedColor    = new Color(0.50f, 0.50f, 0.50f, 1f); // grey

        [Tooltip("How long (seconds) the feedback colour stays on a note block.")]
        public float feedbackDuration = 0.40f;

        [Header("Debug")]
        [Tooltip("Print one line per HIT / WRONG / MISS to the Console.")]
        public bool logEvents = false;

        // ----------------------------------------------------------------
        // Events
        // ----------------------------------------------------------------

        /// <summary>A detected note matched an expected note in the hit window. (midi, points)</summary>
        public event Action<int, int> OnNoteCorrect;

        /// <summary>A note was played that didn't match any expected note in the hit window.</summary>
        public event Action<int> OnNoteIncorrect;

        /// <summary>An expected note passed its hit window without being played.</summary>
        public event Action<int> OnNoteMissed;

        /// <summary>Fires once all song notes have been judged. (score, correct, incorrect, missed)</summary>
        public event Action<int, int, int, int> OnSongComplete;

        // ----------------------------------------------------------------
        // Public read-only scoring
        // ----------------------------------------------------------------

        public int Score          { get; private set; }
        public int NotesCorrect   { get; private set; }
        public int NotesIncorrect { get; private set; }
        public int NotesMissed    { get; private set; }

        /// <summary>Consecutive correct notes right now. Reset to 0 by any wrong or missed note.</summary>
        public int CurrentStreak  { get; private set; }

        /// <summary>Longest run of consecutive correct notes this song. Surfaced on the score board.</summary>
        public int BestStreak     { get; private set; }

        /// <summary>Total notes in the loaded song. Read live from the controller so it's correct
        /// even when ResetScore() runs before the song is loaded (or isn't called at all, as in the
        /// standalone demo path) — otherwise the final readout shows "/0" and 0% accuracy.</summary>
        public int TotalNotes => fallingNotesController != null && fallingNotesController.Song != null
            ? fallingNotesController.Song.notes.Count
            : 0;

        /// <summary>NotesCorrect as a percentage of TotalNotes (0..100).</summary>
        public float AccuracyPercent => TotalNotes == 0
            ? 0f
            : NotesCorrect / (float)TotalNotes * 100f;

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        // Song-note indices already judged (hit or missed); prevents double-counting.
        private readonly HashSet<int> _judgedIndices = new HashSet<int>();
        private bool _songCompleteFired;

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void OnEnable()
        {
            if (audioDetector != null)
                audioDetector.OnNoteOn += HandleNoteOn;
        }

        private void OnDisable()
        {
            if (audioDetector != null)
                audioDetector.OnNoteOn -= HandleNoteOn;
        }

        private void Update()
        {
            CheckMissedNotes();
            CheckSongComplete();
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>Reset score and judgement state. Call this before starting a new song.</summary>
        public void ResetScore()
        {
            Score = NotesCorrect = NotesIncorrect = NotesMissed = 0;
            CurrentStreak = BestStreak = 0;
            _judgedIndices.Clear();
            _songCompleteFired = false;
        }

        /// <summary>Switch between Practice (guidance) and Assessment (graded) mode.</summary>
        public void SetMode(SessionMode newMode) => mode = newMode;

        // ----------------------------------------------------------------
        // Detection handler — one call per keystroke
        // ----------------------------------------------------------------

        private void HandleNoteOn(int detectedMidi)
        {
            if (!IsControllerReady()) return;

            float now = fallingNotesController.CurrentTime;
            var notes = fallingNotesController.Song.notes;

            // Find the closest unjudged matching note within the hit window.
            int bestIndex = -1;
            float bestAbsDiff = float.MaxValue;
            for (int i = 0; i < notes.Count; i++)
            {
                if (_judgedIndices.Contains(i)) continue;
                if (notes[i].midiNumber != detectedMidi) continue;

                float diff = now - notes[i].startTime;
                if (diff < -hitWindowSeconds || diff > hitWindowSeconds) continue;

                float abs = Mathf.Abs(diff);
                if (abs < bestAbsDiff)
                {
                    bestAbsDiff = abs;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                // ----- CORRECT -----
                _judgedIndices.Add(bestIndex);

                // Timing multiplier: 1.0 at perfect, edgeWindowScoreFraction at window edge.
                float t = 1f - bestAbsDiff / hitWindowSeconds;
                int points = Mathf.RoundToInt(
                    pointsPerNote * Mathf.Lerp(edgeWindowScoreFraction, 1f, t));

                Score += points;
                NotesCorrect++;
                CurrentStreak++;
                if (CurrentStreak > BestStreak) BestStreak = CurrentStreak;
                FlashNote(detectedMidi, correctColor);

                if (logEvents)
                    Debug.Log($"[NoteEvaluator] HIT  {NoteUtils.GetNoteName(detectedMidi)} " +
                              $"+{points} (off by {bestAbsDiff * 1000f:F0} ms)");

                OnNoteCorrect?.Invoke(detectedMidi, points);
                return;
            }

            // No matching expected note nearby. Was ANY note expected near now?
            // If yes, that's a wrong-note error. If no, it's almost certainly a harmonic /
            // room noise / lingering resonance — we ignore it to avoid punishing the player
            // for things that aren't their fault.
            if (AnyNoteExpectedNow(notes, now))
            {
                NotesIncorrect++;
                CurrentStreak = 0;

                // Practice only: flash the expected note(s) in the window red so the player can
                // see what they should have played. Assessment withholds this answer-reveal.
                if (mode == SessionMode.Practice)
                {
                    for (int i = 0; i < notes.Count; i++)
                    {
                        if (_judgedIndices.Contains(i)) continue;
                        float diff = now - notes[i].startTime;
                        if (diff >= -hitWindowSeconds && diff <= hitWindowSeconds)
                            FlashNote(notes[i].midiNumber, incorrectColor);
                    }
                }

                if (logEvents)
                    Debug.Log($"[NoteEvaluator] WRONG {NoteUtils.GetNoteName(detectedMidi)}");

                OnNoteIncorrect?.Invoke(detectedMidi);
            }
        }

        // ----------------------------------------------------------------
        // Missed-note check — runs every frame
        // ----------------------------------------------------------------

        private void CheckMissedNotes()
        {
            if (!IsControllerReady()) return;

            float now = fallingNotesController.CurrentTime;
            var notes = fallingNotesController.Song.notes;

            for (int i = 0; i < notes.Count; i++)
            {
                if (_judgedIndices.Contains(i)) continue;

                var note = notes[i];
                float deadline = note.startTime + hitWindowSeconds + missGraceSeconds;
                if (now > deadline)
                {
                    _judgedIndices.Add(i);
                    NotesMissed++;
                    CurrentStreak = 0;
                    FlashNote(note.midiNumber, missedColor);

                    if (logEvents)
                        Debug.Log($"[NoteEvaluator] MISS {NoteUtils.GetNoteName(note.midiNumber)}");

                    OnNoteMissed?.Invoke(note.midiNumber);
                }
            }
        }

        // ----------------------------------------------------------------
        // Song-complete check
        // ----------------------------------------------------------------

        private void CheckSongComplete()
        {
            if (_songCompleteFired) return;
            if (!IsControllerReady() && !IsControllerFinished()) return;
            if (fallingNotesController?.Song == null) return;

            var notes = fallingNotesController.Song.notes;
            if (notes.Count == 0) return;
            if (_judgedIndices.Count < notes.Count) return;

            // Wait until playback also reports finished (or has stopped).
            if (!fallingNotesController.IsFinished && fallingNotesController.IsPlaying) return;

            _songCompleteFired = true;
            if (logEvents)
                Debug.Log($"[NoteEvaluator] DONE — score {Score}, " +
                          $"{NotesCorrect}/{TotalNotes} correct ({AccuracyPercent:F1}%)");

            OnSongComplete?.Invoke(Score, NotesCorrect, NotesIncorrect, NotesMissed);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private void FlashNote(int midi, Color color)
        {
            if (fallingNotesController == null) return;
            var fn = fallingNotesController.FindActiveNote(midi);
            fn?.SetFeedbackColor(color, feedbackDuration);
        }

        private bool IsControllerReady()
            => fallingNotesController != null
            && fallingNotesController.Song != null
            && fallingNotesController.IsPlaying;

        private bool IsControllerFinished()
            => fallingNotesController != null
            && fallingNotesController.IsFinished;

        private bool AnyNoteExpectedNow(List<PianoNote> notes, float now)
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
