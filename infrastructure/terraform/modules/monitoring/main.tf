# Azure Resource Manager Provider - version 3.0
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }
}

# Log Analytics Workspace for centralized logging
resource "azurerm_log_analytics_workspace" "main" {
  name                = "${var.environment}-eventsim-law"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                = var.log_analytics_sku
  retention_in_days   = var.retention_days
  
  # Set daily quota to prevent unexpected costs
  daily_quota_gb = 10
  
  # Enable internet-based ingestion and querying
  internet_ingestion_enabled = true
  internet_query_enabled     = true
  
  tags = var.tags
}

# Application Insights for application telemetry
resource "azurerm_application_insights" "main" {
  name                = "${var.environment}-eventsim-appinsights"
  resource_group_name = var.resource_group_name
  location            = var.location
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = var.application_insights_type
  retention_in_days   = var.retention_days
  
  # Enable full sampling for comprehensive monitoring
  sampling_percentage = 100
  
  # Maintain IP address information for security tracking
  disable_ip_masking = false
  
  tags = var.tags
}

# Action group for alert notifications
resource "azurerm_monitor_action_group" "main" {
  name                = "${var.environment}-eventsim-alerts"
  resource_group_name = var.resource_group_name
  short_name         = "eventsim"
  enabled            = true

  dynamic "email_receiver" {
    for_each = var.alert_email_addresses
    content {
      name                    = "admin-${index}"
      email_address          = email_receiver.value
      use_common_alert_schema = true
    }
  }
}

# Memory usage alert
resource "azurerm_monitor_metric_alert" "memory" {
  name                = "${var.environment}-memory-alert"
  resource_group_name = var.resource_group_name
  scopes             = [azurerm_application_insights.main.id]
  description        = "Alert when memory usage exceeds threshold"
  
  frequency          = "PT1M"  # 1 minute
  window_size        = "PT5M"  # 5 minute window
  severity           = 2       # High severity
  
  criteria {
    metric_namespace = "Microsoft.Insights/Components"
    metric_name      = "performanceCounters/memoryUsedPercentage"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = var.memory_threshold
  }
  
  action {
    action_group_id = azurerm_monitor_action_group.main.id
  }
}

# Failed login attempts alert
resource "azurerm_monitor_scheduled_query_rule_alert" "failed_login" {
  name                = "${var.environment}-failed-login-alert"
  resource_group_name = var.resource_group_name
  location            = var.location
  
  data_source_id     = azurerm_log_analytics_workspace.main.id
  description        = "Alert on multiple failed login attempts"
  
  frequency          = "PT1M"
  query             = <<-QUERY
    SecurityEvent
    | where EventID == 4625
    | summarize FailedLogins=count() by bin(TimeGenerated, 1m)
    | where FailedLogins >= ${var.failed_login_threshold}
  QUERY
  
  time_window        = "PT5M"
  severity           = 1  # Critical
  
  trigger {
    operator         = "GreaterThan"
    threshold        = var.failed_login_threshold
  }
  
  action {
    action_group = [azurerm_monitor_action_group.main.id]
  }
}

# Suspicious operations alert
resource "azurerm_monitor_scheduled_query_rule_alert" "suspicious_ops" {
  name                = "${var.environment}-suspicious-ops-alert"
  resource_group_name = var.resource_group_name
  location            = var.location
  
  data_source_id     = azurerm_log_analytics_workspace.main.id
  description        = "Alert on suspicious operations"
  
  frequency          = "PT5M"
  query             = <<-QUERY
    SecurityEvent
    | where EventID in (4688, 4689, 4672, 4673)
    | where Level == "Warning" or Level == "Error"
    | summarize SuspiciousOps=count() by bin(TimeGenerated, 5m)
  QUERY
  
  time_window        = "PT5M"
  severity           = 2  # High
  
  trigger {
    operator         = "GreaterThan"
    threshold        = 10  # Alert on more than 10 suspicious operations in 5 minutes
  }
  
  action {
    action_group = [azurerm_monitor_action_group.main.id]
  }
}

# Output values for use in other modules
output "workspace_id" {
  description = "ID of the Log Analytics workspace"
  value       = azurerm_log_analytics_workspace.main.id
}

output "instrumentation_key" {
  description = "Application Insights instrumentation key"
  value       = azurerm_application_insights.main.instrumentation_key
  sensitive   = true
}

output "app_insights_connection_string" {
  description = "Application Insights connection string"
  value       = azurerm_application_insights.main.connection_string
  sensitive   = true
}

output "action_group_id" {
  description = "ID of the monitoring action group"
  value       = azurerm_monitor_action_group.main.id
}