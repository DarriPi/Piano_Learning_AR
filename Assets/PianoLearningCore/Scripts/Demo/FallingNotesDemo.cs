using System.IO;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// One-script demo. Add this to an empty GameObject and press Play — it builds
    /// the keyboard, the falling-notes controller, a camera, and loads either the
    /// built-in C-major-scale demo song or a .mid file at the path you specify.
    ///
    /// This is meant for the standalone 3D test scene. When you later move to AR,
    /// you can keep using FallingNotesController + KeyboardLayout directly and
    /// throw this bootstrap script away.
    /// </summary>
    public class FallingNotesDemo : MonoBehaviour
    {
        public enum SongSource
        {
            BuiltInDemo,           // hardcoded C-major scale, no file needed
            StreamingAssetsFile,   // path inside Application.streamingAssetsPath
            AbsoluteFile           // absolute file path (handy in the editor)
        }

        [Header("What to play")]
        public SongSource songSource = SongSource.BuiltInDemo;

        [Tooltip("Used if songSource = StreamingAssetsFile. Example: \"Songs/MaryHadALittleLamb.mid\"")]
        public string streamingAssetsRelativePath = "Songs/example.mid";

        [Tooltip("Used if songSource = AbsoluteFile. Example: \"C:/Users/me/songs/song.mid\"")]
        public string absoluteFilePath = "";

        [Header("Keyboard config")]
        [Tooltip("If null, a default 76-key (E1..G7) config is created automatically at runtime.")]
        public KeyboardConfig keyboardConfig;

        [Tooltip("Where the keyboard root sits in world space.")]
        public Vector3 keyboardPosition = new Vector3(-0.55f, 0.75f, 0.4f); // ~table height, ~40cm in front of camera

        [Header("Falling notes tuning")]
        [Tooltip("Seconds of look-ahead. 3s is a comfortable PianoVision-ish feel.")]
        public float leadTime = 3f;

        [Tooltip("Meters the notes travel from spawn to hit line.")]
        public float spawnDistance = 1.5f;

        [Tooltip("Pause this many seconds before playback starts, so the first notes have time to fall in.")]
        public float startDelay = 0.5f;

        [Header("Scene helpers")]
        [Tooltip("If true, creates a Camera positioned in front of the keyboard so you can see the notes.")]
        public bool createDemoCamera = true;

        [Tooltip("If true, builds the visible keyboard underneath. Disable when running in AR with a real piano.")]
        public bool showVirtualKeyboard = true;

        [Tooltip("If true, enables WASD/arrow-key calibration controls (see KeyboardCalibrator).")]
        public bool enableCalibrationControls = true;

        // ---- Built objects (kept for reference) ----
        private GameObject _keyboardRoot;
        private KeyboardLayout _layout;
        private FallingNotesController _controller;
        private KeyboardVisualizer _visualizer;
        private Camera _demoCamera;

        private void Awake()
        {
            // 1) Make sure we have a config.
            if (keyboardConfig == null)
            {
                keyboardConfig = ScriptableObject.CreateInstance<KeyboardConfig>();
                // The CreateInstance defaults already match a 76-key keyboard, so nothing more to do.
            }

            // 2) Build the keyboard root.
            _keyboardRoot = new GameObject("Keyboard");
            _keyboardRoot.transform.position = keyboardPosition;

            _layout = _keyboardRoot.AddComponent<KeyboardLayout>();
            _layout.config = keyboardConfig;

            if (showVirtualKeyboard)
            {
                _visualizer = _keyboardRoot.AddComponent<KeyboardVisualizer>();
            }

            if (enableCalibrationControls)
            {
                _keyboardRoot.AddComponent<KeyboardCalibrator>();
            }

            // 3) Build the falling-notes controller on its own GameObject (could also live on the keyboard).
            var ctrlGO = new GameObject("FallingNotesController");
            ctrlGO.transform.SetParent(transform, worldPositionStays: true);
            _controller = ctrlGO.AddComponent<FallingNotesController>();
            _controller.keyboard = _layout;
            _controller.leadTime = leadTime;
            _controller.spawnDistance = spawnDistance;
            _controller.startDelay = startDelay;
            _controller.autoStart = true;

            // 4) Demo camera so we can see something without setting one up manually.
            if (createDemoCamera && Camera.main == null)
            {
                var camGO = new GameObject("Demo Camera");
                _demoCamera = camGO.AddComponent<Camera>();
                camGO.tag = "MainCamera";
                // Sit slightly in front of and above the keyboard, looking down at it.
                Vector3 keyCenter = keyboardPosition + new Vector3(keyboardConfig.TotalWidth * 0.5f, 0f, 0f);
                camGO.transform.position = keyCenter + new Vector3(0f, 0.45f, -0.55f);
                camGO.transform.LookAt(keyCenter + new Vector3(0f, 0f, 0.4f));
                _demoCamera.clearFlags = CameraClearFlags.SolidColor;
                _demoCamera.backgroundColor = new Color(0.05f, 0.05f, 0.08f);

                // A simple light so the keyboard isn't pitch black.
                if (FindFirstObjectByTypeCompat<Light>() == null)
                {
                    var lightGO = new GameObject("Demo Light");
                    var l = lightGO.AddComponent<Light>();
                    l.type = LightType.Directional;
                    l.intensity = 1.1f;
                    lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                }
            }
        }

        private void Start()
        {
            // 5) Load the song *after* Awake so all components are ready.
            Song song = LoadConfiguredSong();
            if (song == null || song.notes.Count == 0)
            {
                Debug.LogWarning("[FallingNotesDemo] No notes loaded — falling back to built-in C major scale demo.");
                song = Song.BuildCMajorScaleDemo();
            }

            Debug.Log($"[FallingNotesDemo] Loaded '{song.title}' with {song.notes.Count} notes, " +
                      $"duration ~{song.DurationSeconds:F1}s.");

            _controller.LoadSong(song);
        }

        private Song LoadConfiguredSong()
        {
            switch (songSource)
            {
                case SongSource.BuiltInDemo:
                    return Song.BuildCMajorScaleDemo();

                case SongSource.StreamingAssetsFile:
                    string path = Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath);
                    if (!File.Exists(path))
                    {
                        Debug.LogWarning($"[FallingNotesDemo] StreamingAssets file not found: {path}");
                        return null;
                    }
                    return MidiSongLoader.LoadFromFile(path);

                case SongSource.AbsoluteFile:
                    if (string.IsNullOrEmpty(absoluteFilePath) || !File.Exists(absoluteFilePath))
                    {
                        Debug.LogWarning($"[FallingNotesDemo] Absolute file not found: {absoluteFilePath}");
                        return null;
                    }
                    return MidiSongLoader.LoadFromFile(absoluteFilePath);
            }
            return null;
        }

        // Unity 6 deprecates Object.FindObjectOfType in favour of FindFirstObjectByType.
        // Use a small helper to keep this code working on 2022 LTS too.
        private static T FindFirstObjectByTypeCompat<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>();
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }
    }
}
