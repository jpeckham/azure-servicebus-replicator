# End-to-End Testing

## Overview
This project contains integration tests that verify the replication of messages between Azure Service Bus topics. These tests require the infrastructure to be up and running.

## Prerequisites
- Azure Service Bus namespaces (source and target) must be deployed and configured.
- Key Vault must be accessible and contain the necessary connection strings.

## Running the Tests
1. Ensure the infrastructure is deployed and running.
2. Navigate to the `Exactitude.ServiceBus.Replicator.Tests` directory.
3. Run the integration tests using the following command:
   ```bash
   dotnet test --filter "FullyQualifiedName~Exactitude.ServiceBus.Replicator.Tests.IntegrationTests"
   ```

## Notes
- These tests are separate from unit tests and require the actual Azure infrastructure to be in place.
- Ensure that the `appsettings.json` file in the test project contains the correct Key Vault URI. 