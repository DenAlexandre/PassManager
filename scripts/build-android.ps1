#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the PassManager MAUI client for Android.
.PARAMETER Configuration
    Build configuration (Debug or Release). Defaults to Debug.
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "src\PassManager.Maui\PassManager.Maui.csproj"
$TargetFramework = "net10.0-android"

Write-Host "Building PassManager.Maui for Android ($Configuration)..." -ForegroundColor Cyan

dotnet build $Project -f $TargetFramework -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Android build failed."
    exit $LASTEXITCODE
}

$OutputDir = Join-Path $RepoRoot "src\PassManager.Maui\bin\$Configuration\$TargetFramework"
Write-Host "Build succeeded." -ForegroundColor Green
Write-Host "Output directory: $OutputDir"
