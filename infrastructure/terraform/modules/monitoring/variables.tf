variable "resource_group_name" {
  type        = string
  description = "Name of the resource group where monitoring resources will be deployed"

  validation {
    condition     = length(var.resource_group_name) > 0
    error_message = "Resource group name cannot be empty."
  }
}

variable "location" {
  type        = string
  description = "Azure region where monitoring resources will be deployed"

  validation {
    condition     = length(var.location) > 0
    error_message = "Location cannot be empty."
  }
}

variable "environment" {
  type        = string
  description = "Environment name (e.g., dev, staging, prod) for resource naming and configuration"

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be one of: dev, staging, prod."
  }
}

variable "tags" {
  type        = map(string)
  description = "Resource tags to be applied to all monitoring resources for organization and billing"
  default     = {}
}

variable "retention_days" {
  type        = number
  description = "Number of days to retain monitoring data in Log Analytics and Application Insights for compliance and analysis"
  default     = 90

  validation {
    condition     = var.retention_days >= 30 && var.retention_days <= 730
    error_message = "Retention days must be between 30 and 730 days."
  }
}

variable "alert_email_addresses" {
  type        = list(string)
  description = "List of email addresses for receiving monitoring alerts and security notifications"

  validation {
    condition     = length(var.alert_email_addresses) > 0
    error_message = "At least one email address must be provided for alerts."
  }

  validation {
    condition     = alltrue([for email in var.alert_email_addresses : can(regex("^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$", email))])
    error_message = "All email addresses must be in a valid format."
  }
}

variable "memory_threshold" {
  type        = number
  description = "Memory usage percentage threshold for triggering resource usage alerts"
  default     = 80

  validation {
    condition     = var.memory_threshold >= 0 && var.memory_threshold <= 100
    error_message = "Memory threshold must be between 0 and 100 percent."
  }
}

variable "failed_login_threshold" {
  type        = number
  description = "Number of failed login attempts within 5 minutes to trigger security alert"
  default     = 5

  validation {
    condition     = var.failed_login_threshold >= 1 && var.failed_login_threshold <= 100
    error_message = "Failed login threshold must be between 1 and 100 attempts."
  }
}

variable "log_analytics_sku" {
  type        = string
  description = "SKU for Log Analytics workspace determining features and retention capabilities"
  default     = "PerGB2018"

  validation {
    condition     = contains(["Free", "PerGB2018", "Premium"], var.log_analytics_sku)
    error_message = "Log Analytics SKU must be one of: Free, PerGB2018, Premium."
  }
}

variable "application_insights_type" {
  type        = string
  description = "Type of Application Insights to create for monitoring the Windows Event Simulator"
  default     = "web"

  validation {
    condition     = contains(["web", "other"], var.application_insights_type)
    error_message = "Application Insights type must be one of: web, other."
  }
}