using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FF14P2TTS.Application;
using FF14P2TTS.Infrastructure.Audio;
using FF14P2TTS.Infrastructure.Groq;

namespace FF14P2TTS.Infrastructure.Speechify;

public sealed class SpeechifyTtsService : ITtsService, ISsmlSpeechProvider
{
    private static readonly HttpClient HttpClient = new();
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly SemaphoreSlim _synthesisQueue = new(1, 1);
    private readonly GroqEmotionTagger _groqTagger;

    public SpeechifyTtsService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
        _groqTagger = new GroqEmotionTagger(config, log);
    }

    public Task<bool> IsServerAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(_config.SpeechifyApiKey));

    public async Task<List<VoiceInfo>> GetAvailableVoicesRawAsync(CancellationToken ct = default)
    {
        var voices = new List<VoiceInfo>();
        if (string.IsNullOrWhiteSpace(_config.SpeechifyApiKey))
            return voices;

        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/v1/voices");
            using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning($"[FF14P2TTS-Speechify] Voice request returned {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)}");
                return voices;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("voices", out var voiceArray))
                return voices;

            foreach (var voice in voiceArray.EnumerateArray())
            {
                var id = voice.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                var name = voice.TryGetProperty("display_name", out var nameElement) ? nameElement.GetString() : id;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var gender = voice.TryGetProperty("gender", out var genderElement)
                    ? genderElement.GetString() ?? ""
                    : "";
                var locale = voice.TryGetProperty("locale", out var localeElement)
                    ? localeElement.GetString() ?? ""
                    : "";
                voices.Add(new VoiceInfo
                {
                    Id = id,
                    Name = name ?? id,
                    RawLanguage = "speechify",
                    Language = locale,
                    Gender = gender,
                    DisplayName = name ?? id,
                });
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS-Speechify] Voice request failed: {ex.Message}");
        }

        var englishVoices = voices
            .Where(voice => voice.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return englishVoices.Count > 0 ? englishVoices : voices;
    }

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task SpeakAsync(
        string text,
        string? voice = null,
        double? speed = null,
        int? pitch = null,
        int? volume = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(_config.SpeechifyApiKey))
            return;

        var volumeFactor = Math.Clamp((volume ?? _config.Volume) / 100.0, 0.0, 2.0);

        var effectiveSpeed = speed ?? _config.Speed;
        var voiceId = await ResolveVoiceIdAsync(voice, ct).ConfigureAwait(false);
        if (voiceId is null)
        {
            _log.Warning("[FF14P2TTS-Speechify] No voice ID configured or discoverable.");
            return;
        }

        var audio = await SynthesizeAudioAsync(text, voiceId, effectiveSpeed, ct).ConfigureAwait(false);
        if (audio is not null)
        {
            await Task.Run(
                () => AudioPlayer.PlayAudio(audio, "speechify", 24000, 1, 16, volumeFactor),
                ct).ConfigureAwait(false);
            return;
        }

        var voices = await GetAvailableVoicesRawAsync(ct).ConfigureAwait(false);
        var fallbackId = voices
            .Select(candidate => candidate.Id)
            .FirstOrDefault(candidate => !string.Equals(candidate, voiceId, StringComparison.OrdinalIgnoreCase));
        if (fallbackId is null)
        {
            _log.Warning("[FF14P2TTS-Speechify] Synthesis failed and no alternative voice is available.");
            return;
        }

        _config.SpeechifyDefaultVoice = fallbackId;
        _config.Save();
        var fallbackAudio = await SynthesizeAudioAsync(text, fallbackId, effectiveSpeed, ct).ConfigureAwait(false);
        if (fallbackAudio is not null)
            await Task.Run(
                () => AudioPlayer.PlayAudio(fallbackAudio, "speechify", 24000, 1, 16, volumeFactor),
                ct).ConfigureAwait(false);
    }

    private async Task<string?> ResolveVoiceIdAsync(string? voice, CancellationToken ct)
    {
        var voiceId = voice ?? _config.SpeechifyDefaultVoice;
        if (!string.IsNullOrWhiteSpace(voiceId) && LooksLikePlayer2Uuid(voiceId))
        {
            _log.Warning($"[FF14P2TTS-Speechify] '{voiceId}' is a Player2 UUID, not a Speechify voice. Selecting a Speechify voice automatically.");
            if (string.Equals(_config.SpeechifyDefaultVoice, voiceId, StringComparison.Ordinal))
            {
                _config.SpeechifyDefaultVoice = string.Empty;
                _config.Save();
            }
            voiceId = null;
        }

        if (string.IsNullOrWhiteSpace(voiceId))
            voiceId = (await GetAvailableVoicesRawAsync(ct).ConfigureAwait(false)).FirstOrDefault()?.Id;

        return voiceId;
    }

    private async Task<byte[]?> SynthesizeAudioAsync(string text, string voiceId, double speed, CancellationToken ct)
    {
        var input = await BuildInputAsync(text, speed, ct).ConfigureAwait(false);
        return await SynthesizeAudioWithInputAsync(input, voiceId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Synthesizes audio for a line of text without playing it. Used by the
    /// dialogue preloader to prepare the next line while the current one plays.
    /// </summary>
    public async Task<byte[]?> SynthesizeAsync(
        string text,
        string? voice = null,
        double? speed = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(_config.SpeechifyApiKey))
            return null;

        var effectiveSpeed = speed ?? _config.Speed;
        var voiceId = await ResolveVoiceIdAsync(voice, ct).ConfigureAwait(false);
        if (voiceId is null)
        {
            _log.Warning("[FF14P2TTS-Speechify] No voice ID configured or discoverable.");
            return null;
        }

        return await SynthesizeAudioAsync(text, voiceId, effectiveSpeed, ct).ConfigureAwait(false);
    }

    public async Task SpeakSsmlAsync(string ssml, string? voice = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ssml) || string.IsNullOrWhiteSpace(_config.SpeechifyApiKey))
            return;

        var voiceId = await ResolveVoiceIdAsync(voice, ct).ConfigureAwait(false);
        if (voiceId is null)
            return;

        _log.Information($"[FF14P2TTS-Speechify] Direct SSML with voice '{voiceId}'");
        var volumeFactor = Math.Clamp(_config.Volume / 100.0, 0.0, 2.0);
        var audio = await SynthesizeAudioWithInputAsync(ssml, voiceId, ct).ConfigureAwait(false);
        if (audio is not null)
            await Task.Run(
                () => AudioPlayer.PlayAudio(audio, "speechify", 24000, 1, 16, volumeFactor),
                ct).ConfigureAwait(false);
    }

    private async Task<byte[]?> SynthesizeAudioWithInputAsync(string input, string voiceId, CancellationToken ct)
    {
        var acquired = false;
        try
        {
            await _synthesisQueue.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            using var request = CreateRequest(HttpMethod.Post, "/v1/audio/speech");
            var payload = new
            {
                input,
                voice_id = voiceId,
                output_format = "mp3_24000_128",
                model = "simba-3.2",
            };
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Error($"[FF14P2TTS-Speechify] Synthesis with '{voiceId}' returned {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)}");
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("audio_data", out var audioElement))
                return null;

            var encodedAudio = audioElement.GetString() ?? string.Empty;
            var commaIndex = encodedAudio.IndexOf(',');
            if (encodedAudio.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
                encodedAudio = encodedAudio[(commaIndex + 1)..];

            return Convert.FromBase64String(encodedAudio);
        }
        catch (Exception ex)
        {
            _log.Error($"[FF14P2TTS-Speechify] Synthesis error: {ex.Message}");
            return null;
        }
        finally
        {
            if (acquired)
                _synthesisQueue.Release();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{_config.SpeechifyBaseUrl.Trim().TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.SpeechifyApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static bool LooksLikePlayer2Uuid(string voiceId) => Guid.TryParse(voiceId, out _);

    private async Task<string> BuildInputAsync(string text, double speed, CancellationToken ct)
    {
        var content = text;
        if (_config.SpeechifyUseEmotionTagging && !string.IsNullOrWhiteSpace(_config.GroqApiKey))
        {
            var tagged = await _groqTagger.TagAsync(text, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(tagged))
            {
                content = tagged;
                _log.Debug("[FF14P2TTS-Speechify] Groq emotion tagging applied.");
            }
            else
            {
                _log.Debug("[FF14P2TTS-Speechify] Groq returned no tags; using plain text.");
            }
        }

        return BuildSsml(content, speed);
    }

    private static string BuildSsml(string content, double speed)
    {
        var isTagged = content.Contains("<speechify:style", StringComparison.OrdinalIgnoreCase);
        var needsSpeed = Math.Abs(speed - 1.0) >= 0.01;

        if (!isTagged && !needsSpeed)
            return content;

        if (isTagged)
        {
            var inner = content;
            if (needsSpeed)
            {
                var percent = (int)Math.Round((speed - 1.0) * 100);
                var rate = percent >= 0 ? $"+{percent}%" : $"{percent}%";
                inner = $"<prosody rate=\"{rate}\">{content}</prosody>";
            }
            return $"<speak>{inner}</speak>";
        }

        var escaped = System.Security.SecurityElement.Escape(content);
        var plainPercent = (int)Math.Round((speed - 1.0) * 100);
        var plainRate = plainPercent >= 0 ? $"+{plainPercent}%" : $"{plainPercent}%";
        return $"<speak><prosody rate=\"{plainRate}\">{escaped}</prosody></speak>";
    }

    public void Dispose() => _synthesisQueue.Dispose();
}