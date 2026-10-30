// Job Tracker infrastructure.
//
// Deploys into an EXISTING resource group (create it first, see infra/README.md):
//   Log Analytics workspace  -> where the app's logs go (capped so it cannot run up a bill)
//   Container Apps environment -> the shared, serverless hosting space
//   Container app            -> runs the Docker image, scales to zero when nobody is visiting

@description('Azure region. Defaults to the resource group\'s region.')
param location string = resourceGroup().location

@description('Base name used for the resources.')
@minLength(3)
@maxLength(20)
param appName string = 'jobtracker'

@description('Full image reference to run, for example ghcr.io/<owner>/<repo>:<tag>. The image must be public.')
param image string

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
resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-${appName}'
  location: location
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
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
          // The database path is already set in the image (SQLite at /data, which is temporary here).
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0 // scale to zero: no cost while idle
        maxReplicas: 1 // one copy only, so SQLite and Blazor sessions stay simple
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
