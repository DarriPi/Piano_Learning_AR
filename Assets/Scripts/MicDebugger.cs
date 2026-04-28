using UnityEngine;
using UnityEngine.UI;

public class MicDebugger : MonoBehaviour
{
    public MicrophoneManager micManager;
    public Text statusText;  // Optional: add UI Text

    private float lastVolume = 0f;
    private float lastLogTime = 0f;

    void Start()
    {
        if (micManager == null)
            micManager = FindFirstObjectByType<MicrophoneManager>();

        Debug.Log("=== MIC DEBUGGER STARTED ===");
        Debug.Log($"MicrophoneManager found: {micManager != null}");
    }

    void Update()
    {
        if (micManager == null) return;

        float[] samples = micManager.GetAudioBuffer();

        if (samples != null)
        {
            // Calculate volume (RMS)
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            lastVolume = Mathf.Sqrt(sum / samples.Length);

            // Log every 2 seconds if sound is detected
            if (lastVolume > 0.02f && Time.time - lastLogTime > 2f)
            {
                Debug.Log($"🔊 SOUND DETECTED! Volume: {lastVolume:F4}");
                lastLogTime = Time.time;
            }

            // Update UI if available
            if (statusText != null)
            {
                if (lastVolume > 0.02f)
                    statusText.text = $"🎵 Playing! Volume: {lastVolume:F3}";
                else
                    statusText.text = $"🔇 Silent... Volume: {lastVolume:F3}";
            }
        }
        else
        {
            if (statusText != null)
                statusText.text = "❌ No audio buffer yet...";
        }
    }
}