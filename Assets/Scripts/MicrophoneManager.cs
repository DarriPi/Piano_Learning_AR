using UnityEngine;
using UnityEngine.Android;

public class MicrophoneManager : MonoBehaviour
{
    [Header("Audio Settings")]
    public int sampleRate = 48000;  // Quest 3 mic supports 48kHz
    public int fftWindowSize = 8192; // Power of 2 for FFT
    public float[] audioBuffer;

    private AudioClip micClip;
    private string selectedDevice;
    private bool isRecording = false;

    // Public property to access current audio data
    public float[] GetAudioBuffer() => audioBuffer;

    void Start()
    {
        UnityEngine.Debug.Log("You wrote to the console! This is a test message to confirm that the console is working properly.");
        // Get the default microphone device
        if (Microphone.devices.Length > 0)
        {
            selectedDevice = Microphone.devices[0];
            UnityEngine.Debug.Log($"Using microphone: {selectedDevice}");

            // ** Start recording loop. I changed the buffer to 10 for now tfor testing purposes
            micClip = Microphone.Start(selectedDevice, true, 1, sampleRate);
            while (Microphone.GetPosition(selectedDevice) <= 0) { }
            isRecording = true;

            // Initialize buffer
            audioBuffer = new float[fftWindowSize];

            // ** Play the microphone audio through an AudioSource for testing purposes, you can remove this later
            AudioSource source = GetComponent<AudioSource>();
            source.clip = micClip;
            source.loop = true;
            source.Play();
        }
        else
        {
            Debug.LogError("No microphone found on Quest 3!");
        }
    }

    void Update()
    {
        if (!isRecording) return;

        int micPosition = Microphone.GetPosition(selectedDevice);
        int bufferSize = audioBuffer.Length;

        // Check if we have enough new data
        if (micPosition >= bufferSize)
        {
            // Copy the latest audio chunk
            int startPosition = micPosition - bufferSize;
            if (startPosition < 0) return;

            micClip.GetData(audioBuffer, startPosition);

            // * * Calculate volume (RMS) NOTE: This is for testing wheter the audio data is being captured correctly, you can remove this later
            float sum = 0f;
            for (int i = 0; i < audioBuffer.Length; i++)
            {
                sum += audioBuffer[i] * audioBuffer[i];
            }

            float rms = Mathf.Sqrt(sum / audioBuffer.Length);
            Debug.Log("Mic RMS: " + rms);
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && isRecording)
        {
            Microphone.End(selectedDevice);
            isRecording = false;
        }
        else if (!pauseStatus && !isRecording)
        {
            micClip = Microphone.Start(selectedDevice, false, 1, sampleRate);
            while (Microphone.GetPosition(selectedDevice) <= 0) { }
            isRecording = true;
        }
    }

    void OnDestroy()
    {
        if (isRecording)
            Microphone.End(selectedDevice);
    }
}