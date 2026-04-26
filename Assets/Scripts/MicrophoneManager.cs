using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

public class MicrophoneManager : MonoBehaviour
{
    [Header("Audio Settings")]
    public int sampleRate = 48000;
    public int fftWindowSize = 8192;
    public bool enablePlayback = false;
    public bool calculateRMS = false;

    private float[] audioBuffer;
    private AudioClip micClip;
    private string selectedDevice;

    private bool isRecording = false;
    private bool isInitializing = false;
    private int retryCount = 0;
    private const int MAX_RETRIES = 3;
    // ... inside MicropohoneManager class, add these fields at the top:
    private List<int> lastLoggedNotes = new List<int>();
    private float nextLogTime = 0f;

    public float[] GetAudioBuffer() => audioBuffer;

    IEnumerator Start()
    {
        Debug.Log("MicrophoneManager started");

        // Init buffer EARLY (prevents null race)
        audioBuffer = new float[fftWindowSize];

        // Request permission
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);

            while (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                yield return null;
        }

        yield return StartCoroutine(InitializeMicrophone());
    }

    IEnumerator InitializeMicrophone()
    {
        if (isInitializing) yield break;
        isInitializing = true;
        retryCount = 0;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone found!");
            isInitializing = false;
            yield break;
        }

        selectedDevice = Microphone.devices[0];
        Debug.Log($"Using microphone: {selectedDevice}");

        // Small delay before starting microphone to ensure device is ready
        yield return new WaitForSeconds(0.1f);

        yield return StartCoroutine(StartMicrophoneWithRetry());

        isInitializing = false;
    }

    IEnumerator StartMicrophoneWithRetry()
    {
        bool success = false;

        while (retryCount < MAX_RETRIES && !success)
        {
            yield return StartCoroutine(StartMicrophone());

            // Check if microphone actually started properly
            if (isRecording && micClip != null)
            {
                // Additional verification
                int testPosition = Microphone.GetPosition(selectedDevice);
                if (testPosition > 0)
                {
                    success = true;
                    break;
                }
                else
                {
                    Debug.LogWarning($"Microphone position check failed: {testPosition}");
                    StopMicrophone();
                }
            }

            if (!success)
            {
                retryCount++;
                if (retryCount < MAX_RETRIES)
                {
                    Debug.LogWarning($"Microphone start failed, retrying... ({retryCount}/{MAX_RETRIES})");
                    yield return new WaitForSeconds(0.5f); // Wait before retry

                    // Ensure previous instance is cleaned up
                    if (micClip != null)
                    {
                        Destroy(micClip);
                        micClip = null;
                    }
                }
            }
        }

        if (!success)
        {
            Debug.LogError($"Microphone failed to start after {MAX_RETRIES} attempts!");
            isRecording = false;
        }
    }

    IEnumerator StartMicrophone()
    {
        // End any existing microphone recording on this device
        if (!string.IsNullOrEmpty(selectedDevice))
        {
            Microphone.End(selectedDevice);
            yield return new WaitForSeconds(0.05f); // Small delay after ending
        }

        // Start new recording
        micClip = Microphone.Start(selectedDevice, true, 1, sampleRate);

        if (micClip == null)
        {
            Debug.LogError("Microphone.Start returned null clip!");
            yield break;
        }

        float timeout = 3f; // Increased timeout for better reliability
        float timer = 0f;
        int lastPosition = -1;
        int stableCount = 0;

        while (Microphone.GetPosition(selectedDevice) <= 0)
        {
            timer += Time.deltaTime;

            if (timer > timeout)
            {
                Debug.LogError("Microphone failed to start! Timeout reached.");
                yield break;
            }

            // Check if the clip is valid
            if (micClip == null)
            {
                Debug.LogError("Microphone clip became null during start!");
                yield break;
            }

            yield return null;
        }

        // Additional verification that microphone is actually getting data
        float verifyTimeout = 1f;
        float verifyTimer = 0f;

        while (verifyTimer < verifyTimeout)
        {
            int currentPos = Microphone.GetPosition(selectedDevice);
            if (currentPos > 0 && currentPos == lastPosition)
            {
                stableCount++;
                if (stableCount > 5) // Position hasn't changed for ~5 frames
                {
                    Debug.LogWarning("Microphone position stuck - possible failure");
                    yield break;
                }
            }
            else
            {
                stableCount = 0;
                if (currentPos > 0 && lastPosition > 0 && currentPos != lastPosition)
                {
                    // Microphone is working, we're getting data
                    break;
                }
            }

            lastPosition = currentPos;
            verifyTimer += Time.deltaTime;
            yield return null;
        }

        isRecording = true;

        if (enablePlayback)
        {
            SetupPlayback();
        }

        Debug.Log("Microphone started successfully");
    }

    void SetupPlayback()
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.clip = micClip;
        audioSource.loop = true;
        audioSource.mute = false;
        audioSource.volume = 1f;
        audioSource.Play();
    }

    void Update()
    {
        // HARD GUARDS (no assumptions allowed)
        if (!isRecording || micClip == null || audioBuffer == null || string.IsNullOrEmpty(selectedDevice))
            return;

        // Additional check: make sure the mic clip hasn't been stopped externally
        if (Microphone.GetPosition(selectedDevice) < 0)
        {
            Debug.LogWarning("Microphone position invalid, attempting restart...");
            StartCoroutine(RestartMicrophone());
            return;
        }

        int micPosition = Microphone.GetPosition(selectedDevice);
        if (micPosition < 0) return;

        int bufferSize = audioBuffer.Length;

        if (micPosition >= bufferSize)
        {
            int startPosition = micPosition - bufferSize;
            if (startPosition < 0) return;

            micClip.GetData(audioBuffer, startPosition);

            List<int> notes = AudioPitchDetector.DetectPitches(audioBuffer, sampleRate);
            if (notes.Count > 0)
            {
                // ** Testing code that can be removed later - only log if notes change to avoid spam **
                if (Time.time >= nextLogTime && !ListsEqual(notes, lastLoggedNotes))
                {
                    Debug.Log("Detected notes: " + string.Join(", ", notes));
                    lastLoggedNotes = new List<int>(notes);
                    nextLogTime = Time.time + 1f;   // max 1 log per second
                }

                // TODO: later pass these notes to your AR piano visualisation
            }

            // RMS calculation (debug)
            if (calculateRMS)
            {
                float sum = 0f;
                for (int i = 0; i < bufferSize; i++)
                    sum += audioBuffer[i] * audioBuffer[i];

                float rms = Mathf.Sqrt(sum / bufferSize);

                // Only log occasionally to avoid spam
                if (Time.frameCount % 60 == 0)
                    Debug.Log("Mic RMS: " + rms);
            }
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopMicrophone();
        }
        else
        {
            StartCoroutine(RestartMicrophone());
        }
    }

    IEnumerator RestartMicrophone()
    {
        // Wait longer for system to fully resume audio hardware
        yield return new WaitForSeconds(0.5f);

        // Clean up existing clip
        if (micClip != null)
        {
            Destroy(micClip);
            micClip = null;
        }

        // End any lingering microphone sessions
        if (!string.IsNullOrEmpty(selectedDevice))
        {
            Microphone.End(selectedDevice);
        }

        isRecording = false;

        yield return new WaitForSeconds(0.2f);

        // Re-check devices as they might have changed
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone found on resume!");
            // Keep trying for a few seconds
            float retryTimer = 0f;
            while (Microphone.devices.Length == 0 && retryTimer < 3f)
            {
                yield return new WaitForSeconds(0.5f);
                retryTimer += 0.5f;
            }

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("Still no microphone found after resume!");
                yield break;
            }
        }

        selectedDevice = Microphone.devices[0];
        Debug.Log($"Restarting microphone on resume with device: {selectedDevice}");

        yield return StartCoroutine(StartMicrophoneWithRetry());
    }

    void StopMicrophone()
    {
        if (!string.IsNullOrEmpty(selectedDevice))
        {
            Microphone.End(selectedDevice);
        }

        isRecording = false;

        // Stop any playback sources
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null && enablePlayback)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
    }

    void OnDestroy()
    {
        StopMicrophone();

        // Clean up clip
        if (micClip != null)
        {
            Destroy(micClip);
            micClip = null;
        }
    }

    // Helper method for the class:
    private bool ListsEqual(List<int> a, List<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}