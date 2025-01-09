# Resource Group Configuration
variable "resource_group_name" {
  description = "Name of the resource group where SQL resources will be deployed"
  type        = string

  validation {
    condition     = length(var.resource_group_name) > 0
    error_message = "Resource group name cannot be empty"
  }
}

variable "location" {
  description = "Azure region where SQL resources will be deployed"
  type        = string

  validation {
    condition     = length(var.location) > 0
    error_message = "Location cannot be empty"
  }
}

variable "environment" {
  description = "Environment name (dev, test, prod) with specific configurations per environment"
  type        = string

  validation {
    condition     = contains(["dev", "test", "prod"], var.environment)
    error_message = "Environment must be dev, test, or prod"
  }
}

# SQL Server Configuration
variable "sql_server_name" {
  description = "Name of the Azure SQL Server instance following naming conventions"
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9-]{3,63}$", var.sql_server_name))
    error_message = "SQL server name must be between 3 and 63 characters, and contain only lowercase letters, numbers, and hyphens"
  }
}

variable "sql_server_sku" {
  description = "SKU for SQL Server (GP_Gen5_4 for minimum 4 cores/16GB RAM, GP_Gen5_8 for recommended 8 cores/32GB RAM)"
  type        = string
  default     = "GP_Gen5_4"

  validation {
    condition     = contains(["GP_Gen5_4", "GP_Gen5_8"], var.sql_server_sku)
    error_message = "SQL server SKU must be either GP_Gen5_4 (minimum) or GP_Gen5_8 (recommended)"
  }
}

variable "sql_database_name" {
  description = "Name of the Azure SQL Database following naming conventions"
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9-]{1,128}$", var.sql_database_name))
    error_message = "Database name must be between 1 and 128 characters, and contain only lowercase letters, numbers, and hyphens"
  }
}

# Authentication and Security
variable "sql_admin_username" {
  description = "SQL Server administrator username with enhanced security"
  type        = string
  sensitive   = true

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9]{3,}$", var.sql_admin_username))
    error_message = "Admin username must start with a letter, be at least 4 characters, and contain only alphanumeric characters"
  }
}

variable "sql_admin_password" {
  description = "SQL Server administrator password with strong security requirements"
  type        = string
  sensitive   = true

  validation {
    condition     = can(regex("^(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9])(?=.*[!@#$%^&*()])[A-Za-z0-9!@#$%^&*()]{16,128}$", var.sql_admin_password))
    error_message = "Password must be 16-128 characters and contain at least one uppercase letter, lowercase letter, number, and special character"
  }
}

variable "enable_public_access" {
  description = "Enable public network access to SQL Server (disabled by default for security)"
  type        = bool
  default     = false
}

# Backup and Data Protection
variable "backup_retention_days" {
  description = "Number of days to retain short-term backups (minimum 7 days per best practices)"
  type        = number
  default     = 7

  validation {
    condition     = var.backup_retention_days >= 7 && var.backup_retention_days <= 35
    error_message = "Backup retention days must be between 7 and 35"
  }
}

variable "enable_transparent_data_encryption" {
  description = "Enable Transparent Data Encryption (TDE) for data at rest"
  type        = bool
  default     = true
}

variable "enable_threat_detection" {
  description = "Enable Advanced Threat Protection for SQL Server"
  type        = bool
  default     = true
}

# Resource Tagging
variable "tags" {
  description = "Tags to apply to all SQL resources for resource management"
  type        = map(string)
  default     = {}
}