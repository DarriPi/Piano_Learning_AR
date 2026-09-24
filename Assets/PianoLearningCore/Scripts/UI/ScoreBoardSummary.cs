using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// End-of-song results panel — the "score board summary". Listens to
    /// <see cref="NoteEvaluator.OnSongComplete"/> and reveals a world-space canvas showing the
    /// final score, accuracy, a letter grade, the correct / incorrect / missed breakdown, and the
    /// best note streak. Offers <b>Play Again</b> (replay the same song) and <b>Close</b>.
    ///
    /// All the numbers already exist on <see cref="NoteEvaluator"/> (Score, AccuracyPercent,
    /// NotesCorrect/Incorrect/Missed, BestStreak, TotalNotes) — this just displays them, so the
    /// Assessment mode finally becomes visible to the player.
    ///
    /// Lifecycle mirrors the song-selection menu: put this component on the Canvas ROOT
    /// (which stays active) and point <see cref="panelRoot"/> at the visual PANEL child, so the
    /// component keeps running while the panel is hidden and can re-show itself on song complete.
    ///
    /// Build one with Tools &gt; Piano Learning &gt; Create Score Board Summary, which wires every
    /// reference for you.
    /// </summary>
    public class ScoreBoardSummary : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [Tooltip("Supplies the score/accuracy/streak and the OnSongComplete event.")]
        public NoteEvaluator evaluator;

        [Tooltip("Used for the song title and for the Play Again replay.")]
        public FallingNotesController controller;

        [Tooltip("Shown again when the player presses Close, so they can pick another song. Auto-found.")]
        public SongSelectionMenu songMenu;

        [Header("Panel")]
        [Tooltip("The visual panel revealed on song-complete and hidden otherwise. Point this at the " +
                 "PANEL child, NOT the Canvas root, so this controller keeps running. " +
                 "Defaults to this GameObject if left empty.")]
        public GameObject panelRoot;

        [Header("Text fields")]
        [Tooltip("\"<song> (<mode>)\"")]              public TMP_Text subtitleText;
        [Tooltip("Headline accuracy, e.g. \"88.9%\"")] public TMP_Text accuracyText;
        [Tooltip("Letter grade, e.g. \"Grade A\"")]    public TMP_Text gradeText;
        [Tooltip("Total points scored.")]              public TMP_Text scoreText;
        [Tooltip("Correct count (shown as \"x / total\").")] public TMP_Text correctText;
        [Tooltip("Wrong-note count.")]                 public TMP_Text incorrectText;
        [Tooltip("Missed-note count.")]                public TMP_Text missedText;
        [Tooltip("Longest run of consecutive correct notes.")] public TMP_Text streakText;

        [Header("Reporting")]
        [Tooltip("Hide the 'Incorrect' and 'Best streak' rows.\n\n" +
                 "Both are driven by detector note-on events, which currently also fire on room " +
                 "noise — so neither measures the player. A phantom note inflates the wrong-note " +
                 "count and resets the streak through no fault of theirs, which would mislead a " +
                 "usability-study participant. 'Correct' and 'Missed' are counted against the " +
                 "song's own notes, so noise cannot invent them, and wrong notes are just " +
                 "TotalNotes minus Correct.\n\n" +
                 "Turn this off once the false-positive rate is inside the proposal's 1% criterion.")]
        public bool hideUnreliableStats = true;

        [Header("Buttons")]
        public Button playAgainButton;
        public Button closeButton;

        [Header("Events")]
        [Tooltip("Fired when Close is pressed. Wire it to e.g. a song menu's Show() to return to song " +
                 "selection — kept as an event so the score board doesn't hard-depend on that menu.")]
        public UnityEvent onClosed;

        [Header("Grade thresholds & colours")]
        [Tooltip("Accuracy (%) at or above which the grade is shown in the 'good' colour.")]
        public float goodAccuracy = 80f;
        [Tooltip("Accuracy (%) at or above which the grade is shown in the 'ok' colour (below = 'bad').")]
        public float okAccuracy = 60f;
        public Color goodColor = new Color(0.20f, 0.90f, 0.40f, 1f); // green
        public Color okColor   = new Color(0.95f, 0.80f, 0.25f, 1f); // amber
        public Color badColor  = new Color(0.95f, 0.35f, 0.30f, 1f); // red

        private bool _subscribed;

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            if (panelRoot == null) panelRoot = gameObject;
            AutoFindReferences();
        }

        // Subscribe in OnEnable/OnDisable so we never leak the handler. References are resolved in
        // Awake, which Unity always runs before the first OnEnable.
        private void OnEnable()  => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Start()
        {
            // Late safety net: if a runtime bootstrap created the evaluator/controller after our
            // Awake, pick them up now (Start runs after every Awake) and ensure we're subscribed.
            AutoFindReferences();
            Subscribe();

            if (playAgainButton != null)
            {
                playAgainButton.onClick.RemoveListener(PlayAgain);
                playAgainButton.onClick.AddListener(PlayAgain);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            Hide(); // results only appear once a song finishes
        }

        // ----------------------------------------------------------------
        // Event handling
        // ----------------------------------------------------------------

        private void HandleSongComplete(int score, int correct, int incorrect, int missed)
        {
            // Assessment only: Practice is a guided, unscored mode, so it shows no results board.
            if (evaluator != null && evaluator.mode != SessionMode.Assessment) return;
            Populate();
            Show();
        }

        /// <summary>Fill every text field from the current <see cref="NoteEvaluator"/> state.</summary>
        public void Populate()
        {
            if (evaluator == null) return;

            string songTitle = controller != null && controller.Song != null
                ? controller.Song.title
                : "Song";
            if (subtitleText != null)
                subtitleText.text = $"{songTitle}  ({evaluator.mode})";

            float accuracy = evaluator.AccuracyPercent;
            string grade = GradeFor(accuracy, out Color gradeColor);

            if (accuracyText != null)
            {
                accuracyText.text = $"{accuracy:F1}%";
                accuracyText.color = gradeColor;
            }
            if (gradeText != null)
            {
                gradeText.text = $"Grade {grade}";
                gradeText.color = gradeColor;
            }

            if (scoreText != null)     scoreText.text     = evaluator.Score.ToString("N0");
            if (correctText != null)   correctText.text   = $"{evaluator.NotesCorrect} / {evaluator.TotalNotes}";
            if (missedText != null)    missedText.text    = evaluator.NotesMissed.ToString();

            // "Incorrect" and "Best streak" are deliberately hidden. Both are driven by OnNoteOn,
            // which the detector also fires for room noise, so on current hardware neither is a
            // measurement of the PLAYER: a phantom note inflates the wrong-note count and breaks
            // the streak through no fault of theirs. Showing a number we cannot stand behind would
            // mislead a study participant. Correct and Missed stay — those are counted against the
            // song's own notes, so noise cannot invent them — and wrong notes are simply
            // TotalNotes minus Correct if anyone wants them.
            if (hideUnreliableStats)
            {
                HideStatRow(incorrectText);
                HideStatRow(streakText);
            }
            else
            {
                if (incorrectText != null) incorrectText.text = evaluator.NotesIncorrect.ToString();
                if (streakText != null)    streakText.text    = evaluator.BestStreak.ToString();
            }
        }

        /// <summary>
        /// Hide a whole stat row. The builder nests each row as [Label | Value], so the value's
        /// parent is the row — deactivating it takes the label with it and lets the vertical
        /// layout close the gap.
        /// </summary>
        private static void HideStatRow(TMP_Text value)
        {
            if (value == null) return;
            GameObject row = value.transform.parent != null
                ? value.transform.parent.gameObject
                : value.gameObject;
            if (row.activeSelf) row.SetActive(false);
        }

        // ----------------------------------------------------------------
        // Button actions
        // ----------------------------------------------------------------

        /// <summary>Reset the score and replay the same song from the top.</summary>
        public void PlayAgain()
        {
            if (evaluator != null) evaluator.ResetScore();
            if (controller != null) controller.Restart();
            Hide();
        }

        /// <summary>
        /// Dismiss the summary and return to the song-selection menu so the player can pick another
        /// song. Also fires <see cref="onClosed"/> for any extra Inspector-wired behaviour. Mirrors
        /// <see cref="PlaybackControls.StopToMenu"/>. Deliberately does NOT call
        /// <c>controller.Stop()</c>: the song has already finished (IsFinished stays true), which is
        /// what keeps the playback bar hidden — the next song selection resets everything via
        /// <see cref="FallingNotesController.LoadSong"/>.
        /// </summary>
        public void Close()
        {
            Hide();
            if (songMenu != null) songMenu.Show(); // back to the song list
            onClosed?.Invoke();
        }

        public void Show() { if (panelRoot != null) panelRoot.SetActive(true); }
        public void Hide() { if (panelRoot != null) panelRoot.SetActive(false); }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        // A..F letter grade plus a colour band, driven by the two accuracy thresholds.
        private string GradeFor(float accuracy, out Color color)
        {
            if (accuracy >= 90f)          { color = goodColor; return "A"; }
            if (accuracy >= goodAccuracy) { color = goodColor; return "B"; }
            if (accuracy >= 70f)          { color = okColor;   return "C"; }
            if (accuracy >= okAccuracy)   { color = okColor;   return "D"; }
            color = badColor; return "F";
        }

        private void Subscribe()
        {
            if (_subscribed || evaluator == null) return;
            evaluator.OnSongComplete += HandleSongComplete;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || evaluator == null) return;
            evaluator.OnSongComplete -= HandleSongComplete;
            _subscribed = false;
        }

        private void AutoFindReferences()
        {
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            if (evaluator == null)  evaluator  = FindObjectOfType<NoteEvaluator>();
            if (controller == null) controller = FindObjectOfType<FallingNotesController>();
            if (songMenu == null)   songMenu   = FindObjectOfType<SongSelectionMenu>();
#pragma warning restore CS0618
        }
    }
}
