using System.Collections.Generic;

namespace FF14P2TTS;

public partial class Configuration
{
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
    public bool AzureUseEmotionTagging { get; set; } = false;
    public Dictionary<string, string> AzureNpcVoiceAssignments { get; set; } = new(); // NPC name -> Azure voice name
}
