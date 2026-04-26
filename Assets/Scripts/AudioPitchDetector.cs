// AudioPitchDetector.cs
using System.Collections.Generic;
using UnityEngine;

public static class AudioPitchDetector
{
    // Blackman-Harris 4-term window constants
    private const float A0 = 0.35875f;
    private const float A1 = 0.48829f;
    private const float A2 = 0.14128f;
    private const float A3 = 0.01168f;

    /// <summary>
    /// Detects pitches from raw audio and returns MIDI note numbers.
    /// </summary>
    /// <param name="buffer">Time‑domain samples (length must be a power of two).</param>
    /// <param name="sampleRate">Sample rate used for the microphone.</param>
    /// <param name="thresholdRelative">Peaks below this fraction of the max magnitude are ignored (0.2 = 20%).</param>
    /// <returns>List of detected MIDI note numbers (60 = C4).</returns>
    public static List<int> DetectPitches(float[] buffer, int sampleRate, float thresholdRelative = 0.2f)
    {
        int N = buffer.Length;
        if ((N & (N - 1)) != 0)
        {
            Debug.LogError("Buffer length must be a power of two for radix-2 FFT");
            return new List<int>();
        }

        // 1. Apply Blackman-Harris window
        float[] windowed = new float[N];
        for (int i = 0; i < N; i++)
            windowed[i] = buffer[i] * BlackmanHarrisWindow(i, N);

        // 2. Prepare real/imaginary arrays (copy windowed data to real part)
        float[] real = new float[N];
        float[] imag = new float[N];
        System.Array.Copy(windowed, real, N);

        // 3. Forward FFT (in-place, radix-2)
        FFT(real, imag, false);

        // 4. Compute magnitude spectrum (first half only)
        float[] magnitude = new float[N / 2];
        float maxMag = 0f;
        for (int i = 0; i < N / 2; i++)
        {
            float mag = Mathf.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
            magnitude[i] = mag;
            if (mag > maxMag) maxMag = mag;
        }

        if (maxMag < 1e-6f) return new List<int>();

        float threshold = maxMag * thresholdRelative;

        // 5. Peak picking with parabolic interpolation
        List<(float freq, float mag)> peaks = new List<(float, float)>();
        for (int k = 1; k < N / 2 - 1; k++)
        {
            if (magnitude[k] > threshold &&
                magnitude[k] >= magnitude[k - 1] &&
                magnitude[k] >= magnitude[k + 1])
            {
                float alpha = magnitude[k - 1];
                float beta = magnitude[k];
                float gamma = magnitude[k + 1];
                float p = 0.5f * (alpha - gamma) / (alpha - 2f * beta + gamma);
                float refinedBin = k + p;
                float freq = refinedBin * sampleRate / N;

                // Keep only piano range (A0=27.5 Hz to C8≈4186 Hz)
                if (freq >= 27.5f && freq <= 4186.0f)
                    peaks.Add((freq, beta));
            }
        }

        // 6. Convert to MIDI note numbers
        List<(int note, float freq, float mag)> noteCandidates = new List<(int, float, float)>();
        foreach (var peak in peaks)
        {
            int midi = Mathf.RoundToInt(12f * Mathf.Log(peak.freq / 440f, 2f) + 69f);
            noteCandidates.Add((midi, peak.freq, peak.mag));
        }

        // 7. Remove overtones (harmonic filter)
        List<int> finalNotes = RemoveOvertones(noteCandidates);
        return finalNotes;
    }

    // ---------- Helper Methods ----------
    private static float BlackmanHarrisWindow(int n, int N)
    {
        float factor = 2f * Mathf.PI * n / (N - 1);
        return A0 - A1 * Mathf.Cos(factor) + A2 * Mathf.Cos(2f * factor) - A3 * Mathf.Cos(3f * factor);
    }

    // In‑place radix‑2 FFT (Cooley‑Tukey)
    private static void FFT(float[] real, float[] imag, bool inverse)
    {
        int n = real.Length;
        int bits = (int)Mathf.Log(n, 2);
        // Bit‑reversal permutation
        for (int i = 0; i < n; i++)
        {
            int j = ReverseBits(i, bits);
            if (j > i)
            {
                float tmpR = real[i]; real[i] = real[j]; real[j] = tmpR;
                float tmpI = imag[i]; imag[i] = imag[j]; imag[j] = tmpI;
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            int halfLen = len >> 1;
            float angle = 2f * Mathf.PI / len * (inverse ? -1f : 1f);
            float wRe = Mathf.Cos(angle);
            float wIm = Mathf.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                float curRe = 1f, curIm = 0f;
                for (int j = 0; j < halfLen; j++)
                {
                    int even = i + j;
                    int odd = even + halfLen;
                    float tRe = curRe * real[odd] - curIm * imag[odd];
                    float tIm = curRe * imag[odd] + curIm * real[odd];
                    real[odd] = real[even] - tRe;
                    imag[odd] = imag[even] - tIm;
                    real[even] += tRe;
                    imag[even] += tIm;

                    float nextRe = curRe * wRe - curIm * wIm;
                    float nextIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                    curIm = nextIm;
                }
            }
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                real[i] /= n;
                imag[i] /= n;
            }
        }
    }

    private static int ReverseBits(int x, int bits)
    {
        int rev = 0;
        for (int i = 0; i < bits; i++)
        {
            rev = (rev << 1) | (x & 1);
            x >>= 1;
        }
        return rev;
    }

    // Remove higher note if it lies at an integer frequency ratio and is weaker (likely a harmonic)
    private static List<int> RemoveOvertones(List<(int note, float freq, float mag)> candidates)
    {
        candidates.Sort((a, b) => a.freq.CompareTo(b.freq));
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            for (int j = 0; j < i; j++)
            {
                float ratio = candidates[i].freq / candidates[j].freq;
                float closestInt = Mathf.Round(ratio);
                if (Mathf.Abs(ratio - closestInt) < 0.03f && candidates[i].mag < candidates[j].mag * 0.7f)
                {
                    candidates.RemoveAt(i);
                    break;
                }
            }
        }
        List<int> result = new List<int>();
        foreach (var c in candidates) result.Add(c.note);
        return result;
    }
}