using UnityEngine;
using System;
using System.Collections;

public class PitchDetector : MonoBehaviour, IAudioDetector
{
    public event Action<int, float> OnNoteDetected;

    [Header("Audio Settings")]
    [SerializeField] private int sampleRate = 44100;
    [SerializeField] private int fftSize = 4096;
    [SerializeField] private float detectionInterval = 0.02f;  // 20ms = 50Hz
    [SerializeField] private float minConfidence = 0.7f;

    [Header("Detection Settings")]
    [SerializeField] private float minFrequency = 41.2f;   // E1 (MIDI 28)
    [SerializeField] private float maxFrequency = 3136f;   // G7 (MIDI 103)
    [SerializeField] private float silenceThreshold = 0.02f; // Ignore background noise

    private AudioClip micClip;
    private float[] samples;
    private float[] spectrum;
    private int lastMidi = -1;
    private bool isRunning = false;

    public void StartListening()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[PitchDetector] No microphone found.");
            return;
        }

        samples = new float[fftSize];
        spectrum = new float[fftSize];
        micClip = Microphone.Start(null, true, 1, sampleRate);
        isRunning = true;
        StartCoroutine(DetectionLoop());

        Debug.Log("[PitchDetector] Started microphone.");
    }

    public void StopListening()
    {
        isRunning = false;
        Microphone.End(null);
        Debug.Log("[PitchDetector] Stopped microphone.");
    }

    private IEnumerator DetectionLoop()
    {
        // Wait until microphone has started
        while (Microphone.GetPosition(null) <= 0)
            yield return null;

        while (isRunning)
        {
            yield return new WaitForSeconds(detectionInterval);

            int midi = DetectPitch(out float confidence);

            if (midi >= 0 && confidence >= minConfidence)
            {
                if (midi != lastMidi)
                {
                    lastMidi = midi;
                    OnNoteDetected?.Invoke(midi, confidence);
                    Debug.Log($"[PitchDetector] Note detected: MIDI {midi} " +
                              $"({MidiToNoteName(midi)}) Confidence: {confidence:F2}");
                }
            }
            else
            {
                // Reset last midi when silence detected
                lastMidi = -1;
            }
        }
    }

    private int DetectPitch(out float confidence)
    {
        confidence = 0f;

        // Get current microphone position
        int micPosition = Microphone.GetPosition(null) - fftSize;
        if (micPosition < 0) return -1;

        // Read samples from microphone clip
        micClip.GetData(samples, micPosition);

        // Check if signal is loud enough (not silence)
        float rms = CalculateRMS(samples);
        if (rms < silenceThreshold) return -1;

        // Apply Blackman-Harris window to reduce spectral leakage
        ApplyBlackmanHarrisWindow(samples);

        // Copy samples to spectrum array for FFT
        Array.Copy(samples, spectrum, fftSize);

        // Use Unity's built-in FFT via AudioListener
        // Note: For better accuracy, replace with DSPLib FFT
        AudioListener.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

        // Find peak frequency in spectrum
        float peakFrequency = FindPeakFrequency(spectrum, out confidence);

        // Filter out frequencies outside piano range
        if (peakFrequency < minFrequency || peakFrequency > maxFrequency)
            return -1;

        // Convert Hz to MIDI note number
        int midi = FrequencyToMidi(peakFrequency);

        return midi;
    }

    private float CalculateRMS(float[] audioSamples)
    {
        float sum = 0f;
        foreach (float s in audioSamples)
            sum += s * s;
        return Mathf.Sqrt(sum / audioSamples.Length);
    }

    private void ApplyBlackmanHarrisWindow(float[] audioSamples)
    {
        int n = audioSamples.Length;
        for (int i = 0; i < n; i++)
        {
            // Blackman-Harris window coefficients
            float window = 0.35875f
                - 0.48829f * Mathf.Cos(2f * Mathf.PI * i / (n - 1))
                + 0.14128f * Mathf.Cos(4f * Mathf.PI * i / (n - 1))
                - 0.01168f * Mathf.Cos(6f * Mathf.PI * i / (n - 1));

            audioSamples[i] *= window;
        }
    }

    private float FindPeakFrequency(float[] spectrumData, out float confidence)
    {
        float maxAmplitude = 0f;
        int peakIndex = 0;

        // Search only within piano frequency range
        int minIndex = FrequencyToSpectrumIndex(minFrequency);
        int maxIndex = FrequencyToSpectrumIndex(maxFrequency);

        // Clamp to spectrum bounds
        minIndex = Mathf.Clamp(minIndex, 0, spectrumData.Length - 1);
        maxIndex = Mathf.Clamp(maxIndex, 0, spectrumData.Length - 1);

        for (int i = minIndex; i <= maxIndex; i++)
        {
            if (spectrumData[i] > maxAmplitude)
            {
                maxAmplitude = spectrumData[i];
                peakIndex = i;
            }
        }

        // Use parabolic interpolation for more accurate peak frequency
        float refinedIndex = ParabolicInterpolation(spectrumData, peakIndex);

        // Convert index to frequency
        float frequency = refinedIndex * sampleRate / fftSize;

        // Confidence based on peak amplitude relative to average
        float avgAmplitude = CalculateAverageAmplitude(spectrumData, minIndex, maxIndex);
        confidence = avgAmplitude > 0f
            ? Mathf.Clamp01(maxAmplitude / (avgAmplitude * 10f))
            : 0f;

        return frequency;
    }

    private float ParabolicInterpolation(float[] spectrumData, int peakIndex)
    {
        // Refine peak position using neighbouring samples
        if (peakIndex <= 0 || peakIndex >= spectrumData.Length - 1)
            return peakIndex;

        float left   = spectrumData[peakIndex - 1];
        float center = spectrumData[peakIndex];
        float right  = spectrumData[peakIndex + 1];

        float delta = 0.5f * (right - left) / (2f * center - left - right);
        return peakIndex + delta;
    }

    private float CalculateAverageAmplitude(float[] spectrumData, int minIdx, int maxIdx)
    {
        float sum = 0f;
        int count = maxIdx - minIdx + 1;
        for (int i = minIdx; i <= maxIdx; i++)
            sum += spectrumData[i];
        return count > 0 ? sum / count : 0f;
    }

    private int FrequencyToSpectrumIndex(float frequency)
    {
        return Mathf.RoundToInt(frequency * fftSize / sampleRate);
    }

    private int FrequencyToMidi(float hz)
    {
        if (hz < 20f) return -1;

        // Standard MIDI conversion formula
        int midi = Mathf.RoundToInt(12f * Mathf.Log(hz / 440f, 2f) + 69f);

        // Clamp to 76-key keyboard range (E1=28 to G7=103)
        return (midi >= 28 && midi <= 103) ? midi : -1;
    }

    private string MidiToNoteName(int midi)
    {
        string[] names = { "C","C#","D","D#","E","F","F#","G","G#","A","A#","B" };
        int octave = (midi / 12) - 1;
        return names[midi % 12] + octave;
    }
}