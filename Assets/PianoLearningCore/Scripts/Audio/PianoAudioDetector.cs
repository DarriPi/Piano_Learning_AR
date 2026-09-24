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
        /// <summary>
        /// Which Android audio-capture preset to record from on Quest. Unity's Microphone API on
        /// Quest does NOT expose physical mics by location; Microphone.devices instead lists these
        /// capture presets, each routing to different mic(s) and applying different built-in DSP.
        /// See the 'preferredAudioSource' tooltip for what each one is good for.
        /// </summary>
        public enum MicAudioSource { Default, Camcorder, VoiceRecognition }

        // ----------------------------------------------------------------
        // Inspector — Microphone
        // ----------------------------------------------------------------

        [Header("Microphone")]
        [Tooltip("Which Quest audio-capture preset to record from. On Quest, Unity does NOT expose " +
                 "physical mics by location — Microphone.devices lists Android capture presets, each " +
                 "routing to different mic(s) with different built-in processing:\n" +
                 "  - Camcorder: records the room with minimal voice processing. BEST for piano (this " +
                 "is the forward/ambient capture path).\n" +
                 "  - Default: the comms/voice mic (heavy noise-suppression + AGC + echo-cancel). " +
                 "Tuned for speech; mangles sustained musical tones.\n" +
                 "  - VoiceRecognition: ASR preset (usually no AGC); a fallback to try if Camcorder " +
                 "underperforms in your room.\n" +
                 "Ignored on desktop/editor (uses the system default mic) and when 'Microphone Device' " +
                 "below is set explicitly.")]
        public MicAudioSource preferredAudioSource = MicAudioSource.Camcorder;

        [Tooltip("Optional explicit device-name override. Leave EMPTY to use 'Preferred Audio Source' " +
                 "above (recommended). If set, this exact name from Microphone.devices is used verbatim " +
                 "— handy in the editor where you can paste a real device name.")]
        public string microphoneDevice = "";

        [Header("Microphone — Quest native capture")]
        [Tooltip("Quest only: capture the mic through Android's AudioRecord directly so we can use the " +
                 "unprocessed/camcorder source (far better for piano) instead of Unity's only device — " +
                 "the voice-processed 'Android audio input'. If native capture fails for any reason we " +
                 "automatically fall back to UnityEngine.Microphone. No effect in the editor.")]
        public bool useNativeCapture = true;

        [Tooltip("Quest native capture: which Android source to prefer. Auto tries the least-processed " +
                 "first (Unprocessed -> Camcorder -> Mic). Use the named values to force one for A/B " +
                 "testing — we still fall back to the others if the device refuses it.")]
        public NativeMicSource nativeSourcePreference = NativeMicSource.Auto;

        [Tooltip("Quest native own-FFT window length in samples (power of 2). Smaller = lower latency " +
                 "but coarser low-note resolution. 8192 @ 48 kHz ~= 170 ms window / 5.9 Hz per bin " +
                 "(parabolic interpolation refines pitch). Try 4096 for snappier response, 16384 for " +
                 "finer low-end. No effect on the Unity Microphone fallback / editor.")]
        public int nativeWindowSize = 8192;

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
        // Inspector — Adaptive noise floor (NEW 2026-09-24)
        // ----------------------------------------------------------------

        [Header("Adaptive Noise Floor  (replaces the fixed threshold)")]
        [Tooltip("Judge a peak against the noise floor OF ITS OWN FREQUENCY BAND instead of one " +
                 "absolute number. " +
                 "Why: measured on device, this room's steady tone at 370 Hz is LOUDER than a quiet " +
                 "real note, so no single absolute threshold can separate them — drop the threshold " +
                 "far enough to hear the note and the room walks in with it (61% of detections were " +
                 "false positives). A per-band floor separates them because the noise and the note " +
                 "live in different bands. " +
                 "peakThreshold still applies underneath this as an absolute never-below-this gate.")]
        public bool useAdaptiveFloor = true;

        [Range(1.5f, 20f)]
        [Tooltip("How far above its band's noise floor a peak must rise to count. 6 = ~16 dB. " +
                 "Lower = more sensitive and more phantoms; raise it if phantoms persist.")]
        public float noiseFloorRatio = 6f;

        [Range(1, 12)]
        [Tooltip("Noise-floor resolution. 4 bands per octave is fine — narrow enough that bass " +
                 "rumble doesn't raise the floor under the melody, wide enough that a single loud " +
                 "note can't raise the floor on top of itself.")]
        public int noiseFloorBandsPerOctave = 4;

        // ----------------------------------------------------------------
        // Inspector — Harmonic-sum scoring (NEW 2026-09-24)
        // ----------------------------------------------------------------

        [Header("Harmonic-Sum Scoring  (replaces peak-pick + suppression)")]
        [Tooltip("Score every candidate NOTE by the energy across its whole harmonic series, then " +
                 "claim notes one at a time, subtracting each winner's predicted partials first. " +
                 "Why this replaces 'Suppress Harmonics': that method fails in BOTH directions. " +
                 "(1) When a harmonic is LOUDER than the fundamental it sorts first, the real note " +
                 "then looks like a sub-harmonic, and neither gets removed — measured on device as " +
                 "a phantom A6 after every single A4. (2) It DELETES real notes that happen to sit " +
                 "on a harmonic of a louder one — a high melody over a loud left hand — which is " +
                 "why dense pieces lose notes and keys need pressing twice. " +
                 "It also fixes the bass: at 8192/44.1kHz a semitone at E1 is under half a bin, so " +
                 "low notes CANNOT be named from the fundamental's position — but their upper " +
                 "partials are well resolved, and this reads those.")]
        public bool useHarmonicSum = true;

        [Range(3, 10)]
        [Tooltip("How many partials to sum (1..N). 8 gives the bass enough resolved partials to be " +
                 "identified at all. Unlike the old suppression, a higher count here is SAFE — " +
                 "partials are evidence for a note, not grounds for deleting another one.")]
        public int harmonicSumCount = 8;

        [Range(0.004f, 0.04f)]
        [Tooltip("How far from an exact multiple a partial may sit and still count. 0.012 = 1.2%, " +
                 "which absorbs piano inharmonicity (partials run sharp) up to the 8th partial " +
                 "without needing an explicit stiffness model.")]
        public float harmonicSumTolerance = 0.012f;

        [Range(0f, 1f)]
        [Tooltip("Sub-octave guard. A note's harmonics are a superset of the octave ABOVE it, so " +
                 "naive scoring always drifts an octave down. If a candidate's ODD partials " +
                 "(1x, 3x, 5x) are weaker than this fraction of its EVEN ones (2x, 4x, 6x), the " +
                 "energy really belongs to the octave up and we shift. 0.25 is a safe start.")]
        public float subOctaveOddRatio = 0.25f;

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
        private QuestMicCapture _questMic; // Quest native AudioRecord capture; null when using Unity Microphone
        private float[] _spectrumData;

        // Stage 2 native own-FFT. Set when QuestMicCapture is active; the Microphone fallback path
        // leaves _useOwnFft false and keeps using AudioSource.GetSpectrumData.
        private bool _useOwnFft;
        private SimpleFFT _fft;
        private float[] _fftWindow;
        private int _fftSize;
        private float _lastHzPerBin; // Hz/bin of the most recent spectrum (for the health log)
        // Adaptive noise floor + harmonic-sum scratch (NEW). All preallocated: AnalyzeSpectrum
        // runs 30x a second and must not feed the GC on a 90 FPS budget.
        private float[] _bandFloor;      // noise floor per band
        private float[] _bandSum, _bandSum2;  // bucketing accumulators (see ComputeNoiseFloor)
        private int[]   _bandN,   _bandN2;
        private int[]   _binBand;        // bin -> band index
        private int     _bandCount;
        private float   _bandsBuiltForHz; // hzPerBin the band table was built for
        private float[] _work;           // spectrum copy we subtract claimed partials from
        private float[] _scores;         // harmonic score per MIDI note
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

            // Pump native capture into the ring buffer (no-op unless Quest native AudioRecord is active).
            if (_questMic != null) _questMic.Poll();

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
            bool native = _questMic != null && _questMic.IsActive;
            bool micRecording = native || Microphone.IsRecording(_activeDevice);
            bool sourcePlaying = _audioSource != null && _audioSource.isPlaying;
            int micPos = native ? _questMic.BufferedSamples : Microphone.GetPosition(_activeDevice);

            float hzPerBin = _lastHzPerBin > 0f
                ? _lastHzPerBin
                : AudioSettings.outputSampleRate * 0.5f / Mathf.Max(1, spectrumSize);
            float peakFreq = _healthCheckPeakBin * hzPerBin;

            Debug.Log($"[PianoAudioDetector] health: device='{_activeDevice ?? "default"}' micRecording={micRecording} " +
                      $"sourcePlaying={sourcePlaying} pos/buf={micPos} " +
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

            if (_questMic != null)
            {
                _questMic.Stop();
                _questMic = null;
            }
            else if (Microphone.IsRecording(_activeDevice))
            {
                Microphone.End(_activeDevice);
            }
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

        // The device we actually opened, after resolving preferredAudioSource / the override.
        // null means "system default". StopDetection / LogHealthCheck must use THIS — not the
        // inspector field — or they'd query the wrong device when a preset was chosen.
        private string _activeDevice;

        private void StartMicrophone()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Preferred path on Quest: capture Android's unprocessed/camcorder source directly and run
            // our OWN FFT on the freshest samples (Stage 2). This bypasses the AudioSource +
            // GetSpectrumData bridge entirely — no streaming AudioClip, no playback buffering — which
            // is what kept Stage 1's detection ~0.9 s behind real time.
            if (useNativeCapture && TryStartNativeCapture())
            {
                FinishStart();
                Debug.Log($"[PianoAudioDetector] Started (native own-FFT). device='{_activeDevice}' " +
                          $"micRate={_questMic.SampleRate} fftSize={_fftSize} bins={_fftSize / 2} " +
                          $"hzPerBin={(float)_questMic.SampleRate / _fftSize:F2} " +
                          $"window={1000f * _fftSize / _questMic.SampleRate:F0}ms " +
                          $"threshold={peakThreshold} interval={analysisInterval * 1000f:F0}ms");
                return;
            }
#endif

            // Fallback path: UnityEngine.Microphone + AudioSource + GetSpectrumData
            // (editor/desktop, or if native capture failed).
            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[PianoAudioDetector] No microphone devices found. " +
                               "On Quest, check RECORD_AUDIO permission and that the headset's mic is enabled.", this);
                return;
            }

            _activeDevice = ResolveMicDevice();
            int requestedRate = sampleRate > 0 ? sampleRate : 0; // 0 = device default
            AudioClip micClip = Microphone.Start(_activeDevice, true, 1, requestedRate);
            if (micClip == null)
            {
                Debug.LogError("[PianoAudioDetector] Microphone.Start() returned null. " +
                               "Check RECORD_AUDIO permission.", this);
                return;
            }

            // Keep the source at FULL volume so its DSP graph is processed every frame (that's what
            // GetSpectrumData reads); OnAudioFilterRead zeroes the output afterwards so the mic isn't
            // heard through the headset. (The native path above doesn't route audio out at all.)
            _audioSource.clip = micClip;
            _audioSource.loop = true;
            _audioSource.mute = false;
            _audioSource.volume = 1f;
            _audioSource.Play();

            if (_spectrumData == null || _spectrumData.Length != spectrumSize)
                _spectrumData = new float[spectrumSize];
            _useOwnFft = false;

            FinishStart();

            Debug.Log($"[PianoAudioDetector] Started (Unity Microphone). device='{_activeDevice ?? "default"}' " +
                      $"micRate={micClip.frequency} outputRate={AudioSettings.outputSampleRate} " +
                      $"spectrumSize={spectrumSize} threshold={peakThreshold} " +
                      $"interval={analysisInterval*1000f:F0}ms suppressHarmonics={suppressHarmonics}");
            Debug.Log($"[PianoAudioDetector] Available mic devices: {string.Join(", ", Microphone.devices)}");
        }

        /// <summary>Common startup tail shared by the native and Microphone paths.</summary>
        private void FinishStart()
        {
            PrecomputeFrequencyBounds();
            _warmupTimer = WarmupSeconds;
            _analysisTimer = 0f;
            IsRunning = true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Open native AudioRecord capture and set up our own windowed FFT over its freshest samples.
        // Returns false (with QuestMicCapture cleaned up) if capture couldn't start, so the caller
        // falls back to UnityEngine.Microphone.
        private bool TryStartNativeCapture()
        {
            _questMic = new QuestMicCapture();
            if (!_questMic.Start(sampleRate, nativeSourcePreference))
            {
                Debug.LogWarning("[PianoAudioDetector] Native capture unavailable — " +
                                 "falling back to UnityEngine.Microphone.", this);
                _questMic = null;
                return false;
            }

            _fftSize      = NextPow2(Mathf.Clamp(nativeWindowSize, 1024, 32768));
            _fft          = new SimpleFFT(_fftSize);
            _fftWindow    = new float[_fftSize];
            _spectrumData = new float[_fftSize / 2];
            _useOwnFft    = true;
            _activeDevice = "native:" + _questMic.SourceName;
            return true;
        }
#endif

        /// <summary>
        /// Decide which microphone device name to open. Priority:
        ///   1. An explicit, non-empty 'microphoneDevice' (used verbatim if it exists).
        ///   2. The 'preferredAudioSource' preset, matched against Microphone.devices by keyword
        ///      ("camcorder" / "recognition") — on Quest these read e.g. "Android camcorder input".
        /// Anything unresolved falls back to the system default (null); we never refuse to start
        /// just because a preferred preset isn't listed (e.g. in the editor, or on a future OS that
        /// renames the device). Returns null to mean "system default device".
        /// </summary>
        private string ResolveMicDevice()
        {
            // 1. Explicit override wins.
            string explicitName = NullIfEmpty(microphoneDevice);
            if (explicitName != null)
            {
                if (DeviceExists(explicitName)) return explicitName;
                Debug.LogError($"[PianoAudioDetector] Microphone override '{explicitName}' not found. " +
                               $"Available: {string.Join(", ", Microphone.devices)}. " +
                               "Clear 'Microphone Device' to use Preferred Audio Source. Using default for now.", this);
                return null;
            }

            // 2. Default preset = let Android/Unity choose (the processed comms mic).
            if (preferredAudioSource == MicAudioSource.Default) return null;

            // 3. Map the preset to a Quest device name by keyword.
            string keyword = (preferredAudioSource == MicAudioSource.Camcorder) ? "camcorder" : "recognition";
            string match = FindDeviceContaining(keyword);
            if (match != null) return match;

            Debug.LogWarning($"[PianoAudioDetector] No '{preferredAudioSource}' mic available " +
                             $"(searched for \"{keyword}\"). Available: {string.Join(", ", Microphone.devices)}. " +
                             "Falling back to the system default mic.", this);
            return null;
        }

        /// <summary>First device whose name contains <paramref name="keyword"/> (case-insensitive), or null.</summary>
        private static string FindDeviceContaining(string keyword)
        {
            foreach (var d in Microphone.devices)
                if (!string.IsNullOrEmpty(d) && d.ToLowerInvariant().Contains(keyword)) return d;
            return null;
        }

        // ----------------------------------------------------------------
        // FFT analysis — core of the Putranda et al. method
        // ----------------------------------------------------------------

        // Fill _spectrumData with a Blackman-Harris-windowed magnitude spectrum and return its
        // Hz-per-bin. Native (Stage 2) runs our own FFT over the freshest ring samples at the mic's
        // full rate — no AudioSource/playback buffering. Fallback uses Unity's GetSpectrumData.
        private float FillSpectrum()
        {
            if (_useOwnFft)
            {
                if (_questMic != null && _questMic.CopyLatest(_fftWindow))
                    _fft.MagnitudeSpectrum(_fftWindow, _spectrumData);
                else
                    Array.Clear(_spectrumData, 0, _spectrumData.Length); // not enough samples yet (warm-up)
                return (float)_questMic.SampleRate / _fftSize;
            }

            // Unity's spectrum covers [0, outputSampleRate/2]; GetSpectrumData reads the AudioSource's
            // *output* (post-resample), so the bin width uses outputSampleRate, not the mic rate.
            _audioSource.GetSpectrumData(_spectrumData, 0, FFTWindow.BlackmanHarris);
            return AudioSettings.outputSampleRate * 0.5f / _spectrumData.Length;
        }

        private void AnalyzeSpectrum()
        {
            // ----- Step 1: Blackman-Harris-windowed magnitude spectrum (source-agnostic) -----
            float hzPerBin = FillSpectrum();
            _lastHzPerBin = hzPerBin;

            int bins = _spectrumData.Length;
            int minBin = Mathf.Clamp(FrequencyToBin(_minFrequency, hzPerBin), 1, bins - 2);
            int maxBin = Mathf.Clamp(FrequencyToBin(_maxFrequency, hzPerBin), 1, bins - 2);

            // ----- Step 1b: adaptive per-band noise floor (NEW) -----
            if (useAdaptiveFloor) ComputeNoiseFloor(bins, hzPerBin, minBin, maxBin);

            // ----- Steps 2-6 via harmonic-sum scoring (NEW) -----
            if (useHarmonicSum)
            {
                int[] detectedHs = DetectByHarmonicSum(bins, hzPerBin);
                CurrentNotes = detectedHs;

                if (logRawDetections)
                    Debug.Log($"[PianoAudioDetector] raw: {NotesToString(detectedHs)}");

                OnNotesDetected?.Invoke(detectedHs);
                UpdateNoteStates(_detectedSet);
                return;
            }

            // ----- Step 2: Find local-maximum peaks above the magnitude threshold -----
            var peaks = new List<Peak>(maxPolyphony * 4);

            for (int i = minBin; i <= maxBin; i++)
            {
                float mag = _spectrumData[i];
                if (mag < ThresholdAtBin(i)) continue;
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

        private static int NextPow2(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }

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

        // ----------------------------------------------------------------
        // Adaptive noise floor (NEW 2026-09-24)
        // ----------------------------------------------------------------

        /// <summary>
        /// Minimum magnitude a bin must reach to count as signal: the larger of the absolute
        /// <see cref="peakThreshold"/> and its own band's noise floor times
        /// <see cref="noiseFloorRatio"/>.
        /// </summary>
        private float ThresholdAtBin(int bin)
        {
            if (!useAdaptiveFloor || _binBand == null || bin < 0 || bin >= _binBand.Length)
                return peakThreshold;
            float adaptive = _bandFloor[_binBand[bin]] * noiseFloorRatio;
            return adaptive > peakThreshold ? adaptive : peakThreshold;
        }

        /// <summary>
        /// Estimate the noise floor of each frequency band from THIS frame's spectrum.
        ///
        /// Uses a clipped mean (the mean, then the mean of everything below that mean). That is a
        /// cheap one-pass stand-in for a median, and the clipping is the point: a band holding one
        /// loud note must not let that note drag its own floor up and mask itself.
        ///
        /// Computed across frequency inside a single frame, deliberately, rather than adapted over
        /// time — a time-adaptive floor eventually learns a sustained note and erases it.
        /// </summary>
        private void ComputeNoiseFloor(int bins, float hzPerBin, int minBin, int maxBin)
        {
            EnsureBandTable(bins, hzPerBin);
            if (_bandCount <= 0) return;

            // Bucket by band in two sweeps over the bins — NOT one sweep per band. With ~38 bands
            // over ~620 in-range bins that is the difference between ~1.2k and ~47k iterations,
            // 30 times a second, and this has to stay invisible against the 90 FPS budget.
            Array.Clear(_bandSum, 0, _bandCount);
            Array.Clear(_bandN,   0, _bandCount);
            for (int i = minBin; i <= maxBin; i++)
            {
                int b = _binBand[i];
                _bandSum[b] += _spectrumData[i];
                _bandN[b]++;
            }
            for (int b = 0; b < _bandCount; b++)
                _bandFloor[b] = (_bandN[b] > 0) ? _bandSum[b] / _bandN[b] : peakThreshold;

            // Second sweep: mean of the below-mean population, so the band's own peaks drop out.
            Array.Clear(_bandSum2, 0, _bandCount);
            Array.Clear(_bandN2,   0, _bandCount);
            for (int i = minBin; i <= maxBin; i++)
            {
                int b = _binBand[i];
                float v = _spectrumData[i];
                if (v <= _bandFloor[b]) { _bandSum2[b] += v; _bandN2[b]++; }
            }
            for (int b = 0; b < _bandCount; b++)
                if (_bandN2[b] > 0) _bandFloor[b] = _bandSum2[b] / _bandN2[b];
        }

        /// <summary>Build the bin-to-band map. Rebuilt only when Hz/bin changes.</summary>
        private void EnsureBandTable(int bins, float hzPerBin)
        {
            if (_binBand != null && _binBand.Length == bins &&
                Mathf.Abs(_bandsBuiltForHz - hzPerBin) < 1e-6f) return;

            _binBand = new int[bins];
            int perOct = Mathf.Max(1, noiseFloorBandsPerOctave);
            float lowHz = Mathf.Max(1f, MidiToFrequency(Mathf.Max(0, minMidiNote - 12)));
            int maxBand = 0;
            for (int i = 0; i < bins; i++)
            {
                float hz = i * hzPerBin;
                int b = (hz <= lowHz) ? 0 : Mathf.FloorToInt(Mathf.Log(hz / lowHz, 2f) * perOct);
                if (b < 0) b = 0;
                _binBand[i] = b;
                if (b > maxBand) maxBand = b;
            }
            _bandCount = maxBand + 1;
            _bandFloor = new float[_bandCount];
            _bandSum   = new float[_bandCount];
            _bandN     = new int[_bandCount];
            _bandSum2  = new float[_bandCount];
            _bandN2    = new int[_bandCount];
            _bandsBuiltForHz = hzPerBin;
        }

        // ----------------------------------------------------------------
        // Harmonic-sum scoring (NEW 2026-09-24)
        // ----------------------------------------------------------------

        private readonly HashSet<int> _detectedSet = new HashSet<int>();
        private int[] _detectedArr = Array.Empty<int>();

        /// <summary>
        /// Score every candidate MIDI note by the energy summed across its harmonic series, then
        /// claim notes greedily, subtracting each winner's predicted partials from a working copy
        /// of the spectrum before scoring again.
        ///
        /// The subtraction is what keeps chords alive: a partial is reduced by only the amount the
        /// winning note is EXPECTED to contribute there (about A1/n), so a genuine note sharing
        /// that frequency keeps its surplus and can still be claimed on the next pass. Zeroing the
        /// bin instead would make an octave chord impossible to hear.
        /// </summary>
        private int[] DetectByHarmonicSum(int bins, float hzPerBin)
        {
            if (_work == null || _work.Length != bins) _work = new float[bins];
            Array.Copy(_spectrumData, _work, bins);
            if (_scores == null) _scores = new float[128];

            _detectedSet.Clear();
            int lo = Mathf.Clamp(minMidiNote, 0, 127);
            int hi = Mathf.Clamp(maxMidiNote, 0, 127);
            int partials = Mathf.Max(1, harmonicSumCount);

            for (int claim = 0; claim < maxPolyphony; claim++)
            {
                int best = -1; float bestScore = 0f;
                for (int m = lo; m <= hi; m++)
                {
                    if (_detectedSet.Contains(m)) { _scores[m] = 0f; continue; }
                    float sc = ScoreNote(m, partials, bins, hzPerBin);
                    _scores[m] = sc;
                    if (sc > bestScore) { bestScore = sc; best = m; }
                }
                if (best < 0) break;

                // A winner has to show real evidence, or a smear of band noise could take a slot
                // purely on breadth.
                if (!HasCredibleEvidence(best, partials, bins, hzPerBin)) break;

                int shifted = ApplySubOctaveGuard(best, partials, bins, hzPerBin, hi);

                // The guard can land on a note we already claimed. Don't abandon the search when
                // it does — just take this candidate's energy out so the next pass sees something
                // new. Breaking here would silently drop every remaining note of a chord, and
                // leaving the energy in would spin on the same candidate forever.
                if (!_detectedSet.Add(shifted))
                {
                    SubtractPartials(best, partials, bins, hzPerBin);
                    continue;
                }
                SubtractPartials(shifted, partials, bins, hzPerBin);
            }

            if (_detectedArr.Length != _detectedSet.Count) _detectedArr = new int[_detectedSet.Count];
            _detectedSet.CopyTo(_detectedArr);
            return _detectedArr;
        }

        /// <summary>Sum of 1/n-weighted partial magnitudes for one candidate note.</summary>
        private float ScoreNote(int midi, int partials, int bins, float hzPerBin)
        {
            float f0 = MidiToFrequency(midi);
            float score = 0f;
            for (int n = 1; n <= partials; n++)
            {
                float mag = PartialMagnitude(f0 * n, bins, hzPerBin);
                if (mag <= 0f) continue;
                score += mag / n;
            }
            return score;
        }

        /// <summary>
        /// Largest above-floor magnitude within tolerance of the given frequency. The window is at
        /// least one bin wide, because below about 230 Hz a single bin already spans more than a
        /// semitone and a narrower window would simply miss.
        /// </summary>
        private float PartialMagnitude(float freq, int bins, float hzPerBin)
        {
            if (freq <= 0f) return 0f;
            float halfHz = Mathf.Max(hzPerBin, freq * harmonicSumTolerance);
            int from = Mathf.Max(1, Mathf.FloorToInt((freq - halfHz) / hzPerBin));
            int to   = Mathf.Min(bins - 1, Mathf.CeilToInt((freq + halfHz) / hzPerBin));
            float best = 0f;
            for (int i = from; i <= to; i++)
            {
                float v = _work[i];
                if (v > best && v >= ThresholdAtBin(i)) best = v;
            }
            return best;
        }

        /// <summary>At least two partials present, one of them in the lower half of the series.</summary>
        private bool HasCredibleEvidence(int midi, int partials, int bins, float hzPerBin)
        {
            float f0 = MidiToFrequency(midi);
            int present = 0; bool low = false;
            int lowHalf = Mathf.Max(2, partials / 2);
            for (int n = 1; n <= partials; n++)
            {
                if (PartialMagnitude(f0 * n, bins, hzPerBin) <= 0f) continue;
                present++;
                if (n <= lowHalf) low = true;
            }
            return present >= 2 && low;
        }

        /// <summary>
        /// Every harmonic of note M is also a harmonic of M-12, so a plain harmonic sum always
        /// drifts an octave down. The tell is that a true sub-octave has no ODD partials of its
        /// own: if 1x/3x/5x are weak next to 2x/4x/6x, the energy belongs to the octave up.
        /// </summary>
        private int ApplySubOctaveGuard(int midi, int partials, int bins, float hzPerBin, int hi)
        {
            if (subOctaveOddRatio <= 0f || midi + 12 > hi) return midi;

            float f0 = MidiToFrequency(midi);

            // The test is PRESENCE, not relative strength. A real sub-octave error has literally no
            // energy at 3x/5x/7x, because those frequencies belong to no note being played. Judging
            // by magnitude instead was a bug: a genuine LOW note on a small speaker has a rolled-off
            // fundamental and naturally stronger even partials, so it failed a ratio test and got
            // shifted up an octave — measured on device as zero detections below C3, and every D3
            // reported as D4.
            int oddPresent = 0, evenPresent = 0;
            float odd = 0f, even = 0f;
            for (int n = 2; n <= partials; n++)
            {
                float mag = PartialMagnitude(f0 * n, bins, hzPerBin);
                if ((n & 1) == 1) { odd += mag; if (mag > 0f) oddPresent++; }
                else             { even += mag; if (mag > 0f) evenPresent++; }
            }

            // n=1 is deliberately excluded from the vote: on speaker-limited instruments the
            // fundamental is the LEAST reliable partial a low note has.
            if (oddPresent == 0 && evenPresent >= 2 && odd <= even * subOctaveOddRatio)
                return midi + 12;
            return midi;
        }

        /// <summary>
        /// Remove what the claimed note is expected to contribute (about A1/n at its n-th partial)
        /// from the working spectrum, leaving any surplus behind for a genuine second note.
        /// </summary>
        private void SubtractPartials(int midi, int partials, int bins, float hzPerBin)
        {
            float f0 = MidiToFrequency(midi);
            float a1 = PartialMagnitude(f0, bins, hzPerBin);
            if (a1 <= 0f) a1 = PartialMagnitude(f0 * 2f, bins, hzPerBin);

            for (int n = 1; n <= partials; n++)
            {
                float freq = f0 * n;
                float halfHz = Mathf.Max(hzPerBin, freq * harmonicSumTolerance);
                int from = Mathf.Max(1, Mathf.FloorToInt((freq - halfHz) / hzPerBin));
                int to   = Mathf.Min(bins - 1, Mathf.CeilToInt((freq + halfHz) / hzPerBin));
                float expected = a1 / n;
                for (int i = from; i <= to; i++)
                {
                    float v = _work[i] - expected;
                    _work[i] = v > 0f ? v : 0f;
                }
            }
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
