using System.Collections.Generic;

namespace FF14P2TTS;

public partial class Configuration
{
    // Speechify settings.
    public string SpeechifyApiKey { get; set; } = "";
    public string SpeechifyBaseUrl { get; set; } = "https://api.speechify.ai";
    public string SpeechifyDefaultVoice { get; set; } = "";
    public string SpeechifyMaleVoice { get; set; } = "";
    public string SpeechifyFemaleVoice { get; set; } = "";
    public bool SpeechifyUsePerNpcVoices { get; set; } = true;
    public Dictionary<string, string> SpeechifyNpcVoiceAssignments { get; set; } = new();
    public bool SpeechifyUseEmotionTagging { get; set; } = false;
}
