# Azure SQL Server and Database configuration for Windows Event Simulator
# Provider version: azurerm ~> 3.0

# Get current Azure subscription details for Azure AD integration
data "azurerm_client_config" "current" {}

# Azure SQL Server instance with enhanced security and monitoring capabilities
resource "azurerm_mssql_server" "sql_server" {
  name                         = var.sql_server_name
  resource_group_name          = var.resource_group_name
  location                     = var.location
  version                      = "12.0"
  administrator_login          = var.sql_admin_username
  administrator_login_password = var.sql_admin_password
  minimum_tls_version         = "1.2"
  public_network_access_enabled = var.enable_public_access

  # Azure AD authentication configuration
  azuread_administrator {
    login_username = "AzureAD Admin"
    object_id     = data.azurerm_client_config.current.object_id
    tenant_id     = data.azurerm_client_config.current.tenant_id
  }

  # Managed identity for enhanced security
  identity {
    type = "SystemAssigned"
  }

  tags = var.tags
}

# Azure SQL Database with advanced performance and backup configurations
resource "azurerm_mssql_database" "sql_database" {
  name                        = var.sql_database_name
  server_id                   = azurerm_mssql_server.sql_server.id
  sku_name                    = var.sql_server_sku
  max_size_gb                = 256
  zone_redundant             = true
  auto_pause_delay_in_minutes = -1  # Disable auto-pause for production workloads
  min_capacity               = 0.5
  read_scale                 = true

  # Short-term backup retention configuration
  short_term_retention_policy {
    retention_days = var.backup_retention_days
  }

  # Long-term backup retention configuration
  long_term_retention_policy {
    weekly_retention  = "P4W"   # 4 weeks
    monthly_retention = "P12M"  # 12 months
    yearly_retention  = "P5Y"   # 5 years
    week_of_year     = 1
  }

  tags = var.tags
}

# Restrictive firewall rules for SQL Server access
resource "azurerm_mssql_firewall_rule" "sql_firewall_rule" {
  name      = "AllowAzureServices"
  server_id = azurerm_mssql_server.sql_server.id
  # Only allow Azure services to access the SQL Server
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Comprehensive security alert policy with advanced threat protection
resource "azurerm_mssql_server_security_alert_policy" "sql_security_alert_policy" {
  resource_group_name        = var.resource_group_name
  server_name               = azurerm_mssql_server.sql_server.name
  state                     = "Enabled"
  email_account_admins      = true
  email_addresses           = var.alert_email_addresses
  disabled_alerts           = []
  retention_days           = 30
  storage_account_access_key = var.storage_account_access_key
  storage_endpoint         = var.storage_endpoint
}

# Automated vulnerability assessment configuration
resource "azurerm_mssql_server_vulnerability_assessment" "sql_vulnerability_assessment" {
  server_security_alert_policy_id = azurerm_mssql_server_security_alert_policy.sql_security_alert_policy.id
  storage_container_path         = var.security_storage_container_path

  recurring_scans {
    enabled                    = true
    email_subscription_admins = true
    emails                    = var.alert_email_addresses
  }
}

# Output values for reference by other modules
output "server_name" {
  description = "The name of the created SQL Server"
  value       = azurerm_mssql_server.sql_server.name
}

output "server_id" {
  description = "The resource ID of the SQL Server"
  value       = azurerm_mssql_server.sql_server.id
}

output "database_name" {
  description = "The name of the created SQL Database"
  value       = azurerm_mssql_database.sql_database.name
}

output "connection_string" {
  description = "The connection string for the SQL Database"
  value       = "Server=tcp:${azurerm_mssql_server.sql_server.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.sql_database.name};"
  sensitive   = true
}