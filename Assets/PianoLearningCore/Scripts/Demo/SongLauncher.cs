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

            if (logSummary)
            {
                Debug.Log($"[SongLauncher] Loaded \"{song.title}\" — {song.notes.Count} notes, " +
                          $"~{song.DurationSeconds:F1}s long.");
            }

            controller.LoadSong(song); // controller's autoStart will Play() if set
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
