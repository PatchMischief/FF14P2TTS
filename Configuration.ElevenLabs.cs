using System.Collections.Generic;

namespace FF14P2TTS;

public partial class Configuration
{
    // ElevenLabs settings. Kept separate so switching providers never changes other credentials.
    public string ElevenLabsApiKey { get; set; } = "";
    public string ElevenLabsBaseUrl { get; set; } = "https://api.elevenlabs.io";
    public string ElevenLabsDefaultVoice { get; set; } = "";
    public bool ElevenLabsUsePerNpcVoices { get; set; } = true;
    public Dictionary<string, string> ElevenLabsNpcVoiceAssignments { get; set; } = new();
}
