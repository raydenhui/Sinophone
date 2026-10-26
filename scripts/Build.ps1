<#
.SYNOPSIS
    Builds the plugin and the DataPrep tool.

.DESCRIPTION
    Builds Sinophone.sln. The plugin needs a Dalamud library folder; see
    scripts/Get-DalamudLib.ps1 if you do not have one.

    Fails with a clear message when the translation artifacts are missing, because
    a plugin built without them silently translates nothing.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER SkipArtifactCheck
    Build even when no translation artifacts are present.

.EXAMPLE
    pwsh -File scripts/Build.ps1

.EXAMPLE
    pwsh -File scripts/Build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipArtifactCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Sinophone.sln'
$resources = Join-Path $repoRoot 'src/Sinophone/Resources'

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution not found at $solution"
}

if (-not $SkipArtifactCheck) {
    $artifacts = @()
    if (Test-Path -LiteralPath $resources) {
        $artifacts = Get-ChildItem -LiteralPath $resources -Filter '*.json.gz' -ErrorAction SilentlyContinue
    }

    if ($artifacts.Count -eq 0) {
        throw @"
No translation artifacts found in $resources.

The plugin embeds these files at build time. Without them it builds fine but
translates nothing. Build them first:

  pwsh -File scripts/New-TranslationData.ps1 -CnSource <cn csv folder> -TcSource <tc csv folder>

Pass -SkipArtifactCheck to build anyway.
"@
    }

    Write-Host "Found $($artifacts.Count) translation artifact(s)."
}

Write-Host "Building $solution ($Configuration)..."
& dotnet build $solution -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Build succeeded."
