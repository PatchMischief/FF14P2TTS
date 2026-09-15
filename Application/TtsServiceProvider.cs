using System.Collections.Generic;

namespace FF14P2TTS.Application;

/// <summary>Resolves the configured TTS implementation at the point it is used.</summary>
public sealed class TtsServiceProvider : ITtsServiceProvider
{
    private readonly Configuration _configuration;
    private readonly IReadOnlyDictionary<TtsEngine, ITtsService> _services;

    public TtsServiceProvider(
        Configuration configuration,
        IReadOnlyDictionary<TtsEngine, ITtsService> services)
    {
        _configuration = configuration;
        _services = services;
    }

    public ITtsService Active => _services.TryGetValue(_configuration.ActiveEngine, out var service)
        ? service
        : _services[TtsEngine.Player2];
}
