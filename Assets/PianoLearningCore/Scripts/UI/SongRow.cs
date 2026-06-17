using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One row in the <see cref="SongSelectionMenu"/>: a song title plus a Practice and an
    /// Assessment button. The menu clones a hidden template row and calls <see cref="Bind"/>
    /// to fill in the title and route the two buttons to a single selection callback.
    /// </summary>
    public class SongRow : MonoBehaviour
    {
        [Tooltip("Label that shows the song's display name.")]
        public TMP_Text label;

        [Tooltip("Starts the song in Practice mode (guidance on).")]
        public Button practiceButton;

        [Tooltip("Starts the song in Assessment mode (graded run).")]
        public Button assessmentButton;

        /// <summary>Fill the row with a song and route its two buttons to <paramref name="onChosen"/>.</summary>
        public void Bind(SongEntry entry, Action<SongEntry, SessionMode> onChosen)
        {
            if (label != null) label.text = entry.displayName;

            Wire(practiceButton, entry, SessionMode.Practice, onChosen);
            Wire(assessmentButton, entry, SessionMode.Assessment, onChosen);
        }

        private static void Wire(Button button, SongEntry entry, SessionMode mode,
                                 Action<SongEntry, SessionMode> onChosen)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onChosen?.Invoke(entry, mode));
        }
    }
}
