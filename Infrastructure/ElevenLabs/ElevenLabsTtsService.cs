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

namespace FF14P2TTS.Infrastructure.ElevenLabs;

public sealed class ElevenLabsTtsService : ITtsService
{
    private static readonly HttpClient HttpClient = new();
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly SemaphoreSlim _synthesisQueue = new(1, 1);

    public ElevenLabsTtsService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
    }

    public async Task<bool> IsServerAvailableAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return !string.IsNullOrWhiteSpace(_config.ElevenLabsApiKey);
    }

    public async Task<List<VoiceInfo>> GetAvailableVoicesRawAsync(CancellationToken ct = default)
    {
        var voices = new List<VoiceInfo>();
        if (string.IsNullOrWhiteSpace(_config.ElevenLabsApiKey))
            return voices;

        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/v2/voices?page_size=100");
            using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.Warning($"[FF14P2TTS-ElevenLabs] Voice endpoint {request.RequestUri} returned {(int)response.StatusCode} {response.StatusCode}: {body}");
                return voices;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("voices", out var voiceArray))
                return voices;

            foreach (var voice in voiceArray.EnumerateArray())
            {
                var id = voice.TryGetProperty("voice_id", out var idElement) ? idElement.GetString() : null;
                var name = voice.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;

                var gender = "";
                if (voice.TryGetProperty("labels", out var labels)
                    && labels.TryGetProperty("gender", out var genderElement))
                    gender = genderElement.GetString() ?? "";

                voices.Add(new VoiceInfo
                {
                    Id = id,
                    Name = name,
                    RawLanguage = "elevenlabs",
                    Language = "multilingual",
                    Gender = gender,
                    DisplayName = name,
                });
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS-ElevenLabs] Voice request failed: {ex.Message}");
        }

        return voices;
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
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(_config.ElevenLabsApiKey))
            return;

        var volumeFactor = Math.Clamp((volume ?? _config.Volume) / 100.0, 0.0, 2.0);

        var voiceId = ResolveVoiceId(voice);
        if (voiceId is null)
            return;

        var pcmAudio = await SynthesizePcmAsync(text, voiceId, ct).ConfigureAwait(false);
        if (pcmAudio is not null)
            await Task.Run(
                () => AudioPlayer.PlayAudio(pcmAudio, "elevenlabs", 22050, 1, 16, volumeFactor),
                ct).ConfigureAwait(false);
    }

    private string? ResolveVoiceId(string? voice)
    {
        var voiceId = voice ?? _config.ElevenLabsDefaultVoice;
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            _log.Warning("[FF14P2TTS-ElevenLabs] No voice ID configured. Enter a real ElevenLabs voice ID from the ElevenLabs voice library.");
            return null;
        }

        if (!IsValidElevenLabsVoiceId(voiceId))
        {
            _log.Warning($"[FF14P2TTS-ElevenLabs] '{voiceId}' is not a valid ElevenLabs voice ID. Clear the ElevenLabs default voice and grant voices_read or enter an owned 20-character ElevenLabs voice ID.");
            if (string.Equals(_config.ElevenLabsDefaultVoice, voiceId, StringComparison.Ordinal))
            {
                _config.ElevenLabsDefaultVoice = string.Empty;
                _config.Save();
            }
            return null;
        }

        return voiceId;
    }

    private async Task<byte[]?> SynthesizePcmAsync(string text, string voiceId, CancellationToken ct)
    {
        var queueSlotAcquired = false;
        try
        {
            await _synthesisQueue.WaitAsync(ct).ConfigureAwait(false);
            queueSlotAcquired = true;
            var endpoint = $"/v1/text-to-speech/{Uri.EscapeDataString(voiceId)}?output_format=pcm_22050";
            using var request = CreateRequest(HttpMethod.Post, endpoint);
            var payload = new
            {
                text,
                model_id = "eleven_multilingual_v2",
                voice_settings = new { stability = 0.5, similarity_boost = 0.75 },
            };
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if ((int)response.StatusCode == 402
                    && body.Contains("library voices", StringComparison.OrdinalIgnoreCase))
                {
                    _log.Error("[FF14P2TTS-ElevenLabs] This voice is an ElevenLabs library voice and requires a paid plan. Use a voice owned by your account or upgrade the ElevenLabs plan.");
                }
                else
                {
                    _log.Error($"[FF14P2TTS-ElevenLabs] Synthesis endpoint {request.RequestUri} returned {(int)response.StatusCode} {response.StatusCode}: {body}");
                }
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error($"[FF14P2TTS-ElevenLabs] Synthesis error: {ex.Message}");
            return null;
        }
        finally
        {
            if (queueSlotAcquired)
                _synthesisQueue.Release();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var baseUrl = _config.ElevenLabsBaseUrl.Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            || baseUrl.EndsWith("/v2", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..baseUrl.LastIndexOf('/')];

        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Headers.Add("xi-api-key", _config.ElevenLabsApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static bool IsValidElevenLabsVoiceId(string voiceId) =>
        voiceId.Length == 20 && voiceId.All(char.IsLetterOrDigit);

    public void Dispose()
    {
        _synthesisQueue.Dispose();
    }
}
