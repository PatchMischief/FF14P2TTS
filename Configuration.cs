using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace FF14P2TTS;

public enum TtsEngine
{
    Player2,
    MicrosoftAzure
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;

    // TTS engine selection
    public TtsEngine ActiveEngine { get; set; } = TtsEngine.Player2;

    // Player2 API settings
    public string Player2BaseUrl { get; set; } = "http://127.0.0.1:4315";
    public string DefaultVoice { get; set; } = "";
    public int Volume { get; set; } = 100;
    public double Speed { get; set; } = 1.0;

    // Voice presets (Player2)
    public string UnisexVoiceId { get; set; } = "01955d76-ed5b-74de-83e5-800a44fee0d1";
    public string MaleVoiceId { get; set; } = "01955d76-ed5b-74c6-ac15-ab68ee19d560";
    public string FemaleVoiceId { get; set; } = "01955d76-ed5b-73e0-a88d-cbeb3c5b499d";
    public bool UseGenderedVoices { get; set; } = true;
    public bool UsePerNpcVoices { get; set; } = true; // assign unique voice per NPC
    public Dictionary<string, string> NpcVoiceAssignments { get; set; } = new(); // NPC name -> voice ID (Player2)

    // Azure TTS settings
    public string AzureSubscriptionKey { get; set; } = "";
    public string AzureRegion { get; set; } = "germanywestcentral";
    public string AzureEndpoint { get; set; } = ""; // optional custom endpoint URL
    public string AzureDefaultVoice { get; set; } = "en-US-AriaNeural";
    public string AzureUnisexVoice { get; set; } = "en-US-AriaNeural";
    public string AzureMaleVoice { get; set; } = "en-US-DavisNeural";
    public string AzureFemaleVoice { get; set; } = "en-US-JennyNeural";
    public bool AzureUseGenderedVoices { get; set; } = true;
    public bool AzureUsePerNpcVoices { get; set; } = true;
    public Dictionary<string, string> AzureNpcVoiceAssignments { get; set; } = new(); // NPC name -> Azure voice name

    // NPC dialog
    public bool ReadNpcTalk { get; set; } = true;
    public bool ReadNpcBattleTalk { get; set; } = true;
    public bool SkipTtsDuringCutscenes { get; set; } = true; // skip TTS for voiced cutscenes
    public bool IncludeNpcSpeakerName { get; set; } = true; // say speaker name before dialogue
    public bool AutoAdvanceNpcDialog { get; set; } = false;
    public int AutoAdvanceWpm { get; set; } = 160; // words per minute for timing

    // Chat channel filters
    public bool TtsEnabled { get; set; } = true;
    public bool ReadSay { get; set; } = true;
    public bool ReadParty { get; set; } = true;
    public bool ReadAlliance { get; set; } = false;
    public bool ReadFreeCompany { get; set; } = false;
    public bool ReadTell { get; set; } = true;
    public bool ReadYell { get; set; } = false;
    public bool ReadShout { get; set; } = false;
    public bool ReadLinkshell1 { get; set; } = false;
    public bool ReadLinkshell2 { get; set; } = false;
    public bool ReadLinkshell3 { get; set; } = false;
    public bool ReadLinkshell4 { get; set; } = false;
    public bool ReadLinkshell5 { get; set; } = false;
    public bool ReadLinkshell6 { get; set; } = false;
    public bool ReadLinkshell7 { get; set; } = false;
    public bool ReadLinkshell8 { get; set; } = false;
    public bool ReadNoviceNetwork { get; set; } = false;
    public bool ReadEmote { get; set; } = false;
    public bool ReadSystemMessage { get; set; } = false;

    // Speaker name
    public bool IncludeSpeakerName { get; set; } = true;
    public bool ReadOwnMessages { get; set; } = false;
    public string SpeakerNameFormat { get; set; } = "{0} says: {1}";

    // Voice override per channel
    public Dictionary<string, string> ChannelVoiceOverrides { get; set; } = new();

    // Advanced
    public bool SkipDuplicateMessages { get; set; } = true;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public bool DebugLogging { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
