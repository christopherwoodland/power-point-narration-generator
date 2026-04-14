#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build images, push to ACR, then deploy both Container Apps via Bicep.

.DESCRIPTION
    1. Builds the C# backend and React frontend Docker images.
    2. Pushes them to Azure Container Registry (ACR Build or local push).
    3. Runs az deployment group create with infra/main.bicep.

    Authentication uses your current az login session (DefaultAzureCredential
    in the deployed containers uses Managed Identity — no API keys).

.PARAMETER ResourceGroup
    Existing Azure resource group to deploy into.

.PARAMETER AcrName
    Azure Container Registry name (must already exist).

.PARAMETER Tag
    Image tag. Defaults to a UTC timestamp (e.g. 20240601-153045).

.PARAMETER UseAcrBuild
    When set, use 'az acr build' (cloud build, no local Docker required).
    Defaults to local Docker build.

.EXAMPLE
    .\deploy.ps1 -ResourceGroup my-rg -AcrName myacr

.EXAMPLE
    .\deploy.ps1 -ResourceGroup my-rg -AcrName myacr -UseAcrBuild
#>
param(
    [Parameter(Mandatory)]
    [string] $ResourceGroup,

    [Parameter(Mandatory)]
    [string] $AcrName,

    [string] $Tag          = (Get-Date -Format "yyyyMMdd-HHmmss"),
    [switch] $UseAcrBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolve repo root (parent of the scripts/ folder this script lives in)
$Root     = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$AcrLogin = "$AcrName.azurecr.io"

$BackendImage  = "$AcrLogin/narrator-backend:$Tag"
$FrontendImage = "$AcrLogin/narrator-frontend:$Tag"

function Write-Step([int]$n, [string]$msg) {
    Write-Host ""
    Write-Host "  [$n] $msg" -ForegroundColor Cyan
}

function Require-Command([string]$cmd) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Error "Required command '$cmd' not found in PATH."
        exit 1
    }
}

# ── Pre-flight ────────────────────────────────────────────────────────────────
Require-Command "az"
if (-not $UseAcrBuild) { Require-Command "docker" }

Write-Host ""
Write-Host "  PowerPoint Narration Generator — Deploy" -ForegroundColor Cyan
Write-Host "  ==========================================" -ForegroundColor DarkGray
Write-Host "  Resource Group : $ResourceGroup"
Write-Host "  ACR            : $AcrLogin"
Write-Host "  Backend Image  : $BackendImage"
Write-Host "  Frontend Image : $FrontendImage"
Write-Host "  Bicep template : infra/main.bicep"

# ── Build & push backend ──────────────────────────────────────────────────────
Write-Step 1 "Build + push backend image"

if ($UseAcrBuild) {
    az acr build `
        --registry $AcrName `
        --image "narrator-backend:$Tag" `
        --image "narrator-backend:latest" `
        --file (Join-Path $Root "backend-csharp\Dockerfile") `
        (Join-Path $Root "backend-csharp")
} else {
    az acr login --name $AcrName
    docker build -t $BackendImage -t "$AcrLogin/narrator-backend:latest" `
        -f (Join-Path $Root "backend-csharp\Dockerfile") (Join-Path $Root "backend-csharp")
    docker push $BackendImage
    docker push "$AcrLogin/narrator-backend:latest"
}
if ($LASTEXITCODE -ne 0) { Write-Error "Backend build/push failed."; exit 1 }

# ── Build & push frontend ─────────────────────────────────────────────────────
Write-Step 2 "Build + push frontend image"

if ($UseAcrBuild) {
    az acr build `
        --registry $AcrName `
        --image "narrator-frontend:$Tag" `
        --image "narrator-frontend:latest" `
        --file (Join-Path $Root "frontend\Dockerfile") `
        (Join-Path $Root "frontend")
} else {
    docker build -t $FrontendImage -t "$AcrLogin/narrator-frontend:latest" `
        -f (Join-Path $Root "frontend\Dockerfile") (Join-Path $Root "frontend")
    docker push $FrontendImage
    docker push "$AcrLogin/narrator-frontend:latest"
}
if ($LASTEXITCODE -ne 0) { Write-Error "Frontend build/push failed."; exit 1 }

# ── Bicep deployment ──────────────────────────────────────────────────────────
Write-Step 3 "Deploy Bicep template (infra/main.bicep)"

$paramFile = Join-Path $Root "infra\parameters.json"

az deployment group create `
    --resource-group $ResourceGroup `
    --template-file (Join-Path $Root "infra\main.bicep") `
    --parameters "@$paramFile" `
    --parameters containerRegistryName=$AcrName `
                 backendImage=$BackendImage `
                 frontendImage=$FrontendImage `
    --output json | ConvertFrom-Json | ForEach-Object {
        $outputs = $_.properties.outputs
        if ($outputs) {
            Write-Host ""
            Write-Host "  Deployment outputs:" -ForegroundColor Green
            Write-Host "    Backend  → $($outputs.backendUrl.value)"  -ForegroundColor Green
            Write-Host "    Frontend → $($outputs.frontendUrl.value)" -ForegroundColor Green
        }
    }

if ($LASTEXITCODE -ne 0) { Write-Error "Bicep deployment failed."; exit 1 }

Write-Host ""
Write-Host "  Deployment complete." -ForegroundColor Green
Write-Host ""
Write-Host "  NOTE: The backend Managed Identity needs 'Cognitive Services User' on the AI Foundry" -ForegroundColor Yellow
Write-Host "  resource to call Speech TTS/STT and OpenAI. Assign it once:" -ForegroundColor Yellow
Write-Host ""
Write-Host "    az role assignment create \\" -ForegroundColor DarkYellow
Write-Host "      --assignee <BACKEND_IDENTITY_PRINCIPAL_ID> \\" -ForegroundColor DarkYellow
Write-Host "      --role 'Cognitive Services User' \\" -ForegroundColor DarkYellow
Write-Host "      --scope /subscriptions/{subId}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/bhs-development-public-foundry-r" -ForegroundColor DarkYellow
Write-Host ""
