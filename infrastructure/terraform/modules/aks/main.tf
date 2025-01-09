# Azure Kubernetes Service (AKS) Module Configuration
# Provider version: azurerm ~> 3.0

# Data source for current Azure subscription context
data "azurerm_client_config" "current" {}

# Log Analytics Workspace for AKS monitoring
resource "azurerm_log_analytics_workspace" "main" {
  name                = "${var.environment}-event-simulator-logs"
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_analytics_retention_days

  tags = merge(
    var.tags,
    {
      application = "event-simulator"
      environment = var.environment
    }
  )
}

# AKS Cluster optimized for Windows containers
resource "azurerm_kubernetes_cluster" "main" {
  name                = "${var.environment}-event-simulator-aks"
  location            = var.location
  resource_group_name = var.resource_group_name
  dns_prefix          = "${var.environment}-event-simulator"
  kubernetes_version  = var.kubernetes_version
  sku_tier            = "Standard"

  default_node_pool {
    name                = "default"
    node_count          = var.node_count
    vm_size            = var.vm_size
    os_disk_size_gb    = var.os_disk_size_gb
    type               = "VirtualMachineScaleSets"
    enable_auto_scaling = var.enable_auto_scaling
    min_count          = var.enable_auto_scaling ? var.min_node_count : null
    max_count          = var.enable_auto_scaling ? var.max_node_count : null
    max_pods           = var.max_pods
    os_type            = "Windows"
    
    node_labels = {
      role        = "windows"
      environment = var.environment
    }

    tags = merge(
      var.tags,
      {
        application = "event-simulator"
        environment = var.environment
      }
    )
  }

  windows_profile {
    admin_username = "azureuser"
    admin_password = var.windows_admin_password
  }

  network_profile {
    network_plugin     = "azure"
    network_policy     = "azure"
    dns_service_ip     = "10.0.0.10"
    service_cidr       = "10.0.0.0/16"
    docker_bridge_cidr = "172.17.0.1/16"
    load_balancer_sku  = "standard"
  }

  identity {
    type = "SystemAssigned"
  }

  azure_policy_enabled = true
  
  oms_agent {
    log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  }

  microsoft_defender {
    enabled = true
  }

  tags = merge(
    var.tags,
    {
      application = "event-simulator"
      environment = var.environment
    }
  )

  lifecycle {
    prevent_destroy = true
  }
}

# Enhanced diagnostic settings for AKS monitoring
resource "azurerm_monitor_diagnostic_setting" "main" {
  name                       = "${var.environment}-event-simulator-diagnostics"
  target_resource_id         = azurerm_kubernetes_cluster.main.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  dynamic "enabled_log" {
    for_each = [
      "kube-apiserver",
      "kube-controller-manager",
      "cluster-autoscaler",
      "kube-scheduler",
      "kube-audit-admin",
      "guard"
    ]
    content {
      category = enabled_log.value
      enabled  = true

      retention_policy {
        enabled = true
        days    = var.log_analytics_retention_days
      }
    }
  }

  metric {
    category = "AllMetrics"
    enabled  = true

    retention_policy {
      enabled = true
      days    = var.log_analytics_retention_days
    }
  }
}

# Output values for cluster access and integration
output "cluster_name" {
  description = "Name of the created AKS cluster"
  value       = azurerm_kubernetes_cluster.main.name
}

output "cluster_id" {
  description = "Resource ID of the AKS cluster"
  value       = azurerm_kubernetes_cluster.main.id
}

output "kube_config" {
  description = "Kubernetes configuration for cluster access"
  value       = azurerm_kubernetes_cluster.main.kube_config_raw
  sensitive   = true
}

output "log_analytics_workspace_id" {
  description = "Resource ID of the Log Analytics workspace"
  value       = azurerm_log_analytics_workspace.main.id
}