using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FF14P2TTS.Application;
using FF14P2TTS.Infrastructure.Audio;

namespace FF14P2TTS.Infrastructure.Speechify;

/// <summary>
/// Clears a quest's preloaded audio and cached wiki data once the quest is
/// marked finished/completed.
/// </summary>
public delegate void QuestCompletionClear(string questName);

/// <summary>
/// Preloads Speechify audio for each active quest's wiki-scripted unvoiced cutscene
/// dialogue. Each quest owns its own dialogue linked list and its own linked list
/// of ready-to-play Speechify responses, so multiple active quests preload
/// independently. Speechify synthesis latency is hidden because synthesis happens
/// ahead of time rather than when a line appears on screen.
/// </summary>
public sealed class SpeechifyDialoguePreloader : IDialoguePreloader, IDisposable
{
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly SpeechifyTtsService _speechify;
    private readonly IVoicedCutsceneDetector _wiki;
    private readonly Func<IReadOnlyList<ActiveQuestInfo>> _activeQuests;
    private readonly Func<string, string> _resolveVoice;
    private readonly QuestCompletionClear _clearQuestCache;

    private readonly object _sync = new();
    private readonly Dictionary<string, QuestPreloadState> _quests = new(StringComparer.OrdinalIgnoreCase);
    private string? _cachedDefaultVoice;
    private string? _currentQuestName;
    private PlayingBox? _playingBox;
    private CancellationTokenSource? _cts;

    public SpeechifyDialoguePreloader(
        Configuration config,
        IPluginLog log,
        SpeechifyTtsService speechify,
        IVoicedCutsceneDetector wiki,
        Func<IReadOnlyList<ActiveQuestInfo>> activeQuests,
        Func<string, string> resolveVoice)
    {
        _config = config;
        _log = log;
        _speechify = speechify;
        _wiki = wiki;
        _activeQuests = activeQuests;
        _resolveVoice = resolveVoice;
        _clearQuestCache = ClearQuestAudioAndCache;
    }

    /// <summary>Starts the background preload loop. Safe to call once.</summary>
    public void Start()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Debug($"[FF14P2TTS-Speechify] Dialogue preload failed: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.SpeechifyApiKey)
            || _config.ActiveEngine != TtsEngine.Speechify)
            return;

        var activeQuests = _activeQuests()
            .Where(quest => !string.IsNullOrWhiteSpace(quest.Name))
            .GroupBy(quest => quest.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(quest => quest.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (activeQuests.Count == 0
            || activeQuests.All(quest =>
                quest.Name.StartsWith("Unknown quest slot", StringComparison.OrdinalIgnoreCase)))
            return;

        ClearFinishedQuests(activeQuests.Select(quest => quest.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));

        foreach (var quest in activeQuests)
        {
            ct.ThrowIfCancellationRequested();

            QuestPreloadState state;
            lock (_sync)
            {
                if (!_quests.TryGetValue(quest.Name, out var existing))
                {
                    existing = new QuestPreloadState(quest.Id, quest.Name);
                    _quests[quest.Name] = existing;
                }
                state = existing;
            }

            if (state.PreloadComplete)
                continue;

            if (!state.ScriptFetched)
            {
                // Per-quest linked list #1: ordered dialogue boxes from the wiki API response.
                var dialogueQueue = await _wiki.GetUnvoicedDialogueBoxesAsync(quest.Name).ConfigureAwait(false);
                foreach (var box in dialogueQueue)
                    state.DialogueQueue.AddLast(box);
                state.ScriptFetched = true;

                if (state.DialogueQueue.Count == 0)
                {
                    state.PreloadComplete = true;
                    continue;
                }
            }

            // Per-quest linked list #2: send each whole dialogue box to Speechify
            // in a single request and queue the ready-to-play audio response.
            await PreloadQuestAsync(quest.Name, state, ct).ConfigureAwait(false);
            state.PreloadComplete = true;
        }
    }

    /// <summary>
    /// Evicts finished/abandoned quests. Called on a timer and again on territory
    /// changes so completed quests stop holding audio in memory immediately.
    /// </summary>
    public void SweepForFinishedQuests()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.SpeechifyApiKey))
                return;

            var questNames = _activeQuests()
                .Where(quest => !string.IsNullOrWhiteSpace(quest.Name))
                .Select(quest => quest.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (questNames.Count == 0
                || questNames.All(name => name.StartsWith("Unknown quest slot", StringComparison.OrdinalIgnoreCase)))
                return;

            ClearFinishedQuests(new HashSet<string>(questNames, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS-Speechify] Sweep failed: {ex.Message}");
        }
    }

    private void ClearFinishedQuests(HashSet<string> activeSet)
    {
        List<string> finished;
        lock (_sync)
            finished = _quests.Keys.Where(name => !activeSet.Contains(name)).ToList();

        foreach (var finishedQuest in finished)
            _clearQuestCache(finishedQuest);
    }

    private async Task PreloadQuestAsync(string questName, QuestPreloadState state, CancellationToken ct)
    {
        while (state.DialogueQueue.First is { } node)
        {
            ct.ThrowIfCancellationRequested();
            var box = node.Value;
            state.DialogueQueue.RemoveFirst();

            try
            {
                // One Speechify request for the entire dialogue box.
                var voice = await ResolvePreloadVoiceAsync(box.First.Speaker, ct).ConfigureAwait(false);
                var audio = await _speechify
                    .SynthesizeAsync(box.ConcatenatedText, voice, null, ct)
                    .ConfigureAwait(false);
                if (audio is null)
                    continue;

                var coveredLines = box.Lines
                    .Select(line => Normalize(line.Text))
                    .ToList();

                lock (_sync)
                    state.ReadyAudio.AddLast(new PreloadEntry(
                        box.First.Speaker,
                        Normalize(box.First.Text),
                        coveredLines,
                        audio));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Debug(
                    $"[FF14P2TTS-Speechify] Preload synthesis failed for '{box.First.Speaker}' in '{questName}': {ex.Message}");
            }
        }
    }

    private void ClearQuestAudioAndCache(string questName)
    {
        lock (_sync)
        {
            if (_quests.Remove(questName, out _))
                _log.Debug(
                    $"[FF14P2TTS-Speechify] Cleared preloaded audio and cache for completed quest '{questName}'.");

            if (string.Equals(_currentQuestName, questName, StringComparison.OrdinalIgnoreCase))
                _currentQuestName = null;

            if (_playingBox is not null
                && string.Equals(_playingBox.QuestName, questName, StringComparison.OrdinalIgnoreCase))
                _playingBox = null;
        }

        _wiki.ClearQuestCache(questName);
    }

    public bool TryPlay(string speaker, string text)
    {
        if (_config.ActiveEngine != TtsEngine.Speechify)
            return false;

        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return false;

        lock (_sync)
        {
            // The currently playing box already read all of its lines ahead of
            // time, so suppress any of its covered lines rather than speaking them again.
            var playing = _playingBox;
            if (playing is not null)
            {
                if (playing.CoveredLines.Any(line => IsMatch(line, normalized)))
                    return true;

                _playingBox = null;
            }

            var currentQuest = _currentQuestName;
            var snapshot = _quests
                .Where(kv => kv.Value.ReadyAudio.Count > 0)
                .OrderBy(kv => string.Equals(kv.Key, currentQuest, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();

            foreach (var (questName, state) in snapshot)
            {
                for (var node = state.ReadyAudio.First; node is not null; node = node.Next)
                {
                    if (!IsMatch(node.Value.FirstNormalized, normalized))
                        continue;

                    var entry = node.Value;
                    state.ReadyAudio.Remove(node);
                    _currentQuestName = questName;

                    if (entry.CoveredLines.Count > 1)
                        _playingBox = new PlayingBox(questName, entry.CoveredLines);

                    var volumeFactor = Math.Clamp(_config.Volume / 100.0, 0.0, 2.0);
                    _ = Task.Run(() =>
                        AudioPlayer.PlayAudio(entry.Audio, "speechify-preload", 24000, 1, 16, volumeFactor));
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<string?> ResolvePreloadVoiceAsync(string speaker, CancellationToken ct)
    {
        var voice = _resolveVoice(speaker);
        if (!string.IsNullOrWhiteSpace(voice))
            return voice;

        if (_cachedDefaultVoice is not null)
            return _cachedDefaultVoice;

        var voices = await _speechify.GetAvailableVoicesRawAsync(ct).ConfigureAwait(false);
        _cachedDefaultVoice = voices.FirstOrDefault()?.Id;
        return _cachedDefaultVoice;
    }

    private static bool IsMatch(string wikiNormalized, string gameNormalized)
    {
        if (wikiNormalized.Length == 0 || gameNormalized.Length == 0)
            return false;

        if (string.Equals(wikiNormalized, gameNormalized, StringComparison.Ordinal))
            return true;

        // Allow small differences such as the player name filling a [Forename] placeholder.
        if (wikiNormalized.Length >= 12 && gameNormalized.Contains(wikiNormalized, StringComparison.Ordinal))
            return true;

        if (gameNormalized.Length >= 12 && wikiNormalized.Contains(gameNormalized, StringComparison.Ordinal))
            return true;

        return false;
    }

    private static string Normalize(string text)
    {
        var withoutPlaceholders = Regex.Replace(text, @"\[[^\]]*\]", " ");
        return new string(withoutPlaceholders
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private sealed class QuestPreloadState
    {
        public QuestPreloadState(uint questId, string questName)
        {
            QuestId = questId;
            QuestName = questName;
        }

        public uint QuestId { get; }
        public string QuestName { get; }
        public LinkedList<WikiDialogueBox> DialogueQueue { get; } = new();
        public LinkedList<PreloadEntry> ReadyAudio { get; } = new();
        public bool ScriptFetched;
        public bool PreloadComplete;
    }

    private sealed class PlayingBox
    {
        public PlayingBox(string questName, IReadOnlyList<string> coveredLines)
        {
            QuestName = questName;
            CoveredLines = coveredLines;
        }

        public string QuestName { get; }
        public IReadOnlyList<string> CoveredLines { get; }
    }

    private sealed class PreloadEntry
    {
        public PreloadEntry(string speaker, string firstNormalized, IReadOnlyList<string> coveredLines, byte[] audio)
        {
            Speaker = speaker;
            FirstNormalized = firstNormalized;
            CoveredLines = coveredLines;
            Audio = audio;
        }

        public string Speaker { get; }
        public string FirstNormalized { get; }
        public IReadOnlyList<string> CoveredLines { get; }
        public byte[] Audio { get; }
    }
}
