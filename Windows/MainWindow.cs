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
        "Player2 TTS Control###FF14P2TTSMain",
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
        var engineName = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure ? "Microsoft Azure" : "Player2";
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
            ImGui.Text($"Voice: {(_configuration.DefaultVoice.Length > 0 ? _configuration.DefaultVoice : "preset-based")}");
        }
        else
        {
            ImGui.Text($"Azure Region: {_configuration.AzureRegion}");
            ImGui.Text($"Voice: {_configuration.AzureDefaultVoice}");
        }
        ImGui.Text($"Volume: {_configuration.Volume}% | Speed: {_configuration.Speed:F1}x");
        ImGui.Text($"NPC Talk: {(_configuration.ReadNpcTalk ? "ON" : "OFF")} | Own chat: {(_configuration.ReadOwnMessages ? "ON" : "OFF")}");

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("Chat Commands");

        ImGui.BulletText("/p2tts on - Enable TTS");
        ImGui.BulletText("/p2tts off - Disable TTS");
        ImGui.BulletText("/p2tts toggle - Toggle TTS");
        ImGui.BulletText("/p2tts status - Check server status");
        ImGui.BulletText("/p2tts test <msg> - Speak a test message");
        ImGui.BulletText("/p2tts voice <name> - Change voice");
        ImGui.BulletText("/p2tts engine <player2|azure> - Switch engine");
        ImGui.BulletText("/p2tts config - Open settings");
    }

    private async Task CheckServerStatusAsync()
    {
        if (_isChecking) return;
        _isChecking = true;
        _statusText = "Checking...";

        try
        {
            var engineName = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure ? "Azure" : "Player2";
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
