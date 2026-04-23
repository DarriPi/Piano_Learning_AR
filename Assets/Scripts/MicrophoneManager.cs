using System.Collections;
using UnityEngine;
using UnityEngine.Android;

public class MicrophoneManager : MonoBehaviour
{
    [Header("Audio Settings")]
    public int sampleRate = 48000;  // Quest 3 mic supports 48kHz
    public int fftWindowSize = 8192; // Power of 2 for FFT
    public float[] audioBuffer;
    public bool enablePlayback = false; // Set to true to hear the microphone audio (for testing)
    private AudioClip micClip;
    private string selectedDevice;
    private bool isRecording = false;

    // Public property to access current audio data
    public float[] GetAudioBuffer() => audioBuffer;

    IEnumerator Start()
    {
        Debug.Log("MicrophoneManager started");

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);

            // Wait until permission is granted
            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                yield return null;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone found!");
            yield break;
        }

        selectedDevice = Microphone.devices[0];
        Debug.Log($"Using microphone: {selectedDevice}");

        yield return StartCoroutine(StartMicrophone());
    }

    IEnumerator StartMicrophone()
    {
        micClip = Microphone.Start(selectedDevice, true, 1, sampleRate);

        float timeout = 1f;
        float timer = 0f;

        while (Microphone.GetPosition(selectedDevice) <= 0)
        {
            timer += Time.deltaTime;
            if (timer > timeout)
            {
                Debug.LogError("Microphone failed to start!");
                yield break;
            }
            yield return null;
        }

        audioBuffer = new float[fftWindowSize];
        isRecording = true;

        // ** Check if we have an AudioSource, if not add one to play the microphone audio (for testing purposes, you can remove this later)

        if (enablePlayback)
        {
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.clip = micClip;
            audioSource.loop = true;
            audioSource.mute = false;
            audioSource.Play();
            audioSource.volume = 1f;
        }

        Debug.Log("Microphone started successfully");
    }

    /*
    * * This method is for testing purposes to play back the recorded audio. You can call this method with the current audio buffer to hear what is being captured by the microphone. Note that this will play the most recent chunk of audio data, so it may not be perfectly in sync with real-time recording.
    */
    // private void PlayClip(AudioClip clip)
    // {
    //     AudioSource audioSource = GetComponent<AudioSource>();

    //     if (audioSource == null)
    //     {
    //         audioSource = gameObject.AddComponent<AudioSource>();
    //     }

    //     audioSource.clip = clip;
    //     audioSource.Play();
    // }

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