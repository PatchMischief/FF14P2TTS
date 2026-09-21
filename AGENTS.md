# FF14P2TTS Agent Guide

## Session Workflow (always on)

- **Graphify first.** For any question about this repo's architecture, structure, or component relationships, run `python -m graphify query "<question>"` (or `python -m graphify path "A" "B"` / `python -m graphify explain "X"`) when `graphify-out/graph.json` exists, and prefer that scoped subgraph over reading source files.
- **Auto-use skills.** Match each task to a loaded skill and follow it (`ff14p2tts-dev-loop`, `graphify`, `run-tests`, `writing-mstest-tests`, MSBuild skills).
- **Change loop.** After every change: increment the `<Version>` patch in `FF14P2TTS.csproj` by 1, run `dotnet build FF14P2TTS.csproj -c Release`, then run `python -m graphify update .` (add `--force` after upgrading graphify itself).

## Project Shape

- This is a Dalamud plugin targeting `net10.0-windows`, using `Dalamud.NET.Sdk/15.0.0`.
- `FF14P2TTS.cs` is the composition root: it loads configuration, constructs services/coordinators, registers Dalamud callbacks, and disposes them.
- Keep event and memory-reading adapters thin. Put behavior and decisions in `Application/` coordinators and small interfaces.
- `Player2TtsService` and `AzureTtsService` implement `ITtsService`; `Application/TtsServiceProvider` selects the active engine from `Configuration`.
- `NpcTalkHandler` reads FFXIV addon data in unsafe code. Treat game UI node IDs and memory layouts as update-sensitive.
- `Windows/` contains the ImGui configuration and main windows; configuration changes are generally mutable and saved immediately.

## Build And Validation

Run from the repository root:

```powershell
dotnet restore FF14P2TTS.csproj
dotnet build FF14P2TTS.csproj -c Debug
dotnet build FF14P2TTS.csproj -c Release
```

There is currently no solution, test project, or automated test suite. Use a focused build as the minimum validation and manually test behavior in Dalamud/FFXIV when changing runtime integration.

## Conventions

- Use file-scoped namespaces, nullable-aware C#, and the existing small-interface boundaries.
- Preserve the two-engine contract in `ITtsService`. Player2 voice IDs are UUIDs; Azure voice IDs are names such as `en-US-AriaNeural`.
- Keep configuration migrations versioned and update `Configuration.Version` when adding migration logic.
- Use `IPluginLog` for diagnostics with the existing `[FF14P2TTS]` or `[FF14P2TTS-Azure]` prefixes.
- Preserve fire-and-forget call sites only when the service owns exception handling; avoid introducing unobserved exceptions.

## Runtime Pitfalls

- Player2 expects `http://127.0.0.1:4315` by default and exposes `/v1/health`, `/v1/tts/voices`, `/v1/tts/speak`, and `/v1/tts/stop`.
- Azure configuration is Windows-specific. A non-empty region takes precedence over `AzureEndpoint`; Azure service configuration and voice selection are cached, so settings that affect the SDK may require explicit cache/configuration handling.
- NPC talk and battle-talk processing are currently always wired by `NpcTalkHandler`; verify both the UI flags and runtime checks before changing this behavior.
- Auto-advance sends a global `VK_NUMPAD0` key event and pending delays are not automatically cancellable. Changes must consider dialogue replacement, disabling TTS, and plugin disposal.
- NPC voice assignment is persisted synchronously. Avoid adding frequent saves in frame-driven paths.
- Do not edit generated output under `bin/` or `obj/`; change source and rebuild instead.