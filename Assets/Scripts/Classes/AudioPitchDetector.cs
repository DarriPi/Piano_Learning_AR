// AudioPitchDetector.cs
using System.Collections.Generic;
using UnityEngine;

public static class AudioPitchDetector
{
    // Blackman-Harris window constants
    private const float A0 = 0.35875f;
    private const float A1 = 0.48829f;
    private const float A2 = 0.14128f;
    private const float A3 = 0.01168f;

    // ---- TUNABLE PARAMETERS ----

    /// <summary>Use automatic gain control. If false, manualGain is applied.</summary>
    public static bool useAGC = true;

    /// <summary>Static gain multiplier when AGC is disabled.</summary>
    public static float manualGain = 5f;

    /// <summary>Target RMS level for AGC normalisation.</summary>
    public static float targetRMS = 0.05f;

    /// <summary>Gate threshold = noiseFloor * noiseFloorMultiplier. Raised for Quest 3 mic.</summary>
    public static float noiseFloorMultiplier = 6f;

    /// <summary>
    /// Fractional frequency tolerance for the harmonic sieve (absolute, not scaled).
    /// Slightly looser than default to account for piano string inharmonicity.
    /// </summary>
    public static float harmonicTolerance = 0.04f;

    /// <summary>
    /// Only check harmonics 2–5. Prevents high partials of bass notes from
    /// suppressing real treble notes, and makes the sieve much more aggressive.
    /// </summary>
    public static int maxHarmonicCheck = 5;

    /// <summary>
    /// Maximum number of fundamentals to accept per frame.
    /// A pianist rarely plays more than 10 simultaneous notes.
    /// </summary>
    public static int maxFundamentals = 10;

    /// <summary>Minimum duration (seconds) a note must be continuously present before being reported.</summary>
    public static float minNoteDurationSec = 0.08f;

    /// <summary>Minimum spacing between accepted spectral peaks, in FFT bins.</summary>
    public static int minPeakDistanceBins = 3;

    /// <summary>Peaks whose magnitude is below (maxMagnitude * relPeakThreshold) are rejected.</summary>
    public static float relPeakThreshold = 0.30f;

    /// <summary>Absolute magnitude floor after gain – guards against near-zero peaks.</summary>
    public static float absMinMagnitude = 0.02f;

    /// <summary>
    /// When true, detected MIDI note numbers and their names are printed to the Unity console
    /// each frame that notes are present.
    /// </summary>
    public static bool logMidiToConsole = true;

    // ---- INTERNAL STATE ----
    private static float _noiseFloor = 0.0001f;
    private const float NoiseFloorAlpha = 0.02f;

    // Maps MIDI note number → Time.time when it was first detected
    private static Dictionary<int, float> _activeNotes = new Dictionary<int, float>();

    // Note name lookup (C = 0, C# = 1, … B = 11)
    private static readonly string[] NoteNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // -------------------------------------------------------------------------

    /// <summary>
    /// Detects all sustained pitches present in <paramref name="buffer"/>.
    /// Tuned for piano (88-key range, A0–C8) via Meta Quest 3 microphone.
    /// </summary>
    /// <param name="buffer">PCM audio samples (mono, normalised to ±1).</param>
    /// <param name="sampleRate">Sample rate of <paramref name="buffer"/> in Hz.</param>
    /// <returns>List of MIDI note numbers (21–108) for notes that have been
    /// continuously present for at least <see cref="minNoteDurationSec"/>.</returns>
    public static List<int> DetectPitches(float[] buffer, int sampleRate)
    {
        float timeNow = Time.time;

        // ── 1. Zero-pad to next power of two ──────────────────────────────────
        int N = NextPowerOfTwo(buffer.Length);
        float[] padded = new float[N];
        System.Array.Copy(buffer, padded, buffer.Length);

        // ── 2. Gain stage ─────────────────────────────────────────────────────
        float rawRMS = ComputeRMS(padded, N);
        if (rawRMS < 1e-9f) rawRMS = 1e-9f;

        float gain = useAGC ? (targetRMS / rawRMS) : manualGain;
        for (int i = 0; i < N; i++) padded[i] *= gain;

        float processedRMS = ComputeRMS(padded, N);

        // ── 3. Adaptive noise floor (updated from gained signal when quiet) ───
        if (processedRMS < _noiseFloor * noiseFloorMultiplier * 1.2f)
            _noiseFloor = NoiseFloorAlpha * processedRMS + (1f - NoiseFloorAlpha) * _noiseFloor;

        // ── 4. Noise gate ─────────────────────────────────────────────────────
        if (processedRMS < _noiseFloor * noiseFloorMultiplier)
        {
            _activeNotes.Clear();
            Debug.Log($"[PitchDetector] Silence – RMS(proc)={processedRMS:F6}, noiseFloor={_noiseFloor:F6}");
            return new List<int>();
        }

        // ── 5. Window + FFT ───────────────────────────────────────────────────
        float[] real = new float[N];
        float[] imag = new float[N];
        for (int i = 0; i < N; i++)
            real[i] = padded[i] * BlackmanHarrisWindow(i, N);

        FFT(real, imag, false);

        // ── 6. Build linear-magnitude spectrum ────────────────────────────────
        // Zero out bins below A0 (27.5 Hz) so they don't drag down maxMag and
        // inflate the relative threshold — important for the Quest 3's low-freq
        // handling noise bump.
        int minBin = Mathf.CeilToInt(27.5f * N / (float)sampleRate);
        float[] magnitude = new float[N / 2];
        float maxMag = 0f;

        for (int i = 1; i < N / 2; i++)
        {
            if (i < minBin) { magnitude[i] = 0f; continue; }
            magnitude[i] = Mathf.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
            if (magnitude[i] > maxMag) maxMag = magnitude[i];
        }

        // ── 7. Peak picking ───────────────────────────────────────────────────
        float threshold = Mathf.Max(maxMag * relPeakThreshold, absMinMagnitude);
        List<Peak> peaks = new List<Peak>();

        for (int i = minPeakDistanceBins; i < N / 2 - minPeakDistanceBins; i++)
        {
            if (magnitude[i] < threshold) continue;

            bool isPeak = true;
            for (int d = 1; d <= minPeakDistanceBins; d++)
            {
                if (magnitude[i] <= magnitude[i - d] || magnitude[i] <= magnitude[i + d])
                {
                    isPeak = false;
                    break;
                }
            }
            if (!isPeak) continue;

            // Parabolic interpolation for sub-bin frequency accuracy
            float alpha = magnitude[i - 1];
            float beta  = magnitude[i];
            float gamma = magnitude[i + 1];
            float p     = 0.5f * (alpha - gamma) / (alpha - 2f * beta + gamma + 1e-10f);
            float freq  = (i + p) * sampleRate / (float)N;

            // Piano range: A0 (27.5 Hz) – C8 (4186 Hz)
            if (freq >= 27.5f && freq <= 4186f)
                peaks.Add(new Peak { frequency = freq, magnitude = beta, bin = i });
        }

        // Sort strongest first so the sieve promotes the loudest candidates
        peaks.Sort((a, b) => b.magnitude.CompareTo(a.magnitude));

        // ── 8. Harmonic sieve ─────────────────────────────────────────────────
        // Only checks harmonics 2–maxHarmonicCheck to prevent high bass partials
        // from suppressing real treble notes. Accounts for piano inharmonicity
        // via the slightly looser harmonicTolerance (0.04).
        List<Peak> fundamentals = new List<Peak>();
        foreach (Peak candidate in peaks)
        {
            if (fundamentals.Count >= maxFundamentals) break;

            bool isHarmonic = false;
            foreach (Peak fundamental in fundamentals)
            {
                float ratio      = candidate.frequency / fundamental.frequency;
                int   nearestInt = Mathf.RoundToInt(ratio);
                if (nearestInt >= 2 && nearestInt <= maxHarmonicCheck &&
                    Mathf.Abs(ratio - nearestInt) < harmonicTolerance)
                {
                    isHarmonic = true;
                    break;
                }
            }
            if (!isHarmonic)
                fundamentals.Add(candidate);
        }

        // ── 9. Convert fundamentals to MIDI note numbers ──────────────────────
        List<int> midiNotes = new List<int>(fundamentals.Count);
        foreach (Peak f in fundamentals)
        {
            int midi = Mathf.RoundToInt(12f * Mathf.Log(f.frequency / 440f, 2f) + 69f);
            midiNotes.Add(Mathf.Clamp(midi, 21, 108)); // clamp to 88-key piano range
        }

        // ── 10. Temporal tracking – enforce minNoteDurationSec ────────────────
        Dictionary<int, float> updatedNotes = new Dictionary<int, float>(_activeNotes.Count);
        foreach (var kvp in _activeNotes)
        {
            if (midiNotes.Contains(kvp.Key))
                updatedNotes[kvp.Key] = kvp.Value;
        }
        foreach (int note in midiNotes)
        {
            if (!updatedNotes.ContainsKey(note))
                updatedNotes[note] = timeNow;
        }
        _activeNotes = updatedNotes;

        List<int> result = new List<int>();
        foreach (var kvp in _activeNotes)
        {
            if (timeNow - kvp.Value >= minNoteDurationSec)
                result.Add(kvp.Key);
        }

        // ── 11. Optional MIDI console output ──────────────────────────────────
        if (logMidiToConsole && result.Count > 0)
            LogMidiNotes(result);

        Debug.Log($"[PitchDetector] Peaks={peaks.Count}, Fundamentals={fundamentals.Count}, Reported={result.Count}");

        return result;
    }

    // ── Public utility ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a MIDI note number to a human-readable name such as "C4" or "F#3".
    /// </summary>
    public static string MidiToNoteName(int midi)
    {
        int    octave = (midi / 12) - 1;
        string name   = NoteNames[midi % 12];
        return $"{name}{octave}";
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void LogMidiNotes(List<int> notes)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder("[PitchDetector] Notes: ");
        for (int i = 0; i < notes.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(notes[i]);
            sb.Append(" (");
            sb.Append(MidiToNoteName(notes[i]));
            sb.Append(')');
        }
        Debug.Log(sb.ToString());
    }

    private static float ComputeRMS(float[] samples, int count)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
        return Mathf.Sqrt(sum / count);
    }

    private struct Peak { public float frequency; public float magnitude; public int bin; }

    private static int NextPowerOfTwo(int n)
    {
        int pow = 1;
        while (pow < n) pow <<= 1;
        return pow;
    }

    private static float BlackmanHarrisWindow(int n, int N)
    {
        float factor = 2f * Mathf.PI * n / (N - 1);
        return A0 - A1 * Mathf.Cos(factor) + A2 * Mathf.Cos(2f * factor) - A3 * Mathf.Cos(3f * factor);
    }

    private static void FFT(float[] real, float[] imag, bool inverse)
    {
        int n    = real.Length;
        int bits = (int)Mathf.Log(n, 2);

        // Bit-reversal permutation
        for (int i = 0; i < n; i++)
        {
            int j = ReverseBits(i, bits);
            if (j > i)
            {
                float tmpR = real[i]; real[i] = real[j]; real[j] = tmpR;
                float tmpI = imag[i]; imag[i] = imag[j]; imag[j] = tmpI;
            }
        }

        // Cooley–Tukey butterfly
        for (int len = 2; len <= n; len <<= 1)
        {
            int   halfLen = len >> 1;
            float angle   = 2f * Mathf.PI / len * (inverse ? -1f : 1f);
            float wRe     = Mathf.Cos(angle);
            float wIm     = Mathf.Sin(angle);

            for (int i = 0; i < n; i += len)
            {
                float curRe = 1f, curIm = 0f;
                for (int j = 0; j < halfLen; j++)
                {
                    int   even = i + j;
                    int   odd  = even + halfLen;
                    float tRe  = curRe * real[odd] - curIm * imag[odd];
                    float tIm  = curRe * imag[odd] + curIm * real[odd];
                    real[odd]  = real[even] - tRe;
                    imag[odd]  = imag[even] - tIm;
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
            for (int i = 0; i < n; i++) { real[i] /= n; imag[i] /= n; }
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
}