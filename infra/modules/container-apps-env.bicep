// ────────────────────────────────────────────────────────────────────────────
// container-apps-env.bicep — Log Analytics + Container Apps Environment
// ────────────────────────────────────────────────────────────────────────────

param location string
param environmentName string

var prefix = 'narrator-${environmentName}'

// ── Log Analytics Workspace ───────────────────────────────────────────────────
resource law 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${prefix}-law'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ── Container Apps Managed Environment ───────────────────────────────────────
resource env 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: law.properties.customerId
        sharedKey: law.listKeys().primarySharedKey
      }
    }
  }
}

output environmentId string = env.id
output environmentName string = env.name
