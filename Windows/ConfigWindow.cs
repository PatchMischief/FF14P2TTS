using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
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
    private List<VoiceInfo>? _cachedSpeechifyVoices;
    private bool _voicesFetched;
    private string _npcGenderOverrideName = string.Empty;
    private int _npcGenderOverrideIndex;

    public ConfigWindow(Plugin plugin) : base(
        "FF14 TTS Configuration###FF14TTSConfig",
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
        var engineNames = new[]
        {
            "Player2 (Local)",
            "Microsoft Azure (Cloud)",
            "ElevenLabs (Cloud)",
            "Speechify (Cloud)",
        };
        var currentEngineIndex = (int)_configuration.ActiveEngine;
        ImGui.Text("TTS Engine:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(250);
        if (ImGui.Combo("##engine", ref currentEngineIndex, engineNames, engineNames.Length))
        {
            _configuration.ActiveEngine = (TtsEngine)Math.Clamp(currentEngineIndex, 0, engineNames.Length - 1);
            _configuration.Save();
            // Clear cached voices when switching engine
            _cachedPlayer2Voices = null;
            _cachedAzureVoices = null;
            _cachedSpeechifyVoices = null;
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
        else if (_configuration.ActiveEngine == TtsEngine.MicrosoftAzure)
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

            ImGui.Spacing();
            var azUseEmotion = _configuration.AzureUseEmotionTagging;
            if (ImGui.Checkbox("Use Groq emotion tagging (Azure styles)", ref azUseEmotion))
            {
                _configuration.AzureUseEmotionTagging = azUseEmotion;
                _configuration.Save();
            }
            ImGuiExt.HelpMarker("Classifies each line with an Azure mstts:express-as style using Groq before synthesis. Requires a Groq API key.");

            var groqKey = _configuration.GroqApiKey;
            ImGui.Text("Groq API Key:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##azgroqkey", ref groqKey, 256))
            {
                _configuration.GroqApiKey = groqKey;
                _configuration.Save();
            }

            var groqModel = _configuration.GroqModel;
            ImGui.Text("Groq Model:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##azgroqmodel", ref groqModel, 128))
            {
                _configuration.GroqModel = groqModel;
                _configuration.Save();
            }

            ImGui.SameLine();
            if (ImGui.Button("Test Connection"))
            {
                _ = TestConnectionAsync();
            }
        }
        else if (_configuration.ActiveEngine == TtsEngine.ElevenLabs)
        {
            ImGui.Separator(); ImGui.Text("ElevenLabs Connection");
            var apiKey = _configuration.ElevenLabsApiKey;
            ImGui.Text("API Key:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##elevenkey", ref apiKey, 256))
            {
                _configuration.ElevenLabsApiKey = apiKey;
                _configuration.Save();
            }

            var baseUrl = _configuration.ElevenLabsBaseUrl;
            ImGui.Text("API URL:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##elevenurl", ref baseUrl, 256))
            {
                _configuration.ElevenLabsBaseUrl = baseUrl;
                _configuration.Save();
            }

            ImGui.TextWrapped("ElevenLabs provider wiring is ready; synthesis and voice loading will be added in a later integration.");
        }
        else
        {
            ImGui.Separator(); ImGui.Text("Speechify Connection");
            var apiKey = _configuration.SpeechifyApiKey;
            ImGui.Text("API Key:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##speechifykey", ref apiKey, 256))
            {
                _configuration.SpeechifyApiKey = apiKey;
                _configuration.Save();
            }
            var baseUrl = _configuration.SpeechifyBaseUrl;
            ImGui.Text("API URL:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##speechifyurl", ref baseUrl, 256))
            {
                _configuration.SpeechifyBaseUrl = baseUrl;
                _configuration.Save();
            }

            ImGui.Spacing();
            var useEmotion = _configuration.SpeechifyUseEmotionTagging;
            if (ImGui.Checkbox("Use Groq emotion tagging", ref useEmotion))
            {
                _configuration.SpeechifyUseEmotionTagging = useEmotion;
                _configuration.Save();
            }
            ImGuiExt.HelpMarker("Tags each line with a Speechify emotion using Groq before synthesis. Requires a Groq API key.");

            var groqKey = _configuration.GroqApiKey;
            ImGui.Text("Groq API Key:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##groqkey", ref groqKey, 256))
            {
                _configuration.GroqApiKey = groqKey;
                _configuration.Save();
            }

            var groqModel = _configuration.GroqModel;
            ImGui.Text("Groq Model:");
            ImGui.SetNextItemWidth(350);
            if (ImGui.InputText("##groqmodel", ref groqModel, 128))
            {
                _configuration.GroqModel = groqModel;
                _configuration.Save();
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
            ImGuiExt.HelpMarker("Player2 voice UUID. Leave blank to use preset-based voices.\nUse /ff14tts status to see available voices.");
        }
        else if (_configuration.ActiveEngine == TtsEngine.MicrosoftAzure)
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
        else if (_configuration.ActiveEngine == TtsEngine.ElevenLabs)
        {
            var voice = _configuration.ElevenLabsDefaultVoice;
            ImGui.Text("ElevenLabs Voice ID:");
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("##elevenvoice", ref voice, 128))
            {
                _configuration.ElevenLabsDefaultVoice = voice;
                _configuration.Save();
            }
        }
        else
        {
            var voice = _configuration.SpeechifyDefaultVoice;
            ImGui.Text("Speechify Voice ID:");
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("##speechifyvoice", ref voice, 128))
            {
                _configuration.SpeechifyDefaultVoice = voice;
                _configuration.Save();
            }
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
        ImGui.Separator(); ImGui.Text("NPC Dialog");

        CheckboxConfig("NPC Talk (dialog bubbles)", _configuration.ReadNpcTalk, v => _configuration.ReadNpcTalk = v);
        CheckboxConfig("NPC Battle Talk", _configuration.ReadNpcBattleTalk, v => _configuration.ReadNpcBattleTalk = v);
        CheckboxConfig(
            "Skip TTS during cutscenes (voiced)",
            _configuration.SkipTtsDuringCutscenes,
            v => _configuration.SkipTtsDuringCutscenes = v);
        ImGuiExt.HelpMarker("When enabled, TTS will NOT read dialogue during voiced cutscenes.\nFFXIV cutscenes already have voice acting, so TTS would be redundant.\nRegular NPC dialogue outside of cutscenes is still read normally.");
        ImGui.Spacing();
        CheckboxConfig(
            "Say speaker name before dialogue",
            _configuration.IncludeNpcSpeakerName,
            v => _configuration.IncludeNpcSpeakerName = v);
        ImGuiExt.HelpMarker("When enabled, the NPC's name is spoken before their dialogue.\nThe name is only said when the speaker changes (not repeated for consecutive lines).");

        ImGui.Spacing();
        if (ImGui.Button("Test TTS", new Vector2(120, 30)))
        {
            var engineLabel = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure ? "Azure" : "Player2";
            _ = _plugin.ActiveTtsService.SpeakAsync(
                $"Hello, this is a test message from the {engineLabel} TTS engine.");
            PrintChat($"Test message sent to {engineLabel} TTS.");
        }
    }

    private void DrawNpcVoicesTab()
    {
        var isAzure = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure;
        var assignments = isAzure
            ? _configuration.AzureNpcVoiceAssignments
            : _configuration.ActiveEngine == TtsEngine.ElevenLabs
                ? _configuration.ElevenLabsNpcVoiceAssignments
                : _configuration.ActiveEngine == TtsEngine.Speechify
                    ? _configuration.SpeechifyNpcVoiceAssignments
                : _configuration.NpcVoiceAssignments;

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
        var cachedVoices = isAzure
            ? _cachedAzureVoices
            : _configuration.ActiveEngine == TtsEngine.Speechify
                ? _cachedSpeechifyVoices
                : _cachedPlayer2Voices;
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
                NpcGender.Male => enVoices
                    .Where(v => string.Equals(v.Gender, "male", StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                NpcGender.Female => enVoices
                    .Where(v => string.Equals(v.Gender, "female", StringComparison.OrdinalIgnoreCase)
                        && (!isAzure || !string.Equals(v.Id, "en-US-AnaNeural", StringComparison.OrdinalIgnoreCase)))
                    .ToList(),
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
                _plugin.ForgetNpc(npcName);
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
            var npcNames = assignments.Keys.ToList();
            assignments.Clear();
            foreach (var npcName in npcNames)
                _plugin.ForgetNpc(npcName);
            _configuration.Save();
        }
    }

    private int _npcVoiceFilterIndex;

    private void CheckboxConfig(string label, bool current, Action<bool> setter)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            setter(value);
            _configuration.Save();
        }
    }

    private bool GetUsePerNpcVoices(bool isSpeechify, bool isAzure)
    {
        if (isSpeechify) return _configuration.SpeechifyUsePerNpcVoices;
        if (isAzure) return _configuration.AzureUsePerNpcVoices;
        return _configuration.UsePerNpcVoices;
    }

    private string GetUnisexVoiceId(bool isSpeechify, bool isAzure)
    {
        if (isSpeechify) return _configuration.SpeechifyDefaultVoice;
        if (isAzure) return _configuration.AzureUnisexVoice;
        return _configuration.UnisexVoiceId;
    }

    private string GetMaleVoiceId(bool isSpeechify, bool isAzure)
    {
        if (isSpeechify) return _configuration.SpeechifyMaleVoice;
        if (isAzure) return _configuration.AzureMaleVoice;
        return _configuration.MaleVoiceId;
    }

    private string GetFemaleVoiceId(bool isSpeechify, bool isAzure)
    {
        if (isSpeechify) return _configuration.SpeechifyFemaleVoice;
        if (isAzure) return _configuration.AzureFemaleVoice;
        return _configuration.FemaleVoiceId;
    }

    private void DrawVoicePresetsTab()
    {
        var isAzure = _configuration.ActiveEngine == TtsEngine.MicrosoftAzure;
        var isSpeechify = _configuration.ActiveEngine == TtsEngine.Speechify;
        if (_configuration.ActiveEngine == TtsEngine.ElevenLabs)
        {
            ImGui.TextWrapped("ElevenLabs voice presets are reserved for the upcoming provider integration.");
            return;
        }

        ImGui.Separator(); ImGui.Text("Voice Presets");
        ImGui.TextWrapped(isAzure
            ? "Choose Azure neural voices for different speaker types."
            : isSpeechify
                ? "Choose Speechify voices for different speaker types. Fetched from your Speechify workspace."
                : "Choose voices for different speaker types. Fetched from Player2.");

        if (!isSpeechify)
        {
            var useGendered = isAzure ? _configuration.AzureUseGenderedVoices : _configuration.UseGenderedVoices;
            if (ImGui.Checkbox("Use gendered voices for NPCs", ref useGendered))
            {
                if (isAzure) _configuration.AzureUseGenderedVoices = useGendered;
                else _configuration.UseGenderedVoices = useGendered;
                _configuration.Save();
            }
        }

        var usePerNpc = GetUsePerNpcVoices(isSpeechify, isAzure);
        if (ImGui.Checkbox("Assign unique voice to each NPC", ref usePerNpc))
        {
            if (isSpeechify) _configuration.SpeechifyUsePerNpcVoices = usePerNpc;
            else if (isAzure) _configuration.AzureUsePerNpcVoices = usePerNpc;
            else _configuration.UsePerNpcVoices = usePerNpc;
            _configuration.Save();
        }
        ImGuiExt.HelpMarker("Each NPC gets a random English voice from their gender pool.\nAssignments persist across sessions.");

        var assignments = isSpeechify
            ? _configuration.SpeechifyNpcVoiceAssignments
            : isAzure ? _configuration.AzureNpcVoiceAssignments : _configuration.NpcVoiceAssignments;
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

        var cachedVoices = isSpeechify
            ? _cachedSpeechifyVoices
            : isAzure ? _cachedAzureVoices : _cachedPlayer2Voices;
        if (!_voicesFetched && cachedVoices is null)
            _ = FetchVoicesAsync();

        if (ImGui.Button("Refresh Voice List")) _ = FetchVoicesAsync();

        ImGui.Spacing();

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

            ImGui.Text(isSpeechify
                ? $"Loaded {enVoices.Count} Speechify voices ({maleVoices.Count} male, {femaleVoices.Count} female)"
                : $"Loaded {enVoices.Count} English voices ({maleVoices.Count} male, {femaleVoices.Count} female)");

            var unisexId = GetUnisexVoiceId(isSpeechify, isAzure);
            var maleId = GetMaleVoiceId(isSpeechify, isAzure);
            var femaleId = GetFemaleVoiceId(isSpeechify, isAzure);

            DrawVoiceCombo("Default / Unisex", "##unisex", unisexId,
                enVoices, id =>
                {
                    if (isSpeechify) _configuration.SpeechifyDefaultVoice = id;
                    else if (isAzure) { _configuration.AzureUnisexVoice = id; _configuration.AzureDefaultVoice = id; }
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
                    if (isSpeechify) _configuration.SpeechifyMaleVoice = id;
                    else if (isAzure) _configuration.AzureMaleVoice = id;
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
                    if (isSpeechify) _configuration.SpeechifyFemaleVoice = id;
                    else if (isAzure) _configuration.AzureFemaleVoice = id;
                    else _configuration.FemaleVoiceId = id;
                    _configuration.Save();
                });
            ImGui.SameLine();
            if (ImGui.SmallButton("Test##femaleTest"))
                _ = _plugin.ActiveTtsService.SpeakAsync("Hello, this is the female voice.", voice: femaleId);
        }
        else if (_voicesFetched)
        {
            var msg = isSpeechify
                ? "No voices found. Check your Speechify API key."
                : isAzure
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
            else if (_configuration.ActiveEngine == TtsEngine.Speechify)
                _cachedSpeechifyVoices = raw;
            else
                _cachedPlayer2Voices = raw;
            _voicesFetched = true;
        }
        catch
        {
            _voicesFetched = true;
            if (_configuration.ActiveEngine == TtsEngine.MicrosoftAzure)
                _cachedAzureVoices = null;
            else if (_configuration.ActiveEngine == TtsEngine.Speechify)
                _cachedSpeechifyVoices = null;
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

        var informational = typeof(Plugin).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var version = informational is null ? "?" : informational.Split('+')[0];
        ImGui.TextWrapped($"FF14 TTS v{version}");
        ImGui.TextWrapped("Reads FFXIV NPC dialogue aloud via Player2, Microsoft Azure, ElevenLabs, or Speechify.");
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
