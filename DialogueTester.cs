using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FF14P2TTS;

/// <summary>
/// Reports dialogue when the game finishes setting up its Talk UI.
/// A cutscene context identifies cinematic dialogue, but not whether a voice file is playing.
/// </summary>
public sealed class DialogueTester : IDisposable
{
    private readonly IAddonLifecycle addonLifecycle;
    private readonly INotificationManager notificationManager;
    private readonly ICondition condition;

    /// <summary>
    /// True only while the game's TalkSubtitle addon is visible. This is the
    /// line-level signal used to avoid duplicating native spoken subtitles.
    /// </summary>
    public volatile bool IsNativeVoiceSubtitleVisible;

    public DialogueTester(IAddonLifecycle addonLifecycle, INotificationManager notificationManager, ICondition condition)
    {
        this.addonLifecycle = addonLifecycle;
        this.notificationManager = notificationManager;
        this.condition = condition;

        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "Talk", OnTalkPostSetup);
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "TalkSubtitle", OnTalkSubtitlePostSetup);
        addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "TalkSubtitle", OnTalkSubtitleUpdate);
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Talk", OnTalkPostSetup);
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "TalkSubtitle", OnTalkSubtitlePostSetup);
        addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "TalkSubtitle", OnTalkSubtitleUpdate);
    }

    private unsafe void OnTalkPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!HasValidSetupValues(args))
            return;

        var addon = (AddonTalk*)args.Addon.Address;
        if (addon == null)
            return;

        ReportDialogue("Talk", ReadTextNode(addon->AtkTextNode228));
    }

    private unsafe void OnTalkSubtitlePostSetup(AddonEvent type, AddonArgs args)
    {
        if (!HasValidSetupValues(args))
            return;

        var addon = (AddonTalkSubtitle*)args.Addon.Address;
        if (addon == null)
            return;

        ReportDialogue("TalkSubtitle", addon->SubtitleText.ToString().Trim());
    }

    private unsafe void OnTalkSubtitleUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        IsNativeVoiceSubtitleVisible = addon != null && addon->IsVisible;
    }

    /// <summary>
    /// PostSetup provides AddonSetupArgs. Validate its AtkValue array before accessing
    /// the initialized addon data to avoid reading an incomplete setup payload.
    /// </summary>
    private static bool HasValidSetupValues(AddonArgs args)
    {
        return args is AddonSetupArgs setup
            && args.Addon != nint.Zero
            && setup.AtkValueCount > 0
            && setup.AtkValues != nint.Zero;
    }

    private bool IsInCutscene()
    {
        return condition[ConditionFlag.OccupiedInCutSceneEvent]
            || condition[ConditionFlag.WatchingCutscene]
            || condition[ConditionFlag.WatchingCutscene78];
    }

    private void ReportDialogue(string addonName, string text)
    {
        var cutscene = IsInCutscene();
        var notification = new Notification
        {
            Type = cutscene ? NotificationType.Warning : NotificationType.Info,
            Title = cutscene ? "TTS Dialogue Detector: CUTSCENE DIALOGUE" : "TTS Dialogue Detector: NPC DIALOGUE",
            Content = string.IsNullOrWhiteSpace(text) ? addonName : text,
            InitialDuration = TimeSpan.FromSeconds(3),
        };

        notificationManager.AddNotification(notification);
    }

    private static unsafe string ReadTextNode(AtkTextNode* textNode)
    {
        if (textNode == null)
            return string.Empty;

        return textNode->NodeText.StringPtr.AsDalamudSeString().TextValue
            .Trim()
            .Replace("\n", string.Empty)
            .Replace("\r", string.Empty);
    }
}
