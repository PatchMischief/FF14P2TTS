using System.Threading;
using System.Threading.Tasks;

namespace FF14P2TTS.Application;

/// <summary>Providers that can synthesize pre-built SSML directly.</summary>
public interface ISsmlSpeechProvider
{
    Task SpeakSsmlAsync(string ssml, string? voice = null, CancellationToken ct = default);
}
