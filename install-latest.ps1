[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
$releaseRoot = Join-Path $PSScriptRoot 'Releases'
$initializer = Join-Path $PSScriptRoot 'Releases\initialize-modding.ps1'
if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container) -or
    -not (Test-Path -LiteralPath $initializer -PathType Leaf)) {
    throw 'The release directory or modding initializer is missing.'
}

$versions = @(
    Get-ChildItem -LiteralPath $releaseRoot -Directory | ForEach-Object {
        if ($_.Name -match '^Release-(\d+\.\d+(?:\.\d+){0,2})$') {
            [pscustomobject]@{ Version = [version]$Matches[1]; Label = $Matches[1]; Directory = $_.FullName }
        }
    } | Sort-Object -Property Version -Descending
)
if ($versions.Count -eq 0) {
    throw 'No versioned release directory was found.'
}
$latest = $versions[0]
$archive = Join-Path $latest.Directory ("ProfilesMod-" + $latest.Label + ".zip")
$installer = Join-Path $latest.Directory ("Install-ProfilesMod-" + $latest.Label + ".ps1")
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw "The latest release archive is missing: '$archive'."
}
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "The latest release installer is missing: '$installer'."
}

if (-not $PSBoundParameters.ContainsKey('GameDir')) {
    . (Join-Path $PSScriptRoot 'Releases\Select-GameDirectory.ps1')
    $GameDir = Select-GameDirectory
}

& $initializer -GameDir $GameDir -LoaderOnly
Write-Output "Installing Profiles $($latest.Label)."
& $installer -GameDir $GameDir -Confirm:$false
