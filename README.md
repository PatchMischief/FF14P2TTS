# FF14P2TTS

A Dalamud plugin for **Final Fantasy XIV** that reads in-game chat and NPC dialogue aloud using text-to-speech. Built for players who want dialogue voiced — including unvoiced cutscenes — or who just prefer listening over reading.

## Features

- Reads **chat messages** aloud (channel filters, self-message/PvP options).
- Reads **NPC dialogue** from the `Talk` and `BattleTalk` addons, including unvoiced cutscene lines.
- **Voiced cutscene skip** — automatically avoids double-speaking lines that are already voiced natively, using wiki detection with a subtitle-visibility fallback.
- **Auto-advance** — optionally sends a numpad-0 confirm key after a line finishes so unvoiced scenes flow like voiced ones.
- **Per-NPC voice assignment** with gender-aware pools, persisted across sessions.
- **Emotion tagging** (optional) — classifies each line with an LLM and applies matching speech styles where the backend supports them.
- Multiple interchangeable TTS engines (see below).

## TTS engines

| Engine | Description | Configuration |
| --- | --- | --- |
| **Player2** | Local HTTP TTS service (default `http://127.0.0.1:4315`) | Base URL only |
| **Microsoft Azure** | Azure AI Speech (neural voices), needs subscription key + region | Key, region, optional endpoint |
| **ElevenLabs** | ElevenLabs speech API | Base URL, API key |
| **Speechify** | Speechify TTS | Base URL, API key |

Voices for Azure, ElevenLabs, and Speechify are fetched from the provider at runtime and cached; Azure also ships a built-in fallback catalog of 123 English neural voices.

## Requirements

- **Final Fantasy XIV** with **[Dalamud](https://github.com/goatcorp/Dalamud)** installed (API level 15).
- **.NET 10 SDK** to build (`net10.0-windows`).
- A TTS backend (one of the engines above). Player2 is the easiest local option; Azure/ElevenLabs/Speechify need their own accounts/keys.

## Building

```powershell
dotnet restore FF14P2TTS.csproj
dotnet build FF14P2TTS.csproj -c Release
```

The distributable plugin package is written to `bin\Release\FF14P2TTS\latest.zip`.

## Installing

Two common ways to load the plugin in-game:

### Option A — Custom plugin repository

1. Build `latest.zip` (above).
2. Host `latest.zip` together with `FF14P2TTS.json` somewhere reachable over HTTPS (GitHub Pages, a raw file URL, etc.).
3. In-game: `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, add your URL.
4. Install **FF14 TTS** from the plugin installer.

### Option B — Dev plugin workflow

1. Build the project.
2. Point Dalamud at the output via its dev-plugin settings, or copy `latest.zip`'s contents into your Dalamud dev plugins folder.
3. Enable the plugin in `/xlplugins`.

## Configuration

Everything is configured in-game:

- `/p2tts` — main configuration window (engine, credentials, voices, NPC assignments, channel filters).
- `/ff14tts` — quick commands and status/testing UI.

Useful commands:

```text
/p2tts status          # show active engine + voices
/p2tts test Hello!     # send a sample line through the active engine
/ff14tts on|off|toggle # enable/disable TTS
```

### Setting up Azure

1. Create a Speech resource in the Azure portal and copy a subscription key.
2. In `/p2tts` → **Azure Connection**, paste the key (use the *Paste* button — Ctrl+V doesn't work in-game) and set the **region** to match your resource (e.g. `eastus`, `westeurope`).
3. Run `/p2tts test Hello, Eorzea!` to verify.

> The built-in voice list uses the `azure_english_voices.json` reference catalog (123 English neural voices). That file is intentionally **not** committed to the repo — voices are fetched live at runtime and the fallback catalog is compiled into the plugin.

## Development

- Entry point / composition root: `FF14P2TTS.cs`
- Backend contract: `Domain/ITtsService.cs`
- Engine implementations: `Infrastructure/Azure`, `Infrastructure/Player2`, `Infrastructure/ElevenLabs`, `Infrastructure/Speechify`
- NPC dialogue pipeline: `Application/NpcDialogueCoordinator.cs`, `NpcTalkHandler.cs`
- UI: `Windows/ConfigWindow.cs`, `Windows/MainWindow.cs`

See `AGENTS.md` and `.github/copilot-instructions.md` for repository conventions and the build/versioning workflow.

## License

[AGPL-3.0-or-later](https://www.gnu.org/licenses/agpl-3.0.html)
