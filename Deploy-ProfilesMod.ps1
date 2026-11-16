[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string]$GameDir = 'C:\VR\BSManager\BSInstances\1.46.0'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$gamePath = [System.IO.Path]::GetFullPath($GameDir)
$versionFile = Join-Path $gamePath 'BeatSaberVersion.txt'
$pluginsDirectory = Join-Path $gamePath 'Plugins'

if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'Beat Saber.exe') -PathType Leaf)) {
    throw "Beat Saber.exe was not found in '$gamePath'."
}
if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    throw "BeatSaberVersion.txt was not found in '$gamePath'."
}
$gameVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($gameVersion -notmatch '^1\.46\.0(?:_|$)') {
    throw "Expected Beat Saber 1.46.0, but found '$gameVersion'."
}
if (-not (Test-Path -LiteralPath $pluginsDirectory -PathType Container)) {
    throw "Plugins directory was not found in '$gamePath'."
}

function Assert-GameClosed {
    $processes = @(Get-Process -Name 'Beat Saber' -ErrorAction SilentlyContinue)
    if ($processes.Count -gt 0) {
        throw 'Beat Saber is running. Close it before deploying ProfilesMod.'
    }
}

function Get-Sha256 {
    param([string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hash)).Replace('-', '')
    } finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

Assert-GameClosed

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw 'The .NET SDK was not found.'
}

$projectPath = Join-Path $PSScriptRoot 'ProfilesMod.csproj'
$buildArguments = @(
    'build',
    $projectPath,
    '--configuration',
    'Release',
    '--nologo',
    "-p:BeatSaberDir=$gamePath"
)
$buildOutput = @(& $dotnet.Source @buildArguments 2>&1)
$buildExitCode = $LASTEXITCODE
if ($buildExitCode -ne 0) {
    foreach ($line in $buildOutput) {
        Write-Output ([string]$line)
    }
    throw "ProfilesMod Release build failed with exit code $buildExitCode; nothing was deployed."
}

$warningSummary = $buildOutput |
    ForEach-Object { [string]$_ } |
    Where-Object { $_ -match '^\s*\d+ Warning\(s\)' } |
    Select-Object -First 1
if ($warningSummary) {
    Write-Output "Build: succeeded (Release; $($warningSummary.Trim()))."
} else {
    Write-Output 'Build: succeeded (Release).'
}

$outputDirectory = Join-Path $PSScriptRoot 'bin\Release\net472'
$payload = @(
    [pscustomobject]@{
        Source = Join-Path $outputDirectory 'ProfilesMod.dll'
        RelativeDestination = 'ProfilesMod.dll'
        Destination = Join-Path $pluginsDirectory 'ProfilesMod.dll'
    },
    [pscustomobject]@{
        Source = Join-Path $outputDirectory 'System.Data.SQLite.dll'
        RelativeDestination = 'System.Data.SQLite.dll'
        Destination = Join-Path $pluginsDirectory 'System.Data.SQLite.dll'
    },
    [pscustomobject]@{
        Source = Join-Path $outputDirectory 'x64\SQLite.Interop.dll'
        RelativeDestination = 'x64\SQLite.Interop.dll'
        Destination = Join-Path $pluginsDirectory 'x64\SQLite.Interop.dll'
    }
)

foreach ($file in $payload) {
    if (-not (Test-Path -LiteralPath $file.Source -PathType Leaf)) {
        throw "Required build output is missing: '$($file.Source)'. Nothing was deployed."
    }
}

Assert-GameClosed

foreach ($file in $payload) {
    if (-not (Test-Path -LiteralPath $file.Destination -PathType Leaf)) {
        continue
    }

    $stream = $null
    try {
        $stream = [System.IO.File]::Open(
            $file.Destination,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
    } catch {
        throw "Target file is locked or not writable; nothing was deployed: '$($file.Destination)'. $($_.Exception.Message)"
    } finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

if (-not $PSCmdlet.ShouldProcess($pluginsDirectory, 'Deploy ProfilesMod and its SQLite runtime files')) {
    if ($WhatIfPreference) {
        if (-not (Test-Path -LiteralPath (Join-Path $pluginsDirectory 'x64') -PathType Container)) {
            Write-Output 'Would create Plugins\x64.'
        }
        foreach ($file in $payload) {
            $hash = Get-Sha256 -Path $file.Source
            Write-Output "Would copy $($file.RelativeDestination); source SHA-256 $hash."
        }
        Write-Output 'Dry run: no files copied.'
    } else {
        Write-Output 'Deployment declined; no files copied.'
    }
    return
}

Assert-GameClosed

$nativeDirectory = Join-Path $pluginsDirectory 'x64'
if (-not (Test-Path -LiteralPath $nativeDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $nativeDirectory | Out-Null
}

foreach ($file in $payload) {
    Copy-Item -LiteralPath $file.Source -Destination $file.Destination
    $sourceHash = Get-Sha256 -Path $file.Source
    $targetHash = Get-Sha256 -Path $file.Destination
    if ($sourceHash -ne $targetHash) {
        throw "SHA-256 mismatch after copying '$($file.RelativeDestination)': source=$sourceHash target=$targetHash."
    }
    Write-Output "$($file.RelativeDestination): verified SHA-256 $targetHash."
}
