using System.IO;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Small MonoBehaviour that loads a MIDI file and tells a FallingNotesController to play it.
    /// Designed for the "post-bootstrap" workflow where you've manually set up the Keyboard and
    /// FallingNotesController GameObjects in the scene (see Section 8 of SETUP_GUIDE.md).
    ///
    /// Drop this on its own empty GameObject, drag your FallingNotesController into the
    /// reference field, choose a song source, and press Play.
    ///
    /// Also exposes Play / Pause / Restart / Stop methods so you can hook UI buttons to them
    /// later without writing more code.
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

        [Tooltip("Used when source = AbsoluteFile. Full path on disk.")]
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

            Song song = LoadConfiguredSong();
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

        /// <summary>Resume after a pause. Does NOT reload the song.</summary>
        public void Play()
        {
            if (ValidateController()) controller.Play();
        }

        /// <summary>Pause without losing playback position.</summary>
        public void Pause()
        {
            if (ValidateController()) controller.Pause();
        }

        /// <summary>Stop playback and clear all on-screen notes.</summary>
        public void Stop()
        {
            if (ValidateController()) controller.Stop();
        }

        /// <summary>Stop and play again from the beginning of the currently loaded song.</summary>
        public void Restart()
        {
            if (ValidateController()) controller.Restart();
        }

        // -------- Helpers --------

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

        private Song LoadConfiguredSong()
        {
            switch (source)
            {
                case SongSource.BuiltInDemo:
                    return Song.BuildCMajorScaleDemo();

                case SongSource.StreamingAssetsFile:
                {
                    string path = Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath);
                    if (!File.Exists(path))
                    {
                        Debug.LogError($"[SongLauncher] StreamingAssets file not found:\n{path}\n" +
                                       "Make sure the .mid file exists at " +
                                       $"Assets/StreamingAssets/{streamingAssetsRelativePath}", this);
                        return null;
                    }
                    return MidiSongLoader.LoadFromFile(path);
                }

                case SongSource.AbsoluteFile:
                {
                    if (string.IsNullOrEmpty(absoluteFilePath) || !File.Exists(absoluteFilePath))
                    {
                        Debug.LogError($"[SongLauncher] Absolute file not found: {absoluteFilePath}", this);
                        return null;
                    }
                    return MidiSongLoader.LoadFromFile(absoluteFilePath);
                }

                case SongSource.TextAssetBytes:
                {
                    if (midiAsset == null)
                    {
                        Debug.LogError("[SongLauncher] No TextAsset assigned. Drag your .bytes file into 'Midi Asset'.", this);
                        return null;
                    }
                    return MidiSongLoader.LoadFromTextAsset(midiAsset);
                }
            }
            return null;
        }
    }
}
