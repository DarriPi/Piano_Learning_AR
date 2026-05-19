using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace PianoLearningCore
{
    /// <summary>
    /// Detects piano notes from live microphone audio using FFT with a Blackman-Harris
    /// window function.
    ///
    /// Method source:
    ///   Putranda et al. (2023) "Exploring Audio Processing in Mixed Reality to Boost
    ///   Motivation in Piano Learning", IEEE Access 11, pp. 71194-71200.
    ///   https://doi.org/10.1109/ACCESS.2023.3293250
    ///
    /// The article compared Hanning, Hamming, Blackman, and Blackman-Harris window
    /// functions across three test stages. Blackman-Harris achieved the highest accuracy
    /// (97.28%) on complex polyphonic songs, with the optimal configuration being:
    ///   FFT bin size : 8192
    ///   Sample rate  : 44100 Hz
    ///   DSP buffer   : 512  (set in Edit > Project Settings > Audio)
    ///
    /// Pipeline (per analysis frame):
    ///   Microphone AudioClip
    ///     → AudioSource.GetSpectrumData (applies Blackman-Harris window + FFT internally)
    ///     → Local-maximum peak detection (magnitude threshold)
    ///     → Parabolic interpolation for sub-bin frequency refinement
    ///     → Log₂ formula maps refined frequency → nearest MIDI note number
    ///     → OnNotesDetected event (int[] of MIDI note numbers)
    ///
    /// Attach to a GameObject that also has an AudioSource component.
    /// Call StartDetection() once the scene is ready.
    ///
    /// ANDROID / QUEST: Unity automatically adds the RECORD_AUDIO permission to the
    /// AndroidManifest when Microphone.Start() is called, but on Android 6+ you must
    /// also request the permission at runtime.  StartDetection() handles this.
    ///
    /// RECOMMENDED PROJECT SETTINGS (Edit > Project Settings > Audio):
    ///   DSP Buffer Size : Best latency  (512 samples)
    ///   System Sample Rate : 44100
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class PianoAudioDetector : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // Inspector
        // ----------------------------------------------------------------

        [Header("Microphone")]
        [Tooltip("Leave empty to use the system default microphone (recommended for Quest Pro).")]
        public string microphoneDevice = "";

        [Tooltip("Recording sample rate. The article used 44100 Hz. " +
                 "Must match or be lower than the device's supported rate.")]
        public int sampleRate = 44100;

        [Header("FFT — Putranda et al. 2023 optimal settings")]
        [Tooltip("Number of spectrum bins passed to GetSpectrumData. " +
                 "Unity uses this directly as the FFT size. 8192 matches the article's best result.\n" +
                 "Frequency resolution = (sampleRate / 2) / spectrumSize ≈ 2.69 Hz/bin at 44100 Hz.")]
        public int spectrumSize = 8192;

        [Tooltip("How often (seconds) the FFT analysis runs. " +
                 "0.05 s = 20 Hz update rate, giving ±25 ms timing precision.")]
        public float analysisInterval = 0.05f;

        [Header("Peak Detection")]
        [Range(0.00001f, 0.1f)]
        [Tooltip("Minimum spectrum magnitude for a frequency peak to be treated as a real note. " +
                 "Lower = more sensitive but more false positives from room noise. " +
                 "Raise this in noisy environments. Start with 0.001 and adjust.")]
        public float peakThreshold = 0.001f;

        [Range(1, 10)]
        [Tooltip("Maximum simultaneous notes to report. Piano chords rarely exceed 10 notes.")]
        public int maxPolyphony = 6;

        [Header("Detection Range")]
        [Tooltip("Lowest MIDI note to detect. Accuracy drops below E2 (MIDI 40) per the article — " +
                 "the 76-key keyboard's E1 (28) is at the edge of reliable detection.")]
        public int minMidiNote = NoteUtils.KEY76_LOWEST;   // E1 = 28

        [Tooltip("Highest MIDI note to detect. Accuracy also drops above A5 (MIDI 81) per the article.")]
        public int maxMidiNote = NoteUtils.KEY76_HIGHEST;  // G7 = 103

        [Header("Debug")]
        [Tooltip("Print each frame's detected notes to the Console.")]
        public bool logDetectedNotes = false;

        // ----------------------------------------------------------------
        // Events & public state
        // ----------------------------------------------------------------

        /// <summary>
        /// Fired every analysis frame (at <see cref="analysisInterval"/> rate) with the
        /// set of MIDI note numbers currently detected in the microphone audio.
        /// May be an empty array when no notes are audible.
        /// </summary>
        public event Action<int[]> OnNotesDetected;

        public bool IsRunning { get; private set; }

        /// <summary>Most recent set of detected MIDI notes (snapshot, not live).</summary>
        public int[] CurrentNotes { get; private set; } = Array.Empty<int>();

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        private AudioSource _audioSource;
        private float[]     _spectrumData;
        private float       _analysisTimer;

        // Precomputed frequency bounds to narrow the peak-search loop.
        private float _minFrequency;
        private float _maxFrequency;

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            _audioSource  = GetComponent<AudioSource>();
            _spectrumData = new float[spectrumSize];
            PrecomputeFrequencyBounds();
        }

        private void OnDestroy() => StopDetection();

        private void Update()
        {
            if (!IsRunning) return;
            _analysisTimer += Time.deltaTime;
            if (_analysisTimer >= analysisInterval)
            {
                _analysisTimer = 0f;
                AnalyzeSpectrum();
            }
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Begin microphone capture and FFT analysis.
        /// On Android/Quest, requests RECORD_AUDIO permission if not yet granted.
        /// </summary>
        public void StartDetection()
        {
            if (IsRunning) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                StartCoroutine(RequestMicPermissionThenStart());
                return;
            }
#endif
            StartMicrophone();
        }

        /// <summary>Stop microphone capture and analysis.</summary>
        public void StopDetection()
        {
            if (!IsRunning) return;
            IsRunning = false;

            string device = NullIfEmpty(microphoneDevice);
            if (Microphone.IsRecording(device))
                Microphone.End(device);

            if (_audioSource.isPlaying)
                _audioSource.Stop();

            CurrentNotes = Array.Empty<int>();
        }

        // ----------------------------------------------------------------
        // Microphone startup
        // ----------------------------------------------------------------

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator RequestMicPermissionThenStart()
        {
            Permission.RequestUserPermission(Permission.Microphone);
            // Wait a frame for the dialog to resolve.
            yield return null;
            yield return new WaitUntil(
                () => Permission.HasUserAuthorizedPermission(Permission.Microphone));
            StartMicrophone();
        }
#endif

        private void StartMicrophone()
        {
            string device = NullIfEmpty(microphoneDevice);

            // Record into a 1-second looping clip; we read it via GetSpectrumData, not GetData.
            AudioClip micClip = Microphone.Start(device, true, 1, sampleRate);
            if (micClip == null)
            {
                Debug.LogError("[PianoAudioDetector] Microphone.Start() returned null. " +
                               "Ensure a microphone is connected and RECORD_AUDIO is granted.", this);
                return;
            }

            _audioSource.clip   = micClip;
            _audioSource.loop   = true;
            _audioSource.mute   = true;  // prevent mic feedback through the headset speakers
            _audioSource.Play();

            IsRunning = true;
            Debug.Log($"[PianoAudioDetector] Started. device='{device ?? "default"}' " +
                      $"spectrumSize={spectrumSize} threshold={peakThreshold} " +
                      $"interval={analysisInterval:F3}s");
        }

        // ----------------------------------------------------------------
        // FFT analysis — core of the Putranda et al. method
        // ----------------------------------------------------------------

        private void AnalyzeSpectrum()
        {
            // Step 1 — FFT with Blackman-Harris window (Unity handles windowing internally).
            // This is the key finding of the article: Blackman-Harris outperforms Hanning,
            // Hamming, and plain Blackman, achieving 97.28% accuracy on complex songs.
            _audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);

            // Step 2 — Compute Hz per bin.
            // Unity's spectrum covers [0, outputSampleRate/2] across spectrumSize bins.
            float hzPerBin = AudioSettings.outputSampleRate * 0.5f / spectrumSize;

            // Convert the detection frequency range to bin indices.
            int minBin = Mathf.Clamp(FrequencyToBin(_minFrequency, hzPerBin), 1, spectrumSize - 2);
            int maxBin = Mathf.Clamp(FrequencyToBin(_maxFrequency, hzPerBin), 1, spectrumSize - 2);

            // Step 3 — Find local-maximum peaks above the magnitude threshold.
            var peaks = new List<(float frequency, float magnitude)>(maxPolyphony * 2);

            for (int i = minBin; i <= maxBin; i++)
            {
                float mag = _spectrumData[i];
                if (mag < peakThreshold)                          continue; // below noise floor
                if (mag <= _spectrumData[i - 1])                 continue; // not a local max (left)
                if (mag <= _spectrumData[i + 1])                 continue; // not a local max (right)

                // Step 4 — Parabolic interpolation for sub-bin frequency accuracy.
                // Formula from Liu (2012) as cited by Putranda et al.
                // Given peak at bin k: refined offset p = 0.5*(α−γ) / (α − 2β + γ)
                float alpha = _spectrumData[i - 1];
                float beta  = mag;
                float gamma = _spectrumData[i + 1];
                float denom = alpha - 2f * beta + gamma;
                float refinedBin = (Mathf.Abs(denom) < 1e-10f)
                    ? i
                    : i + 0.5f * (alpha - gamma) / denom;

                peaks.Add((refinedBin * hzPerBin, mag));
            }

            // Keep only the strongest peaks up to the polyphony limit.
            peaks.Sort((a, b) => b.magnitude.CompareTo(a.magnitude));
            if (peaks.Count > maxPolyphony)
                peaks.RemoveRange(maxPolyphony, peaks.Count - maxPolyphony);

            // Step 5 — Map each peak frequency to the nearest MIDI note.
            // Standard formula: MIDI = 69 + 12 * log₂(f / 440)
            var noteSet = new HashSet<int>(maxPolyphony);
            foreach (var (freq, _) in peaks)
            {
                int midi = FrequencyToMidi(freq);
                if (midi >= minMidiNote && midi <= maxMidiNote)
                    noteSet.Add(midi);
            }

            int[] notes = new int[noteSet.Count];
            noteSet.CopyTo(notes);
            CurrentNotes = notes;

            if (logDetectedNotes && notes.Length > 0)
            {
                var sb = new System.Text.StringBuilder("[PianoAudioDetector] ");
                foreach (int n in notes)
                    sb.Append(NoteUtils.GetNoteName(n)).Append(' ');
                Debug.Log(sb.ToString());
            }

            OnNotesDetected?.Invoke(notes);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private void PrecomputeFrequencyBounds()
        {
            // Add a semitone of margin on each side so edge notes aren't clipped.
            _minFrequency = MidiToFrequency(Mathf.Max(0,   minMidiNote - 1));
            _maxFrequency = MidiToFrequency(Mathf.Min(127, maxMidiNote + 1));
        }

        private static float MidiToFrequency(int midi)
            => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        private static int FrequencyToMidi(float frequency)
        {
            if (frequency <= 0f) return -1;
            // Inverse of the equal-temperament formula: MIDI = 69 + 12 * log₂(f / 440)
            return Mathf.RoundToInt(69f + 12f * Mathf.Log(frequency / 440f, 2f));
        }

        private static int FrequencyToBin(float frequency, float hzPerBin)
            => Mathf.RoundToInt(frequency / hzPerBin);

        private static string NullIfEmpty(string s)
            => string.IsNullOrEmpty(s) ? null : s;
    }
}
