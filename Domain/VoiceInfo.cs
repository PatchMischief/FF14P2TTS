namespace FF14P2TTS;

/// <summary>
/// Structured voice metadata shared by every TTS provider.
/// </summary>
public class VoiceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string RawLanguage { get; set; } = "";  // "american_english"
    public string Language { get; set; } = "";     // "EN-US"
    public string Gender { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
