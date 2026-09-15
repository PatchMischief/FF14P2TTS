using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace FF14P2TTS.Infrastructure.Groq;

/// <summary>Tags dialogue with Speechify emotion SSML using the Groq chat API.</summary>
public sealed class GroqEmotionTagger
{
    private const string ApiUrl = "https://api.groq.com/openai/v1/chat/completions";
    private static readonly HttpClient HttpClient = new();
    private readonly Configuration _config;
    private readonly IPluginLog _log;

    public GroqEmotionTagger(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
    }

    public async Task<string?> TagAsync(string text, CancellationToken ct = default)
    {
        var result = await TagWithErrorAsync(text, ct).ConfigureAwait(false);
        return result.Tagged;
    }

    public async Task<(string? Tagged, string? Error)> TagWithErrorAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.GroqApiKey))
        {
            _log.Warning("[FF14P2TTS-Groq] API key is not configured.");
            return (null, "Groq API key is not configured.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
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
                        content = "You are an audio script director for a video game NPC dialogue reader. Wrap each sentence of the user's text in <speechify:style emotion=\"EMOTION\">sentence</speechify:style>. Choose the best emotion from this exact list: angry, cheerful, sad, terrified, relaxed, fearful, surprised, calm, assertive, energetic, warm, direct, bright. Do not change, add, or remove any words. Do not translate. Return only the tagged text with no <speak> wrapper, no code fences, and no extra commentary.",
                    },
                    new { role = "user", content = text },
                },
            };
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var error = $"Groq returned {(int)response.StatusCode} {response.StatusCode}: {Truncate(body, 300)}";
                _log.Warning($"[FF14P2TTS-Groq] {error}");
                return (null, error);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                _log.Warning("[FF14P2TTS-Groq] Response did not contain any choices.");
                return (null, "Groq response did not contain any choices.");
            }

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            var tagged = SanitizeTagged(content);
            if (tagged is null)
            {
                _log.Warning($"[FF14P2TTS-Groq] Response had no emotion tags: {Truncate(content, 200)}");
                return (null, "Groq response had no emotion tags.");
            }

            return (tagged, null);
        }
        catch (Exception ex)
        {
            _log.Warning($"[FF14P2TTS-Groq] Tagging error: {ex.Message}");
            return (null, ex.Message);
        }
    }

    private static string Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= maxLength
            ? value ?? string.Empty
            : value[..maxLength] + "...";

    private static string? SanitizeTagged(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var tagged = content.Trim();
        tagged = Regex.Replace(tagged, @"^```(?:xml|ssml)?\s*", string.Empty, RegexOptions.IgnoreCase);
        tagged = Regex.Replace(tagged, @"\s*```$", string.Empty);

        var openIndex = tagged.IndexOf("<speak", StringComparison.OrdinalIgnoreCase);
        if (openIndex >= 0)
        {
            var tagEnd = tagged.IndexOf('>', openIndex);
            if (tagEnd >= 0)
                tagged = tagged[(tagEnd + 1)..];
        }

        if (tagged.EndsWith("</speak>", StringComparison.OrdinalIgnoreCase))
            tagged = tagged[..^"</speak>".Length];

        return tagged.Contains("<speechify:style", StringComparison.OrdinalIgnoreCase)
            ? tagged.Trim()
            : null;
    }
}
