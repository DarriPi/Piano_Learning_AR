using System;
using System.Collections.Generic;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Which Android capture source the native recorder should prefer. 'Auto' tries the
    /// least-processed sources first (Unprocessed -> Camcorder -> Mic). The named values force
    /// that source to the front of the try-order, but we still fall back to the others if the
    /// device refuses it, so capture never silently fails. Use the named values for A/B testing.
    /// </summary>
    public enum NativeMicSource { Auto, Unprocessed, Camcorder, Mic, VoiceRecognition }

    /// <summary>
    /// Captures microphone audio on Quest by talking to Android's AudioRecord directly (via JNI),
    /// instead of UnityEngine.Microphone.
    ///
    /// WHY THIS EXISTS: on Quest + Unity 6, UnityEngine.Microphone exposes exactly ONE device,
    /// "Android audio input" — the comms/voice capture path with heavy noise-suppression + AGC +
    /// echo-cancellation that's tuned for speech and mangles sustained piano tones. The cleaner
    /// CAMCORDER / UNPROCESSED capture sources only exist down at the Android AudioRecord layer,
    /// which is what this class reaches.
    ///
    /// PIPELINE (Stage 2):
    ///   Android AudioRecord (UNPROCESSED/CAMCORDER) --[main-thread Poll()]--> ring buffer
    ///       --[main-thread CopyLatest(), once per analysis frame]--> our own windowed FFT
    ///
    /// We read AudioRecord in NON-BLOCKING mode once per frame from the main thread (which is
    /// already attached to the JVM), so there is no background thread and no JNI thread-attach
    /// dance. CopyLatest() PEEKS the freshest window each frame — there is no AudioSource,
    /// AudioClip, or playback buffering in the path, which is what keeps latency low.
    /// </summary>
    public class QuestMicCapture
    {
        public bool IsActive { get; private set; }
        public int SampleRate { get; private set; }
        public string SourceName { get; private set; } = "none";

        /// <summary>Samples currently waiting in the ring buffer (for the health log).</summary>
        public int BufferedSamples { get { lock (_lock) { return _count; } } }

        // --- ring buffer: producer = main-thread Poll(); consumer = main-thread CopyLatest() peek ---
        private float[] _ring;
        private int _cap, _writePos, _count;
        private readonly object _lock = new object();

#if UNITY_ANDROID && !UNITY_EDITOR
        // Stable Android platform constants (hardcoded to avoid extra JNI field lookups).
        private const int CHANNEL_IN_MONO       = 16; // AudioFormat.CHANNEL_IN_MONO
        private const int ENCODING_PCM_16BIT    = 2;  // AudioFormat.ENCODING_PCM_16BIT
        private const int STATE_INITIALIZED     = 1;  // AudioRecord.STATE_INITIALIZED
        private const int RECORDSTATE_RECORDING = 3;  // AudioRecord.RECORDSTATE_RECORDING
        private const int READ_NON_BLOCKING     = 1;  // AudioRecord.READ_NON_BLOCKING

        private AndroidJavaObject _audioRecord;
        private IntPtr _arObj;       // raw jobject of _audioRecord (kept alive by _audioRecord)
        private IntPtr _readMethod;  // methodID of read(short[],int,int,int)
        private IntPtr _javaArr;     // global ref to a reusable Java short[]
        private jvalue[] _readArgs;  // reusable JNI args for the read() call
        private float[] _convert;    // scratch: short -> float per read
        private int _chunk;
#endif

        /// <summary>
        /// Open and start AudioRecord. Tries the preferred source first, then falls back through
        /// the remaining sources and a couple of sample rates. Returns false if nothing opened
        /// (the caller should then fall back to UnityEngine.Microphone). No-op in the editor.
        /// </summary>
        public bool Start(int preferredRate, NativeMicSource pref)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                int[] rates = (preferredRate == 48000)
                    ? new[] { 48000, 44100 }
                    : new[] { preferredRate, 48000, 44100 };
                int[] sources = BuildSourceOrder(pref);

                using (var arClass = new AndroidJavaClass("android.media.AudioRecord"))
                {
                    foreach (int rate in rates)
                    {
                        int minBuf = arClass.CallStatic<int>("getMinBufferSize",
                            rate, CHANNEL_IN_MONO, ENCODING_PCM_16BIT);
                        if (minBuf <= 0) continue;            // rate unsupported on this device
                        int bufBytes = minBuf * 4;            // headroom for frame-rate polling

                        foreach (int source in sources)
                        {
                            var ar = TryConstruct(source, rate, bufBytes);
                            if (ar == null) continue;

                            ar.Call("startRecording");
                            if (ar.Call<int>("getRecordingState") != RECORDSTATE_RECORDING)
                            {
                                SafeRelease(ar);
                                continue;
                            }

                            _audioRecord = ar;
                            SampleRate   = rate;
                            SourceName   = NameOf(source);
                            SetupJni();
                            SetupRing(rate);
                            IsActive = true;
                            Debug.Log($"[QuestMicCapture] Recording. source={SourceName} rate={rate} " +
                                      $"minBuf={minBuf}B chunk={_chunk}");
                            return true;
                        }
                    }
                }

                Debug.LogWarning("[QuestMicCapture] Could not open AudioRecord on any source/rate.");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[QuestMicCapture] Start failed: {e}");
                Stop();
                return false;
            }
#else
            return false; // Native capture is Quest/Android-only; the editor uses UnityEngine.Microphone.
#endif
        }

        /// <summary>Main thread: drain all currently-available PCM from AudioRecord into the ring.</summary>
        public void Poll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsActive) return;
            // Non-blocking reads: each returns however many shorts were buffered (0 if none).
            for (int guard = 0; guard < 16; guard++)
            {
                int n = AndroidJNI.CallIntMethod(_arObj, _readMethod, _readArgs);
                if (n <= 0) break;                 // 0 = nothing buffered; <0 = AudioRecord error code
                short[] s = AndroidJNI.FromShortArray(_javaArr);
                int m = Mathf.Min(n, s.Length);
                const float inv = 1f / 32768f;
                for (int i = 0; i < m; i++) _convert[i] = s[i] * inv;
                WriteRing(_convert, m);
                if (n < _chunk) break;             // drained for now
            }
#endif
        }

        /// <summary>
        /// Copy the most recent dest.Length samples (newest-aligned) for analysis. This is a PEEK,
        /// not a consume — the ring keeps overwriting oldest, so each call sees the freshest window,
        /// which is what keeps latency low. Returns false until enough samples have arrived (warm-up).
        /// </summary>
        public bool CopyLatest(float[] dest)
        {
            int len = dest.Length;
            lock (_lock)
            {
                if (_count < len) return false;
                int start = _writePos - len;
                if (start < 0) start += _cap;
                for (int i = 0; i < len; i++)
                    dest[i] = _ring[(start + i) % _cap];
                return true;
            }
        }

        public void Stop()
        {
            IsActive = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_audioRecord != null)
                {
                    _audioRecord.Call("stop");
                    _audioRecord.Call("release");
                    _audioRecord.Dispose();
                }
            }
            catch (Exception e) { Debug.LogWarning($"[QuestMicCapture] Stop: {e.Message}"); }
            _audioRecord = null;
            if (_javaArr != IntPtr.Zero) { AndroidJNI.DeleteGlobalRef(_javaArr); _javaArr = IntPtr.Zero; }
#endif
        }

        private void SetupRing(int rate)
        {
            _cap = Mathf.Max(4096, rate); // ~1 s of headroom
            _ring = new float[_cap];
            _writePos = _count = 0;
        }

        private void WriteRing(float[] src, int n)
        {
            lock (_lock)
            {
                for (int i = 0; i < n; i++)
                {
                    _ring[_writePos] = src[i];
                    _writePos = (_writePos + 1) % _cap;
                    if (_count < _cap) _count++; // when full, the oldest sample is overwritten implicitly
                }
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject TryConstruct(int source, int rate, int bufBytes)
        {
            try
            {
                var ar = new AndroidJavaObject("android.media.AudioRecord",
                    source, rate, CHANNEL_IN_MONO, ENCODING_PCM_16BIT, bufBytes);
                if (ar.Call<int>("getState") == STATE_INITIALIZED) return ar;
                SafeRelease(ar); // constructed but not initialized (source/rate refused)
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QuestMicCapture] source={NameOf(source)} rate={rate} rejected: {e.Message}");
            }
            return null;
        }

        private void SetupJni()
        {
            _arObj      = _audioRecord.GetRawObject();
            IntPtr cls  = _audioRecord.GetRawClass();
            _readMethod = AndroidJNI.GetMethodID(cls, "read", "([SIII)I"); // read(short[],int,int,int)
            _chunk      = 2048;
            IntPtr local = AndroidJNI.NewShortArray(_chunk);
            _javaArr     = AndroidJNI.NewGlobalRef(local);
            AndroidJNI.DeleteLocalRef(local);
            _readArgs = new jvalue[4];
            _readArgs[0].l = _javaArr;
            _readArgs[1].i = 0;
            _readArgs[2].i = _chunk;
            _readArgs[3].i = READ_NON_BLOCKING;
            _convert = new float[_chunk];
        }

        private static void SafeRelease(AndroidJavaObject ar)
        {
            try { ar.Call("release"); ar.Dispose(); } catch { /* best-effort cleanup */ }
        }

        private static int[] BuildSourceOrder(NativeMicSource pref)
        {
            // MediaRecorder.AudioSource: MIC=1, CAMCORDER=5, VOICE_RECOGNITION=6, UNPROCESSED=9.
            var auto = new List<int> { 9, 5, 1 }; // unprocessed -> camcorder -> mic
            int forced;
            switch (pref)
            {
                case NativeMicSource.Unprocessed:      forced = 9; break;
                case NativeMicSource.Camcorder:        forced = 5; break;
                case NativeMicSource.Mic:              forced = 1; break;
                case NativeMicSource.VoiceRecognition: forced = 6; break;
                default:                               return auto.ToArray();
            }
            var list = new List<int> { forced };
            foreach (int s in auto) if (!list.Contains(s)) list.Add(s);
            return list.ToArray();
        }

        private static string NameOf(int source)
        {
            switch (source)
            {
                case 9: return "UNPROCESSED";
                case 5: return "CAMCORDER";
                case 6: return "VOICE_RECOGNITION";
                case 1: return "MIC";
                default: return "src" + source;
            }
        }
#endif
    }
}
