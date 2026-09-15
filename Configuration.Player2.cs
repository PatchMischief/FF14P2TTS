using System.Collections.Generic;

namespace FF14P2TTS;

public partial class Configuration
{
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
}
