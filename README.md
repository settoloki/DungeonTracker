# Dungeon Tracker

**Version 0.0.1 (beta)** — a [Dungeon Helper](https://dungeonhelper.com/) plugin that tracks DDO dungeon runs locally and optionally syncs completions to [DDO Tracker](https://ddotracker.zepsu.com/).

> **Beta notice:** Challenge difficulty (Casual / Normal / Hard / Elite / Reaper) is often reported as **Unknown** with the current Dungeon Helper SDK. Reliable difficulty detection is expected with **Dungeon Helper 5**. Until then, synced difficulty settings only post when a real setting was read (Unknown is not guessed as Elite).

## Features

- **Auto-start** when you enter a dungeon instance (portal + instance quest detection)
- **Live timer** with pause while you briefly leave the instance, resume on re-enter
- **Auto-complete** on the in-game “adventure/quest completed” alert (not on mid-run leave)
- **Stop Tracking** to cancel a run without recording a completion
- **Local history** per character (time, XP, remake tier, challenge setting when known)
- **Quest catalog** for levels / names (bundled + refresh from DDO Tracker when signed in)
- **DDO Tracker cloud sync** — sign in, link website characters, auto-post completions
- **Name canonicalization** so game title casing matches the website catalog (e.g. Murder by Night)
- **In-game overlay** with status, cloud controls, history, and optional debug probe

## Requirements

- Windows x64
- [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) runtime (for building; Dungeon Helper hosts plugins)
- [Dungeon Helper](https://dungeonhelper.com/) with VoK.Sdk-compatible plugin support
- Optional: a [DDO Tracker](https://ddotracker.zepsu.com/) account for cloud sync

## Install (release zip)

1. Close Dungeon Helper.
2. Download `DungeonTracker-0.0.1.zip` from [Releases](https://github.com/settoloki/DungeonTracker/releases).
3. Extract so you have a folder named `DungeonTracker` containing `DungeonTracker.dll`.
4. Copy that folder to:

   `%AppData%\Dungeon Helper\plugins\DungeonTracker`

5. Start Dungeon Helper and enable/load **Dungeon Tracker**.

Do **not** copy someone else’s `ddotracker-settings.json` or `quest-history.json`.

## Build from source

```powershell
dotnet build .\src\DungeonTracker\DungeonTracker.csproj -c Release
```

The project deploys `DungeonTracker.dll`, deps, and `quests.json` into this plugin folder after a successful build.

## Known limitations (0.0.1 beta)

| Area | Status |
|------|--------|
| Challenge difficulty (Hard / Elite / …) | Often **Unknown**; fix targeted for **Dungeon Helper 5** |
| XP-based difficulty inference | Not used as a reliable source (VIP / potions / optional XP) |
| PluginId / PluginKey | Placeholder pending official DH registration for wider distribution |

## Privacy

- Local history and logs stay under the plugin / character folders.
- API sign-in stores a **bearer token** protected with Windows DPAPI (Current User) in `ddotracker-settings.json`.
- Completions sync only when auto-sync is on and a website character is selected.

## License

All rights reserved unless otherwise noted by the author. Provided as beta software — use at your own risk.
