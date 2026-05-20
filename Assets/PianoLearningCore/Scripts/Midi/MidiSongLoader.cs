using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace PianoLearningCore
{
    /// <summary>
    /// Loads a Song from a .mid file. Supports several sources.
    ///
    /// IMPORTANT for Quest / Android builds:
    ///   On Android, StreamingAssets is packed INSIDE the APK and cannot be read with
    ///   File.ReadAllBytes. You MUST use the coroutine LoadFromStreamingAssetsAsync.
    ///   The synchronous LoadFromStreamingAssets still works on desktop / editor.
    ///
    /// In Unity you can:
    ///   - drop a .mid file in Assets/StreamingAssets/Songs/ and call LoadFromStreamingAssetsAsync
    ///     (recommended for cross-platform support).
    ///   - rename a .mid to .bytes, drag it onto a TextAsset field, and call LoadFromTextAsset.
    ///   - in the editor only, point absolute paths via LoadFromFile.
    /// </summary>
    public static class MidiSongLoader
    {
        // ----- Editor / desktop synchronous loaders -----

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

        /// <summary>
        /// Synchronous StreamingAssets loader. WORKS ON DESKTOP / EDITOR ONLY.
        /// On Android (Quest), this will fail because StreamingAssets is inside the APK.
        /// Use LoadFromStreamingAssetsAsync instead for cross-platform support.
        /// </summary>
        public static Song LoadFromStreamingAssets(string relativePathInsideStreamingAssets)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Debug.LogError("[MidiSongLoader] LoadFromStreamingAssets does not work on Android. " +
                           "Use LoadFromStreamingAssetsAsync (coroutine) instead.");
            return null;
#else
            string full = Path.Combine(Application.streamingAssetsPath, relativePathInsideStreamingAssets);
            return LoadFromFile(full);
#endif
        }

        // ----- Cross-platform async loader (works on Android) -----

        /// <summary>
        /// Coroutine that loads a .mid from StreamingAssets using UnityWebRequest.
        /// Works on EVERY platform (desktop, editor, Android, iOS, WebGL).
        /// Call from a MonoBehaviour with StartCoroutine.
        /// </summary>
        /// <param name="relativePathInsideStreamingAssets">e.g. "Songs/MyFile.mid"</param>
        /// <param name="onLoaded">Callback invoked with the Song (or null on failure).</param>
        public static IEnumerator LoadFromStreamingAssetsAsync(
            string relativePathInsideStreamingAssets, Action<Song> onLoaded)
        {
            string full = Path.Combine(Application.streamingAssetsPath, relativePathInsideStreamingAssets);

            using (UnityWebRequest request = UnityWebRequest.Get(full))
            {
                yield return request.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = request.result == UnityWebRequest.Result.Success;
#else
                bool ok = !request.isHttpError && !request.isNetworkError;
#endif
                if (!ok)
                {
                    Debug.LogError($"[MidiSongLoader] Failed to read '{full}': {request.error}");
                    onLoaded?.Invoke(null);
                    yield break;
                }

                byte[] bytes = request.downloadHandler.data;
                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogError($"[MidiSongLoader] Empty file: {full}");
                    onLoaded?.Invoke(null);
                    yield break;
                }

                string title = Path.GetFileNameWithoutExtension(relativePathInsideStreamingAssets);
                Song song = LoadFromBytes(bytes, title);
                onLoaded?.Invoke(song);
            }
        }

        // ----- TextAsset loader -----

        public static Song LoadFromTextAsset(TextAsset asset)
        {
            if (asset == null)
            {
                Debug.LogError("[MidiSongLoader] TextAsset is null.");
                return null;
            }
            // Note: Unity only imports unknown binary files as TextAsset if the file has the
            // .bytes extension. Rename your .mid to .bytes, drop it anywhere in Assets/,
            // then drag it onto the TextAsset field.
            return LoadFromBytes(asset.bytes, asset.name);
        }

        // ----- Shared byte-to-Song conversion -----

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
