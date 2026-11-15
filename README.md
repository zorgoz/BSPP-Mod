# Profiles

Profiles is a BSIPA plugin for Beat Saber 1.46.0. It adds a `Players` button to the main menu and creates a mod-owned SQLite database, `ProfilesMod.sqlite3`, in the Beat Saber instance directory. The database is initialized for writing before the plugin activates; if it cannot be opened and its profile table created, the Players button is disabled and the failure is written to the BSIPA log. When the game exposes `HMUI.HoverHint`, the disabled button also explains the database issue on hover.

The three PNGs in `Resources` are embedded in `ProfilesMod.dll` and used at runtime for the button's normal, highlighted/pressed, and overlay artwork. The sprites preserve their aspect ratio; the cloned menu button's overall size and layout are unchanged.

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

## Manual build and deployment

After BSIPA is installed, build against the intended 1.46.0 instance:

```powershell
dotnet build .\ProfilesMod.csproj -c Release -p:BeatSaberDir="C:\Games\Beat Saber 1.46.0"
```

Copy the output DLLs into that instance's `Plugins` folder and the native provider into its `Plugins\x64` folder:

```powershell
Copy-Item .\bin\Release\net472\ProfilesMod.dll 'C:\Games\Beat Saber 1.46.0\Plugins\ProfilesMod.dll'
Copy-Item .\bin\Release\net472\System.Data.SQLite.dll 'C:\Games\Beat Saber 1.46.0\Plugins\System.Data.SQLite.dll'
New-Item -ItemType Directory -Force 'C:\Games\Beat Saber 1.46.0\Plugins\x64'
Copy-Item .\bin\Release\net472\x64\SQLite.Interop.dll 'C:\Games\Beat Saber 1.46.0\Plugins\x64\SQLite.Interop.dll'
```

The plugin manifest is embedded in the DLL. It declares the BSIPA `^4.3.7` dependency required for the loader to accept the plugin.
