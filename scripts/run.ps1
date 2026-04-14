#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Start the PptxNarrator development environment (C# backend + React frontend).

.DESCRIPTION
    Launches both the C# ASP.NET Core backend and the Vite React frontend
    as concurrent background jobs, then streams their combined output.

    Feature flags (default: all enabled):
        $env:ENABLE_QUALITY_CHECK = "false"
        $env:ENABLE_AI_MODE       = "false"
        $env:ENABLE_VIDEO_EXPORT  = "false"
        .\run.ps1
#>
param(
    [string] $BackendProject = "backend-csharp\src\PptxNarrator.Api",
    [string] $FrontendDir    = "frontend",
    [int]    $BackendPort    = 8080,
    [int]    $FrontendPort   = 3000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolve repo root (parent of the scripts/ folder this script lives in)
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

function Write-Header([string]$msg) {
    Write-Host ""
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host ("  " + ("=" * $msg.Length)) -ForegroundColor DarkGray
}

function Require-Command([string]$cmd) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Error "Required command '$cmd' not found. Please install it and retry."
        exit 1
    }
}

# ── Pre-flight ────────────────────────────────────────────────────────────────
Require-Command "dotnet"
Require-Command "npm"

# ── .env file (optional) ──────────────────────────────────────────────────────
$envFile = Join-Path $Root ".env"
if (Test-Path $envFile) {
    Get-Content $envFile | Where-Object { $_ -match "^\s*[^#\s]" } | ForEach-Object {
        $parts = $_ -split "=", 2
        if ($parts.Count -eq 2) {
            [System.Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim(), "Process")
        }
    }
}

# ── Feature flags ────────────────────────────────────────────────────────────
$flags = @{
    ENABLE_QUALITY_CHECK       = if ($env:ENABLE_QUALITY_CHECK)       { $env:ENABLE_QUALITY_CHECK }       else { "true" }
    ENABLE_AI_MODE             = if ($env:ENABLE_AI_MODE)             { $env:ENABLE_AI_MODE }             else { "true" }
    ENABLE_VIDEO_EXPORT        = if ($env:ENABLE_VIDEO_EXPORT)        { $env:ENABLE_VIDEO_EXPORT }        else { "true" }
    AZURE_SPEECH_RESOURCE_NAME = if ($env:AZURE_SPEECH_RESOURCE_NAME) { $env:AZURE_SPEECH_RESOURCE_NAME } else { "bhs-development-public-foundry-r" }
    AZURE_SPEECH_REGION        = if ($env:AZURE_SPEECH_REGION)        { $env:AZURE_SPEECH_REGION }        else { "eastus2" }
    AZURE_TENANT_ID            = if ($env:AZURE_TENANT_ID)            { $env:AZURE_TENANT_ID }            else { "16b3c013-d300-468d-ac64-7eda0820b6d3" }
}
foreach ($kv in $flags.GetEnumerator()) {
    [System.Environment]::SetEnvironmentVariable($kv.Key, $kv.Value, "Process")
}

Write-Header "PowerPoint Narration Generator"
Write-Host "  Backend  → http://localhost:$BackendPort"
Write-Host "  Frontend → http://localhost:$FrontendPort"
Write-Host "  Swagger  → http://localhost:$BackendPort/swagger"
Write-Host ""

foreach ($kv in $flags.GetEnumerator()) {
    $color = if ($kv.Value -eq "false") { "Yellow" } else { "Green" }
    Write-Host ("  {0,-30} = {1}" -f $kv.Key, $kv.Value) -ForegroundColor $color
}

# ── Backend ────────────────────────────────────────────────────────────────────
$backendPath = Join-Path $Root $BackendProject
if (-not (Test-Path $backendPath)) {
    Write-Error "C# backend project not found at '$backendPath'."
    exit 1
}

$backendJob = Start-Job -Name "backend" -ScriptBlock {
    param($dir, $port, $envVars)
    foreach ($kv in $envVars.GetEnumerator()) {
        [System.Environment]::SetEnvironmentVariable($kv.Key, $kv.Value, "Process")
    }
    Set-Location $dir
    $env:ASPNETCORE_URLS = "http://+:$port"
    dotnet run --no-launch-profile 2>&1
} -ArgumentList $backendPath, $BackendPort, $flags

# ── Frontend ───────────────────────────────────────────────────────────────────
$frontendPath = Join-Path $Root $FrontendDir
if (-not (Test-Path $frontendPath)) {
    Write-Error "Frontend directory not found at '$frontendPath'."
    exit 1
}

if (-not (Test-Path (Join-Path $frontendPath "node_modules"))) {
    Write-Host ""
    Write-Host "  Installing npm dependencies…" -ForegroundColor DarkYellow
    Push-Location $frontendPath
    npm install --legacy-peer-deps
    Pop-Location
}

$frontendJob = Start-Job -Name "frontend" -ScriptBlock {
    param($dir, $port)
    Set-Location $dir
    npm run dev -- --port $port 2>&1
} -ArgumentList $frontendPath, $FrontendPort

Write-Host ""
Write-Host "  Press Ctrl+C to stop all services." -ForegroundColor DarkYellow

try {
    while ($true) {
        Receive-Job -Job $backendJob  -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Host "[backend]  $_" -ForegroundColor DarkCyan }
        Receive-Job -Job $frontendJob -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Host "[frontend] $_" -ForegroundColor DarkMagenta }

        if ($backendJob.State  -eq "Failed") { Write-Error "Backend job failed.";  break }
        if ($frontendJob.State -eq "Failed") { Write-Error "Frontend job failed."; break }

        Start-Sleep -Milliseconds 500
    }
}
finally {
    Stop-Job   -Job $backendJob, $frontendJob -ErrorAction SilentlyContinue
    Remove-Job -Job $backendJob, $frontendJob -ErrorAction SilentlyContinue
    Write-Host ""
    Write-Host "  Services stopped." -ForegroundColor Green
}
