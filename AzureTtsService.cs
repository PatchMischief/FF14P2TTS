using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Microsoft.CognitiveServices.Speech;

namespace FF14P2TTS;

/// <summary>
/// TTS service backed by Microsoft Azure Cognitive Services Speech API.
/// Uses the Microsoft.CognitiveServices.Speech SDK for synthesis.
/// </summary>
public class AzureTtsService : ITtsService
{
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private SpeechConfig? _speechConfig;
    private string _lastMessage = string.Empty;
    private DateTime _lastMessageTime = DateTime.MinValue;
    private readonly object _lock = new();

    // Well-known Azure neural voices (English)
    public static readonly List<VoiceInfo> KnownEnglishVoices = new()
    {
        new() { Id = "en-US-AriaNeural",    Name = "Aria",    RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Aria (EN-US) [F]" },
        new() { Id = "en-US-JennyNeural",   Name = "Jenny",   RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Jenny (EN-US) [F]" },
        new() { Id = "en-US-JaneNeural",    Name = "Jane",    RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Jane (EN-US) [F]" },
        new() { Id = "en-US-NancyNeural",   Name = "Nancy",   RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Nancy (EN-US) [F]" },
        new() { Id = "en-US-AmberNeural",   Name = "Amber",   RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Amber (EN-US) [F]" },
        new() { Id = "en-US-AshleyNeural",  Name = "Ashley",  RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Ashley (EN-US) [F]" },
        new() { Id = "en-US-SaraNeural",    Name = "Sara",    RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Sara (EN-US) [F]" },
        new() { Id = "en-US-AnaNeural",     Name = "Ana",     RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Ana (EN-US) [F]" },
        new() { Id = "en-US-MichelleNeural",Name = "Michelle",RawLanguage = "american_english", Language = "EN-US", Gender = "female", DisplayName = "Michelle (EN-US) [F]" },
        new() { Id = "en-US-DavisNeural",   Name = "Davis",   RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Davis (EN-US) [M]" },
        new() { Id = "en-US-GuyNeural",     Name = "Guy",     RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Guy (EN-US) [M]" },
        new() { Id = "en-US-TonyNeural",    Name = "Tony",    RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Tony (EN-US) [M]" },
        new() { Id = "en-US-JasonNeural",   Name = "Jason",   RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Jason (EN-US) [M]" },
        new() { Id = "en-US-JacobNeural",   Name = "Jacob",   RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Jacob (EN-US) [M]" },
        new() { Id = "en-US-EricNeural",    Name = "Eric",    RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Eric (EN-US) [M]" },
        new() { Id = "en-US-SteffanNeural", Name = "Steffan", RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Steffan (EN-US) [M]" },
        new() { Id = "en-US-RogerNeural",   Name = "Roger",   RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Roger (EN-US) [M]" },
        new() { Id = "en-US-AndrewNeural",  Name = "Andrew",  RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Andrew (EN-US) [M]" },
        new() { Id = "en-US-BrianNeural",   Name = "Brian",   RawLanguage = "american_english", Language = "EN-US", Gender = "male",   DisplayName = "Brian (EN-US) [M]" },
        new() { Id = "en-US-ChristopherNeural", Name = "Christopher", RawLanguage = "american_english", Language = "EN-US", Gender = "male", DisplayName = "Christopher (EN-US) [M]" },
        // UK English voices
        new() { Id = "en-GB-SoniaNeural",   Name = "Sonia",   RawLanguage = "british_english", Language = "EN-UK", Gender = "female", DisplayName = "Sonia (EN-UK) [F]" },
        new() { Id = "en-GB-MaisieNeural",  Name = "Maisie",  RawLanguage = "british_english", Language = "EN-UK", Gender = "female", DisplayName = "Maisie (EN-UK) [F]" },
        new() { Id = "en-GB-LibbyNeural",   Name = "Libby",   RawLanguage = "british_english", Language = "EN-UK", Gender = "female", DisplayName = "Libby (EN-UK) [F]" },
        new() { Id = "en-GB-RyanNeural",    Name = "Ryan",    RawLanguage = "british_english", Language = "EN-UK", Gender = "male",   DisplayName = "Ryan (EN-UK) [M]" },
        new() { Id = "en-GB-ThomasNeural",  Name = "Thomas",  RawLanguage = "british_english", Language = "EN-UK", Gender = "male",   DisplayName = "Thomas (EN-UK) [M]" },
        new() { Id = "en-GB-EthanNeural",   Name = "Ethan",   RawLanguage = "british_english", Language = "EN-UK", Gender = "male",   DisplayName = "Ethan (EN-UK) [M]" },
        new() { Id = "en-GB-OliverNeural",  Name = "Oliver",  RawLanguage = "british_english", Language = "EN-UK", Gender = "male",   DisplayName = "Oliver (EN-UK) [M]" },
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
            if (_speechConfig != null)
                return _speechConfig;

            if (!string.IsNullOrWhiteSpace(_config.AzureRegion))
            {
                _speechConfig = SpeechConfig.FromSubscription(_config.AzureSubscriptionKey, _config.AzureRegion);
            }
            else if (!string.IsNullOrWhiteSpace(_config.AzureEndpoint))
            {
                _log.Warning("[FF14P2TTS-Azure] No region configured, falling back to custom endpoint.");
                _speechConfig = SpeechConfig.FromEndpoint(new Uri(_config.AzureEndpoint.TrimEnd('/')), _config.AzureSubscriptionKey);
            }
            else
            {
                _log.Error("[FF14P2TTS-Azure] Neither Azure Region nor Endpoint configured.");
                _speechConfig = SpeechConfig.FromSubscription(_config.AzureSubscriptionKey, "eastus");
            }

            // Increase timeouts for long NPC dialogue lines
            _speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "15000");
            _speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "10000");

            return _speechConfig;
        }
    }

    public async Task<bool> IsServerAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.AzureSubscriptionKey))
            return false;
        return true; // Azure is cloud-based; we can't easily ping without making a call
    }

    public Task<string[]> GetAvailableVoicesAsync(CancellationToken ct = default)
    {
        var voices = GetAvailableVoicesRawAsync(ct).Result;
        return Task.FromResult(voices.Select(v => v.DisplayName).ToArray());
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

    public async Task SpeakAsync(string text, string? voice = null, double? speed = null, int? pitch = null, int? volume = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        text = SanitizeText(text);

        // Skip duplicate messages within 2 seconds
        if (_config.SkipDuplicateMessages && text == _lastMessage && (DateTime.UtcNow - _lastMessageTime).TotalSeconds < 2)
            return;

        _lastMessage = text;
        _lastMessageTime = DateTime.UtcNow;

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
                await SpeakSingleAsync(sentence, effectiveVoice, effectiveSpeed, pitch, effectiveVolume, ct).ConfigureAwait(false);
            }
        }
        else
        {
            await SpeakSingleAsync(text, effectiveVoice, effectiveSpeed, pitch, effectiveVolume, ct).ConfigureAwait(false);
        }
    }

    private async Task SpeakSingleAsync(string text, string voice, double speed, int? pitch, int volume, CancellationToken ct)
    {
        try
        {
            var speechConfig = GetSpeechConfig();
            speechConfig.SpeechSynthesisVoiceName = voice;

            using var synth = new SpeechSynthesizer(speechConfig, null);

            var ssml = BuildSsml(text, voice, speed, pitch);
            _log.Information($"[FF14P2TTS-Azure] Speaking voice='{voice}' vol={volume}: {text}");

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
        var dataToPlay = factor != 1.0 ? ScaleWavVolume(audioData, factor) : audioData;

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

    /// <summary>
    /// Scale 16-bit PCM samples in a WAV buffer by a multiplier.
    /// Parses the WAV header to find the data chunk, then scales each int16 sample.
    /// </summary>
    private static byte[] ScaleWavVolume(byte[] wav, double factor)
    {
        // WAV header is 44 bytes (standard PCM), data starts at byte 44
        // Bytes 40-43: "data" chunk size (little-endian uint32)
        var result = new byte[wav.Length];
        Buffer.BlockCopy(wav, 0, result, 0, wav.Length);

        var dataOffset = FindWavDataOffset(wav);
        if (dataOffset < 44) return result; // couldn't find data chunk

        // Scale 16-bit samples
        for (var i = dataOffset; i < result.Length - 1; i += 2)
        {
            var sample = (short)(result[i] | (result[i + 1] << 8));
            var scaled = (int)(sample * factor);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            result[i] = (byte)(scaled & 0xFF);
            result[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        return result;
    }

    private static int FindWavDataOffset(byte[] wav)
    {
        // Search for "data" chunk marker in WAV file
        for (var i = 0; i < wav.Length - 8; i++)
        {
            if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
                return i + 8; // skip "data" + 4-byte size
        }
        return 44; // fallback: standard PCM header size
    }

    private static string BuildSsml(string text, string voiceName, double speed, int? pitch)
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

        return $@"<speak version=""1.0"" xmlns=""http://www.w3.org/2001/10/synthesis"" xml:lang=""en-US"">
    <voice name=""{voiceName}"">
        <prosody rate=""{rate}"" pitch=""{pitchStr}"">
            {escapedText}
        </prosody>
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
