namespace PianoLearningCore
{
    /// <summary>
    /// How a song is played, chosen per-song from the SongSelectionMenu and carried through
    /// SongLauncher to the NoteEvaluator:
    ///
    ///   Practice   — guidance on. A wrong press reveals the expected note(s) in red so the
    ///                player learns what they should have played.
    ///   Assessment — a graded run. The answer-reveal is withheld; the score is what matters.
    /// </summary>
    public enum SessionMode
    {
        Practice,
        Assessment
    }
}
