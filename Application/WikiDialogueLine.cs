using System.Collections.Generic;
using System.Linq;

namespace FF14P2TTS.Application;

/// <summary>A single line of scripted dialogue extracted from a wiki page, in cutscene order.</summary>
public sealed record WikiDialogueLine(string Speaker, string Text);

/// <summary>A contiguous group of wiki-scripted dialogue lines (one cutscene box).</summary>
public sealed class WikiDialogueBox
{
    public WikiDialogueBox(IReadOnlyList<WikiDialogueLine> lines)
    {
        Lines = lines;
    }

    public IReadOnlyList<WikiDialogueLine> Lines { get; }

    public WikiDialogueLine First => Lines[0];

    /// <summary>The whole box as one text block, ready to be sent to Speechify in a single request.</summary>
    public string ConcatenatedText => string.Join("\n", Lines.Select(line => $"{line.Speaker}: {line.Text}"));
}
