using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FF14P2TTS.Application;

namespace FF14P2TTS.Infrastructure;

public sealed class ConsoleGamesWikiVoicedCutsceneDetector : IVoicedCutsceneDetector
{
    private const string ApiBaseUrl = "https://ffxiv.consolegameswiki.com/mediawiki/api.php";
    private static readonly HttpClient HttpClient = new();
    private readonly IPluginLog _log;
    private readonly Func<IReadOnlyList<string>> _activeQuestNames;
    private readonly Func<bool>? _questReadAvailable;
    private readonly ConcurrentDictionary<string, bool> _results = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<VoicedCutsceneClassification>> _lookups = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _wikiPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();
    private string _activeQuestSignature = string.Empty;

    public ConsoleGamesWikiVoicedCutsceneDetector(
        IPluginLog log,
        Func<IReadOnlyList<string>> activeQuestNames,
        Func<bool>? questReadAvailable = null)
    {
        _log = log;
        _activeQuestNames = activeQuestNames;
        _questReadAvailable = questReadAvailable;
    }

    public Task<VoicedCutsceneClassification> ClassifyAsync(string speaker, string text)
    {
        var questNames = GetActiveQuestNames();
        RefreshCacheForActiveQuests(questNames);

        var key = GetKey(speaker, text);
        if (_results.TryGetValue(key, out var result))
            return Task.FromResult(result
                ? VoicedCutsceneClassification.Voiced
                : VoicedCutsceneClassification.NotVoiced);

        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(VoicedCutsceneClassification.NotVoiced);

        if (!HasResolvableQuest(questNames))
        {
            // Data gap: no resolvable active quest names (game window inactive,
            // QuestManager unavailable, or rows that don't resolve). The caller
            // falls back to the native subtitle signal for this classification.
            if (!(_questReadAvailable?.Invoke() ?? true))
                _log.Debug("[FF14P2TTS] Voiced-cutscene classification skipped: quest read unavailable; failing open.");
            return Task.FromResult(VoicedCutsceneClassification.Unknown);
        }

        // Score against already-cached pages; only fetch when none gives a
        // confident answer.
        var cached = ScoreAgainstPages(_wikiPages.Values, text);
        if (cached != VoicedCutsceneClassification.Unknown)
            return Task.FromResult(cached);

        return _lookups.GetOrAdd(key, _ => LookupActiveQuestPagesAsync(speaker, text, key, questNames));
    }

    private static bool HasResolvableQuest(IReadOnlyList<string> questNames) =>
        questNames.Any(name => !name.StartsWith("Unknown quest slot", StringComparison.OrdinalIgnoreCase));

    public async Task<LinkedList<WikiDialogueBox>> GetUnvoicedDialogueBoxesAsync(string title)
    {
        var result = new LinkedList<WikiDialogueBox>();
        if (string.IsNullOrWhiteSpace(title))
            return result;

        try
        {
            var wikitext = await GetOrFetchPageAsync(title).ConfigureAwait(false);
            if (wikitext is null)
                return result;

            foreach (var box in ExtractUnvoicedDialogueBoxes(wikitext))
                result.AddLast(box);
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS] Wiki dialogue extraction failed for '{title}': {ex.Message}");
        }

        return result;
    }

    public void ClearQuestCache(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        _wikiPages.TryRemove(title, out _);
    }

    private async Task<VoicedCutsceneClassification> LookupActiveQuestPagesAsync(
        string speaker,
        string text,
        string key,
        IReadOnlyList<string> questNames)
    {
        var classification = VoicedCutsceneClassification.Unknown;
        try
        {
            foreach (var title in questNames)
            {
                var wikitext = await GetOrFetchPageAsync(title).ConfigureAwait(false);
                if (wikitext is null)
                    continue;

                classification = ScoreAgainstPages(_wikiPages.Values, text);
                if (classification != VoicedCutsceneClassification.Unknown)
                {
                    _log.Debug(
                        classification == VoicedCutsceneClassification.Voiced
                            ? $"[FF14P2TTS] Wiki classified '{speaker}' as voiced."
                            : $"[FF14P2TTS] Wiki classified '{speaker}' as unvoiced.");
                    break;
                }
            }

            if (classification == VoicedCutsceneClassification.Unknown)
                _log.Debug($"[FF14P2TTS] Wiki could not match '{speaker}' to any scripted line.");
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS] Wiki voiced-cutscene lookup failed: {ex.Message}");
        }
        finally
        {
            if (classification != VoicedCutsceneClassification.Unknown)
                _results[key] = classification == VoicedCutsceneClassification.Voiced;
            _lookups.TryRemove(key, out _);
        }

        return classification;
    }

    private IReadOnlyList<string> GetActiveQuestNames() => _activeQuestNames()
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private async Task<string?> GetOrFetchPageAsync(string title)
    {
        if (_wikiPages.TryGetValue(title, out var cached))
            return cached;

        var pageUrl =
            $"{ApiBaseUrl}?action=query&prop=revisions&titles={Uri.EscapeDataString(title)}&rvprop=content&rvslots=main&format=json";
        using var pageResponse = await HttpClient.GetAsync(pageUrl).ConfigureAwait(false);
        if (!pageResponse.IsSuccessStatusCode)
            return null;

        using var pageDocument = JsonDocument.Parse(
            await pageResponse.Content.ReadAsStreamAsync().ConfigureAwait(false));
        var wikitext = ExtractWikitext(pageDocument.RootElement);
        if (wikitext is not null)
            _wikiPages[title] = wikitext;
        return wikitext;
    }

    private static IEnumerable<WikiDialogueBox> ExtractUnvoicedDialogueBoxes(string wikitext)
    {
        var cutsceneStart = 0;
        while (true)
        {
            cutsceneStart = wikitext.IndexOf("-cutscene start", cutsceneStart, StringComparison.OrdinalIgnoreCase);
            if (cutsceneStart < 0)
                yield break;

            var blockEnd = wikitext.IndexOf("-cutscene end", cutsceneStart, StringComparison.OrdinalIgnoreCase);
            if (blockEnd < 0)
                blockEnd = wikitext.Length;

            var block = wikitext.Substring(cutsceneStart, blockEnd - cutsceneStart);
            var lines = new List<WikiDialogueLine>();
            foreach (var line in block.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                // Skip control markers, headings, and template/markup lines.
                if ("-={|}*[".Contains(trimmed[0]))
                    continue;

                var colonIndex = trimmed.IndexOf(": ", StringComparison.Ordinal);
                if (colonIndex <= 0)
                    continue;

                var speaker = CleanInlineMarkup(trimmed[..colonIndex].Trim());
                var text = CleanInlineMarkup(trimmed[(colonIndex + 2)..].Trim());
                if (string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(text))
                    continue;

                if (speaker.StartsWith("x@", StringComparison.OrdinalIgnoreCase))
                    speaker = speaker[2..].Trim();

                lines.Add(new WikiDialogueLine(speaker, text));
            }

            if (lines.Count > 0)
                yield return new WikiDialogueBox(lines);

            cutsceneStart += "-cutscene start".Length;
        }
    }

    private void RefreshCacheForActiveQuests(IReadOnlyList<string> questNames)
    {
        // A transient read (game window inactive, or the quest sheet briefly
        // unavailable) yields an empty list or only placeholder rows. Keep the
        // last known cache so voiced-cutscene state survives alt-tab and reloads.
        if (questNames.Count == 0
            || questNames.All(name => name.StartsWith("Unknown quest slot", StringComparison.OrdinalIgnoreCase)))
            return;

        var signature = string.Join("\u001F", questNames);
        lock (_cacheLock)
        {
            if (string.Equals(signature, _activeQuestSignature, StringComparison.Ordinal))
                return;

            _activeQuestSignature = signature;
            _wikiPages.Clear();
            _results.Clear();
        }
    }

    private static string? ExtractWikitext(JsonElement root)
    {
        if (!root.TryGetProperty("query", out var query)
            || !query.TryGetProperty("pages", out var pages))
            return null;

        foreach (var page in pages.EnumerateObject())
        {
            if (!page.Value.TryGetProperty("revisions", out var revisions)
                || revisions.GetArrayLength() == 0)
                continue;

            var main = revisions[0].GetProperty("slots").GetProperty("main");
            if (main.TryGetProperty("*", out var legacyContent))
                return legacyContent.GetString();
            if (main.TryGetProperty("content", out var content))
                return content.GetString();
        }

        return null;
    }

    private static string CleanInlineMarkup(string text)
    {
        var withoutTemplates = Regex.Replace(text, @"\{\{[^{}]*\}\}", " ");
        var withoutLinks = withoutTemplates
            .Replace("'''", string.Empty, StringComparison.Ordinal)
            .Replace("''", string.Empty, StringComparison.Ordinal)
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal);
        return WebUtility.HtmlDecode(withoutLinks).Trim();
    }

    private static VoicedCutsceneClassification ScoreAgainstPages(
        IEnumerable<string> wikitexts, string text)
    {
        var bestVoiced = 0.0;
        var bestUnvoiced = 0.0;
        foreach (var wikitext in wikitexts)
        {
            var (voiced, unvoiced) = ScoreAgainstPage(wikitext, text);
            if (voiced > bestVoiced)
                bestVoiced = voiced;
            if (unvoiced > bestUnvoiced)
                bestUnvoiced = unvoiced;
        }

        const double threshold = 0.7;
        if (bestVoiced >= threshold && bestVoiced > bestUnvoiced)
            return VoicedCutsceneClassification.Voiced;
        if (bestUnvoiced >= threshold && bestUnvoiced > bestVoiced)
            return VoicedCutsceneClassification.NotVoiced;
        return VoicedCutsceneClassification.Unknown;
    }

    private static (double Voiced, double Unvoiced) ScoreAgainstPage(string wikitext, string text)
    {
        var bestVoiced = 0.0;
        var bestUnvoiced = 0.0;
        foreach (var (lineText, isVoiced) in ExtractDialogueLines(wikitext))
        {
            var score = Similarity(lineText, text);
            if (isVoiced)
            {
                if (score > bestVoiced)
                    bestVoiced = score;
            }
            else if (score > bestUnvoiced)
            {
                bestUnvoiced = score;
            }
        }

        return (bestVoiced, bestUnvoiced);
    }

    private static IEnumerable<(string Text, bool IsVoiced)> ExtractDialogueLines(string wikitext)
    {
        bool? voiced = null;
        foreach (var raw in wikitext.Split('\n'))
        {
            var line = raw.Trim();
            if (IsVoicedCutsceneStart(line))
            {
                voiced = true;
                continue;
            }
            if (IsUnvoicedCutsceneStart(line))
            {
                voiced = false;
                continue;
            }
            if (IsCutsceneEnd(line))
            {
                voiced = null;
                continue;
            }
            if (voiced is null || line.Length == 0)
                continue;

            // Skip control markers, headings, and template/markup lines.
            if ("-={|}*[".Contains(line[0]))
                continue;

            var colonIndex = line.IndexOf(": ", StringComparison.Ordinal);
            if (colonIndex <= 0)
                continue;

            var speaker = CleanInlineMarkup(line[..colonIndex].Trim());
            var dialogue = CleanInlineMarkup(line[(colonIndex + 2)..].Trim());
            if (string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(dialogue))
                continue;

            yield return (dialogue, voiced.Value);
        }
    }

    // The wiki has two marker conventions: the raw "-cutscene start"/"-cutscene end"
    // markers and the rendered "Start of cutscene."/"End of cutscene." forms.
    // Accept both so a quest whose page uses either style is classified.
    private static bool IsVoicedCutsceneStart(string line) =>
        line.StartsWith("-voiced cutscene start", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Start of voiced cutscene", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnvoicedCutsceneStart(string line) =>
        line.StartsWith("-cutscene start", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Start of cutscene", StringComparison.OrdinalIgnoreCase);

    private static bool IsCutsceneEnd(string line) =>
        line.StartsWith("-cutscene end", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("End of cutscene", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("End of voiced cutscene", StringComparison.OrdinalIgnoreCase);

    private static double Similarity(string wikiText, string gameText)
    {
        var wikiNorm = NormalizeDialogue(wikiText);
        var gameNorm = NormalizeDialogue(gameText);
        if (wikiNorm.Length == 0 || gameNorm.Length == 0)
            return 0;

        if (string.Equals(wikiNorm, gameNorm, StringComparison.Ordinal))
            return 1.0;

        if (wikiNorm.Length >= 8 && gameNorm.Length >= 8
            && (wikiNorm.Contains(gameNorm, StringComparison.Ordinal)
                || gameNorm.Contains(wikiNorm, StringComparison.Ordinal)))
            return 0.95;

        var wikiWords = new HashSet<string>(TokenizeDialogue(wikiText), StringComparer.Ordinal);
        var gameWords = new HashSet<string>(TokenizeDialogue(gameText), StringComparer.Ordinal);
        if (wikiWords.Count == 0 || gameWords.Count == 0)
            return 0;

        var intersection = wikiWords.Count(gameWords.Contains);
        var union = wikiWords.Count + gameWords.Count - intersection;
        return (double)intersection / union;
    }

    private static string NormalizeDialogue(string value)
    {
        var withoutTemplates = Regex.Replace(value, @"\{\{[^{}]*\}\}", " ");
        var withoutMarkup = withoutTemplates
            .Replace("'''", string.Empty, StringComparison.Ordinal)
            .Replace("''", string.Empty, StringComparison.Ordinal)
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal)
            .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase);
        var decoded = WebUtility.HtmlDecode(withoutMarkup);
        return new string(decoded
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string[] TokenizeDialogue(string value)
    {
        var normalized = Regex.Replace(value, @"\{\{[^{}]*\}\}", " ")
            .Replace("'''", string.Empty, StringComparison.Ordinal)
            .Replace("''", string.Empty, StringComparison.Ordinal)
            .Replace("[[", string.Empty, StringComparison.Ordinal)
            .Replace("]]", string.Empty, StringComparison.Ordinal);
        return Regex.Split(WebUtility.HtmlDecode(normalized), @"[^\p{L}\p{N}]+")
            .Where(token => token.Length > 1)
            .Select(token => token.ToUpperInvariant())
            .ToArray();
    }

    private static string GetKey(string speaker, string text) => $"{speaker}\n{text}";
}
