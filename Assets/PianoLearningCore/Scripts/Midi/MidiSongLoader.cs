using System.IO;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Loads a Song from a .mid file path or from a TextAsset placed in the project.
    ///
    /// In Unity, you can:
    ///   - place a .mid file in `Assets/PianoLearningCore/Songs/` (or any folder) and drag it
    ///     onto a public TextAsset field, then call LoadFromTextAsset.
    ///   - put a .mid file in `Application.streamingAssetsPath` and call LoadFromStreamingAssets.
    ///   - load from any absolute path via LoadFromFile (handy in the editor).
    /// </summary>
    public static class MidiSongLoader
    {
        public static Song LoadFromFile(string absolutePath)
        {
            if (!File.Exists(absolutePath))
            {
                Debug.LogError($"[MidiSongLoader] File not found: {absolutePath}");
                return null;
            }
            byte[] bytes = File.ReadAllBytes(absolutePath);
            string title = Path.GetFileNameWithoutExtension(absolutePath);
            return LoadFromBytes(bytes, title);
        }

        public static Song LoadFromStreamingAssets(string relativePathInsideStreamingAssets)
        {
            string full = Path.Combine(Application.streamingAssetsPath, relativePathInsideStreamingAssets);
            return LoadFromFile(full);
        }

        public static Song LoadFromTextAsset(TextAsset asset)
        {
            if (asset == null)
            {
                Debug.LogError("[MidiSongLoader] TextAsset is null.");
                return null;
            }
            // Note: Unity imports unknown binary files as TextAsset *only if* the file has the .bytes
            // extension OR you've set its importer to "Default". For MIDI files, the safest approach
            // is to rename .mid -> .bytes, or use StreamingAssets/absolute path instead.
            return LoadFromBytes(asset.bytes, asset.name);
        }

        public static Song LoadFromBytes(byte[] bytes, string title)
        {
            var rawNotes = MidiFileParser.Parse(bytes, out _);

            var song = new Song { title = title };
            song.notes.Capacity = rawNotes.Count;

            for (int i = 0; i < rawNotes.Count; i++)
            {
                var rn = rawNotes[i];
                song.notes.Add(new PianoNote(
                    midiNumber: rn.midiNumber,
                    startTime:  rn.startSeconds,
                    duration:   Mathf.Max(0.02f, rn.durationSeconds),
                    velocity:   rn.velocity / 127f
                ));
            }

            song.SortByTime();
            return song;
        }
    }
}
