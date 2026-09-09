using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FF14P2TTS.Application;
using FF14P2TTS.Infrastructure;
using FF14P2TTS.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FF14P2TTS;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/p2tts";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("FF14P2TTS");
    internal readonly Player2TtsService Player2Service;
    internal readonly AzureTtsService AzureService;
    private readonly ITtsServiceProvider _ttsServices;
    internal ITtsService ActiveTtsService => _ttsServices.Active;
    private readonly NpcTalkHandler _npcTalkHandler;
    private readonly AutoAdvanceHandler _autoAdvanceHandler;
    private readonly ChatSpeechCoordinator _chatSpeechCoordinator;
    private readonly NpcDialogueCoordinator _npcDialogueCoordinator;
    private readonly INpcGenderResolver _npcGenderResolver;
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private readonly DialogueTester _dialogueTester;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Migrate old config values
        if (Configuration.Version < 2)
        {
            if (Configuration.DefaultVoice == "default")
                Configuration.DefaultVoice = "";
            if (Configuration.Speed == 0)
                Configuration.Speed = 1.0;
            if (Configuration.Player2BaseUrl.Contains("52218"))
                Configuration.Player2BaseUrl = "http://127.0.0.1:4315";
            Configuration.Version = 2;
            Configuration.Save();
        }
        if (Configuration.Version < 3)
        {
            if (string.IsNullOrEmpty(Configuration.UnisexVoiceId))
                Configuration.UnisexVoiceId = "01955d76-ed5b-74de-83e5-800a44fee0d1";
            if (string.IsNullOrEmpty(Configuration.MaleVoiceId))
                Configuration.MaleVoiceId = "01955d76-ed5b-74c6-ac15-ab68ee19d560";
            if (string.IsNullOrEmpty(Configuration.FemaleVoiceId))
                Configuration.FemaleVoiceId = "01955d76-ed5b-73e0-a88d-cbeb3c5b499d";
            Configuration.Version = 3;
            Configuration.Save();
        }
        if (Configuration.Version < 4)
        {
            // Migrate to Azure defaults
            if (string.IsNullOrEmpty(Configuration.AzureDefaultVoice))
                Configuration.AzureDefaultVoice = "en-US-AriaNeural";
            if (string.IsNullOrEmpty(Configuration.AzureUnisexVoice))
                Configuration.AzureUnisexVoice = "en-US-AriaNeural";
            if (string.IsNullOrEmpty(Configuration.AzureMaleVoice))
                Configuration.AzureMaleVoice = "en-US-DavisNeural";
            if (string.IsNullOrEmpty(Configuration.AzureFemaleVoice))
                Configuration.AzureFemaleVoice = "en-US-JennyNeural";
            Configuration.Version = 5;
            Configuration.Save();
        }

        // Initialize TTS services (both, so user can switch at runtime)
        Player2Service = new Player2TtsService(Configuration, Log);
        AzureService = new AzureTtsService(Configuration, Log);

        _ttsServices = new TtsServiceProvider(Configuration, Player2Service, AzureService);
        _chatSpeechCoordinator = new ChatSpeechCoordinator(Configuration, _ttsServices);

        // Initialize NPC talk handler
        _npcTalkHandler = new NpcTalkHandler(AddonLifecycle, Log, Configuration);
        _npcTalkHandler.OnNpcTalk += OnNpcTalk;

        // Initialize auto-advance handler
        _autoAdvanceHandler = new AutoAdvanceHandler(Configuration, Log, GameGui);

        _npcGenderResolver = new DalamudNpcGenderResolver(DataManager, Log, Configuration);
        _npcDialogueCoordinator = new NpcDialogueCoordinator(
            Configuration,
            _ttsServices,
            new NpcVoiceMapper(Configuration),
            _npcGenderResolver,
            _autoAdvanceHandler,
            Log);

        // Create windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        // Register slash command
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Player2 TTS configuration. Subcommands: on, off, toggle, status, test [message], voice [name], engine [player2|azure]"
        });

        // Register UI callbacks
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Subscribe to chat messages (read-only, after game processes them)
        ChatGui.ChatMessageUnhandled += OnChatMessage;

        var engineName = Configuration.ActiveEngine == TtsEngine.MicrosoftAzure ? "Azure" : "Player2";
        Log.Information($"[FF14P2TTS] Plugin loaded. Engine: {engineName}");
        _dialogueTester = new DialogueTester(AddonLifecycle, NotificationManager, Condition);
    }

    public void Dispose()
    {
        _npcTalkHandler.OnNpcTalk -= OnNpcTalk;
        _npcTalkHandler.Dispose();
        _autoAdvanceHandler.Dispose();
        ChatGui.ChatMessageUnhandled -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        _dialogueTester.Dispose();
        MainWindow.Dispose();
        Player2Service.Dispose();
        AzureService.Dispose();
        CommandManager.RemoveHandler(CommandName);
        ChatGui.ChatMessageUnhandled -= OnChatMessage;
    }

    private void OnCommand(string command, string args)
    {
        var trimmedArgs = args.Trim().ToLowerInvariant();

        switch (trimmedArgs)
        {
            case "":
            case "config":
            case "settings":
                ToggleConfigUi();
                break;

            case "on":
            case "enable":
                Configuration.TtsEnabled = true;
                Configuration.Save();
                PrintChat("TTS enabled.");
                break;

            case "off":
            case "disable":
                Configuration.TtsEnabled = false;
                Configuration.Save();
                PrintChat("TTS disabled.");
                break;

            case "toggle":
                Configuration.TtsEnabled = !Configuration.TtsEnabled;
                Configuration.Save();
                PrintChat($"TTS {(Configuration.TtsEnabled ? "enabled" : "disabled")}.");
                break;

            case "status":
                _ = ShowStatusAsync();
                break;

            default:
                if (trimmedArgs.StartsWith("test "))
                {
                    var testText = args.Substring(5).Trim();
                    if (!string.IsNullOrWhiteSpace(testText))
                    {
                        _ = ActiveTtsService.SpeakAsync(testText);
                        PrintChat($"Sent to TTS: {testText}");
                    }
                }
                else if (trimmedArgs.StartsWith("voice "))
                {
                    var voice = args.Substring(6).Trim();
                    if (!string.IsNullOrWhiteSpace(voice))
                    {
                        Configuration.DefaultVoice = voice;
                        Configuration.Save();
                        PrintChat($"Default voice set to: {voice}");
                    }
                }
                else if (trimmedArgs.StartsWith("engine "))
                {
                    var engineName = args.Substring(7).Trim().ToLowerInvariant();
                    if (engineName == "player2" || engineName == "p2")
                    {
                        Configuration.ActiveEngine = TtsEngine.Player2;
                        Configuration.Save();
                        PrintChat("Switched to Player2 TTS engine.");
                    }
                    else if (engineName == "azure" || engineName == "az")
                    {
                        Configuration.ActiveEngine = TtsEngine.MicrosoftAzure;
                        Configuration.Save();
                        PrintChat("Switched to Microsoft Azure TTS engine.");
                    }
                    else
                    {
                        PrintChat("Usage: /p2tts engine [player2|azure]");
                    }
                }
                else
                {
                    ToggleMainUi();
                }
                break;
        }
    }

    private async Task ShowStatusAsync()
    {
        var engineName = Configuration.ActiveEngine == TtsEngine.MicrosoftAzure ? "Azure" : "Player2";
        PrintChat($"Checking {engineName} TTS server status...");
        var available = await ActiveTtsService.IsServerAvailableAsync();
        if (available)
        {
            PrintChat($"[FF14P2TTS] {engineName} TTS engine is ONLINE.");
            var voices = await ActiveTtsService.GetAvailableVoicesAsync();
            if (voices.Length > 0)
                PrintChat($"[FF14P2TTS] Available voices: {string.Join(", ", voices.Take(15))}{(voices.Length > 15 ? "..." : "")}");
        }
        else
        {
            PrintChat($"[FF14P2TTS] {engineName} TTS service is not available. Check your settings.");
        }
    }

    private void OnChatMessage(IChatMessage msg)
    {
        var chatType = (int)msg.LogKind;
        var senderName = msg.Sender.TextValue;
        var messageText = msg.Message.TextValue;

        var localPlayer = ObjectTable.LocalPlayer;
        var isOwnMessage = !Configuration.ReadOwnMessages
            && localPlayer is not null
            && senderName == localPlayer.Name.TextValue;

        if (_chatSpeechCoordinator.TrySpeak(chatType, senderName, messageText, ClientState.IsPvPExcludingDen, isOwnMessage))
            Log.Debug($"[FF14P2TTS] Queued TTS: {messageText}");
    }

    private void OnNpcTalk(string speaker, string text)
    {
        _npcDialogueCoordinator.Handle(speaker, text, _dialogueTester.IsNativeVoiceSubtitleVisible);
    }

    internal NpcGender GetNpcGender(string speakerName) => _npcGenderResolver.GetGender(speakerName);

#if false // Superseded by Application/NpcDialogueCoordinator.
    private string _lastNpcSpeaker = string.Empty;

    private void OnNpcTalk(string speaker, string text)
    {
        if (!Configuration.TtsEnabled) return;

        // TalkSubtitle is visible only while the game presents native spoken subtitles.
        // It is line-level state, so unvoiced cutscene dialogue remains eligible for TTS.
        if (Configuration.SkipTtsDuringCutscenes && _dialogueTester.IsNativeVoiceSubtitleVisible)
        {
            Log.Debug("[FF14P2TTS] Native voice subtitle visible — skipping NPC TTS");
            return;
        }

        // Prepend speaker name if enabled and different from last speaker
        var textToSpeak = text;
        if (Configuration.IncludeNpcSpeakerName && !string.IsNullOrWhiteSpace(speaker))
        {
            if (!string.Equals(speaker, _lastNpcSpeaker, StringComparison.OrdinalIgnoreCase))
            {
                textToSpeak = $"{speaker} says: {text}";
                _lastNpcSpeaker = speaker;
            }
        }

        var isAzure = Configuration.ActiveEngine == TtsEngine.MicrosoftAzure;
        var useGendered = isAzure ? Configuration.AzureUseGenderedVoices : Configuration.UseGenderedVoices;

        if (useGendered)
        {
            var gender = GetNpcGender(speaker);
            string voiceId;

            if (isAzure ? Configuration.AzureUsePerNpcVoices : Configuration.UsePerNpcVoices)
            {
                // Fetch fresh voice list (cached briefly)
                var voices = _cachedVoiceList;
                if (voices is null)
                {
                    _ = RefreshVoiceCacheAsync();
                    voiceId = isAzure
                        ? gender switch { NpcGender.Male => Configuration.AzureMaleVoice, NpcGender.Female => Configuration.AzureFemaleVoice, _ => Configuration.AzureUnisexVoice }
                        : gender switch { NpcGender.Male => Configuration.MaleVoiceId, NpcGender.Female => Configuration.FemaleVoiceId, _ => Configuration.UnisexVoiceId };
                }
                else
                {
                    voiceId = _npcVoiceMapper.GetVoiceForNpc(speaker, gender, voices);
                }
            }
            else
            {
                voiceId = isAzure
                    ? gender switch { NpcGender.Male => Configuration.AzureMaleVoice, NpcGender.Female => Configuration.AzureFemaleVoice, _ => Configuration.AzureUnisexVoice }
                    : gender switch { NpcGender.Male => Configuration.MaleVoiceId, NpcGender.Female => Configuration.FemaleVoiceId, _ => Configuration.UnisexVoiceId };
            }

            Log.Debug($"[FF14P2TTS] NPC: {speaker} -> {gender}, voice={voiceId}, engine={Configuration.ActiveEngine}");
            _ = ActiveTtsService.SpeakAsync(textToSpeak, voice: voiceId);
            _autoAdvanceHandler.OnDialogSpoken(textToSpeak);
        }
        else
        {
            var defaultVoice = isAzure ? Configuration.AzureUnisexVoice : Configuration.UnisexVoiceId;
            _ = ActiveTtsService.SpeakAsync(textToSpeak, voice: defaultVoice);
            _autoAdvanceHandler.OnDialogSpoken(textToSpeak);
        }
    }

    private List<VoiceInfo>? _cachedVoiceList;

    private async System.Threading.Tasks.Task RefreshVoiceCacheAsync()
    {
        try
        {
            var all = await ActiveTtsService.GetAvailableVoicesRawAsync();
            // Only use English voices for NPC assignments
            _cachedVoiceList = all
                .Where(v => v.RawLanguage == "american_english" || v.RawLanguage == "british_english")
                .ToList();
            if (_cachedVoiceList.Count == 0)
                _cachedVoiceList = all; // fallback if no English filter matches
        }
        catch
        {
            _cachedVoiceList = null;
        }
    }

    private readonly ConcurrentDictionary<string, NpcGender> _genderCache = new();

    internal NpcGender GetNpcGender(string speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName)) return NpcGender.Unknown;

        if (_genderCache.TryGetValue(speakerName, out var cached))
            return cached;

        var residents = DataManager.GetExcelSheet<ENpcResident>();
        var bases = DataManager.GetExcelSheet<ENpcBase>();

        if (residents != null && bases != null)
        {
            foreach (var row in residents)
            {
                if (string.Equals(row.Singular.ExtractText(), speakerName, StringComparison.OrdinalIgnoreCase))
                {
                    var baseRow = bases.GetRow(row.RowId);
                    var gender = baseRow.Gender == 1 ? NpcGender.Female :
                                 baseRow.Gender == 0 ? NpcGender.Male : NpcGender.Unknown;

                    _genderCache[speakerName] = gender;
                    Log.Debug($"[FF14P2TTS] Sheet: '{speakerName}' → {gender}");
                    return gender;
                }
            }
        }

        _genderCache[speakerName] = NpcGender.Unknown;
        Log.Debug($"[FF14P2TTS] '{speakerName}' not found in ENpcResident");
        return NpcGender.Unknown;
    }

    private bool ShouldReadChatType(int chatType)
    {
        // FFXIV chat type codes
        return chatType switch
        {
            10  => Configuration.ReadSay,            // Say
            11  => Configuration.ReadSay,            // Say (alternate)
            14  => Configuration.ReadParty,          // Party
            15  => Configuration.ReadParty,          // Party (alternate)
            16  => Configuration.ReadAlliance,       // Alliance
            17  => Configuration.ReadAlliance,       // Alliance (alternate)
            18  => Configuration.ReadYell,           // Yell
            19  => Configuration.ReadShout,          // Shout
            20  => Configuration.ReadFreeCompany,    // Free Company
            22  => Configuration.ReadTell,           // Tell (incoming)
            23  => Configuration.ReadTell,           // Tell (outgoing) - you'd hear your own tells being sent
            26  => Configuration.ReadSay,            // NPC Say
            30  => Configuration.ReadLinkshell1,     // Linkshell 1
            31  => Configuration.ReadLinkshell2,     // Linkshell 2
            32  => Configuration.ReadLinkshell3,     // Linkshell 3
            33  => Configuration.ReadLinkshell4,     // Linkshell 4
            34  => Configuration.ReadLinkshell5,     // Linkshell 5
            35  => Configuration.ReadLinkshell6,     // Linkshell 6
            36  => Configuration.ReadLinkshell7,     // Linkshell 7
            37  => Configuration.ReadLinkshell8,     // Linkshell 8
            61  => Configuration.ReadNoviceNetwork,  // Novice Network
            56  => Configuration.ReadEmote,          // Custom Emotes
            57  => Configuration.ReadSystemMessage,  // System Messages
            68  => Configuration.ReadEmote,          // Standard Emotes
            105 => Configuration.ReadEmote,          // Battle Emotes
            _   => false
        };
    }

    private static string? GetChannelKey(int chatType)
    {
        return chatType switch
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
            61 => "novicenetwork",
            56 or 68 or 105 => "emote",
            57 => "system",
            _ => null
        };
    }

 #endif

    private void PrintChat(string message)
    {
        ChatGui.Print($"[FF14P2TTS] {message}");
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
