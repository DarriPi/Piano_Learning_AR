using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// The "+ Add Song" button on the song menu, plus the status strip that reports what
    /// happened. Pressing it hands over to <see cref="MidiImporter"/>, which opens the
    /// headset's file browser; when a .mid comes back the song list is rebuilt so the new
    /// song appears straight away.
    ///
    /// Like the other menu controllers this component lives on the canvas ROOT (so it keeps
    /// running while the panel is hidden) while its button and status strip are children of
    /// the PANEL, which means they disappear with the list and the "one laser at a time" rule
    /// is untouched — no extra panel is introduced.
    ///
    /// <see cref="EnsureFor"/> is the runtime self-heal, the same trick
    /// <see cref="SongListScroller.EnsureFor"/> uses: the shipping scene was saved before this
    /// feature existed, so the button is created on the fly rather than requiring the editor
    /// builder to be re-run.
    /// </summary>
    public class AddSongButton : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The song menu this button adds songs to. Auto-found on the same GameObject.")]
        public SongSelectionMenu menu;

        [Tooltip("The button that opens the file browser.")]
        public Button addButton;

        [Tooltip("Strip along the bottom of the panel that reports what happened. Hidden when idle.")]
        public GameObject statusRoot;

        [Tooltip("Label inside the status strip.")]
        public TMP_Text statusLabel;

        [Header("Behaviour")]
        [Tooltip("Seconds a status message stays on screen before it clears itself.")]
        public float statusSeconds = 6f;

        [Tooltip("Seconds a message raised by the importer itself stays up. Longer than a " +
                 "normal result line because it is an instruction the user has to act on.")]
        public float importerStatusSeconds = 25f;

        // Shared look with the rest of the menu.
        private static readonly Color ButtonBlue = new Color(0.18f, 0.40f, 0.62f, 1f);
        private static readonly Color StatusBackground = new Color(0.10f, 0.12f, 0.18f, 0.95f);

        private Coroutine _clearRoutine;

        private void Start()
        {
            if (menu == null) menu = GetComponent<SongSelectionMenu>();
            Wire(addButton, OnAddPressed);
            ShowStatus(null);
        }

        private void OnEnable() { MidiImporter.StatusMessage += ShowImporterMessage; }

        private void OnDisable() { MidiImporter.StatusMessage -= ShowImporterMessage; }

        // The importer raises these by itself, outside any single import — currently only the
        // controllers failing to wake up after the file browser. They stay up much longer than a
        // result line: they ask the user to do something, and in that state they can't click the
        // message away even if they wanted to.
        private void ShowImporterMessage(string message) => ShowStatus(message, importerStatusSeconds);

        /// <summary>Open the file browser and import whatever the user picks.</summary>
        public void OnAddPressed()
        {
            if (MidiImporter.IsBusy) return;

            ShowStatus("Pick a MIDI file...");
            MidiImporter.Import(result =>
            {
                ShowStatus(result.message);
                // Re-read the song folders rather than appending the single new entry: it
                // keeps a removed-then-re-added song from leaving a stale row behind, and it
                // also picks up a file that DID land even though its result went missing.
                if (menu != null) menu.RefreshSongs();
            });
        }

        /// <summary>Put a line on the status strip, or hide it when the message is empty.</summary>
        public void ShowStatus(string message) => ShowStatus(message, statusSeconds);

        /// <summary>As <see cref="ShowStatus(string)"/>, with an explicit time on screen.</summary>
        public void ShowStatus(string message, float seconds)
        {
            if (statusLabel != null) statusLabel.text = message ?? string.Empty;
            if (statusRoot != null) statusRoot.SetActive(!string.IsNullOrEmpty(message));

            if (_clearRoutine != null)
            {
                StopCoroutine(_clearRoutine);
                _clearRoutine = null;
            }
            // Only auto-clear while the object is actually running; a message set from Start
            // before the panel is shown would otherwise leak a coroutine.
            if (!string.IsNullOrEmpty(message) && isActiveAndEnabled)
            {
                _clearRoutine = StartCoroutine(ClearStatusAfterDelay(seconds));
            }
        }

        private IEnumerator ClearStatusAfterDelay(float seconds)
        {
            // Realtime: the playback bar can leave timeScale at 0, and a status message that
            // never cleared itself would sit on the panel for the rest of the session.
            yield return new WaitForSecondsRealtime(seconds);
            _clearRoutine = null;
            ShowStatus(null);
        }

        // ------------------------------------------------------------------ runtime self-heal

        /// <summary>
        /// Make sure the menu has an Add Song button and a status strip, building whatever is
        /// missing. Safe to call from every <see cref="SongSelectionMenu.Build"/>: once the
        /// pieces exist it just returns the component. Returns null if the menu has no panel.
        /// </summary>
        public static AddSongButton EnsureFor(SongSelectionMenu menu)
        {
            if (menu == null) return null;

            var panel = menu.panelRoot != null
                ? menu.panelRoot.GetComponent<RectTransform>()
                : null;
            // panelRoot defaults to the canvas root before Start runs, and the canvas root is
            // not a panel we should be parenting buttons to.
            if (panel == null || menu.panelRoot == menu.gameObject) return null;

            var adder = menu.GetComponent<AddSongButton>();
            if (adder == null) adder = menu.gameObject.AddComponent<AddSongButton>();
            if (adder.menu == null) adder.menu = menu;

            if (adder.addButton == null)
            {
                // Top-LEFT of the panel, mirroring the "Align" help button top-right. The whole
                // right-hand column is already taken by "Align", "Up" and "Down".
                adder.addButton = MakeButton("AddSongButton", "+ Add Song", panel,
                    new Vector2(0f, 1f), new Vector2(200f, 64f), new Vector2(16f, -16f), 24f);
                adder.addButton.onClick.AddListener(adder.OnAddPressed);
            }

            if (adder.statusRoot == null)
            {
                adder.statusLabel = MakeStatusStrip(panel, out GameObject strip);
                adder.statusRoot = strip;
                strip.SetActive(false);
            }

            return adder;
        }

        // Image+Button with a centred TMP label — runtime twin of the builder's CreateButton.
        private static Button MakeButton(string name, string label, RectTransform parent,
                                         Vector2 anchor, Vector2 size, Vector2 anchoredPosition,
                                         float fontSize)
        {
            var rt = NewUIChild(name, parent);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPosition;

            var img = rt.gameObject.AddComponent<Image>();
            img.color = ButtonBlue;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var labelRt = NewUIChild("Label", rt);
            Stretch(labelRt);
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return btn;
        }

        // A strip along the bottom of the panel. Created LAST so it draws over the song list
        // instead of needing the list to make room for it.
        private static TMP_Text MakeStatusStrip(RectTransform panel, out GameObject strip)
        {
            var rt = NewUIChild("AddSongStatus", panel);
            rt.SetAsLastSibling();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(800f, 44f);
            rt.anchoredPosition = new Vector2(0f, 6f);
            rt.gameObject.AddComponent<Image>().color = StatusBackground;

            var labelRt = NewUIChild("Label", rt);
            Stretch(labelRt);
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = string.Empty;
            tmp.fontSize = 24f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            strip = rt.gameObject;
            return tmp;
        }

        private static RectTransform NewUIChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Wire(Button b, UnityAction action)
        {
            if (b == null) return;
            b.onClick.RemoveListener(action); // idempotent across a re-Start
            b.onClick.AddListener(action);
        }
    }
}
