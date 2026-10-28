<#
.SYNOPSIS
    Builds the CN and TC translation artifacts the plugin embeds.

.DESCRIPTION
    Runs Sinophone.DataPrep against the datamining CSV dumps and writes
    src/Sinophone/Resources/Sinophone.<lang>.json.gz.

    The plugin loads artifacts by file name suffix, so the output names must stay
    Sinophone.cn.json.gz and Sinophone.tc.json.gz.

    The CSVs are not in this repo. Point -CnSource and -TcSource at checkouts of
    the two datamining repos, and -EnSource at the English dump (see AGENTS.md for
    links). The script folders under each checkout are indexed too.

    Safe to run more than once: artifacts are overwritten.

.PARAMETER CnSource
    Folder holding the Simplified Chinese CSVs and script folders.

.PARAMETER TcSource
    Folder holding the Traditional Chinese CSVs and script folders.

.PARAMETER EnSource
    Folder holding the English dump, for example ffxiv-datamining\csv\en.
    The plugin keys on this text.

.PARAMETER GameVersion
    Optional patch label recorded in the artifact. Defaults to "unknown".

.EXAMPLE
    pwsh -File scripts/New-TranslationData.ps1 -CnSource C:\ffxiv-datamining-cn -TcSource C:\ffxiv-datamining-tc -EnSource C:\ffxiv-datamining\csv\en -GameVersion 7.4
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$CnSource,

    [Parameter(Mandatory)]
    [string]$TcSource,

    [Parameter(Mandatory)]
    [string]$EnSource,

    [string]$GameVersion = 'unknown'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot 'tools/Sinophone.DataPrep'
$outputDir = Join-Path $repoRoot 'src/Sinophone/Resources'

foreach ($source in @($CnSource, $TcSource, $EnSource)) {
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Source folder not found: $source"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $source 'custom'))) {
        throw "Script folder not found under $source. Point at a full datamining checkout."
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $toolProject 'Sinophone.DataPrep.csproj'))) {
    throw "DataPrep project not found at $toolProject"
}

if (-not $PSCmdlet.ShouldProcess($outputDir, 'Build CN and TC translation artifacts')) {
    return
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$targets = @(
    @{ Language = 'CN'; Source = $CnSource; Output = 'Sinophone.cn.json.gz' }
    @{ Language = 'TC'; Source = $TcSource; Output = 'Sinophone.tc.json.gz' }
)

foreach ($target in $targets) {
    $outputPath = Join-Path $outputDir $target.Output
    Write-Host ""
    Write-Host "Building $($target.Language) artifact -> $outputPath"

    $arguments = @(
        'run'
        '--project', $toolProject
        '--'
        '--source', $target.Source
        '--language', $target.Language
        '--script-source', $target.Source
        '--en-source', $EnSource
        '--output', $outputPath
        '--game-version', $GameVersion
    )

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "DataPrep failed for $($target.Language) with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $outputPath)) {
        throw "DataPrep reported success but $outputPath does not exist."
    }
}

Write-Host ""
Write-Host "Done. Artifacts are in $outputDir"
