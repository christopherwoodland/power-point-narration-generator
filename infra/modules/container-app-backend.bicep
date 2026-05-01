// ────────────────────────────────────────────────────────────────────────────
// container-app-backend.bicep — C# Web API Container App with Managed Identity
// ────────────────────────────────────────────────────────────────────────────

param location string
param environmentName string

@description('Optional suffix appended to every resource name (e.g. "v2"). Leave blank for no suffix.')
param resourceSuffix string = ''

param containerAppsEnvironmentId string
param containerRegistryName string
param backendImage string

// Feature flags
param enableQualityCheck bool
param enableAiMode bool
param enableVideoExport bool
param backendExternalIngress bool
param backendCorsAllowedOrigins array

// Azure service config
param azureSpeechResourceName string
param azureSpeechRegion string
param azureTtsMaxParallelism int
param azureOpenAiEndpoint string
param azureOpenAiDeployment string
param azureImageDeployment string
param azureDocIntelEndpoint string
param appBannerMessage string
param defaultSinglePptxMode bool
param corsAllowedOrigins array = ['*']

@description('Name of the storage account used for Blob-backed UI branding (Entra RBAC, no shared keys).')
param brandingStorageAccountName string

var prefix = 'narrator-${environmentName}${empty(resourceSuffix) ? '' : '-${resourceSuffix}'}'
var appName = '${prefix}-backend'

// ── Container Registry reference ─────────────────────────────────────────────
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

// ── Branding Storage Account reference (for RBAC assignment) ─────────────────
resource brandingStorage 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: brandingStorageAccountName
}

// ── Managed Identity (used for DefaultAzureCredential) ───────────────────────
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-id'
  location: location
}

// Grant AcrPull to the managed identity on the container registry
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, 'acrpull')
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d'
    ) // AcrPull
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant Storage Blob Data Contributor to the managed identity on the branding storage account.
// This allows Entra-authenticated reads and writes to the branding-data blob container.
// No storage account keys are used.
resource blobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(brandingStorage.id, identity.id, 'storageblobdatacontributor')
  scope: brandingStorage
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
    ) // Storage Blob Data Contributor
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Container App ─────────────────────────────────────────────────────────────
resource backendApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: backendExternalIngress
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        corsPolicy: {
          allowedOrigins: backendCorsAllowedOrigins
          allowedMethods: ['GET', 'POST', 'OPTIONS']
          allowedHeaders: ['*']
        }
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'backend'
          image: backendImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ENABLE_QUALITY_CHECK', value: string(enableQualityCheck) }
            { name: 'ENABLE_AI_MODE', value: string(enableAiMode) }
            { name: 'ENABLE_VIDEO_EXPORT', value: string(enableVideoExport) }
            { name: 'CORS_ALLOWED_ORIGINS', value: join(corsAllowedOrigins, ',') }
            { name: 'AZURE_SPEECH_RESOURCE_NAME', value: azureSpeechResourceName }
            { name: 'AZURE_SPEECH_REGION', value: azureSpeechRegion }
            { name: 'AZURE_TTS_MAX_PARALLELISM', value: string(azureTtsMaxParallelism) }
            { name: 'AZURE_OPENAI_ENDPOINT', value: azureOpenAiEndpoint }
            { name: 'AZURE_OPENAI_DEPLOYMENT', value: azureOpenAiDeployment }
            { name: 'AZURE_IMAGE_DEPLOYMENT', value: azureImageDeployment }
            { name: 'AZURE_DOC_INTEL_ENDPOINT', value: azureDocIntelEndpoint }
            { name: 'APP_BANNER_MESSAGE', value: appBannerMessage }
            { name: 'DEFAULT_SINGLE_PPTX_MODE', value: string(defaultSinglePptxMode) }
            // Blob Storage for UI branding — Entra auth via managed identity (no keys)
            { name: 'AZURE_BRANDING_STORAGE_ACCOUNT', value: brandingStorageAccountName }
            { name: 'AZURE_BRANDING_CONTAINER', value: 'branding-data' }
            // Tell the C# app which managed identity client ID to use
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-scale'
            http: { metadata: { concurrentRequests: '20' } }
          }
        ]
      }
    }
  }
  dependsOn: [acrPull, blobDataContributor]
}

output fqdn string = 'https://${backendApp.properties.configuration.ingress.fqdn}'
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
