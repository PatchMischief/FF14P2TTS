using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;

namespace FF14P2TTS;

public sealed class ActiveQuestReader
{
    private const uint QuestSheetRowOffset = 0x10000;
    private readonly IDataManager _dataManager;

    /// <summary>
    /// True when the most recent read had access to both the quest sheet and the
    /// game's QuestManager. False while the game world is unavailable (e.g. the
    /// game window is inactive or the player is not in the world).
    /// </summary>
    public bool QuestReadAvailable { get; private set; }

    public ActiveQuestReader(IDataManager dataManager)
    {
        _dataManager = dataManager;
    }

    public unsafe List<ActiveQuestInfo> ReadActiveQuests(out string diagnostic)
    {
        var result = new List<ActiveQuestInfo>();
        diagnostic = string.Empty;
        QuestReadAvailable = false;

        try
        {
            var questSheet = _dataManager.GetExcelSheet<Quest>();
            var questManager = QuestManager.Instance();
            if (questSheet is null)
            {
                diagnostic = "Quest sheet is unavailable.";
                return result;
            }

            if (questManager is null)
            {
                diagnostic = "The game's QuestManager is unavailable. Enter the game world and try again.";
                return result;
            }

            QuestReadAvailable = true;

            var slotCount = 0;
            var unresolvedCount = 0;
            foreach (ref var questWork in questManager->NormalQuests)
            {
                if (questWork.QuestId == 0)
                    continue;

                slotCount++;
                var questRowId = (uint)questWork.QuestId + QuestSheetRowOffset;
                if (!questSheet.TryGetRow(questRowId, out var quest))
                {
                    unresolvedCount++;
                    result.Add(new ActiveQuestInfo(
                        questRowId,
                        $"Unknown quest slot {questWork.QuestId} (row {questRowId})",
                        QuestManager.GetQuestSequence(questWork.QuestId)));
                    continue;
                }

                result.Add(new ActiveQuestInfo(
                    questRowId,
                    quest.Name.ExtractText(),
                    QuestManager.GetQuestSequence(questWork.QuestId)));
            }

            diagnostic =
                $"QuestManager returned {slotCount} occupied normal quest slots; {unresolvedCount} had no matching Quest row.";
            return result;
        }
        catch (Exception ex)
        {
            diagnostic = $"Quest read failed: {ex.Message}";
            return result;
        }
    }

    public unsafe List<ActiveQuestInfo> ReadActiveQuests() => ReadActiveQuests(out _);
}

public sealed record ActiveQuestInfo(uint Id, string Name, byte Sequence);