using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace FF14P2TTS.Windows;

/// <summary>
/// Main control window for quick TTS actions.
/// </summary>
public class MainWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private readonly Configuration _configuration;
    private string _testMessage = "Hello, Eorzea!";
    private string _statusText = string.Empty;
    private bool _isChecking;

    public MainWindow(Plugin plugin) : base(
        "FF14 TTS Control###FF14TTSMain",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(350, 280);
        SizeCondition = ImGuiCond.FirstUseEver;

        _plugin = plugin;
        _configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Separator(); ImGui.Text("Quick Controls");

        // Enabled status
        var enabled = _configuration.TtsEnabled;
        var enabledText = enabled ? "TTS: ENABLED" : "TTS: DISABLED";
        var enabledColor = enabled ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1);
        ImGui.TextColored(enabledColor, enabledText);

        // Engine indicator
        var engineName = _configuration.ActiveEngine switch
        {
            TtsEngine.MicrosoftAzure => "Microsoft Azure",
            TtsEngine.ElevenLabs => "ElevenLabs",
            TtsEngine.Speechify => "Speechify",
            _ => "Player2",
        };
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1, 1), $"[{engineName}]");

        ImGui.Spacing();

        // Toggle button
        if (ImGui.Button(enabled ? "Disable TTS" : "Enable TTS", new Vector2(150, 30)))
        {
            _configuration.TtsEnabled = !_configuration.TtsEnabled;
            _configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Open Settings", new Vector2(150, 30)))
        {
            _plugin.ToggleConfigUi();
        }

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("Test TTS");

        ImGui.Text("Test message:");
        ImGui.InputText("##testmsg", ref _testMessage, 256);

        if (ImGui.Button("Speak Test", new Vector2(120, 25)))
        {
            _ = _plugin.ActiveTtsService.SpeakAsync(_testMessage);
        }

        ImGui.SameLine();
        if (ImGui.Button("Server Status", new Vector2(120, 25)))
        {
            _ = CheckServerStatusAsync();
        }

        if (!string.IsNullOrEmpty(_statusText))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(_statusText);
        }

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("Info");

        ImGui.Text($"Engine: {engineName}");
        if (_configuration.ActiveEngine == TtsEngine.Player2)
        {
            ImGui.Text($"Player2 URL: {_configuration.Player2BaseUrl}");
            var voiceLabel = _configuration.DefaultVoice.Length > 0
                ? _configuration.DefaultVoice
                : "preset-based";
            ImGui.Text($"Voice: {voiceLabel}");
        }
        else if (_configuration.ActiveEngine == TtsEngine.MicrosoftAzure)
        {
            ImGui.Text($"Azure Region: {_configuration.AzureRegion}");
            ImGui.Text($"Voice: {_configuration.AzureDefaultVoice}");
        }
        else if (_configuration.ActiveEngine == TtsEngine.ElevenLabs)
        {
            ImGui.Text($"ElevenLabs URL: {_configuration.ElevenLabsBaseUrl}");
            ImGui.Text($"Voice: {_configuration.ElevenLabsDefaultVoice}");
        }
        else
        {
            ImGui.Text($"Speechify URL: {_configuration.SpeechifyBaseUrl}");
            ImGui.Text($"Voice: {_configuration.SpeechifyDefaultVoice}");
        }
        ImGui.Text($"Volume: {_configuration.Volume}% | Speed: {_configuration.Speed:F1}x");
        ImGui.Text($"NPC Talk: {(_configuration.ReadNpcTalk ? "ON" : "OFF")}");

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("Chat Commands");

        ImGui.BulletText("/ff14tts on - Enable TTS");
        ImGui.BulletText("/ff14tts off - Disable TTS");
        ImGui.BulletText("/ff14tts toggle - Toggle TTS");
        ImGui.BulletText("/ff14tts status - Check server status");
        ImGui.BulletText("/ff14tts quests - Show active quests");
        ImGui.BulletText("/ff14tts test <msg> - Speak a test message");
        ImGui.BulletText("/ff14tts voice <name> - Change voice");
        ImGui.BulletText("/ff14tts engine <player2|azure|elevenlabs|speechify> - Switch engine");
        ImGui.BulletText("/ff14tts config - Open settings");
    }

    private async Task CheckServerStatusAsync()
    {
        if (_isChecking) return;
        _isChecking = true;
        _statusText = "Checking...";

        try
        {
            var engineName = _configuration.ActiveEngine switch
            {
                TtsEngine.MicrosoftAzure => "Azure",
                TtsEngine.ElevenLabs => "ElevenLabs",
                _ => "Player2",
            };
            var available = await _plugin.ActiveTtsService.IsServerAvailableAsync();
            if (available)
            {
                _statusText = $"✓ {engineName} TTS engine is ONLINE";
                var voices = await _plugin.ActiveTtsService.GetAvailableVoicesAsync();
                if (voices.Length > 0)
                    _statusText += $"\nVoices: {string.Join(", ", voices.Take(10))}{(voices.Length > 10 ? "..." : "")}";
            }
            else
            {
                _statusText = $"✗ {engineName} TTS engine is OFFLINE";
            }
        }
        catch (Exception ex)
        {
            _statusText = $"✗ Error: {ex.Message}";
        }
        finally
        {
            _isChecking = false;
        }
    }
}
