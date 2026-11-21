# Profiles

Profiles is a BSIPA plugin for Beat Saber 1.46.0. It adds a `Players` button to the main menu and creates a mod-owned SQLite database, `ProfilesMod.sqlite3`, alongside the Beat Saber game instance. Initialization creates the schema and performs a write probe before making the database available; if that fails, the Players button is disabled, the cause is written to the BSIPA log, and (when the game exposes `HMUI.HoverHint`) the cause appears on hover.

## Database schema

The database contains `players`, `playlists`, `playlist_songs`, and `runs` tables. Generated row IDs use canonical UUIDv7 strings; `ProfileDatabase.NewId()` supplies them on .NET Framework 4.7.2, which does not provide a built-in UUIDv7 generator. `song_id` stores Beat Saber's `BeatmapKey.levelId` exactly, including the `custom_level_` prefix for custom songs. `playlist_songs` uses `(playlist_id, song_id)` as its composite key. Runs have a UUIDv7 `id` in addition to the requested fields so each run has a globally unique row identity.

`created` and `when` are stored as ISO 8601 text with an explicit UTC offset, suitable for round-tripping `DateTimeOffset` values (for example, `2026-11-15T21:32:04.5510000+00:00`). `modifiers` is JSON text and defaults to `{}`; `cleared` and `selected` are integer booleans. Foreign keys cascade when a player or playlist is deleted, and partial unique indexes allow at most one selected player and one selected playlist per player.

`runs.combo` is a required integer: a negative value denotes max combo, with its absolute value giving the combo count; a positive value denotes less than max.

The three PNGs in `Resources` are embedded in `ProfilesMod.dll` and used at runtime for the button's normal, highlighted/pressed, and overlay artwork. The sprites preserve their aspect ratio; the cloned menu button's overall size and layout are unchanged.

## Player profiles

Open `Players` from the main menu to create or select a player. The selected name is highlighted; `Play` saves that selection and continues through the game's Solo flow. Players can be deleted only when they have no recorded runs. The player list scrolls when it contains more entries than fit on screen.

Each row shows the number of recorded runs and their maximum `points`, or `new player` when there are no runs. Both cleared and uncleared recorded runs are counted.

In profile Solo, the right screen shows the player and a placeholder for playlists. Its statistics use the run with the highest `score` for the selected song (ties use the lowest run ID); the statistics are gray if the player has no cleared run for that song. An index on `(player_id, song_id, score DESC, id)` supports this lookup. Normal Solo keeps the stock leaderboard.

Profile Solo records completed clears and failures for the player active when the attempt starts; practice, quits, and restarts are not recorded. `mode` is the beatmap characteristic's serialized name, `difficulty` is its serialized difficulty, `score` is the game's modifier-adjusted score and `points` is its multiplied score before modifiers. `modifiers` records every GameplayModifiers field as JSON (enum values are the game's numeric values). Full combo is encoded only for cleared runs; a zero full combo cannot be represented distinctly from zero by the signed combo encoding.

The picker is presented through Beat Saber's main flow coordinator. It clones the configured song-search input and uses the game's global VR keyboard when their required references are available. If no safe native input or text-entry screen is available, name entry is disabled and the picker reports that limitation; Profiles does not create its own keyboard or edit private input-field internals.

## Requirements

- A clean, 64-bit Beat Saber 1.46.0 installation.
- Windows PowerShell 5.1 or later.
- Internet access to download the pinned BSIPA release.
- The .NET SDK for building and deploying Profiles. The loader-only option does not require the SDK.

## Compatibility note

As of 2026-11-14, BSManager's mod-installation catalog (BeatMods) does not support Beat Saber 1.46.0. These steps start from a bare 1.46.0 installation, install BSIPA directly, and do not rely on an older game installation or its mods.

Profiles declares a dependency on BSIPA `^4.3.7`. The target installation must have BSIPA 4.3.7 or a newer compatible 4.x version. If no loader is present, the initializer installs the official 4.3.7 release; if an older loader is present, it upgrades it to 4.3.7. A compatible newer 4.x installation is left in place.

The plugin bundles the x64 `System.Data.SQLite` provider and deploys its `SQLite.Interop.dll` native companion under `Plugins\x64`. It does not depend on an SQLite assembly supplied by Beat Saber.

## Initialize a bare 1.46.0 installation

1. Prepare a bare, unmodded Beat Saber 1.46.0 installation and leave the game closed. Find its instance directory, which should contain `Beat Saber.exe`, `Beat Saber_Data`, and `BeatSaberVersion.txt`.
2. Open PowerShell in this `ProfilesMod` directory.
3. Run the initializer, specifying the instance directory if it is not the adjacent `..\1.46.0` folder:

   ```powershell
   .\initialize-modding.ps1 -GameDir 'C:\Games\Beat Saber 1.46.0'
   ```

   The script checks that the target is 1.46.0, downloads the official BSIPA 4.3.7 x64/.NET Framework release, verifies its SHA-256, extracts it into the game directory, and runs `IPA.exe -n` to install the loader. It then builds Profiles against that 1.46.0 installation and copies `ProfilesMod.dll` and its SQLite dependencies into the `Plugins` folder. It does not start Beat Saber.

   To install only BSIPA without building or copying Profiles, use:

   ```powershell
   .\initialize-modding.ps1 -GameDir 'C:\Games\Beat Saber 1.46.0' -LoaderOnly
   ```

   If PowerShell blocks the script, allow scripts only in the current PowerShell process with `Set-ExecutionPolicy -Scope Process Bypass`.
4. Confirm the installer reports BSIPA 4.3.7 or a newer compatible 4.x version. The plugin and provider should be at `Plugins\ProfilesMod.dll`, `Plugins\System.Data.SQLite.dll`, and `Plugins\x64\SQLite.Interop.dll`.
5. Start the game when ready. The script intentionally does not launch it. On a successful run, the BSIPA log should show Profiles initialization and the Players main-menu button. The database is created alongside `Beat Saber.exe` on first initialization.

The script preserves other files in the instance, installing BSIPA when absent and upgrading older versions to 4.3.7; it leaves a compatible newer 4.x version in place. It replaces only Profiles' plugin and SQLite dependency files during deployment. If it detects an incomplete installation or an incompatible major version, it stops rather than replacing it silently; review that loader before retrying.

## Build and deploy

Run the deployment script from this repository. It builds Release for the selected 1.46.0 instance, refuses to proceed if Beat Saber is running, copies only the plugin and required SQLite runtime files, and verifies their SHA-256 hashes. The default instance is `C:\VR\BSManager\BSInstances\1.46.0`; override it with `-GameDir`.

```powershell
.\Deploy-ProfilesMod.ps1
.\Deploy-ProfilesMod.ps1 -GameDir 'C:\Games\Beat Saber 1.46.0'
```

Use `-WhatIf` to build and preview the source hashes without copying files. The script does not launch Beat Saber.

```powershell
.\Deploy-ProfilesMod.ps1 -WhatIf
```

The plugin manifest is embedded in the DLL. It declares the BSIPA `^4.3.7` dependency required for the loader to accept the plugin.
