// ────────────────────────────────────────────────────────────────────────────
// container-apps-env.bicep — Log Analytics + Container Apps Environment + Storage
// ────────────────────────────────────────────────────────────────────────────

param location string
param environmentName string

var prefix = 'narrator-${environmentName}'
// Storage account names: lowercase alphanumeric only, 3-24 chars
var storageAccountName = toLower(take(replace('${prefix}stg', '-', ''), 24))

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

// ── Storage Account + File Share (persistent UI branding data) ───────────────
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

resource fileServices 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource fileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  parent: fileServices
  name: 'branding-data'
  properties: {
    shareQuota: 1 // 1 GiB — only a small JSON file
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

// Mount the Azure File Share into the Container Apps Environment
resource storageMount 'Microsoft.App/managedEnvironments/storages@2023-05-01' = {
  parent: env
  name: 'branding-storage'
  properties: {
    azureFile: {
      accountName: storageAccount.name
      accountKey: storageAccount.listKeys().keys[0].value
      shareName: fileShare.name
      accessMode: 'ReadWrite'
    }
  }
}

output environmentId string = env.id
output environmentName string = env.name
output storageVolumeName string = storageMount.name
