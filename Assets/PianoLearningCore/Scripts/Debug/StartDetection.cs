using System.Collections;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Bootstrap MonoBehaviour for the audio-detection workflow.
    ///
    /// Wires together the three pieces that need to start in a specific order:
    ///   1. PianoAudioDetector  — start the mic FIRST so it can warm up while the song loads
    ///   2. NoteEvaluator       — reset its score
    ///   3. SongLauncher        — load the MIDI and start playback
    ///
    /// Each reference is optional; missing pieces are logged but won't crash the scene.
    /// Place this on its own empty GameObject and drag the three references in.
    /// </summary>
    public class StartDetection : MonoBehaviour
    {
        [Tooltip("The microphone-driven note detector. Will be started on Start().")]
        public PianoAudioDetector audioDetector;

        [Tooltip("The component that scores detected notes against the song.")]
        public NoteEvaluator noteEvaluator;

        [Tooltip("The component that loads a MIDI file and triggers playback.")]
        public SongLauncher songLauncher;

        [Tooltip("Wait this many seconds after starting the mic before launching the song. " +
                 "Lets the mic settle and gives the player a moment before the first note falls.")]
        [Range(0f, 5f)]
        public float startDelaySeconds = 0.5f;

        private void Start() => StartCoroutine(BootstrapRoutine());

        private IEnumerator BootstrapRoutine()
        {
            if (audioDetector != null)
            {
                audioDetector.StartDetection();
            }
            else
            {
                Debug.LogWarning("[StartDetection] No PianoAudioDetector assigned — " +
                                 "scoring won't work. Drag the detector into the Inspector field.", this);
            }

            if (startDelaySeconds > 0f)
                yield return new WaitForSeconds(startDelaySeconds);

            if (noteEvaluator != null)
            {
                noteEvaluator.ResetScore();
            }
            else
            {
                Debug.LogWarning("[StartDetection] No NoteEvaluator assigned.", this);
            }

            if (songLauncher != null)
            {
                songLauncher.LoadAndPlay();
            }
            else
            {
                Debug.LogWarning("[StartDetection] No SongLauncher assigned — " +
                                 "no song will load.", this);
            }
        }
    }
}
