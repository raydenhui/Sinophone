<#
.SYNOPSIS
    Lists the translation failure cases a dev build recorded.

.DESCRIPTION
    Reads the failure folder the plugin writes in a dev build. Each case is one
    lookup that missed or used a loose path, with the steps it took. This script
    is read-only: it filters, counts, and prints cases, and never changes them.

    The folder defaults to the plugin config folder. Set SINOPHONE_FAILURE_DIR in
    the game's environment to record somewhere else, then pass that path here.

.PARAMETER Base
    The failure folder, the one holding latest.json and the session folders.

.PARAMETER Session
    A session folder name. Defaults to the newest one from latest.json.

.PARAMETER Surface
    Only this surface: talk, selection, bubble, battle, nameplate, or item.

.PARAMETER Reason
    Only this reason, such as no-translation or script-row-mismatch.

.PARAMETER Outcome
    Only this outcome: miss, fallback, partial, error, or hit.

.PARAMETER Text
    Only cases whose text holds this substring.

.PARAMETER Trace
    Print the steps of each matching case.

.PARAMETER Stats
    Print the session summary instead of the case list.

.PARAMETER Limit
    How many cases to print. Defaults to 50.

.EXAMPLE
    pwsh -File scripts/Get-Failures.ps1 -Stats

.EXAMPLE
    pwsh -File scripts/Get-Failures.ps1 -Surface selection -Trace

.EXAMPLE
    pwsh -File scripts/Get-Failures.ps1 -Reason no-translation -Text "retainer"
#>
[CmdletBinding()]
param(
    [string]$Base = '',

    [string]$Session = '',

    [string]$Surface = '',

    [string]$Reason = '',

    [string]$Outcome = '',

    [string]$Text = '',

    [switch]$Trace,

    [switch]$Stats,

    [int]$Limit = 50
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Prop($Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

if ($Base -eq '') {
    if (-not $env:APPDATA) {
        throw "Pass -Base with the failure folder. The plugin writes to pluginConfigs/Sinophone/failures."
    }

    $Base = Join-Path $env:APPDATA 'XIVLauncher/pluginConfigs/Sinophone/failures'
}

if (-not (Test-Path -LiteralPath $Base)) {
    throw "No failure folder at $Base. Run a dev build first, or pass -Base."
}

if ($Session -eq '') {
    $latestPath = Join-Path $Base 'latest.json'
    if (-not (Test-Path -LiteralPath $latestPath)) {
        throw "No latest.json in $Base. Pass -Session with a folder name."
    }

    $latest = Get-Content -Raw -LiteralPath $latestPath | ConvertFrom-Json
    $Session = $latest.session
}

$sessionDir = Join-Path $Base $Session
if (-not (Test-Path -LiteralPath $sessionDir)) {
    throw "Session folder not found: $sessionDir"
}

Write-Host "Session: $sessionDir"
Write-Host ""

if ($Stats) {
    $summaryPath = Join-Path $sessionDir 'summary.json'
    if (-not (Test-Path -LiteralPath $summaryPath)) {
        throw "No summary.json yet. The file is written at the end of a session or every 2 seconds."
    }

    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    Write-Host ("{0:N0} cases, {1:N0} lookups" -f $summary.cases, $summary.repeats)
    Write-Host ""

    foreach ($group in 'bySurface', 'byOutcome', 'byReason') {
        Write-Host ($group + ':')
        $values = Get-Prop $summary $group
        if ($null -ne $values) {
            $values.PSObject.Properties |
                Sort-Object -Property Value -Descending |
                ForEach-Object { Write-Host ("  {0,-24} {1,6:N0}" -f $_.Name, $_.Value) }
        }

        Write-Host ""
    }

    return
}

$indexPath = Join-Path $sessionDir 'index.jsonl'
if (-not (Test-Path -LiteralPath $indexPath)) {
    Write-Host 'No cases recorded for this session.'
    return
}

$rows = @(Get-Content -LiteralPath $indexPath |
    Where-Object { $_.Trim() -ne '' } |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object {
        ($Surface -eq '' -or (Get-Prop $_ 'surface') -eq $Surface) -and
        ($Reason -eq '' -or (Get-Prop $_ 'reason') -eq $Reason) -and
        ($Outcome -eq '' -or (Get-Prop $_ 'outcome') -eq $Outcome) -and
        ($Text -eq '' -or ("$(Get-Prop $_ 'text')" -like "*$Text*"))
    })

if ($rows.Count -eq 0) {
    Write-Host 'No cases match.'
    return
}

Write-Host ("{0:N0} matching case(s), showing {1:N0}." -f $rows.Count, [Math]::Min($Limit, $rows.Count))
Write-Host ""
$rows | Select-Object -First $Limit |
    Format-Table @{ Name = 'Id'; Expression = { Get-Prop $_ 'id' } },
        @{ Name = 'Surface'; Expression = { Get-Prop $_ 'surface' } },
        @{ Name = 'Outcome'; Expression = { Get-Prop $_ 'outcome' } },
        @{ Name = 'Reason'; Expression = { Get-Prop $_ 'reason' } },
        @{ Name = 'Repeat'; Expression = { Get-Prop $_ 'repeat' } },
        @{ Name = 'Text'; Expression = { Get-Prop $_ 'text' } } -AutoSize

if (-not $Trace) {
    return
}

foreach ($row in ($rows | Select-Object -First $Limit)) {
    $casePath = Join-Path (Join-Path $sessionDir 'cases') ((Get-Prop $row 'id') + '.json')
    if (-not (Test-Path -LiteralPath $casePath)) {
        Write-Host "Case file missing: $casePath"
        continue
    }

    $case = Get-Content -Raw -LiteralPath $casePath | ConvertFrom-Json
    Write-Host ""
    Write-Host ("Case {0}  {1}  {2}  {3}" -f $case.id, $case.surface, $case.outcome, (Get-Prop $case 'reason'))

    foreach ($step in $case.steps) {
        $line = "  {0}: {1}" -f $step.stage, $step.result
        $detail = Get-Prop $step 'detail'
        if ($null -ne $detail -and $detail -ne '') { $line += " — $detail" }
        Write-Host $line
    }

    foreach ($group in 'input', 'data') {
        $values = Get-Prop $case $group
        if ($null -eq $values) { continue }

        foreach ($property in $values.PSObject.Properties) {
            Write-Host ("  {0}.{1} = {2}" -f $group, $property.Name, $property.Value)
        }
    }
}
