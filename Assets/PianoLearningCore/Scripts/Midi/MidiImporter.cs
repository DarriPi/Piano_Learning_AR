using System;
using System.IO;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>Outcome of one <see cref="MidiImporter.Import"/> attempt.</summary>
    public struct MidiImportResult
    {
        /// <summary>True only when a valid .mid ended up in the user song folder.</summary>
        public bool ok;

        /// <summary>One line to show the user — reason for failure, or the name that was added.</summary>
        public string message;

        /// <summary>The new menu entry, when <see cref="ok"/>.</summary>
        public SongEntry entry;
    }

    /// <summary>
    /// Lets the user add their own .mid file at runtime.
    ///
    /// On Quest this opens Android's system document picker (Storage Access Framework). SAF
    /// is used because it needs NO storage permission at all: the user's choice IS the grant,
    /// which is the only practical way to read /sdcard/Download under scoped storage on
    /// targetSdk 34. The picker can't be driven from C# alone though — Unity's
    /// UnityPlayerGameActivity doesn't override onActivityResult, so the result would be
    /// dropped. A small transparent helper Activity (MidiPickerActivity.java, in
    /// Assets/Plugins/Android) runs the picker and copies the bytes into the folder we hand it.
    ///
    /// That helper runs in its OWN PROCESS, because sharing one with the VR app is what left
    /// the app hidden and unclickable after an import. UnitySendMessage cannot cross a process
    /// boundary, so it leaves the answer in a file and <see cref="WaitForPickerResult"/> polls
    /// for it. Polling rather than a resume callback is deliberate twice over: Unity is paused
    /// for most of the pick, so a coroutine simply stops and continues where it left off when
    /// the app comes back; and if the app is never paused at all (a translucent helper doesn't
    /// always stop it) the same loop still finds the file.
    ///
    /// In the editor the same flow runs through EditorUtility.OpenFilePanel, so the whole
    /// feature can be tested on a PC without a headset.
    ///
    /// Whatever arrives is PARSED before it is accepted: MidiFileParser throws on SMF
    /// Format 2 and SMPTE timing, and a file that can't be read would otherwise leave a
    /// permanently broken row in the song menu.
    /// </summary>
    public class MidiImporter : MonoBehaviour
    {
        /// <summary>
        /// Name of the GameObject that hosts this component. It must survive the app being
        /// backgrounded behind the picker, hence DontDestroyOnLoad.
        /// </summary>
        public const string BridgeObjectName = "MidiImportBridge";

        /// <summary>
        /// File MidiPickerActivity leaves its answer in, inside
        /// <see cref="UserSongLibrary.ImportRoot"/>. Must match RESULT_FILE_NAME on the Java
        /// side. Dot-prefixed and not a .mid, so <see cref="UserSongLibrary.Enumerate"/>
        /// ignores it.
        /// </summary>
        private const string ResultFileName = ".import-result";

        /// <summary>
        /// Marker dropped for MidiPickerActivity the moment VR input focus is back, in the same
        /// folder and for the same reason as <see cref="ResultFileName"/>: the picker runs in
        /// its own process and cannot read the runtime's focus state, so it re-fronts this app
        /// several times over four seconds in case the first attempt didn't take. Each of those
        /// attempts costs a visible out-and-back-in once the app is already fine, so this is how
        /// it is told to stop. Must match REFOCUSED_FILE_NAME on the Java side.
        /// </summary>
        private const string RefocusedFileName = ".vr-refocused";

        // Payload from Java: "<STATUS>|<detail>", split on the FIRST '|'. Filenames are
        // sanitised on both sides, so a path can never contain the separator.
        private const string StatusOk = "OK";
        private const string StatusCancel = "CANCEL";
        private const string StatusError = "ERROR";

        /// <summary>How often we look for the picker's reply file, in real seconds.</summary>
        private const float ResultPollSeconds = 0.4f;

        /// <summary>
        /// How long after the app comes back to the foreground we keep waiting for a result
        /// before assuming it was lost. Generous, because taking the headset off also
        /// pauses/resumes the app. The clock only starts once we are back — the user can spend
        /// as long as they like in the file browser.
        /// </summary>
        private const float ResultGraceSeconds = 10f;

        // ---- Getting the controllers working again afterwards; see RestoreInputFocus ----

        /// <summary>How often the focus watchdog looks, in real seconds.</summary>
        private const float FocusPollSeconds = 0.4f;

        /// <summary>Time the shell is given to hand focus back on its own before we step in.</summary>
        private const float FirstNudgeAfterSeconds = 1.2f;

        /// <summary>Gap between the nudges after the first one.</summary>
        private const float NudgeIntervalSeconds = 2f;

        /// <summary>
        /// Nudges are cheap and the alternative is an app nobody can click, so we try a lot of
        /// them. Whether a single re-front wins is a race: on one headset run focus came back
        /// 50ms after the first attempt, and on the run before that the same build, same code
        /// path, left it stuck for four minutes.
        /// </summary>
        private const int MaxNudges = 6;

        /// <summary>Total time we keep trying before telling the user to do it by hand.</summary>
        private const float FocusWaitSeconds = 20f;

        // android.content.Intent flag, plus the category Horizon OS uses to mark an immersive
        // entry point. Written out rather than read back over JNI: it is a frozen public API
        // value, and this runs on a path where something has already gone wrong.
        private const int FlagActivityNewTask = 0x10000000;   // FLAG_ACTIVITY_NEW_TASK
        private const string VrIntentCategory = "com.oculus.intent.category.VR";

        private static MidiImporter _instance;
        private Action<MidiImportResult> _pending;
        private Coroutine _resultRoutine;
        private Coroutine _focusRoutine;
        private bool _expectPickerReturn;
        private bool _resumedSincePicker;

        /// <summary>Where the Java picker leaves its answer.</summary>
        private static string ResultFilePath =>
            Path.Combine(UserSongLibrary.ImportRoot, ResultFileName);

        /// <summary>Where we tell the picker process the controllers are working again.</summary>
        private static string RefocusedFilePath =>
            Path.Combine(UserSongLibrary.ImportRoot, RefocusedFileName);

        /// <summary>
        /// A line to put in front of the user about something that happens outside a single
        /// <see cref="Import"/> call — currently only the controllers failing to wake up after
        /// the file browser. <see cref="AddSongButton"/>'s status strip listens.
        /// </summary>
        public static event Action<string> StatusMessage;

        /// <summary>True while a pick is in flight, so the button can't launch two at once.</summary>
        public static bool IsBusy => _instance != null && _instance._pending != null;

        /// <summary>
        /// Ask the user for a .mid and copy it into <see cref="UserSongLibrary.ImportRoot"/>.
        /// <paramref name="onFinished"/> always runs exactly once — success, cancel or error.
        /// </summary>
        public static void Import(Action<MidiImportResult> onFinished)
        {
            var importer = Ensure();

            if (importer._pending != null)
            {
                onFinished?.Invoke(Fail("A song is already being added."));
                return;
            }
            importer._pending = onFinished;

#if UNITY_EDITOR
            importer.ImportInEditor();
#elif UNITY_ANDROID
            importer.LaunchAndroidPicker();
#else
            importer.Finish(Fail("Adding your own songs is only supported on the headset."));
#endif
        }

        /// <summary>The bridge GameObject, created on first use and kept for the session.</summary>
        private static MidiImporter Ensure()
        {
            if (_instance != null) return _instance;

            var go = new GameObject(BridgeObjectName);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MidiImporter>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance == null) _instance = this;
        }

        /// <summary>
        /// Coming back from the picker. Nothing is read here — <see cref="WaitForPickerResult"/>
        /// does that, and it un-freezes on its own — but this is the moment the grace clock is
        /// allowed to start and the moment it is worth checking the controllers.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Whether the headset handed VR input focus back decides whether the menu can be
            // CLICKED at all. MidiPickerActivity re-launches the VR app to force this; log it
            // either way so a logcat run says outright whether that worked.
            Debug.Log("[MidiImporter] Resumed — VR input focus: " + OVRPlugin.hasInputFocus);
#endif
            if (!_expectPickerReturn) return;

            _resumedSincePicker = true;
            StartFocusWatchdog();
        }

        /// <summary>
        /// Poll for the file MidiPickerActivity leaves behind and hand whatever it says to the
        /// waiting caller. This is the only reply channel: the picker runs in its own process,
        /// where UnitySendMessage has no player to deliver to.
        ///
        /// The loop is frozen along with the rest of the app while the picker is up. That is
        /// exactly what is wanted — it spends none of the grace budget below while the user is
        /// browsing, and it looks again the instant the app is back.
        /// </summary>
        private System.Collections.IEnumerator WaitForPickerResult()
        {
            float sinceResume = 0f;

            while (true)
            {
                string payload = ReadResultFile();
                if (payload != null)
                {
                    _resultRoutine = null;
                    HandleResult(payload);
                    yield break;
                }

                // The picker's process can be killed under memory pressure while the app is in
                // the background, and then no answer is ever written and nothing would clear
                // the in-flight flag — leaving the Add Song button dead for the rest of the
                // session. Give up, but only once we are back in front of the user: before
                // that, silence just means the file browser is still open.
                if (_resumedSincePicker && sinceResume >= ResultGraceSeconds)
                {
                    _resultRoutine = null;
                    Debug.LogWarning("[MidiImporter] No result came back from the file picker.");
                    Finish(Fail("Nothing came back from the file browser."));
                    yield break;
                }

                // Realtime: the game may well be paused/at timeScale 0 around a menu.
                yield return new WaitForSecondsRealtime(ResultPollSeconds);
                if (_resumedSincePicker) sinceResume += ResultPollSeconds;
            }
        }

        /// <summary>
        /// The picker's answer, or null if it hasn't written one yet. Consumed as it is read,
        /// so a leftover answer can never be served to the next import.
        /// </summary>
        private static string ReadResultFile()
        {
            try
            {
                if (!File.Exists(ResultFilePath)) return null;

                string payload = File.ReadAllText(ResultFilePath).Trim();
                ClearResultFile();
                return payload.Length == 0 ? null : payload;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MidiImporter] Couldn't read the picker's result: " + e.Message);
                ClearResultFile();
                return null;
            }
        }

        private static void ClearResultFile()
        {
            try
            {
                if (File.Exists(ResultFilePath)) File.Delete(ResultFilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MidiImporter] Couldn't clear the result file: " + e.Message);
            }
        }

        /// <summary>
        /// Tell MidiPickerActivity to stand down: the app is back in front with input focus, so
        /// its remaining re-front attempts would only throw it out again. This has to be written
        /// from here because the VR process is the only one that can read the focus state at all.
        /// </summary>
        private static void WriteRefocusedMarker()
        {
            try
            {
                Directory.CreateDirectory(UserSongLibrary.ImportRoot);
                File.WriteAllText(RefocusedFilePath, "1");
            }
            catch (Exception e)
            {
                // Not fatal — the picker just falls back to its fixed ladder, i.e. today's
                // behaviour, so a failure here costs flicker rather than a stuck app.
                Debug.LogWarning("[MidiImporter] Couldn't write the refocused marker: " + e.Message);
            }
        }

        private static void ClearRefocusedMarker()
        {
            try
            {
                if (File.Exists(RefocusedFilePath)) File.Delete(RefocusedFilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MidiImporter] Couldn't clear the refocused marker: " + e.Message);
            }
        }

        // ------------------------------------------------------- Waking the controllers up

        /// <summary>
        /// Start (or restart) the watchdog that makes sure the controllers work again once the
        /// picker is out of the way. Costs nothing when nothing is wrong — it stops on its very
        /// first check.
        /// </summary>
        private void StartFocusWatchdog()
        {
            if (_focusRoutine != null) StopCoroutine(_focusRoutine);
            _focusRoutine = StartCoroutine(RestoreInputFocus());
        }

        /// <summary>
        /// Wait for VR input focus to come back after the file picker, and shove the system
        /// along if it doesn't.
        ///
        /// The picker is an ordinary 2D Android activity, and Horizon OS takes input focus off
        /// the immersive app while one is showing. Losing focus does NOT pause us: the app keeps
        /// rendering, and OVRCameraRig keeps moving the controller anchors because it drives
        /// them from node poses, so the menu laser still follows the controller. What it does
        /// stop is OVRInput — the runtime hands back an empty controller state — so every
        /// trigger press, thumbstick nudge and calibrator control silently does nothing. That is
        /// the "I can see the menu and aim at it but nothing responds" state.
        ///
        /// Focus normally returns the moment our activity is resumed. When it doesn't, asking
        /// Android to put our own activity back in front usually shakes it loose. That request
        /// has to come from a resumed activity, which is why it is made from here and not from
        /// MidiPickerActivity (which is being destroyed at that point) — and why it is checked
        /// afterwards instead of fired blind, so the user can be told when it hasn't worked.
        /// </summary>
        private System.Collections.IEnumerator RestoreInputFocus()
        {
            float waited = 0f;
            int nudges = 0;

            while (waited < FocusWaitSeconds)
            {
                if (HasInputFocus)
                {
                    // First, before anything else: the picker process is partway through a
                    // four-second re-front ladder it cannot evaluate for itself, and every
                    // attempt it makes from here throws this app out and back in where the user
                    // can see it. The sooner it learns, the fewer of those they get.
                    WriteRefocusedMarker();

                    // Always logged, never only on the interesting path: a silent success is
                    // indistinguishable in logcat from a watchdog that never ran at all, which
                    // cost a whole diagnostic round trip once already.
                    Debug.Log($"[MidiImporter] VR input focus back after {waited:0.0}s and " +
                              $"{nudges} nudge(s) — the controllers work again.");
                    _expectPickerReturn = false;
                    _focusRoutine = null;
                    yield break;
                }

                if (nudges < MaxNudges &&
                    waited >= FirstNudgeAfterSeconds + nudges * NudgeIntervalSeconds)
                {
                    nudges++;
                    Debug.LogWarning($"[MidiImporter] Still no VR input focus {waited:0.0}s after " +
                                     $"the picker — bringing the app to the front (attempt {nudges}).");
                    BringAppToFront();
                }

                // Realtime: a menu is usually up, and the playback bar can leave timeScale at 0.
                yield return new WaitForSecondsRealtime(FocusPollSeconds);
                waited += FocusPollSeconds;
            }

            Debug.LogError($"[MidiImporter] VR input focus never came back after the file picker " +
                           $"({MaxNudges} re-front attempts over {FocusWaitSeconds:0}s). The " +
                           "controllers can point but not click until the app is relaunched.");
            // Plain ASCII: the default TMP atlas has no typographic dashes or quotes.
            //
            // "Close and reopen", NOT "press the Meta button and come back": going to Home and
            // stepping back into the app is a task switch, which issues no activity START, so
            // the shell never re-evaluates focus and the app stays dead. That was watched
            // happening on-device. Only a real launch clears it, which is also what the nudges
            // above are trying to do.
            StatusMessage?.Invoke(
                "Controllers asleep - close the app and open it again to fix this.");
            _expectPickerReturn = false;
            _focusRoutine = null;
        }

        /// <summary>
        /// True when the headset is giving this app controller input. Always true off-device,
        /// where there is no such thing and the watchdog should do nothing.
        /// </summary>
        private static bool HasInputFocus
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            get { return OVRPlugin.hasInputFocus; }
#else
            get { return true; }
#endif
        }

        /// <summary>
        /// Ask Android to put our own activity back in front, which is what makes the Horizon OS
        /// shell hand input focus back to the immersive app. Does nothing off-device.
        /// </summary>
        private static void BringAppToFront()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null) return;

                    using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                    {
                        if (packageManager == null) return;

                        using (var intent = packageManager.Call<AndroidJavaObject>(
                                   "getLaunchIntentForPackage", Application.identifier))
                        {
                            if (intent == null) return;

                            // getLaunchIntentForPackage already pins the component, so the
                            // category is purely a hint to the shell that this is the immersive
                            // entry point and can't change which activity is resolved. Each of
                            // these calls hands the same Intent back, hence the Dispose().
                            //
                            // NEW_TASK and nothing else, deliberately: this is the exact intent
                            // shape observed to move the runtime's focus state from 0 to 1 on a
                            // stuck headset. Adding CLEAR_TOP or SINGLE_TOP makes Android
                            // short-circuit to START_DELIVERED_TO_TOP when the activity is
                            // already on top, and then no task transition happens — and it is
                            // the transition that makes the shell re-evaluate VR input focus.
                            intent.Call<AndroidJavaObject>("addCategory", VrIntentCategory).Dispose();
                            intent.Call<AndroidJavaObject>("addFlags", FlagActivityNewTask).Dispose();

                            activity.Call("startActivity", intent);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MidiImporter] Couldn't bring the app back to the front: " + e.Message);
            }
#endif
        }

        // ------------------------------------------------------------------ Android (SAF)

#if UNITY_ANDROID && !UNITY_EDITOR
        private void LaunchAndroidPicker()
        {
            try
            {
                // The helper Activity writes straight into this folder, so it has to exist.
                Directory.CreateDirectory(UserSongLibrary.ImportRoot);

                // An answer left over from a run whose picker process was killed before we read
                // it would otherwise be served instantly as the result of THIS import.
                ClearResultFile();

                // Likewise the marker from the last import: left in place it would tell this
                // import's picker that focus was already back before the app had even lost it,
                // and the fallback re-fronts would be skipped when they are genuinely needed.
                ClearRefocusedMarker();

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    if (activity == null)
                    {
                        Finish(Fail("Couldn't reach the Android activity."));
                        return;
                    }

                    // setClassName(String, String) avoids marshalling a Context or a
                    // java.lang.Class through JNI. Each call returns the same Intent back.
                    intent.Call<AndroidJavaObject>("setClassName", Application.identifier,
                                                   "com.darripi.arpiano.MidiPickerActivity").Dispose();
                    intent.Call<AndroidJavaObject>("putExtra", "destDir",
                                                   UserSongLibrary.ImportRoot).Dispose();
                    // NEW_TASK explicitly, so the picker's android:taskAffinity is actually
                    // consulted: Android only honours an affinity when an activity starts a
                    // task, and a separate task is the whole point — sharing one with the VR
                    // app is what made Horizon hide the immersive app. The shell was observed
                    // adding this flag by itself, but relying on that leaves the fix at the
                    // mercy of a shell update.
                    intent.Call<AndroidJavaObject>("addFlags", FlagActivityNewTask).Dispose();

                    activity.Call("startActivity", intent);
                }

                // From here on the app is hidden behind a 2D panel and has lost VR input focus,
                // and will most likely be paused outright. Both routines below survive that:
                // they simply stop ticking and carry on when the app is resumed.
                _expectPickerReturn = true;
                _resumedSincePicker = false;
                if (_resultRoutine != null) StopCoroutine(_resultRoutine);
                _resultRoutine = StartCoroutine(WaitForPickerResult());

                Debug.Log("[MidiImporter] Opened the system file picker; waiting for a result.");
            }
            catch (Exception e)
            {
                Debug.LogError("[MidiImporter] Couldn't open the file picker: " + e);
                Finish(Fail("Couldn't open the file browser on this device."));
            }
        }
#endif

        /// <summary>
        /// Act on the line MidiPickerActivity left behind, whatever it says.
        /// </summary>
        private void HandleResult(string payload)
        {
            Debug.Log("[MidiImporter] Picker result: " + payload);

            // The picker is gone by the time this runs, so this is the earliest safe moment to
            // check the controllers actually work again. Not covered by the resume callback
            // alone: MidiPickerActivity is translucent, so Unity is not always fully stopped
            // behind it and a resume may never be reported.
            if (_expectPickerReturn) StartFocusWatchdog();

            if (payload == null) payload = string.Empty;
            int split = payload.IndexOf('|');
            string status = split < 0 ? payload : payload.Substring(0, split);
            string detail = split < 0 ? string.Empty : payload.Substring(split + 1);

            switch (status)
            {
                case StatusOk:
                    Finish(Accept(detail));
                    break;

                case StatusCancel:
                    Finish(Fail("Cancelled — no song added."));
                    break;

                case StatusError:
                default:
                    Finish(Fail(string.IsNullOrEmpty(detail)
                        ? "Couldn't add that file."
                        : detail));
                    break;
            }
        }

        // ------------------------------------------------------------------ Editor

#if UNITY_EDITOR
        private void ImportInEditor()
        {
            string picked = UnityEditor.EditorUtility.OpenFilePanel("Choose a MIDI file", "", "mid,midi");
            if (string.IsNullOrEmpty(picked))
            {
                Finish(Fail("Cancelled — no song added."));
                return;
            }

            string destination;
            try
            {
                destination = UserSongLibrary.UniqueDestinationPath(Path.GetFileName(picked));
                File.Copy(picked, destination);
            }
            catch (Exception e)
            {
                Debug.LogError("[MidiImporter] Copy failed: " + e);
                Finish(Fail("Couldn't copy that file into the app."));
                return;
            }

            Finish(Accept(destination));
        }
#endif

        // ------------------------------------------------------------------ Shared

        /// <summary>
        /// Validate a file that is already sitting in the user song folder and turn it into a
        /// menu entry. A file we can't parse is DELETED — leaving it would put a row in the
        /// menu that fails every time it is picked.
        /// </summary>
        private static MidiImportResult Accept(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return Fail("The file didn't arrive — nothing was added.");
            }

            string title = SongSelectionMenu.DisplayNameFromFileName(Path.GetFileName(path));

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var notes = MidiFileParser.Parse(bytes, out _);
                if (notes.Count == 0)
                {
                    UserSongLibrary.Delete(path);
                    return Fail($"\"{title}\" has no notes in it.");
                }
            }
            catch (Exception e)
            {
                // NotSupportedException for SMF Format 2 / SMPTE timing, anything else for a
                // file that simply isn't a MIDI.
                Debug.LogWarning($"[MidiImporter] Rejected '{path}': {e.Message}");
                UserSongLibrary.Delete(path);
                return Fail($"\"{title}\" isn't a MIDI file this app can read.");
            }

            Debug.Log($"[MidiImporter] Added \"{title}\" -> {path}");
            return new MidiImportResult
            {
                ok = true,
                message = $"Added \"{title}\".",
                entry = new SongEntry { displayName = title, absolutePath = path }
            };
        }

        private static MidiImportResult Fail(string message)
        {
            return new MidiImportResult { ok = false, message = message };
        }

        /// <summary>Hand the result to the waiting caller and clear the in-flight flag.</summary>
        private void Finish(MidiImportResult result)
        {
            if (_resultRoutine != null)
            {
                StopCoroutine(_resultRoutine);
                _resultRoutine = null;
            }

            var callback = _pending;
            _pending = null;
            callback?.Invoke(result);
        }
    }
}
