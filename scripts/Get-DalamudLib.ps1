<#
.SYNOPSIS
    Downloads the Dalamud development libraries so the plugin can build.

.DESCRIPTION
    The plugin is built with Dalamud.NET.SDK, which takes its assembly references
    from a folder of Dalamud DLLs. If you already run XIVLauncher, that folder
    exists and you do not need this script.

    This script downloads the official Dalamud distribution zip and extracts it to
    a folder, then tells you what to set DALAMUD_HOME to.

    It is safe to run more than once. An existing folder is left alone unless
    -Force is passed.

.PARAMETER Destination
    Folder to extract the libraries into. Defaults to a folder under the user's
    local application data.

.PARAMETER Force
    Re-download and overwrite an existing folder.

.EXAMPLE
    pwsh -File scripts/Get-DalamudLib.ps1

.EXAMPLE
    pwsh -File scripts/Get-DalamudLib.ps1 -Destination D:\dalamud-lib -Force
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Sinophone/dalamud-lib'),

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$url = 'https://goatcorp.github.io/dalamud-distrib/latest.zip'

if ((Test-Path -LiteralPath $Destination) -and -not $Force) {
    Write-Host "Libraries already exist at $Destination"
    Write-Host "Use -Force to download them again."
    Write-Host ""
    Write-Host "Set this before building:"
    Write-Host "  `$env:DALAMUD_HOME = '$Destination'"
    return
}

if (-not $PSCmdlet.ShouldProcess($Destination, "Download Dalamud libraries from $url")) {
    return
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) "dalamud-lib-$([guid]::NewGuid().ToString('N')).zip"

try {
    Write-Host "Downloading Dalamud libraries..."
    Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing

    $size = [math]::Round((Get-Item -LiteralPath $temp).Length / 1MB, 1)
    Write-Host "Downloaded $size MiB."

    if (Test-Path -LiteralPath $Destination) {
        Write-Host "Removing existing $Destination"
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Expand-Archive -LiteralPath $temp -DestinationPath $Destination -Force

    if (-not (Test-Path -LiteralPath (Join-Path $Destination 'Dalamud.dll'))) {
        throw "Dalamud.dll was not found after extracting to $Destination."
    }

    Write-Host "Extracted to $Destination"
    Write-Host ""
    Write-Host "Set this before building:"
    Write-Host "  `$env:DALAMUD_HOME = '$Destination'"
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Force
    }
}
