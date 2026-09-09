using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace FF14P2TTS.Windows;

/// <summary>
/// Main configuration window for the plugin.
/// </summary>
public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;
    private readonly Plugin _plugin;

    // Cached voice lists
    private List<VoiceInfo>? _cachedPlayer2Voices;
    private List<VoiceInfo>? _cachedAzureVoices;
    private bool _voicesFetched;
    private string _npcGenderOverrideName = string.Empty;
    private int _npcGenderOverrideIndex;

    public ConfigWindow(Plugin plugin) : base(
        "Player2 TTS Configuration###FF14P2TTSConfig",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(550, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        _plugin = plugin;
        _configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("##p2tts_tabs"))
        {
            if (ImGui.BeginTabItem("General"))
            {
                DrawGeneralTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Voice Presets"))
            {
                DrawVoicePresetsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("NPC Voices"))
            {
                DrawNpcVoicesTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Chat Channels"))
            {
                DrawChannelsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Voice Overrides"))
            {
                DrawVoiceOverridesTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Advanced"))
            {
                DrawAdvancedTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawGeneralTab()
    {
        ImGui.Separator(); ImGui.Text("TTS Engine Selection");

        // Engine dropdown
        var engineNames = new[] { "Player2 (Local)", "Microsoft Azure (Cloud)" };
        var currentEngineIndex = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure ? 1 : 0;
        ImGui.Text("TTS Engine:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(250);
        if (ImGui.Combo("##engine", ref currentEngineIndex, engineNames, engineNames.Length))
        {
            _configuration.ActiveEngine = currentEngineIndex == 1 ? TtsEngine.MicrosoftAzure : TtsEngine.Player2;
            _configuration.Save();
            // Clear cached voices when switching engine
            _cachedPlayer2Voices = null;
            _cachedAzureVoices = null;
            _voicesFetched = false;
        }

        ImGui.Spacing();

        // --- Player2-specific settings ---
        if (_configuration.ActiveEngine == TtsEngine.Player2)
        {
            ImGui.Separator(); ImGui.Text("Player2 Connection");

            var baseUrl = _configuration.Player2BaseUrl;
            ImGui.Text("Player2 API URL:");
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("##p2url", ref baseUrl, 256))
            {
                _configuration.Player2BaseUrl = baseUrl;
                _configuration.Save();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##p2urlpaste"))
            {
                var clip = ImGui.GetClipboardText();
                if (!string.IsNullOrEmpty(clip))
                {
                    _configuration.Player2BaseUrl = clip.Trim();
                    _configuration.Save();
                }
            }
            ImGuiExt.HelpMarker("The base URL of your Player2 TTS server.\nDefault: http://127.0.0.1:4315\n\nClick Paste to paste from clipboard (Ctrl+V won't work in-game).");
            ImGui.SameLine();
            if (ImGui.Button("Test Connection"))
            {
                _ = TestConnectionAsync();
            }
        }
        else
        {
            // --- Azure-specific settings ---
            ImGui.Separator(); ImGui.Text("Azure Connection");

            var subKey = _configuration.AzureSubscriptionKey;
            ImGui.Text("Subscription Key:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##azkey", ref subKey, 64))
            {
                _configuration.AzureSubscriptionKey = subKey;
                _configuration.Save();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##azkeypaste"))
            {
                var clip = ImGui.GetClipboardText();
                if (!string.IsNullOrEmpty(clip))
                {
                    _configuration.AzureSubscriptionKey = clip.Trim();
                    _configuration.Save();
                }
            }
            ImGuiExt.HelpMarker("Your Azure Cognitive Services Speech resource key.\nCreate one at portal.azure.com\n\nClick Paste to paste from clipboard (Ctrl+V won't work in-game).");

            var region = _configuration.AzureRegion;
            ImGui.Text("Azure Region:");
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("##azregion", ref region, 32))
            {
                _configuration.AzureRegion = region;
                _configuration.Save();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##azregionpaste"))
            {
                var clip = ImGui.GetClipboardText();
                if (!string.IsNullOrEmpty(clip))
                {
                    _configuration.AzureRegion = clip.Trim();
                    _configuration.Save();
                }
            }
            ImGuiExt.HelpMarker("Your Azure region, e.g. germanywestcentral, eastus, westeurope.");

            var endpoint = _configuration.AzureEndpoint;
            ImGui.Text("Endpoint URL (optional):");
            ImGui.SetNextItemWidth(400);
            if (ImGui.InputText("##azendpoint", ref endpoint, 128))
            {
                _configuration.AzureEndpoint = endpoint;
                _configuration.Save();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Paste##azendpointpaste"))
            {
                var clip = ImGui.GetClipboardText();
                if (!string.IsNullOrEmpty(clip))
                {
                    _configuration.AzureEndpoint = clip.Trim();
                    _configuration.Save();
                }
            }
            ImGuiExt.HelpMarker("Custom Speech service endpoint. Leave blank unless you use a private endpoint or sovereign cloud.\nThe correct format is: https://{region}.tts.speech.microsoft.com/\nIf you set a Region above, this field is ignored — the SDK constructs the correct endpoint automatically.");

            ImGui.SameLine();
            if (ImGui.Button("Test Connection"))
            {
                _ = TestConnectionAsync();
            }
        }

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("TTS Settings");

        // Enable/Disable
        var enabled = _configuration.TtsEnabled;
        if (ImGui.Checkbox("Enable TTS", ref enabled))
        {
            _configuration.TtsEnabled = enabled;
            _configuration.Save();
        }

        // Default Voice (engine-specific)
        if (_configuration.ActiveEngine == TtsEngine.Player2)
        {
            var voice = _configuration.DefaultVoice;
            ImGui.Text("Default Voice ID:");
            ImGui.SameLine();
            if (ImGui.InputText("##voice", ref voice, 64))
            {
                _configuration.DefaultVoice = voice;
                _configuration.Save();
            }
            ImGuiExt.HelpMarker("Player2 voice UUID. Leave blank to use preset-based voices.\nUse /p2tts status to see available voices.");
        }
        else
        {
            var azVoice = _configuration.AzureDefaultVoice;
            ImGui.Text("Default Voice:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(250);
            if (ImGui.InputText("##azvoice", ref azVoice, 64))
            {
                _configuration.AzureDefaultVoice = azVoice;
                _configuration.Save();
            }
            ImGuiExt.HelpMarker("Azure neural voice name, e.g. en-US-AriaNeural.\nSee Voice Presets tab for a list.");
        }

        // Volume
        var volume = _configuration.Volume;
        ImGui.Text("Volume:");
        ImGui.SameLine();
        if (ImGui.SliderInt("##volume", ref volume, 0, 200))
        {
            _configuration.Volume = volume;
            _configuration.Save();
        }

        // Speed
        var speed = (float)_configuration.Speed;
        ImGui.Text("Speed:  ");
        ImGui.SameLine();
        if (ImGui.SliderFloat("##speed", ref speed, 0.5f, 2.0f))
        {
            _configuration.Speed = Math.Round(speed, 1);
            _configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("Speaker Name");

        var includeSpeaker = _configuration.IncludeSpeakerName;
        if (ImGui.Checkbox("Include speaker name in TTS", ref includeSpeaker))
        {
            _configuration.IncludeSpeakerName = includeSpeaker;
            _configuration.Save();
        }

        var readOwn = _configuration.ReadOwnMessages;
        if (ImGui.Checkbox("Also read my own messages", ref readOwn))
        {
            _configuration.ReadOwnMessages = readOwn;
            _configuration.Save();
        }

        if (_configuration.IncludeSpeakerName)
        {
            var format = _configuration.SpeakerNameFormat;
            ImGui.Text("Format:");
            ImGui.SameLine();
            if (ImGui.InputText("##fmt", ref format, 256))
            {
                _configuration.SpeakerNameFormat = format;
                _configuration.Save();
            }
            ImGuiExt.HelpMarker("{0} = speaker name, {1} = message text\nDefault: \"{0} says: {1}\"");
        }

        ImGui.Spacing();
        if (ImGui.Button("Test TTS", new Vector2(120, 30)))
        {
            var engineLabel = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure ? "Azure" : "Player2";
            _ = _plugin.ActiveTtsService.SpeakAsync($"Hello, this is a test message from the {engineLabel} TTS engine.");
            PrintChat($"Test message sent to {engineLabel} TTS.");
        }
    }

    private void DrawNpcVoicesTab()
    {
        var isAzure = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure;
        var assignments = isAzure ? _configuration.AzureNpcVoiceAssignments : _configuration.NpcVoiceAssignments;

        ImGui.Separator(); ImGui.Text("Assigned NPC Voices");
        ImGui.TextWrapped("View and change voices assigned to NPCs you've encountered. Voices are auto-assigned the first time an NPC speaks.");

        ImGui.Text("Correct NPC gender:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        ImGui.InputText("##npcGenderOverrideName", ref _npcGenderOverrideName, 128);
        ImGui.SameLine();
        var overrideLabels = new[] { "Male", "Female" };
        ImGui.SetNextItemWidth(85);
        ImGui.Combo("##npcGenderOverrideValue", ref _npcGenderOverrideIndex, overrideLabels, overrideLabels.Length);
        ImGui.SameLine();
        if (ImGui.SmallButton("Save gender") && !string.IsNullOrWhiteSpace(_npcGenderOverrideName))
        {
            _configuration.NpcGenderOverrides[_npcGenderOverrideName.Trim()] = _npcGenderOverrideIndex == 0
                ? NpcGender.Male
                : NpcGender.Female;
            _configuration.Save();
        }

        if (assignments.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No NPCs have been assigned voices yet. Go talk to some NPCs!");
            return;
        }

        // Gender filter dropdown
        var genderLabels = new[] { "All Genders", "Male", "Female", "Unknown" };
        var genderValues = new[] { NpcGender.Unknown, NpcGender.Male, NpcGender.Female, (NpcGender)99 };
        var filterIdx = _npcVoiceFilterIndex;
        ImGui.Text("Filter:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##npcgenderfilter", ref filterIdx, genderLabels, genderLabels.Length))
        {
            _npcVoiceFilterIndex = filterIdx;
        }
        var filterGender = genderValues[_npcVoiceFilterIndex];

        // Get cached voice list
        var cachedVoices = isAzure ? _cachedAzureVoices : _cachedPlayer2Voices;
        if (cachedVoices is null or { Count: 0 })
        {
            if (ImGui.Button("Load Voice List")) _ = FetchVoicesAsync();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "Click 'Load Voice List' to see available voices.");
            return;
        }

        // Filter voices to English only
        var enVoices = cachedVoices
            .Where(v => v.RawLanguage == "american_english" || v.RawLanguage == "british_english")
            .ToList();
        if (enVoices.Count == 0) enVoices = cachedVoices;

        ImGui.Spacing();
        ImGui.Separator();

        // Scrollable NPC list area
        ImGui.BeginChild("##npcvoicescroll", new Vector2(0, -45), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);

        // Build sorted list of NPC assignments
        var npcList = new List<(string Name, string VoiceId, NpcGender Gender)>();
        foreach (var (npcName, voiceId) in assignments)
        {
            var gender = _plugin.GetNpcGender(npcName);
            npcList.Add((npcName, voiceId, gender));
        }
        npcList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var anyShown = false;
        foreach (var (npcName, voiceId, gender) in npcList)
        {
            // Apply gender filter
            if (filterGender != NpcGender.Unknown)
            {
                var target = filterGender == (NpcGender)99 ? NpcGender.Unknown : filterGender;
                if (gender != target) continue;
            }

            anyShown = true;

            // Gender-colored label
            var genderLabel = gender switch { NpcGender.Male => "[M]", NpcGender.Female => "[F]", _ => "[?]" };
            var genderColor = gender switch
            {
                NpcGender.Male => new Vector4(0.4f, 0.7f, 1, 1),
                NpcGender.Female => new Vector4(1, 0.5f, 0.7f, 1),
                _ => new Vector4(0.6f, 0.6f, 0.6f, 1)
            };

            ImGui.TextColored(genderColor, $"{genderLabel} {npcName}");

            // Voice dropdown filtered by NPC gender
            var voicesForNpc = gender switch
            {
                NpcGender.Male => enVoices.Where(v => string.Equals(v.Gender, "male", StringComparison.OrdinalIgnoreCase)).ToList(),
                NpcGender.Female => enVoices.Where(v => string.Equals(v.Gender, "female", StringComparison.OrdinalIgnoreCase)
                                                        && (!isAzure || !string.Equals(v.Id, "en-US-AnaNeural", StringComparison.OrdinalIgnoreCase))).ToList(),
                _ => enVoices
            };
            if (voicesForNpc.Count == 0) voicesForNpc = enVoices;

            ImGui.SameLine(200);
            ImGui.SetNextItemWidth(220);
            var currentVoice = voiceId;
            var currentDisplay = voicesForNpc.FirstOrDefault(v => v.Id == currentVoice)?.DisplayName ?? currentVoice;
            if (ImGui.BeginCombo($"##npcvoice_{npcName}", currentDisplay))
            {
                foreach (var v in voicesForNpc)
                {
                    var isSelected = v.Id == currentVoice;
                    if (ImGui.Selectable(v.DisplayName, isSelected))
                    {
                        assignments[npcName] = v.Id;
                        _configuration.Save();
                    }
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Test button
            ImGui.SameLine();
            if (ImGui.SmallButton($"Test##npctest_{npcName}"))
            {
                _ = _plugin.ActiveTtsService.SpeakAsync($"Hello, I am {npcName}.", voice: assignments[npcName]);
            }

            // Remove button
            ImGui.SameLine();
            if (ImGui.SmallButton($"X##npcremove_{npcName}"))
            {
                assignments.Remove(npcName);
                _configuration.Save();
            }
        }

        ImGui.EndChild();

        if (!anyShown)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No NPCs match the selected gender filter.");
        }

        ImGui.Spacing();
        ImGui.Separator();

        ImGui.Text($"Total: {assignments.Count} NPCs assigned");
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset All Assignments"))
        {
            assignments.Clear();
            _configuration.Save();
        }
    }

    private int _npcVoiceFilterIndex;

    private void DrawChannelsTab()
    {
        ImGui.Separator(); ImGui.Text("Chat Channels to Read Aloud");
        ImGui.TextWrapped("Select which chat channels should be read via TTS.");

        ImGui.Spacing();

        CheckboxConfig("Say", _configuration.ReadSay, v => _configuration.ReadSay = v);
        CheckboxConfig("Party", _configuration.ReadParty, v => _configuration.ReadParty = v);
        CheckboxConfig("Alliance", _configuration.ReadAlliance, v => _configuration.ReadAlliance = v);
        CheckboxConfig("Free Company", _configuration.ReadFreeCompany, v => _configuration.ReadFreeCompany = v);
        CheckboxConfig("Tell / Whisper", _configuration.ReadTell, v => _configuration.ReadTell = v);
        CheckboxConfig("Yell", _configuration.ReadYell, v => _configuration.ReadYell = v);
        CheckboxConfig("Shout", _configuration.ReadShout, v => _configuration.ReadShout = v);

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("Linkshells");
        CheckboxConfig("Linkshell 1", _configuration.ReadLinkshell1, v => _configuration.ReadLinkshell1 = v);
        CheckboxConfig("Linkshell 2", _configuration.ReadLinkshell2, v => _configuration.ReadLinkshell2 = v);
        CheckboxConfig("Linkshell 3", _configuration.ReadLinkshell3, v => _configuration.ReadLinkshell3 = v);
        CheckboxConfig("Linkshell 4", _configuration.ReadLinkshell4, v => _configuration.ReadLinkshell4 = v);
        CheckboxConfig("Linkshell 5", _configuration.ReadLinkshell5, v => _configuration.ReadLinkshell5 = v);
        CheckboxConfig("Linkshell 6", _configuration.ReadLinkshell6, v => _configuration.ReadLinkshell6 = v);
        CheckboxConfig("Linkshell 7", _configuration.ReadLinkshell7, v => _configuration.ReadLinkshell7 = v);
        CheckboxConfig("Linkshell 8", _configuration.ReadLinkshell8, v => _configuration.ReadLinkshell8 = v);

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("Other");
        CheckboxConfig("Novice Network", _configuration.ReadNoviceNetwork, v => _configuration.ReadNoviceNetwork = v);
        CheckboxConfig("Emotes", _configuration.ReadEmote, v => _configuration.ReadEmote = v);
        CheckboxConfig("System Messages", _configuration.ReadSystemMessage, v => _configuration.ReadSystemMessage = v);

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("NPC Dialog");
        ImGui.TextWrapped("Read NPC dialogue bubbles aloud.");
        CheckboxConfig("NPC Talk (dialog bubbles)", _configuration.ReadNpcTalk, v => _configuration.ReadNpcTalk = v);
        CheckboxConfig("NPC Battle Talk", _configuration.ReadNpcBattleTalk, v => _configuration.ReadNpcBattleTalk = v);
        ImGui.Spacing();
        CheckboxConfig("Skip TTS during cutscenes (voiced)", _configuration.SkipTtsDuringCutscenes, v => _configuration.SkipTtsDuringCutscenes = v);
        ImGuiExt.HelpMarker("When enabled, TTS will NOT read dialogue during voiced cutscenes.\nFFXIV cutscenes already have voice acting, so TTS would be redundant.\nRegular NPC dialogue outside of cutscenes is still read normally.");
        ImGui.Spacing();
        CheckboxConfig("Say speaker name before dialogue", _configuration.IncludeNpcSpeakerName, v => _configuration.IncludeNpcSpeakerName = v);
        ImGuiExt.HelpMarker("When enabled, the NPC's name is spoken before their dialogue.\nThe name is only said when the speaker changes (not repeated for consecutive lines).\nExample: \"Alphinaud says: We must hurry!\"");
        ImGui.Spacing();
        CheckboxConfig("Auto-advance NPC dialog", _configuration.AutoAdvanceNpcDialog, v => _configuration.AutoAdvanceNpcDialog = v);
        ImGuiExt.HelpMarker("Automatically presses Confirm after TTS finishes speaking each line.\nTiming is based on word count and WPM.");
        if (_configuration.AutoAdvanceNpcDialog)
        {
            var wpm = _configuration.AutoAdvanceWpm;
            ImGui.Text("Speech rate (WPM):");
            ImGui.SameLine();
            if (ImGui.SliderInt("##wpm", ref wpm, 80, 300))
            {
                _configuration.AutoAdvanceWpm = wpm;
                _configuration.Save();
            }
            ImGuiExt.HelpMarker("Words per minute. Default 160.\nLower = longer wait before advancing.\nMatch this to how fast your TTS engine speaks.");
        }
    }

    private void CheckboxConfig(string label, bool current, Action<bool> setter)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            setter(value);
            _configuration.Save();
        }
    }

    private void DrawVoiceOverridesTab()
    {
        ImGui.Separator(); ImGui.Text("Per-Channel Voice Overrides");
        ImGui.TextWrapped("Set different voices for different chat channels. Leave blank to use the default voice.");

        ImGui.Spacing();

        var channels = new Dictionary<string, string>
        {
            { "say", "Say" },
            { "party", "Party" },
            { "alliance", "Alliance" },
            { "yell", "Yell" },
            { "shout", "Shout" },
            { "freecompany", "Free Company" },
            { "tell", "Tell / Whisper" },
            { "linkshell1", "Linkshell 1" },
            { "linkshell2", "Linkshell 2" },
            { "linkshell3", "Linkshell 3" },
            { "linkshell4", "Linkshell 4" },
            { "linkshell5", "Linkshell 5" },
            { "linkshell6", "Linkshell 6" },
            { "linkshell7", "Linkshell 7" },
            { "linkshell8", "Linkshell 8" },
            { "novicenetwork", "Novice Network" },
            { "emote", "Emotes" },
            { "system", "System Messages" }
        };

        foreach (var (key, label) in channels)
        {
            var currentVoice = _configuration.ChannelVoiceOverrides.GetValueOrDefault(key, string.Empty);
            ImGui.Text($"{label}:");
            ImGui.SameLine(150);
            if (ImGui.InputText($"##voice_{key}", ref currentVoice, 128))
            {
                if (string.IsNullOrWhiteSpace(currentVoice))
                    _configuration.ChannelVoiceOverrides.Remove(key);
                else
                    _configuration.ChannelVoiceOverrides[key] = currentVoice;
                _configuration.Save();
            }
        }
    }

    private void DrawVoicePresetsTab()
    {
        var isAzure = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure;

        ImGui.Separator(); ImGui.Text("Voice Presets");
        ImGui.TextWrapped(isAzure
            ? "Choose Azure neural voices for different speaker types."
            : "Choose voices for different speaker types. Fetched from Player2.");

        var useGendered = isAzure ? _configuration.AzureUseGenderedVoices : _configuration.UseGenderedVoices;
        if (ImGui.Checkbox("Use gendered voices for NPCs", ref useGendered))
        {
            if (isAzure) _configuration.AzureUseGenderedVoices = useGendered;
            else _configuration.UseGenderedVoices = useGendered;
            _configuration.Save();
        }

        var usePerNpc = isAzure ? _configuration.AzureUsePerNpcVoices : _configuration.UsePerNpcVoices;
        if (ImGui.Checkbox("Assign unique voice to each NPC", ref usePerNpc))
        {
            if (isAzure) _configuration.AzureUsePerNpcVoices = usePerNpc;
            else _configuration.UsePerNpcVoices = usePerNpc;
            _configuration.Save();
        }
        ImGuiExt.HelpMarker("Each NPC gets a random English voice from their gender pool.\nAssignments persist across sessions.");

        var assignments = isAzure ? _configuration.AzureNpcVoiceAssignments : _configuration.NpcVoiceAssignments;
        if (assignments.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset All"))
            {
                assignments.Clear();
                _configuration.Save();
            }
            ImGui.Text($"({assignments.Count} NPC voices assigned)");
        }

        if (!_voicesFetched && (isAzure ? _cachedAzureVoices : _cachedPlayer2Voices) is null)
            _ = FetchVoicesAsync();

        if (ImGui.Button("Refresh Voice List")) _ = FetchVoicesAsync();

        ImGui.Spacing();

        var cachedVoices = isAzure ? _cachedAzureVoices : _cachedPlayer2Voices;

        if (cachedVoices is { Count: > 0 })
        {
            // Filter to English only (US + UK)
            var enVoices = cachedVoices
                .Where(v => v.RawLanguage == "american_english" || v.RawLanguage == "british_english")
                .ToList();
            if (enVoices.Count == 0) enVoices = cachedVoices;

            var maleVoices = enVoices
                .Where(v => string.Equals(v.Gender, "male", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var femaleVoices = enVoices
                .Where(v => string.Equals(v.Gender, "female", StringComparison.OrdinalIgnoreCase)
                            && (!isAzure || !string.Equals(v.Id, "en-US-AnaNeural", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            ImGui.Text($"Loaded {enVoices.Count} English voices ({maleVoices.Count} male, {femaleVoices.Count} female)");

            var unisexId = isAzure ? _configuration.AzureUnisexVoice : _configuration.UnisexVoiceId;
            var maleId = isAzure ? _configuration.AzureMaleVoice : _configuration.MaleVoiceId;
            var femaleId = isAzure ? _configuration.AzureFemaleVoice : _configuration.FemaleVoiceId;

            DrawVoiceCombo("Default / Unisex", "##unisex", unisexId,
                enVoices, id =>
                {
                    if (isAzure) { _configuration.AzureUnisexVoice = id; _configuration.AzureDefaultVoice = id; }
                    else _configuration.UnisexVoiceId = id;
                    _configuration.Save();
                });
            ImGui.SameLine();
            if (ImGui.SmallButton("Test##unisexTest"))
                _ = _plugin.ActiveTtsService.SpeakAsync("Hello, this is the unisex voice.", voice: unisexId);

            DrawVoiceCombo("Male Voice", "##male", maleId,
                maleVoices.Count > 0 ? maleVoices : enVoices,
                id =>
                {
                    if (isAzure) _configuration.AzureMaleVoice = id;
                    else _configuration.MaleVoiceId = id;
                    _configuration.Save();
                });
            ImGui.SameLine();
            if (ImGui.SmallButton("Test##maleTest"))
                _ = _plugin.ActiveTtsService.SpeakAsync("Hello, this is the male voice.", voice: maleId);

            DrawVoiceCombo("Female Voice", "##female", femaleId,
                femaleVoices.Count > 0 ? femaleVoices : enVoices,
                id =>
                {
                    if (isAzure) _configuration.AzureFemaleVoice = id;
                    else _configuration.FemaleVoiceId = id;
                    _configuration.Save();
                });
            ImGui.SameLine();
            if (ImGui.SmallButton("Test##femaleTest"))
                _ = _plugin.ActiveTtsService.SpeakAsync("Hello, this is the female voice.", voice: femaleId);
        }
        else if (_voicesFetched)
        {
            var msg = isAzure
                ? "No voices found. Check your Azure credentials."
                : "No voices found. Is Player2 running?";
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), msg);
        }
        else
        {
            ImGui.Text("Fetching voices...");
        }
    }

    private void DrawVoiceCombo(string label, string id, string currentId,
        List<VoiceInfo> voices, Action<string> onSelect)
    {
        ImGui.Text(label + ":");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(250);

        var currentName = voices.FirstOrDefault(v => v.Id == currentId)?.DisplayName ?? "Select...";
        if (ImGui.BeginCombo(id, currentName, ImGuiComboFlags.HeightLarge))
        {
            foreach (var v in voices)
            {
                var isSelected = v.Id == currentId;
                if (ImGui.Selectable(v.DisplayName, isSelected))
                    onSelect(v.Id);
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    private async System.Threading.Tasks.Task FetchVoicesAsync()
    {
        _voicesFetched = false;
        try
        {
            var raw = await _plugin.ActiveTtsService.GetAvailableVoicesRawAsync();
            if (_configuration.ActiveEngine == TtsEngine.MicrosoftAzure)
                _cachedAzureVoices = raw;
            else
                _cachedPlayer2Voices = raw;
            _voicesFetched = true;
        }
        catch
        {
            _voicesFetched = true;
            if (_configuration.ActiveEngine == TtsEngine.MicrosoftAzure)
                _cachedAzureVoices = null;
            else
                _cachedPlayer2Voices = null;
        }
    }

    private void DrawAdvancedTab()
    {
        ImGui.Separator(); ImGui.Text("Advanced Settings");

        var skipDupes = _configuration.SkipDuplicateMessages;
        if (ImGui.Checkbox("Skip duplicate messages (within 2s)", ref skipDupes))
        {
            _configuration.SkipDuplicateMessages = skipDupes;
            _configuration.Save();
        }

        var timeout = _configuration.HttpTimeoutSeconds;
        ImGui.Text("HTTP Timeout (seconds):");
        ImGui.SameLine();
        if (ImGui.SliderInt("##timeout", ref timeout, 1, 60))
        {
            _configuration.HttpTimeoutSeconds = timeout;
            _configuration.Save();
        }

        var debugLog = _configuration.DebugLogging;
        if (ImGui.Checkbox("Debug logging", ref debugLog))
        {
            _configuration.DebugLogging = debugLog;
            _configuration.Save();
        }
        ImGuiExt.HelpMarker("Enable verbose debug output in /xllog. Useful for troubleshooting.");

        ImGui.Spacing();
        ImGui.Separator(); ImGui.Text("About");

        ImGui.TextWrapped("FF14 Player2 TTS v1.0.0");
        ImGui.TextWrapped("Connects FFXIV chat to Player2 or Microsoft Azure TTS for text-to-speech.");
    }

    private async System.Threading.Tasks.Task TestConnectionAsync()
    {
        var engineName = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure ? "Azure" : "Player2";
        PrintChat($"Testing {engineName} connection...");
        var available = await _plugin.ActiveTtsService.IsServerAvailableAsync();
        if (available)
        {
            PrintChat($"{engineName} TTS is ONLINE!");
        }
        else
        {
            PrintChat($"{engineName} TTS is OFFLINE. Check your settings.");
        }
    }

    private void PrintChat(string message)
    {
        Plugin.ChatGui.Print($"[FF14P2TTS] {message}");
    }
}

/// <summary>
/// Helper for ImGui marker tooltips.
/// </summary>
internal static class ImGuiExt
{
    public static void HelpMarker(string desc)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
