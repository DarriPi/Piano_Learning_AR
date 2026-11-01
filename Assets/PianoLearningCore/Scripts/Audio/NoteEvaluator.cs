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
    ///
    /// Also owns how the app hears the player (<see cref="detection"/>): the microphone, a USB MIDI
    /// keyboard (<see cref="MidiNoteInput"/>, whose note-ons are judged exactly like the mic's), or
    /// nothing at all in Free Play, where no note is judged; see <see cref="SetDetection"/>.
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

        [Tooltip("USB MIDI keyboard input, used in MIDI detection mode. Added to this GameObject at " +
                 "runtime if left empty.")]
        public MidiNoteInput midiInput;

        [Header("Mode")]
        [Tooltip("Practice = guidance on: a wrong press reveals the expected note(s) in red.\n" +
                 "Assessment = graded run: that answer-reveal is withheld (you still see your own " +
                 "correct/green hits, and the score still counts errors).\n" +
                 "Set automatically when a song is chosen from the SongSelectionMenu.")]
        public SessionMode mode = SessionMode.Practice;

        [Header("Detection")]
        [Tooltip("How the app hears the player.\n" +
                 "Microphone = the audio detector judges every note (normal Practice / Assessment); " +
                 "Practice moves on from a note it hasn't heard after 'Mic Practice Timeout Seconds'.\n" +
                 "Midi = a USB MIDI keyboard reports each key press; judged and scored the same way, " +
                 "with the mic switched off.\n" +
                 "FreePlay = nothing listens: the mic is switched off and no note is marked or scored. " +
                 "Practice waits 'Free Play Hold Seconds' on each note instead of for the right key; " +
                 "Assessment plays in real time with no score.\n" +
                 "Set from the song menu's Detection strip, via SetDetection().")]
        public DetectionMode detection = DetectionMode.Microphone;

        [Range(0f, 10f)]
        [Tooltip("Free Play only: how long (seconds) the song holds on each note — or chord — at the " +
                 "line before moving on. Nothing can hear the player in Free Play, so the song can't " +
                 "tell when the note was played; it simply gives them this long.")]
        public float freePlayHoldSeconds = 3f;

        [Range(0f, 10f)]
        [Tooltip("Practice with the Microphone: the longest (seconds) the song waits on a note — or " +
                 "chord — that the detector hasn't heard. Then the note is marked missed and the song " +
                 "moves on, so a note the detector fails to hear can't stall the song. A stop-gap until " +
                 "detection improves; 0 = wait until it's heard, however long. MIDI never gives up: the " +
                 "keyboard can't fail to report a key.")]
        public float micPracticeTimeoutSeconds = 3f;

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

        /// <summary>Notes judged so far this run (correct + incorrect + missed). Grows as the song
        /// plays, unlike TotalNotes which is the whole song known up front.</summary>
        public int NotesJudged => NotesCorrect + NotesIncorrect + NotesMissed;

        /// <summary>Running accuracy over the notes PLAYED so far (correct / judged), reading 100%
        /// before the first judgement. This is the number for the live in-play HUD; AccuracyPercent
        /// (denominator = TotalNotes) stays the right one for the end-of-song board.</summary>
        public float LiveAccuracyPercent => NotesJudged == 0
            ? 100f
            : NotesCorrect / (float)NotesJudged * 100f;

        /// <summary>False in Free Play: nothing is listening, so no note is judged or scored. True for
        /// both the microphone and MIDI.</summary>
        public bool IsListening => detection != DetectionMode.FreePlay;

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        // Song-note indices already judged (hit or missed); prevents double-counting. In Free Play it
        // holds the notes whose wait is over instead — nothing is judged there.
        private readonly HashSet<int> _judgedIndices = new HashSet<int>();
        private bool _songCompleteFired;

        // Set when Free Play or MIDI switched a running mic off, so going back to Microphone restarts
        // exactly that.
        private bool _micStoppedByMode;

        // Seconds the current Practice wait has lasted (paused time excluded); see WaitLimitSeconds.
        private float _waitSeconds;

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            // Built at runtime so the scene needs no edit (same idea as the menu's Detection strip).
            if (midiInput == null) midiInput = GetComponent<MidiNoteInput>();
            if (midiInput == null) midiInput = gameObject.AddComponent<MidiNoteInput>();
        }

        private void OnEnable()
        {
            if (audioDetector != null)
                audioDetector.OnNoteOn += HandleMicNoteOn;
            if (midiInput != null)
                midiInput.OnNoteOn += HandleMidiNoteOn;
        }

        private void OnDisable()
        {
            if (audioDetector != null)
                audioDetector.OnNoteOn -= HandleMicNoteOn;
            if (midiInput != null)
                midiInput.OnNoteOn -= HandleMidiNoteOn;
        }

        private void Update()
        {
            UpdateWaitGate();
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
            _waitSeconds = 0f;
        }

        /// <summary>Switch between Practice (guidance) and Assessment (graded) mode.</summary>
        public void SetMode(SessionMode newMode) => mode = newMode;

        /// <summary>
        /// Switch how the app hears the player (see <see cref="DetectionMode"/>). Only the chosen
        /// source runs: the microphone in Microphone mode, the MIDI keyboard in MIDI mode. Free Play
        /// and MIDI turn the microphone OFF rather than just ignoring it, so the head-locked MIC ON
        /// badge goes too and truthfully shows that the mic isn't listening. Going back to Microphone
        /// restarts only a mic that one of them stopped: one that never started (permission denied)
        /// isn't asked for again here.
        /// </summary>
        public void SetDetection(DetectionMode newDetection)
        {
            detection = newDetection;

            if (midiInput != null)
            {
                if (detection == DetectionMode.Midi) midiInput.StartInput();
                else midiInput.StopInput();
            }

            if (audioDetector == null) return;

            if (detection != DetectionMode.Microphone)
            {
                if (audioDetector.IsRunning)
                {
                    audioDetector.StopDetection();
                    _micStoppedByMode = true;
                    Debug.Log($"[NoteEvaluator] {detection}: microphone switched off.");
                }
            }
            else if (_micStoppedByMode)
            {
                _micStoppedByMode = false;
                Debug.Log("[NoteEvaluator] Back to Microphone: restarting the microphone.");
                audioDetector.StartDetection();
            }
        }

        // ----------------------------------------------------------------
        // Detection handlers — one call per keystroke
        // ----------------------------------------------------------------

        // Each source counts only in its own mode. Switching modes stops the other source, but the
        // mic can still be started elsewhere (its permission callback), and a note must never be
        // judged twice — once heard by the mic and once reported by the keyboard.
        private void HandleMicNoteOn(int detectedMidi)
        {
            if (detection == DetectionMode.Microphone) HandleNoteOn(detectedMidi);
        }

        private void HandleMidiNoteOn(int pressedMidi)
        {
            if (detection == DetectionMode.Midi) HandleNoteOn(pressedMidi);
        }

        private void HandleNoteOn(int detectedMidi)
        {
            // Free Play promises that nothing is judged. (The source handlers above already filter by
            // mode; this keeps that promise for any future caller too.)
            if (!IsListening || !IsControllerReady()) return;

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
            // Nothing can play a note "on time" in Free Play, so nothing can miss one either: without
            // this every note would flash grey as it passed.
            if (!IsListening || !IsControllerReady()) return;

            float now = fallingNotesController.CurrentTime;
            var notes = fallingNotesController.Song.notes;

            for (int i = 0; i < notes.Count; i++)
            {
                if (_judgedIndices.Contains(i)) continue;

                var note = notes[i];
                float deadline = note.startTime + hitWindowSeconds + missGraceSeconds;
                if (now > deadline) MarkMissed(i, note);
            }
        }

        private void MarkMissed(int index, PianoNote note)
        {
            _judgedIndices.Add(index);
            NotesMissed++;
            CurrentStreak = 0;
            FlashNote(note.midiNumber, missedColor);

            if (logEvents)
                Debug.Log($"[NoteEvaluator] MISS {NoteUtils.GetNoteName(note.midiNumber)}");

            OnNoteMissed?.Invoke(note.midiNumber);
        }

        // ----------------------------------------------------------------
        // Song-complete check
        // ----------------------------------------------------------------

        private void CheckSongComplete()
        {
            if (_songCompleteFired) return;
            if (!IsControllerFinished() || fallingNotesController.Song == null) return;

            var notes = fallingNotesController.Song.notes;
            if (notes.Count == 0) return;

            // Playback is over, so a note that is still unjudged can never be played now: count it
            // missed. Waiting for its miss deadline would wait forever, because the clock stops at the
            // end — a song whose last note leaves the keys inside hitWindow + missGrace (a note shorter
            // than that, or one outside the keyboard's range) never completed, so no end-of-song
            // board appeared and, with the playback bar gone, the player had no way out.
            // Free Play judges nothing, so it has nothing to mark — but it still fires the event
            // below: its "Song Complete!" board is the way back to the menu all the same.
            if (IsListening)
            {
                for (int i = 0; i < notes.Count; i++)
                    if (!_judgedIndices.Contains(i)) MarkMissed(i, notes[i]);
            }

            _songCompleteFired = true;
            if (logEvents)
                Debug.Log($"[NoteEvaluator] DONE — score {Score}, " +
                          $"{NotesCorrect}/{TotalNotes} correct ({AccuracyPercent:F1}%)");

            OnSongComplete?.Invoke(Score, NotesCorrect, NotesIncorrect, NotesMissed);
        }

        // ----------------------------------------------------------------
        // Practice wait-for-note gate — freezes playback on the current note(s)
        // ----------------------------------------------------------------

        /// <summary>
        /// Practice mode only: holds the playback clock on a note once it reaches the hit line and
        /// releases it the instant the note is played, so the song waits for the learner instead of
        /// scrolling past. Runs every frame and always writes
        /// <see cref="FallingNotesController.HoldClock"/> (true = hold, false = run) so the flag can
        /// never get stuck. Assessment mode never holds — it is a real-time graded run.
        ///
        /// Holds while ANY unjudged, on-keyboard note has already reached the hit line
        /// (startTime &lt;= CurrentTime): that naturally waits for every note of a chord and steps to the
        /// next note the moment the current one is judged. Out-of-range notes (no physical key to play)
        /// are skipped so they can never soft-lock the song — they fall through to the normal
        /// missed-note path instead.
        ///
        /// A wait can also run out, and then the song moves on without the note(s): always in Free
        /// Play, where nothing can hear the note, and with the Microphone as a stop-gap, so a note the
        /// detector fails to hear can't stall the song. See <see cref="WaitLimitSeconds"/>.
        /// </summary>
        private void UpdateWaitGate()
        {
            if (fallingNotesController == null) return;

            if (mode != SessionMode.Practice || !IsControllerReady())
            {
                fallingNotesController.HoldClock = false;
                return;
            }

            float now = fallingNotesController.CurrentTime;
            var notes = fallingNotesController.Song.notes;

            bool hold = false;
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].startTime > now) break;   // time-sorted: nothing beyond here has arrived yet
                if (_judgedIndices.Contains(i)) continue;
                if (!IsPlayableNote(notes[i].midiNumber)) continue; // no key to play here — don't wait on it
                hold = true;
                break;
            }

            // Time each wait, and start again from zero at the next one: with the mic a wait also ends
            // when the note is played, and the next note must not inherit the time that one used.
            // Paused time doesn't count — a pause clears IsPlaying, which returns above first.
            if (!hold)
            {
                _waitSeconds = 0f;
            }
            else
            {
                _waitSeconds += Time.deltaTime;
                if (_waitSeconds >= WaitLimitSeconds())
                {
                    ReleaseArrivedNotes(now);
                    hold = false;
                }
            }

            fallingNotesController.HoldClock = hold;
        }

        /// <summary>
        /// How long one Practice wait may last before the song moves on without the note(s).
        /// Free Play: <see cref="freePlayHoldSeconds"/>, always — nothing can hear the note, so a fixed
        /// time is all it can give. Microphone: <see cref="micPracticeTimeoutSeconds"/> (0 = no limit),
        /// a stop-gap so a note the detector fails to hear can't stall the song. MIDI: no limit — the
        /// keyboard can't fail to report a key, so a note still at the line hasn't been played yet.
        /// </summary>
        private float WaitLimitSeconds()
        {
            switch (detection)
            {
                case DetectionMode.FreePlay:
                    return freePlayHoldSeconds;
                case DetectionMode.Microphone:
                    return micPracticeTimeoutSeconds > 0f ? micPracticeTimeoutSeconds : float.PositiveInfinity;
                default:
                    return float.PositiveInfinity;
            }
        }

        /// <summary>
        /// The wait ran out: settle every note that has reached the line so the wait gate moves on to
        /// the next one. A chord's notes arrive together, so they share one wait. In Free Play they are
        /// marked done silently — no score, colour or event, since nothing was listening. Otherwise the
        /// detector never heard them, so they are missed, just like an unheard note in Assessment: grey
        /// flash, counted, <see cref="OnNoteMissed"/>.
        /// </summary>
        private void ReleaseArrivedNotes(float now)
        {
            var notes = fallingNotesController.Song.notes;
            string unheard = null;
            for (int i = 0; i < notes.Count && notes[i].startTime <= now; i++)
            {
                if (_judgedIndices.Contains(i)) continue;
                if (!IsListening)
                {
                    _judgedIndices.Add(i);
                    continue;
                }
                unheard = (unheard == null ? "" : unheard + " ") + NoteUtils.GetNoteName(notes[i].midiNumber);
                MarkMissed(i, notes[i]);
            }

            // Always logged: which notes the detector keeps failing to hear is what to fix next.
            if (unheard != null)
                Debug.Log($"[NoteEvaluator] Practice: {unheard} not heard in {_waitSeconds:F1} s — " +
                          "marked missed, moving on.");
            _waitSeconds = 0f;
        }

        /// <summary>True if the note can actually be played on the assigned keyboard. Notes outside the
        /// keyboard range never spawn and could never be pressed, so the wait gate must not block on them.</summary>
        private bool IsPlayableNote(int midiNumber)
        {
            var kb = fallingNotesController != null ? fallingNotesController.keyboard : null;
            return kb == null || kb.ContainsNote(midiNumber);
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
