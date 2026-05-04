using UnityEngine;
using System.Collections.Generic;

public class NoteSpawner : MonoBehaviour
{
    public static NoteSpawner Instance { get; private set; }

    [Header("Song")]
    [SerializeField] private SongData currentSong;

    [Header("Timing Parameters")]
    [SerializeField] public float noteSpeed = 2.5f;        // metres/sec
    [SerializeField] public float spawnDepth = 1.5f;  // metres behind keyboard
    [SerializeField] public float timingOffset = 0.0f;     // calibration offset

    [Header("References")]
    [SerializeField] private KeyboardMapper mapper;
    [SerializeField] private NotePool pool;
    [SerializeField] private Material tapNoteMaterial;
    [SerializeField] private Material holdNoteMaterial;

    private float songClock = 0f;       // seconds since song start
    private bool isPlaying = false;
    private int nextNoteIdx = 0;

    // Active notes tracked for hit detection
    private List<NoteObject> activeNotes = new();

    // Pre-compute how many seconds before hit to spawn note
    private float SpawnLeadTime => spawnDepth / noteSpeed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Find whichever IAudioDetector is active in scene
        var detector = FindAnyObjectByType<StubAudioDetector>() as IAudioDetector
            ?? FindAnyObjectByType<PitchDetector>() as IAudioDetector;

        if (detector != null)
        {
            detector.OnNoteDetected += (midi, confidence) =>
            {
                if (confidence >= 0.7f)
                    CheckHit(midi);
            };
            detector.StartListening();
        }
    }

    public void StartSong(SongData song = null)
    {
        if (song != null) currentSong = song;
        if (currentSong == null) { Debug.LogWarning("No SongData assigned!"); return; }

        songClock = -SpawnLeadTime;  // Start before first note so notes have travel time
        nextNoteIdx = 0;
        isPlaying = true;
        activeNotes.Clear();

        Debug.Log($"[NoteSpawner] Starting: {currentSong.songName} @ {currentSong.bpm} BPM");
    }

    public void StopSong()
    {
        isPlaying = false;
        foreach (var note in activeNotes)
            if (note != null) pool.Return(note);
        activeNotes.Clear();
    }

    void Update()
    {
        if (!isPlaying || currentSong == null) return;

        songClock += Time.deltaTime;

        // Spawn notes that should appear now
        while (nextNoteIdx < currentSong.notes.Count)
        {
            var ev = currentSong.notes[nextNoteIdx];
            float hitTime = currentSong.BeatToSeconds(ev.beatTime) + timingOffset;

            // Spawn when: current clock >= hitTime - lead time
            if (songClock >= hitTime - SpawnLeadTime)
            {
                SpawnNote(ev, hitTime);
                nextNoteIdx++;
            }
            else break;
        }

        // Remove cleared notes from active list
        activeNotes.RemoveAll(n => n == null || !n.gameObject.activeSelf);
    }

    private void SpawnNote(NoteEvent ev, float hitTime)
    {
        var lane = mapper.GetLane(ev.midiNote);
        if (lane == null)
        {
            Debug.LogWarning($"No lane for MIDI {ev.midiNote}");
            return;
        }

        bool isHold = ev.duration > 0f;
        float holdSec = isHold ? currentSong.BeatToSeconds(ev.duration) : 0f;
        var noteType = isHold ? NoteType.Hold : NoteType.Tap;
        var mat = isHold ? holdNoteMaterial : tapNoteMaterial;

        NoteObject note = pool.Get();
        note.transform.SetParent(null);
        note.Initialise(lane, noteSpeed, spawnDepth, hitTime, noteType, holdSec, mat);

        activeNotes.Add(note);
    }

    // Called by InputDetector when a note is played
    public void CheckHit(int midiNote)
    {
        float now = songClock;
        float window = 0.15f;   // ±150ms hit window

        NoteObject best = null;
        float bestDelta = float.MaxValue;

        foreach (var note in activeNotes)
        {
            if (note == null || note.midiNote != midiNote) continue;

            float delta = Mathf.Abs(note.targetHitTime - now);
            if (delta < window && delta < bestDelta)
            {
                bestDelta = delta;
                best = note;
            }
        }

        if (best != null)
            best.OnHit(correct: true);
        else
            FeedbackController.Instance?.TriggerMiss(midiNote);
    }
}