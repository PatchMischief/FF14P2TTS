using System;
using System.Diagnostics;
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
/// Visibility is latched for a short grace window so a voiced line is still
/// recognised when the talk bubble updates a frame before the subtitle addon
/// (alt-tab resume, back-to-back voiced cutscenes).
/// </summary>
public sealed class DialogueTester : IDisposable
{
    /// <summary>How long a hidden subtitle still counts as "showing" to bridge frame races.</summary>
    public static readonly TimeSpan SubtitleGraceWindow = TimeSpan.FromSeconds(1);

    private readonly IAddonLifecycle _addonLifecycle;
    private bool _visible;
    private long _lastVisibleTick;

    public DialogueTester(IAddonLifecycle addonLifecycle)
    {
        _addonLifecycle = addonLifecycle;
        addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "TalkSubtitle", OnTalkSubtitleUpdate);
        addonLifecycle.RegisterListener(AddonEvent.PostHide, "TalkSubtitle", OnTalkSubtitleHide);
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "TalkSubtitle", OnTalkSubtitleUpdate);
        _addonLifecycle.UnregisterListener(AddonEvent.PostHide, "TalkSubtitle", OnTalkSubtitleHide);
    }

    /// <summary>True only while the game's TalkSubtitle addon was visible on its last update.</summary>
    public bool IsNativeVoiceSubtitleVisible => Volatile.Read(ref _visible);

    /// <summary>
    /// True if subtitles are visible now or were visible within
    /// <see cref="SubtitleGraceWindow"/>. Use this to decide whether a line is a
    /// native voiced subtitle; the grace window bridges the one-frame gap between
    /// the talk bubble and the subtitle addon.
    /// </summary>
    public bool IsNativeVoiceSubtitleActive()
    {
        if (IsNativeVoiceSubtitleVisible)
            return true;

        return Stopwatch.GetElapsedTime(Volatile.Read(ref _lastVisibleTick)) < SubtitleGraceWindow;
    }

    private unsafe void OnTalkSubtitleUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        var visible = addon != null && addon->IsVisible;
        Volatile.Write(ref _visible, visible);
        if (visible)
            Volatile.Write(ref _lastVisibleTick, Stopwatch.GetTimestamp());
    }

    private void OnTalkSubtitleHide(AddonEvent type, AddonArgs args)
    {
        // The addon was hidden - clear the latch so the subtitle signal cannot
        // remain stuck in the visible state after the voiced line ends.
        Volatile.Write(ref _visible, false);
    }
}
