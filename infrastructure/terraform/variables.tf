# Terraform variables for Windows Event Simulator Azure infrastructure
# terraform ~> 1.0

# Core infrastructure variables
variable "resource_group_name" {
  type        = string
  description = "Name of the Azure resource group for Windows Event Simulator resources"

  validation {
    condition     = length(var.resource_group_name) >= 3 && length(var.resource_group_name) <= 63 && can(regex("^[a-zA-Z0-9-_]*$", var.resource_group_name))
    error_message = "Resource group name must be 3-63 characters and contain only letters, numbers, hyphens, and underscores"
  }
}

variable "location" {
  type        = string
  description = "Azure region where resources will be deployed"

  validation {
    condition     = contains(["eastus", "westus2", "northeurope", "westeurope"], var.location)
    error_message = "Location must be a supported Azure region with Windows container support"
  }
}

variable "environment" {
  type        = string
  description = "Deployment environment identifier (dev/test/staging/prod)"

  validation {
    condition     = contains(["dev", "test", "staging", "prod"], var.environment)
    error_message = "Environment must be one of: dev, test, staging, prod"
  }
}

# AKS cluster configuration
variable "aks_node_count" {
  type        = number
  description = "Initial number of nodes in AKS cluster based on environment"
  default     = 2

  validation {
    condition     = var.aks_node_count >= 2 && var.aks_node_count <= 10
    error_message = "Node count must be between 2 and 10 for high availability"
  }
}

variable "aks_vm_size" {
  type        = string
  description = "VM size for AKS nodes meeting minimum 4 CPU, 8GB RAM requirement"
  default     = "Standard_D4s_v3"

  validation {
    condition     = contains(["Standard_D4s_v3", "Standard_D8s_v3", "Standard_D16s_v3"], var.aks_vm_size)
    error_message = "VM size must meet minimum requirements for Windows containers"
  }
}

# Database configuration
variable "sql_server_sku" {
  type        = string
  description = "SKU for Azure SQL Server with minimum 4 vCores"
  default     = "GP_Gen5_4"

  validation {
    condition     = contains(["GP_Gen5_4", "GP_Gen5_8", "BC_Gen5_4", "BC_Gen5_8"], var.sql_server_sku)
    error_message = "SQL Server SKU must provide minimum 4 vCores for performance requirements"
  }
}

# Security configuration
variable "key_vault_sku" {
  type        = string
  description = "SKU for Azure Key Vault for secure credential storage"
  default     = "standard"

  validation {
    condition     = contains(["standard", "premium"], var.key_vault_sku)
    error_message = "Key Vault SKU must be either standard or premium"
  }
}

# Monitoring configuration
variable "enable_monitoring" {
  type        = bool
  description = "Enable Azure Monitor and Application Insights for observability"
  default     = true
}

variable "log_retention_days" {
  type        = number
  description = "Number of days to retain logs in Azure Monitor"
  default     = 90

  validation {
    condition     = var.log_retention_days >= 30 && var.log_retention_days <= 730
    error_message = "Log retention must be between 30 and 730 days for compliance"
  }
}

# Network configuration
variable "network_bandwidth" {
  type        = string
  description = "Minimum network bandwidth requirement"
  default     = "1Gbps"

  validation {
    condition     = contains(["100Mbps", "1Gbps", "10Gbps"], var.network_bandwidth)
    error_message = "Network bandwidth must meet minimum requirements"
  }
}