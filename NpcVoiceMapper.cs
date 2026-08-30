using System;
using System.Collections.Generic;
using System.Linq;

namespace FF14P2TTS;

public enum NpcGender { Unknown, Male, Female }

/// <summary>
/// Assigns a unique voice to each NPC by name, persisted in config.
/// Supports both Player2 (UUID-based) and Azure (voice-name-based) engines.
/// </summary>
public class NpcVoiceMapper
{
    private readonly Configuration _config;
    private readonly Random _rng = new();

    public NpcVoiceMapper(Configuration config)
    {
        _config = config;
    }

    /// <summary>
    /// Get the voice ID for a given NPC based on the active TTS engine.
    /// </summary>
    public string GetVoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        if (_config.ActiveEngine == TtsEngine.MicrosoftAzure)
            return GetAzureVoiceForNpc(npcName, gender, availableVoices);
        else
            return GetPlayer2VoiceForNpc(npcName, gender, availableVoices);
    }

    private string GetPlayer2VoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        if (!_config.UsePerNpcVoices)
        {
            return gender switch
            {
                NpcGender.Male => _config.MaleVoiceId,
                NpcGender.Female => _config.FemaleVoiceId,
                _ => _config.UnisexVoiceId
            };
        }

        // Check cache
        if (_config.NpcVoiceAssignments.TryGetValue(npcName, out var cached))
            return cached;

        // Pick a new voice from the appropriate gender pool
        var pool = gender switch
        {
            NpcGender.Male => availableVoices.Where(v =>
                string.Equals(v.Gender, "male", StringComparison.OrdinalIgnoreCase)).ToList(),
            NpcGender.Female => availableVoices.Where(v =>
                string.Equals(v.Gender, "female", StringComparison.OrdinalIgnoreCase)).ToList(),
            _ => availableVoices
        };

        if (pool.Count == 0)
            pool = availableVoices; // fallback to all voices

        var selected = pool[_rng.Next(pool.Count)];

        _config.NpcVoiceAssignments[npcName] = selected.Id;
        _config.Save();

        return selected.Id;
    }

    private string GetAzureVoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        if (!_config.AzureUsePerNpcVoices)
        {
            return gender switch
            {
                NpcGender.Male => _config.AzureMaleVoice,
                NpcGender.Female => _config.AzureFemaleVoice,
                _ => _config.AzureUnisexVoice
            };
        }

        // Check cache
        if (_config.AzureNpcVoiceAssignments.TryGetValue(npcName, out var cached))
            return cached;

        // Pick a new voice from the appropriate gender pool
        var pool = gender switch
        {
            NpcGender.Male => availableVoices.Where(v =>
                string.Equals(v.Gender, "male", StringComparison.OrdinalIgnoreCase)).ToList(),
            NpcGender.Female => availableVoices.Where(v =>
                string.Equals(v.Gender, "female", StringComparison.OrdinalIgnoreCase)).ToList(),
            _ => availableVoices
        };

        if (pool.Count == 0)
            pool = availableVoices; // fallback to all voices

        var selected = pool[_rng.Next(pool.Count)];

        _config.AzureNpcVoiceAssignments[npcName] = selected.Id;
        _config.Save();

        return selected.Id;
    }
}
