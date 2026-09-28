using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One selectable song in the menu: a friendly name plus the path to its .mid file.
    /// The path is EITHER inside StreamingAssets (a song shipped with the app) OR an
    /// absolute path (a song the user added themselves — see <see cref="UserSongLibrary"/>).
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

        [Tooltip("Full path on disk, set for songs the user imported with the Add Song button. " +
                 "When this is filled in it wins over streamingAssetsPath, and the row gets a " +
                 "remove button. Leave empty for songs that ship inside the app.")]
        public string absolutePath = "";

        /// <summary>True for a song the user added, so it can be played from disk and removed.</summary>
        public bool IsUserSong => !string.IsNullOrEmpty(absolutePath);
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
    /// We can't LIST StreamingAssets at runtime on Quest (the folder is sealed inside the
    /// APK), but we CAN fetch a known file from it — so the songs are discovered from
    /// <see cref="ManifestPath"/>, which the editor-side SongManifestGenerator keeps in sync
    /// with the .mid files in Assets/StreamingAssets/Songs. Dropping a .mid into that folder
    /// is all it takes to add a song; the Inspector <see cref="songs"/> list is only the
    /// fallback for when the manifest can't be read.
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
        [Tooltip("FALLBACK ONLY — normally replaced at runtime by the songs discovered via " +
                 "the StreamingAssets song manifest, plus any the user imported. Only used " +
                 "when that manifest can't be read. Tools > Piano Learning > Rebuild Song " +
                 "Manifest regenerates it.")]
        public List<SongEntry> songs = new List<SongEntry>();

        // The songs that ship inside the APK, kept separate from the user's imports so the
        // list can be rebuilt after an Add/Remove without re-fetching the manifest.
        private List<SongEntry> _builtInSongs;

        /// <summary>
        /// Manifest inside StreamingAssets listing every song, one .mid filename per line.
        /// SongManifestGenerator (editor) keeps it in sync with the Songs folder; we fetch it
        /// with UnityWebRequest because Android can't enumerate StreamingAssets directly.
        /// </summary>
        public const string ManifestPath = "Songs/manifest.txt";

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
            StartCoroutine(DiscoverSongsThenBuild());
        }

        /// <summary>
        /// Read the built-in songs from the manifest, then build the rows (adding whatever
        /// the user has imported). Falls back to the Inspector list (with a warning) if the
        /// manifest is missing or empty, so an old build without one still gets a menu. This
        /// component sits on the canvas ROOT (only panelRoot gets hidden), so the coroutine
        /// survives the Help panel hiding the menu at startup.
        /// </summary>
        private IEnumerator DiscoverSongsThenBuild()
        {
            string full = Path.Combine(Application.streamingAssetsPath, ManifestPath);

            using (var request = UnityWebRequest.Get(full))
            {
                yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = request.result == UnityWebRequest.Result.Success;
#else
                bool ok = !request.isHttpError && !request.isNetworkError;
#endif
                var discovered = ok ? ParseManifest(request.downloadHandler.text) : null;

                if (discovered != null && discovered.Count > 0)
                {
                    _builtInSongs = discovered;
                }
                else
                {
                    _builtInSongs = new List<SongEntry>(songs);
                    Debug.LogWarning(
                        "[SongSelectionMenu] Couldn't get songs from manifest '" + full + "' (" +
                        (ok ? "it lists no songs" : request.error) + "); falling back to the " +
                        songs.Count + " song(s) declared in the Inspector. Regenerate it via " +
                        "Tools > Piano Learning > Rebuild Song Manifest.", this);
                }
            }

            RefreshSongs();
        }

        /// <summary>
        /// Rebuild the list from the built-in songs plus everything in the user's song
        /// folders. Call this after a song is imported or removed — it re-reads the folders
        /// but not the manifest, which never changes while the app runs.
        /// </summary>
        public void RefreshSongs()
        {
            songs = _builtInSongs != null ? new List<SongEntry>(_builtInSongs) : new List<SongEntry>();
            songs.AddRange(UserSongLibrary.Enumerate());
            Build();
        }

        /// <summary>Delete a song the user imported and rebuild the list. Built-ins are ignored.</summary>
        public void RemoveUserSong(SongEntry entry)
        {
            if (entry == null || !entry.IsUserSong) return;

            UserSongLibrary.Delete(entry.absolutePath);
            RefreshSongs();
        }

        /// <summary>One .mid filename per line; blanks and '#' comment lines are skipped.</summary>
        private static List<SongEntry> ParseManifest(string text)
        {
            var entries = new List<SongEntry>();
            if (string.IsNullOrEmpty(text)) return entries;

            foreach (string rawLine in text.Split('\n'))
            {
                // Trim whitespace plus a stray UTF-8 BOM (not in Trim()'s whitespace set).
                string fileName = rawLine.Trim('﻿', ' ', '\t', '\r', '\n');
                if (fileName.Length == 0 || fileName[0] == '#') continue;

                entries.Add(new SongEntry
                {
                    displayName = DisplayNameFromFileName(fileName),
                    streamingAssetsPath = "Songs/" + fileName
                });
            }
            return entries;
        }

        // Words kept lowercase inside a derived title (still capitalised as the first word).
        private static readonly HashSet<string> SmallTitleWords = new HashSet<string>
        {
            "a", "an", "and", "at", "for", "in", "of", "on", "or", "the", "to"
        };

        /// <summary>"mary-had-a-little-lamb.mid" → "Mary Had a Little Lamb".</summary>
        internal static string DisplayNameFromFileName(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string[] words = stem.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return stem;

            var sb = new StringBuilder(stem.Length);
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i].ToLowerInvariant();
                if (i > 0) sb.Append(' ');
                if (i > 0 && SmallTitleWords.Contains(word)) sb.Append(word);
                else sb.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
            }
            return sb.ToString();
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

            // Make the list scrollable so it can hold more songs than fit the panel (without
            // this, extra rows squash to fit). Like the layout repair above, this self-heals a
            // scene built before scrolling existed: EnsureFor wraps the flat Content in a
            // ScrollRect + masked viewport and adds Up/Down buttons at runtime. Scenes made by
            // the current builder already have the structure and pass through untouched.
            var scroller = SongListScroller.EnsureFor(this);

            // Same self-heal idea for the two pieces of the "add your own song" feature: the
            // template's remove button, and the Add Song button + status strip on the panel.
            SongRow.EnsureRemoveButton(rowTemplate);
            AddSongButton.EnsureFor(this);

            foreach (var entry in songs)
            {
                if (entry == null) continue;

                var row = Instantiate(rowTemplate, contentParent);
                row.gameObject.SetActive(true);
                row.name = $"SongRow_{entry.displayName}";
                row.Bind(entry, Select, RemoveUserSong);

                _spawnedRows.Add(row.gameObject);
            }

            if (scroller != null) scroller.SnapToTop(); // a fresh list starts at the top
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

            // A song the user imported lives in a normal folder we can read with File IO; a
            // built-in one is sealed inside the APK and has to go through UnityWebRequest.
            if (entry.IsUserSong)
                launcher.PlayFileSong(entry.absolutePath, entry.displayName, mode);
            else
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
