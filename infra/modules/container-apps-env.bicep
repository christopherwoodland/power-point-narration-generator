// ────────────────────────────────────────────────────────────────────────────
// container-apps-env.bicep — Log Analytics + Container Apps Environment +
//   Blob Storage for UI branding (Entra ID / RBAC — no shared keys)
// ────────────────────────────────────────────────────────────────────────────

param location string
param environmentName string

@description('Optional suffix appended to every resource name (e.g. "v2"). Leave blank for no suffix.')
param resourceSuffix string = ''

var prefix = 'narrator-${environmentName}${empty(resourceSuffix) ? '' : '-${resourceSuffix}'}'
// Storage account names: lowercase alphanumeric only, 3-24 chars
var storageAccountBase = replace('${prefix}stg', '-', '')
var storageAccountName = toLower(take('${storageAccountBase}abc', 24))

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

// ── Storage Account (Blob — Entra ID / RBAC only, no shared keys) ─────────────
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // Entra ID / RBAC only — storage account keys are disabled
  }
}

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource brandingContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobServices
  name: 'branding-data'
  properties: {
    publicAccess: 'None'
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
output storageAccountName string = storageAccount.name
