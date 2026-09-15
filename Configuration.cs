using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace FF14P2TTS;

public enum TtsEngine
{
    Player2,
    MicrosoftAzure,
    ElevenLabs,
    Speechify
}

[Serializable]
public partial class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 12;

    // TTS engine selection
    public TtsEngine ActiveEngine { get; set; } = TtsEngine.Player2;

    // NPC gender overrides (displayed NPC name -> verified gender)
    public Dictionary<string, NpcGender> NpcGenderOverrides { get; set; } = new();

    // NPC dialog
    public bool ReadNpcTalk { get; set; } = true;
    public bool ReadNpcBattleTalk { get; set; } = true;
    public bool SkipTtsDuringCutscenes { get; set; } = true; // skip TTS for voiced cutscenes
    public bool IncludeNpcSpeakerName { get; set; } = true; // say speaker name before dialogue

    // Global
    public bool TtsEnabled { get; set; } = true;

    // Advanced
    public bool SkipDuplicateMessages { get; set; } = true;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public bool DebugLogging { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
