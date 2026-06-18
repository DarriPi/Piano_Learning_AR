using TMPro;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Live in-play HUD — a small, peripheral world-space readout of the running accuracy, streak and
    /// score WHILE a song is playing. Complements <see cref="ScoreBoardSummary"/> (the final result
    /// screen): this one updates continuously so the player gets feedback during the run, then hides
    /// itself the moment the song ends so the score board can take over.
    ///
    /// Designed NOT to block the player's view of the keys or the falling notes: it is deliberately
    /// compact, sits above/aside the play area via the builder's transform, and uses a low-opacity
    /// backing so MR passthrough shows through. It is display-only (no buttons), so unlike the score
    /// board / song menu it needs no <see cref="ControllerUIPointer"/> or EventSystem.
    ///
    /// Numbers all come straight off <see cref="NoteEvaluator"/>; they are refreshed on each note
    /// EVENT rather than every frame, to avoid per-frame string allocation (protects the 90 FPS budget).
    ///
    /// Lifecycle mirrors the score board: put this on the Canvas ROOT (which stays active) and point
    /// <see cref="panelRoot"/> at the visual child, so the component keeps running while hidden and can
    /// re-show itself when the next song starts.
    ///
    /// Build one with Tools &gt; Piano Learning &gt; Create Live Score HUD, which wires every reference.
    /// </summary>
    public class LiveScoreHud : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [Tooltip("Supplies the live score/accuracy/streak and the per-note events.")]
        public NoteEvaluator evaluator;

        [Tooltip("Polled for IsPlaying so the HUD shows only while a song is actually running.")]
        public FallingNotesController controller;

        [Header("Panel")]
        [Tooltip("The visual child shown while a song plays and hidden otherwise. Point this at the " +
                 "PANEL child, NOT the Canvas root, so this controller keeps running. " +
                 "Defaults to this GameObject if left empty.")]
        public GameObject panelRoot;

        [Header("Text fields")]
        [Tooltip("Running accuracy over notes played so far, e.g. \"92%\".")] public TMP_Text accuracyText;
        [Tooltip("Current consecutive-correct streak, e.g. \"x7\".")]          public TMP_Text streakText;
        [Tooltip("Running total score.")]                                      public TMP_Text scoreText;

        private bool _subscribed;
        private bool _visible;

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            if (panelRoot == null) panelRoot = gameObject;
            AutoFindReferences();
        }

        // Subscribe in OnEnable/OnDisable so we never leak the handlers. References are resolved in
        // Awake, which Unity always runs before the first OnEnable.
        private void OnEnable()  => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Start()
        {
            // Late safety net: if a runtime bootstrap created the evaluator/controller after our Awake,
            // pick them up now (Start runs after every Awake) and ensure we're subscribed.
            AutoFindReferences();
            Subscribe();
            Refresh();
            SetVisible(false); // stays hidden until a song is actually playing
        }

        private void Update()
        {
            // IsPlaying has no event, so visibility is the one thing we poll. Guarded so we only
            // toggle on a CHANGE — no per-frame SetActive or string formatting.
            bool shouldShow = controller != null && controller.IsPlaying;
            if (shouldShow != _visible) SetVisible(shouldShow);
        }

        // ----------------------------------------------------------------
        // Event handling — one refresh per judged note
        // ----------------------------------------------------------------

        private void HandleCorrect(int midi, int points) => Refresh();
        private void HandleIncorrect(int midi) => Refresh();
        private void HandleMissed(int midi) => Refresh();

        /// <summary>Pull the current live values off the evaluator into the text fields.</summary>
        public void Refresh()
        {
            if (evaluator == null) return;
            if (accuracyText != null) accuracyText.text = $"{evaluator.LiveAccuracyPercent:F0}%";
            if (streakText != null)   streakText.text   = $"x{evaluator.CurrentStreak}";
            if (scoreText != null)    scoreText.text    = evaluator.Score.ToString("N0");
        }

        // ----------------------------------------------------------------
        // Visibility
        // ----------------------------------------------------------------

        public void Show() => SetVisible(true);
        public void Hide() => SetVisible(false);

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (panelRoot != null) panelRoot.SetActive(visible);
            if (visible) Refresh(); // make sure it's current the instant it appears
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private void Subscribe()
        {
            if (_subscribed || evaluator == null) return;
            evaluator.OnNoteCorrect   += HandleCorrect;
            evaluator.OnNoteIncorrect += HandleIncorrect;
            evaluator.OnNoteMissed    += HandleMissed;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || evaluator == null) return;
            evaluator.OnNoteCorrect   -= HandleCorrect;
            evaluator.OnNoteIncorrect -= HandleIncorrect;
            evaluator.OnNoteMissed    -= HandleMissed;
            _subscribed = false;
        }

        private void AutoFindReferences()
        {
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            if (evaluator == null)  evaluator  = FindObjectOfType<NoteEvaluator>();
            if (controller == null) controller = FindObjectOfType<FallingNotesController>();
#pragma warning restore CS0618
        }
    }
}
