# Azure Service Bus Replicator

This service runs as a background process that provides unidirectional message replication between two Azure Service Bus namespaces. It ensures all topics in the source namespace have a subscription that it consumes from, and republishes messages to the same-named topics in the target namespace. The service does not expose any API endpoints except for a health check - it operates autonomously once started.

## Features

- Dynamically discovers all topics in the source namespace
- Automatically creates and configures replication subscriptions
- Preserves message properties and content
- Handles TTL calculation and forwarding
- Uses Azure Key Vault for secure connection string storage
- Provides health check endpoint

## Prerequisites

- .NET 8.0 SDK
- Azure Service Bus namespaces (source and target)
  - Must have SAS authentication enabled via `local_auth_enabled = true` in Terraform
  - Note: In Azure Portal/API this appears as `disableLocalAuth = false` which is correct
- Azure Key Vault
- Azure Managed Identity with appropriate permissions

## Infrastructure Management Policy

**⚠️ IMPORTANT**: All infrastructure changes MUST be made through Terraform only.

- DO NOT use Azure CLI, Azure Portal, or PowerShell to create or modify any infrastructure
- All Service Bus namespaces, topics, subscriptions, and Key Vault resources must be created via Terraform
- Any manual changes made outside of Terraform will be overwritten on the next `terraform apply`
- Infrastructure state must be maintained in the Terraform state files
- All infrastructure changes must go through proper code review process

### Service Bus Authentication

The service uses a hybrid authentication approach:
1. Azure AD authentication for management operations (creating/updating topics and subscriptions)
2. SAS authentication for message operations (sending/receiving messages)

This requires specific configuration:
- Service Bus namespaces:
  - Must have SAS authentication enabled (`local_auth_enabled = true` in Terraform)
  - Must allow Azure AD authentication (default)
  - The API must have an identity with "Azure Service Bus Data Owner" role
- Connection Strings:
  - Stored in Azure Key Vault
  - Used only for message operations, not management
  - Created automatically by Terraform

### Important Note About Authentication Settings
- In Terraform: `local_auth_enabled = true`
- In Azure Portal/API: `disableLocalAuth = false`
- Both settings mean the same thing: SAS authentication is enabled
- Do not change these settings as they are required for message operations to work

The Terraform configurations in the `terraform/` directory are the single source of truth for all infrastructure.

## Configuration

The service requires the following configuration in `appsettings.json`:

```json
{
  "AzureServiceBus": {
    "ConnectionString": {
      "Key": "AzureServiceBus:ConnectionString:Value"
    },
    "ConnectionString2": {
      "Key": "AzureServiceBus:ConnectionString2:Value"
    }
  },
  "Replication": {
    "SubscriptionName": "replicationapi",
    "DefaultTTLMinutes": 10
  },
  "AzureKeyVault": {
    "VaultUri": "https://your-keyvault-name.vault.azure.net/"
  }
}
```

The service uses SAS authentication with connection strings stored in Azure Key Vault:
1. The appsettings.json contains Key Vault connection information and the names of the secrets
2. The Key Vault secrets contain the actual SAS connection strings:
   - `AzureServiceBus-ConnectionString-Value`: Source namespace SAS connection string
   - `AzureServiceBus-ConnectionString2-Value`: Target namespace SAS connection string
3. Important authentication notes:
   - Terraform configures namespaces with `local_auth_enabled = true`
   - In Azure Portal/API this appears as `disableLocalAuth = false`
   - Both settings mean the same thing: SAS authentication is enabled
   - Do not change these settings as they are required for the connection strings to work

These SAS connection strings are automatically created and stored in Key Vault by the Terraform infrastructure deployment.

## Running the Service

1. Update the `appsettings.json` with your Key Vault URI
2. Ensure your Azure Managed Identity has access to:
   - Key Vault secrets
   - Source Service Bus namespace (manage)
   - Target Service Bus namespace (send)
3. Run the service:
   ```bash
   dotnet run --project Exactitude.ServiceBus.Replicator.Api
   ```

## Health Check

The service exposes a health check endpoint at:
```
GET /health
```

## Development

To run the tests:
```bash
dotnet test
```

## Architecture

The service consists of three projects:
- `Exactitude.ServiceBus.Replicator`: Core replication library
- `Exactitude.ServiceBus.Replicator.Api`: ASP.NET Core Web API host
- `Exactitude.ServiceBus.Replicator.Tests`: Test project

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Testing the Service Bus Replication

### Prerequisites
- Ensure Azure infrastructure is deployed using Terraform (`terraform apply` in the terraform directory)
- Have access to Azure resources (Service Bus namespaces, Key Vault)
- .NET 8.0 SDK installed

### Running the Tests

1. **Start the Replication Service:**
   ```sh
   cd Exactitude.ServiceBus.Replicator.Api
   dotnet run
   ```
   The service will start and begin monitoring topics for messages.

2. **Send a Test Message:**
   ```sh
   cd TestMessageSender
   dotnet run
   ```
   This will send a test message to the source Service Bus topic with:
   - Content: "Test message for replication"
   - TTL: 10 minutes (configured in appsettings.json)
   - Custom properties for testing

3. **Run Integration Tests:**
   ```sh
   dotnet test --filter "FullyQualifiedName~Exactitude.ServiceBus.Replicator.Tests.IntegrationTests"
   ```
   This validates the end-to-end replication flow.

### Steps
1. **Start the Replication API:**
   ```sh
   cd Exactitude.ServiceBus.Replicator.Api
   dotnet run
   ```
   Leave this running in a terminal window.

2. **Run the Integration Test:**
   In a new terminal, from the project root, run:
   ```sh
   dotnet test --filter FullyQualifiedName~Exactitude.ServiceBus.Replicator.Tests.IntegrationTests
   ```
   This will execute the integration test that verifies message replication from the source to the target Service Bus topic.

3. **Check Logs:**
   Detailed logs are written to `logs/integration-tests.log` for troubleshooting and verification.

### Troubleshooting
- If the test fails at the API health check, ensure the API is running and accessible at `http://localhost:5000`.
- If the test fails at message replication, check the API logs and Service Bus configuration.

## Infrastructure Management

⚠️ **IMPORTANT: Infrastructure Management Policy** ⚠️

All infrastructure changes MUST be made through Terraform:
- DO NOT use Azure CLI commands to modify infrastructure
- DO NOT make changes through the Azure Portal
- DO NOT use Azure PowerShell cmdlets
- DO NOT use Azure REST APIs directly

All infrastructure changes must be:
1. Made in the terraform files under the `terraform/` directory
2. Reviewed through pull requests
3. Applied using `terraform apply`

This policy ensures:
- Infrastructure changes are version controlled
- Changes are reproducible across environments
- Infrastructure state remains consistent
- All changes are documented through Terraform code

Violating this policy by making direct changes to infrastructure may result in:
- Infrastructure state inconsistencies
- Failed deployments
- Service outages
- Unauthorized or undocumented changes
