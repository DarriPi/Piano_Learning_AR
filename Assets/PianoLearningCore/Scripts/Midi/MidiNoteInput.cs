using System;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Note input from a USB MIDI keyboard: the Detection strip's <b>MIDI</b> mode
    /// (<see cref="DetectionMode.Midi"/>). Fires <see cref="OnNoteOn"/> once per key press — the
    /// same contract as <see cref="PianoAudioDetector.OnNoteOn"/> — so <see cref="NoteEvaluator"/>
    /// judges both sources the same way.
    ///
    /// The keyboard reports exactly which key went down, so none of the microphone path's guesswork
    /// applies: no pitch estimation, harmonics or room noise. Android does the USB side: a
    /// class-compliant interface (a Roland UM-ONE mk2 on its TAB setting) appears in its MIDI service
    /// by itself. The Java half, <c>MidiInputBridge.java</c>, listens there and queues the note-ons,
    /// and this class drains that queue once a frame on the main thread — the same way
    /// <see cref="QuestMicCapture"/> polls AudioRecord — so there is no Java-to-C# callback and no
    /// thread hand-off.
    ///
    /// <see cref="NoteEvaluator"/> adds this to its own GameObject at runtime, so the scene needs no
    /// edit. Quest/Android only: in the Editor <see cref="IsSupported"/> is false.
    /// </summary>
    public class MidiNoteInput : MonoBehaviour
    {
        [Tooltip("Print one line per note-on, and whenever a MIDI device connects or disconnects.")]
        public bool logNotes = true;

        /// <summary>A key went down on the MIDI keyboard. (MIDI note number)</summary>
#pragma warning disable CS0067 // raised only in device builds, so the Editor compile calls it unused
        public event Action<int> OnNoteOn;
#pragma warning restore CS0067

        /// <summary>False where there is no Android MIDI service — always so in the Editor.</summary>
        public bool IsSupported { get; private set; }

        /// <summary>True between <see cref="StartInput"/> and <see cref="StopInput"/>.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>The MIDI device(s) plugged in right now, e.g. "Roland UM-ONE"; empty while none
        /// is. Checked twice a second whether or not input is running, so the menu can offer MIDI only
        /// while a keyboard is actually connected.</summary>
        public string AvailableDevice { get; private set; } = "";

        /// <summary>True while a MIDI device that can send notes is plugged in.</summary>
        public bool HasDevice => AvailableDevice.Length > 0;

#if UNITY_ANDROID && !UNITY_EDITOR
        // A plug or unplug only has to reach the menu, so twice a second is plenty.
        private const float DevicePollSeconds = 0.5f;
        private float _devicePollTimer;

        private const int DrainCapacity = 64; // note-ons taken per frame; the rest wait a frame

        private AndroidJavaObject _bridge;
        private IntPtr _bridgeObj;    // raw jobject of _bridge (kept alive by _bridge)
        private IntPtr _drainMethod;  // methodID of drain(int[])
        private IntPtr _javaBuf;      // global ref to a reusable Java int[]
        private jvalue[] _drainArgs;  // reusable JNI args for the drain() call
#endif

        private void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // A missing or broken Java half must not take the menu down with it: MIDI then just
            // reports itself unavailable and the strip greys the button out.
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _bridge = new AndroidJavaObject("com.darripi.arpiano.MidiInputBridge", activity);
                }

                IsSupported = _bridge.Call<bool>("isSupported");
                if (IsSupported)
                {
                    _bridgeObj   = _bridge.GetRawObject();
                    _drainMethod = AndroidJNI.GetMethodID(_bridge.GetRawClass(), "drain", "([I)I");
                    IntPtr local = AndroidJNI.NewIntArray(DrainCapacity);
                    _javaBuf     = AndroidJNI.NewGlobalRef(local);
                    AndroidJNI.DeleteLocalRef(local);
                    _drainArgs = new jvalue[1];
                    _drainArgs[0].l = _javaBuf;
                }
                Debug.Log($"[MidiNoteInput] Android MIDI service {(IsSupported ? "found" : "NOT found")}.");
            }
            catch (Exception e)
            {
                IsSupported = false;
                Debug.LogError($"[MidiNoteInput] MIDI input unavailable: {e}");
            }

            // Know straight away whether a keyboard is already plugged in, so the menu's first frame
            // shows the MIDI button in the right state.
            if (IsSupported) PollDevice();
#endif
        }

        /// <summary>Start listening for a MIDI keyboard — now and for any plugged in later.
        /// Idempotent; does nothing where MIDI isn't supported.</summary>
        public void StartInput()
        {
            if (IsRunning || !IsSupported) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridge.Call("start");
                IsRunning = true;
                Debug.Log("[MidiNoteInput] Listening for a USB MIDI keyboard.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MidiNoteInput] Start failed: {e}");
            }
#endif
        }

        /// <summary>Stop listening and release the device. Idempotent.</summary>
        public void StopInput()
        {
            if (!IsRunning) return;
            IsRunning = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _bridge.Call("stop"); }
            catch (Exception e) { Debug.LogWarning($"[MidiNoteInput] Stop: {e.Message}"); }
#endif
            Debug.Log("[MidiNoteInput] Stopped listening.");
        }

        private void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsSupported) return;

            _devicePollTimer -= Time.unscaledDeltaTime;
            if (_devicePollTimer <= 0f)
            {
                _devicePollTimer = DevicePollSeconds;
                PollDevice();
            }

            if (!IsRunning) return;

            int n = AndroidJNI.CallIntMethod(_bridgeObj, _drainMethod, _drainArgs);
            if (n > 0)
            {
                int[] notes = AndroidJNI.FromIntArray(_javaBuf);
                for (int i = 0; i < n; i++)
                {
                    if (logNotes)
                        Debug.Log($"[MidiNoteInput] Note on {NoteUtils.GetNoteName(notes[i])} ({notes[i]})");
                    OnNoteOn?.Invoke(notes[i]);
                }
            }
#endif
        }

        /// <summary>Refresh <see cref="AvailableDevice"/>. If the MIDI service itself fails, MIDI is
        /// switched off for the session rather than logging the same error twice a second.</summary>
        private void PollDevice()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string name;
            try
            {
                name = _bridge.Call<string>("getPresentDeviceName") ?? "";
            }
            catch (Exception e)
            {
                Debug.LogError($"[MidiNoteInput] MIDI device check failed; MIDI switched off: {e}");
                IsSupported = false;
                name = "";
            }

            if (name == AvailableDevice) return;
            if (logNotes)
                Debug.Log(name.Length > 0
                    ? $"[MidiNoteInput] MIDI device plugged in: {name}"
                    : $"[MidiNoteInput] MIDI device unplugged: {AvailableDevice}");
            AvailableDevice = name;
#endif
        }

        private void OnDestroy()
        {
            StopInput();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_javaBuf != IntPtr.Zero) { AndroidJNI.DeleteGlobalRef(_javaBuf); _javaBuf = IntPtr.Zero; }
            _bridge?.Dispose();
            _bridge = null;
#endif
        }
    }
}
