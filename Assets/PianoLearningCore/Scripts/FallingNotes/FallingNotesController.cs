using System.Collections.Generic;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// The "brain" of the falling-note system. Owns the playback clock, spawns notes
    /// from the loaded Song into the world ahead of time, and despawns them once they
    /// have passed the keyboard.
    ///
    /// Coordinate system: notes live in the LOCAL space of the assigned KeyboardLayout
    /// transform, so moving/rotating the keyboard automatically carries the falling
    /// notes with it (great for AR alignment).
    /// </summary>
    public class FallingNotesController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The KeyboardLayout that defines where each key sits in space.")]
        public KeyboardLayout keyboard;

        [Header("Visuals")]
        [Tooltip("Material applied to falling notes that map to WHITE keys. If null, a default is created.")]
        public Material whiteNoteMaterial;

        [Tooltip("Material applied to falling notes that map to BLACK keys. If null, a default is created.")]
        public Material blackNoteMaterial;

        [Tooltip("Thickness (Y dimension) of each falling-note block, in meters.")]
        public float noteThickness = 0.006f;

        [Tooltip("Extra vertical gap between the key surface and the note, so they don't visually z-fight.")]
        public float noteHoverHeight = 0.0005f;

        [Header("Timing / motion")]
        [Tooltip("How many seconds before a note's hit time it spawns and starts approaching.")]
        public float leadTime = 3f;

        [Tooltip("How far back from the keyboard the notes spawn, in meters. " +
                 "Combined with leadTime this defines the note speed (= spawnDistance / leadTime).")]
        public float spawnDistance = 1.5f;

        [Header("Playback")]
        [Tooltip("If true, playback starts automatically once a song is loaded.")]
        public bool autoStart = true;

        [Tooltip("Real-time countdown (seconds) before the song starts. Counts down at real " +
                 "speed regardless of playbackSpeed, so the player always has the same prep time. " +
                 "Shown in the headset by StartCountdownDisplay; each remaining whole second is " +
                 "also logged to the Console for debugging.")]
        public float startDelay = 0f;

        [Tooltip("Multiplier on song playback speed. 1.0 = normal tempo. 0.5 = half speed " +
                 "(notes fall slower, hit window is wider in real time — great for testing or " +
                 "for beginner difficulty). Affects everything that reads CurrentTime, " +
                 "including the NoteEvaluator hit window, so timing scoring stays consistent.")]
        [Range(0.1f, 2f)]
        public float playbackSpeed = 1f;

        // ----- Public read-only state -----

        public Song Song { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsFinished { get; private set; }

        /// <summary>The current "song time" in seconds. Notes whose startTime &lt;= this are being hit.</summary>
        public float CurrentTime { get; private set; }

        /// <summary>
        /// When true, the playback clock is frozen even though <see cref="IsPlaying"/> stays true:
        /// CurrentTime stops advancing so the falling notes park where they are. The NoteEvaluator's
        /// Practice "wait for the correct note" gate sets this to hold a note on its key until the
        /// player plays it. Distinct from <see cref="Pause"/> (a user pause clears IsPlaying); this is
        /// an automatic, transient hold the gate releases the instant the note is judged.
        /// </summary>
        public bool HoldClock { get; set; }

        /// <summary>
        /// True while the pre-roll countdown (<see cref="startDelay"/>) is running: the song is playing
        /// but its clock hasn't reached zero yet. Drives <see cref="StartCountdownDisplay"/>.
        /// </summary>
        public bool IsCountingDown => IsPlaying && CurrentTime < 0f;

        /// <summary>Whole seconds left in the pre-roll countdown, rounded up (…3, 2, 1), or 0 once the
        /// song clock has reached zero.</summary>
        public int CountdownSecondsLeft => CurrentTime < 0f ? Mathf.CeilToInt(-CurrentTime) : 0;

        /// <summary>Speed of all falling notes, derived from spawnDistance and leadTime.</summary>
        public float Speed => leadTime > 0.0001f ? spawnDistance / leadTime : 1f;

        // ----- Internals -----

        private int _nextSpawnIndex;
        private readonly List<FallingNote> _activeNotes = new List<FallingNote>();
        private int _lastCountdownLogged = int.MaxValue;

        private void Awake()
        {
            EnsureDefaultMaterials();
        }

        private void Start()
        {
            if (Song != null && autoStart) Play();
        }

        private void Update()
        {
            if (!IsPlaying || Song == null) return;

            // Pre-roll: count down in REAL time so the player always gets the same prep
            // window regardless of playbackSpeed. Once we cross zero, playbackSpeed kicks in.
            if (CurrentTime < 0f)
            {
                CurrentTime += Time.deltaTime;
                LogCountdownIfChanged();
            }
            else
            {
                if (_lastCountdownLogged != 0)
                {
                    Debug.Log("[FallingNotesController] GO!");
                    _lastCountdownLogged = 0;
                }
                // HoldClock freezes time in place (the Practice wait-for-note gate) without ending
                // playback, so the notes park on their keys until the player catches up.
                if (!HoldClock)
                    CurrentTime += Time.deltaTime * playbackSpeed;
            }

            SpawnUpcomingNotes();
            DespawnFinishedNotes();

            if (_nextSpawnIndex >= Song.notes.Count && _activeNotes.Count == 0)
            {
                IsFinished = true;
                IsPlaying = false;
            }
        }

        private void LogCountdownIfChanged()
        {
            int secondsLeft = CountdownSecondsLeft;
            if (secondsLeft <= 0 || secondsLeft == _lastCountdownLogged) return;
            _lastCountdownLogged = secondsLeft;
            Debug.Log($"[FallingNotesController] Starting in {secondsLeft}...");
        }

        // ---------------- Public controls ----------------

        public void LoadSong(Song song)
        {
            Stop();
            Song = song;
            CurrentTime = -startDelay; // negative time = pre-roll
            _nextSpawnIndex = 0;
            IsFinished = false;
            _lastCountdownLogged = int.MaxValue;
            if (autoStart) Play();
        }

        public void Play()
        {
            if (Song == null) return;
            IsPlaying = true;
            IsFinished = false;
        }

        public void Pause() => IsPlaying = false;

        public void Stop()
        {
            IsPlaying = false;
            IsFinished = false;
            HoldClock = false;
            CurrentTime = -startDelay;
            _nextSpawnIndex = 0;
            _lastCountdownLogged = int.MaxValue;
            ClearActiveNotes();
        }

        public void Restart()
        {
            Stop();
            Play();
        }

        public void SetTime(float newTime)
        {
            Stop();
            CurrentTime = newTime;
            // Advance _nextSpawnIndex to skip notes already in the past.
            if (Song != null)
            {
                while (_nextSpawnIndex < Song.notes.Count
                       && Song.notes[_nextSpawnIndex].startTime - CurrentTime < -0.0001f)
                {
                    _nextSpawnIndex++;
                }
            }
        }

        // ---------------- Query ----------------

        /// <summary>
        /// Returns the first currently-active <see cref="FallingNote"/> whose MIDI number matches,
        /// or null if none is on screen. Used by NoteEvaluator to flash feedback colours.
        /// </summary>
        public FallingNote FindActiveNote(int midiNumber)
        {
            foreach (var fn in _activeNotes)
            {
                if (fn != null && fn.note.midiNumber == midiNumber)
                    return fn;
            }
            return null;
        }

        // ---------------- Spawning ----------------

        private void SpawnUpcomingNotes()
        {
            if (keyboard == null) return;

            while (_nextSpawnIndex < Song.notes.Count)
            {
                var n = Song.notes[_nextSpawnIndex];
                float timeUntilHit = n.startTime - CurrentTime;
                if (timeUntilHit > leadTime) break; // not yet in window

                // Skip notes outside the keyboard's range so they don't appear off-keyboard.
                if (!keyboard.ContainsNote(n.midiNumber))
                {
                    _nextSpawnIndex++;
                    continue;
                }

                SpawnNoteVisual(n);
                _nextSpawnIndex++;
            }
        }

        private void SpawnNoteVisual(PianoNote n)
        {
            // Build a primitive cube and parent it to the keyboard so it lives in keyboard-local space.
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"FallingNote_{NoteUtils.GetNoteName(n.midiNumber)}@{n.startTime:F2}";
            go.transform.SetParent(keyboard.transform, worldPositionStays: false);

            // Drop the collider; we don't need physics.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Apply the appropriate material as a per-instance copy.
            var rend = go.GetComponent<MeshRenderer>();
            Material src = NoteUtils.IsBlackKey(n.midiNumber) ? blackNoteMaterial : whiteNoteMaterial;
            rend.sharedMaterial = new Material(src);

            var fn = go.AddComponent<FallingNote>();
            fn.Initialize(n, this, keyboard);
            _activeNotes.Add(fn);
        }

        // ---------------- Despawning ----------------

        private void DespawnFinishedNotes()
        {
            for (int i = _activeNotes.Count - 1; i >= 0; i--)
            {
                var fn = _activeNotes[i];
                if (fn == null)
                {
                    _activeNotes.RemoveAt(i);
                    continue;
                }
                if (fn.IsFullyPast(CurrentTime))
                {
                    Destroy(fn.gameObject);
                    _activeNotes.RemoveAt(i);
                }
            }
        }

        private void ClearActiveNotes()
        {
            for (int i = 0; i < _activeNotes.Count; i++)
            {
                if (_activeNotes[i] != null) Destroy(_activeNotes[i].gameObject);
            }
            _activeNotes.Clear();
        }

        // ---------------- Materials ----------------

        private void EnsureDefaultMaterials()
        {
            if (whiteNoteMaterial == null) whiteNoteMaterial = MakeMat(new Color(0.30f, 0.70f, 1.00f, 1f));
            if (blackNoteMaterial == null) blackNoteMaterial = MakeMat(new Color(0.10f, 0.45f, 0.85f, 1f));
        }

        private static Material MakeMat(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard")
                        ?? Shader.Find("Legacy Shaders/Diffuse");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (keyboard == null || keyboard.config == null) return;
            var cfg = keyboard.config;

            // Draw the hit line (cyan) and the spawn line (yellow) in world space.
            float w = cfg.TotalWidth;
            Vector3 a = keyboard.transform.TransformPoint(new Vector3(0f, 0.001f, 0f));
            Vector3 b = keyboard.transform.TransformPoint(new Vector3(w,  0.001f, 0f));
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(a, b);

            Vector3 c = keyboard.transform.TransformPoint(new Vector3(0f, 0.001f, spawnDistance));
            Vector3 d = keyboard.transform.TransformPoint(new Vector3(w,  0.001f, spawnDistance));
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(c, d);
        }
#endif
    }
}
