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
    private readonly ConcurrentDictionary<string, Task<bool>> _lookups = new(StringComparer.Ordinal);
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

    public Task<bool> IsVoicedAsync(string speaker, string text)
    {
        var questNames = GetActiveQuestNames();
        RefreshCacheForActiveQuests(questNames);

        var key = GetKey(speaker, text);
        if (_results.TryGetValue(key, out var result))
            return Task.FromResult(result);

        foreach (var wikitext in _wikiPages.Values)
        {
            if (ContainsVoicedOccurrence(wikitext, text))
                return Task.FromResult(true);
        }

        // No usable quest data right now (e.g. game window inactive). Don't cache
        // a negative result or fetch pages against an empty quest list.
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(false);

        if (!HasResolvableQuest(questNames))
        {
            // Data gap: no resolvable active quest names (game window inactive,
            // QuestManager unavailable, or rows that don't resolve). We cannot
            // positively classify the line as voiced, so fail open and let it
            // speak. The coordinator's native subtitle signal remains the
            // line-level safety net that still skips voiced cutscenes.
            if (!(_questReadAvailable?.Invoke() ?? true))
                _log.Debug("[FF14P2TTS] Voiced-cutscene classification skipped: quest read unavailable; failing open.");
            return Task.FromResult(false);
        }

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

    private async Task<bool> LookupActiveQuestPagesAsync(
        string speaker,
        string text,
        string key,
        IReadOnlyList<string> questNames)
    {
        var isVoiced = false;
        try
        {
            foreach (var title in questNames)
            {
                var wikitext = await GetOrFetchPageAsync(title).ConfigureAwait(false);
                if (wikitext is null)
                    continue;

                if (ContainsVoicedOccurrence(wikitext, text))
                {
                    isVoiced = true;
                    _log.Debug($"[FF14P2TTS] Wiki classified '{speaker}' as voiced in quest '{title}'");
                    break;
                }
            }

            if (!isVoiced)
                _log.Debug($"[FF14P2TTS] No voiced quest block matched '{speaker}'");
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS] Wiki voiced-cutscene lookup failed: {ex.Message}");
        }
        finally
        {
            _results[key] = isVoiced;
            _lookups.TryRemove(key, out _);
        }

        return isVoiced;
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

    private static bool ContainsVoicedOccurrence(string wikitext, string text)
    {
        var voicedStart = 0;
        while (true)
        {
            voicedStart = wikitext.IndexOf("-voiced cutscene start", voicedStart, StringComparison.OrdinalIgnoreCase);
            if (voicedStart < 0)
                return false;

            var blockEnd = wikitext.IndexOf("-cutscene end", voicedStart, StringComparison.OrdinalIgnoreCase);
            if (blockEnd < 0)
                blockEnd = wikitext.Length;

            var block = wikitext.Substring(voicedStart, blockEnd - voicedStart);
            if (MatchesNormalizedDialogue(block, text))
                return true;

            voicedStart += "-voiced cutscene start".Length;
        }
    }


    private static bool MatchesNormalizedDialogue(string wikitextBlock, string gameText)
    {
        var normalizedBlock = NormalizeDialogue(wikitextBlock);
        var normalizedGameText = NormalizeDialogue(gameText);
        if (normalizedGameText.Length == 0)
            return false;

        if (normalizedBlock.Contains(normalizedGameText, StringComparison.Ordinal))
            return true;

        var gameWords = TokenizeDialogue(gameText);
        if (gameWords.Length < 4)
            return false;

        var blockWords = new HashSet<string>(TokenizeDialogue(wikitextBlock), StringComparer.Ordinal);
        var matchedWords = gameWords.Count(blockWords.Contains);
        return matchedWords >= Math.Max(4, (int)Math.Ceiling(gameWords.Length * 0.8));
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
