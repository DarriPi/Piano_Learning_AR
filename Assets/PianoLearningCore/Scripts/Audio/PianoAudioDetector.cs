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
    /// Detects piano notes from live microphone audio.
    ///
    /// Method source:
    ///   Putranda et al. (2023) "Exploring Audio Processing in Mixed Reality to Boost
    ///   Motivation in Piano Learning", IEEE Access 11, pp. 71194-71200.
    ///   https://doi.org/10.1109/ACCESS.2023.3293250
    ///
    /// Article's pipeline (validated at 97.28% accuracy on complex songs):
    ///     Microphone -> Blackman-Harris window -> FFT (8192 bins) -> peak detection ->
    ///     parabolic interpolation -> MIDI mapping
    ///
    /// THIS implementation follows that pipeline exactly, then adds three improvements
    /// that the article explicitly lists as future work in its Discussion section:
    ///
    ///   (1) Harmonic suppression. Piano notes have strong overtones at 2x, 3x, 4x...
    ///       the fundamental. Without this, every C4 also reports a phantom C5, G5, C6
    ///       etc. The article notes "the presence of harmonics was considered input,
    ///       which could lead to additional validation". We suppress peaks that line up
    ///       with integer multiples of a stronger peak.
    ///
    ///   (2) Per-note state machine. The article fires a detection every analysis frame
    ///       (~20 Hz), so a held note triggers ~20 events per second. We track per-MIDI
    ///       "active" and "silent" frame counters and fire exactly one OnNoteOn when a
    ///       note transitions from silent->sounding, and one OnNoteOff when it goes back.
    ///
    ///   (3) Microphone warm-up. Microphone.Start() returns a clip that's still being
    ///       filled; the first ~0.3 s reads silence and produces garbage. We delay the
    ///       first analysis until the buffer is primed.
    ///
    /// Recommended Unity project settings (Edit > Project Settings > Audio):
    ///     DSP Buffer Size   : Best latency  (512 samples, per the article)
    ///     System Sample Rate: 44100         (or leave 0 = device default)
    ///
    /// Attach to any GameObject; an AudioSource is added automatically.
    /// Call StartDetection() once your scene is ready. On Quest/Android the
    /// RECORD_AUDIO permission is requested at runtime.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class PianoAudioDetector : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // Inspector — Microphone
        // ----------------------------------------------------------------

        [Header("Microphone")]
        [Tooltip("Leave empty to use the system default mic (recommended on Quest Pro). " +
                 "In the editor, you can paste a device name from Microphone.devices.")]
        public string microphoneDevice = "";

        [Tooltip("Recording sample rate. The article used 44100 Hz. " +
                 "If your device doesn't support 44100, set to 0 to use the device default.")]
        public int sampleRate = 44100;

        // ----------------------------------------------------------------
        // Inspector — FFT (Putranda et al. 2023 optimal settings)
        // ----------------------------------------------------------------

        [Header("FFT — article's optimal settings")]
        [Tooltip("Number of spectrum bins. Unity uses this as the FFT size. " +
                 "8192 matches the article's best result.\n" +
                 "Resolution = (sampleRate / 2) / spectrumSize ~= 2.69 Hz/bin at 44100 Hz.")]
        public int spectrumSize = 8192;

        [Range(0.015f, 0.2f)]
        [Tooltip("How often the FFT analysis runs (seconds). " +
                 "0.025 s = 40 Hz, giving ~25 ms timing precision. " +
                 "Lower = lower latency, slightly higher CPU.")]
        public float analysisInterval = 0.025f;

        // ----------------------------------------------------------------
        // Inspector — Peak detection
        // ----------------------------------------------------------------

        [Header("Peak Detection")]
        [Range(0.00001f, 0.1f)]
        [Tooltip("Minimum spectrum magnitude for a peak to be treated as a real note. " +
                 "Lower = more sensitive to quiet notes but more false positives from " +
                 "room noise. Raise in noisy environments. Start at 0.005 and tune.")]
        public float peakThreshold = 0.005f;

        [Range(1, 10)]
        [Tooltip("Maximum simultaneous notes to report per frame. Piano chords rarely exceed 6.")]
        public int maxPolyphony = 6;

        // ----------------------------------------------------------------
        // Inspector — Detection range
        // ----------------------------------------------------------------

        [Header("Detection Range")]
        [Tooltip("Lowest MIDI note to detect. Per the article, accuracy drops below E2 (40); " +
                 "the 76-key keyboard's E1 (28) is at the edge of reliable detection.")]
        public int minMidiNote = NoteUtils.KEY76_LOWEST;

        [Tooltip("Highest MIDI note to detect. Per the article, accuracy also drops above A5 (81).")]
        public int maxMidiNote = NoteUtils.KEY76_HIGHEST;

        // ----------------------------------------------------------------
        // Inspector — Harmonic suppression (NEW — improves on article)
        // ----------------------------------------------------------------

        [Header("Harmonic Suppression  (improves on article's method)")]
        [Tooltip("Drop peaks that line up with an integer multiple (2x..N x) of a stronger peak. " +
                 "Without this, every detected note will also produce phantom octave-up notes. " +
                 "Strongly recommended ON.")]
        public bool suppressHarmonics = true;

        [Range(2, 8)]
        [Tooltip("How many harmonics to check (2x..N x). 5 covers up through the 5th harmonic, " +
                 "which is sufficient for piano (above that the harmonic energy is usually below threshold).")]
        public int harmonicCheckCount = 5;

        [Range(0.005f, 0.05f)]
        [Tooltip("How close a peak's frequency must be to an exact harmonic to be flagged. " +
                 "0.03 = within 3% of N x fundamental.")]
        public float harmonicTolerance = 0.03f;

        [Range(0.1f, 1f)]
        [Tooltip("Only suppress a harmonic if its magnitude is BELOW this fraction of the " +
                 "fundamental's magnitude. 0.8 = if the candidate is weaker than 80% of the " +
                 "fundamental, it's treated as a harmonic. " +
                 "Keep this < 1 so an actual octave chord (where two real notes are both strong) " +
                 "still gets detected.")]
        public float harmonicMagnitudeRatio = 0.8f;

        // ----------------------------------------------------------------
        // Inspector — Note state machine (NEW)
        // ----------------------------------------------------------------

        [Header("Note State Machine  (debouncing)")]
        [Range(1, 6)]
        [Tooltip("How many consecutive analysis frames a note must be detected before OnNoteOn fires. " +
                 "2 frames @ 40 Hz = 50 ms, which filters out transient noise but still feels instant.")]
        public int onsetFrames = 2;

        [Range(1, 12)]
        [Tooltip("How many consecutive silent frames before OnNoteOff fires. " +
                 "4 frames @ 40 Hz = 100 ms hold time, which smooths over brief dropouts " +
                 "between FFT windows when a note is sustained.")]
        public int offsetFrames = 4;

        // ----------------------------------------------------------------
        // Inspector — Debug
        // ----------------------------------------------------------------

        [Header("Debug")]
        [Tooltip("Print one line to the Console per OnNoteOn / OnNoteOff event.")]
        public bool logNoteEvents = false;

        [Tooltip("Print every analysis frame's raw detections (very noisy — use only when " +
                 "investigating false positives).")]
        public bool logRawDetections = false;

        [Tooltip("Once per second, log a one-line 'health check' showing whether the mic is " +
                 "recording, the AudioSource is playing, and the peak spectrum magnitude this " +
                 "frame.\n\n" +
                 "Use this when nothing seems to detect:\n" +
                 "  maxMag = 0  -> the audio pipeline is dead (mic permission, wrong device, or muted source)\n" +
                 "  maxMag > 0 but < threshold -> raise the mic input volume or lower peakThreshold\n" +
                 "  maxMag > threshold but no ON events -> detection range or harmonic settings are off")]
        public bool logHealthCheck = false;

        // ----------------------------------------------------------------
        // Events
        // ----------------------------------------------------------------

        /// <summary>
        /// Fires exactly once when a note transitions from silent -> sounding.
        /// THIS is the event game logic (scoring, feedback) should listen to.
        /// </summary>
        public event Action<int> OnNoteOn;

        /// <summary>Fires exactly once when a note has been silent for offsetFrames analyses.</summary>
        public event Action<int> OnNoteOff;

        /// <summary>
        /// Fires every analysis frame with the raw set of currently-detected MIDI notes
        /// (after harmonic suppression, before debouncing). Useful for visualisations
        /// like a live spectrum display. Most game logic should use OnNoteOn instead.
        /// </summary>
        public event Action<int[]> OnNotesDetected;

        // ----------------------------------------------------------------
        // Public read-only state
        // ----------------------------------------------------------------

        public bool IsRunning { get; private set; }

        /// <summary>MIDI numbers currently considered "on" by the state machine.</summary>
        public IReadOnlyCollection<int> ActiveNotes => _activeNotes;

        /// <summary>Latest raw detection (snapshot, post-harmonic-suppression).</summary>
        public int[] CurrentNotes { get; private set; } = Array.Empty<int>();

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        private AudioSource _audioSource;
        private float[] _spectrumData;
        private float _analysisTimer;
        private float _warmupTimer;
        private float _healthCheckTimer;
        private float _healthCheckPeakMag;
        private int   _healthCheckPeakBin;
        private const float WarmupSeconds = 0.3f;

        // Per-MIDI counters (0..127)
        private int[] _activeFrameCount;
        private int[] _silentFrameCount;
        private bool[] _noteIsOn;
        private readonly HashSet<int> _activeNotes = new HashSet<int>();

        // Cached frequency bounds (so we don't search the whole spectrum every frame).
        private float _minFrequency;
        private float _maxFrequency;

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            _spectrumData = new float[Mathf.Max(64, spectrumSize)];
            _activeFrameCount = new int[128];
            _silentFrameCount = new int[128];
            _noteIsOn = new bool[128];
            PrecomputeFrequencyBounds();
        }

        private void OnDestroy() => StopDetection();

        /// <summary>
        /// Called by Unity's audio system once per audio buffer for the AudioSource on this
        /// GameObject. We zero the buffer so the mic input never reaches the AudioListener —
        /// this is how we keep the source at full volume (so GetSpectrumData works) without
        /// the user hearing mic feedback through the headset.
        /// Runs on the audio thread, NOT the main thread.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!IsRunning) return;
            Array.Clear(data, 0, data.Length);
        }

        private void Update()
        {
            if (!IsRunning) return;

            // Skip analysis until the mic clip is primed.
            if (_warmupTimer > 0f) { _warmupTimer -= Time.deltaTime; return; }

            _analysisTimer += Time.deltaTime;
            if (_analysisTimer >= analysisInterval)
            {
                _analysisTimer = 0f;
                AnalyzeSpectrum();
            }

            if (logHealthCheck)
            {
                // Track the loudest bin seen across all analysis frames in this 1-second window,
                // skipping the DC bin (0) which is dominated by mic offset, not signal.
                if (_spectrumData != null)
                {
                    for (int i = 1; i < _spectrumData.Length; i++)
                    {
                        if (_spectrumData[i] > _healthCheckPeakMag)
                        {
                            _healthCheckPeakMag = _spectrumData[i];
                            _healthCheckPeakBin = i;
                        }
                    }
                }

                _healthCheckTimer += Time.deltaTime;
                if (_healthCheckTimer >= 1f)
                {
                    _healthCheckTimer = 0f;
                    LogHealthCheck();
                    _healthCheckPeakMag = 0f;
                    _healthCheckPeakBin = 0;
                }
            }
        }

        private void LogHealthCheck()
        {
            string device = NullIfEmpty(microphoneDevice);
            bool micRecording = Microphone.IsRecording(device);
            bool sourcePlaying = _audioSource != null && _audioSource.isPlaying;
            int micPos = Microphone.GetPosition(device);

            float hzPerBin = AudioSettings.outputSampleRate * 0.5f / Mathf.Max(1, spectrumSize);
            float peakFreq = _healthCheckPeakBin * hzPerBin;

            Debug.Log($"[PianoAudioDetector] health: micRecording={micRecording} " +
                      $"sourcePlaying={sourcePlaying} micPos={micPos} " +
                      $"activeNotes={_activeNotes.Count} " +
                      $"peakMag(1s)={_healthCheckPeakMag:G3} peakFreq={peakFreq:F0}Hz " +
                      $"threshold={peakThreshold:G3}");
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Begin microphone capture and FFT analysis. Idempotent — calling twice does nothing.
        /// On Android/Quest, requests RECORD_AUDIO permission if not yet granted; the actual
        /// microphone start happens inside the PermissionGranted callback.
        /// </summary>
        public void StartDetection()
        {
            if (IsRunning) return;
            Debug.Log("[PianoAudioDetector] StartDetection called");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Debug.Log("[PianoAudioDetector] Mic permission already granted.");
                StartMicrophone();
                return;
            }

            Debug.Log("[PianoAudioDetector] Mic permission not yet granted — requesting...");
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                Debug.Log("[PianoAudioDetector] Mic permission GRANTED by user.");
                StartMicrophone();
            };
            callbacks.PermissionDenied += _ =>
            {
                Debug.LogError("[PianoAudioDetector] Mic permission DENIED. " +
                               "Open Quest Settings > Apps > [this app] > Permissions > Microphone and enable it, then restart the app.");
            };
            callbacks.PermissionDeniedAndDontAskAgain += _ =>
            {
                Debug.LogError("[PianoAudioDetector] Mic permission DENIED permanently (\"don't ask again\"). " +
                               "Open Quest Settings > Apps > [this app] > Permissions > Microphone and enable it manually, then restart the app.");
            };
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
            return;
#else
            StartMicrophone();
#endif
        }

        /// <summary>
        /// Stop microphone capture and analysis. Fires OnNoteOff for any currently-active
        /// notes so listeners can clean up.
        /// </summary>
        public void StopDetection()
        {
            if (!IsRunning) return;
            IsRunning = false;

            string device = NullIfEmpty(microphoneDevice);
            if (Microphone.IsRecording(device)) Microphone.End(device);
            if (_audioSource != null && _audioSource.isPlaying) _audioSource.Stop();

            // Cleanly turn off any notes still considered "on".
            if (_activeNotes.Count > 0)
            {
                // Copy first because OnNoteOff handlers may mutate state.
                int[] snapshot = new int[_activeNotes.Count];
                _activeNotes.CopyTo(snapshot);
                _activeNotes.Clear();
                foreach (int midi in snapshot) OnNoteOff?.Invoke(midi);
            }

            Array.Clear(_activeFrameCount, 0, _activeFrameCount.Length);
            Array.Clear(_silentFrameCount, 0, _silentFrameCount.Length);
            Array.Clear(_noteIsOn, 0, _noteIsOn.Length);
            CurrentNotes = Array.Empty<int>();
        }

        // ----------------------------------------------------------------
        // Microphone startup
        // ----------------------------------------------------------------

        private void StartMicrophone()
        {
            string device = NullIfEmpty(microphoneDevice);

            // Helpful diagnostic — many Quest issues come down to "no mic device".
            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[PianoAudioDetector] No microphone devices found. " +
                               "On Quest, check RECORD_AUDIO permission and that the headset's mic is enabled.", this);
                return;
            }

            if (device != null && !DeviceExists(device))
            {
                Debug.LogError($"[PianoAudioDetector] Microphone '{device}' not found. " +
                               $"Available devices: {string.Join(", ", Microphone.devices)}. " +
                               "Leave 'microphoneDevice' blank to use the system default.", this);
                return;
            }

            int requestedRate = sampleRate > 0 ? sampleRate : 0; // 0 = device default
            AudioClip micClip = Microphone.Start(device, true, 1, requestedRate);
            if (micClip == null)
            {
                Debug.LogError("[PianoAudioDetector] Microphone.Start() returned null. " +
                               "Check RECORD_AUDIO permission.", this);
                return;
            }

            _audioSource.clip = micClip;
            _audioSource.loop = true;
            // We keep the source at FULL volume so its DSP graph is processed every frame,
            // which is what GetSpectrumData() reads from. On Quest specifically, both
            // mute = true and volume = 0 cause Unity to skip processing the source, so the
            // spectrum returns all zeros (we observed peakMag = 2.78E-08 at the Nyquist bin
            // — pure float noise on a zero input).
            //
            // To prevent audible mic feedback through the headset, we implement
            // OnAudioFilterRead below, which zeroes the source's output samples AFTER the
            // spectrum has already been captured but BEFORE they reach the AudioListener.
            _audioSource.mute = false;
            _audioSource.volume = 1f;
            _audioSource.Play();

            // Recreate spectrum buffer if user changed spectrumSize at edit time.
            if (_spectrumData == null || _spectrumData.Length != spectrumSize)
                _spectrumData = new float[spectrumSize];

            PrecomputeFrequencyBounds();
            _warmupTimer = WarmupSeconds;
            _analysisTimer = 0f;
            IsRunning = true;

            Debug.Log($"[PianoAudioDetector] Started. device='{device ?? "default"}' " +
                      $"micRate={micClip.frequency} outputRate={AudioSettings.outputSampleRate} " +
                      $"spectrumSize={spectrumSize} threshold={peakThreshold} " +
                      $"interval={analysisInterval*1000f:F0}ms suppressHarmonics={suppressHarmonics}");
            Debug.Log($"[PianoAudioDetector] Available mic devices: {string.Join(", ", Microphone.devices)}");
        }

        // ----------------------------------------------------------------
        // FFT analysis — core of the Putranda et al. method
        // ----------------------------------------------------------------

        private void AnalyzeSpectrum()
        {
            // ----- Step 1: FFT with Blackman-Harris window -----
            // Unity applies the window internally before the FFT. This is the
            // article's chosen window (97.28% accuracy on complex songs).
            _audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);

            // Unity's spectrum covers [0, outputSampleRate/2] across spectrumSize bins.
            // GetSpectrumData reads from the AudioSource's *output* (post-resample),
            // so we use outputSampleRate here, not the mic's recording rate.
            float hzPerBin = AudioSettings.outputSampleRate * 0.5f / spectrumSize;

            int minBin = Mathf.Clamp(FrequencyToBin(_minFrequency, hzPerBin), 1, spectrumSize - 2);
            int maxBin = Mathf.Clamp(FrequencyToBin(_maxFrequency, hzPerBin), 1, spectrumSize - 2);

            // ----- Step 2: Find local-maximum peaks above the magnitude threshold -----
            var peaks = new List<Peak>(maxPolyphony * 4);

            for (int i = minBin; i <= maxBin; i++)
            {
                float mag = _spectrumData[i];
                if (mag < peakThreshold) continue;
                if (mag <= _spectrumData[i - 1]) continue; // not a local max (left)
                if (mag <= _spectrumData[i + 1]) continue; // not a local max (right)

                // ----- Step 3: Parabolic interpolation for sub-bin frequency accuracy -----
                // Liu (2012) formula, as cited by Putranda et al.
                //   refined offset p = 0.5 * (a - c) / (a - 2b + c)
                float a = _spectrumData[i - 1];
                float b = mag;
                float c = _spectrumData[i + 1];
                float denom = a - 2f * b + c;
                float refinedBin = (Mathf.Abs(denom) < 1e-10f)
                    ? i
                    : i + 0.5f * (a - c) / denom;

                peaks.Add(new Peak(refinedBin * hzPerBin, mag));
            }

            // Sort by magnitude descending. Used for both harmonic suppression
            // (strongest fundamentals win) and for trimming to maxPolyphony.
            peaks.Sort((x, y) => y.Magnitude.CompareTo(x.Magnitude));

            // ----- Step 4: Harmonic suppression (NEW — improves on article) -----
            if (suppressHarmonics)
                SuppressHarmonics(peaks);

            // ----- Step 5: Trim to polyphony limit -----
            if (peaks.Count > maxPolyphony)
                peaks.RemoveRange(maxPolyphony, peaks.Count - maxPolyphony);

            // ----- Step 6: Map peaks to MIDI notes -----
            // Standard equal-temperament formula: MIDI = 69 + 12 * log2(f / 440)
            var detectedThisFrame = new HashSet<int>();
            foreach (var p in peaks)
            {
                int midi = FrequencyToMidi(p.Frequency);
                if (midi >= minMidiNote && midi <= maxMidiNote)
                    detectedThisFrame.Add(midi);
            }

            int[] detectedArr = new int[detectedThisFrame.Count];
            detectedThisFrame.CopyTo(detectedArr);
            CurrentNotes = detectedArr;

            if (logRawDetections)
                Debug.Log($"[PianoAudioDetector] raw: {NotesToString(detectedArr)}");

            OnNotesDetected?.Invoke(detectedArr);

            // ----- Step 7: Update per-note state machine (NEW) -----
            UpdateNoteStates(detectedThisFrame);
        }

        // ----------------------------------------------------------------
        // Harmonic suppression
        // ----------------------------------------------------------------

        /// <summary>
        /// Walks the peak list strongest-first. For each peak treated as a fundamental,
        /// removes any WEAKER peaks whose frequency is approximately N * fundamental for
        /// N in [2..harmonicCheckCount].
        ///
        /// We require the harmonic to be weaker than the fundamental by
        /// (1 - harmonicMagnitudeRatio) so that genuine octave chords aren't suppressed.
        /// </summary>
        private void SuppressHarmonics(List<Peak> peaks)
        {
            for (int i = 0; i < peaks.Count; i++)
            {
                float fundamental = peaks[i].Frequency;
                if (fundamental <= 0f) continue;
                float fundamentalMag = peaks[i].Magnitude;

                // Iterate backwards so removals don't shift indices we still care about.
                for (int j = peaks.Count - 1; j > i; j--)
                {
                    float candidate = peaks[j].Frequency;
                    float candidateMag = peaks[j].Magnitude;

                    float ratio = candidate / fundamental;
                    int nearestInt = Mathf.RoundToInt(ratio);
                    if (nearestInt < 2 || nearestInt > harmonicCheckCount) continue;

                    if (Mathf.Abs(ratio - nearestInt) > harmonicTolerance) continue;

                    // It's at ~N x fundamental. Treat as a harmonic only if weaker.
                    if (candidateMag < fundamentalMag * harmonicMagnitudeRatio)
                        peaks.RemoveAt(j);
                }
            }
        }

        // ----------------------------------------------------------------
        // Note state machine
        // ----------------------------------------------------------------

        /// <summary>
        /// Updates per-MIDI active/silent frame counters and fires OnNoteOn / OnNoteOff
        /// on transitions. Debouncing prevents noise spikes from creating false events.
        /// </summary>
        private void UpdateNoteStates(HashSet<int> detectedThisFrame)
        {
            for (int midi = minMidiNote; midi <= maxMidiNote; midi++)
            {
                if (detectedThisFrame.Contains(midi))
                {
                    _activeFrameCount[midi]++;
                    _silentFrameCount[midi] = 0;

                    if (!_noteIsOn[midi] && _activeFrameCount[midi] >= onsetFrames)
                    {
                        _noteIsOn[midi] = true;
                        _activeNotes.Add(midi);
                        if (logNoteEvents)
                            Debug.Log($"[PianoAudioDetector] ON  {NoteUtils.GetNoteName(midi)} (MIDI {midi})");
                        OnNoteOn?.Invoke(midi);
                    }
                }
                else
                {
                    _silentFrameCount[midi]++;
                    _activeFrameCount[midi] = 0;

                    if (_noteIsOn[midi] && _silentFrameCount[midi] >= offsetFrames)
                    {
                        _noteIsOn[midi] = false;
                        _activeNotes.Remove(midi);
                        if (logNoteEvents)
                            Debug.Log($"[PianoAudioDetector] OFF {NoteUtils.GetNoteName(midi)} (MIDI {midi})");
                        OnNoteOff?.Invoke(midi);
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private void PrecomputeFrequencyBounds()
        {
            // Pad by one semitone each side so edge notes aren't clipped by rounding.
            _minFrequency = MidiToFrequency(Mathf.Max(0,   minMidiNote - 1));
            _maxFrequency = MidiToFrequency(Mathf.Min(127, maxMidiNote + 1));
        }

        private static float MidiToFrequency(int midi)
            => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        private static int FrequencyToMidi(float frequency)
            => (frequency <= 0f) ? -1 : Mathf.RoundToInt(69f + 12f * Mathf.Log(frequency / 440f, 2f));

        private static int FrequencyToBin(float frequency, float hzPerBin)
            => Mathf.RoundToInt(frequency / hzPerBin);

        private static string NullIfEmpty(string s)
            => string.IsNullOrEmpty(s) ? null : s;

        private static bool DeviceExists(string name)
        {
            foreach (var d in Microphone.devices) if (d == name) return true;
            return false;
        }

        private static string NotesToString(int[] notes)
        {
            if (notes == null || notes.Length == 0) return "<silent>";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < notes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(NoteUtils.GetNoteName(notes[i]));
            }
            return sb.ToString();
        }

        private readonly struct Peak
        {
            public readonly float Frequency;
            public readonly float Magnitude;
            public Peak(float frequency, float magnitude)
            {
                Frequency = frequency;
                Magnitude = magnitude;
            }
        }
    }
}
