[CmdletBinding()]
param(
    [string]$GameDir = (Join-Path $PSScriptRoot '..\1.46.0'),
    [switch]$LoaderOnly
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$bsipaVersion = '4.3.7'
$minimumBsipaVersion = [version]'4.3.7.0'
$maximumBsipaVersion = [version]'5.0.0.0'
$releaseUrl = 'https://github.com/nike4613/BeatSaber-IPA-Reloaded/releases/download/4.3.7/BSIPA-net472-x64.zip'
$expectedSha256 = '899b8a1dda91935bd5c19a211fa48e44e32f7be9ab82b6fc796709c753b7b2bc'
$gamePath = [System.IO.Path]::GetFullPath($GameDir)

function Get-InstalledBsipaVersion {
    param([string]$Installer)

    Push-Location $gamePath
    try {
        $versionOutput = & $Installer -v 2>&1 | Out-String
        $versionExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($versionExitCode -ne 0) {
        throw "Could not read the installed BSIPA version.`n$versionOutput"
    }

    if ($versionOutput -match '(?m)^Installed version:\s*(\d+\.\d+\.\d+(?:\.\d+)?)\s*$') {
        $versionText = $Matches[1]
    } else {
        throw "Could not read the installed BSIPA version.`n$versionOutput"
    }

    if ($versionText.Split('.').Length -eq 3) {
        $versionText += '.0'
    }

    return [System.Version]::Parse($versionText)
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'This initializer requires 64-bit Windows.'
}

$versionFile = Join-Path $gamePath 'BeatSaberVersion.txt'
$gameVersion = if (Test-Path -LiteralPath $versionFile -PathType Leaf) {
    (Get-Content -LiteralPath $versionFile -Raw).Trim()
} else {
    throw "BeatSaberVersion.txt was not found in '$gamePath'."
}

if ($gameVersion -notmatch '^1\.46\.0(?:_|$)') {
    throw "Expected Beat Saber 1.46.0, but found '$gameVersion'. No files were changed."
}

if (Get-Process -Name 'Beat Saber' -ErrorAction SilentlyContinue) {
    throw 'Beat Saber is running. Close it before initializing the mod loader.'
}

$requiredPaths = @(
    (Join-Path $gamePath 'Beat Saber.exe'),
    (Join-Path $gamePath 'Beat Saber_Data\Managed\UnityEngine.CoreModule.dll'),
    (Join-Path $gamePath 'MonoBleedingEdge')
)
foreach ($requiredPath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "The selected directory does not look like a complete 64-bit Mono Beat Saber installation: '$requiredPath' is missing."
    }
}

$installerPath = Join-Path $gamePath 'IPA.exe'
$ipaDirectory = Join-Path $gamePath 'IPA'
$loaderDll = Join-Path $gamePath 'IPA\Data\Managed\IPA.Loader.dll'
$doorstopDll = Join-Path $gamePath 'winhttp.dll'
$existingMarkers = @($installerPath, $ipaDirectory, $loaderDll, $doorstopDll) |
    Where-Object { Test-Path -LiteralPath $_ }
$shouldInstallBsipa = $existingMarkers.Count -eq 0

if ($existingMarkers.Count -gt 0) {
    if ($existingMarkers.Count -ne 4) {
        throw 'A partial mod-loader installation was detected. No files were changed; review the existing IPA/winhttp files or use a clean instance.'
    }

    $installedVersion = Get-InstalledBsipaVersion -Installer $installerPath
    if ($installedVersion -lt $minimumBsipaVersion) {
        Write-Host "Upgrading BSIPA $installedVersion to $bsipaVersion."
        $shouldInstallBsipa = $true
    } elseif ($installedVersion -ge $maximumBsipaVersion) {
        throw "The installed BSIPA version is $installedVersion. TestMod requires 4.3.7 or newer within the compatible 4.x range; no files were changed."
    } else {
        Write-Host "Compatible BSIPA $installedVersion is already installed."
    }
}

if ($shouldInstallBsipa) {
    $workDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('BSIPA-' + [guid]::NewGuid().ToString('N'))
    $archivePath = Join-Path $workDirectory 'BSIPA-net472-x64.zip'
    $extractPath = Join-Path $workDirectory 'extracted'

    New-Item -ItemType Directory -Path $workDirectory | Out-Null
    try {
        Write-Host "Downloading official BSIPA $bsipaVersion..."
        Invoke-WebRequest -Uri $releaseUrl -OutFile $archivePath -UseBasicParsing

        $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualSha256 -ne $expectedSha256) {
            throw "BSIPA archive SHA-256 mismatch. Expected $expectedSha256, received $actualSha256."
        }

        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
        if (-not (Test-Path -LiteralPath (Join-Path $extractPath 'IPA.exe') -PathType Leaf)) {
            throw 'The downloaded BSIPA archive did not contain IPA.exe at its root.'
        }

        Get-ChildItem -LiteralPath $extractPath -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $gamePath -Recurse -Force
        }

        Push-Location $gamePath
        try {
            & $installerPath -n
            $installExitCode = $LASTEXITCODE
        } finally {
            Pop-Location
        }

        if ($installExitCode -ne 0) {
            throw "IPA.exe failed to install BSIPA (exit code $installExitCode)."
        }

        $installedVersion = Get-InstalledBsipaVersion -Installer $installerPath
        if ($installedVersion -lt $minimumBsipaVersion -or $installedVersion -ge $maximumBsipaVersion) {
            throw "BSIPA installation reported version $installedVersion, which is outside the required compatible range."
        }

        if (-not (Test-Path -LiteralPath $doorstopDll -PathType Leaf)) {
            throw 'BSIPA reported success, but the game-root winhttp.dll is missing.'
        }

        Write-Host "Installed and verified BSIPA $installedVersion."
    } finally {
        if (Test-Path -LiteralPath $workDirectory) {
            Remove-Item -LiteralPath $workDirectory -Recurse -Force
        }
    }
}

if (-not $LoaderOnly) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        throw 'The .NET SDK was not found. Install the SDK or rerun with -LoaderOnly.'
    }

    $projectPath = Join-Path $PSScriptRoot 'TestMod.csproj'
    $buildArguments = @(
        'build',
        $projectPath,
        '--configuration',
        'Release',
        "-p:BeatSaberDir=$gamePath"
    )
    & $dotnet.Source @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "TestMod build failed with exit code $LASTEXITCODE."
    }

    $pluginPath = Join-Path $PSScriptRoot 'bin\Release\net472\TestMod.dll'
    if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
        throw "Build succeeded but the plugin DLL was not found at '$pluginPath'."
    }

    $pluginsDirectory = Join-Path $gamePath 'Plugins'
    New-Item -ItemType Directory -Path $pluginsDirectory -Force | Out-Null
    Copy-Item -LiteralPath $pluginPath -Destination (Join-Path $pluginsDirectory 'TestMod.dll') -Force
    Write-Host "Built and copied TestMod.dll to '$pluginsDirectory'."
}

Write-Host 'Initialization complete. Beat Saber was not started; launch it when ready.'
