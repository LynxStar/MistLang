#!/usr/bin/env pwsh
#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# =============================================================================
# publish.ps1 — Build and install the Mist CLI tool (Windows)
# =============================================================================

# ---------------------------------------------------------------------------
# Prerequisite check for dotnet CLI
# ---------------------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "Error: .NET SDK is required but 'dotnet' was not found on PATH."
    exit 1
}

# ---------------------------------------------------------------------------
# Platform RID auto-detection
# ---------------------------------------------------------------------------
Write-Host "Detecting platform..."

$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

switch ($arch) {
    'X64'   { $ridArch = 'x64' }
    'Arm64' { $ridArch = 'arm64' }
    default {
        Write-Error "Error: Unsupported architecture: $arch"
        exit 1
    }
}

$rid = "win-$ridArch"
Write-Host "Detected platform: $rid"

# ---------------------------------------------------------------------------
# dotnet publish invocation
# ---------------------------------------------------------------------------
Write-Host "Publishing Mist CLI (Release, $rid)..."

dotnet publish Source/Mist.CLI/Mist.CLI.csproj -c Release -r $rid
if ($LASTEXITCODE -ne 0) {
    Write-Error "Error: dotnet publish failed."
    exit 1
}

# ---------------------------------------------------------------------------
# Binary installation to ~/.mist/bin
# ---------------------------------------------------------------------------
$installDir = Join-Path $env:USERPROFILE '.mist' 'bin'
$publishDir = Join-Path 'Source' 'Mist.CLI' 'bin' 'Release' 'net10.0' $rid 'publish'
$binarySrc  = Join-Path $publishDir 'mist.exe'
$binaryDst  = Join-Path $installDir 'mist.exe'

Write-Host "Installing mist binary to $installDir..."

if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

Copy-Item -Path $binarySrc -Destination $binaryDst -Force
if (-not (Test-Path $binaryDst)) {
    Write-Error "Error: Failed to copy mist binary to $installDir"
    exit 1
}

# Copy the Standard Library source files alongside the binary so that
# StdlibLoader can find them at AppContext.BaseDirectory/Standard/.
$stdlibSrc = Join-Path $publishDir 'Standard'
$stdlibDst = Join-Path $installDir 'Standard'

if (Test-Path $stdlibSrc) {
    if (Test-Path $stdlibDst) {
        Remove-Item -Recurse -Force $stdlibDst
    }
    Copy-Item -Recurse -Path $stdlibSrc -Destination $stdlibDst
    Write-Host "Installed Standard Library to $stdlibDst"
} else {
    Write-Warning "Standard Library not found at $stdlibSrc — stdlib will not be available."
}

# ---------------------------------------------------------------------------
# PATH configuration (User environment variable)
# ---------------------------------------------------------------------------
Write-Host "Configuring PATH..."

$profileUpdated = $false
$currentUserPath = [Environment]::GetEnvironmentVariable('Path', 'User')

if ($currentUserPath -split ';' | Where-Object { $_ -eq $installDir }) {
    Write-Host "PATH already contains $installDir — skipping."
} else {
    $newPath = "$installDir;$currentUserPath"
    [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    $profileUpdated = $true
    Write-Host "Added $installDir to User PATH."
}

# ---------------------------------------------------------------------------
# Completion summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "✓ Mist CLI installed to $binaryDst"

if ($profileUpdated) {
    Write-Host ""
    Write-Host "To start using mist, restart your terminal or run:"
    Write-Host "  `$env:Path = `"$installDir;`$env:Path`""
}

Write-Host ""
Write-Host "Usage:"
Write-Host "  mist build <project-folder>"
Write-Host "  mist run <project-folder>"
