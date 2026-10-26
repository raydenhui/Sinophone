<#
.SYNOPSIS
    Runs the unit and integration tests.

.DESCRIPTION
    Runs dotnet test over Sinophone.sln. This is the command CI runs, so a green
    local run means a green pull request check.

    Tests that need an FFXIV install skip themselves when none is found. To run
    them, set SINOPHONE_FFXIV_PATH to the folder containing game/.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Filter
    Optional xUnit filter, for example "FullyQualifiedName~MacroNormalizer".

.EXAMPLE
    pwsh -File scripts/Test.ps1

.EXAMPLE
    pwsh -File scripts/Test.ps1 -Filter "FullyQualifiedName~TranslationStore"
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Filter = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Sinophone.sln'

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution not found at $solution"
}

if ($Filter) {
    Write-Host "Running tests matching: $Filter"
} else {
    Write-Host "Running all tests."
}

$arguments = @('test', $solution, '-c', $Configuration)
if ($Filter) {
    $arguments += @('--filter', $Filter)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "All tests passed."
