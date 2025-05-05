# Azure Service Bus Replicator

This service provides unidirectional message replication between two Azure Service Bus namespaces. It ensures all topics in the source namespace have a subscription that it consumes from, and republishes messages to the same-named topics in the target namespace.

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
- Azure Key Vault
- Azure Managed Identity with appropriate permissions

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

The connection strings should be stored in Azure Key Vault with the secret names specified in the configuration.

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
