[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [ValidateNotNullOrEmpty()]
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('GameDir')) {
    if (-not [Environment]::UserInteractive -or
        @([Environment]::GetCommandLineArgs() | Where-Object { $_ -match '^[-/](noninteractive|noni)$' }).Count -gt 0) {
        throw 'Folder selection is unavailable in noninteractive mode. Supply -GameDir explicitly.'
    }
    if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
        throw 'Folder selection requires an STA PowerShell session. Start PowerShell with -STA or supply -GameDir explicitly.'
    }
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        $dialog.Description = 'Select the Beat Saber 1.46.0 installation folder.'
        $dialog.ShowNewFolderButton = $false
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
            throw 'Folder selection canceled. Nothing was changed.'
        }
        $GameDir = $dialog.SelectedPath
    } finally {
        $dialog.Dispose()
    }
}

$gamePath = [System.IO.Path]::GetFullPath($GameDir)
$plugins = Join-Path $gamePath 'Plugins'
$archive = Join-Path $PSScriptRoot 'ProfilesMod-0.9.zip'
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw "Release archive is missing: '$archive'."
}

function Assert-GameClosed {
    if (Get-Process -Name 'Beat Saber' -ErrorAction SilentlyContinue) {
        throw 'Beat Saber is running. Close it before installing Profiles.'
    }
}

$versionFile = Join-Path $gamePath 'BeatSaberVersion.txt'
if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'Beat Saber.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    throw "Beat Saber.exe or BeatSaberVersion.txt is missing in '$gamePath'."
}
if ((Get-Content -LiteralPath $versionFile -Raw).Trim() -notmatch '^1\.46\.0(?:_|$)') {
    throw 'Profiles 0.9 requires Beat Saber 1.46.0.'
}
Assert-GameClosed

foreach ($required in @('IPA.exe', 'IPA\Data\Managed\IPA.Loader.dll', 'winhttp.dll',
    'Beat Saber_Data\Managed\IPA.Loader.dll', 'Libs\0Harmony.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $gamePath $required) -PathType Leaf)) {
        throw "Modding preparation is incomplete: '$required' is missing."
    }
}

Push-Location $gamePath
try {
    $loaderVersionOutput = & (Join-Path $gamePath 'IPA.exe') -v 2>&1 | Out-String
    $loaderExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($loaderExitCode -ne 0 -or
    $loaderVersionOutput -notmatch '(?m)^Installed version:\s*(\d+\.\d+\.\d+(?:\.\d+)?)\s*$') {
    throw "Could not read the installed BSIPA version.`n$loaderVersionOutput"
}
$loaderVersion = [version]$Matches[1]
if ($loaderVersion -lt [version]'4.3.7' -or $loaderVersion -ge [version]'5.0') {
    throw "Profiles requires BSIPA 4.3.7 or newer within 4.x; found $loaderVersion."
}

$workDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('ProfilesMod-' + [guid]::NewGuid().ToString('N'))
try {
    $previousWhatIf = $WhatIfPreference
    $previousConfirm = $ConfirmPreference
    try {
        $WhatIfPreference = $false
        $ConfirmPreference = 'None'
        New-Item -ItemType Directory -Path $workDirectory | Out-Null
        Expand-Archive -LiteralPath $archive -DestinationPath $workDirectory
    } finally {
        $WhatIfPreference = $previousWhatIf
        $ConfirmPreference = $previousConfirm
    }
    $payload = @(
        [pscustomobject]@{ Relative = 'ProfilesMod.dll'; Source = (Join-Path $workDirectory 'Plugins\ProfilesMod.dll'); Destination = (Join-Path $plugins 'ProfilesMod.dll') },
        [pscustomobject]@{ Relative = 'System.Data.SQLite.dll'; Source = (Join-Path $workDirectory 'Plugins\System.Data.SQLite.dll'); Destination = (Join-Path $plugins 'System.Data.SQLite.dll') },
        [pscustomobject]@{ Relative = 'x64\SQLite.Interop.dll'; Source = (Join-Path $workDirectory 'Plugins\x64\SQLite.Interop.dll'); Destination = (Join-Path $plugins 'x64\SQLite.Interop.dll') }
    )
    foreach ($file in $payload) {
        if (-not (Test-Path -LiteralPath $file.Source -PathType Leaf)) {
            throw "Release file is missing: '$($file.Source)'. Nothing was installed."
        }
    }

    Assert-GameClosed
    foreach ($file in $payload) {
        if (-not (Test-Path -LiteralPath $file.Destination -PathType Leaf)) {
            continue
        }
        $stream = $null
        try {
            $stream = [System.IO.File]::Open($file.Destination, [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        } catch {
            throw "Target file is locked or not writable; nothing was installed: '$($file.Destination)'. $($_.Exception.Message)"
        } finally {
            if ($null -ne $stream) { $stream.Dispose() }
        }
    }

    if (-not $PSCmdlet.ShouldProcess($plugins, 'Install Profiles 0.9 and its SQLite runtime files')) {
        return
    }

    Assert-GameClosed
    New-Item -ItemType Directory -Path (Join-Path $plugins 'x64') -Force | Out-Null
    foreach ($file in $payload) {
        Copy-Item -LiteralPath $file.Source -Destination $file.Destination -Force
        $sourceHash = (Get-FileHash -LiteralPath $file.Source -Algorithm SHA256).Hash
        $targetHash = (Get-FileHash -LiteralPath $file.Destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $targetHash) {
            throw "SHA-256 mismatch after copying '$($file.Relative)': source=$sourceHash target=$targetHash."
        }
        Write-Output "$($file.Relative): verified SHA-256 $targetHash."
    }
} finally {
    if (Test-Path -LiteralPath $workDirectory) {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force -WhatIf:$false -Confirm:$false
    }
}
