using TMPro;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Visible count-in before every song: "Starting in 5 … 4 … 1", then "GO!" as the first notes reach
    /// the keys. It shows the pre-roll <see cref="FallingNotesController"/> already runs — a real-time
    /// <see cref="FallingNotesController.startDelay"/> for putting the controllers down — which until now
    /// only went to the Console, so in the headset it was several seconds of dead air.
    ///
    /// Driven purely by the controller's clock, so nothing in the song-start path has to call it: it
    /// appears for every song in both Practice and Assessment, starts over on Restart / Play Again, hides
    /// while paused and after Stop, and never shows at all when startDelay is 0.
    ///
    /// Placed over the KEYBOARD rather than fixed in the room, because the keyboard is only positioned at
    /// runtime (head-relative on start, then calibrated): each time the count-in appears it moves to
    /// <see cref="keyboardOffset"/> from the middle of the keys — raised, and far enough up the note
    /// highway to sit above the first notes as they come in during the last seconds of the pre-roll,
    /// not in front of them. It follows by pose instead of being parented, because the calibrators
    /// stretch the keyboard's X scale to fit the real piano and a child canvas would be stretched too.
    /// Display-only — no GraphicRaycaster, raycastTarget off — so the controller laser passes through.
    ///
    /// Silent on purpose: the microphone is listening for piano notes and would hear a beep.
    ///
    /// Put this on the Canvas ROOT and point <see cref="panelRoot"/> at the visual child, so the
    /// component keeps running while the count-in is hidden and can re-show it.
    ///
    /// Build one with Tools &gt; Piano Learning &gt; Create Start Countdown, which wires every reference.
    /// </summary>
    public class StartCountdownDisplay : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [Tooltip("Whose pre-roll (Start Delay) is counted down. The count-in is placed over its keyboard.")]
        public FallingNotesController controller;

        [Header("Panel")]
        [Tooltip("The visual child shown during the count-in and hidden otherwise. Point this at the " +
                 "PANEL child, NOT the Canvas root, so this controller keeps running. " +
                 "Defaults to this GameObject if left empty.")]
        public GameObject panelRoot;

        [Header("Text fields")]
        [Tooltip("The big readout: the seconds left, then GO!.")]
        public TMP_Text numberText;

        [Tooltip("The small line above the number (\"Starting in\"). Hidden while GO! shows.")]
        public TMP_Text captionText;

        [Header("Placement")]
        [Tooltip("Position relative to the keyboard, in metres: x = sideways from the middle of the keys, " +
                 "y = height above the keys, z = how far up the note highway from the hit line. The default " +
                 "floats above the far half of the highway, clear of the incoming notes. Tune in-headset " +
                 "if it covers anything.")]
        public Vector3 keyboardOffset = new Vector3(0f, 0.35f, 1.2f);

        [Header("GO!")]
        [Tooltip("Seconds GO! stays up once the song starts.")]
        public float goSeconds = 0.7f;

        [Tooltip("Colour of the seconds.")]
        public Color numberColor = Color.white;

        [Tooltip("Colour of GO!.")]
        public Color goColor = new Color(0.25f, 0.90f, 0.45f, 1f);

        // What the panel shows: the seconds left (1 and up), Go, or Hidden.
        private const int Hidden = -1;
        private const int Go = 0;

        private int _shown = Hidden;
        private bool _wasCountingDown;
        private float _goUntil; // unscaled time at which GO! comes down

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
            // Late safety net, as in LiveScoreHud: Start runs after every Awake.
            AutoFindReferences();
            if (panelRoot != null) panelRoot.SetActive(false); // hidden until a song counts in
        }

        private void Update()
        {
            // The controller has no events for this, so poll its clock — Display only touches the
            // panel on a CHANGE, i.e. once a second, not every frame.
            bool countingDown = controller != null && controller.IsCountingDown;
            int state = Hidden;

            if (countingDown)
            {
                state = controller.CountdownSecondsLeft;
            }
            else if (controller != null && controller.IsPlaying)
            {
                // The pre-roll just ran out with the song still playing: that's the start.
                if (_wasCountingDown) _goUntil = Time.unscaledTime + goSeconds;
                if (Time.unscaledTime < _goUntil) state = Go;
            }
            else
            {
                _goUntil = 0f; // paused or stopped: don't bring a stale GO! back on Resume
            }

            _wasCountingDown = countingDown;
            Display(state);
        }

        // ----------------------------------------------------------------
        // Placement
        // ----------------------------------------------------------------

        /// <summary>
        /// Move the count-in to <see cref="keyboardOffset"/> from the middle of the keys, upright and
        /// facing the player. Runs each time the count-in appears, so it follows any re-calibration
        /// between songs; public so the builder can preview the spot.
        /// </summary>
        public void PlaceOverKeyboard()
        {
            KeyboardLayout keyboard = controller != null ? controller.keyboard : null;
            if (keyboard == null)
            {
                Debug.LogWarning("[StartCountdownDisplay] No keyboard to place the count-in over — it stays " +
                                 "where it was built. Assign a FallingNotesController with a keyboard.", this);
                return;
            }

            // Level versions of the keyboard's axes, so the text stays upright: forward runs up the note
            // highway (away from the player), right runs along the keys.
            Transform kb = keyboard.transform;
            Vector3 forward = Vector3.ProjectOnPlane(kb.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            // TransformPoint picks up the calibrated X scale, so this stays the middle of the keys.
            KeyboardConfig cfg = keyboard.config;
            Vector3 keysMiddle = kb.TransformPoint(cfg != null
                ? new Vector3(cfg.TotalWidth * 0.5f, 0f, cfg.hitLineZ)
                : Vector3.zero);

            // A uGUI canvas is read from its -Z side, so pointing +Z up the highway faces the player.
            transform.SetPositionAndRotation(
                keysMiddle + right * keyboardOffset.x + Vector3.up * keyboardOffset.y + forward * keyboardOffset.z,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private void Display(int state)
        {
            if (state == _shown) return;
            bool appearing = _shown == Hidden;
            _shown = state;

            if (state == Hidden)
            {
                if (panelRoot != null) panelRoot.SetActive(false);
                return;
            }

            if (numberText != null)
            {
                numberText.text = state == Go ? "GO!" : state.ToString();
                numberText.color = state == Go ? goColor : numberColor;
            }
            if (captionText != null) captionText.gameObject.SetActive(state != Go);

            if (appearing)
            {
                PlaceOverKeyboard();
                if (panelRoot != null) panelRoot.SetActive(true);
            }
        }

        private void AutoFindReferences()
        {
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            if (controller == null) controller = FindObjectOfType<FallingNotesController>();
#pragma warning restore CS0618
        }
    }
}
