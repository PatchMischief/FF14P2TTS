namespace FF14P2TTS.Application;

/// <summary>
/// Plays pre-synthesized dialogue audio when the matching line appears on screen.
/// Implementations preload ahead of time so synthesis latency is hidden.
/// </summary>
public interface IDialoguePreloader
{
    /// <summary>
    /// Attempts to play a preloaded audio entry for the given on-screen dialogue.
    /// Returns true when a preloaded line matched and playback started; the matched
    /// entry is consumed and cleared from the preload queue.
    /// </summary>
    bool TryPlay(string speaker, string text);
}
