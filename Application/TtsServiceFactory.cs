using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FF14P2TTS.Infrastructure.Azure;
using FF14P2TTS.Infrastructure.ElevenLabs;
using FF14P2TTS.Infrastructure.Player2;
using FF14P2TTS.Infrastructure.Speechify;

namespace FF14P2TTS.Application;

/// <summary>Composition-root factory for provider adapters.</summary>
public sealed class TtsServiceFactory
{
    private readonly Configuration _configuration;
    private readonly IPluginLog _log;

    public TtsServiceFactory(Configuration configuration, IPluginLog log)
    {
        _configuration = configuration;
        _log = log;
    }

    public IReadOnlyDictionary<TtsEngine, ITtsService> Create()
    {
        return new Dictionary<TtsEngine, ITtsService>
        {
            [TtsEngine.Player2] = new Player2TtsService(_configuration, _log),
            [TtsEngine.MicrosoftAzure] = new AzureTtsService(_configuration, _log),
            [TtsEngine.ElevenLabs] = new ElevenLabsTtsService(_configuration, _log),
            [TtsEngine.Speechify] = new SpeechifyTtsService(_configuration, _log),
        };
    }
}