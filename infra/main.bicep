// ────────────────────────────────────────────────────────────────────────────
// main.bicep — orchestrates all PptxNarrator Container App resources
// Deploy with:  az deployment group create -g <rg> -f infra/main.bicep \
//                 --parameters @infra/parameters.json
// ────────────────────────────────────────────────────────────────────────────

targetScope = 'resourceGroup'

// ── Parameters ───────────────────────────────────────────────────────────────
@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Short alphanumeric environment name, used in resource names')
@maxLength(12)
param environmentName string = 'prod'

@description('Name of the Azure Container Registry that holds the images')
param containerRegistryName string

@description('Backend image reference, e.g. myregistry.azurecr.io/narrator-backend:latest')
param backendImage string

@description('Frontend image reference, e.g. myregistry.azurecr.io/narrator-frontend:latest')
param frontendImage string

// ── Feature flag parameters (propagated to Container App env vars) ───────────
@description('Enable quality-check feature')
param enableQualityCheck bool = true

@description('Enable AI presentation generation feature')
param enableAiMode bool = true

@description('Enable video export feature')
param enableVideoExport bool = true

@description('Whether backend ingress is publicly accessible')
param backendExternalIngress bool = true

@description('Allowed CORS origins for backend ingress')
param backendCorsAllowedOrigins array = [
  '*'
]

// ── Azure service parameters ─────────────────────────────────────────────────
@description('Azure Cognitive Services / AI Foundry resource name used for Speech/OpenAI')
param azureSpeechResourceName string = 'bhs-development-public-foundry-r'

@description('Azure Speech service region')
param azureSpeechRegion string = 'eastus2'

@description('Max number of slide TTS operations to run concurrently')
@minValue(1)
param azureTtsMaxParallelism int = 4

@description('Azure OpenAI endpoint URL (leave empty if not using AI mode)')
param azureOpenAiEndpoint string = ''

@description('Azure OpenAI chat deployment name')
param azureOpenAiDeployment string = 'gpt-4o'

@description('Azure OpenAI image generation deployment name')
param azureImageDeployment string = 'dall-e-3'

@description('Azure Document Intelligence endpoint URL (optional)')
param azureDocIntelEndpoint string = ''

@description('Optional banner message shown in the UI')
param appBannerMessage string = ''

// ── Modules ───────────────────────────────────────────────────────────────────
module containerAppsEnv 'modules/container-apps-env.bicep' = {
  name: 'containerAppsEnv'
  params: {
    location: location
    environmentName: environmentName
  }
}

module backendApp 'modules/container-app-backend.bicep' = {
  name: 'backendApp'
  params: {
    location: location
    environmentName: environmentName
    containerAppsEnvironmentId: containerAppsEnv.outputs.environmentId
    containerRegistryName: containerRegistryName
    backendImage: backendImage
    enableQualityCheck: enableQualityCheck
    enableAiMode: enableAiMode
    enableVideoExport: enableVideoExport
    backendExternalIngress: backendExternalIngress
    backendCorsAllowedOrigins: backendCorsAllowedOrigins
    corsAllowedOrigins: backendCorsAllowedOrigins
    azureSpeechResourceName: azureSpeechResourceName
    azureSpeechRegion: azureSpeechRegion
    azureTtsMaxParallelism: azureTtsMaxParallelism
    azureOpenAiEndpoint: azureOpenAiEndpoint
    azureOpenAiDeployment: azureOpenAiDeployment
    azureImageDeployment: azureImageDeployment
    azureDocIntelEndpoint: azureDocIntelEndpoint
    appBannerMessage: appBannerMessage
  }
}

module frontendApp 'modules/container-app-frontend.bicep' = {
  name: 'frontendApp'
  params: {
    location: location
    environmentName: environmentName
    containerAppsEnvironmentId: containerAppsEnv.outputs.environmentId
    containerRegistryName: containerRegistryName
    frontendImage: frontendImage
    backendUrl: backendApp.outputs.fqdn
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output backendUrl string = backendApp.outputs.fqdn
output frontendUrl string = frontendApp.outputs.fqdn
output backendIdentityPrincipalId string = backendApp.outputs.identityPrincipalId
output backendIdentityClientId string = backendApp.outputs.identityClientId
