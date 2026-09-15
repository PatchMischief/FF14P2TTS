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

    // Well-known Azure neural voices (English)
    public static readonly List<VoiceInfo> KnownEnglishVoices = new()
    {
        new()
        {
            Id = "en-US-AriaNeural",
            Name = "Aria",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Aria (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-JennyNeural",
            Name = "Jenny",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Jenny (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-JaneNeural",
            Name = "Jane",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Jane (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-NancyNeural",
            Name = "Nancy",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Nancy (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-AmberNeural",
            Name = "Amber",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Amber (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-AshleyNeural",
            Name = "Ashley",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Ashley (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-SaraNeural",
            Name = "Sara",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Sara (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-AnaNeural",
            Name = "Ana",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Ana (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-MichelleNeural",
            Name = "Michelle",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "female",
            DisplayName = "Michelle (EN-US) [F]",
        },
        new()
        {
            Id = "en-US-DavisNeural",
            Name = "Davis",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Davis (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-GuyNeural",
            Name = "Guy",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Guy (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-TonyNeural",
            Name = "Tony",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Tony (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-JasonNeural",
            Name = "Jason",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Jason (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-JacobNeural",
            Name = "Jacob",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Jacob (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-EricNeural",
            Name = "Eric",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Eric (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-SteffanNeural",
            Name = "Steffan",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Steffan (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-RogerNeural",
            Name = "Roger",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Roger (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-AndrewNeural",
            Name = "Andrew",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Andrew (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-BrianNeural",
            Name = "Brian",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Brian (EN-US) [M]",
        },
        new()
        {
            Id = "en-US-ChristopherNeural",
            Name = "Christopher",
            RawLanguage = "american_english",
            Language = "EN-US",
            Gender = "male",
            DisplayName = "Christopher (EN-US) [M]",
        },
        // UK English voices
        new()
        {
            Id = "en-GB-SoniaNeural",
            Name = "Sonia",
            RawLanguage = "british_english",
            Language = "EN-UK",
            Gender = "female",
            DisplayName = "Sonia (EN-UK) [F]",
        },
        new()
        {
            Id = "en-GB-MaisieNeural",
            Name = "Maisie",
            RawLanguage = "british_english",
            Language = "EN-UK",
            Gender = "female",
            DisplayName = "Maisie (EN-UK) [F]",
        },
        new()
        {
            Id = "en-GB-LibbyNeural",
            Name = "Libby",
            RawLanguage = "british_english",
            Language = "EN-UK",
            Gender = "female",
            DisplayName = "Libby (EN-UK) [F]",
        },
        new()
        {
            Id = "en-GB-RyanNeural",
            Name = "Ryan",
            RawLanguage = "british_english",
            Language = "EN-UK",
            Gender = "male",
            DisplayName = "Ryan (EN-UK) [M]",
        },
        new()
        {
            Id = "en-GB-ThomasNeural",
            Name = "Thomas",
            RawLanguage = "british_english",
            Language = "EN-UK",
            Gender = "male",
            DisplayName = "Thomas (EN-UK) [M]",
        },
        new()
        {
            Id = "en-GB-EthanNeural",
            Name = "Ethan",
            RawLanguage = "british_english",
            Language = "EN-UK",
            Gender = "male",
            DisplayName = "Ethan (EN-UK) [M]",
        },
        new()
        {
            Id = "en-GB-OliverNeural",
            Name = "Oliver",
            RawLanguage = "british_english",
            Language = "EN-UK",
            Gender = "male",
            DisplayName = "Oliver (EN-UK) [M]",
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

    public Task<List<VoiceInfo>> GetAvailableVoicesRawAsync(CancellationToken ct = default)
    {
        // Return known voices + allow custom entry
        var voices = new List<VoiceInfo>(KnownEnglishVoices);

        // If the user's configured voices aren't in the known list, add them
        AddCustomVoiceIfMissing(voices, _config.AzureDefaultVoice);
        AddCustomVoiceIfMissing(voices, _config.AzureUnisexVoice);
        AddCustomVoiceIfMissing(voices, _config.AzureMaleVoice);
        AddCustomVoiceIfMissing(voices, _config.AzureFemaleVoice);

        return Task.FromResult(voices);
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
