using UnityEngine;
using System;

// Stub: used when no real audio detection is integrated
// Forwards InputDetector events directly to the NoteSpawner hit check
public class StubAudioDetector : MonoBehaviour, IAudioDetector
{
    public event Action<int, float> OnNoteDetected;

    void OnEnable()
    {
        if (InputDetector.Instance != null)
            InputDetector.Instance.OnNoteInput += HandleSimulatedInput;
    }

    void OnDisable()
    {
        if (InputDetector.Instance != null)
            InputDetector.Instance.OnNoteInput -= HandleSimulatedInput;
    }

    private void HandleSimulatedInput(int midi, bool isDown)
    {
        if (isDown) OnNoteDetected?.Invoke(midi, 1.0f);
    }

    public void StartListening() => Debug.Log("[StubAudio] Listening (stub).");
    public void StopListening() => Debug.Log("[StubAudio] Stopped (stub).");
}