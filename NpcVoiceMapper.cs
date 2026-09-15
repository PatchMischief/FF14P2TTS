using System;
using System.Collections.Generic;
using System.Linq;

namespace FF14P2TTS;

public class NpcVoiceMapper
{
    private const string AzureAnaVoiceId = "en-US-AnaNeural";
    private const string AzureFallbackFemaleVoiceId = "en-US-JennyNeural";
    private readonly Configuration _config;
    private readonly Random _rng = new();
    
    private readonly Dictionary<(TtsEngine, NpcGender), Queue<string>> _shuffleBags = new();

    // "Fewer Repeats" shuffle (see Spotify's shuffle engineering write-up):
    // generate several mathematically-random sequences, score each for freshness,
    // and pick the freshest so recently used voices are nudged toward the back.
    private const int ShuffleCandidateCount = 16;
    private const int RecentVoiceWindow = 8;
    private readonly Dictionary<string, int> _lastUsedSequence = new(StringComparer.Ordinal);
    private int _sequence;

    public NpcVoiceMapper(Configuration config)
    {
        _config = config;
    }

    public string GetVoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        return _config.ActiveEngine switch
        {
            TtsEngine.MicrosoftAzure => GetAzureVoiceForNpc(npcName, gender, availableVoices),
            TtsEngine.ElevenLabs => GetElevenLabsVoiceForNpc(npcName, gender, availableVoices),
            TtsEngine.Speechify => GetSpeechifyVoiceForNpc(npcName, gender, availableVoices),
            _ => GetPlayer2VoiceForNpc(npcName, gender, availableVoices),
        };
    }

    public void ForgetNpc(string npcName)
    {
        _config.NpcVoiceAssignments.Remove(npcName);
        _config.AzureNpcVoiceAssignments.Remove(npcName);
        _config.ElevenLabsNpcVoiceAssignments.Remove(npcName);
        _config.SpeechifyNpcVoiceAssignments.Remove(npcName);
        _config.Save();
    }

    private string GetPlayer2VoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        if (gender == NpcGender.Unknown)
            return _config.UnisexVoiceId;

        return GetAssignedVoice(
            npcName,
            gender,
            availableVoices,
            TtsEngine.Player2,
            _config.UsePerNpcVoices,
            _config.NpcVoiceAssignments,
            cached => IsCompatibleWithGender(cached, gender, availableVoices, isAzure: false),
            g => g switch
            {
                NpcGender.Male => _config.MaleVoiceId,
                NpcGender.Female => _config.FemaleVoiceId,
                _ => _config.UnisexVoiceId,
            });
    }

    private string GetAzureVoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        if (gender == NpcGender.Unknown)
            return _config.AzureUnisexVoice;

        return GetAssignedVoice(
            npcName,
            gender,
            availableVoices,
            TtsEngine.MicrosoftAzure,
            _config.AzureUsePerNpcVoices,
            _config.AzureNpcVoiceAssignments,
            cached => IsCompatibleWithGender(cached, gender, availableVoices, isAzure: true),
            g => g switch
            {
                NpcGender.Male => _config.AzureMaleVoice,
                NpcGender.Female => GetAzureFemaleDefaultVoice(),
                _ => _config.AzureUnisexVoice,
            });
    }

    private string GetElevenLabsVoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices) =>
        GetAssignedVoice(
            npcName,
            gender,
            availableVoices,
            TtsEngine.ElevenLabs,
            _config.ElevenLabsUsePerNpcVoices,
            _config.ElevenLabsNpcVoiceAssignments,
            cached => IsElevenLabsVoiceId(cached)
                && availableVoices.Any(voice => string.Equals(voice.Id, cached, StringComparison.OrdinalIgnoreCase)
                    && VoiceMatchesGender(voice, gender)),
            _ => _config.ElevenLabsDefaultVoice);

    private string GetSpeechifyVoiceForNpc(string npcName, NpcGender gender, List<VoiceInfo> availableVoices) =>
        GetAssignedVoice(
            npcName,
            gender,
            availableVoices,
            TtsEngine.Speechify,
            _config.SpeechifyUsePerNpcVoices,
            _config.SpeechifyNpcVoiceAssignments,
            cached => !LooksLikePlayer2Uuid(cached)
                && availableVoices.Any(voice => string.Equals(voice.Id, cached, StringComparison.OrdinalIgnoreCase)
                    && VoiceMatchesGender(voice, gender)),
            g => GetSpeechifyGenderVoice(g, availableVoices));

    private string GetAssignedVoice(
        string npcName,
        NpcGender gender,
        List<VoiceInfo> availableVoices,
        TtsEngine engine,
        bool usePerNpcVoices,
        Dictionary<string, string> assignments,
        Func<string, bool> isCachedAssignmentValid,
        Func<NpcGender, string> getDefaultVoice)
    {
        if (!usePerNpcVoices)
            return getDefaultVoice(gender);

        if (assignments.TryGetValue(npcName, out var cached) && isCachedAssignmentValid(cached))
            return cached;

        assignments.Remove(npcName);

        var selected = DrawVoiceFromBag(engine, gender, availableVoices);
        if (string.IsNullOrWhiteSpace(selected))
            return getDefaultVoice(gender);

        assignments[npcName] = selected;
        _config.Save();
        return selected;
    }

    private static bool VoiceMatchesGender(VoiceInfo voice, NpcGender gender)
    {
        if (gender == NpcGender.Unknown)
            return true;

        var target = gender == NpcGender.Male ? "male" : "female";
        return string.Equals(voice.Gender, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(voice.Gender, "not_specified", StringComparison.OrdinalIgnoreCase);
    }

    private string GetSpeechifyGenderVoice(NpcGender gender, List<VoiceInfo> availableVoices)
    {
        var target = gender switch
        {
            NpcGender.Male => "male",
            NpcGender.Female => "female",
            _ => null,
        };

        if (target is not null)
        {
            var match = availableVoices.FirstOrDefault(voice =>
                string.Equals(voice.Gender, target, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Id;
        }

        return _config.SpeechifyDefaultVoice;
    }

    private static bool IsElevenLabsVoiceId(string voiceId) =>
        voiceId.Length == 20 && voiceId.All(char.IsLetterOrDigit);

    private static bool LooksLikePlayer2Uuid(string voiceId) => Guid.TryParse(voiceId, out _);

    private string DrawVoiceFromBag(TtsEngine engine, NpcGender gender, List<VoiceInfo> availableVoices)
    {
        var key = (engine, gender);

        if (!_shuffleBags.TryGetValue(key, out var bag) || bag.Count == 0)
        {
            var pool = BuildVoicePool(engine, gender, availableVoices);
            if (pool.Count == 0)
                return string.Empty;

            bag = new Queue<string>(OrderVoicesForFewerRepeats(pool).Select(v => v.Id));
            _shuffleBags[key] = bag;
        }

        var voiceId = bag.Dequeue();
        MarkVoiceUsed(voiceId);
        return voiceId;
    }

    private static List<VoiceInfo> BuildVoicePool(
        TtsEngine engine,
        NpcGender gender,
        List<VoiceInfo> availableVoices)
    {
        var pool = gender switch
        {
            NpcGender.Male => availableVoices
                .Where(v => string.Equals(v.Gender, "male", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            NpcGender.Female => availableVoices
                .Where(v => string.Equals(v.Gender, "female", StringComparison.OrdinalIgnoreCase)
                    && (engine != TtsEngine.MicrosoftAzure
                        || !string.Equals(v.Id, AzureAnaVoiceId, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            _ => availableVoices.ToList()
        };

        if (pool.Count == 0 && engine == TtsEngine.Speechify)
            pool = availableVoices
                .Where(v => string.Equals(v.Gender, "not_specified", StringComparison.OrdinalIgnoreCase))
                .ToList();
        else if (pool.Count == 0)
            pool = availableVoices.ToList(); // fallback to all voices for Player2/Azure

        return pool;
    }

    /// <summary>
    /// Picks the freshest of several random orderings. Each candidate is a plain
    /// Fisher-Yates shuffle, so the math stays random; we only choose the one that
    /// pushes recently used voices toward the back.
    /// </summary>
    private List<VoiceInfo> OrderVoicesForFewerRepeats(List<VoiceInfo> pool)
    {
        if (pool.Count <= 1)
            return pool;

        var best = Shuffle(pool);
        var bestScore = int.MinValue;
        var recencyWindow = Math.Min(RecentVoiceWindow, pool.Count);

        for (var attempt = 0; attempt < ShuffleCandidateCount; attempt++)
        {
            var candidate = Shuffle(pool);
            var score = ScoreFreshness(candidate, recencyWindow);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private int ScoreFreshness(List<VoiceInfo> candidate, int recencyWindow)
    {
        var score = 0;
        for (var i = 0; i < candidate.Count; i++)
        {
            var recency = GetRecencyWeight(candidate[i].Id, recencyWindow);
            if (recency > 0)
                score -= recency * (candidate.Count - i); // earlier + more recent = bigger penalty
        }

        return score;
    }

    private int GetRecencyWeight(string voiceId, int recencyWindow)
    {
        if (!_lastUsedSequence.TryGetValue(voiceId, out var lastUsed))
            return 0;

        var age = _sequence - lastUsed;
        return age < recencyWindow ? recencyWindow - age : 0;
    }

    private void MarkVoiceUsed(string voiceId)
    {
        _sequence++;
        _lastUsedSequence[voiceId] = _sequence;
    }

    private List<VoiceInfo> Shuffle(List<VoiceInfo> pool)
    {
        var shuffled = new List<VoiceInfo>(pool);
        var n = shuffled.Count;
        while (n > 1)
        {
            n--;
            var k = _rng.Next(n + 1);
            (shuffled[k], shuffled[n]) = (shuffled[n], shuffled[k]);
        }

        return shuffled;
    }

    private string GetAzureFemaleDefaultVoice() => string.Equals(
        _config.AzureFemaleVoice,
        AzureAnaVoiceId,
        StringComparison.OrdinalIgnoreCase)
        ? AzureFallbackFemaleVoiceId
        : _config.AzureFemaleVoice;

    private static bool IsCompatibleWithGender(
        string voiceId,
        NpcGender gender,
        IEnumerable<VoiceInfo> availableVoices,
        bool isAzure)
    {
        if (isAzure && gender == NpcGender.Female
            && string.Equals(voiceId, AzureAnaVoiceId, StringComparison.OrdinalIgnoreCase))
            return false;

        var voice = availableVoices.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, voiceId, StringComparison.OrdinalIgnoreCase));

        return voice is not null
            && string.Equals(
                voice.Gender,
                gender == NpcGender.Male ? "male" : "female",
                StringComparison.OrdinalIgnoreCase);
    }
}
