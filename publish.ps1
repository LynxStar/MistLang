#!/usr/bin/env pwsh
#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot

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

$stormProject = Join-Path $repoRoot 'Source/Storm.SourceGenerator/Storm.SourceGenerator.csproj'
if (-not (Test-Path -LiteralPath $stormProject -PathType Leaf)) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Error "Error: Git is required to initialize the Storm source-generator submodule."
        exit 1
    }

    Write-Host "Initializing Storm source-generator submodule..."
    git -C $repoRoot submodule update --init --recursive -- Source/Storm.SourceGenerator
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Error: Could not initialize Storm. Ensure Git can access the private LynxStar/StormLang repository, then run 'git submodule update --init --recursive'."
        exit 1
    }

    if (-not (Test-Path -LiteralPath $stormProject -PathType Leaf)) {
        Write-Error "Error: Storm source-generator project is still missing at $stormProject."
        exit 1
    }
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

$cliProject = Join-Path $repoRoot 'Source/Mist.CLI/Mist.CLI.csproj'
dotnet publish $cliProject -c Release -r $rid -p:TreatWarningsAsErrors=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "Error: dotnet publish failed."
    exit 1
}

# ---------------------------------------------------------------------------
# Binary installation to ~/.mist/bin
# ---------------------------------------------------------------------------
$installDir = Join-Path $env:USERPROFILE '.mist/bin'
$publishDir = Join-Path $repoRoot "Source/Mist.CLI/bin/Release/net10.0/$rid/publish"
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

$steeringSrc = Join-Path $publishDir 'Steering'
$steeringDst = Join-Path $installDir 'Steering'
if (Test-Path $steeringSrc) {
    if (Test-Path $steeringDst) {
        Remove-Item -Recurse -Force $steeringDst
    }
    Copy-Item -Recurse -Path $steeringSrc -Destination $steeringDst
    Write-Host "Installed steering files to $steeringDst"
} else {
    Write-Warning "Steering files not found at $steeringSrc — 'mist steering' will not be available."
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
Write-Host "  mist steering [project-folder]"
