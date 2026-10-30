# Infrastructure

Azure resources for the Job Tracker, written in [Bicep](https://learn.microsoft.com/azure/azure-resource-manager/bicep/overview).

| Resource | Why |
| --- | --- |
| Log Analytics workspace | Stores the app's logs. A daily cap keeps it from becoming a cost. |
| Container Apps environment | Shared serverless hosting space (Consumption plan). |
| Container app | Runs the Docker image on port 8080 and scales to zero when idle. |

The image comes from GitHub Container Registry (`ghcr.io`), so it must be public and must be
published before the container app is created.

## Try it from your own computer

You need the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) and a signed-in session
(`az login`).

```bash
# One-time: make sure the services this uses are registered on the subscription
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights

# Create an empty resource group to hold everything
az group create --name rg-jobtracker --location eastus

# Preview what would be created (changes nothing)
az deployment group what-if --resource-group rg-jobtracker --parameters infra/main.bicepparam

# Create it for real
az deployment group create --resource-group rg-jobtracker --parameters infra/main.bicepparam

# Remove everything when you are done (stops all charges)
az group delete --name rg-jobtracker --yes
```

Keeping everything in one resource group means one command cleans it all up.
