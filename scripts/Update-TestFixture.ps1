<#
.SYNOPSIS
    Regenerates the test fixture from the datamining CSV dumps.

.DESCRIPTION
    Rebuilds tests/fixtures/ from the CN, TC and English CSV dumps. The fixture is
    a handful of real rows per sheet plus a few script lines, enough to prove the
    pipeline end to end without committing megabytes of game text.

    Run this when you want the fixture to follow a new game patch. Tests do not
    need it: the fixture is checked in.

    Safe to run more than once: outputs are overwritten.

.PARAMETER CnSource
    Folder holding the Simplified Chinese CSVs.

.PARAMETER TcSource
    Folder holding the Traditional Chinese CSVs.

.PARAMETER EnSource
    Folder holding the English CSVs. This is the text the plugin keys on.

.PARAMETER Rows
    Rows to keep per sheet. Defaults to 8.

.EXAMPLE
    pwsh -File scripts/Update-TestFixture.ps1 -CnSource C:\ffxiv-datamining-cn -TcSource C:\ffxiv-datamining-tc -EnSource C:\ffxiv-datamining\csv\en
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$CnSource,

    [Parameter(Mandatory)]
    [string]$TcSource,

    [Parameter(Mandatory)]
    [string]$EnSource,

    [ValidateRange(1, 100)]
    [int]$Rows = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot 'tools/Sinophone.DataPrep'
$fixtureDir = Join-Path $repoRoot 'tests/fixtures'

foreach ($source in @($CnSource, $TcSource, $EnSource)) {
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Source folder not found: $source"
    }

    foreach ($scriptDirectory in @('custom', 'cut_scene', 'quest')) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $scriptDirectory))) {
            throw "Script folder '$scriptDirectory' not found under $source."
        }
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $toolProject 'Sinophone.DataPrep.csproj'))) {
    throw "DataPrep project not found at $toolProject"
}

if (-not $PSCmdlet.ShouldProcess($fixtureDir, "Regenerate test fixture ($Rows rows per sheet)")) {
    return
}

$arguments = @(
    'run'
    '--project', $toolProject
    '--'
    '--fixture'
    '--source', $CnSource
    '--tc-source', $TcSource
    '--en-source', $EnSource
    '--fixture-output', $fixtureDir
    '--rows', $Rows
)

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Fixture generation failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Done. Run scripts/Test.ps1 to check the new fixture."
