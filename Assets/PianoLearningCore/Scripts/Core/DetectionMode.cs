namespace PianoLearningCore
{
    /// <summary>
    /// How the app hears the player, chosen on the song menu's Detection strip
    /// (<see cref="DetectionModeSelector"/>) and applied by <see cref="NoteEvaluator.SetDetection"/>.
    /// Independent of <see cref="SessionMode"/>, which is picked per song:
    ///
    ///   Microphone — the audio note detector listens to the piano. Practice and Assessment behave
    ///                as they always have, except that Practice gives up on a note it hasn't heard
    ///                after <see cref="NoteEvaluator.micPracticeTimeoutSeconds"/> (a stop-gap for notes
    ///                the detector misses) and moves on.
    ///   FreePlay   — nothing listens: the microphone is switched off and no note is judged. In
    ///                Practice each note waits a fixed time at the line, then the song moves on;
    ///                Assessment plays in real time. Neither gives a score.
    ///   Midi       — a USB MIDI keyboard reports each key press (<see cref="MidiNoteInput"/>) and
    ///                the microphone is switched off. Judged and scored exactly like Microphone.
    ///                Supervisor-approved as an extra selectable mode, never a replacement for the mic.
    ///
    /// Midi is last only so the existing values keep their numbers; the strip shows it second.
    /// </summary>
    public enum DetectionMode
    {
        Microphone,
        FreePlay,
        Midi
    }
}
