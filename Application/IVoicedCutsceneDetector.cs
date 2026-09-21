namespace FF14P2TTS.Application;
using System.Collections.Generic;
using System.Threading.Tasks;

public enum VoicedCutsceneClassification
{
    /// <summary>The wiki was consulted and the line is not inside a voiced block.</summary>
    NotVoiced,

    /// <summary>The line appears inside a voiced cutscene block and must be skipped.</summary>
    Voiced,

    /// <summary>The wiki could not classify the line (quest data unavailable/unresolvable).</summary>
    Unknown,
}

public interface IVoicedCutsceneDetector
{
    Task<VoicedCutsceneClassification> ClassifyAsync(string speaker, string text);

    /// <summary>
    /// Returns the wiki-scripted dialogue boxes for the given quest's unvoiced
    /// cutscenes, in script order, as an ordered linked list. Each box is a
    /// contiguous multi-line dialogue block that can be synthesized in one request.
    /// Voiced cutscene blocks are excluded; the skip logic handles those separately.
    /// </summary>
    Task<LinkedList<WikiDialogueBox>> GetUnvoicedDialogueBoxesAsync(string title);

    /// <summary>Clears any cached wiki data for a quest.</summary>
    void ClearQuestCache(string title);
}