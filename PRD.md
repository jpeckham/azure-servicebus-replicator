# Product Requirements Document (PRD)
|short description|user story|expected behavior|
|-|-|-|
|Service Bus Namespaces|As a developer, I want to have two Service Bus namespaces (source and target) so that I can replicate messages between them.|Two namespaces are created with `local_auth_enabled = true` in different regions (Central US and East US 2). Note: In the Azure API, this appears as `disableLocalAuth = false`, which is correct and means local auth IS enabled.|
|SAS Authentication|As a developer, I want SAS authentication to be enabled on both namespaces so that the application can connect using connection strings.|Both namespaces have `local_auth_enabled = true` (appears as `disableLocalAuth = false` in Azure) and connection strings are stored in Key Vault.|
|Topics and Subscriptions|As a developer, I want test topics and subscriptions to be created so that I can verify message replication.|Test topics and subscriptions are created with proper filter rules for replication.|
|Key Vault Integration|As a developer, I want connection strings stored securely in Key Vault so that they can be accessed by the application.|Key Vault is created with proper access policies and connection strings are stored as secrets.|
|Managed Identity|As a developer, I want the API to use managed identity for authentication so that it can securely access Key Vault and Service Bus.|A managed identity is created and granted access to both Key Vault and Service Bus.|

## Azure Service Bus Replicator Infrastructure

- The infrastructure must provision two Azure Service Bus namespaces (source and target) using Terraform.
- Both namespaces must have `local_auth_enabled = true` in their Terraform resource definitions to ensure SAS authentication is enabled and supported by the application.
  - Important: In the Azure Portal and API, this appears as `disableLocalAuth = false`, which is the correct setting - it means local auth (SAS) IS enabled.
  - Do not be confused by the different terminology between Terraform (`local_auth_enabled = true`) and Azure API (`disableLocalAuth = false`) - they mean the same thing.
- Do not use or reference `disable_local_auth` as it is not supported by the Terraform provider.
- The infrastructure must also provision topics, subscriptions, and a Key Vault for storing connection strings.
- All configuration and authentication requirements must be documented and reflected in the Terraform codebase.

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

### Authentication and Security Requirements