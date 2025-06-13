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
  - SAS Authorization Rule:
    - Name: RootManageSharedAccessKey
    - Permissions: Listen, Send, Manage
    - Created during namespace creation
  - Local Authentication: Enabled
  - Minimum TLS Version: 1.2

- **Target Namespace (East US 2)**
  - SKU: Standard
  - Location: East US 2
  - Role: Target for message replication
  - SAS Authorization Rule:
    - Name: RootManageSharedAccessKey
    - Permissions: Listen, Send, Manage
    - Created during namespace creation
  - Local Authentication: Enabled
  - Minimum TLS Version: 1.2

#### Azure Key Vault
- SKU: Standard
- Soft delete enabled (7 days retention)
- Stores Service Bus connection strings as secrets:
  - `AzureServiceBus-ConnectionString-Value`: Source namespace connection string
  - `AzureServiceBus-ConnectionString2-Value`: Target namespace connection string
- Connection string format:
  ```
  Endpoint=sb://{namespace}.servicebus.windows.net/;SharedAccessKeyName={sas-rule-name};SharedAccessKey={primary-key}
  ```

#### Infrastructure as Code
The infrastructure is managed using Terraform with the following components:
- Resource Group creation
- Service Bus namespace provisioning in both regions
- SAS authorization rule creation for each namespace
- Key Vault creation and configuration
- Secret management for connection strings
- Managed Identity integration
- Connection string construction using:
  - Namespace name
  - SAS rule name
  - Primary key from SAS rule
- Test environment setup:
  - Test topics in both namespaces
  - Replication subscriptions with SQL filters
  - Test message sender project
  - Validation scripts

### Configuration Conventions
- Appsettings pattern for indirection:
  ```json
  {
    "AllowedHosts": "*",
    "AzureKeyVault": {
      "VaultUri": "https://my-keyvault-name.vault.azure.net/"
    },
    "AzureServiceBus": {
      "ConnectionString": {
        "Key": "AzureServiceBus:ConnectionString:Value"
      },
      "ConnectionString2": {
        "Key": "AzureServiceBus:ConnectionString2:Value"
      }
    },
    "Logging": {
      "LogLevel": {
        "Default": "Debug",
        "Microsoft.AspNetCore": "Debug",
        "Microsoft.Hosting.Lifetime": "Debug",
        "Azure": "Debug"
      }
    },
    "Replication": {
      "DefaultTTLMinutes": 10,
      "SubscriptionName": "replicationapi"
    }
  }
  ```

### Project Structure
```
Exactitude.ServiceBus.Replicator.sln
├── Exactitude.ServiceBus.Replicator         // Core class library (NuGet-targeted)
├── Exactitude.ServiceBus.Replicator.Api     // Sample ASP.NET Core Web API host
├── Exactitude.ServiceBus.Replicator.Tests   // MSTest + AutoFixture + Moq test library
├── TestMessageSender                        // Test message sender application
│   ├── Program.cs                          // Message sender implementation
│   ├── TestMessageSender.csproj            // Project file
│   └── appsettings.json                    // Key Vault configuration
├── scripts                                 // Validation and setup scripts
│   └── validate-environment.ps1            // Environment validation script
└── terraform/                              // Infrastructure as Code
    ├── main.tf                             // Main Terraform configuration
    ├── variables.tf                        // Variable definitions
    └── terraform.tfvars                    // Default variable values
```

### NuGet Library Usage

#### Installation
```bash
dotnet add package Exactitude.ServiceBus.Replicator
```

#### Basic Usage
```csharp
// Program.cs
using Exactitude.ServiceBus.Replicator;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddServiceBusReplication(builder.Configuration);

var app = builder.Build();
app.Run();
```

#### Configuration Options
The library supports the following configuration options:

1. **Connection Strings**
   - Source namespace connection string via `AzureServiceBus:ConnectionString:Key`
   - Target namespace connection string via `AzureServiceBus:ConnectionString2:Key`

2. **Replication Settings**
   - Subscription name via `Replication:SubscriptionName` (default: "replicationapi")
   - Default TTL via `Replication:DefaultTTLMinutes` (default: 10)

3. **Key Vault Integration**
   - Vault URI via `AzureKeyVault:VaultUri`
   - Uses DefaultAzureCredential for authentication

### Sample Application

#### Test Message Sender
The sample application includes a test message sender that demonstrates how to:
- Connect to Azure Service Bus using Key Vault secrets
- Send messages with custom properties
- Handle errors and retries

To use the test message sender:
1. Deploy the infrastructure using Terraform
2. Navigate to the TestMessageSender directory
3. Run the application:
   ```bash
   dotnet run
   ```

The sender will:
- Connect to the source Service Bus namespace
- Send a test message to the `test-topic`
- Include custom properties for testing
- Log success or failure

#### Replication API
The sample API demonstrates how to:
- Host the replication service
- Configure logging and error handling
- Use dependency injection

To run the API:
1. Ensure infrastructure is deployed
2. Navigate to the API project directory
3. Run the application:
   ```bash
   dotnet run
   ```

The API will:
- Start the replication service
- Log startup and configuration details
- Handle message replication automatically

### Validation Procedures

#### Infrastructure Validation
1. **Terraform Deployment**
   ```bash
   cd terraform
   terraform init
   terraform plan
   terraform apply
   ```

2. **Resource Verification**
   - Verify Service Bus namespaces are created in correct regions
   - Confirm SAS rules are created with correct permissions
   - Validate Key Vault secrets are properly stored
   - Check local authentication is enabled on namespaces

3. **Connection String Validation**
   - Verify connection strings are properly formatted
   - Test connectivity using Azure Portal
   - Validate SAS token permissions

#### Application Validation
1. **Startup Validation**
   - Verify application starts without errors
   - Check Key Vault access is successful
   - Confirm Service Bus connections are established

2. **Replication Validation**
   - Send test message to source topic
   - Verify message appears in target topic
   - Check replication metadata is added
   - Validate TTL is properly set

3. **Error Handling**
   - Test connection failure scenarios
   - Verify retry behavior
   - Check error logging

#### Performance Validation
1. **Message Throughput**
   - Test with various message sizes
   - Verify concurrent processing
   - Measure replication latency

2. **Resource Usage**
   - Monitor memory consumption
   - Track CPU utilization
   - Check network bandwidth

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

### Implementation Progress

#### Completed Work
1. **Infrastructure Setup**
   - Created Terraform configuration for Service Bus namespaces
   - Added SAS key creation and management
   - Configured Key Vault for secret storage
   - Implemented connection string construction
   - Added proper resource tagging
   - Added test environment setup
   - Created validation scripts

2. **Code Changes**
   - Modified `ServiceCollectionExtensions.cs` to use SAS authentication
   - Updated configuration to use direct connection strings
   - Removed Azure AD authentication dependencies
   - Added test message sender
   - Implemented validation procedures

#### Next Steps
1. **Infrastructure Deployment**
   - Deploy Terraform configuration
   - Verify resource creation
   - Test Key Vault access

2. **Application Testing**
   - Run the application
   - Test message replication
   - Verify SAS authentication
   - Monitor performance

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

# Azure Service Bus Replicator - Product Requirements Document (PRD)

## Infrastructure Management Requirements

### Infrastructure as Code Policy

1. All infrastructure changes MUST be implemented through Terraform:
   - Service Bus namespaces and their configurations
   - Topics and subscriptions
   - Key Vault resources and access policies
   - Any other Azure resources used by the replicator

2. Manual infrastructure changes are strictly prohibited:
   - No direct modifications through Azure Portal
   - No Azure CLI or PowerShell commands for infrastructure changes
   - No REST API calls to modify infrastructure

3. Change Management:
   - Infrastructure changes must follow the standard PR review process
   - Changes must be tested in a development environment first
   - Changes must be documented in the PR description
   - Terraform plan output must be included in the PR

4. Compliance:
   - Regular audits will be performed to ensure compliance
   - Any unauthorized manual changes will be reverted
   - Teams must coordinate infrastructure changes through the proper channels

### Authentication Requirements

The service employs a hybrid authentication model:

1. Management Operations (Topic/Subscription Management):
   - Uses Azure AD authentication
   - Requires the Azure.Identity library
   - API must have a Managed Identity with the "Azure Service Bus Data Owner" role
   - No SAS tokens used for management operations

2. Message Operations (Send/Receive):
   - Uses SAS authentication with connection strings
   - Connection strings stored in Azure Key Vault
   - Created and managed through Terraform
   - Requires SAS authentication to be enabled on namespaces

3. Service Bus Namespace Configuration:
   - Required Terraform setting: `local_auth_enabled = true`
   - Appears in Azure as: `disableLocalAuth = false`
   - Both settings mean: SAS authentication is enabled
   - Azure AD authentication always enabled by default

4. Security Best Practices:
   - Use least-privilege access principles
   - Regular rotation of SAS keys (automated through Terraform)
   - Proper secret management in Key Vault
   - Audit logging enabled for all operations

### Key Vault Requirements
