using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace FF14P2TTS.Infrastructure.Player2;

/// <summary>
/// HTTP client for the Player2 TTS API (player2.game by Elefant AI).
/// Local server on port 4315 provides:
///   POST /tts/speak  - Speak text (JSON body)
///   POST /tts/stop   - Stop current speech
///   GET  /tts/voices - List available voices
///   GET  /health     - Health check
/// </summary>
public class Player2TtsService : ITtsService
{
    private readonly HttpClient _httpClient;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly DuplicateMessageFilter _duplicateFilter = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Player2TtsService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.HttpTimeoutSeconds)
        };
    }

    /// <summary>
    /// Check if the Player2 server is reachable.
    /// </summary>
    public async Task<bool> IsServerAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{_config.Player2BaseUrl.TrimEnd('/')}/v1/health";
            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            if (_config.DebugLogging)
                _log.Debug($"[FF14P2TTS] Server check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get structured voice data from Player2 API.
    /// </summary>
    public async Task<List<VoiceInfo>> GetAvailableVoicesRawAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{_config.Player2BaseUrl.TrimEnd('/')}/v1/tts/voices";
            var response = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(response);
            var voices = new List<VoiceInfo>();

            if (doc.RootElement.TryGetProperty("voices", out var arr))
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                    var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var lang = el.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
                    var gender = el.TryGetProperty("gender", out var g) ? g.GetString() ?? "" : "";

                    voices.Add(new VoiceInfo
                    {
                        Id = id,
                        Name = name,
                        RawLanguage = lang,
                        Language = FormatLanguage(lang),
                        Gender = gender,
                        DisplayName = $"{name} ({FormatLanguage(lang)})"
                    });
                }
            }
            return voices;
        }
        catch (Exception ex)
        {
            if (_config.DebugLogging)
                _log.Debug($"[FF14P2TTS] Failed to get voices: {ex.Message}");
            return new List<VoiceInfo>();
        }
    }

    private static string FormatLanguage(string lang) => lang switch
    {
        "american_english" => "EN-US",
        "british_english" => "EN-UK",
        "japanese" => "JP",
        "mandarin_chinese" => "ZH",
        "spanish" => "ES",
        "french" => "FR",
        "hindi" => "HI",
        "italian" => "IT",
        "brazilian_portuguese" => "PT-BR",
        _ => lang.Replace("_", " ").ToUpper()
    };

    /// <summary>
    /// Stop any currently-playing TTS audio. Call before speaking new NPC dialog
    /// to interrupt the previous line instantly.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{_config.Player2BaseUrl.TrimEnd('/')}/v1/tts/stop";
            await _httpClient.PostAsync(url, null, ct).ConfigureAwait(false);
        }
        catch
        {
            // Silently ignore — stop is best-effort
        }
    }

    /// <summary>
    /// Speak a message through Player2 TTS using the configured settings.
    /// </summary>
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

        var selectedSpeed = speed ?? _config.Speed;

        try
        {
            var baseUrl = _config.Player2BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1/tts/speak";

            var effectiveVolume = volume ?? _config.Volume;
            var requestBody = new TtsSpeakRequest
            {
                Text = text,
                PlayInApp = true,
                Speed = selectedSpeed,
                Volume = effectiveVolume / 100.0, // Player2 expects 0.0-2.0 range, config is 0-200
            };

            if (!string.IsNullOrEmpty(voice) && voice != "default")
                requestBody.VoiceIds = new[] { voice };
            else if (!string.IsNullOrEmpty(_config.DefaultVoice) && _config.DefaultVoice != "default")
                requestBody.VoiceIds = new[] { _config.DefaultVoice };
            else
                requestBody.VoiceIds = new[] { "01955d76-ed5b-74de-83e5-800a44fee0d1" }; // Caleb (default)

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);

            _log.Information($"[FF14P2TTS] Sending: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);

            _log.Information($"[FF14P2TTS] Response: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.Error($"[FF14P2TTS] TTS failed ({response.StatusCode}): {body}");
            }
        }
        catch (TaskCanceledException)
        {
            _log.Error("[FF14P2TTS] TTS timed out — Player2 not reachable?");
        }
        catch (HttpRequestException ex)
        {
            _log.Error($"[FF14P2TTS] HTTP error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.Error($"[FF14P2TTS] Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Clean up text for TTS - remove FFXIV special payloads and sanitize.
    /// </summary>
    private static string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Strip SeString payload markers (FFXIV special formatting)
        // These look like [2E 02 ... 02 2F] or similar binary markers
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\uE0BB.*?\uE0BC", // Common payload markers
            string.Empty
        );

        // Replace FFXIV auto-translate markers
        sanitized = sanitized.Replace('\uE040', '[').Replace('\uE041', ']');

        return sanitized.Trim();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Request body for POST /tts/speak (matches Player2 SingleTextToSpeechRequest schema).
/// </summary>
internal class TtsSpeakRequest
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("play_in_app")]
    public bool PlayInApp { get; set; } = true;

    [JsonPropertyName("speed")]
    public double Speed { get; set; } = 1.0;

    [JsonPropertyName("voice_ids")]
    public string[]? VoiceIds { get; set; }

    [JsonPropertyName("volume")]
    public double? Volume { get; set; }
}
