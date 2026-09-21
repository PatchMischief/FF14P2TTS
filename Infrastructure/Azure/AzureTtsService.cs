using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FF14P2TTS.Infrastructure.Audio;
using Microsoft.CognitiveServices.Speech;

namespace FF14P2TTS.Infrastructure.Azure;

/// <summary>
/// TTS service backed by Microsoft Azure Cognitive Services Speech API.
/// Uses the Microsoft.CognitiveServices.Speech SDK for synthesis.
/// </summary>
public class AzureTtsService : ITtsService
{
    private const string GroqApiUrl = "https://api.groq.com/openai/v1/chat/completions";
    private static readonly HttpClient GroqHttpClient = new();
    private static readonly HttpClient VoiceListHttpClient = new();
    private static readonly string[] AzureEmotions =
    {
        "cheerful", "excited", "sad", "angry", "terrified", "fearful", "whispering",
        "shouting", "hopeful", "unfriendly", "disgruntled", "depressed", "embarrassed",
    };

    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private SpeechConfig? _speechConfig;
    private string? _cachedKey;
    private string? _cachedRegion;
    private string? _cachedEndpoint;
    private readonly DuplicateMessageFilter _duplicateFilter = new();
    private readonly object _lock = new();

    // Full list of Azure neural voices (all English locales).
    public static readonly List<VoiceInfo> KnownEnglishVoices = new()
    {
        new()
        {
            Id = "en-US-AdamMultilingualNeural",
            Name = "AdamMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "AdamMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-AlloyTurboMultilingualNeural",
            Name = "AlloyTurboMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "AlloyTurboMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-AmandaMultilingualNeural",
            Name = "AmandaMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "AmandaMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-AmberNeural",
            Name = "Amber",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Amber (en-US) [F]",
        },
        new()
        {
            Id = "en-US-AnaNeural",
            Name = "Ana",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Ana (en-US) [F]",
        },
        new()
        {
            Id = "en-US-AndrewMultilingualNeural",
            Name = "AndrewMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "AndrewMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-AndrewNeural",
            Name = "Andrew",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Andrew (en-US) [M]",
        },
        new()
        {
            Id = "en-US-AriaNeural",
            Name = "Aria",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Aria (en-US) [F]",
        },
        new()
        {
            Id = "en-US-AshTurboMultilingualNeural",
            Name = "AshTurboMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "AshTurboMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-AshleyNeural",
            Name = "Ashley",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Ashley (en-US) [F]",
        },
        new()
        {
            Id = "en-US-AvaMultilingualNeural",
            Name = "AvaMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "AvaMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-AvaNeural",
            Name = "Ava",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Ava (en-US) [F]",
        },
        new()
        {
            Id = "en-US-BlueNeural",
            Name = "Blue",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "not_specified",
            DisplayName = "Blue (en-US) [?]",
        },
        new()
        {
            Id = "en-US-BrandonMultilingualNeural",
            Name = "BrandonMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "BrandonMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-BrandonNeural",
            Name = "Brandon",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Brandon (en-US) [M]",
        },
        new()
        {
            Id = "en-US-BrianMultilingualNeural",
            Name = "BrianMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "BrianMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-BrianNeural",
            Name = "Brian",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Brian (en-US) [M]",
        },
        new()
        {
            Id = "en-US-ChristopherMultilingualNeural",
            Name = "ChristopherMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "ChristopherMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-ChristopherNeural",
            Name = "Christopher",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Christopher (en-US) [M]",
        },
        new()
        {
            Id = "en-US-CoraMultilingualNeural",
            Name = "CoraMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "CoraMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-CoraNeural",
            Name = "Cora",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Cora (en-US) [F]",
        },
        new()
        {
            Id = "en-US-DavisMultilingualNeural",
            Name = "DavisMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "DavisMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-DavisNeural",
            Name = "Davis",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Davis (en-US) [M]",
        },
        new()
        {
            Id = "en-US-DerekMultilingualNeural",
            Name = "DerekMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "DerekMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-DustinMultilingualNeural",
            Name = "DustinMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "DustinMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-EchoTurboMultilingualNeural",
            Name = "EchoTurboMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "EchoTurboMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-ElizabethNeural",
            Name = "Elizabeth",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Elizabeth (en-US) [F]",
        },
        new()
        {
            Id = "en-US-EmmaMultilingualNeural",
            Name = "EmmaMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "EmmaMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-EmmaNeural",
            Name = "Emma",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Emma (en-US) [F]",
        },
        new()
        {
            Id = "en-US-EricNeural",
            Name = "Eric",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Eric (en-US) [M]",
        },
        new()
        {
            Id = "en-US-EvelynMultilingualNeural",
            Name = "EvelynMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "EvelynMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-FableTurboMultilingualNeural",
            Name = "FableTurboMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "not_specified",
            DisplayName = "FableTurboMultilingual (en-US) [?]",
        },
        new()
        {
            Id = "en-US-GuyNeural",
            Name = "Guy",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Guy (en-US) [M]",
        },
        new()
        {
            Id = "en-US-JacobNeural",
            Name = "Jacob",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Jacob (en-US) [M]",
        },
        new()
        {
            Id = "en-US-JaneNeural",
            Name = "Jane",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Jane (en-US) [F]",
        },
        new()
        {
            Id = "en-US-JasonNeural",
            Name = "Jason",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Jason (en-US) [M]",
        },
        new()
        {
            Id = "en-US-JennyMultilingualNeural",
            Name = "JennyMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "JennyMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-JennyNeural",
            Name = "Jenny",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Jenny (en-US) [F]",
        },
        new()
        {
            Id = "en-US-KaiNeural",
            Name = "Kai",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Kai (en-US) [M]",
        },
        new()
        {
            Id = "en-US-LewisMultilingualNeural",
            Name = "LewisMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "LewisMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-LolaMultilingualNeural",
            Name = "LolaMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "LolaMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-LunaNeural",
            Name = "Luna",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Luna (en-US) [F]",
        },
        new()
        {
            Id = "en-US-MichelleNeural",
            Name = "Michelle",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Michelle (en-US) [F]",
        },
        new()
        {
            Id = "en-US-MonicaNeural",
            Name = "Monica",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Monica (en-US) [F]",
        },
        new()
        {
            Id = "en-US-NancyMultilingualNeural",
            Name = "NancyMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "NancyMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-NancyNeural",
            Name = "Nancy",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Nancy (en-US) [F]",
        },
        new()
        {
            Id = "en-US-NovaTurboMultilingualNeural",
            Name = "NovaTurboMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "NovaTurboMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-OnyxTurboMultilingualNeural",
            Name = "OnyxTurboMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "OnyxTurboMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-PhoebeMultilingualNeural",
            Name = "PhoebeMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "PhoebeMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-RogerNeural",
            Name = "Roger",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Roger (en-US) [M]",
        },
        new()
        {
            Id = "en-US-RyanMultilingualNeural",
            Name = "RyanMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "RyanMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-SamuelMultilingualNeural",
            Name = "SamuelMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "SamuelMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-SaraNeural",
            Name = "Sara",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "Sara (en-US) [F]",
        },
        new()
        {
            Id = "en-US-SerenaMultilingualNeural",
            Name = "SerenaMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "SerenaMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-ShimmerTurboMultilingualNeural",
            Name = "ShimmerTurboMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "female",
            DisplayName = "ShimmerTurboMultilingual (en-US) [F]",
        },
        new()
        {
            Id = "en-US-SteffanMultilingualNeural",
            Name = "SteffanMultilingual",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "SteffanMultilingual (en-US) [M]",
        },
        new()
        {
            Id = "en-US-SteffanNeural",
            Name = "Steffan",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Steffan (en-US) [M]",
        },
        new()
        {
            Id = "en-US-TonyNeural",
            Name = "Tony",
            RawLanguage = "english",
            Language = "en-US",
            Gender = "male",
            DisplayName = "Tony (en-US) [M]",
        },
        new()
        {
            Id = "en-AU-AnnetteNeural",
            Name = "Annette",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "female",
            DisplayName = "Annette (en-AU) [F]",
        },
        new()
        {
            Id = "en-AU-CarlyNeural",
            Name = "Carly",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "female",
            DisplayName = "Carly (en-AU) [F]",
        },
        new()
        {
            Id = "en-AU-DarrenNeural",
            Name = "Darren",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "male",
            DisplayName = "Darren (en-AU) [M]",
        },
        new()
        {
            Id = "en-AU-DuncanNeural",
            Name = "Duncan",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "male",
            DisplayName = "Duncan (en-AU) [M]",
        },
        new()
        {
            Id = "en-AU-ElsieNeural",
            Name = "Elsie",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "female",
            DisplayName = "Elsie (en-AU) [F]",
        },
        new()
        {
            Id = "en-AU-FreyaNeural",
            Name = "Freya",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "female",
            DisplayName = "Freya (en-AU) [F]",
        },
        new()
        {
            Id = "en-AU-JoanneNeural",
            Name = "Joanne",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "female",
            DisplayName = "Joanne (en-AU) [F]",
        },
        new()
        {
            Id = "en-AU-KenNeural",
            Name = "Ken",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "male",
            DisplayName = "Ken (en-AU) [M]",
        },
        new()
        {
            Id = "en-AU-KimNeural",
            Name = "Kim",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "female",
            DisplayName = "Kim (en-AU) [F]",
        },
        new()
        {
            Id = "en-AU-NatashaNeural",
            Name = "Natasha",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "female",
            DisplayName = "Natasha (en-AU) [F]",
        },
        new()
        {
            Id = "en-AU-NeilNeural",
            Name = "Neil",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "male",
            DisplayName = "Neil (en-AU) [M]",
        },
        new()
        {
            Id = "en-AU-TimNeural",
            Name = "Tim",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "male",
            DisplayName = "Tim (en-AU) [M]",
        },
        new()
        {
            Id = "en-AU-TinaNeural",
            Name = "Tina",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "female",
            DisplayName = "Tina (en-AU) [F]",
        },
        new()
        {
            Id = "en-AU-WilliamMultilingualNeural",
            Name = "WilliamMultilingual",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "male",
            DisplayName = "WilliamMultilingual (en-AU) [M]",
        },
        new()
        {
            Id = "en-AU-WilliamNeural",
            Name = "William",
            RawLanguage = "english",
            Language = "en-AU",
            Gender = "male",
            DisplayName = "William (en-AU) [M]",
        },
        new()
        {
            Id = "en-CA-ClaraNeural",
            Name = "Clara",
            RawLanguage = "english",
            Language = "en-CA",
            Gender = "female",
            DisplayName = "Clara (en-CA) [F]",
        },
        new()
        {
            Id = "en-CA-LiamNeural",
            Name = "Liam",
            RawLanguage = "english",
            Language = "en-CA",
            Gender = "male",
            DisplayName = "Liam (en-CA) [M]",
        },
        new()
        {
            Id = "en-GB-AbbiNeural",
            Name = "Abbi",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "female",
            DisplayName = "Abbi (en-GB) [F]",
        },
        new()
        {
            Id = "en-GB-AdaMultilingualNeural",
            Name = "AdaMultilingual",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "female",
            DisplayName = "AdaMultilingual (en-GB) [F]",
        },
        new()
        {
            Id = "en-GB-AlfieNeural",
            Name = "Alfie",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "male",
            DisplayName = "Alfie (en-GB) [M]",
        },
        new()
        {
            Id = "en-GB-BellaNeural",
            Name = "Bella",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "female",
            DisplayName = "Bella (en-GB) [F]",
        },
        new()
        {
            Id = "en-GB-ElliotNeural",
            Name = "Elliot",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "male",
            DisplayName = "Elliot (en-GB) [M]",
        },
        new()
        {
            Id = "en-GB-EthanNeural",
            Name = "Ethan",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "male",
            DisplayName = "Ethan (en-GB) [M]",
        },
        new()
        {
            Id = "en-GB-HollieNeural",
            Name = "Hollie",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "female",
            DisplayName = "Hollie (en-GB) [F]",
        },
        new()
        {
            Id = "en-GB-LibbyNeural",
            Name = "Libby",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "female",
            DisplayName = "Libby (en-GB) [F]",
        },
        new()
        {
            Id = "en-GB-MaisieNeural",
            Name = "Maisie",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "female",
            DisplayName = "Maisie (en-GB) [F]",
        },
        new()
        {
            Id = "en-GB-NoahNeural",
            Name = "Noah",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "male",
            DisplayName = "Noah (en-GB) [M]",
        },
        new()
        {
            Id = "en-GB-OliverNeural",
            Name = "Oliver",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "male",
            DisplayName = "Oliver (en-GB) [M]",
        },
        new()
        {
            Id = "en-GB-OliviaNeural",
            Name = "Olivia",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "female",
            DisplayName = "Olivia (en-GB) [F]",
        },
        new()
        {
            Id = "en-GB-OllieMultilingualNeural",
            Name = "OllieMultilingual",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "male",
            DisplayName = "OllieMultilingual (en-GB) [M]",
        },
        new()
        {
            Id = "en-GB-RyanNeural",
            Name = "Ryan",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "male",
            DisplayName = "Ryan (en-GB) [M]",
        },
        new()
        {
            Id = "en-GB-SoniaNeural",
            Name = "Sonia",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "female",
            DisplayName = "Sonia (en-GB) [F]",
        },
        new()
        {
            Id = "en-GB-ThomasNeural",
            Name = "Thomas",
            RawLanguage = "english",
            Language = "en-GB",
            Gender = "male",
            DisplayName = "Thomas (en-GB) [M]",
        },
        new()
        {
            Id = "en-HK-SamNeural",
            Name = "Sam",
            RawLanguage = "english",
            Language = "en-HK",
            Gender = "male",
            DisplayName = "Sam (en-HK) [M]",
        },
        new()
        {
            Id = "en-HK-YanNeural",
            Name = "Yan",
            RawLanguage = "english",
            Language = "en-HK",
            Gender = "female",
            DisplayName = "Yan (en-HK) [F]",
        },
        new()
        {
            Id = "en-IE-ConnorNeural",
            Name = "Connor",
            RawLanguage = "english",
            Language = "en-IE",
            Gender = "male",
            DisplayName = "Connor (en-IE) [M]",
        },
        new()
        {
            Id = "en-IE-EmilyNeural",
            Name = "Emily",
            RawLanguage = "english",
            Language = "en-IE",
            Gender = "female",
            DisplayName = "Emily (en-IE) [F]",
        },
        new()
        {
            Id = "en-IN-AaravNeural",
            Name = "Aarav",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "male",
            DisplayName = "Aarav (en-IN) [M]",
        },
        new()
        {
            Id = "en-IN-AartiIndicNeural",
            Name = "AartiIndic",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "female",
            DisplayName = "AartiIndic (en-IN) [F]",
        },
        new()
        {
            Id = "en-IN-AartiNeural",
            Name = "Aarti",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "female",
            DisplayName = "Aarti (en-IN) [F]",
        },
        new()
        {
            Id = "en-IN-AashiNeural",
            Name = "Aashi",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "female",
            DisplayName = "Aashi (en-IN) [F]",
        },
        new()
        {
            Id = "en-IN-AnanyaNeural",
            Name = "Ananya",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "female",
            DisplayName = "Ananya (en-IN) [F]",
        },
        new()
        {
            Id = "en-IN-ArjunIndicNeural",
            Name = "ArjunIndic",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "male",
            DisplayName = "ArjunIndic (en-IN) [M]",
        },
        new()
        {
            Id = "en-IN-ArjunNeural",
            Name = "Arjun",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "male",
            DisplayName = "Arjun (en-IN) [M]",
        },
        new()
        {
            Id = "en-IN-KavyaNeural",
            Name = "Kavya",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "female",
            DisplayName = "Kavya (en-IN) [F]",
        },
        new()
        {
            Id = "en-IN-KunalNeural",
            Name = "Kunal",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "male",
            DisplayName = "Kunal (en-IN) [M]",
        },
        new()
        {
            Id = "en-IN-NeerjaIndicNeural",
            Name = "NeerjaIndic",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "female",
            DisplayName = "NeerjaIndic (en-IN) [F]",
        },
        new()
        {
            Id = "en-IN-NeerjaNeural",
            Name = "Neerja",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "female",
            DisplayName = "Neerja (en-IN) [F]",
        },
        new()
        {
            Id = "en-IN-PrabhatIndicNeural",
            Name = "PrabhatIndic",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "male",
            DisplayName = "PrabhatIndic (en-IN) [M]",
        },
        new()
        {
            Id = "en-IN-PrabhatNeural",
            Name = "Prabhat",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "male",
            DisplayName = "Prabhat (en-IN) [M]",
        },
        new()
        {
            Id = "en-IN-RehaanNeural",
            Name = "Rehaan",
            RawLanguage = "english",
            Language = "en-IN",
            Gender = "male",
            DisplayName = "Rehaan (en-IN) [M]",
        },
        new()
        {
            Id = "en-KE-AsiliaNeural",
            Name = "Asilia",
            RawLanguage = "english",
            Language = "en-KE",
            Gender = "female",
            DisplayName = "Asilia (en-KE) [F]",
        },
        new()
        {
            Id = "en-KE-ChilembaNeural",
            Name = "Chilemba",
            RawLanguage = "english",
            Language = "en-KE",
            Gender = "male",
            DisplayName = "Chilemba (en-KE) [M]",
        },
        new()
        {
            Id = "en-NG-AbeoNeural",
            Name = "Abeo",
            RawLanguage = "english",
            Language = "en-NG",
            Gender = "male",
            DisplayName = "Abeo (en-NG) [M]",
        },
        new()
        {
            Id = "en-NG-EzinneNeural",
            Name = "Ezinne",
            RawLanguage = "english",
            Language = "en-NG",
            Gender = "female",
            DisplayName = "Ezinne (en-NG) [F]",
        },
        new()
        {
            Id = "en-NZ-MitchellNeural",
            Name = "Mitchell",
            RawLanguage = "english",
            Language = "en-NZ",
            Gender = "male",
            DisplayName = "Mitchell (en-NZ) [M]",
        },
        new()
        {
            Id = "en-NZ-MollyNeural",
            Name = "Molly",
            RawLanguage = "english",
            Language = "en-NZ",
            Gender = "female",
            DisplayName = "Molly (en-NZ) [F]",
        },
        new()
        {
            Id = "en-PH-JamesNeural",
            Name = "James",
            RawLanguage = "english",
            Language = "en-PH",
            Gender = "male",
            DisplayName = "James (en-PH) [M]",
        },
        new()
        {
            Id = "en-PH-RosaNeural",
            Name = "Rosa",
            RawLanguage = "english",
            Language = "en-PH",
            Gender = "female",
            DisplayName = "Rosa (en-PH) [F]",
        },
        new()
        {
            Id = "en-SG-LunaNeural",
            Name = "Luna",
            RawLanguage = "english",
            Language = "en-SG",
            Gender = "female",
            DisplayName = "Luna (en-SG) [F]",
        },
        new()
        {
            Id = "en-SG-WayneNeural",
            Name = "Wayne",
            RawLanguage = "english",
            Language = "en-SG",
            Gender = "male",
            DisplayName = "Wayne (en-SG) [M]",
        },
        new()
        {
            Id = "en-TZ-ElimuNeural",
            Name = "Elimu",
            RawLanguage = "english",
            Language = "en-TZ",
            Gender = "male",
            DisplayName = "Elimu (en-TZ) [M]",
        },
        new()
        {
            Id = "en-TZ-ImaniNeural",
            Name = "Imani",
            RawLanguage = "english",
            Language = "en-TZ",
            Gender = "female",
            DisplayName = "Imani (en-TZ) [F]",
        },
        new()
        {
            Id = "en-ZA-LeahNeural",
            Name = "Leah",
            RawLanguage = "english",
            Language = "en-ZA",
            Gender = "female",
            DisplayName = "Leah (en-ZA) [F]",
        },
        new()
        {
            Id = "en-ZA-LukeNeural",
            Name = "Luke",
            RawLanguage = "english",
            Language = "en-ZA",
            Gender = "male",
            DisplayName = "Luke (en-ZA) [M]",
        },
    };

    public AzureTtsService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>
    /// Get or create the SpeechConfig (shared, no per-call synthesizer caching).
    /// Each SpeakAsync call creates its own SpeechSynthesizer to avoid race conditions.
    /// </summary>
    private SpeechConfig GetSpeechConfig()
    {
        lock (_lock)
        {
            var key = _config.AzureSubscriptionKey;
            var region = _config.AzureRegion;
            var endpoint = _config.AzureEndpoint;

            // Rebuild the cached SpeechConfig whenever the auth settings change.
            // Without this, changing the key/region in-game leaves the old
            // (possibly invalid) config in use until the plugin is reloaded.
            if (_speechConfig != null
                && string.Equals(_cachedKey, key, StringComparison.Ordinal)
                && string.Equals(_cachedRegion, region, StringComparison.Ordinal)
                && string.Equals(_cachedEndpoint, endpoint, StringComparison.Ordinal))
            {
                return _speechConfig;
            }

            if (!string.IsNullOrWhiteSpace(region))
            {
                _speechConfig = SpeechConfig.FromSubscription(key, region);
                _log.Debug($"[FF14P2TTS-Azure] Using Azure region: {region}");
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    _log.Warning("[FF14P2TTS-Azure] AzureRegion is set, so AzureEndpoint is ignored. Clear the region field to use the custom endpoint.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(endpoint))
            {
                _log.Warning("[FF14P2TTS-Azure] No region configured, falling back to custom endpoint.");
                _speechConfig = SpeechConfig.FromEndpoint(new Uri(endpoint.TrimEnd('/')), key);
            }
            else
            {
                _log.Error("[FF14P2TTS-Azure] Neither Azure Region nor Endpoint configured.");
                _speechConfig = SpeechConfig.FromSubscription(key, "eastus");
            }

            // Increase timeouts for long NPC dialogue lines
            _speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "15000");
            _speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "10000");

            _cachedKey = key;
            _cachedRegion = region;
            _cachedEndpoint = endpoint;

            return _speechConfig;
        }
    }

    public async Task<bool> IsServerAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.AzureSubscriptionKey))
            return false;
        return true; // Azure is cloud-based; we can't easily ping without making a call
    }

    public async Task<List<VoiceInfo>> GetAvailableVoicesRawAsync(CancellationToken ct = default)
    {
        // Start from the built-in catalog so the UI always has a complete English list.
        var voices = new List<VoiceInfo>(KnownEnglishVoices);

        // Prefer a live fetch so newly released Azure voices appear without a plugin update.
        var fetched = await FetchVoicesFromAzureAsync(ct).ConfigureAwait(false);
        if (fetched is { Count: > 0 })
        {
            foreach (var voice in fetched)
            {
                var existing = voices.FirstOrDefault(v =>
                    string.Equals(v.Id, voice.Id, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    voices.Add(voice);
                }
                else
                {
                    // Refresh metadata from the live catalog while keeping a stable object identity.
                    existing.Name = voice.Name;
                    existing.RawLanguage = voice.RawLanguage;
                    existing.Language = voice.Language;
                    existing.Gender = voice.Gender;
                    existing.DisplayName = voice.DisplayName;
                }
            }
        }

        // If the user's configured voices aren't in the known list, add them
        AddCustomVoiceIfMissing(voices, _config.AzureDefaultVoice);
        AddCustomVoiceIfMissing(voices, _config.AzureUnisexVoice);
        AddCustomVoiceIfMissing(voices, _config.AzureMaleVoice);
        AddCustomVoiceIfMissing(voices, _config.AzureFemaleVoice);

        return voices;
    }

    /// <summary>
    /// Fetches the authoritative voice list from the Azure Speech REST endpoint and
    /// returns the English subset. Returns null when the key is missing or the call fails,
    /// so callers fall back to the built-in catalog.
    /// </summary>
    private async Task<List<VoiceInfo>?> FetchVoicesFromAzureAsync(CancellationToken ct)
    {
        var key = _config.AzureSubscriptionKey;
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var region = string.IsNullOrWhiteSpace(_config.AzureRegion)
            ? "eastus"
            : _config.AzureRegion.Trim();
        var url = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/voices/list";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Ocp-Apim-Subscription-Key", key);

            using var response = await VoiceListHttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Debug($"[FF14P2TTS-Azure] Voice list fetch returned {(int)response.StatusCode}; using built-in list.");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var voices = new List<VoiceInfo>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("ShortName", out var shortNameEl) ||
                    !element.TryGetProperty("Locale", out var localeEl))
                {
                    continue;
                }

                var id = shortNameEl.GetString();
                var locale = localeEl.GetString();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(locale))
                    continue;
                if (!locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    continue;

                var gender = element.TryGetProperty("Gender", out var genderEl) &&
                             genderEl.ValueKind == JsonValueKind.String
                    ? genderEl.GetString()?.ToLowerInvariant()
                    : null;
                var genderNormalized = gender switch
                {
                    "male" => "male",
                    "female" => "female",
                    _ => "not_specified",
                };

                var name = DeriveVoiceName(id);
                var genderLetter = genderNormalized == "male" ? "M"
                    : genderNormalized == "female" ? "F"
                    : "?";

                voices.Add(new VoiceInfo
                {
                    Id = id,
                    Name = name,
                    RawLanguage = "english",
                    Language = locale,
                    Gender = genderNormalized,
                    DisplayName = $"{name} ({locale}) [{genderLetter}]",
                });
            }

            return voices;
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS-Azure] Voice list fetch error: {ex.Message}");
            return null;
        }
    }

    private static string DeriveVoiceName(string voiceId)
    {
        // "en-US-AriaNeural" -> "Aria"; "en-US-AdamMultilingualNeural" -> "AdamMultilingual"
        var localeEnd = voiceId.IndexOf('-', 3);
        var name = localeEnd > 0 ? voiceId[(localeEnd + 1)..] : voiceId;
        const string suffix = "Neural";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            name = name[..^suffix.Length];
        return name;
    }

    private static void AddCustomVoiceIfMissing(List<VoiceInfo> voices, string voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName)) return;
        if (voices.Any(v => string.Equals(v.Id, voiceName, StringComparison.OrdinalIgnoreCase))) return;

        voices.Add(new VoiceInfo
        {
            Id = voiceName,
            Name = voiceName,
            RawLanguage = "unknown",
            Language = "??",
            Gender = "",
            DisplayName = $"{voiceName} (custom)"
        });
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        // Azure Speech SDK per-call synthesizers can't be externally stopped.
        // Each SpeakAsync owns its synthesizer and disposes it when done.
        // Rapidly arriving new lines will naturally overlap — that's fine.
        await Task.CompletedTask;
    }

    public async Task SpeakAsync(
        string text,
        string? voice = null,
        double? speed = null,
        int? pitch = null,
        int? volume = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (string.IsNullOrWhiteSpace(_config.AzureSubscriptionKey))
        {
            _log.Error("[FF14P2TTS-Azure] Cannot speak: Azure subscription key is empty. Set it under /p2tts -> Azure Connection.");
            return;
        }

        text = SanitizeText(text);

        if (_config.SkipDuplicateMessages && _duplicateFilter.ShouldSkip(text))
            return;

        var effectiveVoice = voice ?? _config.AzureDefaultVoice;
        var effectiveVolume = volume ?? _config.Volume;
        var effectiveSpeed = speed ?? _config.Speed;

        // Split long text (>500 chars) into sentences to avoid synthesis timeouts
        if (text.Length > 500)
        {
            var sentences = SplitIntoSentences(text);
            foreach (var sentence in sentences)
            {
                if (ct.IsCancellationRequested) break;
                await SpeakSingleAsync(
                    sentence, effectiveVoice, effectiveSpeed, pitch, effectiveVolume, ct).ConfigureAwait(false);
            }
        }
        else
        {
            await SpeakSingleAsync(
                text, effectiveVoice, effectiveSpeed, pitch, effectiveVolume, ct).ConfigureAwait(false);
        }
    }

    private async Task SpeakSingleAsync(
        string text,
        string voice,
        double speed,
        int? pitch,
        int volume,
        CancellationToken ct)
    {
        try
        {
            var speechConfig = GetSpeechConfig();
            speechConfig.SpeechSynthesisVoiceName = voice;

            using var synth = new SpeechSynthesizer(speechConfig, null);

            var style = await ResolveEmotionStyleAsync(text, ct).ConfigureAwait(false);
            var ssml = BuildSsml(text, voice, speed, pitch, style);
            _log.Information(
                $"[FF14P2TTS-Azure] Speaking voice='{voice}' vol={volume}{(style is null ? string.Empty : $" style={style}")}: {text}");

            using var result = await synth.SpeakSsmlAsync(ssml).ConfigureAwait(false);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                _log.Information("[FF14P2TTS-Azure] Speech synthesis completed, playing audio...");
                await Task.Run(() => PlayAudioData(result.AudioData, volume), ct).ConfigureAwait(false);
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                _log.Error($"[FF14P2TTS-Azure] Synthesis canceled: {cancellation.Reason}");
                if (cancellation.Reason == CancellationReason.Error)
                {
                    _log.Error($"[FF14P2TTS-Azure] Error details: {cancellation.ErrorDetails}");
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Synthesizer was disposed between calls — ignore
        }
        catch (Exception ex)
        {
            _log.Error($"[FF14P2TTS-Azure] Synthesis exception: {ex.Message}");
        }
    }

    private async Task<string?> ResolveEmotionStyleAsync(string text, CancellationToken ct)
    {
        if (!_config.AzureUseEmotionTagging || string.IsNullOrWhiteSpace(_config.GroqApiKey))
            return null;

        var emotion = await TagAzureEmotionAsync(text, ct).ConfigureAwait(false);
        if (emotion is null)
        {
            _log.Debug("[FF14P2TTS-Azure] Groq returned no Azure emotion; using plain prosody.");
            return null;
        }

        _log.Debug($"[FF14P2TTS-Azure] Groq emotion tagging applied: {emotion}");
        return emotion;
    }

    /// <summary>
    /// Azure-specific Groq emotion classification. Kept fully separate from the
    /// Speechify tagger so the two providers never share tagger state or prompts.
    /// </summary>
    private async Task<string?> TagAzureEmotionAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.GroqApiKey))
        {
            _log.Warning("[FF14P2TTS-Azure] API key is not configured.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, GroqApiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.GroqApiKey);

            var payload = new
            {
                model = _config.GroqModel,
                temperature = 0.2,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You are an audio script director for a video game NPC dialogue reader. Classify the dominant emotion of the user's text. Reply with exactly one lowercase word from this list: cheerful, excited, sad, angry, terrified, fearful, whispering, shouting, hopeful, unfriendly, disgruntled, depressed, embarrassed. If no emotion clearly fits, reply with the word neutral. Return only the single word with no punctuation, no quotes, and no commentary.",
                    },
                    new { role = "user", content = text },
                },
            };
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await GroqHttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var error = $"Groq returned {(int)response.StatusCode} {response.StatusCode}: {Truncate(body, 300)}";
                _log.Warning($"[FF14P2TTS-Azure] {error}");
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                _log.Warning("[FF14P2TTS-Azure] Response did not contain any choices.");
                return null;
            }

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            var emotion = NormalizeAzureEmotion(content);
            if (emotion is null)
            {
                if (IsNeutralResponse(content))
                    _log.Debug("[FF14P2TTS-Azure] Groq returned neutral; using plain prosody.");
                else
                    _log.Warning($"[FF14P2TTS-Azure] No valid Azure emotion in response: {Truncate(content, 200)}");
            }
            return emotion;
        }
        catch (Exception ex)
        {
            _log.Warning($"[FF14P2TTS-Azure] Azure emotion tagging error: {ex.Message}");
            return null;
        }
    }

    private static string? NormalizeAzureEmotion(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var lower = content.Trim().ToLowerInvariant();
        foreach (var emotion in AzureEmotions)
        {
            if (string.Equals(lower, emotion, StringComparison.Ordinal))
                return emotion;
        }

        foreach (var emotion in AzureEmotions)
        {
            if (lower.Contains(emotion, StringComparison.Ordinal))
                return emotion;
        }

        return null;
    }

    private static bool IsNeutralResponse(string? content) =>
        !string.IsNullOrWhiteSpace(content)
        && string.Equals(content.Trim().ToLowerInvariant(), "neutral", StringComparison.Ordinal);

    private static string Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= maxLength
            ? value ?? string.Empty
            : value[..maxLength] + "...";

    /// <summary>
    /// Split text into sentence-sized chunks for synthesis.
    /// </summary>
    private static string[] SplitIntoSentences(string text)
    {
        // Split on sentence-ending punctuation followed by space
        var parts = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?])\s+");
        var result = new List<string>();
        var current = "";

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;

            if ((current + " " + part).Length > 400 && current.Length > 0)
            {
                result.Add(current.Trim());
                current = part;
            }
            else
            {
                current = string.IsNullOrEmpty(current) ? part : current + " " + part;
            }
        }

        if (!string.IsNullOrWhiteSpace(current))
            result.Add(current.Trim());

        return result.Count > 0 ? result.ToArray() : new[] { text };
    }

    /// <summary>
    /// Play raw WAV audio bytes with volume scaling applied directly to PCM samples.
    /// Bypasses SSML volume entirely — no parsing issues, no locale problems.
    /// </summary>
    private static void PlayAudioData(byte[] audioData, int volumePercent)
    {
        if (audioData == null || audioData.Length < 44)
            return;

        // Scale volume: config 0-200 → multiplier 0.0-2.0 (100 = 1.0 = unchanged)
        var factor = Math.Clamp(volumePercent / 100.0, 0.0, 2.0);

        // Apply volume scaling to PCM samples if not at default level
        var dataToPlay = factor != 1.0 ? AudioPlayer.ScaleWavVolume(audioData, factor) : audioData;

        var tempPath = Path.Combine(Path.GetTempPath(), $"ff14p2tts_azure_{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(tempPath, dataToPlay);
            using var player = new SoundPlayer(tempPath);
            player.PlaySync();
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
            }
            catch { /* best effort */ }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private static string BuildSsml(string text, string voiceName, double speed, int? pitch, string? style = null)
    {
        // Escape XML special chars
        var escapedText = System.Security.SecurityElement.Escape(text);

        var rate = speed switch
        {
            <= 0.5 => "x-slow",
            <= 0.75 => "slow",
            <= 1.25 => "medium",
            <= 1.75 => "fast",
            _ => "x-fast"
        };

        var pitchStr = pitch.HasValue ? $"{pitch.Value}Hz" : "medium";

        var prosody = $"<prosody rate=\"{rate}\" pitch=\"{pitchStr}\">{escapedText}</prosody>";

        var inner = string.IsNullOrWhiteSpace(style)
            ? prosody
            : $"<mstts:express-as style=\"{style}\" styledegree=\"1.5\">{prosody}</mstts:express-as>";

        return $@"<speak version=""1.0"" xmlns=""http://www.w3.org/2001/10/synthesis"" xmlns:mstts=""https://www.w3.org/2001/mstts"" xml:lang=""en-US"">
    <voice name=""{voiceName}"">
        {inner}
    </voice>
</speak>";
    }

    private static string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Strip SeString payload markers
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\uE0BB.*?\uE0BC",
            string.Empty
        );

        sanitized = sanitized.Replace('\uE040', '[').Replace('\uE041', ']');

        // Remove any remaining XML-like tags that might interfere with SSML
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"<[^>]+>", string.Empty);

        return sanitized.Trim();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _speechConfig = null;
        }
    }
}
