using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FF14P2TTS.Application;
using FF14P2TTS.Infrastructure;
using FF14P2TTS.Infrastructure.Groq;
using FF14P2TTS.Infrastructure.Speechify;
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
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/ff14tts";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("FF14P2TTS");
    private readonly IReadOnlyDictionary<TtsEngine, ITtsService> _ttsServicesByEngine;
    private readonly ITtsServiceProvider _ttsServices;
    internal ITtsService ActiveTtsService => _ttsServices.Active;
    private readonly NpcTalkHandler _npcTalkHandler;
    private readonly NpcDialogueCoordinator _npcDialogueCoordinator;
    private readonly NpcVoiceMapper _npcVoiceMapper;
    private readonly INpcGenderResolver _npcGenderResolver;
    private readonly ActiveQuestReader _activeQuestReader;
    private readonly GroqEmotionTagger _groqTagger;
    private readonly SpeechifyDialoguePreloader _speechifyPreloader;
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
        if (Configuration.Version < 7)
        {
            Configuration.Version = 7;
            Configuration.Save();
        }
        if (Configuration.Version < 8)
        {
            Configuration.Version = 8;
            Configuration.Save();
        }
        if (Configuration.Version < 9)
        {
            if (Configuration.GroqModel == "llama-3.3-70b-versatile")
                Configuration.GroqModel = "openai/gpt-oss-20b";
            Configuration.Version = 9;
            Configuration.Save();
        }
        if (Configuration.Version < 10)
        {
            if (Configuration.GroqModel == "openai/gpt-oss-20b")
                Configuration.GroqModel = "openai/gpt-oss-120b";
            Configuration.Version = 10;
            Configuration.Save();
        }
        if (Configuration.Version < 11)
        {
            Configuration.Version = 11;
            Configuration.Save();
        }
        if (Configuration.Version < 12)
        {
            Configuration.Version = 12;
            Configuration.Save();
        }

        // Initialize TTS services (both, so user can switch at runtime)
        _ttsServicesByEngine = new TtsServiceFactory(Configuration, Log).Create();
        _ttsServices = new TtsServiceProvider(Configuration, _ttsServicesByEngine);

        // Initialize NPC talk handler
        _npcTalkHandler = new NpcTalkHandler(AddonLifecycle, Log, Configuration);
        _npcTalkHandler.OnNpcTalk += OnNpcTalk;

        _npcGenderResolver = new DalamudNpcGenderResolver(DataManager, Log, Configuration);
        _npcVoiceMapper = new NpcVoiceMapper(Configuration);
        _activeQuestReader = new ActiveQuestReader(DataManager);
        _groqTagger = new GroqEmotionTagger(Configuration, Log);
        Func<IReadOnlyList<ActiveQuestInfo>> activeQuests = () => _activeQuestReader.ReadActiveQuests();
        Func<IReadOnlyList<string>> activeQuestNames = () => activeQuests()
            .Select(quest => quest.Name)
            .ToList();
        Func<bool> questReadAvailable = () => _activeQuestReader.QuestReadAvailable;
        var voicedCutsceneDetector = new ConsoleGamesWikiVoicedCutsceneDetector(
            Log,
            activeQuestNames,
            questReadAvailable);
        _npcDialogueCoordinator = new NpcDialogueCoordinator(
            Configuration,
            _ttsServices,
            _npcVoiceMapper,
            _npcGenderResolver,
            voicedCutsceneDetector,
            Log);

        // Preload Speechify audio for the active quests' wiki-scripted unvoiced
        // dialogue so the next line plays without Speechify synthesis latency.
        _speechifyPreloader = new SpeechifyDialoguePreloader(
            Configuration,
            Log,
            (SpeechifyTtsService)_ttsServicesByEngine[TtsEngine.Speechify],
            voicedCutsceneDetector,
            activeQuests,
            speaker => _npcDialogueCoordinator.ResolveVoiceFor(speaker));
        _npcDialogueCoordinator.SetPreloader(_speechifyPreloader);
        _speechifyPreloader.Start();
        ClientState.TerritoryChanged += OnTerritoryChanged;

        // Create windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        // Register slash command
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open settings or show active quests. Subcommands: on, off, toggle, status, "
                        + "quests, test [message], voice [name], engine [player2|azure|elevenlabs|speechify]"
        });

        // Register UI callbacks
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Subscribe to chat messages (read-only, after game processes them)
        Log.Information($"[FF14P2TTS] Plugin loaded. Engine: {GetEngineDisplayName(Configuration.ActiveEngine)}");
        _dialogueTester = new DialogueTester(AddonLifecycle);
    }

    public void Dispose()
    {
        _npcTalkHandler.OnNpcTalk -= OnNpcTalk;
        _npcTalkHandler.Dispose();
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        _dialogueTester.Dispose();
        MainWindow.Dispose();
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        _speechifyPreloader.Dispose();
        foreach (var service in _ttsServicesByEngine.Values)
            service.Dispose();
        CommandManager.RemoveHandler(CommandName);
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

            case "quests":
                ShowActiveQuests();
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
                else if (trimmedArgs.StartsWith("tag "))
                {
                    var tagText = args.Substring(4).Trim();
                    if (!string.IsNullOrWhiteSpace(tagText))
                        _ = ShowTagAsync(tagText);
                }
                else if (trimmedArgs.StartsWith("emotion "))
                {
                    var rest = args.Substring(8).Trim();
                    var spaceIndex = rest.IndexOf(' ');
                    if (spaceIndex < 0)
                    {
                        PrintChat("Usage: /ff14tts emotion <emotion> <text>");
                    }
                    else
                    {
                        var emotion = rest[..spaceIndex].Trim().ToLowerInvariant();
                        var text = rest[(spaceIndex + 1)..].Trim();
                        _ = SpeakWithEmotionAsync(emotion, text);
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
                    else if (engineName == "elevenlabs" || engineName == "eleven")
                    {
                        Configuration.ActiveEngine = TtsEngine.ElevenLabs;
                        Configuration.Save();
                        PrintChat("Switched to ElevenLabs TTS engine. API integration is not enabled yet.");
                    }
                    else if (engineName == "speechify")
                    {
                        Configuration.ActiveEngine = TtsEngine.Speechify;
                        Configuration.Save();
                        PrintChat("Switched to Speechify TTS engine.");
                    }
                    else
                    {
                        PrintChat("Usage: /ff14tts engine [player2|azure|elevenlabs|speechify]");
                    }
                }
                else
                {
                    ToggleMainUi();
                }
                break;
        }
    }

    private unsafe void ShowActiveQuests()
    {
        PrintChat("Reading active quests from the game...");
        var quests = _activeQuestReader.ReadActiveQuests(out var diagnostic);
        Log.Information($"[FF14P2TTS] {diagnostic}");
        if (quests.Count == 0)
        {
            PrintChat($"No active normal quests found. {diagnostic}");
            return;
        }

        PrintChat($"Active quests: {quests.Count}");
        foreach (var quest in quests)
            PrintChat($"{quest.Name} (ID {quest.Id}, step {quest.Sequence})");
    }

    private static readonly string[] ValidEmotions =
    {
        "angry", "cheerful", "sad", "terrified", "relaxed", "fearful", "surprised",
        "calm", "assertive", "energetic", "warm", "direct", "bright",
    };

    private async Task SpeakWithEmotionAsync(string emotion, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!ValidEmotions.Contains(emotion))
        {
            PrintChat($"Unknown emotion '{emotion}'. Valid: {string.Join(", ", ValidEmotions)}");
            return;
        }

        if (_ttsServices.Active is not ISsmlSpeechProvider ssmlProvider)
        {
            PrintChat("The active TTS engine does not support direct emotion SSML. Switch to Speechify.");
            return;
        }

        var escaped = System.Security.SecurityElement.Escape(text);
        var ssml = $"<speak><speechify:style emotion=\"{emotion}\">{escaped}</speechify:style></speak>";
        PrintChat($"Speaking with emotion '{emotion}': {text}");
        await ssmlProvider.SpeakSsmlAsync(ssml);
    }

    private async Task ShowTagAsync(string text)
    {
        PrintChat("Tagging with Groq...");
        var (tagged, error) = await _groqTagger.TagWithErrorAsync(text, System.Threading.CancellationToken.None);
        if (tagged is null)
            PrintChat($"Groq tagging failed: {error}");
        else
            PrintChat($"Tagged: {tagged}");
    }

    private async Task ShowStatusAsync()
    {
        var engineName = GetEngineDisplayName(Configuration.ActiveEngine);
        PrintChat($"Checking {engineName} TTS server status...");
        var available = await ActiveTtsService.IsServerAvailableAsync();
        if (available)
        {
            PrintChat($"[FF14P2TTS] {engineName} TTS engine is ONLINE.");
            var voices = await ActiveTtsService.GetAvailableVoicesAsync();
            if (voices.Length > 0)
            {
                var preview = string.Join(", ", voices.Take(15));
                var suffix = voices.Length > 15 ? "..." : "";
                PrintChat($"[FF14P2TTS] Available voices: {preview}{suffix}");
            }
        }
        else
        {
            PrintChat($"[FF14P2TTS] {engineName} TTS service is not available. Check your settings.");
        }
    }

    private void OnNpcTalk(string speaker, string text)
    {
        _ = _npcDialogueCoordinator.HandleAsync(speaker, text, _dialogueTester.IsNativeVoiceSubtitleActive());
    }

    private void OnTerritoryChanged(uint territoryType)
    {
        _speechifyPreloader.SweepForFinishedQuests();
    }

    internal NpcGender GetNpcGender(string speakerName) => _npcGenderResolver.GetGender(speakerName);

    internal void ForgetNpc(string speakerName)
    {
        _npcGenderResolver.Forget(speakerName);
        _npcVoiceMapper.ForgetNpc(speakerName);
    }

    private static string GetEngineDisplayName(TtsEngine engine) => engine switch
    {
        TtsEngine.MicrosoftAzure => "Azure",
        TtsEngine.ElevenLabs => "ElevenLabs",
        TtsEngine.Speechify => "Speechify",
        _ => "Player2",
    };

    private void PrintChat(string message)
    {
        ChatGui.Print($"[FF14P2TTS] {message}");
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
