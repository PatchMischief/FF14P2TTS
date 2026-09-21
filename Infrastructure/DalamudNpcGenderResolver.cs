using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FF14P2TTS.Application;
using Lumina.Excel.Sheets;

namespace FF14P2TTS.Infrastructure;

/// <summary>Dalamud data-sheet implementation of NPC gender lookups.</summary>
public sealed class DalamudNpcGenderResolver : INpcGenderResolver
{
    private const string FandomApiBaseUrl = "https://finalfantasy.fandom.com/api.php";
    private static readonly HttpClient HttpClient = new();
    private static readonly Regex GenderFieldRegex = new(
        @"^\s*\|\s*(?:gender|sex)\s*=\s*([^\r\n|]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    // Dialogue uses shortened display names for a few NPCs, which do not always
    // match the name stored in ENpcResident. Keep only verified fallbacks here.
    private static readonly IReadOnlyDictionary<string, NpcGender> KnownGenderOverrides =
        new Dictionary<string, NpcGender>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gaia"] = NpcGender.Female,
            ["Dulia-Chai"] = NpcGender.Female,
            // Main cast and recurring NPCs whose ENpcBase gender is wrong,
            // ambiguous, or missing in the game data.
            ["Alphinaud"] = NpcGender.Male,
            ["Alisaie"] = NpcGender.Female,
            ["Y'shtola"] = NpcGender.Female,
            ["Thancred"] = NpcGender.Male,
            ["Urianger"] = NpcGender.Male,
            ["Estinien"] = NpcGender.Male,
            ["G'raha Tia"] = NpcGender.Male,
            ["Krile"] = NpcGender.Female,
            ["Tataru"] = NpcGender.Female,
            ["Tiamat"] = NpcGender.Female,
        };

    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly Configuration _configuration;
    private readonly ConcurrentDictionary<string, NpcGender> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _fandomLookups = new();
    private readonly ConcurrentDictionary<string, Task<NpcGender>> _genderLookups = new(StringComparer.OrdinalIgnoreCase);

    public DalamudNpcGenderResolver(IDataManager dataManager, IPluginLog log, Configuration configuration)
    {
        _dataManager = dataManager;
        _log = log;
        _configuration = configuration;
    }

    public NpcGender GetGender(string speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName))
            return NpcGender.Unknown;

        if (_cache.TryGetValue(speakerName, out var cached))
            return cached;

        if (_configuration.NpcGenderOverrides.TryGetValue(speakerName, out var configuredGender))
        {
            _cache[speakerName] = configuredGender;
            _log.Debug($"[FF14P2TTS] Configured gender: '{speakerName}' -> {configuredGender}");
            return configuredGender;
        }

        if (KnownGenderOverrides.TryGetValue(speakerName, out var overriddenGender))
        {
            _cache[speakerName] = overriddenGender;
            _log.Debug($"[FF14P2TTS] Gender override: '{speakerName}' -> {overriddenGender}");
            return overriddenGender;
        }

        var residents = _dataManager.GetExcelSheet<ENpcResident>();
        var bases = _dataManager.GetExcelSheet<ENpcBase>();
        if (residents is not null && bases is not null)
        {
            var normalizedSpeakerName = NormalizeName(speakerName);
            var matches = residents
                .Where(resident => NormalizeName(resident.Singular.ExtractText()) == normalizedSpeakerName)
                .ToList();

            // Some dialogue entries include a title, while ENpcResident stores
            // the bare NPC name. Only accept a partial match when it is unique.
            if (matches.Count == 0)
            {
                matches = residents
                    .Where(resident =>
                    {
                        var normalizedResidentName = NormalizeName(resident.Singular.ExtractText());
                        return normalizedResidentName.Length > 0
                            && (normalizedSpeakerName.EndsWith(normalizedResidentName, StringComparison.Ordinal)
                                || normalizedResidentName.EndsWith(normalizedSpeakerName, StringComparison.Ordinal));
                    })
                    .ToList();
            }

            if (matches.Count == 1)
            {
                var gender = bases.GetRow(matches[0].RowId).Gender switch
                {
                    0 => NpcGender.Male,
                    1 => NpcGender.Female,
                    _ => NpcGender.Unknown,
                };
                _cache[speakerName] = gender;
                _log.Debug($"[FF14P2TTS] Sheet match: '{speakerName}' -> {gender}");
                return gender;
            }

            var exactMatches = residents
                .Where(resident => string.Equals(
                    resident.Singular.ExtractText(),
                    speakerName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var exactGenders = exactMatches
                .Select(resident => bases.GetRow(resident.RowId).Gender switch
                {
                    0 => NpcGender.Male,
                    1 => NpcGender.Female,
                    _ => NpcGender.Unknown,
                })
                .Distinct()
                .ToList();

            if (exactGenders.Count == 1)
            {
                var gender = exactGenders[0];
                _cache[speakerName] = gender;
                _log.Debug($"[FF14P2TTS] Sheet: '{speakerName}' -> {gender}");
                return gender;
            }

            if (exactGenders.Count > 1)
                _log.Debug($"[FF14P2TTS] Ambiguous NPC gender for '{speakerName}'; will try the wiki");
        }

        _cache[speakerName] = NpcGender.Unknown;
        _log.Debug($"[FF14P2TTS] '{speakerName}' not found in ENpcResident");
        return NpcGender.Unknown;
    }

    public void RequestGenderLookup(string speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName)
            || _configuration.NpcGenderOverrides.ContainsKey(speakerName)
            || KnownGenderOverrides.ContainsKey(speakerName)
            || _fandomLookups.TryAdd(speakerName, 0) == false)
            return;

        _ = LookupFandomGenderAsync(speakerName);
    }

    public async Task<NpcGender> GetGenderAsync(string speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName))
            return NpcGender.Unknown;

        var gender = GetGender(speakerName);
        if (gender != NpcGender.Unknown)
            return gender;

        // The game data was ambiguous or missing; fall back to the wiki and
        // await the result so the first line gets the correct gender.
        var lookup = _genderLookups.GetOrAdd(speakerName, GetFandomGenderAsync);
        return await lookup.ConfigureAwait(false);
    }

    private async Task<NpcGender> GetFandomGenderAsync(string speakerName)
    {
        await LookupFandomGenderAsync(speakerName).ConfigureAwait(false);
        return _cache.TryGetValue(speakerName, out var cached) ? cached : NpcGender.Unknown;
    }

    public void Forget(string speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName))
            return;

        _cache.TryRemove(speakerName, out _);
        _fandomLookups.TryRemove(speakerName, out _);
    }

    private async Task LookupFandomGenderAsync(string speakerName)
    {
        try
        {
            var searchUrl =
                $"{FandomApiBaseUrl}?action=query&list=search&srsearch={Uri.EscapeDataString(speakerName)}&srlimit=1&format=json";
            using var searchResponse = await HttpClient.GetAsync(searchUrl).ConfigureAwait(false);
            if (!searchResponse.IsSuccessStatusCode)
                return;

            using var searchDocument = JsonDocument.Parse(
                await searchResponse.Content.ReadAsStreamAsync().ConfigureAwait(false));
            if (!searchDocument.RootElement.TryGetProperty("query", out var query)
                || !query.TryGetProperty("search", out var results)
                || results.GetArrayLength() == 0)
                return;

            var pageTitle = results[0].GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(pageTitle))
                return;

            var titleUrl =
                $"{FandomApiBaseUrl}?action=query&prop=revisions&titles={Uri.EscapeDataString(pageTitle)}&rvprop=content&rvslots=main&format=json";
            using var pageResponse = await HttpClient.GetAsync(titleUrl).ConfigureAwait(false);
            if (!pageResponse.IsSuccessStatusCode)
                return;

            using var pageDocument = JsonDocument.Parse(
                await pageResponse.Content.ReadAsStreamAsync().ConfigureAwait(false));
            var wikitext = ExtractWikitext(pageDocument.RootElement);
            var gender = ParseGender(wikitext);
            if (gender == NpcGender.Unknown)
                return;

            _cache[speakerName] = gender;
            _log.Debug($"[FF14P2TTS] Fandom gender: '{speakerName}' -> {gender}");
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS] Fandom gender lookup failed for '{speakerName}': {ex.Message}");
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

            var revision = revisions[0];
            if (!revision.TryGetProperty("slots", out var slots)
                || !slots.TryGetProperty("main", out var main))
                continue;

            if (main.TryGetProperty("*", out var legacyContent))
                return legacyContent.GetString();
            if (main.TryGetProperty("content", out var content))
                return content.GetString();
        }

        return null;
    }

    private static NpcGender ParseGender(string? wikitext)
    {
        if (string.IsNullOrWhiteSpace(wikitext))
            return NpcGender.Unknown;

        var match = GenderFieldRegex.Match(wikitext);
        if (!match.Success)
            return NpcGender.Unknown;

        var value = match.Groups[1].Value.Trim();
        if (value.Contains("female", StringComparison.OrdinalIgnoreCase)
            || value.Equals("f", StringComparison.OrdinalIgnoreCase))
            return NpcGender.Female;
        if (value.Contains("male", StringComparison.OrdinalIgnoreCase)
            || value.Equals("m", StringComparison.OrdinalIgnoreCase))
            return NpcGender.Male;

        return NpcGender.Unknown;
    }

    private static string NormalizeName(string name) => new(name
        .Where(char.IsLetterOrDigit)
        .Select(char.ToUpperInvariant)
        .ToArray());
}
