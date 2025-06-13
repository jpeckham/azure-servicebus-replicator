terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }
  }
}

# Random string for unique names
resource "random_string" "unique" {
  length  = 8
  special = false
  upper   = false
}

# Resource Group
resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.source_location
}

# Service Bus Namespace - Central US (Source)
resource "azurerm_servicebus_namespace" "central" {
  name                          = "sb-central-${random_string.unique.result}"
  location                      = var.source_location
  resource_group_name           = azurerm_resource_group.rg.name
  sku                          = var.servicebus_sku
  local_auth_enabled           = true
  minimum_tls_version          = "1.2"
  public_network_access_enabled = true

  tags = {
    Environment = var.environment
    Role        = "Source"
  }
}

# Service Bus Namespace - East US 2 (Target)
resource "azurerm_servicebus_namespace" "east2" {
  name                          = "sb-east2-${random_string.unique.result}"
  location                      = var.target_location
  resource_group_name           = azurerm_resource_group.rg.name
  sku                          = var.servicebus_sku
  local_auth_enabled           = true
  minimum_tls_version          = "1.2"
  public_network_access_enabled = true

  tags = {
    Environment = var.environment
    Role        = "Target"
  }
}

# Create a managed identity for the API
resource "azurerm_user_assigned_identity" "api_identity" {
  name                = "id-api-${random_string.unique.result}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
}

# Key Vault
resource "azurerm_key_vault" "kv" {
  name                        = "kv-sbrepl-${random_string.unique.result}"
  location                    = azurerm_resource_group.rg.location
  resource_group_name         = azurerm_resource_group.rg.name
  enabled_for_disk_encryption = true
  tenant_id                   = data.azurerm_client_config.current.tenant_id
  soft_delete_retention_days  = 7
  purge_protection_enabled    = false
  sku_name                   = var.key_vault_sku

  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = data.azurerm_client_config.current.object_id

    key_permissions = [
      "Get", "List", "Create", "Delete", "Update", "Purge", "Recover"
    ]

    secret_permissions = [
      "Get", "List", "Set", "Delete", "Purge", "Recover"
    ]
  }

  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = azurerm_user_assigned_identity.api_identity.principal_id

    secret_permissions = [
      "Get", "List"
    ]
  }

  tags = {
    Environment = var.environment
  }
}

# Get current Azure context
data "azurerm_client_config" "current" {}

# Assign roles to the managed identity
resource "azurerm_role_assignment" "api_central_owner" {
  scope                = azurerm_servicebus_namespace.central.id
  role_definition_name = "Azure Service Bus Data Owner"
  principal_id         = azurerm_user_assigned_identity.api_identity.principal_id
}

resource "azurerm_role_assignment" "api_east2_owner" {
  scope                = azurerm_servicebus_namespace.east2.id
  role_definition_name = "Azure Service Bus Data Owner"
  principal_id         = azurerm_user_assigned_identity.api_identity.principal_id
}

# SAS authorization rules
resource "azurerm_servicebus_namespace_authorization_rule" "central_sas" {
  name                = "replicator-central"
  namespace_id        = azurerm_servicebus_namespace.central.id
  
  listen = true
  send   = true
  manage = true
}

resource "azurerm_servicebus_namespace_authorization_rule" "east2_sas" {
  name                = "replicator-east2"
  namespace_id        = azurerm_servicebus_namespace.east2.id
  
  listen = true
  send   = true
  manage = true
}

# Key Vault secrets for connection strings
resource "azurerm_key_vault_secret" "central_connection_string" {
  name         = "AzureServiceBus--ConnectionString--Value"
  value        = azurerm_servicebus_namespace_authorization_rule.central_sas.primary_connection_string
  key_vault_id = azurerm_key_vault.kv.id
}

resource "azurerm_key_vault_secret" "east2_connection_string" {
  name         = "AzureServiceBus--ConnectionString2--Value"
  value        = azurerm_servicebus_namespace_authorization_rule.east2_sas.primary_connection_string
  key_vault_id = azurerm_key_vault.kv.id
}

# Test Topics and Subscriptions
resource "azurerm_servicebus_topic" "central_test_topic" {
  name                = "test-topic"
  namespace_id        = azurerm_servicebus_namespace.central.id
  max_size_in_megabytes = 1024
  default_message_ttl = "PT10M"
}

resource "azurerm_servicebus_topic" "east2_test_topic" {
  name                = "test-topic"
  namespace_id        = azurerm_servicebus_namespace.east2.id
  max_size_in_megabytes = 1024
  default_message_ttl = "PT10M"
}

resource "azurerm_servicebus_subscription" "central_test_subscription" {
  name               = "replicationapi"
  topic_id          = azurerm_servicebus_topic.central_test_topic.id
  max_delivery_count = 1
}

resource "azurerm_servicebus_subscription" "east2_test_subscription" {
  name               = "replicationapi"
  topic_id          = azurerm_servicebus_topic.east2_test_topic.id
  max_delivery_count = 1
}

resource "azurerm_servicebus_subscription_rule" "central_test_subscription_rule" {
  name            = "replication-filter"
  subscription_id = azurerm_servicebus_subscription.central_test_subscription.id
  filter_type     = "SqlFilter"
  sql_filter      = "replicated IS NULL"
}

resource "azurerm_servicebus_subscription_rule" "east2_test_subscription_rule" {
  name            = "replication-filter"
  subscription_id = azurerm_servicebus_subscription.east2_test_subscription.id
  filter_type     = "SqlFilter"
  sql_filter      = "replicated IS NULL"
}

resource "local_file" "test_message_sender_appsettings" {
  filename = "../TestMessageSender/appsettings.json"
  content = jsonencode({
    AzureServiceBus = {
      UniqueId = random_string.unique.result
      ConnectionString = {
        Key = "AzureServiceBus:ConnectionString:Value"
      }
      ConnectionString2 = {
        Key = "AzureServiceBus:ConnectionString2:Value"
      }
    },
    AzureKeyVault = {
      VaultUri = azurerm_key_vault.kv.vault_uri
    }
  })
}

resource "local_file" "replicator_api_appsettings" {
  filename = "../Exactitude.ServiceBus.Replicator.Api/appsettings.json"
  content = jsonencode({
    Logging = {
      LogLevel = {
        Default = "Debug"
        "Microsoft.AspNetCore" = "Information"
        "Exactitude.ServiceBus.Replicator" = "Debug"
      }
    }
    AllowedHosts = "*"
    AzureServiceBus = {
      UniqueId = random_string.unique.result
      ConnectionString = {
        Key = "AzureServiceBus:ConnectionString:Value"
      }
      ConnectionString2 = {
        Key = "AzureServiceBus:ConnectionString2:Value"
      }
    }
    Replication = {
      SubscriptionName = "replicationapi"
      DefaultTTLMinutes = 10
    }
    AzureKeyVault = {
      VaultUri = azurerm_key_vault.kv.vault_uri
    }
  })
}

resource "local_file" "replicator_tests_appsettings" {
  filename = "../Exactitude.ServiceBus.Replicator.Tests/appsettings.json"
  content = jsonencode({
    AzureServiceBus = {
      UniqueId = random_string.unique.result
      ConnectionString = {
        Key = "AzureServiceBus:ConnectionString:Value"
      }
      ConnectionString2 = {
        Key = "AzureServiceBus:ConnectionString2:Value"
      }
    }
    Replication = {
      SubscriptionName = "replicationapi"
      DefaultTTLMinutes = 10
    }
    AzureKeyVault = {
      VaultUri = azurerm_key_vault.kv.vault_uri
    }
  })
}

# Outputs
output "key_vault_uri" {
  value = azurerm_key_vault.kv.vault_uri
}

output "central_servicebus_name" {
  value = azurerm_servicebus_namespace.central.name
}

output "east2_servicebus_name" {
  value = azurerm_servicebus_namespace.east2.name
}

output "resource_group_name" {
  value = azurerm_resource_group.rg.name
}

output "test_message_sender_path" {
  value = "../TestMessageSender"
}
