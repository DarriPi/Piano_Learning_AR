using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Keeps StreamingAssets/Songs/manifest.txt listing every .mid in that folder (one
    /// filename per line, sorted). <see cref="SongSelectionMenu"/> fetches it at runtime to
    /// build the song list — Android can't enumerate StreamingAssets (sealed inside the APK)
    /// but can fetch this known file, so dropping a .mid into the folder is all it takes to
    /// add a song to the menu. Name files kebab-case ("ode-to-joy.mid"): the filename becomes
    /// the display name, and spaces in StreamingAssets URLs are best avoided.
    ///
    /// Regenerates automatically when a .mid under the folder is imported/deleted/moved, and
    /// again before every build so an APK can never ship a stale manifest.
    /// Manual: Tools > Piano Learning > Rebuild Song Manifest.
    /// </summary>
    public class SongManifestGenerator : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private const string SongsFolder = "Assets/StreamingAssets/Songs";
        private const string ManifestAssetPath =
            "Assets/StreamingAssets/" + SongSelectionMenu.ManifestPath;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Rebuild();

        [MenuItem("Tools/Piano Learning/Rebuild Song Manifest")]
        public static void RebuildMenuItem()
        {
            Rebuild();
            if (!File.Exists(ManifestAssetPath)) return;
            AssetDatabase.ImportAsset(ManifestAssetPath);
            Debug.Log("[SongManifestGenerator] Rebuilt " + ManifestAssetPath + ":\n" +
                      File.ReadAllText(ManifestAssetPath));
        }

        // Fires after EVERY import batch; regenerate only when a song file was touched. The
        // manifest write can itself trigger one more batch, but manifest.txt isn't a song
        // file (and Rebuild skips identical content), so it can't loop.
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(movedTo).Concat(movedFrom).Any(IsSongFile))
                Rebuild();
        }

        private static bool IsSongFile(string assetPath)
        {
            return assetPath.StartsWith(SongsFolder + "/", StringComparison.OrdinalIgnoreCase)
                && (assetPath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)
                 || assetPath.EndsWith(".midi", StringComparison.OrdinalIgnoreCase));
        }

        private static void Rebuild()
        {
            if (!Directory.Exists(SongsFolder))
            {
                Debug.LogWarning("[SongManifestGenerator] '" + SongsFolder + "' does not " +
                                 "exist; no manifest written.");
                return;
            }

            var names = Directory.GetFiles(SongsFolder)
                .Select(Path.GetFileName)
                .Where(n => n.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)
                         || n.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string text = string.Join("\n", names) + "\n";
            if (File.Exists(ManifestAssetPath) && File.ReadAllText(ManifestAssetPath) == text)
                return; // unchanged — don't dirty the asset

            // No BOM: the first manifest line must parse clean at runtime.
            File.WriteAllText(ManifestAssetPath, text, new UTF8Encoding(false));
        }
    }
}
