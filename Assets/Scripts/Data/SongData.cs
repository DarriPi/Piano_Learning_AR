using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class NoteEvent
{
    public int midiNote; /// 28 to 103, mimics 76 key keyboard
    public float beatTime; // when to hit in beats
    public float duration; // 0 = tap note; >0 hold note (in beats)
}

[CreateAssetMenu(fileName = "NewSong", menuName = "ARPiano/Song Data")]
public class SongData : ScriptableObject
{
    public string songName = "Untitled";
    public float bpm = 120f;
    public List<NoteEvent> notes = new List<NoteEvent>();
    public float BeatToSeconds(float beat)
    {
        return beat * (60f / bpm);
    }
    public float TotalDurationSeconds => notes.Count > 0 ? BeatToSeconds(notes[^1].beatTime + notes[^1].duration + 2f) : 0f;
}
