using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace FF14P2TTS.Application;

public sealed class NpcDialogueCoordinator
{
    private readonly Configuration _configuration;
    private readonly ITtsServiceProvider _ttsServices;
    private readonly NpcVoiceMapper _voiceMapper;
    private readonly INpcGenderResolver _genderResolver;
    private readonly IVoicedCutsceneDetector _voicedCutsceneDetector;
    private readonly IPluginLog _log;
    private List<VoiceInfo>? _cachedVoiceList;
    private TtsEngine? _cachedVoiceEngine;
    private string _lastNpcSpeaker = string.Empty;
    private IDialoguePreloader? _preloader;

    /// <summary>How long to wait for the TalkSubtitle addon to catch up before reading its visibility.</summary>
    private static readonly TimeSpan SubtitleSettleDelay = TimeSpan.FromMilliseconds(100);

    public NpcDialogueCoordinator(
        Configuration configuration,
        ITtsServiceProvider ttsServices,
        NpcVoiceMapper voiceMapper,
        INpcGenderResolver genderResolver,
        IVoicedCutsceneDetector voicedCutsceneDetector,
        IPluginLog log)
    {
        _configuration = configuration;
        _ttsServices = ttsServices;
        _voiceMapper = voiceMapper;
        _genderResolver = genderResolver;
        _voicedCutsceneDetector = voicedCutsceneDetector;
        _log = log;
    }

    public async Task HandleAsync(string speaker, string text, Func<bool> isNativeVoiceSubtitleVisible)
    {
        if (!_configuration.TtsEnabled)
            return;

        if (_configuration.SkipTtsDuringCutscenes)
        {
            // The wiki's voiced/unvoiced script is the source of truth. Its
            // best-match classifier decides each line.
            var classification = await _voicedCutsceneDetector.ClassifyAsync(speaker, text).ConfigureAwait(false);
            if (classification == VoicedCutsceneClassification.Voiced)
            {
                _log.Information("[FF14P2TTS] Skipping voiced cutscene NPC TTS (wiki classified as voiced)");
                return;
            }

            // When the wiki has no data (quest read unavailable, or the line is
            // missing from the script), the native subtitle is the only runtime
            // signal for voiced lines. Wait a couple of frames for the subtitle
            // addon to catch up with the talk bubble, then read its CURRENT
            // visibility directly (never a stale latch).
            if (classification == VoicedCutsceneClassification.Unknown)
            {
                await Task.Delay(SubtitleSettleDelay).ConfigureAwait(false);
                if (isNativeVoiceSubtitleVisible())
                {
                    _log.Information("[FF14P2TTS] Skipping voiced cutscene NPC TTS (native subtitle visible)");
                    return;
                }
            }
        }

        var textToSpeak = IncludeSpeakerName(speaker, text);
        if (_preloader is not null && _preloader.TryPlay(speaker, text))
        {
            _log.Debug("[FF14P2TTS] Preloaded Speechify dialogue matched; skipping live synthesis.");
            return;
        }

        var gender = await _genderResolver.GetGenderAsync(speaker).ConfigureAwait(false);
        var voiceId = ResolveVoice(speaker, gender);
        _ = _ttsServices.Active.SpeakAsync(textToSpeak, voice: voiceId);
    }

    public void SetPreloader(IDialoguePreloader? preloader) => _preloader = preloader;

    /// <summary>Resolves the configured voice for a speaker; exposed for the dialogue preloader.</summary>
    public string ResolveVoiceFor(string speaker) => ResolveVoice(speaker);

    private string IncludeSpeakerName(string speaker, string text)
    {
        if (!_configuration.IncludeNpcSpeakerName || string.IsNullOrWhiteSpace(speaker))
            return text;

        if (string.Equals(speaker, _lastNpcSpeaker, StringComparison.OrdinalIgnoreCase))
            return text;

        _lastNpcSpeaker = speaker;
        return $"{speaker} says: {text}";
    }

    private string ResolveVoice(string speaker, NpcGender? genderOverride = null)
    {
        var isAzure = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure;
        var isElevenLabs = _configuration.ActiveEngine == TtsEngine.ElevenLabs;
        var isSpeechify = _configuration.ActiveEngine == TtsEngine.Speechify;
        if (_cachedVoiceEngine != _configuration.ActiveEngine)
        {
            _cachedVoiceEngine = _configuration.ActiveEngine;
            _cachedVoiceList = null;
        }
        var usesGenderedVoices = isAzure ? _configuration.AzureUseGenderedVoices : _configuration.UseGenderedVoices;
        if (isElevenLabs || isSpeechify)
            usesGenderedVoices = true;
        if (!usesGenderedVoices)
            return GetUnisexVoice();

        var gender = genderOverride ?? _genderResolver.GetGender(speaker);
        var usesPerNpcVoices = UsePerNpcVoices();
        if (usesPerNpcVoices && _cachedVoiceList is not null)
        {
            var voice = _voiceMapper.GetVoiceForNpc(speaker, gender, _cachedVoiceList);
            _log.Debug(
                $"[FF14P2TTS] NPC: {speaker} -> {gender}, voice={voice}, engine={_configuration.ActiveEngine}");
            return voice;
        }

        if (usesPerNpcVoices)
            _ = RefreshVoiceCacheAsync();

        var defaultVoice = GetDefaultVoice(isAzure, gender);
        _log.Debug(
            $"[FF14P2TTS] NPC: {speaker} -> {gender}, voice={defaultVoice}, engine={_configuration.ActiveEngine}");
        return defaultVoice;
    }

    private string GetUnisexVoice() => _configuration.ActiveEngine switch
    {
        TtsEngine.MicrosoftAzure => _configuration.AzureUnisexVoice,
        TtsEngine.ElevenLabs => _configuration.ElevenLabsDefaultVoice,
        TtsEngine.Speechify => _configuration.SpeechifyDefaultVoice,
        _ => _configuration.UnisexVoiceId,
    };

    private bool UsePerNpcVoices() => _configuration.ActiveEngine switch
    {
        TtsEngine.MicrosoftAzure => _configuration.AzureUsePerNpcVoices,
        TtsEngine.ElevenLabs => _configuration.ElevenLabsUsePerNpcVoices,
        TtsEngine.Speechify => _configuration.SpeechifyUsePerNpcVoices,
        _ => _configuration.UsePerNpcVoices,
    };

    private string GetDefaultVoice(bool isAzure, NpcGender gender)
    {
        if (_configuration.ActiveEngine == TtsEngine.Speechify)
        {
            return gender switch
            {
                NpcGender.Male => ResolveSpeechifyGenderVoice(_configuration.SpeechifyMaleVoice, "male"),
                NpcGender.Female => ResolveSpeechifyGenderVoice(_configuration.SpeechifyFemaleVoice, "female"),
                _ => PickSpeechifyVoice(_configuration.SpeechifyDefaultVoice),
            };
        }

        if (_configuration.ActiveEngine == TtsEngine.ElevenLabs)
            return _configuration.ElevenLabsDefaultVoice;

        return (isAzure, gender) switch
        {
            (true, NpcGender.Male) => _configuration.AzureMaleVoice,
            (true, NpcGender.Female) => GetAzureFemaleDefaultVoice(),
            (true, _) => _configuration.AzureUnisexVoice,
            (false, NpcGender.Male) => _configuration.MaleVoiceId,
            (false, NpcGender.Female) => _configuration.FemaleVoiceId,
            _ => _configuration.UnisexVoiceId,
        };
    }

    private string PickSpeechifyVoice(string voiceId) =>
        string.IsNullOrWhiteSpace(voiceId) ? _configuration.SpeechifyDefaultVoice : voiceId;

    private string ResolveSpeechifyGenderVoice(string preset, string targetGender)
    {
        if (!string.IsNullOrWhiteSpace(preset))
            return preset;

        var match = _cachedVoiceList?.FirstOrDefault(voice =>
            string.Equals(voice.Gender, targetGender, StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? _configuration.SpeechifyDefaultVoice;
    }

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
            _cachedVoiceList = voices
                .Where(voice => voice.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (_cachedVoiceList.Count == 0)
                _cachedVoiceList = voices;
        }
        catch
        {
            _cachedVoiceList = null;
        }
    }
}
