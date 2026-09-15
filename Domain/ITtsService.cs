using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FF14P2TTS;

/// <summary>
/// Common interface for all TTS engine implementations.
/// </summary>
public interface ITtsService : IDisposable
{
    /// <summary>
    /// Check if the TTS server/service is reachable.
    /// </summary>
    Task<bool> IsServerAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Get list of available voices as display strings. Defaults to the
    /// <see cref="VoiceInfo.DisplayName"/> of each raw voice entry.
    /// </summary>
    async Task<string[]> GetAvailableVoicesAsync(CancellationToken ct = default)
    {
        var voices = await GetAvailableVoicesRawAsync(ct).ConfigureAwait(false);
        return voices.Select(voice => voice.DisplayName).ToArray();
    }

    /// <summary>
    /// Get structured voice data from the TTS engine.
    /// </summary>
    Task<List<VoiceInfo>> GetAvailableVoicesRawAsync(CancellationToken ct = default);

    /// <summary>
    /// Stop any currently-playing TTS audio.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Speak a message through the TTS engine.
    /// </summary>
    Task SpeakAsync(
        string text,
        string? voice = null,
        double? speed = null,
        int? pitch = null,
        int? volume = null,
        CancellationToken ct = default);
}
