using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One row in the <see cref="SongSelectionMenu"/>: a song title plus a Practice and an
    /// Assessment button, and — for songs the user imported themselves — a small remove
    /// button. The menu clones a hidden template row and calls <see cref="Bind"/> to fill in
    /// the title and route the buttons to a single selection callback.
    /// </summary>
    public class SongRow : MonoBehaviour
    {
        [Tooltip("Label that shows the song's display name.")]
        public TMP_Text label;

        [Tooltip("Starts the song in Practice mode (guidance on).")]
        public Button practiceButton;

        [Tooltip("Starts the song in Assessment mode (graded run).")]
        public Button assessmentButton;

        [Tooltip("Deletes an imported song. Hidden on rows for songs that ship with the app.")]
        public Button removeButton;

        /// <summary>Fill the row with a song and route its buttons to the callbacks.</summary>
        /// <param name="entry">The song this row represents.</param>
        /// <param name="onChosen">Called with the mode when Practice or Assessment is pressed.</param>
        /// <param name="onRemove">Called when the remove button is pressed (user songs only).</param>
        public void Bind(SongEntry entry, Action<SongEntry, SessionMode> onChosen,
                         Action<SongEntry> onRemove = null)
        {
            if (label != null) label.text = entry.displayName;

            Wire(practiceButton, entry, SessionMode.Practice, onChosen);
            Wire(assessmentButton, entry, SessionMode.Assessment, onChosen);

            if (removeButton != null)
            {
                // Only imported songs can be removed — the built-in ones are inside the APK.
                // Deactivating it also takes it out of the row's layout AND out of reach of
                // the controller laser, which only ever considers active buttons.
                removeButton.gameObject.SetActive(entry.IsUserSong);
                removeButton.onClick.RemoveAllListeners();
                removeButton.onClick.AddListener(() => onRemove?.Invoke(entry));
            }
        }

        private static void Wire(Button button, SongEntry entry, SessionMode mode,
                                 Action<SongEntry, SessionMode> onChosen)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onChosen?.Invoke(entry, mode));
        }

        // ------------------------------------------------------------------ runtime self-heal

        /// <summary>
        /// Give the template row a remove button if it hasn't got one, so a scene saved before
        /// imported songs existed still gets it (same pattern as
        /// <see cref="SongListScroller.EnsureFor"/>). Called from
        /// <see cref="SongSelectionMenu.Build"/> BEFORE the row clones are made, so every
        /// clone inherits it. Rows built by SongSelectionMenuBuilder pass straight through.
        /// </summary>
        public static void EnsureRemoveButton(SongRow template)
        {
            if (template == null || template.removeButton != null) return;

            var rowRt = (RectTransform)template.transform;

            var rt = new GameObject("RemoveButton", typeof(RectTransform))
                .GetComponent<RectTransform>();
            rt.SetParent(rowRt, false);

            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.62f, 0.24f, 0.22f, 1f); // same red as the playback Stop button
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 56f;
            le.flexibleWidth = 0f;

            var labelRt = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
            labelRt.SetParent(rt, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = "X"; // plain ASCII — the default TMP atlas has no '×' or bin glyph
            tmp.fontSize = 26f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            template.removeButton = btn;
        }
    }
}
