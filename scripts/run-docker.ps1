# run-docker.ps1 — Run the PowerPoint Narration Generator locally via Docker Compose
#
# Builds both images (C# backend + React frontend) and starts them together.
# Azure identity is provided via DefaultAzureCredential in the container — the
# host's 'az login' session is NOT passed in; run in Azure Container Apps or
# set AZURE_CLIENT_ID / AZURE_CLIENT_SECRET in a .env file for service principals.
#
# Usage:  .\run-docker.ps1
# Then open:  http://localhost:3000  (frontend)
#             http://localhost:8080  (backend API + Swagger)
#
# Options:
#   -NoBuild      Skip the docker build step
#   -Detach       Run containers in the background

param(
    [switch]$NoBuild,
    [switch]$Detach
)

$ErrorActionPreference = "Stop"
# Resolve repo root (parent of the scripts/ folder this script lives in)
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

Write-Host ""
Write-Host "  PowerPoint Narration Generator (Docker Compose)" -ForegroundColor Cyan
Write-Host "  =================================================" -ForegroundColor DarkGray
Write-Host ""

# ── Ensure Docker is running ──────────────────────────────────────────────────
docker info *>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Docker is not running. Start Docker Desktop and retry." -ForegroundColor Red
    exit 1
}

# ── Stop any previous containers ─────────────────────────────────────────────
Write-Host "  Stopping any running containers..." -ForegroundColor Yellow
Push-Location $root
docker compose down --remove-orphans 2>$null | Out-Null
Pop-Location

# ── Build ─────────────────────────────────────────────────────────────────────
if (-not $NoBuild) {
    Write-Host "  Building images..." -ForegroundColor Yellow
    Push-Location $root
    docker compose build
    if ($LASTEXITCODE -ne 0) { Write-Host "  Build failed." -ForegroundColor Red; exit 1 }
    Pop-Location
    Write-Host "  Build complete." -ForegroundColor Green
    Write-Host ""
}

# ── Run ───────────────────────────────────────────────────────────────────────
Write-Host "  Frontend → http://localhost:3000" -ForegroundColor Green
Write-Host "  Backend  → http://localhost:8080" -ForegroundColor Green
Write-Host "  Swagger  → http://localhost:8080/swagger"
Write-Host ""

$upArgs = @("compose", "up")
if ($Detach) { $upArgs += "--detach" }

Push-Location $root
& docker @upArgs
Pop-Location
