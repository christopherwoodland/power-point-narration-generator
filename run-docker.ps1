# run-docker.ps1 — Run the PowerPoint Narration Generator locally via Docker
#
# Builds a local image, obtains an Azure CLI token from the host (no API keys),
# and starts the container on port 8080 (so it doesn't conflict with other
# services that may be running on port 8000).
#
# Usage:  .\run-docker.ps1
# Then open:  http://localhost:8080
#
# Options:
#   -Port <n>     Override host port (default 8080)
#   -NoBuild      Skip the docker build step (use cached image)

param(
    [int]   $Port    = 8080,
    [switch]$NoBuild
)

$ErrorActionPreference = "Continue"
$root  = Split-Path -Parent $MyInvocation.MyCommand.Path
$image = "pptx-narration-local:latest"
$name  = "pptx-narration-local-run"

Write-Host ""
Write-Host "  PowerPoint Narration Generator (Docker)" -ForegroundColor Cyan
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

# ── 1. Ensure Docker is running ───────────────────────────────────────────────
docker info *>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker is not running. Start Docker Desktop and retry." -ForegroundColor Red
    exit 1
}

# ── 2. Build image ────────────────────────────────────────────────────────────
if (-not $NoBuild) {
    Write-Host "  Building Docker image..." -ForegroundColor Yellow
    docker build -t $image $root
    $imageId = docker images -q $image 2>$null
    if (-not $imageId) { Write-Host "Docker build failed." -ForegroundColor Red; exit 1 }
    Write-Host "  Build complete." -ForegroundColor Green
    Write-Host ""
}

# ── 3. Obtain Azure Cognitive Services token from host az CLI ─────────────────
Write-Host "  Getting Azure token from az CLI..." -ForegroundColor Yellow
$aadJson = az account get-access-token --resource https://cognitiveservices.azure.com/ 2>$null | ConvertFrom-Json
if (-not $aadJson -or -not $aadJson.accessToken) {
    Write-Host "Failed to get Azure token. Run 'az login' and retry." -ForegroundColor Red
    exit 1
}
$token     = $aadJson.accessToken
$expiresOn = [DateTimeOffset]::Parse($aadJson.expiresOn).ToUnixTimeSeconds()
$expStr    = [DateTimeOffset]::FromUnixTimeSeconds($expiresOn).LocalDateTime.ToShortTimeString()
Write-Host "  Token obtained (valid until $expStr)." -ForegroundColor Green
Write-Host ""

# ── 4. Stop any previous local dev container ──────────────────────────────────
$prev = docker ps -q --filter "name=$name" 2>$null
if ($prev) {
    Write-Host "  Stopping previous container..." -ForegroundColor Yellow
    docker stop $name *>$null
}

# ── 5. Load optional .env overrides ──────────────────────────────────────────
$envArgs = @()
$envFile = Join-Path $root ".env"
if (Test-Path $envFile) {
    Write-Host "  Loading .env file..." -ForegroundColor Yellow
    $envArgs = @("--env-file", $envFile)
}

# ── 6. Run container ──────────────────────────────────────────────────────────
Write-Host "  Starting container on http://localhost:$Port" -ForegroundColor Green
Write-Host "  Press Ctrl+C to stop."
Write-Host ""

docker run --rm `
    --name $name `
    -p "${Port}:8000" `
    -e "AZURE_SPEECH_RESOURCE_NAME=bhs-development-public-foundry-r" `
    -e "AZURE_SPEECH_REGION=eastus2" `
    -e "AZURE_STATIC_BEARER_TOKEN=$token" `
    -e "AZURE_STATIC_TOKEN_EXPIRES=$expiresOn" `
    @envArgs `
    $image
