using System.Collections;
using System.IO;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Small MonoBehaviour that loads a MIDI file and tells a FallingNotesController to play it.
    /// Designed for the "post-bootstrap" workflow where you've manually set up the Keyboard and
    /// FallingNotesController GameObjects in the scene.
    ///
    /// Drop this on its own empty GameObject, drag your FallingNotesController into the
    /// reference field, choose a song source, and press Play.
    ///
    /// Cross-platform: uses UnityWebRequest under the hood for StreamingAssets so it works
    /// on Quest (Android) builds, not just the editor.
    ///
    /// Also exposes Play / Pause / Restart / Stop methods so you can hook UI buttons to them.
    /// </summary>
    public class SongLauncher : MonoBehaviour
    {
        public enum SongSource
        {
            BuiltInDemo,
            StreamingAssetsFile,
            AbsoluteFile,
            TextAssetBytes
        }

        [Header("Where to send the loaded song")]
        [Tooltip("The FallingNotesController in the scene. Drag the GameObject that has the " +
                 "FallingNotesController component into this slot.")]
        public FallingNotesController controller;

        [Tooltip("Optional. The NoteEvaluator to put into the chosen Practice/Assessment mode " +
                 "(and reset) when a song is launched from the menu. Auto-found if left empty.")]
        public NoteEvaluator noteEvaluator;

        [Header("Song source")]
        public SongSource source = SongSource.BuiltInDemo;

        [Tooltip("Used when source = StreamingAssetsFile. Path RELATIVE to Application.streamingAssetsPath.\n" +
                 "Example: \"Songs/MaryHadALittleLamb.mid\"\n" +
                 "(Place the .mid file at Assets/StreamingAssets/Songs/MaryHadALittleLamb.mid)")]
        public string streamingAssetsRelativePath = "Songs/example.mid";

        [Tooltip("Used when source = AbsoluteFile. Full path on disk. EDITOR / DESKTOP ONLY (not for Quest builds).")]
        public string absoluteFilePath = "";

        [Tooltip("Used when source = TextAssetBytes. Drag a *.bytes asset here (rename your .mid -> .bytes).")]
        public TextAsset midiAsset;

        [Header("Behaviour")]
        [Tooltip("Load and start the song automatically when the scene starts.")]
        public bool playOnStart = true;

        [Tooltip("Print a one-line summary of the loaded song to the Console.")]
        public bool logSummary = true;

        /// <summary>The Practice/Assessment mode of the most recent launch.</summary>
        public SessionMode CurrentMode { get; private set; } = SessionMode.Practice;

        // One-shot friendly title applied to the next loaded song (set by PlayStreamingAssetsSong).
        private string _pendingTitleOverride;

        // One-shot: reset the NoteEvaluator's score after the next song finishes loading.
        private bool _resetEvaluatorOnLoad;

        private void Start()
        {
            if (playOnStart) LoadAndPlay();
        }

        // -------- Public API (hook these to UI buttons) --------

        /// <summary>Reload the song from the configured source and start playback.</summary>
        public void LoadAndPlay()
        {
            if (!ValidateController()) return;
            // Run as a coroutine so StreamingAssetsFile (which uses UnityWebRequest on Android)
            // can yield until the file is loaded.
            StartCoroutine(LoadAndPlayCoroutine());
        }

        public void Play()    { if (ValidateController()) controller.Play(); }
        public void Pause()   { if (ValidateController()) controller.Pause(); }
        public void Stop()    { if (ValidateController()) controller.Stop(); }
        public void Restart() { if (ValidateController()) controller.Restart(); }

        /// <summary>
        /// Load + play a specific StreamingAssets song at runtime, in the given mode. Used by
        /// SongSelectionMenu so a menu selection works regardless of the Inspector source.
        /// </summary>
        /// <param name="relativePath">Path relative to StreamingAssets, e.g. "Songs/twinkle.mid".</param>
        /// <param name="displayTitle">Optional friendly title shown instead of the .mid filename.</param>
        /// <param name="mode">Practice (guidance) or Assessment (graded).</param>
        public void PlayStreamingAssetsSong(string relativePath, string displayTitle = null,
                                            SessionMode mode = SessionMode.Practice)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                Debug.LogError("[SongLauncher] PlayStreamingAssetsSong called with an empty path.", this);
                return;
            }
            source = SongSource.StreamingAssetsFile;
            streamingAssetsRelativePath = relativePath;
            _pendingTitleOverride = displayTitle;
            _resetEvaluatorOnLoad = true;
            SetMode(mode);
            LoadAndPlay();
        }

        /// <summary>Record the Practice/Assessment mode and push it to the NoteEvaluator.</summary>
        public void SetMode(SessionMode mode)
        {
            CurrentMode = mode;
            var evaluator = EnsureEvaluator();
            if (evaluator != null) evaluator.SetMode(mode);
        }

        private NoteEvaluator EnsureEvaluator()
        {
#pragma warning disable CS0618 // FindObjectOfType works on every Unity version
            if (noteEvaluator == null) noteEvaluator = FindObjectOfType<NoteEvaluator>();
#pragma warning restore CS0618
            return noteEvaluator;
        }

        // -------- Internal --------

        private IEnumerator LoadAndPlayCoroutine()
        {
            Song song = null;

            switch (source)
            {
                case SongSource.BuiltInDemo:
                    song = Song.BuildCMajorScaleDemo();
                    break;

                case SongSource.StreamingAssetsFile:
                    // Cross-platform: works on Android via UnityWebRequest.
                    yield return MidiSongLoader.LoadFromStreamingAssetsAsync(
                        streamingAssetsRelativePath,
                        s => song = s);
                    break;

                case SongSource.AbsoluteFile:
                    if (string.IsNullOrEmpty(absoluteFilePath) || !File.Exists(absoluteFilePath))
                    {
                        Debug.LogError($"[SongLauncher] Absolute file not found: {absoluteFilePath}", this);
                    }
                    else
                    {
                        song = MidiSongLoader.LoadFromFile(absoluteFilePath);
                    }
                    break;

                case SongSource.TextAssetBytes:
                    if (midiAsset == null)
                    {
                        Debug.LogError("[SongLauncher] No TextAsset assigned. Drag your .bytes file into 'Midi Asset'.", this);
                    }
                    else
                    {
                        song = MidiSongLoader.LoadFromTextAsset(midiAsset);
                    }
                    break;
            }

            if (song == null || song.notes.Count == 0)
            {
                Debug.LogWarning("[SongLauncher] No notes loaded. Falling back to built-in C-major demo.");
                song = Song.BuildCMajorScaleDemo();
            }

            // Apply an optional friendly title (e.g. from the song-selection menu) so the
            // displayed title can differ from the .mid filename. One-shot: cleared after use.
            if (!string.IsNullOrEmpty(_pendingTitleOverride) && song != null)
                song.title = _pendingTitleOverride;
            _pendingTitleOverride = null;

            if (logSummary)
            {
                Debug.Log($"[SongLauncher] Loaded \"{song.title}\" — {song.notes.Count} notes, " +
                          $"~{song.DurationSeconds:F1}s long.");
            }

            controller.LoadSong(song); // controller's autoStart will Play() if set

            // When launched from the song menu, reset the evaluator's score now that the song
            // (and therefore its note count) is actually loaded — so accuracy isn't stuck at 0.
            if (_resetEvaluatorOnLoad)
            {
                _resetEvaluatorOnLoad = false;
                var evaluator = EnsureEvaluator();
                if (evaluator != null) evaluator.ResetScore();
            }
        }

        private bool ValidateController()
        {
            if (controller == null)
            {
                Debug.LogError("[SongLauncher] No FallingNotesController assigned. " +
                               "Drag the FallingNotesController GameObject into this script's " +
                               "'Controller' field in the Inspector.", this);
                return false;
            }
            return true;
        }
    }
}
