namespace FF14P2TTS;

public partial class Configuration
{
    // Groq (emotion tagging) settings.
    public string GroqApiKey { get; set; } = "";
    public string GroqModel { get; set; } = "openai/gpt-oss-120b";
}
