using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// Drives the startup Help / instructions panel, which shares the SongSelectionCanvas with the
    /// song list. Two panels, one <see cref="ControllerUIPointer"/>: only ONE panel is ever active,
    /// so exactly one laser is drawn — the same single-laser rule the score board and playback bar
    /// follow (see the note in <see cref="PlaybackControls"/>).
    ///
    /// Flow: on Start the Help panel is shown and the song list is hidden. The Help panel's Close
    /// button hides Help and reveals the list; the list's "?" button brings Help back. Nothing plays
    /// until the user picks a song from the list, so the app never launches straight into a song
    /// (pair this with <c>SongLauncher.playOnStart = false</c>).
    ///
    /// Lives on the Canvas ROOT (which stays active) next to <see cref="SongSelectionMenu"/>; it
    /// toggles the PANEL children, never the canvas, so the pointer keeps running.
    ///
    /// Built automatically by Tools &gt; Piano Learning &gt; Create Song Selection Menu.
    /// </summary>
    public class HelpPanel : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [Tooltip("The Help panel visual child shown at startup and via the '?' button.")]
        public GameObject helpPanelRoot;

        [Tooltip("The song list this coordinates with. Its panel is hidden while Help is up. " +
                 "Auto-found on this GameObject if left empty.")]
        public SongSelectionMenu menu;

        [Header("Buttons")]
        [Tooltip("On the Help panel — closes Help and reveals the song list.")]
        public Button closeButton;

        [Tooltip("The '?' on the song list — reopens Help.")]
        public Button helpButton;

        [Header("Calibration confirm (optional)")]
        [Tooltip("If on, the keyboard calibrator's confirm (A button) also closes Help, so " +
                 "'done positioning' and 'dismiss instructions' are one action.")]
        public bool closeOnCalibrationConfirm = true;

        [Tooltip("Calibrator whose confirm closes Help. Auto-found if left empty.")]
        public XRControllerCalibrator calibrator;

        [Tooltip("Menu input driver. Disabled while Help is open (positioning mode), enabled when " +
                 "Help closes (menu mode), so the thumbstick is never claimed by both. Auto-found.")]
        public ControllerMenuNavigator navigator;

        private void Start()
        {
            AutoFind();
            Wire(closeButton, CloseHelp);
            Wire(helpButton, ShowHelp);

            if (closeOnCalibrationConfirm && calibrator != null)
                calibrator.OnCalibrationConfirmed.AddListener(CloseHelp);

            // Startup: instructions first, list hidden. Nothing plays until a song is chosen.
            ShowHelp();
        }

        private void OnDestroy()
        {
            if (calibrator != null)
                calibrator.OnCalibrationConfirmed.RemoveListener(CloseHelp);
        }

        /// <summary>Show the Help panel and hide the song list.</summary>
        public void ShowHelp()
        {
            if (helpPanelRoot != null) helpPanelRoot.SetActive(true);
            if (menu != null) menu.Hide();
            // Positioning mode: the thumbstick drives the keyboard calibrator, not the menu.
            if (calibrator != null) calibrator.SetActive(true);
            if (navigator != null) navigator.SetActive(false);
        }

        /// <summary>
        /// Hide Help and reveal the song list. No-op when Help isn't currently showing, so a stray
        /// calibrator A-press during a song can't pop the menu back up mid-play.
        /// </summary>
        public void CloseHelp()
        {
            if (helpPanelRoot == null || !helpPanelRoot.activeSelf) return;
            helpPanelRoot.SetActive(false);
            if (menu != null) menu.Show();
            // Menu mode: free the thumbstick for the navigator; lock the keyboard so it can't drift.
            if (calibrator != null) calibrator.SetActive(false);
            if (navigator != null) navigator.SetActive(true);
        }

        private void AutoFind()
        {
            if (menu == null) menu = GetComponent<SongSelectionMenu>();
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            if (menu == null) menu = FindObjectOfType<SongSelectionMenu>();
            if (calibrator == null) calibrator = FindObjectOfType<XRControllerCalibrator>();
            if (navigator == null) navigator = FindObjectOfType<ControllerMenuNavigator>();
#pragma warning restore CS0618

            if (helpPanelRoot == null)
                Debug.LogWarning("[HelpPanel] 'helpPanelRoot' is not assigned — Help cannot show.", this);
        }

        private static void Wire(Button b, UnityAction action)
        {
            if (b == null) return;
            b.onClick.RemoveListener(action); // idempotent across a re-Start
            b.onClick.AddListener(action);
        }
    }
}
