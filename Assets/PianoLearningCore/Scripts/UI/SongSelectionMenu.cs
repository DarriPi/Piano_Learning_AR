using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One selectable song in the menu: a friendly name plus the path to its .mid file.
    /// </summary>
    [System.Serializable]
    public class SongEntry
    {
        [Tooltip("Name shown on the row in the menu.")]
        public string displayName = "New Song";

        [Tooltip("Path to the .mid RELATIVE to Application.streamingAssetsPath.\n" +
                 "Example: \"Songs/twinkle-twinkle-little-star.mid\"\n" +
                 "(the file must live at Assets/StreamingAssets/Songs/...)")]
        public string streamingAssetsPath = "Songs/example.mid";
    }

    /// <summary>UnityEvent carrying the chosen <see cref="SessionMode"/> so it can be wired in the Inspector.</summary>
    [System.Serializable]
    public class SessionModeEvent : UnityEvent<SessionMode> { }

    /// <summary>
    /// Builds a world-space song-selection menu: one <see cref="SongRow"/> per
    /// <see cref="SongEntry"/>, each offering a Practice and an Assessment button. Selecting
    /// either asks the <see cref="SongLauncher"/> to load + play that song in that mode, fires
    /// <see cref="onModeSelected"/>, and (optionally) hides the menu panel.
    ///
    /// The buttons are plain uGUI Buttons, so this works with BOTH the Meta Interaction SDK
    /// ray pointer in VR and the mouse in the Editor Game view.
    ///
    /// We can't list StreamingAssets at runtime on Quest (the folder is sealed inside the
    /// APK), so the available songs are declared explicitly in the <see cref="songs"/> list.
    /// </summary>
    public class SongSelectionMenu : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The SongLauncher that actually loads + plays the chosen song. " +
                 "Set its 'Play On Start' to FALSE so the menu controls when playback begins.")]
        public SongLauncher launcher;

        [Tooltip("Parent the song rows are added under. Give it a Vertical Layout Group " +
                 "so the rows arrange themselves automatically.")]
        public Transform contentParent;

        [Tooltip("A single SongRow used as a template; it is cloned once per song and kept " +
                 "hidden. It needs a label plus a Practice and an Assessment button.")]
        public SongRow rowTemplate;

        [Tooltip("The object hidden when a song starts. Point this at the visual PANEL child " +
                 "(not the Canvas root) so this controller keeps running and can re-show it. " +
                 "Defaults to this GameObject if left empty.")]
        public GameObject panelRoot;

        [Header("Songs")]
        public List<SongEntry> songs = new List<SongEntry>();

        [Header("Behaviour")]
        [Tooltip("Hide the menu automatically when a song is selected.")]
        public bool hideOnSelect = true;

        [Header("Events")]
        [Tooltip("Fires with the chosen mode whenever a song is selected. The launcher already " +
                 "applies the mode to the NoteEvaluator; use this for extras like a HUD.")]
        public SessionModeEvent onModeSelected;

        private readonly List<GameObject> _spawnedRows = new List<GameObject>();

        private void Start()
        {
            if (panelRoot == null) panelRoot = gameObject;
            Build();
        }

        /// <summary>Rebuild the row list from the current <see cref="songs"/>.</summary>
        public void Build()
        {
            if (rowTemplate == null || contentParent == null)
            {
                Debug.LogError("[SongSelectionMenu] Assign 'rowTemplate' and 'contentParent' " +
                               "in the Inspector before building the menu.", this);
                return;
            }

            Clear();
            rowTemplate.gameObject.SetActive(false); // keep the raw template hidden

            // Give the row's mode buttons a real, full-height hit rectangle. The builder left the
            // row's HorizontalLayoutGroup with childForceExpandHeight = false while the Practice /
            // Assessment buttons carry no preferred height, which collapsed them to ~zero height:
            // invisible, AND impossible for the controller-laser ray to intersect (the ray tests
            // each button's RectTransform quad). Forcing full-height expansion here repairs it at
            // runtime, so a scene built before this fix works without re-running the builder — the
            // clones inherit the corrected layout.
            var rowHlg = rowTemplate.GetComponent<HorizontalLayoutGroup>();
            if (rowHlg != null)
            {
                rowHlg.childControlHeight = true;
                rowHlg.childForceExpandHeight = true;
            }

            foreach (var entry in songs)
            {
                if (entry == null) continue;

                var row = Instantiate(rowTemplate, contentParent);
                row.gameObject.SetActive(true);
                row.name = $"SongRow_{entry.displayName}";
                row.Bind(entry, Select);

                _spawnedRows.Add(row.gameObject);
            }
        }

        /// <summary>Load + play the given song in the given mode, then optionally hide the menu.</summary>
        public void Select(SongEntry entry, SessionMode mode)
        {
            if (entry == null) return;
            if (launcher == null)
            {
                Debug.LogError("[SongSelectionMenu] No SongLauncher assigned; can't play '" +
                               entry.displayName + "'.", this);
                return;
            }

            launcher.PlayStreamingAssetsSong(entry.streamingAssetsPath, entry.displayName, mode);
            onModeSelected?.Invoke(mode);
            if (hideOnSelect) Hide();
        }

        public void Show() { if (panelRoot != null) panelRoot.SetActive(true); }
        public void Hide() { if (panelRoot != null) panelRoot.SetActive(false); }

        private void Clear()
        {
            for (int i = 0; i < _spawnedRows.Count; i++)
            {
                if (_spawnedRows[i] != null) Destroy(_spawnedRows[i]);
            }
            _spawnedRows.Clear();
        }
    }
}
