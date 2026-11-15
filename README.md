# TestMod

A minimal BSIPA plugin for Beat Saber 1.46.0. It adds a `Players` button to the main menu and logs when it is clicked; it does not implement a game mode or change the logged-in identity.

The three PNGs in `Resources` are embedded in `TestMod.dll` and used at runtime for the button's normal, highlighted/pressed, and overlay artwork. The sprites preserve their aspect ratio; the cloned menu button's overall size and layout are unchanged.

## Requirements

- A clean, 64-bit Beat Saber 1.46.0 installation.
- Windows PowerShell 5.1 or later.
- Internet access to download the pinned BSIPA release.
- The .NET SDK for building and deploying TestMod. The loader-only option does not require the SDK.

## Compatibility note

As of 2026-11-14, BSManager's mod-installation catalog (BeatMods) does not support Beat Saber 1.46.0. These steps start from a bare 1.46.0 installation, install BSIPA directly, and do not rely on an older game installation or its mods.

TestMod declares a dependency on BSIPA `^4.3.7`. The target installation must have BSIPA 4.3.7 or a newer compatible 4.x version. If no loader is present, the initializer installs the official 4.3.7 release; if an older loader is present, it upgrades it to 4.3.7. A compatible newer 4.x installation is left in place.

## Initialize a bare 1.46.0 installation

1. Prepare a bare, unmodded Beat Saber 1.46.0 installation and leave the game closed. Find its instance directory, which should contain `Beat Saber.exe`, `Beat Saber_Data`, and `BeatSaberVersion.txt`.
2. Open PowerShell in this `TestMod` directory.
3. Run the initializer, specifying the instance directory if it is not the adjacent `..\1.46.0` folder:

   ```powershell
   .\initialize-modding.ps1 -GameDir 'C:\Games\Beat Saber 1.46.0'
   ```

   The script checks that the target is 1.46.0, downloads the official BSIPA 4.3.7 x64/.NET Framework release, verifies its SHA-256, extracts it into the game directory, and runs `IPA.exe -n` to install the loader. It then builds TestMod against that 1.46.0 installation and copies `TestMod.dll` into its `Plugins` folder. It does not start Beat Saber.

   To install only BSIPA without building or copying TestMod, use:

   ```powershell
   .\initialize-modding.ps1 -GameDir 'C:\Games\Beat Saber 1.46.0' -LoaderOnly
   ```

   If PowerShell blocks the script, allow scripts only in the current PowerShell process with `Set-ExecutionPolicy -Scope Process Bypass`.
4. Confirm the installer reports BSIPA 4.3.7 or a newer compatible 4.x version. The plugin DLL should be at `Plugins\TestMod.dll`.
5. Start the game when ready. The script intentionally does not launch it. On a successful run, the BSIPA log should show TestMod initialization and the Players main-menu button; clicking it adds a click log entry.

The script preserves other files in the instance, installing BSIPA when absent and upgrading older versions to 4.3.7; it leaves a compatible newer 4.x version in place. It replaces only `Plugins\TestMod.dll` during deployment. If it detects an incomplete installation or an incompatible major version, it stops rather than replacing it silently; review that loader before retrying.

## Manual build and deployment

After BSIPA is installed, build against the intended 1.46.0 instance:

```powershell
dotnet build .\TestMod.csproj -c Release -p:BeatSaberDir="C:\Games\Beat Saber 1.46.0"
```

Copy the output DLL into that instance's `Plugins` folder:

```powershell
Copy-Item .\bin\Release\net472\TestMod.dll 'C:\Games\Beat Saber 1.46.0\Plugins\TestMod.dll'
```

The plugin manifest is embedded in the DLL. It declares the BSIPA `^4.3.7` dependency required for the loader to accept the plugin.
