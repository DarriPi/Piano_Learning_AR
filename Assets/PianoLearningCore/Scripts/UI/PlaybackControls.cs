using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// In-world playback controls for a song session — Pause/Resume, Restart, and tempo down/up —
    /// wired to the existing <see cref="FallingNotesController"/> (Play/Pause/Restart and the
    /// playbackSpeed field). This is step 2 of the agreed UI path, the companion to the score board.
    ///
    /// Visible only while a song session is active (a song is loaded and not yet finished), so it
    /// stays up across a Pause — it deliberately does NOT key off IsPlaying alone, or pausing would
    /// hide its own Resume button. It hides when the song finishes so the end-of-song board's pointer
    /// is the only laser on screen (the VR pointer drops a hidden panel's buttons, so two visible
    /// interactive panels would otherwise draw two lasers).
    ///
    /// Interactive, so it carries a <see cref="ControllerUIPointer"/> (added by the builder) and needs
    /// an EventSystem — same as the score board / song menu, and unlike the display-only HUD.
    ///
    /// Lifecycle mirrors the score board: put this on the Canvas ROOT (stays active) and point
    /// <see cref="panelRoot"/> at the visual child so the component keeps running while hidden.
    ///
    /// Build one with Tools &gt; Piano Learning &gt; Create Playback Controls.
    /// </summary>
    public class PlaybackControls : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [Tooltip("The controller these buttons drive (Play/Pause/Restart + playbackSpeed).")]
        public FallingNotesController controller;

        [Tooltip("Reset on Restart so a replay starts from a clean score. Auto-found.")]
        public NoteEvaluator evaluator;

        [Tooltip("Shown again when the player presses Stop, so they can pick another song. Auto-found.")]
        public SongSelectionMenu songMenu;

        [Header("Panel")]
        [Tooltip("The visual child shown during a song session and hidden otherwise. Point this at the " +
                 "PANEL child, NOT the Canvas root, so this controller keeps running. " +
                 "Defaults to this GameObject if left empty.")]
        public GameObject panelRoot;

        [Header("Buttons")]
        public Button pauseResumeButton;
        public Button restartButton;
        [Tooltip("Stops the song and returns to the song-selection menu.")]
        public Button stopButton;
        public Button tempoDownButton;
        public Button tempoUpButton;

        [Header("Labels")]
        [Tooltip("Label on the pause/resume button — toggles between 'Pause' and 'Resume'.")]
        public TMP_Text pauseResumeLabel;
        [Tooltip("Current tempo readout, e.g. \"1.0x\".")]
        public TMP_Text tempoLabel;

        [Header("Tempo")]
        [Tooltip("How much each -/+ press changes the playback speed.")]
        public float tempoStep = 0.25f;
        [Tooltip("Slowest tempo the buttons allow (the controller itself permits down to 0.1).")]
        public float tempoMin = 0.5f;
        [Tooltip("Fastest tempo the buttons allow (the controller itself permits up to 2).")]
        public float tempoMax = 1.5f;

        private bool _visible;
        private bool _lastPlaying;
        private bool _sessionEnded; // set by Stop; keeps the bar hidden until the next song plays

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            if (panelRoot == null) panelRoot = gameObject;
            AutoFindReferences();
        }

        private void Start()
        {
            // Late safety net for runtime-bootstrapped scenes (Start runs after every Awake).
            AutoFindReferences();
            Wire(pauseResumeButton, TogglePauseResume);
            Wire(restartButton, Restart);
            Wire(stopButton, StopToMenu);
            Wire(tempoDownButton, TempoDown);
            Wire(tempoUpButton, TempoUp);
            RefreshTempoLabel();
            RefreshPauseLabel(force: true);
            SetVisible(false); // hidden until a song session is active
        }

        private void Update()
        {
            // Show during an active session (loaded + not finished) so a Pause doesn't hide Resume,
            // and so the bar is gone at song-end when the Assessment score board (and its laser) takes
            // over. In Practice there IS no board to hand off to, so the bar stays up after the song
            // ends — keeping Restart/tempo reachable, and still only one laser on screen.
            // A fresh playback clears the Stop latch so the bar returns for the next song.
            if (controller != null && controller.IsPlaying) _sessionEnded = false;

            bool finished = controller != null && controller.IsFinished;
            bool practiceMode = evaluator != null && evaluator.mode == SessionMode.Practice;
            bool sessionActive = !_sessionEnded && controller != null && controller.Song != null
                                 && (!finished || practiceMode);
            if (sessionActive != _visible) SetVisible(sessionActive);

            // The play/pause state can change outside our button (song end, external Pause), so keep
            // the label honest — guarded so it only writes the string on an actual change.
            if (_visible) RefreshPauseLabel();
        }

        // ----------------------------------------------------------------
        // Button actions
        // ----------------------------------------------------------------

        public void TogglePauseResume()
        {
            if (controller == null) return;
            if (controller.IsPlaying) controller.Pause();
            else controller.Play();
            RefreshPauseLabel(force: true);
        }

        /// <summary>Reset the score and replay the song from the top (mirrors the board's Play Again).</summary>
        public void Restart()
        {
            if (evaluator != null) evaluator.ResetScore();
            if (controller != null) controller.Restart();
            RefreshPauseLabel(force: true);
        }

        /// <summary>Stop the song entirely and return to the song-selection menu to pick another.</summary>
        public void StopToMenu()
        {
            if (controller != null) controller.Stop();
            _sessionEnded = true;      // keep the bar hidden until the next song actually plays
            SetVisible(false);
            if (songMenu != null) songMenu.Show(); // back to the song list
        }

        public void TempoDown() => SetTempo(-tempoStep);
        public void TempoUp()   => SetTempo(+tempoStep);

        private void SetTempo(float delta)
        {
            if (controller == null) return;
            controller.playbackSpeed = Mathf.Clamp(controller.playbackSpeed + delta, tempoMin, tempoMax);
            RefreshTempoLabel();
        }

        // ----------------------------------------------------------------
        // Visibility / labels
        // ----------------------------------------------------------------

        public void Show() => SetVisible(true);
        public void Hide() => SetVisible(false);

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (panelRoot != null) panelRoot.SetActive(visible);
            if (visible) { RefreshTempoLabel(); RefreshPauseLabel(force: true); }
        }

        private void RefreshPauseLabel(bool force = false)
        {
            if (pauseResumeLabel == null || controller == null) return;
            bool playing = controller.IsPlaying;
            if (!force && playing == _lastPlaying) return;
            _lastPlaying = playing;
            pauseResumeLabel.text = playing ? "Pause" : "Resume";
        }

        private void RefreshTempoLabel()
        {
            if (tempoLabel == null || controller == null) return;
            tempoLabel.text = $"{controller.playbackSpeed:0.0}x";
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static void Wire(Button b, UnityAction action)
        {
            if (b == null) return;
            b.onClick.RemoveListener(action); // no-op first time; prevents a double-subscribe on re-Start
            b.onClick.AddListener(action);
        }

        private void AutoFindReferences()
        {
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            if (controller == null) controller = FindObjectOfType<FallingNotesController>();
            if (evaluator == null)  evaluator  = FindObjectOfType<NoteEvaluator>();
            if (songMenu == null)   songMenu   = FindObjectOfType<SongSelectionMenu>();
#pragma warning restore CS0618
        }
    }
}
