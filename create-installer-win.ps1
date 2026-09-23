#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a Windows installer for Shelfwarden.Desktop.

.DESCRIPTION
    Shelfwarden.Desktop uses ElectronNET.Core. Packaging is a self-contained
    `dotnet publish` for win-x64. ElectronNET.Core then runs electron-builder,
    which produces an NSIS installer from
    Shelfwarden.Desktop/Properties/electron-builder.json.

    The older Electron.NET `electronize build /target win` CLI is not used.

.NOTES
    Requires the .NET 10 SDK and Node.js 22 or later.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$projectDir = Join-Path $repoRoot 'Shelfwarden.Desktop'
$project = Join-Path $projectDir 'Shelfwarden.Desktop.csproj'
$outputDir = Join-Path $projectDir 'publish\Release\net10.0\win-x64'

function Assert-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $InstallHint
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found on PATH. $InstallHint"
    }
}

Assert-Command -Name 'dotnet' -InstallHint 'Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
Assert-Command -Name 'node' -InstallHint 'Install Node.js 22 or later: https://nodejs.org/'
Assert-Command -Name 'npm' -InstallHint 'npm ships with Node.js.'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project file not found: $project"
}

$nodeVersion = (& node -p "process.versions.node").Trim()
$nodeMajor = [int]($nodeVersion.Split('.')[0])
if ($nodeMajor -lt 22) {
    throw "Node.js 22 or later is required (found $nodeVersion)."
}

Write-Host "Publishing Shelfwarden.Desktop (Release, win-x64, self-contained)..."
Write-Host "Project: $project"
Write-Host ""

Push-Location $projectDir
try {
    & dotnet publish $project `
        --configuration Release `
        -p:PublishProfile=win-x64

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Publish finished. Installer output:"
Write-Host "  $outputDir"

if (Test-Path -LiteralPath $outputDir) {
    Get-ChildItem -LiteralPath $outputDir -File |
        Where-Object { $_.Extension -in '.exe', '.msi', '.blockmap' } |
        ForEach-Object { Write-Host ("  " + $_.Name) }
}
