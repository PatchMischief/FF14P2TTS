using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FF14P2TTS;

/// <summary>
/// Captures text from FFXIV's NPC dialogue bubbles (Talk and BattleTalk addons).
/// </summary>
public class NpcTalkHandler : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private string _lastNpcText = string.Empty;

    public event Action<string, string>? OnNpcTalk; // speaker, text

    public NpcTalkHandler(IAddonLifecycle addonLifecycle, IPluginLog log, Configuration config)
    {
        _addonLifecycle = addonLifecycle;
        _log = log;
        _config = config;

        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "BattleTalk", OnBattleTalkUpdate);
        _log.Information("[FF14P2TTS] NPC talk handlers registered");
    }

    private unsafe void OnTalkUpdate(AddonEvent type, AddonArgs args)
    {
        try
        {
            var ptr = new IntPtr(args.Addon);
            if (ptr == IntPtr.Zero) return;
            var addon = (FFXIVClientStructs.FFXIV.Client.UI.AddonTalk*)ptr;
            if (!addon->AtkUnitBase.IsVisible) return;

            var speaker = ReadTextNode(addon->AtkTextNode220);
            var text = ReadTextNode(addon->AtkTextNode228);

            _log.Debug($"[FF14P2TTS] Talk: visible, speaker='{speaker}', text='{text}'");
            if (!string.IsNullOrWhiteSpace(text))
                EmitNpcText(speaker, text, "Talk");
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS] Talk error: {ex.Message}");
        }
    }

    private unsafe void OnBattleTalkUpdate(AddonEvent type, AddonArgs args)
    {
        try
        {
            var ptr = new IntPtr(args.Addon);
            if (ptr == IntPtr.Zero) return;
            var unitBase = (AtkUnitBase*)ptr;
            if (!unitBase->IsVisible) return;

            var speaker = ReadTextNodeFromList(unitBase, 6);
            var text = ReadTextNodeFromList(unitBase, 4);

            _log.Debug($"[FF14P2TTS] BattleTalk: visible, speaker='{speaker}', text='{text}'");
            if (!string.IsNullOrWhiteSpace(text))
                EmitNpcText(speaker, text, "BattleTalk");
        }
        catch (Exception ex)
        {
            _log.Debug($"[FF14P2TTS] BattleTalk error: {ex.Message}");
        }
    }

    private static unsafe string ReadTextNodeFromList(AtkUnitBase* unitBase, uint nodeId)
    {
        var node = unitBase->GetNodeById(nodeId);
        if (node == null) return string.Empty;

        var textNode = (AtkTextNode*)node;
        return ReadTextNode(textNode);
    }

    private void EmitNpcText(string speaker, string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Only fire when text actually changes (PostUpdate fires every frame)
        if (text == _lastNpcText) return;

        _lastNpcText = text;

        _log.Debug($"[FF14P2TTS] NPC {source}: {text}");
        OnNpcTalk?.Invoke(speaker, text);
    }

    private static unsafe string ReadTextNode(AtkTextNode* textNode)
    {
        if (textNode == null) return string.Empty;
        var seString = textNode->NodeText.StringPtr.AsDalamudSeString();
        return seString.TextValue.Trim().Replace("\n", "").Replace("\r", "");
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "BattleTalk", OnBattleTalkUpdate);
    }
}
