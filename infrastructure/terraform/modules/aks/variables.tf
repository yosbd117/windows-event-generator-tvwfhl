# Required variables with validation
variable "resource_group_name" {
  type        = string
  description = "Name of the resource group where AKS cluster will be deployed"

  validation {
    condition     = length(var.resource_group_name) > 0
    error_message = "Resource group name cannot be empty"
  }
}

variable "location" {
  type        = string
  description = "Azure region where AKS cluster will be deployed"

  validation {
    condition     = length(var.location) > 0
    error_message = "Location cannot be empty"
  }
}

variable "environment" {
  type        = string
  description = "Environment name (dev, staging, prod)"

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be dev, staging, or prod"
  }
}

# Optional variables with defaults and validation
variable "kubernetes_version" {
  type        = string
  description = "Kubernetes version for AKS cluster with Windows container support"
  default     = "1.25.6"

  validation {
    condition     = can(regex("^1\\.2[5-9]", var.kubernetes_version))
    error_message = "Kubernetes version must be 1.25 or higher for Windows container support"
  }
}

variable "node_count" {
  type        = number
  description = "Initial number of nodes in the AKS cluster"
  default     = 2

  validation {
    condition     = var.node_count >= 1
    error_message = "Node count must be at least 1"
  }
}

variable "vm_size" {
  type        = string
  description = "Size of the VM instances optimized for Windows containers"
  default     = "Standard_D4s_v3"

  validation {
    condition     = can(regex("^Standard_D[4-9]s_v3|^Standard_D1[0-6]s_v3", var.vm_size))
    error_message = "VM size must be at least Standard_D4s_v3 for Windows containers"
  }
}

variable "os_disk_size_gb" {
  type        = number
  description = "OS disk size in GB for AKS nodes running Windows containers"
  default     = 128

  validation {
    condition     = var.os_disk_size_gb >= 128
    error_message = "OS disk size must be at least 128 GB for Windows containers"
  }
}

variable "enable_auto_scaling" {
  type        = bool
  description = "Enable cluster autoscaling for dynamic workload management"
  default     = true
}

variable "min_node_count" {
  type        = number
  description = "Minimum number of nodes for autoscaling"
  default     = 1

  validation {
    condition     = var.min_node_count >= 1
    error_message = "Minimum node count must be at least 1"
  }
}

variable "max_node_count" {
  type        = number
  description = "Maximum number of nodes for autoscaling"
  default     = 5

  validation {
    condition     = var.max_node_count >= var.min_node_count
    error_message = "Maximum node count must be greater than or equal to minimum node count"
  }
}

variable "max_pods" {
  type        = number
  description = "Maximum number of pods per node (optimized for Windows containers)"
  default     = 30

  validation {
    condition     = var.max_pods >= 10 && var.max_pods <= 250
    error_message = "Max pods must be between 10 and 250"
  }
}

variable "log_analytics_retention_days" {
  type        = number
  description = "Retention period in days for Log Analytics workspace"
  default     = 30

  validation {
    condition     = var.log_analytics_retention_days >= 30
    error_message = "Log retention must be at least 30 days"
  }
}

variable "tags" {
  type        = map(string)
  description = "Tags to be applied to all resources"
  default     = {}
}