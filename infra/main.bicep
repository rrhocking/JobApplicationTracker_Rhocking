// Job Tracker infrastructure.
//
// Deploys into an EXISTING resource group (create it first, see infra/README.md):
//   Log Analytics workspace  -> where the app's logs go (capped so it cannot run up a bill)
//   SQL server + database    -> Azure SQL Database on the FREE offer, so data survives redeploys
//   Container Apps environment -> the shared, serverless hosting space
//   Container app            -> runs the Docker image, scales to zero when nobody is visiting

@description('Azure region. Defaults to the resource group\'s region.')
param location string = resourceGroup().location

@description('Region for the SQL server. New subscriptions are sometimes blocked from creating SQL servers in a busy region; if the deployment says so, set this to another region.')
param sqlLocation string = location

@description('Base name used for the resources.')
@minLength(3)
@maxLength(20)
param appName string = 'jobtracker'

@description('Full image reference to run, for example ghcr.io/<owner>/<repo>:<tag>. The image must be public.')
param image string

@description('Administrator login name for the SQL server. "admin" and "sa" are not allowed by Azure.')
param sqlAdminLogin string = 'jobtrackeradmin'

@description('Administrator password for the SQL server. Passed in at deploy time from a GitHub secret; never stored in this repository. Letters and numbers only, so it is safe inside a connection string.')
@secure()
@minLength(12)
param sqlAdminPassword string

var databaseName = 'jobtracker'

// ---------------------------------------------------------------------------
// Logs
// ---------------------------------------------------------------------------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${appName}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      // Hard daily limit on log ingestion (in GB) so logs can never become a surprise cost.
      dailyQuotaGb: json('0.1')
    }
  }
}

// ---------------------------------------------------------------------------
// Database: Azure SQL Database, free offer
// ---------------------------------------------------------------------------
// The server is the "logical" front door (name, login, firewall). The database lives inside it.
// The name has to be unique across all of Azure, so a short hash of the resource group id is added.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${appName}-${uniqueString(resourceGroup().id)}'
  location: sqlLocation
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// This special rule (0.0.0.0 to 0.0.0.0) means "allow other Azure services", which is how the container
// app reaches the database. The password and TLS still protect it. A stricter setup would use a private
// network, which is more than this project needs.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: sqlLocation
  // Serverless General Purpose: it pauses itself when idle and wakes on the first connection.
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2 // most vCores it may use
  }
  properties: {
    useFreeLimit: true // the free monthly allowance (one free database per subscription)
    freeLimitExhaustionBehavior: 'AutoPause' // when the month's free allowance is used up, pause instead of billing
    autoPauseDelay: 60 // minutes idle before it pauses
    minCapacity: json('0.5') // vCores kept while running
    maxSizeBytes: 34359738368 // 32 GB, the free limit
    requestedBackupStorageRedundancy: 'Local'
  }
}

// ---------------------------------------------------------------------------
// Container Apps environment (Consumption plan: you pay only while the app is running)
// ---------------------------------------------------------------------------
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${appName}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// ---------------------------------------------------------------------------
// The app itself
// ---------------------------------------------------------------------------
// The app reads its connection string from a Container Apps secret (kept out of the plain settings and logs).
// Connect Timeout is long on purpose: a paused free database takes a while to wake up.
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${databaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${appName}'
  location: location
  // The database and its firewall rule must exist before the app starts and tries to connect.
  dependsOn: [
    database
    allowAzureServices
  ]
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [
        {
          name: 'sql-connection'
          value: sqlConnectionString
        }
      ]
      ingress: {
        external: true
        targetPort: 8080 // the port the Dockerfile exposes
        transport: 'auto'
        allowInsecure: false // plain http is redirected to https
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          // Settings are environment variables, the same idea as the Dockerfile's ENV lines.
          // These override the image's SQLite defaults, so the app uses Azure SQL instead.
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'Database__Provider'
              value: 'SqlServer'
            }
            {
              name: 'ConnectionStrings__JobTracker'
              secretRef: 'sql-connection'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0 // scale to zero: no cost while idle
        maxReplicas: 1 // one copy only, so Blazor sessions stay simple
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

@description('Public address of the app.')
output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'

@description('Name of the SQL server (the database is called jobtracker).')
output sqlServerName string = sqlServer.name
