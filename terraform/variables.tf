variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
  default     = "rg-servicebus-replication"
}

variable "source_location" {
  description = "Azure region for the source Service Bus namespace"
  type        = string
  default     = "centralus"
}

variable "target_location" {
  description = "Azure region for the target Service Bus namespace"
  type        = string
  default     = "eastus2"
}

variable "servicebus_sku" {
  description = "SKU for Service Bus namespaces"
  type        = string
  default     = "Standard"
}

variable "environment" {
  description = "Environment name for tagging"
  type        = string
  default     = "Production"
}

variable "key_vault_sku" {
  description = "SKU for Azure Key Vault"
  type        = string
  default     = "standard"
} 