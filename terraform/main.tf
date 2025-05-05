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

# Resource Group
resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.source_location
}

# Service Bus Namespace - Central US (Source)
resource "azurerm_servicebus_namespace" "central" {
  name                = "sb-central-${random_string.unique.result}"
  location            = var.source_location
  resource_group_name = azurerm_resource_group.rg.name
  sku                = var.servicebus_sku

  tags = {
    Environment = var.environment
    Role        = "Source"
  }
}

# Service Bus Namespace - East US 2 (Target)
resource "azurerm_servicebus_namespace" "east2" {
  name                = "sb-east2-${random_string.unique.result}"
  location            = var.target_location
  resource_group_name = azurerm_resource_group.rg.name
  sku                = var.servicebus_sku

  tags = {
    Environment = var.environment
    Role        = "Target"
  }
}

# Random string for unique names
resource "random_string" "unique" {
  length  = 8
  special = false
  upper   = false
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
      "Get", "List", "Create", "Delete", "Update",
    ]

    secret_permissions = [
      "Get", "List", "Set", "Delete",
    ]
  }

  tags = {
    Environment = var.environment
  }
}

# Get current Azure context
data "azurerm_client_config" "current" {}

# Store Service Bus connection strings in Key Vault
resource "azurerm_key_vault_secret" "central_connection_string" {
  name         = "AzureServiceBus-ConnectionString-Value"
  value        = azurerm_servicebus_namespace.central.default_primary_connection_string
  key_vault_id = azurerm_key_vault.kv.id

  tags = {
    Environment = var.environment
    Role        = "Source"
  }
}

resource "azurerm_key_vault_secret" "east2_connection_string" {
  name         = "AzureServiceBus-ConnectionString2-Value"
  value        = azurerm_servicebus_namespace.east2.default_primary_connection_string
  key_vault_id = azurerm_key_vault.kv.id

  tags = {
    Environment = var.environment
    Role        = "Target"
  }
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