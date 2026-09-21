using System;
using System.Threading;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FF14P2TTS;

/// <summary>
/// Tracks whether the game's TalkSubtitle addon is visible, which signals that
/// native voice subtitles are being shown.
///
/// Visibility is latched on the last addon update/hide so it survives alt-tab
/// (where addon updates stop firing) but never bleeds across scene boundaries.
/// The one-frame gap between the talk bubble and the subtitle addon is handled
/// by the coordinator, which re-checks visibility a few frames after the line
/// arrives instead of looking backward in time.
/// </summary>
public sealed class DialogueTester : IDisposable
{
    private readonly IAddonLifecycle _addonLifecycle;
    private bool _visible;

    public DialogueTester(IAddonLifecycle addonLifecycle)
    {
        _addonLifecycle = addonLifecycle;
        addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "TalkSubtitle", OnTalkSubtitleUpdate);
        addonLifecycle.RegisterListener(AddonEvent.PostHide, "TalkSubtitle", OnTalkSubtitleHide);
        addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        addonLifecycle.RegisterListener(AddonEvent.PostHide, "Talk", OnTalkHide);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "TalkSubtitle", OnTalkSubtitleUpdate);
        _addonLifecycle.UnregisterListener(AddonEvent.PostHide, "TalkSubtitle", OnTalkSubtitleHide);
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        _addonLifecycle.UnregisterListener(AddonEvent.PostHide, "Talk", OnTalkHide);
    }

    /// <summary>True while the game's TalkSubtitle addon was visible on its last update.</summary>
    public bool IsNativeVoiceSubtitleVisible => Volatile.Read(ref _visible);

    /// <summary>
    /// Reads the TalkSubtitle addon's current visibility directly from the game,
    /// bypassing the event-driven latch (which can stay stale when the addon is
    /// unloaded rather than hidden).
    /// </summary>
    public bool IsTalkSubtitleVisibleNow()
        => Plugin.GameGui.GetAddonByName("TalkSubtitle").IsVisible;

    private unsafe void OnTalkSubtitleUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        Volatile.Write(ref _visible, addon != null && addon->IsVisible);
    }

    private void OnTalkSubtitleHide(AddonEvent type, AddonArgs args)
    {
        // The addon was hidden - clear the latch so the subtitle signal cannot
        // remain stuck in the visible state after the voiced line ends.
        Volatile.Write(ref _visible, false);
    }

    private unsafe void OnTalkUpdate(AddonEvent type, AddonArgs args)
    {
        // The talk bubble is the dialogue container. When it is not visible, no
        // dialogue is on screen, so clear any stale subtitle latch (the subtitle
        // addon's own hide event may not fire if it is unloaded instead of hidden).
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible)
            Volatile.Write(ref _visible, false);
    }

    private void OnTalkHide(AddonEvent type, AddonArgs args)
    {
        // End of a dialogue section - clear the latch so a stale "visible" state
        // cannot bleed into the next scene.
        Volatile.Write(ref _visible, false);
    }
}
