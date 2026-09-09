using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
namespace FF14P2TTS.Application;

/// <summary>Coordinates NPC dialogue rules independently of the Dalamud event adapter.</summary>
public sealed class NpcDialogueCoordinator
{
    private readonly Configuration _configuration;
    private readonly ITtsServiceProvider _ttsServices;
    private readonly NpcVoiceMapper _voiceMapper;
    private readonly INpcGenderResolver _genderResolver;
    private readonly IAutoAdvanceHandler _autoAdvanceHandler;
    private readonly IPluginLog _log;
    private List<VoiceInfo>? _cachedVoiceList;
    private string _lastNpcSpeaker = string.Empty;

    public NpcDialogueCoordinator(
        Configuration configuration,
        ITtsServiceProvider ttsServices,
        NpcVoiceMapper voiceMapper,
        INpcGenderResolver genderResolver,
        IAutoAdvanceHandler autoAdvanceHandler,
        IPluginLog log)
    {
        _configuration = configuration;
        _ttsServices = ttsServices;
        _voiceMapper = voiceMapper;
        _genderResolver = genderResolver;
        _autoAdvanceHandler = autoAdvanceHandler;
        _log = log;
    }

    public void Handle(string speaker, string text, bool isNativeVoiceSubtitleVisible)
    {
        if (!_configuration.TtsEnabled)
            return;

        if (_configuration.SkipTtsDuringCutscenes && isNativeVoiceSubtitleVisible)
        {
            _log.Debug("[FF14P2TTS] Native voice subtitle visible â€” skipping NPC TTS");
            return;
        }

        var textToSpeak = IncludeSpeakerName(speaker, text);
        var voiceId = ResolveVoice(speaker);

        _ = _ttsServices.Active.SpeakAsync(textToSpeak, voice: voiceId);
        _autoAdvanceHandler.OnDialogSpoken(textToSpeak);
    }

    private string IncludeSpeakerName(string speaker, string text)
    {
        if (!_configuration.IncludeNpcSpeakerName || string.IsNullOrWhiteSpace(speaker))
            return text;

        if (string.Equals(speaker, _lastNpcSpeaker, StringComparison.OrdinalIgnoreCase))
            return text;

        _lastNpcSpeaker = speaker;
        return $"{speaker} says: {text}";
    }

    private string ResolveVoice(string speaker)
    {
        var isAzure = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure;
        var usesGenderedVoices = isAzure ? _configuration.AzureUseGenderedVoices : _configuration.UseGenderedVoices;
        if (!usesGenderedVoices)
            return isAzure ? _configuration.AzureUnisexVoice : _configuration.UnisexVoiceId;

        var gender = _genderResolver.GetGender(speaker);
        var usesPerNpcVoices = isAzure ? _configuration.AzureUsePerNpcVoices : _configuration.UsePerNpcVoices;
        if (usesPerNpcVoices && _cachedVoiceList is not null)
        {
            var voice = _voiceMapper.GetVoiceForNpc(speaker, gender, _cachedVoiceList);
            _log.Debug($"[FF14P2TTS] NPC: {speaker} -> {gender}, voice={voice}, engine={_configuration.ActiveEngine}");
            return voice;
        }

        if (usesPerNpcVoices)
            _ = RefreshVoiceCacheAsync();

        var defaultVoice = GetDefaultVoice(isAzure, gender);
        _log.Debug($"[FF14P2TTS] NPC: {speaker} -> {gender}, voice={defaultVoice}, engine={_configuration.ActiveEngine}");
        return defaultVoice;
    }

    private string GetDefaultVoice(bool isAzure, NpcGender gender) => (isAzure, gender) switch
    {
        (true, NpcGender.Male) => _configuration.AzureMaleVoice,
        (true, NpcGender.Female) => GetAzureFemaleDefaultVoice(),
        (true, _) => _configuration.AzureUnisexVoice,
        (false, NpcGender.Male) => _configuration.MaleVoiceId,
        (false, NpcGender.Female) => _configuration.FemaleVoiceId,
        _ => _configuration.UnisexVoiceId,
    };

    private string GetAzureFemaleDefaultVoice() => string.Equals(
        _configuration.AzureFemaleVoice,
        "en-US-AnaNeural",
        StringComparison.OrdinalIgnoreCase)
        ? "en-US-JennyNeural"
        : _configuration.AzureFemaleVoice;

    private async Task RefreshVoiceCacheAsync()
    {
        try
        {
            var voices = await _ttsServices.Active.GetAvailableVoicesRawAsync();
            _cachedVoiceList = voices.Where(voice => voice.RawLanguage is "american_english" or "british_english").ToList();
            if (_cachedVoiceList.Count == 0)
                _cachedVoiceList = voices;
        }
        catch
        {
            _cachedVoiceList = null;
        }
    }
}
