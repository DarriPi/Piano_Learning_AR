using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Reads a SongData asset, advances a song clock, and spawns NoteObjects
/// at the correct moment so each note arrives at its lane hit-point exactly
/// on its target beat.
///
/// Works for any keyboard orientation because it delegates spawn position
/// calculation to KeyboardMapper.GetSpawnPosition(), which returns a point
/// along the keyboard's forward axis.
/// </summary>
public class NoteSpawner : MonoBehaviour
{
    public static NoteSpawner Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Song")]
    [SerializeField] private SongData currentSong;

    [Header("Timing")]
    [Tooltip("How fast notes travel along the lane in metres/second.")]
    [SerializeField] public float noteSpeed     = 2.5f;
    [Tooltip("Extra time offset to fine-tune note arrival (positive = later).")]
    [SerializeField] public float timingOffset  = 0.0f;

    [Header("References")]
    [SerializeField] private KeyboardMapper mapper;
    [SerializeField] private NotePool       pool;
    [SerializeField] private Material       tapNoteMaterial;
    [SerializeField] private Material       holdNoteMaterial;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private float            songClock    = 0f;
    private bool             isPlaying    = false;
    private int              nextNoteIdx  = 0;
    private List<NoteObject> activeNotes  = new();

    /// <summary>
    /// How many seconds before the hit time a note must be spawned so it
    /// travels the full spawnDistance and arrives exactly on the beat.
    /// </summary>
    private float SpawnLeadTime => mapper.spawnDistance / noteSpeed;

    // ── Unity callbacks ───────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Auto-wire audio detector (stub or real)
        WireAudioDetector();
    }

    void Update()
    {
        if (!isPlaying || currentSong == null) return;

        songClock += Time.deltaTime;

        // Spawn any notes whose lead-time window has opened
        while (nextNoteIdx < currentSong.notes.Count)
        {
            NoteEvent ev      = currentSong.notes[nextNoteIdx];
            float     hitTime = currentSong.BeatToSeconds(ev.beatTime) + timingOffset;

            if (songClock >= hitTime - SpawnLeadTime)
            {
                SpawnNote(ev, hitTime);
                nextNoteIdx++;
            }
            else
            {
                break; // Notes are sorted by beat time; safe to stop here
            }
        }

        // Prune destroyed/returned notes from the active list
        activeNotes.RemoveAll(n => n == null || !n.gameObject.activeSelf);
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public void StartSong(SongData song = null)
    {
        if (song != null) currentSong = song;
        if (currentSong == null)
        {
            Debug.LogWarning("[NoteSpawner] No SongData assigned!");
            return;
        }

        // Start the clock negative so the very first note has full travel time
        songClock   = -SpawnLeadTime;
        nextNoteIdx = 0;
        isPlaying   = true;
        activeNotes.Clear();

        Debug.Log($"[NoteSpawner] Starting '{currentSong.songName}' " +
                  $"@ {currentSong.bpm} BPM | lead time = {SpawnLeadTime:F2}s");
    }

    public void StopSong()
    {
        isPlaying = false;
        foreach (var note in activeNotes)
            if (note != null) pool.Return(note);
        activeNotes.Clear();
        Debug.Log("[NoteSpawner] Song stopped.");
    }

    /// <summary>
    /// Called by InputDetector or the audio detector when a key is pressed.
    /// Finds the closest in-window note for that MIDI note and marks it as hit.
    /// </summary>
    public void CheckHit(int midiNote)
    {
        const float HIT_WINDOW = 0.15f; // ±150 ms

        NoteObject best      = null;
        float      bestDelta = float.MaxValue;

        foreach (NoteObject note in activeNotes)
        {
            if (note == null || note.midiNote != midiNote) continue;

            float delta = Mathf.Abs(note.targetHitTime - songClock);
            if (delta < HIT_WINDOW && delta < bestDelta)
            {
                bestDelta = delta;
                best      = note;
            }
        }

        if (best != null)
            best.OnHit(correct: true);
        else
            FeedbackController.Instance?.TriggerMiss(midiNote);
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private void SpawnNote(NoteEvent ev, float hitTime)
    {
        KeyLane lane = mapper.GetLane(ev.midiNote);
        if (lane == null)
        {
            Debug.LogWarning($"[NoteSpawner] No lane for MIDI {ev.midiNote}");
            return;
        }

        bool     isHold  = ev.duration > 0f;
        float    holdSec = isHold ? currentSong.BeatToSeconds(ev.duration) : 0f;
        NoteType type    = isHold ? NoteType.Hold : NoteType.Tap;
        Material mat     = isHold ? holdNoteMaterial : tapNoteMaterial;

        // Ask the mapper for the world-space spawn point for this MIDI note.
        // This already accounts for keyboard orientation and spawn distance.
        Vector3 spawnPos = mapper.GetSpawnPosition(ev.midiNote);

        NoteObject note = pool.Get();
        note.transform.SetParent(null);
        note.Initialise(lane, spawnPos, noteSpeed, hitTime, type, holdSec, mat);

        activeNotes.Add(note);
    }

    /// <summary>
    /// Finds whichever IAudioDetector is active in the scene and subscribes
    /// to its OnNoteDetected event so hits can be registered automatically.
    /// </summary>
    private void WireAudioDetector()
    {
        // Try stub first (Test Mode), then real detector (AR Mode)
        IAudioDetector detector =
            FindAnyObjectByType<StubAudioDetector>() as IAudioDetector
            ?? FindAnyObjectByType<PitchDetector>()  as IAudioDetector;

        if (detector != null)
        {
            detector.OnNoteDetected += (midi, confidence) =>
            {
                if (confidence >= 0.7f)
                    CheckHit(midi);
            };
            detector.StartListening();
            Debug.Log($"[NoteSpawner] Wired to {detector.GetType().Name}");
        }
        else
        {
            Debug.LogWarning("[NoteSpawner] No IAudioDetector found in scene.");
        }
    }
}