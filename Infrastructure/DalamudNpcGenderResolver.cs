using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FF14P2TTS.Application;
using Lumina.Excel.Sheets;

namespace FF14P2TTS.Infrastructure;

/// <summary>Dalamud data-sheet implementation of NPC gender lookups.</summary>
public sealed class DalamudNpcGenderResolver : INpcGenderResolver
{
    // Dialogue uses shortened display names for a few NPCs, which do not always
    // match the name stored in ENpcResident. Keep only verified fallbacks here.
    private static readonly IReadOnlyDictionary<string, NpcGender> KnownGenderOverrides =
        new Dictionary<string, NpcGender>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gaia"] = NpcGender.Female,
            ["Dulia-Chai"] = NpcGender.Female,
        };

    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly Configuration _configuration;
    private readonly ConcurrentDictionary<string, NpcGender> _cache = new();

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
                _log.Warning($"[FF14P2TTS] Ambiguous NPC gender for '{speakerName}'; add a verified override");
        }

        _cache[speakerName] = NpcGender.Unknown;
        _log.Debug($"[FF14P2TTS] '{speakerName}' not found in ENpcResident");
        return NpcGender.Unknown;
    }

    private static string NormalizeName(string name) => new(name
        .Where(char.IsLetterOrDigit)
        .Select(char.ToUpperInvariant)
        .ToArray());
}
