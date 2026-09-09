using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FF14P2TTS.Application;

namespace FF14P2TTS;

public class AutoAdvanceHandler : IDisposable, IAutoAdvanceHandler
{
    private readonly Configuration _config;
    private readonly IPluginLog _log;
    private readonly IGameGui _gameGui;

    private const int KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_NUMPAD0 = 0x60;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, UIntPtr dwExtraInfo);

    public AutoAdvanceHandler(Configuration config, IPluginLog log, IGameGui gameGui)
    {
        _config = config;
        _log = log;
        _gameGui = gameGui;
    }

    public void OnDialogSpoken(string text)
    {
        if (!_config.AutoAdvanceNpcDialog) return;

        if (IsGameAutoAdvancing()) return;

        var wordCount = Math.Max(1, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        var delayMs = (int)(wordCount * 60000.0 / Math.Max(50, _config.AutoAdvanceWpm));

        _log.Debug($"[FF14P2TTS] Auto-advance: {wordCount} words, {delayMs}ms @ {_config.AutoAdvanceWpm}wpm");

        _ = PressConfirmAfterAsync(delayMs);
    }

    private bool IsGameAutoAdvancing()
    {
        try
        {
            // Cutscene playing — game handles advancement itself
            if (_gameGui.GetAddonByName("CutScene", 1) != nint.Zero)
            {
                _log.Debug("[FF14P2TTS] CutScene active — skipping");
                return true;
            }

            // Dialog has choices — player must select, don't auto-advance
            if (_gameGui.GetAddonByName("SelectString", 1) != nint.Zero)
            {
                _log.Debug("[FF14P2TTS] SelectString active (choices) — skipping");
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS] Check error: {ex.Message}");
        }
        return false;
    }

    private static async Task PressConfirmAfterAsync(int delayMs)
    {
        await Task.Delay(delayMs).ConfigureAwait(false);
        keybd_event(VK_NUMPAD0, 0x45, 0, UIntPtr.Zero);
        await Task.Delay(30).ConfigureAwait(false);
        keybd_event(VK_NUMPAD0, 0x45, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public void Dispose() { }
}
