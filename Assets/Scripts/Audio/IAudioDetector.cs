using System;
public interface IAudioDetector
{
    event Action<int, float> OnNoteDetected; // (midiNote, confidence)
    void StartListening();
    void StopListening();
}
