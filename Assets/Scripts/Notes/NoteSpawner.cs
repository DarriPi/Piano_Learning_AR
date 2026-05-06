using System.Collections.Generic;
using UnityEngine;

public class NoteSpawner : MonoBehaviour
{
    public static NoteSpawner Instance { get; private set; }

    [Header("Song")]
    [SerializeField] private SongData currentSong;

    [Header("Timing Parameters")]
    [SerializeField] public float noteSpeed = 2.5f;        // metres/sec (now horizontal speed)
    [SerializeField] public float spawnDistance = 0.5f;    // metres behind the keyboard
    [SerializeField] public float timingOffset = 0.0f;

    [Header("References")]
    [SerializeField] private KeyboardMapper mapper;
    [SerializeField] private NotePool pool;
    [SerializeField] private Material tapNoteMaterial;
    [SerializeField] private Material holdNoteMaterial;

    private float songClock = 0f;
    private bool isPlaying = false;
    private int nextNoteIdx = 0;
    private List<NoteObject> activeNotes = new();

    // Pre‑compute how many seconds before hit to spawn note
    private float SpawnLeadTime => spawnDistance / noteSpeed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void StartSong(SongData song = null)
    {
        if (song != null) currentSong = song;
        if (currentSong == null) { Debug.LogWarning("No SongData assigned!"); return; }
        songClock = -SpawnLeadTime;
        nextNoteIdx = 0;
        isPlaying = true;
        activeNotes.Clear();
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

        while (nextNoteIdx < currentSong.notes.Count)
        {
            var ev = currentSong.notes[nextNoteIdx];
            float hitTime = currentSong.BeatToSeconds(ev.beatTime) + timingOffset;
            if (songClock >= hitTime - SpawnLeadTime)
            {
                SpawnNote(ev, hitTime);
                nextNoteIdx++;
            }
            else break;
        }
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

        // Movement direction: from back of keyboard to front (player)
        Vector3 moveDir = mapper.transform.forward;   // must point toward the player

        NoteObject note = pool.Get();
        note.transform.SetParent(null);
        note.Initialise(lane, moveDir, noteSpeed, spawnDistance, hitTime, noteType, holdSec, mat);
        activeNotes.Add(note);
    }

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