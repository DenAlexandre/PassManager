#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the PassManager MAUI client for Windows.
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
$TargetFramework = "net10.0-windows10.0.19041.0"

Write-Host "Building PassManager.Maui for Windows ($Configuration)..." -ForegroundColor Cyan

dotnet build $Project -f $TargetFramework -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Error "Windows build failed."
    exit $LASTEXITCODE
}

$ExePath = Join-Path $RepoRoot "src\PassManager.Maui\bin\$Configuration\$TargetFramework\win-x64\PassManager.Maui.exe"
Write-Host "Build succeeded." -ForegroundColor Green
Write-Host "Executable: $ExePath"
