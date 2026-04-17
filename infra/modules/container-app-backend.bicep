// ────────────────────────────────────────────────────────────────────────────
// container-app-backend.bicep — C# Web API Container App with Managed Identity
// ────────────────────────────────────────────────────────────────────────────

param location string
param environmentName string
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
param azureOpenAiEndpoint string
param azureOpenAiDeployment string
param azureImageDeployment string
param azureDocIntelEndpoint string
param appBannerMessage string

var prefix = 'narrator-${environmentName}'
var appName = '${prefix}-backend'

// ── Container Registry reference ─────────────────────────────────────────────
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
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
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d') // AcrPull
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
            { name: 'ASPNETCORE_ENVIRONMENT',        value: 'Production' }
            { name: 'ASPNETCORE_URLS',                value: 'http://+:8080' }
            { name: 'ENABLE_QUALITY_CHECK',           value: string(enableQualityCheck) }
            { name: 'ENABLE_AI_MODE',                 value: string(enableAiMode) }
            { name: 'ENABLE_VIDEO_EXPORT',            value: string(enableVideoExport) }
            { name: 'AZURE_SPEECH_RESOURCE_NAME',     value: azureSpeechResourceName }
            { name: 'AZURE_SPEECH_REGION',            value: azureSpeechRegion }
            { name: 'AZURE_OPENAI_ENDPOINT',          value: azureOpenAiEndpoint }
            { name: 'AZURE_OPENAI_DEPLOYMENT',        value: azureOpenAiDeployment }
            { name: 'AZURE_IMAGE_DEPLOYMENT',         value: azureImageDeployment }
            { name: 'AZURE_DOC_INTEL_ENDPOINT',       value: azureDocIntelEndpoint }
            { name: 'APP_BANNER_MESSAGE',             value: appBannerMessage }
            // Tell the C# app which managed identity client ID to use
            { name: 'AZURE_CLIENT_ID',                value: identity.properties.clientId }
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
  dependsOn: [acrPull]
}

output fqdn string = 'https://${backendApp.properties.configuration.ingress.fqdn}'
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
