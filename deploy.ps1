<#
.SYNOPSIS
    Build, push to ACR, and deploy the PowerPoint Narration Generator
    to an Azure Container App.

.PARAMETER ResourceGroup
    Azure resource group containing the Container App environment.

.PARAMETER ContainerAppEnv
    Name of the existing Container Apps environment to deploy into.

.PARAMETER AppName
    Name to give the Container App (created if it does not exist).

.PARAMETER Tag
    Docker image tag. Defaults to "latest".

.EXAMPLE
    .\deploy.ps1 -ResourceGroup "my-rg" -ContainerAppEnv "my-env"
#>
param(
    [Parameter(Mandatory)]
    [string]$ResourceGroup,

    [Parameter(Mandatory)]
    [string]$ContainerAppEnv,

    [string]$AppName  = "pptx-narration",
    [string]$Tag      = "latest"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ACR       = "bhsdevelopmentacr4znv2wxlxs4xq"
$ACR_LOGIN = "$ACR.azurecr.io"

# Use a timestamp tag so every deploy creates a distinct image reference,
# forcing Azure Container Apps to create a new revision (instead of ignoring
# a re-push to ':latest' which has the same reference string).
if ($Tag -eq "latest") {
    $Tag = Get-Date -Format "yyyyMMdd-HHmmss"
}
$IMAGE       = "$ACR_LOGIN/${AppName}:$Tag"
$IMAGE_LATEST = "$ACR_LOGIN/${AppName}:latest"

# ── 0. Prereq checks ─────────────────────────────────────────────────────────
foreach ($cmd in "docker", "az") {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Error "$cmd is not installed or not on PATH."
        exit 1
    }
}

Write-Host ""
Write-Host "  PowerPoint Narration Generator — Deploy" -ForegroundColor Cyan
Write-Host "  ==========================================" -ForegroundColor Cyan
Write-Host "  ACR    : $ACR_LOGIN"
Write-Host "  Image  : $IMAGE"
Write-Host "  RG     : $ResourceGroup"
Write-Host "  Env    : $ContainerAppEnv"
Write-Host "  App    : $AppName"
Write-Host ""

# ── 1. Docker build ───────────────────────────────────────────────────────────
Write-Host "[1/5] Building Docker image (tag: $Tag)..." -ForegroundColor Yellow
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
docker build -t $IMAGE -t $IMAGE_LATEST $root
if ($LASTEXITCODE -ne 0) { Write-Error "docker build failed"; exit 1 }

# ── 2. ACR login & push ───────────────────────────────────────────────────────
Write-Host "[2/5] Logging into ACR (uses your az login credentials)..." -ForegroundColor Yellow
az acr login --name $ACR
if ($LASTEXITCODE -ne 0) { Write-Error "az acr login failed"; exit 1 }

Write-Host "[3/5] Pushing image to ACR..." -ForegroundColor Yellow
docker push $IMAGE
if ($LASTEXITCODE -ne 0) { Write-Error "docker push failed"; exit 1 }
docker push $IMAGE_LATEST | Out-Null  # also update :latest pointer (best-effort)

# ── 3. Create or update Container App ────────────────────────────────────────
# az containerapp extension always writes to stderr (WARNING) which trips ErrorActionPreference=Stop.
# Locally suppress and restore around every az containerapp call.
$ErrorActionPreference = "Continue"
$exists = az containerapp show --name $AppName --resource-group $ResourceGroup `
          --query "name" -o tsv 2>$null
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($exists)) {
    Write-Host "[4/5] Creating Container App '$AppName'..." -ForegroundColor Yellow

    $ErrorActionPreference = "Continue"
    az containerapp create `
        --name $AppName `
        --resource-group $ResourceGroup `
        --environment $ContainerAppEnv `
        --image $IMAGE `
        --ingress external `
        --target-port 8000 `
        --min-replicas 1 `
        --max-replicas 5 `
        --cpu 1.0 `
        --memory 2.0Gi `
        --registry-server $ACR_LOGIN `
        --system-assigned `
        --env-vars "AZURE_TTS_ENDPOINT=https://eastus2.tts.speech.microsoft.com/cognitiveservices/v1" `
                   "APP_BANNER_MESSAGE=This is a public tenant. Avoid sharing sensitive or proprietary information here."
    $ErrorActionPreference = "Stop"

    if ($LASTEXITCODE -ne 0) { Write-Error "containerapp create failed"; exit 1 }

    # Grant the new managed identity AcrPull on the ACR
    Write-Host "   → Granting AcrPull to managed identity on ACR..." -ForegroundColor DarkYellow
    $ErrorActionPreference = "Continue"
    $principalId = az containerapp show --name $AppName --resource-group $ResourceGroup `
                   --query "identity.principalId" -o tsv
    $acrId = az acr show --name $ACR --query "id" -o tsv
    az role assignment create `
        --assignee $principalId `
        --role AcrPull `
        --scope $acrId | Out-Null
    $ErrorActionPreference = "Stop"

} else {
    Write-Host "[4/5] Updating existing Container App '$AppName'..." -ForegroundColor Yellow

    $ErrorActionPreference = "Continue"
    az containerapp update `
        --name $AppName `
        --resource-group $ResourceGroup `
        --image $IMAGE `
        --set-env-vars "APP_BANNER_MESSAGE=This is a public tenant. Avoid sharing sensitive or proprietary information here."

    # Note: az containerapp update may exit 1 due to extension warnings even on success.
    # Verify by checking provisioningState instead.
    $state = az containerapp show --name $AppName --resource-group $ResourceGroup `
             --query "properties.provisioningState" -o tsv 2>$null
    $ErrorActionPreference = "Stop"
    if ($state -ne "Succeeded") { Write-Error "containerapp update failed (state=$state)"; exit 1 }
}

# ── 4. Print managed identity info for Speech RBAC ───────────────────────────
Write-Host "[5/5] Fetching app details..." -ForegroundColor Yellow

$ErrorActionPreference = "Continue"
$fqdn        = az containerapp show --name $AppName --resource-group $ResourceGroup `
               --query "properties.configuration.ingress.fqdn" -o tsv
$principalId = az containerapp show --name $AppName --resource-group $ResourceGroup `
               --query "identity.principalId" -o tsv
$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "  ✅  Deployed!" -ForegroundColor Green
Write-Host "  URL : https://$fqdn" -ForegroundColor Green
Write-Host ""
Write-Host "  ─────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  NEXT STEP — Grant Speech access to the managed identity:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Managed Identity Principal ID: $principalId"
Write-Host ""
Write-Host "  Run the following (replace <speech-resource-id>):"
Write-Host ""
Write-Host "    az role assignment create \"" -ForegroundColor Cyan
Write-Host "      --assignee $principalId \"" -ForegroundColor Cyan
Write-Host "      --role 'Cognitive Services User' \"" -ForegroundColor Cyan
Write-Host "      --scope {speech-resource-id}" -ForegroundColor Cyan
Write-Host ""
Write-Host "  To get your Speech resource ID:"
Write-Host "    az cognitiveservices account show -n bhs-development-public-foundry-r -g {rg} --query id -o tsv" -ForegroundColor DarkGray
Write-Host "  ─────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""
