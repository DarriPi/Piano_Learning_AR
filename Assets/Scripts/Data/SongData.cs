using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class NoteEvent
{
    public int   midiNote;  // 28–103  (E1–G7)
    public float beatTime;  // Beat on which the note should be hit
    public float duration;  // 0 = tap; >0 = hold (in beats)
}

/// <summary>
/// ScriptableObject that holds all data for one song.
/// Create via: Assets ▶ Create ▶ ARPiano ▶ Song Data
/// </summary>
[CreateAssetMenu(fileName = "NewSong", menuName = "ARPiano/Song Data")]
public class SongData : ScriptableObject
{
    public string           songName = "Untitled";
    public float            bpm      = 80f;
    public List<NoteEvent>  notes    = new();

    /// <summary>Converts a beat number to wall-clock seconds.</summary>
    public float BeatToSeconds(float beat) => beat * (60f / bpm);

    /// <summary>Total song length, padded by two beats after the last note.</summary>
    public float TotalDurationSeconds =>
        notes.Count > 0
            ? BeatToSeconds(notes[^1].beatTime + notes[^1].duration + 2f)
            : 0f;
}