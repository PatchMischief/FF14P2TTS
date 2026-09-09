namespace FF14P2TTS.Application;

/// <summary>Resolves the configured TTS implementation at the point it is used.</summary>
public sealed class TtsServiceProvider : ITtsServiceProvider
{
    private readonly Configuration _configuration;
    private readonly ITtsService _player2Service;
    private readonly ITtsService _azureService;

    public TtsServiceProvider(
        Configuration configuration,
        ITtsService player2Service,
        ITtsService azureService)
    {
        _configuration = configuration;
        _player2Service = player2Service;
        _azureService = azureService;
    }

    public ITtsService Active => _configuration.ActiveEngine == TtsEngine.MicrosoftAzure
        ? _azureService
        : _player2Service;
}
