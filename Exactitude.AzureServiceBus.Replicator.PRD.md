# Exactitude Azure Service Bus Replicator

## Product Requirements Document (PRD)

### Overview
This product provides a **unidirectional message replication** mechanism between two Azure Service Bus namespaces. It is implemented as a class library (`Exactitude.ServiceBus.Replicator`) consumable via an ASP.NET Core Web API host (`Exactitude.ServiceBus.Replicator.Api`). It ensures all topics in the **source** namespace have a `replicationapi` (configurable) subscription that it consumes from, and republishes messages to the same-named topics in the **target** namespace.

> To achieve bidirectional replication between two regions, you must deploy and configure two separate instances of this replicator, each with reversed source/target roles.

### Product Objectives
- Enable multi-region resilience and failover by replicating messages from one Azure Service Bus namespace to another.
- Avoid polling: use event-driven `ServiceBusProcessor`s.
- Secure secrets via Azure Key Vault, indirectly referenced in configuration.
- All messages are forwarded as-is, with replication metadata and expiration rules.

### Product Features
- Dynamically discover all topics on startup from the **source** namespace.
- Automatically ensure `replicationapi` (or custom-named) subscriptions exist.
- Replicate messages to same topic name in the **target** namespace.
- Use replication marker (`replicated = 1`) and SQL rule to avoid message reprocessing.
- Configurable TTL for replicated messages.
- Use Managed Identity to access Key Vault securely.

### Infrastructure Requirements
The solution requires two Azure Service Bus namespaces in different regions and an Azure Key Vault for secret management. These are provisioned using Terraform with the following specifications:

#### Service Bus Namespaces
- **Source Namespace (Central US)**
  - SKU: Standard
  - Location: Central US
  - Role: Source for message replication

- **Target Namespace (East US 2)**
  - SKU: Standard
  - Location: East US 2
  - Role: Target for message replication

#### Azure Key Vault
- SKU: Standard
- Soft delete enabled (7 days retention)
- Stores Service Bus connection strings as secrets:
  - `AzureServiceBus-ConnectionString-Value`: Source namespace connection string
  - `AzureServiceBus-ConnectionString2-Value`: Target namespace connection string

#### Infrastructure as Code
The infrastructure is managed using Terraform with the following components:
- Resource Group creation
- Service Bus namespace provisioning in both regions
- Key Vault creation and configuration
- Secret management for connection strings
- Managed Identity integration

### Configuration Conventions
- Appsettings pattern for indirection:
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
      "VaultUri": "https://my-keyvault-name.vault.azure.net/"
    }
  }
  ```

### Project Structure
```
Exactitude.ServiceBus.Replicator.sln
├── Exactitude.ServiceBus.Replicator         // Core class library (NuGet-targeted)
├── Exactitude.ServiceBus.Replicator.Api     // Sample ASP.NET Core Web API host
├── Exactitude.ServiceBus.Replicator.Tests   // MSTest + AutoFixture + Moq test library
└── terraform/                               // Infrastructure as Code
    ├── main.tf                             // Main Terraform configuration
    ├── variables.tf                        // Variable definitions
    └── terraform.tfvars                    // Default variable values
```

### Testing Strategy

#### Unit Tests
1. **Configuration Tests**
   - Verify configuration binding
   - Validate default values
   - Test configuration validation

2. **Replication Logic Tests**
   - Test message property preservation
   - Verify TTL calculation
   - Test replication marker handling
   - Validate subscription rule creation

3. **Error Handling Tests**
   - Test connection failure scenarios
   - Verify retry logic
   - Validate error logging

#### Integration Tests
1. **Infrastructure Tests**
   - Verify Terraform deployment
   - Validate Key Vault access
   - Test Service Bus connectivity

2. **End-to-End Tests**
   - Test message replication flow
   - Verify bidirectional setup
   - Validate message ordering
   - Test at scale (high message volume)

3. **Failure Recovery Tests**
   - Test region failover scenarios
   - Verify message recovery
   - Validate subscription recreation

#### Performance Tests
1. **Throughput Tests**
   - Measure messages/second
   - Test with different message sizes
   - Verify concurrent processing

2. **Latency Tests**
   - Measure replication delay
   - Test cross-region latency
   - Verify TTL impact

3. **Resource Usage Tests**
   - Monitor memory usage
   - Track CPU utilization
   - Measure network bandwidth

### Test Execution Process
1. **Local Testing**
   - Run unit tests via `dotnet test`
   - Execute integration tests against dev resources
   - Perform manual verification

2. **CI/CD Pipeline**
   - Automated unit test execution
   - Infrastructure deployment validation
   - Integration test suite execution

3. **Production Validation**
   - Canary deployment testing
   - Performance baseline verification
   - Monitoring and alerting validation

### Product Non-Goals
- No metrics or dashboards.
- No HTTP endpoints in core.
- No dynamic runtime reconfiguration.
- No deduplication beyond `replicated` filter.

### Success Criteria
1. All unit tests pass with >90% coverage
2. Integration tests verify successful message replication
3. Performance tests show <1s replication latency
4. Infrastructure deploys successfully via Terraform
5. No message loss during normal operation
6. Successful failover demonstration

---

## Technical Requirements Document (TRD)

### Frameworks & Libraries
- `Azure.Messaging.ServiceBus`
- `Azure.Identity`
- `Azure.Extensions.AspNetCore.Configuration.Secrets`
- `Microsoft.Extensions.Hosting`, `IServiceCollection`
- `MSTest.TestFramework`, `AutoFixture`, `Moq`

### Key Behaviors
- **Startup:**
  - Load config with indirection to Key Vault.
  - Use `DefaultAzureCredential` to authenticate via Managed Identity.
  - Register Key Vault provider with `VaultUri` from config.
  - Resolve and cache connection strings for source and target.
  - Instantiate `ServiceBusClient` and `ServiceBusAdministrationClient` for both.
  - Discover all topics from the **source** namespace.
  - For each topic:
    - Ensure `replicationapi` subscription exists.
    - Remove default rule if exists (`$Default`).
    - Add SQL rule:
      ```sql
      FILTER: replicated IS NULL
      ACTION: SET replicated = 1
      ```

- **Message Reception:**
  - Use `ServiceBusProcessor` for each (topic, subscription) pair in the **source** namespace.
  - On message receipt:
    - Skip messages marked `replicated` (filtered by subscription rule).
    - Forward message to same topic in **target** namespace.
    - Use base implementation pattern from [ServiceBusReplicationTasks.cs](https://github.com/Azure-Samples/azure-messaging-replication-dotnet/blob/main/src/Azure.Messaging.Replication/ServiceBusReplicationTasks.cs)
    - Add/overwrite properties:
      - `replicated = 1`
      - `repl-origin`, `repl-enqueue-time`, `repl-sequence`
    - Set TTL:
      - If present, TTL = original TTL - elapsed
      - Else, use `Replication:DefaultTTLMinutes`
    - Complete source message

### Message Transformation Rules
- Preserve: `MessageId`, `SessionId`, `ContentType`, `CorrelationId`, all user properties
- Add replication metadata headers (see above)
- No message body modification

### Failure Behavior
- If forwarding fails to target namespace:
  - Log the failure.
  - Complete source message anyway (V1 behavior).
  - Later version may allow abandon/retry or DLQ forwarding.

### Service Hosting
- Minimal ASP.NET Core Web API hosts the background replicator service
- All replicator logic lives in class library
- Exposes DI extension to `IServiceCollection`

### Tests
- MSTest for framework
- AutoFixture for auto data
- Moq for `ServiceBusClient`, `ServiceBusSender`, `ServiceBusProcessor`
- Validate:
  - TTL math
  - Property mapping
  - SQL rule filter presence
  - One-way propagation

### Security
- All connection strings stored in Azure Key Vault
- Key Vault accessed via Managed Identity using `DefaultAzureCredential`
- No secrets in plain-text files
