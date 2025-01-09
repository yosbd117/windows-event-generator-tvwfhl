# Azure providers configuration
# Provider versions:
# azurerm ~> 3.0
# azuread ~> 2.0
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 2.0"
    }
  }
}

# Local variables for resource tagging
locals {
  tags = {
    Project             = "Windows Event Simulator"
    Environment         = var.environment
    ManagedBy          = "Terraform"
    SecurityLevel      = "High"
    ComplianceRequired = "True"
  }
}

# Get current Azure subscription details
data "azurerm_client_config" "current" {}

# Resource group for all components
resource "azurerm_resource_group" "resource_group" {
  name     = var.resource_group_name
  location = var.location
  tags     = local.tags
}

# Enhanced Key Vault configuration
resource "azurerm_key_vault" "key_vault" {
  name                            = "${var.resource_group_name}-kv-${var.environment}"
  location                        = var.location
  resource_group_name             = azurerm_resource_group.resource_group.name
  tenant_id                       = data.azurerm_client_config.current.tenant_id
  sku_name                       = "Premium"
  soft_delete_retention_days     = 90
  purge_protection_enabled       = true
  enabled_for_disk_encryption    = true
  enabled_for_template_deployment = true

  network_acls {
    bypass         = "AzureServices"
    default_action = "Deny"
  }

  tags = local.tags
}

# Monitoring workspace configuration
resource "azurerm_log_analytics_workspace" "monitor_workspace" {
  name                = "${var.resource_group_name}-law-${var.environment}"
  location            = var.location
  resource_group_name = azurerm_resource_group.resource_group.name
  sku                = "PerGB2018"
  retention_in_days   = var.monitor_retention_days
  tags               = local.tags
}

# Backup vault configuration
resource "azurerm_recovery_services_vault" "backup_vault" {
  name                = "${var.resource_group_name}-rsv-${var.environment}"
  location            = var.location
  resource_group_name = azurerm_resource_group.resource_group.name
  sku                = "Standard"
  soft_delete_enabled = true
  tags               = local.tags
}

# AKS cluster module
module "aks" {
  source = "./modules/aks"

  resource_group_name   = azurerm_resource_group.resource_group.name
  location             = var.location
  environment          = var.environment
  node_count           = var.aks_node_count
  vm_size              = "Standard_D8s_v3"
  windows_pool_enabled = true
  windows_vm_size      = "Standard_D8s_v3"
  network_bandwidth    = "1Gbps"
  monitor_workspace_id = azurerm_log_analytics_workspace.monitor_workspace.id
  tags                = local.tags
}

# SQL Server module
module "sql" {
  source = "./modules/sql"

  resource_group_name    = azurerm_resource_group.resource_group.name
  location              = var.location
  environment           = var.environment
  server_sku            = "BusinessCritical"
  database_name         = var.sql_database_name
  backup_retention_days = var.backup_retention_days
  backup_vault_id       = azurerm_recovery_services_vault.backup_vault.id
  monitor_workspace_id  = azurerm_log_analytics_workspace.monitor_workspace.id
  tags                 = local.tags
}

# Output values
output "resource_group_name" {
  description = "The name of the created resource group"
  value       = azurerm_resource_group.resource_group.name
}

output "key_vault_uri" {
  description = "The URI of the Key Vault"
  value       = azurerm_key_vault.key_vault.vault_uri
}

output "monitor_workspace_id" {
  description = "The ID of the Log Analytics workspace"
  value       = azurerm_log_analytics_workspace.monitor_workspace.id
}

output "aks_cluster_name" {
  description = "The name of the AKS cluster"
  value       = module.aks.cluster_name
}

output "sql_server_name" {
  description = "The name of the SQL Server"
  value       = module.sql.server_name
}