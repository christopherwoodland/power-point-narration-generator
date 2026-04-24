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

.PARAMETER AiResourceName
    Optional Azure AI / Cognitive Services account name for backend role assignment.
    If omitted, uses azureSpeechResourceName from infra/parameters.json.

.PARAMETER AiResourceGroup
    Optional resource group containing the Azure AI / Cognitive Services account.
    Defaults to -ResourceGroup.

.PARAMETER TtsMaxParallelism
    Optional override for AZURE_TTS_MAX_PARALLELISM passed to the backend Container App.
    Defaults to azureTtsMaxParallelism from infra/parameters.json, or 4 if omitted there.

.PARAMETER SkipRoleAssignment
    Skip automatic assignment of the backend managed identity role on Azure AI/Cognitive Services.

.EXAMPLE
    .\deploy.ps1 -ResourceGroup my-rg -AcrName myacr

.EXAMPLE
    .\deploy.ps1 -ResourceGroup my-rg -AcrName myacr -UseAcrBuild

.EXAMPLE
    .\deploy.ps1 -ResourceGroup my-rg -AcrName myacr -TtsMaxParallelism 6
#>
param(
    [Parameter(Mandatory)]
    [string] $ResourceGroup,

    [Parameter(Mandatory)]
    [string] $AcrName,

    [string] $Tag = (Get-Date -Format "yyyyMMdd-HHmmss"),
    [switch] $UseAcrBuild,
    [string] $AiResourceName = "",
    [string] $AiResourceGroup = "",
    [int] $TtsMaxParallelism = 0,
    [switch] $SkipRoleAssignment
)

$paramFile = ""

$ErrorActionPreference = "Stop"

# Resolve repo root (parent of the scripts/ folder this script lives in).
# Prefer $PSScriptRoot for Windows PowerShell compatibility.
$scriptDir = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
}
elseif ($MyInvocation.MyCommand.Path) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}
else {
    (Get-Location).Path
}
$Root = Split-Path -Parent $scriptDir
$AcrLogin = "$AcrName.azurecr.io"

$BackendImage = "$AcrLogin/narrator-backend:$Tag"
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

$paramFile = Join-Path $Root "infra\parameters.json"
if (-not (Test-Path $paramFile)) {
    Write-Error "Parameter file not found: $paramFile"
    exit 1
}

$paramJson = Get-Content $paramFile -Raw | ConvertFrom-Json
$environmentName = $paramJson.parameters.environmentName.value
if ([string]::IsNullOrWhiteSpace($environmentName)) { $environmentName = "prod" }

$resolvedAiResourceName = if ([string]::IsNullOrWhiteSpace($AiResourceName)) {
    $paramJson.parameters.azureSpeechResourceName.value
}
else {
    $AiResourceName
}

$resolvedAiResourceGroup = if ([string]::IsNullOrWhiteSpace($AiResourceGroup)) {
    $ResourceGroup
}
else {
    $AiResourceGroup
}

$resolvedTtsMaxParallelism = if ($TtsMaxParallelism -gt 0) {
    $TtsMaxParallelism
}
elseif ($null -ne $paramJson.parameters.azureTtsMaxParallelism -and [int]$paramJson.parameters.azureTtsMaxParallelism.value -gt 0) {
    [int]$paramJson.parameters.azureTtsMaxParallelism.value
}
else {
    4
}

Write-Host "  TTS Parallelism: $resolvedTtsMaxParallelism"

# ── Build & push backend ──────────────────────────────────────────────────────
Write-Step 1 "Build + push backend image"

if ($UseAcrBuild) {
    az acr build `
        --registry $AcrName `
        --image "narrator-backend:$Tag" `
        --image "narrator-backend:latest" `
        --file (Join-Path $Root "backend-csharp\Dockerfile") `
    (Join-Path $Root "backend-csharp")
}
else {
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
}
else {
    docker build -t $FrontendImage -t "$AcrLogin/narrator-frontend:latest" `
        -f (Join-Path $Root "frontend\Dockerfile") (Join-Path $Root "frontend")
    docker push $FrontendImage
    docker push "$AcrLogin/narrator-frontend:latest"
}
if ($LASTEXITCODE -ne 0) { Write-Error "Frontend build/push failed."; exit 1 }

# ── Bicep deployment ──────────────────────────────────────────────────────────
Write-Step 3 "Deploy Bicep template (infra/main.bicep)"

$deployment = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file (Join-Path $Root "infra\main.bicep") `
    --parameters "@$paramFile" `
    --parameters containerRegistryName=$AcrName `
    backendImage=$BackendImage `
    frontendImage=$FrontendImage `
    azureTtsMaxParallelism=$resolvedTtsMaxParallelism `
    --output json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) { Write-Error "Bicep deployment failed."; exit 1 }

$outputs = $deployment.properties.outputs
if ($outputs) {
    Write-Host ""
    Write-Host "  Deployment outputs:" -ForegroundColor Green
    Write-Host "    Backend  → $($outputs.backendUrl.value)" -ForegroundColor Green
    Write-Host "    Frontend → $($outputs.frontendUrl.value)" -ForegroundColor Green
}

# Keep frontend BACKEND_URL in sync even when updating an existing environment.
Write-Step 4 "Ensure frontend BACKEND_URL is configured"

$backendUrl = $outputs.backendUrl.value
$frontendAppName = "narrator-$environmentName-frontend"
if (-not [string]::IsNullOrWhiteSpace($backendUrl)) {
    az containerapp update `
        --resource-group $ResourceGroup `
        --name $frontendAppName `
        --set-env-vars "BACKEND_URL=$backendUrl" `
        --only-show-errors | Out-Null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  BACKEND_URL updated on $frontendAppName" -ForegroundColor Green
    }
    else {
        Write-Warning "Failed to update BACKEND_URL on $frontendAppName."
    }
}
else {
    Write-Warning "Backend URL not found in deployment outputs; skipped BACKEND_URL update."
}

# Assign backend managed identity role for Azure AI/Cognitive Services access.
if (-not $SkipRoleAssignment) {
    Write-Step 5 "Assign backend identity role on Azure AI/Cognitive Services"

    $backendPrincipalId = $outputs.backendIdentityPrincipalId.value
    if ([string]::IsNullOrWhiteSpace($backendPrincipalId)) {
        Write-Warning "Backend identity principal ID not found in outputs; skipped role assignment."
    }
    elseif ([string]::IsNullOrWhiteSpace($resolvedAiResourceName)) {
        Write-Warning "Azure AI resource name is empty; skipped role assignment."
    }
    else {
        $aiScope = az cognitiveservices account show `
            --resource-group $resolvedAiResourceGroup `
            --name $resolvedAiResourceName `
            --query id -o tsv 2>$null

        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($aiScope)) {
            Write-Warning "Could not resolve Azure AI resource scope for '$resolvedAiResourceName' in '$resolvedAiResourceGroup'."
        }
        else {
            $existingRoleId = az role assignment list `
                --assignee-object-id $backendPrincipalId `
                --scope $aiScope `
                --role "Cognitive Services User" `
                --query "[0].id" -o tsv

            if ([string]::IsNullOrWhiteSpace($existingRoleId)) {
                az role assignment create `
                    --assignee-object-id $backendPrincipalId `
                    --assignee-principal-type ServicePrincipal `
                    --role "Cognitive Services User" `
                    --scope $aiScope `
                    --only-show-errors | Out-Null

                if ($LASTEXITCODE -eq 0) {
                    Write-Host "  Assigned 'Cognitive Services User' to backend managed identity." -ForegroundColor Green
                }
                else {
                    Write-Warning "Automatic role assignment failed. Ensure deployer has roleAssignments/write (Owner or User Access Administrator)."
                }
            }
            else {
                Write-Host "  Backend identity already has 'Cognitive Services User' on Azure AI resource." -ForegroundColor Green
            }
        }
    }
}

Write-Host ""
Write-Host "  Deployment complete." -ForegroundColor Green

