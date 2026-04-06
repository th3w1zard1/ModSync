# Code style verification/fix for CI and local use. Matches .editorconfig (including end_of_line).
param(
    [switch]$Verify,
    [switch]$Fix
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$solution = "KOTORModSync.sln"

if ($Verify -and $Fix) {
    Write-Error "Specify either -Verify or -Fix, not both."
    exit 2
}

if (-not $Verify -and -not $Fix) {
    Write-Error "Usage: .\cleanup-code.ps1 -Verify  or  .\cleanup-code.ps1 -Fix"
    exit 2
}

if ($Verify) {
    dotnet format whitespace --verify-no-changes $solution
}
else {
    dotnet format whitespace $solution
}
