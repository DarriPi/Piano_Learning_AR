using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// The songs the USER added, as opposed to the ones shipped inside the APK.
    ///
    /// Built-in songs live in StreamingAssets and are listed by a manifest, because that
    /// folder is sealed inside the APK and can't be enumerated (see SongSelectionMenu). User
    /// songs have the opposite problem and the opposite solution: they live in ordinary
    /// writable folders that we CAN enumerate with System.IO, so no manifest is involved.
    ///
    /// Two folders are scanned, both writable/readable with NO Android permission:
    ///   * <see cref="ImportRoot"/>  — Application.persistentDataPath/Songs. Where the
    ///     in-app file picker (<see cref="MidiImporter"/>) copies files to.
    ///   * <see cref="ExternalDropRoot"/> — the app's external files dir,
    ///     /sdcard/Android/data/&lt;package&gt;/files/Songs. Not written by the app, but it IS
    ///     reachable from a PC over USB, so a .mid can be dropped in there directly. That's
    ///     the fallback for getting songs on the headset without the picker.
    /// </summary>
    public static class UserSongLibrary
    {
        /// <summary>Sub-folder used inside both roots, so the layout matches StreamingAssets/Songs.</summary>
        public const string FolderName = "Songs";

        private static readonly string[] MidiExtensions = { ".mid", ".midi" };

        // Characters we allow in a stored filename. Anything else becomes '_' — imported
        // names come from other apps, so they can hold path separators and worse.
        private const string AllowedPunctuation = " ._()-";

        /// <summary>
        /// Where imported songs are written: internal app storage, always writable, never
        /// needs a permission, and cleared only when the app is uninstalled.
        /// </summary>
        public static string ImportRoot => Path.Combine(Application.persistentDataPath, FolderName);

        private static bool _externalRootResolved;
        private static string _externalRoot;

        /// <summary>
        /// Optional second folder: /sdcard/Android/data/&lt;package&gt;/files/Songs on Android,
        /// null everywhere else. Songs are only READ from here — a user (or adb push) puts
        /// them there. Resolved once and cached; never throws.
        /// </summary>
        public static string ExternalDropRoot
        {
            get
            {
                if (_externalRootResolved) return _externalRoot;
                _externalRootResolved = true;
                _externalRoot = ResolveExternalDropRoot();
                return _externalRoot;
            }
        }

        private static string ResolveExternalDropRoot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Same JNI style as QuestMicCapture: high-level AndroidJavaObject where it is easy,
            // raw AndroidJNI where it isn't, all wrapped so a failure returns null rather than
            // throwing.
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null) return null;

                    // getExternalFilesDir(null) gives the ROOT of the app's external files dir,
                    // and it creates the folder if it doesn't exist yet. It has to be called by
                    // hand: AndroidJavaObject builds the JNI signature from the RUNTIME type of
                    // each argument, so a null is always typed as java.lang.Object and the
                    // (Ljava/lang/String;) overload would never be found.
                    IntPtr getDir = AndroidJNI.GetMethodID(activity.GetRawClass(),
                        "getExternalFilesDir", "(Ljava/lang/String;)Ljava/io/File;");
                    if (getDir == IntPtr.Zero) return null;

                    var args = new jvalue[1];
                    args[0].l = IntPtr.Zero; // the null argument
                    IntPtr fileObj = AndroidJNI.CallObjectMethod(activity.GetRawObject(), getDir, args);
                    if (fileObj == IntPtr.Zero) return null;

                    try
                    {
                        string basePath = CallStringMethod(fileObj, "getAbsolutePath");
                        return string.IsNullOrEmpty(basePath)
                            ? null
                            : Path.Combine(basePath, FolderName);
                    }
                    finally { AndroidJNI.DeleteLocalRef(fileObj); }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UserSongLibrary] Couldn't resolve the external drop folder: " + e.Message);
                return null;
            }
#else
            return null;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Call a no-argument String-returning method on a raw jobject.</summary>
        private static string CallStringMethod(IntPtr obj, string methodName)
        {
            IntPtr cls = AndroidJNI.GetObjectClass(obj);
            try
            {
                IntPtr method = AndroidJNI.GetMethodID(cls, methodName, "()Ljava/lang/String;");
                if (method == IntPtr.Zero) return null;

                IntPtr result = AndroidJNI.CallObjectMethod(obj, method, new jvalue[0]);
                if (result == IntPtr.Zero) return null;

                try { return AndroidJNI.GetStringUTFChars(result); }
                finally { AndroidJNI.DeleteLocalRef(result); }
            }
            finally { AndroidJNI.DeleteLocalRef(cls); }
        }
#endif

        /// <summary>Both folders to scan, skipping the ones that don't exist on this platform.</summary>
        public static IEnumerable<string> Roots
        {
            get
            {
                yield return ImportRoot;
                string external = ExternalDropRoot;
                if (!string.IsNullOrEmpty(external)) yield return external;
            }
        }

        /// <summary>
        /// Every .mid / .midi in the user folders, as menu entries with
        /// <see cref="SongEntry.absolutePath"/> set. Sorted by display name so the list is
        /// stable between runs. Missing folders are simply skipped (nothing imported yet).
        /// </summary>
        public static List<SongEntry> Enumerate()
        {
            var entries = new List<SongEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in Roots)
            {
                string[] files;
                try
                {
                    if (!Directory.Exists(root)) continue;
                    files = Directory.GetFiles(root);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UserSongLibrary] Couldn't read '{root}': {e.Message}");
                    continue;
                }

                foreach (string file in files)
                {
                    if (!IsMidiFile(file)) continue;

                    string fileName = Path.GetFileName(file);
                    if (!seen.Add(fileName)) continue; // same name in both roots: keep the import copy

                    entries.Add(new SongEntry
                    {
                        displayName = SongSelectionMenu.DisplayNameFromFileName(fileName),
                        absolutePath = file
                    });
                }
            }

            entries.Sort((a, b) => string.Compare(a.displayName, b.displayName,
                                                  StringComparison.OrdinalIgnoreCase));
            return entries;
        }

        public static bool IsMidiFile(string path)
        {
            string ext = Path.GetExtension(path);
            for (int i = 0; i < MidiExtensions.Length; i++)
            {
                if (string.Equals(ext, MidiExtensions[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// A safe, unique path inside <see cref="ImportRoot"/> for a file the user picked.
        /// The name is sanitised, forced to a .mid extension, and given a " (2)" suffix if
        /// something with that name is already there. Creates the folder if needed.
        ///
        /// The Java picker does the same thing on its side (it names the file before Unity
        /// ever sees it); this is used by the editor import path.
        /// </summary>
        public static string UniqueDestinationPath(string fileName)
        {
            Directory.CreateDirectory(ImportRoot);

            string safe = SanitizeFileName(fileName);
            string stem = Path.GetFileNameWithoutExtension(safe);
            string ext = Path.GetExtension(safe);
            if (!IsMidiFile(safe)) ext = ".mid";

            string candidate = Path.Combine(ImportRoot, stem + ext);
            for (int i = 2; File.Exists(candidate) && i < 1000; i++)
            {
                candidate = Path.Combine(ImportRoot, $"{stem} ({i}){ext}");
            }
            return candidate;
        }

        /// <summary>Strip anything that isn't a letter, digit or simple punctuation, and cap the length.</summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "song.mid";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || AllowedPunctuation.IndexOf(c) >= 0;
                sb.Append(ok ? c : '_');
            }

            string cleaned = sb.ToString().Trim(' ', '.');
            if (cleaned.Length == 0) return "song.mid";
            if (cleaned.Length > 80) cleaned = cleaned.Substring(0, 80);
            return cleaned;
        }

        /// <summary>Delete an imported song. Only paths inside the user folders are touched.</summary>
        public static bool Delete(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return false;

            if (!IsInUserFolder(absolutePath))
            {
                Debug.LogWarning("[UserSongLibrary] Refusing to delete a file outside the user " +
                                 "song folders: " + absolutePath);
                return false;
            }

            try
            {
                if (File.Exists(absolutePath)) File.Delete(absolutePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UserSongLibrary] Couldn't delete '{absolutePath}': {e.Message}");
                return false;
            }
        }

        /// <summary>True when the path sits directly inside one of the user song folders.</summary>
        public static bool IsInUserFolder(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return false;

            string dir;
            try { dir = Path.GetFullPath(Path.GetDirectoryName(absolutePath) ?? string.Empty); }
            catch (Exception) { return false; }

            foreach (string root in Roots)
            {
                string full;
                try { full = Path.GetFullPath(root); }
                catch (Exception) { continue; }

                if (string.Equals(dir.TrimEnd('/', '\\'), full.TrimEnd('/', '\\'),
                                  StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
