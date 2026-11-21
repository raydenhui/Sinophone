<#
.SYNOPSIS
    Measures how much of each component's text the plugin can translate, against the real data.

.DESCRIPTION
    Runs the coverage tool: it enumerates every English string a component can show, resolves each
    one through that component's own code, and writes coverage.md and coverage.json. The English
    text comes from the xivapi/ffxiv-datamining CSVs, which are downloaded once and reused.

    This is an evaluation run, not a build gate. It needs the downloaded translation artifacts and
    a few minutes on the first run; scripts/Test.ps1 stays offline and fast.

.PARAMETER Data
    Folder holding Sinophone.cn.json.gz and Sinophone.tc.json.gz. Defaults to the plugin config
    folder, which is where the running plugin keeps them.

.PARAMETER EnCsv
    Folder for the English dump CSVs. Defaults to <Data>/en-csv.

.PARAMETER Out
    Report folder. Defaults to <Data>/coverage.

.PARAMETER Variant
    Which artifact to measure: tc or cn.

.PARAMETER Gate
    A coverage.json from an earlier run. The run then exits 1 when a component translates fewer
    cases than that baseline.

.EXAMPLE
    pwsh -File scripts/Test-Coverage.ps1

.EXAMPLE
    pwsh -File scripts/Test-Coverage.ps1 -Variant cn -Out C:\tmp\coverage-cn
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Data = (Join-Path $env:APPDATA 'XIVLauncher/pluginConfigs/Sinophone'),

    [string]$EnCsv = '',

    [string]$Out = '',

    [ValidateSet('tc', 'cn')]
    [string]$Variant = 'tc',

    [string]$Gate = '',

    [switch]$ListSheets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot 'tools/Sinophone.Coverage/Sinophone.Coverage.csproj'

if (-not (Test-Path -LiteralPath $toolProject)) {
    throw "Coverage tool not found at $toolProject."
}

if (-not $ListSheets) {
    if (-not (Test-Path -LiteralPath $Data)) {
        throw "Data folder not found: $Data. Pass -Data with the folder holding the artifacts."
    }

    $artifact = Join-Path $Data "Sinophone.$Variant.json.gz"
    if (-not (Test-Path -LiteralPath $artifact)) {
        throw "No artifact at $artifact. Run the plugin once, or pass -Data with the folder that has it."
    }
}

$arguments = @('run', '--project', $toolProject, '--', '--data', $Data, '--variant', $Variant)

if ($EnCsv -ne '') { $arguments += @('--en', $EnCsv) }
if ($Out -ne '') { $arguments += @('--out', $Out) }
if ($Gate -ne '') { $arguments += @('--gate', $Gate) }
if ($ListSheets) { $arguments += '--list-sheets' }

$reportFolder = if ($Out -ne '') { $Out } else { Join-Path $Data 'coverage' }

if (-not $PSCmdlet.ShouldProcess($reportFolder, "Measure $Variant coverage")) {
    return
}

Push-Location $repoRoot
try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The coverage run failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Report written to $reportFolder"
