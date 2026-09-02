using System;
using System.Collections.Generic;
using System.Linq;

namespace FF14P2TTS;

public enum NpcGender { Unknown, Male, Female }

public class NpcVoiceMapper
{
    private readonly Configuration _config;
    private readonly Random _rng = new();
    
    // Tracks the shuffled remaining voices. Keyed by (IsAzure, Gender).
    private readonly Dictionary<(bool, NpcGender), Queue<string>> _shuffleBags = new();

    public NpcVoiceMapper(Configuration config)
    {
        _config = config;
    }

    public string GetVoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        // Restored your original correct enum check
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

        if (_config.NpcVoiceAssignments.TryGetValue(npcName, out var cached))
            return cached;

        string selectedId = DrawVoiceFromBag(false, gender, availableVoices);
        
        _config.NpcVoiceAssignments[npcName] = selectedId;
        _config.Save();

        return selectedId;
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

        if (_config.AzureNpcVoiceAssignments.TryGetValue(npcName, out var cached))
            return cached;

        string selectedId = DrawVoiceFromBag(true, gender, availableVoices);
        
        _config.AzureNpcVoiceAssignments[npcName] = selectedId;
        _config.Save();

        return selectedId;
    }

    private string DrawVoiceFromBag(bool isAzure, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        var key = (isAzure, gender);

        if (!_shuffleBags.TryGetValue(key, out var bag) || bag.Count == 0)
        {
            var pool = gender switch
            {
                NpcGender.Male => availableVoices.Where(v => string.Equals(v.Gender, "male", StringComparison.OrdinalIgnoreCase)).ToList(),
                NpcGender.Female => availableVoices.Where(v => string.Equals(v.Gender, "female", StringComparison.OrdinalIgnoreCase)).ToList(),
                _ => availableVoices.ToList()
            };

            if (pool.Count == 0)
                pool = availableVoices.ToList(); // fallback to all voices

            // Fisher-Yates Shuffle
            int n = pool.Count;
            while (n > 1)
            {
                n--;
                int k = _rng.Next(n + 1);
                (pool[k], pool[n]) = (pool[n], pool[k]);
            }

            bag = new Queue<string>(pool.Select(v => v.Id));
            _shuffleBags[key] = bag;
        }

        return bag.Dequeue();
    }
}