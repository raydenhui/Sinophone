<#
.SYNOPSIS
    Builds the plugin and links it into a Dalamud dev plugin folder.

.DESCRIPTION
    Builds Sinophone and creates a directory junction in the Dalamud dev plugin
    folder, so the game loads the build output directly. Rebuilding updates the
    linked folder; run this script again only when the output path changes.

    Add the dev plugin folder once in Dalamud settings: open /xlsettings, go to
    Experimental -> Dev Plugin Locations, and add the folder. This script creates
    the Sinophone folder inside it.

    The game locks the plugin DLL while the plugin is loaded. Close the game or
    unload the plugin before rebuilding. Pass -NoBuild to link an existing build.

.PARAMETER DevPluginPath
    The Dalamud dev plugin folder. Add this folder in Dalamud settings first.
    For example: C:\Users\you\AppData\Roaming\XIVLauncher\devPlugins

.PARAMETER Configuration
    Build configuration. Defaults to Debug so rebuilds are fast.

.PARAMETER NoBuild
    Skip the build and link the existing output.

.PARAMETER SkipArtifactCheck
    Build even when no translation artifacts are present. The plugin then
    downloads data at runtime instead of embedding it.

.EXAMPLE
    pwsh -File scripts/Deploy-DevPlugin.ps1 -DevPluginPath "$env:APPDATA\XIVLauncher\devPlugins"

.EXAMPLE
    pwsh -File scripts/Deploy-DevPlugin.ps1 -DevPluginPath D:\devPlugins -NoBuild
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$DevPluginPath,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoBuild,

    [switch]$SkipArtifactCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pluginDir = Join-Path $repoRoot 'src/Sinophone'
$linkPath = Join-Path $DevPluginPath 'Sinophone'

if (-not (Test-Path -LiteralPath $DevPluginPath -PathType Container)) {
    throw "Dev plugin folder not found: $DevPluginPath. Add it in Dalamud settings under Experimental -> Dev Plugin Locations, or pass the correct path."
}

if (-not $NoBuild) {
    $buildScript = Join-Path $PSScriptRoot 'Build.ps1'
    $buildArgs = @{ Configuration = $Configuration }
    if ($SkipArtifactCheck) { $buildArgs.SkipArtifactCheck = $true }

    try {
        & $buildScript @buildArgs
    }
    catch {
        $running = Get-Process -Name 'ffxiv_dx11', 'ffxiv' -ErrorAction SilentlyContinue
        if ($running) {
            Write-Warning "The game is running. If the build failed on a locked file, close the game or unload the plugin and run this script again. Use -NoBuild to link an existing build."
        }
        throw
    }
}

$binDir = Join-Path $pluginDir "bin/$Configuration"
$dll = Get-ChildItem -LiteralPath $binDir -Recurse -Filter 'Sinophone.dll' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $dll) {
    throw "Sinophone.dll was not found under $binDir. Build first or check -Configuration."
}

$target = $dll.Directory.FullName
Write-Host "Plugin output: $target"

$existing = Get-Item -LiteralPath $linkPath -Force -ErrorAction SilentlyContinue

if ($null -ne $existing) {
    if ($existing.LinkType -ne 'Junction') {
        throw "$linkPath already exists and is not a junction. Move or remove it, then run this script again."
    }

    $existingTarget = @($existing.Target)[0]
    if ($existingTarget -ne $target) {
        throw "$linkPath is a junction to $existingTarget, not $target. Remove it with: cmd /c rmdir `"$linkPath`""
    }

    Write-Host "Dev plugin already linked to $target"
}
elseif ($PSCmdlet.ShouldProcess($linkPath, "Create a junction to $target")) {
    New-Item -ItemType Junction -Path $linkPath -Target $target | Out-Null
    Write-Host "Linked $linkPath -> $target"
}

Write-Host ""
Write-Host "Reload the plugin in Dalamud, then run /sinophone."
