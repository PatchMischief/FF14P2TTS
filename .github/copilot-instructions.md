# Copilot instructions for FF14P2TTS

## Session Workflow (always on)

- **Graphify first.** For any question about this repo's architecture, structure, or component relationships, run `python -m graphify query "<question>"` (or `python -m graphify path "A" "B"` / `python -m graphify explain "X"`) when `graphify-out/graph.json` exists, and prefer that scoped subgraph over reading source files.
- **Auto-use skills.** Match each task to a loaded skill and follow it (`ff14p2tts-dev-loop`, `graphify`, `run-tests`, `writing-mstest-tests`, MSBuild skills).
- **Change loop.** After every change: increment the `<Version>` patch in `FF14P2TTS.csproj` by 1, run `dotnet build FF14P2TTS.csproj -c Release`, then run `python -m graphify update .` (add `--force` after upgrading graphify itself).

## Project overview

FF14P2TTS is a Dalamud plugin for Final Fantasy XIV that reads chat and NPC dialogue aloud. It targets `net10.0-windows`, uses Dalamud API level 15, and supports two interchangeable TTS backends:

- **Player2**: a local HTTP service, normally at `http://127.0.0.1:4315`.
- **Microsoft Azure**: Azure Cognitive Services Speech, synthesized through the Speech SDK and played locally as WAV audio.

The plugin is configured in-game with `/p2tts` and stores settings through Dalamud's plugin configuration system. Do not treat `FF14P2TTS.json` as runtime configuration; it is the Dalamud plugin manifest.

## Build, test, and lint

Run commands from the repository root:

```powershell
# Restore/build the plugin
dotnet build FF14P2TTS.csproj

# Build the distributable configuration
dotnet build FF14P2TTS.csproj --configuration Release
```

There is no solution file, test project, or configured lint command in this repository. There are currently no automated tests to run individually. For a manual smoke test in-game, use:

```text
/p2tts status
/p2tts test Hello, Eorzea!
```

The first command checks the selected engine and lists voices when available; the second sends a sample utterance through the active backend.

## Architecture

- [`FF14P2TTS.cs`](../FF14P2TTS.cs) contains the `Plugin` entry point and composition root. It obtains Dalamud services through `[PluginService]`, loads/migrates [`Configuration.cs`](../Configuration.cs), creates both TTS services, registers chat/addon/UI callbacks, and owns disposal.
- `Plugin.ActiveTtsService` selects the backend at runtime from `Configuration.ActiveEngine`; both services remain initialized so switching engines does not require plugin reload.
- [`ITtsService.cs`](../ITtsService.cs) is the shared backend contract: availability, voice enumeration, stopping speech, and speaking with optional voice/speed/pitch/volume overrides.
- [`Player2TtsService.cs`](../Player2TtsService.cs) serializes requests and talks to the local Player2 `/v1/health`, `/v1/tts/voices`, `/v1/tts/speak`, and `/v1/tts/stop` endpoints. It sanitizes FFXIV text and suppresses duplicate utterances.
- [`AzureTtsService.cs`](../AzureTtsService.cs) uses the Azure Speech SDK, maintains a shared `SpeechConfig`, creates a synthesizer per utterance, splits long text into sentence-sized chunks, and plays/scales returned WAV PCM data on Windows.
- Chat messages flow from `ChatGui.ChatMessageUnhandled` into `Plugin.OnChatMessage`: global enable/PvP/self-message checks and channel filters run first, then speaker formatting and per-channel voice overrides are applied before fire-and-forget synthesis.
- [`NpcTalkHandler.cs`](../NpcTalkHandler.cs) observes `Talk` and `BattleTalk` addon updates, extracts FFXIV `AtkTextNode` text, and emits only changed lines because updates occur every frame. [`DialogueTester.cs`](../DialogueTester.cs) tracks `TalkSubtitle` visibility so voiced native subtitles can be skipped line-by-line.
- `Plugin.OnNpcTalk` applies cutscene/subtitle suppression, speaker-name changes, gender detection, and [`NpcVoiceMapper.cs`](../NpcVoiceMapper.cs)'s persisted per-NPC voice assignment. Voice lists are fetched asynchronously and cached for NPC mapping.
- [`AutoAdvanceHandler.cs`](../AutoAdvanceHandler.cs) optionally estimates reading duration from words-per-minute and sends a numpad-0 confirmation key event, except during cutscenes or choice dialogs.
- [`Windows/ConfigWindow.cs`](../Windows/ConfigWindow.cs) exposes engine, credentials/endpoints, voice presets, NPC assignments, channel filters, overrides, and advanced settings. [`Windows/MainWindow.cs`](../Windows/MainWindow.cs) provides quick controls and status/testing UI.

## Repository-specific conventions

- Keep backend-specific behavior behind `ITtsService`; code handling chat/NPC events should call `ActiveTtsService` rather than directly coupling to Player2 or Azure.
- Configuration is the source of truth for behavior and must be persisted with `Configuration.Save()` after UI, command, or migration changes. Increment and migrate `Configuration.Version` when changing the serialized schema.
- Player2 voice values are UUIDs; Azure voice values are names such as `en-US-AriaNeural`. Preserve this distinction in UI labels, mapping, and serialized fields.
- NPC voice assignment is intentionally persisted by NPC name in separate Player2/Azure dictionaries. Gender pools are shuffled with per-engine/per-gender queues, with a fallback to all available voices when a pool is empty.
- Dalamud addon callbacks run frequently and may expose incomplete native data. Follow the existing visibility/null checks and deduplication pattern when reading `AtkUnitBase` or `AtkTextNode` structures.
- Chat and addon callbacks must not block the game pipeline: speech, status checks, and voice refreshes are launched asynchronously. Keep cancellation/error logging consistent with each service's existing pattern.
- Preserve FFXIV text cleanup before synthesis. Player2 strips SeString payload markers and Azure performs its own equivalent sanitization; do not pass raw payload/control text to a TTS API.
- Use Dalamud's ImGui bindings and `WindowSystem` for UI. Configuration edits should update the model and save immediately, matching the existing window patterns.
- Register every event/listener/command in the plugin constructor and unregister it in `Dispose`; native addon lifecycle listeners and chat callbacks are especially important to avoid duplicate processing after reload.
- Avoid logging secrets such as the Azure subscription key. Existing service logs may include synthesized text and request details, so keep new diagnostic logging gated or appropriately sanitized.
