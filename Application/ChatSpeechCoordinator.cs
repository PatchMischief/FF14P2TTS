using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FF14P2TTS.Application;

/// <summary>Contains application rules for deciding which chat messages are spoken.</summary>
public sealed class ChatSpeechCoordinator
{
    private readonly Configuration _configuration;
    private readonly ITtsServiceProvider _ttsServices;

    public ChatSpeechCoordinator(Configuration configuration, ITtsServiceProvider ttsServices)
    {
        _configuration = configuration;
        _ttsServices = ttsServices;
    }

    public bool TrySpeak(int chatType, string senderName, string messageText, bool isPvp, bool isOwnMessage)
    {
        if (!_configuration.TtsEnabled || isPvp || isOwnMessage || string.IsNullOrWhiteSpace(messageText))
            return false;

        if (!ShouldReadChatType(chatType))
            return false;

        var textToSpeak = _configuration.IncludeSpeakerName
            ? string.Format(_configuration.SpeakerNameFormat, senderName, messageText)
            : messageText;

        _ = _ttsServices.Active.SpeakAsync(textToSpeak, GetVoiceOverride(chatType));
        return true;
    }

    private string? GetVoiceOverride(int chatType)
    {
        var channelKey = GetChannelKey(chatType);
        return channelKey is not null && _configuration.ChannelVoiceOverrides.TryGetValue(channelKey, out var voice)
            ? voice
            : null;
    }

    private bool ShouldReadChatType(int chatType) => chatType switch
    {
        10 or 11 or 26 => _configuration.ReadSay,
        14 or 15 => _configuration.ReadParty,
        16 or 17 => _configuration.ReadAlliance,
        18 => _configuration.ReadYell,
        19 => _configuration.ReadShout,
        20 => _configuration.ReadFreeCompany,
        22 or 23 => _configuration.ReadTell,
        30 => _configuration.ReadLinkshell1,
        31 => _configuration.ReadLinkshell2,
        32 => _configuration.ReadLinkshell3,
        33 => _configuration.ReadLinkshell4,
        34 => _configuration.ReadLinkshell5,
        35 => _configuration.ReadLinkshell6,
        36 => _configuration.ReadLinkshell7,
        37 => _configuration.ReadLinkshell8,
        56 or 68 or 105 => _configuration.ReadEmote,
        57 => _configuration.ReadSystemMessage,
        61 => _configuration.ReadNoviceNetwork,
        _ => false,
    };

    private static string? GetChannelKey(int chatType) => chatType switch
    {
        10 or 11 => "say",
        14 or 15 => "party",
        16 or 17 => "alliance",
        18 => "yell",
        19 => "shout",
        20 => "freecompany",
        22 or 23 => "tell",
        30 => "linkshell1",
        31 => "linkshell2",
        32 => "linkshell3",
        33 => "linkshell4",
        34 => "linkshell5",
        35 => "linkshell6",
        36 => "linkshell7",
        37 => "linkshell8",
        56 or 68 or 105 => "emote",
        57 => "system",
        61 => "novicenetwork",
        _ => null,
    };
}
